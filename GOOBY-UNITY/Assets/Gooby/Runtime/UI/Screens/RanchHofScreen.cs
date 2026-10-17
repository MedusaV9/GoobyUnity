using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The Gooby ranch farm (port of ranch_hof_scene.gd, pflege_screen.gd and ausbau_panel.gd as one place): the farm
    /// in 3D behind, the horses as round portraits on the left, the selected horse's card on the right (mood, bond,
    /// hunger, thirst, cleanliness, the stable) and the care dock in the thumb zone — hay and apples from the farm
    /// store, carrots from the fridge, water, the brush and the pitchfork; every action plays out on stage (the treat
    /// arcs into the mouth, the horse eats, drinks, shakes its mane) and earns bond under the daily cap, a new bond
    /// stage is celebrated. Taps on the scene mow the ripe hay meadow, pick the apple trees, water at the trough and
    /// muck out the stable. "Hof &amp; Laden" sells hay, builds boxes, the riding arena and the pasture fence and kits
    /// the horses out; "Reiten &amp; Turniere" starts the ranch games with the best horse. Before the purchase the gate
    /// card offers the ranch (level 15, 2500 ᴳ); the start horses already graze as a teaser. The closed time is
    /// caught up on entry (night in the stable, the pasture at a third), slipped values come as gentle notes.
    /// </summary>
    public sealed partial class RanchHofScreen : UiScreen
    {
        private const string Fuettern = "fuettern";

        /// <summary>The care dock: one feed button (opens the food tray) and the three free actions.</summary>
        private static readonly (string id, string icon, string labelKey)[] Aktionen =
        {
            (Fuettern, "hay", "rhof.fuettern"),
            (RanchPflege.Traenken, "bucket", "ranchplay.pflege.traenken"),
            (RanchPflege.Striegeln, "brush", "ranchplay.pflege.striegeln"),
            (RanchPflege.Ausmisten, "pitchfork", "ranchplay.pflege.ausmisten"),
        };

        private static readonly (string id, string icon)[] FutterIcons =
        {
            (RanchPflege.Heu, "hay"), (RanchPflege.Apfel, "food_apple"), (RanchPflege.Karotte, "food_carrot"),
            (RanchPflege.Hafer, "sprout"), (RanchPflege.Leckerli, "candy"),
        };

        private static readonly string[] Spiele = { "ranchTurnier", "ranchTonnen", "ranchZeit", "ranchParcours", "ranchHerde" };

        private static readonly (string key, string hex)[] Werte =
            { ("hunger", "#E8A23A"), ("durst", "#5FA4D9"), ("sauberkeit", "#8FC98A"), ("bindung", "#E98CA0"), ("stall", "#B79A6B") };

        private static readonly Dictionary<string, string> BandFarbe = new Dictionary<string, string>
        {
            ["strahlend"] = "#FFD166", ["froh"] = "#BDE6A8", ["zufrieden"] = "#E8E2D6", ["muffig"] = "#F7C59F", ["elend"] = "#F4A6A6",
        };

        private readonly GameHost _host;
        private readonly Juice _juice = new Juice();
        private RanchHofStage _stage;
        private RanchWetterView _wetter;
        private string _sel;
        private bool _gekauft, _night;
        private float _refreshIn = 1f, _idleIn = 7f;
        private VisualElement _field, _horseCol, _card, _dock, _overlay, _bubble;
        private Label _hufChip, _wieseLabel;
        private Button _wieseTag;
        private Label _subLabel, _hayLabel, _appleLabel, _carrotLabel, _coinsLabel, _nameLabel, _levelLabel, _moodChip, _bondChip;
        private VisualElement _hayPill, _applePill, _coinPill;
        private readonly Dictionary<string, (VisualElement fill, Label value)> _bars = new Dictionary<string, (VisualElement, Label)>();
        private readonly Dictionary<string, Button> _actionBtns = new Dictionary<string, Button>();
        private readonly Dictionary<string, Label> _badges = new Dictionary<string, Label>();
        private readonly Dictionary<string, Button> _trayChips = new Dictionary<string, Button>();
        private VisualElement _tray;
        private Button _tageChip, _fuersorgeBtn, _stammBtn;
        private VisualElement _zuchtRow;
        private Label _zuchtLabel;
        private Label _tageLabel;
        private readonly Dictionary<string, Button> _horseBtns = new Dictionary<string, Button>();

        public RanchHofScreen(GameHost host)
        {
            _host = host;
            Id = "ranchhof";
            Opaque = true;
        }

        public override string SoundScene => _wetter?.Scene ?? "ranch/hof";

        /// <summary>Puddles on the yard, in the paddock and before the stable (off the horses' lanes).</summary>
        private static readonly Vector3[] Pfuetzen =
        {
            new Vector3(-2.7f, 0f, 0.3f), new Vector3(3.0f, 0f, 0.5f), new Vector3(5.4f, 0f, 2.2f), new Vector3(-1.9f, 0f, 6.9f),
            new Vector3(2.2f, 0f, 7.1f), new Vector3(-4.0f, 0f, 8.7f), new Vector3(0.9f, 0f, 12.6f), new Vector3(-4.6f, 0f, 13.4f),
        };

        private JObject State => _host.Store.State;
        private long Now => _host.Store.Clock.NowMs();
        private string Today => _host.Store.Clock.LocalDay(Now);
        private bool Reduced => _host.Settings.ReducedMotion;
        private string T(string key, params (string, object)[] args) => _host.Strings.T(key, args);

        // ── build ──

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-ranchhof");
            _field = new VisualElement { name = "ranchhof-field" };
            Ux.Abs(_field, 0, 0, 0, 0);
            _field.RegisterCallback<PointerDownEvent>(e => FeldTippen(e.localPosition));
            root.Add(_field);
            var events = Aufholen();
            _gekauft = RanchHof.IstGekauft(State);
            _night = RanchOffline.IstNacht(RanchOffline.StundeLokal(Now, _host.Store.Clock.UtcOffsetMinutes(Now)));
            if (MgStage3D.Supported)
            {
                var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(State["cosmetics"]));
                _stage = new RanchHofStage(_field, outfit, _night);
            }

            var back = Ux.IconBtn("back_white", Quit, "gb-place-back");
            back.name = "ranchhof-quit";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.Row("gb-card", "gb-ranchhof-head");
            head.pickingMode = PickingMode.Ignore;
            Ux.Abs(head, 84, 12);
            head.Add(Ux.IconTile("horseshoe", new Color(0.98f, 0.86f, 0.62f)));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(T("ranch.hof.titel"), "gb-place-title"));
            _subLabel = Ux.Text("", "gb-sm", "gb-mild");
            _subLabel.name = "ranchhof-sub";
            hcol.Add(_subLabel);
            head.Add(hcol);
            root.Add(head);
            _tageChip = new Button(ZeigeTage) { name = "ranchhof-tage" };
            _tageChip.ClearClassList();
            _tageChip.AddToClassList("gb-ranchhof-tagechip");
            var star = Ux.Icon("star");
            star.pickingMode = PickingMode.Ignore;
            _tageChip.Add(star);
            _tageLabel = Ux.Text("0/3", "gb-ranchhof-tagechip-text");
            _tageLabel.pickingMode = PickingMode.Ignore;
            _tageChip.Add(_tageLabel);
            head.pickingMode = PickingMode.Position;
            head.Add(_tageChip);
            var wetterStage = _stage != null ? new StageWeather(_stage.Stage, new Vector3(0.5f, 0f, 6.5f), new Vector3(13f, 1f, 11f), 1f, Pfuetzen) : null;
            _wetter = new RanchWetterView(_host, "hof", "hof", wetterStage, _field);
            head.Add(_wetter.Chip);
            BaueQuests(head, root);
            _wieseTag = new Button(WieseTippen) { name = "ranchhof-wiese" };
            _wieseTag.ClearClassList();
            _wieseTag.AddToClassList("gb-ranchdorf-tag");
            _wieseTag.AddToClassList("gb-ranchhof-wiesetag");
            var wieseIcon = Ux.Icon("hay");
            wieseIcon.pickingMode = PickingMode.Ignore;
            _wieseTag.Add(wieseIcon);
            _wieseLabel = Ux.Text("", "gb-ranchdorf-tag-text");
            _wieseLabel.pickingMode = PickingMode.Ignore;
            _wieseTag.Add(_wieseLabel);
            _wieseTag.style.visibility = Visibility.Hidden;
            root.Add(_wieseTag);

            var pills = Ux.Row("gb-ranchhof-pills");
            Ux.Abs(pills, null, 14, 16);
            _hayPill = Ux.Pill("hay", "0", out _hayLabel);
            _hayPill.name = "ranchhof-heu";
            pills.Add(_hayPill);
            _applePill = Ux.Pill("food_apple", "0", out _appleLabel);
            _applePill.name = "ranchhof-apfel";
            pills.Add(_applePill);
            var carrots = Ux.Pill("food_carrot", "0", out _carrotLabel);
            carrots.name = "ranchhof-karotte";
            pills.Add(carrots);
            _coinPill = Ux.Pill("coin", "0", out _coinsLabel);
            _coinPill.name = "ranchhof-coins";
            pills.Add(_coinPill);
            root.Add(pills);

            _horseCol = Ux.Col("gb-ranchhof-horses");
            _horseCol.name = "ranchhof-pferde";
            root.Add(_horseCol);

            _card = Ux.Col("gb-card", "gb-ranchhof-card");
            _card.name = "ranchhof-karte";
            var nameRow = Ux.Row("gb-ranchhof-namerow");
            _nameLabel = Ux.Text("", "gb-h2");
            _nameLabel.name = "ranchhof-name";
            nameRow.Add(_nameLabel);
            _levelLabel = Ux.Text("", "gb-ranchhof-level");
            nameRow.Add(_levelLabel);
            _card.Add(nameRow);
            var chips = Ux.Row("gb-ranchhof-chips");
            _moodChip = Ux.Text("", "gb-ranchhof-chip");
            _moodChip.name = "ranchhof-laune";
            chips.Add(_moodChip);
            _bondChip = Ux.Text("", "gb-ranchhof-chip", "gb-ranchhof-chip--bond");
            _bondChip.name = "ranchhof-bindung";
            chips.Add(_bondChip);
            _hufChip = Ux.Text(T("rhof.event.hufschmied.chip"), "gb-ranchhof-chip");
            _hufChip.style.backgroundColor = ArtLibrary.Hex("#D8E6F7");
            chips.Add(_hufChip);
            _card.Add(chips);
            _zuchtRow = Ux.Row("gb-ranchhof-zuchtrow");
            _zuchtRow.name = "ranchhof-zucht";
            _zuchtLabel = Ux.Text("", "gb-sm", "gb-ranchhof-zuchttext");
            _zuchtRow.Add(_zuchtLabel);
            _fuersorgeBtn = Ux.IconTextBtn("heart", T("rhof.zucht.fuersorge"), Fuersorge, "gb-btn-confirm", "gb-btn-sm");
            _fuersorgeBtn.name = "ranchhof-fuersorge";
            _zuchtRow.Add(_fuersorgeBtn);
            _stammBtn = Ux.IconTextBtn("heart", T("rpferd.stammbaum.titel"), ZeigeStammbaum, "gb-btn-secondary", "gb-btn-sm");
            _stammBtn.name = "ranchhof-stammbaum";
            _zuchtRow.Add(_stammBtn);
            _card.Add(_zuchtRow);
            foreach (var (key, hex) in Werte)
            {
                var row = Ux.Row("gb-ranchhof-bar-row");
                row.Add(Ux.Text(T("ranchplay.werte." + key), "gb-ranchhof-bar-label"));
                var bar = Ux.Bar(0f, out var fill);
                bar.AddToClassList("gb-ranchhof-bar");
                fill.style.backgroundColor = ArtLibrary.Hex(hex);
                row.Add(bar);
                var value = Ux.Text("", "gb-ranchhof-bar-value");
                row.Add(value);
                _bars[key] = (fill, value);
                _card.Add(row);
            }
            root.Add(_card);

            _dock = Ux.Row("gb-ranchhof-dock");
            _dock.name = "ranchhof-dock";
            var hof = Ux.IconTextBtn("hammer", T("rhof.hof"), ZeigeHof, "gb-btn-secondary", "gb-ranchhof-bigbtn");
            hof.name = "ranchhof-hof";
            _dock.Add(hof);
            var actions = Ux.Row("gb-card", "gb-ranchhof-actions");
            foreach (var (id, icon, labelKey) in Aktionen)
            {
                var aktion = id;
                var btn = new Button(() => { if (aktion == Fuettern) ToggleTray(); else Pflegen(aktion); }) { name = "ranchhof-" + id };
                btn.ClearClassList();
                btn.AddToClassList("gb-ranchhof-action");
                btn.Add(ActionFace(icon, T(labelKey)));
                if (id == Fuettern)
                {
                    var badge = Ux.Text("0", "gb-ranchhof-badge");
                    badge.pickingMode = PickingMode.Ignore;
                    btn.Add(badge);
                    _badges[id] = badge;
                }
                actions.Add(btn);
                _actionBtns[id] = btn;
            }
            _dock.Add(actions);

            _tray = Ux.Row("gb-card", "gb-ranchhof-tray");
            _tray.name = "ranchhof-futter";
            _tray.style.display = DisplayStyle.None;
            foreach (var (id, icon) in FutterIcons)
            {
                var futter = id;
                var chip = new Button(() => Pflegen(futter)) { name = "ranchhof-" + id };
                chip.ClearClassList();
                chip.AddToClassList("gb-ranchhof-action");
                chip.Add(ActionFace(icon, T("rhof.futter." + id)));
                var badge = Ux.Text("0", "gb-ranchhof-badge");
                badge.pickingMode = PickingMode.Ignore;
                chip.Add(badge);
                _badges[id] = badge;
                _tray.Add(chip);
                _trayChips[id] = chip;
            }
            var reiten = Ux.IconTextBtn("horseshoe", T("rhof.reiten"), ZeigeReiten, "gb-btn-primary", "gb-ranchhof-bigbtn");
            reiten.name = "ranchhof-reiten";
            _dock.Add(reiten);
            root.Add(_dock);
            root.Add(_tray);
            root.Add(_juice.Layer);

            TageNachReiten();
            BuildHorses();
            RefreshAll();
            root.schedule.Execute(Geburten).StartingIn(1500);
            root.schedule.Execute(RanchEreignis).StartingIn(3000);
            if (!_gekauft) root.schedule.Execute(ZeigeTor).StartingIn(300);
            else
            {
                var name = _sel != null ? PferdName(_sel) : "";
                var notes = events.Select(Notiz).Where(n => n.Length > 0).Distinct().Take(2).ToList();
                root.schedule.Execute(() => Sag(notes.Count > 0 ? string.Join("  ", notes) : _night ? T("rhof.schlaeft") : T("rhof.willkommen"))).StartingIn(900);
                if (_trainingToast != null)
                    root.schedule.Execute(() => { Sfx.PlayUi("soft_levelup"); _host.Ui.Toast(_trainingToast, "horseshoe", 3.5f); }).StartingIn(2400);
            }
            Debug.Log("[ranchhof] auf: gekauft=" + _gekauft + " pferde=" + RanchHof.PferdIds(State).Count + " nacht=" + _night + " events=" + string.Join(",", events));
        }

        private static VisualElement ActionFace(string icon, string text)
        {
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("gb-ranchhof-action-face");
            var disc = Ux.El("gb-ranchhof-action-disc");
            disc.pickingMode = PickingMode.Ignore;
            var ic = Ux.Icon(icon);
            ic.pickingMode = PickingMode.Ignore;
            disc.Add(ic);
            face.Add(disc);
            var label = Ux.Text(text, "gb-ranchhof-action-label");
            label.pickingMode = PickingMode.Ignore;
            face.Add(label);
            return face;
        }

        public bool TrayOpen => _tray != null && _tray.style.display == DisplayStyle.Flex;

        /// <summary>The food tray above the dock: every feed with its stock, empty ones greyed (a tap says where to get it).</summary>
        private void ToggleTray()
        {
            var open = !TrayOpen;
            _tray.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _actionBtns[Fuettern].EnableInClassList("gb-ranchhof-action--an", open);
            if (open)
            {
                Sfx.PlayUi("soft_open");
                _juice.ScalePop(_tray, 1.06f, 0.18f);
            }
            else Sfx.PlayUi("soft_chip");
        }

        private void CloseTray()
        {
            if (!TrayOpen) return;
            _tray.style.display = DisplayStyle.None;
            _actionBtns[Fuettern].EnableInClassList("gb-ranchhof-action--an", false);
        }

        /// <summary>
        /// The closed time on the horses (offline rules, the build's pasture facilities helping) — once per visit, before
        /// anything is shown. The old extensions move onto the build grid the first time, and the horse walker trains.
        /// </summary>
        private List<string> Aufholen()
        {
            List<string> events = null;
            if (!RanchHof.IstGekauft(State)) return new List<string>();
            var now = Now;
            var offset = _host.Store.Clock.UtcOffsetMinutes(now);
            var tag = Today;
            List<RanchBauEffekte.Training> training = null;
            _host.Store.Update(s =>
            {
                RanchBauState.MigriereBestand(s);
                RanchBelohnungen.Liefern(s);
                events = RanchPflege.Aufholen(s, now, offset);
                training = RanchBauEffekte.FuehranlageTag(s, tag, now);
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            if (training != null && training.Count > 0)
            {
                var namen = string.Join(", ", training.Select(t => PferdName(t.PferdId)));
                var xp = Mathf.RoundToInt((float)training[0].Buchung.Gewinn);
                _trainingToast = T("rbau.ui.fuehranlage", ("namen", namen), ("xp", xp));
                Debug.Log("[ranchhof] fuehranlage " + string.Join(",", training.Select(t => t.PferdId)) + " +" + xp);
            }
            return events ?? new List<string>();
        }

        private string _trainingToast;

        private string Notiz(string ev)
        {
            var i = ev.IndexOf(':');
            var kind = i < 0 ? ev : ev.Substring(0, i);
            var id = i < 0 ? "" : ev.Substring(i + 1);
            return kind == "stallDreckig" || id.Length > 0 ? T("rhof.event." + kind, ("name", PferdName(id))) : "";
        }

        // ── horses ──

        private List<(string id, JObject pferd)> Pferde()
        {
            if (_gekauft) return RanchHof.PferdIds(State).Select(id => (id, RanchHof.Pferd(State, id))).ToList();
            // the teaser before the purchase: the start horses as they would come
            return RanchHof.StartTiere.Where(t => t.Art == "pferd")
                .Select(t => (t.Id, new JObject { ["name"] = "", ["nameKey"] = t.NameKey, ["farbe"] = t.FellId, ["farbeHex"] = t.Farbe, ["maehneHex"] = t.Maehne }))
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        }

        private string PferdName(string id)
        {
            var p = RanchHof.Pferd(State, id) ?? (JObject)Pferde().FirstOrDefault(x => x.id == id).pferd;
            if (p == null) return "";
            var key = J.Str(p["nameKey"]);
            return key.Length > 0 ? T(key) : J.Str(p["name"]);
        }

        private static (Color fell, Color maehne) Fell(JObject p) => RanchPferdLook.Fell(p);

        private void BuildHorses()
        {
            var pferde = Pferde();
            _stage?.SetHorses(pferde.Select(p => { var (f, m) = Fell(p.pferd); return (p.id, f, m, _gekauft ? (float)RanchZucht.PhasenSkala(RanchZucht.Alter(p.pferd, Now, RanchBauEffekte.AufzuchtTempo(State))) : 1f); }).ToList());
            KleidePferde();
            _horseCol.Clear();
            _horseBtns.Clear();
            foreach (var (id, pferd) in pferde)
            {
                var pid = id;
                var btn = new Button(() => Waehle(pid)) { name = "ranchhof-pferd-" + id };
                btn.ClearClassList();
                btn.AddToClassList("gb-ranchhof-horse");
                var disc = Ux.El("gb-ranchhof-horse-disc");
                disc.pickingMode = PickingMode.Ignore;
                disc.style.backgroundColor = Fell(pferd).fell;
                var ic = Ux.Icon("horseshoe");
                ic.pickingMode = PickingMode.Ignore;
                disc.Add(ic);
                btn.Add(disc);
                var name = Ux.Text(PferdName(id), "gb-ranchhof-horse-name");
                name.pickingMode = PickingMode.Ignore;
                btn.Add(name);
                var mood = Ux.El("gb-ranchhof-horse-mood");
                mood.pickingMode = PickingMode.Ignore;
                btn.Add(mood);
                _horseCol.Add(btn);
                _horseBtns[id] = btn;
            }
            if (_sel == null || !pferde.Any(p => p.id == _sel)) Waehle(BestePflege(pferde), instant: true);
        }

        /// <summary>Every horse on stage wears its gear and looks (the teaser before the purchase goes bare).</summary>
        private void KleidePferde()
        {
            if (_stage == null) return;
            foreach (var (id, _) in Pferde()) RanchPferdLook.Kleide(_stage.HorseView(id), State, _gekauft ? id : null);
        }

        /// <summary>The horse that needs you most comes first (lowest mood); ties keep the save order.</summary>
        private string BestePflege(List<(string id, JObject pferd)> pferde)
        {
            if (pferde.Count == 0) return null;
            if (!_gekauft) return pferde.Any(p => p.id == "tier_pferd_karamell") ? "tier_pferd_karamell" : pferde[0].id;
            return pferde.OrderBy(p => RanchCare.Laune(J.Dict(p.pferd["werte"]), J.NumOr(p.pferd["bindung"], 0.0))).First().id;
        }

        private void Waehle(string id, bool instant = false)
        {
            if (string.IsNullOrEmpty(id)) return;
            var wechsel = _sel != id;
            _sel = id;
            _stage?.Select(id, instant);
            foreach (var kv in _horseBtns) kv.Value.EnableInClassList("gb-ranchhof-horse--on", kv.Key == id);
            if (wechsel && !instant)
            {
                Sfx.Play(UnityEngine.Random.value < 0.5f ? "ranch_schnauben_a" : "ranch_schnauben_b", 0f, 0.8f);
                Debug.Log("[ranchhof] pferd " + id);
            }
            if (wechsel && _gekauft && RanchHof.ReitPferd(State) != id) _host.Store.Update(s => RanchHof.SetzeReitPferd(s, id), GameHost.TicksMs);
            RefreshCard();
        }

        // ── refresh ──

        private void RefreshAll()
        {
            _stage?.SetSchleife(_gekauft && RanchBelohnungen.Hat(State, "schleife_bronze"));
            var lager = RanchHof.Lager(State);
            var heu = J.Int(lager["heu"], _gekauft ? 0 : 4);
            var apfel = J.Int(lager["apfel"], _gekauft ? 0 : 2);
            var karotten = RanchPflege.Karotten(State);
            _hayLabel.text = Ux.FormatNumber(heu);
            _appleLabel.text = Ux.FormatNumber(apfel);
            _carrotLabel.text = Ux.FormatNumber(karotten);
            _coinsLabel.text = Ux.FormatNumber(J.Int(J.Path(State, "economy", "coins")));
            var gesamt = 0;
            foreach (var (id, _) in FutterIcons)
            {
                var n = id == RanchPflege.Heu ? heu : id == RanchPflege.Apfel ? apfel : _gekauft ? RanchPflege.Vorrat(State, id) : id == RanchPflege.Karotte ? karotten : 0;
                gesamt += n;
                _badges[id].text = n.ToString();
                _trayChips[id].EnableInClassList("gb-ranchhof-action--leer", n <= 0);
            }
            _badges[Fuettern].text = gesamt.ToString();
            _actionBtns[Fuettern].EnableInClassList("gb-ranchhof-action--leer", gesamt <= 0);
            foreach (var b in _actionBtns.Values) b.EnableInClassList("gb-ranchhof-action--zu", !_gekauft);
            var pferde = Pferde();
            var heuIn = RanchWirtschaft.HeuBereitIn(State, Now);
            _subLabel.text = _gekauft ? T("rhof.untertitel", ("pferde", pferde.Count)) : T("ranch.tor.text");
            _wieseLabel.text = heuIn <= 0 ? T("rhof.wiese_tag") : Dauer(heuIn);
            _wieseTag.EnableInClassList("gb-ranchhof-wiesetag--reif", heuIn <= 0);
            foreach (var (id, pferd) in pferde)
            {
                if (!_horseBtns.TryGetValue(id, out var btn)) continue;
                var mood = btn.Q(className: "gb-ranchhof-horse-mood");
                if (mood == null) continue;
                var band = _gekauft ? RanchCare.LauneBand(RanchCare.Laune(J.Dict(pferd["werte"]), J.NumOr(pferd["bindung"], 0.0))) : "zufrieden";
                mood.style.backgroundColor = ArtLibrary.Hex(BandFarbe[band]);
                btn.EnableInClassList("gb-ranchhof-horse--needs", band == "muffig" || band == "elend");
            }
            var tage = RanchTage.Heute(State, Today);
            _tageLabel.text = tage.Count(t => t.Fertig) + "/" + tage.Count;
            _tageChip.style.display = _gekauft ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshQuests();
            _tageChip.EnableInClassList("gb-ranchhof-tagechip--bereit", tage.Any(t => t.Fertig && !t.Abgeholt));
            if (_stage != null)
            {
                _stage.SetBales(heu);
                _stage.SetMeadow(heuIn <= 0);
                for (var i = 0; i < 3; i++) _stage.SetApples(i, RanchWirtschaft.ApfelBereitIn(State, i, Now) <= 0);
                _stage.SetStall(_gekauft ? RanchHof.Stall(State) : 100.0);
            }
            RefreshCard();
        }

        private void RefreshCard()
        {
            if (_sel == null) { _card.style.display = DisplayStyle.None; return; }
            _card.style.display = DisplayStyle.Flex;
            var p = _gekauft ? RanchHof.Pferd(State, _sel) : null;
            _nameLabel.text = PferdName(_sel);
            _levelLabel.text = T("rhof.level", ("n", J.Int(p?["level"], 1)));
            var werte = p != null ? J.Dict(p["werte"]) : new JObject { ["hunger"] = 80.0, ["durst"] = 85.0, ["sauberkeit"] = 75.0 };
            var bindung = J.NumOr(p?["bindung"], 0.0);
            var band = RanchCare.LauneBand(RanchCare.Laune(werte, bindung));
            _moodChip.text = T("ranchplay.laune." + band);
            _moodChip.style.backgroundColor = ArtLibrary.Hex(BandFarbe[band]);
            _bondChip.text = T("ranchplay.bindung." + RanchCare.BindungStufe(bindung));
            _hufChip.style.display = _gekauft && RanchEvents.HufCheckAktiv(State, Now) ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshZucht(p);
            foreach (var (key, _) in Werte)
            {
                var v = key == "bindung" ? bindung : key == "stall" ? (_gekauft ? RanchHof.Stall(State) : 100.0) : J.NumOr(werte[key], 0.0);
                var (fill, label) = _bars[key];
                Ux.SetBar(fill, v / 100.0);
                fill.style.backgroundColor = ArtLibrary.Hex(key != "bindung" && RanchCare.IstNiedrig(v) ? "#E8574C" : Werte.First(w => w.key == key).hex);
                label.text = Mathf.RoundToInt((float)v).ToString();
            }
        }

        private static string Dauer(long ms)
        {
            var min = (int)Math.Ceiling(ms / 60000.0);
            return min >= 60 ? (min / 60) + " h " + (min % 60).ToString("00") + " min" : min + " min";
        }

        // ── care ──

        private void Pflegen(string aktion)
        {
            if (!_gekauft) { Sfx.PlayUi("soft_error"); Sag(T("rhof.nur_schauen")); ZeigeTor(); return; }
            if (_sel == null) return;
            var istFutter = Array.IndexOf(RanchPflege.FutterIds, aktion) >= 0;
            if (!istFutter) CloseTray();
            var knopf = _actionBtns.TryGetValue(aktion, out var a) ? a : _trayChips.TryGetValue(aktion, out var c) ? c : null;
            var id = _sel;
            var name = PferdName(id);
            var stufeVor = RanchCare.BindungStufe(J.NumOr(RanchHof.Pferd(State, id)?["bindung"], 0.0));
            RanchPflege.Ergebnis res = null;
            var now = Now;
            var tag = Today;
            _host.Store.Update(s => res = RanchPflege.Pflegen(s, id, aktion, now, tag), GameHost.TicksMs);
            if (res == null || !res.Ok)
            {
                Sfx.PlayUi("soft_error");
                if (res?.Code == RanchWirtschaft.LagerLeer) Sag(T("rhof.leer." + aktion));
                if (knopf != null) _juice.ScalePop(knopf, 1.08f, 0.18f);
                Debug.Log("[ranchhof] pflege " + aktion + " nein: " + res?.Code);
                return;
            }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            if (aktion == RanchPflege.Karotte) _host.Store.NotifySliceChanged("inventory");
            switch (aktion)
            {
                case RanchPflege.Heu:
                case RanchPflege.Hafer:
                    _stage?.Feed(aktion);
                    Sfx.Play("ranch_heu", 0f, 0.7f);
                    break;
                case RanchPflege.Apfel:
                case RanchPflege.Karotte:
                case RanchPflege.Leckerli:
                    _stage?.Feed(aktion);
                    Sfx.Play("nom_nom");
                    break;
                case RanchPflege.Traenken:
                    _stage?.Drink();
                    Sfx.Play("care_wasser", 0f, 0.8f);
                    break;
                case RanchPflege.Striegeln:
                    _stage?.Brush();
                    Sfx.Play("ranch_striegeln", 0f, 0.7f);
                    break;
                case RanchPflege.Ausmisten:
                    _stage?.MuckOut(true);
                    Sfx.Play("build_hammer", -2f, 0.6f);
                    break;
            }
            if (knopf != null) _juice.ScalePop(knopf, 1.12f, 0.2f);
            var top = FieldPoint(_stage != null ? _stage.HorseTop : Vector3.zero);
            Root.schedule.Execute(() =>
            {
                _juice.Burst(top, new Color(1f, 0.55f, 0.66f), 12);
                if (res.Gewinn > 0) _host.Ui.FloatText(top + new Vector2(0f, -20f), T("ranchplay.pflege.bindung_plus", ("n", Mathf.RoundToInt((float)res.Gewinn))), new Color(0.95f, 0.45f, 0.6f), 30f);
                else _host.Ui.FloatText(top + new Vector2(0f, -20f), T("ranchplay.pflege.gemacht"), new Color(0.45f, 0.75f, 0.55f), 26f);
                if (UnityEngine.Random.value < 0.45f) Sfx.Play(UnityEngine.Random.value < 0.5f ? "ranch_wiehern_a" : "ranch_wiehern_b", 0f, 0.55f);
            }).StartingIn(aktion == RanchPflege.Striegeln || aktion == RanchPflege.Ausmisten ? 900 : 600);
            if (res.Warmwasser > 0)
            {
                var warm = res.Warmwasser;
                Root.schedule.Execute(() =>
                {
                    Sfx.Play("care_spuelung", 0f, 0.6f);
                    _host.Ui.Toast(T("rbau.ui.warmwasser", ("n", warm)), "brush");
                }).StartingIn(1100);
            }
            var stufeNach = RanchCare.BindungStufe(res.Bindung);
            if (stufeNach != stufeVor)
            {
                Root.schedule.Execute(() =>
                {
                    _juice.Confetti(60);
                    Sfx.PlayUi("soft_levelup");
                    _host.Ui.Toast(T("rhof.stufe", ("name", name), ("stufe", T("ranchplay.bindung." + stufeNach))), "heart");
                    _stage?.HorseReact("kopfschuetteln");
                }).StartingIn(1300);
            }
            var satt = res.Laune >= 92.0 && res.Gewinn <= 0;
            Sag(satt ? T("rhof.voll", ("name", name)) : T("rhof.reaktion." + aktion, ("name", name)));
            Debug.Log("[ranchhof] pflege " + aktion + " " + id + " bindung=" + res.Bindung.ToString("0.#") + " +" + res.Gewinn + " laune=" + res.Laune.ToString("0"));
            Melde("pflege:" + (istFutter ? "fuettern" : aktion));
            _idleIn = 9f;
            RefreshAll();
        }

        // ── the day's tasks ──

        /// <summary>Notes ranch progress for today's tasks; a task just finished gets a toast and the star chip glows.</summary>
        private void Melde(string ereignis, int n = 1)
        {
            if (!_gekauft) return;
            List<string> fertig = null;
            var tag = Today;
            _host.Store.Update(s => fertig = RanchTage.Melde(s, tag, ereignis, n), GameHost.TicksMs);
            Geschafft(fertig);
            var teile = ereignis.Split(':');
            if (teile.Length == 2) QuestMelde(teile[0], teile[1], n);
        }

        private void Geschafft(IEnumerable<string> ids)
        {
            var i = 0;
            foreach (var id in ids ?? Enumerable.Empty<string>())
            {
                var text = T("rhof.tage.text." + id);
                Root.schedule.Execute(() =>
                {
                    Sfx.PlayUi("soft_levelup");
                    _host.Ui.Toast(T("rhof.tage.geschafft", ("text", text)), "star");
                    _juice.ScalePop(_tageChip, 1.25f, 0.3f);
                }).StartingIn(1500 + 900 * i++);
                Debug.Log("[ranchhof] aufgabe geschafft " + id);
            }
        }

        /// <summary>A new ranch game played since the farm was last shown finishes "reiten" here.</summary>
        private void TageNachReiten()
        {
            if (!_gekauft) return;
            var tag = Today;
            var vorher = RanchTage.Heute(State, tag).Where(t => t.Fertig).Select(t => t.Aufgabe.Id).ToList();
            _host.Store.Update(s => RanchTage.Im(s, tag), GameHost.TicksMs);
            if (_tageFertig != null) Geschafft(vorher.Except(_tageFertig));
            _tageFertig = vorher;
        }

        private List<string> _tageFertig;

        private void ZeigeTage()
        {
            CloseTray();
            var tag = Today;
            var body = OpenSheet("ranchhof-tagesheet", "star", new Color(1f, 0.86f, 0.5f), T("rhof.tage.titel"), T("rhof.tage.untertitel"));
            foreach (var s in RanchTage.Heute(State, tag))
            {
                var a = s.Aufgabe;
                var row = Ux.Row("gb-ranchhof-taskrow");
                row.name = "ranchhof-aufgabe-" + a.Id;
                var disc = Ux.El("gb-ranchhof-action-disc");
                disc.Add(Ux.Icon(a.Icon));
                row.Add(disc);
                var col = Ux.Col("gb-ranchhof-taskcol");
                col.Add(Ux.Text(T("rhof.tage.text." + a.Id), "gb-md", "gb-bold"));
                var bar = Ux.Bar(s.Fortschritt / (float)a.Ziel, out var fill);
                bar.AddToClassList("gb-ranchhof-bar");
                fill.style.backgroundColor = ArtLibrary.Hex(s.Fertig ? "#8FC98A" : "#F2C14E");
                var barRow = Ux.Row("gb-ranchhof-bar-row");
                barRow.Add(bar);
                barRow.Add(Ux.Text(s.Fortschritt + "/" + a.Ziel, "gb-ranchhof-bar-value"));
                col.Add(barRow);
                row.Add(col);
                if (s.Abgeholt) row.Add(Ux.Text("✓ " + T("rhof.tage.abgeholt"), "gb-md", "gb-ranchhof-ok"));
                else
                {
                    var id = a.Id;
                    var btn = Ux.IconTextBtn("coin", T("rhof.tage.abholen", ("n", a.Muenzen)), () => Abholen(id), s.Fertig ? "gb-btn-confirm" : "gb-btn-secondary", "gb-btn-sm");
                    btn.name = "ranchhof-abholen-" + a.Id;
                    btn.SetEnabled(s.Fertig);
                    row.Add(btn);
                }
                body.Add(row);
            }
            body.Add(Ux.Text(T("rhof.tage.morgen"), "gb-sm", "gb-mild", "gb-ranchhof-center"));
            Sfx.PlayUi("soft_open");
        }

        private void Abholen(string id)
        {
            var muenzen = 0;
            var tag = Today;
            _host.Store.Update(s => muenzen = RanchTage.Abholen(s, tag, id), GameHost.TicksMs);
            if (muenzen <= 0) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            var from = Root.Q("ranchhof-abholen-" + id)?.worldBound.center ?? Root.worldBound.center;
            _host.Ui.FlyIcons(from, _coinPill, "coin", Mathf.Clamp(muenzen / 8, 3, 8), _ => Sfx.PlayUi("soft_tick"));
            _juice.Confetti(40);
            Sfx.PlayUi("soft_buy");
            Debug.Log("[ranchhof] aufgabe abgeholt " + id + " +" + muenzen);
            ZeigeTage();
            RefreshAll();
        }

        private Vector2 FieldPoint(Vector3 local)
        {
            var w = _field.layout.width;
            var h = _field.layout.height;
            if (_stage == null || float.IsNaN(w) || w <= 0f) return new Vector2(Root.worldBound.center.x, Root.worldBound.height * 0.45f);
            return _stage.ToField(local, w, h);
        }

        // ── taps on the farm ──

        private void FeldTippen(Vector2 local)
        {
            if (_stage == null || _overlay != null) return;
            var (ziel, id, index) = _stage.Pick(local, _field.layout.width, _field.layout.height);
            Tippe(ziel, id, index);
        }

        private void Tippe(RanchHofStage.Ziel ziel, string id, int index)
        {
            if (EreignisTippen(ziel, id, index)) return;
            switch (ziel)
            {
                case RanchHofStage.Ziel.Pferd:
                    if (id == _sel && _gekauft)
                    {
                        _stage?.HorseReact("kopfschuetteln");
                        Sfx.Play("ranch_schnauben_a", 2f, 0.6f);
                        QuestMelde("pflege", "taetscheln");
                    }
                    QuestMelde("gehe_zu", "koppel_pferde");
                    Waehle(id);
                    break;
                case RanchHofStage.Ziel.Trog: QuestMelde("gehe_zu", "trog"); Pflegen(RanchPflege.Traenken); break;
                case RanchHofStage.Ziel.Stall: QuestMelde("gehe_zu", "stall"); Pflegen(RanchPflege.Ausmisten); break;
                case RanchHofStage.Ziel.Ballen: QuestMelde("gehe_zu", "heulager"); ZeigeHof(); break;
                case RanchHofStage.Ziel.Person: ZeigePerson(id); break;
                case RanchHofStage.Ziel.Wiese: Maehen(); break;
                case RanchHofStage.Ziel.Baum: Pfluecken(index); break;
            }
        }

        /// <summary>The meadow's tag floats over the hay field: "Mähen!" when it is ripe (a gentle bob), else the time left.</summary>
        private void PlaceWieseTag()
        {
            var w = _field.layout.width;
            var show = _gekauft && _stage != null && _overlay == null && !float.IsNaN(w) && w > 0f;
            _wieseTag.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
            if (!show) return;
            var p = FieldPoint(RanchHofStage.FieldCentre + Vector3.up * 1.6f);
            var tw = _wieseTag.layout.width;
            var reif = _wieseTag.ClassListContains("gb-ranchhof-wiesetag--reif");
            var bob = reif && !Reduced ? Mathf.Sin(Time.unscaledTime * 3.2f) * 4f : 0f;
            _wieseTag.style.left = p.x - (float.IsNaN(tw) ? 50f : tw * 0.5f);
            _wieseTag.style.top = p.y - 19f + bob;
        }

        private void WieseTippen()
        {
            if (RanchWirtschaft.HeuBereitIn(State, Now) <= 0) Maehen();
            else
            {
                Sfx.PlayUi("soft_tap");
                Sag(T("rhof.wiese_waechst", ("zeit", Dauer(RanchWirtschaft.HeuBereitIn(State, Now)))));
            }
        }

        private void Maehen()
        {
            if (!_gekauft) { ZeigeTor(); return; }
            (string code, int menge) res = (RanchWirtschaft.Unbekannt, 0);
            var now = Now;
            _host.Store.Update(s => res = RanchWirtschaft.HeuErnten(s, now), GameHost.TicksMs);
            if (res.code != RanchWirtschaft.Ok)
            {
                Sfx.PlayUi("soft_error");
                Sag(T("rhof.wiese_waechst", ("zeit", Dauer(RanchWirtschaft.HeuBereitIn(State, Now)))));
                return;
            }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _stage?.Mow();
            Sfx.Play("ranch_heu");
            var from = FieldPoint(RanchHofStage.FieldCentre + Vector3.up * 0.8f);
            _host.Ui.FloatText(from + new Vector2(0f, -30f), T("rhof.gemaeht", ("n", res.menge)), new Color(0.95f, 0.78f, 0.3f), 32f);
            _host.Ui.FlyIcons(from, _hayPill, "hay", res.menge, _ => Sfx.PlayUi("soft_collect"));
            Debug.Log("[ranchhof] gemaeht +" + res.menge);
            Melde("sammle:heu", res.menge);
            Root.schedule.Execute(RefreshAll).StartingIn(700);
        }

        private void Pfluecken(int baum)
        {
            if (!_gekauft) { ZeigeTor(); return; }
            (string code, int menge) res = (RanchWirtschaft.Unbekannt, 0);
            var now = Now;
            _host.Store.Update(s => res = RanchWirtschaft.ApfelPfluecken(s, baum, now), GameHost.TicksMs);
            if (res.code != RanchWirtschaft.Ok)
            {
                Sfx.PlayUi("soft_error");
                Sag(T("rhof.baum_waechst", ("zeit", Dauer(RanchWirtschaft.ApfelBereitIn(State, baum, Now)))));
                return;
            }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _stage?.Pick(baum);
            Sfx.PlayUi("soft_collect");
            var from = FieldPoint(_stage != null ? _stage.TreeTop(baum) : Vector3.zero);
            _host.Ui.FloatText(from + new Vector2(0f, -24f), T("rhof.gepflueckt", ("n", res.menge)), new Color(0.92f, 0.36f, 0.32f), 30f);
            _host.Ui.FlyIcons(from, _applePill, "food_apple", res.menge, _ => Sfx.PlayUi("soft_tick"));
            Debug.Log("[ranchhof] gepflueckt baum " + baum + " +" + res.menge);
            Melde("sammle:apfel", res.menge);
            Root.schedule.Execute(RefreshAll).StartingIn(700);
        }

        // ── Gooby's line over the farm ──

        private void Sag(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _bubble?.RemoveFromHierarchy();
            var bubble = Ux.Row("gb-card", "gb-ranchhof-bubble");
            bubble.name = "ranchhof-sagt";
            bubble.pickingMode = PickingMode.Ignore;
            var label = Ux.Text(text, "gb-ranchhof-bubble-text");
            bubble.Add(label);
            Root.Add(bubble);
            _bubble = bubble;
            _juice.ScalePop(bubble, 1.05f, 0.18f);
            bubble.schedule.Execute(() => { if (_bubble == bubble) { _juice.FadeOut(bubble, 0.4f); } }).StartingIn(5200);
            bubble.schedule.Execute(() => { if (_bubble == bubble) { bubble.RemoveFromHierarchy(); _bubble = null; } }).StartingIn(5700);
        }

        // ── the gate (before the purchase) ──

        private void ZeigeTor()
        {
            if (_gekauft) return;
            var card = OpenCard("ranchhof-tor");
            card.Add(Ux.Text(T("ranch.tor.titel"), "gb-h1"));
            var text = Ux.Text(T("ranch.tor.text"), "gb-md");
            text.style.whiteSpace = WhiteSpace.Normal;
            card.Add(text);
            var check = RanchKauf.Check(State);
            card.Add(Ux.Text(T("ranch.angebot.preis", ("preis", RanchHof.Preis)), "gb-h2", "gb-ranchhof-center"));
            var level = J.Int(J.Path(State, "progression", "level"), 1);
            var hint = Ux.Text(check == RanchKauf.Gesperrt ? T("rhof.tor_gesperrt", ("level", RanchHof.FreischaltLevel), ("aktuell", level))
                : check == RanchKauf.Pleite ? T("ranch.tor.zu_teuer", ("fehlt", RanchHof.Preis - J.Int(J.Path(State, "economy", "coins")))) : T("ranch.angebot.frage"),
                "gb-sm", "gb-mild", "gb-ranchhof-center");
            hint.name = "ranchhof-torhinweis";
            hint.style.whiteSpace = WhiteSpace.Normal;
            card.Add(hint);
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var look = Ux.Btn(T("rhof.tor_umschauen"), CloseOverlay, "gb-btn-secondary");
            look.name = "ranchhof-umschauen";
            look.style.marginRight = 10;
            actions.Add(look);
            var buy = Ux.IconTextBtn("coin", T("ranch.tor.kaufen", ("preis", RanchHof.Preis)), Kaufen, check == RanchKauf.Ok ? "gb-btn-confirm" : "gb-btn-secondary");
            buy.name = "ranchhof-kaufen";
            actions.Add(buy);
            card.Add(actions);
        }

        private void Kaufen()
        {
            var result = RanchKauf.Pleite;
            var now = Now;
            _host.Store.Update(s => result = RanchKauf.Kaufe(s, now), GameHost.TicksMs);
            if (result != RanchKauf.Ok)
            {
                Sfx.PlayUi("soft_error");
                var hint = Root.Q<Label>("ranchhof-torhinweis");
                if (hint != null)
                    hint.text = result == RanchKauf.Gesperrt
                        ? T("rhof.tor_gesperrt", ("level", RanchHof.FreischaltLevel), ("aktuell", J.Int(J.Path(State, "progression", "level"), 1)))
                        : T("ranch.tor.zu_teuer", ("fehlt", RanchHof.Preis - J.Int(J.Path(State, "economy", "coins"))));
                return;
            }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            Sfx.Play("ranch_wiehern_a", 0f, 0.8f);
            CloseOverlay();
            _gekauft = true;
            _sel = null;
            TageNachReiten();
            BuildHorses();
            RefreshAll();
            _juice.Confetti(90);
            _host.Ui.Toast(T("ranch.tor.gekauft"), "horseshoe");
            Sag(T("rhof.willkommen"));
            Debug.Log("[ranchhof] gekauft");
        }

        // ── overlays: farm & shop, riding ──

        private VisualElement OpenCard(string name)
        {
            CloseOverlay();
            _overlay = Ux.El("gb-scrim", "gb-ranchhof-veil");
            _overlay.name = name + "-veil";
            var card = Ux.Col("gb-card", "gb-ranchhof-dialog");
            card.name = name;
            _overlay.Add(card);
            Root.Add(_overlay);
            _juice.ScalePop(card, 1.06f, 0.22f);
            return card;
        }

        private VisualElement OpenSheet(string name, string icon, Color tint, string title, string sub)
        {
            CloseOverlay();
            _overlay = Ux.El("gb-scrim", "gb-ranchhof-veil");
            _overlay.name = name + "-veil";
            _overlay.RegisterCallback<ClickEvent>(e => { if (e.target == _overlay) CloseOverlay(); });
            var sheet = Ux.Col("gb-card", "gb-ranchhof-sheet");
            sheet.name = name;
            var head = CounterPanel.Head(icon, tint, title, sub, CloseOverlay);
            sheet.Add(head.Row);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-ranchhof-sheet-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sheet.Add(scroll);
            _sheetScroll = scroll;
            _overlay.Add(sheet);
            Root.Add(_overlay);
            _juice.ScalePop(sheet, 1.04f, 0.2f);
            return scroll.contentContainer;
        }

        private ScrollView _sheetScroll;

        private void CloseOverlay()
        {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
        }

        private void ZeigeHof()
        {
            if (!_gekauft) { ZeigeTor(); return; }
            var body = OpenSheet("ranchhof-laden", "hammer", new Color(0.98f, 0.86f, 0.62f), T("ranchplay.ausbau.titel"), T("ranchplay.ausbau.coins", ("n", J.Int(J.Path(State, "economy", "coins")))));
            var bauen = new Button(Bauen) { name = "ranchhof-bauen" };
            bauen.ClearClassList();
            bauen.AddToClassList("gb-ranchhof-dorfcard");
            bauen.AddToClassList("gb-ranchhof-dorfcard--gross");
            var bauenIcon = Ux.IconTile("hammer", new Color(1f, 0.9f, 0.7f));
            bauenIcon.pickingMode = PickingMode.Ignore;
            bauen.Add(bauenIcon);
            var bauenText = Ux.Col();
            bauenText.pickingMode = PickingMode.Ignore;
            bauenText.style.marginLeft = 12;
            bauenText.style.flexShrink = 1;
            bauenText.Add(Ux.Text(T("rbau.ui.hof_karte"), "gb-h2"));
            var bau = RanchBauState.Lese(State);
            var bauenSub = Ux.Text(T("rbau.ui.hof_karte_text") + " " + T("rbau.ui.untertitel", ("stil", RanchBauEffekte.DekoStilpunkte(bau, null)), ("max", RanchBauEffekte.DekoStilMax(null)),
                ("boxen", RanchWirtschaft.BoxenKapazitaet(State)), ("heu", RanchBauEffekte.HeuKapazitaet(bau, null))), "gb-sm", "gb-mild");
            bauenSub.style.whiteSpace = WhiteSpace.Normal;
            bauenText.Add(bauenSub);
            bauen.Add(bauenText);
            body.Add(bauen);
            var lager = RanchHof.Lager(State);
            body.Add(Ux.Text(T("ranchplay.ausbau.lager"), "gb-ranchhof-sec"));
            body.Add(Ux.Text(T("ranchplay.ausbau.bestand", ("heu", J.Int(lager["heu"])), ("apfel", J.Int(lager["apfel"])), ("boxen", RanchWirtschaft.BoxenKapazitaet(State))), "gb-md"));
            var row = Ux.Row("gb-ranchhof-shoprow");
            row.Add(ShopBtn("ranchhof-heu-1", "hay", T("ranchplay.ausbau.heu_kaufen", ("preis", RanchWirtschaft.HeuKaufPreis + " ᴳ")), () => HeuKaufen(1)));
            row.Add(ShopBtn("ranchhof-heu-5", "hay", "5 × " + T("ranchplay.ausbau.heu_kaufen", ("preis", RanchWirtschaft.HeuKaufPreis * 5 + " ᴳ")), () => HeuKaufen(5)));
            var heuIn = RanchWirtschaft.HeuBereitIn(State, Now);
            var ernte = ShopBtn("ranchhof-ernten", "sprout", heuIn <= 0 ? T("ranchplay.ausbau.heu_ernten") : T("rhof.waechst", ("zeit", Dauer(heuIn))), () => { CloseOverlay(); Maehen(); });
            ernte.SetEnabled(heuIn <= 0);
            row.Add(ernte);
            for (var i = 0; i < 3; i++)
            {
                var baum = i;
                var inMs = RanchWirtschaft.ApfelBereitIn(State, i, Now);
                var pick = ShopBtn("ranchhof-baum-" + i, "food_apple", inMs <= 0 ? T("ranchplay.ausbau.apfel_pfluecken") : T("rhof.waechst", ("zeit", Dauer(inMs))), () => { CloseOverlay(); Pfluecken(baum); });
                pick.SetEnabled(inMs <= 0);
                row.Add(pick);
            }
            body.Add(row);

            ZuchtSection(body);
            body.Add(Ux.Text(T("ranchplay.ausbau.gear") + (_sel != null ? " · " + PferdName(_sel) : ""), "gb-ranchhof-sec"));
            RanchGearUi.Reihen(_host, body, _sel, "ranchhof", (slot, farbe) => RanchWirtschaft.GearPreisFuer(State, slot, farbe), Gear);
            RanchGearUi.Look(_host, body, _sel, "ranchhof", Look);
            RanchGearUi.Sets(_host, body, _sel, "ranchhof", SetSpeichern, SetAnlegen);
            Sfx.PlayUi("soft_open");
        }

        /// <summary>A look chip: on (with a sparkle round the horse) or off on the selected horse.</summary>
        private void Look(string look)
        {
            if (_sel == null) return;
            var sel = _sel;
            var an = !RanchBelohnungen.TraegtLook(RanchHof.Pferd(State, sel), look);
            var code = RanchBelohnungen.Unbekannt;
            _host.Store.Update(s => code = RanchBelohnungen.SetzeLook(s, sel, look, an), GameHost.TicksMs);
            if (code != RanchBelohnungen.Ok) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            BuildHorses();
            Sfx.PlayUi(an ? "soft_levelup" : "soft_chip");
            _host.Ui.Toast(T(an ? "ranchplay.look.an" : "ranchplay.look.ab", ("look", T("ranchplay.look." + look)), ("name", PferdName(sel))), "sparkle");
            if (an) Funkeln(sel, look == RanchBelohnungen.Nebel ? ArtLibrary.Hex("#DCE4EE") : ArtLibrary.Hex("#FFE7C2"));
            Debug.Log("[ranchhof] look " + sel + " " + look + " " + (an ? "an" : "ab"));
            ZeigeHof();
            RefreshAll();
        }

        private void SetSpeichern(int platz)
        {
            if (_sel == null) return;
            var sel = _sel;
            var code = RanchWirtschaft.Unbekannt;
            _host.Store.Update(s => code = RanchWirtschaft.OutfitSpeichern(s, platz, sel), GameHost.TicksMs);
            if (code != RanchWirtschaft.Ok) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            Sfx.PlayUi("soft_confirm");
            _host.Ui.Toast(T("ranchplay.sets.gespeichert", ("n", platz + 1), ("teile", RanchGearUi.TeileText(_host, RanchWirtschaft.OutfitSet(State, platz)))), "check");
            Debug.Log("[ranchhof] set " + platz + " gespeichert von " + sel);
            ZeigeHof();
        }

        private void SetAnlegen(int platz)
        {
            if (_sel == null) return;
            var sel = _sel;
            var code = RanchWirtschaft.Unbekannt;
            _host.Store.Update(s => code = RanchWirtschaft.OutfitAnlegen(s, platz, sel), GameHost.TicksMs);
            if (code != RanchWirtschaft.Ok) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            KleidePferde();
            Sfx.Play("ranch_sattel", 0f, 0.8f);
            Funkeln(sel, ArtLibrary.Hex("#FFE7C2"));
            _host.Ui.Toast(T("ranchplay.sets.angelegt", ("n", platz + 1)), "shirt");
            Debug.Log("[ranchhof] set " + platz + " an " + sel);
            ZeigeHof();
            RefreshAll();
        }

        /// <summary>A sparkle burst round a horse on stage (and it shakes its head with joy).</summary>
        private void Funkeln(string id, Color farbe)
        {
            var view = _stage?.HorseView(id);
            if (view == null) return;
            view.Action("kopfschuetteln");
            _juice.Burst(FieldPoint(view.transform.position - MgStage3D.Origin + Vector3.up * 1.7f), farbe, 18);
        }

        private Button ShopBtn(string name, string icon, string text, Action onClick)
        {
            var b = Ux.IconTextBtn(icon, text, onClick, "gb-btn-secondary", "gb-ranchhof-shopbtn");
            b.name = name;
            return b;
        }

        private void HeuKaufen(int n)
        {
            var result = RanchWirtschaft.Unbekannt;
            _host.Store.Update(s => result = RanchWirtschaft.HeuKaufen(s, n), GameHost.TicksMs);
            if (result == RanchWirtschaft.LagerVoll) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T("rbau.ui.heu_voll", ("n", RanchBauEffekte.HeuKapazitaet(State))), "hay", 3.5f); return; }
            if (result != RanchWirtschaft.Ok) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T("customize.zu_teuer"), "coin"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            CloseOverlay();
            _host.Ui.FlyIcons(FieldPoint(RanchHofStage.BalesSpot + Vector3.up), _hayPill, "hay", n, _ => Sfx.PlayUi("soft_tick"));
            _host.Ui.Toast(T("ranchplay.ausbau.heu_da"), "hay");
            RefreshAll();
        }

        private void Ausbauen(string id)
        {
            var result = RanchWirtschaft.Unbekannt;
            _host.Store.Update(s => result = RanchWirtschaft.AusbauKaufen(s, id), GameHost.TicksMs);
            if (result != RanchWirtschaft.Ok) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T(result == RanchWirtschaft.ZuTeuer ? "customize.zu_teuer" : "customize.fehler"), "coin"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_levelup");
            _juice.Confetti(50);
            _host.Ui.Toast(T("ranchplay.ausbau.gebaut_toast"), "hammer");
            Debug.Log("[ranchhof] ausbau " + id);
            ZeigeHof();
            RefreshAll();
        }

        /// <summary>A colour chip: bought → put on (or take off) the selected horse; not bought → buy it.</summary>
        private void Gear(string slot, string farbe)
        {
            var gid = slot + "_" + farbe;
            var result = RanchWirtschaft.Unbekannt;
            if (!RanchWirtschaft.GearBesitzt(State, gid))
            {
                _host.Store.Update(s => result = RanchWirtschaft.GearKaufen(s, gid), GameHost.TicksMs);
                if (result != RanchWirtschaft.Ok) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T("customize.zu_teuer"), "coin"); return; }
                Sfx.PlayUi("soft_buy");
                _host.Ui.Toast(T("ranchplay.ausbau.gear_toast"), "star");
                _host.Store.NotifySliceChanged("economy");
            }
            if (_sel != null)
            {
                var an = RanchWirtschaft.GearAn(State, _sel, slot) == farbe ? null : farbe;
                var sel = _sel;
                _host.Store.Update(s => result = RanchWirtschaft.GearAnlegen(s, sel, slot, an), GameHost.TicksMs);
                Sfx.PlayUi("soft_chip");
                if (an != null)
                {
                    Sfx.Play("ranch_sattel", 0f, 0.7f);
                    Funkeln(sel, ArtLibrary.Hex(RanchGear3D.Hex(an)));
                }
            }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            KleidePferde();
            Debug.Log("[ranchhof] gear " + gid + " → " + result);
            ZeigeHof();
            RefreshAll();
        }

        private void ZeigeReiten()
        {
            var name = _sel != null ? PferdName(_sel) : "";
            var bindung = _gekauft && _sel != null ? J.NumOr(RanchHof.Pferd(State, _sel)?["bindung"], 0.0) : 0.0;
            var (tempo, _) = RanchCare.ReitPerks(bindung);
            var sub = _gekauft && _sel != null ? name + " · " + T("ranchplay.bindung." + RanchCare.BindungStufe(bindung)) + (tempo > 1.0 ? " · +" + Mathf.RoundToInt((float)((tempo - 1.0) * 100)) + " % Galopp" : "") : T("rhof.nur_schauen");
            var body = OpenSheet("ranchhof-reitsheet", "horseshoe", new Color(0.99f, 0.82f, 0.6f), T("rhof.reiten"), sub);
            var aus = new Button(Ausreiten) { name = "ranchhof-ausreiten" };
            aus.ClearClassList();
            aus.AddToClassList("gb-ranchhof-dorfcard");
            aus.AddToClassList("gb-ranchhof-dorfcard--gross");
            var ausIcon = Ux.IconTile("compass", new Color(0.8f, 0.92f, 0.7f));
            ausIcon.pickingMode = PickingMode.Ignore;
            aus.Add(ausIcon);
            var ausText = Ux.Col();
            ausText.pickingMode = PickingMode.Ignore;
            ausText.style.marginLeft = 12;
            ausText.style.flexShrink = 1;
            ausText.Add(Ux.Text(T("rwelt.hud.ausreiten"), "gb-h2"));
            var f = RanchEntdeckerKarte.Fortschritt(State);
            var ausSub = Ux.Text(T("rwelt.ui.ausreiten_text") + " " + T("rkarte.fortschritt", ("funde", f.funde), ("funde_gesamt", f.fundeGesamt), ("zonen", f.zonen), ("zonen_gesamt", f.zonenGesamt)), "gb-sm", "gb-mild");
            ausSub.style.whiteSpace = WhiteSpace.Normal;
            ausText.Add(ausSub);
            aus.Add(ausText);
            body.Add(aus);
            var dorf = new Button(NachHufingen) { name = "ranchhof-hufingen" };
            dorf.ClearClassList();
            dorf.AddToClassList("gb-ranchhof-dorfcard");
            var dorfIcon = Ux.IconTile("map", new Color(0.98f, 0.8f, 0.6f));
            dorfIcon.pickingMode = PickingMode.Ignore;
            dorf.Add(dorfIcon);
            var dorfText = Ux.Col();
            dorfText.pickingMode = PickingMode.Ignore;
            dorfText.style.marginLeft = 12;
            dorfText.style.flexShrink = 1;
            dorfText.Add(Ux.Text(T("rdorf.reiten_karte"), "gb-h2"));
            var dorfSub = Ux.Text(T("rdorf.reiten_karte_text"), "gb-sm", "gb-mild");
            dorfSub.style.whiteSpace = WhiteSpace.Normal;
            dorfText.Add(dorfSub);
            dorf.Add(dorfText);
            body.Add(dorf);
            var wald = new Button(ZumWaldrand) { name = "ranchhof-waldrand" };
            wald.ClearClassList();
            wald.AddToClassList("gb-ranchhof-dorfcard");
            var waldIcon = Ux.IconTile("leaf", new Color(0.75f, 0.9f, 0.7f));
            waldIcon.pickingMode = PickingMode.Ignore;
            wald.Add(waldIcon);
            var waldText = Ux.Col();
            waldText.pickingMode = PickingMode.Ignore;
            waldText.style.marginLeft = 12;
            waldText.style.flexShrink = 1;
            waldText.Add(Ux.Text(T("rhof.zaehmen.karte"), "gb-h2"));
            var waldSub = Ux.Text(T(RanchZaehmen.HeuteDa(State, Today) ? "rhof.zaehmen.karte_text" : "rhof.zaehmen.karte_morgen"), "gb-sm", "gb-mild");
            waldSub.style.whiteSpace = WhiteSpace.Normal;
            waldText.Add(waldSub);
            wald.Add(waldText);
            body.Add(wald);
            var grid = Ux.Row("gb-ranchhof-games");
            foreach (var id in Spiele)
            {
                var gid = id;
                var meta = Gooby.Core.Minigames.MinigameRegistry.GetGame(id);
                var cardBtn = new Button(() => Reiten(gid)) { name = "ranchhof-spiel-" + id };
                cardBtn.ClearClassList();
                cardBtn.AddToClassList("gb-ranchhof-game");
                var cover = Ux.El("gb-ranchhof-game-cover");
                var tex = Resources.Load<Texture2D>("gooby-ui/Covers/" + id);
                if (tex != null) cover.style.backgroundImage = new StyleBackground(tex);
                else cover.Add(Ux.Icon("horseshoe"));
                cover.pickingMode = PickingMode.Ignore;
                cardBtn.Add(cover);
                var title = Ux.Text(T(meta?.TitleKey ?? "mg." + id + ".title"), "gb-md", "gb-bold");
                title.pickingMode = PickingMode.Ignore;
                cardBtn.Add(title);
                var hint = Ux.Text(T("mg." + id + ".hint"), "gb-xs", "gb-mild");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.pickingMode = PickingMode.Ignore;
                cardBtn.Add(hint);
                grid.Add(cardBtn);
            }
            body.Add(grid);
            Sfx.PlayUi("soft_open");
        }

        /// <summary>To the forest edge, where today's wild horse grazes (taming, one a day).</summary>
        private void ZumWaldrand()
        {
            if (!_gekauft) { ZeigeTor(); return; }
            if (!RanchZaehmen.HeuteDa(State, Today)) { Sfx.PlayUi("soft_error"); Sag(T("rhof.zaehmen.karte_morgen")); return; }
            if (DorfHaendler.StallVollFuer(State)) { Sfx.PlayUi("soft_error"); Sag(T("rhof.zaehmen.stall_voll")); return; }
            CloseOverlay();
            CloseTray();
            _host.Ui.Push(new RanchZaehmScreen(_host, Today));
        }

        /// <summary>Into the build mode (ranch_bau_mode.gd): the farm's grid with its facilities, decoration, paths and fences.</summary>
        private void Bauen()
        {
            if (!_gekauft) { ZeigeTor(); return; }
            CloseOverlay();
            CloseTray();
            Sfx.PlayUi("soft_open");
            _host.Ui.Push(new RanchBauScreen(_host));
        }

        /// <summary>Out into the open region on the selected horse (ranch_region_scene.gd), starting at the farm.</summary>
        private void Ausreiten()
        {
            if (!_gekauft || _sel == null) { ZeigeTor(); return; }
            if (!ReitbarSel()) return;
            CloseOverlay();
            CloseTray();
            Sfx.Play("ranch_wiehern_a", 0f, 0.7f);
            Sfx.Play("ranch_sattel", 0f, 0.8f);
            _host.Ui.Push(new RanchWeltScreen(_host, _sel));
        }

        /// <summary>Off to Hufingen on the selected horse (ranch_welt.gd: the village is ridden to, never a menu teleport).</summary>
        private void NachHufingen()
        {
            if (!_gekauft || _sel == null) { ZeigeTor(); return; }
            if (!ReitbarSel()) return;
            CloseOverlay();
            CloseTray();
            Sfx.Play("ranch_wiehern_a", 0f, 0.7f);
            Sfx.Play("ranch_sattel", 0f, 0.8f);
            _host.Ui.Push(new RanchDorfScreen(_host, _sel));
        }

        private void Reiten(string gameId)
        {
            if (_gekauft && !ReitbarSel()) return;
            var screen = MinigameHostScreen.For(_host, gameId);
            if (screen == null) return;
            CloseOverlay();
            Sfx.Play("ranch_wiehern_b", 0f, 0.7f);
            Sfx.Play("ranch_sattel", 0f, 0.8f);
            _host.Ui.Push(screen);
        }

        // ── per frame ──

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            _stage?.Tick(dt, Reduced);
            _stage?.TickLeute(RanchOffline.StundeLokal(Now, _host.Store.Clock.UtcOffsetMinutes(Now)));
            _wetter?.Tick(dt);
            PlaceWieseTag();
            PlaceMarken();
            _refreshIn -= dt;
            if (_refreshIn <= 0f)
            {
                _refreshIn = 5f;
                if (_overlay == null) RefreshAll();
                EreignisPruefen();
                RanchQuestUi.Tick(_host);
                RefreshQuests();
            }
            _idleIn -= dt;
            if (_idleIn <= 0f && _gekauft && _overlay == null)
            {
                _idleIn = UnityEngine.Random.Range(9f, 16f);
                var p = _sel != null ? RanchHof.Pferd(State, _sel) : null;
                if (p != null)
                {
                    var band = RanchCare.LauneBand(RanchCare.Laune(J.Dict(p["werte"]), J.NumOr(p["bindung"], 0.0)));
                    if (band == "strahlend" || band == "froh") { _stage?.HorseReact("kopfschuetteln"); Sfx.Play("ranch_schnauben_b", 0f, 0.45f); }
                    else if (band == "muffig" || band == "elend") { _stage?.HorseReact("fressen-loop", 1.4f); Sfx.Play("ranch_schnauben_a", -2f, 0.45f); }
                }
            }
        }

        public override void OnShown()
        {
            if (Root == null) return;
            _stage?.SetVisible(true);
            _wetter?.SetActive(true);
            _gekauft = RanchHof.IstGekauft(State);
            if (!new HashSet<string>(Pferde().Select(p => p.id)).SetEquals(_horseBtns.Keys)) BuildHorses();
            TageNachReiten();
            RefreshAll();
            Geburten();
            RanchEreignis();
            HofBesucht();
            RefreshQuests();
        }

        private void Quit()
        {
            Sfx.PlayUi("soft_back");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this))
            {
                _stage?.SetVisible(false);
                _wetter?.SetActive(false);
                return;
            }
            _wetter?.Destroy();
            _wetter = null;
            _stage?.Destroy();
            _stage = null;
        }

        // ── QA ──

        public JObject QaState()
        {
            var horses = new JObject();
            if (_stage != null)
                foreach (var (id, pos, walking, sleeping) in _stage.HorseStates())
                {
                    var view = _stage.HorseView(id);
                    var gear = new JObject();
                    if (view != null)
                        foreach (var kv in view.Gear) gear[kv.Key] = kv.Value;
                    horses[id] = new JObject
                    {
                        ["x"] = Math.Round(pos.x, 2), ["z"] = Math.Round(pos.z, 2), ["walking"] = walking, ["sleeping"] = sleeping,
                        ["gear"] = gear, ["flausch"] = view != null && view.Flausch,
                    };
                }
            var leute = new JObject();
            if (_stage != null)
                foreach (var (id, da, laeuft, pos) in _stage.LeuteStand())
                    leute[id] = new JObject { ["da"] = da, ["laeuft"] = laeuft, ["x"] = Math.Round(pos.x, 1), ["z"] = Math.Round(pos.z, 1) };
            return new JObject
            {
                ["leute"] = leute,
                ["sel"] = _sel ?? "",
                ["stageSel"] = _stage?.Selected ?? "",
                ["night"] = _night,
                ["gekauft"] = _gekauft,
                ["overlay"] = _overlay?.name ?? "",
                ["tray"] = TrayOpen,
                ["horses"] = horses,
                ["wetter"] = _wetter?.QaState(),
            };
        }

        /// <summary>
        /// "ranch kaufen | pferd &lt;id&gt; | pflege &lt;aktion&gt; | maehen | pfluecken n | heu n | ausbau id | gear slot farbe |
        /// hof | reiten | tor | zu | tap &lt;ziel&gt; [index] | wetter &lt;typ|regenbogen|plan&gt;".
        /// </summary>
        public void Qa(string[] parts)
        {
            var args = string.Join(" ", parts.Skip(1)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = args.Length > 0 ? args[0] : "";
            var a1 = args.Length > 1 ? args[1] : "";
            switch (cmd)
            {
                case "kaufen": Kaufen(); break;
                case "tor": ZeigeTor(); break;
                case "pferd": Waehle(RanchHof.PferdIds(State).FirstOrDefault(id => id.EndsWith(a1)) ?? a1); break;
                case "pflege": Pflegen(a1); break;
                case "maehen": Maehen(); break;
                case "pfluecken": Pfluecken(a1.Length > 0 ? int.Parse(a1) : 0); break;
                case "heu": HeuKaufen(a1.Length > 0 ? int.Parse(a1) : 1); break;
                case "ausbau": Ausbauen(a1); break;
                case "gear": if (args.Length > 2) Gear(a1, args[2]); break;
                case "hof":
                    ZeigeHof();
                    // "ranch hof unten": scrolled to the gear, looks and sets
                    if (a1 == "unten") _sheetScroll?.schedule.Execute(() => _sheetScroll.scrollOffset = new Vector2(0f, 100000f)).StartingIn(300);
                    break;
                case "bauen": Bauen(); break;
                case "reiten": ZeigeReiten(); break;
                case "dorf": NachHufingen(); break;
                case "ausreiten": Ausreiten(); break;
                case "futter": ToggleTray(); break;
                case "tage": ZeigeTage(); break;
                case "event": QaEreignis(a1); break;
                case "waldrand": ZumWaldrand(); break;
                case "wetter": _wetter?.Erzwingen(a1); break;
                case "quests": ZeigeQuests(); break;
                case "person": ZeigePerson(a1); break;
                case "quest":
                    // "ranch quest annehmen haupt_01 | abgeben haupt_01 | melde sprich_mit rosi [n]"
                    var a2 = args.Length > 2 ? args[2] : "";
                    if (a1 == "annehmen") RanchQuestUi.Annehmen(_host, a2);
                    else if (a1 == "abgeben") RanchQuestUi.Abgeben(_host, a2, new Vector2(800f, 300f), _coinPill);
                    else if (a1 == "melde" && args.Length > 3) RanchQuestUi.Melde(_host, a2, args[3], args.Length > 4 ? int.Parse(args[4]) : 1);
                    RefreshQuests();
                    break;
                case "zucht": case "fuersorge": case "geburt": case "stammbaum": QaZucht(cmd, a1); break;
                case "belohnung":
                    // "ranch belohnung alle | sattel_kartoffel": unlocked as if the quest was handed in, delivered at once
                    var ids = a1 == "alle" ? RanchBelohnungen.Alle.Select(d => d.Id).ToList() : new List<string> { a1 };
                    _host.Store.Update(s =>
                    {
                        var frei = (JArray)RanchQuests.Slice(s)["freigeschaltet"];
                        foreach (var id in ids)
                            if (!frei.Any(t => J.Str(t) == id)) frei.Add(id);
                        RanchBelohnungen.Liefern(s);
                    }, GameHost.TicksMs);
                    _host.Store.NotifySliceChanged(RanchHof.SliceId);
                    BuildHorses();
                    RefreshAll();
                    break;
                case "look": Look(a1); break;
                case "set":
                    // "ranch set speichern 0 | anlegen 1"
                    var platz = args.Length > 2 && int.TryParse(args[2], out var p) ? p : 0;
                    if (a1 == "speichern") SetSpeichern(platz);
                    else SetAnlegen(platz);
                    break;
                case "sattelkammer":
                    // "ranch sattelkammer 2": the saddle room's level without a build (QA)
                    var stufe = int.TryParse(a1, out var st) ? st : 1;
                    _host.Store.Update(s => ((JObject)RanchBauState.Im(s)["anlagen"])["sattelkammer"] = new JObject { ["stufe"] = stufe }, GameHost.TicksMs);
                    _host.Store.NotifySliceChanged(RanchHof.SliceId);
                    break;
                case "abholen": Abholen(a1.Length > 0 ? a1 : RanchTage.Heute(State, Today).FirstOrDefault(t => t.Fertig && !t.Abgeholt)?.Aufgabe.Id ?? ""); break;
                case "zu": CloseOverlay(); CloseTray(); break;
                case "tap":
                    if (Enum.TryParse<RanchHofStage.Ziel>(a1, true, out var ziel)) Tippe(ziel, args.Length > 2 ? args[2] : "", args.Length > 2 && int.TryParse(args[2], out var n) ? n : 0);
                    break;
                default: Debug.LogWarning("[qa] ranch: unknown " + cmd); break;
            }
            Debug.Log("[qa] ranch " + string.Join(" ", args));
        }
    }
}
