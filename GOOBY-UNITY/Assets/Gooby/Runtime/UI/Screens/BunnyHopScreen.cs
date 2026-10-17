using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Hasenhüpfer host on the shared minigame frame (port of bunny_hop.gd + its stage): Gooby flies at the left
    /// third of a portrait sky over a meadow, hedge gates scroll in from the right, a tap anywhere hops. Coins sit
    /// in the gaps, the wind telegraphs and shoves, every tenth gate narrows the gap. A crash tumbles Gooby and
    /// ends the round. With a GPU the field is the 3D hedge meadow of <c>BunnyHopScreen.Stage3D.cs</c>.
    /// </summary>
    public sealed partial class BunnyHopScreen : MinigameHostScreen
    {
        private BunnyHopGame _game;
        private readonly Dictionary<BunnyHopGame.Pillar, (VisualElement top, VisualElement bottom)> _pillars = new Dictionary<BunnyHopGame.Pillar, (VisualElement, VisualElement)>();
        private readonly Dictionary<BunnyHopGame.Coin, VisualElement> _coins = new Dictionary<BunnyHopGame.Coin, VisualElement>();
        private readonly List<VisualElement> _windStreaks = new List<VisualElement>();
        private VisualElement _gooby, _meadow;
        private Label _gateLabel;
        private float _pulse, _crashTilt;
        private string _lastGustPhase = "none";

        private static readonly Color Sky = new Color(0.72f, 0.86f, 1f);
        private static readonly Color Hedge = MgScenery.Hex(0x79C46E);
        private static readonly Color HedgeRim = MgScenery.Hex(0x4E9A4F);
        private static readonly Color HedgeCap = MgScenery.Hex(0xA6DE8C);
        private static readonly Color Bark = MgScenery.Hex(0xC39468);
        private static readonly Color BarkRim = MgScenery.Hex(0x8E623F);
        private static readonly Color BarkLight = MgScenery.Hex(0xD9B088);
        private static readonly Color BarkDark = MgScenery.Hex(0xA87A52);

        public BunnyHopScreen(GameHost host) : base(host, "bunnyHop", "bunnyHop") { }

        protected override (double w, double h) ViewSize => (BunnyHopGame.ViewW, BunnyHopGame.ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0x86B8E8);
        protected override Color FieldColor => Sky;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public BunnyHopGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new BunnyHopGame(Difficulty, seed);
            _game.SetAspect(BunnyHopGame.ViewW / BunnyHopGame.ViewH);
            _game.GatePassed += (points, gusting) =>
            {
                FloatText(GoobyAt() + new Vector2(50f, -10f), "+" + points, gusting ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.24f, 0.55f, 0.32f));
                StageGate(gusting);
            };
            _game.CoinTaken += () =>
            {
                FloatText(GoobyAt() + new Vector2(30f, -40f), "+1", new Color(0.95f, 0.75f, 0.2f));
                StageCoin();
            };
            _game.Narrowed += () => Banner(Host.Strings.T("mg.bunnyHop.narrow"), 1.2);
            _game.Gust += d => Banner(Host.Strings.T("mg.bunnyHop.gust"), 1.4);
            _game.Crashed_ += () => Banner(Host.Strings.T("mg.bunnyHop.crash"), 1.4);
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (_game != null && _game.Hop()) StageHop();
            });
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // wind streaks (shown while the wind telegraphs and blows)
            for (var i = 0; i < 5; i++)
            {
                var streak = new VisualElement { pickingMode = PickingMode.Ignore };
                streak.style.position = Position.Absolute;
                streak.style.height = 4; streak.style.width = 60 + i * 12;
                streak.style.borderTopLeftRadius = 2; streak.style.borderTopRightRadius = 2; streak.style.borderBottomLeftRadius = 2; streak.style.borderBottomRightRadius = 2;
                streak.style.backgroundColor = new Color(1f, 1f, 1f, 0.7f);
                streak.style.display = DisplayStyle.None;
                Field.Add(streak);
                _windStreaks.Add(streak);
            }
            _gooby = Ux.Portrait(64);
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;
            _gateLabel = Ux.Text("", "gb-md", "gb-bold");
            _gateLabel.name = "gates";
            Ux.Abs(_gateLabel, null, 84);
            _gateLabel.style.left = Length.Percent(50);
            _gateLabel.style.translate = new Translate(Length.Percent(-50), 0);
            Root.Add(_gateLabel);
            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.bunnyHop.intro"), BunnyHopGame.IntroSec + 0.7);
        }

        // ── scenery (board 06): gradient sky + sun, then four parallax strips — clouds, hazy far hills with round trees,
        //    near hills, bushes and daisies on the meadow line — and the meadow with its grass edge and tufts. Built once,
        //    pointer-transparent, behind Gooby and the gates; RenderGame only translates the four strips. ──

        private VisualElement _clouds, _hillsFar, _hillsNear, _bushes;
        private float _stripBottom = -1f;
        private const float CloudP = 300f, FarP = 260f, NearP = 220f, BushP = 180f;

        private void BuildScenery()
        {
            _stripBottom = -1f;
            var sky = MgScenery.Layer(Field, "mgf-bunnyhop-sky");
            MgScenery.Gradient(sky, MgScenery.Hex(0xA6CFFF), MgScenery.Hex(0xEEF6FF));
            var sun = MgScenery.Sun(sky, 46f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, null, 7f, 8f);
            _clouds = Strip("mgf-bunnyhop-clouds", CloudP);
            _clouds.style.top = 0; _clouds.style.bottom = 0; _clouds.style.height = StyleKeyword.Auto;
            for (var k = 0; k < 3; k++)
            {
                var a = MgScenery.Cloud(_clouds, 104f);
                a.style.left = k * CloudP + 20f; MgScenery.Pct(a, null, 7f);
                var b = MgScenery.Cloud(_clouds, 74f, MgScenery.Hex(0xF4F8FF));
                b.style.left = k * CloudP + 175f; MgScenery.Pct(b, null, 20f);
            }
            _hillsFar = Strip("mgf-bunnyhop-far", FarP);
            var farC = MgScenery.Hex(0xBFE6CC); var farRim = MgScenery.Hex(0xD9F3E1);
            for (var k = 0; k < 3; k++)
            {
                var h1 = MgScenery.Hill(_hillsFar, 200f, 128f, farC, farRim);
                h1.style.left = k * FarP; h1.style.bottom = 0;
                var tree = MgScenery.Sprite(h1, "plant_tree_round", 48f, 62f);
                tree.style.left = 76f; tree.style.top = -50f;
                var h2 = MgScenery.Hill(_hillsFar, 170f, 88f, farC, farRim);
                h2.style.left = k * FarP + 140f; h2.style.bottom = 0;
            }
            _hillsNear = Strip("mgf-bunnyhop-near", NearP);
            var nearC = MgScenery.Hex(0xA6DB98); var nearRim = MgScenery.Hex(0xC6F39A);
            for (var k = 0; k < 3; k++)
            {
                var h1 = MgScenery.Hill(_hillsNear, 240f, 64f, nearC, nearRim);
                h1.style.left = k * NearP - 40f; h1.style.bottom = 0;
                var h2 = MgScenery.Hill(_hillsNear, 150f, 46f, nearC, nearRim);
                h2.style.left = k * NearP + 130f; h2.style.bottom = 0;
            }
            _bushes = Strip("mgf-bunnyhop-bushes", BushP);
            for (var k = 0; k < 4; k++)
            {
                var bush = MgScenery.Sprite(_bushes, "plant_bush", 62f, 56f);
                bush.style.left = k * BushP + 14f; bush.style.bottom = -8f;
                var daisies = MgScenery.Sprite(_bushes, "plant_daisies", 36f, 38f);
                daisies.style.left = k * BushP + 112f; daisies.style.bottom = -10f;
            }
            // the meadow the floor line sits on: grass edge, tufts poking over it, a few dot flowers
            _meadow = MgScenery.Shape(Field, MgScenery.Hex(0x9ED499), 0f, "gb-mgf-meadow");
            _meadow.name = "mgf-meadow";
            Ux.Abs(_meadow, 0, null, 0, 0);
            _meadow.style.borderTopColor = MgScenery.Hex(0x7CC486);
            RoundTopMeadow(_meadow);
            var tuftC = MgScenery.Hex(0x6DBE78);
            for (var i = 0; i < 6; i++)
            {
                var tuft = MgScenery.Tuft(_meadow, tuftC, i % 2 == 0 ? 1f : 0.8f);
                MgScenery.Pct(tuft, 4f + i * 17f, null);
                tuft.style.top = -15f + (i % 2) * 3f;
            }
            var petals = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFF8B8B) };
            for (var i = 0; i < 5; i++)
            {
                var f = MgScenery.Flower(_meadow, petals[i % petals.Length], 0.85f);
                f.style.left = Length.Percent(10f + i * 19f); f.style.top = 16f + (i % 2) * 14f;
            }
        }

        private static void RoundTopMeadow(VisualElement meadow) => MgScenery.RoundTop(meadow, 26f);

        /// <summary>A full-field scenery layer holding one horizontal strip (four periods wide, standing on the meadow line
        /// once RenderGame knows it) that scrolls as one translate.</summary>
        private VisualElement Strip(string name, float period)
        {
            var layer = MgScenery.Layer(Field, name);
            var strip = MgScenery.Group(layer, "gb-mgf-strip");
            strip.name = name + "-strip";
            strip.style.left = 0; strip.style.width = period * 4f; strip.style.height = 1f;
            strip.style.bottom = Length.Percent(20);
            return strip;
        }

        /// <summary>Parallax: each strip slides left with the gates' scroll at its own depth (wrapping by its period).</summary>
        private void Parallax(float floorY)
        {
            var bottom = Mathf.Max(0f, FieldH - floorY - 3f);
            if (!Mathf.Approximately(bottom, _stripBottom))
            {
                _stripBottom = bottom;
                _hillsFar.style.bottom = bottom; _hillsNear.style.bottom = bottom; _bushes.style.bottom = bottom;
            }
            var px = (float)_game.Scroll * Ppu;
            var drift = ReducedMotion ? 0f : _pulse * 5f;
            _clouds.style.translate = new Translate(-Mathf.Repeat(px * 0.06f + drift, CloudP), 0);
            _hillsFar.style.translate = new Translate(-Mathf.Repeat(px * 0.15f, FarP), 0);
            _hillsNear.style.translate = new Translate(-Mathf.Repeat(px * 0.32f, NearP), 0);
            _bushes.style.translate = new Translate(-Mathf.Repeat(px * 0.55f, BushP), 0);
        }

        private float Ppu => FieldH / (float)_game.WorldH;
        private float ScreenY(double worldY) => FieldH * 0.5f - (float)worldY * Ppu;
        private Vector2 GoobyAt() => new Vector2((float)_game.GoobyX * Ppu, ScreenY(_game.GoobyY));

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _pulse += (float)dt;
            if (_game.Crashed) _crashTilt = Mathf.Min(90f, _crashTilt + (float)dt * 220f);
        }

        protected override void RenderGame()
        {
            if (_stage != null)
            {
                var gust = _game.GustPhase;
                if (gust != _lastGustPhase && gust == "telegraph") Banner(Host.Strings.T("mg.bunnyHop.gust_warn"), 1.2);
                _lastGustPhase = gust;
                RenderStage();
                TimeLabel.text = Host.Strings.T("mg.game.time", ("sec", (int)_game.Elapsed));
                ScoreLabel.text = _game.LiveScore.ToString();
                _gateLabel.text = Host.Strings.T("mg.bunnyHop.gates", ("n", _game.Gates));
                return;
            }
            var ppu = Ppu;
            var floorY = ScreenY(_game.Tune.FloorY);
            _meadow.style.height = FieldH - floorY;
            Parallax(floorY);
            // Gooby: tilts up on a hop, noses down while falling, tumbles after the crash
            var at = GoobyAt();
            _gooby.style.left = at.x - 32; _gooby.style.top = at.y - 32;
            var tilt = _game.Crashed ? _crashTilt : Mathf.Clamp((float)-_game.GoobyVy * 8f, -25f, 35f);
            _gooby.style.rotate = new Rotate(tilt);
            // gates: the hedge above the gap from the ceiling, the hedge below it down to the meadow
            var live = new HashSet<BunnyHopGame.Pillar>(_game.Pillars);
            foreach (var p in _game.Pillars)
            {
                if (!_pillars.TryGetValue(p, out var v))
                {
                    v = (HedgeBlock(true), HedgeBlock(false));
                    _pillars[p] = v;
                    Field.Add(v.top); Field.Add(v.bottom);
                }
                var x = (float)(p.X - _game.Scroll) * ppu;
                var w = (float)_game.Tune.PillarHalfW * 2f * ppu;
                var gapTop = ScreenY(p.GapCenterY + p.GapHeight / 2.0);
                var gapBottom = ScreenY(p.GapCenterY - p.GapHeight / 2.0);
                v.top.style.left = x - w * 0.5f; v.top.style.width = w; v.top.style.top = -10; v.top.style.height = gapTop + 10;
                v.bottom.style.left = x - w * 0.5f; v.bottom.style.width = w; v.bottom.style.top = gapBottom; v.bottom.style.height = FieldH - gapBottom + 10;
            }
            var dead = new List<BunnyHopGame.Pillar>();
            foreach (var kv in _pillars) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _pillars[d].top.RemoveFromHierarchy(); _pillars[d].bottom.RemoveFromHierarchy(); _pillars.Remove(d); }
            // coins in the gaps
            var liveCoins = new HashSet<BunnyHopGame.Coin>(_game.Coins);
            foreach (var c in _game.Coins)
            {
                if (!_coins.TryGetValue(c, out var cv))
                {
                    cv = Ux.Icon("coin");
                    cv.style.position = Position.Absolute;
                    cv.style.width = 34; cv.style.height = 34;
                    cv.pickingMode = PickingMode.Ignore;
                    _coins[c] = cv;
                    Field.Add(cv);
                }
                cv.style.left = (float)(c.X - _game.Scroll) * ppu - 17;
                cv.style.top = ScreenY(c.Y) - 17 + Mathf.Sin(_pulse * 4f + (float)c.X) * 4f;
            }
            var deadCoins = new List<BunnyHopGame.Coin>();
            foreach (var kv in _coins) if (!liveCoins.Contains(kv.Key)) deadCoins.Add(kv.Key);
            foreach (var d in deadCoins) { _coins[d].RemoveFromHierarchy(); _coins.Remove(d); }
            // the wind: streaks race across the sky while it telegraphs and blows
            var phase = _game.GustPhase;
            if (phase != _lastGustPhase && phase == "telegraph") Banner(Host.Strings.T("mg.bunnyHop.gust_warn"), 1.2);
            _lastGustPhase = phase;
            var windy = phase == "telegraph" || phase == "gust";
            for (var i = 0; i < _windStreaks.Count; i++)
            {
                var s = _windStreaks[i];
                s.style.display = windy ? DisplayStyle.Flex : DisplayStyle.None;
                if (!windy) continue;
                var speed = phase == "gust" ? 900f : 400f;
                s.style.left = FieldW - ((_pulse * speed + i * 137f) % (FieldW + 120f));
                s.style.top = FieldH * (0.12f + i * 0.15f) + (_game.GustDirection > 0 ? -1f : 1f) * (_pulse * 30f % 20f);
            }
            TimeLabel.text = Host.Strings.T("mg.game.time", ("sec", (int)_game.Elapsed));
            ScoreLabel.text = _game.LiveScore.ToString();
            _gateLabel.text = Host.Strings.T("mg.bunnyHop.gates", ("n", _game.Gates));
        }

        /// <summary>
        /// One half of a gate, drawn as a tree trunk with a leafy hedge cap at the gap end (board 06 toy look: bark
        /// stripes, chunky rim, leaf highlights, a blossom). The element keeps exactly the engine's box — RenderGame
        /// sets left/width/top/height as before; the cap and bark are children inside it.
        /// </summary>
        private static VisualElement HedgeBlock(bool top)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore, name = top ? "gate-top" : "gate-bottom" };
            v.AddToClassList("gb-mgf-trunk");
            v.style.position = Position.Absolute;
            v.style.backgroundColor = Bark;
            const float r = 12f;
            if (top) MgScenery.RoundBottom(v, r); else MgScenery.RoundTop(v, r);
            v.style.borderLeftWidth = 3; v.style.borderRightWidth = 3;
            v.style.borderLeftColor = BarkRim; v.style.borderRightColor = BarkRim;
            var stripe = MgScenery.Shape(v, BarkLight, 4f);
            MgScenery.Pct(stripe, 20f, 0, null, 0); stripe.style.width = Length.Percent(16);
            var groove = MgScenery.Shape(v, BarkDark, 3f);
            MgScenery.Pct(groove, 62f, 0, null, 0); groove.style.width = Length.Percent(9);
            // the leafy cap at the gap end
            var cap = MgScenery.Shape(v, Hedge, 18f, "gb-mgf-leafcap");
            cap.style.left = -3; cap.style.right = -3; cap.style.height = 42;
            if (top) cap.style.bottom = 0; else cap.style.top = 0;
            MgScenery.Outline(cap, 3f, HedgeRim);
            for (var i = 0; i < 3; i++)
            {
                var leaf = MgScenery.Shape(cap, HedgeCap, 7f);
                MgScenery.Pct(leaf, 10f + i * 30f, top ? 52f : 16f);
                MgScenery.Size(leaf, 14f, 10f);
            }
            var blossom = MgScenery.Shape(cap, MgScenery.Hex(0xFF9BC1), 5f);
            MgScenery.Pct(blossom, 58f, top ? 18f : 52f);
            MgScenery.Size(blossom, 10f, 10f);
            MgScenery.Outline(blossom, 2f, Color.white);
            return v;
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _pillars.Clear(); _coins.Clear(); _windStreaks.Clear();
            _crashTilt = 0f; _lastGustPhase = "none";
            _gateLabel?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }
    }
}
