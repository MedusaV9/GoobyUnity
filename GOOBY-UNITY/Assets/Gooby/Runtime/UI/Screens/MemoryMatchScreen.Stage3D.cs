using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D picnic of Memory (port of memory_match_stage3d.gd): the cards lie as chunky tiles on a gingham blanket in
    /// the meadow, seen at a slant from above; turning one flips it over its long edge and the real food model pops up on
    /// its face (the Kenney food kit). The player's Gooby sits behind the blanket, cheers a pair and gets dizzy on a miss.
    /// Each tile is laid exactly under its 2D tap rectangle by a ray through the tilted camera, so the 2D cards stay the
    /// (invisible) tap targets.
    /// </summary>
    public sealed partial class MemoryMatchScreen
    {
        private World.MgStage3D _stage;
        private readonly List<Tile3D> _tiles = new List<Tile3D>();
        private Transform _blanket3d;
        private Material _faceMat, _matchedMat, _backMat, _emblemMat, _dotMat;
        private float _goobyBounce;
        private static readonly string[] FaceModels = { "carrot", "apple", "banana", "cheese", "watermelon", "donut-sprinkles", "cupcake", "burger", "ice-cream", "pizza", "cake", "strawberry" };

        private sealed class Tile3D
        {
            public Transform Root, Flipper, Back, Holder;
            public Renderer Body;
            public string Face;
            public float Flip, W;
            public bool Matched;
        }

        protected override bool StageHasGooby => _stage != null;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewSize.w, ViewSize.h, new Color(0.62f, 0.8f, 0.95f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 10.5f, -8f), 42f, 52f);
            _faceMat = World.ArtLibrary.Solid(new Color(0.98f, 0.94f, 0.84f), 0.1f);
            _matchedMat = World.ArtLibrary.Solid(new Color(0.62f, 0.84f, 0.56f), 0.1f);
            _backMat = World.ArtLibrary.Solid(new Color(0.94f, 0.48f, 0.6f), 0.2f);
            _emblemMat = World.ArtLibrary.Solid(new Color(0.7f, 0.27f, 0.42f), 0.25f);
            _dotMat = World.ArtLibrary.Solid(new Color(1f, 0.95f, 0.86f), 0.1f);
            var s = _stage;
            s.Ground(0f, new Color(0.62f, 0.8f, 0.5f), "grass", 80f, 70f, 14f);
            s.Painting("bg_town_horizon", 0f, 34f, 22f);
            s.Fence("picket_fence", 0f, 15.5f, -11f, 11f, 1.9f, 1f);
            s.Row("tree_round_big", 0f, 19f, -12f, 12f, 5, 1.6f, 0f, 1.2f, 7);
            s.Row("tree_round", 0f, 12.5f, -9.5f, -8.2f, 2, 1.4f, 0f, 0.6f, 8);
            s.Row("tree_round", 0f, 13.2f, 8.6f, 9.8f, 2, 1.5f, 0f, 0.6f, 9);
            s.Meadow(0f, -9f, 9f, 9.5f, 15f, 26, 12, 11);
            s.Meadow(0f, -7f, 7f, -7f, -2.5f, 18, 8, 12);
            // the gingham blanket (rose cloth with pale bands both ways); laid out under the deal in LayoutStage
            _blanket3d = new GameObject("Blanket").transform;
            _blanket3d.SetParent(s.Root.transform, false);
            _blanket3d.position = World.MgStage3D.At(0f, 0f, 3f);
            var cloth = World.ArtLibrary.Box(_blanket3d, "Cloth", _blanket3d.position + Vector3.up * 0.02f, new Vector3(1f, 0.04f, 1f), World.ArtLibrary.Solid(new Color(0.97f, 0.84f, 0.8f), 0.05f), false);
            cloth.transform.SetParent(_blanket3d, true);
            var band = World.ArtLibrary.Solid(new Color(0.96f, 0.72f, 0.72f), 0.05f);
            for (var i = 0; i < 9; i++)
            {
                var o = -0.44f + i * 0.11f;
                var a = World.ArtLibrary.Box(_blanket3d, "BandX", _blanket3d.position + new Vector3(0f, 0.043f, o), new Vector3(1f, 0.004f, 0.016f), band, false);
                var b = World.ArtLibrary.Box(_blanket3d, "BandZ", _blanket3d.position + new Vector3(o, 0.044f, 0f), new Vector3(0.016f, 0.004f, 1f), band, false);
                a.transform.SetParent(_blanket3d, true);
                b.transform.SetParent(_blanket3d, true);
            }
            s.Prop("picnic_basket_round", World.MgStage3D.At(-1.35f, 0f, -2.9f), 0.9f, 25f);
            s.Prop("apple", World.MgStage3D.At(1.0f, 0f, -3.1f), 0.8f, 10f);
            s.Prop("strawberry", World.MgStage3D.At(1.5f, 0f, -2.8f), 0.8f, 40f);
            s.Prop("cupcake", World.MgStage3D.At(0.7f, 0f, -2.6f), 0.8f, 0f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnStageGooby(outfit);
        }

        private async System.Threading.Tasks.Task SpawnStageGooby(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(1.4f, 0f, 9f), 1.9f, outfit);
            if (g == null || _stage == null) return;
            g.Play("sit-loop");
            g.SetEmotion("happy");
            PlaceStageGooby();
        }

        /// <summary>Gooby sits right behind the blanket's far edge, a little off centre, looking at the cards.</summary>
        private void PlaceStageGooby()
        {
            if (_stage?.Gooby == null || _blanket3d == null) return;
            var far = _blanket3d.position.z + _blanket3d.localScale.z * 0.5f + 0.9f;
            var p = new Vector3(_blanket3d.position.x + _blanket3d.localScale.x * 0.22f, World.MgStage3D.Origin.y + Mathf.Sin(_goobyBounce * Mathf.PI) * 0.35f, far);
            _stage.Gooby.transform.position = p;
            _stage.Gooby.transform.rotation = Quaternion.Euler(0f, 195f, Mathf.Sin(Time.time * 2.4f) * 2f);
        }

        /// <summary>One tile per 2D card, each under its tap rectangle; the blanket spans the deal with a hem.</summary>
        private void LayoutStage()
        {
            if (_stage == null || FieldW <= 0f) return;
            while (_tiles.Count < _cards.Count) _tiles.Add(SpawnTile());
            var sx = (float)ViewSize.w / FieldW;
            var sy = (float)ViewSize.h / FieldH;
            float lx = float.MaxValue, lz = float.MaxValue, hx = float.MinValue, hz = float.MinValue;
            for (var i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                var on = i < _cards.Count;
                tile.Root.gameObject.SetActive(on);
                if (!on) continue;
                var c = _cards[i];
                var cx = (c.style.left.value.value + CardW * 0.5f) * sx;
                var cy = (c.style.top.value.value + CardH * 0.5f) * sy;
                var centre = _stage.GroundPoint(cx, cy);
                var w = Mathf.Clamp(_stage.GroundMetres(cx, cy, CardW * sx) * 0.96f, 0.3f, 4f);
                tile.W = w;
                tile.Root.position = centre;
                tile.Root.localScale = Vector3.one * w;
                tile.Flip = 0f;
                tile.Matched = false;
                SetTileFace(tile, _game.Cards[i].Key);
                lx = Mathf.Min(lx, centre.x - w * 0.6f); hx = Mathf.Max(hx, centre.x + w * 0.6f);
                lz = Mathf.Min(lz, centre.z - w * 0.7f); hz = Mathf.Max(hz, centre.z + w * 0.7f);
            }
            if (lx > hx) return;
            const float pad = 0.7f;
            _blanket3d.position = new Vector3((lx + hx) * 0.5f, World.MgStage3D.Origin.y, (lz + hz) * 0.5f);
            _blanket3d.localScale = new Vector3(hx - lx + pad * 2f, 1f, hz - lz + pad * 2f);
            PlaceStageGooby();
        }

        private Tile3D SpawnTile()
        {
            var t = new Tile3D { Root = new GameObject("Tile").transform };
            t.Root.SetParent(_stage.Root.transform, false);
            t.Flipper = new GameObject("Flipper").transform;
            t.Flipper.SetParent(t.Root, false);
            var body = World.ArtLibrary.Box(t.Flipper, "Body", Vector3.zero, new Vector3(0.92f, 0.08f, 1.12f), _faceMat);
            body.transform.localPosition = Vector3.zero;
            t.Body = body.GetComponent<Renderer>();
            t.Back = new GameObject("Back").transform;
            t.Back.SetParent(t.Flipper, false);
            var plate = World.ArtLibrary.Box(t.Back, "Plate", Vector3.zero, new Vector3(0.84f, 0.02f, 1.04f), _backMat, false);
            plate.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var emblem = World.ArtLibrary.Cylinder(t.Back, "Emblem", Vector3.zero, 0.22f, 0.03f, _emblemMat, false);
            emblem.transform.localPosition = new Vector3(0f, 0.065f, 0f);
            // a paw on the stamp: one pad and three toes
            var pad = World.ArtLibrary.Sphere(t.Back, "Pad", Vector3.zero, 0.075f, _dotMat, false);
            pad.transform.localPosition = new Vector3(0f, 0.08f, -0.03f);
            pad.transform.localScale = new Vector3(0.16f, 0.03f, 0.13f);
            for (var k = -1; k <= 1; k++)
            {
                var toe = World.ArtLibrary.Sphere(t.Back, "Toe", Vector3.zero, 0.035f, _dotMat, false);
                toe.transform.localPosition = new Vector3(k * 0.075f, 0.08f, 0.075f - Mathf.Abs(k) * 0.02f);
                toe.transform.localScale = new Vector3(0.07f, 0.025f, 0.07f);
            }
            t.Holder = new GameObject("Face").transform;
            t.Holder.SetParent(t.Flipper, false);
            t.Holder.localPosition = new Vector3(0f, -0.04f, 0f);
            t.Holder.localRotation = Quaternion.Euler(180f, 0f, 0f);
            t.Holder.gameObject.SetActive(false);
            return t;
        }

        private void SetTileFace(Tile3D tile, string key)
        {
            if (tile.Face == key) return;
            tile.Face = key;
            for (var i = tile.Holder.childCount - 1; i >= 0; i--) Object.Destroy(tile.Holder.GetChild(i).gameObject);
            var model = System.Array.IndexOf(FaceModels, key) >= 0 ? key : "apple";
            var food = _stage.Prop(model, tile.Holder.position, 1f);
            food.SetParent(tile.Holder, false);
            food.localPosition = Vector3.zero;
            food.localRotation = Quaternion.identity;
            food.localScale = Vector3.one * 1.05f;
        }

        private float _emoteLeft;

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _goobyBounce = Mathf.Max(0f, _goobyBounce - dt * 2.6f);
            if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) _stage.Gooby?.SetEmotion("happy");
            PlaceStageGooby();
            var revealing = _game.Revealing;
            for (var i = 0; i < _tiles.Count && i < _game.Cards.Count; i++)
            {
                var tile = _tiles[i];
                var state = _game.Cards[i].State;
                var up = state != MemoryMatchGame.Down || revealing;
                tile.Flip = Mathf.MoveTowards(tile.Flip, up ? 180f : 0f, 520f * dt);
                var mid = Mathf.Sin(Mathf.Clamp01(tile.Flip / 180f) * Mathf.PI);
                tile.Flipper.localRotation = Quaternion.Euler(tile.Flip, 0f, 0f);
                tile.Flipper.localPosition = new Vector3(0f, 0.06f + mid * 0.45f, 0f);
                var faceUp = tile.Flip > 90f;
                if (tile.Holder.gameObject.activeSelf != faceUp) tile.Holder.gameObject.SetActive(faceUp);
                if (tile.Back.gameObject.activeSelf == faceUp) tile.Back.gameObject.SetActive(!faceUp);
                var matched = state == MemoryMatchGame.Matched;
                if (matched != tile.Matched)
                {
                    tile.Matched = matched;
                    tile.Body.sharedMaterial = matched ? _matchedMat : _faceMat;
                }
                // the food on a face-up tile turns slowly so it reads as a little 3D treat
                if (faceUp && tile.Holder.childCount > 0) tile.Holder.GetChild(0).localRotation = Quaternion.Euler(0f, Time.time * 40f + i * 30f, 0f);
            }
        }

        /// <summary>A pair: stars from the tile, Gooby beams and hops. A miss: a dust poof, Gooby dizzy.</summary>
        private void StageResolved(bool hit, int index)
        {
            if (_stage == null || index < 0 || index >= _tiles.Count) return;
            var tile = _tiles[index];
            var at = tile.Root.position + Vector3.up * (tile.W * 0.7f);
            if (!ReducedMotion)
            {
                if (hit) World.Effects.Puff(at, new Color(1f, 0.9f, 0.5f), 16, 0.14f, 2.2f, 0.6f, Vector3.up * 2.4f, 1.1f);
                else World.Effects.Puff(at, new Color(0.85f, 0.82f, 0.78f, 0.85f), 10, 0.12f, 1.2f, 0.5f, Vector3.up, 0.9f);
            }
            if (hit && !ReducedMotion) _goobyBounce = 1f;
            _stage.Gooby?.SetEmotion(hit ? "ecstatic" : "dizzy");
            _emoteLeft = 1.1f;
        }

        private void StageCleared()
        {
            if (_stage?.Gooby == null) return;
            _stage.Gooby.SetEmotion("ecstatic");
            _stage.Gooby.Play("celebrate");
            if (!ReducedMotion) World.Effects.Sparkle(_stage.Gooby.transform.position + Vector3.up * 1.2f, 0.8f, 30);
        }

        private void DropStage()
        {
            _tiles.Clear();
            _blanket3d = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
