using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Ranch friends leaderboards (ranchmp.js REST): board directions, value windows and ghost rules like the server,
    /// parsing of board/score/ghost replies, the four REST calls against the fake HTTP (offline nothing is sent), and the
    /// tournament racing a loaded friend's ghost instead of the own best run.
    /// </summary>
    public class RanchBoardTests
    {
        private string _dir;
        private long _now;
        private List<FakeNetTransport> _fakes;

        [OneTimeSetUp]
        public void Content() => Fixtures.InstallContentSource();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gooby-rboard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _now = 1_000_000;
            _fakes = new List<FakeNetTransport>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { /* best effort */ }
        }

        private SocialClient NewSocial(out FakeNetHttp http)
        {
            http = new FakeNetHttp();
            var client = new SocialClient(Path.Combine(_dir, "social"), () =>
            {
                var f = new FakeNetTransport();
                _fakes.Add(f);
                return f;
            }, () => _now, http);
            client.Configure(NetAddress.Parse("192.168.0.10"), "Anna", "Flausch", "");
            return client;
        }

        private void GoOnline(SocialClient c)
        {
            c.Connect();
            var fake = _fakes.Last();
            fake.Open();
            c.Pump();
            var welcome = new JObject
            {
                ["friendCode"] = "GOOBY-AB23", ["serverTime"] = 123, ["heartbeatSec"] = 20, ["friendRequests"] = new JArray(),
                ["palPending"] = new JArray(), ["pendingEvents"] = new JArray(), ["mailUnread"] = 0, ["instantUnseen"] = 0,
            };
            fake.Receive("WELCOME", welcome, (long)fake.LastOfType("HELLO")["seq"]);
            c.Pump();
            Assert.That(c.Status, Is.EqualTo(NetStatus.Online));
        }

        private static string GhostB64(string disziplin)
        {
            var rec = RanchCompGhost.NeuerRecorder(disziplin, new V2(3f, -4f));
            for (var i = 0; i < 40; i++) RanchCompGhost.Tick(rec, 0.1, new V2(3f + i * 0.8f, -4f + i * 0.2f), 0.0, 1.2, "galopp");
            return RanchCompGhost.ToB64(rec);
        }

        private static JObject Line(string code, string name, int wert, long at = 5000, bool ghost = false) => new JObject
        {
            ["friendCode"] = code, ["name"] = name, ["goobyName"] = name + "i", ["wert"] = wert, ["at"] = at, ["hatGhost"] = ghost,
        };

        // ------------------------------------------------------------------ rules

        [Test]
        public void DirectionsAndGhostsFollowTheServer()
        {
            Assert.That(RanchBoards.Alle().ToList(), Is.EqualTo(new[]
            {
                "grasbahn", "hof_parcours", "rw5_springen", "rw5_dressur", "rw5_gelaende", "rw5_rennen", "rw5_trail", "rw5_schau", "rw5_tonnen", "rw5_zeit",
            }));
            Assert.That(RanchBoards.Richtung("grasbahn"), Is.EqualTo(RanchBoards.Ab));
            Assert.That(RanchBoards.Richtung("hof_parcours"), Is.EqualTo(RanchBoards.Ab));
            Assert.That(RanchBoards.Richtung("weide_fangen"), Is.EqualTo(""), "the tag meadow has no board");
            Assert.That(RanchBoards.Richtung("rw5_springen"), Is.EqualTo(RanchBoards.Auf));
            Assert.That(RanchBoards.Richtung("rw5_gelaende"), Is.EqualTo(RanchBoards.Ab));
            Assert.That(RanchBoards.Richtung("rw5_tonnen"), Is.EqualTo(RanchBoards.Ab));
            Assert.That(RanchBoards.Richtung("rw5_nope"), Is.EqualTo(""));
            Assert.That(RanchBoards.GeistMoeglich("rw5_springen"), Is.True);
            Assert.That(RanchBoards.GeistMoeglich("rw5_schau"), Is.False, "RcompLauf records no ghost for the show");
            Assert.That(RanchBoards.GeistMoeglich("rw5_rennen"), Is.False);
            Assert.That(RanchBoards.GeistMoeglich("grasbahn"), Is.False, "live courses keep no G5 ghosts");
            Assert.That(RanchBoards.Disziplin("rw5_trail"), Is.EqualTo("trail"));
            Assert.That(RanchBoards.Disziplin("grasbahn"), Is.EqualTo(""));
        }

        [Test]
        public void RunValuesBecomeServerValuesOrNothing()
        {
            Assert.That(RanchBoards.WertFuer("springen", 87.6), Is.EqualTo(88), "points rounded");
            Assert.That(RanchBoards.WertFuer("tonnen", 24.3605), Is.EqualTo(24361), "seconds → ms");
            Assert.That(RanchBoards.WertFuer("zeit", 2.5), Is.Null, "faster than 3 s is refused by the server");
            Assert.That(RanchBoards.WertFuer("dressur", 0.2), Is.Null, "0 points are no entry");
            Assert.That(RanchBoards.WertFuer("trail", 100_000.4), Is.EqualTo(100_000));
            Assert.That(RanchBoards.WertFuer("trail", 100_001.0), Is.Null);
            Assert.That(RanchBoards.WertFuer("gelaende", 31 * 60.0), Is.Null, "over 30 minutes");
            Assert.That(RanchBoards.WertFuer("nope", 10.0), Is.Null);
            Assert.That(RanchBoards.WertFuer("springen", double.NaN), Is.Null);
            var gras = RmpKurse.Get("grasbahn");
            var min = Math.Max(1000, gras.MinSegmentMs * gras.Checkpoints.Length);
            Assert.That(RanchBoards.WertOk("grasbahn", min), Is.True);
            Assert.That(RanchBoards.WertOk("grasbahn", min - 1), Is.False, "faster than the physical minimum of the course");
            Assert.That(RanchBoards.Anzeige("rw5_tonnen", 24361), Is.EqualTo("24.36"));
            Assert.That(RanchBoards.Anzeige("grasbahn", 29093, new System.Globalization.CultureInfo("de-DE")), Is.EqualTo("29,09"), "hundredths tell close races apart");
            Assert.That(RanchBoards.Anzeige("rw5_springen", 88), Is.EqualTo("88"));
        }

        // ------------------------------------------------------------------ parsing

        [Test]
        public void BoardParsesAndSortsInItsDirection()
        {
            var times = RanchBoard.FromJson(new JObject
            {
                ["ok"] = true, ["kurs"] = "grasbahn", ["richtung"] = "ab", ["me"] = "GOOBY-AB23",
                ["entries"] = new JArray(Line("GOOBY-AB23", "Anna", 41_200), Line("GOOBY-BOB2", "Bob", 38_900), Line("GOOBY-CARA", "Cara", 41_200, 1000),
                    new JObject { ["name"] = "no code" }, Line("GOOBY-BOB2", "Bob again", 1)),
            });
            Assert.That(times.Ok, Is.True);
            Assert.That(times.Entries.Select(e => e.Code), Is.EqualTo(new[] { "GOOBY-BOB2", "GOOBY-CARA", "GOOBY-AB23" }), "smaller first, then earlier");
            Assert.That(times.Rank, Is.EqualTo(3));
            Assert.That(times.Mine.Wert, Is.EqualTo(41_200));
            Assert.That(times.HasFriends, Is.True);
            Assert.That(times.Entries[0].Gooby, Is.EqualTo("Bobi"));

            var points = RanchBoard.FromJson(new JObject
            {
                ["ok"] = true, ["kurs"] = "rw5_springen", ["richtung"] = "auf", ["me"] = "GOOBY-AB23",
                ["entries"] = new JArray(Line("GOOBY-AB23", "Anna", 70), Line("GOOBY-BOB2", "Bob", 92, ghost: true)),
            });
            Assert.That(points.Richtung, Is.EqualTo(RanchBoards.Auf));
            Assert.That(points.Entries.Select(e => e.Wert), Is.EqualTo(new[] { 92, 70 }), "larger first");
            Assert.That(points.Entries[0].HatGhost, Is.True);
            Assert.That(points.Rank, Is.EqualTo(2));

            var fail = RanchBoard.FromJson(JObject.Parse("{\"ok\":false,\"code\":\"BAD_KURS\"}"));
            Assert.That(fail.Ok, Is.False);
            Assert.That(fail.Code, Is.EqualTo("BAD_KURS"));
            Assert.That(RanchBoard.FromJson(null).Code, Is.EqualTo("BAD_REPLY"));
        }

        [Test]
        public void ScoreAndGhostRepliesParse()
        {
            var score = RanchScoreResult.FromJson(JObject.Parse("{\"ok\":true,\"ranked\":true,\"verbessert\":true,\"best\":38900}"), "grasbahn");
            Assert.That((score.Ok, score.Ranked, score.Improved, score.Best), Is.EqualTo((true, true, true, 38900)));
            var kept = RanchScoreResult.FromJson(JObject.Parse("{\"ok\":true,\"gespeichert\":false,\"best\":80}"), "rw5_springen");
            Assert.That((kept.Ok, kept.Improved, kept.Best), Is.EqualTo((true, false, 80)));
            Assert.That(RanchScoreResult.FromJson(JObject.Parse("{\"ok\":false,\"code\":\"RATE_LIMIT\"}"), "x").Code, Is.EqualTo("RATE_LIMIT"));

            var b64 = GhostB64("springen");
            var ghost = RanchGhostReply.FromJson(new JObject { ["ok"] = true, ["v"] = 1, ["kurs"] = "rw5_springen", ["wert"] = 92, ["b64"] = b64 }, "rw5_springen", "GOOBY-BOB2");
            Assert.That((ghost.Ok, ghost.Wert, ghost.FriendCode), Is.EqualTo((true, 92, "GOOBY-BOB2")));
            Assert.That(RanchCompGhost.FromB64(ghost.B64).Ok, Is.True);
            Assert.That(RanchGhostReply.FromJson(JObject.Parse("{\"ok\":true,\"wert\":5}"), "rw5_springen", "X").Code, Is.EqualTo("NO_GHOST"));
        }

        // ------------------------------------------------------------------ REST calls

        [Test]
        public void OfflineNothingIsSent()
        {
            var c = NewSocial(out var http);
            RanchScoreResult score = null, ghost = null;
            RanchBoard board = null;
            RanchGhostReply fetched = null;
            c.SubmitRanchScore("grasbahn", 40_000, r => score = r);
            c.FetchRanchLeaderboard("grasbahn", b => board = b);
            c.UploadRanchGhost("rw5_springen", 80, GhostB64("springen"), r => ghost = r);
            c.FetchRanchGhost("rw5_springen", "GOOBY-BOB2", g => fetched = g);
            c.Pump();
            Assert.That(http.Calls, Is.Empty);
            Assert.That(new[] { score.Code, board.Code, ghost.Code, fetched.Code }, Is.All.EqualTo(NetErrorCodes.Offline));
        }

        [Test]
        public void ScoreBoardAndGhostCallsTalkRestLikeRanchmp()
        {
            var c = NewSocial(out var http);
            GoOnline(c);

            // refused before any request: unknown board, implausible value, ghost on a board without ghosts
            RanchScoreResult bad = null, tooFast = null, noGhost = null;
            c.SubmitRanchScore("weide", 40_000, r => bad = r);
            c.SubmitRanchScore("rw5_zeit", 1_200, r => tooFast = r);
            c.UploadRanchGhost("rw5_schau", 80, GhostB64("schau"), r => noGhost = r);
            Assert.That((bad.Code, tooFast.Code, noGhost.Code), Is.EqualTo(("BAD_KURS", "BAD_ZEIT", "BAD_KURS")));
            Assert.That(http.Calls, Is.Empty);

            RanchScoreResult score = null;
            c.SubmitRanchScore("grasbahn", 40_000, r => score = r);
            var post = http.Calls[0];
            Assert.That(post.Method, Is.EqualTo("POST"));
            Assert.That(post.Url, Does.EndWith("/api/rmp/score"));
            Assert.That(post.Bearer, Is.EqualTo(c.Identity.Bearer));
            Assert.That(JObject.Parse(post.Body).ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"kurs\":\"grasbahn\",\"wert\":40000}"));
            http.Respond(post, 200, "{\"ok\":true,\"ranked\":true,\"verbessert\":true,\"best\":40000}");
            c.Pump();
            Assert.That(score.Improved, Is.True);

            RanchBoard board = null;
            c.FetchRanchLeaderboard("rw5_springen", b => board = b);
            Assert.That(http.Calls[1].Url, Does.EndWith("/api/rmp/leaderboard/rw5_springen"));
            http.Respond(http.Calls[1], 200, new JObject
            {
                ["ok"] = true, ["kurs"] = "rw5_springen", ["richtung"] = "auf", ["me"] = "GOOBY-AB23",
                ["entries"] = new JArray(Line("GOOBY-BOB2", "Bob", 92, ghost: true), Line("GOOBY-AB23", "Anna", 70)),
            }.ToString());
            c.Pump();
            Assert.That((board.Ok, board.Rank), Is.EqualTo((true, 2)));

            var b64 = GhostB64("springen");
            RanchScoreResult put = null;
            c.UploadRanchGhost("rw5_springen", 70, b64, r => put = r);
            var putCall = http.Calls[2];
            Assert.That(putCall.Method, Is.EqualTo("PUT"));
            Assert.That(putCall.Url, Does.EndWith("/api/rmp/ghost/rw5_springen"));
            var putBody = JObject.Parse(putCall.Body);
            Assert.That(((int)putBody["wert"], (string)putBody["b64"]), Is.EqualTo((70, b64)));
            http.Respond(putCall, 200, "{\"ok\":true,\"gespeichert\":true,\"best\":70}");
            c.Pump();
            Assert.That(put.Improved, Is.True);

            RanchGhostReply fetched = null;
            c.FetchRanchGhost("rw5_springen", "gooby-bob2", g => fetched = g);
            Assert.That(http.Calls[3].Url, Does.EndWith("/api/rmp/ghost/rw5_springen/GOOBY-BOB2"));
            http.Respond(http.Calls[3], 200, new JObject { ["ok"] = true, ["v"] = 1, ["kurs"] = "rw5_springen", ["wert"] = 92, ["b64"] = b64 }.ToString());
            c.Pump();
            Assert.That((fetched.Ok, fetched.Wert), Is.EqualTo((true, 92)));

            RanchGhostReply stranger = null;
            c.FetchRanchGhost("rw5_springen", "GOOBY-CARA", g => stranger = g);
            http.Respond(http.Calls[4], 403, "{\"ok\":false,\"code\":\"NOT_FRIENDS\"}");
            c.Pump();
            Assert.That(stranger.Code, Is.EqualTo("NOT_FRIENDS"));
        }

        // ------------------------------------------------------------------ tournament

        [Test]
        public void TournamentRacesTheLoadedFriendsGhost()
        {
            var game = new RanchTurnierGame("normal", 4242, null, "2026-10-01");
            var friend = GhostB64("springen");
            game.FreundGeist = d => d == "springen" ? friend : null;
            Assert.That(game.OeffneEinweisung("springen"), Is.True);
            Assert.That(game.LaufMitFreundGeist, Is.True, "a loaded friend's ghost replaces the own best run");
            game.ZurueckZumMenu();
            Assert.That(game.OeffneEinweisung("dressur"), Is.True);
            Assert.That(game.LaufMitFreundGeist, Is.False, "no ghost loaded for dressage");
            game.ZurueckZumMenu();
            game.SetGeistAn(false);
            Assert.That(game.OeffneEinweisung("springen"), Is.True);
            Assert.That(game.LaufMitFreundGeist, Is.False, "the ghost toggle switches the friend's ghost off too");
        }
    }
}
