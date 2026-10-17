using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Audio;
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
    /// Out riding in the open ranch region (port of ranch_region_scene.gd's HUD and flow): the 3D world behind
    /// (<see cref="RanchWelt3D"/>), the zone's name and the time · weather on top, coins and the places found on the right,
    /// a floating stick on the left half (forward rides on, sideways turns), the gallop switch and the map on the right.
    /// Riding into a zone for the first time saves it with a toast; reaching one of the sixteen places pays its coins
    /// (they fly to the pill) and stops its glitter. Near the village, the arena, the wild herd or the farm a button takes
    /// you in. The map's "Dahin reiten" hands the reins to an autopilot along the paths.
    /// </summary>
    public sealed partial class RanchWeltScreen : UiScreen
    {
        public const float StickRadius = 70f;

        private readonly GameHost _host;
        private readonly string _pferdId, _startZone;
        private readonly Juice _juice = new Juice();
        private RanchWelt3D _welt;
        private RanchWetterView _wetter;
        private RanchKarteView _karte;
        private VisualElement _field, _sattel, _sattelFill, _stickZone, _stickBase, _stickKnob, _head, _hint, _karteHost;
        private Label _zoneLabel, _subLabel, _coinsLabel, _fundeLabel, _gaitLabel, _aktionLabel;
        private VisualElement _coinPill, _fundePill;
        private Button _galoppBtn, _aktionBtn;
        private float _checkIn = 0.5f, _hintIn = 7f, _questTick = 5f;
        private int _stickPointer = -1;
        private Vector2 _stickStart;
        private string _aktion = "";
        private bool _night, _weltKamera = true, _gezogen, _sattelOffen;
        private List<V2d> _bachPunkte;

        public RanchWeltScreen(GameHost host, string pferdId, string startZone = "hof")
        {
            _host = host;
            _pferdId = pferdId;
            _startZone = startZone;
            Id = "ranchwelt";
            Opaque = true;
        }

        public override string SoundScene => _wetter?.Scene ?? "ranch/welt";

        private JObject State => _host.Store.State;
        private long Now => _host.Store.Clock.NowMs();
        private string Today => _host.Store.Clock.LocalDay(Now);
        private string T(string key, params (string, object)[] args) => _host.Strings.T(key, args);
        public RanchWelt3D Welt => _welt;
        public bool KarteOffen => _karteHost != null && _karteHost.style.display == DisplayStyle.Flex;

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-ranchwelt");
            _night = RanchOffline.IstNacht(RanchOffline.StundeLokal(Now, _host.Store.Clock.UtcOffsetMinutes(Now)));
            _field = new VisualElement { name = "ranchwelt-field" };
            Ux.Abs(_field, 0, 0, 0, 0);
            _field.style.backgroundColor = _night ? new Color(0.2f, 0.25f, 0.42f) : new Color(0.71f, 0.85f, 0.95f);
            root.Add(_field);
            if (MgStage3D.Supported)
            {
                var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(State["cosmetics"]));
                var pferd = RanchHof.Pferd(State, _pferdId);
                var (fell, maehne) = RanchPferdLook.Fell(pferd, "palomino");
                _welt = new RanchWelt3D(_field, outfit, _night, _startZone, fell, maehne);
                RanchPferdLook.Kleide(_welt.Pferd, State, _pferdId);
                if (RanchHof.IstGekauft(State)) _welt.Hofbau(RanchBauState.Lese(State), RanchBelohnungen.Wandschleifen(State));
                _welt.ZoneGewechselt += ZoneNeu;
                _welt.Angekommen += () => { Sfx.PlayUi("soft_confirm"); _host.Ui.Toast(T("rwelt.ui.angekommen", ("zone", T("rwelt.zone." + (_welt.Zone.Length > 0 ? _welt.Zone : "frei")))), "map"); };
                WeltKamera(false);
            }
            _bachPunkte = RanchKarte.BachPunkte;

            _stickZone = new VisualElement { name = "ranchwelt-stick" };
            _stickZone.AddToClassList("gb-ranchwelt-stickzone");
            _stickZone.RegisterCallback<PointerDownEvent>(StickDown);
            _stickZone.RegisterCallback<PointerMoveEvent>(StickMove);
            _stickZone.RegisterCallback<PointerUpEvent>(StickUp);
            _stickZone.RegisterCallback<PointerCancelEvent>(_ => StickLoslassen());
            root.Add(_stickZone);
            _stickBase = Ux.El("gb-ranchwelt-stickbase");
            _stickBase.pickingMode = PickingMode.Ignore;
            _stickKnob = Ux.El("gb-ranchwelt-stickknob");
            _stickKnob.pickingMode = PickingMode.Ignore;
            _stickBase.Add(_stickKnob);
            _stickZone.Add(_stickBase);

            var back = Ux.IconBtn("back_white", Quit, "gb-place-back");
            back.name = "ranchwelt-quit";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            _head = Ux.Row("gb-card", "gb-ranchhof-head");
            _head.name = "ranchwelt-head";
            Ux.Abs(_head, 84, 12);
            _head.Add(Ux.IconTile("map", new Color(0.8f, 0.92f, 0.7f)));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            _zoneLabel = Ux.Text(T("rwelt.titel"), "gb-place-title");
            _zoneLabel.name = "ranchwelt-zone";
            hcol.Add(_zoneLabel);
            _subLabel = Ux.Text("", "gb-sm", "gb-mild");
            hcol.Add(_subLabel);
            _head.Add(hcol);
            _wetter = new RanchWetterView(_host, _startZone, "welt", null, _field);
            _wetter.PlanAn = plan => _welt?.Wetter(plan);
            _wetter.WasserAbstand = BachAbstand;
            _wetter.Refresh();
            _head.Add(_wetter.Chip);
            root.Add(_head);

            var pills = Ux.Row("gb-ranchhof-pills");
            Ux.Abs(pills, null, 14, 16);
            _fundePill = Ux.Pill("sparkle", "0/16", out _fundeLabel);
            _fundePill.name = "ranchwelt-funde";
            _fundePill.RegisterCallback<PointerDownEvent>(_ => ZeigeKarte());
            pills.Add(_fundePill);
            _coinPill = Ux.Pill("coin", "0", out _coinsLabel);
            _coinPill.name = "ranchwelt-coins";
            pills.Add(_coinPill);
            root.Add(pills);

            var rechts = Ux.Col("gb-ranchwelt-rechts");
            rechts.name = "ranchwelt-rechts";
            var karteBtn = RundKnopf("ranchwelt-karte", "map", T("rkarte.knopf"), ZeigeKarte, false);
            rechts.Add(karteBtn);
            _galoppBtn = RundKnopf("ranchwelt-galopp", "horseshoe", T("rwelt.ui.galopp"), ToggleGalopp, true);
            rechts.Add(_galoppBtn);
            root.Add(rechts);
            BaueQuests(root, rechts);
            _gaitLabel = Ux.Text("", "gb-ranchwelt-gait");
            _gaitLabel.name = "ranchwelt-gait";
            _gaitLabel.pickingMode = PickingMode.Ignore;
            root.Add(_gaitLabel);

            _aktionBtn = new Button(Aktion) { name = "ranchwelt-aktion" };
            _aktionBtn.ClearClassList();
            _aktionBtn.AddToClassList("gb-btn");
            _aktionBtn.AddToClassList("gb-btn-primary");
            _aktionBtn.AddToClassList("gb-ranchwelt-aktion");
            _aktionLabel = Ux.Text("", "gb-ranchwelt-aktion-text");
            _aktionLabel.pickingMode = PickingMode.Ignore;
            _aktionBtn.Add(_aktionLabel);
            _aktionBtn.style.display = DisplayStyle.None;
            root.Add(_aktionBtn);

            _hint = Ux.Row("gb-card", "gb-ranchwelt-hint");
            _hint.name = "ranchwelt-hint";
            _hint.pickingMode = PickingMode.Ignore;
            _hint.Add(Ux.Icon("joy_tri"));
            var hintText = Ux.Text(T("rwelt.ui.tipp"), "gb-sm");
            hintText.style.marginLeft = 8;
            _hint.Add(hintText);
            root.Add(_hint);

            _sattel = Ux.Col("gb-card", "gb-ranchwelt-sattel");
            _sattel.name = "ranchwelt-sattel";
            _sattel.Add(Ux.IconTile("horseshoe", new Color(0.98f, 0.86f, 0.62f)));
            var st = Ux.Text(T("rwelt.ui.satteln"), "gb-h2");
            st.style.marginTop = 8;
            _sattel.Add(st);
            var bar = Ux.Bar(0.05f, out _sattelFill);
            bar.style.width = 260;
            bar.style.marginTop = 10;
            _sattel.Add(bar);
            root.Add(_sattel);
            _sattelOffen = _welt != null;
            _sattel.style.display = _sattelOffen ? DisplayStyle.Flex : DisplayStyle.None;

            _karteHost = new VisualElement { name = "ranchwelt-kartehost" };
            Ux.Abs(_karteHost, 0, 0, 0, 0);
            _karteHost.AddToClassList("gb-rkarte-host");
            _karteHost.style.display = DisplayStyle.None;
            root.Add(_karteHost);
            root.Add(_juice.Layer);
            RefreshHud();
            Debug.Log("[ranchwelt] auf: " + _startZone + (_night ? " (nacht)" : ""));
        }

        private Button RundKnopf(string name, string icon, string text, Action click, bool gross)
        {
            var b = new Button(click) { name = name };
            b.ClearClassList();
            b.AddToClassList("gb-ranchhof-action");
            b.AddToClassList("gb-ranchwelt-knopf");
            if (gross) b.AddToClassList("gb-ranchwelt-knopf--gross");
            var disc = Ux.El("gb-ranchhof-action-disc");
            disc.pickingMode = PickingMode.Ignore;
            var ic = Ux.Icon(icon);
            ic.pickingMode = PickingMode.Ignore;
            disc.Add(ic);
            b.Add(disc);
            var l = Ux.Text(text, "gb-ranchhof-action-label");
            l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            return b;
        }

        private double BachAbstand()
        {
            if (_welt == null || _bachPunkte == null) return -1.0;
            var best = double.MaxValue;
            for (var i = 0; i + 1 < _bachPunkte.Count; i++)
                best = Math.Min(best, RanchGelaende.AbstandZuStrecke(_welt.X, _welt.Z, _bachPunkte[i], _bachPunkte[i + 1]));
            return best;
        }

        private void WeltKamera(bool on)
        {
            var cam = _host.World != null ? _host.World.Camera : null;
            if (cam == null) return;
            if (!on) _weltKamera = cam.enabled;
            cam.enabled = on && _weltKamera;
        }

        // ── stick and buttons ──

        private void StickDown(PointerDownEvent e)
        {
            if (_stickPointer >= 0 || KarteOffen) return;
            _stickPointer = e.pointerId;
            _stickStart = e.localPosition;
            _stickZone.CapturePointer(e.pointerId);
            _stickBase.style.left = _stickStart.x - StickRadius;
            _stickBase.style.top = _stickStart.y - StickRadius;
            _stickBase.AddToClassList("gb-ranchwelt-stickbase--an");
            SetStick(Vector2.zero);
        }

        private void StickMove(PointerMoveEvent e)
        {
            if (e.pointerId != _stickPointer) return;
            var d = (Vector2)e.localPosition - _stickStart;
            if (d.magnitude > StickRadius) d = d.normalized * StickRadius;
            SetStick(d);
        }

        private void StickUp(PointerUpEvent e)
        {
            if (e.pointerId != _stickPointer) return;
            StickLoslassen();
        }

        private void StickLoslassen()
        {
            if (_stickPointer >= 0 && _stickZone.HasPointerCapture(_stickPointer)) _stickZone.ReleasePointer(_stickPointer);
            _stickPointer = -1;
            _stickBase.RemoveFromClassList("gb-ranchwelt-stickbase--an");
            SetStick(Vector2.zero);
        }

        private void SetStick(Vector2 d)
        {
            _stickKnob.style.translate = new Translate(d.x, d.y);
            if (_welt == null) return;
            // screen y down: dragging up rides on
            _welt.Stick = new Vector2(d.x / StickRadius, -d.y / StickRadius);
            if (d.sqrMagnitude > 400f) Gezogen();
        }

        private void Gezogen()
        {
            if (_gezogen) return;
            _gezogen = true;
            _hint.AddToClassList("gb-ranchwelt-hint--weg");
        }

        private void ToggleGalopp()
        {
            if (_welt == null) return;
            _welt.Galopp = !_welt.Galopp;
            _galoppBtn.EnableInClassList("gb-ranchhof-action--an", _welt.Galopp);
            Sfx.PlayUi(_welt.Galopp ? "soft_toggle" : "soft_tick");
            if (_welt.Galopp) Sfx.Play("ranch_schnauben_b", 2f, 0.5f);
        }

        // ── zones, places, spots ──

        private void ZoneNeu(string zone)
        {
            if (_wetter != null) _wetter.Zone = zone;
            var neu = false;
            _host.Store.Update(s => neu = RanchWeltState.EntdeckeZone(s, zone), GameHost.TicksMs);
            Debug.Log("[ranchwelt] zone " + zone + (neu ? " (neu)" : ""));
            RefreshHud();
            _juice.ScalePop(_head, 1.06f, 0.18f);
            if (!neu) return;
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            Sfx.PlayUi("soft_sticker");
            Haptics.Tap();
            _host.Ui.Toast(T("rwelt.entdeckt", ("zone", T("rwelt.zone." + zone))), "map", 3.6f);
        }

        private void PruefeFunde()
        {
            if (_welt == null || !_welt.Bereit) return;
            var ort = RanchEntdeckungen.FundBei(State, _welt.X, _welt.Z);
            if (ort == null) return;
            (bool neu, int muenzen, string nameKey) res = (false, 0, "");
            _host.Store.Update(s => res = RanchEntdeckungen.Entdecke(s, ort.Id), GameHost.TicksMs);
            if (!res.neu) return;
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            _welt.Gefunden(ort.Id, true);
            Sfx.PlayUi("soft_win");
            Haptics.Success();
            var w = _field.layout.width;
            var h = _field.layout.height;
            var from = !float.IsNaN(w) && w > 0f ? _welt.ToField(ort.Pos.X, RanchGelaende.Hoehe(ort.Pos.X, ort.Pos.Z) + 1.5, ort.Pos.Z, w, h) : new Vector2(800f, 300f);
            if (from.x < -1000f) from = new Vector2(w * 0.5f, h * 0.5f);
            _host.Ui.FloatText(from + new Vector2(0f, -40f), "+" + res.muenzen, new Color(0.98f, 0.8f, 0.3f), 38f);
            _host.Ui.FlyIcons(from, _coinPill, "coin", Math.Min(10, Math.Max(3, res.muenzen / 6)), _ => Sfx.PlayUi("soft_collect"));
            _host.Ui.Toast(T("rwelt.fund_entdeckt", ("name", T(res.nameKey)), ("muenzen", res.muenzen)), "sparkle", 4f);
            Debug.Log("[ranchwelt] fund " + ort.Id + " +" + res.muenzen);
            Root.schedule.Execute(RefreshHud).StartingIn(900);
        }

        /// <summary>The way in near a spot: the village, the arena, the wild herd, the farm.</summary>
        private void PruefeAktion()
        {
            var neu = "";
            if (_welt != null && _welt.Bereit)
            {
                var x = _welt.X;
                var z = _welt.Z;
                if (_person.Length > 0) neu = "person:" + _person;
                else if (RanchKarte.ZoneVon("hufingen")?.Punkt("strasse_ende").Abstand(x, z) < 70.0 || new V2d(500, 522).Abstand(x, z) < 45.0) neu = "hufingen";
                else if (InArena(x, z)) neu = "turnier";
                else if (RanchKarte.ZoneVon("weidetal")?.Punkt("wildpferde_weide").Abstand(x, z) < 45.0) neu = "wildpferde";
                else if (new V2d(0, 110).Abstand(x, z) < 75.0) neu = "hof";
            }
            if (neu == _aktion) return;
            _aktion = neu;
            _aktionBtn.style.display = neu.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (neu.Length > 0)
            {
                _aktionLabel.text = neu.StartsWith("person:") ? T("rquest.ui.sprechen", ("name", RanchQuestUi.Name(_host, neu.Substring(7)).Split(' ')[0])) : T("rwelt.aktion." + neu);
                _juice.ScalePop(_aktionBtn, 1.1f, 0.2f);
                Sfx.PlayUi("soft_open");
            }
        }

        private static bool InArena(double x, double z)
        {
            var a = RanchKarte.ZoneVon("turnierplatz")?.Daten?["arena"] as JArray;
            return a != null && a.Count >= 4 && Math.Abs(x - (double)a[0]) < (double)a[2] / 2 + 6 && Math.Abs(z - (double)a[1]) < (double)a[3] / 2 + 6;
        }

        private void Aktion()
        {
            if (_aktion.StartsWith("person:")) { ZeigePerson(_aktion.Substring(7)); return; }
            switch (_aktion)
            {
                case "hufingen":
                    Sfx.Play("ranch_wiehern_a", 0f, 0.7f);
                    _host.Ui.Push(new RanchDorfScreen(_host, _pferdId));
                    break;
                case "turnier":
                    var screen = MinigameHostScreen.For(_host, "ranchTurnier");
                    if (screen != null) _host.Ui.Push(screen);
                    break;
                case "wildpferde":
                    if (!RanchZaehmen.HeuteDa(State, Today)) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T("rhof.zaehmen.karte_morgen"), "leaf"); return; }
                    if (DorfHaendler.StallVollFuer(State)) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T("rhof.zaehmen.stall_voll"), "leaf"); return; }
                    _host.Ui.Push(new RanchZaehmScreen(_host, Today));
                    break;
                case "hof":
                    Quit();
                    break;
            }
        }

        // ── the map ──

        private void ZeigeKarte()
        {
            if (_welt == null) return;
            Sfx.PlayUi("soft_open");
            if (_karte == null)
            {
                _karte = new RanchKarteView(_host, () => (_welt.X, _welt.Z, _welt.Heading), Dahin, SchliesseKarte);
                _karteHost.Add(_karte);
            }
            else _karte.Aufbauen();
            _karteHost.style.display = DisplayStyle.Flex;
            StickLoslassen();
        }

        private void SchliesseKarte()
        {
            if (_karteHost != null) _karteHost.style.display = DisplayStyle.None;
            RefreshHud();
        }

        /// <summary>"Dahin reiten": the paths from zone to zone (BFS over the map's paths), then the trail to a place.</summary>
        private void Dahin(string art, string id)
        {
            if (_welt == null) return;
            var zielZone = art == "zone" ? id : RanchKarte.ZoneBei(RanchEntdeckungen.Fundort(id)?.Pos.X ?? 0, RanchEntdeckungen.Fundort(id)?.Pos.Z ?? 0);
            if (zielZone.Length == 0 && art == "fund") zielZone = RanchEntdeckerKarte.HinweisZone(RanchEntdeckungen.Fundort(id).Pos);
            var route = Route(_welt.Zone.Length > 0 ? _welt.Zone : RanchEntdeckerKarte.HinweisZone(new V2d(_welt.X, _welt.Z)), zielZone);
            if (art == "zone") route.Add(RanchKarte.ZoneVon(id).Spawn);
            else
            {
                var ort = RanchEntdeckungen.Fundort(id);
                var pfad = RanchEntdeckungen.AllePfade.FirstOrDefault(p => p.Id == "pfad_" + id);
                if (pfad != null) route.AddRange(pfad.Punkte);
                route.Add(ort.Pos);
            }
            _welt.Reite(route);
            SchliesseKarte();
            _host.Ui.Toast(T("rwelt.ui.unterwegs", ("ziel", art == "zone" ? T("rwelt.zone." + id) : T(RanchEntdeckungen.Fundort(id).NameKey))), "horseshoe");
            Debug.Log("[ranchwelt] dahin " + art + " " + id + " über " + route.Count + " Punkte");
        }

        private static List<V2d> Route(string von, string nach)
        {
            var vorher = new Dictionary<string, string> { [von] = null };
            var offen = new Queue<string>(new[] { von });
            while (offen.Count > 0 && !vorher.ContainsKey(nach))
            {
                var z = offen.Dequeue();
                foreach (var n in RanchKarte.Nachbarn(z))
                    if (!vorher.ContainsKey(n)) { vorher[n] = z; offen.Enqueue(n); }
            }
            var kette = new List<string>();
            for (var z = vorher.ContainsKey(nach) ? nach : null; z != null; z = vorher[z]) kette.Insert(0, z);
            var punkte = new List<V2d>();
            for (var i = 0; i + 1 < kette.Count; i++) punkte.AddRange(RanchKarte.Wegpunkte(kette[i], kette[i + 1]));
            return punkte;
        }

        // ── per frame ──

        private void RefreshHud()
        {
            if (Root == null) return;
            var zone = _welt != null && _welt.Zone.Length > 0 ? _welt.Zone : _startZone;
            _zoneLabel.text = T("rwelt.zone." + zone);
            var now = Now;
            var local = DateTimeOffset.FromUnixTimeMilliseconds(now).ToOffset(TimeSpan.FromMinutes(_host.Store.Clock.UtcOffsetMinutes(now)));
            _subLabel.text = local.ToString("HH:mm") + " · " + T("rwelt.titel");
            _coinsLabel.text = Ux.FormatNumber(J.Int(J.Path(State, "economy", "coins")));
            var f = RanchEntdeckerKarte.Fortschritt(State);
            _fundeLabel.text = f.funde + "/" + f.fundeGesamt;
        }

        public override void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _juice.Tick(dt);
            _welt?.Tick(dt);
            _wetter?.Tick(dt);
            SammleTraining(dt);
            if (_welt != null && _sattelOffen)
            {
                _sattelFill.style.width = Length.Percent(100f * _welt.BauFortschritt);
                if (_welt.Bereit)
                {
                    _sattelOffen = false;
                    _sattel.style.display = DisplayStyle.None;
                    Sfx.Play("ranch_wiehern_b", 0f, 0.6f);
                }
            }
            if (_karte != null && KarteOffen) _karte.Tick(dt);
            if (_welt != null)
            {
                var gang = _welt.Gangart;
                _gaitLabel.text = gang == "stand" ? "" : T("rpferd.gang." + gang);
                if (_welt.Tempo > 2f) Gezogen();
            }
            if (!_gezogen && (_hintIn -= dt) <= 0f) Gezogen();
            PruefeQuests(dt);
            PlaceMarken();
            if ((_questTick -= dt) <= 0f)
            {
                _questTick = 5f;
                RanchQuestUi.Tick(_host);
                RefreshQuests();
            }
            if ((_checkIn -= dt) > 0f) return;
            _checkIn = 0.5f;
            _welt?.TickLeute(Stunde);
            PruefeFunde();
            PruefeAktion();
        }

        public override void OnShown()
        {
            if (Root == null) return;
            _welt?.SetVisible(true);
            _wetter?.SetActive(true);
            WeltKamera(false);
            RefreshHud();
        }

        private void Quit()
        {
            Sfx.PlayUi("soft_back");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        public override void OnHidden()
        {
            StickLoslassen();
            BucheTraining(true);
            if (_host.Ui.IsMounted(this))
            {
                _welt?.SetVisible(false);
                _wetter?.SetActive(false);
                WeltKamera(true);
                return;
            }
            _wetter?.Destroy();
            _wetter = null;
            _welt?.Destroy();
            _welt = null;
            WeltKamera(true);
        }

        // ── QA ──

        public JObject QaState() => new JObject
        {
            ["bereit"] = _welt != null && _welt.Bereit,
            ["x"] = _welt != null ? Math.Round(_welt.X, 1) : 0.0,
            ["z"] = _welt != null ? Math.Round(_welt.Z, 1) : 0.0,
            ["zone"] = _welt?.Zone ?? "",
            ["gangart"] = _welt?.Gangart ?? "",
            ["tempo"] = _welt != null ? Math.Round(_welt.Tempo, 1) : 0.0,
            ["training"] = new JObject { ["unterwegsS"] = Math.Round(_tele.UnterwegsS, 1), ["galoppM"] = Math.Round(_tele.GaloppM, 1), ["schrittS"] = Math.Round(_tele.SchrittLauneS, 1) },
            ["leute"] = new JObject(RanchWelt3D.RegionLeute.Select(id => new JProperty(id, _welt?.LeuteOrt(id) ?? ""))),
            ["untergrund"] = _welt?.Untergrund ?? "",
            ["autopilot"] = _welt != null && _welt.Autopilot,
            ["aktion"] = _aktion,
            ["zonen"] = new JArray(RanchWeltState.EntdeckteZonen(State)),
            ["funde"] = new JArray(RanchEntdeckungen.Gefunden(State)),
            ["aktiv"] = _welt?.AktiveZonen() ?? 0,
            ["karte"] = KarteOffen,
            ["person"] = _person,
            ["sammel"] = _welt?.SammelZahl ?? 0,
            ["aufgesammelt"] = new JArray(RanchSammeln.Aufgesammelt(State, Today)),
            ["quests"] = new JArray(RanchQuests.MitStatus(State, RanchQuests.Aktiv, RanchQuests.Wartend, RanchQuests.Erfuellbar).Select(q => q.Id + ":" + RanchQuests.Status(State, q))),
            ["wetter"] = _wetter?.QaState(),
        };

        /// <summary>"welt zone &lt;id&gt; | fund &lt;id&gt; | reite &lt;zone&gt; | dahin &lt;fund&gt; | stick x y | galopp | karte | tippe &lt;id&gt; | aktion | wetter &lt;typ&gt; | zu".</summary>
        public void Qa(string[] parts)
        {
            var args = string.Join(" ", parts.Skip(1)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = args.Length > 0 ? args[0] : "";
            var a1 = args.Length > 1 ? args[1] : "";
            switch (cmd)
            {
                case "zone": _welt?.SpringeZu(a1); break;
                case "hin":
                    if (args.Length > 2) _welt?.SpringeZu(double.Parse(a1, System.Globalization.CultureInfo.InvariantCulture), double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args.Length > 3 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) * Mathf.Deg2Rad : 0f);
                    break;
                case "fund":
                    var ort = RanchEntdeckungen.Fundort(a1);
                    if (ort != null) _welt?.SpringeZu(ort.Blick.X, ort.Blick.Z, Mathf.Atan2((float)(ort.Pos.X - ort.Blick.X), (float)-(ort.Pos.Z - ort.Blick.Z)));
                    break;
                case "reite": Dahin("zone", a1); break;
                case "dahin": Dahin("fund", a1); break;
                case "tiere":
                    if (_welt == null) break;
                    Debug.Log("[qa] welt tiere " + string.Join(" ", _welt.WildZaehlung().OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
                    if (a1.Length > 0) foreach (var line in _welt.WildDetails(a1 == "alle" ? "" : a1)) Debug.Log("[qa] welt tier " + line);
                    break;
                case "tierblick":
                    // "welt tierblick ente [abstand]": the rider in front of a wild animal (close-ups)
                    var blick = _welt != null && _welt.BlickAufTier(a1, args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 14.0);
                    Debug.Log("[qa] welt tierblick " + a1 + " " + blick);
                    break;
                case "stick":
                    if (_welt != null && args.Length > 2) _welt.Stick = new Vector2(float.Parse(a1, System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "galopp": ToggleGalopp(); break;
                case "karte": ZeigeKarte(); break;
                case "tippe": _karte?.QaTippe(a1); break;
                case "aktion": Aktion(); break;
                case "wetter": _wetter?.Erzwingen(a1); break;
                case "zu": SchliesseKarte(); CloseOverlay(); break;
                case "quests": ZeigeQuests(); break;
                case "person": ZeigePerson(a1); break;
                case "sammle":
                    // ride straight to the nearest thing to collect of a kind ("welt sammle hufeisen")
                    var stelle = RanchSammeln.FuerTag(Today).Where(st => st.Item == a1 && !RanchSammeln.Aufgesammelt(State, Today).Contains(st.Id))
                        .OrderBy(st => Math.Abs(st.X - (_welt?.X ?? 0)) + Math.Abs(st.Z - (_welt?.Z ?? 0))).FirstOrDefault();
                    if (stelle != null) _welt?.SpringeZu(stelle.X, stelle.Z + 2.0, 0f);
                    break;
                case "quest":
                    var a2 = args.Length > 2 ? args[2] : "";
                    if (a1 == "annehmen") RanchQuestUi.Annehmen(_host, a2);
                    else if (a1 == "abgeben") RanchQuestUi.Abgeben(_host, a2, new Vector2(800f, 300f), _coinPill);
                    else if (a1 == "melde" && args.Length > 3) RanchQuestUi.Melde(_host, a2, args[3], args.Length > 4 ? int.Parse(args[4]) : 1);
                    RefreshQuests();
                    break;
                default: Debug.LogWarning("[qa] welt: unknown " + cmd); break;
            }
            Debug.Log("[qa] welt " + string.Join(" ", args));
        }
    }
}
