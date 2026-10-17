using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The map of the open ranch region (port of ranch_karte.gd, data from minigames/ranch/ranch_karte.json — the Godot
    /// file 1:1): sixteen zones with their rects, moods and spawn points, the paths between them, the brook with its ford
    /// and bridge, the gorge with the suspension bridge, puddles. Coordinates are region metres (x east, z south), the farm
    /// zone at the origin; the ground height comes from <see cref="RanchGelaende"/>.
    /// </summary>
    public static class RanchKarte
    {
        public const string Pfad = "minigames/ranch/ranch_karte";
        /// <summary>Around the ford and the bridge the brook can be crossed.</summary>
        public const double QuerungRadiusM = 14.0;

        public sealed class Zone
        {
            public string Id, NameKey, Stimmung;
            public Rect Rect;
            public V2d Spawn;
            public double HoeheBasis;
            public JObject Daten;
            public V2d Punkt(string key) => Daten?[key] is JArray a && a.Count >= 2 ? new V2d(J.Num(a[0]), J.Num(a[1])) : Rect.Mitte;
            public double Zahl(string key, double fallback = 0.0) => J.NumOr(Daten?[key], fallback);
        }

        public sealed class Weg
        {
            public string Id, Von, Nach;
            public double Breite;
            public List<V2d> Punkte;
        }

        public readonly struct Rect
        {
            public readonly double X, Z, W, D;
            public Rect(double x, double z, double w, double d) { X = x; Z = z; W = w; D = d; }
            public double EndX => X + W;
            public double EndZ => Z + D;
            public V2d Mitte => new V2d(X + W * 0.5, Z + D * 0.5);
            public bool Enthaelt(double x, double z) => x >= X && x < EndX && z >= Z && z < EndZ;
            public bool Enthaelt(Rect r) => r.X >= X && r.Z >= Z && r.EndX <= EndX && r.EndZ <= EndZ;
            public bool Schneidet(Rect r) => r.X < EndX && r.EndX > X && r.Z < EndZ && r.EndZ > Z;
            /// <summary>Distance of a point to the rect (0 inside).</summary>
            public double Abstand(double x, double z)
            {
                var dx = Math.Max(Math.Max(X - x, x - EndX), 0.0);
                var dz = Math.Max(Math.Max(Z - z, z - EndZ), 0.0);
                return Math.Sqrt(dx * dx + dz * dz);
            }
        }

        private static JObject _daten;
        private static List<Zone> _zonen;
        private static List<Weg> _wege;

        public static JObject Daten => _daten ?? (_daten = ContentSource.LoadObject(Pfad));

        /// <summary>Tests: reload the map (and the height caches that read it).</summary>
        public static void ResetCache()
        {
            _daten = null;
            _zonen = null;
            _wege = null;
            RanchGelaende.ResetCache();
        }

        public static long SeedWert => (long)J.NumOr(Daten["seed"], RanchWeltState.KartenSeed);

        public static Rect Grenzen
        {
            get
            {
                var g = J.Dict(Daten["grenzen"]);
                var minX = J.NumOr(g["min_x"], -1000.0);
                var minZ = J.NumOr(g["min_z"], -1300.0);
                return new Rect(minX, minZ, J.NumOr(g["max_x"], 1000.0) - minX, J.NumOr(g["max_z"], 980.0) - minZ);
            }
        }

        public static IReadOnlyList<Zone> Zonen
        {
            get
            {
                if (_zonen != null) return _zonen;
                _zonen = new List<Zone>();
                foreach (var z in (Daten["zonen"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var r = z["rect"] as JArray;
                    var s = z["spawn"] as JArray;
                    if (r == null || r.Count < 4) continue;
                    _zonen.Add(new Zone
                    {
                        Id = J.Str(z["id"]),
                        NameKey = J.Str(z["name_key"]),
                        Stimmung = J.Str(z["stimmung"]),
                        Rect = new Rect(J.Num(r[0]), J.Num(r[1]), J.Num(r[2]), J.Num(r[3])),
                        Spawn = s != null && s.Count >= 2 ? new V2d(J.Num(s[0]), J.Num(s[1])) : new V2d(J.Num(r[0]) + J.Num(r[2]) * 0.5, J.Num(r[1]) + J.Num(r[3]) * 0.5),
                        HoeheBasis = J.NumOr(z["hoehe_basis"], 0.0),
                        Daten = z,
                    });
                }
                return _zonen;
            }
        }

        public static IReadOnlyList<Weg> Wege
        {
            get
            {
                if (_wege != null) return _wege;
                _wege = new List<Weg>();
                foreach (var w in (Daten["wege"] as JArray ?? new JArray()).OfType<JObject>())
                    _wege.Add(new Weg
                    {
                        Id = J.Str(w["id"]),
                        Von = J.Str(w["von"]),
                        Nach = J.Str(w["nach"]),
                        Breite = J.NumOr(w["breite"], 4.0),
                        Punkte = Punkte(w["punkte"]),
                    });
                return _wege;
            }
        }

        public static List<string> ZonenIds() => Zonen.Select(z => z.Id).ToList();

        public static Zone ZoneVon(string id) => Zonen.FirstOrDefault(z => z.Id == id);

        /// <summary>The zone at a position ("" = the green land between the zones).</summary>
        public static string ZoneBei(double x, double z)
        {
            foreach (var zone in Zonen)
                if (zone.Rect.Enthaelt(x, z)) return zone.Id;
            return "";
        }

        /// <summary>A zone's spawn point on the ground.</summary>
        public static V3d SpawnPunkt(string zoneId)
        {
            var zone = ZoneVon(zoneId);
            return zone == null ? new V3d(0, 0, 0) : Punkt(zone.Spawn.X, zone.Spawn.Z);
        }

        /// <summary>Directly connected zones.</summary>
        public static List<string> Nachbarn(string zoneId)
        {
            var out_ = new List<string>();
            foreach (var w in Wege)
            {
                if (w.Von == zoneId) out_.Add(w.Nach);
                else if (w.Nach == zoneId) out_.Add(w.Von);
            }
            return out_;
        }

        /// <summary>Waypoints between two zones in riding direction (the reverse way too); empty without a direct path.</summary>
        public static List<V2d> Wegpunkte(string von, string nach)
        {
            foreach (var w in Wege)
            {
                if (w.Von == von && w.Nach == nach) return new List<V2d>(w.Punkte);
                if (w.Von == nach && w.Nach == von) { var r = new List<V2d>(w.Punkte); r.Reverse(); return r; }
            }
            return new List<V2d>();
        }

        public static JObject Bach => J.Dict(Daten["bach"]);

        public static List<V2d> BachPunkte => Punkte(Bach["punkte"]);

        public static V2d Furt => Punkt2(Bach["furt"]);

        public static V2d Bruecke => Punkt2(Bach["bruecke"]);

        public static List<V2d> Pfuetzen => Punkte(Daten["pfuetzen"]);

        public static V3d Punkt(double x, double z) => new V3d(x, RanchGelaende.Hoehe(x, z), z);

        /// <summary>
        /// Can a rider stand here? Blocked: outside the bounds and deep water (lake, bay, brook, mountain lake) — except
        /// within <see cref="QuerungRadiusM"/> of the ford and the bridge.
        /// </summary>
        public static bool IstBegehbar(double x, double z)
        {
            if (!Grenzen.Enthaelt(x, z)) return false;
            if (!RanchGelaende.IstWasser(x, z)) return true;
            foreach (var q in new[] { Furt, Bruecke })
                if (q.Abstand(x, z) <= QuerungRadiusM) return true;
            return false;
        }

        /// <summary>Map integrity (empty = fine): required zones, disjoint rects in the bounds, walkable spawns and paths.</summary>
        public static List<string> Probleme()
        {
            var out_ = new List<string>();
            foreach (var pflicht in new[] { "hof", "weidetal", "waeldchen", "see", "huegelkamm", "bachlauf", "scheune_alt", "turnierplatz", "hufingen" })
                if (ZoneVon(pflicht) == null) out_.Add("Pflichtzone fehlt: " + pflicht);
            var welt = Grenzen;
            var gesehen = new List<Rect>();
            foreach (var z in Zonen)
            {
                if (!welt.Enthaelt(z.Rect)) out_.Add("Zone " + z.Id + " ragt aus der Welt");
                if (gesehen.Any(r => r.Schneidet(z.Rect))) out_.Add("Zone " + z.Id + " überlappt eine andere");
                gesehen.Add(z.Rect);
                if (!z.Rect.Enthaelt(z.Spawn.X, z.Spawn.Z)) out_.Add("Spawn von " + z.Id + " liegt außerhalb der Zone");
                if (!IstBegehbar(z.Spawn.X, z.Spawn.Z)) out_.Add("Spawn von " + z.Id + " ist nicht begehbar");
            }
            foreach (var w in Wege)
            {
                var von = ZoneVon(w.Von);
                var nach = ZoneVon(w.Nach);
                if (von == null || nach == null) { out_.Add("Weg " + w.Id + " verbindet unbekannte Zonen"); continue; }
                if (w.Punkte.Count < 2) { out_.Add("Weg " + w.Id + " hat zu wenige Punkte"); continue; }
                if (!von.Rect.Enthaelt(w.Punkte[0].X, w.Punkte[0].Z)) out_.Add("Weg " + w.Id + " startet nicht in " + w.Von);
                var letzter = w.Punkte[w.Punkte.Count - 1];
                if (!nach.Rect.Enthaelt(letzter.X, letzter.Z)) out_.Add("Weg " + w.Id + " endet nicht in " + w.Nach);
                foreach (var p in w.Punkte)
                    if (!IstBegehbar(p.X, p.Z)) out_.Add("Wegpunkt (" + Math.Round(p.X) + ", " + Math.Round(p.Z) + ") auf " + w.Id + " blockiert");
            }
            return out_;
        }

        public static List<V2d> Punkte(JToken roh)
        {
            var out_ = new List<V2d>();
            foreach (var p in (roh as JArray ?? new JArray()).OfType<JArray>())
                if (p.Count >= 2) out_.Add(new V2d(J.Num(p[0]), J.Num(p[1])));
            return out_;
        }

        private static V2d Punkt2(JToken roh) => roh is JArray a && a.Count >= 2 ? new V2d(J.Num(a[0]), J.Num(a[1])) : new V2d(0, 0);
    }

    /// <summary>A region point (x east, z south) in double precision.</summary>
    public readonly struct V2d
    {
        public readonly double X, Z;
        public V2d(double x, double z) { X = x; Z = z; }
        public double Abstand(double x, double z) => Math.Sqrt((X - x) * (X - x) + (Z - z) * (Z - z));
        public double Abstand(V2d o) => Abstand(o.X, o.Z);
        public static V2d operator +(V2d a, V2d b) => new V2d(a.X + b.X, a.Z + b.Z);
        public static V2d operator -(V2d a, V2d b) => new V2d(a.X - b.X, a.Z - b.Z);
        public static V2d operator *(V2d a, double k) => new V2d(a.X * k, a.Z * k);
        public override string ToString() => "(" + Math.Round(X, 1) + ", " + Math.Round(Z, 1) + ")";
    }

    /// <summary>A region point with its ground height.</summary>
    public readonly struct V3d
    {
        public readonly double X, Y, Z;
        public V3d(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
}
