using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Sheep herding (ranchHerde) — port of herde_logic.gd: a flock with boid behaviour (separation / cohesion /
    /// alignment + fleeing from the rider) has to be driven through the gate into the pen before the time runs out.
    /// 10 levels from <c>minigames/ranch/herde_level.json</c>. <see cref="Step"/> is pure and deterministic — no RNG in
    /// the step, the wander wobble comes from the sheep's spawn phase + absolute time. Vectors are Godot float32
    /// (<see cref="V2"/>), scalars double like GDScript.
    /// </summary>
    public static class RanchHerde
    {
        public const string LevelPath = "minigames/ranch/herde_level";
        public const int LevelCount = 10;
        public const double TreibRadius = 1.7, TreibAbstand = 2.4;

        public sealed class Tune
        {
            public double SchafTempo = 3.0, FluchtRadius = 6.0, FluchtKraft = 7.0, SepRadius = 1.1, SepKraft = 4.5, KohRadius = 5.5,
                KohKraft = 0.7, AusrRadius = 4.0, AusrKraft = 0.5, WandRand = 1.2, WandKraft = 8.0, WanderKraft = 0.5, Daempfung = 2.2,
                TorSogRadius = 4.5, TorSogKraft = 1.5, DruckZumTor = 0.45, DrinBremse = 4.0, ReiterTempo = 5.6, PunkteProRestS = 2.0,
                ZeitMult = 1.0;
            public int PunkteBasis = 40, PunkteProLevel = 4, FirstClearBonus = 25;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Base = new Tune();

        /// <summary>easy = a nimbler rider + calmer sheep, hard the other way round (§G5.3 pattern).</summary>
        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double reiter, wander, zeit;
            switch (mode)
            {
                case "easy": reiter = 1.15; wander = 0.6; zeit = 1.25; break;
                case "hard":
                case "endless": reiter = 0.92; wander = 1.45; zeit = 0.85; break;
                default: return tune;
            }
            var t = tune.Clone();
            t.ReiterTempo = tune.ReiterTempo * reiter;
            t.WanderKraft = tune.WanderKraft * wander;
            t.ZeitMult = zeit;
            t.Mode = mode;
            return t;
        }

        public static double Zeitlimit(JObject level, Tune tune) => Num(level, "zeit_s", 60.0) * tune.ZeitMult;

        public static List<JObject> LoadLevels()
        {
            var list = new List<JObject>();
            if (ContentSource.LoadObject(LevelPath)["level"] is JArray arr)
                foreach (var t in arr) if (t is JObject o) list.Add((JObject)o.DeepClone());
            return list;
        }

        public static JObject LevelById(IEnumerable<JObject> levels, int id)
        {
            foreach (var l in levels) if ((int)Num(l, "id", 0) == id) return l;
            return null;
        }

        public static List<string> ValidateLevels(IList<JObject> levels)
        {
            var errors = new List<string>();
            if (levels.Count != LevelCount) errors.Add($"erwartet {LevelCount} Level, gefunden {levels.Count}");
            var before = 0;
            foreach (var lv in levels)
            {
                var id = (int)Num(lv, "id", 0);
                var schafe = (int)Num(lv, "schafe", 0);
                if (schafe < before) errors.Add($"Level {id}: Schafzahl sinkt ({schafe} < {before})");
                before = schafe;
                if (!(lv["feld"] is JArray feld) || feld.Count != 2) { errors.Add($"Level {id}: feld braucht [halbX, halbZ]"); continue; }
                var p = Pferch.Of(lv);
                if (p.Tor < 2.0 || p.Tor > p.W) errors.Add($"Level {id}: Tor-Breite unplausibel");
                if (Math.Abs(p.X) + p.W * 0.5 > Feld(lv).x || Math.Abs(p.Z) + p.T * 0.5 > Feld(lv).z) errors.Add($"Level {id}: Pferch ragt aus dem Feld");
            }
            return errors;
        }

        /// <summary>Pen geometry; the gate sits in the middle of the SOUTH wall (towards the field centre, +z).</summary>
        public readonly struct Pferch
        {
            public readonly double X, Z, W, T, Tor;
            public Pferch(double x, double z, double w, double t, double tor) { X = x; Z = z; W = w; T = t; Tor = tor; }

            public static Pferch Of(JObject level)
            {
                var p = level?["pferch"] as JObject ?? new JObject();
                return new Pferch(Num(p, "x", 0.0), Num(p, "z", -7.0), Num(p, "w", 5.0), Num(p, "t", 3.5), Num(p, "tor", 2.6));
            }
        }

        public static (double x, double z) Feld(JObject level) =>
            level?["feld"] is JArray f && f.Count >= 2 ? (f[0].Value<double>(), f[1].Value<double>()) : (12.0, 9.0);

        public static V2 TorPos(JObject level)
        {
            var p = Pferch.Of(level);
            return new V2(p.X, p.Z + p.T * 0.5);
        }

        public static bool IstImPferch(JObject level, V2 pos, double inset = 0.05)
        {
            var p = Pferch.Of(level);
            return Math.Abs(pos.X - p.X) <= p.W * 0.5 - inset && Math.Abs(pos.Y - p.Z) <= p.T * 0.5 - inset;
        }

        public sealed class Schaf
        {
            public V2 Pos, Vel;
            public double Phase;
            public bool Drin;
            public Schaf Copy() => (Schaf)MemberwiseClone();
            public override string ToString() => Pos + "|" + Vel + "|" + Phase.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + Drin;
        }

        /// <summary>Spawn the flock (deterministic via GoobyRng) in the south half of the field.</summary>
        public static List<Schaf> SpawnSchafe(JObject level, GoobyRng rng)
        {
            var feld = Feld(level);
            var output = new List<Schaf>();
            var n = (int)Num(level, "schafe", 3);
            for (var i = 0; i < n; i++)
            {
                var x = (rng.Next() * 2.0 - 1.0) * feld.x * 0.6;
                var z = (0.15 + rng.Next() * 0.55) * feld.z;
                output.Add(new Schaf { Pos = new V2(x, z), Vel = V2.Zero, Phase = rng.Next() * Math.PI * 2.0 });
            }
            return output;
        }

        /// <summary>ONE simulation step of the flock. Pure: a new list, the input stays untouched.</summary>
        public static List<Schaf> Step(IList<Schaf> schafe, V2 reiter, double tAbs, double dt, Tune tune, JObject level)
        {
            var feld = Feld(level);
            var tor = TorPos(level);
            var output = new List<Schaf>(schafe.Count);
            for (var i = 0; i < schafe.Count; i++)
            {
                var s = schafe[i];
                var pos = s.Pos;
                var vel = s.Vel;
                if (s.Drin)
                {
                    vel = vel.MoveToward(V2.Zero, (float)(tune.DrinBremse * dt));
                    output.Add(Bewege(s, pos, vel, dt, level, feld));
                    continue;
                }
                var kraft = BoidKraefte(schafe, i, pos, vel, tune);
                kraft += Flucht(pos, reiter, tune, tor);
                kraft += WandKraft(pos, feld, tune);
                kraft += TorSog(pos, level, tune);
                kraft += new V2(Math.Cos(s.Phase + tAbs * 1.7), Math.Sin(s.Phase * 1.3 + tAbs)) * tune.WanderKraft;
                vel += kraft * dt;
                vel = vel.MoveToward(V2.Zero, (float)(tune.Daempfung * dt * 0.35));
                var maxTempo = tune.SchafTempo;
                if (vel.Length > maxTempo) vel = vel.Normalized() * maxTempo;
                output.Add(Bewege(s, pos, vel, dt, level, feld));
            }
            return output;
        }

        public static int DrinAnzahl(IEnumerable<Schaf> schafe)
        {
            var n = 0;
            foreach (var s in schafe) if (s.Drin) n++;
            return n;
        }

        /// <summary>Rider step toward the target (clamped to the field).</summary>
        public static V2 ReiterStep(V2 reiter, V2 ziel, double dt, Tune tune, JObject level)
        {
            var feld = Feld(level);
            var neu = reiter.MoveToward(ziel, (float)(tune.ReiterTempo * dt));
            return new V2(Math.Min(Math.Max(neu.X, -feld.x), feld.x), Math.Min(Math.Max(neu.Y, -feld.z), feld.z));
        }

        /// <summary>Level score: base + rest-time bonus + level bonus (+ first clear).</summary>
        public static int LevelScore(int levelId, double restS, bool firstClear, Tune tune)
        {
            var total = tune.PunkteBasis;
            total += (int)Math.Round(Math.Max(0.0, restS) * tune.PunkteProRestS, MidpointRounding.AwayFromZero);
            total += levelId * tune.PunkteProLevel;
            if (firstClear) total += tune.FirstClearBonus;
            return total;
        }

        /// <summary>Stars by the share of time left: ≥ 40 % = 3, ≥ 15 % = 2, else 1.</summary>
        public static int Sterne(double restS, double limitS)
        {
            if (limitS <= 0.0) return 1;
            var frac = restS / limitS;
            if (frac >= 0.4) return 3;
            return frac >= 0.15 ? 2 : 1;
        }

        /// <summary>
        /// The bot's herding target: always BEHIND the sheep farthest from the gate (seen from the gate) — the classic
        /// one-after-the-other drive; cohesion pulls the neighbours along. Never north of the gate line in the corridor.
        /// </summary>
        public static V2 BotZiel(IList<Schaf> schafe, JObject level)
        {
            var tor = TorPos(level);
            var zielSchaf = tor;
            var maxD = -1.0;
            foreach (var s in schafe)
            {
                if (s.Drin) continue;
                double d = s.Pos.DistanceTo(tor);
                if (d > maxD) { maxD = d; zielSchaf = s.Pos; }
            }
            if (maxD < 0.0) return tor;
            return BehindSheep(zielSchaf, tor, 2.8, level);
        }

        /// <summary>Tap help: a tap near a free sheep jumps the riding target behind it (same policy as the bot).</summary>
        public static V2 TreibZiel(V2 tipp, IList<Schaf> herde, JObject level)
        {
            var schaf = V2.Zero;
            var best = TreibRadius;
            var found = false;
            foreach (var s in herde)
            {
                if (s.Drin) continue;
                double d = tipp.DistanceTo(s.Pos);
                if (d <= best) { best = d; schaf = s.Pos; found = true; }
            }
            return found ? BehindSheep(schaf, TorPos(level), TreibAbstand, level) : tipp;
        }

        private static V2 BehindSheep(V2 schaf, V2 tor, double abstand, JObject level)
        {
            var richtung = !schaf.Equals(tor) ? (schaf - tor).Normalized() : new V2(0f, 1f);
            var ziel = schaf + richtung * abstand;
            var p = Pferch.Of(level);
            if (Math.Abs(schaf.X - p.X) < p.Tor * 0.5 + 1.0) ziel = new V2(ziel.X, (float)Math.Max(ziel.Y, p.Z + p.T * 0.5 + 0.6));
            return ziel;
        }

        public sealed class SimResult
        {
            public bool Geschafft;
            public double ZeitS, RestS;
            public int Score, Sterne, Drin;
        }

        /// <summary>Deterministic bot certification of a level (dt = 1/30).</summary>
        public static SimResult SimulateHueten(JObject level, long seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Base, mode);
            var rng = new GoobyRng(seed + (long)Num(level, "id", 0) * 211);
            var schafe = SpawnSchafe(level, rng);
            var feld = Feld(level);
            var reiter = new V2(0.0, feld.z * 0.9);
            var limit = Zeitlimit(level, tune);
            const double dt = 1.0 / 30.0;
            var t = 0.0;
            while (t < limit)
            {
                reiter = ReiterStep(reiter, BotZiel(schafe, level), dt, tune, level);
                schafe = Step(schafe, reiter, t, dt, tune, level);
                t += dt;
                if (DrinAnzahl(schafe) == schafe.Count) break;
            }
            var geschafft = DrinAnzahl(schafe) == schafe.Count;
            var rest = Math.Max(0.0, limit - t);
            return new SimResult
            {
                Geschafft = geschafft,
                ZeitS = t,
                RestS = rest,
                Score = geschafft ? LevelScore((int)Num(level, "id", 0), rest, false, tune) : 0,
                Sterne = geschafft ? Sterne(rest, limit) : 0,
                Drin = DrinAnzahl(schafe),
            };
        }

        private static V2 BoidKraefte(IList<Schaf> schafe, int index, V2 pos, V2 vel, Tune tune)
        {
            var sep = V2.Zero;
            var kohSumme = V2.Zero;
            var kohN = 0;
            var ausrSumme = V2.Zero;
            var ausrN = 0;
            for (var j = 0; j < schafe.Count; j++)
            {
                if (j == index || schafe[j].Drin) continue;
                var andere = schafe[j].Pos;
                double d = pos.DistanceTo(andere);
                if (d < 0.001) continue;
                if (d < tune.SepRadius) sep += (pos - andere) / d * (1.0 - d / tune.SepRadius);
                if (d < tune.KohRadius) { kohSumme += andere; kohN++; }
                if (d < tune.AusrRadius) { ausrSumme += schafe[j].Vel; ausrN++; }
            }
            var kraft = sep * tune.SepKraft;
            if (kohN > 0) kraft += (kohSumme / (float)kohN - pos).LimitLength(1f) * tune.KohKraft;
            if (ausrN > 0) kraft += (ausrSumme / (float)ausrN - vel).LimitLength(1f) * tune.AusrKraft;
            return kraft;
        }

        /// <summary>Flee from the rider — with a herding twist: DRUCK_ZUM_TOR of the force points at the gate.</summary>
        private static V2 Flucht(V2 pos, V2 reiter, Tune tune, V2 tor)
        {
            double d = pos.DistanceTo(reiter);
            var radius = tune.FluchtRadius;
            if (d >= radius || d < 0.001) return V2.Zero;
            var staerke = tune.FluchtKraft * (1.0 - d / radius);
            var weg = (pos - reiter) / d;
            var zumTor = pos.DistanceTo(tor) > 0.001 ? (tor - pos).Normalized() : V2.Zero;
            return weg * staerke + zumTor * staerke * tune.DruckZumTor;
        }

        private static V2 WandKraft(V2 pos, (double x, double z) feld, Tune tune)
        {
            var rand = tune.WandRand;
            float kx = 0f, ky = 0f;
            if (pos.X > feld.x - rand) kx = (float)(-(pos.X - (feld.x - rand)) / rand);
            else if (pos.X < -feld.x + rand) kx = (float)((-feld.x + rand - pos.X) / rand);
            if (pos.Y > feld.z - rand) ky = (float)(-(pos.Y - (feld.z - rand)) / rand);
            else if (pos.Y < -feld.z + rand) ky = (float)((-feld.z + rand - pos.Y) / rand);
            return new V2(kx, ky) * tune.WandKraft;
        }

        /// <summary>A soft funnel in front of the gate: nearby sheep are drawn to the gate centre.</summary>
        private static V2 TorSog(V2 pos, JObject level, Tune tune)
        {
            var tor = TorPos(level);
            double d = pos.DistanceTo(tor);
            var radius = tune.TorSogRadius;
            if (d >= radius || d < 0.001 || IstImPferch(level, pos, 0.0)) return V2.Zero;
            return (tor - pos) / d * tune.TorSogKraft * (1.0 - d / radius);
        }

        /// <summary>Movement + pen walls, axis-separated: a blocked axis stops only itself (sheep slide along walls).</summary>
        private static Schaf Bewege(Schaf s, V2 pos, V2 vel, double dt, JObject level, (double x, double z) feld)
        {
            var p = Pferch.Of(level);
            var drinAlt = s.Drin;
            float neuX = pos.X, neuY = pos.Y;
            float vx = vel.X, vy = vel.Y;
            var nx = Math.Min(Math.Max(pos.X + vel.X * dt, -feld.x), feld.x);
            if (WandBlockiert(pos, new V2(nx, pos.Y), p, drinAlt)) vx = 0f;
            else neuX = (float)nx;
            var nz = Math.Min(Math.Max(pos.Y + vel.Y * dt, -feld.z), feld.z);
            if (WandBlockiert(new V2(neuX, pos.Y), new V2(neuX, (float)nz), p, drinAlt)) vy = 0f;
            else neuY = (float)nz;
            var neu = new V2(neuX, neuY);
            return new Schaf { Pos = neu, Vel = new V2(vx, vy), Phase = s.Phase, Drin = drinAlt || IstImPferch(level, neu) };
        }

        private static bool WandBlockiert(V2 von, V2 nach, Pferch p, bool drin)
        {
            var innenV = ImRect(von, p);
            var innenN = ImRect(nach, p);
            if (innenV == innenN) return false;
            if (drin) return innenV;
            if (!innenN) return false;
            var suedZ = p.Z + p.T * 0.5;
            var imTor = Math.Abs(nach.X - p.X) < p.Tor * 0.5 - 0.1;
            return !(imTor && von.Y >= suedZ - 0.001);
        }

        private static bool ImRect(V2 pos, Pferch p) => Math.Abs(pos.X - p.X) < p.W * 0.5 && Math.Abs(pos.Y - p.Z) < p.T * 0.5;

        public static double Num(JObject o, string key, double fallback)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            return t.Type == JTokenType.Integer || t.Type == JTokenType.Float ? t.Value<double>() : fallback;
        }
    }
}
