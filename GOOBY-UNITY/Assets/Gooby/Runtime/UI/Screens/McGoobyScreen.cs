using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Dlc;
using Gooby.Core.Json;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// A McGooby shift (port of schicht_scene.gd + schicht_ui_teile.gd): the kitchen in 3D behind, the order ticket on
    /// top, one station at a time at the bottom — tap "Wenden!" when the patty glows golden, tap the ingredients in
    /// ticket order onto the bun, hold the fryer basket and let go while the fries are golden, hold the cup and let go
    /// when the crown stands. Late is never lost (half points), early just goes on. After the last order the till:
    /// points, perfect moments, tips, coins, the shop's star rank (a new star is celebrated). Without the restaurant one
    /// free trial shift a day.
    /// </summary>
    public sealed class McGoobyScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly long _seed;
        private McGoobyStage _stage;
        private JObject _bal;
        private List<JObject> _menu;
        private List<McGoobySchicht.Bestellung> _orders;
        private int _orderIndex, _phaseIndex, _actionIndex, _orderPoints, _shiftPerfect, _platziert, _perfektKette;
        private bool _orderFlawless = true, _running, _holding, _done, _demo;
        private double _t;
        private string _phase = "";
        private JObject _rezept;
        private List<string> _ticket = new List<string>(), _aktionen = new List<string>();
        private readonly List<(int, bool)> _results = new List<(int, bool)>();
        private McGoobyKatalog.Timing _timing;

        private VisualElement _field, _ticketCard, _ticketList, _panel, _bar, _barFill, _barWindow, _chips, _endCard;
        private Label _orderLabel, _recipeLabel, _stepLabel, _stateLabel, _points;
        private Button _action;

        public McGoobyScreen(GameHost host, long seed = 0)
        {
            _host = host;
            _seed = seed != 0 ? seed : (long)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            Id = "mcgooby";
            Opaque = true;
        }

        public override string SoundScene => "mg/burgerBuild";

        private string T(string key, params (string, object)[] args) => _host.Strings.T("dlc_mcgooby." + key, args);

        private string Today => DateTimeOffset.FromUnixTimeMilliseconds(_host.Store.Clock.NowMs()).ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-mcgooby");
            _field = new VisualElement { name = "mcgooby-field", pickingMode = PickingMode.Ignore };
            Ux.Abs(_field, 0, 0, 0, 0);
            root.Add(_field);
            if (MgStage3D.Supported)
            {
                var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"]));
                _stage = new McGoobyStage(_field, outfit);
            }

            var back = Ux.IconBtn("back_white", Quit, "gb-place-back");
            back.name = "mcgooby-quit";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.El("gb-card", "gb-row");
            Ux.Abs(head, 84, 12);
            head.style.paddingLeft = 8; head.style.paddingTop = 6; head.style.paddingBottom = 6; head.style.paddingRight = 18;
            head.Add(Ux.IconTile("food_burger", new Color(1f, 0.86f, 0.5f)));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(T("titel"), "gb-place-title"));
            _orderLabel = Ux.Text("", "gb-sm", "gb-mild");
            hcol.Add(_orderLabel);
            head.Add(hcol);
            root.Add(head);
            var pts = Ux.El("gb-card", "gb-row", "gb-mcg-points");
            Ux.Abs(pts, null, 12, 16);
            pts.Add(Ux.Icon("star"));
            _points = Ux.Text("0", "gb-lg", "gb-bold");
            _points.name = "mcgooby-points";
            _points.style.marginLeft = 6;
            pts.Add(_points);
            root.Add(pts);

            _ticketCard = Ux.Col("gb-card", "gb-mcg-ticket");
            _ticketCard.name = "mcgooby-ticket";
            Ux.Abs(_ticketCard, null, 12);
            _ticketCard.style.left = Length.Percent(50);
            _ticketCard.style.translate = new Translate(Length.Percent(-50), 0);
            _recipeLabel = Ux.Text("", "gb-h3");
            _ticketCard.Add(_recipeLabel);
            _ticketList = Ux.Row("gb-mcg-ticket-list");
            _ticketCard.Add(_ticketList);
            root.Add(_ticketCard);

            _panel = Ux.Col("gb-card", "gb-mcg-panel");
            _panel.name = "mcgooby-panel";
            Ux.Abs(_panel, null, null, null, 18);
            _panel.style.left = Length.Percent(50);
            _panel.style.translate = new Translate(Length.Percent(-50), 0);
            _stepLabel = Ux.Text("", "gb-md", "gb-bold");
            _panel.Add(_stepLabel);
            _bar = Ux.El("gb-mcg-bar");
            _barWindow = Ux.El("gb-mcg-bar-window");
            _bar.Add(_barWindow);
            _barFill = Ux.El("gb-mcg-bar-fill");
            _bar.Add(_barFill);
            _panel.Add(_bar);
            _stateLabel = Ux.Text("", "gb-lg", "gb-bold", "gb-mcg-state");
            _panel.Add(_stateLabel);
            _chips = Ux.Row("gb-mcg-chips");
            _panel.Add(_chips);
            _action = new Button { name = "mcgooby-action" };
            _action.ClearClassList();
            _action.AddToClassList("gb-btn");
            _action.AddToClassList("gb-btn-primary");
            _action.AddToClassList("gb-mcg-action");
            _action.RegisterCallback<PointerDownEvent>(_ => Press(), TrickleDown.TrickleDown);
            _action.RegisterCallback<PointerUpEvent>(_ => Release(), TrickleDown.TrickleDown);
            _action.RegisterCallback<PointerLeaveEvent>(_ => { if (_holding) Release(); });
            _panel.Add(_action);
            root.Add(_panel);

            StartShift();
        }

        // ── the shift ──

        private void StartShift()
        {
            var state = _host.Store.State;
            _demo = !McGoobyState.IstGekauft(state);
            if (!McGoobyState.SchichtErlaubt(state, Today))
            {
                ShowLocked();
                return;
            }
            if (_demo) _host.Store.Update(s => McGoobyState.DemoVerbuchen(s, Today), GameHost.TicksMs);
            _bal = McGoobyKatalog.Balance();
            _menu = McGoobyKatalog.Rezepte();
            _orders = McGoobySchicht.BestellFolge(_seed, _menu, _bal);
            _orderIndex = -1;
            _results.Clear();
            _shiftPerfect = 0;
            _running = true;
            Debug.Log("[mcgooby] schicht seed " + _seed + " (" + _orders.Count + " bestellungen" + (_demo ? ", probe" : "") + ")");
            NextOrder();
        }

        private void NextOrder()
        {
            _orderIndex++;
            if (_orderIndex >= _orders.Count) { EndShift(); return; }
            var o = _orders[_orderIndex];
            _rezept = _menu.FirstOrDefault(r => J.Str(r["id"]) == o.RezeptId);
            _orderPoints = 0;
            _orderFlawless = true;
            _phaseIndex = -1;
            _stage?.ClearStack();
            _stage?.ShowPatties(0);
            _stage?.ResetShake();
            _stage?.Customer(_orderIndex);
            _orderLabel.text = T("schicht.bestellung", ("nr", _orderIndex + 1), ("gesamt", _orders.Count));
            _recipeLabel.text = McGoobyKatalog.TextVon(_rezept, "name", _host.Strings.Locale);
            Sfx.PlayUi("soft_toast");
            NextPhase();
        }

        private void NextPhase()
        {
            _phaseIndex++;
            var phasen = McGoobySchicht.PhasenVon(_rezept);
            if (_phaseIndex >= phasen.Count) { OrderDone(); return; }
            _phase = phasen[_phaseIndex];
            _actionIndex = 0;
            _platziert = 0;
            _t = 0;
            _holding = false;
            _timing = McGoobyKatalog.TimingFuer(_phase, _rezept);
            _ticket = _phase == "belegen" ? McGoobySchicht.TicketVon(_rezept) : new List<string>();
            _aktionen = _phase == "grill"
                ? Enumerable.Repeat("wenden", _orders[_orderIndex].Patties).ToList()
                : _phase == "belegen" ? new List<string>() : McGoobySchicht.AktionenFuer(_rezept, _phase);
            _stage?.Station(_phase);
            if (_phase == "grill") _stage?.ShowPatties(_aktionen.Count);
            if (_phase == "shake") _stage?.ResetShake();
            RefreshPanel();
            if (_phase == "belegen" && _ticket.Count == 0) NextPhase();
            else if (_phase != "belegen" && _aktionen.Count == 0) NextPhase();
        }

        private void RefreshPanel()
        {
            var station = McGoobyKatalog.Station(_phase);
            var stationName = McGoobyKatalog.TextVon(station, "name", _host.Strings.Locale);
            _chips.Clear();
            _ticketList.Clear();
            var timing = _phase != "belegen";
            _bar.style.display = timing ? DisplayStyle.Flex : DisplayStyle.None;
            _action.style.display = timing ? DisplayStyle.Flex : DisplayStyle.None;
            _chips.style.display = timing ? DisplayStyle.None : DisplayStyle.Flex;
            if (timing)
            {
                var aktion = _actionIndex < _aktionen.Count ? _aktionen[_actionIndex] : "";
                _stepLabel.text = stationName + " · " + (_phase == "grill"
                    ? T("schicht.patty", ("nr", _actionIndex + 1), ("gesamt", _aktionen.Count))
                    : T("schicht.schritt", ("nr", _actionIndex + 1), ("gesamt", _aktionen.Count), ("aktion", _host.Strings.HasKey("dlc_mcgooby.aktion." + aktion) ? T("aktion." + aktion) : aktion)));
                var ende = _timing.Ende + _timing.NachlaufSec;
                _barWindow.style.left = Length.Percent((float)(_timing.GarSec / ende * 100.0));
                _barWindow.style.width = Length.Percent((float)(_timing.FensterSec / ende * 100.0));
                _action.text = _phase == "grill" ? T("aktion.wenden") + "!" : _phase == "fritteuse" ? T("aktion.frittieren") : T("aktion.mixen");
                _stateLabel.text = _phase == "grill" ? T("schicht.roh") : _phase == "fritteuse" ? T("schicht.frit_blass") : T("schicht.shake_fluessig");
            }
            else
            {
                _stepLabel.text = stationName + " · " + T("schicht.turm", ("platziert", _platziert), ("gesamt", _ticket.Count));
                _stateLabel.text = "";
                foreach (var zutat in McGoobySchicht.Leiste(_ticket))
                {
                    var z = zutat;
                    var chip = Ux.Btn(T("zutat." + z), () => Wisch(z), "gb-chip", "gb-mcg-zutat");
                    chip.name = "zutat-" + z;
                    _chips.Add(chip);
                }
                for (var i = 0; i < _ticket.Count; i++)
                {
                    var l = Ux.Text(T("zutat." + _ticket[i]), "gb-sm", i < _platziert ? "gb-mcg-done" : i == _platziert ? "gb-mcg-next" : "gb-mild");
                    _ticketList.Add(l);
                }
            }
        }

        // ── timing stations: grill taps once; fryer and shake are held and let go ──

        private bool Held => _phase == "fritteuse" || _phase == "shake";

        private void Press()
        {
            if (!_running || _phase == "belegen" || _done) return;
            if (_phase == "grill") { Judge(); return; }
            _holding = true;
        }

        private void Release()
        {
            if (!_running || !_holding || !Held) return;
            _holding = false;
            Judge();
        }

        private void Judge()
        {
            var w = McGoobySchicht.Bewerte(_phase, _t, _timing, _bal);
            if (w.Punkte == 0 && w.Art != McGoobySchicht.Perfekt)
            {
                // too early: nothing lost, the round simply goes on
                Sfx.PlayUi("soft_tick");
                _stateLabel.text = _phase == "grill" ? T("schicht.roh") : _phase == "fritteuse" ? T("schicht.frit_blass") : T("schicht.shake_fluessig");
                if (Held) _holding = false;
                return;
            }
            Score(w);
        }

        private void Score(McGoobySchicht.Wertung w)
        {
            var perfekt = w.Art == McGoobySchicht.Perfekt;
            _orderPoints += w.Punkte;
            if (perfekt) { _shiftPerfect++; _perfektKette++; }
            else { _orderFlawless = false; _perfektKette = 0; }
            Float(perfekt ? T("schicht.perfekt") + " +" + w.Punkte : (_phase == "fritteuse" ? T("schicht.knusper") : _phase == "shake" ? T("schicht.schaum") : T("schicht.roestaroma")) + " +" + w.Punkte, perfekt);
            if (perfekt && _perfektKette >= 3) Float(T("schicht.kette", ("n", _perfektKette)), true, 1);
            Sfx.Play(perfekt ? "game_perfect" : "game_miss", perfekt ? Mathf.Min(_perfektKette, 6) : 0f);
            if (perfekt) _stage?.Cheer(); else _stage?.Wince();
            if (_phase == "grill") _stage?.FlipPatty(_actionIndex);
            Debug.Log("[mcgooby] " + _phase + " " + w.Art + " +" + w.Punkte);
            _actionIndex++;
            _t = 0;
            UpdatePoints();
            if (_actionIndex >= _aktionen.Count) { Root?.schedule.Execute(NextPhase).StartingIn(500); return; }
            RefreshPanel();
        }

        private void Wisch(string zutat)
        {
            if (!_running || _phase != "belegen") return;
            var (w, platziert) = McGoobySchicht.BewerteWisch(_ticket, _platziert, zutat, _bal);
            if (w.Art == McGoobySchicht.Richtig)
            {
                _orderPoints += w.Punkte;
                _platziert = platziert;
                _stage?.StackLayer(zutat);
                Sfx.Play("game_pop", _platziert * 0.7f);
                if (McGoobySchicht.NaechsteZutat(_ticket, _platziert) == "")
                {
                    Float(T("schicht.passt"), true);
                    _stage?.Cheer();
                    UpdatePoints();
                    RefreshPanel();
                    Root?.schedule.Execute(NextPhase).StartingIn(600);
                    return;
                }
            }
            else
            {
                _orderPoints = Math.Max(0, _orderPoints + w.Punkte);
                _orderFlawless = false;
                Float(T("schicht.falsch"), false);
                Sfx.PlayUi("soft_error");
                _stage?.Wince();
            }
            UpdatePoints();
            RefreshPanel();
        }

        private void OrderDone()
        {
            var bonus = J.Int(_bal["bestellung_fertig_bonus"], 15);
            _orderPoints += bonus;
            _results.Add((_orderPoints, _orderFlawless));
            _stage?.CustomerCheers();
            _stage?.Celebrate();
            Sfx.PlayUi("soft_coins");
            Float("+" + bonus, true);
            UpdatePoints();
            Debug.Log("[mcgooby] bestellung " + (_orderIndex + 1) + " fertig: " + _orderPoints + (_orderFlawless ? " (fehlerfrei)" : ""));
            Root?.schedule.Execute(NextOrder).StartingIn(1300);
        }

        private int Total => _results.Sum(r => r.Item1) + (_running && !_done ? _orderPoints : 0);

        private void UpdatePoints() => _points.text = Total.ToString();

        public override void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stage?.Tick(dt);
            if (!_running || _done || _phase == "belegen" || _actionIndex >= _aktionen.Count) return;
            var reduced = _host.Settings.ReducedMotion;
            if (_phase == "grill" || _holding) _t += dt;
            var z = McGoobySchicht.Zustand(_phase, _t, _timing);
            var f = McGoobySchicht.Fortschritt(_t, _timing);
            var ende = _timing.Ende + _timing.NachlaufSec;
            _barFill.style.width = Length.Percent((float)Math.Min(100.0, _t / ende * 100.0));
            _barFill.EnableInClassList("gb-mcg-bar-fill--gold", z == McGoobySchicht.Goldbraun || z == McGoobySchicht.Goldgelb || z == McGoobySchicht.Krone);
            switch (_phase)
            {
                case "grill":
                    _stage?.Patty(_actionIndex, f, z, reduced);
                    _stateLabel.text = z == McGoobySchicht.Goldbraun ? T("schicht.goldbraun") : z == McGoobySchicht.Kohle ? T("schicht.kohle") : T("schicht.roh");
                    break;
                case "fritteuse":
                    _stage?.Basket(_holding, f, z, reduced);
                    if (_holding) _stateLabel.text = z == McGoobySchicht.Goldgelb ? T("schicht.frit_goldgelb") : z == McGoobySchicht.Dunkel ? T("schicht.frit_dunkel") : T("schicht.frit_blass");
                    break;
                case "shake":
                    if (_holding) _stage?.Shake(f, z, reduced);
                    if (_holding) _stateLabel.text = z == McGoobySchicht.Krone ? T("schicht.shake_krone") : z == McGoobySchicht.Schaum ? T("schicht.shake_schaum") : T("schicht.shake_fluessig");
                    break;
            }
            // left too long past the grace: it counts like a late one (never lost)
            if (_t > _timing.Ende + _timing.NachlaufSec)
            {
                _holding = false;
                Score(McGoobySchicht.Vergessen(_phase, _bal));
            }
        }

        private void Float(string text, bool good, int lane = 0)
        {
            if (Root?.panel == null) return;
            var r = Root.worldBound;
            _host.Ui.FloatText(new Vector2(r.center.x, r.center.y - 40f - lane * 46f), text, good ? new Color(1f, 0.8f, 0.25f) : new Color(1f, 0.55f, 0.5f), 34f);
        }

        // ── the till ──

        private void EndShift()
        {
            _running = false;
            _done = true;
            var kasse = McGoobySchicht.Abrechnung(_results, _bal);
            var flawless = _results.Count > 0 && _results.All(r => r.Item2);
            var vorher = McGoobyState.Sterne(_host.Store.State);
            (bool feiern, int stern) rang = (false, vorher);
            _host.Store.Update(s =>
            {
                McGoobyState.SchichtVerbuchen(s, kasse.Punkte, flawless);
                rang = McGoobyState.AufstiegPruefen(s, vorher);
                Gooby.Core.Life.Economy.Award(J.EnsureDict(s, "economy"), kasse.Muenzen, "mcgooby");
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged(McGoobyState.SliceId);
            Debug.Log("[mcgooby] feierabend: " + kasse.Punkte + " punkte, " + kasse.Muenzen + " muenzen, trinkgeld " + kasse.Trinkgeld + ", sterne " + rang.stern);
            _panel.style.display = DisplayStyle.None;
            _ticketCard.style.display = DisplayStyle.None;
            ShowEnd(kasse, rang.stern, rang.feiern);
        }

        private void ShowEnd(McGoobySchicht.Kasse kasse, int sterne, bool neuerStern)
        {
            _stage?.Celebrate();
            _endCard = Ux.Col("gb-card", "gb-mcg-end");
            _endCard.name = "mcgooby-end";
            _endCard.Add(Ux.Text(T("ende.titel"), "gb-h1"));
            var sub = Ux.Text(T("ende.untertitel"), "gb-md", "gb-mild");
            sub.style.whiteSpace = WhiteSpace.Normal;
            _endCard.Add(sub);
            var rows = Ux.Col("gb-mcg-end-rows");
            void Row(string label, string value, string icon)
            {
                var r = Ux.Row("gb-mcg-end-row");
                r.Add(Ux.Icon(icon));
                var l = Ux.Text(label, "gb-lg");
                l.style.flexGrow = 1;
                l.style.marginLeft = 10;
                r.Add(l);
                r.Add(Ux.Text(value, "gb-lg", "gb-bold"));
                rows.Add(r);
            }
            Row(T("ende.punkte"), kasse.Punkte.ToString(), "star");
            Row(T("ende.perfekt"), _shiftPerfect.ToString(), "sparkle");
            Row(T("ende.trinkgeld"), "+" + kasse.Trinkgeld, "heart");
            Row(T("ende.muenzen"), "+" + kasse.Muenzen, "coin");
            Row(T("ende.rang"), new string('★', sterne) + new string('☆', McGoobyState.MaxSterne - sterne), "trophy");
            _endCard.Add(rows);
            var next = McGoobyState.Naechster(_host.Store.State);
            var ziel = next == null ? T("fortschritt.voll") : T("fortschritt.ziel", ("ziele", string.Join(" · ", new[]
            {
                next.FehltSchichten > 0 ? T("fortschritt.ziel_schichten", ("n", next.FehltSchichten)) : null,
                next.FehltBestwert > 0 ? T("fortschritt.ziel_bestwert", ("punkte", next.BestwertZiel)) : null,
                next.FehltPerfekt > 0 ? T("fortschritt.ziel_perfekt", ("n", next.FehltPerfekt)) : null,
            }.Where(x => x != null))));
            var goal = Ux.Text(ziel, "gb-md", "gb-mild");
            goal.style.whiteSpace = WhiteSpace.Normal;
            _endCard.Add(goal);
            if (_demo)
            {
                var hint = Ux.Text(T("ende.demo_hinweis"), "gb-sm", "gb-mild");
                hint.style.whiteSpace = WhiteSpace.Normal;
                _endCard.Add(hint);
            }
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            if (!_demo)
            {
                var again = Ux.IconTextBtn("redo", T("ende.nochmal"), () => { _host.Ui.Pop(); _host.Ui.Push(new McGoobyScreen(_host)); }, "gb-btn-secondary");
                again.name = "mcgooby-again";
                again.style.marginRight = 10;
                actions.Add(again);
            }
            var done = Ux.IconTextBtn("check", T("ende.feierabend"), Quit, "gb-btn-primary");
            done.name = "mcgooby-done";
            actions.Add(done);
            _endCard.Add(actions);
            Root.Add(_endCard);
            _host.Ui.Confetti(80);
            Sfx.PlayUi("soft_win");
            if (neuerStern)
                Root.schedule.Execute(() => _host.Ui.Celebrate(T("fortschritt.aufstieg_titel"), T("fortschritt.aufstieg_unter", ("n", sterne)), "trophy")).StartingIn(900);
        }

        private void ShowLocked()
        {
            _panel.style.display = DisplayStyle.None;
            _ticketCard.style.display = DisplayStyle.None;
            _endCard = Ux.Col("gb-card", "gb-mcg-end");
            _endCard.name = "mcgooby-locked";
            _endCard.Add(Ux.Text(T("sperre.titel"), "gb-h1"));
            var text = Ux.Text(T("sperre.text"), "gb-md", "gb-mild");
            text.style.whiteSpace = WhiteSpace.Normal;
            _endCard.Add(text);
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var ok = Ux.IconTextBtn("check", T("zurueck"), Quit, "gb-btn-primary");
            ok.name = "mcgooby-done";
            actions.Add(ok);
            _endCard.Add(actions);
            Root.Add(_endCard);
        }

        private void Quit()
        {
            Sfx.PlayUi("soft_back");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            _stage?.Destroy();
            _stage = null;
        }

        /// <summary>QA: "mcgooby bot" plays the next action well; "mcgooby wisch &lt;zutat&gt;".</summary>
        public void QaBot()
        {
            if (!_running || _done) return;
            if (_phase == "belegen") { Wisch(McGoobySchicht.NaechsteZutat(_ticket, _platziert)); return; }
            _t = _timing.GarSec + _timing.FensterSec * 0.5;
            if (Held) _holding = true;
            if (Held) Release(); else Judge();
        }
    }
}
