using System;
using System.Collections.Generic;
using System.IO;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The region's dressing (the Unity cut of ranch_zonen_deko.gd, ranch_neue_zonen.gd and ranch_fundorte_bau.gd): every
    /// zone gets its look from the ranch, scenery and minigame models — the farm with barn, stable, silo, water tower,
    /// windmill, paddock and animals; the wild herd in the Weidetal; the little forest with its clearing; reeds, a jetty and
    /// a boat at the lake; rocks and pines on the ridge; the old barn; the tournament arena with fence, jumps and flags;
    /// Hufingen's houses; the mountain's pines, rocks and the swaying suspension bridge; lavender rows; the moor's dead trees;
    /// the ruined tower; the beach; the apple orchard; the corn field with its scarecrow — and the sixteen discoveries as
    /// landmarks that glitter until found. Zone groups sleep beyond <see cref="StreamAbstand"/>.
    /// </summary>
    public sealed partial class RanchWelt3D
    {
        private readonly Dictionary<string, Transform> _gruppen = new Dictionary<string, Transform>();
        private Transform _koppel;
        private readonly Dictionary<string, (Transform marke, ParticleSystem glitzer)> _funde = new Dictionary<string, (Transform, ParticleSystem)>();
        private readonly List<(RanchHorse3D pferd, V2d heim, float phase)> _herde = new List<(RanchHorse3D, V2d, float)>();
        private readonly List<Transform> _irrlichter = new List<Transform>();
        private float _streamIn, _t;

        private static Material M(string hex, float smooth = 0.2f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);
        private static string Ranch(string n) => Path.Combine(AssetLoader.Root, "ranch", n + ".glb");
        private static string Mini(string n) => Path.Combine(AssetLoader.Root, "minigame", n + ".glb");

        private Vector3 Boden(double x, double z, double dy = 0.0) => _root.position + L(x, RanchGelaende.Hoehe(x, z) + dy, z);

        private Transform Gruppe(string zone)
        {
            if (_gruppen.TryGetValue(zone, out var g)) return g;
            g = new GameObject("Zone:" + zone).transform;
            g.SetParent(_root, false);
            _gruppen[zone] = g;
            return g;
        }

        private Transform Glb(Transform parent, string path, double x, double z, float scale, float yaw, double dy = 0.0)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var anchor = new GameObject("Glb:" + name).transform;
            anchor.SetParent(parent, false);
            anchor.position = Boden(x, z, dy);
            anchor.rotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.localScale = Vector3.one * scale;
            _ = FillAsync(anchor, path, name);
            return anchor;
        }

        private static async System.Threading.Tasks.Task FillAsync(Transform anchor, string path, string name)
        {
            var clone = await AssetLoader.InstanceAsync(path, anchor, name);
            if (clone == null) return;
            if (anchor == null) { UnityEngine.Object.Destroy(clone); return; }
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
        }

        private Transform Szenerie(Transform parent, string archetype, double x, double z, float yaw, float scale) =>
            Scenery.Place(parent, archetype, Boden(x, z) - parent.position, yaw, scale).transform;

        private Transform Getoent(Transform t, string hex, string only = null)
        {
            _ = RanchHofStage.TintWhenLoaded(t, ArtLibrary.Hex(hex), only);
            return t;
        }

        /// <summary>Room for a prop: in the bounds, off the paths and trails, not in water, not on a cliff, clear of the landmarks.</summary>
        private static bool Frei(double x, double z, double rand = 3.0)
        {
            if (!RanchKarte.Grenzen.Enthaelt(x, z)) return false;
            var d = RanchGelaende.WegAbstand(x, z, out var halb);
            if (d < halb + rand) return false;
            foreach (var p in RanchEntdeckungen.AllePfade)
                for (var i = 0; i + 1 < p.Punkte.Length; i++)
                    if (RanchGelaende.AbstandZuStrecke(x, z, p.Punkte[i], p.Punkte[i + 1]) < p.Breite + rand * 0.5) return false;
            foreach (var o in RanchEntdeckungen.AlleOrte)
                if (o.Pos.Abstand(x, z) < 12.0) return false;
            if (RanchGelaende.IstWasser(x, z) || RanchGelaende.Hoehe(x, z) < -0.7) return false;
            var n = RanchGelaende.Normale(x, z);
            return n.y > 0.82;
        }

        /// <summary><paramref name="n"/> props at free random spots of a zone (deterministic per zone).</summary>
        private void Streue(Transform g, RanchKarte.Zone zone, System.Random rng, int n, Action<double, double, System.Random> setze, double rand = 3.0, Func<double, double, bool> extra = null, RanchKarte.Rect? feld = null)
        {
            var r = feld ?? zone.Rect;
            var gesetzt = 0;
            for (var versuch = 0; versuch < n * 6 && gesetzt < n; versuch++)
            {
                var x = r.X + rng.NextDouble() * r.W;
                var z = r.Z + rng.NextDouble() * r.D;
                if (!Frei(x, z, rand) || (extra != null && !extra(x, z))) continue;
                setze(x, z, rng);
                gesetzt++;
            }
        }

        private static string Pick(System.Random rng, params string[] namen) => namen[rng.Next(namen.Length)];

        private void BaueDeko()
        {
            foreach (var zone in RanchKarte.Zonen)
            {
                var g = Gruppe(zone.Id);
                var rng = new System.Random((int)(RanchKarte.SeedWert % 100000) + zone.Id.GetHashCode() % 977);
                switch (zone.Id)
                {
                    case "hof": Hof(g, zone, rng); break;
                    case "weidetal": Weidetal(g, zone, rng); break;
                    case "waeldchen": Waeldchen(g, zone, rng); break;
                    case "see": See(g, zone, rng); break;
                    case "huegelkamm": Kamm(g, zone, rng); break;
                    case "bachlauf": Bachlauf(g, zone, rng); break;
                    case "scheune_alt": AlteScheune(g, zone, rng); break;
                    case "turnierplatz": Turnierplatz(g, zone, rng); break;
                    case "hufingen": Hufingen(g, zone, rng); break;
                    case "bergmassiv": Berg(g, zone, rng); break;
                    case "blumenwiese": Blumen(g, zone, rng); break;
                    case "moor": Moor(g, zone, rng); break;
                    case "ruine": Ruine(g, zone, rng); break;
                    case "strand": Strand(g, zone, rng); break;
                    case "obstgarten": Obstgarten(g, zone, rng); break;
                    case "kornfeld": Kornfeld(g, zone, rng); break;
                }
            }
            var rngFrei = new System.Random(4242);
            var grenzen = RanchKarte.Grenzen;
            for (var v = 0; v < 2400; v++)
            {
                var x = grenzen.X + rngFrei.NextDouble() * grenzen.W;
                var z = grenzen.Z + rngFrei.NextDouble() * grenzen.D;
                if (RanchKarte.ZoneBei(x, z).Length > 0 || !Frei(x, z, 4.0)) continue;
                // groves: trees gather where a slow noise says so, the rest of the free land stays open meadow
                var hain = Math.Sin(x * 0.012 + 1.3) * Math.Cos(z * 0.011 - 0.7) + Math.Sin((x - z) * 0.02) * 0.4;
                if (hain < 0.35) continue;
                var nah = RanchEntdeckerKarte.HinweisZone(new V2d(x, z));
                Baum(nah, x, z, 0.8f + (float)rngFrei.NextDouble() * 0.45f, rngFrei.Next(4) switch { 0 => BaumArt.Linde, 1 => BaumArt.Kiefer, _ => BaumArt.Eiche }, rngFrei);
            }
            foreach (var ort in RanchEntdeckungen.AlleOrte) Fundort(ort);
            Bruecken();
        }

        // ── the zones ──

        private void Hof(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            // the farm yard right in front of the ride-out spot (0, 168): barn and stable facing it, the paddock beside
            Glb(g, Ranch("scheune"), -26, 92, 0.95f, 0f);
            Glb(g, Ranch("scheune_offen"), 30, 84, 0.95f, 0f);
            Glb(g, Ranch("silo"), -58, 86, 0.8f, 0f);
            Glb(g, Ranch("wasserturm"), -86, 118, 0.7f, 20f);
            Glb(g, Ranch("windmuehle"), 84, 58, 0.95f, -25f);
            Glb(g, Ranch("huehnerstall"), -58, 132, 1.0f, 35f);
            Glb(g, Ranch("brunnen"), -12, 128, 0.8f, 10f);
            for (var i = 0; i < 6; i++) Glb(g, Ranch("heuballen"), 2 + (i % 3) * 3.2, 104 - (i / 3) * 2.4, 0.75f, 90f + i * 3f, (i / 3) * 0.9);
            // the paddock in its own group: the multiplayer parcours duel's gates lead through it, so it steps aside then
            _koppel = new GameObject("Koppel").transform;
            _koppel.SetParent(g, false);
            Glb(_koppel, Ranch("trog"), 46, 120, 1.4f, 90f);
            for (var k = 0; k < 9; k++)
            {
                Glb(_koppel, Ranch("zaun_holz_a"), 40 + k * 4.2, 112, 1.6f, 0f);
                Glb(_koppel, Ranch("zaun_holz_a"), 40 + k * 4.2, 148, 1.6f, 0f);
            }
            for (var k = 0; k < 9; k++)
            {
                if (k != 4) Glb(_koppel, Ranch("zaun_holz_a"), 38, 114 + k * 4.0, 1.6f, 90f);
                Glb(_koppel, Ranch("zaun_holz_a"), 78, 114 + k * 4.0, 1.6f, 90f);
            }
            foreach (var (x, z, yaw, fell) in new[] { (52.0, 126.0, 120f, "#D9A066"), (66.0, 138.0, 250f, "#F2E9DC") })
            {
                var h = RanchHorse3D.Create(_koppel, "Koppel", ArtLibrary.Hex(fell), ArtLibrary.Hex("#8A5A33"));
                h.transform.position = Boden(x, z);
                h.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                h.Action("fressen-loop", 100000f);
            }
            foreach (var (x, z, art, s) in new[] { (104.0, 112.0, "kuh", 1.4f), (114.0, 126.0, "kuh", 1.4f), (96.0, 142.0, "schaf", 1.3f), (104.0, 150.0, "schaf", 1.3f), (90.0, 156.0, "schaf", 1.3f) })
                Glb(g, Ranch(art), x, z, s, (float)rng.NextDouble() * 360f);
            Szenerie(g, "welcome_sign", 27, 170, 160f, 1.1f);
            Wald("hof", zone.Rect, 46, rng, r => r.Next(3) == 0 ? BaumArt.Linde : BaumArt.Eiche, 0.8f, 1.15f, (x, z) => !(Math.Abs(x) < 130 && z > 30 && z < 190) && !AufBauplatz(x, z), 4.0);
            Streue(g, zone, rng, 30, (x, z, r) => Glb(g, Ranch(Pick(r, "flower_yellowA", "flower_redA", "grass_large")), x, z, 2.2f, (float)r.NextDouble() * 360f));
        }

        private void Weidetal(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var weide = zone.Punkt("wildpferde_weide");
            var felle = new[] { ("#B07A4A", "#5E3B22"), ("#3E3A3A", "#1E1B1B"), ("#E8DCC8", "#B8A88E"), ("#C98A5A", "#F2E3C9") };
            for (var i = 0; i < 4; i++)
            {
                var heim = new V2d(weide.X + Math.Cos(i * 1.7) * 14.0, weide.Z + Math.Sin(i * 1.7) * 10.0);
                var h = RanchHorse3D.Create(g, "Wild", ArtLibrary.Hex(felle[i].Item1), ArtLibrary.Hex(felle[i].Item2));
                h.transform.position = Boden(heim.X, heim.Z);
                h.transform.rotation = Quaternion.Euler(0f, i * 90f + 20f, 0f);
                h.Action("fressen-loop", 100000f);
                _herde.Add((h, heim, i * 1.3f));
            }
            Wald("weidetal", zone.Rect, 34, rng, r => r.Next(2) == 0 ? BaumArt.Linde : BaumArt.Eiche, 0.8f, 1.2f, (x, z) => weide.Abstand(x, z) > 45, 4.0);
            Streue(g, zone, rng, 60, (x, z, r) => Glb(g, Ranch(Pick(r, "flower_purpleA", "flower_yellowA", "flower_redA", "grass_large")), x, z, 2.4f, (float)r.NextDouble() * 360f), 1.5);
            Streue(g, zone, rng, 8, (x, z, r) => Glb(g, Ranch("rock_largeA"), x, z, 2.2f + (float)r.NextDouble() * 1.6f, (float)r.NextDouble() * 360f));
        }

        private void Waeldchen(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var lichtung = zone.Punkt("lichtung");
            Wald("waeldchen", zone.Rect, 430, rng, r => r.Next(5) switch { 0 => BaumArt.Kiefer, 1 => BaumArt.Linde, _ => BaumArt.Eiche }, 0.85f, 1.35f, (x, z) => lichtung.Abstand(x, z) > 34, 2.5);
            Streue(g, zone, rng, 24, (x, z, r) =>
            {
                Getoent(Glb(g, Ranch("tree_oak"), x, z, 3.8f + (float)r.NextDouble() * 1.4f, (float)r.NextDouble() * 360f), "#6FAE5B", "leaf");
                Stamm(x, z, 0.5f);
            }, 3.0, (x, z) => RanchGelaende.WegAbstand(x, z, out var halb) < halb + 9);
            Streue(g, zone, rng, 30, (x, z, r) => Glb(g, Ranch(Pick(r, "plant_bushLarge", "plant_bush")), x, z, 3.0f, (float)r.NextDouble() * 360f), 2.0);
            Streue(g, zone, rng, 8, (x, z, r) => Glb(g, Ranch(Pick(r, "log", "stump_round")), x, z, 2.6f, (float)r.NextDouble() * 360f));
            for (var i = 0; i < 12; i++)
            {
                var a = i * 0.52;
                Glb(g, Mini(i % 2 == 0 ? "mushroom_red" : "mushroom"), lichtung.X + Math.Cos(a) * (10 + i % 4 * 4), lichtung.Z + Math.Sin(a) * (10 + i % 3 * 5), 2.4f, i * 37f);
            }
        }

        private void See(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var mitte = zone.Punkt("see_mitte");
            var steg = zone.Punkt("steg");
            // the jetty: planks from the shore toward the middle of the lake
            var dx = mitte.X - steg.X;
            var dz = mitte.Z - steg.Z;
            var len = Math.Sqrt(dx * dx + dz * dz);
            dx /= len;
            dz /= len;
            var yaw = Mathf.Atan2((float)dx, (float)-dz) * Mathf.Rad2Deg;
            var holz = M("#B98A5E", 0.15f);
            var stegRoot = new GameObject("Steg").transform;
            stegRoot.SetParent(g, false);
            for (var i = 0; i < 14; i++)
            {
                var x = steg.X + dx * i * 2.0;
                var z = steg.Z + dz * i * 2.0;
                var p = _root.position + L(x, Math.Max(RanchGelaende.Hoehe(x, z), RanchGelaende.WasserHoehe) + 0.45, z);
                var plank = ArtLibrary.Box(stegRoot, "Planke", p, new Vector3(3.2f, 0.18f, 1.8f), holz);
                plank.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                if (i % 3 == 0)
                    foreach (var s in new[] { -1.5, 1.5 })
                        ArtLibrary.Cylinder(stegRoot, "Pfahl", p + Quaternion.Euler(0f, yaw, 0f) * new Vector3((float)s, -0.5f, 0f), 0.12f, 1.4f, holz);
            }
            Glb(g, Mini("boat-row-small"), steg.X + dx * 30 + dz * 6, steg.Z + dz * 30 - dx * 6, 3.0f, yaw + 70f, RanchGelaende.WasserHoehe - RanchGelaende.Hoehe(steg.X + dx * 30 + dz * 6, steg.Z + dz * 30 - dx * 6) + 0.05);
            // reeds round the shore
            var r0 = zone.Zahl("see_radius", 85.0);
            for (var i = 0; i < 36; i++)
            {
                var a = i * Math.PI * 2.0 / 36.0 + rng.NextDouble() * 0.1;
                var rr = r0 * (1.02 + rng.NextDouble() * 0.12);
                var x = mitte.X + Math.Cos(a) * rr;
                var z = mitte.Z + Math.Sin(a) * rr;
                if (RanchGelaende.WegAbstand(x, z, out var halb) < halb + 2 || steg.Abstand(x, z) < 8) continue;
                Getoent(Glb(g, Ranch("grass_large"), x, z, 3.4f, (float)rng.NextDouble() * 360f), "#6FA35A");
            }
            Wald("see", zone.Rect, 40, rng, r => r.Next(3) == 0 ? BaumArt.Pappel : BaumArt.Eiche, 0.8f, 1.2f, (x, z) => mitte.Abstand(x, z) > r0 * 1.35, 4.0);
        }

        private void Kamm(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            Streue(g, zone, rng, 22, (x, z, r) => Glb(g, Ranch("rock_largeA"), x, z, 2.4f + (float)r.NextDouble() * 2.4f, (float)r.NextDouble() * 360f));
            Wald("huegelkamm", zone.Rect, 80, rng, r => r.Next(4) == 0 ? BaumArt.Eiche : BaumArt.Kiefer, 0.8f, 1.25f, null, 3.0);
            Streue(g, zone, rng, 30, (x, z, r) => Glb(g, Ranch(Pick(r, "flower_yellowA", "grass_large")), x, z, 2.2f, (float)r.NextDouble() * 360f), 1.5);
        }

        private void Bachlauf(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var pts = RanchKarte.BachPunkte;
            for (var i = 0; i < 24; i++)
            {
                var s = rng.Next(pts.Count - 1);
                var t = rng.NextDouble();
                var a = pts[s];
                var b = pts[s + 1];
                var len = a.Abstand(b);
                var nx = -(b.Z - a.Z) / len;
                var nz = (b.X - a.X) / len;
                var side = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
                var x = a.X + (b.X - a.X) * t + nx * side * (6.0 + rng.NextDouble() * 3.0);
                var z = a.Z + (b.Z - a.Z) * t + nz * side * (6.0 + rng.NextDouble() * 3.0);
                if (!zone.Rect.Enthaelt(x, z) || RanchGelaende.WegAbstand(x, z, out var halb) < halb + 2) continue;
                Glb(g, Ranch("rock_largeA"), x, z, 1.4f + (float)rng.NextDouble() * 1.4f, (float)rng.NextDouble() * 360f);
            }
            Streue(g, zone, rng, 16, (x, z, r) => Glb(g, Ranch(Pick(r, "plant_bushLarge", "plant_bush")), x, z, 3.0f, (float)r.NextDouble() * 360f), 3.0, (x, z) => RanchGelaende.BachKerbe(x, z) <= 0.0);
            Wald("bachlauf", zone.Rect, 60, rng, r => r.Next(2) == 0 ? BaumArt.Linde : BaumArt.Eiche, 0.8f, 1.2f, (x, z) => RanchGelaende.BachKerbe(x, z) <= 0.0, 3.0);
        }

        private void AlteScheune(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var s = zone.Punkt("scheune");
            Getoent(Glb(g, Ranch("scheune"), s.X, s.Z, 0.95f, 32f), "#9C8C7E");
            for (var i = 0; i < 7; i++)
                if (i != 3) Glb(g, Ranch("zaun_holz_a"), s.X - 16 + i * 4.2, s.Z + 22, 1.6f, i % 2 == 0 ? 4f : -9f);
            Streue(g, zone, rng, 6, (x, z, r) => Glb(g, Ranch("stump_round"), x, z, 2.4f, (float)r.NextDouble() * 360f));
            Streue(g, zone, rng, 40, (x, z, r) => Getoent(Glb(g, Ranch("grass_large"), x, z, 3.2f, (float)r.NextDouble() * 360f), "#9DB46A"), 1.5);
            Wald("scheune_alt", zone.Rect, 30, rng, r => r.Next(3) == 0 ? BaumArt.Kahl : BaumArt.Linde, 0.8f, 1.15f, (x, z) => s.Abstand(x, z) > 30, 4.0);
        }

        private void Turnierplatz(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var a = zone.Daten?["arena"] as Newtonsoft.Json.Linq.JArray;
            var cx = a != null ? (double)a[0] : 130.0;
            var cz = a != null ? (double)a[1] : 470.0;
            var w = a != null ? (double)a[2] : 96.0;
            var d = a != null ? (double)a[3] : 60.0;
            var weiss = M("#F4EAD5", 0.2f);
            for (var x = -w / 2; x <= w / 2 + 0.1; x += 4.0)
                foreach (var z in new[] { -d / 2, d / 2 })
                    if (!(z < 0 && Math.Abs(x) < 6)) Glb(g, Ranch("fence_simple"), cx + x, cz + z, 2.0f, 0f);
            for (var z = -d / 2; z <= d / 2 + 0.1; z += 4.0)
                foreach (var x in new[] { -w / 2, w / 2 })
                    Glb(g, Ranch("fence_simple"), cx + x, cz + z, 2.0f, 90f);
            var hinder = new[] { "hindernis_a", "hindernis_b", "hindernis_c" };
            for (var i = 0; i < 5; i++) Glb(g, Ranch(hinder[i % 3]), cx - 30 + i * 15, cz + (i % 2 == 0 ? -8 : 9), 1.6f, 90f);
            for (var i = 0; i < 10; i++)
            {
                var x = cx - w / 2 + i * w / 9.0;
                var z = cz - d / 2 - 4;
                ArtLibrary.Cylinder(g, "Mast", Boden(x, z, 2.5), 0.08f, 5f, weiss);
                Glb(g, Mini("flag-red"), x, z, 2.2f, 0f, 4.2);
            }
            for (var i = 0; i < 4; i++) Glb(g, Mini("bench"), cx - 36 + i * 24, cz + d / 2 + 6, 3.2f, 180f);
            Wald("turnierplatz", zone.Rect, 30, rng, r => r.Next(3) == 0 ? BaumArt.Pappel : BaumArt.Eiche, 0.8f, 1.15f,
                (x, z) => Math.Abs(x - cx) > w / 2 + 14 || Math.Abs(z - cz) > d / 2 + 16, 4.0);
        }

        private void Hufingen(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var ende = zone.Punkt("strasse_ende");
            var platz = new V2d(500, 522);
            Glb(g, Ranch("brunnen"), platz.X, platz.Z, 1.3f, 20f);
            var haeuser = new (string glb, double x, double z, float s, float yaw)[]
            {
                ("scheune_klein", 470, 480, 0.85f, 15f), ("silo_haus", 512, 474, 1.0f, -10f), ("scheune_offen", 552, 488, 0.9f, -30f),
                ("scheune", 470, 568, 0.7f, 175f), ("scheune_gross", 530, 574, 0.72f, 190f), ("windmuehle_turm", 600, 546, 0.8f, -60f),
            };
            foreach (var hs in haeuser) Glb(g, Ranch(hs.glb), hs.x, hs.z, hs.s, hs.yaw);
            foreach (var (x, z, c) in new[] { (486.0, 500.0, "cottage_pink"), (540.0, 510.0, "cottage_green"), (586.0, 512.0, "cottage_blue") })
                Szenerie(g, c, x, z, 200f, 1.0f);
            Szenerie(g, "market_stall_red", 488, 536, 170f, 1.0f);
            Szenerie(g, "market_stall_yellow", 514, 540, 190f, 1.0f);
            for (var i = 0; i < 6; i++) Szenerie(g, "lamp_post", 452 + i * 22, 524 + (i % 2) * 10, 0f, 1.0f);
            Szenerie(g, "welcome_sign", 430, 492, 200f, 1.1f);
            Wald("hufingen", zone.Rect, 30, rng, r => r.Next(2) == 0 ? BaumArt.Linde : BaumArt.Eiche, 0.8f, 1.15f,
                (x, z) => platz.Abstand(x, z) > 75 && ende.Abstand(x, z) > 25, 4.0);
        }

        private void Berg(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            Wald("bergmassiv", zone.Rect, 240, rng, _ => BaumArt.Kiefer, 0.85f, 1.4f, (x, z) => RanchGelaende.Hoehe(x, z) < 66.0, 3.0);
            Streue(g, zone, rng, 34, (x, z, r) => Getoent(Glb(g, Ranch("rock_largeA"), x, z, 2.6f + (float)r.NextDouble() * 3.4f, (float)r.NextDouble() * 360f), "#A7A49C"), 2.0);
        }

        private void Blumen(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var lav = zone.Daten?["lavendel_rect"] as Newtonsoft.Json.Linq.JArray;
            var feld = lav != null ? new RanchKarte.Rect((double)lav[0], (double)lav[1], (double)lav[2], (double)lav[3]) : zone.Rect;
            for (var x = feld.X + 6; x < feld.EndX - 4; x += 7.0)
                for (var z = feld.Z + 6; z < feld.EndZ - 4; z += 16.0)
                {
                    var zz = z + rng.NextDouble() * 6;
                    if (!Frei(x, zz, 1.0)) continue;
                    Glb(g, Ranch("flower_purpleA"), x, zz, 3.0f + (float)rng.NextDouble(), (float)rng.NextDouble() * 360f);
                }
            Wald("blumenwiese", zone.Rect, 24, rng, _ => BaumArt.Pappel, 0.85f, 1.2f, (x, z) => !feld.Enthaelt(x, z), 4.0);
        }

        private void Moor(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            Wald("moor", zone.Rect, 34, rng, _ => BaumArt.Kahl, 0.7f, 1.1f, null, 3.0);
            Streue(g, zone, rng, 6, (x, z, r) => Glb(g, Ranch("stump_round"), x, z, 2.4f, (float)r.NextDouble() * 360f), 3.0);
            Streue(g, zone, rng, 50, (x, z, r) => Getoent(Glb(g, Ranch("grass_large"), x, z, 3.0f, (float)r.NextDouble() * 360f), "#7E9561"), 1.0);
            Streue(g, zone, rng, 5, (x, z, r) => Glb(g, Ranch("log"), x, z, 2.6f, (float)r.NextDouble() * 360f));
        }

        private void Ruine(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var turm = zone.Punkt("turm");
            var stein = M("#B8B0A2", 0.1f);
            var dunkel = M("#9C9486", 0.1f);
            var root = new GameObject("Turm").transform;
            root.SetParent(g, false);
            var y0 = RanchGelaende.Hoehe(turm.X, turm.Z);
            for (var ring = 0; ring < 7; ring++)
            {
                var n = 14 - ring / 3;
                for (var i = 0; i < n; i++)
                {
                    if (ring >= 4 && (i * 7 + ring * 3) % 5 < 2) continue;
                    var a = (float)((i + ring * 0.5) * Mathf.PI * 2f / n);
                    var p = _root.position + L(turm.X + Math.Cos(a) * 5.2, y0 + 0.9 + ring * 1.7, turm.Z + Math.Sin(a) * 5.2);
                    var b = ArtLibrary.Box(root, "Stein", p, new Vector3(2.5f, 1.6f, 1.4f), (i + ring) % 3 == 0 ? dunkel : stein);
                    b.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                }
            }
            for (var i = 0; i < 6; i++)
            {
                var a = i * 1.05 + 0.4;
                var x = turm.X + Math.Cos(a) * (18 + i * 3);
                var z = turm.Z + Math.Sin(a) * (18 + i * 3);
                var h = 1.2 + (i % 3) * 0.8;
                var mauer = ArtLibrary.Box(root, "Mauer", Boden(x, z, h / 2), new Vector3(5f, (float)h, 1.1f), stein);
                mauer.transform.rotation = Quaternion.Euler(0f, (float)(a * Mathf.Rad2Deg) + 90f, 0f);
            }
            Streue(g, zone, rng, 16, (x, z, r) => Getoent(Glb(g, Ranch("rock_largeA"), x, z, 2.0f + (float)r.NextDouble() * 2.6f, (float)r.NextDouble() * 360f), "#ABA69B"), 2.0, (x, z) => turm.Abstand(x, z) > 10);
            Streue(g, zone, rng, 30, (x, z, r) => Glb(g, Ranch(Pick(r, "grass_large", "flower_yellowA")), x, z, 2.4f, (float)r.NextDouble() * 360f), 1.5);
            Wald("ruine", zone.Rect, 30, rng, r => r.Next(2) == 0 ? BaumArt.Kiefer : BaumArt.Eiche, 0.8f, 1.2f, (x, z) => turm.Abstand(x, z) > 30, 3.0);
        }

        private void Strand(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var bucht = zone.Punkt("bucht_mitte");
            var r0 = zone.Zahl("bucht_radius", 85.0);
            Glb(g, Mini("boat-fishing-small"), bucht.X - 20, bucht.Z - 10, 3.0f, 30f, RanchGelaende.WasserHoehe - RanchGelaende.Hoehe(bucht.X - 20, bucht.Z - 10) + 0.05);
            for (var i = 0; i < 4; i++)
            {
                var a = i * 1.4 + 0.3;
                var x = bucht.X + Math.Cos(a) * r0 * 0.6;
                var z = bucht.Z + Math.Sin(a) * r0 * 0.6;
                Glb(g, Mini(i % 2 == 0 ? "buoy-flag" : "buoy"), x, z, 2.4f, i * 50f, RanchGelaende.WasserHoehe - RanchGelaende.Hoehe(x, z));
            }
            Streue(g, zone, rng, 8, (x, z, r) => Glb(g, Ranch("rock_largeA"), x, z, 1.8f + (float)r.NextDouble() * 2f, (float)r.NextDouble() * 360f), 2.0, (x, z) => bucht.Abstand(x, z) > r0 * 1.05);
            Streue(g, zone, rng, 5, (x, z, r) => Glb(g, Ranch("log"), x, z, 2.4f, (float)r.NextDouble() * 360f), 2.0, (x, z) => bucht.Abstand(x, z) > r0 * 1.1);
            Wald("strand", zone.Rect, 22, rng, _ => BaumArt.Pappel, 0.85f, 1.2f, (x, z) => bucht.Abstand(x, z) > r0 * 1.55, 4.0);
        }

        private void Obstgarten(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var raster = zone.Daten?["baumraster"] as Newtonsoft.Json.Linq.JArray;
            var r = raster != null ? new RanchKarte.Rect((double)raster[0], (double)raster[1], (double)raster[2], (double)raster[3]) : zone.Rect;
            var apfel = M("#E04848", 0.5f);
            for (var x = r.X + 20; x < r.EndX - 10; x += 40.0)
                for (var z = r.Z + 20; z < r.EndZ - 10; z += 36.0)
                {
                    if (!Frei(x, z, 2.0)) continue;
                    Getoent(Glb(g, Ranch("tree_fat"), x, z, 3.2f, (float)(x * 7 % 360)), "#79BE5C", "leaf");
                    for (var k = 0; k < 5; k++)
                    {
                        var a = k * 1.3 + x * 0.1;
                        ArtLibrary.Sphere(g, "Apfel", Boden(x + Math.Cos(a) * 1.6, z + Math.Sin(a) * 1.6, 3.2 + (k % 3) * 0.5), 0.22f, apfel, false);
                    }
                }
            Streue(g, zone, rng, 6, (x, z, rr) => Glb(g, Ranch("heuballen"), x, z, 0.75f, (float)rr.NextDouble() * 360f), 3.0, (x, z) => !r.Enthaelt(x, z));
        }

        private void Kornfeld(Transform g, RanchKarte.Zone zone, System.Random rng)
        {
            var f = zone.Daten?["feld_rect"] as Newtonsoft.Json.Linq.JArray;
            var feld = f != null ? new RanchKarte.Rect((double)f[0], (double)f[1], (double)f[2], (double)f[3]) : zone.Rect;
            for (var i = 0; i < 70; i++)
            {
                var x = feld.X + rng.NextDouble() * feld.W;
                var z = feld.Z + rng.NextDouble() * feld.D;
                if (!Frei(x, z, 1.0) || RanchEntdeckungen.Fundort("kornkreis").Pos.Abstand(x, z) < 20) continue;
                Getoent(Glb(g, Ranch("grass_large"), x, z, 3.6f, (float)rng.NextDouble() * 360f), "#E2BE52");
            }
            // the scarecrow
            var m = feld.Mitte;
            var holz = M("#8C613D");
            var vogel = new GameObject("Vogelscheuche").transform;
            vogel.SetParent(g, false);
            ArtLibrary.Cylinder(vogel, "Pfahl", Boden(m.X + 40, m.Z, 1.4), 0.1f, 2.8f, holz);
            ArtLibrary.Box(vogel, "Arme", Boden(m.X + 40, m.Z, 2.2), new Vector3(2.2f, 0.12f, 0.12f), holz);
            ArtLibrary.Box(vogel, "Hemd", Boden(m.X + 40, m.Z, 1.9), new Vector3(0.9f, 0.9f, 0.4f), M("#5C8CC7"));
            ArtLibrary.Sphere(vogel, "Kopf", Boden(m.X + 40, m.Z, 2.75), 0.32f, M("#F2E3C9"));
            ArtLibrary.Cylinder(vogel, "Hut", Boden(m.X + 40, m.Z, 3.1), 0.55f, 0.08f, M("#E8C860"));
            for (var i = 0; i < 6; i++) Glb(g, Ranch("heuballen"), feld.EndX + 10 + (i % 2) * 3, feld.Z + 20 + i * 4, 0.75f, 90f);
            Wald("kornfeld", zone.Rect, 18, rng, _ => BaumArt.Linde, 0.8f, 1.1f, (x, z) => !feld.Enthaelt(x, z), 4.0);
        }

        // ── the bridges ──

        private void Bruecken()
        {
            var holz = M("#A8794E", 0.15f);
            var seil = M("#7A5A3C", 0.1f);
            foreach (var (a, b, halb) in RanchGelaende.Bruecken())
            {
                var g = Gruppe("bergmassiv");
                var len = a.Abstand(b);
                var n = (int)(len / 1.2);
                var yaw = Mathf.Atan2((float)(b.X - a.X), (float)-(b.Z - a.Z)) * Mathf.Rad2Deg;
                for (var i = 0; i <= n; i++)
                {
                    var t = (double)i / n;
                    var x = a.X + (b.X - a.X) * t;
                    var z = a.Z + (b.Z - a.Z) * t;
                    var y = RanchGelaende.ReitHoehe(x, z) - 0.08;
                    var p = ArtLibrary.Box(g, "Planke", _root.position + L(x, y, z), new Vector3((float)halb * 2f, 0.12f, 0.9f), holz, false);
                    p.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    if (i % 4 == 0)
                        foreach (var s in new[] { -1f, 1f })
                            ArtLibrary.Cylinder(g, "Pfosten", _root.position + L(x, y, z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(s * (float)halb, 0.6f, 0f), 0.07f, 1.2f, seil);
                }
            }
            // the wooden bridge over the brook
            var br = RanchKarte.Bruecke;
            Getoent(Glb(Gruppe("bachlauf"), Mini("bridge_wood"), br.X, br.Z, 3.2f, 80f, 0.2), "#B98A5E");
        }

        // ── the discoveries ──

        private void Fundort(RanchEntdeckungen.Ort ort)
        {
            var zone = RanchKarte.ZoneBei(ort.Pos.X, ort.Pos.Z);
            var g = Gruppe(zone.Length > 0 ? zone : "frei");
            var marke = new GameObject("Fund:" + ort.Id).transform;
            marke.SetParent(g, false);
            marke.position = Boden(ort.Pos.X, ort.Pos.Z);
            var x = ort.Pos.X;
            var z = ort.Pos.Z;
            switch (ort.Id)
            {
                case "wasserfall":
                    for (var i = 0; i < 5; i++) Getoent(Glb(marke, Ranch("rock_largeA"), x - 6 + i * 3, z - 4 - (i % 2) * 2, 4.2f + i % 2, i * 70f), "#A9A59C");
                    ArtLibrary.Box(marke, "Fall", Boden(x, z - 2.5, 4.0), new Vector3(2.6f, 8f, 0.4f), ArtLibrary.Transparent(new Color(0.6f, 0.86f, 0.98f, 0.75f)), false);
                    ArtLibrary.Cylinder(marke, "Becken", Boden(x, z + 0.5, 0.15), 3.2f, 0.1f, M("#7EC8E3", 0.9f), false);
                    break;
                case "hoehle":
                    for (var i = 0; i < 6; i++) Getoent(Glb(marke, Ranch("rock_largeA"), x + Math.Cos(i * 0.6 + 2.2) * 5, z + Math.Sin(i * 0.6 + 2.2) * 4, 5f, i * 50f), "#9E9A92");
                    var mund = ArtLibrary.Sphere(marke, "Eingang", Boden(x, z - 2.6, 1.0), 1.6f, M("#2E2A33", 0.05f), false);
                    mund.transform.localScale = new Vector3(3.4f, 2.6f, 1.2f);
                    break;
                case "alter_baum":
                    Getoent(Glb(marke, Ranch("tree_oak"), x, z, 9.5f, 40f), "#5E9F4E", "leaf");
                    for (var i = 0; i < 4; i++) Glb(marke, Ranch("log"), x + Math.Cos(i * 1.6) * 4.5, z + Math.Sin(i * 1.6) * 4.5, 2.4f, i * 90f + 20f);
                    break;
                case "steinkreis":
                    var hell = M("#B9B4AA", 0.08f);
                    var grau = M("#A39E94", 0.08f);
                    for (var i = 0; i < 9; i++)
                    {
                        var a = i * Mathf.PI * 2f / 9f;
                        var sx = x + Math.Cos(a) * 7.5;
                        var sz = z + Math.Sin(a) * 7.5;
                        var hoch = 2.4f + (i % 3) * 0.45f;
                        var stein = ArtLibrary.Box(marke, "Stein", Boden(sx, sz, hoch * 0.5 - 0.2), new Vector3(1.1f, hoch, 0.75f), i % 2 == 0 ? hell : grau);
                        stein.transform.rotation = Quaternion.Euler((i % 4 - 1.5f) * 2.5f, -a * Mathf.Rad2Deg + 90f, (i % 3 - 1) * 3f);
                        if (i % 3 != 0) continue;
                        // a lintel across to the next stone
                        var b = (i + 1) * Mathf.PI * 2f / 9f;
                        var mx = x + Math.Cos((a + b) * 0.5) * 7.5;
                        var mz = z + Math.Sin((a + b) * 0.5) * 7.5;
                        var deck = ArtLibrary.Box(marke, "Deckstein", Boden(mx, mz, hoch + 0.1), new Vector3(0.8f, 0.5f, 5.6f), hell);
                        deck.transform.rotation = Quaternion.Euler(0f, -((a + b) * 0.5f) * Mathf.Rad2Deg, 0f);
                    }
                    break;
                case "aussicht_kamm":
                case "aussicht_see":
                    Szenerie(marke, "park_bench", x, z, ort.Id == "aussicht_see" ? 120f : 200f, 1.1f);
                    var rohr = ArtLibrary.Cylinder(marke, "Fernrohr", Boden(x + 2.2, z, 1.3), 0.12f, 0.9f, M("#C9A24A", 0.6f), false);
                    rohr.transform.rotation = Quaternion.Euler(70f, 30f, 0f);
                    ArtLibrary.Cylinder(marke, "Stativ", Boden(x + 2.2, z, 0.6), 0.05f, 1.2f, M("#6B4F3A"), false);
                    break;
                case "truhe_wald":
                case "truhe_scheune":
                case "truhe_ufer":
                case "ruine_schatz":
                    Truhe(marke, x, z);
                    break;
                case "gipfelkreuz":
                    var holz = M("#8C613D");
                    ArtLibrary.Cylinder(marke, "Sockel", Boden(x, z, 0.4), 1.2f, 0.8f, M("#A9A59C"), true);
                    ArtLibrary.Box(marke, "Stamm", Boden(x, z, 3.2), new Vector3(0.35f, 5f, 0.35f), holz);
                    ArtLibrary.Box(marke, "Balken", Boden(x, z, 4.4), new Vector3(2.4f, 0.32f, 0.32f), holz);
                    break;
                case "bergsee_perle":
                case "strand_muschel":
                    var muschel = ArtLibrary.Sphere(marke, "Muschel", Boden(x, z, 0.4), ort.Id == "strand_muschel" ? 1.8f : 0.9f, M("#F6C7C1", 0.5f));
                    muschel.transform.localScale = Vector3.Scale(muschel.transform.localScale, new Vector3(1.3f, 0.5f, 1f));
                    ArtLibrary.Sphere(marke, "Perle", Boden(x, z, ort.Id == "strand_muschel" ? 1.4 : 0.8), ort.Id == "strand_muschel" ? 0.55f : 0.32f, ArtLibrary.Glow(new Color(1.2f, 1.18f, 1.12f)), false);
                    break;
                case "moor_irrlicht":
                    var licht = ArtLibrary.Sphere(marke, "Irrlicht", Boden(x, z, 1.8), 0.45f, ArtLibrary.Glow(new Color(0.55f, 0.95f, 1.5f)), false);
                    _irrlichter.Add(licht.transform);
                    var pl = licht.AddComponent<Light>();
                    pl.type = LightType.Point;
                    pl.color = new Color(0.55f, 0.9f, 1f);
                    pl.range = 9f;
                    pl.intensity = 2.2f;
                    break;
                case "lavendel_bienen":
                    for (var i = 0; i < 3; i++)
                    {
                        var bx = x + (i - 1) * 2.4;
                        ArtLibrary.Box(marke, "Kasten", Boden(bx, z, 0.5), new Vector3(1.2f, 1f, 1.1f), M(i == 1 ? "#F2C14E" : "#F4EAD5"));
                        ArtLibrary.Box(marke, "Deckel", Boden(bx, z, 1.08), new Vector3(1.4f, 0.16f, 1.3f), M("#B98A5E"));
                    }
                    break;
                case "kornkreis":
                    for (var r = 3; r <= 12; r += 3)
                    {
                        ArtLibrary.Cylinder(marke, "Ring", Boden(x, z, 0.04 + r * 0.002), r, 0.04f, M(r % 2 == 0 ? "#F3DE8E" : "#D9B44E"), false);
                    }
                    break;
            }
            var glitzer = Effects.Fireflies(marke, new Bounds(marke.position + Vector3.up * 2.2f, new Vector3(9f, 4f, 9f)));
            var main = glitzer.main;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.55f), new Color(1f, 0.75f, 0.9f));
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.26f);
            _funde[ort.Id] = (marke, glitzer);
        }

        private void Truhe(Transform marke, double x, double z)
        {
            var holz = M("#9A6A3E", 0.2f);
            var gold = M("#F2C14E", 0.7f);
            ArtLibrary.Box(marke, "Truhe", Boden(x, z, 0.45), new Vector3(1.5f, 0.9f, 1f), holz);
            ArtLibrary.Box(marke, "Band", Boden(x, z, 0.45), new Vector3(1.56f, 0.18f, 1.06f), gold);
            var deckel = new GameObject("Deckel").transform;
            deckel.SetParent(marke, true);
            deckel.position = Boden(x, z - 0.5, 0.9);
            ArtLibrary.Box(deckel, "DeckelHolz", Boden(x, z, 1.02), new Vector3(1.5f, 0.24f, 1f), holz);
        }

        /// <summary>A place found: its glitter stops in a burst, a chest's lid swings open.</summary>
        public void Gefunden(string id, bool gerade)
        {
            if (!_funde.TryGetValue(id, out var f)) return;
            if (f.glitzer != null) f.glitzer.Stop(true, gerade ? ParticleSystemStopBehavior.StopEmitting : ParticleSystemStopBehavior.StopEmittingAndClear);
            var deckel = f.marke.Find("Deckel");
            if (deckel != null) deckel.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            if (!gerade) return;
            Effects.Sparkle(f.marke.position + Vector3.up * 1.4f, 2.2f, 48);
        }

        private void TickDeko(float dt)
        {
            _t += dt;
            TickSammeln();
            foreach (var l in _irrlichter)
                if (l != null) l.localPosition += Vector3.up * Mathf.Sin(_t * 1.6f) * 0.004f;
            if ((_streamIn -= dt) > 0f) return;
            _streamIn = 0.5f;
            foreach (var kv in _gruppen)
            {
                if (kv.Key == "frei") continue;
                var zone = RanchKarte.ZoneVon(kv.Key);
                if (zone == null) continue;
                var an = zone.Rect.Abstand(_x, _z) < StreamAbstand;
                if (kv.Value.gameObject.activeSelf != an) kv.Value.gameObject.SetActive(an);
            }
        }

        /// <summary>QA: the zone groups awake now.</summary>
        public int AktiveZonen()
        {
            var n = 0;
            foreach (var kv in _gruppen) if (kv.Value.gameObject.activeSelf) n++;
            return n;
        }
    }
}
