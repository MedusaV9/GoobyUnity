using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
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
    /// The riding village Hufingen (port of hufingen_szene.gd and dorf_laden.gd): you ride in from the ranch on the
    /// selected horse (the first time the long way with the camera alongside — then Hufingen counts as discovered), the
    /// square in 3D behind, name tags over the five shops and a shop dock in the thumb zone. Each shop opens as a sheet
    /// with its keeper's greeting: Wilma's three horses of the day (buy into a free box — the horse walks out of the
    /// corral to yours; sell your own for half), the feed yard (hay, oats, treats, buying the harvest back), the saddlery
    /// (gear for your horse), the smithy (horseshoes) and the furniture barn (pieces into the home storage). The fountain
    /// takes a wish.
    /// </summary>
    public sealed partial class RanchDorfScreen : UiScreen
    {
        private static readonly (string id, string icon)[] LadenIcons =
        {
            (RanchDorf.Pferdehaendlerin, "horseshoe"), (RanchDorf.Futterhof, "hay"), (RanchDorf.Reitladen, "shirt"),
            (RanchDorf.Schmiede, "hammer"), (RanchDorf.Moebelscheune, "sofa"),
        };

        private readonly GameHost _host;
        private readonly string _pferdId;
        private readonly Juice _juice = new Juice();
        private RanchDorfStage _stage;
        private RanchWetterView _wetter;
        private VisualElement _field, _tags, _overlay, _bubble;
        private Label _coinsLabel, _hayLabel, _oatLabel, _treatLabel;
        private VisualElement _hayPill, _oatPill, _treatPill, _coinPill;
        private readonly Dictionary<string, VisualElement> _tagEls = new Dictionary<string, VisualElement>();
        private bool _night, _arrived;
        private string _verkaufSicher;

        public string Laden { get; private set; } = "";

        public RanchDorfScreen(GameHost host, string pferdId)
        {
            _host = host;
            _pferdId = pferdId;
            Id = "ranchdorf";
            Opaque = true;
        }

        public override string SoundScene => _wetter?.Scene ?? "ranch/dorf";

        /// <summary>Puddles on the square, the road in and before the corral (off the riding line and the fountain).</summary>
        private static readonly Vector3[] Pfuetzen =
        {
            new Vector3(2.8f, 0f, 2.2f), new Vector3(5.6f, 0f, 5.0f), new Vector3(-6.0f, 0f, 6.2f), new Vector3(-1.4f, 0f, 4.0f),
            new Vector3(3.6f, 0f, 9.8f), new Vector3(-10.5f, 0f, 0.5f), new Vector3(-17f, 0f, -0.6f), new Vector3(1.2f, 0f, -1.0f),
        };

        private JObject State => _host.Store.State;
        private long Now => _host.Store.Clock.NowMs();
        private string Today => _host.Store.Clock.LocalDay(Now);
        private string T(string key, params (string, object)[] args) => _host.Strings.T(key, args);

        // ── build ──

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-ranchhof");
            root.AddToClassList("gb-ranchdorf");
            _field = new VisualElement { name = "ranchdorf-field" };
            Ux.Abs(_field, 0, 0, 0, 0);
            _field.RegisterCallback<PointerDownEvent>(e => FeldTippen(e.localPosition));
            root.Add(_field);
            _night = RanchOffline.IstNacht(RanchOffline.StundeLokal(Now, _host.Store.Clock.UtcOffsetMinutes(Now)));
            var lang = !RanchDorf.IstEntdeckt(State);
            if (MgStage3D.Supported)
            {
                var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(State["cosmetics"]));
                _stage = new RanchDorfStage(_field, outfit, _night);
                _stage.Arrived += Angekommen;
            }

            _tags = new VisualElement { name = "ranchdorf-tags", pickingMode = PickingMode.Ignore };
            Ux.Abs(_tags, 0, 0, 0, 0);
            root.Add(_tags);
            foreach (var (id, icon) in LadenIcons)
            {
                var lid = id;
                var tag = new Button(() => ZeigeLaden(lid)) { name = "ranchdorf-tag-" + id };
                tag.ClearClassList();
                tag.AddToClassList("gb-ranchdorf-tag");
                tag.style.backgroundColor = ArtLibrary.Hex(RanchDorfStage.ShopColor[id]);
                var ic = Ux.Icon(icon);
                ic.pickingMode = PickingMode.Ignore;
                tag.Add(ic);
                var l = Ux.Text(T("rdorf.laden." + id), "gb-ranchdorf-tag-text");
                l.pickingMode = PickingMode.Ignore;
                tag.Add(l);
                tag.style.visibility = Visibility.Hidden;
                _tags.Add(tag);
                _tagEls[id] = tag;
            }

            var back = Ux.IconBtn("back_white", Quit, "gb-place-back");
            back.name = "ranchdorf-quit";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.Row("gb-card", "gb-ranchhof-head");
            head.pickingMode = PickingMode.Ignore;
            Ux.Abs(head, 84, 12);
            head.Add(Ux.IconTile("horseshoe", new Color(0.98f, 0.8f, 0.6f)));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(T("rdorf.ortsname"), "gb-place-title"));
            hcol.Add(Ux.Text(T("rdorf.ortsschild"), "gb-sm", "gb-mild"));
            head.Add(hcol);
            var wetterStage = _stage != null ? new StageWeather(_stage.Stage, new Vector3(-4f, 0f, 5f), new Vector3(18f, 1f, 11f), 1.3f, Pfuetzen) : null;
            _wetter = new RanchWetterView(_host, "hufingen", "dorf", wetterStage, _field);
            _wetter.Chip.style.marginLeft = 14;
            head.pickingMode = PickingMode.Position;
            head.Add(_wetter.Chip);
            root.Add(head);

            var pills = Ux.Row("gb-ranchhof-pills");
            Ux.Abs(pills, null, 14, 16);
            _hayPill = Ux.Pill("hay", "0", out _hayLabel);
            _hayPill.name = "ranchdorf-heu";
            pills.Add(_hayPill);
            _oatPill = Ux.Pill("sprout", "0", out _oatLabel);
            _oatPill.name = "ranchdorf-hafer";
            pills.Add(_oatPill);
            _treatPill = Ux.Pill("candy", "0", out _treatLabel);
            _treatPill.name = "ranchdorf-leckerli";
            pills.Add(_treatPill);
            _coinPill = Ux.Pill("coin", "0", out _coinsLabel);
            _coinPill.name = "ranchdorf-coins";
            pills.Add(_coinPill);
            root.Add(pills);

            var dock = Ux.Row("gb-card", "gb-ranchdorf-dock");
            dock.name = "ranchdorf-dock";
            foreach (var (id, icon) in LadenIcons)
            {
                var lid = id;
                var btn = new Button(() => ZeigeLaden(lid)) { name = "ranchdorf-laden-" + id };
                btn.ClearClassList();
                btn.AddToClassList("gb-ranchhof-action");
                var disc = Ux.El("gb-ranchhof-action-disc");
                disc.pickingMode = PickingMode.Ignore;
                disc.style.borderTopColor = disc.style.borderBottomColor = disc.style.borderLeftColor = disc.style.borderRightColor = ArtLibrary.Hex(RanchDorfStage.ShopColor[id]);
                var ic = Ux.Icon(icon);
                ic.pickingMode = PickingMode.Ignore;
                disc.Add(ic);
                btn.Add(disc);
                var label = Ux.Text(T("rdorf.kurz." + id), "gb-ranchhof-action-label");
                label.pickingMode = PickingMode.Ignore;
                btn.Add(label);
                dock.Add(btn);
            }
            root.Add(dock);
            root.Add(_juice.Layer);

            RefreshPills();
            SetOffer();
            var pferd = RanchHof.Pferd(State, _pferdId);
            var (fell, maehne) = Fell(pferd);
            if (_stage != null)
            {
                _stage.RideIn(_pferdId ?? "", fell, maehne, lang);
                RanchPferdLook.Kleide(_stage.RideView, State, _pferdId);
                if (lang) Sag(T("rdorf.reitet_ein", ("name", PferdName(_pferdId))));
            }
            else root.schedule.Execute(Angekommen).StartingIn(200);
            Debug.Log("[ranchdorf] auf: pferd=" + _pferdId + " entdeckt=" + !lang + " nacht=" + _night + " angebot=" + string.Join(",", DorfHaendler.Angebot(State, Today).Select(p => p.Id)));
        }

        private void Angekommen()
        {
            if (_arrived) return;
            _arrived = true;
            RanchQuestUi.Melde(_host, "gehe_zu", "markt");
            var neu = false;
            var now = Now;
            _host.Store.Update(s => neu = RanchDorf.Entdecke(s, now), GameHost.TicksMs);
            if (neu)
            {
                _host.Store.NotifySliceChanged(RanchHof.SliceId);
                _juice.Confetti(70);
                Sfx.PlayUi("soft_levelup");
                _host.Ui.Toast(T("rdorf.entdeckt"), "map");
                Sag(T("rdorf.ankunft_neu"));
            }
            else Sag(T("rdorf.ankunft"));
            Sfx.Play("ranch_schnauben_a", 0f, 0.7f);
            List<string> fertig = null;
            var tag = Today;
            _host.Store.Update(s => fertig = RanchTage.Melde(s, tag, "dorf"), GameHost.TicksMs);
            foreach (var id in fertig)
            {
                var text = T("rhof.tage.text." + id);
                Root.schedule.Execute(() => { Sfx.PlayUi("soft_levelup"); _host.Ui.Toast(T("rhof.tage.geschafft", ("text", text)), "star"); }).StartingIn(2200);
            }
            Debug.Log("[ranchdorf] angekommen neu=" + neu + (fertig.Count > 0 ? " aufgabe=" + string.Join(",", fertig) : ""));
        }

        // ── helpers ──

        private string PferdName(string id)
        {
            var p = RanchHof.Pferd(State, id);
            if (p == null) return "";
            var key = J.Str(p["nameKey"]);
            return key.Length > 0 ? T(key) : J.Str(p["name"]);
        }

        private static (Color fell, Color maehne) Fell(JObject p) => RanchPferdLook.Fell(p);

        private void SetOffer()
        {
            _stage?.SetOffer(DorfHaendler.Angebot(State, Today).Select(p => (p.Id, ArtLibrary.Hex(p.Farbe), ArtLibrary.Hex(p.Maehne))).ToList());
        }

        private void RefreshPills()
        {
            var lager = RanchHof.Lager(State);
            _hayLabel.text = Ux.FormatNumber(J.Int(lager["heu"]));
            _oatLabel.text = Ux.FormatNumber(RanchDorf.Futter(State, "hafer"));
            _treatLabel.text = Ux.FormatNumber(RanchDorf.Futter(State, "leckerli"));
            _coinsLabel.text = Ux.FormatNumber(J.Int(J.Path(State, "economy", "coins")));
        }

        private Vector2 FieldPoint(Vector3 local)
        {
            var w = _field.layout.width;
            var h = _field.layout.height;
            if (_stage == null || float.IsNaN(w) || w <= 0f) return new Vector2(Root.worldBound.center.x, Root.worldBound.height * 0.45f);
            return _stage.ToField(local, w, h);
        }

        private void Sag(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _bubble?.RemoveFromHierarchy();
            var bubble = Ux.Row("gb-card", "gb-ranchhof-bubble");
            bubble.name = "ranchdorf-sagt";
            bubble.pickingMode = PickingMode.Ignore;
            bubble.Add(Ux.Text(text, "gb-ranchhof-bubble-text"));
            Root.Add(bubble);
            _bubble = bubble;
            _juice.ScalePop(bubble, 1.05f, 0.18f);
            bubble.schedule.Execute(() => { if (_bubble == bubble) _juice.FadeOut(bubble, 0.4f); }).StartingIn(5200);
            bubble.schedule.Execute(() => { if (_bubble == bubble) { bubble.RemoveFromHierarchy(); _bubble = null; } }).StartingIn(5700);
        }

        private void Fehler(string code)
        {
            Sfx.PlayUi("soft_error");
            _host.Ui.Toast(T("rdorf.fehler." + code), "coin");
            Debug.Log("[ranchdorf] nein: " + code);
        }

        // ── taps on the square ──

        private void FeldTippen(Vector2 local)
        {
            if (_stage == null || _overlay != null) return;
            var (ziel, id) = _stage.Pick(local, _field.layout.width, _field.layout.height);
            Tippe(ziel, id);
        }

        private void Tippe(RanchDorfStage.Ziel ziel, string id)
        {
            switch (ziel)
            {
                case RanchDorfStage.Ziel.Laden: ZeigeLaden(id); break;
                case RanchDorfStage.Ziel.Pferd: ZeigeLaden(RanchDorf.Pferdehaendlerin); break;
                case RanchDorfStage.Ziel.Brunnen: Wunsch(); break;
            }
        }

        private void Wunsch()
        {
            _stage?.Wish();
            Sfx.Play("care_wasser", 2f, 0.7f);
            var wuensche = _host.Strings.Items("rdorf.wuensche");
            var wunsch = wuensche != null && wuensche.Count > 0 ? J.Str(wuensche[UnityEngine.Random.Range(0, wuensche.Count)]) : "";
            Sag(T("rdorf.brunnen") + "  " + T("rdorf.brunnen_wunsch", ("wunsch", wunsch)));
            _juice.Burst(FieldPoint(RanchDorfStage.Plaza + Vector3.up * 2f), new Color(0.62f, 0.84f, 1f), 14);
        }

        // ── the shops ──

        private VisualElement OpenSheet(string id)
        {
            CloseOverlay();
            Laden = id;
            _overlay = Ux.El("gb-scrim", "gb-ranchhof-veil");
            _overlay.name = "ranchdorf-" + id + "-veil";
            _overlay.RegisterCallback<ClickEvent>(e => { if (e.target == _overlay) CloseOverlay(); });
            var sheet = Ux.Col("gb-card", "gb-ranchhof-sheet");
            sheet.name = "ranchdorf-" + id;
            var icon = LadenIcons.First(l => l.id == id).icon;
            var head = CounterPanel.Head(icon, ArtLibrary.Hex(RanchDorfStage.ShopColor[id]), T("rdorf.laden." + id), T("rdorf.npc." + id) + " · " + T("rdorf.npc_rolle." + id), CloseOverlay);
            sheet.Add(head.Row);
            var gruss = Ux.Row("gb-ranchdorf-gruss");
            _grussLabel = Ux.Text("„" + T("rdorf.gruss." + id) + "“", "gb-md", "gb-mild");
            _grussLabel.style.whiteSpace = WhiteSpace.Normal;
            _grussLabel.style.flexShrink = 1;
            gruss.Add(_grussLabel);
            sheet.Add(gruss);
            sheet.Add(FreundZeile(id));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-ranchhof-sheet-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sheet.Add(scroll);
            _overlay.Add(sheet);
            Root.Add(_overlay);
            _juice.ScalePop(sheet, 1.04f, 0.2f);
            return scroll.contentContainer;
        }

        private void CloseOverlay()
        {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            Laden = "";
        }

        private void ZeigeLaden(string id, bool still = false)
        {
            if (!still)
            {
                Sfx.PlayUi("soft_open");
                _stage?.Greet(id);
                _verkaufSicher = null;
            }
            var body = OpenSheet(id);
            var npc = RanchNpc.VomLaden(id)?.Id;
            if (!still && npc != null)
            {
                RanchQuestUi.Melde(_host, "sprich_mit", npc);
                if (id == RanchDorf.Schmiede) RanchQuestUi.Melde(_host, "gehe_zu", "schmiede");
                if (id == RanchDorf.Reitladen) RanchQuestUi.Melde(_host, "gehe_zu", "sattlerei");
            }
            if (npc != null)
            {
                // the keeper's quests sit on top of the shop: an offer, a ready hand-in
                var quests = RanchQuests.VomGeber(State, npc, RanchQuests.Erfuellbar, RanchQuests.Verfuegbar, RanchQuests.Aktiv, RanchQuests.Wartend);
                foreach (var q in quests.OrderBy(q => RanchQuests.Status(State, q) == RanchQuests.Erfuellbar ? 0 : 1).Take(2))
                    body.Add(RanchQuestUi.Karte(_host, q, () => ZeigeLaden(id, true), _coinPill));
            }
            switch (id)
            {
                case RanchDorf.Pferdehaendlerin: Haendlerin(body); break;
                case RanchDorf.Futterhof: Futterhof(body); break;
                case RanchDorf.Reitladen: Reitladen(body); break;
                case RanchDorf.Schmiede: Schmiede(body); break;
                case RanchDorf.Moebelscheune: Moebelscheune(body); break;
            }
        }

        private Button Knopf(string name, string icon, string text, Action onClick, string style = "gb-btn-confirm")
        {
            var b = icon != null ? Ux.IconTextBtn(icon, text, onClick, style, "gb-btn-sm") : Ux.Btn(text, onClick, style, "gb-btn-sm");
            b.name = name;
            return b;
        }

        private static VisualElement Disc(Color c, string icon)
        {
            var disc = Ux.El("gb-ranchhof-horse-disc", "gb-ranchdorf-disc");
            disc.style.backgroundColor = c;
            disc.pickingMode = PickingMode.Ignore;
            if (icon != null) disc.Add(Ux.Icon(icon));
            return disc;
        }

        // Wilma, the horse trader

        private void Haendlerin(VisualElement body)
        {
            var angebot = DorfHaendler.Angebot(State, Today);
            var belegt = RanchHof.PferdIds(State).Count;
            var frei = DorfHaendler.Kapazitaet(State);
            body.Add(Ux.Text(T("rdorf.angebot_heute"), "gb-ranchhof-sec"));
            var boxen = Ux.Text(T("rdorf.boxen", ("belegt", belegt), ("frei", frei)) + (belegt >= frei ? "  ·  " + T("rdorf.boxen_bauen") : ""), "gb-sm", "gb-mild");
            boxen.name = "ranchdorf-boxen";
            boxen.style.whiteSpace = WhiteSpace.Normal;
            body.Add(boxen);
            if (angebot.Count == 0) body.Add(Ux.Text(T("rdorf.kein_angebot"), "gb-md"));
            var row = Ux.Row("gb-ranchhof-games");
            var coins = J.Int(J.Path(State, "economy", "coins"));
            foreach (var p in angebot)
            {
                var pid = p.Id;
                var card = Ux.Col("gb-ranchhof-game", "gb-ranchdorf-horsecard");
                card.name = "ranchdorf-angebot-" + p.Id;
                card.Add(Disc(ArtLibrary.Hex(p.Farbe), "horseshoe"));
                card.Add(Ux.Text(T(p.NameKey), "gb-h2"));
                card.Add(Ux.Text(T("rdorf.pferd_karte", ("fell", T("rdorf.fell." + p.FellId))), "gb-xs", "gb-mild"));
                var preis = RanchNpc.MitRabatt(p.Preis, RanchNpc.Rabatt(State, RanchDorf.Pferdehaendlerin));
                var kann = belegt < frei && coins >= preis;
                card.Add(Knopf("ranchdorf-kaufen-" + p.Id, "coin", T("rdorf.kaufen_preis", ("n", preis)), () => PferdKaufen(pid), kann ? "gb-btn-confirm" : "gb-btn-secondary"));
                row.Add(card);
            }
            body.Add(row);
            body.Add(Ux.Text(T("rdorf.eigene_pferde"), "gb-ranchhof-sec"));
            var ids = RanchHof.PferdIds(State);
            foreach (var id in ids)
            {
                var pferdId = id;
                var p = RanchHof.Pferd(State, id);
                var line = Ux.Row("gb-ranchhof-gearrow");
                line.name = "ranchdorf-eigen-" + id;
                line.Add(Disc(Fell(p).fell, null));
                line.Add(Ux.Text(PferdName(id), "gb-md", "gb-bold", "gb-ranchdorf-name"));
                var preis = DorfHaendler.Verkaufspreis(State, id);
                if (id == _pferdId || ids.Count <= 1)
                    line.Add(Ux.Text(id == _pferdId ? T("rdorf.anbinden") : T("rdorf.fehler.letztesPferd"), "gb-sm", "gb-mild"));
                else
                {
                    var sicher = _verkaufSicher == id;
                    line.Add(Knopf("ranchdorf-verkaufen-" + id, "coin", sicher ? T("rdorf.verkaufen") + "? · " + preis + " ᴳ" : T("rdorf.verkaufen_preis", ("n", preis)), () => PferdVerkaufen(pferdId), sicher ? "gb-btn-confirm" : "gb-btn-secondary"));
                }
                body.Add(line);
            }
            body.Add(Ux.Text(T("rdorf.angebot_morgen"), "gb-sm", "gb-mild", "gb-ranchhof-center"));
        }

        private void PferdKaufen(string poolId)
        {
            (string code, int preis) res = (DorfHaendler.Unbekannt, 0);
            var tag = Today;
            _host.Store.Update(s => res = DorfHaendler.PferdKaufen(s, poolId, tag), GameHost.TicksMs);
            if (res.code != DorfHaendler.Ok) { Fehler(res.code); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            var name = PferdName(poolId);
            var top = FieldPoint(_stage != null ? _stage.OfferTop(poolId) : Vector3.zero);
            _stage?.Bought(poolId);
            Sfx.PlayUi("soft_buy");
            Sfx.Play("ranch_wiehern_a", 0f, 0.8f);
            _juice.Confetti(80);
            _juice.Burst(top, new Color(1f, 0.6f, 0.7f), 16);
            _host.Ui.Toast(T("rdorf.zieht_ein", ("name", name)), "horseshoe");
            Debug.Log("[ranchdorf] pferd gekauft " + poolId + " -" + res.preis);
            CloseOverlay();
            Sag(T("rdorf.zieht_ein", ("name", name)));
            RefreshPills();
        }

        /// <summary>The first tap turns the button into "Verkaufen? · n ᴳ", the second sells.</summary>
        private void PferdVerkaufen(string pferdId)
        {
            if (_verkaufSicher != pferdId)
            {
                _verkaufSicher = pferdId;
                Sfx.PlayUi("soft_chip");
                ZeigeLaden(RanchDorf.Pferdehaendlerin, true);
                return;
            }
            _verkaufSicher = null;
            var name = PferdName(pferdId);
            (string code, int erloes) res = (DorfHaendler.Unbekannt, 0);
            _host.Store.Update(s => res = DorfHaendler.PferdVerkaufen(s, pferdId), GameHost.TicksMs);
            if (res.code != DorfHaendler.Ok) { Fehler(res.code); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_collect");
            _host.Ui.FlyIcons(FieldPoint(new Vector3(0f, 2.5f, 13f)), _coinPill, "coin", Mathf.Clamp(res.erloes / 50, 3, 12), _ => Sfx.PlayUi("soft_tick"));
            _host.Ui.Toast(T("rdorf.verkauft_an", ("name", name), ("n", res.erloes)), "coin");
            Debug.Log("[ranchdorf] pferd verkauft " + pferdId + " +" + res.erloes);
            ZeigeLaden(RanchDorf.Pferdehaendlerin, true);
            RefreshPills();
        }

        // the feed yard

        private void Futterhof(VisualElement body)
        {
            var lager = RanchHof.Lager(State);
            body.Add(Ux.Text(T("rdorf.im_lager", ("heu", J.Int(lager["heu"])), ("apfel", J.Int(lager["apfel"]))) + " · " + T("rdorf.futter_vorrat", ("hafer", RanchDorf.Futter(State, "hafer")), ("leckerli", RanchDorf.Futter(State, "leckerli"))), "gb-sm", "gb-mild"));
            foreach (var w in RanchDorf.FutterWaren)
            {
                var wid = w.Id;
                var line = Ux.Row("gb-ranchhof-gearrow");
                line.Add(Disc(new Color(0.99f, 0.93f, 0.78f), wid.StartsWith("heu") ? "hay" : wid == "hafer" ? "sprout" : "candy"));
                line.Add(Ux.Text(T("rdorf.waren." + wid), "gb-md", "gb-bold", "gb-ranchdorf-name"));
                line.Add(Knopf("ranchdorf-futter-" + wid, "coin", T("rdorf.kaufen_preis", ("n", RanchNpc.MitRabatt(w.Preis, RanchNpc.Rabatt(State, RanchDorf.Futterhof)))), () => FutterKaufen(wid)));
                body.Add(line);
            }
            body.Add(Ux.Text(T("rdorf.ankauf_titel"), "gb-ranchhof-sec"));
            foreach (var art in new[] { "heu", "apfel" })
            {
                var a = art;
                var n = J.Int(lager[art]);
                var stueck = RanchDorf.Ankauf[art];
                var line = Ux.Row("gb-ranchhof-gearrow");
                line.Add(Disc(new Color(0.99f, 0.93f, 0.78f), art == "heu" ? "hay" : "food_apple"));
                line.Add(Ux.Text(T("rdorf.waren." + art) + " · " + n, "gb-md", "gb-bold", "gb-ranchdorf-name"));
                var eins = Knopf("ranchdorf-ankauf-" + art, "coin", T("rdorf.einen_verkaufen", ("n", stueck)), () => ErnteVerkaufen(a, 1), "gb-btn-secondary");
                eins.SetEnabled(n > 0);
                line.Add(eins);
                var alle = Knopf("ranchdorf-ankauf-" + art + "-alle", null, T("rdorf.alles_verkaufen", ("n", n)), () => ErnteVerkaufen(a, J.Int(RanchHof.Lager(State)[a])), "gb-btn-secondary");
                alle.SetEnabled(n > 1);
                alle.style.marginLeft = 6;
                line.Add(alle);
                body.Add(line);
            }
        }

        private void FutterKaufen(string ware)
        {
            (string code, int preis, int menge) res = (DorfWirtschaft.Unbekannt, 0, 0);
            _host.Store.Update(s => res = DorfWirtschaft.FutterKaufen(s, ware), GameHost.TicksMs);
            if (res.code == DorfWirtschaft.LagerVoll && ware.StartsWith("heu", StringComparison.Ordinal))
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(T("rbau.ui.heu_voll", ("n", RanchBauEffekte.HeuKapazitaet(State))), "hay", 3.5f);
                return;
            }
            if (res.code != DorfWirtschaft.Ok) { Fehler(res.code); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            _stage?.Cheer(RanchDorf.Futterhof);
            var pill = ware.StartsWith("heu") ? _hayPill : ware == "hafer" ? _oatPill : _treatPill;
            _host.Ui.FlyIcons(FieldPoint(RanchDorfStage.TagPoint(RanchDorf.Futterhof)), pill, ware.StartsWith("heu") ? "hay" : ware == "hafer" ? "sprout" : "candy", Math.Min(res.menge, 5), _ => Sfx.PlayUi("soft_tick"));
            Debug.Log("[ranchdorf] futter " + ware + " +" + res.menge + " -" + res.preis);
            ZeigeLaden(RanchDorf.Futterhof, true);
            RefreshPills();
        }

        private void ErnteVerkaufen(string art, int n)
        {
            (string code, int erloes) res = (DorfWirtschaft.Unbekannt, 0);
            _host.Store.Update(s => res = DorfWirtschaft.ErnteVerkaufen(s, art, n), GameHost.TicksMs);
            if (res.code != DorfWirtschaft.Ok) { Fehler(res.code); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_collect");
            _stage?.Cheer(RanchDorf.Futterhof);
            _host.Ui.FlyIcons(FieldPoint(RanchDorfStage.TagPoint(RanchDorf.Futterhof)), _coinPill, "coin", Mathf.Clamp(res.erloes / 4, 1, 10), _ => Sfx.PlayUi("soft_tick"));
            Debug.Log("[ranchdorf] ankauf " + art + " x" + n + " +" + res.erloes);
            ZeigeLaden(RanchDorf.Futterhof, true);
            RefreshPills();
        }

        // the saddlery

        private void Reitladen(VisualElement body)
        {
            body.Add(Ux.Text(T("rdorf.gear_fuer", ("name", PferdName(_pferdId))), "gb-ranchhof-sec"));
            RanchGearUi.Reihen(_host, body, _pferdId, "ranchdorf", (slot, farbe) => RanchWirtschaft.GearPreisFuer(State, slot, farbe, RanchNpc.Rabatt(State, RanchDorf.Reitladen)), Gear);
        }

        private void Gear(string slot, string farbe)
        {
            var gid = slot + "_" + farbe;
            var result = RanchWirtschaft.Unbekannt;
            if (!RanchWirtschaft.GearBesitzt(State, gid))
            {
                _host.Store.Update(s => result = RanchWirtschaft.GearKaufen(s, gid, RanchNpc.Rabatt(s, RanchDorf.Reitladen)), GameHost.TicksMs);
                if (result != RanchWirtschaft.Ok) { Fehler(result == RanchWirtschaft.ZuTeuer ? DorfWirtschaft.ZuTeuer : DorfWirtschaft.Unbekannt); return; }
                Sfx.PlayUi("soft_buy");
                _stage?.Cheer(RanchDorf.Reitladen);
                _host.Ui.Toast(T("ranchplay.ausbau.gear_toast"), "star");
                _host.Store.NotifySliceChanged("economy");
            }
            if (!string.IsNullOrEmpty(_pferdId))
            {
                var an = RanchWirtschaft.GearAn(State, _pferdId, slot) == farbe ? null : farbe;
                var pid = _pferdId;
                _host.Store.Update(s => result = RanchWirtschaft.GearAnlegen(s, pid, slot, an), GameHost.TicksMs);
                Sfx.PlayUi("soft_chip");
                if (an != null) Sfx.Play("ranch_sattel", 0f, 0.7f);
            }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            RanchPferdLook.Kleide(_stage?.RideView, State, _pferdId);
            Debug.Log("[ranchdorf] gear " + gid + " → " + result);
            ZeigeLaden(RanchDorf.Reitladen, true);
            RefreshPills();
        }

        // the smithy

        private static string EisenHex(string id) => id.EndsWith("gold") ? "#F2C230" : id.EndsWith("silber") ? "#D8DCE4" : "#C98A4E";

        private void Schmiede(VisualElement body)
        {
            var name = PferdName(_pferdId);
            var traegt = RanchDorf.HufeisenAn(State, _pferdId);
            body.Add(Ux.Text(traegt != null ? T("rdorf.hufeisen_an", ("name", name), ("eisen", T("rdorf.waren." + traegt))) : T("rdorf.hufeisen_fuer", ("name", name)), "gb-sm", "gb-mild"));
            foreach (var w in RanchDorf.SchmiedeWaren)
            {
                var wid = w.Id;
                var line = Ux.Row("gb-ranchhof-gearrow");
                line.name = "ranchdorf-eisen-" + wid;
                line.Add(Disc(ArtLibrary.Hex(EisenHex(wid)), "horseshoe"));
                line.Add(Ux.Text(T("rdorf.waren." + wid), "gb-md", "gb-bold", "gb-ranchdorf-name"));
                if (!RanchDorf.HufeisenBesitzt(State, wid))
                    line.Add(Knopf("ranchdorf-schmiede-" + wid, "coin", T("rdorf.kaufen_preis", ("n", RanchNpc.MitRabatt(w.Preis, RanchNpc.Rabatt(State, RanchDorf.Schmiede)))), () => SchmiedeKaufen(wid)));
                else if (traegt == wid)
                    line.Add(Knopf("ranchdorf-eisen-ab-" + wid, null, T("rdorf.hufeisen_abnehmen"), () => Anlegen(""), "gb-btn-secondary"));
                else
                    line.Add(Knopf("ranchdorf-eisen-an-" + wid, "horseshoe", T("rdorf.hufeisen_anlegen"), () => Anlegen(wid)));
                body.Add(line);
            }
        }

        private void SchmiedeKaufen(string ware)
        {
            (string code, int preis) res = (DorfWirtschaft.Unbekannt, 0);
            _host.Store.Update(s => res = DorfWirtschaft.SchmiedeKaufen(s, ware), GameHost.TicksMs);
            if (res.code != DorfWirtschaft.Ok) { Fehler(res.code); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            Sfx.Play("build_hammer", 2f, 0.6f);
            _stage?.Cheer(RanchDorf.Schmiede);
            Debug.Log("[ranchdorf] schmiede " + ware + " -" + res.preis);
            Anlegen(ware);
        }

        private void Anlegen(string ware)
        {
            var code = DorfWirtschaft.Unbekannt;
            var pid = _pferdId;
            _host.Store.Update(s => code = DorfWirtschaft.HufeisenAnlegen(s, pid, ware), GameHost.TicksMs);
            if (code != DorfWirtschaft.Ok) { Fehler(code); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            if (!string.IsNullOrEmpty(ware))
            {
                Sfx.PlayUi("soft_levelup");
                _juice.Burst(FieldPoint(RanchDorfStage.HitchSpot + Vector3.up * 1.6f), ArtLibrary.Hex(EisenHex(ware)), 14);
                RanchQuestUi.Melde(_host, "pflege", "hufe");
            }
            else Sfx.PlayUi("soft_chip");
            Debug.Log("[ranchdorf] hufeisen " + pid + " → " + (ware.Length > 0 ? ware : "ab"));
            ZeigeLaden(RanchDorf.Schmiede, true);
            RefreshPills();
        }

        // the furniture barn

        private void Moebelscheune(VisualElement body)
        {
            // dorf_laden.gd: the ranch decoration first — it waits in the build store and is set up for free
            body.Add(Ux.Text(T("rdorf.ranch_deko_titel"), "gb-ranchhof-sec"));
            var deko = Ux.Row("gb-ranchhof-shoprow");
            var bau = RanchBauState.Lese(State);
            foreach (var id in RanchDorf.RanchDeko)
            {
                var did = id;
                var def = RanchBauKatalog.Def(null, id);
                if (def == null) continue;
                var tile = Ux.Col("gb-ranchhof-tile");
                tile.name = "ranchdorf-deko-" + id;
                tile.Add(Ux.Icon(id == "bank_holz" ? "sofa" : id == "laterne" ? "bulb" : id == "vogelhaus" ? "home" : id == "wegweiser" ? "sign" : id == "strohpuppe" ? "user" : id == "fahne" ? "race_flag" : "flower"));
                tile.Add(Ux.Text(T(def.NameKey), "gb-md", "gb-bold"));
                var lager = RanchBauState.LagerAnzahl(bau, id);
                if (lager > 0) tile.Add(Ux.Text(T("rbau.ui.lager", ("n", lager)), "gb-sm", "gb-mild"));
                tile.Add(Knopf("ranchdorf-deko-kaufen-" + id, "coin", T("rdorf.kaufen_preis", ("n", def.Kosten)), () => DekoKaufen(did)));
                deko.Add(tile);
            }
            body.Add(deko);
            body.Add(Ux.Text(T("rdorf.moebel_titel"), "gb-ranchhof-sec"));
            var grid = Ux.Row("gb-ranchhof-shoprow");
            foreach (var w in RanchDorf.Moebel)
            {
                var wid = w.Id;
                var def = FurnitureCatalog.Def(wid);
                var tile = Ux.Col("gb-ranchhof-tile");
                tile.name = "ranchdorf-moebel-" + wid;
                tile.Add(Ux.Icon("sofa"));
                tile.Add(Ux.Text(def != null ? def.DisplayName(_host.Settings.Locale) : wid, "gb-md", "gb-bold"));
                tile.Add(Knopf("ranchdorf-moebel-kaufen-" + wid, "coin", T("rdorf.kaufen_preis", ("n", w.Preis)), () => MoebelKaufen(wid)));
                grid.Add(tile);
            }
            body.Add(grid);
        }

        private void DekoKaufen(string id)
        {
            (string code, int preis) res = (DorfWirtschaft.Unbekannt, 0);
            _host.Store.Update(s => res = DorfWirtschaft.DekoKaufen(s, id), GameHost.TicksMs);
            if (res.code != DorfWirtschaft.Ok) { Fehler(res.code); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            _stage?.Cheer(RanchDorf.Moebelscheune);
            _host.Ui.Toast(T("rdorf.gekauft") + " " + T("rdorf.deko_lager"), "flower");
            Debug.Log("[ranchdorf] deko " + id + " -" + res.preis);
            RefreshPills();
            ZeigeLaden(RanchDorf.Moebelscheune, true);
        }

        private void MoebelKaufen(string id)
        {
            (string code, int preis) res = (DorfWirtschaft.Unbekannt, 0);
            _host.Store.Update(s => res = DorfWirtschaft.MoebelKaufen(s, id), GameHost.TicksMs);
            if (res.code != DorfWirtschaft.Ok) { Fehler(res.code); return; }
            _host.Store.NotifySliceChanged(HomeState.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            _stage?.Cheer(RanchDorf.Moebelscheune);
            _host.Ui.Toast(T("rdorf.gekauft") + " " + T("rdorf.moebel_lager"), "sofa");
            Debug.Log("[ranchdorf] moebel " + id + " -" + res.preis);
            RefreshPills();
        }

        // ── per frame ──

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            _stage?.Tick(dt, _host.Settings.ReducedMotion);
            _wetter?.Tick(dt);
            PlaceTags();
        }

        /// <summary>The shop name tags follow their counters (hidden during the ride-in and behind a sheet).</summary>
        private void PlaceTags()
        {
            var w = _field.layout.width;
            var h = _field.layout.height;
            var show = _arrived && _overlay == null && _stage != null && !_stage.Riding && !float.IsNaN(w) && w > 0f;
            foreach (var kv in _tagEls)
            {
                if (!show) { kv.Value.style.visibility = Visibility.Hidden; continue; }
                var p = _stage.ToField(RanchDorfStage.TagPoint(kv.Key), w, h);
                var tw = kv.Value.layout.width;
                kv.Value.style.left = p.x - (float.IsNaN(tw) ? 60f : tw * 0.5f);
                kv.Value.style.top = p.y - 18f;
                kv.Value.style.visibility = Visibility.Visible;
            }
        }

        public override void OnShown()
        {
            _stage?.SetVisible(true);
            _wetter?.SetActive(true);
            if (Root != null) RefreshPills();
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

        public JObject QaState() => new JObject
        {
            ["laden"] = Laden,
            ["angekommen"] = _arrived,
            ["reitet"] = _stage != null && _stage.Riding,
            ["pferd"] = _pferdId ?? "",
            ["entdeckt"] = RanchDorf.IstEntdeckt(State),
            ["angebot"] = new JArray(DorfHaendler.Angebot(State, Today).Select(p => p.Id)),
            ["pferde"] = new JArray(RanchHof.PferdIds(State)),
            ["hafer"] = RanchDorf.Futter(State, "hafer"),
            ["leckerli"] = RanchDorf.Futter(State, "leckerli"),
            ["hufeisen"] = RanchDorf.HufeisenAn(State, _pferdId) ?? "",
            ["wetter"] = _wetter?.QaState(),
        };

        /// <summary>"dorf laden &lt;id&gt; | kaufen [poolId] | verkaufen &lt;pferdId&gt; | futter &lt;ware&gt; | ankauf &lt;art&gt; [n] | schmiede &lt;ware&gt; | anlegen [ware] | moebel &lt;id&gt; | gear &lt;slot&gt; &lt;farbe&gt; | brunnen | zu | heim".</summary>
        public void Qa(string[] parts)
        {
            var args = string.Join(" ", parts.Skip(1)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = args.Length > 0 ? args[0] : "";
            var a1 = args.Length > 1 ? args[1] : "";
            switch (cmd)
            {
                case "laden": ZeigeLaden(a1); break;
                case "kaufen": PferdKaufen(a1.Length > 0 ? a1 : DorfHaendler.Angebot(State, Today).Select(p => p.Id).FirstOrDefault() ?? ""); break;
                case "verkaufen":
                    var vid = RanchHof.PferdIds(State).FirstOrDefault(id => id.EndsWith(a1)) ?? a1;
                    _verkaufSicher = vid;
                    PferdVerkaufen(vid);
                    break;
                case "futter": FutterKaufen(a1); break;
                case "ankauf": ErnteVerkaufen(a1, args.Length > 2 ? int.Parse(args[2]) : 1); break;
                case "schmiede": SchmiedeKaufen(a1); break;
                case "anlegen": Anlegen(a1); break;
                case "moebel": MoebelKaufen(a1); break;
                case "deko": DekoKaufen(a1); break;
                case "gear": if (args.Length > 2) Gear(a1, args[2]); break;
                case "brunnen": Wunsch(); break;
                case "wetter": _wetter?.Erzwingen(a1); break;
                case "plaudern": if (RanchNpc.VomLaden(Laden) != null) Plaudern(RanchNpc.VomLaden(Laden).Id); break;
                case "schenken": if (RanchNpc.VomLaden(Laden) != null) Schenken(RanchNpc.VomLaden(Laden).Id, a1); break;
                case "geschichte": if (RanchNpc.VomLaden(Laden) != null) Erzaehlen(RanchNpc.VomLaden(Laden).Id); break;
                case "zu": CloseOverlay(); break;
                case "heim": Quit(); break;
                default: Debug.LogWarning("[qa] dorf: unknown " + cmd); break;
            }
            Debug.Log("[qa] dorf " + string.Join(" ", args));
        }
    }
}
