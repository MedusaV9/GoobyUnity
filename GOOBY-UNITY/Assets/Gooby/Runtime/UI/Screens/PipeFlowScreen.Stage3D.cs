using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D pipe panel of the Rohr-Wirrwarr (port of pipe_flow_stage3d.gd): the 5×5 board is a wooden panel in front of a
    /// warm garden wall with ivy; every tile is a real pipe piece (metal arms round a hub on a cream plate) that visibly
    /// turns when tapped, everything hooked to the tap glows with water, a brass faucet feeds the board from the top and the
    /// sprinkler at its foot sprays the flower bed when a line is solved. A leak rings the dripping joint red. The player's
    /// Gooby stands on the lawn in front and screws along, cheers on a solve and jumps at a leak. The panel stands on the
    /// engine plane of the default camera, so every tile is exactly behind its 2D tap square.
    /// </summary>
    public sealed partial class PipeFlowScreen
    {
        private sealed class Tile3D
        {
            public Transform Root, Spin;
            public Renderer Plate;
            public readonly List<Renderer> Pipes = new List<Renderer>();
            public int Mask = -1;
        }

        private World.MgStage3D _stage;
        private readonly List<Tile3D> _tiles3d = new List<Tile3D>();
        private Material _metal, _water, _plate, _plateWet;
        private Transform _faucet, _sprinkler, _leakRing;
        private float _sprayEmit, _dripEmit, _groundY;

        protected override bool StageHasGooby => _stage != null;

        /// <summary>Field pixel → stage world on the panel plane (depth + behind it).</summary>
        private Vector3 F(float fx, float fy, float depth = 0f) =>
            _stage.ViewToWorld(fx * ViewW / Mathf.Max(1f, FieldW), fy * ViewH / Mathf.Max(1f, FieldH), depth);

        private float CellM => _cell * (float)(ViewW / Mathf.Max(1f, FieldW)) * World.MgStage3D.MetresPerPx;

        private void BuildStage()
        {
            EnsureFieldSize();
            LayoutBoard();
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#A9D3FF"));
            _stage.Bind(Field);
            var root = _stage.Root.transform;
            _groundY = 1.2f;
            _metal = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#9AA8B4"), 0.75f);
            _water = World.ArtLibrary.Glow(new Color(0.42f, 1f, 1.15f));
            _plate = World.ArtLibrary.Pbr("tiles", 0.5f, World.ArtLibrary.Hex("#F1E8D6"), 0.35f);
            _plateWet = World.ArtLibrary.Pbr("tiles", 0.5f, World.ArtLibrary.Hex("#BFE6F2"), 0.45f);
            // the garden: lawn, the warm wall with coping and ivy, sky dressing over it, the flower bed at the front
            _stage.Ground(_groundY, World.ArtLibrary.Hex("#9CD38E"), "grass", 40f, 30f, 6f);
            var wallTop = F(0f, FieldH * 0.13f, 0.5f).y;
            World.ArtLibrary.Box(root, "Wall", World.MgStage3D.At(0f, (wallTop + _groundY) * 0.5f - World.MgStage3D.Origin.y, 0.75f), new Vector3(16f, wallTop - _groundY, 0.4f),
                World.ArtLibrary.PbrSlab("paving", new Vector2(16f, wallTop - _groundY), 1.1f, World.ArtLibrary.Hex("#F0BFA2"), 0.15f, 1.4f), false);
            World.ArtLibrary.Box(root, "Coping", World.MgStage3D.At(0f, wallTop + 0.08f - World.MgStage3D.Origin.y, 0.7f), new Vector3(16f, 0.18f, 0.56f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#EFE2D0"), 0.3f), false);
            var leaf = World.ArtLibrary.Pbr("leaves", 0.6f, World.ArtLibrary.Hex("#7CC486"), 0.1f);
            var leafLight = World.ArtLibrary.Pbr("leaves", 0.6f, World.ArtLibrary.Hex("#9ED499"), 0.1f);
            foreach (var side in new[] { -1f, 1f })
                for (var i = 0; i < 9; i++)
                {
                    var y = wallTop - 0.15f - i * 0.42f;
                    var x = side * (1.75f + Mathf.Sin(i * 1.3f) * 0.18f);
                    var ivy = World.ArtLibrary.Sphere(root, "Ivy", World.MgStage3D.At(x, y - World.MgStage3D.Origin.y, 0.5f), 0.16f + (i % 3) * 0.03f, i % 2 == 0 ? leaf : leafLight, false).transform;
                    ivy.localScale = new Vector3(1.3f, 1f, 0.6f) * ivy.localScale.x;
                }
            _stage.Clouds(3, wallTop + 0.9f - World.MgStage3D.Origin.y, 12f, 14f, 1.1f, 5);
            _stage.Sun(new Vector3(2.2f, wallTop + 1.4f - World.MgStage3D.Origin.y, 20f), 0.7f);
            _stage.Row("tree_round", _groundY, 6f, -8f, 8f, 5, 1.6f, 0f, 0.5f, 17);
            var bedZ = -2.2f;
            World.ArtLibrary.Box(root, "Bed", World.MgStage3D.At(0f, _groundY + 0.04f, bedZ), new Vector3(16f, 0.1f, 1.6f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#A67A5C"), 0.1f), false);
            string[] blooms = { "flower_redA", "flower_yellowA", "flower_purpleA" };
            for (var i = 0; i < 11; i++)
                _stage.Prop(blooms[i % 3], World.MgStage3D.At(-2.5f + i * 0.5f, _groundY + 0.09f, bedZ + (i % 2) * 0.45f - 0.2f), 1.5f, i * 53f);
            // the wooden board panel and its tiles
            var n = _game.Size;
            var board = _cell * n;
            var tl = F(_ox - 10f, _oy - 10f, 0.12f);
            var br = F(_ox + board + 10f, _oy + board + 10f, 0.12f);
            var centre = (tl + br) * 0.5f;
            World.ArtLibrary.Box(root, "Panel", centre, new Vector3(br.x - tl.x, tl.y - br.y, 0.12f), World.ArtLibrary.Pbr("wood_planks", 0.9f, World.ArtLibrary.Hex("#D9A77A"), 0.25f));
            World.ArtLibrary.Box(root, "PanelLeg", new Vector3(centre.x - (br.x - tl.x) * 0.38f, (br.y + World.MgStage3D.Origin.y + _groundY) * 0.5f, centre.z + 0.05f), new Vector3(0.12f, br.y - World.MgStage3D.Origin.y - _groundY + 0.05f, 0.12f), World.ArtLibrary.Pbr("wood_planks", 0.9f, World.ArtLibrary.Hex("#B98A5E"), 0.25f));
            World.ArtLibrary.Box(root, "PanelLeg", new Vector3(centre.x + (br.x - tl.x) * 0.38f, (br.y + World.MgStage3D.Origin.y + _groundY) * 0.5f, centre.z + 0.05f), new Vector3(0.12f, br.y - World.MgStage3D.Origin.y - _groundY + 0.05f, 0.12f), World.ArtLibrary.Pbr("wood_planks", 0.9f, World.ArtLibrary.Hex("#B98A5E"), 0.25f));
            for (var i = 0; i < n * n; i++) _tiles3d.Add(NewTile(root));
            // the faucet over the source column, the sprinkler under the goal column
            var brass = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#C8943F"), 0.7f);
            _faucet = new GameObject("Faucet").transform;
            _faucet.SetParent(root, false);
            World.ArtLibrary.Cylinder(_faucet, "Riser", Vector3.zero, 0.07f, 0.5f, brass).transform.localPosition = new Vector3(0f, 0.25f, 0f);
            var spout = World.ArtLibrary.Cylinder(_faucet, "Spout", Vector3.zero, 0.07f, 0.3f, brass).transform;
            spout.localPosition = new Vector3(0f, 0.5f, -0.12f);
            spout.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var handle = World.ArtLibrary.Box(_faucet, "Handle", Vector3.zero, new Vector3(0.36f, 0.05f, 0.05f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#DC383D"), 0.5f)).transform;
            handle.localPosition = new Vector3(0f, 0.62f, 0f);
            _sprinkler = new GameObject("Sprinkler").transform;
            _sprinkler.SetParent(root, false);
            World.ArtLibrary.Cylinder(_sprinkler, "Stem", Vector3.zero, 0.04f, 0.4f, brass).transform.localPosition = new Vector3(0f, 0.2f, 0f);
            World.ArtLibrary.Sphere(_sprinkler, "Head", Vector3.zero, 0.09f, brass).transform.localPosition = new Vector3(0f, 0.42f, 0f);
            _leakRing = _stage.Torus(null, "Leak", Vector3.zero, 1f, 0.06f, World.ArtLibrary.Glow(new Color(1.3f, 0.4f, 0.4f))).transform;
            _leakRing.gameObject.SetActive(false);
            PlaceFittings();
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnPlumber(outfit);
        }

        private async System.Threading.Tasks.Task SpawnPlumber(Dictionary<string, string> outfit)
        {
            var at = _stage.GroundPoint(ViewW * 0.5, ViewH * 0.985, _groundY);
            var g = await _stage.SpawnGoobyAsync(at, 0.72f, outfit);
            if (g == null || _stage == null) return;
            g.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            g.Play("idle-loop");
            g.SetEmotion("happy");
        }

        private Tile3D NewTile(Transform root)
        {
            var t = new Tile3D { Root = new GameObject("Tile").transform };
            t.Root.SetParent(root, false);
            var plate = World.ArtLibrary.Box(t.Root, "Plate", Vector3.zero, Vector3.one, _plate, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            plate.transform.localScale = new Vector3(0.92f, 0.92f, 0.06f);
            t.Plate = plate.GetComponent<Renderer>();
            t.Spin = new GameObject("Spin").transform;
            t.Spin.SetParent(t.Root, false);
            return t;
        }

        /// <summary>Arms toward every open side (north, east, south, west) round a hub — rebuilt only when the opening mask changes.</summary>
        private void BuildPipes(Tile3D t, int mask)
        {
            for (var i = t.Spin.childCount - 1; i >= 0; i--) Object.Destroy(t.Spin.GetChild(i).gameObject);
            t.Pipes.Clear();
            t.Mask = mask;
            var hub = World.ArtLibrary.Sphere(t.Spin, "Hub", Vector3.zero, 0.5f, _metal, false);
            hub.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            hub.transform.localScale = Vector3.one * 0.26f;
            t.Pipes.Add(hub.GetComponent<Renderer>());
            for (var d = 0; d < 4; d++)
            {
                if ((mask & (1 << d)) == 0) continue;
                var dir = d == 0 ? Vector3.up : d == 1 ? Vector3.right : d == 2 ? Vector3.down : Vector3.left;
                var arm = World.ArtLibrary.Cylinder(t.Spin, "Arm", Vector3.zero, 0.5f, 1f, _metal, false).transform;
                arm.localPosition = dir * 0.25f + new Vector3(0f, 0f, -0.06f);
                arm.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                arm.localScale = new Vector3(0.18f, 0.25f, 0.18f);
                t.Pipes.Add(arm.GetComponent<Renderer>());
                var collar = World.ArtLibrary.Cylinder(t.Spin, "Collar", Vector3.zero, 0.5f, 1f, _metal, false).transform;
                collar.localPosition = dir * 0.44f + new Vector3(0f, 0f, -0.06f);
                collar.localRotation = arm.localRotation;
                collar.localScale = new Vector3(0.24f, 0.03f, 0.24f);
                t.Pipes.Add(collar.GetComponent<Renderer>());
            }
        }

        private void PlaceFittings()
        {
            var n = _game.Size;
            var cm = CellM;
            var tap = F(_ox + (_game.Board.SrcCol + 0.5f) * _cell, _oy, -0.05f);
            _faucet.position = tap + Vector3.up * 0.02f;
            _faucet.localScale = Vector3.one * cm * 1.3f;
            var spr = F(_ox + (_game.Board.GoalCol + 0.5f) * _cell, _oy + _cell * n, -0.05f);
            _sprinkler.position = new Vector3(spr.x, World.MgStage3D.Origin.y + _groundY, spr.z - 0.6f);
            _sprinkler.localScale = Vector3.one * 1.2f;
        }

        private int _fittingsFor = -1;

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            var n = g.Size;
            if (g.PuzzleNo != _fittingsFor) { _fittingsFor = g.PuzzleNo; PlaceFittings(); }
            var cm = CellM;
            for (var i = 0; i < _tiles3d.Count && i < n * n; i++)
            {
                var t = _tiles3d[i];
                var tile = g.Board.Tiles[i];
                var mask = PipeFlow.MaskOf(tile.Shape, tile.Rot);
                if (mask != t.Mask) BuildPipes(t, mask);
                var c = CellCentre(i);
                t.Root.position = F(c.x, c.y);
                t.Root.localScale = new Vector3(cm, cm, cm);
                var frac = (float)(g.Spin[i] / System.Math.Max(1e-6, g.Tune.RotateSec));
                t.Spin.localRotation = Quaternion.Euler(0f, 0f, frac * 90f);
                var pop = 1f + (ReducedMotion ? 0f : Mathf.Sin(Mathf.Clamp01(frac) * Mathf.PI) * 0.08f);
                t.Spin.localScale = Vector3.one * pop;
                var wet = g.TileWatered(i) || g.TileReached(i);
                t.Plate.sharedMaterial = wet ? _plateWet : _plate;
                foreach (var r in t.Pipes) r.sharedMaterial = wet ? _water : _metal;
            }
            // the leak: a red ring round the dripping joint and drops
            if (g.LeakPending && g.LeakIndex >= 0)
            {
                var c = CellCentre(g.LeakIndex);
                _leakRing.gameObject.SetActive(true);
                _leakRing.position = F(c.x, c.y, -0.18f);
                _leakRing.rotation = Quaternion.Euler(-90f, 0f, 0f);
                _leakRing.localScale = Vector3.one * cm * 0.45f * (1f + (ReducedMotion ? 0f : Mathf.Sin(_show * 10f) * 0.06f));
                _dripEmit -= dt;
                if (_dripEmit <= 0f && !ReducedMotion)
                {
                    _dripEmit = 0.25f;
                    World.Effects.Puff(F(c.x, c.y, -0.2f), new Color(0.45f, 0.85f, 1f, 0.9f), 1, 0.06f, 1.5f, 0.6f, Vector3.down, 0.05f);
                }
            }
            else _leakRing.gameObject.SetActive(false);
            // water runs out of the faucet while the line fills; the sprinkler sprays the bed
            if (g.Filling && !ReducedMotion)
            {
                _sprayEmit -= dt;
                if (_sprayEmit <= 0f)
                {
                    _sprayEmit = 0.05f;
                    World.Effects.Puff(_sprinkler.position + Vector3.up * 0.5f, new Color(0.55f, 0.88f, 1f, 0.85f), 4, 0.07f, 2.6f, 0.6f, new Vector3(Random.value - 0.5f, 1f, -0.4f), 0.5f);
                }
            }
            var gb = _stage.Gooby;
            if (gb != null) gb.transform.rotation = Quaternion.Euler(0f, 180f + Mathf.Sin(_show * 1.3f) * 10f, 0f);
        }

        private void StageTapped()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("build_hammer-loop", 0.1f);
            _hammerLeft = 0.6f;
        }

        private float _hammerLeft;

        private void TickHammer(float dt)
        {
            if (_hammerLeft <= 0f) return;
            _hammerLeft -= dt;
            if (_hammerLeft <= 0f) _stage?.Gooby?.Play("idle-loop", 0.2f);
        }

        private void StageSolved()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("celebrate", 0.1f);
            gb.SetEmotion("ecstatic");
            _hammerLeft = 0f;
            if (!ReducedMotion) World.Effects.Sparkle(_sprinkler.position + Vector3.up * 0.6f, 0.6f, 24);
        }

        private void StageLeaked()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("hop", 0.1f);
            gb.SetEmotion("scared");
        }

        private void DropStage()
        {
            _tiles3d.Clear();
            _fittingsFor = -1;
            _hammerLeft = 0f;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
