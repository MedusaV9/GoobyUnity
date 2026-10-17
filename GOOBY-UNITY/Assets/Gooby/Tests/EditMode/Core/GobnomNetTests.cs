using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Gobnom;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Net;
using Gooby.Tests.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// GOB NOM online coop — port of test_w15_gobnom_netz.gd (two lockstep sims, one input stream: identical hashes, the
    /// input-delay window, the desync guard, the rejoin replay) plus the session in online mode: all ten coop levels won
    /// by two clients that each only play their own half over a lagging wire, own-half input, planned cuts once, and the
    /// server result as fallback for a sim stuck on a missing fence.
    /// </summary>
    public class GobnomNetTests
    {
        private JObject _balance;

        [OneTimeSetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            _balance = GobnomData.LoadBalance();
        }

        /// <summary>Split at x=480, rope 0 belongs to a (left), rope 1 to b (right) — both hold the candy.</summary>
        private static JObject CoopLevel() => JObject.FromObject(new
        {
            id = 1,
            kind = "coop",
            split = new { axis = "x", at = 480 },
            candy = new { x = 480, y = 200 },
            mouth = new { x = 480, y = 470 },
            ropes = new[] { new { x = 200, y = 120, rest = 80, owner = "a" }, new { x = 700, y = 120, rest = 80, owner = "b" } },
            jars = new[] { new { x = 480, y = 300 }, new { x = 480, y = 360 }, new { x = 480, y = 420 } },
        });

        /// <summary>The wire of the server: compact JSON both ways (exactly what a ROOM_MSG body goes through).</summary>
        private static JObject OverTheWire(JObject body) => NetJson.ParseObject(body.ToString(Formatting.None));

        /// <summary>One wall tick of both sims, due frames/hashes delivered crosswise (lossless, in order).</summary>
        private static void Pump(GobnomLockstep a, GobnomLockstep b, int wallTicks, Dictionary<string, List<JObject>> frames = null, Dictionary<int, string> hashByTick = null)
        {
            for (var i = 0; i < wallTicks; i++)
            {
                if (a.Advance().stepped && hashByTick != null) hashByTick[a.State.Tick] = GobnomLogic.StateHash(a.State);
                b.Advance();
                var fa = a.TakeFrame();
                if (fa != null) { frames?["a"].Add((JObject)fa.DeepClone()); b.ReceiveFrame(OverTheWire(fa)); }
                var fb = b.TakeFrame();
                if (fb != null) { frames?["b"].Add((JObject)fb.DeepClone()); a.ReceiveFrame(OverTheWire(fb)); }
                foreach (var (t, h) in a.TakeHashes()) b.ReceiveHash(t, h);
                foreach (var (t, h) in b.TakeHashes()) a.ReceiveHash(t, h);
            }
        }

        private static bool RopeCut(GobnomLockstep sim, int id) => GobnomLogic.RopeById(sim.State, id).Cut;

        // ── lockstep: two sims, one input stream ──

        [Test]
        public void TwoSimsKeepTheSameHashAfterRelayingBothInputStreams()
        {
            const long seedFromServer = 987_654_321;
            var a = new GobnomLockstep();
            var b = new GobnomLockstep();
            a.Start(CoopLevel(), _balance, seedFromServer, GobnomLogic.PlayerA);
            b.Start(CoopLevel(), _balance, seedFromServer, GobnomLogic.PlayerB);
            // the start fence as in the game: the first frame leaves right away
            b.ReceiveFrame(OverTheWire(a.TakeFrame(true)));
            a.ReceiveFrame(OverTheWire(b.TakeFrame(true)));

            Pump(a, b, 10);
            a.Schedule("cut", 0);
            Pump(a, b, 40);
            b.Schedule("cut", 1);
            Pump(a, b, 100);

            Assert.That(a.State.Tick, Is.GreaterThan(60), "the sims really ran (past the first hash exchange)");
            Assert.That(b.State.Tick, Is.EqualTo(a.State.Tick));
            Assert.That(GobnomLogic.StateHash(b.State), Is.EqualTo(GobnomLogic.StateHash(a.State)), "identical state after relaying both input streams");
            Assert.That(RopeCut(a, 0) && RopeCut(b, 0), Is.True, "a's cut on both sims");
            Assert.That(RopeCut(a, 1) && RopeCut(b, 1), Is.True, "b's cut on both sims");
            Assert.That(a.Desynced || b.Desynced, Is.False, "the hash guard stayed quiet");
        }

        [Test]
        public void TheInputDelayWindowPlansActionsAtNowPlusDelay()
        {
            var sim = new GobnomLockstep();
            sim.Start(CoopLevel(), _balance, 7, GobnomLogic.PlayerA);
            Assert.That(sim.Advance().stepped, Is.False, "no partner fence, no tick (lockstep)");
            Assert.That(sim.State.Tick, Is.EqualTo(0));

            var sim2 = new GobnomLockstep();
            sim2.Start(CoopLevel(), _balance, 7, GobnomLogic.PlayerA);
            var action = sim2.Schedule("cut", 0);
            var t = (int)action["t"];
            Assert.That(t, Is.EqualTo(sim2.InputDelay), "a fresh sim plans at tick 0 + delay");
            Assert.That(t, Is.InRange(3, 5), "delay window 3–5 ticks (C §3.8)");
            sim2.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            while (sim2.State.Tick < t)
            {
                Assert.That(RopeCut(sim2, 0), Is.False, "the rope holds before the planned tick");
                sim2.Advance();
            }
            Assert.That(RopeCut(sim2, 0), Is.False, "at tick t the action is still due");
            sim2.Advance();
            Assert.That(RopeCut(sim2, 0), Is.True, "IN tick t the cut happens — on both devices");
        }

        [Test]
        public void AHashMismatchStopsTheSimInsteadOfPlayingOn()
        {
            var sim = new GobnomLockstep();
            sim.Start(CoopLevel(), _balance, 11, GobnomLogic.PlayerA);
            sim.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            while (sim.State.Tick <= GobnomLockstep.HashTicksDefault) sim.Advance();
            Assert.That(GobnomLogic.IsOver(sim.State), Is.False, "the test level must not end early");
            var reports = sim.TakeHashes();
            Assert.That(reports.Count, Is.EqualTo(1), "exactly one hash report at tick 60");
            Assert.That(reports[0].t, Is.EqualTo(GobnomLockstep.HashTicksDefault));

            sim.ReceiveHash(GobnomLockstep.HashTicksDefault, "kaputt-vom-anderen-stern");
            Assert.That(sim.Desynced, Is.True);
            Assert.That(sim.DesyncTick, Is.EqualTo(GobnomLockstep.HashTicksDefault));
            Assert.That(sim.Advance().stepped, Is.False, "nothing computes after a desync");

            // the other arrival order: the partner hash is there before our own tick
            var sim2 = new GobnomLockstep();
            sim2.Start(CoopLevel(), _balance, 11, GobnomLogic.PlayerB);
            sim2.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            sim2.ReceiveHash(GobnomLockstep.HashTicksDefault, "falsch");
            while (!sim2.Desynced && sim2.State.Tick <= GobnomLockstep.HashTicksDefault + 1) sim2.Advance();
            Assert.That(sim2.Desynced, Is.True, "early foreign hashes trip the guard too");
        }

        [Test]
        public void ARejoinReplaysTheFrameBufferToTheLiveState()
        {
            const long seedFromServer = 424_242;
            var a = new GobnomLockstep();
            var b = new GobnomLockstep();
            a.Start(CoopLevel(), _balance, seedFromServer, GobnomLogic.PlayerA);
            b.Start(CoopLevel(), _balance, seedFromServer, GobnomLogic.PlayerB);
            // the server's buffer: ALL frames of both sides in send order
            var frames = new Dictionary<string, List<JObject>> { ["a"] = new List<JObject>(), ["b"] = new List<JObject>() };
            var hashByTick = new Dictionary<int, string>();
            var startA = a.TakeFrame(true);
            frames["a"].Add((JObject)startA.DeepClone());
            b.ReceiveFrame(startA);
            var startB = b.TakeFrame(true);
            frames["b"].Add((JObject)startB.DeepClone());
            a.ReceiveFrame(startB);
            Pump(a, b, 10, frames, hashByTick);
            a.Schedule("cut", 0);
            Pump(a, b, 60, frames, hashByTick);
            // the live sim a runs out to its fence: the replay may fast-forward to min(both fences)
            while (true)
            {
                var drain = a.Advance();
                if (!drain.stepped) break;
                hashByTick[a.State.Tick] = GobnomLogic.StateHash(a.State);
            }

            // b's device crashes → a new sim, GOBNOM_SNAPSHOT brings the frames
            var snapshot = OverTheWire(new JObject { ["a"] = new JArray(frames["a"]), ["b"] = new JArray(frames["b"]) });
            var rejoined = new GobnomLockstep();
            rejoined.Resume(CoopLevel(), _balance, seedFromServer, GobnomLogic.PlayerB, snapshot);
            var replayTick = rejoined.State.Tick;
            Assert.That(replayTick, Is.GreaterThan(20), "the replay really fast-forwards");
            Assert.That(RopeCut(rejoined, 0), Is.True, "a's cut from the history is replayed");
            Assert.That(hashByTick.ContainsKey(replayTick), Is.True, "the live sim has a hash for the replay tick");
            Assert.That(GobnomLogic.StateHash(rejoined.State), Is.EqualTo(hashByTick[replayTick]), "the rejoin replay lands exactly on the live state of tick " + replayTick);
            Assert.That(rejoined.Desynced, Is.False);
        }

        // ── the session in online mode ──

        /// <summary>Two online sessions joined by a wire that delivers frames and hashes <c>latency</c> wall frames later.</summary>
        private sealed class Wire
        {
            public readonly GobnomGame A, B;
            public int Frames, HashesA, HashesB;
            private readonly int _latency;
            private int _now;
            private readonly Queue<(int at, Action deliver)> _queue = new Queue<(int, Action)>();

            public Wire(GobnomGame a, GobnomGame b, int latency)
            {
                A = a;
                B = b;
                _latency = latency;
                a.FrameOut += f => Send(f, () => B);
                b.FrameOut += f => Send(f, () => A);
                a.HashOut += (t, h) => { HashesA++; _queue.Enqueue((_now + _latency, () => B.Net?.ReceiveHash(t, h))); };
                b.HashOut += (t, h) => { HashesB++; _queue.Enqueue((_now + _latency, () => A.Net?.ReceiveHash(t, h))); };
            }

            private void Send(JObject frame, Func<GobnomGame> to)
            {
                Frames++;
                var wire = OverTheWire(frame);
                _queue.Enqueue((_now + _latency, () => to().Net?.ReceiveFrame(wire)));
            }

            public void Step(double dt, bool bots)
            {
                _now++;
                while (_queue.Count > 0 && _queue.Peek().at <= _now) _queue.Dequeue().deliver();
                if (bots) { A.ApplyBot(); B.ApplyBot(); }
                A.Tick(dt);
                B.Tick(dt);
            }
        }

        private static bool Done(GobnomGame g) => g.Phase == GobnomGame.PhaseWon || g.Phase == GobnomGame.PhaseLost;

        [Test]
        public void EveryCoopLevelIsWonOnlineByTwoClientsThatOnlyPlayTheirOwnHalf([Values(0, 5)] int latency)
        {
            foreach (var level in GobnomData.LoadCoop())
            {
                var id = (int)level["id"];
                var solver = GobnomSolver.RunSolution(level, _balance, 31_337);
                var a = new GobnomGame(1);
                var b = new GobnomGame(2);
                var wire = new Wire(a, b, latency);
                var endHashA = ""; var endHashB = ""; var endTickA = -1; var endTickB = -1;
                a.PhaseChanged += p => { if (p == GobnomGame.PhaseWon || p == GobnomGame.PhaseLost) { endHashA = GobnomLogic.StateHash(a.Run); endTickA = a.Run.Tick; } };
                b.PhaseChanged += p => { if (p == GobnomGame.PhaseWon || p == GobnomGame.PhaseLost) { endHashB = GobnomLogic.StateHash(b.Run); endTickB = b.Run.Tick; } };
                Assert.That(a.StartOnline(id, 31_337, GobnomLogic.PlayerA), Is.True);
                Assert.That(b.StartOnline(id, 31_337, GobnomLogic.PlayerB), Is.True);
                Assert.That(a.OnlineRun && b.OnlineRun && a.Track == "coop" && a.LevelId == id, Is.True);
                for (var i = 0; i < 60 * 60 && !(Done(a) && Done(b)); i++) wire.Step(1.0 / 60.0, bots: true);

                Assert.That(a.Phase, Is.EqualTo(GobnomGame.PhaseWon), $"CN{id} (latency {latency}): a wins online");
                Assert.That(b.Phase, Is.EqualTo(GobnomGame.PhaseWon), $"CN{id} (latency {latency}): b wins online");
                Assert.That(endTickB, Is.EqualTo(endTickA), $"CN{id}: both sims end on the same tick");
                Assert.That(endHashB, Is.EqualTo(endHashA), $"CN{id}: identical final state on both devices");
                Assert.That(endTickA, Is.EqualTo(solver.Ticks), $"CN{id}: every planned action ran exactly on its plan tick (= the solver run)");
                Assert.That(a.LastStars, Is.EqualTo(solver.Stars));
                Assert.That(a.Net.Desynced || b.Net.Desynced, Is.False);
                Assert.That(GobnomProgress.IsCleared(a.Save, "coop", id) && GobnomProgress.IsCleared(b.Save, "coop", id), Is.True, $"CN{id} booked on both devices");
                if (endTickA > 120) Assert.That(wire.HashesA, Is.GreaterThan(0), "hashes were compared on the way");
            }
        }

        [Test]
        public void OnlineTouchesActOnlyOnTheOwnHalfAndACutIsPlannedOnce()
        {
            var g = new GobnomGame(5);
            Assert.That(g.StartOnline(1, 99, GobnomLogic.PlayerA), Is.True);
            var level = g.Level;
            g.Net.ReceiveFrame(new JObject { ["n"] = 1, ["upTo"] = 100_000, ["a"] = new JArray() });
            for (var i = 0; i < 100; i++) g.Tick(1.0 / 60.0); // past the establish beat
            Assert.That(g.AcceptsInput, Is.True);
            Assert.That(g.PlayerFor(new V2(800f, 100f)), Is.EqualTo("a"), "online every touch is ours");
            var split = (float)(double)level["split"]["at"];
            var axisX = (string)level["split"]["axis"] == "x";
            var own = axisX ? new V2(split - 200f, 150f) : new V2(480f, split - 100f);
            var foreign = axisX ? new V2(split + 200f, 150f) : new V2(480f, split + 100f);
            Assert.That(g.IsOwnSide(own), Is.True);
            Assert.That(g.IsOwnSide(foreign), Is.False);

            // a swipe across a's rope (CN1: rope 0 belongs to a); a second swipe within the delay plans nothing new
            var rope = g.Run.Ropes.First(r => r.Owner == "a");
            var candy = g.Run.CandyPos;
            var mid = new V2((rope.Anchor.X + candy.X) * 0.5f, (rope.Anchor.Y + candy.Y) * 0.5f);
            var along = new V2(candy.X - rope.Anchor.X, candy.Y - rope.Anchor.Y);
            var across = new V2(-along.Y, along.X) * (40f / Math.Max(1f, along.Length));
            var cut = g.Swipe(mid - across, mid + across, g.PlayerFor(mid));
            Assert.That(cut, Does.Contain(rope.Id), "the crossed rope is planned");
            Assert.That(rope.Cut, Is.False, "planned, not cut yet (it falls at now + delay on both devices)");
            Assert.That(g.Swipe(mid - across, mid + across, "a"), Is.Empty, "the same rope is not planned twice");
            JObject sent = null;
            g.FrameOut += f => sent = f;
            for (var i = 0; i < 10; i++) g.Tick(1.0 / 60.0);
            Assert.That(sent, Is.Not.Null, "the planned cut left in a frame");
            Assert.That(rope.Cut, Is.True, "the cut ran on its tick");
        }

        [Test]
        public void AStalledSimTakesTheServerResultAfterTheGrace()
        {
            var g = new GobnomGame(6);
            Assert.That(g.StartOnline(3, 1234, GobnomLogic.PlayerB), Is.True);
            for (var i = 0; i < 120; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.NetWaiting, Is.True, "no partner fence: the sim waits");
            Assert.That(g.Run.Tick, Is.EqualTo(0));
            g.ApplyServerResult("won", 2);
            for (var i = 0; i < 60; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Phase, Is.EqualTo(GobnomGame.PhasePlay), "within the grace the sim may still catch up");
            for (var i = 0; i < 90; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Phase, Is.EqualTo(GobnomGame.PhaseWon), "after the grace the server's result counts");
            Assert.That(g.LastStars, Is.EqualTo(GobnomLogic.StarsFor(2)));
            Assert.That(GobnomProgress.IsCleared(g.Save, "coop", 3), Is.True);

            // a desync / abort ends a running online round politely; the hot-seat retry is not offered online
            var h = new GobnomGame(7);
            h.StartOnline(2, 55, GobnomLogic.PlayerA);
            h.AbortOnline();
            Assert.That(h.Phase, Is.EqualTo(GobnomGame.PhaseSelect));
            Assert.That(h.OnlineRun, Is.False);
            h.Open("coop", 1);
            Assert.That(h.OnlineRun, Is.False, "a local level is hot-seat again");
        }
    }
}
