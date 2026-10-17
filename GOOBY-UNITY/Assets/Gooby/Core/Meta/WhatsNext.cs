using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>
    /// "Was nun?" adviser (port of whats_next_advisor.gd, REST-2 red thread): from the save and the daily quest board it
    /// proposes the ONE next sensible step. First match wins:
    /// 1 a quest reward waiting to be claimed · 2 the open daily quest with the most progress ("almost done" motivates
    /// more than "start over") · 3 urgent care (hunger, hygiene, energy under <see cref="CareThreshold"/>, the worst one) ·
    /// 4 the ranch once the level allows it · 5 the next level as a far goal when the board is done · 6 a gentle arcade
    /// invitation. Pure: callers pass the state, the board rows and the context.
    /// </summary>
    public static class WhatsNextAdvisor
    {
        /// <summary>Care is suggested under this stat (the HUD alarm is 25 — the hint comes a little earlier).</summary>
        public const double CareThreshold = 35.0;
        public const int DefaultRanchLevel = 15;

        /// <summary>One quest row as the adviser needs it.</summary>
        public readonly struct QuestRow
        {
            public readonly string Id, TitleKey;
            public readonly int Progress, Target;
            public readonly bool Complete, Claimed;

            public QuestRow(string id, string titleKey, int progress, int target, bool complete, bool claimed)
            {
                Id = id; TitleKey = titleKey; Progress = progress; Target = target; Complete = complete; Claimed = claimed;
            }
        }

        /// <summary>A suggestion: id (per-day dismissal key), the string key with its arguments, the tap action ("quests" or "").</summary>
        public sealed class Suggestion
        {
            public string Id, TextKey, Action = "";
            public readonly Dictionary<string, object> Args = new Dictionary<string, object>();
        }

        private static readonly (string stat, string key)[] CareChecks =
        {
            ("hunger", "quests.wasnun.pflege_hunger"), ("hygiene", "quests.wasnun.pflege_hygiene"), ("energy", "quests.wasnun.pflege_energie"),
        };

        public static Suggestion Suggest(JObject state, IList<QuestRow> board, int claimable, bool allClaimed, int ranchLevel = DefaultRanchLevel)
        {
            if (claimable > 0) return new Suggestion { Id = "abholen", TextKey = "quests.wasnun.abholen", Action = "quests" };
            var open = BestOpenQuest(board);
            if (open.HasValue)
            {
                var s = new Suggestion { Id = "quest_" + open.Value.Id, TextKey = "quests.wasnun.quest", Action = "quests" };
                s.Args["titel_key"] = open.Value.TitleKey;
                return s;
            }
            var care = CareSuggestion(state);
            if (care != null) return care;
            var level = LevelOf(state);
            if (level >= Math.Max(1, ranchLevel)) return new Suggestion { Id = "ranch", TextKey = "quests.wasnun.ranch" };
            if (allClaimed)
            {
                var s = new Suggestion { Id = "level", TextKey = "quests.wasnun.level" };
                s.Args["level"] = level + 1;
                return s;
            }
            return new Suggestion { Id = "arcade", TextKey = "quests.wasnun.arcade" };
        }

        private static QuestRow? BestOpenQuest(IList<QuestRow> board)
        {
            QuestRow? best = null;
            var bestRatio = -1.0;
            if (board == null) return null;
            foreach (var row in board)
            {
                if (row.Claimed || row.Complete) continue;
                var ratio = (double)row.Progress / Math.Max(1, row.Target);
                if (ratio <= bestRatio) continue;
                bestRatio = ratio;
                best = row;
            }
            return best;
        }

        private static Suggestion CareSuggestion(JObject state)
        {
            var stats = J.Dict(J.Dict(state?["gooby"])["stats"]);
            string worstStat = null, worstKey = null;
            var worst = CareThreshold;
            foreach (var (stat, key) in CareChecks)
            {
                var value = J.NumOr(stats[stat], 100.0);
                if (value >= worst) continue;
                worst = value;
                worstStat = stat;
                worstKey = key;
            }
            return worstStat == null ? null : new Suggestion { Id = "pflege_" + worstStat, TextKey = worstKey };
        }

        private static int LevelOf(JObject state) => Math.Max(1, (int)J.NumOr(J.Dict(state?["progression"])["level"], 1.0));
    }
}
