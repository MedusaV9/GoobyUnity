using System.Collections.Generic;
using Gooby.Core.Minigames.Gvz;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D front yard of Goobys vs Zombies (port of gvz_stage3d.gd, gvz_stage3d_crowd.gd, gvz_stage3d_props.gd): a
    /// checkered lawn in front of a picket fence, bushes, trees and the neighbours' houses, the defended house on the left
    /// with a wooden deck where the player's Gooby keeps watch. Every anchor comes from the 2D layout: the lawn tiles are
    /// the tap rectangles cast onto the ground (exactly under the finger), figures stand on cell centres and are sized by
    /// the projected cell. Towers are chibi Goobys with the tool of their job (carrots, ice, nutella jar, magnet,
    /// trampoline, melon catapult …), zombies wear their cones, buckets, papers and shields, waddle and munch; carrots,
    /// frost, stars and melons fly, nutella drops bob, the lane mowers wait with a sleepy Gooby, the boss truck rumbles.
    /// Placement hints, meters, fog and rain stay on the canvas above.
    /// </summary>
    public sealed partial class GvzScreen
    {
        private sealed class Figure { public Transform Root, Body; public string Type, Skin = ""; public List<Renderer> SkinParts = new List<Renderer>(); }

        private World.MgStage3D _stage;
        private GvzRun _shownRun;
        private string _shownSetting = "";
        private Transform _yard, _lawnRoot, _boss3d;
        private readonly Dictionary<int, Figure> _towers3d = new Dictionary<int, Figure>();
        private readonly Dictionary<int, Figure> _zombies3d = new Dictionary<int, Figure>();
        private readonly Dictionary<int, Transform> _drops3d = new Dictionary<int, Transform>();
        private readonly Dictionary<int, Transform> _mowers3d = new Dictionary<int, Transform>();
        private readonly Dictionary<string, Stack<Transform>> _shotPool = new Dictionary<string, Stack<Transform>>();
        private readonly Dictionary<int, (string kind, Transform node)> _shots3d = new Dictionary<int, (string, Transform)>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private Material _skin, _skinRaged, _skinSlowed;
        private float _stageT;

        private const float CamPitch = 42f, CamFov = 28f;
        private static readonly Vector3 CamPos = new Vector3(0f, 10f, -11.1f);

        protected override bool StageHasGooby => _stage != null;

        private Vector3 G(float px, float py) => _stage.GroundPoint(px, py);

        /// <summary>One cell's width in metres on its lane (far lanes are wider — every figure still looks cell-sized).</summary>
        private float CellMetres(int lane)
        {
            var y = LawnY + (lane + 0.5f) * LaneH;
            return Vector3.Distance(G(LawnX, y), G(LawnX + CellW, y));
        }

        private Vector3 CellCentre3D(int lane, int col) => G(LawnX + (col + 0.5f) * CellW, LawnY + (lane + 0.5f) * LaneH);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, MgScenery.Hex(0xBFE6FF));
            _stage.Bind(Field);
            _stage.Aim(CamPos, CamPitch, CamFov);
            _skin = World.ArtLibrary.Solid(ZombieSkin, 0.2f);
            _skinRaged = World.ArtLibrary.Solid(MgScenery.Hex(0xE7A0A0), 0.2f);
            _skinSlowed = World.ArtLibrary.Solid(Color.Lerp(ZombieSkin, Frost, 0.55f), 0.3f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnDefender(outfit);
        }

        // ── the yard: rebuilt per level setting (lawn colours, lanes in play) ──

        private void BuildYard(GvzRun run, string setting)
        {
            if (_yard != null) Object.Destroy(_yard.gameObject);
            _yard = new GameObject("Yard").transform;
            _yard.SetParent(_stage.Root.transform, false);
            _shownSetting = setting;
            var (a, b) = LawnColors(setting);
            // the far ground, a little paler, and the soil band under the deck
            var far = World.ArtLibrary.Box(_yard, "FarGround", World.MgStage3D.At(0f, -0.06f, 20f), new Vector3(90f, 0.1f, 60f),
                World.ArtLibrary.PbrSlab(setting == "garage" || setting == "strasse" || setting == "dach" ? "paving" : "grass", new Vector2(90f, 60f), 2.5f, Color.Lerp(b, Color.white, 0.12f), 0.05f), false);
            far.name = "FarGround";
            // the lawn: every cell is the tap rectangle cast onto the ground — one mesh, two (or three) colours
            _lawnRoot = new GameObject("Lawn").transform;
            _lawnRoot.SetParent(_yard, false);
            var dirt = Color.Lerp(a, MgScenery.Hex(0xB9A78F), 0.65f);
            var verts = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };
            var origin = _stage.Root.transform.position;
            void Quad(float x0, float y0, float x1, float y1, int sub, float lift)
            {
                var i = verts.Count;
                verts.Add(G(x0, y0) - origin + Vector3.up * lift);
                verts.Add(G(x1, y0) - origin + Vector3.up * lift);
                verts.Add(G(x1, y1) - origin + Vector3.up * lift);
                verts.Add(G(x0, y1) - origin + Vector3.up * lift);
                tris[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Quad(LawnX - 6f, LawnY - 6f, LawnX + LawnW + 6f, LawnY + LawnH + 6f, 2, 0.005f);
            for (var lane = 0; lane < 5; lane++)
            {
                var active = run == null || run.Lanes.Contains(lane);
                for (var col = 0; col < GvzLogic.Cols; col++)
                    Quad(LawnX + col * CellW, LawnY + lane * LaneH, LawnX + (col + 1) * CellW, LawnY + (lane + 1) * LaneH, active ? (lane + col) % 2 : 2, 0.02f);
            }
            var mesh = new Mesh { name = "GvzLawn", subMeshCount = 3 };
            mesh.SetVertices(verts);
            for (var s = 0; s < 3; s++) mesh.SetTriangles(tris[s], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("LawnMesh");
            go.transform.SetParent(_lawnRoot, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var grassA = World.ArtLibrary.Pbr(setting == "garage" || setting == "strasse" ? "paving" : setting == "dach" ? "roof_tiles" : "grass", 1.6f, a, 0.06f);
            var grassB = World.ArtLibrary.Pbr(setting == "garage" || setting == "strasse" ? "paving" : setting == "dach" ? "roof_tiles" : "grass", 1.6f, b, 0.06f);
            mr.sharedMaterials = new[] { grassA, grassB, World.ArtLibrary.Pbr("grass", 2f, run == null ? Color.Lerp(b, Color.black, 0.15f) : dirt, 0.04f) };
            BuildBackdrop(setting);
            BuildHouse();
        }

        /// <summary>Behind the lawn: picket fence, flowering bushes, round trees and the neighbours' houses (per setting).</summary>
        private void BuildBackdrop(string setting)
        {
            var fenceY = LawnY - 22f;
            var left = G(LawnX, fenceY);
            var right = G(960f, fenceY);
            var z = (left.z + right.z) * 0.5f - World.MgStage3D.Origin.z;
            var x0 = left.x - World.MgStage3D.Origin.x;
            var x1 = right.x - World.MgStage3D.Origin.x + 4f;
            switch (setting)
            {
                case "strasse":
                    for (var i = 0; i < 6; i++)
                        _stage.PlaceScenery(i % 2 == 0 ? "house_pink" : "house_blue", World.MgStage3D.At(x0 + 2f + i * 4.6f, 0f, z + 5.5f), 180f, 0.9f).SetParent(_yard, true);
                    break;
                case "garage":
                    World.ArtLibrary.Box(_yard, "GarageWall", World.MgStage3D.At((x0 + x1) * 0.5f, 2f, z + 1.2f), new Vector3(x1 - x0 + 10f, 4f, 0.3f), World.ArtLibrary.Pbr("concrete", 3f, MgScenery.Hex(0xC9C2B6), 0.1f), false);
                    break;
                default:
                    for (var x = x0; x <= x1; x += 1.9f) _stage.PlaceScenery("picket_fence", World.MgStage3D.At(x, 0f, z + 0.4f), 0f, 1f).SetParent(_yard, true);
                    for (var i = 0; i < 6; i++)
                        _stage.PlaceScenery(i % 2 == 0 ? "bush_flowers" : "bush_big", World.MgStage3D.At(x0 + 1.5f + i * 3.3f, 0f, z + 1.6f), i * 40f, 1f).SetParent(_yard, true);
                    for (var i = 0; i < 5; i++)
                        _stage.PlaceScenery(i % 2 == 0 ? "tree_round_big" : "tree_round", World.MgStage3D.At(x0 + 3f + i * 4.4f, 0f, z + 5f + (i % 2) * 1.5f), i * 70f, 1.1f).SetParent(_yard, true);
                    _stage.PlaceScenery("house_cream", World.MgStage3D.At(x1 - 3f, 0f, z + 10f), 180f, 1f).SetParent(_yard, true);
                    _stage.PlaceScenery("cottage_blue", World.MgStage3D.At(x0 + 8f, 0f, z + 11f), 180f, 1f).SetParent(_yard, true);
                    break;
            }
        }

        /// <summary>The defended house on the left: its wall with a door and windows, a red roof edge, a wooden deck.</summary>
        private void BuildHouse()
        {
            var midY = LawnY + LawnH * 0.5f;
            var top = G(LawnX - 8f, LawnY - 30f);
            var bottom = G(LawnX - 8f, LawnY + LawnH + 10f);
            var x = top.x - World.MgStage3D.Origin.x - 0.25f;
            var zTop = top.z - World.MgStage3D.Origin.z;
            var zBottom = bottom.z - World.MgStage3D.Origin.z;
            var zMid = (zTop + zBottom) * 0.5f;
            var length = zTop - zBottom + 2f;
            var wallX = x - 1.2f;
            World.ArtLibrary.Box(_yard, "HouseWall", World.MgStage3D.At(wallX, 1.6f, zMid), new Vector3(0.4f, 3.2f, length),
                World.ArtLibrary.Pbr("plaster", 2.5f, MgScenery.Hex(0xFFE6CF), 0.1f), true);
            var roof = World.ArtLibrary.Box(_yard, "Roof", World.MgStage3D.At(wallX - 0.5f, 3.45f, zMid), new Vector3(2.2f, 0.25f, length + 0.4f),
                World.ArtLibrary.Pbr("roof_tiles", 1.2f, MgScenery.Hex(0xE07A6A), 0.15f), true).transform;
            roof.rotation = Quaternion.Euler(0f, 0f, -22f);
            var door = G(LawnX - 8f, midY);
            var doorZ = door.z - World.MgStage3D.Origin.z;
            World.ArtLibrary.Box(_yard, "Door", World.MgStage3D.At(wallX + 0.22f, 1.05f, doorZ), new Vector3(0.08f, 2.1f, 1.1f), World.ArtLibrary.Solid(MgScenery.Hex(0x9A6A4A), 0.3f), false);
            World.ArtLibrary.Sphere(_yard, "Knob", World.MgStage3D.At(wallX + 0.28f, 1.05f, doorZ - 0.38f), 0.05f, World.ArtLibrary.Solid(Gold, 0.7f), false);
            foreach (var wz in new[] { zMid + length * 0.3f, zMid - length * 0.3f })
            {
                World.ArtLibrary.Box(_yard, "Window", World.MgStage3D.At(wallX + 0.21f, 1.7f, wz), new Vector3(0.06f, 1.0f, 1.2f), World.ArtLibrary.Glow(new Color(0.75f, 0.9f, 1f)), false);
                World.ArtLibrary.Box(_yard, "Sill", World.MgStage3D.At(wallX + 0.26f, 1.16f, wz), new Vector3(0.12f, 0.08f, 1.4f), World.ArtLibrary.Solid(Color.white, 0.3f), false);
            }
            // the deck between the house and the lawn
            World.ArtLibrary.Box(_yard, "Deck", World.MgStage3D.At(wallX + 0.85f, 0.08f, zMid), new Vector3(1.3f, 0.16f, length),
                World.ArtLibrary.PbrSlab("wood_planks", new Vector2(1.3f, length), 1f, MgScenery.Hex(0xC79A6A), 0.25f), true);
        }

        private async System.Threading.Tasks.Task SpawnDefender(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(0f, -30f, 0f), 1f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            g.gameObject.SetActive(false);
        }

        // ── chibi figures ──

        private static Transform Part(Transform parent, string name, Vector3 local, Vector3 size, Material mat, bool shadows = true)
        {
            var p = World.ArtLibrary.Sphere(parent, name, parent.position, 0.5f, mat, shadows).transform;
            p.localPosition = local;
            p.localScale = size;
            return p;
        }

        /// <summary>A cream Gooby (lop ears, eyes, cheeks) facing +x — the body of most towers and of the mower rider.</summary>
        private static void ChibiGooby(Transform root, Color fur, bool sleepy = false)
        {
            var furMat = World.ArtLibrary.Solid(fur, 0.15f);
            var ink = World.ArtLibrary.Solid(Outline, 0.3f);
            Part(root, "Body", new Vector3(0f, 0.3f, 0f), new Vector3(0.62f, 0.56f, 0.58f), furMat);
            Part(root, "Head", new Vector3(0.02f, 0.72f, 0f), new Vector3(0.56f, 0.5f, 0.56f), furMat);
            foreach (var s in new[] { -1f, 1f })
            {
                Part(root, "Ear", new Vector3(-0.02f, 0.66f, s * 0.31f), new Vector3(0.16f, 0.46f, 0.12f), furMat).localRotation = Quaternion.Euler(s * 22f, 0f, 0f);
                Part(root, "Eye", new Vector3(0.26f, 0.76f, s * 0.11f), sleepy ? new Vector3(0.03f, 0.02f, 0.09f) : new Vector3(0.05f, 0.09f, 0.07f), ink, false);
                Part(root, "Cheek", new Vector3(0.23f, 0.66f, s * 0.19f), new Vector3(0.04f, 0.07f, 0.1f), World.ArtLibrary.Solid(new Color(0.96f, 0.63f, 0.6f), 0.2f), false);
            }
        }

        private Figure BuildTower(GvzTower t)
        {
            var f = new Figure { Root = new GameObject("Tower" + t.Id).transform, Type = t.Type };
            f.Root.SetParent(_stage.Root.transform, false);
            f.Body = new GameObject("Body").transform;
            f.Body.SetParent(f.Root, false);
            var b = f.Body;
            var carrot = World.ArtLibrary.Solid(Carrot, 0.3f);
            var leaf = World.ArtLibrary.Solid(Leaf, 0.2f);
            void CarrotAt(Vector3 at, Material tip)
            {
                var c = _stage.Cone(b.position, 0.07f, 0.34f, tip, true).transform;
                c.SetParent(b, true);
                c.localPosition = at;
                c.localRotation = Quaternion.Euler(0f, 0f, -90f);
                Part(b, "Leaf", at + new Vector3(-0.04f, 0.04f, 0f), new Vector3(0.12f, 0.05f, 0.06f), leaf, false);
            }
            switch (t.Type)
            {
                case "schnarch_knolle":
                    Part(b, "Potato", new Vector3(0f, 0.18f, 0f), new Vector3(0.66f, 0.38f, 0.56f), World.ArtLibrary.Solid(MgScenery.Hex(0xC79A62), 0.15f));
                    foreach (var s in new[] { -1f, 1f }) Part(b, "Eye", new Vector3(0.26f, 0.24f, s * 0.1f), new Vector3(0.04f, 0.04f, 0.06f), World.ArtLibrary.Solid(Outline, 0.3f), false);
                    Part(b, "Light", new Vector3(0f, 0.42f, 0f), Vector3.one * 0.12f, World.ArtLibrary.Glow(new Color(1.2f, 0.3f, 0.35f)), false).name = "ArmedLight";
                    return f;
                case "boom_beere":
                    foreach (var (dz, dy) in new[] { (-0.17f, 0.22f), (0.17f, 0.26f) })
                    {
                        Part(b, "Cherry", new Vector3(0f, dy, dz), Vector3.one * 0.42f, World.ArtLibrary.Solid(Berry, 0.55f));
                        var stem = World.ArtLibrary.Cylinder(b, "Stem", b.position, 0.02f, 0.4f, leaf).transform;
                        stem.localPosition = new Vector3(0f, dy + 0.34f, dz * 0.5f);
                        stem.localRotation = Quaternion.Euler(dz > 0f ? -25f : 25f, 0f, 0f);
                    }
                    return f;
                case "dicker_bert":
                    Part(b, "Wall", new Vector3(0f, 0.46f, 0f), new Vector3(0.78f, 0.92f, 0.86f), World.ArtLibrary.Solid(MgScenery.Hex(0xE9D3B0), 0.15f));
                    foreach (var s in new[] { -1f, 1f }) Part(b, "Eye", new Vector3(0.38f, 0.56f, s * 0.13f), new Vector3(0.05f, 0.09f, 0.07f), World.ArtLibrary.Solid(Outline, 0.3f), false);
                    return f;
                case "melonen_meier":
                    ChibiGooby(b, Cream);
                    World.ArtLibrary.Box(b, "Catapult", b.position, new Vector3(0.5f, 0.1f, 0.3f), World.ArtLibrary.Solid(MgScenery.Hex(0x9A6A4A), 0.3f)).transform.localPosition = new Vector3(0.32f, 0.08f, 0f);
                    var arm = World.ArtLibrary.Box(b, "Arm", b.position, new Vector3(0.06f, 0.5f, 0.06f), World.ArtLibrary.Solid(MgScenery.Hex(0x9A6A4A), 0.3f)).transform;
                    arm.localPosition = new Vector3(0.42f, 0.32f, 0f);
                    arm.localRotation = Quaternion.Euler(0f, 0f, -35f);
                    Part(b, "Melon", new Vector3(0.56f, 0.56f, 0f), new Vector3(0.28f, 0.24f, 0.24f), World.ArtLibrary.Solid(MgScenery.Hex(0x5FB55A), 0.4f));
                    return f;
                case "trampolin_gooby":
                    var tramp = World.ArtLibrary.Cylinder(b, "Trampoline", b.position, 0.36f, 0.06f, World.ArtLibrary.Solid(MgScenery.Hex(0x6FA8E8), 0.4f)).transform;
                    tramp.localPosition = new Vector3(0f, 0.18f, 0f);
                    foreach (var s in new[] { -1f, 1f })
                        World.ArtLibrary.Cylinder(b, "Leg", b.position, 0.025f, 0.18f, World.ArtLibrary.Solid(Outline, 0.3f)).transform.localPosition = new Vector3(s * 0.26f, 0.09f, 0f);
                    var rider = new GameObject("Rider").transform;
                    rider.SetParent(b, false);
                    rider.localPosition = new Vector3(0f, 0.2f, 0f);
                    rider.localScale = Vector3.one * 0.7f;
                    rider.name = "Bouncer";
                    ChibiGooby(rider, Cream);
                    return f;
            }
            var fur = t.Type == "eis_gooby" ? MgScenery.Hex(0xDDF2FB) : t.Type == "goldi" ? MgScenery.Hex(0xFFE58A) : Cream;
            ChibiGooby(b, fur);
            switch (t.Type)
            {
                case "moehrenschuetze": CarrotAt(new Vector3(0.34f, 0.36f, 0f), carrot); break;
                case "doppelmoehre": CarrotAt(new Vector3(0.34f, 0.3f, -0.1f), carrot); CarrotAt(new Vector3(0.34f, 0.46f, 0.1f), carrot); break;
                case "eis_gooby":
                    CarrotAt(new Vector3(0.34f, 0.36f, 0f), World.ArtLibrary.Solid(Frost, 0.6f));
                    Part(b, "Snow", new Vector3(0f, 1.06f, 0f), Vector3.one * 0.14f, World.ArtLibrary.Glow(new Color(0.9f, 0.97f, 1.1f)), false);
                    break;
                case "nutella_sammler":
                case "goldi":
                    var jar = World.ArtLibrary.Cylinder(b, "Jar", b.position, 0.11f, 0.24f, World.ArtLibrary.Solid(Nutella, 0.5f)).transform;
                    jar.localPosition = new Vector3(0.26f, 0.16f, 0.2f);
                    World.ArtLibrary.Cylinder(b, "Lid", b.position, 0.12f, 0.06f, World.ArtLibrary.Solid(NutellaLid, 0.4f)).transform.localPosition = new Vector3(0.26f, 0.3f, 0.2f);
                    if (t.Type == "goldi") Part(b, "Star", new Vector3(0f, 1.08f, 0f), new Vector3(0.18f, 0.18f, 0.06f), World.ArtLibrary.Glow(new Color(1.2f, 0.95f, 0.35f)), false);
                    break;
                case "magnet_gooby":
                    var magnet = _stage.Torus(b, "Magnet", b.position, 0.13f, 0.045f, World.ArtLibrary.Solid(Berry, 0.5f)).transform;
                    magnet.localPosition = new Vector3(0.36f, 0.42f, 0f);
                    magnet.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;
                case "pust_gooby":
                    Part(b, "Puff", new Vector3(0.3f, 0.64f, 0f), Vector3.one * 0.16f, World.ArtLibrary.Solid(new Color(0.96f, 0.63f, 0.6f), 0.2f), false);
                    break;
                case "sternchen_gooby":
                    Part(b, "Star", new Vector3(0.36f, 0.5f, 0f), new Vector3(0.22f, 0.22f, 0.07f), World.ArtLibrary.Glow(new Color(1.2f, 0.95f, 0.35f)), false);
                    break;
            }
            return f;
        }

        private Figure BuildZombie(GvzZombie z)
        {
            var f = new Figure { Root = new GameObject("Zombie" + z.Id).transform, Type = z.Type };
            f.Root.SetParent(_stage.Root.transform, false);
            f.Body = new GameObject("Body").transform;
            f.Body.SetParent(f.Root, false);
            var b = f.Body;
            var shirt = World.ArtLibrary.Solid(z.Type == "sprinter" ? MgScenery.Hex(0xF2C14E) : z.Type == "tuersteher" ? MgScenery.Hex(0x5A6070) : z.Type == "brocken" ? MgScenery.Hex(0x8A6A9E) : MgScenery.Hex(0x7FA7D9), 0.2f);
            var ink = World.ArtLibrary.Solid(Outline, 0.3f);
            Part(b, "Shirt", new Vector3(0f, 0.36f, 0f), new Vector3(0.44f, 0.5f, 0.42f), shirt);
            f.SkinParts.Add(Part(b, "Head", new Vector3(-0.04f, 0.78f, 0f), new Vector3(0.44f, 0.4f, 0.42f), _skin).GetComponent<Renderer>());
            foreach (var s in new[] { -1f, 1f })
            {
                var armRoot = new GameObject("Arm").transform;
                armRoot.SetParent(b, false);
                armRoot.localPosition = new Vector3(-0.05f, 0.5f, s * 0.2f);
                armRoot.name = s < 0f ? "ArmL" : "ArmR";
                f.SkinParts.Add(Part(armRoot, "Hand", new Vector3(-0.22f, 0f, 0f), new Vector3(0.42f, 0.11f, 0.11f), _skin).GetComponent<Renderer>());
                Part(b, "Leg", new Vector3(0f, 0.08f, s * 0.11f), new Vector3(0.13f, 0.22f, 0.13f), ink);
                Part(b, "Eye", new Vector3(-0.24f, 0.82f, s * 0.09f), Vector3.one * 0.08f, World.ArtLibrary.Solid(Color.white, 0.4f), false);
            }
            if (z.ArmorHp > 0)
            {
                switch (z.Armor)
                {
                    case "huetchen":
                        var cone = _stage.Cone(b.position, 0.17f, 0.42f, World.ArtLibrary.Solid(MgScenery.Hex(0xFF9A3C), 0.4f), true).transform;
                        cone.SetParent(b, true);
                        cone.localPosition = new Vector3(-0.03f, 0.94f, 0f);
                        cone.name = "Armor";
                        break;
                    case "eimer":
                        var bucket = World.ArtLibrary.Cylinder(b, "Armor", b.position, 0.2f, 0.3f, World.ArtLibrary.Solid(MgScenery.Hex(0xA9B2BA), 0.75f)).transform;
                        bucket.localPosition = new Vector3(-0.03f, 1.02f, 0f);
                        break;
                    case "zeitung":
                        World.ArtLibrary.Box(b, "Armor", b.position, new Vector3(0.04f, 0.38f, 0.42f), World.ArtLibrary.Solid(MgScenery.Hex(0xF4F0E6), 0.2f)).transform.localPosition = new Vector3(-0.42f, 0.5f, 0f);
                        break;
                    case "schild":
                        World.ArtLibrary.Box(b, "Armor", b.position, new Vector3(0.06f, 0.7f, 0.42f), World.ArtLibrary.Solid(MgScenery.Hex(0xB5885E), 0.3f)).transform.localPosition = new Vector3(-0.38f, 0.42f, 0f);
                        break;
                }
            }
            if (z.Flying)
            {
                Part(b, "Balloon", new Vector3(0f, 1.6f, 0f), new Vector3(0.42f, 0.5f, 0.42f), World.ArtLibrary.Solid(MgScenery.Hex(0xFF8FB0), 0.6f));
                World.ArtLibrary.Cylinder(b, "String", b.position, 0.01f, 0.5f, ink).transform.localPosition = new Vector3(0f, 1.15f, 0f);
            }
            if (z.Jumps > 0) World.ArtLibrary.Cylinder(b, "Pogo", b.position, 0.025f, 0.9f, World.ArtLibrary.Solid(MgScenery.Hex(0x9A6A4A), 0.3f)).transform.localPosition = new Vector3(0.24f, 0.45f, 0f);
            if (z.Type == "brocken") b.localScale = Vector3.one * 1.35f;
            else if (z.Type == "sprinter") b.localScale = Vector3.one * 0.9f;
            return f;
        }

        private Transform BuildShot(string kind)
        {
            var root = new GameObject("Shot:" + kind).transform;
            root.SetParent(_stage.Root.transform, false);
            switch (kind)
            {
                case "frost": Part(root, "Ice", Vector3.zero, Vector3.one * 0.2f, World.ArtLibrary.Glow(new Color(0.75f, 0.95f, 1.15f)), false); break;
                case "star": Part(root, "Star", Vector3.zero, new Vector3(0.22f, 0.22f, 0.08f), World.ArtLibrary.Glow(new Color(1.25f, 1f, 0.4f)), false); break;
                case "melon": Part(root, "Melon", Vector3.zero, new Vector3(0.26f, 0.22f, 0.22f), World.ArtLibrary.Solid(MgScenery.Hex(0x5FB55A), 0.4f)); break;
                default:
                    var c = _stage.Cone(root.position, 0.06f, 0.28f, World.ArtLibrary.Solid(Carrot, 0.3f), true).transform;
                    c.SetParent(root, true);
                    c.localPosition = new Vector3(-0.1f, 0f, 0f);
                    c.localRotation = Quaternion.Euler(0f, 0f, -90f);
                    Part(root, "Leaf", new Vector3(-0.14f, 0.03f, 0f), new Vector3(0.1f, 0.05f, 0.06f), World.ArtLibrary.Solid(Leaf, 0.2f), false);
                    break;
            }
            return root;
        }

        private Transform BuildDrop()
        {
            var root = new GameObject("Nutella").transform;
            root.SetParent(_stage.Root.transform, false);
            var nut = World.ArtLibrary.Solid(Nutella, 0.6f);
            Part(root, "Blob", new Vector3(0f, 0.14f, 0f), new Vector3(0.32f, 0.26f, 0.32f), nut);
            var tip = _stage.Cone(root.position, 0.1f, 0.18f, nut, false).transform;
            tip.SetParent(root, true);
            tip.localPosition = new Vector3(0f, 0.24f, 0f);
            Part(root, "Shine", new Vector3(-0.06f, 0.2f, -0.12f), Vector3.one * 0.07f, World.ArtLibrary.Glow(Color.white), false);
            return root;
        }

        private Transform BuildMower()
        {
            var root = new GameObject("Mower").transform;
            root.SetParent(_stage.Root.transform, false);
            World.ArtLibrary.Box(root, "Deck", root.position, new Vector3(0.62f, 0.16f, 0.46f), World.ArtLibrary.Solid(MgScenery.Hex(0xFF8B8B), 0.4f)).transform.localPosition = new Vector3(0f, 0.14f, 0f);
            foreach (var (x, zz) in new[] { (-0.22f, -0.24f), (0.22f, -0.24f), (-0.22f, 0.24f), (0.22f, 0.24f) })
            {
                var w = World.ArtLibrary.Cylinder(root, "Wheel", root.position, 0.08f, 0.06f, World.ArtLibrary.Solid(Outline, 0.3f)).transform;
                w.localPosition = new Vector3(x, 0.08f, zz);
                w.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var rider = new GameObject("Rider").transform;
            rider.SetParent(root, false);
            rider.localPosition = new Vector3(-0.05f, 0.2f, 0f);
            rider.localScale = Vector3.one * 0.62f;
            ChibiGooby(rider, Cream, sleepy: true);
            return root;
        }

        private void BuildBoss()
        {
            _boss3d = new GameObject("BossTruck").transform;
            _boss3d.SetParent(_stage.Root.transform, false);
            var b = _boss3d;
            World.ArtLibrary.Box(b, "Box", b.position, new Vector3(1.5f, 1.0f, 1.0f), World.ArtLibrary.Solid(MgScenery.Hex(0x6E9C6A), 0.3f)).transform.localPosition = new Vector3(0.25f, 0.75f, 0f);
            World.ArtLibrary.Box(b, "Cab", b.position, new Vector3(0.6f, 0.75f, 0.95f), World.ArtLibrary.Solid(MgScenery.Hex(0xA9D6A0), 0.3f)).transform.localPosition = new Vector3(-0.8f, 0.62f, 0f);
            World.ArtLibrary.Box(b, "Windshield", b.position, new Vector3(0.04f, 0.3f, 0.8f), World.ArtLibrary.Glow(new Color(0.8f, 0.92f, 1f)), false).transform.localPosition = new Vector3(-1.11f, 0.78f, 0f);
            foreach (var x in new[] { -0.8f, 0.0f, 0.6f })
                foreach (var zz in new[] { -0.5f, 0.5f })
                {
                    var w = World.ArtLibrary.Cylinder(b, "Wheel", b.position, 0.2f, 0.1f, World.ArtLibrary.Solid(Outline, 0.3f)).transform;
                    w.localPosition = new Vector3(x, 0.2f, zz);
                    w.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            Part(b, "King", new Vector3(0.1f, 1.45f, 0f), new Vector3(0.44f, 0.4f, 0.42f), _skin);
            var crown = _stage.Torus(b, "Crown", b.position, 0.16f, 0.05f, World.ArtLibrary.Glow(new Color(1.2f, 0.95f, 0.35f))).transform;
            crown.localPosition = new Vector3(0.1f, 1.68f, 0f);
            foreach (var s in new[] { -1f, 1f }) Part(b, "Eye", new Vector3(-0.12f, 1.5f, s * 0.09f), Vector3.one * 0.08f, World.ArtLibrary.Solid(Outline, 0.3f), false);
        }

        // ── every frame ──

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            var run = _game?.Run;
            var setting = run != null ? GvzJson.S(run.Level, "setting", "vorgarten") : "vorgarten";
            if (run != _shownRun || setting != _shownSetting || _yard == null)
            {
                ClearFigures();
                _shownRun = run;
                BuildYard(run, setting);
            }
            var gb = _stage.Gooby;
            var defending = run != null && _game.Phase != GvzGame.PhaseSelect && !_game.NetZombie;
            if (gb != null)
            {
                if (gb.gameObject.activeSelf != defending) gb.gameObject.SetActive(defending);
                if (defending)
                {
                    var door = G(LawnX - 12f, LawnY + LawnH * 0.5f + 14f);
                    gb.transform.position = door;
                    gb.transform.localScale = Vector3.one * CellMetres(2) * 0.9f;
                    gb.transform.rotation = Quaternion.Euler(0f, 120f, 0f);
                }
            }
            if (run == null) return;
            var tick = run.Tick;
            // towers
            _seen.Clear();
            foreach (var key in run.Towers.Keys)
            {
                var t = run.Towers[key];
                _seen.Add(t.Id);
                if (!_towers3d.TryGetValue(t.Id, out var f) || f.Type != t.Type)
                {
                    if (f != null) Object.Destroy(f.Root.gameObject);
                    f = BuildTower(t);
                    _towers3d[t.Id] = f;
                }
                f.Root.position = CellCentre3D(t.Lane, t.Col);
                f.Root.localScale = Vector3.one * CellMetres(t.Lane) * 0.95f;
                var bob = ReducedMotion ? 0f : Mathf.Sin(_stageT * 3f + t.Id) * 0.025f;
                f.Body.localPosition = new Vector3(0f, bob, 0f);
                f.Body.localRotation = Quaternion.Euler(0f, 25f, 0f);
                if (t.Type == "schnarch_knolle")
                {
                    var armed = tick >= (t.ArmedAt ?? 0);
                    var light = f.Body.Find("ArmedLight");
                    if (light != null) light.gameObject.SetActive(armed && Mathf.Sin(_stageT * 8f) > 0f);
                }
                else if (t.Type == "trampolin_gooby")
                {
                    var bouncer = f.Body.Find("Bouncer");
                    if (bouncer != null) bouncer.localPosition = new Vector3(0f, 0.2f + Mathf.Abs(Mathf.Sin(_stageT * 5f)) * 0.18f, 0f);
                }
                else if (t.Type == "boom_beere" && t.FuseAt.HasValue)
                    f.Body.localScale = Vector3.one * (1f + Mathf.Abs(Mathf.Sin(_stageT * 22f)) * 0.12f);
            }
            Sweep(_towers3d);
            // zombies: waddle toward the house, munch when eating, tinted when raging or slowed, dig as a mound
            _seen.Clear();
            foreach (var z in run.Zombies)
            {
                if (z.Dead) continue;
                var x = XToView(z.X);
                if (x > LawnX + LawnW + 60f) continue;
                _seen.Add(z.Id);
                if (!_zombies3d.TryGetValue(z.Id, out var f))
                {
                    f = BuildZombie(z);
                    _zombies3d[z.Id] = f;
                }
                var y = LawnY + (z.Lane + 0.5f) * LaneH;
                var at = G(x, y);
                var s = CellMetres(z.Lane) * 0.95f;
                f.Root.position = at + (z.Flying ? Vector3.up * s * 0.6f : Vector3.zero);
                f.Root.localScale = Vector3.one * s;
                var digging = z.State == "dig";
                f.Body.gameObject.SetActive(!digging);
                var walk = ReducedMotion ? 0f : Mathf.Sin(_stageT * 7f + z.Id);
                f.Body.localRotation = Quaternion.Euler(0f, -25f, 0f) * Quaternion.Euler(walk * 6f, 0f, 4f + walk * 3f);
                f.Body.localPosition = new Vector3(0f, Mathf.Abs(walk) * 0.03f + (z.Jumps > 0 ? Mathf.Abs(Mathf.Sin(_stageT * 6f + z.Id)) * 0.25f : 0f), 0f);
                var munch = z.State == "eat" && !ReducedMotion ? Mathf.Sin(_stageT * 12f) * 18f : 0f;
                foreach (var armName in new[] { "ArmL", "ArmR" })
                {
                    var arm = f.Body.Find(armName);
                    if (arm != null) arm.localRotation = Quaternion.Euler(0f, 0f, munch + (armName == "ArmL" ? walk * 8f : -walk * 8f));
                }
                var armor = f.Body.Find("Armor");
                if (armor != null && z.ArmorHp <= 0) armor.gameObject.SetActive(false);
                var skin = z.Raged ? "raged" : z.SlowUntil > tick && !z.Flying ? "slowed" : "";
                if (skin != f.Skin)
                {
                    f.Skin = skin;
                    var mat = skin == "raged" ? _skinRaged : skin == "slowed" ? _skinSlowed : _skin;
                    foreach (var r in f.SkinParts) if (r != null) r.sharedMaterial = mat;
                }
            }
            Sweep(_zombies3d);
            // projectiles (pooled per kind)
            _seen.Clear();
            foreach (var pr in run.Projectiles)
            {
                var x = XToView(pr.X);
                if (x > LawnX + LawnW + 10f) continue;
                _seen.Add(pr.Id);
                var kind = pr.Kind == "frost" || pr.Kind == "star" || pr.Kind == "melon" ? pr.Kind : "carrot";
                if (!_shots3d.TryGetValue(pr.Id, out var shot))
                {
                    shot = (kind, TakeShot(kind));
                    _shots3d[pr.Id] = shot;
                }
                var lift = kind == "melon" ? Mathf.Sin(((pr.X % 2000) / 2000f) * Mathf.PI) * 0.9f : 0f;
                var s = CellMetres(pr.Lane);
                shot.node.position = G(x, LawnY + (pr.Lane + 0.5f) * LaneH) + Vector3.up * (0.42f * s + lift * s);
                shot.node.localScale = Vector3.one * s;
                if (kind == "star" || kind == "melon") shot.node.Rotate(0f, 0f, -360f * dt, Space.Self);
            }
            _gone.Clear();
            foreach (var kv in _shots3d) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var id in _gone)
            {
                var (kind, node) = _shots3d[id];
                _shots3d.Remove(id);
                node.gameObject.SetActive(false);
                if (!_shotPool.TryGetValue(kind, out var stack)) _shotPool[kind] = stack = new Stack<Transform>();
                stack.Push(node);
            }
            // nutella drops bob on their cells and blink before they melt away
            _seen.Clear();
            foreach (var d in run.Drops)
            {
                _seen.Add(d.Id);
                if (!_drops3d.TryGetValue(d.Id, out var node)) _drops3d[d.Id] = node = BuildDrop();
                var s = CellMetres(d.Lane);
                node.position = CellCentre3D(d.Lane, d.Col) + Vector3.up * (0.12f * s + Mathf.Sin(_stageT * 3f + d.Id) * 0.05f * s) + new Vector3(0f, 0f, -0.15f * s);
                node.localScale = Vector3.one * s;
                node.gameObject.SetActive(!(d.Expires - tick < 40 && Mathf.Sin(_stageT * 18f) < 0f));
            }
            _gone.Clear();
            foreach (var kv in _drops3d) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var id in _gone) { Object.Destroy(_drops3d[id].gameObject); _drops3d.Remove(id); }
            // mowers: parked at the lane's start with a dozing Gooby, rolling once they run
            foreach (var lane in run.Mowers.Keys)
            {
                var m = run.Mowers[lane];
                if (!_mowers3d.TryGetValue(lane, out var node)) _mowers3d[lane] = node = BuildMower();
                var gone = m.Used && !m.Active;
                node.gameObject.SetActive(!gone);
                if (gone) continue;
                var x = m.Active ? XToView(m.X) : LawnX - 18f;
                var s = CellMetres(lane) * 0.9f;
                node.position = G(x, LawnY + (lane + 0.5f) * LaneH);
                node.localScale = Vector3.one * s;
                node.rotation = Quaternion.Euler(0f, 0f, m.Active && !ReducedMotion ? Mathf.Sin(_stageT * 30f) * 2f : 0f);
            }
            // the boss truck with King Knurps
            var boss = run.Boss;
            var showBoss = boss != null && boss.Hp > 0;
            if (showBoss && _boss3d == null) BuildBoss();
            if (_boss3d != null)
            {
                _boss3d.gameObject.SetActive(showBoss);
                if (showBoss)
                {
                    var s = CellMetres(boss.Lane) * 1.1f;
                    var shake = boss.Phase >= 3 && !ReducedMotion ? Mathf.Sin(_stageT * 30f) * 0.03f : 0f;
                    _boss3d.position = G(XToView(boss.X), LawnY + (boss.Lane + 0.5f) * LaneH) + new Vector3(shake, 0f, 0f);
                    _boss3d.localScale = Vector3.one * s;
                }
            }
        }

        private Transform TakeShot(string kind)
        {
            if (_shotPool.TryGetValue(kind, out var stack) && stack.Count > 0)
            {
                var n = stack.Pop();
                n.gameObject.SetActive(true);
                return n;
            }
            return BuildShot(kind);
        }

        /// <summary>Removes the figures whose ids were not seen this frame.</summary>
        private void Sweep(Dictionary<int, Figure> figures)
        {
            _gone.Clear();
            foreach (var kv in figures) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var id in _gone)
            {
                if (figures[id].Root != null) Object.Destroy(figures[id].Root.gameObject);
                figures.Remove(id);
            }
        }

        private void ClearFigures()
        {
            foreach (var f in _towers3d.Values) if (f.Root != null) Object.Destroy(f.Root.gameObject);
            foreach (var f in _zombies3d.Values) if (f.Root != null) Object.Destroy(f.Root.gameObject);
            foreach (var n in _drops3d.Values) if (n != null) Object.Destroy(n.gameObject);
            foreach (var n in _mowers3d.Values) if (n != null) Object.Destroy(n.gameObject);
            foreach (var s in _shots3d.Values) if (s.node != null) Object.Destroy(s.node.gameObject);
            foreach (var st in _shotPool.Values) foreach (var n in st) if (n != null) Object.Destroy(n.gameObject);
            _towers3d.Clear(); _zombies3d.Clear(); _drops3d.Clear(); _mowers3d.Clear(); _shots3d.Clear(); _shotPool.Clear();
            if (_boss3d != null) Object.Destroy(_boss3d.gameObject);
            _boss3d = null;
        }

        // ── moments ──

        private void StageEvent(GvzEvent ev)
        {
            if (_stage == null || _game?.Run == null) return;
            var gb = _stage.Gooby;
            switch (ev.Kind)
            {
                case "place":
                    if (!ReducedMotion) World.Effects.Puff(CellCentre3D(ev.Lane, ev.Col), new Color(0.75f, 0.9f, 0.6f, 0.9f), 10, 0.1f, 1.2f, 0.5f, Vector3.up, 1f);
                    break;
                case "die":
                    if (!ReducedMotion) World.Effects.Puff(G(XToView(ev.X), LawnY + (ev.Lane + 0.5f) * LaneH) + Vector3.up * 0.4f, new Color(0.66f, 0.84f, 0.62f, 0.9f), 12, 0.12f, 1.6f, 0.6f, Vector3.up, 1.2f);
                    break;
                case "blast":
                    if (!ReducedMotion) World.Effects.Puff(CellCentre3D(ev.Lane, ev.Col) + Vector3.up * 0.3f, new Color(1f, 0.6f, 0.25f, 0.95f), 22, 0.2f, 3f, 0.7f, Vector3.up, 1.6f);
                    break;
                case "collect":
                    if (!ReducedMotion) World.Effects.Sparkle(CellCentre3D(ev.Lane, ev.Col) + Vector3.up * 0.3f, 0.3f, 10);
                    break;
                case "wave":
                    gb?.SetEmotion("scared");
                    break;
                case "mower":
                    gb?.Play("hop", 0.1f);
                    break;
                case "boss_enter":
                    gb?.SetEmotion("scared");
                    break;
                case "won":
                    gb?.SetEmotion("ecstatic");
                    gb?.Play("celebrate", 0.1f);
                    break;
                case "lost":
                    gb?.SetEmotion("sad");
                    break;
            }
        }

        private void DropStage()
        {
            ClearFigures();
            _shownRun = null;
            _shownSetting = "";
            _yard = _lawnRoot = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
