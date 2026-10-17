using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Dlc;
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
    /// Goo und Bye, Gooby's own corner store (port of laden_scene.gd, laden_grossmarkt.gd and laden_angebot.gd): the
    /// store in 3D behind, a chip over every crate of the produce stand — tap to put wares from the stores onto the
    /// shelf (chilled ones only while cooler room is free) — and the bar in the thumb zone: the wholesale run (order
    /// with the trunk's room, the car drives, unload the crates), the daily deal (one group −15 %, a doodled sign over
    /// the stand), "Auffüllen" for the casual manager and "Laden öffnen!". Open, the deterministic market day plays out:
    /// customers come through the door ("Goo!"), take their wares off the crates, the till beeps its babble melody,
    /// the coins land in the till and they leave ("Bye!"). Uncle Alwin buys exactly one carrot, families add a sweet
    /// at the till, the hamster buys a whole crate. The till count shows takings, customers, goods sold and the profit
    /// after wholesale; the store's level grows with its market days and takings (new decoration, more customers), the
    /// company van arrives at level 5.
    /// </summary>
    public sealed class GoobyeScreen : UiScreen
    {
        private const string Einraeumen = "einraeumen", Offen = "offen", Abschluss = "abschluss";

        private readonly GameHost _host;
        private readonly Juice _juice = new Juice();
        private GoobyeStage _stage;
        private GoobyeRegal _regal, _anzeige;
        private Dictionary<string, int> _lager = new Dictionary<string, int>();
        private string _phase = Einraeumen;
        private GoobyeMarkttag.Plan _plan;
        private int _bonNext, _kasse, _levelUp, _besterVorher, _gewinn, _bonStreak, _lieferungGezeigt = -1;
        private bool _schnell, _kasseFrei = true, _gekauft;
        private float _spawnIn;
        private readonly List<GoobyeStage.Kunde> _kunden = new List<GoobyeStage.Kunde>();
        private readonly Dictionary<string, int> _korb = new Dictionary<string, int>();

        private VisualElement _field, _chipLayer, _bubbleLayer, _bar, _dayBar, _overlay, _sign, _levelFill, _kassePill, _coinsPill, _grossFill;
        private readonly Button[] _chips = new Button[GoobyeStage.Slots];
        private Label _levelLabel, _levelHint, _coinsLabel, _lagerLabel, _kasseLabel, _ticker;
        private Button _grossBtn, _angebotBtn, _fuellBtn, _openBtn, _fastBtn;
        private Label _grossText;

        public GoobyeScreen(GameHost host)
        {
            _host = host;
            Id = "goobye";
            Opaque = true;
        }

        public override string SoundScene => "city/ort/goobye";

        private JObject State => _host.Store.State;
        private long Now => _host.Store.Clock.NowMs();
        private string Today => _host.Store.Clock.LocalDay(Now);
        private bool Reduced => _host.Settings.ReducedMotion;
        private float Speed => _schnell || Reduced ? 2.6f : 1f;

        private string T(string key, params (string, object)[] args) => _host.Strings.T("dlc_goobye." + key, args);

        private string WareName(string id) => _host.Strings.T(J.Str(GoobyeKatalog.Ware(id)?["name_key"]));

        private string GruppeName(string id) => _host.Strings.T(J.Str(GoobyeKatalog.Gruppe(id)?["name_key"]));

        private static (string farbe, string form) Look(string wareId)
        {
            var g = GoobyeKatalog.Gruppe(GoobyeKatalog.GruppeVon(wareId));
            return (J.Str(g?["farbe"], "#CCCCCC"), J.Str(g?["form"], "eckig"));
        }

        private static string Glyph(string form)
        {
            switch (form)
            {
                case "rund": return "●";
                case "tropfen": return "◆";
                case "dreieck": return "▲";
                case "stern": return "★";
                case "raute": return "◆";
                default: return "■";
            }
        }

        private static Color Farbe(string hex) => ArtLibrary.Hex(hex);

        // ── build ──

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-goobye");
            _field = new VisualElement { name = "goobye-field", pickingMode = PickingMode.Ignore };
            Ux.Abs(_field, 0, 0, 0, 0);
            root.Add(_field);
            _gekauft = GoobyeState.IstGekauft(State);
            _regal = GoobyeState.RegalVon(State);
            _anzeige = Kopie(_regal);
            _lager = GoobyeState.LagerVon(State);
            if (MgStage3D.Supported)
            {
                var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(State["cosmetics"]));
                _stage = new GoobyeStage(_field, outfit, GoobyeLevel.LevelFuer(State));
            }

            var back = Ux.IconBtn("back_white", Quit, "gb-place-back");
            back.name = "goobye-quit";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.Row("gb-card", "gb-goobye-head");
            Ux.Abs(head, 84, 12);
            head.Add(Ux.IconTile("shop", new Color(0.66f, 0.86f, 0.84f)));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(T("titel"), "gb-place-title"));
            _levelLabel = Ux.Text("", "gb-sm", "gb-bold");
            _levelLabel.name = "goobye-level";
            hcol.Add(_levelLabel);
            var bar = Ux.Bar(0f, out _levelFill);
            bar.AddToClassList("gb-goobye-levelbar");
            hcol.Add(bar);
            _levelHint = Ux.Text("", "gb-xs", "gb-mild");
            hcol.Add(_levelHint);
            head.Add(hcol);
            root.Add(head);

            var pills = Ux.Row("gb-goobye-pills");
            Ux.Abs(pills, null, 14, 16);
            _kassePill = Ux.Pill("shop", "0", out _kasseLabel);
            _kassePill.name = "goobye-kasse";
            _kassePill.style.display = DisplayStyle.None;
            pills.Add(_kassePill);
            var lagerPill = Ux.Pill("inventory", "0", out _lagerLabel);
            lagerPill.name = "goobye-lager";
            pills.Add(lagerPill);
            _coinsPill = Ux.Pill("coin", "0", out _coinsLabel);
            _coinsPill.name = "goobye-coins";
            pills.Add(_coinsPill);
            root.Add(pills);

            _sign = Ux.Col("gb-goobye-sign");
            _sign.name = "goobye-sign";
            _sign.pickingMode = PickingMode.Ignore;
            _sign.style.display = DisplayStyle.None;
            root.Add(_sign);

            _chipLayer = new VisualElement { name = "goobye-chips", pickingMode = PickingMode.Ignore };
            Ux.Abs(_chipLayer, 0, 0, 0, 0);
            root.Add(_chipLayer);
            for (var i = 0; i < GoobyeStage.Slots; i++)
            {
                var idx = i;
                var chip = new Button(() => SlotTippen(idx)) { name = "goobye-slot-" + i };
                chip.ClearClassList();
                chip.AddToClassList("gb-goobye-chip");
                _chipLayer.Add(chip);
                _chips[i] = chip;
            }
            _bubbleLayer = new VisualElement { name = "goobye-bubbles", pickingMode = PickingMode.Ignore };
            Ux.Abs(_bubbleLayer, 0, 0, 0, 0);
            root.Add(_bubbleLayer);

            _bar = Ux.Row("gb-goobye-bar");
            _bar.name = "goobye-bar";
            _grossBtn = new Button(GrossmarktTippen) { name = "goobye-grossmarkt" };
            _grossBtn.ClearClassList();
            _grossBtn.AddToClassList("gb-btn");
            _grossBtn.AddToClassList("gb-btn-secondary");
            _grossBtn.AddToClassList("gb-goobye-barbtn");
            _grossFill = Ux.El("gb-goobye-tripfill");
            _grossFill.pickingMode = PickingMode.Ignore;
            _grossBtn.Add(_grossFill);
            var grossIcon = Ux.Icon("car");
            grossIcon.pickingMode = PickingMode.Ignore;
            _grossBtn.Add(grossIcon);
            _grossText = Ux.Text(T("grossmarkt.knopf"), "gb-goobye-bartext");
            _grossText.pickingMode = PickingMode.Ignore;
            _grossBtn.Add(_grossText);
            _bar.Add(_grossBtn);
            _angebotBtn = Ux.IconTextBtn("sign", T("tagesangebot.knopf"), ZeigeAngebot, "gb-btn-secondary", "gb-goobye-barbtn");
            _angebotBtn.name = "goobye-angebot";
            _bar.Add(_angebotBtn);
            _fuellBtn = Ux.IconTextBtn("inventory", T("laden.alle_fuellen"), AllesAuffuellen, "gb-btn-secondary", "gb-goobye-barbtn");
            _fuellBtn.name = "goobye-fuellen";
            _bar.Add(_fuellBtn);
            _openBtn = Ux.IconTextBtn("shop", T("laden.oeffnen"), LadenOeffnen, "gb-btn-primary", "gb-goobye-barbtn", "gb-goobye-open");
            _openBtn.name = "goobye-oeffnen";
            _bar.Add(_openBtn);
            root.Add(_bar);

            _dayBar = Ux.Row("gb-goobye-daybar");
            _dayBar.name = "goobye-daybar";
            _dayBar.style.display = DisplayStyle.None;
            var tickerCard = Ux.Row("gb-card", "gb-goobye-ticker");
            tickerCard.Add(Ux.IconTile("shop", new Color(1f, 0.92f, 0.62f)));
            _ticker = Ux.Text("", "gb-goobye-ticker-text");
            _ticker.name = "goobye-ticker";
            tickerCard.Add(_ticker);
            _dayBar.Add(tickerCard);
            _fastBtn = Ux.IconTextBtn("stopwatch", T("laden.schneller"), () => { _schnell = !_schnell; RefreshDayBar(); }, "gb-btn-secondary", "gb-btn-sm");
            _fastBtn.name = "goobye-schnell";
            _dayBar.Add(_fastBtn);
            var skip = Ux.IconTextBtn("coin", T("laden.ueberspringen"), Abrechnen, "gb-btn-secondary", "gb-btn-sm");
            skip.name = "goobye-abrechnen";
            _dayBar.Add(skip);
            root.Add(_dayBar);
            root.Add(_juice.Layer);

            RefreshAll();
            if (!_gekauft) root.schedule.Execute(ZeigeKaufangebot).StartingIn(250);
            else if (ErstbesuchMerken()) root.schedule.Execute(ZeigeIntro).StartingIn(500);
            Debug.Log("[goobye] laden auf: gekauft=" + _gekauft + " level=" + GoobyeLevel.LevelFuer(State) + " lager=" + _lager.Values.Sum() + " regal=" + _regal.GesamtBestand());
        }

        private bool ErstbesuchMerken()
        {
            var erst = false;
            _host.Store.Update(s => erst = GoobyeState.ErstbesuchMerken(s), GameHost.TicksMs);
            if (erst) _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            return erst;
        }

        private static GoobyeRegal Kopie(GoobyeRegal r) => GoobyeRegal.Normalisieren(r.ToJson());

        // ── refresh ──

        private void RefreshAll()
        {
            RefreshHead();
            RefreshSlots();
            RefreshBar();
            RefreshSign();
        }

        private void RefreshHead()
        {
            var level = GoobyeLevel.LevelFuer(State);
            _levelLabel.text = T("laden.level", ("name", _host.Strings.T(GoobyeLevel.NameKey(level))), ("level", level));
            var next = GoobyeLevel.Naechste(State);
            if (next == null) Ux.SetBar(_levelFill, 1.0);
            else
            {
                var n = next.Value;
                var u = J.Path(State, GoobyeState.SliceId, GoobyeState.Key, "umsatz");
                var tageR = n.stufe.Tage <= 0 ? 1.0 : Math.Min(1.0, J.Int(u?["tage"]) / (double)n.stufe.Tage);
                var umsatzR = n.stufe.Gesamt <= 0 ? 1.0 : Math.Min(1.0, J.Int(u?["gesamt"]) / (double)n.stufe.Gesamt);
                Ux.SetBar(_levelFill, (tageR + umsatzR) * 0.5);
            }
            _levelHint.text = ZielText();
            _coinsLabel.text = Ux.FormatNumber(J.Int(J.Path(State, "economy", "coins")));
            _lagerLabel.text = Ux.FormatNumber(_lager.Values.Sum());
            _kasseLabel.text = Ux.FormatNumber(_kasse);
        }

        /// <summary>"To Minimarkt: 1 more market day and 5 ᴳ in takings" — only what is still missing, with the right plural.</summary>
        private string ZielText()
        {
            var next = GoobyeLevel.Naechste(State);
            if (next == null) return T("laden.voll_ausgebaut");
            var n = next.Value;
            var teile = new List<string>();
            if (n.fehltTage > 0) teile.Add(n.fehltTage == 1 ? T("laden.ziel_tag") : T("laden.ziel_tage", ("n", n.fehltTage)));
            if (n.fehltUmsatz > 0) teile.Add(T("laden.ziel_umsatz", ("n", n.fehltUmsatz)));
            return T("laden.ziel", ("name", _host.Strings.T(n.stufe.NameKey)), ("was", string.Join(T("laden.ziel_und"), teile)));
        }

        private void RefreshSlots()
        {
            var deal = GoobyeAngebot.AktiveGruppe(State, Today);
            for (var i = 0; i < GoobyeStage.Slots; i++)
            {
                var slot = _anzeige.Slots[i];
                var chip = _chips[i];
                chip.Clear();
                chip.EnableInClassList("gb-goobye-chip--leer", slot.Menge <= 0);
                chip.EnableInClassList("gb-goobye-chip--deal", slot.Menge > 0 && deal.Length > 0 && GoobyeKatalog.GruppeVon(slot.Ware) == deal);
                if (slot.Menge <= 0)
                {
                    var plus = Ux.Icon("plus");
                    plus.pickingMode = PickingMode.Ignore;
                    chip.Add(plus);
                    chip.tooltip = T("laden.slot_leer");
                }
                else
                {
                    var ware = GoobyeKatalog.Ware(slot.Ware);
                    var (farbe, form) = Look(slot.Ware);
                    var icon = CounterPanel.FoodIcon(slot.Ware, null);
                    VisualElement thumb;
                    if (icon != null) thumb = Ux.Icon(icon);
                    else
                    {
                        thumb = Ux.Text(Glyph(form), "gb-goobye-glyph");
                        thumb.style.color = Farbe(farbe);
                    }
                    thumb.pickingMode = PickingMode.Ignore;
                    chip.Add(thumb);
                    var col = Ux.Col();
                    col.pickingMode = PickingMode.Ignore;
                    var n = Ux.Text("×" + slot.Menge, "gb-goobye-chip-text");
                    n.pickingMode = PickingMode.Ignore;
                    col.Add(n);
                    var preis = deal.Length > 0 && GoobyeKatalog.GruppeVon(slot.Ware) == deal ? GoobyeAngebot.AngebotsPreis(ware) : GoobyePreis.Verkaufspreis(ware);
                    var p = Ux.Text(preis + " ᴳ", "gb-goobye-chip-price");
                    p.pickingMode = PickingMode.Ignore;
                    col.Add(p);
                    chip.Add(col);
                    var band = Ux.El("gb-goobye-chip-band");
                    band.style.backgroundColor = Farbe(farbe);
                    band.pickingMode = PickingMode.Ignore;
                    chip.Add(band);
                }
                var look = Look(slot.Ware);
                _stage?.SetSlot(i, slot.Ware, slot.Menge, GoobyeKuehl.IstKuehlware(slot.Ware), look.farbe, look.form);
            }
        }

        private void RefreshBar()
        {
            var einr = _phase == Einraeumen;
            _bar.style.display = einr ? DisplayStyle.Flex : DisplayStyle.None;
            _dayBar.style.display = _phase == Offen ? DisplayStyle.Flex : DisplayStyle.None;
            _kassePill.style.display = _phase == Einraeumen && _kasse == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _fuellBtn.SetEnabled(_lager.Values.Sum() > 0 && _regal.Slots.Any(s => s.Menge < GoobyeRegal.MaxJeSlot));
            _openBtn.EnableInClassList("gb-goobye-open--pulse", _regal.GesamtBestand() > 0);
            RefreshTrip();
        }

        private void RefreshDayBar() => _fastBtn.EnableInClassList("gb-goobye-fast--on", _schnell);

        private void RefreshTrip()
        {
            var status = GoobyeTransport.StatusVon(State, Now);
            if (status == null)
            {
                _grossText.text = T("grossmarkt.knopf");
                _grossBtn.RemoveFromClassList("gb-btn-confirm");
                _grossFill.style.width = 0;
                if (_lieferungGezeigt != 0) { _stage?.SetDelivery(0); _lieferungGezeigt = 0; }
                return;
            }
            if (status.Phase == GoobyeTransport.PhaseDa)
            {
                _grossText.text = T("grossmarkt.ausladen");
                _grossBtn.AddToClassList("gb-btn-confirm");
                _grossFill.style.width = 0;
                var kisten = GoobyeTransport.KistenImKorb(Korb(GoobyeTransport.UnterwegsVon(State)));
                if (_lieferungGezeigt != kisten)
                {
                    _stage?.SetDelivery(kisten);
                    if (_lieferungGezeigt >= 0 && _lieferungGezeigt != kisten) { Sfx.PlayUi("soft_toast"); _host.Ui.Toast(T("grossmarkt.ankunft"), "car"); }
                    _lieferungGezeigt = kisten;
                }
                return;
            }
            _grossBtn.RemoveFromClassList("gb-btn-confirm");
            var s = (int)Math.Ceiling(Math.Max(0, status.RestMs) / 1000.0);
            _grossText.text = T("grossmarkt.status_" + status.Phase, ("rest", (s / 60) + ":" + (s % 60).ToString("00")));
            _grossFill.style.width = Length.Percent((float)(status.Fortschritt * 100.0));
        }

        private static Dictionary<string, int> Korb(JObject fahrt)
        {
            var korb = new Dictionary<string, int>();
            if (fahrt?["warenkorb"] is JObject k)
                foreach (var p in k.Properties()) korb[p.Name] = J.Int(p.Value);
            return korb;
        }

        private void RefreshSign()
        {
            var gruppe = GoobyeAngebot.AktiveGruppe(State, Today);
            _sign.Clear();
            if (gruppe.Length == 0) { _sign.style.display = DisplayStyle.None; return; }
            var stil = SchildStile[GoobyeAngebot.SchildVariante(Today, gruppe)];
            _sign.style.display = DisplayStyle.Flex;
            _sign.style.backgroundColor = Farbe(stil.papier);
            _sign.style.rotate = new Rotate(new Angle(stil.neigung, AngleUnit.Degree));
            var big = Ux.Text(T("tagesangebot.schild"), "gb-goobye-sign-big");
            big.style.color = Farbe(stil.tinte);
            _sign.Add(big);
            var name = Ux.Text(GruppeName(gruppe), "gb-goobye-sign-name");
            name.style.color = Farbe(stil.tinte);
            _sign.Add(name);
        }

        /// <summary>The six doodle styles of the deal sign (laden_angebot.gd SCHILD_STILE).</summary>
        private static readonly (string papier, string tinte, float neigung)[] SchildStile =
        {
            ("#FFF4E6", "#4A3B36", -8f), ("#F2C94C", "#4A3B36", 5f), ("#F2B5D4", "#7A3B5E", -4f),
            ("#A7DCD6", "#2F5D57", 9f), ("#FFD8A8", "#7A4A1F", -11f), ("#CDEB8B", "#3F6212", 3f),
        };

        // ── per frame ──

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            _stage?.Tick(dt);
            if (_phase == Einraeumen) RefreshTrip();
            PlaceChips();
            if (_phase != Offen) { TickKunden(dt); return; }
            TickKunden(dt);
            if (_plan == null) return;
            _spawnIn -= dt * Speed;
            if (_bonNext < _plan.Bons.Count && (_spawnIn <= 0f || _kunden.Count == 0)) SpawnKunde(_plan.Bons[_bonNext++]);
            if (_bonNext >= _plan.Bons.Count && _kunden.Count == 0) TagEnde();
        }

        private void TickKunden(float dt)
        {
            for (var i = _kunden.Count - 1; i >= 0; i--)
            {
                var k = _kunden[i];
                k.Tick(dt, Speed);
                if (!k.Fertig) continue;
                _stage?.Remove(k);
                _kunden.RemoveAt(i);
            }
        }

        private float _placedW, _placedH;
        private bool _signPlaced;

        private void PlaceChips()
        {
            var w = _field.layout.width;
            var h = _field.layout.height;
            if (float.IsNaN(w) || w <= 0f) return;
            var signShown = _sign.style.display == DisplayStyle.Flex;
            if (Mathf.Approximately(w, _placedW) && Mathf.Approximately(h, _placedH) && signShown == _signPlaced) return;
            _placedW = w;
            _placedH = h;
            _signPlaced = signShown;
            for (var i = 0; i < GoobyeStage.Slots; i++)
            {
                Vector2 p;
                if (_stage != null) p = _stage.ToField(GoobyeStage.SlotChipPoint(i), w, h);
                else p = new Vector2(w * (0.2f + i * 0.08f), h * 0.42f);
                _chips[i].style.left = p.x;
                _chips[i].style.top = p.y;
            }
            if (signShown)
            {
                var s = _stage != null ? _stage.ToField(GoobyeStage.SignPoint, w, h) : new Vector2(w * 0.36f, h * 0.3f);
                _sign.style.left = s.x;
                _sign.style.top = s.y;
            }
        }

        private Vector2 FieldPoint(Vector3 local)
        {
            var w = _field.layout.width;
            var h = _field.layout.height;
            if (_stage == null || float.IsNaN(w) || w <= 0f) return new Vector2(Root.worldBound.center.x, Root.worldBound.height * 0.45f);
            return _stage.ToField(local, w, h);
        }

        // ── stocking ──

        private void SlotTippen(int i)
        {
            if (_phase != Einraeumen) { Sfx.PlayUi("soft_error"); return; }
            Sfx.PlayUi("soft_open");
            ZeigeSlotSheet(i);
        }

        /// <summary>Puts as many of the ware as fit (slot cap, stores, cooler room) into slot i; an occupied slot is emptied first.</summary>
        private int InsRegal(int i, string wareId, bool still = false)
        {
            if (i < 0 || i >= GoobyeStage.Slots || _phase != Einraeumen) return 0;
            var slot = _regal.Slots[i];
            if (slot.Menge > 0 && slot.Ware != wareId) _regal.Ausraeumen(i, _lager);
            var wunsch = GoobyeKuehl.Einraeumbar(_regal, wareId, GoobyeRegal.MaxJeSlot, GoobyeKuehl.ModuleVon(State));
            if (wunsch <= 0)
            {
                if (!still) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T("kuehl.voll", ("je", GoobyeKuehl.StueckJeModul)), "snow"); }
                return 0;
            }
            var bewegt = _regal.Einraeumen(i, wareId, wunsch, _lager);
            if (bewegt <= 0)
            {
                if (!still) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(T(slot.Menge >= GoobyeRegal.MaxJeSlot ? "laden.regal_voll" : "laden.lager_leer"), "inventory"); }
                return 0;
            }
            if (!still)
            {
                Sfx.Play("soft_chip", Semitone(GoobyeKatalog.TonFuer(wareId)));
                _stage?.SlotSparkle(i);
                _juice.ScalePop(_chips[i], 1.15f, 0.2f);
            }
            return bewegt;
        }

        private static float Semitone(double ton) => (float)(12.0 * Math.Log(Math.Max(0.5, ton), 2.0));

        private void Speichern()
        {
            var regal = _regal;
            var lager = new Dictionary<string, int>(_lager);
            _host.Store.Update(s => { GoobyeState.RegalSetzen(s, regal); GoobyeState.LagerSetzen(s, lager); }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            _anzeige = Kopie(_regal);
            RefreshAll();
        }

        /// <summary>"Einfach führen": top up every stocked slot, then fill empty ones with what the stores hold most of.</summary>
        private void AllesAuffuellen()
        {
            if (_phase != Einraeumen) return;
            var n = 0;
            for (var i = 0; i < GoobyeStage.Slots; i++)
                if (_regal.Slots[i].Menge > 0 && _lager.TryGetValue(_regal.Slots[i].Ware, out var da) && da > 0) n += InsRegal(i, _regal.Slots[i].Ware, true);
            for (var i = 0; i < GoobyeStage.Slots; i++)
            {
                if (_regal.Slots[i].Menge > 0) continue;
                var listed = new HashSet<string>(_regal.Slots.Where(s => s.Menge > 0).Select(s => s.Ware));
                foreach (var kv in _lager.Where(x => x.Value > 0 && !listed.Contains(x.Key)).OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList())
                {
                    var moved = InsRegal(i, kv.Key, true);
                    if (moved > 0) { n += moved; break; }
                }
            }
            if (n <= 0)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(T(_lager.Values.Sum() > 0 ? "laden.regal_voll" : "laden.lager_leer"), "inventory");
                return;
            }
            Speichern();
            for (var i = 0; i < GoobyeStage.Slots; i++) _stage?.SlotSparkle(i);
            for (var k = 0; k < Math.Min(6, n); k++)
            {
                var kk = k;
                Root.schedule.Execute(() => Sfx.Play("soft_chip", kk * 1.5f)).StartingIn(kk * 70);
            }
            _host.Ui.Toast(T("laden.gefuellt_toast", ("n", n)), "inventory");
            Debug.Log("[goobye] auffuellen +" + n + " → regal " + _regal.GesamtBestand());
        }

        private void ZeigeSlotSheet(int i)
        {
            var slot = _regal.Slots[i];
            var body = OpenSheet("inventory", new Color(0.66f, 0.86f, 0.84f), T("laden.slot_titel", ("nr", i + 1)),
                slot.Menge > 0 ? T("laden.slot_inhalt", ("name", WareName(slot.Ware)), ("n", slot.Menge)) : T("laden.waehlen_hinweis"), out var footer);
            if (slot.Menge > 0)
            {
                var row = Ux.Row("gb-goobye-slotactions");
                _lager.TryGetValue(slot.Ware, out var da);
                var platz = Math.Min(GoobyeRegal.MaxJeSlot - slot.Menge, da);
                var auf = Ux.IconTextBtn("plus", T("laden.auffuellen", ("n", Math.Max(0, platz))), () => { if (InsRegal(i, slot.Ware) > 0) { Speichern(); CloseSheet(); } }, "gb-btn-confirm", "gb-btn-sm");
                auf.name = "goobye-auffuellen";
                auf.SetEnabled(platz > 0);
                row.Add(auf);
                var aus = Ux.IconTextBtn("undo", T("laden.ausraeumen"), () => { _regal.Ausraeumen(i, _lager); Sfx.PlayUi("soft_back"); Speichern(); CloseSheet(); }, "gb-btn-secondary", "gb-btn-sm");
                aus.name = "goobye-ausraeumen";
                aus.style.marginLeft = 10;
                row.Add(aus);
                body.Add(row);
            }
            var waren = GoobyeKatalog.Waren().Where(w => _lager.TryGetValue(J.Str(w["id"]), out var n) && n > 0).ToList();
            if (waren.Count == 0)
            {
                body.Add(CounterPanel.Empty(T("laden.lager_leer"), T("grossmarkt.hinweis_kurz")));
                var go = Ux.IconTextBtn("car", T("grossmarkt.knopf"), () => { CloseSheet(); ZeigeGrossmarkt(); }, "gb-btn-primary");
                footer.Add(go);
                return;
            }
            body.Add(Ux.Text(T("laden.waehlen_titel"), "gb-md", "gb-bold"));
            var grid = Ux.Row("gb-goobye-waregrid");
            foreach (var w in waren)
            {
                var id = J.Str(w["id"]);
                var (farbe, form) = Look(id);
                var icon = CounterPanel.FoodIcon(id, null);
                VisualElement thumb = icon != null ? Ux.Icon(icon) : (VisualElement)Ux.Text(Glyph(form), "gb-goobye-glyph");
                if (icon == null) thumb.style.color = Farbe(farbe);
                var card = Ux.Col("gb-goobye-ware");
                card.name = "goobye-ware-" + id;
                card.Add(thumb);
                card.Add(Ux.Text(WareName(id), "gb-sm", "gb-bold"));
                card.Add(Ux.Text(T("laden.lager_stueck", ("n", _lager[id])) + " · " + GoobyePreis.Verkaufspreis(w) + " ᴳ", "gb-xs", "gb-mild"));
                var band = Ux.El("gb-goobye-ware-band");
                band.style.backgroundColor = Farbe(farbe);
                card.Add(band);
                if (GoobyeKuehl.IstKuehlware(id))
                {
                    var cold = Ux.Icon("snow", "gb-goobye-ware-cold");
                    card.Add(cold);
                }
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    if (InsRegal(i, id) <= 0) return;
                    Speichern();
                    CloseSheet();
                    _host.Ui.Toast(T("laden.eingeraeumt", ("name", WareName(id)), ("n", _regal.Slots[i].Menge)), "inventory");
                });
                grid.Add(card);
            }
            body.Add(grid);
        }

        // ── the market day ──

        private void LadenOeffnen()
        {
            if (_phase != Einraeumen) return;
            if (_regal.GesamtBestand() <= 0)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(T("laden.erst_einraeumen"), "inventory");
                foreach (var c in _chips) _juice.ScalePop(c, 1.2f, 0.25f);
                return;
            }
            var tag = Today;
            var level = GoobyeLevel.LevelFuer(State);
            var (kmin, kmax) = GoobyeLevel.KundenFuer(level);
            var deal = GoobyeAngebot.AktiveGruppe(State, tag);
            var sortiment = GoobyeAngebot.SortimentMarkieren(_regal.SortimentVon(), deal);
            var seed = GoobyeState.RundenSeed(State, tag);
            _plan = GoobyeMarkttag.TagPlanen(seed, sortiment, kmin, kmax);
            _anzeige = Kopie(_regal);
            var nachher = Kopie(_regal);
            _gewinn = 0;
            foreach (var bon in _plan.Bons)
                foreach (var pos in bon.Positionen)
                {
                    nachher.Entnehmen(pos.Ware);
                    _gewinn += pos.Preis - GoobyePreis.Einkaufspreis(GoobyeKatalog.Ware(pos.Ware));
                }
            _besterVorher = J.Int(J.Path(State, GoobyeState.SliceId, GoobyeState.Key, "umsatz", "bester"));
            var plan = _plan;
            var up = 0;
            _host.Store.Update(s =>
            {
                GoobyeState.RegalSetzen(s, nachher);
                GoobyeState.UmsatzVerbuchen(s, plan.Umsatz);
                GoobyeState.RundeVerbuchen(s, tag);
                GoobyeState.StatsVerbuchen(s, plan);
                if (plan.Umsatz > 0) Economy.Award(J.EnsureDict(s, "economy"), plan.Umsatz, "gooundbye");
                up = GoobyeState.LevelAufstieg(s);
            }, GameHost.TicksMs);
            _levelUp = up;
            _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            _host.Store.NotifySliceChanged("economy");
            _regal = nachher;
            _phase = Offen;
            _kasse = 0;
            _bonNext = 0;
            _bonStreak = 0;
            _spawnIn = 0.3f;
            _kasseFrei = true;
            _stage?.SetOpen(true);
            CloseSheet();
            RefreshBar();
            RefreshDayBar();
            RefreshHead();
            _coinsLabel.text = Ux.FormatNumber(J.Int(J.Path(State, "economy", "coins")) - plan.Umsatz);
            _ticker.text = T("laden.offen_toast");
            Sfx.PlayUi("soft_confirm");
            Debug.Log("[goobye] offen seed=" + seed + " kunden=" + plan.Kundenzahl + " umsatz=" + plan.Umsatz + " verkauft=" + plan.Verkauft.Values.Sum() + " verpasst=" + plan.Verpasst + " deal=" + deal);
        }

        private static readonly Dictionary<string, string>[][] Looks =
        {
            new[] { new Dictionary<string, string> { ["fell"] = "ash", ["hut"] = "hut_melone", ["brille"] = "readingGlasses", ["hals"] = "scarfRed" } },
            new[]
            {
                new Dictionary<string, string> { ["fell"] = "fell_minze", ["hut"] = "beanie", ["brille"] = "roundGlasses" },
                new Dictionary<string, string> { ["fell"] = "fell_himmel", ["hut"] = "cap", ["brille"] = "readingGlasses" },
            },
            new[]
            {
                new Dictionary<string, string> { ["fell"] = "rose", ["hut"] = "strawHat", ["hals"] = "hals_schleife" },
                new Dictionary<string, string> { ["fell"] = "fell_zuckerwatte", ["hut"] = "beret", ["hals"] = "pearlNecklace" },
            },
            new[]
            {
                new Dictionary<string, string> { ["fell"] = "golden", ["hut"] = "cap", ["hals"] = "bandana" },
                new Dictionary<string, string> { ["fell"] = "caramel", ["hut"] = "partyHat", ["hals"] = "bowtie" },
            },
        };

        private static Dictionary<string, string> LookFuer(string archetyp, int nr)
        {
            var i = archetyp == GoobyeMarkttag.ArchetypAlwin ? 0 : archetyp == GoobyeMarkttag.ArchetypListe ? 1 : archetyp == GoobyeMarkttag.ArchetypFamilie ? 2 : 3;
            return Looks[i][nr % Looks[i].Length];
        }

        private static Dictionary<string, string> KindLook(int nr) => nr % 2 == 0
            ? new Dictionary<string, string> { ["fell"] = "cream", ["ruecken"] = "balloonRed" }
            : new Dictionary<string, string> { ["fell"] = "fell_lavendel", ["hut"] = "partyHat" };

        private string KundenName(string archetyp) => T("laden.kunde_" + archetyp);

        /// <summary>A customer's whole visit as a walk script — picks in crate order, misses with a shrug, the till, the door.</summary>
        private void SpawnKunde(GoobyeMarkttag.Bon bon)
        {
            var k = new GoobyeStage.Kunde(bon.Archetyp, GoobyeStage.Door);
            var name = KundenName(bon.Archetyp);
            var sim = Kopie(_anzeige);
            var picks = new List<(int slot, string ware)>();
            foreach (var pos in bon.Positionen)
            {
                var idx = sim.Slots.FindIndex(s => s.Ware == pos.Ware && s.Menge > 0);
                if (idx < 0) idx = 0;
                sim.Entnehmen(pos.Ware);
                picks.Add((idx, pos.Ware));
            }
            var misses = bon.Verpasst.Where(w => w.Length > 0).Select(w => (slot: Math.Max(0, _anzeige.Slots.FindIndex(s => s.Ware == w)), ware: w)).ToList();
            k.Tu(() =>
            {
                _stage?.Ring();
                Sfx.Play("soft_tick", 7f);
                Bubble(k, T("laden.goo"), 1.2f);
                _host.Audio?.Say("Goo!", "happy");
                _ticker.text = T("laden.kunde_hinweis", ("name", name));
            });
            k.Geh(GoobyeStage.BackLane);
            var stops = picks.Select(p => p.slot).Concat(misses.Select(m => m.slot)).Distinct().OrderByDescending(s => s).ToList();
            if (stops.Count > 0) k.Geh(GoobyeStage.LaneAt(stops[0]));
            foreach (var stop in stops)
            {
                k.Geh(GoobyeStage.SlotBack(stop)).Dreh(180f);
                foreach (var p in picks.Where(p => p.slot == stop))
                {
                    var pick = p;
                    k.Warte(0.32f).Tu(() => Nehmen(pick.slot, pick.ware));
                }
                foreach (var m in misses.Where(m => m.slot == stop))
                {
                    var miss = m;
                    k.Clip("shake", "sad").Tu(() => Bubble(k, bon.Archetyp == GoobyeMarkttag.ArchetypAlwin ? T("laden.alwin_keine") : T("laden.liste_fehlt", ("name", WareName(miss.ware))), 1.8f)).Warte(0.9f);
                }
                k.Warte(0.15f);
            }
            if (stops.Count > 0) k.Geh(GoobyeStage.StandEnd);
            if (bon.Positionen.Count == 0)
            {
                k.Tu(() => _ticker.text = T("laden.kunde_leer", ("name", name)));
                k.Geh(GoobyeStage.QueueSpot).Geh(GoobyeStage.Aisle).Tu(() => { Bubble(k, T("laden.bye"), 1.0f); _host.Audio?.Say("Bye!", "sad"); })
                    .Geh(GoobyeStage.Door).Tu(() => { _stage?.Ring(); _spawnIn = 0f; });
                Add(k, bon);
                return;
            }
            k.Geh(GoobyeStage.QueueSpot).WarteBis(() => _kasseFrei).Tu(() => _kasseFrei = false).Geh(GoobyeStage.PaySpot).Dreh(160f).Tu(() =>
            {
                _spawnIn = 0.8f;
                _stage?.PutOnBelt(bon.Positionen.Select(p => p.Ware).ToList(), Look);
                _stage?.KeeperWave();
                _ticker.text = T("laden.kasse", ("name", name), ("n", bon.Positionen.Count));
                if (bon.Archetyp == GoobyeMarkttag.ArchetypAlwin) Bubble(k, T("laden.alwin_spruch"), 2.2f);
                else if (bon.Archetyp == GoobyeMarkttag.ArchetypHamster) Bubble(k, T("laden.hamster"), 2.0f);
            });
            for (var n = 0; n < bon.Positionen.Count; n++)
            {
                var nr = n;
                k.Warte(0.3f).Tu(() => Piepen(k, bon, nr));
            }
            k.Warte(0.25f).Tu(() => Bezahlen(k, bon)).Clip("wave", "happy").Warte(0.7f).Tu(() =>
            {
                _kasseFrei = true;
                Bubble(k, T("laden.bye"), 1.1f);
                _host.Audio?.Say("Bye!", "happy");
            });
            k.Geh(GoobyeStage.Aisle).Geh(GoobyeStage.Door).Tu(() => _stage?.Ring());
            Add(k, bon);
        }

        private void Add(GoobyeStage.Kunde k, GoobyeMarkttag.Bon bon)
        {
            _kunden.Add(k);
            _spawnIn = 99f;
            if (_stage != null) _ = _stage.AttachAsync(k, LookFuer(bon.Archetyp, bon.Kunde), bon.Archetyp == GoobyeMarkttag.ArchetypFamilie ? KindLook(bon.Kunde) : null);
        }

        private void Nehmen(int slot, string ware)
        {
            _anzeige.Entnehmen(ware);
            Sfx.Play("soft_tick", Semitone(GoobyeKatalog.TonFuer(ware)) + 3f);
            RefreshSlots();
            _juice.ScalePop(_chips[slot], 0.88f, 0.15f);
        }

        private void Piepen(GoobyeStage.Kunde k, GoobyeMarkttag.Bon bon, int nr)
        {
            var pos = bon.Positionen[nr];
            _kasse += pos.Preis;
            _kasseLabel.text = Ux.FormatNumber(_kasse);
            _kassePill.style.display = DisplayStyle.Flex;
            _stage?.Beep(nr);
            Sfx.Play("soft_chip", Semitone(GoobyeKatalog.TonFuer(pos.Ware)));
            _juice.ScalePop(_kassePill, 1.12f, 0.16f);
            var at = FieldPoint(GoobyeStage.RegisterPoint);
            _host.Ui.FloatText(at + new Vector2(UnityEngine.Random.Range(-14f, 14f), 0f), "+" + pos.Preis, new Color(1f, 0.82f, 0.25f), 26f);
            if (pos.Quengel) Bubble(k, T("laden.quengel"), 2.2f);
        }

        private void Bezahlen(GoobyeStage.Kunde k, GoobyeMarkttag.Bon bon)
        {
            _bonStreak++;
            Sfx.Play("game_coin", Sfx.ComboSemitones(_bonStreak));
            var at = FieldPoint(GoobyeStage.RegisterPoint);
            _host.Ui.FloatText(at + new Vector2(0f, -36f), "+" + bon.Summe + " ᴳ", new Color(1f, 0.78f, 0.2f), 36f);
            _juice.Burst(at, new Color(1f, 0.82f, 0.3f), 12);
            _stage?.KeeperCheer(bon.Summe >= 25);
            _stage?.ClearBelt();
            _ticker.text = T("laden.bon", ("name", KundenName(bon.Archetyp)), ("n", bon.Positionen.Count), ("summe", bon.Summe));
        }

        private void Bubble(GoobyeStage.Kunde k, string text, float seconds)
        {
            var p = FieldPoint(k.Kopf);
            var b = Ux.Text(text, "gb-goobye-bubble");
            b.pickingMode = PickingMode.Ignore;
            b.style.left = p.x;
            b.style.top = p.y;
            _bubbleLayer.Add(b);
            b.schedule.Execute(() => b.RemoveFromHierarchy()).StartingIn((long)(seconds * 1000f / Speed));
        }

        /// <summary>Skips the rest of the day straight to the till count (the day was booked when the door opened).</summary>
        private void Abrechnen()
        {
            if (_phase != Offen) return;
            foreach (var k in _kunden) _stage?.Remove(k);
            _kunden.Clear();
            _bonNext = _plan?.Bons.Count ?? 0;
            _kasse = _plan?.Umsatz ?? 0;
            _anzeige = Kopie(_regal);
            _stage?.ClearBelt();
            RefreshSlots();
            Sfx.PlayUi("soft_coins");
            TagEnde();
        }

        private void TagEnde()
        {
            if (_phase != Offen) return;
            _phase = Abschluss;
            _kasse = _plan?.Umsatz ?? _kasse;
            _anzeige = Kopie(_regal);
            _stage?.SetOpen(false);
            _stage?.ClearBelt();
            RefreshSlots();
            RefreshBar();
            ZeigeKassensturz();
        }

        // ── the till count ──

        private void ZeigeKassensturz()
        {
            var plan = _plan;
            var card = OpenCard("goobye-abschluss");
            card.Add(Ux.Text(T("abschluss.titel"), "gb-h1"));
            var sub = Ux.Text(T("abschluss.untertitel"), "gb-md", "gb-mild");
            sub.style.whiteSpace = WhiteSpace.Normal;
            card.Add(sub);
            var total = Ux.Row("gb-goobye-total");
            total.Add(Ux.Icon("coin"));
            var num = Ux.Text("0", "gb-goobye-total-text");
            num.name = "goobye-umsatz";
            total.Add(num);
            card.Add(Ux.Text(T("abschluss.umsatz"), "gb-sm", "gb-mild", "gb-goobye-center"));
            card.Add(total);
            if (plan != null && plan.Umsatz > _besterVorher && _besterVorher > 0)
                card.Add(Ux.Text("★ " + T("abschluss.rekord"), "gb-md", "gb-bold", "gb-goobye-rekord"));
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
            Row(T("abschluss.kunden"), (plan?.Bons.Count ?? 0).ToString(), "friends");
            Row(T("abschluss.artikel"), (plan?.Verkauft.Values.Sum() ?? 0).ToString(), "inventory");
            Row(T("abschluss.gewinn"), (_gewinn >= 0 ? "+" : "") + _gewinn + " ᴳ", "sparkle");
            card.Add(rows);
            if (plan != null && plan.Verpasst > 0)
            {
                var tipp = Ux.Text(T("abschluss.verpasst", ("anzahl", plan.Verpasst)), "gb-sm", "gb-mild");
                tipp.style.whiteSpace = WhiteSpace.Normal;
                card.Add(tipp);
            }
            var ziel = Ux.Text(ZielText(), "gb-sm", "gb-mild");
            ziel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(ziel);
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var done = Ux.IconTextBtn("check", T("abschluss.feierabend"), Feierabend, "gb-btn-primary");
            done.name = "goobye-feierabend";
            actions.Add(done);
            card.Add(actions);
            var umsatz = plan?.Umsatz ?? 0;
            _juice.CountTo(num, 0, umsatz, Reduced ? 0.2f : Mathf.Clamp(umsatz / 40f, 0.6f, 1.6f), v => { if (v > 0 && v % 5 == 0) Sfx.Play("soft_tick", Mathf.Min(10f, v / 6f)); });
            Sfx.PlayUi("soft_win");
            if (umsatz > 0) _host.Ui.Confetti(Reduced ? 0 : 50);
            _stage?.KeeperCheer(true);
            Debug.Log("[goobye] kassensturz umsatz=" + umsatz + " gewinn=" + _gewinn + " kunden=" + (plan?.Bons.Count ?? 0) + " level=" + GoobyeLevel.LevelFuer(State) + (_levelUp > 0 ? " AUFSTIEG " + _levelUp : ""));
        }

        private void Feierabend()
        {
            if (_phase != Abschluss) return;
            CloseOverlay();
            _phase = Einraeumen;
            var from = FieldPoint(GoobyeStage.RegisterPoint);
            var umsatz = _plan?.Umsatz ?? 0;
            Sfx.PlayUi("soft_coins");
            _kasse = 0;
            _plan = null;
            RefreshAll();
            if (umsatz > 0)
            {
                // the takings were booked when the door opened — the purse counts up as the coins land
                _coinsLabel.text = Ux.FormatNumber(J.Int(J.Path(State, "economy", "coins")) - umsatz);
                _host.Ui.FlyIcons(from, _coinsPill, "coin", Mathf.Clamp(umsatz / 8, 3, 12), _ =>
                {
                    _coinsLabel.text = Ux.FormatNumber(J.Int(J.Path(State, "economy", "coins")));
                    _juice.ScalePop(_coinsPill, 1.15f, 0.18f);
                });
            }
            if (_levelUp > 0)
            {
                var lvl = _levelUp;
                _levelUp = 0;
                Root.schedule.Execute(() =>
                {
                    _stage?.LevelUp(lvl);
                    _host.Ui.Confetti(Reduced ? 0 : 90);
                    Sfx.PlayUi("soft_levelup");
                    _host.Ui.Celebrate(_host.Strings.T(GoobyeLevel.NameKey(lvl)), T("abschluss.level_unter", ("name", _host.Strings.T(GoobyeLevel.NameKey(lvl)))), "shop");
                    Debug.Log("[goobye] aufstieg → level " + lvl);
                }).StartingIn(700);
            }
        }

        // ── the wholesale run ──

        private void GrossmarktTippen()
        {
            if (_phase != Einraeumen) return;
            var status = GoobyeTransport.StatusVon(State, Now);
            if (status == null) { Sfx.PlayUi("soft_open"); ZeigeGrossmarkt(); return; }
            if (status.Phase == GoobyeTransport.PhaseDa) { Ausladen(); return; }
            Sfx.PlayUi("soft_tap");
            _host.Ui.Toast(T("grossmarkt.status_" + status.Phase, ("rest", RestText(status.RestMs))), "car");
        }

        private static string RestText(long ms)
        {
            var s = (int)Math.Ceiling(Math.Max(0, ms) / 1000.0);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        private Label _korbLabel, _kofferLabel, _kuehlLabel;
        private readonly Dictionary<string, Label> _korbZahlen = new Dictionary<string, Label>();
        private Button _losBtn, _eilBtn;

        private void ZeigeGrossmarkt()
        {
            _korb.Clear();
            _korbZahlen.Clear();
            var body = OpenSheet("car", new Color(0.66f, 0.86f, 0.84f), T("grossmarkt.titel"), T("grossmarkt.hinweis"), out var footer);
            var info = Ux.Col("gb-goobye-info");
            _kofferLabel = Ux.Text("", "gb-md", "gb-bold");
            _kofferLabel.name = "goobye-kofferraum";
            info.Add(_kofferLabel);
            var kuehl = Ux.Row("gb-goobye-inforow");
            _kuehlLabel = Ux.Text("", "gb-md");
            _kuehlLabel.style.flexGrow = 1;
            kuehl.Add(Ux.Icon("snow"));
            kuehl.Add(_kuehlLabel);
            var modul = Ux.IconTextBtn("plus", T("kuehl.modul_kaufen", ("preis", GoobyeKuehl.ModulPreis)), KuehlmodulKaufen, "gb-btn-secondary", "gb-btn-sm");
            modul.name = "goobye-kuehlmodul";
            kuehl.Add(modul);
            info.Add(kuehl);
            if (!GoobyeTransport.LieferwagenFrei(State))
            {
                var van = Ux.Row("gb-goobye-inforow");
                var vl = Ux.Text(T("grossmarkt.lieferwagen_zeile", ("kisten", GoobyeTransport.KistenLieferwagen)), "gb-md");
                vl.style.flexGrow = 1;
                vl.style.whiteSpace = WhiteSpace.Normal;
                van.Add(Ux.Icon("car"));
                van.Add(vl);
                var level = GoobyeLevel.LevelFuer(State);
                if (level >= GoobyeTransport.LieferwagenLevel)
                {
                    var kaufen = Ux.IconTextBtn("coin", T("grossmarkt.lieferwagen_kaufen", ("preis", GoobyeTransport.LieferwagenPreis)), LieferwagenKaufen, "gb-btn-confirm", "gb-btn-sm");
                    kaufen.name = "goobye-lieferwagen";
                    van.Add(kaufen);
                }
                else van.Add(Ux.Text(T("grossmarkt.lieferwagen_gate", ("ziel", GoobyeTransport.LieferwagenLevel), ("level", level), ("name", _host.Strings.T(GoobyeLevel.NameKey(level)))), "gb-xs", "gb-mild"));
                info.Add(van);
            }
            body.Add(info);
            foreach (var g in GoobyeKatalog.Gruppen())
            {
                var gid = J.Str(g["id"]);
                var head = Ux.Row("gb-goobye-grouphead");
                var glyph = Ux.Text(Glyph(J.Str(g["form"])), "gb-goobye-glyph");
                glyph.style.color = Farbe(J.Str(g["farbe"]));
                head.Add(glyph);
                head.Add(Ux.Text(GruppeName(gid), "gb-md", "gb-bold"));
                body.Add(head);
                var grid = Ux.Row("gb-goobye-ordergrid");
                foreach (var w in GoobyeKatalog.WarenDerGruppe(gid))
                {
                    var id = J.Str(w["id"]);
                    var row = Ux.Row("gb-goobye-orderrow");
                    row.name = "goobye-order-" + id;
                    var icon = CounterPanel.FoodIcon(id, null);
                    VisualElement thumb = icon != null ? Ux.Icon(icon) : (VisualElement)Ux.Text(Glyph(J.Str(g["form"])), "gb-goobye-glyph");
                    if (icon == null) thumb.style.color = Farbe(J.Str(g["farbe"]));
                    row.Add(thumb);
                    var col = Ux.Col();
                    col.style.flexGrow = 1;
                    col.Add(Ux.Text(WareName(id), "gb-sm", "gb-bold"));
                    _lager.TryGetValue(id, out var da);
                    col.Add(Ux.Text(T("grossmarkt.preiszeile", ("ek", GoobyePreis.Einkaufspreis(w)), ("vk", GoobyePreis.Verkaufspreis(w)), ("lager", da)), "gb-xs", "gb-mild"));
                    row.Add(col);
                    var minus = Ux.Btn("−", () => KorbAendern(id, -1), "gb-btn-secondary", "gb-goobye-step");
                    minus.name = "goobye-minus-" + id;
                    row.Add(minus);
                    var zahl = Ux.Text("0", "gb-goobye-stepnum");
                    _korbZahlen[id] = zahl;
                    row.Add(zahl);
                    var plus = Ux.Btn("+", () => KorbAendern(id, 1), "gb-btn-secondary", "gb-goobye-step");
                    plus.name = "goobye-plus-" + id;
                    row.Add(plus);
                    grid.Add(row);
                }
                body.Add(grid);
            }
            _korbLabel = Ux.Text("", "gb-md", "gb-bold");
            _korbLabel.style.flexGrow = 1;
            _korbLabel.style.alignSelf = Align.Center;
            footer.Add(_korbLabel);
            _eilBtn = Ux.IconTextBtn("takeaway_white", "", EilLieferung, "gb-btn-secondary");
            _eilBtn.name = "goobye-eil";
            _eilBtn.style.marginRight = 10;
            footer.Add(_eilBtn);
            _losBtn = Ux.IconTextBtn("car", T("grossmarkt.losfahren"), Losfahren, "gb-btn-primary");
            _losBtn.name = "goobye-losfahren";
            footer.Add(_losBtn);
            KorbZeilen();
        }

        private void KorbAendern(string id, int richtung)
        {
            if (GoobyeKatalog.Ware(id) == null) return;
            _korb.TryGetValue(id, out var n);
            if (richtung > 0)
            {
                if (GoobyeTransport.KistenImKorb(_korb) >= GoobyeTransport.KapazitaetFuer(State))
                {
                    Sfx.PlayUi("soft_error");
                    _host.Ui.Toast(T("grossmarkt.kofferraum_voll"), "car");
                    return;
                }
                _korb[id] = n + 1;
                Sfx.Play("soft_chip", Semitone(GoobyeKatalog.TonFuer(id)));
            }
            else
            {
                if (n <= 0) return;
                if (n == 1) _korb.Remove(id); else _korb[id] = n - 1;
                Sfx.PlayUi("soft_back");
            }
            KorbZeilen();
        }

        private void KorbZeilen()
        {
            var kisten = GoobyeTransport.KistenImKorb(_korb);
            var cap = GoobyeTransport.KapazitaetFuer(State);
            var auto = GoobyeTransport.LieferwagenFrei(State) ? T("grossmarkt.lieferwagen") : AutoName();
            if (_kofferLabel != null) _kofferLabel.text = T("grossmarkt.kofferraum", ("kisten", kisten), ("kapazitaet", cap), ("auto", auto));
            if (_kuehlLabel != null) _kuehlLabel.text = T("kuehl.zeile", ("belegt", GoobyeKuehl.KuehlStueck(_regal)), ("kapazitaet", GoobyeKuehl.Kapazitaet(GoobyeKuehl.ModuleVon(State))));
            foreach (var kv in _korbZahlen) kv.Value.text = _korb.TryGetValue(kv.Key, out var n) ? n.ToString() : "0";
            var kosten = GoobyeTransport.Kosten(_korb);
            var eil = _korb.Sum(kv => GoobyeTransport.EilPreis(GoobyeKatalog.Ware(kv.Key)) * kv.Value);
            if (_korbLabel != null) _korbLabel.text = T("grossmarkt.kosten", ("kosten", kosten), ("kisten", kisten));
            if (_eilBtn != null)
            {
                _eilBtn.Q<Label>().text = T("grossmarkt.eil", ("preis", eil));
                _eilBtn.SetEnabled(kisten > 0);
            }
            _losBtn?.SetEnabled(kisten > 0);
        }

        private string AutoName()
        {
            var id = GoobyeTransport.AktivesAutoId(State);
            var auto = Gooby.Core.City.AutoKatalog.Get(id);
            return auto?.NameDe ?? id;
        }

        private void Losfahren()
        {
            var korb = new Dictionary<string, int>(_korb);
            var result = GoobyeTransport.Pleite;
            var now = Now;
            _host.Store.Update(s => result = GoobyeTransport.Bestelle(s, korb, now), GameHost.TicksMs);
            switch (result)
            {
                case GoobyeTransport.Ok:
                    _host.Store.NotifySliceChanged(GoobyeState.SliceId);
                    _host.Store.NotifySliceChanged("economy");
                    Sfx.PlayUi("soft_buy");
                    _host.Ui.Toast(T("grossmarkt.bestellt_toast"), "car");
                    CloseSheet();
                    _lieferungGezeigt = 0;
                    RefreshAll();
                    Debug.Log("[goobye] grossmarkt bestellt " + string.Join(",", korb.Select(kv => kv.Key + "×" + kv.Value)) + " kosten " + GoobyeTransport.Kosten(korb));
                    break;
                case GoobyeTransport.Leer: Fehler("grossmarkt.korb_leer"); break;
                case GoobyeTransport.ZuViel: Fehler("grossmarkt.kofferraum_voll"); break;
                case GoobyeTransport.Unterwegs: Fehler("grossmarkt.schon_unterwegs"); break;
                default: Fehler("grossmarkt.zu_teuer"); break;
            }
        }

        private void EilLieferung()
        {
            var korb = new Dictionary<string, int>(_korb);
            if (korb.Count == 0) { Fehler("grossmarkt.korb_leer"); return; }
            var preis = korb.Sum(kv => GoobyeTransport.EilPreis(GoobyeKatalog.Ware(kv.Key)) * kv.Value);
            if (J.Int(J.Path(State, "economy", "coins")) < preis) { Fehler("grossmarkt.zu_teuer"); return; }
            var ok = true;
            _host.Store.Update(s =>
            {
                foreach (var kv in korb)
                    if (GoobyeTransport.EilKauf(s, kv.Key, kv.Value) != GoobyeTransport.Ok) ok = false;
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            _host.Store.NotifySliceChanged("economy");
            _lager = GoobyeState.LagerVon(State);
            Sfx.PlayUi(ok ? "soft_buy" : "soft_error");
            _host.Ui.Toast(T("grossmarkt.eil_da", ("kisten", GoobyeTransport.KistenImKorb(korb))), "takeaway_white");
            CloseSheet();
            RefreshAll();
            Debug.Log("[goobye] eil-lieferung " + string.Join(",", korb.Select(kv => kv.Key + "×" + kv.Value)) + " preis " + preis);
        }

        private void Ausladen()
        {
            (bool ok, string grund, int kisten, Dictionary<string, int> warenkorb) res = (false, "", 0, null);
            var now = Now;
            _host.Store.Update(s => res = GoobyeTransport.Ausladen(s, now), GameHost.TicksMs);
            if (!res.ok) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            _lager = GoobyeState.LagerVon(State);
            var toene = res.warenkorb.SelectMany(kv => Enumerable.Repeat(GoobyeKatalog.TonFuer(kv.Key), kv.Value)).Take(6).ToList();
            for (var i = 0; i < toene.Count; i++)
            {
                var ton = toene[i];
                Root.schedule.Execute(() => Sfx.Play("soft_chip", Semitone(ton) - 5f)).StartingIn(i * 110);
            }
            _stage?.SetDelivery(0);
            _lieferungGezeigt = 0;
            _host.Ui.FlyIcons(FieldPoint(GoobyeStage.DeliveryPoint), _lagerLabel.parent, "inventory", Mathf.Clamp(res.kisten, 2, 8), _ => _juice.ScalePop(_lagerLabel.parent, 1.15f, 0.16f));
            _host.Ui.Toast(T("grossmarkt.ausgeladen", ("kisten", res.kisten)), "inventory");
            RefreshAll();
            Debug.Log("[goobye] ausgeladen " + res.kisten + " kisten → lager " + _lager.Values.Sum());
        }

        private void KuehlmodulKaufen()
        {
            var result = GoobyeKuehl.Pleite;
            _host.Store.Update(s => result = GoobyeKuehl.KaufeModul(s), GameHost.TicksMs);
            if (result != GoobyeKuehl.Ok) { Fehler("kuehl.modul_zu_teuer"); return; }
            _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            _host.Ui.Toast(T("kuehl.modul_gekauft", ("je", GoobyeKuehl.StueckJeModul)), "snow");
            KorbZeilen();
            RefreshHead();
        }

        private void LieferwagenKaufen()
        {
            var result = GoobyeTransport.Pleite;
            _host.Store.Update(s => result = GoobyeTransport.KaufeLieferwagen(s), GameHost.TicksMs);
            if (result != GoobyeTransport.Ok)
            {
                Fehler(result == GoobyeTransport.Gesperrt ? "grossmarkt.lieferwagen_gesperrt" : "grossmarkt.zu_teuer", ("ziel", GoobyeTransport.LieferwagenLevel));
                return;
            }
            _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            CloseSheet();
            RefreshHead();
            ZeigeStoryKarte("uebergabe", "car");
            Debug.Log("[goobye] lieferwagen gekauft");
        }

        private void Fehler(string key, params (string, object)[] args)
        {
            Sfx.PlayUi("soft_error");
            _host.Ui.Toast(T(key, args), "close");
        }

        // ── the daily deal ──

        private void ZeigeAngebot()
        {
            if (_phase != Einraeumen) return;
            Sfx.PlayUi("soft_open");
            var aktiv = GoobyeAngebot.AktiveGruppe(State, Today);
            var body = OpenSheet("sign", new Color(1f, 0.85f, 0.66f), T("tagesangebot.titel"), T("tagesangebot.hinweis"), out _);
            if (aktiv.Length > 0) body.Add(Ux.Text(T("tagesangebot.aktiv", ("name", GruppeName(aktiv))), "gb-md", "gb-bold"));
            var grid = Ux.Row("gb-goobye-dealgrid");
            foreach (var g in GoobyeKatalog.Gruppen())
            {
                var gid = J.Str(g["id"]);
                var card = Ux.Col("gb-goobye-deal");
                card.name = "goobye-deal-" + gid;
                card.EnableInClassList("gb-goobye-deal--on", gid == aktiv);
                card.EnableInClassList("gb-goobye-deal--off", aktiv.Length > 0 && gid != aktiv);
                var glyph = Ux.Text(Glyph(J.Str(g["form"])), "gb-goobye-glyph", "gb-goobye-glyph--big");
                glyph.style.color = Farbe(J.Str(g["farbe"]));
                card.Add(glyph);
                card.Add(Ux.Text(GruppeName(gid), "gb-md", "gb-bold"));
                card.Add(Ux.Text(string.Join(", ", GoobyeKatalog.WarenDerGruppe(gid).Take(3).Select(w => WareName(J.Str(w["id"])))), "gb-xs", "gb-mild"));
                card.RegisterCallback<ClickEvent>(_ => AngebotWaehlen(gid));
                grid.Add(card);
            }
            body.Add(grid);
        }

        private void AngebotWaehlen(string gid)
        {
            var result = GoobyeAngebot.Unbekannt;
            var tag = Today;
            _host.Store.Update(s => result = GoobyeAngebot.Waehle(s, gid, tag), GameHost.TicksMs);
            if (result == GoobyeAngebot.Ok)
            {
                _host.Store.NotifySliceChanged(GoobyeState.SliceId);
                Sfx.PlayUi("soft_confirm");
                _host.Ui.Toast(T("tagesangebot.gewaehlt", ("name", GruppeName(gid))), "sign");
                CloseSheet();
                RefreshAll();
                _juice.ScalePop(_sign, 1.25f, 0.3f);
                Debug.Log("[goobye] tagesangebot " + gid);
                return;
            }
            Fehler(result == GoobyeAngebot.SchonGewaehlt ? "tagesangebot.schon_gewaehlt" : "tagesangebot.titel");
        }

        // ── story cards: the offer, the key handover, the van ──

        private void ZeigeKaufangebot()
        {
            var card = OpenCard("goobye-angebot");
            card.Add(Ux.Text(T("angebot.titel"), "gb-h1"));
            var text = Ux.Text(T("angebot.text"), "gb-md");
            text.style.whiteSpace = WhiteSpace.Normal;
            card.Add(text);
            card.Add(Ux.Text(T("angebot.preis", ("preis", GoobyeKatalog.Preis())), "gb-h2", "gb-goobye-center"));
            var check = GoobyeKauf.Check(State);
            var hint = Ux.Text(check == GoobyeKauf.Gesperrt
                ? _host.Strings.T("dlc.gesperrt_hinweis", ("aktuell", J.Int(J.Path(State, "progression", "level"), 1)))
                : check == GoobyeKauf.Pleite ? T("angebot.zu_wenig", ("preis", GoobyeKatalog.Preis())) : T("angebot.frage"), "gb-sm", "gb-mild", "gb-goobye-center");
            hint.name = "goobye-kaufhinweis";
            hint.style.whiteSpace = WhiteSpace.Normal;
            card.Add(hint);
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var later = Ux.Btn(T("angebot.spaeter"), () =>
            {
                _host.Store.Update(s => GoobyeState.AngebotVerschieben(s), GameHost.TicksMs);
                Quit();
            }, "gb-btn-secondary");
            later.style.marginRight = 10;
            actions.Add(later);
            var buy = Ux.IconTextBtn("coin", T("angebot.kaufen"), Kaufen, check == GoobyeKauf.Ok ? "gb-btn-confirm" : "gb-btn-secondary");
            buy.name = "goobye-kaufen";
            actions.Add(buy);
            card.Add(actions);
        }

        private void Kaufen()
        {
            var result = GoobyeKauf.Pleite;
            var now = Now;
            _host.Store.Update(s => result = GoobyeKauf.Kaufe(s, now), GameHost.TicksMs);
            if (result != GoobyeKauf.Ok)
            {
                Sfx.PlayUi("soft_error");
                var hint = Root.Q<Label>("goobye-kaufhinweis");
                if (hint != null) hint.text = result == GoobyeKauf.Gesperrt
                    ? _host.Strings.T("dlc.gesperrt_hinweis", ("aktuell", J.Int(J.Path(State, "progression", "level"), 1)))
                    : T("angebot.zu_wenig", ("preis", GoobyeKatalog.Preis()));
                return;
            }
            _host.Store.NotifySliceChanged(GoobyeState.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            _gekauft = true;
            _lager = GoobyeState.LagerVon(State);
            CloseOverlay();
            RefreshAll();
            Debug.Log("[goobye] gekauft");
            if (ErstbesuchMerken()) ZeigeIntro();
        }

        private void ZeigeIntro()
        {
            var alwin = new GoobyeStage.Kunde(GoobyeMarkttag.ArchetypAlwin, GoobyeStage.PaySpot);
            alwin.Dreh(165f).Clip("wave", "happy").WarteBis(() => _overlay == null).Tu(() => { Bubble(alwin, T("laden.bye"), 1.2f); _host.Audio?.Say("Bye!", "happy"); })
                .Geh(GoobyeStage.Aisle).Geh(GoobyeStage.Door).Tu(() => _stage?.Ring());
            _kunden.Add(alwin);
            if (_stage != null) _ = _stage.AttachAsync(alwin, LookFuer(GoobyeMarkttag.ArchetypAlwin, 0), null);
            ZeigeStoryKarte("intro", "shop");
        }

        private void ZeigeStoryKarte(string block, string icon)
        {
            var card = OpenCard("goobye-" + block, links: true);
            var head = Ux.Row();
            head.Add(Ux.IconTile(icon, new Color(0.66f, 0.86f, 0.84f)));
            var title = Ux.Text(T(block + ".titel"), "gb-h1");
            title.style.marginLeft = 12;
            head.Add(title);
            card.Add(head);
            foreach (var key in new[] { "zeile1", "zeile2", "zeile3" })
            {
                var l = Ux.Text(T(block + "." + key), "gb-md");
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.marginTop = 8;
                card.Add(l);
            }
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var ok = Ux.IconTextBtn("check", T(block + ".knopf"), () => { Sfx.PlayUi("soft_confirm"); CloseOverlay(); RefreshAll(); }, "gb-btn-primary");
            ok.name = "goobye-story-ok";
            actions.Add(ok);
            card.Add(actions);
            Sfx.PlayUi("soft_toast");
        }

        // ── overlays ──

        /// <summary>A modal card; <paramref name="links"/> puts it on the left so the till (Alwin, the van) stays in view.</summary>
        private VisualElement OpenCard(string name, bool links = false)
        {
            CloseOverlay();
            _overlay = Ux.El("gb-scrim", "gb-goobye-veil");
            _overlay.EnableInClassList("gb-goobye-veil--links", links);
            _overlay.name = name + "-veil";
            var card = Ux.Col("gb-card", "gb-goobye-card");
            card.name = name;
            _overlay.Add(card);
            Root.Add(_overlay);
            _juice.ScalePop(card, 1.06f, 0.22f);
            return card;
        }

        private VisualElement OpenSheet(string icon, Color tint, string title, string sub, out VisualElement footer)
        {
            CloseOverlay();
            _overlay = Ux.El("gb-scrim", "gb-goobye-veil");
            _overlay.name = "goobye-sheet-veil";
            _overlay.RegisterCallback<ClickEvent>(e => { if (e.target == _overlay) CloseSheet(); });
            var sheet = Ux.Col("gb-card", "gb-goobye-sheet");
            sheet.name = "goobye-sheet";
            var head = CounterPanel.Head(icon, tint, title, sub, CloseSheet);
            sheet.Add(head.Row);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-goobye-sheet-scroll");
            var body = Ux.Col("gb-goobye-sheet-body");
            scroll.Add(body);
            sheet.Add(scroll);
            footer = Ux.Row("gb-goobye-sheet-foot");
            sheet.Add(footer);
            _overlay.Add(sheet);
            Root.Add(_overlay);
            return body;
        }

        private void CloseSheet()
        {
            if (_overlay == null) return;
            Sfx.PlayUi("soft_close");
            CloseOverlay();
        }

        private void CloseOverlay()
        {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            _korbLabel = _kofferLabel = _kuehlLabel = null;
            _losBtn = _eilBtn = null;
            _korbZahlen.Clear();
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

        // ── QA ──

        /// <summary>
        /// "goobye kaufen | fuellen | slot n | einraeumen n ware | oeffnen | schnell | abrechnen | feierabend | grossmarkt |
        /// korb ware n | losfahren | eil | ankunft | ausladen | angebot gruppe | modul | lieferwagen | ok | zu"
        /// </summary>
        public void Qa(string[] raw)
        {
            // the driver splits into at most three parts ("goobye", sub, rest): split the rest again
            var parts = string.Join(" ", raw).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var a1 = parts.Length > 1 ? parts[1] : "";
            switch (a1)
            {
                case "kaufen": Kaufen(); break;
                case "fuellen": AllesAuffuellen(); break;
                case "slot": ZeigeSlotSheet(int.Parse(parts[2])); break;
                case "einraeumen": if (InsRegal(int.Parse(parts[2]), parts[3]) > 0) Speichern(); break;
                case "oeffnen": LadenOeffnen(); break;
                case "schnell": _schnell = true; RefreshDayBar(); break;
                case "abrechnen": Abrechnen(); break;
                case "feierabend": Feierabend(); break;
                case "grossmarkt": ZeigeGrossmarkt(); break;
                case "korb":
                    for (var i = 0; i < (parts.Length > 3 ? int.Parse(parts[3]) : 1); i++) KorbAendern(parts[2], 1);
                    break;
                case "losfahren": Losfahren(); break;
                case "eil": EilLieferung(); break;
                case "ankunft":
                    var now = Now;
                    _host.Store.Update(s =>
                    {
                        if (J.Path(s, GoobyeState.SliceId, GoobyeState.Key, "transport", "unterwegs") is JObject u && u.HasValues)
                        {
                            u["bestelltAt"] = now - GoobyeTransport.FahrzeitMs(1);
                            u["ankunftAt"] = now - 1;
                        }
                    }, GameHost.TicksMs);
                    break;
                case "ausladen": Ausladen(); break;
                case "angebot": ZeigeAngebot(); if (parts.Length > 2) AngebotWaehlen(parts[2]); break;
                case "modul": KuehlmodulKaufen(); break;
                case "lieferwagen": LieferwagenKaufen(); break;
                case "ok": CloseOverlay(); RefreshAll(); break;
                case "zu": CloseOverlay(); break;
                default: Debug.LogWarning("[qa] goobye " + a1 + ": unknown"); break;
            }
            Debug.Log("[qa] goobye " + a1 + " → phase " + _phase + " regal " + _regal.GesamtBestand() + " lager " + _lager.Values.Sum() + " coins " + J.Int(J.Path(State, "economy", "coins")));
        }
    }
}
