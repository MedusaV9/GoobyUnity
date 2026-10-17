using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D moon of the Raketen-Rettung (port of rocket_rescue_stage.gd): a pastel lander with the player's Gooby under its
    /// glass dome hovers over a cratered lavender moon — legs, a flickering flame while it thrusts, a real tilt; five floating
    /// moon rocks carry stranded Gooby friends who wave until he picks them up, glowing fuel cans hang in the sky, the
    /// station's dome and landing pad with its beacon wait on the ground; stars, a ringed planet and a nebula behind. The
    /// default front camera frames the engine plane as the 2D field did (the world slides with the game's camera x), so
    /// floats and the zone bar stay where they were.
    /// </summary>
    public sealed partial class RocketRescueScreen
    {
        private World.MgStage3D _stage;
        private Transform _world, _lander, _flame, _carried, _beacon;
        private readonly List<Transform> _rocks3d = new List<Transform>();
        private readonly List<World.GoobyAvatar> _castaways = new List<World.GoobyAvatar>();
        private readonly List<Transform> _cans3d = new List<Transform>();
        private float _u = 0.5f, _groundView;

        private static readonly string[] CastawayFurs = { "fell_minze", "fell_zuckerwatte", "fell_himmel", "fell_lavendel", "snow" };

        protected override bool StageHasGooby => _stage != null;

        /// <summary>Engine point (camera-relative x, height over the ground) → stage world on the engine plane.</summary>
        private Vector3 R(double wx, double wy, float depth = 0f)
        {
            var k = (float)(ViewH / Mathf.Max(1f, FieldH));
            return _stage.ViewToWorld(ViewW * 0.5 + (wx - _game.CamX) * _scale * k, _groundView - wy * _scale * k, depth);
        }

        private void BuildStage()
        {
            EnsureFieldSize();
            _scale = (FieldH * (float)RocketRescueGame.WorldHFrac) / ((float)_game.Tune.CeilingY + 1.4f);
            _groundView = (float)(ViewH * RocketRescueGame.GroundFrac);
            _u = _scale * (float)(ViewH / FieldH) * World.MgStage3D.MetresPerPx;
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#22184A"));
            _stage.Bind(Field);
            World.MgStudio.Night();
            var root = _stage.Root.transform;
            _world = new GameObject("MoonWorld").transform;
            _world.SetParent(root, false);
            var groundY = R(0, 0).y;
            var halfW = (float)_game.Tune.WorldHalfW;
            // the moon: lavender regolith with craters and rocks, spread over the whole flyable width and beyond
            World.ArtLibrary.Box(_world, "Regolith", new Vector3(World.MgStage3D.Origin.x, groundY - 0.6f, World.MgStage3D.Origin.z + 3f), new Vector3(halfW * _u * 6f, 1.2f, 14f), World.ArtLibrary.PbrSlab("concrete", new Vector2(halfW * _u * 6f, 14f), 1.5f, World.ArtLibrary.Hex("#BDB3D8"), 0.08f, 1.6f), false);
            var rim = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#A89CC8"), 0.1f);
            var rng = new System.Random(5);
            for (var i = 0; i < 16; i++)
            {
                var x = (float)(rng.NextDouble() * 2.0 - 1.0) * halfW * 1.6f;
                var crater = _stage.Torus(_world, "Crater", Vector3.zero, (0.6f + (float)rng.NextDouble()) * _u * 1.4f, 0.12f * _u * 2f, rim).transform;
                crater.position = R(x + _game.CamX, 0, 0.6f + (float)rng.NextDouble() * 4f);
                crater.localScale = new Vector3(crater.localScale.x, crater.localScale.y * 0.4f, crater.localScale.z);
                if (i % 3 == 0) _stage.Prop(i % 2 == 0 ? "rock_largeA" : "meteor_half", R(x + 1.2 + _game.CamX, 0, 1.5f + i % 4), _u * 2.2f, i * 40f).SetParent(_world, true);
            }
            // the station: a dome, a tube, an antenna and the landing pad with its beacon
            var pad = R(_game.Engine.Layout.PadX + _game.CamX, _game.Engine.Layout.PadY);
            var padHalf = (float)_game.Engine.Layout.PadHalfW * _u;
            World.ArtLibrary.Cylinder(_world, "Pad", pad + Vector3.up * 0.04f, padHalf, 0.08f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#E8F0FF"), 0.5f));
            World.ArtLibrary.Cylinder(_world, "PadRing", pad + Vector3.up * 0.09f, padHalf * 0.75f, 0.02f, World.ArtLibrary.Glow(new Color(1.2f, 1.05f, 0.5f)), false);
            var dome = World.ArtLibrary.Sphere(_world, "Dome", pad + new Vector3(padHalf + 1.2f * _u, 0f, 1.2f), 1.2f * _u, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6F0FF"), 0.6f)).transform;
            dome.localScale = new Vector3(dome.localScale.x, dome.localScale.y * 0.8f, dome.localScale.z);
            World.ArtLibrary.Cylinder(_world, "Antenna", pad + new Vector3(padHalf + 1.6f * _u, 1.6f * _u, 1.4f), 0.03f * _u * 2f, 1.6f * _u, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#9AA6BC"), 0.6f), false);
            _beacon = World.ArtLibrary.Sphere(_world, "Beacon", pad + new Vector3(padHalf + 1.6f * _u, 2.45f * _u, 1.4f), 0.16f * _u * 2f, World.ArtLibrary.Glow(new Color(1.5f, 0.6f, 0.6f)), false).transform;
            // floating moon rocks with a stranded Gooby each, fuel cans
            for (var i = 0; i < _game.Engine.Layout.Platforms.Count; i++)
            {
                var plat = _game.Engine.Layout.Platforms[i];
                var rock = new GameObject("Rock").transform;
                rock.SetParent(_world, false);
                rock.position = R(plat.X + _game.CamX, plat.Y);
                var slab = World.ArtLibrary.Cylinder(rock, "Top", rock.position + Vector3.down * 0.05f * _u, (float)plat.HalfW * _u, 0.12f * _u, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#C9B79A"), 0.15f)).transform;
                var under = World.ArtLibrary.Sphere(rock, "Under", rock.position + Vector3.down * 0.45f * _u, (float)plat.HalfW * _u, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#9C8A72"), 0.1f)).transform;
                under.localScale = new Vector3(under.localScale.x, under.localScale.y * 0.55f, under.localScale.z * 0.8f);
                slab.name = "Top";
                _rocks3d.Add(rock);
                _ = SpawnCastaway(rock, i);
            }
            for (var i = 0; i < _game.Engine.Layout.FuelPickups.Count; i++)
            {
                var can = new GameObject("FuelCan").transform;
                can.SetParent(_world, false);
                World.ArtLibrary.Cylinder(can, "Body", Vector3.zero, 0.22f * _u, 0.55f * _u, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#7CCB86"), 0.5f)).transform.localPosition = Vector3.zero;
                World.ArtLibrary.Cylinder(can, "Band", Vector3.zero, 0.23f * _u, 0.1f * _u, World.ArtLibrary.Solid(Color.white, 0.5f), false).transform.localPosition = Vector3.zero;
                World.ArtLibrary.Sphere(can, "Glow", Vector3.zero, 0.45f * _u, World.ArtLibrary.Transparent(new Color(0.5f, 1f, 0.6f, 0.18f)), false).transform.localPosition = Vector3.zero;
                _cans3d.Add(can);
            }
            // space: stars, nebulae and the ringed planet (fixed to the screen, only a slow drift)
            var sky = new GameObject("Sky").transform;
            sky.SetParent(root, false);
            var starMat = World.ArtLibrary.Glow(new Color(1.3f, 1.25f, 1.15f));
            for (var i = 0; i < 70; i++)
                World.ArtLibrary.Sphere(sky, "Star", World.MgStage3D.At((float)(rng.NextDouble() - 0.5) * 30f, groundY + (float)rng.NextDouble() * 22f - World.MgStage3D.Origin.y, 30f + (float)rng.NextDouble() * 8f), 0.04f + (float)rng.NextDouble() * 0.07f, starMat, false);
            var neb = World.ArtLibrary.Sphere(sky, "Nebula", World.MgStage3D.At(-4f, groundY + 10f - World.MgStage3D.Origin.y, 34f), 1f, World.ArtLibrary.Transparent(new Color(1f, 0.5f, 0.85f, 0.12f)), false).transform;
            neb.localScale = new Vector3(18f, 8f, 2f);
            var planet = World.ArtLibrary.Sphere(sky, "Planet", World.MgStage3D.At(3.6f, groundY + 9.5f - World.MgStage3D.Origin.y, 24f), 1.7f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFB48A"), 0.3f), false).transform;
            var ring = _stage.Torus(sky, "PlanetRing", planet.position, 2.8f, 0.08f, World.ArtLibrary.Glow(new Color(1.2f, 1.05f, 0.75f))).transform;
            ring.rotation = Quaternion.Euler(-16f, 0f, -18f);
            BuildLander(root);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnPilot(outfit);
        }

        private void BuildLander(Transform root)
        {
            _lander = new GameObject("Lander").transform;
            _lander.SetParent(root, false);
            var shell = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF9BC1"), 0.55f);
            var trim = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6F0FF"), 0.6f);
            var body = World.ArtLibrary.Sphere(_lander, "Body", Vector3.zero, 0.5f, shell).transform;
            body.localPosition = new Vector3(0f, 0.55f, 0f) * _u;
            body.localScale = new Vector3(0.95f, 0.75f, 0.95f) * _u;
            var band = World.ArtLibrary.Cylinder(_lander, "Band", Vector3.zero, 0.5f, 0.12f, trim, false).transform;
            band.localPosition = new Vector3(0f, 0.5f, 0f) * _u;
            band.localScale = new Vector3(0.98f, 0.06f, 0.98f) * _u;
            var glass = World.ArtLibrary.Sphere(_lander, "Dome", Vector3.zero, 0.5f, World.ArtLibrary.Transparent(new Color(0.75f, 0.9f, 1f, 0.32f)), false).transform;
            glass.localPosition = new Vector3(0f, 0.95f, 0f) * _u;
            glass.localScale = new Vector3(0.62f, 0.62f, 0.62f) * _u;
            for (var i = 0; i < 3; i++)
            {
                var a = i * 120f + 90f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.right;
                var leg = World.ArtLibrary.Cylinder(_lander, "Leg", Vector3.zero, 0.5f, 1f, trim, false).transform;
                leg.localPosition = (dir * 0.42f + Vector3.up * 0.18f) * _u;
                leg.localRotation = Quaternion.FromToRotation(Vector3.up, (dir * 0.35f + Vector3.up).normalized);
                leg.localScale = new Vector3(0.05f, 0.22f, 0.05f) * _u;
                var foot = World.ArtLibrary.Cylinder(_lander, "Foot", Vector3.zero, 0.5f, 1f, trim, false).transform;
                foot.localPosition = dir * 0.55f * _u;
                foot.localScale = new Vector3(0.18f, 0.02f, 0.18f) * _u;
            }
            _flame = new GameObject("Flame").transform;
            _flame.SetParent(_lander, false);
            var outer = World.ArtLibrary.Sphere(_flame, "Outer", Vector3.zero, 0.5f, World.ArtLibrary.Glow(new Color(1.6f, 0.8f, 0.35f)), false).transform;
            outer.localScale = new Vector3(0.22f, 0.5f, 0.22f) * _u;
            var inner = World.ArtLibrary.Sphere(_flame, "Inner", Vector3.zero, 0.5f, World.ArtLibrary.Glow(new Color(1.8f, 1.6f, 0.8f)), false).transform;
            inner.localScale = new Vector3(0.12f, 0.3f, 0.12f) * _u;
            _flame.localPosition = new Vector3(0f, 0.05f, 0f) * _u;
        }

        private async System.Threading.Tasks.Task SpawnPilot(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(_lander.position, 0.55f * _u, outfit);
            if (g == null || _stage == null) return;
            g.transform.SetParent(_lander, true);
            g.transform.localPosition = new Vector3(0f, 0.72f, 0f) * _u;
            g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
        }

        private async System.Threading.Tasks.Task SpawnCastaway(Transform rock, int i)
        {
            var g = await World.GoobyAvatar.CreateAsync(rock, rock.position);
            if (g == null) return;
            if (_stage == null || rock == null) { Object.Destroy(g.gameObject); return; }
            g.transform.position = rock.position + Vector3.up * 0.02f * _u;
            g.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            g.transform.localScale = Vector3.one * 0.5f * _u;
            g.Wear(new Dictionary<string, string> { ["fell"] = CastawayFurs[i % CastawayFurs.Length] });
            g.Play("wave", 0f);
            g.SetEmotion("scared");
            World.RanchDressing.QuietShadows(g.transform);
            while (_castaways.Count <= i) _castaways.Add(null);
            _castaways[i] = g;
        }

        private void RenderStage()
        {
            var g = _game;
            var e = g.Engine;
            // the world slides with the game's camera
            _world.position = new Vector3(-(float)g.CamX * _scale * (float)(ViewH / Mathf.Max(1f, FieldH)) * World.MgStage3D.MetresPerPx, 0f, 0f);
            for (var i = 0; i < _rocks3d.Count && i < e.Layout.Platforms.Count; i++)
            {
                var plat = e.Layout.Platforms[i];
                var bob = ReducedMotion ? 0f : Mathf.Sin(_show * 1.4f + i) * 0.06f * _u;
                _rocks3d[i].localPosition = new Vector3(_rocks3d[i].localPosition.x, R(plat.X, plat.Y).y + bob - _world.position.y, _rocks3d[i].localPosition.z);
                var home = plat.Bunny && !(e.Carrying && e.LastLandedOn == i && e.LandedOn == i);
                if (i < _castaways.Count && _castaways[i] != null) _castaways[i].gameObject.SetActive(home);
            }
            for (var i = 0; i < _cans3d.Count && i < e.Layout.FuelPickups.Count; i++)
            {
                var f = e.Layout.FuelPickups[i];
                _cans3d[i].gameObject.SetActive(!f.Taken);
                if (f.Taken) continue;
                _cans3d[i].position = R(f.X, f.Y + Mathf.Sin(_show * 3f + i) * 0.06f, -0.05f);
                _cans3d[i].rotation = Quaternion.Euler(0f, _show * 60f, 12f);
            }
            if (_beacon != null) _beacon.localScale = Vector3.one * 0.32f * _u * (1f + (float)g.BeaconLeft * 1.6f + Mathf.Sin(_show * 4f) * 0.08f);
            // the lander tilts, squashes on touchdown and flames while it thrusts
            var squash = 1f - 0.18f * (float)g.Squash01;
            _lander.position = R(e.X, e.Y, -0.1f);
            _lander.rotation = Quaternion.Euler(0f, 0f, -(float)(e.Tilt * Mathf.Rad2Deg));
            _lander.localScale = new Vector3(1f / squash, squash, 1f / squash);
            var thrusting = g.Thrust && e.Fuel > 0.0 && !e.Towing;
            _flame.gameObject.SetActive(thrusting);
            if (thrusting)
            {
                var flicker = 0.75f + 0.25f * Mathf.Sin(_show * 28f);
                _flame.localScale = new Vector3(1f, flicker, 1f);
                _flame.localPosition = new Vector3(0f, -0.08f * _u * flicker, 0f);
            }
            // a picked-up friend rides on the lander's side
            if (e.Carrying && _carried == null)
            {
                var idx = e.LastLandedOn ?? -1;
                if (idx >= 0 && idx < _castaways.Count && _castaways[idx] != null)
                {
                    var c = _castaways[idx];
                    c.gameObject.SetActive(true);
                    c.transform.SetParent(_lander, true);
                    c.transform.localPosition = new Vector3(0.42f, 0.55f, -0.1f) * _u;
                    c.Play("wave", 0.1f);
                    c.SetEmotion("ecstatic");
                    _carried = c.transform;
                }
            }
            else if (!e.Carrying && _carried != null)
            {
                _carried.gameObject.SetActive(false);
                _carried = null;
            }
        }

        private void StageMood(string emotion)
        {
            _stage?.Gooby?.SetEmotion(emotion);
        }

        private void StageRescue()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("ecstatic");
            if (!ReducedMotion) World.Effects.Sparkle(_lander.position + Vector3.up * _u, _u, 30);
        }

        private void StageDust()
        {
            if (_stage == null || ReducedMotion) return;
            World.Effects.Puff(_lander.position, new Color(0.85f, 0.8f, 0.92f, 0.9f), 10, 0.2f * _u * 2f, 1.2f * _u, 0.6f, Vector3.up, 1f);
        }

        private void DropStage()
        {
            if (_stage != null) World.MgStudio.Day();
            _rocks3d.Clear(); _castaways.Clear(); _cans3d.Clear();
            _carried = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
