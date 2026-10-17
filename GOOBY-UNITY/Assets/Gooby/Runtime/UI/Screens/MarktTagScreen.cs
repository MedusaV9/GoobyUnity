using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Core.Cosmetics;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Market day at the own stall (port of the Godot replay + settlement card): the camera looks at the stall, three
    /// customer Goobys queue, and the deterministic day (MarktSim) plays out — the clock runs from 8 to 14, every sale
    /// pops its coins over the counter and adds to the till, every miss is a shrug in the ticker. "Gleich abrechnen" skips
    /// ahead. The settlement card lists per ware what sold, what is left (back in the stores) and what it made, names the
    /// best ware, and the coins fly into the till pill. Opened with watch=false it goes straight to the card.
    /// </summary>
    public sealed class MarktTagScreen : UiScreen
    {
        private const float StepS = 0.6f;
        private const float WaitForCustomersS = 3f;

        private readonly GameHost _host;
        private readonly bool _watch;
        private readonly Juice _juice = new Juice();
        private MarktSim.Ergebnis _sim;
        private int _von;
        private int _next;
        private float _stepIn, _waited, _endIn = -1f;
        private int _till, _streak;
        private bool _settled;
        private VisualElement _hud, _tickerCard, _tillPill;
        private Label _clock, _tillLabel, _ticker;

        public MarktTagScreen(GameHost host, bool watch)
        {
            _host = host;
            _watch = watch;
            Id = "markt-tag";
        }

        private long Now => _host.Store.Clock.NowMs();
        private int Bias => _host.Store.Clock.UtcOffsetMinutes(Now);

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            root.pickingMode = PickingMode.Ignore;
            _sim = MarktStand.Ergebnis(_host.Store);
            _von = MarktStand.Marktfenster().von;

            _hud = Ux.Row("gb-markttag-hud");
            _hud.pickingMode = PickingMode.Ignore;
            var clockPill = Ux.Pill("sun", Clock(0), out _clock);
            clockPill.name = "tag-clock";
            _hud.Add(clockPill);
            _tillPill = Ux.Pill("coin", "0", out _tillLabel);
            _tillPill.name = "tag-till";
            _tillPill.style.marginLeft = 10;
            _hud.Add(_tillPill);
            root.Add(_hud);
            var skip = Ux.IconTextBtn("coin", t.T("nl.stand.gleich_abrechnen"), Settle, "gb-btn-secondary", "gb-btn-sm", "gb-markttag-skip");
            skip.name = "tag-skip";
            root.Add(skip);
            _tickerCard = Ux.Row("gb-card", "gb-markttag-ticker");
            _tickerCard.Add(Ux.IconTile("shop", new Color(1f, 0.92f, 0.62f)));
            _ticker = Ux.Text(t.T("markt.replay.laeuft"), "gb-markttag-ticker-text");
            _ticker.name = "tag-ticker";
            _tickerCard.Add(_ticker);
            root.Add(_tickerCard);
            root.Add(_juice.Layer);
            if (!_watch || _sim == null || _sim.Events.Count == 0) root.schedule.Execute(Settle).StartingIn(_watch ? 600 : 50);
            Debug.Log("[markttag] open watch=" + _watch + " kunden=" + (_sim?.Kunden ?? 0) + " events=" + (_sim?.Events.Count ?? 0));
        }

        public override void OnShown()
        {
            var interior = _host.World?.Interior;
            if (interior == null || !interior.HasOwnStall) return;
            _host.World.GlideCamera(interior.StallDayCameraPosition, interior.StallDayCameraRotation, 1.0f);
            interior.SetStallWares(MarktStand.SliceVon(_host.Store).Slots);
            _ = interior.ShowStallGoobyAsync(CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"])));
            if (_watch) _ = interior.SpawnCustomersAsync();
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            _host.World?.Interior?.ClearCustomers();
        }

        private string Clock(int minute)
        {
            var total = _von * 60 + minute;
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            if (_settled || !_watch || _sim == null) return;
            var interior = _host.World?.Interior;
            // let the customers walk in first (spawned async); without a stage the day still plays in the ticker
            if (_next == 0 && interior != null && !interior.CustomersReady && _waited < WaitForCustomersS) { _waited += dt; return; }
            if (_endIn >= 0f)
            {
                _endIn -= dt;
                if (_endIn <= 0f) Settle();
                return;
            }
            _stepIn -= dt;
            if (_stepIn > 0f) return;
            _stepIn = _host.Settings.ReducedMotion ? 0.12f : StepS;
            if (_next >= _sim.Events.Count)
            {
                _clock.text = Clock((MarktStand.Marktfenster().bis - _von) * 60);
                _endIn = 0.9f;
                return;
            }
            Step(_sim.Events[_next++], interior);
        }

        private void Step(MarktSim.Event e, World.InteriorScene interior)
        {
            var t = _host.Strings;
            _clock.text = Clock(e.Minute);
            var name = WareName(e.Ware);
            interior?.CustomerStep(e.Gekauft, _host.Settings.ReducedMotion);
            if (e.Gekauft)
            {
                _streak++;
                _till += e.Preis;
                _tillLabel.text = Ux.FormatNumber(_till);
                _ticker.text = t.T("markt.replay.kauf", ("name", name), ("preis", e.Preis));
                Sfx.Play("game_coin", Sfx.ComboSemitones(_streak));
                _juice.ScalePop(_tillPill, 1.16f, 0.2f);
                FloatOverStall("+" + e.Preis + " ᴳ", new Color(1f, 0.82f, 0.25f));
            }
            else
            {
                _streak = 0;
                _ticker.text = t.T("markt.replay.vorbei", ("name", name));
                Sfx.PlayUi("soft_tick");
            }
        }

        private void FloatOverStall(string text, Color color)
        {
            var interior = _host.World?.Interior;
            var cam = _host.World?.Camera;
            if (Root?.panel == null) return;
            Vector2 at;
            if (interior != null && cam != null)
            {
                at = RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, interior.StallCounterWorld, cam);
                at.x += Random.Range(-30f, 30f);
            }
            else at = new Vector2(Root.layout.width * 0.5f, Root.layout.height * 0.45f);
            _juice.FloatText(at, text, color, 30f, 80f);
        }

        private string WareName(string id)
        {
            var t = _host.Strings;
            var w = MarktWaren.Ware(id);
            if (w != null && w.Lager == "food" && t.HasKey("rewards.food." + id)) return t.T("rewards.food." + id);
            return MarktWaren.Anzeigename(id, _host.Settings.Locale);
        }

        // ── the settlement card ──

        private void Settle()
        {
            if (_settled) return;
            _settled = true;
            var t = _host.Strings;
            var karte = MarktStand.Abholen(_host.Store, Now, Bias);
            _hud.style.display = DisplayStyle.None;
            _tickerCard.style.display = DisplayStyle.None;
            var skip = Root.Q("tag-skip");
            if (skip != null) skip.style.display = DisplayStyle.None;
            if (karte == null)
            {
                Debug.Log("[markttag] nothing to settle");
                _host.Ui.Pop();
                return;
            }
            Debug.Log("[markttag] settled erloes=" + karte.Erloes + " beste=" + karte.BesteWare + " rows=" + karte.Zeilen.Count);
            var interior = _host.World?.Interior;
            interior?.SetStallWares(null);
            if (karte.Erloes > 0) interior?.CheerStall();

            var scrim = Ux.El("gb-scrim");
            Root.Add(scrim);
            var win = Ux.El("gb-win", "gb-markttag-card");
            win.name = "tag-card";
            win.Add(CounterPanel.Head("coin", new Color(1f, 0.92f, 0.62f), t.T("markt.abrechnung.titel"),
                string.IsNullOrEmpty(karte.BesteWare) ? t.T("markt.abrechnung.rueck") : t.T("markt.abrechnung.beste", ("name", WareName(karte.BesteWare))), null).Row);
            var body = Ux.Col("gb-win-body");
            foreach (var z in karte.Zeilen)
            {
                var row = Ux.Row("gb-markttag-row");
                var thumb = MarktStandScreen.WareThumb(z.Ware);
                thumb.AddToClassList("gb-markttag-thumb");
                row.Add(thumb);
                var texts = Ux.Col();
                texts.style.flexGrow = 1; texts.style.flexShrink = 1;
                texts.Add(Ux.Text(WareName(z.Ware), "gb-bold"));
                texts.Add(Ux.Text(t.T("nl.stand.zeile", ("verkauft", z.Verkauft), ("uebrig", z.Uebrig)), "gb-sm", "gb-mild"));
                row.Add(texts);
                row.Add(Ux.Text("+" + Ux.FormatNumber(z.Erloes) + " ᴳ", "gb-markttag-row-sum"));
                body.Add(row);
            }
            var total = Ux.Row("gb-markttag-total");
            var totalLabel = Ux.Text(karte.Erloes > 0 ? t.T("markt.abrechnung.summe", ("erloes", 0)) : t.T("markt.abrechnung.nichts"), "gb-markttag-total-text");
            totalLabel.name = "tag-total";
            totalLabel.style.whiteSpace = WhiteSpace.Normal;
            total.Add(totalLabel);
            body.Add(total);
            var note = Ux.Text(t.T("markt.abrechnung.rueck"), "gb-sm", "gb-mild");
            note.style.whiteSpace = WhiteSpace.Normal;
            body.Add(note);
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var ok = Ux.IconTextBtn("check", t.T("markt.abrechnung.ok"), Done, "gb-btn-confirm");
            ok.name = "tag-ok";
            actions.Add(ok);
            body.Add(actions);
            win.Add(body);
            Root.Add(win);
            _juice.Layer.BringToFront();
            Sfx.Play(karte.Erloes > 0 ? "soft_win" : "soft_toast");
            if (karte.Erloes <= 0) return;
            _host.Ui.Confetti(_host.Settings.ReducedMotion ? 16 : 60);
            // the total counts up while the coins fly into the till pill
            var flyers = Mathf.Clamp(karte.Erloes / 10, 4, 12);
            _tillPill.style.display = DisplayStyle.Flex;
            _hud.style.display = DisplayStyle.Flex;
            _clock.parent.style.display = DisplayStyle.None;
            _tillLabel.text = "0";
            var landed = 0;
            win.schedule.Execute(() =>
            {
                var from = new Vector2(win.worldBound.center.x, win.worldBound.yMax - 90f);
                _host.Ui.FlyIcons(from, _tillPill, "coin", flyers, _ =>
                {
                    landed++;
                    var shown = landed >= flyers ? karte.Erloes : karte.Erloes * landed / flyers;
                    _tillLabel.text = Ux.FormatNumber(shown);
                    totalLabel.text = t.T("markt.abrechnung.summe", ("erloes", shown));
                    Sfx.Play("soft_coins", landed * 0.5f, 0.7f);
                });
            }).StartingIn(350);
        }

        private void Done()
        {
            Sfx.PlayUi("soft_confirm");
            _host.Ui.Pop();
            _host.Ui.Push(new MarktStandScreen(_host));
        }
    }
}
