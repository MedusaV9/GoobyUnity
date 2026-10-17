using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Rohr-Wirrwarr host on the shared minigame frame (port of pipe_flow.gd + its garden panel): a 5×5 pipe board
    /// on a nursery bench, tap (north) to sprinkler (south). A tap rotates a tile; water tints everything on the
    /// tap, a solved path fills then the next puzzle. From puzzle 3 a joint drips. Game feel after pipe_flow.gd: a chip
    /// tick per turn (a brighter plop + splash when it hooks new pipe to the water), a plop climbing the scale with the
    /// fill wave, spray + perfect tone at the sprinkler on a solve (streak line there, confetti from three in a row),
    /// a red drip burst, knock and blue flash on a leak. With a GPU the board, pipes and garden are the 3D stage of
    /// <c>PipeFlowScreen.Stage3D.cs</c>; the 2D tiles stay as invisible tap squares.
    /// </summary>
    public sealed partial class PipeFlowScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private PipeFlowGame _game;
        private BoardCanvas _canvas;
        private readonly VisualElement[] _hits = new VisualElement[25];
        private VisualElement _gooby, _puzzlePill;
        private Label _puzzleLabel;
        private float _show;
        private float _cell, _ox, _oy;
        /// <summary>The fill wave's depth already answered (a rising plop per step).</summary>
        private int _fillSeen;

        private static readonly Color Sky = new Color(0.55f, 0.8f, 0.96f);
        private static readonly Color Bench = MgScenery.Hex(0xD9A77A);
        private static readonly Color Tile = new Color(0.93f, 0.9f, 0.82f);
        private static readonly Color Pipe = new Color(0.55f, 0.62f, 0.68f);
        private static readonly Color PipeWet = new Color(0.31f, 0.78f, 0.92f);
        private static readonly Color Water = new Color(0.31f, 0.85f, 0.97f);
        private static readonly Color Leak = new Color(0.95f, 0.43f, 0.43f);
        private static readonly Color Brass = new Color(0.78f, 0.58f, 0.28f);

        public PipeFlowScreen(GameHost host) : base(host, "pipeFlow", "pipeFlow") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        /// <summary>Every extra tap trims the efficiency bonus — only solved puzzles get the score pop.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.GainsOnly;
        protected override Color BackdropColor => MgScenery.Hex(0xF2C6AE);
        protected override Color FieldColor => Sky;

        public PipeFlowGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new PipeFlowGame(Difficulty, seed);
            _game.Solved_ += () => { Banner(Host.Strings.T("mg.pipeFlow.solved"), 1.4); OnSolved(); StageSolved(); };
            _game.Leaked += () => { Banner(Host.Strings.T("mg.pipeFlow.leak"), 1.3); OnLeaked(); StageLeaked(); };
            _game.PuzzleStarted += () => PlaySfx("game_whoosh", 2f, 0.6f);
            _game.Finished_ += RoundOver;
            _fillSeen = 0;
            Sfx.Preload("game_perfect", "game_whoosh", "soft_chip");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new BoardCanvas { Paint = Paint, name = "board" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            for (var i = 0; i < 25; i++)
            {
                var hit = new VisualElement { pickingMode = PickingMode.Position, name = "tile-" + i };
                hit.style.position = Position.Absolute;
                var idx = i;
                hit.RegisterCallback<PointerDownEvent>(_ => TapTile(idx));
                Field.Add(hit);
                _hits[i] = hit;
            }

            _gooby = Ux.Portrait(80);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            _puzzlePill = Ux.El("gb-card", "gb-row");
            _puzzlePill.name = "puzzle-pill";
            Ux.Abs(_puzzlePill, null, 62);
            _puzzlePill.style.left = Length.Percent(50);
            _puzzlePill.style.translate = new Translate(Length.Percent(-50), 0);
            _puzzlePill.style.alignItems = Align.Center;
            _puzzlePill.style.paddingTop = 6; _puzzlePill.style.paddingBottom = 6; _puzzlePill.style.paddingLeft = 14; _puzzlePill.style.paddingRight = 16;
            _puzzlePill.pickingMode = PickingMode.Ignore;
            _puzzleLabel = Ux.Text("", "gb-md", "gb-bold");
            _puzzlePill.Add(_puzzleLabel);
            Root.Add(_puzzlePill);

            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.pipeFlow.intro"), PipeFlowGame.IntroSec + 0.7);
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _show += (float)dt;
            WatchFill();
            TickHammer((float)dt);
        }

        // ── scenery (board 06, "Gartenmauer"): a sky strip with a sun and a cloud over a warm pastel brick garden wall
        //    with rounded coping stones and trailing ivy, the nursery bench (painted) in front of it, then a lawn strip
        //    and the flower bed the sprinkler waters, with tulips, daisies and pink flowers. A layer behind the board
        //    canvas; built once, pointer-transparent, nothing moves. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-pipeflow");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 14f);
            var sun = MgScenery.Sun(scene, 32f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, null, 1.5f, 6f);
            var cloud = MgScenery.Cloud(scene, 80f);
            MgScenery.Pct(cloud, 8f, 2f);
            var wall = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, MgScenery.Hex(0xE9B89C));
                var bh = 22f; var bw = 46f;
                for (var row = 0; row * bh < r.height; row++)
                {
                    var off = row % 2 == 0 ? 0f : bw * 0.5f;
                    for (var x = -off; x < r.width; x += bw)
                    {
                        var c = MgScenery.Hash(row * 31 + (int)(x / bw), 91) > 0.5f ? MgScenery.Hex(0xF6CDB4) : MgScenery.Hex(0xF2C2A6);
                        MgScenery.FillRect(p, x + 2f, row * bh + 2f, bw - 4f, bh - 4f, c);
                        MgScenery.FillRect(p, x + 4f, row * bh + 3f, bw - 12f, 3f, new Color(1f, 1f, 1f, 0.25f));
                    }
                }
            }, "gb-mgf-bricks");
            wall.name = "mgf-wall";
            MgScenery.Pct(wall, 0, 13f, 0, 27f);
            var cap = MgScenery.Shape(scene, MgScenery.Hex(0xEFE2D0), 8f, "gb-mgf-coping");
            MgScenery.Pct(cap, -2f, 11.6f, -2f);
            cap.style.height = 16f;
            MgScenery.Outline(cap, 3f, MgScenery.Hex(0xD6C4AC));
            var ivy = MgScenery.Painted(scene, (p, r) =>
            {
                for (var s = 0; s < 2; s++)
                {
                    var x0 = s == 0 ? 6f : r.width - 6f; var dir = s == 0 ? 1f : -1f;
                    p.strokeColor = MgScenery.Hex(0x6FA866); p.lineWidth = 3f;
                    p.BeginPath(); p.MoveTo(new Vector2(x0, 0f)); p.BezierCurveTo(new Vector2(x0 + dir * 30f, r.height * 0.3f), new Vector2(x0 - dir * 4f, r.height * 0.6f), new Vector2(x0 + dir * 18f, r.height)); p.Stroke();
                    for (var i = 0; i < 9; i++)
                    {
                        var k = i / 8f;
                        var at = new Vector2(x0 + dir * (10f + Mathf.Sin(k * 5f) * 14f), r.height * k);
                        MgScenery.Oval(p, at + new Vector2(dir * 7f, 0f), 8f, 6f, i % 2 == 0 ? MgScenery.Hex(0x7CC486) : MgScenery.Hex(0x9ED499), MgScenery.Hex(0x5FA866), 1.5f);
                    }
                }
            }, "gb-mgf-ivy");
            MgScenery.Pct(ivy, 0, 12.5f, 0);
            ivy.style.height = Length.Percent(34f);
            var lawn = MgScenery.Shape(scene, MgScenery.Hex(0xA6DB98), 0f, "gb-mgf-meadow");
            MgScenery.Pct(lawn, 0, 72f, 0, 0);
            lawn.style.borderTopColor = MgScenery.Hex(0x8CCF8A);
            var bed = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, MgScenery.Hex(0xB98B6A));
                for (var y = 10f; y < r.height; y += 18f) MgScenery.FillRect(p, 0f, y, r.width, 5f, MgScenery.Hex(0xA67A5C));
                MgScenery.FillRect(p, 0f, 0f, r.width, 7f, MgScenery.Hex(0xD6A987));
            }, "gb-mgf-bed");
            MgScenery.Pct(bed, 0, 86f, 0, 0);
            string[] bloom = { "plant_tulips_red", "plant_flowers_pink", "plant_daisies", "plant_tulips_white", "plant_flowers_pink", "plant_tulips_red" };
            for (var i = 0; i < bloom.Length; i++)
            {
                var b = MgScenery.Sprite(scene, bloom[i], 44f, 46f);
                MgScenery.Pct(b, 1f + i * 16.5f, null, null, 7f + (i % 2) * 2f);
            }
            var tuftC = MgScenery.Hex(0x7CC486);
            for (var i = 0; i < 6; i++)
            {
                var tuft = MgScenery.Tuft(lawn, tuftC, 0.75f);
                MgScenery.Pct(tuft, 4f + i * 17f, null);
                tuft.style.top = -12f;
            }
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var n = g.Size;
            LayoutBoard();
            for (var i = 0; i < n * n; i++)
            {
                var col = i % n; var row = i / n;
                _hits[i].style.left = _ox + col * _cell;
                _hits[i].style.top = _oy + row * _cell;
                _hits[i].style.width = _cell;
                _hits[i].style.height = _cell;
            }
            _gooby.style.left = FieldW * 0.5f - 40f;
            _gooby.style.top = FieldH * 0.82f;
            TimeLabel.text = g.Tune.Endless
                ? t.T("mg.pipeFlow.failures", ("n", g.Failures), ("max", g.Tune.EndlessFailureLimit))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            _puzzleLabel.text = t.T("mg.pipeFlow.puzzle", ("n", g.PuzzleNo)) + " · " + t.T("mg.pipeFlow.taps", ("n", g.TotalTaps));
            if (_stage != null) RenderStage();
            _canvas.MarkDirtyRepaint();
        }

        /// <summary>The board's cell size and top-left corner in field pixels (under the pill, above the bed).</summary>
        private void LayoutBoard()
        {
            var n = _game.Size;
            var top = FieldH * 0.16f;
            var bottom = FieldH * 0.22f;
            var avail = Mathf.Min(FieldW * 0.88f, FieldH - top - bottom);
            _cell = avail / n;
            var board = _cell * n;
            _ox = (FieldW - board) * 0.5f;
            _oy = top + (FieldH - top - bottom - board) * 0.35f;
        }

        private Vector2 CellCentre(int i)
        {
            var n = _game.Size;
            return new Vector2(_ox + (i % n + 0.5f) * _cell, _oy + (i / n + 0.5f) * _cell);
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f) return;
            var g = _game;
            var n = g.Size;
            if (_stage != null) return;
            // the sky, the garden wall with its ivy, the lawn and the flower bed are the scenery layer behind the canvas

            var board = _cell * n;
            Poly(p, Rect(new Vector2(_ox + board * 0.5f, _oy + board * 0.5f), board * 0.52f + 10f, board * 0.52f + 10f), Bench);

            // faucet over the source column
            var tap = new Vector2(_ox + (g.Board.SrcCol + 0.5f) * _cell, _oy - _cell * 0.28f);
            p.strokeColor = Brass; p.lineWidth = 7f;
            Line(p, tap + new Vector2(0f, -18f), tap + new Vector2(0f, 8f));
            Line(p, tap + new Vector2(-14f, -18f), tap + new Vector2(14f, -18f));
            Ellipse(p, tap + new Vector2(0f, 10f), 7f, 5f, Water);

            for (var i = 0; i < n * n; i++)
            {
                var at = CellCentre(i);
                var wet = g.TileWatered(i) || g.TileReached(i);
                Poly(p, Rect(at, _cell * 0.46f, _cell * 0.46f), wet ? Color.Lerp(Tile, PipeWet, 0.35f) : Tile);
                DrawPipe(p, at, g.Board.Tiles[i], g.Spin[i], wet);
            }

            var spr = new Vector2(_ox + (g.Board.GoalCol + 0.5f) * _cell, _oy + board + _cell * 0.22f);
            Ellipse(p, spr, 16f, 8f, Brass);
            if (g.Filling)
            {
                for (var k = 0; k < 7; k++)
                {
                    var a = -0.4f + k * 0.13f;
                    var r = 18f + 10f * Mathf.Abs(Mathf.Sin(_show * 6f + k));
                    p.strokeColor = new Color(Water.r, Water.g, Water.b, 0.55f); p.lineWidth = 2.5f;
                    Line(p, spr, spr + new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r * 0.7f + 8f));
                }
            }

            if (g.LeakPending)
            {
                var c = CellCentre(g.LeakIndex);
                var ratio = Mathf.Clamp01((float)(g.PuzzleElapsed / Math.Max(0.35, g.Tune.LeakSec)));
                p.strokeColor = Leak; p.lineWidth = 4f;
                p.BeginPath();
                var steps = 22;
                for (var s = 0; s <= steps; s++)
                {
                    var t = s / (float)steps * ratio;
                    var a = -Mathf.PI * 0.5f + t * Mathf.PI * 2f;
                    var pt = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (_cell * 0.42f);
                    if (s == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.Stroke();
                var drop = (_show * 40f) % (_cell * 0.5f);
                Ellipse(p, c + new Vector2(0f, drop), 4f, 5f, Water);
            }
        }

        private void DrawPipe(Painter2D p, Vector2 at, PipeFlow.Tile tile, double spin, bool wet)
        {
            var vis = tile.Rot - (float)(spin / Math.Max(1e-6, _game.Tune.RotateSec));
            var color = wet ? PipeWet : Pipe;
            p.strokeColor = color;
            p.lineWidth = Mathf.Max(8f, _cell * 0.18f);
            var len = _cell * 0.42f;
            var mask = PipeFlow.MaskOf(tile.Shape, tile.Rot);
            // openings follow the committed rot; the leftover spin just thickens the hub
            for (var d = 0; d < 4; d++)
            {
                if ((mask & (1 << d)) == 0) continue;
                var ang = -Mathf.PI * 0.5f + d * Mathf.PI * 0.5f - (float)spin / (float)Math.Max(1e-6, _game.Tune.RotateSec) * (Mathf.PI * 0.5f);
                Line(p, at, at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len);
            }
            Ellipse(p, at, _cell * 0.12f, _cell * 0.12f, color);
            _ = vis;
        }

        private void Band(Painter2D p, float y0, float y1, Color a, Color b)
        {
            const int steps = 5;
            for (var i = 0; i < steps; i++)
            {
                var t0 = i / (float)steps; var t1 = (i + 1) / (float)steps;
                Poly(p, new[] {
                    new Vector2(0f, Mathf.Lerp(y0, y1, t0)), new Vector2(FieldW, Mathf.Lerp(y0, y1, t0)),
                    new Vector2(FieldW, Mathf.Lerp(y0, y1, t1)), new Vector2(0f, Mathf.Lerp(y0, y1, t1)),
                }, Color.Lerp(a, b, (t0 + t1) * 0.5f));
            }
        }

        private static Vector2[] Rect(Vector2 c, float hx, float hy) => new[]
        {
            c + new Vector2(-hx, -hy), c + new Vector2(hx, -hy), c + new Vector2(hx, hy), c + new Vector2(-hx, hy),
        };

        private static void Poly(Painter2D p, Vector2[] pts, Color color)
        {
            p.fillColor = color;
            p.BeginPath(); p.MoveTo(pts[0]);
            for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath(); p.Fill();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b) { p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i <= 18; i++)
            {
                var a = Mathf.PI * 2f * i / 18f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Fill();
        }

        // ── juice (pipe_flow.gd _unhandled_input / _fill_tick / _solve / _apply_leak; the frame pops the pill on a solve) ──

        private static readonly Color Spray = new Color(0.5f, 0.85f, 0.95f);
        private static readonly Color Teal = new Color(0.2f, 0.62f, 0.66f);

        /// <summary>A tap on a tile: the engine turns it; a turn that hooks new pipe to the water plops brighter and splashes.</summary>
        private void TapTile(int idx)
        {
            var g = _game;
            if (g == null) return;
            var before = g.Reach.Count;
            if (!g.Tap(idx)) return;
            PlaySfx("soft_chip", (idx % 5) * 0.7f, 0.8f);
            StageTapped();
            if (g.Filling || g.Reach.Count <= before || _cell <= 0f) return;
            Burst(CellCentre(idx), Water, 6);
            PlaySfx("game_pop", Mathf.Min(12f, g.Reach.Count * 0.5f), 0.8f);
        }

        /// <summary>The fill wave runs the connected line in BFS steps: each step plops a semitone higher, its tiles spray.</summary>
        private void WatchFill()
        {
            var g = _game;
            if (g == null) return;
            if (!g.Filling) { _fillSeen = 0; return; }
            if (g.FillDepth <= _fillSeen) return;
            _fillSeen = g.FillDepth;
            PlaySfx("game_pop", Sfx.ComboSemitones(_fillSeen), 0.55f);
            if (ReducedMotion || _cell <= 0f) return;
            foreach (var kv in g.Depths)
                if (kv.Value == _fillSeen - 1) Burst(CellCentre(kv.Key), Water, 3);
        }

        private Vector2 SprinklerAt() =>
            new Vector2(_ox + (_game.Board.GoalCol + 0.5f) * _cell, _oy + _cell * _game.Size + _cell * 0.22f);

        private void OnSolved()
        {
            var g = _game;
            if (g == null || _cell <= 0f) return;
            var at = SprinklerAt();
            Burst(at, Spray, 16);
            Burst(at, Teal, 10);
            Flash(new Color(0.6f, 0.9f, 1f, 0.16f), 0.2f);
            PlaySfx("game_perfect", Sfx.ComboSemitones(g.SolveStreak));
            ScalePop(_puzzlePill, 1.2f);
            if (g.SolveStreak >= 2)
                Float(at + new Vector2(0f, -_cell * 0.9f), Host.Strings.T("mg.game.streak", ("n", g.SolveStreak)), new Color(0.95f, 0.45f, 0.66f), 26f);
            if (g.SolveStreak >= 3)
            {
                ConfettiRain(50);
                Haptics.Success();
            }
            else Haptics.Tap();
        }

        /// <summary>The joint drips (−5 — the pill stays quiet on losses in this host, so the leak carries its own sound).</summary>
        private void OnLeaked()
        {
            var g = _game;
            if (g == null || _cell <= 0f || g.LeakIndex < 0) return;
            var at = CellCentre(g.LeakIndex);
            Burst(at, Leak, 10);
            Burst(at, Water, 8);
            Float(at, "−" + g.Tune.LeakPenalty, Leak, 28f);
            Flash(new Color(0.5f, 0.65f, 0.95f, 0.18f), 0.18f);
            if (!ReducedMotion) Shake(0.3f);
            PlaySfx("game_miss");
            Haptics.Warn();
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            _game = null;
            _puzzlePill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class BoardCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public BoardCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
