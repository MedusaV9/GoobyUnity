using System;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Taming a wild horse (port of horse_taming.gd, IDEAS-1 A5): sneak up (attention 0..100 rises with nearness while
    /// moving, crouched far less, standing still calms), then calm it by tapping in its snort rhythm (beat 1.4 s, window
    /// ±0.3 s; calm 30 → 100, +14 a hit, −8 a miss). No throwing off — a failure only means it trots away and comes back
    /// after 20 s. Godot's open world spawns the herds; Unity has one wild horse a day at the forest edge behind the
    /// ranch (seeded by the date), so taming stays a treat next to buying.
    /// </summary>
    public sealed class RanchZaehmen
    {
        public const double AufmerksamMax = 100.0, AufmerksamAnstiegProS = 22.0, DuckMult = 0.35, StillMult = 0.25, AufmerksamAbbauProS = 8.0,
            SichtRadiusM = 10.0, BeruehrAbstandM = 1.5, TaktS = 1.4, TrefferFensterS = 0.3, RuheStart = 30.0, RuheZiel = 100.0,
            RuheTreffer = 14.0, RuheDaneben = -8.0, CooldownS = 20.0, WildScheckeChance = 0.6, WildGlitzerTraegerChance = 0.25;
        public static readonly string[] WildRassen = { "puschelhufer", "zottelgnuff", "moosmaehne", "flitzewind", "westernwuschel" };

        public const string Anschleichen = "anschleichen", Beruhigen = "beruhigen", Gezaehmt = "gezaehmt", Davongetrabt = "davongetrabt";

        public long Seed { get; }
        public string Phase { get; private set; } = Anschleichen;
        public double Aufmerksamkeit { get; private set; }
        public double Ruhe { get; private set; } = RuheStart;
        public double SeitSchnaubenS { get; private set; }
        public double CooldownRestS { get; private set; }
        /// <summary>"" | bereit | davongetrabt | schnauben | treffer | daneben | gezaehmt | wieder_da — what the last step brought.</summary>
        public string Event { get; private set; } = "";

        public RanchZaehmen(long seed) { Seed = seed; }

        public void StepAnschleichen(double dt, double abstandM, bool bewegtSich, bool geduckt)
        {
            Event = "";
            if (Phase != Anschleichen) return;
            var a = Aufmerksamkeit;
            if (abstandM <= SichtRadiusM)
            {
                var naehe = Math.Max(0.0, Math.Min(1.0, (SichtRadiusM - abstandM) / SichtRadiusM));
                var mult = (geduckt ? DuckMult : 1.0) * (bewegtSich ? 1.0 : StillMult);
                a += AufmerksamAnstiegProS * naehe * mult * dt;
            }
            else a -= AufmerksamAbbauProS * dt;
            if (!bewegtSich && geduckt) a -= AufmerksamAbbauProS * 0.5 * dt;
            Aufmerksamkeit = Math.Max(0.0, Math.Min(AufmerksamMax, a));
            if (Aufmerksamkeit >= AufmerksamMax) { Wegtraben(); return; }
            if (abstandM <= BeruehrAbstandM)
            {
                Phase = Beruhigen;
                SeitSchnaubenS = 0.0;
                Event = "bereit";
            }
        }

        public void StepBeruhigen(double dt)
        {
            Event = "";
            if (Phase != Beruhigen) return;
            var t = SeitSchnaubenS + Math.Max(0.0, dt);
            if (t >= TaktS)
            {
                t %= TaktS;
                Event = "schnauben";
            }
            SeitSchnaubenS = t;
        }

        public void BeruhigenTap()
        {
            Event = "";
            if (Phase != Beruhigen) return;
            var abstand = Math.Min(SeitSchnaubenS, TaktS - SeitSchnaubenS);
            Ruhe += abstand <= TrefferFensterS ? RuheTreffer : RuheDaneben;
            Event = abstand <= TrefferFensterS ? "treffer" : "daneben";
            Ruhe = Math.Max(0.0, Math.Min(RuheZiel, Ruhe));
            if (Ruhe >= RuheZiel) { Phase = Gezaehmt; Event = "gezaehmt"; }
            else if (Ruhe <= 0.0) Wegtraben();
        }

        public void StepCooldown(double dt)
        {
            Event = "";
            if (Phase != Davongetrabt) return;
            CooldownRestS -= Math.Max(0.0, dt);
            if (CooldownRestS > 0.0) return;
            Phase = Anschleichen;
            Aufmerksamkeit = 0.0;
            Ruhe = RuheStart;
            SeitSchnaubenS = 0.0;
            CooldownRestS = 0.0;
            Event = "wieder_da";
        }

        private void Wegtraben()
        {
            Phase = Davongetrabt;
            CooldownRestS = CooldownS;
            Event = "davongetrabt";
        }

        // ── the day's wild horse ──

        public static long TagesSeed(string tag) => GodotRng.StringHash("waldrand|" + (tag ?? ""));

        /// <summary>
        /// The wild horse of a seed (wildpferd_dict): a wild breed, random coat genes (Unity rolls them itself — Godot's
        /// breed data is not ported), 60 % piebald, 25 % carrying glitter.
        /// </summary>
        public static JObject Wildpferd(long seed)
        {
            var rng = new GodotRng(GodotRng.StringHash("wild|" + seed));
            var rasse = WildRassen[rng.RandIRange(0, WildRassen.Length - 1)];
            string G() => RanchGene.GenG[rng.RandIRange(0, RanchGene.GenG.Length - 1)];
            string Hh() => rng.RandF() < 0.25f ? "h+" : "h0";
            var gene = new JObject
            {
                ["g"] = new JArray(G(), G()), ["h"] = new JArray(Hh(), Hh()), ["s"] = new JArray("s0", "s0"), ["glitzer"] = new JArray("g0", "g0"),
            };
            if (rng.RandF() < WildScheckeChance) gene["s"] = new JArray("Sch", "s0");
            if (rng.RandF() < WildGlitzerTraegerChance) gene["glitzer"] = new JArray("gx", "g0");
            var stats = new JObject();
            foreach (var k in RanchPferd.StatKeys) stats[k] = rng.RandIRange(8, 14);
            return new JObject
            {
                ["rasse"] = rasse,
                ["gene"] = gene,
                ["farbe"] = RanchGene.IstSchecke(gene) ? "schecke" : RanchGene.FellfarbeAusGenen(gene),
                ["stats"] = stats,
                ["groesse"] = Math.Round(0.92 + rng.RandF() * 0.16, 3),
                ["charakter"] = new JArray(RanchGene.Charakterzuege[rng.RandIRange(0, 4)], RanchGene.Charakterzuege[rng.RandIRange(5, 9)]),
                ["wild"] = true,
            };
        }

        public static readonly string[] WildNamen = { "Sturmwind", "Distel", "Moos", "Funke", "Wildfang", "Nebel", "Eichel", "Brise" };

        /// <summary>Today's wild horse is still out there (not tamed yet today).</summary>
        public static bool HeuteDa(JObject state, string tag) => J.Str(J.Path(state, RanchHof.SliceId, "wild", "gezaehmtAm")) != (tag ?? "");

        /// <summary>Tamed: the wild horse moves into the ranch; returns its id (null when the stable is full or it was already tamed today).</summary>
        public static string Aufnehmen(JObject state, string tag)
        {
            if (!HeuteDa(state, tag) || DorfHaendler.StallVollFuer(state)) return null;
            var seed = TagesSeed(tag);
            var wild = Wildpferd(seed);
            var name = WildNamen[(int)(seed % WildNamen.Length)];
            var pferd = RanchHof.NeuesPferd(name, J.Str(wild["farbe"], "braun"));
            foreach (var p in wild.Properties()) pferd[p.Name] = p.Value.DeepClone();
            pferd["bindung"] = 10.0;
            var ranch = RanchHof.Slice(state);
            var id = "wild_" + seed;
            ((JObject)ranch["tiere"]["pferde"])[id] = pferd;
            J.EnsureDict(ranch, "wild")["gezaehmtAm"] = tag ?? "";
            return id;
        }
    }
}
