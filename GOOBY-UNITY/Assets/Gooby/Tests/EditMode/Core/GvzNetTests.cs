using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Gvz;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Net;
using Gooby.Tests.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// GvZ online PvP — the lockstep core (port of gvz_pvp_lockstep.gd: two sims from one input stream, the side gate,
    /// mud drip + escalation, summon gates, the survive timer and the breakthrough, the hash guard) and the session in
    /// online mode: two clients whose bots each play their side over a lagging wire end on the same tick with the same
    /// match hash and opposite results; a confirmed result books its coins exactly once.
    /// </summary>
    public class GvzNetTests
    {
        private JObject _balance;
        private JObject _pvp;

        [OneTimeSetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            _balance = GvzData.LoadBalance();
            _pvp = GvzData.LoadPvp();
        }

        private JObject Rules(int surviveSec, int matschStart = 0)
        {
            var rules = (JObject)_pvp.DeepClone();
            rules["match"]["gooby_survive_sec"] = surviveSec;
            rules["matsch"]["start"] = matschStart;
            return rules;
        }

        private static JObject OverTheWire(JObject body) => NetJson.ParseObject(body.ToString(Formatting.None));

        private static void Pump(GvzPvpLockstep a, GvzPvpLockstep b, int wallTicks)
        {
            for (var i = 0; i < wallTicks; i++)
            {
                a.Advance();
                b.Advance();
                var fa = a.TakeFrame();
                if (fa != null) b.ReceiveFrame(OverTheWire(fa));
                var fb = b.TakeFrame();
                if (fb != null) a.ReceiveFrame(OverTheWire(fb));
                foreach (var (t, h) in a.TakeHashes()) b.ReceiveHash(t, h);
                foreach (var (t, h) in b.TakeHashes()) a.ReceiveHash(t, h);
            }
        }

        private (GvzPvpLockstep gooby, GvzPvpLockstep zombie) Pair(long seed, JObject rules)
        {
            var g = new GvzPvpLockstep();
            var z = new GvzPvpLockstep();
            g.Start(_balance, rules, seed, GvzPvpLockstep.SideGooby);
            z.Start(_balance, rules, seed, GvzPvpLockstep.SideZombie);
            z.ReceiveFrame(OverTheWire(g.TakeFrame(true)));
            g.ReceiveFrame(OverTheWire(z.TakeFrame(true)));
            return (g, z);
        }

        [Test]
        public void TheArenaHasFiveLanesEveryStandardTowerNoMowersAndAFarGuardSpawn()
        {
            var arena = GvzPvpLockstep.ArenaLevel(_balance);
            Assert.That(GvzJson.Ints(arena["lanes"]), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            var towers = GvzJson.Strings(arena["unlock_towers"]);
            Assert.That(towers, Does.Contain("moehrenschuetze").And.Contain("nutella_sammler"));
            Assert.That(towers, Has.No.Member("goldi"), "code-gated towers stay out of PvP");
            var sim = new GvzPvpLockstep();
            sim.Start(_balance, _pvp, 5, GvzPvpLockstep.SideGooby);
            Assert.That(sim.State.Mowers.Keys.All(l => sim.State.Mowers[l].Used), Is.True, "no mowers in PvP");
            Assert.That(sim.SurviveTicks, Is.EqualTo(210 * 20));
            Assert.That(sim.SecondsLeft(), Is.EqualTo(210));
            Assert.That(sim.ZombieTypes().First(), Is.EqualTo("schlurfi"), "data order: light ones first");
        }

        [Test]
        public void TwoSimsKeepTheSameMatchHashWhileBothSidesAct()
        {
            var (g, z) = Pair(424_242, Rules(60, 200));
            Pump(g, z, 10);
            Assert.That(GvzLogic.CanPlace(g.State, "nutella_sammler", 2, 1).Ok, Is.True, "50 start nutella buy a collector");
            g.SchedulePlace("nutella_sammler", 2, 1);
            Assert.That(z.CanSpawn("schlurfi", 2).Ok, Is.True);
            z.ScheduleSpawn("schlurfi", 2);
            Pump(g, z, 30);
            Assert.That(g.State.Towers.ContainsKey(GvzLogic.CellKey(2, 1)) && z.State.Towers.ContainsKey(GvzLogic.CellKey(2, 1)), Is.True, "the Gooby's tower on both sims");
            Assert.That(g.State.Zombies.Count(zz => zz.Lane == 2) == 1 && z.State.Zombies.Count(zz => zz.Lane == 2) == 1, Is.True, "the summoned zombie on both sims");
            Assert.That(z.Matsch, Is.EqualTo(200 - z.ZombieCost("schlurfi")), "the summon cost mud");
            Assert.That(z.ZombieCooldownLeft("schlurfi"), Is.GreaterThan(0));
            Pump(g, z, 200);
            Assert.That(g.State.Tick, Is.GreaterThan(120), "past two hash exchanges");
            Assert.That(z.State.Tick, Is.EqualTo(g.State.Tick));
            Assert.That(z.StateHashText(), Is.EqualTo(g.StateHashText()), "identical match state on both devices");
            Assert.That(g.StateHashText().Length, Is.EqualTo(16), "the hash fits the server's 64-char limit");
            Assert.That(g.Desynced || z.Desynced, Is.False);
        }

        [Test]
        public void TheSideGateDropsForeignActionsOnBothDevices()
        {
            var (g, z) = Pair(7, Rules(60, 500));
            // a forged frame: the zombie side "plants", the Gooby side would "summon" — the dispatch drops both identically
            g.ReceiveFrame(new JObject { ["n"] = 99, ["upTo"] = 100, ["a"] = new JArray(new JObject { ["t"] = 6, ["do"] = "place", ["type"] = "moehrenschuetze", ["lane"] = 0, ["col"] = 0 }) });
            var forged = g.ScheduleSpawn("schlurfi", 1);
            for (var i = 0; i < 20; i++) g.Advance();
            Assert.That(g.State.Towers.Count, Is.EqualTo(0), "a zombie-side place is ignored");
            Assert.That(g.State.Zombies.Count, Is.EqualTo(0), "a Gooby-side spawn is ignored");
            Assert.That(forged, Is.Not.Null);
        }

        [Test]
        public void MudDripsEscalatesAndGatesTheSummons()
        {
            var sim = new GvzPvpLockstep();
            sim.Start(_balance, _pvp, 3, GvzPvpLockstep.SideZombie);
            sim.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            Assert.That(sim.CanSpawn("schlurfi", 0).Reason, Is.EqualTo("matsch"));
            Assert.That(sim.CanSpawn("drache", 0).Reason, Is.EqualTo("unknown_zombie"));
            Assert.That(sim.CanSpawn("schlurfi", 7).Reason, Is.EqualTo("lane"));
            while (sim.State.Tick < 200) sim.Advance();
            Assert.That(sim.Matsch, Is.EqualTo(15), "15 every 10 s");
            while (sim.State.Tick < 900) sim.Advance();
            // drips at 10…40 s = 4×15, at 45 s escalation starts: +5×(tick/900) on every drip from then on
            Assert.That(sim.Matsch, Is.EqualTo(4 * 15), "no drip lands on 45 s itself (10-s grid)");
            while (sim.State.Tick < 1000) sim.Advance();
            Assert.That(sim.Matsch, Is.EqualTo(5 * 15 + 5), "the 50-s drip carries one escalation bonus");
            Assert.That(sim.CanSpawn("schlurfi", 0).Ok, Is.True);
            sim.ScheduleSpawn("schlurfi", 0);
            for (var i = 0; i < 8; i++) sim.Advance();
            Assert.That(sim.State.Zombies.Count, Is.EqualTo(1));
            Assert.That(sim.CanSpawn("schlurfi", 1).Reason, Is.EqualTo("cooldown"));
        }

        [Test]
        public void TheTimerWinsForTheGoobysAndABreakthroughForTheZombies()
        {
            var timer = new GvzPvpLockstep();
            timer.Start(_balance, Rules(5), 11, GvzPvpLockstep.SideGooby);
            timer.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            while (!timer.IsMatchOver && timer.State.Tick < 1000) timer.Advance();
            Assert.That(timer.Winner, Is.EqualTo(GvzPvpLockstep.SideGooby));
            Assert.That(timer.State.Tick, Is.EqualTo(100));
            Assert.That(timer.Advance().stepped, Is.False, "nothing computes after the match");

            var rush = new GvzPvpLockstep();
            rush.Start(_balance, Rules(600, 5000), 12, GvzPvpLockstep.SideZombie);
            rush.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            rush.ScheduleSpawn("sprinter", 3);
            while (!rush.IsMatchOver && rush.State.Tick < 12_000) rush.Advance();
            Assert.That(rush.Winner, Is.EqualTo(GvzPvpLockstep.SideZombie), "an undefended lane (no mowers) is a breakthrough");
            Assert.That(rush.State.Outcome, Is.EqualTo("lost"));
        }

        [Test]
        public void AHashMismatchStopsTheMatch()
        {
            var sim = new GvzPvpLockstep();
            sim.Start(_balance, _pvp, 9, GvzPvpLockstep.SideGooby);
            sim.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            while (sim.State.Tick <= GvzPvpLockstep.HashTicksDefault) sim.Advance();
            var reports = sim.TakeHashes();
            Assert.That(reports.Count, Is.EqualTo(1));
            sim.ReceiveHash(reports[0].t, "kaputt");
            Assert.That(sim.Desynced, Is.True);
            Assert.That(sim.Advance().stepped, Is.False);
        }

        // ── the session in online mode ──

        private sealed class Wire
        {
            private readonly int _latency;
            private int _now;
            private readonly Queue<(int at, Action deliver)> _queue = new Queue<(int, Action)>();
            public readonly GvzGame A, B;

            public Wire(GvzGame a, GvzGame b, int latency)
            {
                A = a; B = b; _latency = latency;
                a.FrameOut += f => { var w = OverTheWire(f); _queue.Enqueue((_now + _latency, () => B.Net?.ReceiveFrame(w))); };
                b.FrameOut += f => { var w = OverTheWire(f); _queue.Enqueue((_now + _latency, () => A.Net?.ReceiveFrame(w))); };
                a.HashOut += (t, h) => _queue.Enqueue((_now + _latency, () => B.Net?.ReceiveHash(t, h)));
                b.HashOut += (t, h) => _queue.Enqueue((_now + _latency, () => A.Net?.ReceiveHash(t, h)));
            }

            public void Step(double dt)
            {
                _now++;
                while (_queue.Count > 0 && _queue.Peek().at <= _now) _queue.Dequeue().deliver();
                A.ApplyBot(); B.ApplyBot();
                A.Tick(dt); B.Tick(dt);
            }
        }

        private static bool Done(GvzGame g) => g.Phase == GvzGame.PhaseWon || g.Phase == GvzGame.PhaseLost;

        [Test]
        public void AnOnlineMatchOfTwoBotsEndsIdenticallyOnBothClients([Values(0, 4)] int latency)
        {
            var rules = Rules(40, 100);
            var a = new GvzGame("normal", 1) { PvpRules = rules };
            var b = new GvzGame("normal", 2) { PvpRules = rules };
            var wire = new Wire(a, b, latency);
            string hashA = "", hashB = ""; int tickA = -1, tickB = -1;
            a.PhaseChanged += p => { if (p == GvzGame.PhaseWon || p == GvzGame.PhaseLost) { hashA = a.Net.StateHashText(); tickA = a.Run.Tick; } };
            b.PhaseChanged += p => { if (p == GvzGame.PhaseWon || p == GvzGame.PhaseLost) { hashB = b.Net.StateHashText(); tickB = b.Run.Tick; } };
            Assert.That(a.StartOnline(777, GvzPvpLockstep.SideGooby), Is.True);
            Assert.That(b.StartOnline(777, GvzPvpLockstep.SideZombie), Is.True);
            Assert.That(a.NetZombie, Is.False);
            Assert.That(b.NetZombie, Is.True);
            Assert.That(b.Cards(), Is.EqualTo(b.Net.ZombieTypes()), "the zombie side summons");
            Assert.That(a.Cards(), Does.Contain(GvzGame.Shovel));
            for (var i = 0; i < 20 * 120 && !(Done(a) && Done(b)); i++) wire.Step(0.05);

            TestContext.Progress.WriteLine($"PvP bots (latency {latency}): winner={a.Net?.Winner} tick={tickA} towers={a.Run?.Towers.Count} kills={a.Run?.Kills} zombies={b.Run?.Zombies.Count} mud={b.Net?.Matsch}");
            Assert.That(Done(a) && Done(b), Is.True, $"both matches end (a={a.Phase} b={b.Phase})");
            Assert.That(tickB, Is.EqualTo(tickA), "the same end tick");
            Assert.That(hashB, Is.EqualTo(hashA), "the same final match state");
            Assert.That(a.Net.Winner, Is.EqualTo(b.Net.Winner));
            Assert.That(a.LastWon, Is.Not.EqualTo(b.LastWon), "exactly one side wins");
            Assert.That(a.Net.Desynced || b.Net.Desynced, Is.False);
            Assert.That(b.Run.Zombies.Count + b.Run.Kills, Is.GreaterThan(0), "the zombie bot summoned");
            Assert.That(a.Run.Towers.Count, Is.GreaterThan(0), "the Gooby bot planted");
            Assert.That(a.LevelId, Is.EqualTo(0));
            Assert.That(GvzProgress.MaxUnlocked(a.Save), Is.EqualTo(1), "PvP books no campaign progress");
        }

        [Test]
        public void ZombieCardsAndPlannedCollectsGoThroughTheLockstep()
        {
            var z = new GvzGame("normal", 3) { PvpRules = Rules(60, 300) };
            z.StartOnline(5, GvzPvpLockstep.SideZombie);
            z.Net.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            Assert.That(z.Resource, Is.EqualTo(300), "the zombie counter shows mud");
            z.SelectCard("eimer");
            Assert.That(z.UseCard(2, 5).Ok, Is.True);
            Assert.That(z.SelectedCard, Is.EqualTo(""));
            Assert.That(z.Run.Zombies.Count, Is.EqualTo(0), "planned, not summoned yet");
            for (var i = 0; i < 10; i++) z.Tick(0.05);
            Assert.That(z.Run.Zombies.Count(zz => zz.Lane == 2 && zz.Type == "eimer"), Is.EqualTo(1));
            Assert.That(z.Resource, Is.EqualTo(300 - z.CardCost("eimer")));
            Assert.That(z.CardCooldownFrac("eimer"), Is.GreaterThan(0.0));
            Assert.That(z.Collect(1), Is.EqualTo(-1), "the zombie side collects no nutella");

            var g = new GvzGame("normal", 4) { PvpRules = Rules(60) };
            g.StartOnline(6, GvzPvpLockstep.SideGooby);
            g.Net.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            while (g.Run.Drops.Count == 0 && g.Run.Tick < 400) g.Tick(0.05);
            Assert.That(g.Run.Drops.Count, Is.GreaterThan(0), "a sky blob fell");
            var drop = g.Run.Drops[0];
            var nutella = g.Run.Nutella;
            Assert.That(g.Collect(drop.Id), Is.EqualTo(drop.Amount), "the amount shows at once");
            Assert.That(g.Collect(drop.Id), Is.EqualTo(-1), "a second tap plans nothing");
            for (var i = 0; i < 10; i++) g.Tick(0.05);
            Assert.That(g.Run.Nutella, Is.EqualTo(nutella + drop.Amount), "collected on its tick");
        }

        [Test]
        public void AConfirmedResultBooksItsCoinsExactlyOnce()
        {
            var save = new JObject { ["economy"] = new JObject { ["coins"] = 100 } };
            Assert.That(GvzProgress.BookNetResult(save, "gvz-abc-GOOBY-AAAA", true, 30), Is.True);
            Assert.That(GvzProgress.BookNetResult(save, "gvz-abc-GOOBY-AAAA", true, 30), Is.False, "a repeat (WELCOME after the push) books nothing");
            Assert.That(GvzProgress.BookNetResult(save, "gvz-def-GOOBY-AAAA", false, 10), Is.True);
            Assert.That((int)save["economy"]["coins"], Is.EqualTo(140));
            Assert.That(GvzProgress.NetWins(save), Is.EqualTo(1));
            Assert.That(GvzProgress.NetLosses(save), Is.EqualTo(1));
            Assert.That(GvzProgress.BookNetResult(save, "", true, 30), Is.False);
        }
    }
}
