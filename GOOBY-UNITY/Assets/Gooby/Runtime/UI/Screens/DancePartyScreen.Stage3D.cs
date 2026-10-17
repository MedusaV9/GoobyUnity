using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D disco club of the Tanzparty (port of dance_party_stage3d.gd): notes fall as glowing balls with dark backing
    /// discs through three glass lanes onto camera-facing hit rings over a glowing hit line; behind them the club pumps on
    /// the beat — a checkered floor that glows in the lane colours, an equalizer wall, speaker towers with thumping woofers,
    /// sweeping spot cones, string lights and the mirror ball spinning faster with every combo tier. The player's Gooby is
    /// the star at the front, dancing and hopping on every beat, flanked by four Gooby fans who dance along. Notes and lanes
    /// sit on the engine plane of the default camera, so the lanes are exactly where the taps land.
    /// </summary>
    public sealed partial class DancePartyScreen
    {
        private World.MgStage3D _stage;
        private readonly Dictionary<Gooby.Core.Minigames.Play.DancePartyGame.ChartNote, Transform> _notes3d = new Dictionary<Gooby.Core.Minigames.Play.DancePartyGame.ChartNote, Transform>();
        private readonly Stack<Transform>[] _notePool = { new Stack<Transform>(), new Stack<Transform>(), new Stack<Transform>() };
        private readonly HashSet<Gooby.Core.Minigames.Play.DancePartyGame.ChartNote> _liveNotes = new HashSet<Gooby.Core.Minigames.Play.DancePartyGame.ChartNote>();
        private readonly List<Gooby.Core.Minigames.Play.DancePartyGame.ChartNote> _deadNotes = new List<Gooby.Core.Minigames.Play.DancePartyGame.ChartNote>();
        private readonly List<Renderer> _tiles = new List<Renderer>();
        private readonly List<Transform> _eqBars = new List<Transform>();
        private readonly List<Transform> _woofers = new List<Transform>();
        private readonly List<Transform> _cones = new List<Transform>();
        private readonly List<Transform> _laneGlass = new List<Transform>();
        private readonly List<Material> _ownedMats = new List<Material>();
        private readonly List<World.GoobyAvatar> _fans = new List<World.GoobyAvatar>();
        private Material[] _tileLit, _laneMats;
        private Material _tileDark, _tileDim;
        private Transform _ball, _ballSparkles;
        private float _floorY, _danceYaw;
        private int _lastBeat = -1;

        private static readonly string[] FanFurs = { "fell_minze", "fell_zuckerwatte", "fell_himmel", "fell_lavendel" };

        protected override bool StageHasGooby => _stage != null;

        /// <summary>Field pixel → stage world on the engine plane (depth + behind it).</summary>
        private Vector3 F(float fx, float fy, float depth = 0f) =>
            _stage.ViewToWorld(fx * ViewW / Mathf.Max(1f, FieldW), fy * ViewH / Mathf.Max(1f, FieldH), depth);

        private float FieldToM => (float)(ViewW / Mathf.Max(1f, FieldW)) * World.MgStage3D.MetresPerPx;

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#3C2E70"));
            _stage.Bind(Field);
            var root = _stage.Root.transform;
            var o = World.MgStage3D.Origin;
            _floorY = 0.62f;
            // the checkered dance floor, every tile its own renderer so the beat can light it
            _tileDark = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#6A57A8"), 0.6f);
            _tileDim = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#4A3C80"), 0.6f);
            _tileLit = new Material[LaneColors.Length];
            _laneMats = new Material[LaneColors.Length];
            for (var i = 0; i < LaneColors.Length; i++)
            {
                _tileLit[i] = new Material(World.ArtLibrary.Glow(Color.Lerp(FloorLight, LaneColors[i], 0.35f)));
                _laneMats[i] = new Material(World.ArtLibrary.Transparent(new Color(LaneColors[i].r, LaneColors[i].g, LaneColors[i].b, 0.08f)));
                _ownedMats.Add(_tileLit[i]);
                _ownedMats.Add(_laneMats[i]);
            }
            const float tile = 0.62f;
            for (var row = 0; row < 12; row++)
                for (var col = -5; col <= 5; col++)
                {
                    var t = World.ArtLibrary.Box(root, "Tile", new Vector3(o.x + col * tile, _floorY - 0.05f, o.z - 2.4f + row * tile), new Vector3(tile * 0.97f, 0.1f, tile * 0.97f), (row + col) % 2 == 0 ? _tileLit[(row + col + 20) % 3] : _tileDark, false);
                    _tiles.Add(t.GetComponent<Renderer>());
                }
            // the club behind: violet back wall, an equalizer wall, pink valance, string lights and side walls
            World.ArtLibrary.Box(root, "Wall", new Vector3(o.x, 5f, o.z + 5.4f), new Vector3(16f, 12f, 0.2f), World.ArtLibrary.Pbr("plaster", 2.5f, World.ArtLibrary.Hex("#5B4A9E"), 0.15f), false);
            var eq = new GameObject("EQ").transform;
            eq.SetParent(root, false);
            for (var i = 0; i < 13; i++)
            {
                var bar = World.ArtLibrary.Box(eq, "EqBar", new Vector3(o.x - 3.6f + i * 0.6f, _floorY + 1.6f, o.z + 5.2f), new Vector3(0.42f, 1f, 0.1f), World.ArtLibrary.Glow(Color.Lerp(LaneColors[i % 3], Color.white, 0.2f)), false).transform;
                _eqBars.Add(bar);
            }
            World.ArtLibrary.Box(root, "Valance", new Vector3(o.x, 8.1f, o.z + 3.5f), new Vector3(9f, 0.5f, 0.1f), World.ArtLibrary.Pbr("fabric", 0.5f, World.ArtLibrary.Hex("#FF9BC1"), 0.15f), false);
            for (var i = 0; i < 12; i++)
                World.ArtLibrary.Sphere(root, "Scallop", new Vector3(o.x - 4.1f + i * 0.75f, 7.85f, o.z + 3.45f), 0.38f, World.ArtLibrary.Pbr("fabric", 0.5f, World.ArtLibrary.Hex("#FF9BC1"), 0.15f), false).transform.localScale = new Vector3(0.76f, 0.5f, 0.12f);
            var bulbs = new[] { "#FF9BC1", "#FFD166", "#9BE7E1", "#B7D8FF", "#C6F39A" };
            for (var s = 0; s < 2; s++)
                for (var i = 0; i < 11; i++)
                {
                    var t = i / 10f;
                    var p = new Vector3(o.x - 3.4f + t * 6.8f, 7.2f - s * 0.9f - Mathf.Sin(t * Mathf.PI) * 0.5f, o.z + 2.6f + s * 0.8f);
                    World.ArtLibrary.Sphere(root, "Bulb", p, 0.07f, World.ArtLibrary.Glow(World.ArtLibrary.Hex(bulbs[(i + s * 2) % bulbs.Length]) * 1.3f), false);
                }
            foreach (var side in new[] { -1f, 1f })
            {
                World.ArtLibrary.Box(root, "SideWall", new Vector3(o.x + side * 4.4f, 5f, o.z + 1.5f), new Vector3(0.2f, 12f, 8f), World.ArtLibrary.Pbr("plaster", 2.5f, World.ArtLibrary.Hex("#4E3F8C"), 0.15f), false);
                for (var k = 0; k < 3; k++)
                    World.ArtLibrary.Box(root, "Neon", new Vector3(o.x + side * 4.25f, _floorY + 2.6f, o.z - 0.5f + k * 1.8f), new Vector3(0.06f, 3.6f, 0.06f), World.ArtLibrary.Glow(LaneColors[k] * 1.2f), false);
                Speaker(root, new Vector3(o.x + side * 1.72f, _floorY, o.z + 1.4f));
                // spot cones sweeping from the ceiling corners
                var cone = _stage.Cone(new Vector3(o.x + side * 1.6f, 8.6f, o.z + 1.2f), 1.1f, 6.8f, World.ArtLibrary.Transparent(new Color(1f, 0.92f, 1f, 0.06f))).transform;
                cone.rotation = Quaternion.Euler(180f, 0f, 0f);
                _cones.Add(cone);
            }
            var middleCone = _stage.Cone(new Vector3(o.x, 8.6f, o.z + 0.4f), 1.2f, 7.4f, World.ArtLibrary.Transparent(new Color(1f, 0.85f, 0.95f, 0.05f))).transform;
            middleCone.rotation = Quaternion.Euler(180f, 0f, 0f);
            _cones.Add(middleCone);
            // the mirror ball on its chain
            World.ArtLibrary.Cylinder(root, "Chain", new Vector3(o.x, 8.15f, o.z + 1f), 0.015f, 0.9f, World.ArtLibrary.Solid(new Color(0.75f, 0.75f, 0.82f), 0.8f), false);
            _ball = World.ArtLibrary.Sphere(root, "MirrorBall", new Vector3(o.x, 7.45f, o.z + 1f), 0.42f, World.ArtLibrary.Solid(new Color(0.86f, 0.88f, 0.96f), 0.95f)).transform;
            _ballSparkles = new GameObject("Facets").transform;
            _ballSparkles.SetParent(_ball, false);
            for (var i = 0; i < 26; i++)
            {
                var a = i * 2.39996f;
                var y = 1f - (i + 0.5f) / 13f;
                var r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                var facet = World.ArtLibrary.Box(_ballSparkles, "Facet", Vector3.zero, Vector3.one * 0.1f, World.ArtLibrary.Glow(Color.Lerp(Color.white, LaneColors[i % 3], 0.4f) * 1.2f), false).transform;
                facet.localPosition = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) * 0.5f;
                facet.localScale = Vector3.one * 0.09f;
            }
            // lanes: glass columns, the camera-facing hit rings and the glowing hit line
            var hitY = HitLineY;
            for (var lane = 0; lane < 3; lane++)
            {
                var x = LaneX(lane);
                var top = F(x, hitY - FieldH * TravelFrac);
                var bottom = F(x, hitY + 14f);
                var w = LaneSpan * 0.86f * FieldToM;
                var glass = World.ArtLibrary.Box(root, "LaneGlass", (top + bottom) * 0.5f + Vector3.forward * 0.3f, new Vector3(w, top.y - bottom.y, 0.05f), _laneMats[lane], false).transform;
                _laneGlass.Add(glass);
                foreach (var edge in new[] { -0.5f, 0.5f })
                    World.ArtLibrary.Box(root, "Rail", (top + bottom) * 0.5f + new Vector3(edge * w, 0f, 0.28f), new Vector3(0.03f, top.y - bottom.y, 0.03f), World.ArtLibrary.Glow(LaneColors[lane] * 0.9f), false);
                var ring = World.RanchDressing.Ring(root, "HitRing", w * 0.38f, w * 0.48f, World.ArtLibrary.Glow(LaneColors[lane] * 1.15f));
                ring.position = F(x, hitY, -0.05f);
                ring.rotation = Quaternion.Euler(-90f, 0f, 0f);
                var pad = World.RanchDressing.Ring(root, "Pad", 0f, w * 0.38f, World.ArtLibrary.Transparent(new Color(0f, 0f, 0f, 0.35f)));
                pad.position = F(x, hitY, -0.04f);
                pad.rotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            var lineL = F(FieldW * 0.5f - LaneSpan * 1.5f, hitY, -0.02f);
            var lineR = F(FieldW * 0.5f + LaneSpan * 1.5f, hitY, -0.02f);
            World.ArtLibrary.Box(root, "HitLine", (lineL + lineR) * 0.5f, new Vector3(lineR.x - lineL.x, 0.035f, 0.02f), World.ArtLibrary.Glow(new Color(1.2f, 1.2f, 1.25f)), false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnDancer(outfit);
            for (var i = 0; i < 4; i++) _ = SpawnFan(i);
        }

        private void Speaker(Transform root, Vector3 foot)
        {
            World.ArtLibrary.Box(root, "Speaker", foot + Vector3.up * 0.75f, new Vector3(0.62f, 1.5f, 0.5f), World.ArtLibrary.Pbr("wood_planks", 0.8f, World.ArtLibrary.Hex("#4A3C80"), 0.3f));
            foreach (var (y, r) in new[] { (1.15f, 0.16f), (0.5f, 0.24f) })
            {
                var pivot = new GameObject("Woofer").transform;
                pivot.SetParent(root, false);
                pivot.position = foot + new Vector3(0f, y, -0.26f);
                var rim = World.ArtLibrary.Cylinder(pivot, "Rim", pivot.position, r, 0.04f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8A78C8"), 0.5f), false).transform;
                rim.rotation = Quaternion.Euler(90f, 0f, 0f);
                var cone = World.ArtLibrary.Cylinder(pivot, "Cone", pivot.position + new Vector3(0f, 0f, -0.02f), r * 0.45f, 0.05f, World.ArtLibrary.Glow(LaneColors[(int)(y * 2f) % 3] * 0.8f), false).transform;
                cone.rotation = rim.rotation;
                _woofers.Add(pivot);
            }
        }

        private async System.Threading.Tasks.Task SpawnDancer(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(F(FieldW * 0.5f, FieldH * 0.95f, -0.6f), 0.95f, outfit);
            if (g == null || _stage == null) return;
            var p = g.transform.position;
            g.transform.position = new Vector3(p.x, World.MgStage3D.Origin.y + _floorY, p.z);
            g.Play("dance-loop", 0f);
            g.SetEmotion("happy");
        }

        private async System.Threading.Tasks.Task SpawnFan(int i)
        {
            var side = i % 2 == 0 ? -1f : 1f;
            var at = new Vector3(World.MgStage3D.Origin.x + side * (1.05f + (i / 2) * 0.75f), World.MgStage3D.Origin.y + _floorY, World.MgStage3D.Origin.z + 2.2f + (i / 2) * 0.6f);
            var g = await World.GoobyAvatar.CreateAsync(_stage.Root.transform, at);
            if (g == null) return;
            if (_stage == null) { Object.Destroy(g.gameObject); return; }
            g.transform.position = at;
            g.transform.rotation = Quaternion.Euler(0f, 180f - side * 18f, 0f);
            g.transform.localScale = Vector3.one * (0.68f + (i % 2) * 0.05f);
            g.Wear(new Dictionary<string, string> { ["fell"] = FanFurs[i % FanFurs.Length], ["hut"] = i == 1 ? "partyHat" : i == 2 ? "cap" : null });
            g.Play("dance-loop", 0f);
            g.SetEmotion(i % 2 == 0 ? "ecstatic" : "happy");
            World.RanchDressing.QuietShadows(g.transform);
            _fans.Add(g);
        }

        private Transform NewNote(int lane)
        {
            if (_notePool[lane].Count > 0)
            {
                var pooled = _notePool[lane].Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            var t = new GameObject("Note").transform;
            t.SetParent(_stage.Root.transform, false);
            var back = World.RanchDressing.Ring(t, "Back", 0f, 0.3f, World.ArtLibrary.Solid(new Color(0.12f, 0.07f, 0.2f), 0.3f));
            back.localPosition = new Vector3(0f, 0f, 0.06f);
            back.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var halo = World.RanchDressing.Ring(t, "Halo", 0.24f, 0.36f, World.ArtLibrary.Glow(LaneColors[lane] * 0.8f));
            halo.localPosition = new Vector3(0f, 0f, 0.04f);
            halo.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ball = World.ArtLibrary.Sphere(t, "Ball", Vector3.zero, 0.22f, World.ArtLibrary.Glow(LaneColors[lane] * 1.1f), false).transform;
            ball.localPosition = Vector3.zero;
            var shine = World.ArtLibrary.Sphere(t, "Shine", Vector3.zero, 0.06f, World.ArtLibrary.Glow(new Color(1.4f, 1.4f, 1.4f)), false).transform;
            shine.localPosition = new Vector3(-0.07f, 0.08f, -0.18f);
            return t;
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            var beatF = (float)(g.SongTime * BeatsPerSec);
            var beat = Mathf.FloorToInt(beatF);
            var pulse = Mathf.Max(0f, 1f - Mathf.Repeat(beatF, 1f) * 2.5f);
            // floor: the lit tiles swap colours on every beat; a miss dims the club for a moment
            if (beat != _lastBeat)
            {
                _lastBeat = beat;
                for (var i = 0; i < _tiles.Count; i++)
                {
                    var row = i / 11; var col = i % 11;
                    var lit = (((row + col + beat) % 2) + 2) % 2 == 0;
                    _tiles[i].sharedMaterial = lit ? _tileLit[(((row + col / 2 + beat) % 3) + 3) % 3] : (_missDim > 0.3f ? _tileDim : _tileDark);
                }
            }
            var glow = 0.75f + 0.5f * pulse + 0.25f * g.Tier + _encoreGlow * 0.6f - _missDim * 0.5f;
            for (var i = 0; i < _tileLit.Length; i++)
            {
                var c = Color.Lerp(FloorLight, LaneColors[i], 0.35f + 0.2f * g.Tier / 3f) * glow;
                c.a = 1f;
                if (_tileLit[i].HasProperty("_BaseColor")) _tileLit[i].SetColor("_BaseColor", c);
                if (_tileLit[i].HasProperty("_EmissionColor")) _tileLit[i].SetColor("_EmissionColor", c);
            }
            // the equalizer wall, the woofers and the spot cones pump with the beat
            for (var i = 0; i < _eqBars.Count; i++)
            {
                var h = 0.4f + Mathf.Abs(Mathf.Sin(beatF * Mathf.PI * 0.5f + i * 0.9f)) * (1.2f + 0.5f * g.Tier) * (0.6f + 0.4f * pulse);
                var b = _eqBars[i];
                b.localScale = new Vector3(0.42f, h, 0.1f);
                b.position = new Vector3(b.position.x, World.MgStage3D.Origin.y + _floorY + 1.1f + h * 0.5f, b.position.z);
            }
            for (var i = 0; i < _woofers.Count; i++)
            {
                var k = 1f + (ReducedMotion ? 0f : pulse * 0.12f);
                _woofers[i].localScale = new Vector3(k, k, 1f);
            }
            for (var i = 0; i < _cones.Count; i++)
            {
                var sway = ReducedMotion ? 0f : Mathf.Sin(_show * (0.7f + i * 0.2f) + i * 2f) * 16f;
                _cones[i].rotation = Quaternion.Euler(180f, 0f, sway);
            }
            // the mirror ball spins faster with the tier and pops on hits
            _ball.rotation = Quaternion.Euler(0f, _ballSpin * 60f, 0f);
            _ball.localScale = Vector3.one * 0.84f * (1f + _ballPop * 0.35f);
            for (var lane = 0; lane < 3; lane++)
            {
                var a = 0.07f + _laneFlash[lane] * 1.4f;
                var c = new Color(LaneColors[lane].r, LaneColors[lane].g, LaneColors[lane].b, Mathf.Min(0.45f, a));
                _laneMats[lane].color = c;
                if (_laneMats[lane].HasProperty("_BaseColor")) _laneMats[lane].SetColor("_BaseColor", c);
            }
            // notes fall down their lanes
            _liveNotes.Clear();
            foreach (var (note, progress) in g.Visible())
            {
                var y = NoteY(progress);
                if (y < -30f) continue;
                _liveNotes.Add(note);
                if (!_notes3d.TryGetValue(note, out var tr)) _notes3d[note] = tr = NewNote(note.Lane);
                tr.position = F(LaneX(note.Lane), y, -0.1f);
                tr.localScale = Vector3.one * (0.85f + 0.3f * Mathf.Clamp01((float)progress));
            }
            _deadNotes.Clear();
            foreach (var kv in _notes3d) if (!_liveNotes.Contains(kv.Key)) _deadNotes.Add(kv.Key);
            foreach (var d in _deadNotes)
            {
                var tr = _notes3d[d];
                tr.gameObject.SetActive(false);
                _notePool[Mathf.Clamp(d.Lane, 0, 2)].Push(tr);
                _notes3d.Remove(d);
            }
            // Gooby hops on every beat, swaying more with the tier; the fans bob along
            var gb = _stage.Gooby;
            var bob = ReducedMotion ? 0f : Mathf.Abs(Mathf.Sin(beatF * Mathf.PI)) * (0.12f + 0.06f * g.Tier);
            if (gb != null)
            {
                var p = gb.transform.position;
                gb.transform.position = new Vector3(p.x, World.MgStage3D.Origin.y + _floorY + bob, p.z);
                _danceYaw = Mathf.Sin(beatF * Mathf.PI * 0.5f) * (12f + 6f * g.Tier);
                gb.transform.rotation = Quaternion.Euler(0f, 180f + _danceYaw, 0f);
                var squash = 1f - bob * 0.5f;
                gb.transform.localScale = new Vector3(2f - squash, squash, 2f - squash) * 0.95f;
            }
            for (var i = 0; i < _fans.Count; i++)
            {
                var f = _fans[i];
                if (f == null) continue;
                var fp = f.transform.position;
                var fb = ReducedMotion ? 0f : Mathf.Abs(Mathf.Sin(beatF * Mathf.PI + i * 0.8f)) * 0.08f * (1f + _encoreGlow);
                f.transform.position = new Vector3(fp.x, World.MgStage3D.Origin.y + _floorY + fb, fp.z);
            }
        }

        private void StageJudged(string kind)
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            if (kind == "miss") { gb.SetEmotion("sad"); return; }
            gb.SetEmotion(kind == "perfect" ? "ecstatic" : "happy");
            if (_game != null && _game.Combo > 0 && _game.Combo % 8 == 0)
            {
                gb.Play("celebrate", 0.1f);
                gb.Play("dance-loop", 0.4f);
            }
        }

        private void StageEncore()
        {
            if (_stage == null || ReducedMotion) return;
            for (var i = 0; i < 3; i++)
                World.Effects.Puff(World.MgStage3D.At(-1.4f + i * 1.4f, 7f, 0.5f), Color.Lerp(LaneColors[i], Color.white, 0.2f), 18, 0.12f, 1.6f, 2.2f, Vector3.down, 1f);
            foreach (var f in _fans) if (f != null) { f.Play("celebrate", 0.1f); f.SetEmotion("ecstatic"); }
        }

        private void DropStage()
        {
            _notes3d.Clear();
            foreach (var p in _notePool) p.Clear();
            _liveNotes.Clear(); _deadNotes.Clear();
            _tiles.Clear(); _eqBars.Clear(); _woofers.Clear(); _cones.Clear(); _laneGlass.Clear(); _fans.Clear();
            _stage?.Destroy();
            _stage = null;
            foreach (var m in _ownedMats) if (m != null) Object.Destroy(m);
            _ownedMats.Clear();
            _lastBeat = -1;
        }
    }
}
