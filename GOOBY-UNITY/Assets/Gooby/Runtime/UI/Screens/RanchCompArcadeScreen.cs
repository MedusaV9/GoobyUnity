using System;
using System.Globalization;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The two RW-5 arcade rides on the shared minigame frame (tonnen_game.gd / zeit_game.gd): run select (10 cards with
    /// stars and best time, unlocked in order) → intro beat → the ride on <see cref="RcompRideView"/> (chase view, stick +
    /// gait controls, ghost of the best run) → end card (stars, new best run; next / again / select); "Fertig" books the
    /// session. Too slow books nothing.
    /// </summary>
    public abstract class RanchCompArcadeScreen : MinigameHostScreen
    {
        public const double ViewW = 960.0, ViewH = 540.0;

        protected RcompRideView View;
        private VisualElement _select, _grid, _overlay;
        private Label _selectStars;
        private bool _hintShown;
        private readonly string _tilePrefix;

        private static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        private static readonly Color Gold = MgScenery.Hex(0xFFD34D);

        protected RanchCompArcadeScreen(GameHost host, string gameId, string tilePrefix) : base(host, gameId, gameId) => _tilePrefix = tilePrefix;

        protected abstract RanchCompArcadeGame Game { get; }
        protected abstract RanchCompArcadeGame CreateGame(int seed);
        protected abstract void ClearGame();

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => Game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE3A6);
        protected override Color FieldColor => MgScenery.Hex(0x8FC66B);
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;
        protected override bool StageHasGooby => View?.Has3D == true;

        public RanchCompArcadeGame Session => Game;
        public RcompRideView RideView => View;

        private string Key(string suffix) => "mg." + Meta.Id + "." + suffix;

        protected override void StartGame(int seed)
        {
            var game = CreateGame(seed);
            game.PhaseChanged += OnPhase;
            game.Finished_ += RoundOver;
            _hintShown = false;
            Sfx.Preload("game_perfect");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            View?.Dispose();
            View = new RcompRideView(Host, Field, Fx);
            Field.Add(View.Canvas);
            Field.Add(View.Hud);
            Field.Add(View.Controls);
            View.Bind(Game);

            _select = Ux.Col();
            _select.name = "rcomp-select-screen";
            Ux.Abs(_select, 0, 0, 0, 0);
            _select.style.paddingLeft = Length.Percent(4); _select.style.paddingRight = Length.Percent(4);
            _select.style.paddingTop = 60; _select.style.paddingBottom = 16;
            _select.style.backgroundColor = new Color(0.98f, 0.97f, 0.92f, 0.9f);
            var head = Ux.Row();
            head.style.alignItems = Align.Center;
            head.style.justifyContent = Justify.SpaceBetween;
            head.Add(Ux.Text(t.T(Key("title")), "gb-h1", "gb-nowrap"));
            _selectStars = Ux.Text("", "gb-bold", "gb-nowrap");
            head.Add(_selectStars);
            _select.Add(head);
            var hint = Ux.Text(t.T(Key("hint")), "gb-sm");
            hint.style.marginTop = 4;
            _select.Add(hint);
            _grid = Ux.Row();
            _grid.name = "rcomp-levels";
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.flexGrow = 1;
            _grid.style.alignContent = Align.FlexStart;
            _grid.style.marginTop = 12;
            _select.Add(_grid);
            var foot = Ux.Row();
            foot.style.justifyContent = Justify.Center;
            var done = Ux.Btn(t.T("rcomp.menu.fertig"), () => { Sfx.PlayUi("soft_confirm"); Game?.Finish(); }, "gb-btn-primary");
            done.name = "rcomp-done";
            foot.Add(done);
            _select.Add(foot);
            Field.Add(_select);

            _overlay = Ux.El();
            _overlay.name = "rcomp-overlay";
            Ux.Abs(_overlay, 0, 0, 0, 0);
            _overlay.style.display = DisplayStyle.None;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0.29f, 0.23f, 0.21f, 0.32f);
            Field.Add(_overlay);
            OnPhase(Game.Phase);
        }

        private void RefreshSelect()
        {
            var t = Host.Strings;
            var g = Game;
            _selectStars.text = t.T("rcomp.select.sterne", ("n", RanchCompState.ArcadeTotalStars(g.Save, g.Spiel)), ("max", RanchCompArcade.LevelAnzahl * 3));
            _grid.Clear();
            var open = RanchCompState.ArcadeMaxUnlocked(g.Save, g.Spiel);
            for (var id = 1; id <= RanchCompArcade.LevelAnzahl; id++)
            {
                var levelId = id;
                var unlocked = id <= open;
                var stars = RanchCompState.ArcadeStars(g.Save, g.Spiel, id);
                var best = RanchCompState.ArcadeBest(g.Save, g.Spiel, id);
                var card = new Button(() =>
                {
                    if (!Game.IsUnlocked(levelId))
                    {
                        Sfx.PlayUi("soft_error");
                        Banner(Host.Strings.T("rcomp.select.offen", ("n", RanchCompState.ArcadeMaxUnlocked(Game.Save, Game.Spiel))), 1.6);
                        return;
                    }
                    Sfx.PlayUi("soft_confirm");
                    Game.Open(levelId);
                }) { text = "" };
                card.ClearClassList();
                card.AddToClassList("gb-card");
                card.name = "rcomp-level-" + id;
                card.style.width = Length.Percent(18.4f);
                card.style.height = Length.Percent(44f);
                card.style.marginRight = Length.Percent(1.6f);
                card.style.marginBottom = 10;
                card.style.alignItems = Align.Center;
                card.style.paddingTop = 10;
                if (!unlocked) card.style.opacity = 0.55f;
                var num = Ux.Text(_tilePrefix + id, "gb-h2", "gb-bold");
                num.pickingMode = PickingMode.Ignore;
                card.Add(num);
                var art = new P.Canvas { Paint = p => PaintCard(p, card, levelId, unlocked, stars) };
                art.style.flexGrow = 1;
                art.style.alignSelf = Align.Stretch;
                card.Add(art);
                var bestLabel = Ux.Text(best > 0 ? t.T("rcomp.select.best", ("s", (best / 10.0).ToString("0.0", CultureInfo.InvariantCulture))) : " ", "gb-sm", "gb-nowrap");
                bestLabel.pickingMode = PickingMode.Ignore;
                bestLabel.style.marginBottom = 6;
                card.Add(bestLabel);
                _grid.Add(card);
            }
        }

        private void PaintCard(Painter2D p, VisualElement card, int levelId, bool unlocked, int stars)
        {
            var w = card.contentRect.width;
            var h = card.contentRect.height;
            if (w <= 1f) return;
            if (!unlocked)
            {
                var c = new Vector2(w * 0.5f, h * 0.32f);
                P.Arc(p, c + new Vector2(0f, -3f), 6f, 180f, 360f, 3f, new Color(0.45f, 0.38f, 0.35f));
                P.Rect(p, c + new Vector2(-8f, -3f), c + new Vector2(8f, 9f), new Color(0.45f, 0.38f, 0.35f));
                return;
            }
            PaintCardArt(p, levelId, new Rect(w * 0.12f, h * 0.06f, w * 0.76f, h * 0.36f));
            for (var i = 0; i < 3; i++) P.Star(p, new Vector2(w * 0.5f + (i - 1) * 20f, h * 0.6f), 8f, i < stars ? Gold : new Color(0.85f, 0.8f, 0.74f));
        }

        /// <summary>The little course drawing of a run card.</summary>
        protected abstract void PaintCardArt(Painter2D p, int levelId, Rect box);

        private void OnPhase(string phase)
        {
            if (_select == null) return;
            var t = Host.Strings;
            var g = Game;
            _select.style.display = phase == RanchCompArcadeGame.PhaseSelect ? DisplayStyle.Flex : DisplayStyle.None;
            _overlay.style.display = DisplayStyle.None;
            _overlay.Clear();
            View.Bind(g);
            switch (phase)
            {
                case RanchCompArcadeGame.PhaseSelect:
                    RefreshSelect();
                    break;
                case RanchCompArcadeGame.PhaseIntro:
                    Banner(t.T(Key("intro"), ("n", g.LevelId)), RanchCompArcadeGame.IntroSec);
                    PlaySfx("ranch_fanfare");
                    break;
                case RanchCompArcadeGame.PhasePlay:
                    var geist = RanchCompState.Geist(g.Save, g.GeistKey(g.LevelId));
                    if (geist.Count > 0) View.Callout(t.T("mg.ranchZeit.geist_zeit", ("s", RanchCompKatalog.Num(geist["wert"], 0.0).ToString("0.0", CultureInfo.InvariantCulture))), MgScenery.Hex(0x5FA8A0));
                    else View.Callout(t.T(Key("level"), ("n", g.LevelId)), Gold);
                    if (!_hintShown)
                    {
                        _hintShown = true;
                        Banner(t.T("mg.rcomp.steuerung"), 3.2);
                    }
                    Sfx.Play("game_go");
                    break;
                case RanchCompArcadeGame.PhaseEnd:
                    if (g.LastStars > 0)
                    {
                        PlaySfx(g.LastStars >= 3 || g.LastGeistNeu ? "ranch_fanfare_sieg" : "game_win");
                        PlaySfx("ranch_menge_jubel", 0f, 0.6f);
                        if (g.LastStars >= 3 || g.LastGeistNeu) ConfettiRain(60);
                    }
                    else
                    {
                        PlaySfx("game_lose");
                        Shake(0.3f);
                    }
                    BuildEndCard();
                    break;
            }
        }

        private void BuildEndCard()
        {
            var t = Host.Strings;
            var g = Game;
            var won = g.LastStars > 0;
            var card = Ux.Col("gb-card");
            card.name = "rcomp-end";
            card.style.alignItems = Align.Center;
            card.style.paddingLeft = 28; card.style.paddingRight = 28; card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.minWidth = Length.Percent(44f);
            var wertung = g.LastWertung.ToString("0.0", CultureInfo.InvariantCulture);
            card.Add(Ux.Text(won ? t.T(Key("geschafft"), ("s", wertung), ("stars", g.LastStars)) : t.T(Key("zu_langsam")), "gb-h3", "gb-bold"));
            var ziel = t.T("rcomp.einweisung.richtzeit", ("s", g.Ziel(g.LevelId).ToString("0.0", CultureInfo.InvariantCulture))) + " · " + t.T("rcomp.ergebnis.zeit", ("s", wertung));
            var sub = Ux.Text(ziel, "gb-sm");
            sub.style.marginTop = 4;
            card.Add(sub);
            if (won)
            {
                var stars = new P.Canvas { Paint = p => { for (var i = 0; i < 3; i++) P.Star(p, new Vector2(26f + i * 46f, 24f), 19f, i < (Game?.LastStars ?? 0) ? Gold : new Color(0.85f, 0.8f, 0.74f)); } };
                stars.style.width = 144; stars.style.height = 48; stars.style.marginTop = 8;
                card.Add(stars);
                if (g.LastGeistNeu)
                {
                    var neu = Ux.Text(t.T("mg.ranchZeit.neuer_geist"), "gb-bold");
                    neu.style.color = MgScenery.Hex(0x5FA8A0);
                    card.Add(neu);
                }
            }
            var row = Ux.Row();
            row.style.marginTop = 14;
            if (won && g.LevelId < RanchCompArcade.LevelAnzahl) row.Add(EndButton("rcomp-next", t.T("gvz.end.next"), "gb-btn-primary", () => Game.Next()));
            row.Add(EndButton("rcomp-again", t.T("gvz.end.retry"), won ? "gb-btn" : "gb-btn-primary", () => Game.Retry()));
            row.Add(EndButton("rcomp-to-select", t.T("gvz.end.select"), "gb-btn", () => Game.BackToSelect()));
            card.Add(row);
            _overlay.Add(card);
            _overlay.style.display = DisplayStyle.Flex;
            if (FieldW > 1f) ScalePop(card, 1.1f);
        }

        private static Button EndButton(string name, string label, string cls, Action action)
        {
            var b = Ux.Btn(label, () => { Sfx.PlayUi("soft_confirm"); action(); }, cls);
            b.name = name;
            b.style.marginLeft = 6; b.style.marginRight = 6;
            return b;
        }

        protected override void TickGame(double dt)
        {
            Game.Tick(dt);
            View.Tick(dt);
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = Game;
            TimeLabel.text = g.Lauf == null || g.Phase == RanchCompArcadeGame.PhaseSelect
                ? t.T(Key("title"))
                : t.T("rcomp.hud.zeit", ("s", g.Lauf.Zeit.ToString("0.0", CultureInfo.InvariantCulture)));
            ScoreLabel.text = g.SessionScore.ToString();
            View.Render();
        }

        protected override int TakeFinalScore()
        {
            var score = Game?.FinalScore ?? 0;
            View?.Dispose();
            ClearGame();
            return score;
        }

        protected override void DropGame()
        {
            View?.Dispose();
            ClearGame();
        }

        protected static Color OutlineColor => Outline;
    }

    /// <summary>Barrel race (ranchTonnen): cloverleaf around three barrels; the ideal time drops 26 → 17 s over the runs.</summary>
    public sealed class RanchTonnenScreen : RanchCompArcadeScreen
    {
        private RanchTonnenGame _game;

        public RanchTonnenScreen(GameHost host) : base(host, "ranchTonnen", "L") { }

        protected override RanchCompArcadeGame Game => _game;
        protected override RanchCompArcadeGame CreateGame(int seed) => _game = new RanchTonnenGame(Difficulty, seed, Host?.Store);
        protected override void ClearGame() => _game = null;

        protected override void PaintCardArt(Painter2D p, int levelId, Rect box)
        {
            var c = box.center;
            var s = Mathf.Min(box.width, box.height) / 30f;
            Vector2 Px(V2 w) => c + new Vector2(w.X * s, w.Y * s);
            var spur = new Color(0.85f, 0.62f, 0.38f, 0.85f);
            var pfad = new[] { new V2(0f, 13f), new V2(5f, -2f), new V2(9f, 2f), new V2(-9f, 2f), new V2(-5f, -2f), new V2(-4f, -13f), new V2(0f, -9f), new V2(0f, 13f) };
            for (var i = 0; i + 1 < pfad.Length; i++) P.Line(p, Px(pfad[i]), Px(pfad[i + 1]), 1.4f, spur);
            foreach (var b in RcompKurs.TonnenPositionen)
            {
                P.Ring(p, Px(b), 4f * s, 1.4f, spur);
                P.Circle(p, Px(b), 4.6f, OutlineColor);
                P.Circle(p, Px(b), 3.6f, MgScenery.Hex(0x5C8CC7));
            }
        }
    }

    /// <summary>Time trial (ranchZeit): through every flag gate across the meadow, against the clock and the own ghost.</summary>
    public sealed class RanchZeitScreen : RanchCompArcadeScreen
    {
        private RanchZeitGame _game;

        public RanchZeitScreen(GameHost host) : base(host, "ranchZeit", "S") { }

        protected override RanchCompArcadeGame Game => _game;
        protected override RanchCompArcadeGame CreateGame(int seed) => _game = new RanchZeitGame(Difficulty, seed, Host?.Store);
        protected override void ClearGame() => _game = null;

        protected override void PaintCardArt(Painter2D p, int levelId, Rect box)
        {
            var route = RanchCompArcade.ZeitRoute(levelId);
            var s = Mathf.Min(box.width / 110f, box.height / 120f);
            var c = box.center;
            Vector2 Px(V2 w) => c + new Vector2(w.X * s, w.Y * s);
            var vorher = Px(RanchCompArcade.ZeitStart);
            foreach (var tor in route)
            {
                var jetzt = Px(tor.Pos);
                P.Line(p, vorher, jetzt, 1.6f, new Color(0.85f, 0.62f, 0.38f, 0.85f));
                vorher = jetzt;
            }
            foreach (var tor in route) P.Circle(p, Px(tor.Pos), 2.4f, MgScenery.Hex(0xE8676B));
            P.Circle(p, Px(RanchCompArcade.ZeitStart), 3.2f, OutlineColor);
        }
    }
}
