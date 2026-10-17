using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The workshop diorama behind the workbench sheet: a warm wooden room (plank floor, plaster wall with a pegboard of
    /// tools, a sunny window, a shelf of material crates), the potting bench, a little display podium with the piece the
    /// selected recipe makes, and Gooby beside the bench. While crafting he hammers (build_hammer-loop) with a knock and
    /// a puff of sawdust on every blow and the piece grows on the podium; at the end it pops to full size with sparkles
    /// and he cheers. Renders into the sheet's stage field through an <see cref="MgStage3D"/>.
    /// </summary>
    public sealed class WorkshopStage
    {
        private const float GoobyScale = 1.32f;
        private const float PieceMax = 0.82f;
        private static readonly Vector3 Podium = new Vector3(-1.05f, 0.14f, -0.15f);
        private static readonly Vector3 GoobyAt = new Vector3(0.78f, 0f, 0.3f);

        private readonly MgStage3D _s;
        private GoobyAvatar _gooby;
        private Transform _piece;
        private string _pieceId;
        private int _loadToken;
        private float _pieceScale = 1f;
        private float _craftT = -1f, _craftDur, _hitIn;
        private float _popT = -1f;
        private float _idleT;

        public bool Crafting => _craftT >= 0f;

        public WorkshopStage(VisualElement field, float viewW, float viewH, Dictionary<string, string> outfit)
        {
            _s = new MgStage3D(viewW, viewH, ArtLibrary.Hex("#F6E3C8")) { HeightShare = 0.7f };
            _s.Bind(field);
            _s.Camera.nearClipPlane = 0.1f;
            _s.Camera.farClipPlane = 30f;
            var lookAt = MgStage3D.At(-0.1f, 0.78f, 0.3f);
            _s.Camera.transform.position = MgStage3D.At(0f, 1.75f, -4.7f);
            _s.Camera.transform.rotation = Quaternion.LookRotation(lookAt - _s.Camera.transform.position, Vector3.up);
            _s.Camera.fieldOfView = 34f;
            BuildRoom();
            _ = SpawnGooby(outfit);
        }

        private Transform Root => _s.Root.transform;

        private GameObject Box(string name, Vector3 local, Vector3 size, Material m, bool shadows = true) =>
            ArtLibrary.Box(Root, name, MgStage3D.At(local.x, local.y, local.z), size, m, shadows);

        private void BuildRoom()
        {
            var floor = ArtLibrary.PbrSlab("wood_floor", new Vector2(9f, 6f), 1.6f, ArtLibrary.Hex("#E9C9A0"), 0.35f, 0.75f);
            Box("Floor", new Vector3(0f, -0.1f, 0.6f), new Vector3(9f, 0.2f, 6f), floor, false);
            var wall = ArtLibrary.PbrSlab("plaster", new Vector2(9f, 3.4f), 2.4f, ArtLibrary.Hex("#FBE9D2"), 0.05f, 0.5f);
            Box("Wall", new Vector3(0f, 1.7f, 1.55f), new Vector3(9f, 3.4f, 0.1f), wall, false);
            var wainscot = ArtLibrary.Pbr("wood_planks", 0.6f, ArtLibrary.Hex("#D9AE7E"), 0.2f, 0.6f);
            Box("Wainscot", new Vector3(0f, 0.42f, 1.48f), new Vector3(9f, 0.84f, 0.05f), wainscot, false);
            Box("Rail", new Vector3(0f, 0.86f, 1.46f), new Vector3(9f, 0.06f, 0.08f), ArtLibrary.Solid(ArtLibrary.Hex("#B98B5E"), 0.2f), false);

            // pegboard with the tools above the bench
            var board = Box("Pegboard", new Vector3(-0.3f, 1.62f, 1.47f), new Vector3(1.7f, 0.9f, 0.04f), ArtLibrary.Solid(ArtLibrary.Hex("#E8B97F"), 0.15f), false);
            board.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var dark = ArtLibrary.Solid(ArtLibrary.Hex("#6B4F3A"), 0.2f);
            var steel = ArtLibrary.Solid(ArtLibrary.Hex("#C9D0DC"), 0.6f);
            var red = ArtLibrary.Solid(ArtLibrary.Hex("#FF8B8B"), 0.3f);
            for (var i = 0; i < 6; i++)
                for (var j = 0; j < 3; j++)
                    Box("Peg", new Vector3(-1.0f + i * 0.28f, 1.35f + j * 0.27f, 1.44f), new Vector3(0.025f, 0.025f, 0.02f), dark, false);
            // hammer, saw, wrench, a ruler and a coil of string
            Box("HammerHandle", new Vector3(-0.85f, 1.6f, 1.43f), new Vector3(0.05f, 0.42f, 0.03f), ArtLibrary.Solid(ArtLibrary.Hex("#C98E55"), 0.2f), false);
            Box("HammerHead", new Vector3(-0.85f, 1.82f, 1.42f), new Vector3(0.2f, 0.08f, 0.05f), steel, false);
            Box("SawBlade", new Vector3(-0.42f, 1.62f, 1.43f), new Vector3(0.36f, 0.16f, 0.015f), steel, false);
            Box("SawGrip", new Vector3(-0.2f, 1.64f, 1.42f), new Vector3(0.1f, 0.14f, 0.04f), red, false);
            var wrench = Box("Wrench", new Vector3(0.1f, 1.6f, 1.43f), new Vector3(0.05f, 0.4f, 0.025f), steel, false);
            wrench.transform.rotation = Quaternion.Euler(0f, 0f, 18f);
            Box("Ruler", new Vector3(0.36f, 1.55f, 1.43f), new Vector3(0.06f, 0.5f, 0.012f), ArtLibrary.Solid(ArtLibrary.Hex("#FFD166"), 0.2f), false);
            ArtLibrary.Cylinder(Root, "String", MgStage3D.At(0.38f, 1.92f, 1.42f), 0.08f, 0.05f, ArtLibrary.Solid(ArtLibrary.Hex("#7FCF9B"), 0.2f))
                .transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // sunny window on the right, a soft beam of motes through it
            var frame = ArtLibrary.Solid(ArtLibrary.Hex("#FFFFFF"), 0.3f);
            Box("WindowFrame", new Vector3(1.55f, 1.85f, 1.47f), new Vector3(1.1f, 1.0f, 0.06f), frame, false);
            Box("WindowSky", new Vector3(1.55f, 1.85f, 1.44f), new Vector3(0.94f, 0.84f, 0.02f), ArtLibrary.Glow(new Color(1.15f, 1.35f, 1.6f)), false);
            Box("WindowCross", new Vector3(1.55f, 1.85f, 1.43f), new Vector3(0.05f, 0.84f, 0.02f), frame, false);
            Box("WindowBar", new Vector3(1.55f, 1.85f, 1.43f), new Vector3(0.94f, 0.05f, 0.02f), frame, false);
            Box("WindowSill", new Vector3(1.55f, 1.32f, 1.38f), new Vector3(1.2f, 0.06f, 0.2f), frame);
            Effects.SunMotes(Root, MgStage3D.At(1.55f, 1.85f, 1.35f), new Vector3(-0.45f, -0.55f, -0.7f).normalized, 2.4f, 0.32f);

            // shelf with material crates on the left
            var shelfWood = ArtLibrary.Pbr("wood_planks", 0.5f, ArtLibrary.Hex("#C98E55"), 0.2f, 0.6f);
            foreach (var y in new[] { 0.55f, 1.15f })
                Box("Shelf", new Vector3(-2.15f, y, 1.25f), new Vector3(1.0f, 0.05f, 0.45f), shelfWood);
            foreach (var x in new[] { -2.62f, -1.68f })
                Box("ShelfSide", new Vector3(x, 0.62f, 1.25f), new Vector3(0.05f, 1.24f, 0.45f), shelfWood);
            _s.PlaceScenery("garden_crate", MgStage3D.At(-2.35f, 0.58f, 1.25f), 8f, 0.55f);
            _s.PlaceScenery("shop_crate", MgStage3D.At(-1.95f, 1.18f, 1.25f), -6f, 0.5f);
            _s.PlaceScenery("watering_can", MgStage3D.At(-1.9f, 0.58f, 1.2f), 30f, 0.8f);
            // a stack of planks and a log pile on the floor
            var plank = ArtLibrary.Pbr("wood_planks", 0.5f, ArtLibrary.Hex("#E2B889"), 0.2f, 0.55f);
            for (var i = 0; i < 4; i++)
                Box("Plank", new Vector3(1.9f, 0.04f + i * 0.07f, 0.85f), new Vector3(1.2f, 0.06f, 0.22f), plank).transform.rotation = Quaternion.Euler(0f, 12f + i * 3f, 0f);
            _s.PlaceScenery("tree_stump", MgStage3D.At(2.45f, 0f, 0.2f), 20f, 0.7f);

            // the bench and the display podium for the piece
            _s.PlaceScenery("workbench", MgStage3D.At(-0.05f, 0f, 0.55f), 180f, 1.1f);
            var podium = ArtLibrary.Cylinder(Root, "Podium", MgStage3D.At(Podium.x, Podium.y * 0.5f, Podium.z), 0.5f, Podium.y, ArtLibrary.Solid(ArtLibrary.Hex("#FFF8EE"), 0.3f));
            podium.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.Cylinder(Root, "PodiumRim", MgStage3D.At(Podium.x, 0.02f, Podium.z), 0.56f, 0.04f, ArtLibrary.Solid(ArtLibrary.Hex("#FFD166"), 0.3f));
            var lamp = new GameObject("WorkLight").AddComponent<Light>();
            lamp.transform.SetParent(Root, false);
            lamp.transform.position = MgStage3D.At(-0.6f, 2.4f, -0.8f);
            lamp.type = LightType.Point;
            lamp.color = ArtLibrary.Hex("#FFE9C7");
            lamp.intensity = 1.2f;
            lamp.range = 6f;
            lamp.shadows = LightShadows.None;
        }

        private async System.Threading.Tasks.Task SpawnGooby(Dictionary<string, string> outfit)
        {
            var g = await _s.SpawnGoobyAsync(MgStage3D.At(GoobyAt.x, GoobyAt.y, GoobyAt.z), GoobyScale, outfit);
            if (g == null || _s.Root == null) return;
            _gooby = g;
            g.transform.rotation = Quaternion.Euler(0f, 228f, 0f);
            g.SetEmotion("happy");
            if (Crafting) g.Play("build_hammer-loop", 0.15f);
        }

        /// <summary>Shows the piece of a furniture item on the podium (its Blender prop, scaled to fit).</summary>
        public void ShowPiece(string itemId)
        {
            if (itemId == _pieceId) return;
            _pieceId = itemId;
            var token = ++_loadToken;
            if (_piece != null) Object.Destroy(_piece.gameObject);
            _piece = null;
            if (string.IsNullOrEmpty(itemId)) return;
            _ = LoadPiece(itemId, token);
        }

        private async System.Threading.Tasks.Task LoadPiece(string itemId, int token)
        {
            var anchor = new GameObject("Piece:" + itemId).transform;
            anchor.SetParent(Root, false);
            anchor.position = MgStage3D.At(Podium.x, Podium.y, Podium.z);
            anchor.rotation = Quaternion.Euler(0f, 200f, 0f);
            var prop = await AssetLoader.PropInstanceAsync(AssetLoader.ArchetypeFor(itemId), anchor);
            if (token != _loadToken || _s.Root == null) { if (anchor != null) Object.Destroy(anchor.gameObject); return; }
            if (prop == null) { Object.Destroy(anchor.gameObject); return; }
            var bounds = new Bounds(anchor.position, Vector3.zero);
            var any = false;
            foreach (var r in prop.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            var size = any ? Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) : 1f;
            _pieceScale = size > 0.01f ? Mathf.Min(1.6f, PieceMax / size) : 1f;
            anchor.localScale = Vector3.one * (Crafting ? 0.05f : _pieceScale);
            _piece = anchor;
        }

        /// <summary>Gooby starts hammering; the piece grows over <paramref name="seconds"/>.</summary>
        public void StartCraft(float seconds)
        {
            _craftDur = Mathf.Max(0.2f, seconds);
            _craftT = 0f;
            _hitIn = 0.18f;
            _popT = -1f;
            if (_piece != null) _piece.localScale = Vector3.one * 0.05f;
            if (_gooby != null)
            {
                _gooby.SetEmotion("happy");
                _gooby.Play("build_hammer-loop", 0.15f);
            }
        }

        /// <summary>The piece is done: it pops to full size with sparkles and Gooby cheers.</summary>
        public void FinishCraft(bool reducedMotion)
        {
            _craftT = -1f;
            _popT = 0f;
            var at = MgStage3D.At(Podium.x, Podium.y + 0.4f, Podium.z);
            Effects.Sparkle(at, 0.55f, reducedMotion ? 10 : 34);
            if (!reducedMotion) Effects.Puff(MgStage3D.At(Podium.x, Podium.y + 0.05f, Podium.z), ArtLibrary.Hex("#F3D3A2"), 14, 0.16f, 1.2f, 0.7f, Vector3.up, 0.9f);
            if (_gooby != null)
            {
                _gooby.SetEmotion("ecstatic");
                _gooby.Play("celebrate", 0.12f);
            }
        }

        public void Tick(float dt, bool reducedMotion)
        {
            if (_s.Root == null) return;
            if (_craftT >= 0f)
            {
                _craftT += dt;
                var k = Mathf.Clamp01(_craftT / _craftDur);
                _hitIn -= dt;
                if (_hitIn <= 0f)
                {
                    _hitIn = 0.45f;
                    Gooby.Unity.App.Sfx.Play("build_hammer", Random.Range(-1.5f, 1.5f));
                    if (!reducedMotion)
                        Effects.Puff(MgStage3D.At(Podium.x + Random.Range(-0.2f, 0.2f), Podium.y + 0.1f + k * 0.4f, Podium.z - 0.1f),
                            ArtLibrary.Hex("#EBC99A"), 6, 0.08f, 0.9f, 0.5f, Vector3.up, 0.8f);
                }
                if (_piece != null)
                {
                    // grows in steps with the blows, a little squash on each knock
                    var steps = Mathf.Floor(k * 6f) / 6f;
                    var knock = 1f - Mathf.Clamp01(_hitIn / 0.45f);
                    var squash = reducedMotion ? 1f : 1f + 0.06f * Mathf.Sin(knock * Mathf.PI);
                    var s = _pieceScale * Mathf.Lerp(0.12f, 0.82f, steps);
                    _piece.localScale = new Vector3(s * squash, s / squash, s * squash);
                }
            }
            else if (_popT >= 0f && _piece != null)
            {
                _popT += dt;
                var k = Mathf.Clamp01(_popT / 0.45f);
                var back = reducedMotion ? 1f : Gooby.Unity.UI.Juice.EaseOutBack(k);
                _piece.localScale = Vector3.one * _pieceScale * Mathf.Lerp(0.82f, 1f, back);
                if (k >= 1f)
                {
                    _popT = -1f;
                    _piece.localScale = Vector3.one * _pieceScale;
                }
            }
            if (_piece != null && !reducedMotion && _craftT < 0f)
            {
                _idleT += dt;
                _piece.rotation = Quaternion.Euler(0f, 200f + Mathf.Sin(_idleT * 0.6f) * 18f, 0f);
            }
        }

        public void Destroy()
        {
            _loadToken++;
            _s.Destroy();
        }
    }
}
