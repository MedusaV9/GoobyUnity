using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The coat genes (port of ranch_rassen.gd's gene model, Kap. 1.2): base colour B &gt; F &gt; Z (dominance from the
    /// left), lightening h0/h+ (one h+ light, two white), the piebald Sch over s0, glitter gx (recessive). Godot's start
    /// and shop horses carry no genes, so their foals would all be brown; Unity reads missing genes off the coat
    /// (<see cref="FuerFell"/>) so a foal takes after its parents.
    /// </summary>
    public static class RanchGene
    {
        public static readonly string[] GenG = { "B", "F", "Z" };
        public static readonly string[] Orte = { "g", "h", "s", "glitzer" };
        public static readonly string[] MerkmalAbzeichen = { "blesse", "stern", "schnippe" };
        public static readonly string[] Maehnenformen = { "glatt", "wellig", "puschel" };
        public static readonly string[] Charakterzuege = { "mutig", "scheu", "verfressen", "verspielt", "gelassen", "fleissig", "eitel", "anhaenglich", "stur", "neugierig" };

        private static readonly Dictionary<string, string[]> FarbeTabelle = new Dictionary<string, string[]>
        {
            ["B"] = new[] { "braun", "palomino", "weiss" },
            ["F"] = new[] { "fuchs", "apricot", "weiss" },
            ["Z"] = new[] { "schwarz", "rauchgrau", "weiss" },
        };

        /// <summary>Eigenheit of each breed (rassen.json) — only mixed foals need it.</summary>
        public static readonly IReadOnlyDictionary<string, string> Eigenheiten = new Dictionary<string, string>
        {
            ["puschelhufer"] = "allrounder", ["knuffpony"] = "slalom_ass", ["zottelgnuff"] = "kuschelherz", ["wolkentraber"] = "taktgefuehl",
            ["flitzewind"] = "langer_antritt", ["moosmaehne"] = "gelaende_profi", ["donnerbommel"] = "unerschrocken", ["federsprung"] = "sprunggefuehl",
            ["westernwuschel"] = "trail_praezision", ["toelterle"] = "toelt", ["miniknopf"] = "schauliebling", ["sternschnuppler"] = "glitzerfell",
        };

        public static string Fallback(string ort) => ort == "g" ? "B" : ort == "h" ? "h0" : ort == "s" ? "s0" : "g0";

        /// <summary>The allele pair of a locus (broken → [fallback, fallback]).</summary>
        public static string[] Allele(JObject gene, string ort, string fallback)
        {
            if (gene?[ort] is JArray a && a.Count == 2) return new[] { J.Str(a[0], fallback), J.Str(a[1], fallback) };
            return new[] { fallback, fallback };
        }

        public static string FellfarbeAusGenen(JObject gene)
        {
            var g = Allele(gene, "g", "B");
            var basis = GenG.FirstOrDefault(k => g.Contains(k)) ?? "Z";
            var hell = Allele(gene, "h", "h0").Count(x => x == "h+");
            return FarbeTabelle[basis][Math.Max(0, Math.Min(2, hell))];
        }

        public static bool IstSchecke(JObject gene) => Allele(gene, "s", "s0").Contains("Sch");

        public static bool IstGlitzer(JObject gene) => Allele(gene, "glitzer", "g0").Count(x => x == "gx") >= 2;

        /// <summary>Genes that show a coat (Unity addition for horses without genes).</summary>
        public static JObject FuerFell(string fell)
        {
            (string g, string h1, string h2, string s) = fell switch
            {
                "palomino" => ("B", "h+", "h0", "s0"),
                "weiss" => ("B", "h+", "h+", "s0"),
                "fuchs" => ("F", "h0", "h0", "s0"),
                "apricot" => ("F", "h+", "h0", "s0"),
                "schwarz" => ("Z", "h0", "h0", "s0"),
                "rauchgrau" => ("Z", "h+", "h0", "s0"),
                "schecke" => ("B", "h0", "h0", "Sch"),
                _ => ("B", "h0", "h0", "s0"),
            };
            return new JObject
            {
                ["g"] = new JArray(g, g), ["h"] = new JArray(h1, h2), ["s"] = new JArray(s, "s0"), ["glitzer"] = new JArray("g0", "g0"),
            };
        }

        /// <summary>A horse's genes: its own, else read off its coat.</summary>
        public static JObject Von(JObject pferd)
        {
            if (pferd?["gene"] is JObject g && g.Count > 0) return g;
            return FuerFell(J.Str(pferd?["farbe"], "braun"));
        }
    }

    /// <summary>
    /// Breeding and foals (port of horse_breeding.gd, Kap. 4): a mare and a stallion of level 8+ with bond 45+ and a
    /// happy mare (mood 60+) start a 48-h pregnancy (at most two at once, the mare rests five days afterwards); five
    /// care checkpoints every 9.6 h each shorten it by 2 h — missing them never hurts. At birth the foal is rolled from
    /// the seed exactly like Godot (one allele per parent and locus, stats = parents' mean + the dice, markings,
    /// mane, character 45/45/10, size ± 5 %, pure breed or mix) and grows up in visible phases: foal 3 days, yearling 4,
    /// young horse 3, then grown for good; riding from young horse on. State in <c>ranch.zucht</c>.
    /// </summary>
    public static class RanchZucht
    {
        public const string Key = "zucht";
        public const int MinLevel = 8, MaxTraechtigkeiten = 2, Checkpoints = 5;
        public const double MinBindung = 45.0, MinLaune = 60.0;
        public const long RuhezeitMs = 5L * 86_400_000L, GrundDauerMs = 48L * 3_600_000L, CheckpointBonusMs = 2L * 3_600_000L;
        public const long CheckpointIntervallMs = GrundDauerMs / Checkpoints;
        public const double AbzeichenBasis = 0.05, AbzeichenJeElter = 0.35, MaehneElterChance = 0.4, CharakterElterChance = 0.45,
            RassensprungChance = 0.10, GroesseVarianz = 0.05;
        public const string MischlingRasse = "puschelmix";

        private static readonly (int wert, double p)[] Wuerfel = { (-1, 0.15), (0, 0.55), (1, 0.20), (2, 0.08), (3, 0.02) };

        /// <summary>[age id, days, visible scale].</summary>
        public static readonly (string alter, int tage, double skala)[] Phasen =
            { ("fohlen", 3, 0.55), ("jaehrling", 4, 0.75), ("jungpferd", 3, 0.90), ("ausgewachsen", 0, 1.0) };

        public static JObject Slice(JObject state)
        {
            var ranch = J.EnsureDict(state, RanchHof.SliceId);
            if (!(ranch[Key] is JObject z)) { z = new JObject(); ranch[Key] = z; }
            if (!(z["traechtigkeiten"] is JObject)) z["traechtigkeiten"] = new JObject();
            if (!(z["ruhezeitBis"] is JObject)) z["ruhezeitBis"] = new JObject();
            return z;
        }

        /// <summary>A healed copy of <c>ranch.zucht</c> for reading.</summary>
        public static JObject Lese(JObject state)
        {
            var z = J.Path(state, RanchHof.SliceId, Key) is JObject roh ? (JObject)roh.DeepClone() : new JObject();
            if (!(z["traechtigkeiten"] is JObject)) z["traechtigkeiten"] = new JObject();
            if (!(z["ruhezeitBis"] is JObject)) z["ruhezeitBis"] = new JObject();
            return z;
        }

        /// <summary>
        /// May mare and stallion breed now? Error ids: traechtig | voll | ruhezeit | level | bindung | laune. The farm's
        /// breeding stable adds room for <paramref name="extraPlaetze"/> more pregnant mares.
        /// </summary>
        public static (bool ok, string fehler) ZuchtErlaubt(string stuteId, JObject stute, JObject hengst, double laune, JObject zucht, long nowMs, int extraPlaetze = 0)
        {
            var t = J.Dict(zucht?["traechtigkeiten"]);
            if (t.ContainsKey(stuteId ?? "")) return (false, "traechtig");
            if (t.Count >= MaxTraechtigkeiten + Math.Max(0, extraPlaetze)) return (false, "voll");
            if (J.Long(J.Dict(zucht?["ruhezeitBis"])[stuteId ?? ""]) > nowMs) return (false, "ruhezeit");
            foreach (var pferd in new[] { stute, hengst })
            {
                if (J.Int(pferd?["level"], 1) < MinLevel) return (false, "level");
                if (J.NumOr(pferd?["bindung"], 0.0) < MinBindung) return (false, "bindung");
            }
            if (laune < MinLaune) return (false, "laune");
            return (true, "");
        }

        /// <summary>The pregnancy starts with a frozen snapshot of the father.</summary>
        public static void TraechtigkeitStarten(JObject zucht, string stuteId, JObject vater, long nowMs, long seed)
        {
            ((JObject)zucht["traechtigkeiten"])[stuteId] = new JObject
            {
                ["startAt"] = nowMs, ["checkpoints"] = 0, ["letzterCheckpoint"] = -1, ["seed"] = seed, ["vater"] = ElternSnapshot(vater),
            };
        }

        public static long DauerMs(int erledigt) => GrundDauerMs - Math.Max(0, Math.Min(Checkpoints, erledigt)) * CheckpointBonusMs;

        public static JObject Traechtigkeit(JObject zucht, string stuteId) => J.Dict(zucht?["traechtigkeiten"])[stuteId ?? ""] as JObject;

        /// <summary>The care checkpoint due now (0..4) or −1.</summary>
        public static int CheckpointFaellig(JObject traechtigkeit, long nowMs)
        {
            if (traechtigkeit == null) return -1;
            var start = J.Long(traechtigkeit["startAt"]);
            var idx = (int)Math.Min(Checkpoints - 1, Math.Floor((double)(nowMs - start) / CheckpointIntervallMs));
            if (idx < 0 || idx <= J.Int(traechtigkeit["letzterCheckpoint"], -1)) return -1;
            return idx;
        }

        public static bool CheckpointPflegen(JObject zucht, string stuteId, long nowMs)
        {
            var t = Traechtigkeit(zucht, stuteId);
            var idx = CheckpointFaellig(t, nowMs);
            if (idx < 0) return false;
            t["letzterCheckpoint"] = idx;
            t["checkpoints"] = J.Int(t["checkpoints"]) + 1;
            return true;
        }

        public static long GeburtIn(JObject zucht, string stuteId, long nowMs)
        {
            var t = Traechtigkeit(zucht, stuteId);
            if (t == null) return long.MaxValue;
            return Math.Max(0L, J.Long(t["startAt"]) + DauerMs(J.Int(t["checkpoints"])) - nowMs);
        }

        public static bool GeburtBereit(JObject zucht, string stuteId, long nowMs) => Traechtigkeit(zucht, stuteId) != null && GeburtIn(zucht, stuteId, nowMs) <= 0;

        /// <summary>The birth: the foal is rolled, the pregnancy ends, the mare rests five days. Null = not yet.</summary>
        public static JObject Gebaeren(JObject zucht, string stuteId, JObject mutter, long nowMs)
        {
            if (!GeburtBereit(zucht, stuteId, nowMs)) return null;
            var t = Traechtigkeit(zucht, stuteId);
            var vater = t["vater"] as JObject ?? new JObject();
            var fohlen = WuerfleFohlen(mutter, vater, J.Long(t["seed"]));
            fohlen["geborenAm"] = nowMs;
            fohlen["eltern"] = new JArray(stuteId, J.Str(vater["ref"], "npc"));
            fohlen["ahnen"] = new JObject { ["mutter"] = ElternSnapshot(mutter), ["vater"] = vater.DeepClone() };
            ((JObject)zucht["traechtigkeiten"]).Remove(stuteId);
            ((JObject)zucht["ruhezeitBis"])[stuteId] = nowMs + RuhezeitMs;
            return fohlen;
        }

        /// <summary>The inheritance model, deterministic from the seed (wuerfle_fohlen, same draws as Godot).</summary>
        public static JObject WuerfleFohlen(JObject mutter, JObject vater, long seed)
        {
            var rng = new GodotRng(GodotRng.StringHash("fohlen|" + seed));
            var gm = RanchGene.Von(mutter);
            var gv = RanchGene.Von(vater);
            var gene = new JObject();
            foreach (var ort in RanchGene.Orte)
            {
                var fb = RanchGene.Fallback(ort);
                var a = RanchGene.Allele(gm, ort, fb)[rng.RandIRange(0, 1)];
                var b = RanchGene.Allele(gv, ort, fb)[rng.RandIRange(0, 1)];
                gene[ort] = new JArray(a, b);
            }
            var stats = new JObject();
            foreach (var k in RanchPferd.StatKeys)
            {
                var mitte = Math.Round((StatBasis(mutter, k) + StatBasis(vater, k)) / 2.0, MidpointRounding.AwayFromZero);
                stats[k] = Math.Max(1, Math.Min(20, (int)mitte + Wuerfeln(rng)));
            }
            var fohlen = new JObject
            {
                ["rasse"] = MischlingRasse,
                ["gene"] = gene,
                ["farbe"] = RanchGene.FellfarbeAusGenen(gene),
                ["abzeichen"] = ErbeAbzeichen(rng, mutter, vater),
                ["charakter"] = ErbeCharakter(rng, mutter, vater),
                ["stats"] = stats,
                ["groesse"] = Snap(ErbeGroesse(rng, mutter, vater)),
                ["stimmPitch"] = Snap(rng.RandFRange(0.85f, 1.15f)),
                ["phasenOffset"] = Snap(rng.RandF()),
                ["alter"] = "fohlen",
                ["eltern"] = new JArray(),
                ["geborenAm"] = 0,
            };
            var rm = J.Str(mutter?["rasse"], "puschelhufer");
            var rv = J.Str(vater?["rasse"], "puschelhufer");
            if (rm == rv) fohlen["rasse"] = rm;
            else if (rng.RandF() < RassensprungChance) fohlen["rasse"] = rng.RandF() < 0.5f ? rm : rv;
            else fohlen["mixEigenheiten"] = new JArray(new[] { EigenheitId(mutter), EigenheitId(vater) }.Where(e => e.Length > 0).Distinct());
            return fohlen;
        }

        public static JObject ElternSnapshot(JObject pferd)
        {
            var p = pferd ?? new JObject();
            var outp = new JObject
            {
                ["ref"] = J.Str(p["ref"], J.Str(p["packId"], "eigen")),
                ["name"] = J.Str(p["name"]),
                ["rasse"] = J.Str(p["rasse"], "puschelhufer"),
                ["farbe"] = J.Str(p["farbe"], "braun"),
                ["gene"] = RanchGene.Von(p).DeepClone(),
                ["stats"] = p["stats"]?.DeepClone(),
                ["abzeichen"] = p["abzeichen"]?.DeepClone(),
                ["charakter"] = p["charakter"]?.DeepClone(),
                ["groesse"] = J.NumOr(p["groesse"], 1.0),
            };
            foreach (var k in new[] { "farbeHex", "maehneHex", "nameKey" }) if (p[k] != null) outp[k] = p[k].DeepClone();
            if (p["ahnen"] is JObject ahnen)
            {
                var flach = new JObject();
                foreach (var rolle in ahnen.Properties())
                    if (rolle.Value is JObject a) { var kurz = (JObject)a.DeepClone(); kurz.Remove("ahnen"); flach[rolle.Name] = kurz; }
                outp["ahnen"] = flach;
            }
            return outp;
        }

        /// <summary>The age phase from the birth stamp: foal (3 days) → yearling (4) → young horse (3) → grown for good.</summary>
        public static string AlterFuer(long geborenAmMs, long nowMs, double tempo = 1.0)
        {
            if (geborenAmMs <= 0) return "ausgewachsen";
            var tage = (nowMs - geborenAmMs) / 86_400_000.0 * Math.Max(1.0, tempo);
            var grenze = 0.0;
            foreach (var (alter, dauer, _) in Phasen)
            {
                if (dauer <= 0) return alter;
                grenze += dauer;
                if (tage < grenze) return alter;
            }
            return "ausgewachsen";
        }

        public static double PhasenSkala(string alter)
        {
            foreach (var (a, _, skala) in Phasen) if (a == alter) return skala;
            return 1.0;
        }

        public static bool Reitbar(string alter) => alter == "jungpferd" || alter == "ausgewachsen";

        /// <summary>Real days until the next phase (0 = grown).</summary>
        public static double TageBisNaechstePhase(long geborenAmMs, long nowMs, double tempo = 1.0)
        {
            if (geborenAmMs <= 0) return 0.0;
            tempo = Math.Max(1.0, tempo);
            var tage = (nowMs - geborenAmMs) / 86_400_000.0 * tempo;
            var grenze = 0.0;
            foreach (var (_, dauer, _) in Phasen)
            {
                if (dauer <= 0) return 0.0;
                grenze += dauer;
                if (tage < grenze) return (grenze - tage) / tempo;
            }
            return 0.0;
        }

        // ── on the save ──

        public static readonly string[] FohlenNamen = { "Krümel", "Flöckchen", "Muffin", "Pünktchen", "Tupfer", "Bommel", "Nugget", "Wölkchen", "Keksi", "Sternchen" };

        /// <summary>The mare's mood as the care maths sees it.</summary>
        public static double Laune(JObject pferd) => RanchCare.Laune(J.Dict(pferd?["werte"]), J.NumOr(pferd?["bindung"], 0.0));

        /// <summary>The horse's age phase; <paramref name="tempo"/> is the foal pasture's faster clock (RanchBauEffekte.AufzuchtTempo).</summary>
        public static string Alter(JObject pferd, long nowMs, double tempo = 1.0) => AlterFuer(J.Long(pferd?["geborenAm"]), nowMs, tempo);

        /// <summary>Mare and stallion of the farm start a pregnancy (nothing changes on an error).</summary>
        public static (bool ok, string fehler) Decken(JObject state, string stuteId, string hengstId, long nowMs)
        {
            if (stuteId == hengstId) return (false, "selbe");
            RanchHof.Slice(state);
            var stute = RanchHof.Pferd(state, stuteId);
            var hengst = RanchHof.Pferd(state, hengstId);
            if (stute == null || hengst == null) return (false, "keinPferd");
            var tempo = RanchBauEffekte.AufzuchtTempo(state);
            if (!Reitbar(Alter(stute, nowMs, tempo)) || !Reitbar(Alter(hengst, nowMs, tempo))) return (false, "jung");
            var zucht = Slice(state);
            var (ok, fehler) = ZuchtErlaubt(stuteId, stute, hengst, Laune(stute), zucht, nowMs, RanchBauEffekte.Traechtigkeiten(state));
            if (!ok) return (false, fehler);
            var vater = (JObject)hengst.DeepClone();
            vater["ref"] = hengstId;
            TraechtigkeitStarten(zucht, stuteId, vater, nowMs, nowMs ^ GodotRng.StringHash(stuteId + "|" + hengstId));
            return (true, "");
        }

        public static bool Fuersorge(JObject state, string stuteId, long nowMs) => CheckpointPflegen(Slice(state), stuteId, nowMs);

        /// <summary>The birth on the farm: the foal moves into the horses (id returned, null = not yet).</summary>
        public static string Geburt(JObject state, string stuteId, long nowMs)
        {
            var mutter = RanchHof.Pferd(state, stuteId);
            if (mutter == null) return null;
            var mutterSnapshot = (JObject)mutter.DeepClone();
            mutterSnapshot["ref"] = stuteId;
            var fohlen = Gebaeren(Slice(state), stuteId, mutterSnapshot, nowMs);
            if (fohlen == null) return null;
            var seed = (long)(J.NumOr(fohlen["phasenOffset"], 0.0) * 1000.0) + nowMs / 1000;
            var name = FohlenNamen[(int)(Math.Abs(seed) % FohlenNamen.Length)];
            var pferd = RanchHof.NeuesPferd(name, J.Str(fohlen["farbe"], "braun"));
            foreach (var p in fohlen.Properties()) pferd[p.Name] = p.Value.DeepClone();
            if (RanchGene.IstSchecke((JObject)fohlen["gene"])) pferd["farbe"] = "schecke";
            var id = "fohlen_" + nowMs;
            var ranch = RanchHof.Slice(state);
            ((JObject)ranch["tiere"]["pferde"])[id] = pferd;
            return id;
        }

        // ── inheritance details ──

        private static double Snap(double x) => Math.Floor(x / 0.001 + 0.5) * 0.001;

        private static double StatBasis(JObject pferd, string key) => Math.Max(1.0, Math.Min(20.0, J.NumOr(J.Dict(pferd?["stats"])[key], 10.0)));

        private static int Wuerfeln(GodotRng rng)
        {
            var wurf = rng.RandF();
            var summe = 0.0;
            foreach (var (wert, p) in Wuerfel)
            {
                summe += p;
                if (wurf < summe) return wert;
            }
            return Wuerfel[Wuerfel.Length - 1].wert;
        }

        private static JObject ErbeAbzeichen(GodotRng rng, JObject mutter, JObject vater)
        {
            var am = J.Dict(mutter?["abzeichen"]);
            var av = J.Dict(vater?["abzeichen"]);
            var outp = new JObject();
            foreach (var merkmal in RanchGene.MerkmalAbzeichen)
            {
                var n = (J.IsTrue(am[merkmal]) ? 1 : 0) + (J.IsTrue(av[merkmal]) ? 1 : 0);
                outp[merkmal] = rng.RandF() < AbzeichenBasis + AbzeichenJeElter * n;
            }
            var sm = am["socken"] as JArray ?? new JArray();
            var sv = av["socken"] as JArray ?? new JArray();
            var socken = new JArray();
            for (var i = 0; i < 4; i++)
            {
                var n = (i < sm.Count && J.Int(sm[i]) == 1 ? 1 : 0) + (i < sv.Count && J.Int(sv[i]) == 1 ? 1 : 0);
                socken.Add(rng.RandF() < AbzeichenBasis + AbzeichenJeElter * n ? 1 : 0);
            }
            outp["socken"] = socken;
            var wurf = rng.RandF();
            if (wurf < MaehneElterChance) outp["maehnenform"] = J.Str(am["maehnenform"], "glatt");
            else if (wurf < MaehneElterChance * 2.0) outp["maehnenform"] = J.Str(av["maehnenform"], "glatt");
            else outp["maehnenform"] = RanchGene.Maehnenformen[rng.RandIRange(0, RanchGene.Maehnenformen.Length - 1)];
            return outp;
        }

        private static JArray ErbeCharakter(GodotRng rng, JObject mutter, JObject vater)
        {
            var outp = new List<string>();
            for (var slot = 0; slot < 2; slot++)
            {
                var zug = "";
                var guard = 0;
                while ((zug == "" || outp.Contains(zug)) && guard < 32)
                {
                    guard++;
                    var wurf = rng.RandF();
                    if (wurf < CharakterElterChance) zug = ZufallsZug(rng, mutter);
                    else if (wurf < CharakterElterChance * 2.0) zug = ZufallsZug(rng, vater);
                    else zug = RanchGene.Charakterzuege[rng.RandIRange(0, RanchGene.Charakterzuege.Length - 1)];
                }
                outp.Add(zug);
            }
            return new JArray(outp);
        }

        private static string ZufallsZug(GodotRng rng, JObject elter)
        {
            if (elter?["charakter"] is JArray liste && liste.Count > 0) return J.Str(liste[rng.RandIRange(0, liste.Count - 1)]);
            return RanchGene.Charakterzuege[rng.RandIRange(0, RanchGene.Charakterzuege.Length - 1)];
        }

        private static double ErbeGroesse(GodotRng rng, JObject mutter, JObject vater)
        {
            var mitte = (J.NumOr(mutter?["groesse"], 1.0) + J.NumOr(vater?["groesse"], 1.0)) / 2.0;
            return mitte * (1.0 + rng.RandFRange((float)-GroesseVarianz, (float)GroesseVarianz));
        }

        private static string EigenheitId(JObject pferd)
        {
            if (pferd?["mixEigenheiten"] is JArray mix && mix.Count > 0) return J.Str(mix[0]);
            return RanchGene.Eigenheiten.TryGetValue(J.Str(pferd?["rasse"], "puschelhufer"), out var e) ? e : "";
        }
    }
}
