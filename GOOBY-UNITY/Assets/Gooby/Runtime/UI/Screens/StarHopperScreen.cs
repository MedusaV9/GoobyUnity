using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Sternenhüpfer host on the shared minigame frame (port of star_hopper.gd + its stage): deep space with a
    /// starfield streaming past, three lanes, Gooby's little rocket at the lower third. Meteors and pickups (stars,
    /// golden carrots, the shield) come down the lanes; a tap on the left/right half hops one lane, a sideways swipe
    /// two. Showers telegraph their danger lanes in red, the wormhole tints the sky, the shield rings the ship.
    /// </summary>
    public sealed partial class StarHopperScreen : MinigameHostScreen
    {
        private StarHopperGame _game;
        private readonly Dictionary<StarHopperGame.Meteor, VisualElement> _meteors = new Dictionary<StarHopperGame.Meteor, VisualElement>();
        private readonly Dictionary<StarHopperGame.Pickup, VisualElement> _pickups = new Dictionary<StarHopperGame.Pickup, VisualElement>();
        private readonly List<VisualElement> _stars = new List<VisualElement>();
        private readonly List<VisualElement> _laneWarns = new List<VisualElement>();
        private VisualElement _ship, _shieldRing;
        private Label _distLabel, _stateLabel;
        private Vector2 _touchFrom;
        private float _pulse;

        private const float ShipAnchor = 0.78f;   // of the field height
        private const float ViewAheadM = (float)StarHopperGame.ViewAheadM;
        private static readonly Color Space = MgScenery.Hex(0x221B45);

        public StarHopperScreen(GameHost host) : base(host, "starHopper", "starHopper") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        /// <summary>Distance scores a point every ~second: a fanfare per 25, pickups as hits.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Milestones;
        protected override int ScoreMilestone => 25;
        protected override Color BackdropColor => MgScenery.Hex(0x15112B);
        protected override Color FieldColor => Space;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public StarHopperGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new StarHopperGame(Difficulty, seed);
            _game.Collected += p => { StageEvent("pickup"); FloatText(ShipAt() + new Vector2(0f, -70f), "+" + p.Points, p.Kind == "gold" ? new Color(1f, 0.62f, 0.2f) : new Color(1f, 0.88f, 0.4f)); };
            _game.ShieldGained += () => { StageEvent("shield"); Banner(Host.Strings.T("mg.starHopper.shield"), 1.2); };
            _game.ShieldPopped += () => { StageEvent("hit"); Banner(Host.Strings.T("mg.starHopper.shield_pop"), 1.2); };
            _game.WormholeOpened += () => Banner(Host.Strings.T("mg.starHopper.wormhole"), 1.4);
            _game.ShowerWarned += () => { StageEvent("shower"); Banner(Host.Strings.T("mg.starHopper.shower"), 1.3); };
            _game.Crashed_ += () => StageEvent("hit");
            _game.SpeedStep += n => Banner(Host.Strings.T("mg.starHopper.speed_up"), 0.9);
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e => _touchFrom = e.localPosition);
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_game == null) return;
                var d = (Vector2)e.localPosition - _touchFrom;
                var threshold = Mathf.Max(40f, FieldW * 0.08f);
                if (Mathf.Abs(d.x) >= threshold) _game.Swipe(d.x < 0f ? "left" : "right");
                else _game.Tap(e.localPosition.x < FieldW * 0.5f ? "left" : "right");
            });
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the starfield: little dots that stream down with the speed
            for (var i = 0; i < 40; i++)
            {
                var star = new VisualElement { pickingMode = PickingMode.Ignore };
                star.style.position = Position.Absolute;
                var sz = 2 + (i % 3);
                star.style.width = sz; star.style.height = sz;
                star.style.borderTopLeftRadius = sz; star.style.borderTopRightRadius = sz; star.style.borderBottomLeftRadius = sz; star.style.borderBottomRightRadius = sz;
                star.style.backgroundColor = new Color(1f, 1f, 1f, 0.35f + (i % 4) * 0.15f);
                star.userData = new Vector2((i * 137) % 1000 / 1000f, (i * 251) % 1000 / 1000f);
                Field.Add(star);
                _stars.Add(star);
            }
            // lane markers and the shower warnings per lane
            for (var l = 0; l < 3; l++)
            {
                var lane = new VisualElement { pickingMode = PickingMode.Ignore };
                lane.style.position = Position.Absolute;
                lane.style.top = 0; lane.style.bottom = 0;
                lane.style.width = 2;
                lane.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
                lane.userData = l;
                Field.Add(lane);
                var warn = new VisualElement { pickingMode = PickingMode.Ignore };
                warn.style.position = Position.Absolute;
                warn.style.top = 0; warn.style.bottom = 0;
                warn.style.backgroundColor = new Color(1f, 0.3f, 0.3f, 0.18f);
                warn.style.display = DisplayStyle.None;
                Field.Add(warn);
                _laneWarns.Add(warn);
            }
            // the ship: Gooby's rocket — a portrait in a capsule with a flame
            _ship = new VisualElement { pickingMode = PickingMode.Ignore, name = "ship" };
            _ship.style.position = Position.Absolute;
            _ship.style.width = 64; _ship.style.height = 84;
            _ship.style.backgroundColor = new Color(0.95f, 0.95f, 1f);
            _ship.style.borderTopLeftRadius = 32; _ship.style.borderTopRightRadius = 32; _ship.style.borderBottomLeftRadius = 10; _ship.style.borderBottomRightRadius = 10;
            var stripe = new VisualElement { pickingMode = PickingMode.Ignore };
            stripe.style.position = Position.Absolute; stripe.style.left = 0; stripe.style.right = 0; stripe.style.bottom = 0; stripe.style.height = 22;
            stripe.style.backgroundColor = new Color(1f, 0.55f, 0.55f);
            stripe.style.borderBottomLeftRadius = 10; stripe.style.borderBottomRightRadius = 10;
            _ship.Add(stripe);
            var face = Ux.Portrait(40);
            face.style.position = Position.Absolute; face.style.left = 12; face.style.top = 12;
            face.pickingMode = PickingMode.Ignore;
            _ship.Add(face);
            var flame = new VisualElement { pickingMode = PickingMode.Ignore, name = "flame" };
            flame.style.position = Position.Absolute; flame.style.left = 22; flame.style.bottom = -22; flame.style.width = 20; flame.style.height = 26;
            flame.style.borderBottomLeftRadius = 10; flame.style.borderBottomRightRadius = 10;
            flame.style.backgroundColor = new Color(1f, 0.7f, 0.3f);
            _ship.Add(flame);
            _shieldRing = new VisualElement { pickingMode = PickingMode.Ignore };
            _shieldRing.style.position = Position.Absolute; _shieldRing.style.left = -14; _shieldRing.style.top = -12; _shieldRing.style.width = 92; _shieldRing.style.height = 108;
            _shieldRing.style.borderTopLeftRadius = 46; _shieldRing.style.borderTopRightRadius = 46; _shieldRing.style.borderBottomLeftRadius = 46; _shieldRing.style.borderBottomRightRadius = 46;
            _shieldRing.style.borderTopWidth = 4; _shieldRing.style.borderBottomWidth = 4; _shieldRing.style.borderLeftWidth = 4; _shieldRing.style.borderRightWidth = 4;
            var ring = new Color(0.5f, 0.85f, 1f, 0.9f);
            _shieldRing.style.borderTopColor = ring; _shieldRing.style.borderBottomColor = ring; _shieldRing.style.borderLeftColor = ring; _shieldRing.style.borderRightColor = ring;
            _shieldRing.style.display = DisplayStyle.None;
            _ship.Add(_shieldRing);
            Field.Add(_ship);
            if (_stage != null)
            {
                _ship.style.visibility = Visibility.Hidden;
                foreach (var st in _stars) st.style.display = DisplayStyle.None;
                foreach (var child in Field.Children()) if (child.userData is int) child.style.display = DisplayStyle.None;
                foreach (var w in _laneWarns) w.style.visibility = Visibility.Hidden;
            }
            _distLabel = Ux.Text("", "gb-md", "gb-bold");
            _distLabel.name = "distance";
            Ux.Abs(_distLabel, null, 58);
            _distLabel.style.left = Length.Percent(50);
            _distLabel.style.translate = new Translate(Length.Percent(-50), 0);
            _distLabel.style.color = new Color(1f, 0.97f, 0.9f);
            Root.Add(_distLabel);
            _stateLabel = Ux.Text("", "gb-sm", "gb-mild");
            _stateLabel.name = "speed";
            Ux.Abs(_stateLabel, null, 84);
            _stateLabel.style.left = Length.Percent(50);
            _stateLabel.style.translate = new Translate(Length.Percent(-50), 0);
            _stateLabel.style.color = new Color(0.8f, 0.8f, 0.95f);
            Root.Add(_stateLabel);
            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.starHopper.intro"), StarHopperGame.IntroSec + 0.7);
        }

        // ── scenery (board 06 "Nacht – ruhig"): a violet gradient night, three soft nebula glows, a few sparkles, and a
        //    ringed peach planet + a little lilac moon that drift down far slower than the stars (two translates a frame).
        //    Everything sits behind the starfield, lanes, meteors and the ship and ignores the pointer. ──

        private VisualElement _planet, _moon;
        private const float PlanetSpan = 150f, MoonSpan = 60f;

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-starhopper");
            MgScenery.Gradient(scene, MgScenery.Hex(0x1E1840), MgScenery.Hex(0x3B2B5E));
            Nebula(scene, new Color(1f, 0.6f, 0.85f, 0.1f), 260f, -24f, 18f);
            Nebula(scene, new Color(0.5f, 0.9f, 0.95f, 0.08f), 220f, 60f, 50f);
            Nebula(scene, new Color(0.72f, 0.6f, 1f, 0.1f), 300f, 4f, 72f);
            var sparkAt = new[] { new Vector2(12f, 30f), new Vector2(82f, 16f), new Vector2(70f, 64f), new Vector2(20f, 86f) };
            foreach (var at in sparkAt)
            {
                var sp = Ux.Icon("sparkle");
                sp.pickingMode = PickingMode.Ignore;
                sp.style.position = Position.Absolute;
                MgScenery.Pct(sp, at.x, at.y);
                MgScenery.Size(sp, 16f, 16f);
                sp.style.opacity = 0.55f;
                scene.Add(sp);
            }
            // the ringed planet
            _planet = MgScenery.Group(scene, "gb-mgf-planet");
            _planet.name = "mgf-planet";
            MgScenery.Pct(_planet, 8f, 0);
            MgScenery.Size(_planet, 130f, 100f);
            var ball = MgScenery.Shape(_planet, MgScenery.Hex(0xFFB38A), 40f);
            ball.style.left = 25f; ball.style.top = 10f; MgScenery.Size(ball, 80f, 80f);
            MgScenery.Outline(ball, 3f, MgScenery.Hex(0xFFD6B8));
            ball.style.overflow = Overflow.Hidden;
            var band = MgScenery.Shape(ball, MgScenery.Hex(0xF29A74), 6f);
            band.style.left = -4f; band.style.right = -4f; band.style.top = 30f; band.style.height = 12f;
            var shine = MgScenery.Shape(ball, new Color(1f, 1f, 1f, 0.45f), 6f);
            shine.style.left = 14f; shine.style.top = 10f; MgScenery.Size(shine, 16f, 12f);
            var ring = MgScenery.Shape(_planet, Color.clear, 14f, "gb-mgf-ring");
            ring.style.left = 0; ring.style.top = 38f; MgScenery.Size(ring, 130f, 28f);
            MgScenery.Outline(ring, 4f, new Color(1f, 0.89f, 0.64f, 0.9f));
            ring.style.rotate = new Rotate(-16f);
            // the little moon with two craters
            _moon = MgScenery.Group(scene, "gb-mgf-moon");
            _moon.name = "mgf-moon";
            MgScenery.Pct(_moon, null, 0, 10f);
            MgScenery.Size(_moon, 36f, 36f);
            var disc = MgScenery.Shape(_moon, MgScenery.Hex(0xDCD6F7), 18f);
            Ux.Abs(disc, 0, 0, 0, 0);
            MgScenery.Outline(disc, 2f, MgScenery.Hex(0xF2EFFF));
            var cr1 = MgScenery.Shape(disc, MgScenery.Hex(0xC2BAE8), 5f); cr1.style.left = 8f; cr1.style.top = 9f; MgScenery.Size(cr1, 10f, 10f);
            var cr2 = MgScenery.Shape(disc, MgScenery.Hex(0xC2BAE8), 3f); cr2.style.left = 19f; cr2.style.top = 19f; MgScenery.Size(cr2, 6f, 6f);
        }

        private static void Nebula(VisualElement scene, Color c, float d, float leftPct, float topPct)
        {
            var n = MgScenery.Shape(scene, c, d * 0.5f, "gb-mgf-nebula");
            MgScenery.Pct(n, leftPct, topPct);
            MgScenery.Size(n, d, d);
        }

        /// <summary>The planet and the moon sink with the flight at a fraction of the stars' pace and wrap off the bottom.</summary>
        private void DriftPlanets(float flow)
        {
            if (FieldH <= 0f) return;
            _planet.style.translate = new Translate(0, Mathf.Repeat(FieldH * (0.1f + flow * 0.05f) + PlanetSpan, FieldH + PlanetSpan * 1.5f) - PlanetSpan);
            _moon.style.translate = new Translate(0, Mathf.Repeat(FieldH * (0.52f + flow * 0.11f) + MoonSpan, FieldH + MoonSpan * 1.5f) - MoonSpan);
        }

        private float LaneX(double lane) => FieldW * (0.5f + (float)(lane - 1.0) * 0.3f);
        private float DepthY(double m) => FieldH * ShipAnchor - (float)((m - _game.Traveled) / ViewAheadM) * FieldH * (ShipAnchor + 0.08f);
        private Vector2 ShipAt() => new Vector2(LaneX(_game.LaneVisual), FieldH * ShipAnchor);

        protected override void TickGame(double dt) { _game.Tick(dt); _pulse += (float)dt; }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            // the starfield streams down with the speed
            var flow = (float)(g.Traveled / ViewAheadM);
            if (_stage != null)
            {
                RenderStage();
                TimeLabel.text = t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
                ScoreLabel.text = g.Score.ToString();
                _distLabel.text = t.T("mg.starHopper.distance", ("m", (int)g.Traveled));
                _stateLabel.text = t.T("mg.starHopper.speed", ("v", (int)System.Math.Round(g.Speed)));
                return;
            }
            DriftPlanets(flow);
            for (var i = 0; i < _stars.Count; i++)
            {
                var seed = (Vector2)_stars[i].userData;
                var y = (seed.y + flow * (0.6f + seed.x * 0.5f)) % 1f;
                _stars[i].style.left = seed.x * FieldW; _stars[i].style.top = y * FieldH;
            }
            // lanes and the shower warnings
            var laneEls = new List<VisualElement>();
            foreach (var child in Field.Children()) if (child.userData is int) laneEls.Add(child);
            foreach (var lane in laneEls) lane.style.left = LaneX((int)lane.userData) - 1f;
            for (var l = 0; l < 3; l++)
            {
                var danger = g.ShowerState != "idle" && g.ShowerSafeLane != l;
                _laneWarns[l].style.display = danger ? DisplayStyle.Flex : DisplayStyle.None;
                _laneWarns[l].style.left = LaneX(l) - FieldW * 0.14f; _laneWarns[l].style.width = FieldW * 0.28f;
                _laneWarns[l].style.backgroundColor = new Color(1f, 0.3f, 0.3f, g.ShowerState == "warn" ? 0.1f + 0.1f * Mathf.Sin(_pulse * 12f) : 0.2f);
            }
            // the ship slides between the lanes, leans into the change
            var at = ShipAt();
            _ship.style.left = at.x - 32f; _ship.style.top = at.y - 42f;
            _ship.style.rotate = new Rotate((float)(g.Lane - g.LaneVisual) * -18f);
            _shieldRing.style.display = g.Shielded ? DisplayStyle.Flex : DisplayStyle.None;
            var flame = _ship.Q("flame");
            if (flame != null) flame.style.height = 20f + Mathf.Sin(_pulse * 30f) * 5f + (float)(g.Speed - 11.0);
            // meteors
            var live = new HashSet<StarHopperGame.Meteor>(g.Meteors);
            foreach (var m in g.Meteors)
            {
                if (!_meteors.TryGetValue(m, out var v))
                {
                    v = MeteorShape(m.Approach > 0.0);
                    _meteors[m] = v;
                    Field.Add(v);
                }
                var depth = Mathf.Clamp01((float)((m.M - g.Traveled) / ViewAheadM));
                var sz = Mathf.Lerp(64f, 24f, depth);
                v.style.width = sz; v.style.height = sz;
                v.style.left = LaneX(m.Lane) - sz * 0.5f; v.style.top = DepthY(m.M) - sz * 0.5f;
                v.style.rotate = new Rotate((float)m.Spin * Mathf.Rad2Deg + _pulse * 40f);
            }
            var dead = new List<StarHopperGame.Meteor>();
            foreach (var kv in _meteors) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _meteors[d].RemoveFromHierarchy(); _meteors.Remove(d); }
            // pickups
            var livePickups = new HashSet<StarHopperGame.Pickup>(g.Pickups);
            foreach (var p in g.Pickups)
            {
                if (!_pickups.TryGetValue(p, out var pv))
                {
                    pv = Ux.Icon(p.Kind == "gold" ? "food_carrot" : p.Kind == "shield" ? "heart" : "star");
                    pv.style.position = Position.Absolute;
                    pv.pickingMode = PickingMode.Ignore;
                    _pickups[p] = pv;
                    Field.Add(pv);
                }
                var depth = Mathf.Clamp01((float)((p.M - g.Traveled) / ViewAheadM));
                var sz = Mathf.Lerp(46f, 18f, depth);
                pv.style.width = sz; pv.style.height = sz;
                pv.style.left = LaneX(p.Lane) - sz * 0.5f; pv.style.top = DepthY(p.M) - sz * 0.5f;
            }
            var deadPickups = new List<StarHopperGame.Pickup>();
            foreach (var kv in _pickups) if (!livePickups.Contains(kv.Key)) deadPickups.Add(kv.Key);
            foreach (var d in deadPickups) { _pickups[d].RemoveFromHierarchy(); _pickups.Remove(d); }
            Field.style.backgroundColor = g.WormholeLeft > 0.0 ? Color.Lerp(Space, new Color(0.35f, 0.15f, 0.45f), 0.6f) : Space;
            TimeLabel.text = t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            _distLabel.text = t.T("mg.starHopper.distance", ("m", (int)g.Traveled));
            _stateLabel.text = t.T("mg.starHopper.speed", ("v", (int)System.Math.Round(g.Speed)));
        }

        private static VisualElement MeteorShape(bool shower)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.backgroundColor = shower ? new Color(0.75f, 0.35f, 0.3f) : new Color(0.55f, 0.5f, 0.6f);
            v.style.borderTopLeftRadius = 30; v.style.borderTopRightRadius = 22; v.style.borderBottomLeftRadius = 26; v.style.borderBottomRightRadius = 32;
            var rim = shower ? new Color(1f, 0.6f, 0.4f) : new Color(0.35f, 0.32f, 0.4f);
            v.style.borderTopWidth = 3; v.style.borderBottomWidth = 3; v.style.borderLeftWidth = 3; v.style.borderRightWidth = 3;
            v.style.borderTopColor = rim; v.style.borderBottomColor = rim; v.style.borderLeftColor = rim; v.style.borderRightColor = rim;
            var crater = new VisualElement { pickingMode = PickingMode.Ignore };
            crater.style.position = Position.Absolute; crater.style.left = Length.Percent(30); crater.style.top = Length.Percent(28); crater.style.width = Length.Percent(28); crater.style.height = Length.Percent(24);
            crater.style.borderTopLeftRadius = 20; crater.style.borderTopRightRadius = 20; crater.style.borderBottomLeftRadius = 20; crater.style.borderBottomRightRadius = 20;
            crater.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
            v.Add(crater);
            return v;
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _meteors.Clear(); _pickups.Clear(); _stars.Clear(); _laneWarns.Clear();
            _distLabel?.RemoveFromHierarchy();
            _stateLabel?.RemoveFromHierarchy();
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
