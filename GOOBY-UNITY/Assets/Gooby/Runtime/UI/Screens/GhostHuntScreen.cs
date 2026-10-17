using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Geisterjagd host on the shared minigame frame (port of the ghost_hunt scene): a moonlit graveyard — dark
    /// violet night, the moon, twelve spots (gravestones, pumpkins, the crypt at the back) in a shallow perspective.
    /// Sheet ghosts rise out of the spots for a shrinking window and sink again; a tap on one catches it, a tap on a
    /// flickering pumpkin is the decoy, lantern and net tokens float in the middle. Boo waves send five at once.
    /// </summary>
    public sealed partial class GhostHuntScreen : MinigameHostScreen
    {
        private GhostHuntGame _game;
        private readonly List<VisualElement> _spots = new List<VisualElement>();
        private readonly List<VisualElement> _decoys = new List<VisualElement>();
        private readonly Dictionary<int, VisualElement> _ghosts = new Dictionary<int, VisualElement>();
        private readonly Dictionary<int, VisualElement> _tokens = new Dictionary<int, VisualElement>();
        private Label _chainLabel;
        private float _pulse;

        private static readonly Color Night = MgScenery.Hex(0x2B2256);
        private static readonly Color Grass = new Color(0.2f, 0.3f, 0.26f);
        private static readonly Color Stone = MgScenery.Hex(0xB9B4D9);
        private static readonly Color StoneRim = MgScenery.Hex(0x7E78A8);
        private static readonly Color Pumpkin = new Color(0.95f, 0.55f, 0.2f);

        public GhostHuntScreen(GameHost host) : base(host, "ghostHunt", "ghostHunt") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0x2E2658);
        protected override Color FieldColor => Night;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public GhostHuntGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new GhostHuntGame(Difficulty, seed);
            _game.Caught += (spot, points, chain) => { StageCatch(true, false); FloatText(SpotAt(spot) + new Vector2(0f, -60f), "+" + points, chain > 1 ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.7f, 0.95f, 0.8f)); };
            _game.DecoyTapped += d => { StageCatch(false, false); Banner(Host.Strings.T("mg.ghostHunt.decoy"), 1.2); };
            _game.PowerUp += kind => { StageCatch(true, false); Banner(Host.Strings.T(kind == "lantern" ? "mg.ghostHunt.lantern" : "mg.ghostHunt.net"), 1.4); };
            _game.BooWave += n => { StageCatch(false, true); Banner(Host.Strings.T("mg.ghostHunt.boo"), 1.4); };
            _game.BooBonus += b => Banner(Host.Strings.T("mg.ghostHunt.booBonus", ("n", b)), 1.4);
            _game.BooMissed += c => Banner(Host.Strings.T("mg.ghostHunt.booMiss", ("n", c)), 1.4);
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(_ => _game?.TapMiss());
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the spots: gravestones, pumpkins, the crypt — the ghosts rise from behind them
            var order = new List<int>();
            for (var i = 0; i < GhostHunt.Spots.Length; i++) order.Add(i);
            order.Sort((a, b) => GhostHunt.Spots[a].Z.CompareTo(GhostHunt.Spots[b].Z));   // far ones first, near ones drawn over them
            var spotViews = new VisualElement[GhostHunt.Spots.Length];
            var ghostHolders = new VisualElement[GhostHunt.Spots.Length];
            foreach (var i in order)
            {
                var spot = GhostHunt.Spots[i];
                var holder = new VisualElement { pickingMode = PickingMode.Ignore, name = "ghost-holder-" + i };
                holder.style.position = Position.Absolute;
                Field.Add(holder);
                ghostHolders[i] = holder;
                var v = SpotShape(spot.Kind);
                v.name = "spot-" + i;
                if (_stage != null) v.style.visibility = Visibility.Hidden;
                Field.Add(v);
                spotViews[i] = v;
            }
            _spots.AddRange(spotViews);
            _ghostHolders = ghostHolders;
            // the decoy pumpkins (dark until they flicker)
            for (var i = 0; i < GhostHunt.DecoySpots.Length; i++)
            {
                var d = PumpkinShape(true);
                d.name = "decoy-" + i;
                var decoy = i;
                d.RegisterCallback<PointerDownEvent>(e => { _game?.TapDecoy(decoy); e.StopPropagation(); });
                d.style.display = DisplayStyle.None;
                if (_stage != null) d.style.opacity = 0f;
                Field.Add(d);
                _decoys.Add(d);
            }
            _chainLabel = Ux.Text("", "gb-md", "gb-bold");
            _chainLabel.name = "chain";
            Ux.Abs(_chainLabel, null, 58);
            _chainLabel.style.left = Length.Percent(50);
            _chainLabel.style.translate = new Translate(Length.Percent(-50), 0);
            _chainLabel.style.color = new Color(1f, 0.9f, 0.6f);
            Root.Add(_chainLabel);
            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.ghostHunt.intro"), GhostHuntGame.IntroSec + 0.7);
        }

        private VisualElement[] _ghostHolders;

        private static VisualElement SpotShape(string kind)
        {
            if (kind == "pumpkin") return PumpkinShape(false);
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            if (kind == "crypt")
            {
                v.style.width = 120; v.style.height = 84;
                v.style.backgroundColor = MgScenery.Hex(0x9A93C4);
                v.style.borderTopLeftRadius = 30; v.style.borderTopRightRadius = 30;
                MgScenery.Outline(v, 3f, StoneRim);
                var door = new VisualElement { pickingMode = PickingMode.Ignore };
                door.style.position = Position.Absolute; door.style.left = 44; door.style.bottom = 0; door.style.width = 32; door.style.height = 48;
                door.style.borderTopLeftRadius = 16; door.style.borderTopRightRadius = 16;
                door.style.backgroundColor = MgScenery.Hex(0x3A3066);
                v.Add(door);
                return v;
            }
            v.style.width = 46; v.style.height = 58;
            v.style.backgroundColor = Stone;
            v.style.borderTopLeftRadius = 23; v.style.borderTopRightRadius = 23; v.style.borderBottomLeftRadius = 4; v.style.borderBottomRightRadius = 4;
            var rim = StoneRim;
            v.style.borderTopWidth = 3; v.style.borderBottomWidth = 3; v.style.borderLeftWidth = 3; v.style.borderRightWidth = 3;
            v.style.borderTopColor = rim; v.style.borderBottomColor = rim; v.style.borderLeftColor = rim; v.style.borderRightColor = rim;
            var cross = new VisualElement { pickingMode = PickingMode.Ignore };
            cross.style.position = Position.Absolute; cross.style.left = 19; cross.style.top = 12; cross.style.width = 4; cross.style.height = 22;
            cross.style.backgroundColor = rim;
            v.Add(cross);
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute; bar.style.left = 12; bar.style.top = 18; bar.style.width = 18; bar.style.height = 4;
            bar.style.backgroundColor = rim;
            v.Add(bar);
            return v;
        }

        private static VisualElement PumpkinShape(bool decoy)
        {
            var v = new VisualElement { pickingMode = decoy ? PickingMode.Position : PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.width = 54; v.style.height = 42;
            v.style.backgroundColor = decoy ? new Color(0.6f, 0.33f, 0.12f) : Pumpkin;
            v.style.borderTopLeftRadius = 22; v.style.borderTopRightRadius = 22; v.style.borderBottomLeftRadius = 20; v.style.borderBottomRightRadius = 20;
            var rim = new Color(0.55f, 0.28f, 0.1f);
            v.style.borderTopWidth = 3; v.style.borderBottomWidth = 3; v.style.borderLeftWidth = 3; v.style.borderRightWidth = 3;
            v.style.borderTopColor = rim; v.style.borderBottomColor = rim; v.style.borderLeftColor = rim; v.style.borderRightColor = rim;
            var stem = new VisualElement { pickingMode = PickingMode.Ignore };
            stem.style.position = Position.Absolute; stem.style.left = 23; stem.style.top = -8; stem.style.width = 8; stem.style.height = 12;
            stem.style.backgroundColor = new Color(0.35f, 0.5f, 0.3f);
            v.Add(stem);
            foreach (var x in new[] { 14f, 30f })
            {
                var eye = new VisualElement { pickingMode = PickingMode.Ignore };
                eye.style.position = Position.Absolute; eye.style.left = x; eye.style.top = 14; eye.style.width = 10; eye.style.height = 8;
                eye.style.backgroundColor = new Color(1f, 0.9f, 0.5f);
                eye.style.rotate = new Rotate(x < 20 ? 20f : -20f);
                v.Add(eye);
            }
            return v;
        }

        /// <summary>A sheet ghost: white rounded body with a wavy hem and two dark eyes.</summary>
        private VisualElement GhostShape(int id)
        {
            var v = new VisualElement { pickingMode = PickingMode.Position, name = "ghost-" + id };
            v.style.position = Position.Absolute;
            v.style.width = 60; v.style.height = 72;
            v.style.backgroundColor = new Color(0.97f, 0.97f, 1f);
            v.style.borderTopLeftRadius = 30; v.style.borderTopRightRadius = 30; v.style.borderBottomLeftRadius = 12; v.style.borderBottomRightRadius = 12;
            foreach (var x in new[] { 16f, 34f })
            {
                var eye = new VisualElement { pickingMode = PickingMode.Ignore };
                eye.style.position = Position.Absolute; eye.style.left = x; eye.style.top = 24; eye.style.width = 10; eye.style.height = 14;
                eye.style.borderTopLeftRadius = 5; eye.style.borderTopRightRadius = 5; eye.style.borderBottomLeftRadius = 5; eye.style.borderBottomRightRadius = 5;
                eye.style.backgroundColor = new Color(0.15f, 0.12f, 0.25f);
                v.Add(eye);
            }
            for (var i = 0; i < 3; i++)
            {
                var hem = new VisualElement { pickingMode = PickingMode.Ignore };
                hem.style.position = Position.Absolute; hem.style.left = i * 20; hem.style.bottom = -8; hem.style.width = 20; hem.style.height = 16;
                hem.style.borderBottomLeftRadius = 10; hem.style.borderBottomRightRadius = 10;
                hem.style.backgroundColor = new Color(0.97f, 0.97f, 1f);
                v.Add(hem);
            }
            var ghostId = id;
            v.RegisterCallback<PointerDownEvent>(e => { _game?.TapGhost(ghostId); e.StopPropagation(); });
            return v;
        }

        /// <summary>Screen position of a graveyard point: x across the field, z as depth (near = low on screen, far = high and smaller).</summary>
        private Vector2 Project(double x, double z)
        {
            var depth = Mathf.Clamp01((float)((-z - 1.5) / 4.9));
            return new Vector2(FieldW * 0.5f + (float)x * (FieldW / 5.4f) * Mathf.Lerp(1f, 0.8f, depth), FieldH * Mathf.Lerp(0.8f, 0.34f, depth));
        }

        private static float DepthScale(double z) => Mathf.Lerp(1f, 0.72f, Mathf.Clamp01((float)((-z - 1.5) / 4.9)));
        private Vector2 SpotAt(int spot) => Project(GhostHunt.Spots[spot].X, GhostHunt.Spots[spot].Z);

        protected override void TickGame(double dt) { _game.Tick(dt); _pulse += (float)dt; }

        // ── scenery (board 06 "Nacht – ruhig", never black): a violet night with stars and a big soft moon, lavender
        //    clouds, the round hills and a rounded iron fence on the horizon, the moonlit lawn with a pale path up to the
        //    crypt, a few small far-off stones, two lantern posts at the edges and three fog banks drifting across
        //    (three translates a frame, still with reduced motion). Built once, pointer-transparent, behind the spots,
        //    ghosts and pumpkins; the lantern power-up lights a warm veil on top of the scenery. ──

        private readonly List<VisualElement> _fog = new List<VisualElement>();
        private VisualElement _veil;
        private bool _veilOn;
        private static readonly float[] FogTop = { 36f, 58f, 80f };
        private static readonly float[] FogSpeed = { 7f, -5f, 9f };
        private static readonly float[] FogW = { 260f, 220f, 300f };

        private void BuildScenery()
        {
            _fog.Clear();
            _veilOn = false;
            var scene = MgScenery.Layer(Field, "mgf-ghosthunt");
            MgScenery.Gradient(scene, MgScenery.Hex(0x2B2256), MgScenery.Hex(0x6B5AA3), 0f, 32f);
            var stars = MgScenery.Dots(scene, 34, new Color(1f, 0.97f, 0.85f, 0.75f), 1f, 2.2f, 3);
            MgScenery.Pct(stars, 0, 0, 0);
            stars.style.height = Length.Percent(24f);
            float[] twAt = { 12f, 38f, 60f, 24f }; float[] twTop = { 9f, 5f, 14f, 18f };
            for (var i = 0; i < twAt.Length; i++) MgScenery.Pct(MgScenery.Twinkle(scene, i % 2 == 0 ? 12f : 9f, new Color(1f, 0.95f, 0.75f, 0.85f)), twAt[i], twTop[i]);
            // the moon with its halo and two soft craters
            var moon = MgScenery.Sun(scene, 64f, MgScenery.Hex(0xFFF4D6), new Color(1f, 0.96f, 0.82f, 0.16f));
            moon.name = "mgf-moon";
            MgScenery.Pct(moon, null, 6.5f, 4f);
            var disc = moon.Q(className: "gb-mgf-sun-core");
            if (disc != null)
            {
                var c1 = MgScenery.Shape(disc, MgScenery.Hex(0xF1E3BD), 8f); c1.style.left = 14f; c1.style.top = 18f; MgScenery.Size(c1, 16f, 16f);
                var c2 = MgScenery.Shape(disc, MgScenery.Hex(0xF1E3BD), 5f); c2.style.left = 34f; c2.style.top = 36f; MgScenery.Size(c2, 10f, 10f);
            }
            var cloudA = MgScenery.Cloud(scene, 120f, MgScenery.Hex(0x8676BE));
            MgScenery.Pct(cloudA, 4f, 13f);
            var cloudB = MgScenery.Cloud(scene, 84f, MgScenery.Hex(0x7A6AB2));
            MgScenery.Pct(cloudB, 54f, 19f);
            // the horizon: round hills, then the fence
            var hillC = MgScenery.Hex(0x4A3F84); var hillRim = MgScenery.Hex(0x5E519C);
            foreach (var (l, w, h) in new[] { (-14f, 210f, 90f), (38f, 230f, 110f), (70f, 180f, 80f) })
            {
                var hill = MgScenery.Hill(scene, w, h, hillC, hillRim);
                MgScenery.Pct(hill, l, null, null, 73f);
            }
            var fence = MgScenery.Fence(scene, 11, 34f, MgScenery.Hex(0x6E62A6), MgScenery.Hex(0x51468C));
            fence.name = "mgf-fence";
            MgScenery.Pct(fence, null, 21.5f);
            // the moonlit lawn with its rounded shoulders, the pale path up to the crypt
            var lawn = MgScenery.Shape(scene, MgScenery.Hex(0x4E7C74), 0f, "gb-mgf-lawn");
            lawn.name = "mgf-lawn";
            MgScenery.Pct(lawn, 0, 26f, 0, 0);
            MgScenery.RoundTop(lawn, 60f);
            lawn.style.borderTopWidth = 5; lawn.style.borderTopColor = MgScenery.Hex(0x6C9C8A);
            var near = MgScenery.Gradient(lawn, MgScenery.Alpha(MgScenery.Hex(0x4E7C74), 0f), MgScenery.Hex(0x5E9079), 30f, 100f);
            near.pickingMode = PickingMode.Ignore;
            var path = MgScenery.Painted(lawn, (p, r) =>
            {
                p.fillColor = MgScenery.Hex(0x7FA496, 0.55f);
                var w = r.width;
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.46f, 0f)); p.LineTo(new Vector2(w * 0.54f, 0f));
                p.BezierCurveTo(new Vector2(w * 0.62f, r.height * 0.35f), new Vector2(w * 0.4f, r.height * 0.6f), new Vector2(w * 0.7f, r.height));
                p.LineTo(new Vector2(w * 0.3f, r.height));
                p.BezierCurveTo(new Vector2(w * 0.2f, r.height * 0.6f), new Vector2(w * 0.5f, r.height * 0.35f), new Vector2(w * 0.46f, 0f));
                p.ClosePath(); p.Fill();
            }, "gb-mgf-path");
            Ux.Abs(path, 0, 0, 0, 0);
            var tuftC = MgScenery.Hex(0x3F6A60);
            for (var i = 0; i < 8; i++)
            {
                var tuft = MgScenery.Tuft(lawn, tuftC, 0.7f + (i % 3) * 0.15f);
                MgScenery.Pct(tuft, 4f + (i * 37) % 90, 12f + (i * 23) % 80);
            }
            // a few small, faded stones far off by the fence
            float[] farAt = { 8f, 30f, 66f, 86f };
            for (var i = 0; i < farAt.Length; i++)
            {
                var st = MgScenery.Tombstone(scene, 18f, 22f, MgScenery.Hex(0x7C76A8), MgScenery.Hex(0x5F5990), i % 2 == 0);
                MgScenery.Pct(st, farAt[i], 27.5f + (i % 2) * 1.2f);
            }
            // lantern posts at the edges
            for (var s = 0; s < 2; s++)
            {
                var post = MgScenery.Shape(scene, MgScenery.Hex(0x5B4F92), 3f, "gb-mgf-lamppost");
                MgScenery.Pct(post, s == 0 ? 2.5f : null, s == 0 ? 44f : 62f, s == 0 ? (float?)null : 2.5f);
                MgScenery.Size(post, 7f, 120f);
                var lamp = MgScenery.Lantern(scene, 18f, MgScenery.Hex(0xFFE3A0), MgScenery.Hex(0x51468C), new Color(1f, 0.85f, 0.5f, 0.22f));
                MgScenery.Pct(lamp, s == 0 ? -1.5f : null, s == 0 ? 41f : 59f, s == 0 ? (float?)null : -1.5f);
            }
            // the fog banks
            for (var i = 0; i < FogTop.Length; i++)
            {
                var fog = MgScenery.Puffs(scene, 5, new Color(0.86f, 0.84f, 1f, 0.16f), null, 20 + i);
                fog.name = "mgf-fog-" + i;
                fog.style.left = 0;
                MgScenery.Pct(fog, null, FogTop[i]);
                MgScenery.Size(fog, FogW[i], 54f);
                _fog.Add(fog);
            }
            _veil = MgScenery.Shape(scene, Color.clear, 0f, "gb-mgf-veil");
            _veil.name = "mgf-veil";
            Ux.Abs(_veil, 0, 0, 0, 0);
        }

        private void MoveScenery(bool lantern)
        {
            if (FieldW <= 0f) return;
            var t = ReducedMotion ? 0f : _pulse;
            for (var i = 0; i < _fog.Count; i++)
            {
                var span = FieldW + FogW[i];
                var x = Mathf.Repeat(i * 140f + t * FogSpeed[i], span) - FogW[i];
                _fog[i].style.translate = new Translate(x, 0);
            }
            if (lantern == _veilOn) return;
            _veilOn = lantern;
            _veil.style.backgroundColor = lantern ? new Color(1f, 0.9f, 0.7f, 0.16f) : Color.clear;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var s = _game.State;
            // the spots (laid out once the field has its size)
            for (var i = 0; i < _spots.Count; i++)
            {
                var spot = GhostHunt.Spots[i];
                var at = SpotAt(i);
                var sc = DepthScale(spot.Z);
                var v = _spots[i];
                var w = v.resolvedStyle.width > 0 ? v.resolvedStyle.width : 46f;
                var h = v.resolvedStyle.height > 0 ? v.resolvedStyle.height : 58f;
                v.style.left = at.x - w * 0.5f; v.style.top = at.y - h;
                v.style.scale = new Scale(Vector2.one * sc);
            }
            // ghosts: rise out of their spot with the window's rise/sink, fade a little when about to sink
            var live = new HashSet<int>();
            foreach (var g in s.Ghosts)
            {
                live.Add(g.Id);
                if (!_ghosts.TryGetValue(g.Id, out var gv))
                {
                    gv = GhostShape(g.Id);
                    if (_stage != null) gv.style.opacity = 0f;
                    _ghostHolders[g.Spot].Add(gv);
                    _ghosts[g.Id] = gv;
                }
                var at = SpotAt(g.Spot);
                var rise = (float)_game.Rise(g);
                var sc = DepthScale(GhostHunt.Spots[g.Spot].Z);
                var lift = rise * 62f * sc;
                gv.style.left = at.x - 30f + Mathf.Sin(_pulse * 3f + g.Id) * 3f;
                gv.style.top = at.y - 20f - lift;
                gv.style.height = Mathf.Max(8f, 72f * Mathf.Lerp(0.15f, 1f, rise));
                gv.style.scale = new Scale(Vector2.one * sc);
                gv.style.opacity = _stage != null ? 0f : g.Revealed || s.LanternT > 0.0 ? 1f : 0.92f;
            }
            var dead = new List<int>();
            foreach (var kv in _ghosts) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _ghosts[d].RemoveFromHierarchy(); _ghosts.Remove(d); }
            // decoys: a pumpkin glows and jiggles while it flickers
            for (var i = 0; i < _decoys.Count; i++)
            {
                var f = s.Flickers.Find(x => x.Decoy == i);
                var dv = _decoys[i];
                dv.style.display = f != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (f == null) continue;
                var (id, x, z) = GhostHunt.DecoySpots[i];
                var at = Project(x, z);
                dv.style.left = at.x - 27f; dv.style.top = at.y - 42f + Mathf.Sin(_pulse * 14f) * 2f;
                dv.style.backgroundColor = Color.Lerp(new Color(0.6f, 0.33f, 0.12f), new Color(1f, 0.7f, 0.3f), 0.5f + 0.5f * Mathf.Sin(_pulse * 10f));
            }
            // tokens: lantern / net floating at their anchors
            var liveTokens = new HashSet<int>();
            foreach (var tk in s.Tokens)
            {
                liveTokens.Add(tk.Window);
                if (!_tokens.TryGetValue(tk.Window, out var tv))
                {
                    tv = Ux.IconTile(tk.Kind == "lantern" ? "bulb" : "grid", tk.Kind == "lantern" ? new Color(1f, 0.9f, 0.55f) : new Color(0.75f, 0.9f, 1f));
                    tv.name = "token-" + tk.Window;
                    tv.style.position = Position.Absolute;
                    tv.pickingMode = PickingMode.Position;
                    var window = tk.Window;
                    tv.RegisterCallback<PointerDownEvent>(e => { _game?.TapToken(window); e.StopPropagation(); });
                    Field.Add(tv);
                    _tokens[tk.Window] = tv;
                }
                var (ax, az) = GhostHunt.TokenAnchors[tk.Window % GhostHunt.TokenAnchors.Length];
                var at = Project(ax, az);
                tv.style.left = at.x - 24f; tv.style.top = at.y - 90f + Mathf.Sin(_pulse * 2f) * 6f;
            }
            var deadTokens = new List<int>();
            foreach (var kv in _tokens) if (!liveTokens.Contains(kv.Key)) deadTokens.Add(kv.Key);
            foreach (var d in deadTokens) { _tokens[d].RemoveFromHierarchy(); _tokens.Remove(d); }
            // the lantern brightens the night (a warm veil over the scenery), the fog drifts
            if (_stage != null) RenderStage();
            else MoveScenery(s.LanternT > 0.0);
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.ghostHunt.escapes", ("n", s.EscapedWaves), ("max", _game.Tune.EndlessEscapeLimit))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            var pills = new List<string>();
            if (s.Chain > 1) pills.Add(t.T("mg.ghostHunt.chainPill", ("n", s.Chain)));
            if (s.NetLeft > 0) pills.Add(t.T("mg.ghostHunt.netPill", ("n", s.NetLeft)));
            if (s.LanternT > 0.0) pills.Add(t.T("mg.ghostHunt.lanternPill", ("n", Mathf.CeilToInt((float)s.LanternT))));
            _chainLabel.text = string.Join("   ", pills);
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _spots.Clear(); _decoys.Clear(); _ghosts.Clear(); _tokens.Clear();
            _chainLabel?.RemoveFromHierarchy();
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
