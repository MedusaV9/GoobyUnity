using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The HUD side of the home interactables: a tap on a piece of furniture pops a small bubble over it with what it can
    /// do (switch the lamp, shower or bathe, the loo, brush teeth, the mirror, sleep, the fridge, the radio, GOB.TY, sit
    /// down, crank the Nougatschleuse); while a routine runs a bar above the action row offers its button (rinse, zap / TV off with the show's
    /// caption, stand up). Gooby's lines go into his bubble and the stat he gained into a toast.
    /// </summary>
    public sealed partial class HomeHudScreen
    {
        private VisualElement _furnBubble, _routineBar;
        private Label _routineText;
        private float _furnBubbleLeft;
        private GobtyTv _hookedTv;

        private bool _ballGrab;

        private void OnBallFetched(double gained, string lineKey)
        {
            Say(_host.Strings.T(lineKey));
            if (gained >= 0.5) Toast(_host.Strings.T("nl.interakt.plus_spass", ("n", Mathf.RoundToInt((float)gained))), "heart");
        }

        private void HookInteract()
        {
            var w = _host.World;
            if (w == null) return;
            w.BallFetched -= OnBallFetched; w.BallFetched += OnBallFetched;
            w.FurnitureTapped -= OnFurnitureTapped; w.FurnitureTapped += OnFurnitureTapped;
            w.RoutineChanged -= OnRoutineChanged; w.RoutineChanged += OnRoutineChanged;
            w.RoutineDone -= OnRoutineDone; w.RoutineDone += OnRoutineDone;
            w.RoutineSays -= OnRoutineSays; w.RoutineSays += OnRoutineSays;
        }

        private void UnhookInteract()
        {
            var w = _host.World;
            if (w == null) return;
            w.FurnitureTapped -= OnFurnitureTapped;
            w.BallFetched -= OnBallFetched;
            w.RoutineChanged -= OnRoutineChanged;
            w.RoutineDone -= OnRoutineDone;
            w.RoutineSays -= OnRoutineSays;
            if (_hookedTv != null) _hookedTv.Changed -= OnTvChanged;
            _hookedTv = null;
        }

        private void OnRoutineSays(string key) => Say(_host.Strings.T(key));

        private void OnRoutineDone(string kind, double gained)
        {
            if (gained < 0.5) return;
            var stat = kind == Interactables.Fernseher ? "nl.interakt.plus_spass" : "nl.interakt.plus_hygiene";
            Toast(_host.Strings.T(stat, ("n", Mathf.RoundToInt((float)gained))), kind == Interactables.Fernseher ? "star" : "sparkle");
        }

        // ── the bubble over a tapped piece ──

        private void OnFurnitureTapped(FurnitureMarker m, Vector3 worldPoint)
        {
            if (_host.Ui.Current != this || AwayBlocks()) return;
            var t = _host.Strings;
            var w = _host.World;
            CloseFurnBubble();
            var bubble = Ux.Col("gb-card", "gb-interakt");
            bubble.name = "interakt-bubble";
            var def = FurnitureCatalog.Def(m.ItemId);
            bubble.Add(Ux.Text(def?.DisplayName(_host.Settings.Locale) ?? m.ItemId, "gb-interakt-title"));
            var row = Ux.Row("gb-interakt-actions");
            void Act(string icon, string label, string name, System.Action run, string style = "gb-btn-primary")
            {
                var b = Ux.IconTextBtn(icon, label, () => { CloseFurnBubble(); run(); }, style, "gb-btn-sm");
                b.name = name;
                row.Add(b);
            }
            var flat = Sleep.FlatOf(_host.Store.State);
            switch (m.Kind)
            {
                case Interactables.Licht:
                    var lit = BadState.LightOn(_host.Store, m.Uid);
                    Act("bulb", t.T(lit ? "nl.interakt.licht_aus" : "nl.interakt.licht_an"), "interakt-licht", () =>
                    {
                        w.ToggleLamp(m);
                        Say(t.T("bad.lampe.klick"));
                    });
                    break;
                case Interactables.Dusche:
                    Act("rain", t.T(m.ItemId == "bathtub" ? "nl.interakt.baden" : "nl.interakt.duschen"), "interakt-dusche", () => w.StartShower(m));
                    break;
                case Interactables.Klo:
                    Act("home", t.T("nl.interakt.klo"), "interakt-klo", () => w.StartKlo(m), "gb-btn-secondary");
                    break;
                case Interactables.Zahnputz:
                    Act("sparkle", t.T("nl.interakt.zaehne"), "interakt-zaehne", () =>
                    {
                        w.StartBrushing(m);
                        _host.Ui.Push(new BrushScreen(_host));
                    });
                    break;
                case Interactables.Spiegel:
                    Act("user", t.T("nl.interakt.spiegel"), "interakt-spiegel", () => _host.Ui.Push(new MirrorScreen(_host)));
                    break;
                case Interactables.Bett:
                    var sleepBtn = Ux.IconTextBtn("moon", t.T("nl.care.schlafen"), () => { CloseFurnBubble(); BedSleep(nap: false); }, "gb-btn-confirm", "gb-btn-sm");
                    sleepBtn.name = "interakt-schlafen";
                    sleepBtn.SetEnabled(Sleep.CanSleep(flat));
                    row.Add(sleepBtn);
                    var napBtn = Ux.IconTextBtn("cloud", t.T("nl.care.nickerchen"), () => { CloseFurnBubble(); BedSleep(nap: true); }, "gb-btn-secondary", "gb-btn-sm");
                    napBtn.SetEnabled(Sleep.CanNap(flat));
                    row.Add(napBtn);
                    break;
                case Interactables.Kuehlschrank:
                    Act("carrot", t.T("nl.interakt.kuehlschrank"), "interakt-kuehlschrank", () => _host.Ui.Push(new CareSheetScreen(_host, Say)));
                    break;
                case Interactables.Radio:
                    Act("speaker", t.T("nl.interakt.radio"), "interakt-radio", () => _host.Ui.Push(new RadioScreen(_host)));
                    break;
                case Interactables.Fernseher:
                    Act("play", t.T("nl.interakt.fernsehen"), "interakt-tv", () => w.StartTv(m, k => _host.Strings.T(k)));
                    break;
                case Interactables.Sitzen:
                    Act("sofa", t.T("nl.interakt.sitzen"), "interakt-sitzen", () => w.StartSitting(m), "gb-btn-secondary");
                    break;
                case Interactables.Nougat:
                    Act("candy", t.T("nougat.kurbeln"), "interakt-nougat", () => NougatKurbeln(m));
                    break;
                case Interactables.Postkarten:
                    Act("mail", t.T("postkarten.ansehen"), "interakt-postkarten", () => _host.Ui.Push(new PostcardsScreen(_host)));
                    break;
                default:
                    return;
            }
            bubble.Add(row);
            Root.Add(bubble);
            // over the tapped spot, kept on screen
            var cam = w.Camera;
            var at = cam != null && Root.panel != null ? RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, worldPoint, cam) : new Vector2(400f, 300f);
            bubble.style.position = Position.Absolute;
            bubble.style.left = Mathf.Clamp(at.x - 120f, 12f, Mathf.Max(12f, Root.layout.width - 300f));
            bubble.style.top = Mathf.Clamp(at.y - 130f, 90f, Mathf.Max(90f, Root.layout.height - 240f));
            _furnBubble = bubble;
            _furnBubbleLeft = 7f;
            Sfx.PlayUi("soft_tap");
            Debug.Log("[interact] tap " + m.ItemId + " (" + m.Kind + ")");
        }

        // ── the Nougatschleuse ──

        /// <summary>Crank! Asleep, sick or still pausing → a soft no; otherwise the crank sequence, then the dollop.</summary>
        private void NougatKurbeln(FurnitureMarker m)
        {
            var t = _host.Strings;
            var state = _host.Store.State;
            var now = _host.Store.Clock.NowMs();
            var verdict = Nougat.CanGlob(state, now);
            if (verdict != Nougat.Ok)
            {
                Sfx.PlayUi("soft_error");
                if (verdict == Nougat.Cooldown)
                    Say(t.T("nougat.pause", ("minuten", Mathf.Max(1, Mathf.CeilToInt(Nougat.CooldownRemainingMs(state, now) / 60000f)))));
                else Say(t.T(verdict == Nougat.Sleeping ? "nougat.schlaeft" : "nougat.krank"));
                Debug.Log("[nougat] nein: " + verdict);
                return;
            }
            var w = _host.World;
            if (w == null || !w.StartNougat(m, NougatKlecks)) NougatKlecks();
        }

        private void NougatKlecks()
        {
            var t = _host.Strings;
            Nougat.Result res = null;
            var now = _host.Store.Clock.NowMs();
            _host.Store.Update(s => res = Nougat.ApplyGlob(s, now), GameHost.TicksMs);
            if (res == null) { Sfx.PlayUi("soft_error"); return; }
            foreach (var slice in new[] { "gooby", "inventory", "progression", "economy", "achievements", "easterEggs" }) _host.Store.NotifySliceChanged(slice);
            Sfx.Play("game_perfect", 1f);
            Say(t.T("nougat.klecks" + UnityEngine.Random.Range(1, 4)));
            FloatOverGooby("+" + Mathf.RoundToInt((float)res.HungerGain), new Color(0.45f, 0.86f, 0.72f));
            Root.schedule.Execute(() => FloatOverGooby("+1 " + t.T("rewards.food." + Nougat.JarFoodId), new Color(1f, 0.8f, 0.3f))).StartingIn(350);
            HeartBurst(10);
            Debug.Log("[nougat] klecks #" + res.Globs + " hunger+" + res.HungerGain + " glaeser " + res.Jars + (res.LevelsGained > 0 ? " level " + res.Level : ""));
        }

        /// <summary>A freshly hung Nougatschleuse is greeted once (easterEggs.nougat.installed).</summary>
        private void NougatInstalliertPruefen()
        {
            var w = _host.World;
            if (w?.Room == null || Nougat.IsInstalled(_host.Store.State)) return;
            if (!w.Room.Markers().Any(x => x != null && x.Kind == Interactables.Nougat)) return;
            var fresh = false;
            _host.Store.Update(s => fresh = Nougat.MarkInstalled(s), GameHost.TicksMs);
            if (!fresh) return;
            _host.Store.NotifySliceChanged("easterEggs");
            Say(_host.Strings.T("nougat.installiert"));
            Debug.Log("[nougat] installiert");
        }

        /// <summary>QA: "nougat haengen" hangs one on a free kitchen wall; "nougat" cranks the first one in the room.</summary>
        public void QaNougat(string what)
        {
            if (what == "haengen")
            {
                var grid = HomeState.LoadRoomGrid(_host.Store, "kitchen");
                var def = FurnitureCatalog.Def(Nougat.FurnitureId);
                var hung = false;
                // the east wall is free in the default kitchen (the north wall hides it behind fridge and stove)
                foreach (var wall in new[] { "E", "W", "N" })
                {
                    var mid = (grid.WallWidth(wall) - (def?.WallSize ?? 1)) / 2;
                    foreach (var x in Enumerable.Range(0, grid.WallWidth(wall)).OrderBy(o => Mathf.Abs(o - mid)))
                        if (def != null && !hung && grid.PlaceWall(def, wall, x, HomeState.NextUid(_host.Store)).Ok)
                        {
                            HomeState.SaveRoomGrid(_host.Store, "kitchen", grid);
                            Debug.Log("[qa] nougat hängt an " + wall + "/" + x);
                            hung = true;
                        }
                    if (hung) break;
                }
                if (_host.World?.RoomId == "kitchen") _host.World.Room?.Refresh(_host.Store);
                else _host.World?.EnterRoom("kitchen");
                _host.World?.InvalidateWalkGrid();
                return;
            }
            var m = _host.World?.Room?.Markers().FirstOrDefault(x => x != null && x.Kind == Interactables.Nougat);
            if (m == null) { Debug.LogWarning("[qa] nougat: none in this room"); return; }
            NougatKurbeln(m);
        }

        private void BedSleep(bool nap)
        {
            var ok = false;
            _host.Store.Update(s => ok = Sleep.StartSleepState(s, _host.Store.Clock.NowMs(), nap), GameHost.TicksMs);
            if (ok) Say(_host.Strings.T("home.suche.schlaeft"));
        }

        private void CloseFurnBubble()
        {
            _furnBubble?.RemoveFromHierarchy();
            _furnBubble = null;
        }

        // ── the routine bar ──

        private void OnRoutineChanged(string kind)
        {
            var t = _host.Strings;
            var w = _host.World;
            _routineBar?.RemoveFromHierarchy();
            _routineBar = null;
            if (_hookedTv != null) _hookedTv.Changed -= OnTvChanged;
            _hookedTv = null;
            if (string.IsNullOrEmpty(kind) || kind == Interactables.Zahnputz || kind == Interactables.Klo) return;
            var bar = Ux.Row("gb-card", "gb-routine-bar");
            bar.name = "routine-bar";
            _routineText = Ux.Text("", "gb-routine-text");
            switch (kind)
            {
                case Interactables.Dusche:
                    _routineText.text = t.T("nl.interakt.duscht");
                    bar.Add(Ux.IconTile("rain", new Color(0.86f, 0.94f, 1f)));
                    bar.Add(_routineText);
                    var rinse = Ux.IconTextBtn("sparkle", t.T("nl.interakt.abspuelen"), () => w.FinishShower(), "gb-btn-primary", "gb-btn-sm");
                    rinse.name = "routine-abspuelen";
                    bar.Add(rinse);
                    break;
                case Interactables.Fernseher:
                    bar.Add(Ux.IconTile("play", new Color(1f, 0.9f, 0.9f)));
                    bar.Add(_routineText);
                    var zap = Ux.IconTextBtn("redo", t.T("nl.interakt.umschalten"), () => { w.Tv?.Zap(); Sfx.PlayUi("soft_tick"); }, "gb-btn-secondary", "gb-btn-sm");
                    zap.name = "routine-zap";
                    bar.Add(zap);
                    var off = Ux.IconTextBtn("close", t.T("gobty.aus_knopf"), () => w.StopTv(), "gb-btn-cancel", "gb-btn-sm");
                    off.name = "routine-tv-aus";
                    bar.Add(off);
                    _hookedTv = w.Tv;
                    if (_hookedTv != null) _hookedTv.Changed += OnTvChanged;
                    OnTvChanged();
                    Say(t.T("gobty.an"));
                    break;
                case Interactables.Sitzen:
                    _routineText.text = t.T("nl.interakt.sitzt");
                    bar.Add(Ux.IconTile("sofa", new Color(1f, 0.93f, 0.85f)));
                    bar.Add(_routineText);
                    var up = Ux.IconTextBtn("arrow_up", t.T("nl.interakt.aufstehen"), () => w.StandUp(), "gb-btn-secondary", "gb-btn-sm");
                    up.name = "routine-aufstehen";
                    bar.Add(up);
                    break;
                default:
                    return;
            }
            Root.Add(bar);
            _routineBar = bar;
        }

        private void OnTvChanged()
        {
            if (_routineText == null || _hookedTv == null) return;
            _routineText.text = _hookedTv.Title + " — " + _hookedTv.Banner;
        }

        private float _nougatCheckIn = 1.5f;

        private void TickInteractUi(float dt)
        {
            _nougatCheckIn -= dt;
            if (_nougatCheckIn <= 0f)
            {
                _nougatCheckIn = 2f;
                NougatInstalliertPruefen();
            }
            if (_furnBubble == null) return;
            _furnBubbleLeft -= dt;
            if (_furnBubbleLeft <= 0f) CloseFurnBubble();
        }
    }
}
