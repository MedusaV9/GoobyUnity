using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D park course of the Minigolf (port of mini_golf_scenery.gd + mini_golf_course.gd): every hole is built as
    /// a felt fairway of checkered tiles with wooden rails on the open sides, the cup with a waving flag, the windmill
    /// (blades turning on the engine's angle), the bump dome, the ramp and the tunnel arch, set in a lawn with trees,
    /// hedges, a pond and pennant poles. The ball rolls and hops on the engine's height, the aim shows as a row of dots.
    /// Gooby stands at the tee with his putter and swings it on every putt, cheers in the cup, groans on a give-up. The
    /// camera frames each hole from above at a slant (forward = up on screen), so the drag-back aiming reads the same.
    /// </summary>
    public sealed partial class MiniGolfScreen
    {
        private World.MgStage3D _stage;
        private Transform _course3d, _ball3d, _flag3d, _blades3d, _putter;
        private readonly List<Transform> _aimDots = new List<Transform>();
        private int _builtHole = -1;
        private float _swingT, _emoteLeft, _cheerHop;
        private Vector3 _teeSpot;

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 Cell3(double x, double z, float y = 0f) => World.MgStage3D.At((float)x, y, (float)z);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.62f, 0.8f, 0.95f));
            _stage.Bind(Field);
            var s = _stage;
            s.Ground(-0.01f, new Color(0.6f, 0.8f, 0.5f), "grass", 90f, 90f, 10f);
            s.Painting("bg_town_horizon", 0f, 40f, 24f);
            s.Row("tree_round_big", 0f, 30f, -16f, 16f, 7, 1.7f, 0f, 2f, 41);
            s.Row("hedge_long", 0f, 24f, -12f, 12f, 4, 1f, 0f, 0f, 42);
            s.Row("tree_round", 0f, -6f, -12f, 12f, 4, 1.4f, 0f, 2f, 43);
            s.Meadow(0f, -10f, 10f, -8f, 22f, 70, 26, 44);
            _ball3d = World.ArtLibrary.Sphere(s.Root.transform, "Ball", Vector3.zero, 0.11f, World.ArtLibrary.Solid(new Color(1f, 0.99f, 0.96f), 0.7f)).transform;
            var dot = World.ArtLibrary.Glow(new Color(1.2f, 1.2f, 1.2f));
            for (var i = 0; i < 10; i++)
            {
                var d = World.ArtLibrary.Sphere(s.Root.transform, "Aim", Vector3.zero, 0.045f - i * 0.0025f, dot, false).transform;
                d.gameObject.SetActive(false);
                _aimDots.Add(d);
            }
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnGolfer(outfit);
        }

        private async System.Threading.Tasks.Task SpawnGolfer(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(0f, 0f, 0f), 0.62f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            _putter = new GameObject("Putter").transform;
            _putter.SetParent(g.transform, false);
            _putter.localPosition = new Vector3(0.32f, 0.5f, 0.22f);
            var shaft = World.ArtLibrary.Cylinder(_putter, "Shaft", Vector3.zero, 0.02f, 0.62f, World.ArtLibrary.Solid(new Color(0.75f, 0.76f, 0.8f), 0.8f));
            shaft.transform.SetParent(_putter, false);
            shaft.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            shaft.transform.localScale = new Vector3(0.04f, 0.31f, 0.04f);
            var head = World.ArtLibrary.Box(_putter, "Head", Vector3.zero, Vector3.one, World.ArtLibrary.Solid(new Color(0.95f, 0.45f, 0.5f), 0.5f));
            head.transform.SetParent(_putter, false);
            head.transform.localPosition = new Vector3(0f, -0.62f, 0.04f);
            head.transform.localScale = new Vector3(0.18f, 0.06f, 0.07f);
        }

        /// <summary>Builds the current hole: tiles, rails, cup + flag and its features; frames the camera on it.</summary>
        private void BuildHole(MiniGolf.Hole hole)
        {
            if (_course3d != null) Object.Destroy(_course3d.gameObject);
            _course3d = new GameObject("Hole").transform;
            _course3d.SetParent(_stage.Root.transform, false);
            var feltA = World.ArtLibrary.Solid(new Color(0.5f, 0.84f, 0.5f), 0.12f);
            var feltB = World.ArtLibrary.Solid(new Color(0.45f, 0.78f, 0.46f), 0.12f);
            var rail = World.ArtLibrary.Solid(new Color(0.94f, 0.8f, 0.6f), 0.3f);
            var railTop = World.ArtLibrary.Solid(new Color(1f, 0.62f, 0.68f), 0.3f);
            float minX = 1e9f, maxX = -1e9f, minZ = 1e9f, maxZ = -1e9f;
            foreach (var c in hole.Cells)
            {
                World.ArtLibrary.Box(_course3d, "Tile", Cell3(c.x, c.z, 0.04f), new Vector3(1f, 0.08f, 1f), ((c.x + c.z) & 1) == 0 ? feltA : feltB, false);
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x); minZ = Mathf.Min(minZ, c.z); maxZ = Mathf.Max(maxZ, c.z);
                void Rail(Vector3 at, Vector3 size)
                {
                    World.ArtLibrary.Box(_course3d, "Rail", at, size, rail);
                    World.ArtLibrary.Box(_course3d, "RailTop", at + Vector3.up * (size.y * 0.5f + 0.012f), new Vector3(size.x + 0.01f, 0.025f, size.z + 0.01f), railTop, false);
                }
                if (!hole.CellSet.Contains((c.x - 1, c.z))) Rail(Cell3(c.x - 0.5, c.z, 0.13f), new Vector3(0.1f, 0.18f, 1.06f));
                if (!hole.CellSet.Contains((c.x + 1, c.z))) Rail(Cell3(c.x + 0.5, c.z, 0.13f), new Vector3(0.1f, 0.18f, 1.06f));
                if (!hole.CellSet.Contains((c.x, c.z - 1))) Rail(Cell3(c.x, c.z - 0.5, 0.13f), new Vector3(1.06f, 0.18f, 0.1f));
                if (!hole.CellSet.Contains((c.x, c.z + 1))) Rail(Cell3(c.x, c.z + 0.5, 0.13f), new Vector3(1.06f, 0.18f, 0.1f));
            }
            // the cup and its flag
            World.ArtLibrary.Cylinder(_course3d, "Cup", Cell3(hole.HoleAt.x, hole.HoleAt.z, 0.085f), 0.14f, 0.012f, World.ArtLibrary.Solid(new Color(0.12f, 0.15f, 0.13f), 0.1f), false);
            World.ArtLibrary.Cylinder(_course3d, "Pole", Cell3(hole.HoleAt.x, hole.HoleAt.z, 0.6f), 0.015f, 1.05f, World.ArtLibrary.Solid(Color.white, 0.5f));
            _flag3d = World.ArtLibrary.Box(_course3d, "Flag", Cell3(hole.HoleAt.x + 0.17, hole.HoleAt.z, 1.0f), new Vector3(0.32f, 0.22f, 0.015f), World.ArtLibrary.Solid(new Color(0.94f, 0.3f, 0.34f), 0.3f), false).transform;
            if (hole.HasBump)
            {
                var bump = World.ArtLibrary.Sphere(_course3d, "Bump", Cell3(hole.BumpX, hole.BumpZ, 0.08f), 0.5f, World.ArtLibrary.Solid(new Color(0.42f, 0.72f, 0.42f), 0.1f));
                bump.transform.localScale = new Vector3(0.5f, 0.2f, 0.42f);
            }
            if (hole.HasRamp)
            {
                var ramp = World.ArtLibrary.Box(_course3d, "Ramp", Cell3(hole.RampCell.x, hole.RampCell.z, 0.08f + (float)hole.RampH * 0.5f), new Vector3(0.8f, 0.06f, 1f), World.ArtLibrary.Solid(new Color(0.48f, 0.78f, 0.5f), 0.1f));
                ramp.transform.rotation = Quaternion.Euler(hole.RampDir.z != 0 ? -14f * hole.RampDir.z : 0f, 0f, hole.RampDir.x != 0 ? 14f * hole.RampDir.x : 0f);
            }
            if (hole.HasTunnel)
            {
                var stone = World.ArtLibrary.Solid(new Color(0.72f, 0.66f, 0.6f), 0.15f);
                World.ArtLibrary.Box(_course3d, "TunnelL", Cell3(hole.TunnelCell.x - 0.42, hole.TunnelCell.z, 0.3f), new Vector3(0.14f, 0.5f, 0.6f), stone);
                World.ArtLibrary.Box(_course3d, "TunnelR", Cell3(hole.TunnelCell.x + 0.42, hole.TunnelCell.z, 0.3f), new Vector3(0.14f, 0.5f, 0.6f), stone);
                World.ArtLibrary.Box(_course3d, "TunnelTop", Cell3(hole.TunnelCell.x, hole.TunnelCell.z, 0.58f), new Vector3(1f, 0.14f, 0.62f), World.ArtLibrary.Solid(new Color(1f, 0.62f, 0.68f), 0.2f));
            }
            _blades3d = null;
            if (hole.HasWindmill)
            {
                var wood = World.ArtLibrary.Solid(new Color(0.98f, 0.94f, 0.88f), 0.2f);
                var mx = hole.MillCellX; var mz = hole.MillGateZ + 0.35;
                World.ArtLibrary.Box(_course3d, "MillL", Cell3(mx - 0.4, mz + 0.2, 0.45f), new Vector3(0.2f, 0.8f, 0.5f), wood);
                World.ArtLibrary.Box(_course3d, "MillR", Cell3(mx + 0.4, mz + 0.2, 0.45f), new Vector3(0.2f, 0.8f, 0.5f), wood);
                World.ArtLibrary.Box(_course3d, "MillBody", Cell3(mx, mz + 0.2, 1.0f), new Vector3(1f, 0.6f, 0.5f), wood);
                var roof = World.ArtLibrary.Box(_course3d, "MillRoof", Cell3(mx, mz + 0.2, 1.38f), new Vector3(0.55f, 0.55f, 0.56f), World.ArtLibrary.Solid(new Color(0.95f, 0.45f, 0.48f), 0.2f));
                roof.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                _blades3d = new GameObject("Blades").transform;
                _blades3d.SetParent(_course3d, false);
                _blades3d.position = Cell3(mx, mz - 0.1, 1.05f);
                var sail = World.ArtLibrary.Solid(new Color(0.85f, 0.62f, 0.42f), 0.2f);
                for (var i = 0; i < 4; i++)
                {
                    var b = World.ArtLibrary.Box(_blades3d, "Blade", _blades3d.position, new Vector3(0.14f, 0.62f, 0.03f), sail);
                    b.transform.SetParent(_blades3d, true);
                    b.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                    b.transform.localPosition = b.transform.localRotation * new Vector3(0f, 0.33f, 0f);
                }
            }
            // frame the hole: the camera looks up the course (+z) from behind the tee at a slant
            var cx = (minX + maxX) * 0.5f; var cz = (minZ + maxZ) * 0.5f;
            var spanX = maxX - minX + 2.2f; var spanZ = maxZ - minZ + 2.4f;
            var reach = Mathf.Max(spanZ * 1.1f, spanX * 2.15f);
            const float pitch = 56f;
            var back = new Vector3(0f, Mathf.Sin(pitch * Mathf.Deg2Rad), -Mathf.Cos(pitch * Mathf.Deg2Rad)) * reach;
            _stage.Aim(new Vector3(cx, 0f, cz - 0.4f) + back, pitch, 48f);
            var tee = hole.Start;
            _teeSpot = Cell3(tee.x + 0.62, tee.z - 0.2);
        }

        private void RenderStage()
        {
            var g = _game;
            if (_builtHole != g.HoleIndex) { _builtHole = g.HoleIndex; BuildHole(g.Current); }
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var hole = g.Current;
            var by = (float)MiniGolf.HeightAt(hole, g.Ball.X, g.Ball.Z);
            var sink = g.Phase == "pause" && g.SinkT > 0.0 ? 1f - (float)(g.SinkT / MiniGolfGame.SinkSec) : 0f;
            _ball3d.gameObject.SetActive(sink < 0.95f);
            _ball3d.position = Cell3(g.Ball.X, g.Ball.Z, 0.19f + by - sink * 0.2f);
            if (_blades3d != null) _blades3d.localRotation = Quaternion.Euler(0f, 0f, -(float)(g.Theta + hole.MillPhase) * Mathf.Rad2Deg);
            if (_flag3d != null) _flag3d.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 2.4f) * 14f, 0f);
            // the aim: dots from the ball in the pull direction (drag back → the ball goes the other way)
            var aiming = g.Dragging && g.Phase == "aim";
            var dx = g.DragFromX - g.DragToX; var dz = -(g.DragFromY - g.DragToY);
            var d = System.Math.Sqrt(dx * dx + dz * dz);
            for (var i = 0; i < _aimDots.Count; i++)
            {
                var on = aiming && d > 4;
                _aimDots[i].gameObject.SetActive(on);
                if (!on) continue;
                var reach = (float)g.AimPower * 0.22f * (i + 1) / _aimDots.Count;
                _aimDots[i].position = Cell3(g.Ball.X + dx / d * reach * 4.5, g.Ball.Z + dz / d * reach * 4.5, 0.2f);
            }
            var gb = _stage.Gooby;
            if (gb == null) return;
            _swingT = Mathf.Max(0f, _swingT - dt * 2.8f);
            _cheerHop = Mathf.Max(0f, _cheerHop - dt * 2.2f);
            var toBall = new Vector3((float)g.Ball.X, 0f, (float)g.Ball.Z) - new Vector3(_teeSpot.x - World.MgStage3D.Origin.x, 0f, _teeSpot.z - World.MgStage3D.Origin.z);
            var yaw = toBall.sqrMagnitude > 0.01f ? Mathf.Atan2(toBall.x, toBall.z) * Mathf.Rad2Deg : 0f;
            gb.transform.position = _teeSpot + Vector3.up * (0.08f + Mathf.Sin(_cheerHop * Mathf.PI) * 0.3f);
            gb.transform.rotation = Quaternion.Euler(0f, yaw - 60f, 0f);
            if (_putter != null)
            {
                var k = _swingT > 0.6f ? (1f - _swingT) / 0.4f : _swingT / 0.6f;
                _putter.localRotation = Quaternion.Euler(-60f * k + (aiming ? 25f * (float)g.AimPower : 0f), 0f, 0f);
            }
            if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) gb.SetEmotion("happy");
        }

        private void StageEvent(string what)
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            switch (what)
            {
                case "putt": _swingT = 1f; break;
                case "holed":
                    gb.SetEmotion("ecstatic"); gb.Play("celebrate"); _cheerHop = 1f;
                    if (!ReducedMotion) World.Effects.Sparkle(Cell3(_game.Current.HoleAt.x, _game.Current.HoleAt.z, 0.3f), 0.5f, 26);
                    break;
                case "ace":
                    gb.SetEmotion("ecstatic"); gb.Play("dance-loop"); _cheerHop = 1f;
                    if (!ReducedMotion) World.Effects.Sparkle(Cell3(_game.Current.HoleAt.x, _game.Current.HoleAt.z, 0.3f), 0.8f, 40);
                    break;
                case "bonk": gb.SetEmotion("scared"); break;
                default: gb.SetEmotion("sad"); break;
            }
            _emoteLeft = what == "putt" ? 0f : 1.4f;
        }

        private void DropStage()
        {
            _aimDots.Clear();
            _course3d = null; _ball3d = null; _flag3d = null; _blades3d = null; _putter = null;
            _builtHole = -1;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
