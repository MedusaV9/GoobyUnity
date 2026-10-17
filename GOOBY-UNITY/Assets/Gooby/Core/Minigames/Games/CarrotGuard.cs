using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Karottenwache — port of GOOBY/src/minigames/games/carrotGuard.logic.js (§C6.1 #4): 3×3 mounds, moles
    /// stay up 0.9 s → 0.5 s, 10 carrots in the bed, hit +1, every 5-combo +3; round ends after 45 s or when
    /// all carrots are gone. Every 20 bonks the mole king appears (3 taps, +8 + 2 coins). Coin row /3, 4..25.
    /// </summary>
    public static class CarrotGuard
    {
        public sealed class Tune
        {
            public double DurationSec = 45.0;
            public int Grid = 3, Carrots = 10;
            public double UpTimeStart = 0.9, UpTimeEnd = 0.5;
            public int HitPoints = 1, ComboBonusAt = 5, ComboBonus = 3;
            public double SpawnStartSec = 1.3, SpawnEndSec = 0.75, DoubleChanceEnd = 0.35, PopSec = 0.16;
            public int KingEveryBonks = 20, KingTaps = 3, KingPoints = 8, KingCoinDrop = 2, KingScorePerCoin = 3;
            public double TapDebounceSec = 0.075, WhiffCooldownSec = 0.18;
            public double SpawnIntervalMult = 1.0, WindowMult = 1.0, DurationMult = 1.0;
            public bool Endless = false;
            public int EndlessStolen = 3;
            public double BotReactionSec = 0.34;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Guard = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double spawn, window, duration;
            bool endless;
            switch (mode)
            {
                case "easy": spawn = 1.2; window = 1.25; duration = 1.2; endless = false; break;
                case "hard": spawn = 0.85; window = 0.8; duration = 1.0; endless = false; break;
                case "endless": spawn = 0.85; window = 0.8; duration = 1.0; endless = true; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * duration;
            o.SpawnStartSec = tune.SpawnStartSec * spawn;
            o.SpawnEndSec = tune.SpawnEndSec * spawn;
            o.UpTimeStart = Math.Max(0.35, tune.UpTimeStart * window);
            o.UpTimeEnd = Math.Max(0.35, tune.UpTimeEnd * window);
            o.SpawnIntervalMult = spawn;
            o.WindowMult = window;
            o.DurationMult = duration;
            o.Endless = endless;
            o.BotReactionSec = Math.Min(tune.BotReactionSec, Math.Max(0.24, tune.BotReactionSec * window));
            o.Mode = mode;
            return o;
        }

        private static double T(double elapsed, double duration) => Math.Min(1.0, Math.Max(0.0, elapsed / duration));

        public static double UpTimeAt(double elapsed, double duration, Tune tune) =>
            tune.UpTimeStart + (tune.UpTimeEnd - tune.UpTimeStart) * T(elapsed, duration);

        public static double SpawnIntervalAt(double elapsed, double duration, Tune tune) =>
            tune.SpawnStartSec + (tune.SpawnEndSec - tune.SpawnStartSec) * T(elapsed, duration);

        public static double DoubleChanceAt(double elapsed, double duration, Tune tune) => tune.DoubleChanceEnd * T(elapsed, duration);

        public static int ComboBonusFor(int combo) => combo > 0 && combo % Guard.ComboBonusAt == 0 ? Guard.ComboBonus : 0;

        public static (int score, int combo, int bonus) ApplyBonk(int score, int combo)
        {
            var c = combo + 1;
            var bonus = ComboBonusFor(c);
            return (score + Guard.HitPoints + bonus, c, bonus);
        }

        public static (int carrots, int combo) ApplyEscape(int carrots, int combo) => (Math.Max(0, carrots - 1), 0);

        public static bool IsRoundOver(double elapsed, int carrots, double duration, Tune tune)
        {
            if (tune.Endless) return tune.Carrots - carrots >= tune.EndlessStolen;
            return elapsed >= duration || carrots <= 0;
        }

        public static bool IsKingDue(int bonks, int kingsSpawned) => bonks >= (kingsSpawned + 1) * Guard.KingEveryBonks;

        public static (int score, int combo, int hp, bool complete, int bonus, int gained) ApplyKingTap(int score, int combo, int hp)
        {
            var nhp = Math.Max(0, hp - 1);
            if (nhp > 0) return (score, combo, nhp, false, 0, 0);
            var c = combo + 1;
            var bonus = ComboBonusFor(c);
            var gained = Guard.KingPoints + Guard.KingCoinDrop * Guard.KingScorePerCoin + bonus;
            return (score + gained, c, 0, true, bonus, gained);
        }

        public static bool AcceptsTapAfter(double sinceLastSec, double cooldownSec = 0.075) =>
            double.IsNaN(sinceLastSec) || double.IsInfinity(sinceLastSec) || sinceLastSec >= cooldownSec;

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Guard, mode);
            var rng = new GoobyRng(seed);
            var accuracy = mode == "easy" ? 0.96 : (mode == "hard" || mode == "endless" ? 0.82 : 0.93);
            var elapsed = 0.0;
            var carrots = tune.Carrots;
            int score = 0, combo = 0;
            var duration = tune.DurationSec;
            while (!IsRoundOver(elapsed, carrots, duration, tune) && elapsed < 240.0)
            {
                var count = rng.Next() < DoubleChanceAt(elapsed, duration, tune) ? 2 : 1;
                for (var i = 0; i < count; i++)
                {
                    if (rng.Next() < accuracy)
                    {
                        var r = ApplyBonk(score, combo);
                        score = r.score; combo = r.combo;
                    }
                    else
                    {
                        var e = ApplyEscape(carrots, combo);
                        carrots = e.carrots; combo = e.combo;
                    }
                }
                elapsed += SpawnIntervalAt(elapsed, duration, tune);
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["elapsed"] = elapsed, ["stolen"] = tune.Carrots - carrots };
        }
    }
}
