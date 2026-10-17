using System;
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
    /// Sheep herding (ranchHerde) on the shared minigame frame — port of herde_game.gd as a painted top-down paddock:
    /// level select (10 levels, stars by time left) → herding → all penned / time up → next/retry/select; "Fertig"
    /// books the session. Tap or drag on the meadow: Gooby rides there, the flock flees and the herding twist nudges it
    /// toward the yellow gate in the pen's south wall. Mechanics: certified <see cref="RanchHerde"/> boids.
    /// </summary>
    public sealed partial class RanchHerdeScreen : MinigameHostScreen
    {
        public const double ViewW = 960.0, ViewH = 540.0;
        private const float AreaX = 40f, AreaY = 70f, AreaW = 880f, AreaH = 452f;

        private RanchHerdeGame _game;
        private P.Canvas _canvas;
        private VisualElement _select, _grid, _overlay, _chip;
        private Label _selectStars, _drinLabel;
        private bool _down;
        private float _anim;
        private V2 _riderPrev;
        private float _riderYaw;

        private static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        private static readonly Color Cream = MgScenery.Hex(0xF9EDD6);
        private static readonly Color Wool = MgScenery.Hex(0xFFFDF7);
        private static readonly Color Wood = MgScenery.Hex(0xB98552);
        private static readonly Color WoodDark = MgScenery.Hex(0x8A5E36);
        private static readonly Color Gold = MgScenery.Hex(0xFFD34D);
        private static readonly Color Horse = MgScenery.Hex(0xA8714A);

        public RanchHerdeScreen(GameHost host) : base(host, "ranchHerde", "ranchHerde") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE3A6);
        protected override Color FieldColor => MgScenery.Hex(0x9ED37A);
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;

        public RanchHerdeGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new RanchHerdeGame(Difficulty, seed, Host?.Store);
            _game.PhaseChanged += OnPhase;
            _game.SheepIn += OnSheepIn;
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_coin");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            _canvas = new P.Canvas { Paint = Paint, name = "herde-paddock" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null || !_game.AcceptsInput || e.target != Field) return;
                _down = true;
                Field.CapturePointer(e.pointerId);
                _game.SetZiel(ToWorld(e.position), true);
                if (_stage != null) StageTap();
            });
            Field.RegisterCallback<PointerMoveEvent>(e => { if (_down && _game != null) _game.SetZiel(ToWorld(e.position), false); });
            Field.RegisterCallback<PointerUpEvent>(e => { _down = false; if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId); });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _down = false);

            _chip = Ux.Row("gb-card");
            _chip.name = "herde-hud";
            _chip.pickingMode = PickingMode.Ignore;
            _chip.style.position = Position.Absolute;
            _chip.style.top = 10;
            _chip.style.left = Length.Percent(50);
            _chip.style.translate = new Translate(Length.Percent(-50), 0);
            _chip.style.paddingLeft = 16; _chip.style.paddingRight = 16; _chip.style.paddingTop = 6; _chip.style.paddingBottom = 6;
            _drinLabel = Ux.Text("", "gb-bold", "gb-nowrap");
            _drinLabel.pickingMode = PickingMode.Ignore;
            _chip.Add(_drinLabel);
            Field.Add(_chip);

            _select = Ux.Col();
            _select.name = "herde-select-screen";
            Ux.Abs(_select, 0, 0, 0, 0);
            _select.style.paddingLeft = Length.Percent(4); _select.style.paddingRight = Length.Percent(4);
            _select.style.paddingTop = 60; _select.style.paddingBottom = 16;
            _select.style.backgroundColor = new Color(0.98f, 0.97f, 0.92f, 0.86f);
            var head = Ux.Row();
            head.style.alignItems = Align.Center;
            head.style.justifyContent = Justify.SpaceBetween;
            head.Add(Ux.Text(t.T("mg.ranchHerde.title"), "gb-h1", "gb-nowrap"));
            _selectStars = Ux.Text("", "gb-bold", "gb-nowrap");
            head.Add(_selectStars);
            _select.Add(head);
            var hint = Ux.Text(t.T("ranchplay.select.hint_herde"), "gb-sm");
            hint.style.marginTop = 4;
            _select.Add(hint);
            _grid = Ux.Row();
            _grid.name = "herde-levels";
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.flexGrow = 1;
            _grid.style.alignContent = Align.FlexStart;
            _grid.style.marginTop = 12;
            _select.Add(_grid);
            var foot = Ux.Row();
            foot.style.justifyContent = Justify.FlexEnd;
            var done = Ux.Btn(t.T("ranchplay.select.done"), () => { Sfx.PlayUi("soft_confirm"); _game?.Finish(); }, "gb-btn-primary");
            done.name = "herde-done";
            foot.Add(done);
            _select.Add(foot);
            Field.Add(_select);

            _overlay = Ux.El();
            _overlay.name = "herde-overlay";
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
            _selectStars.text = t.T("ranchplay.select.stars", ("n", RanchSpieleProgress.TotalStars(_game.Save, RanchSpieleProgress.Herde)), ("max", RanchHerde.LevelCount * 3));
            _grid.Clear();
            var open = RanchSpieleProgress.MaxUnlocked(_game.Save, RanchSpieleProgress.Herde);
            for (var id = 1; id <= RanchHerde.LevelCount; id++)
            {
                var levelId = id;
                var unlocked = id <= open;
                var stars = RanchSpieleProgress.LevelStars(_game.Save, RanchSpieleProgress.Herde, id);
                var sheep = (int)RanchHerde.Num(RanchHerde.LevelById(_game.Levels, id), "schafe", 3);
                var card = new Button(() =>
                {
                    if (!_game.IsUnlocked(levelId)) { Sfx.PlayUi("soft_error"); return; }
                    Sfx.PlayUi("soft_confirm");
                    _game.Open(levelId);
                }) { text = "" };
                card.ClearClassList();
                card.AddToClassList("gb-card");
                card.name = "herde-level-" + id;
                card.style.width = Length.Percent(18.4f);
                card.style.height = Length.Percent(40f);
                card.style.marginRight = Length.Percent(1.6f);
                card.style.marginBottom = 10;
                card.style.alignItems = Align.Center;
                card.style.paddingTop = 14;
                if (!unlocked) card.style.opacity = 0.55f;
                var num = Ux.Text(id.ToString(), "gb-h2", "gb-bold");
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
                        for (var i = 0; i < Mathf.Min(sheep, 8); i++) SheepIcon(p, new Vector2(w * 0.5f + (i - Mathf.Min(sheep, 8) * 0.5f + 0.5f) * 13f, h * 0.3f), 5.5f);
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
            _select.style.display = phase == RanchHerdeGame.PhaseSelect ? DisplayStyle.Flex : DisplayStyle.None;
            _chip.style.display = phase == RanchHerdeGame.PhaseSelect ? DisplayStyle.None : DisplayStyle.Flex;
            _overlay.style.display = DisplayStyle.None;
            _overlay.Clear();
            _down = false;
            switch (phase)
            {
                case RanchHerdeGame.PhaseSelect: RefreshSelect(); break;
                case RanchHerdeGame.PhasePlay:
                    _riderPrev = _game.Reiter;
                    _riderYaw = Mathf.PI;
                    Banner(t.T("mg.ranchHerde.hint"), RanchHerdeGame.IntroSec + 1.2);
                    break;
                case RanchHerdeGame.PhaseWon:
                    StageEnd(true);
                    PlaySfx("game_win");
                    ConfettiRain(60);
                    BuildEndCard(true);
                    break;
                case RanchHerdeGame.PhaseLost:
                    StageEnd(false);
                    PlaySfx("game_lose");
                    Shake(0.35f);
                    BuildEndCard(false);
                    break;
            }
        }

        private void BuildEndCard(bool won)
        {
            var t = Host.Strings;
            var card = Ux.Col("gb-card");
            card.name = "herde-end";
            card.style.alignItems = Align.Center;
            card.style.paddingLeft = 28; card.style.paddingRight = 28; card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.minWidth = Length.Percent(42f);
            card.Add(Ux.Text(won ? t.T("mg.ranchHerde.geschafft", ("stars", _game.LastStars), ("score", _game.LastScore)) : t.T("mg.ranchHerde.zeit_um"), "gb-h3", "gb-bold"));
            if (won)
            {
                var stars = new P.Canvas { Paint = p => { for (var i = 0; i < 3; i++) P.Star(p, new Vector2(26f + i * 46f, 24f), 19f, i < (_game?.LastStars ?? 0) ? Gold : new Color(0.85f, 0.8f, 0.74f)); } };
                stars.style.width = 144; stars.style.height = 48; stars.style.marginTop = 8;
                card.Add(stars);
            }
            var row = Ux.Row();
            row.style.marginTop = 14;
            if (won && _game.LevelId < RanchHerde.LevelCount) row.Add(EndButton("herde-next", t.T("gvz.end.next"), "gb-btn-primary", () => _game.Next()));
            row.Add(EndButton("herde-again", t.T("gvz.end.retry"), won ? "gb-btn" : "gb-btn-primary", () => _game.Retry()));
            row.Add(EndButton("herde-to-select", t.T("gvz.end.select"), "gb-btn", () => _game.BackToSelect()));
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

        private void OnSheepIn(int drin, int total)
        {
            PlaySfx("game_coin", Sfx.ComboSemitones(drin), 0.9f);
            Haptics.Tap();
            if (_stage != null) StageSheepIn(drin == total);
            if (FieldW > 1f && _game?.Level != null)
            {
                var gate = RanchHerde.TorPos(_game.Level);
                var at = _stage != null ? StageToField(gate, 1.4f) : WorldPx(gate);
                Burst(at, Gold, 12);
                Float(at + new Vector2(0f, -S(30f)), drin + "/" + total, MgScenery.Hex(0xD08A1E), 26f);
            }
            ScalePop(_chip, 1.15f);
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
            TimeLabel.text = g.Phase == RanchHerdeGame.PhaseSelect ? t.T("mg.ranchHerde.title") : t.T("mg.ranchHerde.zeit", ("s", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.SessionScore.ToString();
            if (g.Level != null) _drinLabel.text = t.T("mg.ranchHerde.drin", ("n", g.DrinAnzahl), ("max", g.Schafe.Count));
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

        // ── world ↔ field ──

        private float Scale => Mathf.Max(0.0001f, Mathf.Min(FieldW / (float)ViewW, FieldH / (float)ViewH));
        private Vector2 Offset => new Vector2((FieldW - (float)ViewW * Scale) * 0.5f, (FieldH - (float)ViewH * Scale) * 0.5f);
        private float S(float v) => v * Scale;

        /// <summary>Metres per view unit for the current level: the field (±feld.x, ±feld.z) fills the area.</summary>
        private float MetrePx
        {
            get
            {
                var feld = RanchHerde.Feld(_game?.Level);
                return Mathf.Min(AreaW / (float)(feld.x * 2.0), AreaH / (float)(feld.z * 2.0));
            }
        }

        private Vector2 WorldView(float x, float z) => new Vector2(AreaX + AreaW * 0.5f + x * MetrePx, AreaY + AreaH * 0.5f + z * MetrePx);
        private Vector2 WorldPx(V2 w) => Offset + WorldView(w.X, w.Y) * Scale;
        private Vector2 Px(float x, float z) => Offset + WorldView(x, z) * Scale;
        private float M(float metres) => metres * MetrePx * Scale;

        /// <summary>Sheep and rider are drawn larger than their metre size so they read on a phone (mechanics unchanged).</summary>
        private const float ArtScale = 1.7f;
        private float A(float metres) => M(metres) * ArtScale;

        private V2 ToWorld(Vector3 panel)
        {
            if (_stage != null) return ToWorld3D(panel);
            var local = Field.WorldToLocal(panel);
            var v = (new Vector2(local.x, local.y) - Offset) / Scale;
            return new V2((v.x - AreaX - AreaW * 0.5f) / MetrePx, (v.y - AreaY - AreaH * 0.5f) / MetrePx);
        }

        // ── painting ──

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            P.Rect(p, Offset, Offset + new Vector2((float)ViewW, (float)ViewH) * Scale, MgScenery.Hex(0x8FC66B));
            var level = _game.Level;
            if (level == null)
            {
                for (var i = 0; i < 40; i++) P.Line(p, Offset + new Vector2((i * 97) % 960, (i * 61) % 540) * Scale, Offset + new Vector2((i * 97) % 960 + 4, (i * 61) % 540 - 9) * Scale, S(2f), MgScenery.Hex(0x7DB35B));
                return;
            }
            var feld = RanchHerde.Feld(level);
            var fx = (float)feld.x;
            var fz = (float)feld.z;
            // meadow with mowing stripes and grass tufts
            for (var i = 0; i < 12; i++)
            {
                var x0 = -fx + i * fx * 2f / 12f;
                P.Rect(p, Px(x0, -fz), Px(x0 + fx * 2f / 12f, fz), i % 2 == 0 ? MgScenery.Hex(0xA4D67E) : MgScenery.Hex(0x98CC72));
            }
            for (var i = 0; i < 60; i++)
            {
                var x = -fx + (i * 7.31f) % (fx * 2f);
                var z = -fz + (i * 3.77f) % (fz * 2f);
                P.Line(p, Px(x, z), Px(x + 0.08f, z - 0.3f), M(0.06f), MgScenery.Hex(0x7DB35B));
                P.Line(p, Px(x + 0.12f, z), Px(x + 0.24f, z - 0.26f), M(0.06f), MgScenery.Hex(0x7DB35B));
            }
            for (var i = 0; i < 8; i++) P.Circle(p, Px(-fx + (i * 5.9f) % (fx * 2f), -fz + (i * 4.3f) % (fz * 2f)), M(0.12f), i % 2 == 0 ? Color.white : MgScenery.Hex(0xFFE06A));
            // paddock fence
            FenceRect(p, -fx, -fz, fx, fz, M(0.22f));
            PaintPen(p, level);
            // influence ring + target flag
            var reiter = _game.Reiter;
            P.Ring(p, WorldPx(reiter), M((float)_game.Tune.FluchtRadius), M(0.06f), new Color(1f, 1f, 1f, 0.22f));
            if (_game.Phase == RanchHerdeGame.PhasePlay)
            {
                var flag = WorldPx(_game.Ziel);
                P.Line(p, flag, flag + new Vector2(0f, -M(1.0f)), M(0.07f), WoodDark);
                P.Poly(p, new[] { flag + new Vector2(0f, -M(1.0f)), flag + new Vector2(M(0.6f), -M(0.82f)), flag + new Vector2(0f, -M(0.64f)) }, MgScenery.Hex(0xFF8B8B));
                P.Blob(p, flag, M(0.25f), M(0.1f), new Color(0f, 0f, 0f, 0.15f));
            }
            foreach (var s in _game.Schafe) PaintSheep(p, s);
            PaintRider(p, reiter);
        }

        private void FenceRect(Painter2D p, float x0, float z0, float x1, float z1, float w)
        {
            var a = Px(x0, z0); var b = Px(x1, z1);
            P.Line(p, new Vector2(a.x, a.y), new Vector2(b.x, a.y), w, Wood);
            P.Line(p, new Vector2(a.x, b.y), new Vector2(b.x, b.y), w, Wood);
            P.Line(p, new Vector2(a.x, a.y), new Vector2(a.x, b.y), w, Wood);
            P.Line(p, new Vector2(b.x, a.y), new Vector2(b.x, b.y), w, Wood);
            var step = Mathf.Max(1.2f, (x1 - x0) / 16f);
            for (var x = x0; x <= x1 + 0.01f; x += step) { P.Circle(p, Px(x, z0), w * 0.9f, WoodDark); P.Circle(p, Px(x, z1), w * 0.9f, WoodDark); }
            for (var z = z0; z <= z1 + 0.01f; z += step) { P.Circle(p, Px(x0, z), w * 0.9f, WoodDark); P.Circle(p, Px(x1, z), w * 0.9f, WoodDark); }
        }

        private void PaintPen(Painter2D p, Newtonsoft.Json.Linq.JObject level)
        {
            var pf = RanchHerde.Pferch.Of(level);
            var x0 = (float)(pf.X - pf.W * 0.5); var x1 = (float)(pf.X + pf.W * 0.5);
            var z0 = (float)(pf.Z - pf.T * 0.5); var z1 = (float)(pf.Z + pf.T * 0.5);
            P.Rect(p, Px(x0, z0), Px(x1, z1), MgScenery.Hex(0xD9C08E));
            for (var i = 0; i < 10; i++) P.Line(p, Px(x0 + 0.3f + (i * 0.73f) % (x1 - x0 - 0.6f), z0 + 0.3f + (i * 0.41f) % (z1 - z0 - 0.6f)), Px(x0 + 0.6f + (i * 0.73f) % (x1 - x0 - 0.6f), z0 + 0.35f + (i * 0.41f) % (z1 - z0 - 0.6f)), M(0.06f), MgScenery.Hex(0xE8D49C));
            var w = M(0.18f);
            var gl = (float)(pf.X - pf.Tor * 0.5); var gr = (float)(pf.X + pf.Tor * 0.5);
            P.Line(p, Px(x0, z0), Px(x1, z0), w, Wood);
            P.Line(p, Px(x0, z0), Px(x0, z1), w, Wood);
            P.Line(p, Px(x1, z0), Px(x1, z1), w, Wood);
            P.Line(p, Px(x0, z1), Px(gl, z1), w, Wood);
            P.Line(p, Px(gr, z1), Px(x1, z1), w, Wood);
            // the yellow gate: posts + pennants, pulsing while the flock is out
            var pulse = 1f + 0.08f * Mathf.Sin(_anim * 4f);
            foreach (var gx in new[] { gl, gr })
            {
                P.Circle(p, Px(gx, z1), M(0.28f) * pulse, Gold);
                P.Ring(p, Px(gx, z1), M(0.28f) * pulse, M(0.05f), Outline);
            }
            for (var i = 0; i < 5; i++)
            {
                var x = gl + (gr - gl) * (i + 0.5f) / 5f;
                P.Poly(p, new[] { Px(x - 0.14f, z1 - 0.02f), Px(x + 0.14f, z1 - 0.02f), Px(x, z1 + 0.3f) }, i % 2 == 0 ? MgScenery.Hex(0xFF8B8B) : Gold);
            }
        }

        private void PaintSheep(Painter2D p, RanchHerde.Schaf s)
        {
            var c = WorldPx(s.Pos);
            var speed = s.Vel.Length;
            var dir = speed > 0.05f ? new Vector2(s.Vel.X, s.Vel.Y) / speed : new Vector2(0f, 1f);
            var hop = speed > 0.5f ? Mathf.Abs(Mathf.Sin(_anim * 9f + (float)s.Phase)) * A(0.08f) : 0f;
            P.Blob(p, c + new Vector2(0f, A(0.25f)), A(0.42f), A(0.16f), new Color(0f, 0f, 0f, 0.15f));
            var body = c - new Vector2(0f, hop);
            foreach (var off in new[] { new Vector2(-0.18f, -0.08f), new Vector2(0.16f, -0.1f), new Vector2(-0.12f, 0.14f), new Vector2(0.18f, 0.12f), Vector2.zero })
                P.Circle(p, body + off * A(1f), A(0.24f), Outline);
            foreach (var off in new[] { new Vector2(-0.18f, -0.08f), new Vector2(0.16f, -0.1f), new Vector2(-0.12f, 0.14f), new Vector2(0.18f, 0.12f), Vector2.zero })
                P.Circle(p, body + off * A(1f), A(0.2f), s.Drin ? MgScenery.Hex(0xFFF6D8) : Wool);
            var head = body + dir * A(0.36f);
            P.Blob(p, head, A(0.15f), A(0.13f), MgScenery.Hex(0x3E3532));
            P.Circle(p, head + new Vector2(-dir.y, dir.x) * A(0.06f) + dir * A(0.04f), A(0.03f), Color.white);
            P.Circle(p, head - new Vector2(-dir.y, dir.x) * A(0.06f) + dir * A(0.04f), A(0.03f), Color.white);
        }

        private static void SheepIcon(Painter2D p, Vector2 c, float r)
        {
            P.Circle(p, c, r * 1.15f, Outline);
            P.Circle(p, c, r, Wool);
            P.Circle(p, c + new Vector2(r * 0.8f, 0f), r * 0.45f, MgScenery.Hex(0x3E3532));
        }

        private void PaintRider(Painter2D p, V2 reiter)
        {
            var moved = reiter - _riderPrev;
            if (moved.Length > 0.01f) _riderYaw = Mathf.Atan2(moved.Y, moved.X);
            _riderPrev = reiter;
            var c = WorldPx(reiter);
            var dir = new Vector2(Mathf.Cos(_riderYaw), Mathf.Sin(_riderYaw));
            var side = new Vector2(-dir.y, dir.x);
            var gallop = moved.Length > 0.01f ? Mathf.Sin(_anim * 14f) * A(0.05f) : 0f;
            P.Blob(p, c + new Vector2(0f, A(0.35f)), A(0.8f), A(0.26f), new Color(0f, 0f, 0f, 0.15f));
            P.RotBlob(p, c, A(0.78f) + A(0.05f), A(0.34f) + A(0.05f), _riderYaw, Outline);
            P.RotBlob(p, c, A(0.78f), A(0.34f), _riderYaw, Horse);
            var head = c + dir * A(0.88f) + new Vector2(0f, gallop);
            P.RotBlob(p, head, A(0.3f) + A(0.04f), A(0.17f) + A(0.04f), _riderYaw, Outline);
            P.RotBlob(p, head, A(0.3f), A(0.17f), _riderYaw, Horse);
            P.Line(p, c + dir * A(0.45f), c - dir * A(0.2f), A(0.12f), MgScenery.Hex(0x6B4A2F));
            P.Line(p, c - dir * A(0.72f), c - dir * A(1.0f) + side * A(0.1f), A(0.12f), MgScenery.Hex(0x6B4A2F));
            // Gooby in the saddle (seen from above): cream head with lop ears and the sprout
            var g = c - dir * A(0.08f) - new Vector2(0f, A(0.18f) + gallop);
            foreach (var sd in new[] { -1f, 1f })
            {
                P.RotBlob(p, g + side * sd * A(0.26f), A(0.1f) + A(0.03f), A(0.2f) + A(0.03f), _riderYaw + sd * 0.3f, Outline);
                P.RotBlob(p, g + side * sd * A(0.26f), A(0.1f), A(0.2f), _riderYaw + sd * 0.3f, Cream);
            }
            P.Circle(p, g, A(0.3f), Outline);
            P.Circle(p, g, A(0.26f), Cream);
            P.Circle(p, g + dir * A(0.1f) + side * A(0.09f), A(0.04f), Outline);
            P.Circle(p, g + dir * A(0.1f) - side * A(0.09f), A(0.04f), Outline);
            P.RotBlob(p, g - new Vector2(0f, A(0.28f)), A(0.09f), A(0.05f), -0.4f, MgScenery.Hex(0x8ED081));
        }
    }
}
