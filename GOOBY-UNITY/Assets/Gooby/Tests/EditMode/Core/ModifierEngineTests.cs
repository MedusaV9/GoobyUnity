using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Life;
using Gooby.Core.Minigames;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Arcade modifier system (port of tests/unit/test_modifier_engine.gd): scheduler (grace, cadence, window), unlocks by
    /// player level, consume/refund (anti-farming), the luck roll, the central effect in the award (coins ×n against the
    /// 150-coin day ledger, score ×n, XP ×n) and the ticker wiring with the "modifierStarted" event string.
    /// </summary>
    public class ModifierEngineTests
    {
        private const long NowMs = 1768478400000;
        private const long Min = ModifierEngine.MsPerMin;
        private static readonly string[] GameIds = { "teaParty", "carrotCatch", "gvz" };
        private static readonly MinigameMeta Meta = MinigameMeta.From(JObject.Parse("{\"id\":\"teaParty\",\"coin_table\":{\"divisor\":4,\"min\":4,\"max\":26},\"target\":40}"));

        [OneTimeSetUp]
        public void Content() => Fixtures.InstallContentSource();

        [TearDown]
        public void Unhook()
        {
            GoobyTicker.Modifiers = null;
            MinigameAward.GluecksrolleRoller = null;
        }

        private static JObject Fresh(int level = 12)
        {
            var state = SaveSchema.DefaultState(NowMs);
            ((JObject)state["progression"])["level"] = level;
            state[ModifierEngine.SliceKey] = ModifierEngine.DefaultSlice();
            return state;
        }

        private static JObject Cur(JToken slice) => slice?["current"] as JObject ?? new JObject();

        // ── slice & unlocks ──

        [Test]
        public void NormalizeSliceRepairsJunk()
        {
            Assert.That(JToken.DeepEquals(ModifierEngine.NormalizeSlice(null), ModifierEngine.DefaultSlice()), Is.True, "null → fresh");
            var fixedSlice = ModifierEngine.NormalizeSlice(JObject.Parse("{\"nextAt\":-5,\"seed\":\"kaputt\",\"current\":{\"type\":\"gibtsNicht\"}}"));
            Assert.That((long)fixedSlice["nextAt"], Is.EqualTo(0), "negative nextAt clamped");
            Assert.That((long)fixedSlice["seed"], Is.EqualTo(0), "junk seed → 0 (derived again on the tick)");
            Assert.That(fixedSlice["current"].Type, Is.EqualTo(JTokenType.Null), "unknown type dropped");
            var ok = ModifierEngine.NormalizeSlice(JObject.Parse("{\"nextAt\":9,\"seed\":7,\"lastGameId\":\"gvz\",\"current\":{\"gameId\":\"teaParty\",\"type\":\"turbo\",\"startedAt\":1,\"endsAt\":99,\"playsLeft\":2}}"));
            Assert.That((string)Cur(ok)["type"], Is.EqualTo("turbo"), "a valid event survives");
        }

        [Test]
        public void UnlocksGrowWithTheLevel()
        {
            Assert.That(ModifierEngine.UnlockedTypes(1), Is.Empty, "level 1: nothing yet");
            Assert.That(ModifierEngine.UnlockedTypes(2), Is.EqualTo(new[] { "doppelGold" }));
            Assert.That(ModifierEngine.UnlockedTypes(6).Count, Is.EqualTo(3));
            Assert.That(ModifierEngine.UnlockedTypes(12).Count, Is.EqualTo(6), "level 12: the whole pool");
            Assert.That(ModifierEngine.UnlockedTypes(Leveling.MaxLevel).Count, Is.EqualTo(6));
        }

        [Test]
        public void EligiblePairsAvoidTheLastGame()
        {
            var pairs = ModifierEngine.EligiblePairs(12, "teaParty", GameIds);
            Assert.That(pairs.Count, Is.EqualTo(2 * 6), "6 types × 2 remaining games");
            Assert.That(pairs.Select(p => p.gameId), Has.No.Member("teaParty"), "the no-repeat pin holds");
            Assert.That(ModifierEngine.PlayableGameIds().Count, Is.EqualTo(38), "all registry games are candidates");
        }

        // ── scheduler ──

        [Test]
        public void TickPlansTheGraceThenStartsDeterministically()
        {
            var state = Fresh();
            var (changes, evt) = ModifierEngine.Tick(state, NowMs, GameIds);
            Assert.That(evt, Is.EqualTo(ModifierEngine.EventScheduled), "the first tick only plans");
            state[ModifierEngine.SliceKey] = changes;
            Assert.That((long)changes["nextAt"], Is.EqualTo(NowMs + ModifierEngine.GraceMin * Min), "grace = 30 min");
            Assert.That((long)changes["seed"], Is.Not.EqualTo(0), "seed derived from meta.createdAt");
            var start = (long)changes["nextAt"];
            var (started, evt2) = ModifierEngine.Tick(state, start, GameIds);
            Assert.That(evt2, Is.EqualTo(ModifierEngine.EventStarted), "a due nextAt starts the event");
            state[ModifierEngine.SliceKey] = started;
            var cur = Cur(started);
            Assert.That(ModifierEngine.Types.ContainsKey((string)cur["type"]), Is.True);
            Assert.That(GameIds, Has.Member((string)cur["gameId"]));
            Assert.That((int)cur["playsLeft"], Is.EqualTo(ModifierEngine.Types[(string)cur["type"]].Plays), "the type's round budget");
            Assert.That((long)cur["endsAt"], Is.EqualTo(start + ModifierEngine.WindowMin * Min), "45-min window");
            var next = (long)started["nextAt"];
            Assert.That(next, Is.InRange(start + ModifierEngine.CadenceMinLo * Min, start + ModifierEngine.CadenceMinHi * Min), "cadence 50–120 min");
            // determinism: the same start state rolls the same event
            var state2 = Fresh();
            state2[ModifierEngine.SliceKey] = ModifierEngine.Tick(state2, NowMs, GameIds).changes;
            var (again, _) = ModifierEngine.Tick(state2, start, GameIds);
            Assert.That(JToken.DeepEquals(Cur(again), cur), Is.True, "same seed → same (game, type) pair");
        }

        [Test]
        public void TickWithoutUnlockedTypesOnlyReplans()
        {
            var state = Fresh(1);
            state[ModifierEngine.SliceKey] = ModifierEngine.Tick(state, NowMs, GameIds).changes;
            var start = (long)state[ModifierEngine.SliceKey]["nextAt"];
            var (changes, evt) = ModifierEngine.Tick(state, start, GameIds);
            Assert.That(evt, Is.EqualTo(ModifierEngine.EventRescheduled));
            Assert.That(Cur(changes).Count, Is.EqualTo(0), "no event started");
        }

        [Test]
        public void TickClearsExpiredEvents()
        {
            var state = Fresh();
            ModifierEngine.ForceEvent(state, "gvz", "turbo", NowMs);
            var end = NowMs + ModifierEngine.WindowMin * Min;
            state[ModifierEngine.SliceKey]["nextAt"] = end + 10 * Min;
            var (changes, evt) = ModifierEngine.Tick(state, end, GameIds);
            Assert.That(evt, Is.EqualTo(ModifierEngine.EventExpired));
            Assert.That(Cur(changes).Count, Is.EqualTo(0), "event gone");
            Assert.That((string)changes["lastGameId"], Is.EqualTo("gvz"), "the no-repeat pin stays");
        }

        // ── consume / refund ──

        [Test]
        public void ConsumeCountsDownAndEmptiesAtTheEnd()
        {
            var state = Fresh();
            ModifierEngine.ForceEvent(state, "teaParty", "doppelGold", NowMs);
            Assert.That(ModifierEngine.Consume(state, "carrotCatch", NowMs).ok, Is.False, "another game consumes nothing");
            var r1 = ModifierEngine.Consume(state, "teaParty", NowMs);
            Assert.That((r1.ok, r1.cleared), Is.EqualTo((true, false)));
            Assert.That((int)Cur(state[ModifierEngine.SliceKey])["playsLeft"], Is.EqualTo(1));
            var r2 = ModifierEngine.Consume(state, "teaParty", NowMs);
            Assert.That(r2.cleared, Is.True, "the 2nd round empties the event");
            Assert.That(Cur(state[ModifierEngine.SliceKey]).Count, Is.EqualTo(0));
            Assert.That((string)state[ModifierEngine.SliceKey]["lastGameId"], Is.EqualTo("teaParty"));
        }

        [Test]
        public void RefundWorksExactlyOnceAndOnlyInTheWindow()
        {
            var state = Fresh();
            ModifierEngine.ForceEvent(state, "teaParty", "turbo", NowMs);
            var snap = ModifierEngine.Consume(state, "teaParty", NowMs).snapshot;
            Assert.That(ModifierEngine.Refund(state, snap, NowMs + Min), Is.True, "1st refund");
            Assert.That((int)Cur(state[ModifierEngine.SliceKey])["playsLeft"], Is.EqualTo(3), "round given back");
            var snap2 = ModifierEngine.Consume(state, "teaParty", NowMs).snapshot;
            Assert.That((bool)snap2["refundUsed"], Is.True, "refundUsed sticks to the event");
            Assert.That(ModifierEngine.Refund(state, snap2, NowMs + Min), Is.False, "a 2nd refund of the same event is refused");
            var late = NowMs + ModifierEngine.WindowMin * Min + 1;
            var other = Fresh();
            ModifierEngine.ForceEvent(other, "gvz", "turbo", NowMs);
            var snap3 = ModifierEngine.Consume(other, "gvz", NowMs).snapshot;
            Assert.That(ModifierEngine.Refund(other, snap3, late), Is.False, "no refund after the window");
        }

        [Test]
        public void RefundRestoresAnEmptiedEvent()
        {
            var state = Fresh();
            ModifierEngine.ForceEvent(state, "teaParty", "doppelGold", NowMs);
            ModifierEngine.Consume(state, "teaParty", NowMs);
            var snap = ModifierEngine.Consume(state, "teaParty", NowMs).snapshot;
            Assert.That(Cur(state[ModifierEngine.SliceKey]).Count, Is.EqualTo(0));
            Assert.That(ModifierEngine.Refund(state, snap, NowMs + Min), Is.True, "the refund after the last consume still works");
            var cur = Cur(state[ModifierEngine.SliceKey]);
            Assert.That(((int)cur["playsLeft"], (bool)cur["refundUsed"]), Is.EqualTo((1, true)));
        }

        [Test]
        public void LuckRollStaysInItsBounds()
        {
            var state = Fresh();
            var rolls = Enumerable.Range(0, 40).Select(_ => ModifierEngine.RollGlueckspilz(state)).ToList();
            Assert.That(rolls.Min(), Is.GreaterThanOrEqualTo(ModifierEngine.GlueckspilzMin));
            Assert.That(rolls.Max(), Is.LessThanOrEqualTo(ModifierEngine.GlueckspilzMax));
            Assert.That(rolls.Max(), Is.GreaterThan(rolls.Min()), "the seed moves on");
        }

        [Test]
        public void DescriptorsAndLaunchParamsFeedTheAward()
        {
            var state = Fresh();
            Assert.That(ModifierEngine.ActiveEvent(state, NowMs), Is.Null);
            ModifierEngine.ForceEvent(state, "teaParty", "doppelGold", NowMs);
            var active = ModifierEngine.GetActiveFor(state, "teaParty", NowMs + Min);
            Assert.That((active.Type, active.RemainingPlays, active.Def.NameKey), Is.EqualTo(("doppelGold", 2, "modifier.name.doppelGold")));
            Assert.That((double)active.Params["coin_mult"], Is.EqualTo(2.0));
            Assert.That(ModifierEngine.GetActiveFor(state, "gvz", NowMs), Is.Null, "only for its game");
            Assert.That(ModifierEngine.ActiveEvent(state, NowMs).GameId, Is.EqualTo("teaParty"));
            Assert.That(ModifierEngine.CountdownText(active.EndsAt, NowMs + 2 * Min + 500), Is.EqualTo("43:00"));
            Assert.That(ModifierEngine.CountdownText(active.EndsAt, active.EndsAt + 9), Is.EqualTo("0:00"));
            Assert.That(ModifierEngine.LaunchParams(JObject.Parse("{\"type\":\"federleicht\"}")).ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"type\":\"federleicht\",\"energy_free\":true}"));
            Assert.That(ModifierEngine.ClearEvent(state), Is.True);
            Assert.That(ModifierEngine.ActiveEvent(state, NowMs), Is.Null);
        }

        // ── central effect in the award ──

        [Test]
        public void AwardLuckRollGoesThroughTheEngine()
        {
            MinigameAward.GluecksrolleRoller = ModifierEngine.RollGlueckspilz;
            var state = Fresh();
            var plain = Fresh();
            var mod = JObject.Parse("{\"type\":\"glueckspilz\",\"gluecksrolle\":true}");
            var b = MinigameAward.Award(state, Meta, 60, "normal", "2026-01-15", null, mod);
            var p = MinigameAward.Award(plain, Meta, 60, "normal", "2026-01-15");
            Assert.That(b.GluecksrolleCoins, Is.InRange(ModifierEngine.GlueckspilzMin, ModifierEngine.GlueckspilzMax));
            Assert.That(b.Coins, Is.EqualTo(p.Coins), "the regular payout stays");
            Assert.That((int)state["economy"]["coins"], Is.EqualTo((int)plain["economy"]["coins"] + b.GluecksrolleCoins), "the roll lands on the account");
            Assert.That((long)state[ModifierEngine.SliceKey]["seed"], Is.Not.EqualTo(0), "the roll moved the stream");
        }

        // ── ticker wiring ──

        [Test]
        public void LiveTickStartsDueEventsAndReportsThem()
        {
            GoobyTicker.Modifiers = new ModifierEngine.Scheduler { GameIds = () => GameIds };
            var state = Fresh();
            state[ModifierEngine.SliceKey] = ModifierEngine.Tick(state, NowMs, GameIds).changes;
            var start = (long)state[ModifierEngine.SliceKey]["nextAt"];
            ((JObject)state["gooby"])["lastTickAt"] = start - 5000;
            var events = GoobyTicker.LiveTick(state, start);
            var cur = Cur(state[ModifierEngine.SliceKey]);
            Assert.That(cur.Count, Is.GreaterThan(0), "the live tick starts the due event");
            Assert.That(events, Has.Member(ModifierEngine.StartedEventPrefix + (string)cur["gameId"] + ":" + (string)cur["type"]));
        }

        [Test]
        public void CatchUpStartsMissedEvents()
        {
            GoobyTicker.Modifiers = new ModifierEngine.Scheduler { GameIds = () => GameIds };
            var state = Fresh();
            state[ModifierEngine.SliceKey] = ModifierEngine.Tick(state, NowMs, GameIds).changes;
            var later = (long)state[ModifierEngine.SliceKey]["nextAt"] + 10 * Min;
            ((JObject)state["gooby"])["lastTickAt"] = NowMs;
            var events = GoobyTicker.CatchUp(state, later);
            Assert.That(Cur(state[ModifierEngine.SliceKey]).Count, Is.GreaterThan(0), "the offline catch-up starts the event");
            Assert.That(events.Any(e => e.StartsWith(ModifierEngine.StartedEventPrefix)), Is.True, "and reports it");
        }
    }
}
