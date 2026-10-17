using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames
{
    /// <summary>
    /// Registry of the pure, deterministic minigame simulations (the "release bots"). Every entry is
    /// certified against Tests/Fixtures/expected/&lt;id&gt;.json, generated from the ORIGINAL web logic
    /// (tools/cross_check.mjs): 4 modes × 50 seeds, ints exact, floats ±1e-9.
    /// </summary>
    public sealed class MinigameSpec
    {
        public string Id;
        public string[] IntFields = Array.Empty<string>();
        public string[] FloatFields = Array.Empty<string>();
        public string[] StringFields = Array.Empty<string>();
        /// <summary>(seed, mode) → result object with the web field names.</summary>
        public Func<int, string, JObject> Autoplay;
        /// <summary>Optional: mode → tune object with the web key names (fixture "tune" section).</summary>
        public Func<string, JObject> TuneFor;
    }

    public static class MinigameSims
    {
        private static readonly List<MinigameSpec> Specs = new List<MinigameSpec>
        {
            new MinigameSpec { Id = "teaParty", IntFields = new[] { "score", "cups", "spills" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.TeaParty.SimulateAutoplay(m, s), TuneFor = m => JObject.FromObject(Games.TeaParty.ApplyDifficulty(Games.TeaParty.Tea, m)) },
            new MinigameSpec { Id = "hideSeek", IntFields = new[] { "score", "waves", "found", "expired" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.HideSeek.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "goobySays", IntFields = new[] { "rounds", "score" }, Autoplay = (s, m) => Games.GoobySays.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "gardenRush", IntFields = new[] { "score", "withered" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.GardenRush.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "burgerBuild", IntFields = new[] { "completed", "expired" }, FloatFields = new[] { "score" }, Autoplay = (s, m) => Games.BurgerBuild.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "bunnyHop", IntFields = new[] { "score", "gates" }, Autoplay = (s, m) => Games.BunnyHop.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "memoryMatch", IntFields = new[] { "score", "rawScore", "misses" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.MemoryMatch.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "bubblePop", IntFields = new[] { "score", "spikyPops" }, Autoplay = (s, m) => Games.BubblePop.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "carrotGuard", IntFields = new[] { "score", "stolen" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.CarrotGuard.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "trampoline", IntFields = new[] { "score", "failures" }, Autoplay = (s, m) => Games.Trampoline.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "lanternFloat", IntFields = new[] { "score", "rings", "hits", "golds", "fireflies", "bumps" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.LanternFloat.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "pancakeTower", IntFields = new[] { "score", "layers" }, FloatFields = new[] { "width" }, Autoplay = (s, m) => Games.PancakeTower.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "carrotCatch", IntFields = new[] { "score", "missedCarrots" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.CarrotCatch.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "goalieGooby", IntFields = new[] { "score", "saves", "goals" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.GoalieGooby.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "starHopper", IntFields = new[] { "score", "pickups" }, FloatFields = new[] { "distance" }, Autoplay = (s, m) => Games.StarHopper.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "fishingPond", IntFields = new[] { "score", "failures" }, Autoplay = (s, m) => Games.FishingPond.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "veggieChop", IntFields = new[] { "score", "misses", "junkHits" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.VeggieChop.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "danceParty", IntFields = new[] { "score", "tally.perfect", "tally.good", "tally.miss", "tally.combo", "tally.maxCombo", "tally.bonus" }, Autoplay = (s, m) => Games.DanceParty.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "miniGolf", IntFields = new[] { "score", "overPar" }, Autoplay = (s, m) => Games.MiniGolf.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "basketBounce", IntFields = new[] { "score", "missStreak", "baskets" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.BasketBounce.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "deliveryRush", IntFields = new[] { "score", "crashes", "coinPoints" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.DeliveryRush.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "runner", IntFields = new[] { "score", "hits" }, FloatFields = new[] { "elapsed", "meters" }, Autoplay = (s, m) => Games.Runner.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "ghostHunt", IntFields = new[] { "score", "caught", "missed", "escapedWaves", "booBonuses" }, FloatFields = new[] { "time" }, Autoplay = (s, m) => Games.GhostHunt.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "pipeFlow", IntFields = new[] { "score", "solved", "failures" }, Autoplay = (s, m) => Games.PipeFlow.SimulateAutoplay(s, m) },
            new MinigameSpec { Id = "snailMail", IntFields = new[] { "score", "deliveries", "splashes", "flowersPicked" }, FloatFields = new[] { "elapsed" }, Autoplay = (s, m) => Games.SnailMail.SimulateAutoplay(m, s) },
            new MinigameSpec { Id = "rocketRescue", IntFields = new[] { "score", "rescued", "softLandings", "hardLandings" }, FloatFields = new[] { "fuelLeft", "elapsed" }, StringFields = new[] { "endReason" }, Autoplay = (s, m) => Games.RocketRescue.SimulateAutoplay(m, s) },
        };

        public static IReadOnlyList<MinigameSpec> All => Specs;

        public static MinigameSpec Find(string id)
        {
            foreach (var s in Specs) if (s.Id == id) return s;
            return null;
        }

        /// <summary>JS-style uniform float helper: lo + rng*(hi-lo) is intentionally NOT used here — each game keeps its web op order.</summary>
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
