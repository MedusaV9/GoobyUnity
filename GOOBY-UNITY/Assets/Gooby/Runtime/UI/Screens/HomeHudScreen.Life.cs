using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Gooby's own life at home (port of gooby_reactions.gd + seele_runner.gd + gooby_gespraech.gd): tap him and he
    /// giggles, gets ticklish, then dizzy — every pet sounds a step higher with hearts, every tenth gives a coin; a
    /// lasting mood (SoulMood) sets his resting face; when the player leaves him be he acts on a need first (walks to the
    /// fridge, the bed, the bath, his toy, the window) and otherwise picks an idle act (tidy up, TV, doze in his crate,
    /// window, dance to the radio, his favourite spot); rare surprises (a coin under the sofa, a loud dream, a pillow
    /// tower, a wave), a small find now and then, staged feelings with a symbol over his head (thunder, finds, records,
    /// the favourite food, the dark, being caught at it …), comments on new furniture, food and neglect, idle chatter,
    /// answer chips after his moments and, after a few days, the question for your birthday. Everything rides on the
    /// soul's frequency brake — never pushy.
    /// </summary>
    public sealed partial class HomeHudScreen
    {
        /// <summary>The HUD in use (QA reaches Gooby's life through it).</summary>
        public static HomeHudScreen Instance { get; private set; }

        /// <summary>QA/shots: no doings of his own (taps, moments and feelings still answer).</summary>
        public static bool LifePaused;

        private const float IdleMinS = 18f, IdleMaxS = 35f, SurpriseCheckS = 60f, FundIntervalS = 90f, MoodRefreshS = 5f;
        private const float PlauderMinS = 55f, PlauderMaxS = 110f, QuietNeededS = 8f, LifeBubbleGapS = 12f, IdleTextQuote = 0.7f;
        private const long ErtapptFensterMs = 8_000;
        private const double NeglectThreshold = 20.0;
        private const int PetMoodMaxPerDay = 20;
        private const double StossFuettern = 5.0, StossStreicheln = 1.0, StossKitzeln = 2.0;

        private static readonly Dictionary<string, double> GrussStoss = new Dictionary<string, double>
        {
            ["gruss_gefreut"] = 3.0, ["gruss_vermisst"] = 5.0, ["gruss_eingeschnappt"] = -4.0,
        };

        private float _idleIn = 22f, _surpriseIn = SurpriseCheckS, _fundIn = FundIntervalS, _moodIn = 0.5f, _plauderIn = 75f, _quiet, _donnerIn = -1f;
        private readonly Dictionary<string, long> _idleCooldowns = new Dictionary<string, long>(), _intentCooldowns = new Dictionary<string, long>();
        private long _intentUntilMs;
        private int _tapCount;
        private float _tapLast = -100f, _lastLifeBubble = -100f, _faceHoldUntil, _clipHoldUntil;
        private string _heldClip;
        private readonly StreichelUebermut _uebermut = new StreichelUebermut();
        private double _moodWert = SoulMood.DefaultWert;
        private bool _sad, _lifeHooked;
        private int _coinsSeen = -1, _feelPrio;
        private float _feelUntil;
        private VisualElement _feelBadge, _chips;
        private float _feelAge = -1f, _chipsLeft;
        private int _chipLevel;
        private readonly List<Heart> _hearts = new List<Heart>();

        private sealed class Heart
        {
            public VisualElement E;
            public Vector2 From, Drift;
            public float Age, Life;
        }

        private long NowMs => _host.Store.Clock.NowMs();

        private DateTimeOffset LocalNow => DateTimeOffset.FromUnixTimeMilliseconds(NowMs).ToLocalTime();

        // ── setup ──

        private void BuildLife(VisualElement root)
        {
            Instance = this;
            _feelBadge = Ux.El("gb-feel");
            _feelBadge.name = "feel-badge";
            _feelBadge.pickingMode = PickingMode.Ignore;
            _feelBadge.style.position = Position.Absolute;
            _feelBadge.style.display = DisplayStyle.None;
            root.Add(_feelBadge);
            _chips = Ux.Row("gb-soul-chips");
            _chips.name = "soul-chips";
            _chips.style.position = Position.Absolute;
            _chips.style.left = Length.Percent(50);
            _chips.style.translate = new Translate(Length.Percent(-50), 0);
            _chips.style.top = 196;
            _chips.style.display = DisplayStyle.None;
            root.Add(_chips);
        }

        private void HookLife()
        {
            if (_host.World != null)
            {
                _host.World.GoobyTapped -= OnGoobyTapped; _host.World.GoobyTapped += OnGoobyTapped;
                _host.World.CareMoment -= OnCareMoment; _host.World.CareMoment += OnCareMoment;
                _host.World.ShakeLine -= OnShakeLine; _host.World.ShakeLine += OnShakeLine;
            }
            CareSheetScreen.Fed -= OnFed;
            CareSheetScreen.Fed += OnFed;
            _lifeHooked = true;
        }

        /// <summary>Now and then Gooby says something about his yawn or sneeze (pflege_runner.gd: 35 % / 50 %).</summary>
        private void OnCareMoment(string what)
        {
            var chance = what == "gaehnen" ? Gooby.Core.Life.CareLook.YawnLineChance : Gooby.Core.Life.CareLook.SneezeLineChance;
            if (UnityEngine.Random.value >= chance) return;
            Say(_host.Strings.T(what == "gaehnen" ? "sleep.gaehnen" : "health.hatschi"));
        }

        /// <summary>The shake secret talks through the bubble (shake.stufe1 … shake.wieder_gut).</summary>
        private void OnShakeLine(string key) => Say(_host.Strings.T(key));

        private void UnhookLife()
        {
            if (_host.World != null) { _host.World.GoobyTapped -= OnGoobyTapped; _host.World.CareMoment -= OnCareMoment; _host.World.ShakeLine -= OnShakeLine; }
            // the care sheet sits on top of the HUD: keep listening while it is mounted underneath
            if (!_host.Ui.IsMounted(this)) CareSheetScreen.Fed -= OnFed;
            HideChips();
            _lifeHooked = false;
        }

        /// <summary>The player did something (stick, a tap on the world): Gooby leaves his own doings for a while.</summary>
        private void NoteActivity() => _quiet = 0f;

        private bool Onboarded => J.IsTrue(J.Path(_host.Store.State, "onboarding", "done"));

        private bool Asleep => Sleep.IsSleeping(Sleep.FlatOf(_host.Store.State));

        /// <summary>No doings of his own now: a sheet on top, a celebration, a fade, asleep, away, steered, busy.</summary>
        private bool LifeBusy()
        {
            var w = _host.World;
            return LifePaused || _host.Ui.Current != this || _host.Ui.Celebrating || _host.Ui.Fading || w == null || !w.LifeFree
                || !Onboarded || Asleep || _away || _quiet < QuietNeededS;
        }

        // ── the tick ──

        private void TickLife(float dt)
        {
            if (!_lifeHooked) return;
            _quiet += dt;
            TickHearts(dt);
            TickFeelBadge(dt);
            TickChips(dt);
            TickHeldClip();
            _moodIn -= dt;
            if (_moodIn <= 0f)
            {
                _moodIn = MoodRefreshS;
                RefreshMood();
                Beobachte();
            }
            TickDonner(dt);
            if (LifeBusy()) return;
            if (TickCoach(dt)) return;
            _idleIn -= dt;
            if (_idleIn <= 0f)
            {
                _idleIn = UnityEngine.Random.Range(IdleMinS, IdleMaxS) * (float)SoulMood.IdleTaktFaktor(_moodWert);
                if (!TryIntent()) RunIdle();
                return;
            }
            _surpriseIn -= dt;
            if (_surpriseIn <= 0f)
            {
                _surpriseIn = SurpriseCheckS;
                RunSurprise(null);
                return;
            }
            _fundIn -= dt;
            if (_fundIn <= 0f)
            {
                _fundIn = FundIntervalS;
                RunMiniFund();
                return;
            }
            _plauderIn -= dt;
            if (_plauderIn <= 0f)
            {
                _plauderIn = UnityEngine.Random.Range(PlauderMinS, PlauderMaxS);
                Plaudern();
            }
        }

        // ── mood ──

        private void RefreshMood()
        {
            var store = _host.Store;
            var now = NowMs;
            store.Update(s =>
            {
                var slice = Soul.Slice(s);
                var flat = GoobyTicker.FlatView(s);
                var ziel = SoulMood.Ziel(J.Dict(J.Path(s, "gooby", "stats")), Sleep.GrumpyDebuff(flat, now));
                slice["stimmung"] = SoulMood.Advance(slice["stimmung"], ziel, now);
                _moodWert = SoulMood.Wert(slice["stimmung"]);
            }, GameHost.TicksMs);
            ApplyRestingFace();
        }

        private void Stoss(double delta)
        {
            var now = NowMs;
            _host.Store.Update(s =>
            {
                var slice = Soul.Slice(s);
                slice["stimmung"] = SoulMood.Bump(slice["stimmung"], delta, now);
                _moodWert = SoulMood.Wert(slice["stimmung"]);
            }, GameHost.TicksMs);
            ApplyRestingFace();
        }

        /// <summary>The face between moments: the mood band's face (sad when neglected), never over a moment's face or sleep.</summary>
        private void ApplyRestingFace()
        {
            var w = _host.World;
            var avatar = w?.Avatar;
            if (avatar == null || Time.unscaledTime < _faceHoldUntil || w.CrateNapping || Asleep || w.Routine != SimpleWorld.RoutineNone) return;
            var state = _host.Store.State;
            var sick = J.Str(J.Path(state, "gooby", "health", "state")) == "sick";
            var face = sick ? "dizzy" : _sad ? "sad" : SoulMood.RuheEmotion(_moodWert, J.Dict(J.Path(state, "gooby", "stats")));
            if (avatar.Emotion != face) avatar.SetEmotion(face);
        }

        // ── taps ──

        private void OnGoobyTapped()
        {
            if (_host.Ui.Current != this || _away) return;
            NoteActivity();
            var now = Time.unscaledTime;
            if (now - _tapLast > Soul.TapWindowS) _tapCount = 0;
            _tapLast = now;
            _tapCount++;
            var w = _host.World;
            var nowMs = NowMs;
            if (w != null && w.CrateNapping && !Asleep) w.WakeInCrate(8f);
            if (nowMs < _intentUntilMs) MeldeGefuehl("ertappt");
            var today = LocalNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var pets = 0;
            _host.Store.Update(s =>
            {
                pets = Soul.CountPet(s, today);
                if (pets <= PetMoodMaxPerDay)
                {
                    var g = J.EnsureDict(s, "gooby");
                    g["stats"] = Stats.ApplyDeltas(J.Dict(g["stats"]), new Dictionary<string, double> { ["fun"] = 1.0 });
                }
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged("achievements");
            Sfx.Play("pet_squish", (float)((Soul.PetPitch(pets) - 1.0) * 12.0) + UnityEngine.Random.Range(-0.3f, 0.3f), 0.9f);
            HeartBurst(Asleep ? 2 : 4);
            if (pets <= PetMoodMaxPerDay) Stoss(StossStreicheln);
            if (Soul.PetBonusDue(pets))
            {
                GrantCoins(Soul.PetBonusCoins, "soul_pet");
                FloatOverGooby("+" + Soul.PetBonusCoins, new Color(1f, 0.78f, 0.2f));
                HeartBurst(10);
                MeldeGefuehl("streichel_bonus");
            }
            if (Asleep) return;
            if (_uebermut.Registriere(NowMs))
            {
                w?.Avatar?.Play("refuse");
                Hold(3f);
                w?.Avatar?.SetEmotion("angry");
                Say(_host.Strings.T("soul.linie.uebermut.pause"));
                return;
            }
            var stage = Soul.TapStage(_tapCount);
            if (stage == "") { FaceHappy(); return; }
            if (stage == "tipp_schwindelig") _tapCount = 0;
            if (stage == "tipp_kitzlig")
            {
                _host.Store.Update(s =>
                {
                    var counters = J.EnsureDict(J.EnsureDict(s, "achievements"), "counters");
                    counters["tickles"] = J.Int(counters["tickles"]) + 1;
                }, GameHost.TicksMs);
                Stoss(StossKitzeln);
                MeldeGefuehl("kitzeln");
            }
            var def = Soul.DefById(Soul.Defs(), stage);
            ShowLifeMoment(Soul.MomentOf(def, SliceView(), Ctx(), UnityEngine.Random.value), quietOk: false);
        }

        private void FaceHappy()
        {
            var w = _host.World;
            if (w?.Avatar == null || w.CrateNapping) return;
            w.Avatar.SetEmotion(_moodWert >= 80 ? "ecstatic" : "happy");
            Hold(1.6f);
        }

        // ── intents and idle acts ──

        private JObject SliceView() => Soul.Slice(_host.Store.State);

        private Soul.Ctx Ctx(long gapMs = 0)
        {
            var local = LocalNow;
            var lage = SoulWetter.Zustand(local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), local.Hour + local.Minute / 60.0);
            var s = _host.Store.State;
            return new Soul.Ctx
            {
                NowMs = NowMs, GapMs = gapMs, Year = local.Year, Month = local.Month, Day = local.Day, Hour = local.Hour,
                WetterTyp = lage.Typ, Schnee = lage.Schnee,
                PlayerName = J.Str(J.Path(s, "meta", "playerName")), Nickname = J.Str(J.Path(s, "meta", "goobyNickname"), "Gooby"),
            };
        }

        private bool Regen()
        {
            var local = LocalNow;
            return SoulWetter.Zustand(local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), local.Hour + local.Minute / 60.0).Regen;
        }

        /// <summary>Need → visible doing: he walks to the target, looks at it, then at you — then (maybe) a line.</summary>
        private bool TryIntent(bool force = false)
        {
            var w = _host.World;
            if (w == null || w.InGarden) return false;
            var vorhanden = new HashSet<string>();
            foreach (var (_, ziele) in SoulIntent.Ziele)
                foreach (var e in ziele)
                {
                    var teile = e.Split(':');
                    if (teile[0] == "item" && w.PiecesMatching(teile[1]).Count > 0) vorhanden.Add(e);
                    else if (teile[0] == "tuer" && w.DoorSpot(teile[1]) != null) vorhanden.Add(e);
                }
            var stats = J.Dict(J.Path(_host.Store.State, "gooby", "stats"));
            var a = SoulIntent.Waehle(stats, Regen(), Asleep, vorhanden, force ? null : _intentCooldowns, NowMs);
            if (a == null) return false;
            _intentCooldowns[a.Id] = NowMs + SoulIntent.CooldownMs;
            PerformIntent(a);
            return true;
        }

        private void PerformIntent(SoulIntent.Absicht a)
        {
            var w = _host.World;
            _intentUntilMs = NowMs + ErtapptFensterMs;
            var moment = Soul.MomentOf(Soul.DefById(Soul.Defs(), a.Id), SliceView(), Ctx(), UnityEngine.Random.value);
            Vector3? spot = null, look = null;
            switch (a.ZielArt)
            {
                case "item":
                    var piece = w.PiecesMatching(a.Ziel).FirstOrDefault();
                    spot = w.SpotAt(piece);
                    look = piece.Bounds.center;
                    break;
                case "tuer":
                    spot = w.DoorSpot(a.Ziel);
                    look = spot;
                    break;
                default:
                    spot = w.WindowSpot();
                    look = spot.HasValue ? w.WindowLook(spot.Value) : (Vector3?)null;
                    break;
            }
            Debug.Log("[seele] absicht " + a.Id + " → " + a.ZielArt + ":" + a.Ziel);
            if (spot == null) { ShowLifeMoment(moment, quietOk: true); return; }
            w.WalkGoobyTo(spot.Value, () => Arrive(look, moment, 1.1f), 8f);
        }

        /// <summary>On arrival: look at the target a beat, turn to the player, then the moment.</summary>
        private void Arrive(Vector3? look, Soul.Moment moment, float lookSeconds, Action atTarget = null)
        {
            var w = _host.World;
            if (w == null) return;
            if (look.HasValue) w.LookAtPoint(look.Value);
            atTarget?.Invoke();
            Root?.schedule.Execute(() =>
            {
                if (_host.World == null || _host.World.Routine != SimpleWorld.RoutineNone) return;
                if (moment?.Clip != "sit" && moment?.Clip != "sleep") _host.World.FaceCamera();
                ShowLifeMoment(moment, quietOk: true);
            }).StartingIn((long)(lookSeconds * 1000f));
        }

        private void RunIdle(string forceId = null)
        {
            var w = _host.World;
            if (w == null) return;
            var slice = SliceView();
            var fav = J.Str(slice["favFurniture"]);
            if (fav == "") _host.Store.Update(s => fav = Soul.PickFavFurniture(Soul.Slice(s), HomeItemIds()), GameHost.TicksMs);
            var ctx = new Soul.IdleCtx
            {
                NowMs = NowMs,
                HatTv = w.PiecesMatching("television").Count > 0,
                RadioAn = _host.Audio?.RadioStation != null,
                HatFav = fav != "" && w.PiecesMatching(fav, exact: true).Count > 0,
                LauneBand = SoulMood.Band(_moodWert),
            };
            var defs = Soul.Defs();
            var act = forceId != null ? Soul.DefById(defs, forceId) : Soul.PickIdle(defs, ctx, _idleCooldowns, UnityEngine.Random.value);
            if (act == null) return;
            var id = J.Str(act["id"]);
            _idleCooldowns[id] = NowMs + Soul.IdleActCooldownMs;
            var moment = Soul.MomentOf(act, SliceView(), Ctx(), UnityEngine.Random.value);
            if (forceId == null && UnityEngine.Random.value > IdleTextQuote) moment.TextKey = "";
            if (moment.Sfx == "") moment.Sfx = "ui_tick";
            Sparkle();
            Debug.Log("[seele] idle " + id);
            switch (moment.Aktion)
            {
                case "moebel":
                {
                    var pieces = w.AllPieces();
                    if (pieces.Count == 0) break;
                    var p = pieces[UnityEngine.Random.Range(0, pieces.Count)];
                    moment.Args["moebel"] = ItemName(p.ItemId);
                    w.WalkGoobyTo(w.SpotAt(p), () => Arrive(p.Bounds.center, moment, 1.4f, () => w.Avatar?.Play("idle_lookaround")), 8f);
                    return;
                }
                case "tv":
                {
                    var tv = w.PiecesMatching("television").FirstOrDefault();
                    w.WalkGoobyTo(w.ApproachPoint(tv.Bounds, tv.Front, 1.1f), () => Arrive(tv.Bounds.center, moment, 0.4f,
                        () => w.TvGlance(k => _host.Strings.T(k), 9f)), 8f);
                    return;
                }
                case "fenster":
                {
                    var spot = w.WindowSpot();
                    if (spot == null) return;
                    w.WalkGoobyTo(spot.Value, () => Arrive(w.WindowLook(spot.Value), moment, 2.2f), 8f);
                    return;
                }
                case "fav":
                {
                    var p = w.PiecesMatching(fav, exact: true).FirstOrDefault();
                    moment.Args["moebel"] = ItemName(fav);
                    w.WalkGoobyTo(w.SpotAt(p), () => Arrive(p.Bounds.center, moment, 1.2f), 8f);
                    return;
                }
            }
            if (id == "idle_doesen" && w.CrateSpot.HasValue && !w.CrateNapping)
            {
                // a nap belongs in his crate (board 08): back he goes, curls up, and the Zzz rise
                w.WalkGoobyTo(w.CrateSpot.Value, () => Root?.schedule.Execute(() => ShowLifeMoment(moment, quietOk: true)).StartingIn(500), 9f);
                return;
            }
            ShowLifeMoment(moment, quietOk: true);
        }

        private string ItemName(string itemId) => FurnitureCatalog.Def(itemId)?.DisplayName(_host.Settings.Locale) ?? itemId;

        private List<string> HomeItemIds()
        {
            var ids = new List<string>();
            foreach (var room in RoomDefs.Ids())
                foreach (var item in HomeState.LoadRoomGrid(_host.Store, room).Items)
                    if (item?.Def != null) ids.Add(item.Def.Id);
            return ids;
        }

        // ── surprises, finds, chatter ──

        private void RunSurprise(string forceId)
        {
            Soul.Moment m = null;
            var ctx = Ctx();
            _host.Store.Update(s =>
            {
                var slice = Soul.Slice(s);
                if (forceId != null)
                {
                    var def = Soul.DefById(Soul.Defs(), forceId);
                    m = def != null ? Soul.MomentOf(def, slice, ctx, UnityEngine.Random.value) : null;
                }
                else m = Soul.PickSurprise(slice, Soul.Defs(), ctx, UnityEngine.Random.value, UnityEngine.Random.value);
                if (m != null) Soul.BookSurprise(slice, m, ctx.NowMs);
            }, GameHost.TicksMs);
            if (m == null) return;
            _host.Store.NotifySliceChanged(Soul.SliceId);
            Debug.Log("[seele] ueberraschung " + m.Id);
            switch (m.Aktion)
            {
                case "muenze":
                    GrantCoins(Soul.SofaFundCoins, "soul_sofa_fund");
                    FloatOverGooby("+" + Soul.SofaFundCoins, new Color(1f, 0.78f, 0.2f));
                    MeldeGefuehl("fund");
                    break;
                case "turm":
                    _host.World?.PillowTower();
                    break;
            }
            ShowLifeMoment(m, quietOk: false);
        }

        /// <summary>A small find now and then (+1 coin, sparkle, a line) — through the soul's brake, never a stream.</summary>
        private void RunMiniFund(bool force = false)
        {
            var ctx = Ctx();
            var ok = false;
            _host.Store.Update(s =>
            {
                var slice = Soul.Slice(s);
                if (!force && !Soul.AmbientAllowed(slice["ambient"] as JObject, ctx.NowMs, ctx.Today)) return;
                slice["ambient"] = Soul.NoteAmbient(slice["ambient"] as JObject, ctx.NowMs, ctx.Today);
                ok = true;
            }, GameHost.TicksMs);
            if (!ok) return;
            GrantCoins(Soul.MiniFundCoins, "soul_fund");
            FloatOverGooby("+" + Soul.MiniFundCoins, new Color(1f, 0.78f, 0.2f));
            Sparkle();
            var key = "rewards.fund." + "abc"[UnityEngine.Random.Range(0, 3)];
            Say(_host.Strings.T(key, ("gooby", ctx.Nickname)));
            _lastLifeBubble = Time.unscaledTime;
            Debug.Log("[seele] fund");
        }

        /// <summary>Idle chatter: on notable weather Gooby sometimes comments on it, else he talks to himself.</summary>
        private void Plaudern(bool force = false)
        {
            var ctx = Ctx();
            var key = "";
            _host.Store.Update(s =>
            {
                var slice = Soul.Slice(s);
                if (!force && !Soul.AmbientAllowed(slice["ambient"] as JObject, ctx.NowMs, ctx.Today)) return;
                var kategorie = SoulLinien.PlauderKategorie(ctx.WetterTyp, UnityEngine.Random.value);
                key = SoulLinien.Waehle(kategorie, slice["linien"] as JObject, UnityEngine.Random.value);
                if (key == "") return;
                slice["linien"] = SoulLinien.Merke(slice["linien"] as JObject, kategorie, key);
                slice["ambient"] = Soul.NoteAmbient(slice["ambient"] as JObject, ctx.NowMs, ctx.Today);
            }, GameHost.TicksMs);
            if (key == "") return;
            Say(_host.Strings.T(key, Args(ctx)));
            _lastLifeBubble = Time.unscaledTime;
            Debug.Log("[seele] plaudern " + key);
        }

        /// <summary>A fresh line of a category for feature owners (ball, GOB.TY, results …); "" when the brake holds.</summary>
        public string Kommentar(string kategorie)
        {
            var ctx = Ctx();
            var key = "";
            _host.Store.Update(s =>
            {
                var slice = Soul.Slice(s);
                if (!Soul.AmbientAllowed(slice["ambient"] as JObject, ctx.NowMs, ctx.Today)) return;
                key = SoulLinien.Waehle(kategorie, slice["linien"] as JObject, UnityEngine.Random.value);
                if (key == "") return;
                slice["linien"] = SoulLinien.Merke(slice["linien"] as JObject, kategorie, key);
                slice["ambient"] = Soul.NoteAmbient(slice["ambient"] as JObject, ctx.NowMs, ctx.Today);
            }, GameHost.TicksMs);
            if (key != "") Say(_host.Strings.T(key, Args(ctx)));
            return key;
        }

        private (string, object)[] Args(Soul.Ctx ctx)
        {
            var name = ctx.PlayerName ?? "";
            return new (string, object)[]
            {
                ("name", name), ("namek", name == "" ? "" : ", " + name), ("gooby", ctx.Nickname),
                ("tage", Soul.DaysKnown(J.Long(SliceView()["firstMetAt"]), ctx.NowMs)),
            };
        }

        private void GrantCoins(int n, string reason)
        {
            _host.Store.Update(s => Economy.Award(J.EnsureDict(s, "economy"), n, reason), GameHost.TicksMs);
            if (_coinsSeen >= 0) _coinsSeen = J.Int(J.Path(_host.Store.State, "economy", "coins"));
        }

        // ── comments: food, furniture, neglect ──

        private void OnFed(string foodId, int count)
        {
            Stoss(StossFuettern);
            var slice = SliceView();
            var fav = SoulFeelings.IstLieblingsessen(slice["foodGiven"] as JObject, foodId, Soul.FavFoodMin);
            MeldeGefuehl(fav ? "lieblingsessen" : "essen");
            if (fav) HeartBurst(8);
            var defId = fav ? "kommentar_lieblingsessen" : "kommentar_geschenk";
            var ctx = Ctx();
            Soul.Moment m = null;
            _host.Store.Update(s =>
            {
                var sl = Soul.Slice(s);
                if (Soul.CelebratedToday(sl["celebrated"] as JObject, "essen_" + defId, ctx.Today)) return;
                if (!Soul.AmbientAllowed(sl["ambient"] as JObject, ctx.NowMs, ctx.Today)) return;
                m = Soul.MomentOf(Soul.DefById(Soul.Defs(), defId), sl, ctx, UnityEngine.Random.value);
                if (m == null) return;
                m.Args["essen"] = SoulMemories.FoodTitle(foodId, _host.Strings);
                m.GateKey = "essen_" + defId;
                m.GateStamp = ctx.Today;
                Soul.BookEnter(sl, m, ctx);
            }, GameHost.TicksMs);
            // the care sheet already says its line: Gooby's thanks follows a beat later
            if (m != null) Root?.schedule.Execute(() => ShowLifeMoment(m, quietOk: false)).StartingIn(1600);
        }

        /// <summary>Back from build mode (or a room change): new pieces get a comment and a curious look.</summary>
        private void CheckFreshItems()
        {
            if (_away) return;
            var fresh = new List<string>();
            _host.Store.Update(s => fresh = Soul.FreshItems(Soul.Slice(s), HomeItemIds()), GameHost.TicksMs);
            if (fresh.Count == 0) return;
            MeldeGefuehl("neues_moebel");
            var ctx = Ctx();
            Soul.Moment m = null;
            _host.Store.Update(s =>
            {
                var sl = Soul.Slice(s);
                if (!Soul.AmbientAllowed(sl["ambient"] as JObject, ctx.NowMs, ctx.Today)) return;
                m = Soul.MomentOf(Soul.DefById(Soul.Defs(), "kommentar_moebel_neu"), sl, ctx, UnityEngine.Random.value);
                if (m == null) return;
                m.Args["moebel"] = ItemName(fresh[0]);
                sl["ambient"] = Soul.NoteAmbient(sl["ambient"] as JObject, ctx.NowMs, ctx.Today);
            }, GameHost.TicksMs);
            if (m != null) Root?.schedule.Execute(() => ShowLifeMoment(m, quietOk: false)).StartingIn(900);
        }

        /// <summary>Neglect is sad looks, never punishment: a sad face and, once a day, a soft line.</summary>
        private void CheckNeglect(JObject stats)
        {
            var lowest = Stats.Keys.Min(k => J.NumOr(stats?[k], 100.0));
            if (lowest >= NeglectThreshold)
            {
                if (_sad) { _sad = false; ApplyRestingFace(); }
                return;
            }
            if (_sad) return;
            _sad = true;
            ApplyRestingFace();
            if (!_lifeHooked || _host.Ui.Current != this || Asleep || _away) return;
            MeldeGefuehl("vernachlaessigt");
            var ctx = Ctx();
            Soul.Moment m = null;
            _host.Store.Update(s =>
            {
                var sl = Soul.Slice(s);
                if (Soul.CelebratedToday(sl["celebrated"] as JObject, "traurig", ctx.Today)) return;
                if (!Soul.AmbientAllowed(sl["ambient"] as JObject, ctx.NowMs, ctx.Today)) return;
                m = Soul.MomentOf(Soul.DefById(Soul.Defs(), "kommentar_traurig"), sl, ctx, UnityEngine.Random.value);
                if (m == null) return;
                m.Emotion = "";
                m.GateKey = "traurig";
                m.GateStamp = ctx.Today;
                Soul.BookEnter(sl, m, ctx);
            }, GameHost.TicksMs);
            if (m != null) ShowLifeMoment(m, quietOk: false);
        }

        // ── after the entering moment: mood, conversation, reunion ladder; else a habit or the birthday question ──

        private void AfterEnterMoment(Soul.Moment moment, long gapMs)
        {
            if (moment == null)
            {
                MaybeHabit();
                MaybeAskBirthday();
                return;
            }
            if (GrussStoss.TryGetValue(moment.Id, out var d))
            {
                Stoss(d);
                MeldeGefuehl(moment.Id);
                var key = SoulLinien.WiedersehenKey(gapMs);
                if (key != "" && gapMs >= Soul.MsPerDay)
                    Root?.schedule.Execute(() => { if (_host.Ui.Current == this) Say(_host.Strings.T(key, Args(Ctx()))); }).StartingIn(3200);
            }
            StartGespraech(moment.Kind == "erinnerung" ? "erinnerung" : moment.Id);
        }

        private void MaybeHabit()
        {
            var ctx = Ctx();
            if (ctx.Hour < 21) return;
            Soul.Moment m = null;
            _host.Store.Update(s =>
            {
                var sl = Soul.Slice(s);
                if (Soul.CelebratedToday(sl["celebrated"] as JObject, "alltag_gutenacht", ctx.Today)) return;
                if (!Soul.AmbientAllowed(sl["ambient"] as JObject, ctx.NowMs, ctx.Today)) return;
                m = Soul.MomentOf(Soul.DefById(Soul.Defs(), "alltag_gutenacht"), sl, ctx, UnityEngine.Random.value);
                if (m == null) return;
                m.GateKey = "alltag_gutenacht";
                m.GateStamp = ctx.Today;
                Soul.BookEnter(sl, m, ctx);
            }, GameHost.TicksMs);
            if (m != null) ShowLifeMoment(m, quietOk: false);
        }

        private void MaybeAskBirthday(bool force = false)
        {
            var ctx = Ctx();
            var ask = false;
            _host.Store.Update(s =>
            {
                var sl = Soul.Slice(s);
                if (!force && !Soul.ShouldAskBirthday(sl, ctx)) return;
                sl["askedBirthdayAt"] = ctx.NowMs;
                sl["ambient"] = Soul.NoteAmbient(sl["ambient"] as JObject, ctx.NowMs, ctx.Today);
                ask = true;
            }, GameHost.TicksMs);
            if (!ask) return;
            Say(_host.Strings.T("soul.frage.geburtstag", Args(ctx)));
            Root?.schedule.Execute(() =>
            {
                if (_host.Ui.Current != this) return;
                _host.Ui.Push(new BirthdayScreen(_host, OnBirthdaySaved));
            }).StartingIn(1400);
        }

        private void OnBirthdaySaved(int month, int day)
        {
            _host.Store.Update(s => Soul.SetPlayerBirthday(Soul.Slice(s), month, day), GameHost.TicksMs);
            _host.Store.NotifySliceChanged(Soul.SliceId);
            Say(_host.Strings.T("soul.geburtstag_panel.danke", ("tag", day), ("monat", month)));
            HeartBurst(8);
            Debug.Log("[seele] geburtstag " + day + "." + month);
        }

        // ── answer chips (gooby_gespraech.gd) ──

        private void StartGespraech(string anlassId, bool force = false)
        {
            var g = SoulGespraech.FuerAnlass(SoulGespraech.Lade(), anlassId);
            if (g == null || (!force && UnityEngine.Random.value >= J.NumOr(g["chance"], 1.0))) return;
            _chipLevel = 1;
            Root?.schedule.Execute(() => ShowChips(g)).StartingIn(force ? 0 : 1200);
        }

        private void ShowChips(JObject ebene)
        {
            if (_chips == null || _host.Ui.Current != this) return;
            _chips.Clear();
            var t = _host.Strings;
            foreach (var antwort in SoulGespraech.Antworten(ebene))
            {
                var a = antwort;
                var chip = Ux.Btn(t.T(J.Str(a["label_key"])), () => OnAntwort(a), "gb-chip", "gb-soul-chip");
                chip.name = "soul-chip-" + J.Str(a["id"]);
                _chips.Add(chip);
            }
            if (_bubble != null && _bubble.layout.height > 1f) _chips.style.top = _bubble.layout.yMax + 10f;
            _chips.style.display = DisplayStyle.Flex;
            _chips.style.opacity = 1f;
            _chipsLeft = SoulGespraech.ChipTimeoutS;
            Sfx.PlayUi("soft_open");
            Debug.Log("[seele] chips " + string.Join(",", SoulGespraech.Antworten(ebene).Select(x => J.Str(x["id"]))));
        }

        private void HideChips()
        {
            if (_chips == null) return;
            _chips.style.display = DisplayStyle.None;
            _chips.Clear();
            _chipsLeft = 0f;
        }

        private void TickChips(float dt)
        {
            if (_chipsLeft <= 0f) return;
            _chipsLeft -= dt;
            if (_chipsLeft <= 0f || _host.Ui.Current != this) HideChips();
        }

        private void OnAntwort(JObject antwort)
        {
            HideChips();
            NoteActivity();
            var laune = J.Num(antwort["laune"]);
            if (Math.Abs(laune) > 1e-6) Stoss(laune);
            var id = SoulGespraech.GespraechIdOf(antwort);
            _host.Store.Update(s =>
            {
                var sl = Soul.Slice(s);
                sl["gespraeche"] = SoulGespraech.MerkeAntwort(sl["gespraeche"] as JObject, id, antwort, NowMs);
            }, GameHost.TicksMs);
            Sfx.PlayUi("soft_confirm");
            var key = SoulGespraech.FolgeKey(antwort, UnityEngine.Random.value);
            if (key != "")
            {
                Say(_host.Strings.T(key, Args(Ctx())));
                var avatar = _host.World?.Avatar;
                var emotion = J.Str(antwort["emotion"], "happy");
                if (avatar != null && !(_host.World?.CrateNapping ?? false)) { avatar.SetEmotion(emotion); Hold(3.5f); }
                if (J.Str(antwort["stil"]) == "witz") _host.World?.Avatar?.Play("hop");
            }
            Debug.Log("[seele] antwort " + id + "/" + J.Str(antwort["id"]));
            var weiter = SoulGespraech.NaechsteEbene(antwort);
            if (weiter == null || _chipLevel >= SoulGespraech.MaxEbenen) { _chipLevel = 0; return; }
            _chipLevel++;
            Root?.schedule.Execute(() => ShowChips(weiter)).StartingIn((long)(SoulGespraech.Ebene2PauseS * 1000f));
        }

        // ── feelings (FEEL-AC) ──

        private static (string face, string clip, string icon, string glyph, string sfx) FeelLook(string emotion)
        {
            switch (emotion)
            {
                case "schreck": return ("scared", "hop", "", "!", "game_miss");
                case "ueberraschung": return ("ecstatic", "hop", "sparkle", "", "game_star");
                case "verlegenheit": return ("happy", "idle_lookaround", "", "...", "soft_tick");
                case "stolz": return ("ecstatic", "celebrate", "trophy", "", "game_star");
                case "verliebtheit": return ("ecstatic", "hop", "heart", "", "soft_sticker");
                case "freude": return ("happy", "", "sparkle", "", "soft_tick");
                case "angst": return ("scared", "", "", "!", "");
                case "begeisterung": return ("ecstatic", "celebrate", "star", "", "game_pop");
                case "trotz": return ("angry", "refuse", "cloud", "", "");
                case "muedigkeit": return ("sleepy", "idle_stretch-loop", "moon", "", "");
                case "traurigkeit": return ("sad", "", "rain", "", "");
                case "neugier": return ("neutral", "idle_lookaround", "", "?", "soft_tick");
                default: return ("happy", "", "sparkle", "", "");
            }
        }

        /// <summary>
        /// A real event stages a feeling through its own brake: the face, a clip, a symbol over his head, a sound and —
        /// for notable ones — a short line. A running feeling yields only to a truly stronger one.
        /// </summary>
        private string MeldeGefuehl(string ereignis)
        {
            var emotion = SoulFeelings.EmotionFuer(ereignis);
            if (emotion == "" || Asleep || _away) return "";
            var prio = SoulFeelings.Prio(ereignis);
            if (Time.unscaledTime < _feelUntil && prio <= _feelPrio) return "";
            var now = NowMs;
            var today = Ctx().Today;
            var ok = false;
            _host.Store.Update(s =>
            {
                var sl = Soul.Slice(s);
                if (!SoulFeelings.Erlaubt(sl["feelings"], ereignis, now, today)) return;
                sl["feelings"] = SoulFeelings.Buche(sl["feelings"], ereignis, now, today);
                ok = true;
            }, GameHost.TicksMs);
            if (!ok) return "";
            _feelPrio = prio;
            _feelUntil = Time.unscaledTime + 2.6f;
            var look = FeelLook(emotion);
            var w = _host.World;
            if (w?.Avatar != null && !w.CrateNapping)
            {
                w.Avatar.SetEmotion(look.face);
                Hold(3.2f);
                if (look.clip != "" && w.LifeFree)
                {
                    w.Avatar.Play(look.clip);
                    if (look.clip.EndsWith("-loop")) HoldClip(look.clip, 2.6f);
                }
            }
            ShowFeelBadge(look.icon, look.glyph);
            if (look.sfx != "") Sfx.PlayUi(look.sfx);
            // fright, delight, pride and love get the camera moment (moment_regie.gd) while the home is on screen
            if (_host.Ui.Current == this) w?.PlayMoment(emotion);
            if (prio >= 2 && _host.Ui.Current == this)
                Say(_host.Strings.T("soul.feel." + emotion + "." + (UnityEngine.Random.value < 0.5f ? "a" : "b"), Args(Ctx())));
            Debug.Log("[seele] gefuehl " + ereignis + " → " + emotion);
            return emotion;
        }

        /// <summary>Every 5 s: the dark, tiredness, a new record, coins found, the storm's thunder (asleep he feels nothing staged).</summary>
        private void Beobachte()
        {
            var state = _host.Store.State;
            var coins = J.Int(J.Path(state, "economy", "coins"));
            var ctx = Ctx();
            var gewitter = ctx.WetterTyp == "gewitter";
            if (!gewitter) _donnerIn = -1f;
            else if (_donnerIn < 0f) _donnerIn = (float)SoulFeelings.DonnerIntervallS(UnityEngine.Random.value);
            // a record or a win made in a game is felt once he sees you again at home
            if (_host.Ui.Current != this && _coinsSeen >= 0) return;
            var best = SoulFeelings.RekordMax(state);
            var bestMax = J.Int(SliceView()["feelings"]?["bestMax"]);
            if (best > bestMax)
            {
                _host.Store.Update(s => Soul.Slice(s)["feelings"]["bestMax"] = best, GameHost.TicksMs);
                if (_coinsSeen >= 0) MeldeGefuehl("rekord");
            }
            if (_coinsSeen >= 0 && coins > _coinsSeen && _host.Ui.Current == this) MeldeGefuehl("fund");
            _coinsSeen = coins;
            if (_host.Ui.Current != this || Asleep || _away) return;
            if (SoulFeelings.IstDunkel(ctx.Hour)) MeldeGefuehl("dunkelheit");
            if (SoulFeelings.IstMuede(J.Dict(J.Path(state, "gooby", "stats")))) MeldeGefuehl("muede");
        }

        private void TickDonner(float dt)
        {
            if (_donnerIn < 0f) return;
            _donnerIn -= dt;
            if (_donnerIn > 0f) return;
            _donnerIn = (float)SoulFeelings.DonnerIntervallS(UnityEngine.Random.value);
            if (_host.Ui.Current != this || _host.World == null || _host.World.Outdoors && !_host.World.InGarden) return;
            Sfx.Play("amb_donner", UnityEngine.Random.Range(-2f, 1f), 0.8f);
            MeldeGefuehl("donner");
        }

        // ── showing ──

        private void Hold(float seconds) => _faceHoldUntil = Time.unscaledTime + seconds;

        private void HoldClip(string clip, float seconds)
        {
            _heldClip = clip;
            _clipHoldUntil = Time.unscaledTime + seconds;
        }

        /// <summary>A looping moment clip (sit, sleep, dance, stretch) ends after a while unless something else took over.</summary>
        private void TickHeldClip()
        {
            if (_heldClip == null || Time.unscaledTime < _clipHoldUntil) return;
            var w = _host.World;
            var clip = _heldClip;
            _heldClip = null;
            if (w?.Avatar == null || w.Avatar.Clip != clip || w.CrateNapping || w.Routine != SimpleWorld.RoutineNone) return;
            w.Avatar.Play("idle-loop");
            ApplyRestingFace();
        }

        private static string ClipOf(string clip)
        {
            switch (clip)
            {
                case "sit": return "sit-loop";
                case "sleep": return "sleep-loop";
                case "brush_teeth": return "brush_teeth-loop";
                case "dance": return "dance-loop";
                default: return clip ?? "";
            }
        }

        private static string SfxOf(string sfx)
        {
            if (string.IsNullOrEmpty(sfx)) return "";
            if (sfx == "mg_perfect") return "game_perfect";
            if (sfx == "gvz_pop") return "game_pop";
            return sfx.StartsWith("ui_", StringComparison.Ordinal) ? "soft_" + sfx.Substring(3) : sfx;
        }

        /// <summary>A life moment: the line (life lines keep 12 s apart unless it matters), face, clip, sound, action.</summary>
        private void ShowLifeMoment(Soul.Moment m, bool quietOk)
        {
            if (m == null) return;
            var w = _host.World;
            if (!string.IsNullOrEmpty(m.TextKey) && (!quietOk || Time.unscaledTime - _lastLifeBubble >= LifeBubbleGapS))
            {
                _lastLifeBubble = Time.unscaledTime;
                var args = m.Args.Select(kv => (kv.Key, kv.Value)).ToArray();
                Say(_host.Strings.T(m.TextKey, args));
            }
            var napping = w != null && w.CrateNapping;
            if (w?.Avatar != null && !string.IsNullOrEmpty(m.Emotion) && !napping) { w.Avatar.SetEmotion(m.Emotion); Hold(4f); }
            var clip = ClipOf(m.Clip);
            if (w?.Avatar != null && clip != "" && !napping)
            {
                w.Avatar.Play(clip);
                if (clip.EndsWith("-loop")) HoldClip(clip, m.Id == "idle_fernsehen" ? 9f : clip == "sleep-loop" ? 7f : 6f);
            }
            if (m.Id == "idle_tanzen" && w?.Avatar != null) { w.Avatar.Play("dance-loop"); HoldClip("dance-loop", 6f); }
            var sfx = SfxOf(m.Sfx);
            if (sfx != "") Sfx.PlayUi(sfx);
            if (m.Aktion == "konfetti") _host.Ui.Confetti(90);
            if (m.Aktion == "winken" && w?.Avatar != null) { w.FaceCamera(); w.Avatar.Play("wave"); }
        }

        private void Sparkle()
        {
            var w = _host.World;
            if (w?.Gooby != null) Effects.Sparkle(w.GoobyHead, 0.3f, _host.Settings.ReducedMotion ? 4 : 10);
        }

        private Vector2? HeadPanel()
        {
            var w = _host.World;
            if (w?.Gooby == null || w.Camera == null || Root?.panel == null) return null;
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, w.GoobyHead, w.Camera);
            return Root.WorldToLocal(p);
        }

        private void FloatOverGooby(string text, Color color)
        {
            var w = _host.World;
            if (w?.Gooby == null || w.Camera == null || Root?.panel == null) return;
            _host.Ui.FloatText(RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, w.GoobyHead + Vector3.up * 0.25f, w.Camera), text, color, 34f);
        }

        private void HeartBurst(int n)
        {
            var at = HeadPanel();
            if (at == null || Root == null) return;
            if (_host.Settings.ReducedMotion) n = Mathf.Min(n, 2);
            for (var i = 0; i < n && _hearts.Count < 24; i++)
            {
                var e = Ux.Icon("heart", "gb-heart-float");
                e.pickingMode = PickingMode.Ignore;
                e.style.position = Position.Absolute;
                Root.Add(e);
                _hearts.Add(new Heart
                {
                    E = e, From = at.Value + new Vector2(UnityEngine.Random.Range(-26f, 26f), UnityEngine.Random.Range(-8f, 8f)),
                    Drift = new Vector2(UnityEngine.Random.Range(-40f, 40f), -UnityEngine.Random.Range(70f, 120f)),
                    Age = -i * 0.06f, Life = UnityEngine.Random.Range(0.8f, 1.15f),
                });
            }
        }

        private void TickHearts(float dt)
        {
            for (var i = _hearts.Count - 1; i >= 0; i--)
            {
                var h = _hearts[i];
                h.Age += dt;
                var k = Mathf.Clamp01(h.Age / h.Life);
                var p = h.From + h.Drift * k + new Vector2(Mathf.Sin(h.Age * 9f) * 6f, 0f);
                var size = Mathf.Lerp(18f, 30f, Mathf.Min(1f, k * 3f));
                h.E.style.left = p.x - size * 0.5f;
                h.E.style.top = p.y - size * 0.5f;
                h.E.style.width = size;
                h.E.style.height = size;
                h.E.style.opacity = h.Age < 0f ? 0f : 1f - k * k;
                if (h.Age < h.Life) continue;
                h.E.RemoveFromHierarchy();
                _hearts.RemoveAt(i);
            }
        }

        private void ShowFeelBadge(string icon, string glyph)
        {
            if (_feelBadge == null) return;
            _feelBadge.Clear();
            if (icon != "") _feelBadge.Add(Ux.Icon(icon, "gb-feel-icon"));
            else _feelBadge.Add(Ux.Text(glyph, "gb-feel-glyph"));
            _feelBadge.style.display = DisplayStyle.Flex;
            _feelAge = 0f;
        }

        private void TickFeelBadge(float dt)
        {
            if (_feelAge < 0f || _feelBadge == null) return;
            _feelAge += dt;
            var at = HeadPanel();
            if (at == null || _feelAge > 2.4f || _host.Ui.Current != this)
            {
                _feelBadge.style.display = DisplayStyle.None;
                _feelAge = -1f;
                return;
            }
            var pop = _feelAge < 0.18f ? Mathf.Lerp(0.4f, 1.15f, _feelAge / 0.18f) : _feelAge < 0.3f ? Mathf.Lerp(1.15f, 1f, (_feelAge - 0.18f) / 0.12f) : 1f;
            _feelBadge.style.left = at.Value.x + 18f;
            _feelBadge.style.top = at.Value.y - 64f - Mathf.Min(_feelAge, 0.4f) * 30f;
            _feelBadge.style.scale = new Scale(new Vector3(pop, pop, 1f));
            _feelBadge.style.opacity = _feelAge > 1.9f ? 1f - (_feelAge - 1.9f) / 0.5f : 1f;
        }

        // ── QA ──

        /// <summary>QA "seele …": tap [n] | idle [id] | absicht | ueberraschung [id] | fund | plaudern | gefuehl &lt;event&gt; |
        /// geburtstag | chips &lt;moment id&gt; | moment | aus | an.</summary>
        public void QaSeele(string[] parts)
        {
            var what = parts.Length > 1 ? parts[1] : "";
            var arg = parts.Length > 2 ? parts[2] : null;
            switch (what)
            {
                case "tap":
                    for (var i = 0; i < (arg != null ? int.Parse(arg) : 1); i++) OnGoobyTapped();
                    break;
                case "idle": RunIdle(arg); break;
                case "absicht": Debug.Log("[seele] absicht forced → " + TryIntent(force: true)); break;
                case "ueberraschung": RunSurprise(arg ?? "sup_sofa_fund"); break;
                case "fund": RunMiniFund(force: true); break;
                case "plaudern": Plaudern(force: true); break;
                case "gefuehl":
                    _host.Store.Update(s => Soul.Slice(s)["feelings"] = SoulFeelings.Default(), GameHost.TicksMs);
                    _feelUntil = 0f;
                    MeldeGefuehl(arg ?? "fund");
                    break;
                case "geburtstag": MaybeAskBirthday(force: true); break;
                case "chips": StartGespraech(arg ?? "gruss_morgen", force: true); break;
                case "antwort":
                    var chip = _chips?.Q<Button>("soul-chip-" + arg) ?? _chips?.Q<Button>();
                    if (chip != null) using (var e = NavigationSubmitEvent.GetPooled()) { e.target = chip; chip.SendEvent(e); }
                    break;
                case "wasnun":
                    _coachIn = 0f;
                    _coachShownAt.Clear();
                    Debug.Log("[seele] wasnun → " + (TickCoach(0f) ? _coachId : "nothing to suggest"));
                    break;
                case "aus": LifePaused = true; break;
                case "an": LifePaused = false; _quiet = QuietNeededS; _idleIn = 1f; break;
            }
        }
    }
}
