using System;
using System.Globalization;
using System.Linq;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Tournament league (ranchTurnier) on the shared minigame frame — turnier_game.gd: menu (league panel with promotion
    /// bar, horse, record and today's bonus disciplines; start class; ghost toggle; seven discipline cards) → briefing with
    /// rules, set time and the field → the run on <see cref="RcompRideView"/> → standings and reward → podium ceremony for
    /// places 1–3 → menu. "Fertig" books the session points (20 + 10 per league point).
    /// </summary>
    public sealed class RanchTurnierScreen : MinigameHostScreen
    {
        public const double ViewW = 960.0, ViewH = 540.0;

        private RanchTurnierGame _game;
        private RcompRideView _view;
        private VisualElement _menu, _panel;
        private float _anim;

        private static readonly Color Ink = MgScenery.Hex(0x3B3630);
        private static readonly Color Paper = MgScenery.Hex(0xFFF6E3);
        private static readonly Color Gold = MgScenery.Hex(0xF2B04C);
        private static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        private static readonly string[] DisziplinFarben = { "#FFB3C1", "#C9E7FF", "#CFEBC4", "#FFE7A8", "#E8D5C0", "#F3D1F4", "#BFD8FF" };

        public RanchTurnierScreen(GameHost host) : base(host, "ranchTurnier", "ranchTurnier") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE3A6);
        protected override Color FieldColor => MgScenery.Hex(0x8FC66B);
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;
        protected override bool StageHasGooby => _view?.Has3D == true;

        public RanchTurnierGame Game => _game;
        public RcompRideView RideView => _view;

        protected override void StartGame(int seed)
        {
            _game = new RanchTurnierGame(Difficulty, seed, Host?.Store);
            var online = Host?.Online;
            if (online != null) _game.FreundGeist = online.RanchFriendGhost;
            _game.PhaseChanged += OnPhase;
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_perfect");
        }

        protected override void BuildPlayField()
        {
            _view?.Dispose();
            _view = new RcompRideView(Host, Field, Fx);
            Field.Add(_view.Canvas);
            Field.Add(_view.Hud);
            Field.Add(_view.Controls);
            _view.Bind(_game);
            _menu = new VisualElement { name = "turnier-menu" };
            Ux.Abs(_menu, 0, 0, 0, 0);
            Field.Add(_menu);
            _panel = new VisualElement { name = "turnier-panel" };
            Ux.Abs(_panel, 0, 0, 0, 0);
            _panel.style.alignItems = Align.Center;
            _panel.style.justifyContent = Justify.Center;
            _panel.style.display = DisplayStyle.None;
            Field.Add(_panel);
            OnPhase(_game.Phase);
        }

        private string Klasse(string id) => Host.Strings.T("rcomp.klasse." + id);
        private string Disz(string id) => Host.Strings.T("rcomp.disziplin." + id);
        private string PferdName => _game.PferdName ?? Host.Strings.T("rcomp.menu.leihpferd");

        private void OnPhase(string phase)
        {
            if (_menu == null) return;
            var t = Host.Strings;
            _view.Bind(_game);
            _menu.style.display = phase == RanchTurnierGame.PhaseMenu ? DisplayStyle.Flex : DisplayStyle.None;
            _panel.Clear();
            _panel.style.display = DisplayStyle.None;
            _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            switch (phase)
            {
                case RanchTurnierGame.PhaseMenu:
                    BuildMenu();
                    break;
                case RanchTurnierGame.PhaseEinweisung:
                    BuildEinweisung();
                    break;
                case RanchTurnierGame.PhaseIntro:
                    Banner(Disz(_game.Disziplin), RanchTurnierGame.IntroSec);
                    PlaySfx("ranch_fanfare");
                    break;
                case RanchTurnierGame.PhaseLauf:
                    _view.Callout(Disz(_game.Disziplin), Gold);
                    if (_game.LaufMitFreundGeist)
                        Banner(t.T("nl.rmp.geist_gegen", ("name", Host.Online.RanchFriendGhostName(_game.Disziplin))), 2.6);
                    else if (_game.Lauf.Controller != null) Banner(t.T("mg.rcomp.steuerung"), 2.6);
                    Sfx.Play("game_go");
                    break;
                case RanchTurnierGame.PhaseErgebnis:
                    PlaySfx(_game.LastPlatz <= 3 ? "game_win" : "game_star");
                    if (_game.LastPlatz <= 3) PlaySfx("ranch_menge_jubel", 0f, 0.8f);
                    if (_game.Disziplin == "springen") RanchQuestUi.Melde(Host, "gewinne_wettbewerb", "parcours", 1, _game.LastPlatz);
                    BuildErgebnis();
                    var e = _game.LastErgebnis;
                    if (e != null) Host.Online?.SubmitRanchRun(_game.Disziplin, e.Wert, e.GeistB64, _game.LastGeistNeu);
                    break;
                case RanchTurnierGame.PhaseZeremonie:
                    PlaySfx("ranch_fanfare_sieg");
                    PlaySfx("ranch_menge_jubel", 0f, 0.7f);
                    ConfettiRain(110);
                    BuildZeremonie();
                    break;
            }
        }

        // ── menu ──

        private void BuildMenu()
        {
            var t = Host.Strings;
            var g = _game;
            var comp = g.Comp;
            _menu.Clear();
            _menu.style.backgroundColor = Paper;
            _menu.style.paddingLeft = Length.Percent(3); _menu.style.paddingRight = Length.Percent(3);
            _menu.style.paddingTop = 54; _menu.style.paddingBottom = 12;
            var head = Ux.Row();
            head.style.alignItems = Align.FlexEnd;
            head.style.justifyContent = Justify.SpaceBetween;
            var title = Ux.Text(t.T("rcomp.menu.title"), "gb-h1", "gb-nowrap");
            title.style.color = Ink;
            head.Add(title);
            head.Add(Ux.Text(t.T("rcomp.menu.untertitel"), "gb-sm"));
            _menu.Add(head);

            var body = Ux.Row();
            body.style.flexGrow = 1;
            body.style.marginTop = 10;
            _menu.Add(body);

            var links = Ux.Col();
            links.style.width = Length.Percent(37);
            links.style.marginRight = Length.Percent(2);
            body.Add(links);
            var liga = Ux.Col("gb-card");
            liga.name = "turnier-liga";
            liga.style.paddingLeft = 14; liga.style.paddingRight = 14; liga.style.paddingTop = 10; liga.style.paddingBottom = 12;
            var ligaKlasse = (string)comp["klasse"] ?? "holz";
            liga.Add(Ux.Text(t.T("rcomp.menu.liga", ("klasse", Klasse(ligaKlasse))), "gb-h3", "gb-bold"));
            var schwelle = RanchCompLiga.AufstiegAb(g.Balance, ligaKlasse);
            var punkte = (int)RanchCompKatalog.Num(comp["punkte"]?[ligaKlasse], 0.0);
            liga.Add(Ux.Text(schwelle > 0 ? t.T("rcomp.menu.punkte", ("n", punkte), ("ziel", schwelle)) : t.T("rcomp.menu.punkte_dach"), "gb-sm"));
            var bar = new VisualElement();
            bar.style.height = 12; bar.style.marginTop = 4; bar.style.marginBottom = 6;
            bar.style.backgroundColor = MgScenery.Hex(0xEADFCB);
            Round(bar, 6f);
            var fill = new VisualElement { name = "turnier-liga-bar" };
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent((float)(100.0 * RanchCompLiga.AufstiegFortschritt(g.Balance, ligaKlasse, punkte)));
            fill.style.backgroundColor = Gold;
            Round(fill, 6f);
            bar.Add(fill);
            liga.Add(bar);
            liga.Add(Ux.Text(t.T("rcomp.menu.pferd", ("name", PferdName), ("level", g.PferdLevel)), "gb-sm"));
            liga.Add(Ux.Text(t.T("rcomp.menu.bilanz", ("schleifen", ((Newtonsoft.Json.Linq.JObject)comp["schleifen"]).Count), ("trophaeen", ((Newtonsoft.Json.Linq.JArray)comp["trophaeen"]).Count), ("siege", (int)RanchCompKatalog.Num(comp["siege"], 0.0))), "gb-sm"));
            var heute = Ux.Text(t.T("rcomp.menu.turniertag") + "  ·  " + string.Join(" · ", g.Plan.Select(Disz)), "gb-sm", "gb-bold");
            heute.name = "turnier-turniertag";
            heute.style.color = MgScenery.Hex(0xB57A1E);
            heute.style.marginTop = 6;
            heute.style.whiteSpace = WhiteSpace.Normal;
            liga.Add(heute);
            links.Add(liga);

            var klasseCard = Ux.Col("gb-card");
            klasseCard.style.marginTop = 10;
            klasseCard.style.paddingLeft = 14; klasseCard.style.paddingRight = 14; klasseCard.style.paddingTop = 8; klasseCard.style.paddingBottom = 10;
            klasseCard.Add(Ux.Text(t.T("rcomp.menu.klasse_wahl"), "gb-sm", "gb-bold"));
            var chips = Ux.Row();
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 4;
            foreach (var k in RanchCompKatalog.Klassen)
            {
                var id = k;
                var erlaubt = g.StartErlaubt(id);
                var chip = Ux.Btn(Klasse(id), () =>
                {
                    if (!_game.StartErlaubt(id))
                    {
                        Sfx.PlayUi("soft_error");
                        var grund = RanchCompKatalog.KlasseIndex(id) > RanchCompKatalog.KlasseIndex(_game.LigaKlasse)
                            ? Host.Strings.T("rcomp.menu.gesperrt_liga")
                            : Host.Strings.T("rcomp.menu.gesperrt_level", ("level", (int)RanchCompKatalog.Num(RanchCompKatalog.Klasse(_game.Balance, id)["ab_level"], 1.0)));
                        Banner(Klasse(id) + " — " + grund, 1.8);
                        return;
                    }
                    Sfx.PlayUi("soft_chip");
                    _game.SetKlasse(id);
                    BuildMenu();
                }, "gb-chip");
                chip.name = "turnier-klasse-" + id;
                chip.style.marginRight = 6; chip.style.marginBottom = 6;
                if (id == g.Klasse) chip.AddToClassList("gb-chip--active");
                if (!erlaubt) chip.style.opacity = 0.45f;
                chips.Add(chip);
            }
            klasseCard.Add(chips);
            var geist = Ux.Btn((g.GeistAn ? "✓ " : "") + t.T("rcomp.menu.geist"), () =>
            {
                Sfx.PlayUi("soft_toggle");
                _game.SetGeistAn(!_game.GeistAn);
                BuildMenu();
            }, "gb-chip");
            geist.name = "turnier-geist";
            if (g.GeistAn) geist.AddToClassList("gb-chip--active");
            geist.style.alignSelf = Align.FlexStart;
            klasseCard.Add(geist);
            links.Add(klasseCard);

            var grid = Ux.Row();
            grid.name = "turnier-disziplinen";
            grid.style.flexGrow = 1;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignContent = Align.FlexStart;
            body.Add(grid);
            var schleifen = (Newtonsoft.Json.Linq.JObject)comp["schleifen"];
            for (var i = 0; i < RanchCompKatalog.Disziplinen.Length; i++)
            {
                var d = RanchCompKatalog.Disziplinen[i];
                var gespielt = g.Gespielt.TryGetValue(d, out var platz);
                var card = new Button(() =>
                {
                    if (_game.Gespielt.ContainsKey(d)) { Sfx.PlayUi("soft_error"); return; }
                    Sfx.PlayUi("soft_confirm");
                    _game.OeffneEinweisung(d);
                }) { text = "" };
                card.ClearClassList();
                card.AddToClassList("gb-card");
                card.name = "turnier-disziplin-" + d;
                card.style.width = Length.Percent(48);
                card.style.height = Length.Percent(23);
                card.style.marginRight = Length.Percent(2);
                card.style.marginBottom = 8;
                card.style.flexDirection = FlexDirection.Row;
                card.style.alignItems = Align.Center;
                card.style.paddingLeft = 8;
                if (gespielt) card.style.opacity = 0.6f;
                ColorUtility.TryParseHtmlString(DisziplinFarben[i], out var farbe);
                var tile = new P.Canvas { Paint = p => PaintDisziplin(p, d, 46f, farbe) };
                tile.style.width = 46; tile.style.height = 46; tile.style.marginRight = 10; tile.style.flexShrink = 0;
                card.Add(tile);
                var col = Ux.Col();
                col.pickingMode = PickingMode.Ignore;
                col.style.flexShrink = 1;
                var name = Ux.Text(Disz(d), "gb-bold", "gb-nowrap");
                name.pickingMode = PickingMode.Ignore;
                col.Add(name);
                var extras = new System.Collections.Generic.List<string>();
                if (g.IstTurniertag(d)) extras.Add(t.T("rcomp.menu.bonus"));
                var key = RanchCompLiga.SchleifenKey(d, g.Klasse);
                if (schleifen?[key] != null) extras.Add(t.T("rcomp.menu.schleife", ("platz", (int)schleifen[key])));
                if (gespielt) extras.Add(t.T("rcomp.menu.gespielt", ("platz", platz)));
                if (extras.Count > 0)
                {
                    var ex = Ux.Text(string.Join(" · ", extras), "gb-sm");
                    ex.pickingMode = PickingMode.Ignore;
                    ex.style.color = g.IstTurniertag(d) && !gespielt ? MgScenery.Hex(0xB57A1E) : Ink;
                    ex.style.whiteSpace = WhiteSpace.Normal;
                    col.Add(ex);
                }
                card.Add(col);
                grid.Add(card);
            }

            var foot = Ux.Row();
            foot.style.justifyContent = Justify.Center;
            var done = Ux.Btn(t.T("rcomp.menu.fertig"), () => { Sfx.PlayUi("soft_back"); _game?.Finish(); }, "gb-btn-primary");
            done.name = "turnier-fertig";
            foot.Add(done);
            _menu.Add(foot);
        }

        /// <summary>A little picture per discipline: pole, dressage dots, flag, oval, rails, star, barrel.</summary>
        private static void PaintDisziplin(Painter2D p, string d, float s, Color farbe)
        {
            var c = new Vector2(s * 0.5f, s * 0.5f);
            P.RoundRect(p, Vector2.zero, new Vector2(s, s), s * 0.28f, farbe);
            var u = s / 46f;
            switch (d)
            {
                case "springen":
                    P.Line(p, c + new Vector2(-14f, 8f) * u, c + new Vector2(14f, 8f) * u, 4f * u, MgScenery.Hex(0xE8676B));
                    P.Line(p, c + new Vector2(-14f, 12f) * u, c + new Vector2(-14f, -6f) * u, 3f * u, Outline);
                    P.Line(p, c + new Vector2(14f, 12f) * u, c + new Vector2(14f, -6f) * u, 3f * u, Outline);
                    P.Arc(p, c + new Vector2(0f, 6f) * u, 12f * u, 200f, 340f, 2.5f * u, Outline);
                    break;
                case "dressur":
                    for (var i = 0; i < 8; i++) P.Circle(p, c + new Vector2(Mathf.Cos(i * Mathf.PI / 4f), Mathf.Sin(i * Mathf.PI / 4f)) * 12f * u, 2.6f * u, Outline);
                    break;
                case "gelaende":
                    P.Line(p, c + new Vector2(-6f, 14f) * u, c + new Vector2(-6f, -14f) * u, 3f * u, Outline);
                    P.Poly(p, new[] { c + new Vector2(-6f, -14f) * u, c + new Vector2(12f, -8f) * u, c + new Vector2(-6f, -2f) * u }, MgScenery.Hex(0xE8676B));
                    break;
                case "rennen":
                    P.Ring(p, c, 13f * u, 5f * u, MgScenery.Hex(0x7FB86A));
                    P.Line(p, c + new Vector2(0f, 9f) * u, c + new Vector2(0f, 17f) * u, 2.5f * u, Color.white);
                    break;
                case "trail":
                    P.Line(p, c + new Vector2(-14f, -6f) * u, c + new Vector2(14f, -6f) * u, 3.5f * u, MgScenery.Hex(0xC98C59));
                    P.Line(p, c + new Vector2(-14f, 6f) * u, c + new Vector2(14f, 6f) * u, 3.5f * u, MgScenery.Hex(0xC98C59));
                    break;
                case "schau":
                    P.Star(p, c, 14f * u, MgScenery.Hex(0xFFD34D));
                    break;
                default:
                    P.Circle(p, c, 11f * u, Outline);
                    P.Circle(p, c, 9f * u, MgScenery.Hex(0x5C8CC7));
                    P.Ring(p, c, 6f * u, 1.6f * u, Color.white);
                    break;
            }
        }

        // ── briefing / result / ceremony ──

        private VisualElement CenterCard(string name)
        {
            _panel.style.display = DisplayStyle.Flex;
            _panel.style.backgroundColor = new Color(0.16f, 0.13f, 0.1f, 0.55f);
            var card = Ux.Col("gb-card");
            card.name = name;
            card.style.maxWidth = Length.Percent(72);
            card.style.maxHeight = Length.Percent(86);
            card.style.paddingLeft = 24; card.style.paddingRight = 24; card.style.paddingTop = 16; card.style.paddingBottom = 16;
            card.style.alignItems = Align.Center;
            _panel.Add(card);
            return card;
        }

        private void BuildEinweisung()
        {
            var t = Host.Strings;
            var g = _game;
            var card = CenterCard("turnier-einweisung");
            card.Add(Ux.Text(t.T("rcomp.einweisung.titel", ("disziplin", Disz(g.Disziplin)), ("klasse", Klasse(g.Klasse))), "gb-h2", "gb-bold"));
            var regeln = Ux.Text(t.T("rcomp.regeln." + g.Disziplin), "gb-sm");
            regeln.style.whiteSpace = WhiteSpace.Normal;
            regeln.style.marginTop = 6;
            regeln.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(regeln);
            if (g.Richtzeit > 0.0) card.Add(Ux.Text(t.T("rcomp.einweisung.richtzeit", ("s", g.Richtzeit.ToString("0", CultureInfo.InvariantCulture))), "gb-sm", "gb-bold"));
            var feld = Ux.Text(t.T("rcomp.einweisung.starterfeld"), "gb-bold");
            feld.style.marginTop = 8;
            card.Add(feld);
            foreach (var b in g.Bots) card.Add(Ux.Text("· " + t.T("rcomp.einweisung.starter", ("name", b.Name), ("pferd", b.Pferd)), "gb-sm"));
            var row = Ux.Row();
            row.style.marginTop = 12;
            var zurueck = Ux.Btn(t.T("rcomp.einweisung.zurueck"), () => { Sfx.PlayUi("soft_back"); _game.ZurueckZumMenu(); }, "gb-btn");
            zurueck.name = "turnier-zurueck";
            zurueck.style.marginRight = 10;
            row.Add(zurueck);
            var los = Ux.Btn(t.T("rcomp.einweisung.los"), () => { Sfx.PlayUi("soft_confirm"); _game.StarteLauf(); }, "gb-btn-primary");
            los.name = "turnier-los";
            row.Add(los);
            card.Add(row);
            if (FieldW > 1f) ScalePop(card, 1.06f);
        }

        private string Starter(RcompStarter s) =>
            s.IstSpieler ? Host.Strings.T("rcomp.ergebnis.du", ("pferd", PferdName)) : Host.Strings.T("rcomp.einweisung.starter", ("name", s.Name), ("pferd", s.Pferd));

        private string WertText(RcompStarter s) =>
            _game.ZeitGewinnt
                ? Host.Strings.T("rcomp.ergebnis.zeit", ("s", s.Wert.ToString("0.0", CultureInfo.InvariantCulture)))
                : Host.Strings.T("rcomp.ergebnis.punkte", ("n", (int)Math.Round(s.Wert)));

        private void BuildErgebnis()
        {
            var t = Host.Strings;
            var g = _game;
            var card = CenterCard("turnier-ergebnis");
            card.Add(Ux.Text(t.T("rcomp.ergebnis.titel"), "gb-h2", "gb-bold"));
            for (var i = 0; i < g.Stand.Count; i++)
            {
                var s = g.Stand[i];
                var zeile = Ux.Text((i + 1) + ". " + Starter(s) + " — " + WertText(s), s.IstSpieler ? "gb-bold" : "gb-sm");
                if (s.IstSpieler)
                {
                    zeile.name = "turnier-du";
                    zeile.style.color = MgScenery.Hex(0xB57A1E);
                }
                card.Add(zeile);
            }
            var b = g.LastBericht;
            var lohn = Ux.Text(t.T("rcomp.ergebnis.gold", ("n", b.Gold)) + "  ·  " + t.T("rcomp.ergebnis.xp", ("n", b.Xp)) + "  ·  " + t.T("rcomp.ergebnis.liga", ("n", b.LigaPunkte)), "gb-bold");
            lohn.style.marginTop = 8;
            card.Add(lohn);
            void Extra(string text)
            {
                var l = Ux.Text(text, "gb-sm", "gb-bold");
                l.style.color = MgScenery.Hex(0xB57A1E);
                card.Add(l);
            }
            if (b.Aufgestiegen) Extra(t.T("rcomp.ergebnis.aufstieg", ("klasse", Klasse(b.NeueKlasse))));
            if (b.SchleifeNeu) Extra(t.T("rcomp.ergebnis.schleife", ("disziplin", Disz(g.Disziplin))));
            if (b.TrophaeeNeu) Extra(t.T("rcomp.ergebnis.trophaee"));
            if (g.LastGeistNeu) Extra(t.T("rcomp.ergebnis.geist"));
            var xp = g.LastPferdXp;
            if (xp != null && xp.LevelUps.Count > 0)
            {
                var levelUp = Ux.Text(t.T("rpferd.levelup.titel", ("level", xp.LevelUps[xp.LevelUps.Count - 1])) + "  " +
                    t.T("rpferd.levelup.untertitel", ("name", g.PferdName ?? "")), "gb-sm", "gb-bold");
                levelUp.name = "turnier-pferd-level";
                levelUp.style.color = MgScenery.Hex(0x3E9B6B);
                card.Add(levelUp);
                foreach (var geschenk in xp.Meilensteine)
                    Extra(t.T("rpferd.meilenstein.titel") + " " + t.T("rpferd.meilenstein." + geschenk));
            }
            var weiter = Ux.Btn(t.T("rcomp.ergebnis.weiter"), () => { Sfx.PlayUi("soft_confirm"); _game.NachErgebnis(); }, "gb-btn-primary");
            weiter.name = "turnier-weiter";
            weiter.style.marginTop = 12;
            card.Add(weiter);
            if (FieldW > 1f)
            {
                ScalePop(card, 1.08f);
                Float(new Vector2(FieldW * 0.5f, FieldH * 0.2f), t.T("rcomp.ergebnis.gold", ("n", b.Gold)), Gold, 30f);
            }
        }

        private void BuildZeremonie()
        {
            var t = Host.Strings;
            var g = _game;
            _panel.style.display = DisplayStyle.Flex;
            _panel.style.backgroundColor = new Color(0.16f, 0.13f, 0.1f, 0.35f);
            var col = Ux.Col();
            col.name = "turnier-zeremonie";
            col.style.alignItems = Align.Center;
            col.style.width = Length.Percent(80);
            col.style.height = Length.Percent(90);
            var title = Ux.Text(t.T("rcomp.zeremonie.titel"), "gb-h1", "gb-bold");
            title.style.color = MgScenery.Hex(0xFFF6E8);
            title.style.unityTextOutlineWidth = 3f;
            title.style.unityTextOutlineColor = Ink;
            col.Add(title);
            var podest = new P.Canvas { Paint = PaintPodest, name = "turnier-podest" };
            podest.style.flexGrow = 1;
            podest.style.alignSelf = Align.Stretch;
            col.Add(podest);
            var karte = Ux.Col("gb-card");
            karte.style.alignItems = Align.Center;
            karte.style.paddingLeft = 22; karte.style.paddingRight = 22; karte.style.paddingTop = 10; karte.style.paddingBottom = 12;
            for (var i = 0; i < Math.Min(3, g.Stand.Count); i++)
            {
                var zeile = Ux.Text(t.T("rcomp.zeremonie.platz" + (i + 1), ("name", Starter(g.Stand[i]))), g.Stand[i].IstSpieler ? "gb-bold" : "gb-sm");
                if (g.Stand[i].IstSpieler) zeile.style.color = MgScenery.Hex(0xB57A1E);
                karte.Add(zeile);
            }
            var weiter = Ux.Btn(t.T("rcomp.zeremonie.weiter"), () => { Sfx.PlayUi("soft_back"); _game.ZeremonieWeiter(); }, "gb-btn-primary");
            weiter.name = "turnier-zur-uebersicht";
            weiter.style.marginTop = 8;
            karte.Add(weiter);
            col.Add(karte);
            _panel.Add(col);
        }

        /// <summary>The podium (gold in the middle, silver left, bronze right) with the top three horses and riders on top.</summary>
        private void PaintPodest(Painter2D p)
        {
            var g = _game;
            if (g == null) return;
            var w = FieldW * 0.8f;
            var h = FieldH * 0.5f;
            if (w <= 1f || h <= 1f) return;
            var basis = h * 0.92f;
            var block = Mathf.Min(w * 0.2f, h * 0.55f);
            var hoehen = new[] { 1.0f, 0.65f, 0.4f };
            var xs = new[] { 0f, -1.05f, 1.05f };
            var farben = new[] { MgScenery.Hex(0xF2B04C), MgScenery.Hex(0xCCD1DB), MgScenery.Hex(0xC98C59) };
            for (var i = 0; i < 3; i++)
            {
                var cx = w * 0.5f + xs[i] * block;
                var top = basis - hoehen[i] * block * 0.9f;
                P.Rect(p, new Vector2(cx - block * 0.5f + 4f, top + 5f), new Vector2(cx + block * 0.5f + 4f, basis + 5f), new Color(0f, 0f, 0f, 0.2f));
                P.RoundRect(p, new Vector2(cx - block * 0.5f, top), new Vector2(cx + block * 0.5f, basis), 8f, farben[i]);
                P.Rect(p, new Vector2(cx - block * 0.5f, top), new Vector2(cx + block * 0.5f, top + 6f), Color.Lerp(farben[i], Color.white, 0.35f));
                Ziffer(p, i + 1, new Vector2(cx, top + Mathf.Min(block * 0.32f, (basis - top) * 0.5f)), block * 0.2f);
                if (i >= g.Stand.Count) continue;
                var starter = g.Stand[i];
                var fell = starter.IstSpieler ? RcompRideView.Fell["braun"] : RcompRideView.Fell.TryGetValue(starter.Farbe ?? "braun", out var f) ? f : RcompRideView.Fell["braun"];
                var hop = Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 4f + i)) * block * 0.05f;
                PaintSideHorse(p, new Vector2(cx, top - block * 0.02f - hop), block * 0.42f, fell, starter.IstSpieler);
            }
        }

        /// <summary>The place digit (1–3) on a podium block, drawn as rounded strokes.</summary>
        private static void Ziffer(Painter2D p, int n, Vector2 c, float h)
        {
            var w = h * 0.55f;
            var dicke = Mathf.Max(3f, h * 0.18f);
            var farbe = new Color(1f, 1f, 1f, 0.92f);
            Vector2 Q(float x, float y) => c + new Vector2(x * w, y * h * 0.5f);
            switch (n)
            {
                case 1:
                    P.Line(p, Q(0.1f, -1f), Q(0.1f, 1f), dicke, farbe);
                    P.Line(p, Q(-0.35f, -0.55f), Q(0.1f, -1f), dicke, farbe);
                    break;
                case 2:
                    P.Arc(p, Q(0f, -0.45f), w * 0.5f, 180f, 380f, dicke, farbe);
                    P.Line(p, Q(0.45f, -0.25f), Q(-0.5f, 1f), dicke, farbe);
                    P.Line(p, Q(-0.5f, 1f), Q(0.5f, 1f), dicke, farbe);
                    break;
                default:
                    P.Arc(p, Q(0f, -0.48f), w * 0.48f, 190f, 450f, dicke, farbe);
                    P.Arc(p, Q(0f, 0.48f), w * 0.5f, 270f, 530f, dicke, farbe);
                    break;
            }
        }

        /// <summary>A horse from the side (body, neck, head, legs, tail) with a rider dot or Gooby for the player.</summary>
        private static void PaintSideHorse(Painter2D p, Vector2 fuss, float s, (Color hell, Color dunkel) fell, bool gooby)
        {
            var koerper = fuss + new Vector2(0f, -s * 0.5f);
            foreach (var x in new[] { -0.36f, -0.2f, 0.2f, 0.36f }) P.Line(p, koerper + new Vector2(x * s, s * 0.1f), fuss + new Vector2(x * s, -s * 0.02f), s * 0.14f, fell.dunkel);
            P.Line(p, koerper + new Vector2(-s * 0.5f, -s * 0.05f), koerper + new Vector2(-s * 0.72f, s * 0.25f), s * 0.1f, fell.dunkel);
            P.OutlinedBlob(p, koerper, s * 0.52f, s * 0.26f, 2.5f, fell.hell, Outline);
            P.Line(p, koerper + new Vector2(s * 0.36f, -s * 0.12f), koerper + new Vector2(s * 0.56f, -s * 0.46f), s * 0.2f, fell.hell);
            P.OutlinedBlob(p, koerper + new Vector2(s * 0.66f, -s * 0.5f), s * 0.2f, s * 0.13f, 2f, fell.hell, Outline);
            P.Circle(p, koerper + new Vector2(s * 0.7f, -s * 0.54f), s * 0.025f, Outline);
            var reiter = koerper + new Vector2(-s * 0.02f, -s * 0.42f);
            if (gooby)
            {
                P.OutlinedBlob(p, reiter, s * 0.17f, s * 0.17f, 2f, MgScenery.Hex(0xF9EDD6), Outline);
                P.OutlinedBlob(p, reiter + new Vector2(-s * 0.15f, -s * 0.02f), s * 0.05f, s * 0.11f, 1.5f, MgScenery.Hex(0xF9EDD6), Outline);
                P.Line(p, reiter + new Vector2(0f, -s * 0.16f), reiter + new Vector2(s * 0.03f, -s * 0.26f), s * 0.04f, MgScenery.Hex(0x8ED081));
            }
            else P.OutlinedBlob(p, reiter, s * 0.14f, s * 0.18f, 2f, MgScenery.Hex(0xE8C4A0), Outline);
        }

        private static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        // ── frame hooks ──

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _view.Tick(dt);
            _anim += (float)dt;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            TimeLabel.text = g.Lauf != null && (g.Phase == RanchTurnierGame.PhaseLauf || g.Phase == RanchTurnierGame.PhaseIntro)
                ? t.T("rcomp.hud.zeit", ("s", g.Lauf.Zeit.ToString("0.0", CultureInfo.InvariantCulture)))
                : t.T("mg.ranchTurnier.title");
            ScoreLabel.text = g.SessionScore.ToString();
            _view.Render();
            if (g.Phase == RanchTurnierGame.PhaseZeremonie) _panel.Q("turnier-podest")?.MarkDirtyRepaint();
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            _game = null;
            _view?.Dispose();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            _view?.Dispose();
        }
    }
}
