using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D show stage of "Gooby sagt" (port of gooby_says_stage3d.gd): four chunky pastel pads with their symbols lie on
    /// the plank floor of a little theatre — raspberry curtains, a gold valance with bunting, spot cones and footlights — and
    /// the player's Gooby conducts from a round podium behind them. While he plays the sequence he turns to the pad, waves,
    /// and a ring in the pad's colour glows around his feet; a lit pad pops up and glows, a mistake flashes every pad red,
    /// a won round sends confetti. The camera looks down at the stage like the Godot set; every pad is built on the floor
    /// exactly under its 2D tap square (MgStage3D.GroundPoint of the square's corners), so taps keep landing where they did.
    /// </summary>
    public sealed partial class GoobySaysScreen
    {
        private World.MgStage3D _stage;
        private readonly List<Transform> _pads3d = new List<Transform>();
        private readonly List<Material> _padMats = new List<Material>();
        private readonly List<Transform> _rims = new List<Transform>();
        private readonly List<Material> _ownedMats = new List<Material>();
        private readonly float[] _padPop = new float[4];
        private Transform _halo, _podium;
        private Material _haloMat, _rimMat;
        private float _failLeft, _goobyYaw;
        private int _lastLit = -1;

        private const float PadH = 0.16f;

        protected override bool StageHasGooby => _stage != null;

        private Vector2 ViewOf(Vector2 field) => new Vector2(field.x * 390f / Mathf.Max(1f, FieldW), field.y * 844f / Mathf.Max(1f, FieldH));

        /// <summary>A pad's 2D tap square in field pixels (the same box the pad element covers).</summary>
        private Rect PadRect(int i)
        {
            var dx = (i % 2 == 0 ? -1f : 1f) * (PadSize + PadGap) * 0.5f - PadSize * 0.5f;
            var dy = (i / 2) * (PadSize + PadGap);
            return new Rect(FieldW * 0.5f + dx, FieldH * 0.56f + dy, PadSize, PadSize);
        }

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(390.0, 844.0, World.ArtLibrary.Hex("#3E3270"));
            _stage.Bind(Field);
            var camPos = new Vector3(0f, 4.6f, -4.9f);
            var look = new Vector3(0f, 0.35f, 0.3f);
            var dir = look - camPos;
            _stage.Aim(camPos, Mathf.Atan2(-dir.y, dir.z) * Mathf.Rad2Deg, 62f);
            var root = _stage.Root.transform;
            var o = World.MgStage3D.Origin;
            // the stage floor, its lip and the footlights
            World.ArtLibrary.Box(root, "Boards", World.MgStage3D.At(0f, -0.1f, 2.5f), new Vector3(12f, 0.2f, 12f),
                World.ArtLibrary.PbrSlab("wood_planks", new Vector2(12f, 12f), 1.6f, World.ArtLibrary.Hex("#D19C6E"), 0.3f), false);
            var lipZ = _stage.GroundPoint(195, 844 * 0.985, 0f).z - o.z;
            World.ArtLibrary.Box(root, "Lip", World.MgStage3D.At(0f, -0.05f, lipZ), new Vector3(12f, 0.22f, 0.3f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6E4C8"), 0.3f), false);
            for (var i = 0; i < 9; i++)
            {
                var x = -3.2f + i * 0.8f;
                World.ArtLibrary.Sphere(root, "Footlight", World.MgStage3D.At(x, 0.08f, lipZ - 0.05f), 0.07f, World.ArtLibrary.Glow(new Color(1.4f, 1.15f, 0.6f)), false);
            }
            // the theatre behind: violet back wall, raspberry curtains tied back, the gold valance with bunting
            World.ArtLibrary.Box(root, "BackWall", World.MgStage3D.At(0f, 4f, 7.4f), new Vector3(14f, 9f, 0.2f), World.ArtLibrary.Pbr("plaster", 2.5f, World.ArtLibrary.Hex("#5A4A8C"), 0.15f), false);
            var velvet = World.ArtLibrary.Pbr("fabric", 0.7f, World.ArtLibrary.Hex("#E8718F"), 0.25f);
            var velvetLight = World.ArtLibrary.Pbr("fabric", 0.7f, World.ArtLibrary.Hex("#F590A8"), 0.25f);
            foreach (var side in new[] { -1f, 1f })
            {
                for (var f = 0; f < 5; f++)
                {
                    var x = side * (2.6f + f * 0.32f);
                    var fold = World.ArtLibrary.Cylinder(root, "Fold", World.MgStage3D.At(x, 3.4f, 5.6f + f * 0.05f), 0.2f, 6.8f, f % 2 == 0 ? velvet : velvetLight, false).transform;
                    fold.localScale = new Vector3(0.42f, 3.4f, 0.3f);
                    fold.rotation = Quaternion.Euler(0f, 0f, side * (f == 0 ? 6f : 2f));
                }
                World.ArtLibrary.Box(root, "Tieback", World.MgStage3D.At(side * 2.85f, 1.9f, 5.45f), new Vector3(0.9f, 0.1f, 0.12f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFD166"), 0.5f), false);
                World.ArtLibrary.Sphere(root, "Tassel", World.MgStage3D.At(side * 2.45f, 1.75f, 5.4f), 0.1f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFD166"), 0.5f), false);
                // spot lamps up in the corners and their cones on the podium
                World.ArtLibrary.Cylinder(root, "SpotCan", World.MgStage3D.At(side * 2.2f, 6.4f, 4.6f), 0.18f, 0.3f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#3A2F63"), 0.4f), false);
                World.ArtLibrary.Sphere(root, "SpotLens", World.MgStage3D.At(side * 2.2f, 6.22f, 4.5f), 0.12f, World.ArtLibrary.Glow(new Color(1.4f, 1.25f, 0.85f)), false);
            }
            World.ArtLibrary.Box(root, "Valance", World.MgStage3D.At(0f, 6.9f, 5.3f), new Vector3(8f, 0.5f, 0.12f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6C453"), 0.35f), false);
            for (var i = 0; i < 10; i++)
                World.ArtLibrary.Sphere(root, "Scallop", World.MgStage3D.At(-3.6f + i * 0.8f, 6.65f, 5.28f), 0.4f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6C453"), 0.35f), false).transform.localScale = new Vector3(0.8f, 0.45f, 0.1f);
            World.RanchDressing.Bunting(root, World.MgStage3D.At(-3.4f, 6.3f, 5.1f), World.MgStage3D.At(3.4f, 6.3f, 5.1f), 0.45f, 0.5f);
            for (var i = 0; i < 18; i++)
                World.ArtLibrary.Sphere(root, "Twinkle", World.MgStage3D.At(-3.2f + (i * 37 % 64) / 10f, 3.4f + (i * 23 % 30) / 10f, 7.25f), 0.03f + (i % 3) * 0.015f, World.ArtLibrary.Glow(new Color(1.3f, 1.2f, 0.9f)), false);
            // the podium at the back of the pads with the conductor's halo
            var podiumAt = _stage.GroundPoint(195, 844 * 0.47, 0f);
            _podium = World.ArtLibrary.Cylinder(root, "Podium", podiumAt + Vector3.up * 0.15f, 0.95f, 0.3f, World.ArtLibrary.Pbr("wood_planks", 0.9f, World.ArtLibrary.Hex("#F2C79A"), 0.3f)).transform;
            World.ArtLibrary.Cylinder(root, "PodiumRim", podiumAt + Vector3.up * 0.31f, 0.98f, 0.03f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFF1DC"), 0.4f), false);
            _haloMat = new Material(World.ArtLibrary.Glow(Color.white));
            _ownedMats.Add(_haloMat);
            _halo = World.RanchDressing.Ring(root, "Halo", 0.82f, 0.95f, _haloMat);
            _halo.position = podiumAt + Vector3.up * 0.335f;
            _halo.gameObject.SetActive(false);
            foreach (var side in new[] { -1f, 1f })
            {
                var cone = _stage.Cone(new Vector3(podiumAt.x + side * 2.1f, 6.2f, podiumAt.z + 1.4f), 1.05f, 6.2f, World.ArtLibrary.Transparent(new Color(1f, 0.95f, 0.78f, 0.07f))).transform;
                cone.rotation = Quaternion.Euler(180f + 12f, 0f, -side * 20f);
            }
            BuildPads(root);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnConductor(podiumAt + Vector3.up * 0.32f, outfit);
        }

        private async System.Threading.Tasks.Task SpawnConductor(Vector3 at, Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(at, 1.35f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            g.SetEmotion("happy");
        }

        private void BuildPads(Transform root)
        {
            var colours = PadColors;
            var icons = new[] { "star", "heart", "sun", "leaf" };
            _rimMat = new Material(World.ArtLibrary.Glow(new Color(1f, 0.56f, 0.32f)));
            _ownedMats.Add(_rimMat);
            for (var i = 0; i < 4; i++)
            {
                // the pad's footprint on the floor: where the camera sees the corners of its tap square (a little inset)
                var r = PadRect(i);
                var tl = _stage.GroundPoint(ViewOf(new Vector2(r.xMin + 8f, r.yMin + 8f)).x, ViewOf(new Vector2(r.xMin + 8f, r.yMin + 8f)).y, PadH);
                var br = _stage.GroundPoint(ViewOf(new Vector2(r.xMax - 8f, r.yMax - 8f)).x, ViewOf(new Vector2(r.xMax - 8f, r.yMax - 8f)).y, PadH);
                var centre = _stage.GroundPoint(ViewOf(r.center).x, ViewOf(r.center).y, 0f);
                var w = Mathf.Abs(br.x - tl.x);
                var d = Mathf.Abs(tl.z - br.z);
                var pad = new GameObject("Pad" + i).transform;
                pad.SetParent(root, false);
                pad.position = centre;
                var mat = new Material(World.ArtLibrary.Solid(colours[i], 0.55f));
                _ownedMats.Add(mat);
                _padMats.Add(mat);
                var body = World.ArtLibrary.Box(pad, "Body", centre + Vector3.up * PadH * 0.5f, new Vector3(w, PadH, d), mat).transform;
                var bevel = World.ArtLibrary.Box(pad, "Bevel", centre + Vector3.up * 0.02f, new Vector3(w + 0.1f, 0.04f, d + 0.1f), World.ArtLibrary.Solid(Color.Lerp(colours[i], Color.black, 0.25f), 0.3f), false).transform;
                bevel.SetParent(pad, true);
                var tex = Ux.IconTexture(icons[i]);
                if (tex != null)
                {
                    var glyph = World.ArtLibrary.Quad(body, "Glyph", centre + Vector3.up * (PadH + 0.012f), Quaternion.Euler(90f, 0f, 0f), new Vector2(Mathf.Min(w, d) * 0.62f, Mathf.Min(w, d) * 0.62f), World.ArtLibrary.Cutout(tex, "says-glyph-" + icons[i], new Color(1f, 1f, 1f, 1f))).transform;
                    glyph.SetParent(pad, true);
                }
                var rim = World.ArtLibrary.Box(root, "Rim", centre + Vector3.up * 0.01f, new Vector3(w + 0.28f, 0.03f, d + 0.28f), _rimMat, false).transform;
                rim.gameObject.SetActive(false);
                _rims.Add(rim);
                _pads3d.Add(pad);
            }
        }

        private void RenderStage(bool watching, float urgency)
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            _failLeft = Mathf.Max(0f, _failLeft - dt);
            var failing = _failLeft > 0f;
            for (var i = 0; i < _pads3d.Count; i++)
            {
                var lit = g.LitPad == i || (watching && g.LitLeft > 0.0 && _chordPartner == i) || g.ChordFirst == i;
                var c = failing ? new Color(1f, 0.35f, 0.3f) : lit ? Color.Lerp(PadColors[i], Color.white, 0.45f) * 1.35f : PadColors[i];
                c.a = 1f;
                _padMats[i].color = c;
                if (_padMats[i].HasProperty("_BaseColor")) _padMats[i].SetColor("_BaseColor", c);
                if (lit) _padPop[i] = 1f;
                _padPop[i] = Mathf.Max(0f, _padPop[i] - dt * 4f);
                var pop = 1f + (ReducedMotion ? 0f : _padPop[i] * 0.08f);
                _pads3d[i].localScale = new Vector3(pop, 1f + (lit ? 0.6f : 0f), pop);
            }
            var warn = urgency > 0f && !watching && !failing;
            foreach (var rim in _rims) rim.gameObject.SetActive(warn);
            if (warn)
            {
                var beat = ReducedMotion ? 0.85f : 0.55f + 0.45f * Mathf.Sin(_pulse * 10f);
                var rc = new Color(1f, 0.56f, 0.32f) * ((0.5f + 1.3f * urgency) * beat);
                rc.a = 1f;
                _rimMat.color = rc;
                if (_rimMat.HasProperty("_BaseColor")) _rimMat.SetColor("_BaseColor", rc);
            }
            // the conductor: a halo in the pad's colour while he plays, turning to the pad and waving
            var showing = watching && g.LitPad >= 0 && g.LitLeft > 0.0;
            _halo.gameObject.SetActive(showing);
            var gb = _stage.Gooby;
            if (showing)
            {
                var hc = PadColors[g.LitPad] * 1.4f;
                hc.a = 1f;
                _haloMat.color = hc;
                if (_haloMat.HasProperty("_BaseColor")) _haloMat.SetColor("_BaseColor", hc);
                _halo.rotation = Quaternion.Euler(0f, _pulse * 90f, 0f);
                if (g.LitPad != _lastLit && gb != null)
                {
                    gb.Play("wave", 0.1f);
                    var to = _pads3d[g.LitPad].position - gb.transform.position;
                    _goobyYaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 180f, -40f, 40f);
                }
            }
            else if (!watching) _goobyYaw = Mathf.MoveTowards(_goobyYaw, 0f, dt * 90f);
            _lastLit = showing ? g.LitPad : -1;
            if (gb != null)
            {
                var sway = ReducedMotion ? 0f : Mathf.Sin(_pulse * 5f) * (watching ? 3.5f : 1.2f);
                gb.transform.rotation = Quaternion.Euler(0f, 180f + _goobyYaw, sway);
            }
        }

        private void StageRoundWon()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("celebrate", 0.1f);
            gb.SetEmotion("ecstatic");
            if (ReducedMotion) return;
            var colours = PadColors;
            for (var i = 0; i < 4; i++)
                World.Effects.Puff(gb.transform.position + Vector3.up * 2f, colours[i], 8, 0.12f, 1.8f, 1.4f, Vector3.up, 1f);
        }

        private void StageFailed()
        {
            _failLeft = 0.6f;
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("dizzy");
            gb.Play("refuse", 0.1f);
        }

        private void StageTapped(int pad)
        {
            if (_stage == null || pad < 0 || pad >= _pads3d.Count) return;
            _padPop[pad] = 1f;
            if (!ReducedMotion) World.Effects.Sparkle(_pads3d[pad].position + Vector3.up * 0.25f, 0.4f, 10);
        }

        private void DropStage()
        {
            _pads3d.Clear(); _padMats.Clear(); _rims.Clear();
            _stage?.Destroy();
            _stage = null;
            foreach (var m in _ownedMats) if (m != null) Object.Destroy(m);
            _ownedMats.Clear();
            _lastLit = -1;
            _failLeft = _goobyYaw = 0f;
        }
    }
}
