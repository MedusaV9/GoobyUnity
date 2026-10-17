using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The farm's build ground in 3D (port of ranch_bau_mode.gd's world, ranch_bau_overlay.gd and ranch_bau_visuals.gd):
    /// the 16×16 grid of 3 m cells on the meadow beside the farm — the barn, silo and windmill behind it, tree rows and
    /// hills around — with the locked zones shaded and their rims dashed, every placed piece built from its catalogue
    /// def at its level (<see cref="RanchBauTeile"/>), the ghost of the piece being placed hovering over green or red
    /// cells, a line of paths or fences drawn by the finger, the selected piece outlined, and an orbit camera (pan,
    /// zoom, quarter turns). Grid metres (x east, z south) map to stage-local (x − 24, y, 24 − z): north is away from
    /// the camera at its first turn.
    /// </summary>
    public sealed class RanchBau3D
    {
        public const float ViewH = 720f;
        public const float Halb = 24f;
        public const float DistMin = 16f, DistMax = 86f, PitchStandard = 52f;

        public MgStage3D Stage { get; }

        private readonly Transform _root, _items, _overlay, _ghostRoot, _fx;
        private readonly Dictionary<string, (Transform t, string sig)> _teile = new Dictionary<string, (Transform, string)>();
        private readonly List<GameObject> _kacheln = new List<GameObject>();
        private readonly List<(Transform t, Vector3 vel, float life, float max, float scale)> _puffs = new List<(Transform, Vector3, float, float, float)>();
        private readonly List<(Transform t, float time)> _pops = new List<(Transform, float)>();
        private Transform _ghost, _auswahl, _zonen;
        private string _ghostSig = "", _versteckt, _zonenSig = "";
        private Vector3 _pivot, _pivotZiel;
        private float _yaw, _yawZiel, _dist = 54f, _distZiel = 54f, _pitch = PitchStandard, _t;
        private bool _destroyed;

        private static readonly Material Ok = ArtLibrary.Transparent(new Color(0.45f, 0.92f, 0.5f, 0.55f));
        private static readonly Material Nein = ArtLibrary.Transparent(new Color(0.98f, 0.38f, 0.36f, 0.55f));
        private static readonly Material Gelb = ArtLibrary.Transparent(new Color(1f, 0.86f, 0.3f, 0.85f));

        public static Vector3 L(double gx, double y, double gz) => new Vector3((float)gx - Halb, (float)y, Halb - (float)gz);

        public RanchBau3D(UnityEngine.UIElements.VisualElement field)
        {
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            Stage = new MgStage3D(ViewH * aspect, ViewH, ArtLibrary.Hex("#BFE3F7"));
            Stage.Bind(field);
            Stage.Camera.fieldOfView = 40f;
            Stage.Camera.nearClipPlane = 0.3f;
            Stage.Camera.farClipPlane = 420f;
            _root = new GameObject("RanchBau").transform;
            _root.SetParent(Stage.Root.transform, false);
            _root.position = MgStage3D.Origin;
            _items = Kind(_root, "Items");
            _overlay = Kind(_root, "Overlay");
            _ghostRoot = Kind(_root, "Ghost");
            _fx = Kind(_root, "Fx");
            BaueUmgebung();
            _pivot = _pivotZiel = L(15, 0, 33);
            Kamera(true);
            SetVisible(true);
        }

        private static Transform Kind(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        public void SetVisible(bool on) => Stage.Park(!on);

        public void Destroy()
        {
            _destroyed = true;
            Stage.Destroy();
        }

        // ── the meadow and the farm around it ──

        private void BaueUmgebung()
        {
            Stage.Ground(0f, ArtLibrary.Hex("#A8D58A"), "grass", 300f, 300f, 0f);
            var weg = ArtLibrary.Solid(ArtLibrary.Hex("#E8D6AA"), 0.05f);
            // the farm lane from the yard in the south up to the build ground
            ArtLibrary.Box(_root, "Lane", MgStage3D.At(-9f, 0.015f, -Halb - 22f), new Vector3(4.2f, 0.03f, 44f), weg, false);
            ArtLibrary.Box(_root, "Yard", MgStage3D.At(-9f, 0.012f, -Halb - 50f), new Vector3(26f, 0.03f, 16f), weg, false);
            var glb = new (string name, Vector3 at, float groesse, float yaw)[]
            {
                ("scheune", L(-14, 0, -12), 15f, 160f), ("silo", L(-26, 0, -2), 6f, 0f), ("windmuehle_turm", L(66, 0, -14), 9f, -30f),
                ("wasserturm", L(-22, 0, 30), 7f, 20f), ("huehnerstall", L(-20, 0, 52), 7f, 70f), ("scheune_offen", L(-26, 0, 70), 11f, 90f),
                ("brunnen", L(-8, 0, 64), 3.2f, 15f),
            };
            foreach (var (name, at, groesse, yaw) in glb) RanchBauTeile.Glb(_root, Pfad(name), at, yaw, groesse);
            foreach (var (x, z) in new[] { (-4.0, 60.0), (-4.0, 63.0), (-1.0, 61.5) }) RanchBauTeile.Glb(_root, Pfad("heuballen"), L(x, 0, z), 90f, 2.2f);
            Stage.Hills(0f, 120f, 9, ArtLibrary.Hex("#9CC98C"), 14f, 300f, 0f, 5);
            Stage.Hills(0f, 150f, 7, ArtLibrary.Hex("#B9DDB0"), 22f, 360f, 10f, 9);
            Stage.Row("tree_round_big", 0f, 52f, -80f, 70f, 13, 2.4f, 0f, 3.5f, 11);
            Stage.Row("tree_poplar", 0f, 64f, -90f, 90f, 11, 2.2f, 0f, 4f, 12);
            Stage.Row("tree_round_big", 0f, -6f, 40f, 80f, 4, 2.4f, 0f, 4f, 13);
            Stage.Row("tree_round_big", 0f, -6f, -80f, -40f, 4, 2.4f, 0f, 4f, 14);
            Stage.Clouds(6, 34f, 140f, 220f, 6f, 4);
            Stage.Sun(new Vector3(-70f, 70f, 160f), 9f);
            Stage.Meadow(0f, 30f, 70f, -50f, 40f, 60, 14, 21);
            Stage.Meadow(0f, -70f, -32f, -50f, 40f, 60, 14, 22);
            Stage.Meadow(0f, -30f, 30f, 28f, 46f, 50, 12, 23);
            // the build ground: a slightly fresher green with a low kerb all round
            var kante = ArtLibrary.Solid(ArtLibrary.Hex("#C9B48A"), 0.1f);
            foreach (var (c, s) in new[] { (L(24, 0, -0.15), new Vector3(48.6f, 0.16f, 0.3f)), (L(24, 0, 48.15), new Vector3(48.6f, 0.16f, 0.3f)),
                (L(-0.15, 0, 24), new Vector3(0.3f, 0.16f, 48f)), (L(48.15, 0, 24), new Vector3(0.3f, 0.16f, 48f)) })
                ArtLibrary.Box(_root, "Kerb", _root.position + c + Vector3.up * 0.08f, s, kante, false);
        }

        private static string Pfad(string name) => Path.Combine(AssetLoader.Root, "ranch", name + ".glb");

        /// <summary>The zones: unlocked ones fresh green with faint cell lines, locked ones shaded with a dashed rim.</summary>
        private void BaueZonen(RanchGridData grid, JObject balance)
        {
            var sig = string.Join(",", grid.Zonen.Select(z => z.X + ":" + z.Y + ":" + z.W + ":" + z.H));
            if (sig == _zonenSig) return;
            _zonenSig = sig;
            if (_zonen != null) UnityEngine.Object.Destroy(_zonen.gameObject);
            _zonen = Kind(_overlay, "Zonen");
            var frei = ArtLibrary.Solid(ArtLibrary.Hex("#B5DE93"), 0.05f);
            var zu = ArtLibrary.Solid(ArtLibrary.Hex("#93B57A"), 0.05f);
            var linie = ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.32f));
            var strich = ArtLibrary.Solid(ArtLibrary.Hex("#FFF6E0"), 0.2f);
            var teile = new List<GameObject>();
            var linien = new List<GameObject>();
            for (var y = 0; y < grid.Size.Y; y++)
                for (var x = 0; x < grid.Size.X; x++)
                {
                    var offen = grid.IstFrei(new Zelle(x, y));
                    teile.Add(ArtLibrary.Box(_zonen, "Zelle", _root.position + L(x * 3 + 1.5, 0.02, y * 3 + 1.5), new Vector3(3f, 0.04f, 3f), offen ? frei : zu, false));
                }
            for (var i = 0; i <= grid.Size.X; i++)
                for (var y = 0; y < grid.Size.Y; y++)
                {
                    var links = new Zelle(i - 1, y);
                    var rechts = new Zelle(i, y);
                    var a = grid.InBounds(links) && grid.IstFrei(links);
                    var b = grid.InBounds(rechts) && grid.IstFrei(rechts);
                    if (a && b) linien.Add(ArtLibrary.Box(_zonen, "Linie", _root.position + L(i * 3, 0.05, y * 3 + 1.5), new Vector3(0.06f, 0.02f, 3f), linie, false));
                    else if (a != b) Strichel(teile, i * 3, y * 3, true, strich);
                }
            for (var i = 0; i <= grid.Size.Y; i++)
                for (var x = 0; x < grid.Size.X; x++)
                {
                    var oben = new Zelle(x, i - 1);
                    var unten = new Zelle(x, i);
                    var a = grid.InBounds(oben) && grid.IstFrei(oben);
                    var b = grid.InBounds(unten) && grid.IstFrei(unten);
                    if (a && b) linien.Add(ArtLibrary.Box(_zonen, "Linie", _root.position + L(x * 3 + 1.5, 0.05, i * 3), new Vector3(3f, 0.02f, 0.06f), linie, false));
                    else if (a != b) Strichel(teile, x * 3, i * 3, false, strich);
                }
            Stage.Merge(_zonen, teile, "ZonenFlaeche");
            Stage.Merge(_zonen, linien, "ZonenLinien");
        }

        /// <summary>A dashed rim along one cell edge (vertical: along z at x, else along x at z).</summary>
        private void Strichel(List<GameObject> teile, double x, double z, bool laengsZ, Material m)
        {
            for (var k = 0; k < 3; k++)
            {
                var off = 0.5 + k;
                var c = laengsZ ? L(x, 0.07, z + off) : L(x + off, 0.07, z);
                teile.Add(ArtLibrary.Box(_zonen, "Strich", _root.position + c, laengsZ ? new Vector3(0.16f, 0.04f, 0.6f) : new Vector3(0.6f, 0.04f, 0.16f), m, false));
            }
        }

        // ── the pieces ──

        /// <summary>The farm's ribbons for the ribbon wall (<see cref="RanchBelohnungen.Wandschleifen"/>).</summary>
        public IReadOnlyList<int> Schleifen { get; set; }

        /// <summary>Builds what changed since the last call (signature per uid: item, spot, turn, level, fence level, ribbons).</summary>
        public void Sync(RanchGridData grid, JObject bau, JObject balance)
        {
            BaueZonen(grid, balance);
            var zaunStufe = RanchBauState.AnlageStufe(bau, "weidezaun");
            var da = new HashSet<string>();
            foreach (var item in grid.Items.Values)
            {
                da.Add(item.Uid);
                var stufe = Math.Max(1, RanchBauState.AnlageStufe(bau, item.Def.Id));
                var sig = item.Def.Id + "|" + item.At + "|" + item.Rot + "|" + item.Kante + "|" + stufe + "|" + (item.Kante != null ? zaunStufe : 0)
                    + (item.Def.Id == "deko_schleifenwand" ? "|" + string.Join(",", Schleifen ?? new List<int>()) : "");
                if (_teile.TryGetValue(item.Uid, out var alt) && alt.sig == sig) continue;
                if (alt.t != null) UnityEngine.Object.Destroy(alt.t.gameObject);
                var t = BaueTeil(_items, item.Def, stufe, zaunStufe, item.At, item.Rot, item.Kante);
                t.name = "Teil:" + item.Uid;
                _teile[item.Uid] = (t, sig);
            }
            foreach (var uid in _teile.Keys.Where(u => !da.Contains(u)).ToList())
            {
                if (_teile[uid].t != null) UnityEngine.Object.Destroy(_teile[uid].t.gameObject);
                _teile.Remove(uid);
            }
            foreach (var kv in _teile) kv.Value.t.gameObject.SetActive(kv.Key != _versteckt);
        }

        private Transform BaueTeil(Transform parent, RanchBauDef def, int stufe, int zaunStufe, Zelle at, int rot, string kante)
        {
            var t = Kind(parent, def.Id);
            if (kante != null)
            {
                var w = RanchGridData.KanteWorld(at, kante);
                t.localPosition = L(w.x, 0, w.z);
                t.localRotation = Quaternion.Euler(0f, (float)(w.rot * Mathf.Rad2Deg), 0f);
                RanchBauTeile.Zaun(t, zaunStufe);
            }
            else
            {
                var c = RanchGridData.WorldCenter(at, def.Footprint, rot);
                t.localPosition = L(c.x, 0, c.z);
                t.localRotation = Quaternion.Euler(0f, rot * 90f, 0f);
                RanchBauTeile.Baue(t, def, stufe, Schleifen);
            }
            return t;
        }

        public bool HatTeil(string uid) => uid != null && _teile.ContainsKey(uid);

        /// <summary>A piece being moved hides while its ghost shows the new spot (null shows every piece).</summary>
        public void Verstecke(string uid)
        {
            _versteckt = uid;
            foreach (var kv in _teile) kv.Value.t.gameObject.SetActive(kv.Key != _versteckt);
        }

        /// <summary>The freshly built (or upgraded) piece pops up and dust puffs round its feet.</summary>
        public void Pop(string uid, bool funkeln = false)
        {
            if (!_teile.TryGetValue(uid ?? "", out var e)) return;
            e.t.localScale = Vector3.one * 0.05f;
            _pops.Add((e.t, 0f));
            var p = e.t.localPosition;
            Puff(p, 10, 1.4f);
            if (funkeln) Funkeln(p);
        }

        // ── the ghost and the previews ──

        /// <summary>The piece being placed, hovering over its cells (green: fits, red: does not).</summary>
        public void GhostZelle(RanchBauDef def, int stufe, Zelle at, int rot, bool ok)
        {
            var sig = def.Id + "|" + stufe + "|" + rot;
            if (_ghost == null || _ghostSig != sig)
            {
                GhostWeg();
                _ghost = Kind(_ghostRoot, "Ghost:" + def.Id);
                RanchBauTeile.Baue(_ghost, def, stufe, Schleifen);
                _ghost.localRotation = Quaternion.Euler(0f, rot * 90f, 0f);
                _ghostSig = sig;
            }
            var c = RanchGridData.WorldCenter(at, def.Footprint, rot);
            var ziel = L(c.x, 0, c.z);
            _ghost.localPosition = new Vector3(ziel.x, _ghost.localPosition.y, ziel.z);
            Kacheln(RanchGridData.CellsFor(at, def.Footprint, rot).Select(z => (z, ok)).ToList());
        }

        /// <summary>A fence ghost on an edge.</summary>
        public void GhostKante(Zelle cell, string seite, int zaunStufe, bool ok)
        {
            var sig = "zaun|" + zaunStufe;
            if (_ghost == null || _ghostSig != sig)
            {
                GhostWeg();
                _ghost = Kind(_ghostRoot, "Ghost:zaun");
                RanchBauTeile.Zaun(_ghost, zaunStufe);
                _ghostSig = sig;
            }
            var w = RanchGridData.KanteWorld(cell, seite);
            var ziel = L(w.x, 0, w.z);
            _ghost.localPosition = new Vector3(ziel.x, _ghost.localPosition.y, ziel.z);
            _ghost.localRotation = Quaternion.Euler(0f, (float)(w.rot * Mathf.Rad2Deg), 0f);
            KantenKacheln(new List<(Zelle, string, bool)> { (cell, seite, ok) });
        }

        public void GhostWeg()
        {
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost.gameObject);
            _ghost = null;
            _ghostSig = "";
            Kacheln(null);
        }

        private Transform _vorschau;

        /// <summary>A drawn line of ground tiles: each cell shows its tile, green or red underneath.</summary>
        public void Vorschau(RanchBauDef def, List<(Zelle cell, bool ok)> zellen)
        {
            VorschauWeg();
            if (zellen == null || zellen.Count == 0) return;
            _vorschau = Kind(_ghostRoot, "Vorschau");
            foreach (var (cell, ok) in zellen)
            {
                if (!ok) continue;
                var t = Kind(_vorschau, def.Id);
                var c = RanchGridData.WorldCenter(cell, def.Footprint, 0);
                t.localPosition = L(c.x, 0.04, c.z);
                RanchBauTeile.Baue(t, def, 1);
            }
            Kacheln(zellen);
        }

        /// <summary>A drawn line of fences along the edges.</summary>
        public void VorschauKanten(int zaunStufe, List<(Zelle cell, string seite, bool ok)> kanten)
        {
            VorschauWeg();
            if (kanten == null || kanten.Count == 0) return;
            _vorschau = Kind(_ghostRoot, "Vorschau");
            foreach (var (cell, seite, ok) in kanten)
            {
                if (!ok) continue;
                var t = Kind(_vorschau, "zaun");
                var w = RanchGridData.KanteWorld(cell, seite);
                t.localPosition = L(w.x, 0.04, w.z);
                t.localRotation = Quaternion.Euler(0f, (float)(w.rot * Mathf.Rad2Deg), 0f);
                RanchBauTeile.Zaun(t, zaunStufe);
            }
            KantenKacheln(kanten);
        }

        public void VorschauWeg()
        {
            if (_vorschau != null) UnityEngine.Object.Destroy(_vorschau.gameObject);
            _vorschau = null;
            if (_ghost == null) Kacheln(null);
        }

        private void Kacheln(List<(Zelle cell, bool ok)> zellen)
        {
            foreach (var k in _kacheln) if (k != null) UnityEngine.Object.Destroy(k);
            _kacheln.Clear();
            if (zellen == null) return;
            foreach (var (cell, ok) in zellen)
                _kacheln.Add(ArtLibrary.Box(_overlay, "Kachel", _root.position + L(cell.X * 3 + 1.5, 0.09, cell.Y * 3 + 1.5), new Vector3(2.8f, 0.03f, 2.8f), ok ? Ok : Nein, false));
        }

        private void KantenKacheln(List<(Zelle cell, string seite, bool ok)> kanten)
        {
            Kacheln(null);
            foreach (var (cell, seite, ok) in kanten)
            {
                var w = RanchGridData.KanteWorld(cell, seite);
                var bar = ArtLibrary.Box(_overlay, "KantenKachel", _root.position + L(w.x, 0.09, w.z), new Vector3(3f, 0.03f, 0.7f), ok ? Ok : Nein, false);
                bar.transform.rotation = Quaternion.Euler(0f, (float)(w.rot * Mathf.Rad2Deg), 0f);
                _kacheln.Add(bar);
            }
        }

        // ── the selection ──

        /// <summary>Outlines a placed piece's footprint (or its edge); null clears.</summary>
        public void Auswahl(RanchGridData.Item item)
        {
            if (_auswahl != null) UnityEngine.Object.Destroy(_auswahl.gameObject);
            _auswahl = null;
            if (item == null) return;
            _auswahl = Kind(_overlay, "Auswahl");
            if (item.Kante != null)
            {
                var w = RanchGridData.KanteWorld(item.At, item.Kante);
                var bar = ArtLibrary.Box(_auswahl, "Rand", _root.position + L(w.x, 0.1, w.z), new Vector3(3.2f, 0.05f, 0.9f), Gelb, false);
                bar.transform.rotation = Quaternion.Euler(0f, (float)(w.rot * Mathf.Rad2Deg), 0f);
                return;
            }
            var fp = RanchGridData.RotatedFootprint(item.Def.Footprint, item.Rot);
            double x0 = item.At.X * 3, z0 = item.At.Y * 3, x1 = (item.At.X + fp.X) * 3, z1 = (item.At.Y + fp.Y) * 3;
            const float d = 0.32f;
            foreach (var (c, s) in new[]
            {
                (L((x0 + x1) / 2, 0.1, z0), new Vector3((float)(x1 - x0) + d, 0.05f, d)), (L((x0 + x1) / 2, 0.1, z1), new Vector3((float)(x1 - x0) + d, 0.05f, d)),
                (L(x0, 0.1, (z0 + z1) / 2), new Vector3(d, 0.05f, (float)(z1 - z0) + d)), (L(x1, 0.1, (z0 + z1) / 2), new Vector3(d, 0.05f, (float)(z1 - z0) + d)),
            })
                ArtLibrary.Box(_auswahl, "Rand", _root.position + c, s, Gelb, false);
        }

        // ── camera ──

        public float Yaw => _yawZiel;
        public float Dist => _distZiel;
        public Vector3 Pivot => _pivotZiel;

        /// <summary>Drags the ground along with the finger (pixels of a field <paramref name="fieldH"/> tall).</summary>
        public void Pan(Vector2 deltaPx, float fieldH)
        {
            var mpp = 2f * _dist * Mathf.Tan(Stage.Camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, fieldH);
            var dreh = Quaternion.Euler(0f, _yaw, 0f);
            var rechts = dreh * Vector3.right;
            var vor = dreh * Vector3.forward;
            _pivotZiel += (-rechts * deltaPx.x + vor * deltaPx.y / Mathf.Sin(_pitch * Mathf.Deg2Rad)) * mpp;
            Klemme();
        }

        public void Zoom(float faktor)
        {
            _distZiel = Mathf.Clamp(_distZiel * faktor, DistMin, DistMax);
        }

        /// <summary>A quarter turn round the pivot (+1 clockwise).</summary>
        public void Drehe(int richtung) => _yawZiel += 90f * Math.Sign(richtung);

        public void Fokus(double gx, double gz, float? dist = null)
        {
            _pivotZiel = L(gx, 0, gz);
            if (dist.HasValue) _distZiel = Mathf.Clamp(dist.Value, DistMin, DistMax);
            Klemme();
        }

        /// <summary>Glides the camera to a yaw, a distance and a ground point (grid metres).</summary>
        public void SetzeKamera(float yaw, float dist, double gx, double gz)
        {
            _yawZiel = yaw;
            _distZiel = Mathf.Clamp(dist, DistMin, DistMax);
            _pivotZiel = L(gx, 0, gz);
            Klemme();
        }

        private void Klemme()
        {
            _pivotZiel.x = Mathf.Clamp(_pivotZiel.x, -Halb - 6f, Halb + 6f);
            _pivotZiel.z = Mathf.Clamp(_pivotZiel.z, -Halb - 10f, Halb + 6f);
        }

        private void Kamera(bool sofort)
        {
            var k = sofort ? 1f : 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);
            _pivot = Vector3.Lerp(_pivot, _pivotZiel, k);
            _yaw = Mathf.LerpAngle(_yaw, _yawZiel, k);
            _dist = Mathf.Lerp(_dist, _distZiel, k);
            // close in, the camera tilts down less: a builder's look from the side, the whole farm from high up
            _pitch = Mathf.Lerp(38f, PitchStandard + 6f, Mathf.InverseLerp(DistMin, DistMax, _dist));
            var dreh = Quaternion.Euler(_pitch, _yaw, 0f);
            var pos = _root.position + _pivot + dreh * new Vector3(0f, 0f, -_dist);
            Stage.Camera.transform.position = pos;
            Stage.Camera.transform.rotation = Quaternion.LookRotation(_root.position + _pivot + Vector3.up * 0.6f - pos);
        }

        /// <summary>A field point (px, y down) → grid metres on the ground; false when the ray misses it.</summary>
        public bool Boden(Vector2 feld, float w, float h, out double gx, out double gz)
        {
            gx = gz = 0;
            if (w <= 0f || h <= 0f || Stage.Parked) return false;
            var ray = Stage.Camera.ViewportPointToRay(new Vector3(feld.x / w, 1f - feld.y / h, 0f));
            if (ray.direction.y > -1e-4f) return false;
            var t = (_root.position.y - ray.origin.y) / ray.direction.y;
            var p = ray.origin + ray.direction * t - _root.position;
            gx = p.x + Halb;
            gz = Halb - p.z;
            return true;
        }

        /// <summary>Grid metres (and a height) → the field's pixels (y down), for chips and floats over the 3D.</summary>
        public Vector2 ToField(double gx, double y, double gz, float w, float h) => Stage.ToField(_root.position + L(gx, y, gz), w, h);

        /// <summary>Is the point in front of the camera (chips behind it hide)?</summary>
        public bool Sichtbar(double gx, double y, double gz) => Stage.Camera.WorldToViewportPoint(_root.position + L(gx, y, gz)).z > 0f;

        // ── juice ──

        public void Puff(double gx, double gz, int n = 10, float groesse = 1.4f) => Puff(L(gx, 0, gz), n, groesse);

        private void Puff(Vector3 lokal, int n, float groesse)
        {
            var staub = ArtLibrary.Solid(ArtLibrary.Hex("#F3E6CF"), 0.05f);
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2f / n + UnityEngine.Random.value * 0.4f;
                var t = ArtLibrary.Sphere(_fx, "Staub", _root.position + lokal + new Vector3(Mathf.Cos(a) * groesse * 0.6f, 0.3f, Mathf.Sin(a) * groesse * 0.6f), 0.35f, staub, false).transform;
                var s = 0.4f + UnityEngine.Random.value * 0.5f;
                t.localScale = Vector3.one * s;
                _puffs.Add((t, new Vector3(Mathf.Cos(a), 0.5f + UnityEngine.Random.value * 0.6f, Mathf.Sin(a)) * (1.6f + groesse), 0f, 0.7f, s));
            }
        }

        /// <summary>Gold stars rise from an upgraded piece.</summary>
        public void Funkeln(Vector3 lokal)
        {
            var gold = ArtLibrary.Glow(new Color(1.4f, 1.15f, 0.45f));
            for (var i = 0; i < 14; i++)
            {
                var a = UnityEngine.Random.value * Mathf.PI * 2f;
                var r = 1f + UnityEngine.Random.value * 2.5f;
                var t = ArtLibrary.Sphere(_fx, "Funke", _root.position + lokal + new Vector3(Mathf.Cos(a) * r, 1f + UnityEngine.Random.value * 2f, Mathf.Sin(a) * r), 0.16f, gold, false).transform;
                _puffs.Add((t, new Vector3(0f, 2.5f + UnityEngine.Random.value * 2f, 0f), 0f, 1.2f, 0.32f));
            }
        }

        public void Funkeln(double gx, double gz) => Funkeln(L(gx, 0, gz));

        public void Tick(float dt)
        {
            if (_destroyed) return;
            _t += dt;
            Kamera(false);
            if (_ghost != null) _ghost.localPosition = new Vector3(_ghost.localPosition.x, 0.35f + Mathf.Sin(_t * 4f) * 0.12f, _ghost.localPosition.z);
            if (_auswahl != null) _auswahl.localScale = new Vector3(1f, 1f + Mathf.Sin(_t * 6f) * 0.5f, 1f);
            for (var i = _pops.Count - 1; i >= 0; i--)
            {
                var (t, time) = _pops[i];
                if (t == null) { _pops.RemoveAt(i); continue; }
                time += dt;
                var k = Mathf.Clamp01(time / 0.45f);
                // ease out back: overshoots a touch, then settles
                const float c1 = 1.9f;
                var s = 1f + (c1 + 1f) * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
                t.localScale = Vector3.one * Mathf.Max(0.05f, s);
                if (k >= 1f) { t.localScale = Vector3.one; _pops.RemoveAt(i); }
                else _pops[i] = (t, time);
            }
            for (var i = _puffs.Count - 1; i >= 0; i--)
            {
                var (t, vel, life, max, scale) = _puffs[i];
                life += dt;
                if (t == null || life >= max)
                {
                    if (t != null) UnityEngine.Object.Destroy(t.gameObject);
                    _puffs.RemoveAt(i);
                    continue;
                }
                t.position += vel * dt;
                vel *= Mathf.Exp(-3f * dt);
                t.localScale = Vector3.one * scale * (1f - life / max) * (1f + life);
                _puffs[i] = (t, vel, life, max, scale);
            }
        }
    }
}
