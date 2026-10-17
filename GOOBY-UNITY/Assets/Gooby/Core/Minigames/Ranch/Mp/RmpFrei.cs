using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch.Mp
{
    /// <summary>
    /// The free ride of the ranch multiplayer — "Ranch-Besuch" (2) and "Gemeinsamer Ausritt" (up to 4), modes without a
    /// course or match in ranchmp.js (poses and reactions only) — through the open ranch region like Godot
    /// (rmp_ausritt_controller.gd): everyone starts on the yard ring in front of the farm, rides wherever the land carries
    /// a horse, and riding into a zone for the first time is a discovery shared as ENTDECKUNG {zone}. Region metres:
    /// east = +x, north = −z; headings in the ride controller's convention (forward −(sin h, cos h): 0 rides north).
    /// </summary>
    public static class RmpFrei
    {
        /// <summary>The yard ring in front of the farm buildings: riders start on its south side, a visit's horses stand on the north side.</summary>
        public static readonly V2 HofMitte = new V2(8f, 140f);
        public const double HofRingRadius = 18.0;

        /// <summary>The region's bounds (ranch_karte.json "grenzen": x −1000..1000, z −1300..980).</summary>
        public static readonly V2 FeldMitte = new V2(0f, -160f), FeldHalb = new V2(1000f, 1140f);

        /// <summary>The bot's trail: along the paths from the yard into these zones (one after the other), then home.</summary>
        public static readonly string[] BotZiele = { "turnierplatz" };

        /// <summary>The zone at a region point ("" between the zones, or without the map).</summary>
        public static string OrtBei(V2 pos) => ContentSource.Installed ? RanchKarte.ZoneBei(pos.X, pos.Y) : "";

        /// <summary>Does the land carry a horse here (no deep water, no cliff)? Without the map everything does.</summary>
        public static bool Begehbar(V2 pos) => !ContentSource.Installed || RanchKarte.IstBegehbar(pos.X, pos.Y);

        /// <summary>Start places side by side on the south side of the yard ring, looking north across it to the farm.</summary>
        public static V2 StartPosition(int platz, int anzahl)
        {
            var n = Math.Max(1, anzahl);
            var x = (platz - (n - 1) * 0.5) * 6.0;
            return new V2(HofMitte.X + x, HofMitte.Y + HofRingRadius * 0.55);
        }

        public const double StartHeading = 0.0;

        /// <summary>
        /// The host's horses on a visit: the first six on an inner arc in the north half of the yard ring — in view of the
        /// riders starting on the south side — the others on an outer arc behind them.
        /// </summary>
        public static V2 BesuchPferdPos(int i, int anzahl)
        {
            const int ErsteReihe = 6;
            var aussen = i >= ErsteReihe;
            var inReihe = aussen ? anzahl - ErsteReihe : Math.Min(ErsteReihe, anzahl);
            var k = aussen ? i - ErsteReihe : i;
            var t = inReihe <= 1 ? 0.5 : (double)k / (inReihe - 1);
            var a = Math.PI * (1.18 + 0.64 * t);
            return HofMitte + new V2(Math.Cos(a), Math.Sin(a)) * (aussen ? HofRingRadius - 4.0 : 9.0);
        }

        /// <summary>The trail from the yard ring along the region's path into a zone (its spawn at the end).</summary>
        public static List<V2> BotRoute(string zone)
        {
            var route = new List<V2> { HofMitte };
            if (!ContentSource.Installed) return route;
            foreach (var p in RanchKarte.Wegpunkte("hof", zone)) route.Add(new V2(p.X, p.Z));
            var z = RanchKarte.ZoneVon(zone);
            if (z != null) route.Add(new V2(z.Spawn.X, z.Spawn.Z));
            return route;
        }

        /// <summary>
        /// The next point to ride for: the end of the route segment closest to <paramref name="pos"/> (a later one on a tie),
        /// or the first point after it that is still <paramref name="vorM"/> away — always onward, never back to the start.
        /// </summary>
        public static V2 Weiter(IList<V2> route, V2 pos, double vorM = 9.0)
        {
            if (route.Count == 1) return route[0];
            var seg = 0;
            var best = double.MaxValue;
            for (var i = 0; i + 1 < route.Count; i++)
            {
                var d = AbstandZuStrecke(pos, route[i], route[i + 1]);
                if (d <= best) { best = d; seg = i; }
            }
            for (var i = seg + 1; i < route.Count; i++)
                if ((route[i] - pos).Length > vorM) return route[i];
            return route[route.Count - 1];
        }

        private static double AbstandZuStrecke(V2 p, V2 a, V2 b)
        {
            var ab = b - a;
            var len2 = ab.LengthSquared;
            var t = len2 <= 0f ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2));
            return (p - (a + ab * t)).Length;
        }
    }

    /// <summary>
    /// Ranch metadata for a friend's visit — port of rmp_ranch_meta.gd. The host uploads a small display-only JSON before the
    /// visit (PUT /api/ranch, 16 KB), the guest renders the build-out, the horses (name, breed, level, coat) and trophies from
    /// it — and in Unity the host's build grid (compact: [item, x, y, turn or edge] per piece, the levels, the zones), which
    /// stands beside the paddock when the guest rides in. Never economy or storage. Validate before upload and normalise
    /// after download (foreign data is healed, not trusted).
    /// </summary>
    public static class RmpRanchMeta
    {
        public const int MaxBytes = 16 * 1024, MaxPferde = 12, MaxTrophaeen = 24, MaxNameLen = 24, MaxBauTeile = 160;

        /// <summary>The snapshot of the own ranch (ranch.wirtschaft.ausbau, ranch.tiere.pferde, ranch.comp) with the names.</summary>
        public static JObject Build(JObject state, string playerName, string goobyName)
        {
            var meta = new JObject
            {
                ["v"] = 1,
                ["name"] = Kurz(string.IsNullOrEmpty(playerName) ? "Gooby-Fan" : playerName),
                ["goobyName"] = Kurz(string.IsNullOrEmpty(goobyName) ? "Gooby" : goobyName),
                ["ausbau"] = Ausbau(state?["ranch"]?["wirtschaft"]?["ausbau"]),
                ["pferde"] = new JArray(),
                ["trophaeen"] = new JArray(),
                ["schleifen"] = 0,
            };
            if (state?["ranch"]?["tiere"]?["pferde"] is JObject pferde)
            {
                var list = (JArray)meta["pferde"];
                foreach (var p in pferde.Properties())
                {
                    if (list.Count >= MaxPferde) break;
                    if (p.Value is JObject pferd) list.Add(Pferd(pferd, ""));
                }
            }
            var comp = state?["ranch"]?["comp"] as JObject;
            if (comp?["trophaeen"] is JArray trophaeen)
                foreach (var t in trophaeen)
                    if (t.Type == JTokenType.String && ((JArray)meta["trophaeen"]).Count < MaxTrophaeen) ((JArray)meta["trophaeen"]).Add((string)t);
            meta["schleifen"] = comp?["schleifen"] is JObject schleifen ? schleifen.Count : 0;
            if (state != null && RanchBauState.Vorhanden(state)) meta["bau"] = BauKurz(RanchBauState.Lese(state));
            return meta;
        }

        /// <summary>The build grid in the visit's compact form (pieces, levels, zones).</summary>
        public static JObject BauKurz(JObject bau)
        {
            var teile = new JArray();
            foreach (var e in (bau?["items"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (teile.Count >= MaxBauTeile) break;
                var at = e["at"] as JArray;
                if (at == null || at.Count < 2 || !IstText(e["item"])) continue;
                JToken dreh = IstText(e["kante"]) ? (JToken)(string)e["kante"] : (int)Num(e["rot"], 0.0);
                teile.Add(new JArray((string)e["item"], (int)Num(at[0], 0.0), (int)Num(at[1], 0.0), dreh));
            }
            var stufen = new JObject();
            foreach (var p in (bau?["anlagen"] as JObject ?? new JObject()).Properties()) stufen[p.Name] = (int)Num(p.Value?["stufe"], 1.0);
            var zonen = new JArray();
            foreach (var z in bau?["zonen"] as JArray ?? new JArray()) if (IstText(z)) zonen.Add((string)z);
            return new JObject { ["teile"] = teile, ["stufen"] = stufen, ["zonen"] = zonen };
        }

        /// <summary>A guest's view of the host's build (a <c>ranch.bau</c>-shaped object for the 3D), null when the meta has none.</summary>
        public static JObject BauFuerGast(JToken meta)
        {
            if (!(meta?["bau"] is JObject kurz)) return null;
            var items = new JArray();
            var i = 0;
            foreach (var t in kurz["teile"] as JArray ?? new JArray())
            {
                if (!(t is JArray a) || a.Count < 4) continue;
                var entry = new JObject { ["uid"] = "g" + (i++), ["item"] = (string)a[0], ["at"] = new JArray((int)a[1], (int)a[2]) };
                if (a[3].Type == JTokenType.String) entry["kante"] = (string)a[3];
                else entry["rot"] = (int)a[3];
                items.Add(entry);
            }
            var anlagen = new JObject();
            foreach (var p in (kurz["stufen"] as JObject ?? new JObject()).Properties()) anlagen[p.Name] = new JObject { ["stufe"] = (int)p.Value };
            return new JObject { ["items"] = items, ["anlagen"] = anlagen, ["zonen"] = (kurz["zonen"] as JArray ?? new JArray("start")).DeepClone(), ["bestandMigriert"] = true };
        }

        private static JObject BauHeilen(JToken raw)
        {
            if (!(raw is JObject kurz)) return null;
            var teile = new JArray();
            foreach (var t in kurz["teile"] as JArray ?? new JArray())
            {
                if (teile.Count >= MaxBauTeile) break;
                if (!(t is JArray a) || a.Count < 4 || !IstText(a[0]) || ((string)a[0]).Length > 32 || !IstZahl(a[1]) || !IstZahl(a[2])) continue;
                var x = (int)Num(a[1], 0.0);
                var y = (int)Num(a[2], 0.0);
                if (x < 0 || y < 0 || x > 16 || y > 16) continue;
                JToken dreh;
                if (IstText(a[3]) && ((string)a[3] == "N" || (string)a[3] == "W")) dreh = (string)a[3];
                else if (IstZahl(a[3])) dreh = Math.Max(0, Math.Min(3, (int)Num(a[3], 0.0)));
                else continue;
                teile.Add(new JArray((string)a[0], x, y, dreh));
            }
            var stufen = new JObject();
            foreach (var p in (kurz["stufen"] as JObject ?? new JObject()).Properties())
                if (p.Name.Length <= 32 && IstZahl(p.Value)) stufen[p.Name] = Math.Max(1, Math.Min(9, (int)Num(p.Value, 1.0)));
            var zonen = new JArray();
            foreach (var z in kurz["zonen"] as JArray ?? new JArray())
                if (IstText(z) && ((string)z).Length <= 16 && zonen.Count < 8) zonen.Add((string)z);
            if (!zonen.Any(z => (string)z == "start")) zonen.Insert(0, "start");
            return new JObject { ["teile"] = teile, ["stufen"] = stufen, ["zonen"] = zonen };
        }

        /// <summary>Before the upload AND after the download: (ok, reason) — BAD_META, TOO_LARGE, TOO_MANY_HORSES.</summary>
        public static (bool ok, string reason) Validate(JToken meta)
        {
            if (!(meta is JObject o)) return (false, "BAD_META");
            if (System.Text.Encoding.UTF8.GetByteCount(o.ToString(Newtonsoft.Json.Formatting.None)) > MaxBytes) return (false, "TOO_LARGE");
            if (!(o["pferde"] is JArray pferde)) return (false, "BAD_META");
            if (pferde.Count > MaxPferde) return (false, "TOO_MANY_HORSES");
            return (true, "");
        }

        /// <summary>The healed display form for the guest.</summary>
        public static JObject Normalize(JToken raw)
        {
            var roh = raw as JObject ?? new JObject();
            var sauber = new JObject
            {
                ["v"] = 1,
                ["name"] = Kurz(Str(roh["name"], "?")),
                ["goobyName"] = Kurz(Str(roh["goobyName"], "Gooby")),
                ["ausbau"] = Ausbau(roh["ausbau"]),
                ["pferde"] = new JArray(),
                ["trophaeen"] = new JArray(),
                ["schleifen"] = Math.Max(0, (int)Num(roh["schleifen"], 0.0)),
            };
            if (roh["pferde"] is JArray pferde)
                for (var i = 0; i < pferde.Count && i < MaxPferde; i++)
                    if (pferde[i] is JObject p) ((JArray)sauber["pferde"]).Add(Pferd(p, "?"));
            if (roh["trophaeen"] is JArray trophaeen)
                for (var i = 0; i < trophaeen.Count && i < MaxTrophaeen; i++)
                    if (trophaeen[i].Type == JTokenType.String) ((JArray)sauber["trophaeen"]).Add((string)trophaeen[i]);
            var bau = BauHeilen(roh["bau"]);
            if (bau != null) sauber["bau"] = bau;
            return sauber;
        }

        private static JObject Ausbau(JToken raw)
        {
            var a = raw as JObject;
            return new JObject
            {
                ["boxen"] = Math.Max(1, Math.Min(3, (int)Num(a?["boxen"], 1.0))),
                ["reitplatz"] = IsTrue(a?["reitplatz"]),
                ["weidezaun"] = IsTrue(a?["weidezaun"]),
            };
        }

        private static JObject Pferd(JObject p, string fallbackName) => new JObject
        {
            ["name"] = Kurz(Str(p["name"], fallbackName)),
            ["rasse"] = Str(p["rasse"], "puschelhufer"),
            ["level"] = Math.Max(1, Math.Min(99, (int)Num(p["level"], 1.0))),
            ["farbe"] = Str(p["farbe"], "braun"),
        };

        private static string Kurz(string s) => s == null ? "" : s.Length <= MaxNameLen ? s : s.Substring(0, MaxNameLen);
        private static string Str(JToken t, string fallback) => t != null && t.Type == JTokenType.String ? (string)t : fallback;
        private static bool IsTrue(JToken t) => t != null && t.Type == JTokenType.Boolean && (bool)t;
        private static bool IstText(JToken t) => t != null && t.Type == JTokenType.String;
        private static bool IstZahl(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
        private static double Num(JToken t, double fallback) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : fallback;
    }
}
