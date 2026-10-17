using System.Collections.Generic;
using Gooby.Core.Content;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gobnom
{
    /// <summary>
    /// GOB NOM data (port of gobnom_data.gd): balance and the 15 campaign + 10 coop levels from
    /// <c>Resources/gooby/minigames/gobnom/</c> (verbatim copies of the Godot JSONs), the doc-G §5.3 introduction curve
    /// and the structural validation. Everything stays double — continuous 2D physics.
    /// </summary>
    public static class GobnomData
    {
        public const string BalancePath = "minigames/gobnom/gobnom_balance";
        public const string LevelsPath = "minigames/gobnom/gobnom_levels";
        public const int CampaignCount = 15, CoopCount = 10;
        public const string TrackCampaign = "campaign", TrackCoop = "coop";

        /// <summary>Element introduced by each campaign level (doc G §5.3 table).</summary>
        public static readonly IReadOnlyDictionary<int, string> CampaignIntro = new Dictionary<int, string>
        {
            [1] = "rope", [2] = "ropes", [3] = "timing", [4] = "bubble", [5] = "bubble_rope", [6] = "cushion",
            [7] = "cushion_combo", [8] = "slider", [9] = "shooter", [10] = "spikes", [11] = "fan", [12] = "cloud",
            [13] = "all", [14] = "cut_limit", [15] = "finale",
        };

        public static JObject LoadBalance() => (JObject)ContentSource.LoadObject(BalancePath).DeepClone();

        public static List<JObject> LoadCampaign() => LoadTrack(TrackCampaign);

        public static List<JObject> LoadCoop() => LoadTrack(TrackCoop);

        public static List<JObject> LoadTrack(string track)
        {
            var list = new List<JObject>();
            if (!(ContentSource.LoadObject(LevelsPath)[track] is JArray arr)) return list;
            foreach (var t in arr)
            {
                if (!(t is JObject level)) continue;
                var copy = (JObject)level.DeepClone();
                copy["kind"] = track;
                list.Add(copy);
            }
            return list;
        }

        public static JObject LevelById(IEnumerable<JObject> levels, int id)
        {
            foreach (var level in levels)
                if (GobnomLogic.Int(level, "id", 0) == id) return level;
            return null;
        }

        /// <summary>Structural validation of both tracks against the balance — a list of readable errors (empty = fine).</summary>
        public static List<string> ValidateLevels(IList<JObject> campaign, IList<JObject> coop, JObject balance)
        {
            var errors = new List<string>();
            if (campaign.Count != CampaignCount) errors.Add($"erwartet {CampaignCount} Kampagnen-Level, gefunden {campaign.Count}");
            if (coop.Count != CoopCount) errors.Add($"erwartet {CoopCount} Coop-Level, gefunden {coop.Count}");
            var world = balance?["world"] as JObject ?? new JObject();
            for (var i = 0; i < campaign.Count; i++)
            {
                var level = campaign[i];
                var id = GobnomLogic.Int(level, "id", -1);
                var prefix = "L" + id.ToString("00");
                if (id != i + 1) errors.Add(prefix + ": id nicht fortlaufend");
                CampaignIntro.TryGetValue(i + 1, out var intro);
                if (GobnomLogic.Str(level, "intro", "") != (intro ?? "")) errors.Add(prefix + ": intro-Tag weicht von der Doc-G-§5.3-Tabelle ab");
                ValidateLevel(level, world, prefix, false, errors);
            }
            for (var i = 0; i < coop.Count; i++)
            {
                var level = coop[i];
                var id = GobnomLogic.Int(level, "id", -1);
                var prefix = "CN" + id;
                if (id != i + 1) errors.Add(prefix + ": id nicht fortlaufend");
                ValidateLevel(level, world, prefix, true, errors);
            }
            return errors;
        }

        private static void ValidateLevel(JObject level, JObject world, string prefix, bool coop, List<string> errors)
        {
            if (!(level["candy"] is JObject)) errors.Add(prefix + ": candy fehlt");
            if (!(level["mouth"] is JObject)) errors.Add(prefix + ": mouth fehlt");
            var jars = level["jars"] as JArray ?? new JArray();
            if (jars.Count != 3) errors.Add($"{prefix}: genau 3 Nutella-Gläser erwartet (sind {jars.Count})");
            var actions = level["solution"]?["actions"] as JArray ?? new JArray();
            if (actions.Count == 0) errors.Add(prefix + ": Lösungs-Plan fehlt (Lösbarkeits-Beweis!)");
            var w = GobnomLogic.Num(world, "w", 960.0);
            var h = GobnomLogic.Num(world, "h", 540.0);
            foreach (var key in new[] { "ropes", "bubbles", "cushions", "fans", "shooters", "jars" })
                foreach (var row in level[key] as JArray ?? new JArray())
                {
                    var r = row as JObject;
                    var x = GobnomLogic.Num(r, "x", 0.0);
                    var y = GobnomLogic.Num(r, "y", 0.0);
                    if (x < 0.0 || x > w || y < 0.0 || y > h) errors.Add($"{prefix}: {key} außerhalb der Welt ({(int)x},{(int)y})");
                }
            if (!coop) return;
            var split = level["split"] as JObject;
            var axis = GobnomLogic.Str(split, "axis", "");
            if (split == null || !split.HasValues || (axis != "x" && axis != "y")) errors.Add(prefix + ": Coop ohne split_axis");
            foreach (var key in new[] { "ropes", "cushions", "fans", "shooters" })
                foreach (var row in level[key] as JArray ?? new JArray())
                {
                    var owner = (row as JObject)?["owner"];
                    if (owner != null && owner.ToString() != "a" && owner.ToString() != "b") errors.Add($"{prefix}: {key}-owner muss a/b sein");
                }
            var players = new HashSet<string>();
            foreach (var action in actions) players.Add(GobnomLogic.Str(action as JObject, "player", ""));
            if (!(players.Contains("a") && players.Contains("b"))) errors.Add(prefix + ": Lösung braucht Aktionen von Spieler a UND b");
        }
    }
}
