using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Angelteich — port of GOOBY/src/minigames/games/fishingPond.logic.js (§C6.1 #10 / §C6 species / §C10.2 rare
    /// variants): 90-s round, HOLD lowers the hook, RELEASE catches the nearest swimmer in range; S/M/L give
    /// 2/3/5, a boot −3; big fish need ~5 reel taps within 2 s or the line breaks.
    /// </summary>
    public static class FishingPond
    {
        public sealed class Tune
        {
            public double DurationSec = 90.0;
            public int ReelTaps = 5;
            public double ReelWindowSec = 2.0, ReelMaxFrameSec = 0.1, CatchRadius = 0.55, HookX = 0.0, MaxDepth = 3.9;
            public double LowerSpeed = 2.1, RaiseSpeed = 3.4, FishDepthMin = 0.55, FishDepthMax = 3.7, PondHalfW = 1.8;
            public int FishCount = 7;
            public double RespawnSec = 1.2, BootMinGapSec = 14.0, BootChance = 0.6, BootSpeed = 0.28;
            public bool Endless = false;
            public int EndlessFailureLimit = 3;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Fishing = new Tune();

        public struct Size
        {
            public string Kind;
            public int Weight;
            public double Scale, SpeedMin, SpeedMax;
        }

        public static readonly Size[] Sizes =
        {
            new Size { Kind = "S", Weight = 45, Scale = 0.34, SpeedMin = 0.5, SpeedMax = 0.85 },
            new Size { Kind = "M", Weight = 35, Scale = 0.5, SpeedMin = 0.38, SpeedMax = 0.62 },
            new Size { Kind = "L", Weight = 20, Scale = 0.72, SpeedMin = 0.28, SpeedMax = 0.48 },
        };

        public static readonly Dictionary<string, string[]> FishSpecies = new Dictionary<string, string[]>
        {
            { "S", new[] { "tinyMinnow", "blueDace", "sunnyCarp" } },
            { "M", new[] { "pinkKoi", "stripeBass" } },
            { "L", new[] { "bigWhopper", "nightEel" } },
        };

        public const double GoldenFishChance = 0.02;
        public const double NightEelChance = 0.5;
        public const int RareSetBonus = 15;

        public struct Rare
        {
            public string Id, Kind, CollectionId;
            public int Weight;
        }

        public static readonly Rare[] RareSpecies =
        {
            new Rare { Id = "pearlMinnow", Kind = "S", Weight = 8, CollectionId = "tinyMinnow" },
            new Rare { Id = "sunsetKoi", Kind = "M", Weight = 5, CollectionId = "pinkKoi" },
            new Rare { Id = "gildedWhopper", Kind = "L", Weight = 2, CollectionId = "goldenFish" },
        };

        public static readonly Dictionary<string, string> SpeciesColors = new Dictionary<string, string>
        {
            { "tinyMinnow", "#9FB2C8" }, { "blueDace", "#5B8BD9" }, { "sunnyCarp", "#E8A33D" }, { "pinkKoi", "#E88BB0" },
            { "stripeBass", "#7A9E7E" }, { "bigWhopper", "#4E6E8E" }, { "nightEel", "#6E5E9E" }, { "goldenFish", "#FFD24A" },
            { "pearlMinnow", "#D8F5F2" }, { "sunsetKoi", "#FF8A6B" }, { "gildedWhopper", "#F7C948" },
        };

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var spawnMult = hard ? 0.85 : 1.2;
            var windowMult = hard ? 0.8 : 1.25;
            var o = tune.Clone();
            o.DurationSec = hard ? tune.DurationSec : tune.DurationSec * 1.2;
            o.RespawnSec = tune.RespawnSec * spawnMult;
            o.BootMinGapSec = tune.BootMinGapSec * spawnMult;
            o.ReelWindowSec = Math.Max(0.35, tune.ReelWindowSec * windowMult);
            o.CatchRadius = Math.Max(tune.CatchRadius * 0.55, tune.CatchRadius * windowMult);
            o.Endless = mode == "endless";
            o.Mode = mode;
            return o;
        }

        public static double LowerDepth(double depth, double dt, Tune tune) => Math.Min(tune.MaxDepth, depth + tune.LowerSpeed * dt);

        public static int CatchValue(string kind)
        {
            switch (kind)
            {
                case "S": return 2;
                case "M": return 3;
                case "L": return 5;
                case "boot": return -3;
                default: return 0;
            }
        }

        public static bool NeedsReel(string kind) => kind == "L";

        public static string ReelResolve(int tapCount, double elapsedSec, Tune tune)
        {
            if (tapCount >= tune.ReelTaps) return "caught";
            if (elapsedSec >= tune.ReelWindowSec) return "escaped";
            return "reeling";
        }

        public static double AdvanceReelElapsed(double elapsedSec, double dt, Tune tune) => elapsedSec + Math.Min(Math.Max(0.0, dt), tune.ReelMaxFrameSec);

        public static string RollFishKind(Func<double> rng)
        {
            var total = 0.0;
            foreach (var s in Sizes) total += s.Weight;
            var roll = rng() * total;
            foreach (var s in Sizes)
            {
                roll -= s.Weight;
                if (roll < 0.0) return s.Kind;
            }
            return "S";
        }

        public static double FishSpeedFor(string kind, Func<double> rng)
        {
            foreach (var s in Sizes)
                if (s.Kind == kind) return s.SpeedMin + rng() * (s.SpeedMax - s.SpeedMin);
            return Sizes[0].SpeedMin;
        }

        public static bool ShouldSpawnBoot(Func<double> rng, double sinceLastBoot, Tune tune) =>
            sinceLastBoot >= tune.BootMinGapSec && rng() < tune.BootChance;

        public static int ApplyCatch(int score, int value) => Math.Max(0, score + value);

        public static string RollSpecies(string kind, Func<double> rng, bool night = false)
        {
            if (kind == "L")
            {
                if (rng() < GoldenFishChance) return "goldenFish";
                if (night && rng() < NightEelChance) return "nightEel";
                return "bigWhopper";
            }
            var options = FishSpecies.TryGetValue(kind, out var o) ? o : FishSpecies["S"];
            return options[Math.Min(options.Length - 1, (int)Math.Floor(rng() * options.Length))];
        }

        public static (string species, string collectionId, bool rare) RollSpeciesDetail(string kind, Func<double> rng, bool night = false)
        {
            foreach (var def in RareSpecies)
            {
                if (def.Kind != kind) continue;
                if (rng() * 100.0 < def.Weight) return (def.Id, def.CollectionId, true);
                break;
            }
            var species = RollSpecies(kind, rng, night);
            return (species, species, false);
        }

        public static string SpeciesCollectionId(string species)
        {
            foreach (var def in RareSpecies) if (def.Id == species) return def.CollectionId;
            return species;
        }

        public static int RareSetBonusFor(ICollection<string> species)
        {
            foreach (var def in RareSpecies) if (!species.Contains(def.Id)) return 0;
            return RareSetBonus;
        }

        public static JObject SimulateAutoplay(int seed, string mode = "normal")
        {
            var tune = ApplyDifficulty(Fishing, mode);
            var rng = new GoobyRng(seed);
            Func<double> stream = () => rng.Next();
            var duration = tune.Endless ? 120.0 : tune.DurationSec;
            var attempts = (int)Math.Floor(duration / (1.75 + tune.RespawnSec * 0.25));
            int score = 0, failures = 0;
            for (var i = 0; i < attempts; i++)
            {
                var kind = RollFishKind(stream);
                var accuracy = Math.Min(0.94, 0.72 * (tune.CatchRadius / Fishing.CatchRadius));
                if (rng.Next() > accuracy) continue;
                if (kind == "L" && rng.Next() > Math.Min(0.96, tune.ReelWindowSec / 2.2))
                {
                    failures += 1;
                    continue;
                }
                score = ApplyCatch(score, CatchValue(kind));
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["failures"] = failures };
        }
    }
}
