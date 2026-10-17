using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D tea room of the Teestube (port of tea_party_stage3d.gd): Gooby hosts beside a round table with a gingham
    /// cloth, the kettle hovers over a glass cup and tips while the finger holds, the tea rises as a real column, and the
    /// target band wraps the cup as a green glass ring with a golden line at the perfect fill (it glows while the tea
    /// stands in it). A served cup slides off to the left, a spill leaves a puddle. Back wall with window, wallpaper,
    /// bunting, a shelf of treats and a pendant lamp; a kitchen row in the corner. Taps pour anywhere on the field, so the
    /// camera frames the room freely; the 2D fill meter stays as the readout on the right.
    /// </summary>
    public sealed partial class TeaPartyScreen
    {
        private World.MgStage3D _stage;
        private Transform _cup3d, _tea3d, _band3d, _core3d, _kettle3d, _stream3d, _ghost3d, _puddle3d, _ghostTea;
        private Renderer _coreR, _bandR;
        private Material _bandMat, _bandHotMat, _coreMat, _coreHotMat;
        private float _puddleT, _emoteLeft, _goobyHop;
        private const float CupX = 0.3f, TableTop = 1.62f, CupR = 0.44f, GlassH = 0.76f;

        protected override bool StageHasGooby => _stage != null;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewSize.w, ViewSize.h, new Color(0.96f, 0.86f, 0.78f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0.2f, 2.45f, -7.4f), 4f, 42f);
            var s = _stage;
            var root = s.Root.transform;
            // floor boards, the back wall with wallpaper stripes, skirting and a border
            var floor = World.ArtLibrary.PbrSlab("wood_floor", new Vector2(14f, 12f), 1.6f, new Color(0.86f, 0.7f, 0.54f), 0.15f, 0.6f);
            World.ArtLibrary.Box(root, "Floor", World.MgStage3D.At(0f, -0.05f, 2f), new Vector3(14f, 0.1f, 12f), floor, false);
            World.ArtLibrary.Box(root, "Wall", World.MgStage3D.At(0f, 3.5f, 3.2f), new Vector3(14f, 7f, 0.1f), World.ArtLibrary.Solid(new Color(0.99f, 0.9f, 0.84f), 0.05f), false);
            var stripe = World.ArtLibrary.Solid(new Color(0.97f, 0.82f, 0.8f), 0.05f);
            for (var i = 0; i < 12; i++) World.ArtLibrary.Box(root, "Stripe", World.MgStage3D.At(-5.5f + i * 1f, 3.5f, 3.14f), new Vector3(0.32f, 7f, 0.02f), stripe, false);
            World.ArtLibrary.Box(root, "Skirting", World.MgStage3D.At(0f, 0.12f, 3.12f), new Vector3(14f, 0.24f, 0.06f), World.ArtLibrary.Solid(new Color(0.85f, 0.66f, 0.5f), 0.2f), false);
            World.ArtLibrary.Box(root, "Border", World.MgStage3D.At(0f, 2.55f, 3.12f), new Vector3(14f, 0.16f, 0.05f), World.ArtLibrary.Solid(new Color(1f, 0.62f, 0.7f), 0.2f), false);
            BuildWindow(new Vector3(-0.55f, 3.75f, 3.1f));
            // a shelf with treats and the bunting above the table
            World.ArtLibrary.Box(root, "Shelf", World.MgStage3D.At(1.15f, 3.35f, 3.0f), new Vector3(1.3f, 0.07f, 0.34f), World.ArtLibrary.Solid(new Color(0.72f, 0.52f, 0.36f), 0.2f), false);
            s.Prop("cupcake", World.MgStage3D.At(0.75f, 3.39f, 3.0f), 0.55f, 20f);
            s.Prop("macaron_pink", World.MgStage3D.At(1.15f, 3.39f, 3.0f), 1.0f, 0f);
            s.Prop("macaron_blue", World.MgStage3D.At(1.4f, 3.39f, 3.0f), 1.0f, 40f);
            var flagMats = new[] { World.ArtLibrary.Solid(new Color(1f, 0.62f, 0.72f), 0.1f), World.ArtLibrary.Solid(new Color(1f, 0.84f, 0.42f), 0.1f), World.ArtLibrary.Solid(new Color(0.6f, 0.88f, 0.84f), 0.1f), World.ArtLibrary.Solid(new Color(0.72f, 0.82f, 1f), 0.1f) };
            for (var i = 0; i < 11; i++)
            {
                var f = i / 10f;
                var flag = World.ArtLibrary.Box(root, "Flag", World.MgStage3D.At(-2.2f + f * 4.4f, 4.75f - Mathf.Sin(f * Mathf.PI) * 0.45f, 2.6f), new Vector3(0.22f, 0.22f, 0.02f), flagMats[i % 4], false);
                flag.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            }
            // the pendant lamp over the table
            World.ArtLibrary.Cylinder(root, "Cord", World.MgStage3D.At(CupX, 5.2f, 0.6f), 0.012f, 1.6f, World.ArtLibrary.Solid(new Color(0.3f, 0.26f, 0.24f)), false);
            var shade = World.ArtLibrary.Sphere(root, "Shade", World.MgStage3D.At(CupX, 4.35f, 0.6f), 0.3f, World.ArtLibrary.Solid(new Color(1f, 0.93f, 0.8f), 0.3f), false);
            shade.transform.localScale = new Vector3(0.62f, 0.34f, 0.62f);
            World.ArtLibrary.Sphere(root, "Bulb", World.MgStage3D.At(CupX, 4.24f, 0.6f), 0.08f, World.ArtLibrary.Glow(new Color(1.5f, 1.3f, 0.9f)), false);
            // kitchen row in the right corner and a pot plant on the left
            s.Prop("kitchenCabinet", World.MgStage3D.At(1.55f, 0f, 2.65f), 2.4f, 180f);
            s.Prop("kitchenSink", World.MgStage3D.At(2.6f, 0f, 2.65f), 2.4f, 180f);
            s.Prop("dishrack_plates", World.MgStage3D.At(1.5f, 1.08f, 2.6f), 0.55f, 180f);
            s.HomeProp("plant_tall", World.MgStage3D.At(-1.55f, 0f, 2.4f), 1.2f, 30f);
            // the round table with a gingham cloth and a plate of sweets
            var leg = World.ArtLibrary.Solid(new Color(0.78f, 0.58f, 0.42f), 0.25f);
            World.ArtLibrary.Cylinder(root, "TableLeg", World.MgStage3D.At(CupX, TableTop * 0.5f, 0.6f), 0.12f, TableTop, leg);
            World.ArtLibrary.Cylinder(root, "TableFoot", World.MgStage3D.At(CupX, 0.04f, 0.6f), 0.55f, 0.08f, leg);
            World.ArtLibrary.Cylinder(root, "TableTop", World.MgStage3D.At(CupX, TableTop - 0.04f, 0.6f), 1.25f, 0.08f, World.ArtLibrary.Solid(new Color(0.84f, 0.64f, 0.48f), 0.3f));
            World.ArtLibrary.Cylinder(root, "Cloth", World.MgStage3D.At(CupX, TableTop - 0.11f, 0.6f), 1.32f, 0.22f, World.ArtLibrary.Solid(new Color(1f, 0.9f, 0.92f), 0.05f));
            var check = World.ArtLibrary.Solid(new Color(1f, 0.74f, 0.8f), 0.05f);
            for (var i = 0; i < 16; i++)
            {
                var a = i * Mathf.PI * 2f / 16f;
                World.ArtLibrary.Box(root, "Check", World.MgStage3D.At(CupX + Mathf.Sin(a) * 1.325f, TableTop - 0.11f, 0.6f + Mathf.Cos(a) * 1.325f), new Vector3(0.16f, 0.2f, 0.02f), check, false)
                    .transform.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
            }
            World.ArtLibrary.Cylinder(root, "Plate", World.MgStage3D.At(CupX - 0.75f, TableTop + 0.02f, 0.3f), 0.28f, 0.03f, World.ArtLibrary.Solid(new Color(0.98f, 0.98f, 1f), 0.6f));
            s.Prop("cookie", World.MgStage3D.At(CupX - 0.82f, TableTop + 0.04f, 0.28f), 1.2f, 10f);
            s.Prop("macaron_yellow", World.MgStage3D.At(CupX - 0.66f, TableTop + 0.04f, 0.36f), 0.9f, 40f);
            // the glass cup (glass wall, rim, handle), its tea, the band rings
            _cup3d = SpawnCup("Cup", out _tea3d);
            _ghost3d = SpawnCup("Ghost", out _ghostTea);
            _ghost3d.gameObject.SetActive(false);
            _bandMat = World.ArtLibrary.Transparent(new Color(0.36f, 0.82f, 0.46f, 0.32f));
            _bandHotMat = World.ArtLibrary.Transparent(new Color(0.36f, 0.9f, 0.5f, 0.55f));
            _coreMat = World.ArtLibrary.Solid(new Color(1f, 0.74f, 0.2f), 0.6f);
            _coreHotMat = World.ArtLibrary.Glow(new Color(1.5f, 1.15f, 0.4f));
            _band3d = World.ArtLibrary.Cylinder(_cup3d, "Band", _cup3d.position, CupR + 0.035f, 1f, _bandMat, false).transform;
            _bandR = _band3d.GetComponent<Renderer>();
            _core3d = World.ArtLibrary.Cylinder(_cup3d, "Core", _cup3d.position, CupR + 0.05f, 0.025f, _coreMat, false).transform;
            _coreR = _core3d.GetComponent<Renderer>();
            // the kettle and the stream it pours
            _kettle3d = new GameObject("KettlePivot").transform;
            _kettle3d.SetParent(root, false);
            var kettle = s.Prop("kettle", _kettle3d.position, 1.05f, 90f);
            kettle.SetParent(_kettle3d, false);
            kettle.localPosition = new Vector3(0.12f, -0.25f, 0f);
            kettle.localRotation = Quaternion.Euler(0f, 90f, 0f);
            _stream3d = World.ArtLibrary.Cylinder(root, "Stream", Vector3.zero, 0.035f, 1f, World.ArtLibrary.Solid(new Color(0.7f, 0.42f, 0.18f), 0.6f), false).transform;
            _stream3d.gameObject.SetActive(false);
            _puddle3d = World.ArtLibrary.Cylinder(root, "Puddle", World.MgStage3D.At(CupX + 0.1f, TableTop + 0.012f, 0.25f), 0.42f, 0.01f, World.ArtLibrary.Transparent(new Color(0.62f, 0.38f, 0.16f, 0.7f)), false).transform;
            _puddle3d.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = s.SpawnGoobyAsync(World.MgStage3D.At(-0.62f, 0f, 1.75f), 1.5f, outfit);
        }

        private void BuildWindow(Vector3 local)
        {
            var root = _stage.Root.transform;
            World.ArtLibrary.Box(root, "Glass", World.MgStage3D.At(local.x, local.y, local.z), new Vector3(1.3f, 1.1f, 0.02f), World.ArtLibrary.Glow(new Color(0.8f, 0.93f, 1.05f)), false);
            var frame = World.ArtLibrary.Solid(new Color(1f, 0.97f, 0.92f), 0.2f);
            foreach (var (p, sz) in new[] { (new Vector3(0f, 0.58f, -0.03f), new Vector3(1.46f, 0.1f, 0.06f)), (new Vector3(0f, -0.58f, -0.03f), new Vector3(1.46f, 0.1f, 0.06f)),
                         (new Vector3(-0.7f, 0f, -0.03f), new Vector3(0.1f, 1.26f, 0.06f)), (new Vector3(0.7f, 0f, -0.03f), new Vector3(0.1f, 1.26f, 0.06f)),
                         (new Vector3(0f, 0f, -0.03f), new Vector3(0.05f, 1.15f, 0.04f)), (new Vector3(0f, -0.68f, -0.12f), new Vector3(1.6f, 0.07f, 0.24f)) })
                World.ArtLibrary.Box(root, "Frame", World.MgStage3D.At(local.x + p.x, local.y + p.y, local.z + p.z), sz, frame, false);
            var curtain = World.ArtLibrary.Solid(new Color(0.62f, 0.86f, 0.8f), 0.1f);
            World.ArtLibrary.Box(root, "CurtainL", World.MgStage3D.At(local.x - 0.86f, local.y, local.z - 0.1f), new Vector3(0.3f, 1.4f, 0.05f), curtain, false);
            World.ArtLibrary.Box(root, "CurtainR", World.MgStage3D.At(local.x + 0.86f, local.y, local.z - 0.1f), new Vector3(0.3f, 1.4f, 0.05f), curtain, false);
            _stage.Prop("flower_redA", World.MgStage3D.At(local.x - 0.3f, local.y - 0.64f, local.z - 0.12f), 0.5f);
            _stage.Prop("flower_yellowA", World.MgStage3D.At(local.x + 0.3f, local.y - 0.64f, local.z - 0.12f), 0.5f);
        }

        /// <summary>A glass cup on its saucer: clear wall, white rim and base, a handle; the tea column is returned.</summary>
        private Transform SpawnCup(string name, out Transform tea)
        {
            var cup = new GameObject(name).transform;
            cup.SetParent(_stage.Root.transform, false);
            cup.position = World.MgStage3D.At(CupX, TableTop, 0.6f);
            var white = World.ArtLibrary.Solid(new Color(1f, 0.99f, 0.97f), 0.7f);
            void Part(GameObject go, Vector3 local) { go.transform.SetParent(cup, false); go.transform.localPosition = local; }
            Part(World.ArtLibrary.Cylinder(cup, "Saucer", Vector3.zero, CupR + 0.18f, 0.03f, white), new Vector3(0f, 0.015f, 0f));
            Part(World.ArtLibrary.Cylinder(cup, "Base", Vector3.zero, CupR, 0.06f, white), new Vector3(0f, 0.06f, 0f));
            Part(World.ArtLibrary.Cylinder(cup, "Glass", Vector3.zero, CupR, GlassH, World.ArtLibrary.Transparent(new Color(0.86f, 0.95f, 1f, 0.28f)), false), new Vector3(0f, GlassH * 0.5f + 0.03f, 0f));
            Part(World.ArtLibrary.Cylinder(cup, "Rim", Vector3.zero, CupR + 0.012f, 0.04f, white), new Vector3(0f, GlassH + 0.03f, 0f));
            var handle = World.ArtLibrary.Box(cup, "Handle", Vector3.zero, new Vector3(0.07f, 0.32f, 0.07f), white);
            Part(handle, new Vector3(CupR + 0.13f, GlassH * 0.52f, 0f));
            Part(World.ArtLibrary.Box(cup, "HandleTop", Vector3.zero, new Vector3(0.16f, 0.06f, 0.07f), white), new Vector3(CupR + 0.08f, GlassH * 0.52f + 0.15f, 0f));
            Part(World.ArtLibrary.Box(cup, "HandleLow", Vector3.zero, new Vector3(0.16f, 0.06f, 0.07f), white), new Vector3(CupR + 0.08f, GlassH * 0.52f - 0.15f, 0f));
            tea = World.ArtLibrary.Cylinder(cup, "Tea", Vector3.zero, CupR - 0.03f, 1f, World.ArtLibrary.Solid(new Color(0.72f, 0.44f, 0.18f), 0.7f), false).transform;
            tea.SetParent(cup, false);
            tea.gameObject.SetActive(false);
            return cup;
        }

        private static void FillTea(Transform tea, float level)
        {
            var h = Mathf.Clamp01(level) * (GlassH - 0.04f);
            tea.gameObject.SetActive(h > 0.005f);
            tea.localScale = new Vector3((CupR - 0.03f) * 2f, h * 0.5f, (CupR - 0.03f) * 2f);
            tea.localPosition = new Vector3(0f, 0.09f + h * 0.5f, 0f);
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var slide = (float)_game.CupSlide * 2.6f;
            _cup3d.position = World.MgStage3D.At(CupX + slide, TableTop, 0.6f);
            var level = (float)_game.Level;
            FillTea(_tea3d, level);
            // the band and its perfect line on the glass; both light up while the tea stands in them
            var band = _game.Band;
            var inner = GlassH - 0.04f;
            var bandH = Mathf.Max(0.02f, (float)(band.Half * 2.0) * inner);
            _band3d.localScale = new Vector3((CupR + 0.035f) * 2f, bandH * 0.5f, (CupR + 0.035f) * 2f);
            _band3d.localPosition = new Vector3(0f, 0.09f + (float)band.Center * inner, 0f);
            _core3d.localPosition = new Vector3(0f, 0.09f + (float)band.Center * inner, 0f);
            var inBand = level >= band.Center - band.Half && level <= band.Center + band.Half;
            var inCore = level >= band.Center - band.PerfectHalf && level <= band.Center + band.PerfectHalf;
            _bandR.sharedMaterial = inBand ? _bandHotMat : _bandMat;
            _coreR.sharedMaterial = inCore ? _coreHotMat : _coreMat;
            var pulse = inCore && !ReducedMotion ? 1f + Mathf.Sin(Time.time * 14f) * 0.04f : 1f;
            _core3d.localScale = new Vector3((CupR + 0.05f) * 2f * pulse, 0.0125f, (CupR + 0.05f) * 2f * pulse);
            // the kettle hovers over the cup, tips while pouring; the stream reaches down to the tea
            var pouring = _game.Holding;
            var tip = pouring ? -38f : -6f;
            _kettle3d.position = World.MgStage3D.At(CupX + slide - 0.42f, TableTop + GlassH + 0.85f + (pouring ? -0.08f : 0f) + Mathf.Sin(_pulse * 3f) * 0.03f, 0.6f);
            _kettle3d.rotation = Quaternion.Slerp(_kettle3d.rotation, Quaternion.Euler(0f, 0f, tip), 1f - Mathf.Exp(-10f * dt));
            _stream3d.gameObject.SetActive(pouring);
            if (pouring)
            {
                var top = _kettle3d.position + new Vector3(0.42f, -0.16f, 0f);
                var bottom = _cup3d.position + Vector3.up * (0.09f + Mathf.Clamp01(level) * inner);
                var len = Mathf.Max(0.05f, top.y - bottom.y);
                _stream3d.position = new Vector3(bottom.x, bottom.y + len * 0.5f, bottom.z);
                _stream3d.localScale = new Vector3(0.07f, len * 0.5f, 0.07f);
            }
            // the served cup glides off to the left with its tea
            if (_ghostT > 0f)
            {
                _ghost3d.gameObject.SetActive(true);
                var k = 1f - _ghostT / 0.6f;
                _ghost3d.position = World.MgStage3D.At(CupX - k * 2.8f, TableTop, 0.6f);
            }
            else if (_ghost3d.gameObject.activeSelf) _ghost3d.gameObject.SetActive(false);
            if (_puddleT > 0f)
            {
                _puddleT -= dt;
                _puddle3d.gameObject.SetActive(_puddleT > 0f);
                var grow = Mathf.Clamp01((2.5f - _puddleT) * 4f);
                _puddle3d.localScale = new Vector3(0.84f * grow, 0.005f, 0.6f * grow);
            }
            // Gooby, the host: leans in while the tea runs, hops on a perfect cup
            var g = _stage.Gooby;
            if (g == null) return;
            _goobyHop = Mathf.Max(0f, _goobyHop - dt * 2.4f);
            var lean = pouring ? Mathf.Sin(_pulse * 7f) * 2f - 4f : Mathf.Sin(_pulse * 2.2f) * 1.5f;
            g.transform.position = World.MgStage3D.At(-0.62f, Mathf.Sin(_goobyHop * Mathf.PI) * 0.35f, 1.75f);
            g.transform.rotation = Quaternion.Euler(0f, 205f, lean);
            if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) g.SetEmotion("happy");
        }

        private void StagePoured(string result)
        {
            if (_stage == null) return;
            var at = _cup3d.position + Vector3.up * (GlassH + 0.2f);
            var g = _stage.Gooby;
            switch (result)
            {
                case "perfect":
                    if (!ReducedMotion) { World.Effects.Puff(at, new Color(1f, 0.85f, 0.4f), 18, 0.1f, 2f, 0.6f, Vector3.up * 2f, 1.2f); _goobyHop = 1f; }
                    g?.SetEmotion("ecstatic");
                    g?.Play("celebrate");
                    break;
                case "good":
                    if (!ReducedMotion) World.Effects.Puff(at, new Color(0.7f, 0.95f, 0.7f), 10, 0.08f, 1.5f, 0.5f, Vector3.up * 1.5f, 1f);
                    g?.SetEmotion("happy");
                    g?.Play("wave");
                    break;
                default:
                    _puddleT = 2.5f;
                    if (!ReducedMotion) World.Effects.Puff(at, new Color(0.7f, 0.45f, 0.2f, 0.9f), 12, 0.07f, 1.6f, 0.5f, Vector3.up, 1.4f);
                    g?.SetEmotion("dizzy");
                    break;
            }
            _emoteLeft = 1.3f;
            if (result == "perfect" || result == "good") FillTea(_ghostTea, (float)_game.Band.Center);
        }

        private void DropStage()
        {
            _stage?.Destroy();
            _stage = null;
        }
    }
}
