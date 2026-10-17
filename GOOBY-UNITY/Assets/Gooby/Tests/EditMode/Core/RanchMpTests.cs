using System;
using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Ranch multiplayer without the server (rmp_kurse.gd, rmp_interp.gd, rmp_state.gd): courses and hashes as in
    /// ranchmp.js, the start grid, the pose buffer, the keepsake statistics, and the run — a bot finishes both courses
    /// with every checkpoint in order and no segment faster than the server's minimum, the tag reach on the meadow.
    /// </summary>
    public class RanchMpTests
    {
        private static List<RmpPlayer> Players(params string[] codes)
        {
            var list = new List<RmpPlayer>();
            foreach (var c in codes) list.Add(new RmpPlayer { Code = c, Name = c.Substring(c.Length - 2) });
            return list;
        }

        [Test]
        public void CoursesAndHashesMatchTheServer()
        {
            Assert.That(RmpKurse.KursHash("grasbahn"), Is.EqualTo("grasbahn:v1:8"));
            Assert.That(RmpKurse.KursHash("hof_parcours"), Is.EqualTo("hof_parcours:v1:8"));
            Assert.That(RmpKurse.KursHash("weide_fangen"), Is.EqualTo("weide_fangen:v1:0"));
            Assert.That(RmpKurse.KursHash("mond"), Is.EqualTo(""));
            Assert.That(RmpKurse.StandardKurs(RmpKurse.Rennen), Is.EqualTo("grasbahn"));
            Assert.That(RmpKurse.StandardKurs(RmpKurse.Parcours), Is.EqualTo("hof_parcours"));
            Assert.That(RmpKurse.StandardKurs(RmpKurse.Fangen), Is.EqualTo("weide_fangen"));
            Assert.That(RmpKurse.Get("grasbahn").Checkpoints[2], Is.EqualTo(new V2(-420f, 120f)));
            Assert.That(RmpKurse.Get("hof_parcours").Radius, Is.EqualTo(10.0));
            Assert.That(RmpKurse.MaxSpieler(RmpKurse.Rennen), Is.EqualTo(4));
            Assert.That(RmpKurse.MaxSpieler(RmpKurse.Besuch), Is.EqualTo(2));
            Assert.That(RmpKurse.GaitIndex("galopp"), Is.EqualTo(4));
            Assert.That(RmpKurse.GaitName(2), Is.EqualTo("trab"));
            Assert.That(RmpKurse.GaitIndex("quatsch"), Is.EqualTo(0));
        }

        [Test]
        public void TheStartGridStandsBehindTheFirstGateFacingTheSecond()
        {
            var a = RmpKurse.StartPosition("grasbahn", 0, 2);
            var b = RmpKurse.StartPosition("grasbahn", 1, 2);
            var cp0 = new V2(-330f, 60f);
            Assert.That((a - b).Length, Is.EqualTo(3.0).Within(0.001), "3 m apart");
            Assert.That((a - cp0).Length, Is.EqualTo(Math.Sqrt(36.0 + 2.25)).Within(0.001), "6 m behind, 1.5 m aside (rmp_kurse.gd)");
            var h = RmpKurse.StartHeading("grasbahn", 0, 2);
            var vor = new V2(Math.Sin(h), Math.Cos(h)) * -1.0;
            var zuCp1 = (new V2(-356f, 102f) - a).Normalized();
            Assert.That(vor.Dot(zuCp1), Is.GreaterThan(0.999f), "the horse looks at checkpoint 1");
            var f0 = RmpKurse.StartPosition("weide_fangen", 0, 4);
            var f2 = RmpKurse.StartPosition("weide_fangen", 2, 4);
            Assert.That((f0 - new V2(-380f, 90f)).Length, Is.EqualTo(35.0).Within(0.01), "half the arena radius");
            Assert.That((f0 - f2).Length, Is.EqualTo(70.0).Within(0.01), "opposite sides");
            Assert.That(RmpKurse.CheckpointErreicht("grasbahn", 0, a), Is.True, "the start box lies in gate 0");
            Assert.That(RmpKurse.CheckpointErreicht("grasbahn", 1, a), Is.False);
        }

        [Test]
        public void ThePoseBufferInterpolatesTheShortWayAndFreezesOnSilence()
        {
            var b = new RmpInterp();
            Assert.That(b.SampleAt(100), Is.Null);
            b.Push(1000, new V2(0f, 0f), 0.0, 3.1, 2);
            b.Push(1100, new V2(10f, 0f), 0.0, -3.1, 4);
            var mid = b.SampleAt(1200).Value; // 150 ms back → halfway
            Assert.That(mid.Pos.X, Is.EqualTo(5f).Within(0.01f));
            Assert.That(Math.Abs(mid.Yaw), Is.GreaterThan(3.1), "across ±π, not through 0");
            Assert.That(mid.Gait, Is.EqualTo(4));
            Assert.That(b.SampleAt(1100).Value.Pos.X, Is.EqualTo(0f), "before the buffer: the first pose");
            var hold = b.SampleAt(1500).Value;
            Assert.That(hold.Pos.X, Is.EqualTo(10f), "lag spike: hold the last pose");
            Assert.That(hold.Stale, Is.False);
            Assert.That(b.SampleAt(2400).Value.Stale, Is.True, "1.2 s of silence");
            b.Reset();
            Assert.That(b.HasData, Is.False);
        }

        [Test]
        public void TheSaveBooksEveryResultOnce()
        {
            var state = new JObject { ["ranch"] = new JObject { ["gekauft"] = true, ["mp"] = new JObject { ["fremd"] = 7, ["statistik"] = "kaputt" } } };
            var win = new RmpResult { RewardId = "rmp-a-ALICE", Mode = "rennen", Kurs = "grasbahn", Rank = 1, ZeitMs = 31000, Ranked = true };
            Assert.That(RmpState.ErgebnisVerbuchen(state, win, "2026-10-01"), Is.True);
            Assert.That(RmpState.ErgebnisVerbuchen(state, win, "2026-10-01"), Is.False, "the WELCOME repeats it after a reconnect");
            Assert.That(RmpState.Statistik(state, "rennen"), Is.EqualTo((1, 1)));
            var lost = new RmpResult { RewardId = "rmp-b-ALICE", Mode = "fangen", Kurs = "weide_fangen", Rank = 2, ZeitMs = 6000 };
            RmpState.ErgebnisVerbuchen(state, lost, "2026-10-01");
            Assert.That(RmpState.Statistik(state, "fangen"), Is.EqualTo((1, 0)));
            var gaveUp = new RmpResult { RewardId = "rmp-c-ALICE", Mode = "parcours", Rank = 1, Dnf = true };
            RmpState.ErgebnisVerbuchen(state, gaveUp, "2026-10-01");
            Assert.That(RmpState.Statistik(state, "parcours"), Is.EqualTo((1, 0)), "given up never wins");
            var mp = RmpState.Lese(state);
            Assert.That(J(mp["fremd"]), Is.EqualTo(7), "foreign keys survive");
            Assert.That(((JArray)mp["verlauf"]).Count, Is.EqualTo(3));
            Assert.That(state["ranch"]["gekauft"].Value<bool>(), Is.True);
            for (var i = 0; i < 70; i++) RmpState.ErgebnisVerbuchen(state, new RmpResult { RewardId = "r" + i, Mode = "rennen", Rank = 3 }, "d");
            mp = RmpState.Lese(state);
            Assert.That(((JArray)mp["verlauf"]).Count, Is.EqualTo(RmpState.VerlaufMax));
            Assert.That(((JArray)mp["gesehen"]).Count, Is.EqualTo(RmpState.GesehenMax));
            Assert.That(RmpState.Statistik(state, "rennen"), Is.EqualTo((71, 1)));
        }

        private static int J(JToken t) => t.Value<int>();

        /// <summary>The run with an instant server: every due checkpoint is confirmed at once, the finish too.</summary>
        private static (double zeit, List<double> segmente) RideToTheEnd(string mode)
        {
            var lauf = new RmpLauf(mode, RmpKurse.StandardKurs(mode), Players("GOOBY-AAAA", "GOOBY-BBBB"), "GOOBY-AAAA");
            var segmente = new List<double>();
            var letzte = 0.0;
            lauf.CheckpointDue += idx =>
            {
                Assert.That(idx, Is.EqualTo(lauf.NextCheckpoint), "in order");
                segmente.Add(lauf.Zeit - letzte);
                letzte = lauf.Zeit;
                lauf.Confirm(idx + 1);
            };
            lauf.FinishDue += lauf.ConfirmFinish;
            const double dt = 1.0 / 30.0;
            for (var i = 0; i < 30 * 180 && !lauf.InZiel; i++)
            {
                RmpBot.Drive(lauf, "", 0);
                lauf.Tick(dt, true);
            }
            Assert.That(lauf.InZiel, Is.True, mode + " finished");
            Assert.That(lauf.NextCheckpoint, Is.EqualTo(lauf.CheckpointCount));
            return (lauf.Zeit, segmente);
        }

        [Test]
        public void ABotRidesBothCoursesInOrderAtAFairPace()
        {
            var (zeitRennen, segRennen) = RideToTheEnd(RmpKurse.Rennen);
            Assert.That(segRennen.Count, Is.EqualTo(8));
            Assert.That(zeitRennen, Is.InRange(18.0, 75.0), "a gallop lap of the grass track");
            foreach (var s in segRennen) Assert.That(s * 1000.0, Is.GreaterThanOrEqualTo(900.0), "never faster than minSegmentMs (unranked)");
            var (zeitParcours, segParcours) = RideToTheEnd(RmpKurse.Parcours);
            Assert.That(segParcours.Count, Is.EqualTo(8));
            Assert.That(zeitParcours, Is.InRange(15.0, 90.0));
            foreach (var s in segParcours) Assert.That(s * 1000.0, Is.GreaterThanOrEqualTo(700.0));
        }

        [Test]
        public void TheRideHasTheOpenWorldTempoWithoutStamina()
        {
            var lauf = new RmpLauf(RmpKurse.Rennen, "grasbahn", Players("GOOBY-AAAA"), "GOOBY-AAAA");
            var c = lauf.Controller;
            lauf.Tick(0.1, false);
            Assert.That(c.Active, Is.False, "the countdown holds the horse");
            c.GaitUp(); c.GaitUp(); c.GaitUp();
            Assert.That(c.Gait, Is.EqualTo("galopp"));
            for (var i = 0; i < 30 * 40; i++) lauf.Tick(1.0 / 30.0, true);
            Assert.That(c.Gait, Is.EqualTo("galopp"), "40 s of gallop: no stamina in the meadow");
            Assert.That(c.Tempo, Is.EqualTo(14.5).Within(0.05), "ranch_welt_reiter.gd TEMPO_GALOPP");
            var (center, half) = RmpKurse.Feld("grasbahn");
            Assert.That(Math.Abs(c.Pos.X - center.X), Is.LessThanOrEqualTo(half.X + 0.01f), "the meadow keeps the horse");
            Assert.That(Math.Abs(c.Pos.Y - center.Y), Is.LessThanOrEqualTo(half.Y + 0.01f));
        }

        [Test]
        public void ARejectedCheckpointRestsBeforeTheNextTry()
        {
            var lauf = new RmpLauf(RmpKurse.Rennen, "grasbahn", Players("GOOBY-AAAA"), "GOOBY-AAAA");
            var tries = 0;
            lauf.CheckpointDue += idx => { tries++; lauf.Reject(); };
            for (var i = 0; i < 30 * 2; i++) lauf.Tick(1.0 / 30.0, true);
            Assert.That(tries, Is.InRange(1, 3), "about one try per " + RmpLauf.RetryPauseS + " s after the 1.1-s segment guard");
            lauf.Reject(3);
            Assert.That(lauf.NextCheckpoint, Is.EqualTo(3), "the server's progress wins");
            lauf.Resync(8, true);
            Assert.That(lauf.InZiel, Is.True);
        }

        [Test]
        public void TheCatcherReachesOnlyRidersCloseBy()
        {
            var lauf = new RmpLauf(RmpKurse.Fangen, "weide_fangen", Players("GOOBY-AAAA", "GOOBY-BBBB", "GOOBY-CCCC"), "GOOBY-AAAA");
            var me = lauf.Controller.Pos;
            Assert.That(lauf.Remotes.Count, Is.EqualTo(2));
            lauf.PushPeer("GOOBY-BBBB", 0, new RmpPeerPose { Pos = me + new V2(3.5f, 0f) });
            lauf.PushPeer("GOOBY-CCCC", 0, new RmpPeerPose { Pos = me + new V2(0f, 8f) });
            Assert.That(lauf.TagTarget(100), Is.EqualTo("GOOBY-BBBB"), "3 m tag radius + 0.8 m");
            lauf.PushPeer("GOOBY-BBBB", 100, new RmpPeerPose { Pos = me + new V2(6f, 0f) });
            Assert.That(lauf.TagTarget(400), Is.Null, "nobody in reach");
            Assert.That(lauf.NearestPeerM(400), Is.EqualTo(6.0).Within(0.01));
            Assert.That(lauf.TagTarget(5000), Is.Null, "frozen riders cannot be caught");
            var race = new RmpLauf(RmpKurse.Rennen, "grasbahn", Players("GOOBY-AAAA", "GOOBY-BBBB"), "GOOBY-AAAA");
            Assert.That(race.TagTarget(0), Is.Null, "races have no catcher");
        }
    }
}
