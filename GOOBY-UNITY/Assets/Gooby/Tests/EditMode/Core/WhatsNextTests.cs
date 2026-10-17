using System.Collections.Generic;
using Gooby.Core.Meta;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>Port of test_rest2_whats_next.gd: the "Was nun?" suggestion in different saves.</summary>
    public class WhatsNextTests
    {
        private static JObject State(int level = 1, Dictionary<string, double> stats = null)
        {
            var s = new JObject { ["hunger"] = 80.0, ["energy"] = 90.0, ["hygiene"] = 85.0, ["fun"] = 70.0 };
            if (stats != null) foreach (var kv in stats) s[kv.Key] = kv.Value;
            return new JObject { ["gooby"] = new JObject { ["stats"] = s }, ["progression"] = new JObject { ["level"] = level, ["xp"] = 0 } };
        }

        private static WhatsNextAdvisor.QuestRow Row(string id, int progress, int target, bool claimed = false, bool complete = false) =>
            new WhatsNextAdvisor.QuestRow(id, $"quests.q.{id}.titel", progress, target, complete, claimed);

        [Test]
        public void AWaitingRewardComesFirstAndOpensTheQuests()
        {
            var s = WhatsNextAdvisor.Suggest(State(), new[] { Row("a", 3, 3, false, true) }, 1, false);
            Assert.That(s.Id, Is.EqualTo("abholen"));
            Assert.That(s.Action, Is.EqualTo("quests"));
        }

        [Test]
        public void TheOpenQuestWithTheMostProgressIsSuggested()
        {
            var board = new[] { Row("kaum", 0, 4), Row("fast", 3, 4), Row("fertig", 4, 4, true, true) };
            var s = WhatsNextAdvisor.Suggest(State(), board, 0, false);
            Assert.That(s.Id, Is.EqualTo("quest_fast"), "almost done motivates");
            Assert.That(s.TextKey, Is.EqualTo("quests.wasnun.quest"));
            Assert.That(s.Args["titel_key"], Is.EqualTo("quests.q.fast.titel"));
        }

        [Test]
        public void CareIsSuggestedWhenAStatIsLowAndTheWorstOneWins()
        {
            Assert.That(WhatsNextAdvisor.Suggest(State(1, new Dictionary<string, double> { ["hunger"] = 20.0 }), new WhatsNextAdvisor.QuestRow[0], 0, false).Id, Is.EqualTo("pflege_hunger"));
            Assert.That(WhatsNextAdvisor.Suggest(State(1, new Dictionary<string, double> { ["hunger"] = 30.0, ["hygiene"] = 10.0 }), new WhatsNextAdvisor.QuestRow[0], 0, false).Id, Is.EqualTo("pflege_hygiene"));
        }

        [Test]
        public void TheRanchIsSuggestedFromLevelFifteen()
        {
            Assert.That(WhatsNextAdvisor.Suggest(State(15), new WhatsNextAdvisor.QuestRow[0], 0, false).Id, Is.EqualTo("ranch"));
            Assert.That(WhatsNextAdvisor.Suggest(State(14), new WhatsNextAdvisor.QuestRow[0], 0, false).Id, Is.Not.EqualTo("ranch"));
        }

        [Test]
        public void TheNextLevelIsTheFarGoalOnceTheBoardIsDone()
        {
            var s = WhatsNextAdvisor.Suggest(State(7), new WhatsNextAdvisor.QuestRow[0], 0, true);
            Assert.That(s.Id, Is.EqualTo("level"));
            Assert.That(s.Args["level"], Is.EqualTo(8));
        }

        [Test]
        public void TheArcadeInvitationStaysGentle()
        {
            var s = WhatsNextAdvisor.Suggest(State(3), new WhatsNextAdvisor.QuestRow[0], 0, false);
            Assert.That(s.Id, Is.EqualTo("arcade"));
            Assert.That(s.Action, Is.EqualTo(""), "just text, no push");
        }
    }
}
