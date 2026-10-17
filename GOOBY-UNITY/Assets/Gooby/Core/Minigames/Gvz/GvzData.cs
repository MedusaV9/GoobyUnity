using System.Collections.Generic;
using Gooby.Core.Content;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// GvZ data (port of gvz_data.gd): balance and the 15 campaign levels from <c>Resources/gooby/minigames/gvz/</c>
    /// (verbatim copies of the Godot JSONs) plus the structural validation. Numbers are read as ints at the use site
    /// (<see cref="GvzJson"/>); level times "t" stay seconds until <see cref="GvzLogic.NewRun"/> turns them into ticks.
    /// </summary>
    public static class GvzData
    {
        public const string BalancePath = "minigames/gvz/gvz_balance";
        public const string LevelsPath = "minigames/gvz/gvz_levels";
        public const string PvpPath = "minigames/gvz/gvz_pvp";
        public const int LevelCount = 15;

        public static JObject LoadBalance() => (JObject)ContentSource.LoadObject(BalancePath).DeepClone();

        /// <summary>The online PvP rules (gvz_pvp.json: survive timer, mud drip + escalation, zombie costs/cooldowns).</summary>
        public static JObject LoadPvp() => (JObject)ContentSource.LoadObject(PvpPath).DeepClone();

        public static List<JObject> LoadLevels()
        {
            var list = new List<JObject>();
            if (!(ContentSource.LoadObject(LevelsPath)["levels"] is JArray arr)) return list;
            foreach (var t in arr) if (t is JObject level) list.Add((JObject)level.DeepClone());
            return list;
        }

        public static JObject LevelById(IEnumerable<JObject> levels, int id)
        {
            foreach (var level in levels) if (GvzJson.I(level, "id", 0) == id) return level;
            return null;
        }

        /// <summary>Structural validation of the levels against the balance (empty list = fine).</summary>
        public static List<string> ValidateLevels(IList<JObject> levels, JObject balance)
        {
            var errors = new List<string>();
            if (levels.Count != LevelCount) errors.Add($"erwartet {LevelCount} Level, gefunden {levels.Count}");
            var towers = balance?["towers"] as JObject ?? new JObject();
            var zombies = balance?["zombies"] as JObject ?? new JObject();
            var prevUnlocks = new List<string>();
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                var id = GvzJson.I(level, "id", -1);
                var prefix = "L" + id.ToString("00");
                if (id != i + 1) errors.Add(prefix + ": id nicht fortlaufend");
                var lanes = GvzJson.Ints(level["lanes"]);
                if (lanes.Count == 0) errors.Add(prefix + ": keine aktiven Reihen");
                var unlocks = GvzJson.Strings(level["unlock_towers"]);
                foreach (var tower in unlocks) if (towers[tower] == null) errors.Add($"{prefix}: unbekannter Turm '{tower}'");
                foreach (var tower in prevUnlocks) if (!unlocks.Contains(tower)) errors.Add($"{prefix}: Freischaltung '{tower}' verloren");
                foreach (var tower in GvzJson.Strings(level["new_towers"]))
                    if (!unlocks.Contains(tower) || prevUnlocks.Contains(tower)) errors.Add($"{prefix}: new_tower '{tower}' inkonsistent");
                prevUnlocks = unlocks;
                var lastT = -1.0;
                foreach (var s in level["spawns"] as JArray ?? new JArray())
                {
                    if (!(s is JObject spawn)) { errors.Add(prefix + ": Spawn ist kein Dictionary"); continue; }
                    var type = GvzJson.S(spawn, "type", "");
                    if (zombies[type] == null) errors.Add($"{prefix}: unbekannter Zombie '{type}'");
                    var lane = GvzJson.I(spawn, "lane", -1);
                    if (lane != -1 && !lanes.Contains(lane)) errors.Add($"{prefix}: Spawn-Reihe {lane} inaktiv");
                    var t = GvzJson.D(spawn, "t", -1.0);
                    if (t < lastT) errors.Add(prefix + ": Spawns nicht zeitlich sortiert");
                    lastT = t;
                }
                if (GvzJson.B(level["mods"] as JObject, "conveyor", false) && !(level["conveyor"] is JObject))
                    errors.Add(prefix + ": conveyor-Mod ohne conveyor-Daten");
                if (level["boss"] is JObject boss && zombies[GvzJson.S(boss, "type", "")] == null) errors.Add(prefix + ": unbekannter Boss-Typ");
            }
            return errors;
        }
    }
}
