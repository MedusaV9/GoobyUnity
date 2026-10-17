using System;
using System.Linq;
using Gooby.Core.Json;
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
    /// Obstacle course (ranchParcours) on the shared minigame frame — port of parcours_game.gd as a painted side view of
    /// the riding arena: course select (K1–K10) → ride (hold "Galopp" for pace, tap "Sprung" or the field at the golden
    /// takeoff ring) → finish flag → stars/score; "Fertig" books the session. The jump is rated at the takeoff against the
    /// first obstacle inside the arc (<see cref="RanchParcours.BewerteSprung"/>); passing an obstacle on the ground
    /// knocks it (+2.5 s, tempo loss).
    /// </summary>
    public sealed partial class RanchParcoursScreen : MinigameHostScreen
    {
        public const double ViewW = 960.0, ViewH = 540.0;
        private const float PxPerM = 34f, RiderX = 270f, GroundY = 404f;

        private RanchParcoursGame _game;
        private P.Canvas _canvas;
        private VisualElement _select, _grid, _overlay, _chip, _controls;
        private Label _selectStars, _chipLabel;
        private float _anim;

        private static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        private static readonly Color Cream = MgScenery.Hex(0xF9EDD6);
        private static readonly Color Horse = MgScenery.Hex(0xA8714A);
        private static readonly Color HorseDark = MgScenery.Hex(0x7E5236);
        private static readonly Color Gold = MgScenery.Hex(0xFFD34D);
        private static readonly Color Rail = MgScenery.Hex(0xE35D5D);

        public RanchParcoursScreen(GameHost host) : base(host, "ranchParcours", "ranchParcours") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE3A6);
        protected override Color FieldColor => MgScenery.Hex(0xCFEFFF);
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;

        public RanchParcoursGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new RanchParcoursGame(Difficulty, seed, Host?.Store);
            _game.PhaseChanged += OnPhase;
            _game.Rated += OnRated;
            _game.Jumped += () => PlaySfx("soft_tap", 2f, 0.8f);
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            _canvas = new P.Canvas { Paint = Paint, name = "parcours-arena" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null || e.target != Field) return;
                _game.Jump();
            });

            _chip = Ux.Row("gb-card");
            _chip.name = "parcours-hud";
            _chip.pickingMode = PickingMode.Ignore;
            _chip.style.position = Position.Absolute;
            _chip.style.top = 10;
            _chip.style.left = Length.Percent(50);
            _chip.style.translate = new Translate(Length.Percent(-50), 0);
            _chip.style.paddingLeft = 16; _chip.style.paddingRight = 16; _chip.style.paddingTop = 6; _chip.style.paddingBottom = 6;
            _chipLabel = Ux.Text("", "gb-bold", "gb-nowrap");
            _chipLabel.pickingMode = PickingMode.Ignore;
            _chip.Add(_chipLabel);
            Field.Add(_chip);

            _controls = new VisualElement { name = "parcours-controls", pickingMode = PickingMode.Ignore };
            Ux.Abs(_controls, 0, 0, 0, 0);
            var gallop = Ux.El("gb-card");
            gallop.name = "parcours-galopp";
            gallop.style.position = Position.Absolute;
            gallop.style.left = 18; gallop.style.bottom = 16;
            gallop.style.width = 150; gallop.style.height = 70;
            gallop.style.alignItems = Align.Center; gallop.style.justifyContent = Justify.Center;
            gallop.Add(Ux.Text(t.T("mg.ranchParcours.galopp"), "gb-h3", "gb-bold"));
            gallop.RegisterCallback<PointerDownEvent>(e => { e.StopPropagation(); gallop.CapturePointer(e.pointerId); _game?.SetGalopp(true); gallop.AddToClassList("gb-arcade-card--pressed"); });
            gallop.RegisterCallback<PointerUpEvent>(e => { if (gallop.HasPointerCapture(e.pointerId)) gallop.ReleasePointer(e.pointerId); _game?.SetGalopp(false); gallop.RemoveFromClassList("gb-arcade-card--pressed"); });
            gallop.RegisterCallback<PointerCaptureOutEvent>(_ => { _game?.SetGalopp(false); gallop.RemoveFromClassList("gb-arcade-card--pressed"); });
            _controls.Add(gallop);
            var jump = Ux.El("gb-card");
            jump.name = "parcours-sprung";
            jump.style.position = Position.Absolute;
            jump.style.right = 18; jump.style.bottom = 16;
            jump.style.width = 150; jump.style.height = 70;
            jump.style.alignItems = Align.Center; jump.style.justifyContent = Justify.Center;
            jump.style.backgroundColor = MgScenery.Hex(0xFFE7A8);
            jump.Add(Ux.Text(t.T("mg.ranchParcours.sprung"), "gb-h3", "gb-bold"));
            jump.RegisterCallback<PointerDownEvent>(e => { e.StopPropagation(); _game?.Jump(); ScalePop(jump, 1.08f); });
            _controls.Add(jump);
            Field.Add(_controls);

            _select = Ux.Col();
            _select.name = "parcours-select-screen";
            Ux.Abs(_select, 0, 0, 0, 0);
            _select.style.paddingLeft = Length.Percent(4); _select.style.paddingRight = Length.Percent(4);
            _select.style.paddingTop = 60; _select.style.paddingBottom = 16;
            _select.style.backgroundColor = new Color(0.98f, 0.97f, 0.92f, 0.88f);
            var head = Ux.Row();
            head.style.alignItems = Align.Center;
            head.style.justifyContent = Justify.SpaceBetween;
            head.Add(Ux.Text(t.T("mg.ranchParcours.title"), "gb-h1", "gb-nowrap"));
            _selectStars = Ux.Text("", "gb-bold", "gb-nowrap");
            head.Add(_selectStars);
            _select.Add(head);
            var hint = Ux.Text(t.T("ranchplay.select.hint_parcours"), "gb-sm");
            hint.style.marginTop = 4;
            _select.Add(hint);
            _grid = Ux.Row();
            _grid.name = "parcours-levels";
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.flexGrow = 1;
            _grid.style.alignContent = Align.FlexStart;
            _grid.style.marginTop = 12;
            _select.Add(_grid);
            var foot = Ux.Row();
            foot.style.justifyContent = Justify.FlexEnd;
            var done = Ux.Btn(t.T("ranchplay.select.done"), () => { Sfx.PlayUi("soft_confirm"); _game?.Finish(); }, "gb-btn-primary");
            done.name = "parcours-done";
            foot.Add(done);
            _select.Add(foot);
            Field.Add(_select);

            _overlay = Ux.El();
            _overlay.name = "parcours-overlay";
            Ux.Abs(_overlay, 0, 0, 0, 0);
            _overlay.style.display = DisplayStyle.None;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0.29f, 0.23f, 0.21f, 0.32f);
            Field.Add(_overlay);
            OnPhase(_game.Phase);
        }

        private void RefreshSelect()
        {
            var t = Host.Strings;
            _selectStars.text = t.T("ranchplay.select.stars", ("n", RanchSpieleProgress.TotalStars(_game.Save, RanchSpieleProgress.Parcours)), ("max", RanchParcours.KursAnzahl * 3));
            _grid.Clear();
            var open = RanchSpieleProgress.MaxUnlocked(_game.Save, RanchSpieleProgress.Parcours);
            for (var id = 1; id <= RanchParcours.KursAnzahl; id++)
            {
                var kursId = id;
                var unlocked = id <= open;
                var stars = RanchSpieleProgress.LevelStars(_game.Save, RanchSpieleProgress.Parcours, id);
                var types = RanchParcours.Hindernisse(RanchParcours.KursById(_game.Kurse, id));
                var card = new Button(() =>
                {
                    if (!_game.IsUnlocked(kursId)) { Sfx.PlayUi("soft_error"); return; }
                    Sfx.PlayUi("soft_confirm");
                    _game.Open(kursId);
                }) { text = "" };
                card.ClearClassList();
                card.AddToClassList("gb-card");
                card.name = "parcours-level-" + id;
                card.style.width = Length.Percent(18.4f);
                card.style.height = Length.Percent(40f);
                card.style.marginRight = Length.Percent(1.6f);
                card.style.marginBottom = 10;
                card.style.alignItems = Align.Center;
                card.style.paddingTop = 14;
                if (!unlocked) card.style.opacity = 0.55f;
                var num = Ux.Text("K" + id, "gb-h2", "gb-bold");
                num.pickingMode = PickingMode.Ignore;
                card.Add(num);
                var art = new P.Canvas
                {
                    Paint = p =>
                    {
                        var w = card.contentRect.width;
                        var h = card.contentRect.height;
                        if (w <= 1f) return;
                        if (!unlocked)
                        {
                            var c = new Vector2(w * 0.5f, h * 0.35f);
                            P.Arc(p, c + new Vector2(0f, -3f), 6f, 180f, 360f, 3f, new Color(0.45f, 0.38f, 0.35f));
                            P.Rect(p, c + new Vector2(-8f, -3f), c + new Vector2(8f, 9f), new Color(0.45f, 0.38f, 0.35f));
                            return;
                        }
                        var n = Mathf.Min(types.Count, 8);
                        for (var i = 0; i < n; i++)
                        {
                            var c = new Vector2(w * 0.5f + (i - n * 0.5f + 0.5f) * 12f, h * 0.3f);
                            var col = types[i].typ == "wasser" ? MgScenery.Hex(0x6FB7E8) : types[i].typ == "hecke" ? MgScenery.Hex(0x6DB86B) : Rail;
                            P.RoundRect(p, c - new Vector2(4f, 6f), c + new Vector2(4f, 6f), 2f, col);
                        }
                        for (var i = 0; i < 3; i++) P.Star(p, new Vector2(w * 0.5f + (i - 1) * 20f, h * 0.62f), 8f, i < stars ? Gold : new Color(0.85f, 0.8f, 0.74f));
                    },
                };
                art.style.flexGrow = 1;
                art.style.alignSelf = Align.Stretch;
                card.Add(art);
                _grid.Add(card);
            }
        }

        private void OnPhase(string phase)
        {
            if (_select == null) return;
            var t = Host.Strings;
            _select.style.display = phase == RanchParcoursGame.PhaseSelect ? DisplayStyle.Flex : DisplayStyle.None;
            _chip.style.display = phase == RanchParcoursGame.PhaseSelect ? DisplayStyle.None : DisplayStyle.Flex;
            _controls.style.display = phase == RanchParcoursGame.PhasePlay ? DisplayStyle.Flex : DisplayStyle.None;
            _overlay.style.display = DisplayStyle.None;
            _overlay.Clear();
            switch (phase)
            {
                case RanchParcoursGame.PhaseSelect: RefreshSelect(); break;
                case RanchParcoursGame.PhasePlay: Banner(t.T("mg.ranchParcours.hint"), RanchParcoursGame.IntroSec + 1.2); break;
                case RanchParcoursGame.PhaseWon:
                    StageWon();
                    PlaySfx("game_win");
                    ConfettiRain(60);
                    BuildEndCard();
                    // the riding arena's course for the quests; three stars ride like a win, two like second place
                    RanchQuestUi.Melde(Host, "reite_strecke", "reitplatz_parcours");
                    if (_game.LastStars > 0) RanchQuestUi.Melde(Host, "gewinne_wettbewerb", "parcours", 1, 4 - _game.LastStars);
                    break;
            }
        }

        private void BuildEndCard()
        {
            var t = Host.Strings;
            var card = Ux.Col("gb-card");
            card.name = "parcours-end";
            card.style.alignItems = Align.Center;
            card.style.paddingLeft = 28; card.style.paddingRight = 28; card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.minWidth = Length.Percent(42f);
            card.Add(Ux.Text(t.T("mg.ranchParcours.geschafft", ("stars", _game.LastStars), ("score", _game.LastScore)), "gb-h3", "gb-bold"));
            var stars = new P.Canvas { Paint = p => { for (var i = 0; i < 3; i++) P.Star(p, new Vector2(26f + i * 46f, 24f), 19f, i < (_game?.LastStars ?? 0) ? Gold : new Color(0.85f, 0.8f, 0.74f)); } };
            stars.style.width = 144; stars.style.height = 48; stars.style.marginTop = 8;
            card.Add(stars);
            card.Add(Ux.Text(t.T("mg.ranchParcours.zeit", ("s", _game.Zeit.ToString("0.0"))), "gb-sm", "gb-bold"));
            var row = Ux.Row();
            row.style.marginTop = 14;
            if (_game.KursId < RanchParcours.KursAnzahl) row.Add(EndButton("parcours-next", t.T("gvz.end.next"), "gb-btn-primary", () => _game.Next()));
            row.Add(EndButton("parcours-again", t.T("gvz.end.retry"), "gb-btn", () => _game.Retry()));
            row.Add(EndButton("parcours-to-select", t.T("gvz.end.select"), "gb-btn", () => _game.BackToSelect()));
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

        private int _sprungGut, _sprungPerfekt;

        /// <summary>The jumps of the round train the farm's horse (jumping power; halved in the rain without a riding hall).</summary>
        protected override void NachAward(AwardResult award)
        {
            var (gut, perfekt) = (_sprungGut, _sprungPerfekt);
            _sprungGut = _sprungPerfekt = 0;
            if (Host?.Store == null || gut + perfekt == 0 || !RanchHof.IstGekauft(Host.Store.State)) return;
            var now = Host.Store.Clock.NowMs();
            var regen = RanchWeltState.RegnetJetzt(Host.Store.State, now, Host.Store.Clock.UtcOffsetMinutes(now));
            var mult = regen && !RanchBauEffekte.TrainingBeiRegen(Host.Store.State) ? RanchPferd.RegenStatMult : 1.0;
            var pid = RanchHof.ReitPferd(Host.Store.State);
            var tag = Host.Store.Clock.LocalDay(now);
            RanchPferd.RittBuchung b = null;
            Host.Store.Update(s => b = RanchPferd.RittAnPferd(s, pid, new RanchPferd.Telemetrie { SprungGut = gut, SprungPerfekt = perfekt }, tag, mult), GameHost.TicksMs);
            if (b == null) return;
            Host.Store.NotifySliceChanged(RanchHof.SliceId);
            Debug.Log("[parcours] training " + pid + " gut=" + gut + " perfekt=" + perfekt + (b.NeuePunkte.Count > 0 ? " sprungkraft+" + b.NeuePunkte.Values.Sum() : "") + (mult < 1.0 ? " (regen ×0,5)" : ""));
            var t = Host.Strings;
            foreach (var kv in b.NeuePunkte)
                Host.Ui.Toast(t.T("rpferd.levelup.punkt", ("stat", t.T("rpferd.stat." + kv.Key))), "star", 3.5f);
            if (mult < 1.0)
            {
                var p = RanchHof.Pferd(Host.Store.State, pid);
                var key = J.Str(p?["nameKey"]);
                Host.Ui.Toast(t.T("rpferd.training.regen", ("name", key.Length > 0 ? t.T(key) : J.Str(p?["name"]))), "rain", 4f);
            }
        }

        private void OnRated(RanchParcoursGame.Hindernis h, string quality, int points)
        {
            if (quality == "perfekt") _sprungPerfekt++;
            else if (quality == "gut") _sprungGut++;
            var t = Host.Strings;
            var at = _stage != null ? StageToField(h.At, 2.2f) : Px(ScreenXOf(h.At), GroundY - 70f);
            StageRated(quality);
            switch (quality)
            {
                case "perfekt":
                    PlaySfx("game_star", Sfx.ComboSemitones(_game.Kombo));
                    Banner(t.T("mg.ranchParcours.perfekt") + " +" + points, 1.0);
                    if (FieldW > 1f) Burst(at, Gold, 16);
                    Haptics.Tap();
                    break;
                case "gut":
                    PlaySfx("game_combo", Sfx.ComboSemitones(_game.Kombo), 0.8f);
                    if (FieldW > 1f) Float(at, t.T("mg.ranchParcours.gut") + " +" + points, MgScenery.Hex(0xD08A1E), 24f);
                    break;
                default:
                    PlaySfx("game_miss");
                    Shake(0.25f);
                    Haptics.Warn();
                    if (FieldW > 1f) Float(at, t.T("mg.ranchParcours.abwurf"), MgScenery.Hex(0xE0655F), 26f);
                    break;
            }
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _anim += (float)dt;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            TimeLabel.text = g.Phase == RanchParcoursGame.PhaseSelect ? t.T("mg.ranchParcours.title") : t.T("mg.ranchParcours.zeit", ("s", g.Zeit.ToString("0.0")));
            ScoreLabel.text = g.SessionScore.ToString();
            if (g.Kurs != null) _chipLabel.text = t.T("mg.ranchParcours.punkte", ("n", g.Punkte), ("kombo", g.Kombo));
            RenderStage();
            if (_stage == null) _canvas.MarkDirtyRepaint();
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            DropStage();
            _game = null;
            return score;
        }

        protected override void DropGame()
        {
            DropStage();
            _game = null;
        }

        // ── painting ──

        private float Scale => Mathf.Max(0.0001f, Mathf.Min(FieldW / (float)ViewW, FieldH / (float)ViewH));
        private Vector2 Offset => new Vector2((FieldW - (float)ViewW * Scale) * 0.5f, (FieldH - (float)ViewH * Scale) * 0.5f);
        private Vector2 Px(float x, float y) => Offset + new Vector2(x, y) * Scale;
        private float S(float v) => v * Scale;
        private float ScreenXOf(double metres) => RiderX + (float)(metres - (_game?.X ?? 0.0)) * PxPerM;

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            var camX = (float)_game.X;
            // sky + distant hills (parallax 0.15) + tribune with Gooby spectators (parallax 0.45)
            for (var i = 0; i < 6; i++) P.Rect(p, Px(0f, i * 40f), Px(960f, (i + 1) * 40f + 1f), Color.Lerp(MgScenery.Hex(0xBFE6FF), MgScenery.Hex(0xEAF8FF), i / 5f));
            P.Blob(p, Px(180f, 52f), S(50f), S(15f), new Color(1f, 1f, 1f, 0.85f));
            P.Blob(p, Px(640f, 70f), S(64f), S(17f), new Color(1f, 1f, 1f, 0.8f));
            for (var i = -1; i < 6; i++)
            {
                var x = ((i * 260f - camX * PxPerM * 0.15f) % 1300f + 1300f) % 1300f - 160f;
                P.Blob(p, Px(x, 250f), S(220f), S(70f), MgScenery.Hex(0xA9D98A));
            }
            for (var i = -1; i < 12; i++)
            {
                var x = ((i * 120f - camX * PxPerM * 0.45f) % 1440f + 1440f) % 1440f - 120f;
                P.Rect(p, Px(x, 230f), Px(x + 112f, 300f), MgScenery.Hex(0xE7C79B));
                P.Rect(p, Px(x - 4f, 222f), Px(x + 116f, 232f), MgScenery.Hex(0xE35D5D));
                for (var j = 0; j < 4; j++)
                {
                    var hop = Mathf.Abs(Mathf.Sin(_anim * 6f + i * 1.3f + j)) * S(3f);
                    var c = Px(x + 16f + j * 26f, 262f) - new Vector2(0f, hop);
                    P.Circle(p, c, S(9f), Outline);
                    P.Circle(p, c, S(7.5f), j % 2 == 0 ? Cream : MgScenery.Hex(0xFFD7E4));
                }
            }
            // arena: far rail, grass, sand track, near grass
            P.Rect(p, Px(0f, 300f), Px(960f, 372f), MgScenery.Hex(0x98CC72));
            for (var i = -1; i < 20; i++)
            {
                var x = ((i * 60f - camX * PxPerM * 0.8f) % 1200f + 1200f) % 1200f - 60f;
                P.Rect(p, Px(x, 336f), Px(x + 8f, 372f), Color.white);
            }
            P.Rect(p, Px(0f, 344f), Px(960f, 350f), Color.white);
            P.Rect(p, Px(0f, 372f), Px(960f, 440f), MgScenery.Hex(0xE9D3A6));
            for (var i = 0; i < 30; i++)
            {
                var x = ((i * 47f - camX * PxPerM) % 1410f + 1410f) % 1410f - 30f;
                P.Circle(p, Px(x, 386f + (i * 13 % 46)), S(2f), MgScenery.Hex(0xD9BE8C));
            }
            P.Rect(p, Px(0f, 440f), Px(960f, 540f), MgScenery.Hex(0x8FC66B));
            var kurs = _game.Kurs;
            if (kurs == null) return;
            // start + finish
            DrawFlagPole(p, ScreenXOf(0.0), false);
            DrawFlagPole(p, ScreenXOf(_game.Laenge), true);
            // takeoff ring for the next open obstacle
            var next = _game.NextObstacle();
            if (next != null && !next.Rating.HasValue && _game.Phase == RanchParcoursGame.PhasePlay)
            {
                var ideal = ScreenXOf(_game.IdealTakeoff(next));
                var window = Math.Abs(_game.X - _game.IdealTakeoff(next)) < RanchParcours.Base.PerfektM;
                var pulse = window ? 1f + 0.15f * Mathf.Sin(_anim * 14f) : 1f;
                P.Blob(p, Px(ideal, GroundY + 6f), S(26f) * pulse, S(8f) * pulse, window ? new Color(1f, 0.83f, 0.3f, 0.85f) : new Color(1f, 0.83f, 0.3f, 0.45f));
            }
            foreach (var h in _game.Hindernisse)
            {
                var x = ScreenXOf(h.At);
                if (x < -80f || x > 1040f) continue;
                DrawObstacle(p, h, x);
            }
            DrawRider(p);
        }

        private void DrawFlagPole(Painter2D p, float x, bool finish)
        {
            if (x < -60f || x > 1020f) return;
            P.Line(p, Px(x, GroundY + 10f), Px(x, GroundY - 120f), S(5f), Outline);
            var top = Px(x, GroundY - 120f);
            if (!finish)
            {
                P.Poly(p, new[] { top, top + new Vector2(S(40f), S(10f)), top + new Vector2(0f, S(22f)) }, MgScenery.Hex(0x6FB7E8));
                return;
            }
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 4; c++)
                    P.Rect(p, top + new Vector2(S(c * 11f), S(r * 9f)), top + new Vector2(S((c + 1) * 11f), S((r + 1) * 9f)), (r + c) % 2 == 0 ? Color.white : Outline);
        }

        private void DrawObstacle(Painter2D p, RanchParcoursGame.Hindernis h, float x)
        {
            var w = (float)(RanchParcours.HindernisBreite.TryGetValue(h.Typ, out var b) ? b : 0.4) * PxPerM;
            var knocked = h.Knocked;
            switch (h.Typ)
            {
                case "wasser":
                    P.Blob(p, Px(x, GroundY + 8f), S(w * 0.55f), S(9f), MgScenery.Hex(0x6FB7E8));
                    P.Blob(p, Px(x - w * 0.1f, GroundY + 6f), S(w * 0.3f), S(3f), new Color(1f, 1f, 1f, 0.6f));
                    P.Blob(p, Px(x - w * 0.6f, GroundY - 4f), S(10f), S(12f), MgScenery.Hex(0x6DB86B));
                    break;
                case "hecke":
                    P.Blob(p, Px(x, GroundY - 14f), S(w * 0.6f + 6f), S(22f), Outline);
                    P.Blob(p, Px(x, GroundY - 14f), S(w * 0.6f + 3f), S(19f), knocked ? MgScenery.Hex(0x8DAA6B) : MgScenery.Hex(0x6DB86B));
                    for (var i = 0; i < 4; i++) P.Circle(p, Px(x - w * 0.4f + i * w * 0.27f, GroundY - 26f), S(2.5f), MgScenery.Hex(0xFF8FB0));
                    break;
                default:
                    var fences = h.Typ == "oxer" ? new[] { -w * 0.5f, w * 0.5f } : new[] { 0f };
                    foreach (var off in fences)
                    {
                        var fx = x + off;
                        P.Line(p, Px(fx - 18f, GroundY + 4f), Px(fx - 18f, GroundY - 40f), S(5f), Color.white);
                        P.Line(p, Px(fx + 18f, GroundY + 4f), Px(fx + 18f, GroundY - 40f), S(5f), Color.white);
                        if (knocked)
                        {
                            P.Line(p, Px(fx - 24f, GroundY + 2f), Px(fx + 22f, GroundY - 6f), S(5f), Rail);
                            P.Line(p, Px(fx - 20f, GroundY + 4f), Px(fx + 26f, GroundY), S(5f), Color.white);
                        }
                        else
                        {
                            P.Line(p, Px(fx - 18f, GroundY - 30f), Px(fx + 18f, GroundY - 30f), S(5f), Rail);
                            P.Line(p, Px(fx - 18f, GroundY - 16f), Px(fx + 18f, GroundY - 16f), S(5f), Color.white);
                        }
                    }
                    break;
            }
            if (h.Rating.HasValue && !h.Gewertet) P.Star(p, Px(x, GroundY - 60f), S(7f), h.Rating.Value.Qualitaet == "perfekt" ? Gold : new Color(1f, 1f, 1f, 0.8f));
        }

        private void DrawRider(Painter2D p)
        {
            var lift = (float)_game.JumpY * PxPerM * 1.6f;
            var gallopRate = _game.Galopp ? 16f : 11f;
            var bob = _game.InLuft ? 0f : Mathf.Abs(Mathf.Sin(_anim * gallopRate)) * 4f;
            var c = Px(RiderX, GroundY - 34f - lift - bob);
            var s = Scale;
            P.Blob(p, Px(RiderX, GroundY + 6f), S(Mathf.Max(14f, 34f - lift * 0.2f)), S(6f), new Color(0f, 0f, 0f, 0.18f));
            // legs (tucked in the air), body, neck + head, tail
            var tuck = _game.InLuft ? 0.6f : 0f;
            var step = _game.InLuft ? 0f : Mathf.Sin(_anim * gallopRate) * 10f;
            foreach (var (lx, phase) in new[] { (-22f, 0f), (-12f, 1.6f), (16f, 3.1f), (26f, 4.7f) })
            {
                var swing = _game.InLuft ? 0f : Mathf.Sin(_anim * gallopRate + phase) * 8f;
                var knee = c + new Vector2(S(lx + swing * 0.5f), S(14f));
                var hoof = c + new Vector2(S(lx + swing - tuck * 10f * Mathf.Sign(lx)), S(30f - tuck * 14f));
                P.Line(p, c + new Vector2(S(lx), S(8f)), knee, S(6f), HorseDark);
                P.Line(p, knee, hoof, S(5f), HorseDark);
            }
            P.OutlinedBlob(p, c, S(36f), S(16f), S(2.5f), Horse, Outline);
            P.Line(p, c + new Vector2(S(26f), S(-6f)), c + new Vector2(S(40f), S(-24f)), S(11f), Outline);
            P.Line(p, c + new Vector2(S(26f), S(-6f)), c + new Vector2(S(40f), S(-24f)), S(8f), Horse);
            P.RotBlob(p, c + new Vector2(S(46f), S(-26f)), S(14f), S(8f), 0.35f, Outline);
            P.RotBlob(p, c + new Vector2(S(46f), S(-26f)), S(12f), S(6.5f), 0.35f, Horse);
            P.Circle(p, c + new Vector2(S(48f), S(-29f)), S(1.8f), Outline);
            P.Line(p, c + new Vector2(S(-34f), S(-4f)), c + new Vector2(S(-46f), S(8f + step * 0.2f)), S(6f), HorseDark);
            P.Line(p, c + new Vector2(S(32f), S(-16f)), c + new Vector2(S(38f), S(-30f)), S(4f), HorseDark);
            // saddle pad + Gooby rider
            P.RoundRect(p, c + new Vector2(S(-14f), S(-18f)), c + new Vector2(S(10f), S(-8f)), S(3f), MgScenery.Hex(0xFF8B8B));
            var g = c + new Vector2(S(-2f), S(-36f));
            P.RotBlob(p, g + new Vector2(S(-14f), S(4f)), S(5f) + S(1.5f), S(11f) + S(1.5f), 0.4f, Outline);
            P.RotBlob(p, g + new Vector2(S(-14f), S(4f)), S(5f), S(11f), 0.4f, Cream);
            P.OutlinedBlob(p, g, S(15f), S(14f), S(2f), Cream, Outline);
            P.Circle(p, g + new Vector2(S(6f), S(-2f)), S(2.2f), Outline);
            P.Circle(p, g + new Vector2(S(9f), S(4f)), S(3f), new Color(0.96f, 0.63f, 0.6f, 0.85f));
            P.RotBlob(p, g + new Vector2(S(2f), S(-17f)), S(5f), S(2.6f), -0.4f, MgScenery.Hex(0x8ED081));
            _ = s;
        }
    }
}
