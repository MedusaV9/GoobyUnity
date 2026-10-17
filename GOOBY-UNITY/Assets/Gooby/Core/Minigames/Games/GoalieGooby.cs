using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Torwart-Gooby — port of GOOBY/src/minigames/games/goalieGooby.logic.js (PLAN2 §C1.2 #7, §C10.2): 5 lanes,
    /// telegraph 0.9 s → 0.45 s, lobs (swipe up) and rollers (swipe down), save +4 (+2 super save in the last
    /// 0.15 s), 3 goals end early, else 60 s; every 10 saves the crowd cheers and everything gets 10 % faster.
    /// The last ten seconds are a five-shot penalty shoot-out.
    /// </summary>
    public static class GoalieGooby
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0;
            public int Lanes = 5;
            public double TelegraphStartSec = 0.9, TelegraphEndSec = 0.45, TelegraphRampSec = 60.0;
            public int SavePts = 4, SuperPts = 2;
            public double SuperWindowSec = 0.15;
            public int MaxGoals = 3, CheerEverySaves = 10;
            public double CheerSpeedMult = 1.1, MixFromSec = 8.0, LobChance = 0.22, RollerChance = 0.22;
            public double FlightSec = 0.55, GapSec = 0.8, DiveHoldSec = 0.45, VkindMinPx = 24.0, LaneInnerDeg = 18.0, LaneOuterDeg = 54.0;
            public double AutoplayLeadSec = 0.2, AutoplayJitterSec = 0.1, AutoplayErrBase = 0.07, AutoplayErrRamp = 0.48;
            public double ShootoutStartSec = 50.0;
            public int ShootoutShots = 5;
            public double ShootoutTelegraphSec = 0.38, ShootoutFlightSec = 0.42, ShootoutGapSec = 0.28;
            public int ShootoutSaveMult = 2;
            public bool Endless = false;
            public int EndlessGoals = 3;
            public double RenderScale = 1.0, HitboxMult = 1.0, AutoplaySkillMult = 0.12;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Goalie = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double spawnMult, windowMult, durationMult, botSkillMult;
            switch (mode)
            {
                case "easy": spawnMult = 1.2; windowMult = 1.25; durationMult = 1.2; botSkillMult = 0.08; break;
                case "hard":
                case "endless": spawnMult = 0.85; windowMult = 0.8; durationMult = 1.0; botSkillMult = 0.2; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * durationMult;
            o.GapSec = tune.GapSec * spawnMult;
            o.TelegraphStartSec = Math.Max(0.35, tune.TelegraphStartSec * windowMult);
            o.TelegraphEndSec = Math.Max(0.35, tune.TelegraphEndSec * windowMult);
            o.DiveHoldSec = Math.Max(0.35, tune.DiveHoldSec * windowMult);
            o.ShootoutTelegraphSec = Math.Max(0.35, tune.ShootoutTelegraphSec * windowMult);
            o.Endless = mode == "endless";
            o.AutoplaySkillMult = botSkillMult;
            o.Mode = mode;
            return o;
        }

        public static Tune ApplyRiesenGooby(Tune tune, double scale = 1.0, double hitboxMult = 1.0)
        {
            var safeScale = Math.Max(1.0, scale);
            var safeHitbox = Math.Max(1.0, hitboxMult);
            var o = tune.Clone();
            o.RenderScale = safeScale;
            o.HitboxMult = safeHitbox;
            o.DiveHoldSec = tune.DiveHoldSec * safeHitbox;
            return o;
        }

        public static double TelegraphSecAt(double elapsed, Tune tune)
        {
            var t = Math.Max(0.0, Math.Min(1.0, elapsed / tune.TelegraphRampSec));
            return tune.TelegraphStartSec + (tune.TelegraphEndSec - tune.TelegraphStartSec) * t;
        }

        public static double SpeedMultAt(int cheers, Tune tune) => Math.Pow(tune.CheerSpeedMult, Math.Max(0, cheers));
        public static double FlightSecAt(int cheers, Tune tune) => tune.FlightSec / SpeedMultAt(cheers, tune);

        public static (int lane, string kind) RollKick(Func<double> rng, double elapsed)
        {
            var lanes = Goalie.Lanes;
            var lane = Math.Min(lanes - 1, (int)Math.Floor(rng() * lanes));
            if (elapsed < Goalie.MixFromSec) return (lane, "straight");
            var r = rng();
            if (r < Goalie.LobChance) return (lane, "lob");
            if (r < Goalie.LobChance + Goalie.RollerChance) return (lane, "roller");
            return (lane, "straight");
        }

        public static int LaneFromSwipe(double dx, double dy)
        {
            var deg = Math.Atan2(dx, Math.Max(1e-6, Math.Abs(dy))) * 180.0 / Math.PI;
            if (deg < -Goalie.LaneOuterDeg) return 0;
            if (deg < -Goalie.LaneInnerDeg) return 1;
            if (deg <= Goalie.LaneInnerDeg) return 2;
            if (deg <= Goalie.LaneOuterDeg) return 3;
            return 4;
        }

        public static string VKindFromSwipe(double dy)
        {
            if (dy <= -Goalie.VkindMinPx) return "up";
            if (dy >= Goalie.VkindMinPx) return "down";
            return "mid";
        }

        public static bool SaveMatches(int kickLane, string kickKind, int diveLane, string diveV)
        {
            if (kickLane != diveLane) return false;
            if (kickKind == "lob") return diveV == "up";
            if (kickKind == "roller") return diveV == "down";
            return true;
        }

        public static bool DiveCovers(double diveT, double arriveT, Tune tune)
        {
            var lead = arriveT - diveT;
            return lead >= 0.0 && lead <= tune.DiveHoldSec + 1e-9;
        }

        public static bool IsSuperSave(double diveT, double arriveT, Tune tune)
        {
            var lead = arriveT - diveT;
            return lead >= 0.0 && lead <= tune.SuperWindowSec + 1e-9;
        }

        public static int SavePoints(bool superSave, bool shootout, Tune tune)
        {
            var b = tune.SavePts + (superSave ? tune.SuperPts : 0);
            return b * (shootout ? tune.ShootoutSaveMult : 1);
        }

        public static bool IsShootoutAt(double elapsed, Tune tune)
        {
            if (tune.Endless) return false;
            return elapsed >= tune.ShootoutStartSec && elapsed <= tune.DurationSec;
        }

        public static double ShootoutShotAt(int index, Tune tune)
        {
            var cycle = tune.ShootoutTelegraphSec + tune.ShootoutFlightSec + tune.ShootoutGapSec;
            return tune.ShootoutStartSec + Math.Max(0, index) * cycle;
        }

        public static int CheersAt(int saves) => (int)Math.Floor((double)Math.Max(0, saves) / Goalie.CheerEverySaves);

        public static double AutoplayErrAt(double telegraphSec, Tune tune, double skillMult = 1.0)
        {
            var start = tune.TelegraphStartSec;
            var span = start - tune.TelegraphEndSec;
            var t = span <= 0.0 ? 1.0 : Math.Max(0.0, Math.Min(1.0, (start - telegraphSec) / span));
            return (tune.AutoplayErrBase + tune.AutoplayErrRamp * t) * skillMult;
        }

        public static bool EndlessShouldEnd(int goals, Tune tune) => tune.Endless && goals >= tune.EndlessGoals;

        public static JObject SimulateAutoplay(int seed, string mode = "normal")
        {
            var tune = ApplyDifficulty(Goalie, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            int score = 0, saves = 0, goals = 0;
            var limit = tune.Endless ? 600.0 : tune.DurationSec;
            while (elapsed < limit && goals < tune.EndlessGoals)
            {
                var shootout = IsShootoutAt(elapsed, tune);
                var telegraph = shootout ? tune.ShootoutTelegraphSec : TelegraphSecAt(elapsed, tune);
                var flight = shootout ? tune.ShootoutFlightSec : FlightSecAt(CheersAt(saves), tune);
                var gap = shootout ? tune.ShootoutGapSec : tune.GapSec;
                elapsed += telegraph + flight + gap;
                var error = AutoplayErrAt(telegraph, tune, tune.AutoplaySkillMult);
                if (rng.Next() < error) goals += 1;
                else
                {
                    saves += 1;
                    score += SavePoints(false, shootout, tune);
                }
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["saves"] = saves, ["goals"] = goals, ["elapsed"] = elapsed };
        }
    }
}
