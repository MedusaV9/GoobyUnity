using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Photo letters (REST /api/mail with photoB64, lazy /api/mail/blob) and the InstantGooby feed (REST /api/instant,
    /// FEED_LIST/FEED_ACK/INSTANT_LIKE, pushes INSTANT_NEW/INSTANT_LIKE) against the fake transport + fake HTTP, plus the
    /// engine-free placeholder PNG of headless players.
    /// </summary>
    public class PhotoNetTests
    {
        private string _dir;
        private long _now;
        private List<FakeNetTransport> _fakes;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gooby-photo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _now = 1_000_000;
            _fakes = new List<FakeNetTransport>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { /* best effort */ }
        }

        private FakeNetTransport Fake => _fakes.Last();

        private static long SeqOf(JObject msg) => (long)msg["seq"];

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

        private void GoOnline(SocialClient c, int instantUnseen = 0)
        {
            c.Connect();
            Fake.Open();
            c.Pump();
            var welcome = new JObject
            {
                ["friendCode"] = "GOOBY-AB23",
                ["serverTime"] = 123,
                ["heartbeatSec"] = 20,
                ["friendRequests"] = new JArray(),
                ["palPending"] = new JArray(),
                ["pendingEvents"] = new JArray(),
                ["mailUnread"] = 0,
                ["instantUnseen"] = instantUnseen,
            };
            Fake.Receive("WELCOME", welcome, SeqOf(Fake.LastOfType("HELLO")));
            c.Pump();
            Assert.That(c.Status, Is.EqualTo(NetStatus.Online));
        }

        private static byte[] Jpeg(int size = 64)
        {
            var b = new byte[size];
            b[0] = 0xFF;
            b[1] = 0xD8;
            b[2] = 0xFF;
            for (var i = 3; i < size; i++) b[i] = (byte)(i * 7);
            return b;
        }

        private static JObject Post(string id, string from, bool mine = false, int likes = 0, bool liked = false) => new JObject
        {
            ["id"] = id,
            ["kind"] = "instant",
            ["from"] = from,
            ["fromName"] = mine ? "Anna" : "Ben",
            ["fromGooby"] = mine ? "Flausch" : "Knopf",
            ["at"] = 5000,
            ["caption"] = "Garten!",
            ["photoId"] = "instp-" + id,
            ["likes"] = likes,
            ["liked"] = liked,
            ["mine"] = mine,
        };

        // ------------------------------------------------------------------ NetPhoto

        [Test]
        public void PlaceholderIsAValidPngUnderTheServerLimit()
        {
            var png = NetPhoto.Placeholder(320, 240, 3);
            Assert.That(NetPhoto.IsPng(png), Is.True);
            Assert.That(NetPhoto.Check(png), Is.EqualTo(""));
            Assert.That(png.Length, Is.LessThan(NetPhoto.MaxBytes));
            // IHDR: width/height big-endian after the 8-byte signature + length + "IHDR"
            Assert.That((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], Is.EqualTo(320));
            Assert.That((png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23], Is.EqualTo(240));
            // IEND chunk with its well-known CRC closes the file
            Assert.That(png.Skip(png.Length - 8).ToArray(), Is.EqualTo(new byte[] { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }));
            // IDAT inflates to one filter byte + RGB per row
            var idatLen = (png[33] << 24) | (png[34] << 16) | (png[35] << 8) | png[36];
            Assert.That(System.Text.Encoding.ASCII.GetString(png, 37, 4), Is.EqualTo("IDAT"));
            using (var ms = new MemoryStream(png, 41 + 2, idatLen - 6))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                ds.CopyTo(outMs);
                Assert.That(outMs.Length, Is.EqualTo((320 * 3 + 1) * 240));
            }
            Assert.That(NetPhoto.Placeholder(320, 240, 4), Is.Not.EqualTo(png), "seed changes the picture");
        }

        [Test]
        public void PhotoCheckMirrorsTheServerRules()
        {
            Assert.That(NetPhoto.Check(null), Is.EqualTo(NetPhoto.NoPhoto));
            Assert.That(NetPhoto.Check(new byte[0]), Is.EqualTo(NetPhoto.NoPhoto));
            Assert.That(NetPhoto.Check(new byte[] { 1, 2, 3, 4 }), Is.EqualTo(NetPhoto.BadPhoto));
            Assert.That(NetPhoto.Check(Jpeg()), Is.EqualTo(""));
            Assert.That(NetPhoto.Check(Jpeg(NetPhoto.MaxBytes + 1)), Is.EqualTo(NetPhoto.TooLarge));
            Assert.That(NetPhoto.FromBase64("%%%"), Is.Null);
            Assert.That(NetPhoto.FromBase64(NetPhoto.ToBase64(Jpeg())), Is.EqualTo(Jpeg()));
        }

        // ------------------------------------------------------------------ photo letters

        [Test]
        public void PhotoMailGoesOverRestWithBearerAndBase64()
        {
            var c = NewSocial(out var http);
            GoOnline(c);
            MailSendResult result = null;
            var photo = Jpeg();
            c.SendPhotoMail("gooby-cd45", "", null, null, 0, photo, r => result = r);
            Assert.That(result, Is.Null, "waits for the REST reply");
            Assert.That(Fake.SentOfType("MAIL_SEND"), Is.Empty, "no WS path for photo letters");
            var call = http.Calls.Single();
            Assert.That(call.Method, Is.EqualTo("POST"));
            Assert.That(call.Url, Is.EqualTo("http://192.168.0.10:8080/api/mail"));
            Assert.That(call.Bearer, Is.EqualTo(c.Identity.Bearer));
            var body = JObject.Parse(call.Body);
            Assert.That((string)body["to"], Is.EqualTo("GOOBY-CD45"));
            Assert.That(Convert.FromBase64String((string)body["photoB64"]), Is.EqualTo(photo));
            Assert.That((string)body["clientId"], Is.Not.Empty);

            http.Respond(call, 200, "{\"ok\":true,\"id\":\"mail-7\",\"sentToday\":2,\"dailyLimit\":20}");
            c.Pump();
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Id, Is.EqualTo("mail-7"));
            Assert.That(result.SentToday, Is.EqualTo(2));
            Assert.That(result.PhotoDropped, Is.False);

            // server refusals keep their code
            MailSendResult refused = null;
            c.SendPhotoMail("GOOBY-CD45", "hi", null, null, 0, photo, r => refused = r);
            http.Respond(http.Calls.Last(), 403, "{\"ok\":false,\"code\":\"NOT_FRIENDS\"}");
            c.Pump();
            Assert.That(refused.Ok, Is.False);
            Assert.That(refused.Code, Is.EqualTo("NOT_FRIENDS"));

            MailSendResult bad = null;
            c.SendPhotoMail("GOOBY-CD45", "hi", null, null, 0, new byte[] { 1, 2, 3 }, r => bad = r);
            Assert.That(bad.Code, Is.EqualTo(NetPhoto.BadPhoto), "local magic check");
            Assert.That(http.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void PhotoMailDegradesToTextWhenOfflineOrUnreachable()
        {
            var c = NewSocial(out var http);
            MailSendResult queued = null;
            c.SendPhotoMail("GOOBY-CD45", "Schau mal!", null, null, 0, Jpeg(), r => queued = r);
            Assert.That(queued.Queued, Is.True);
            Assert.That(queued.PhotoDropped, Is.True);
            Assert.That(c.OutboxCount, Is.EqualTo(1));
            MailSendResult photoOnly = null;
            c.SendPhotoMail("GOOBY-CD45", "  ", null, null, 0, Jpeg(), r => photoOnly = r);
            Assert.That(photoOnly.Ok || photoOnly.Queued, Is.False);
            Assert.That(photoOnly.Code, Is.EqualTo(NetErrorCodes.Offline));
            Assert.That(http.Calls, Is.Empty);

            GoOnline(c);
            var flushed = Fake.LastOfType("MAIL_SEND");
            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = true, ["id"] = "mail-1" }, SeqOf(flushed));
            c.Pump();
            Assert.That(c.OutboxCount, Is.EqualTo(0));

            // REST unreachable (status 0) → the same letter goes out over WS without the photo, same clientId
            MailSendResult viaWs = null;
            c.SendPhotoMail("GOOBY-CD45", "Zweiter Versuch", "food", "carrot", 1, Jpeg(), r => viaWs = r);
            var call = http.Calls.Single();
            var clientId = (string)JObject.Parse(call.Body)["clientId"];
            http.Respond(call, 0, "timeout");
            c.Pump();
            var ws = Fake.LastOfType("MAIL_SEND");
            Assert.That((string)ws["d"]["clientId"], Is.EqualTo(clientId));
            Assert.That((string)ws["d"]["item"]["id"], Is.EqualTo("carrot"));
            Assert.That(ws["d"]["photoB64"], Is.Null);
            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = true, ["id"] = "mail-2" }, SeqOf(ws));
            c.Pump();
            Assert.That(viaWs.Ok, Is.True);
            Assert.That(viaWs.PhotoDropped, Is.True);
        }

        [Test]
        public void PhotoFetchCoalescesCachesAndRejectsGarbage()
        {
            var c = NewSocial(out var http);
            GoOnline(c);
            var got = new List<string>();
            c.FetchMailPhoto("mailp-1", (ok, bytes, code) => got.Add("a:" + ok + ":" + (bytes?.Length ?? 0)));
            c.FetchMailPhoto("mailp-1", (ok, bytes, code) => got.Add("b:" + ok + ":" + (bytes?.Length ?? 0)));
            var call = http.Calls.Single();
            Assert.That(call.Method, Is.EqualTo("GET"));
            Assert.That(call.Url, Is.EqualTo("http://192.168.0.10:8080/api/mail/blob/mailp-1"));
            Assert.That(call.Bearer, Is.EqualTo(c.Identity.Bearer));
            http.Respond(call, 200, new JObject { ["ok"] = true, ["photoB64"] = NetPhoto.ToBase64(Jpeg(80)) }.ToString());
            c.Pump();
            Assert.That(got, Is.EqualTo(new[] { "a:True:80", "b:True:80" }));
            c.FetchMailPhoto("mailp-1", (ok, bytes, code) => got.Add("c:" + ok));
            Assert.That(http.Calls.Count, Is.EqualTo(1), "cached");
            Assert.That(got.Last(), Is.EqualTo("c:True"));

            string code2 = null;
            c.FetchInstantPhoto("instp-9", (ok, bytes, code) => code2 = code);
            Assert.That(http.Calls.Last().Url, Is.EqualTo("http://192.168.0.10:8080/api/instant/blob/instp-9"));
            http.Respond(http.Calls.Last(), 200, "{\"ok\":true,\"photoB64\":\"AAAA\"}");
            c.Pump();
            Assert.That(code2, Is.EqualTo(NetPhoto.BadPhoto));
            string code3 = null;
            c.FetchInstantPhoto("instp-x", (ok, bytes, code) => code3 = code);
            http.Respond(http.Calls.Last(), 404, "{\"ok\":false,\"code\":\"NOT_FOUND\"}");
            c.Pump();
            Assert.That(code3, Is.EqualTo("NOT_FOUND"));
        }

        // ------------------------------------------------------------------ InstantGooby

        [Test]
        public void FeedListPushLikeAndAck()
        {
            var c = NewSocial(out _);
            GoOnline(c, instantUnseen: 2);
            Assert.That(c.InstantUnseen, Is.EqualTo(2));
            c.RefreshFeed();
            var list = Fake.LastOfType("FEED_LIST");
            Assert.That((int)list["d"]["limit"], Is.EqualTo(SocialClient.FeedCap));
            Fake.Receive("FEED_STATE", new JObject
            {
                ["posts"] = new JArray(Post("inst-2", "GOOBY-CD45", likes: 1), Post("inst-1", "GOOBY-AB23", mine: true)),
                ["total"] = 2,
                ["cap"] = 30,
                ["unseen"] = 1,
                ["offset"] = 0,
            }, SeqOf(list));
            c.Pump();
            Assert.That(c.FeedLoaded, Is.True);
            Assert.That(c.Feed.Select(p => p.Id), Is.EqualTo(new[] { "inst-2", "inst-1" }));
            Assert.That(c.Feed[0].Caption, Is.EqualTo("Garten!"));
            Assert.That(c.Feed[0].PhotoId, Is.EqualTo("instp-inst-2"));
            Assert.That(c.Feed[1].Mine, Is.True);
            Assert.That(c.InstantUnseen, Is.EqualTo(1));

            var arrived = new List<string>();
            c.InstantArrived += p => arrived.Add(p.Id + ":" + p.FromName);
            Fake.Receive("INSTANT_NEW", new JObject { ["post"] = Post("inst-3", "GOOBY-CD45"), ["unseen"] = 2 });
            c.Pump();
            Assert.That(arrived, Is.EqualTo(new[] { "inst-3:Ben" }));
            Assert.That(c.Feed[0].Id, Is.EqualTo("inst-3"));
            Assert.That(c.FeedTotal, Is.EqualTo(3));
            Assert.That(c.InstantUnseen, Is.EqualTo(2));

            NetReply like = null;
            c.LikeInstant("inst-2", r => like = r);
            var likeMsg = Fake.LastOfType("INSTANT_LIKE");
            Assert.That((string)likeMsg["d"]["id"], Is.EqualTo("inst-2"));
            Fake.Receive("OK", new JObject { ["likes"] = 2 }, SeqOf(likeMsg));
            c.Pump();
            Assert.That(like.Ok, Is.True);
            Assert.That(c.FindPost("inst-2").Liked, Is.True);
            Assert.That(c.FindPost("inst-2").Likes, Is.EqualTo(2));

            NetReply self = null;
            c.LikeInstant("inst-1", r => self = r);
            Assert.That(self.Code, Is.EqualTo("SELF"), "own post: no request");
            Assert.That(Fake.SentOfType("INSTANT_LIKE").Count, Is.EqualTo(1));

            var liked = new List<string>();
            c.InstantLiked += (id, by, name, n) => liked.Add(id + ":" + by + ":" + name + ":" + n);
            Fake.Receive("INSTANT_LIKE", new JObject { ["id"] = "inst-1", ["by"] = "GOOBY-CD45", ["byName"] = "Ben", ["likes"] = 1 });
            c.Pump();
            Assert.That(liked, Is.EqualTo(new[] { "inst-1:GOOBY-CD45:Ben:1" }));
            Assert.That(c.FindPost("inst-1").Likes, Is.EqualTo(1));

            c.AckFeed();
            var ack = Fake.LastOfType("FEED_ACK");
            Fake.Receive("OK", new JObject { ["unseen"] = 0 }, SeqOf(ack));
            c.Pump();
            Assert.That(c.InstantUnseen, Is.EqualTo(0));
        }

        [Test]
        public void PostInstantValidatesThenPostsOverRestAndRefreshes()
        {
            var c = NewSocial(out var http);
            InstantPostResult r = null;
            c.PostInstant("hi", Jpeg(), x => r = x);
            Assert.That(r.Code, Is.EqualTo(NetErrorCodes.Offline), "a feed post needs a connection");
            GoOnline(c);
            c.PostInstant("hi", null, x => r = x);
            Assert.That(r.Code, Is.EqualTo(NetPhoto.NoPhoto));
            c.PostInstant(new string('x', NetPhoto.CaptionMax + 1), Jpeg(), x => r = x);
            Assert.That(r.Code, Is.EqualTo("CAPTION_TOO_LONG"));
            Assert.That(http.Calls, Is.Empty);

            r = null;
            var png = NetPhoto.Placeholder(160, 120, 1);
            c.PostInstant("Mein Garten 🌷", png, x => r = x);
            var call = http.Calls.Single();
            Assert.That(call.Method, Is.EqualTo("POST"));
            Assert.That(call.Url, Is.EqualTo("http://192.168.0.10:8080/api/instant"));
            Assert.That(call.Bearer, Is.EqualTo(c.Identity.Bearer));
            var body = JObject.Parse(call.Body);
            Assert.That((string)body["caption"], Is.EqualTo("Mein Garten 🌷"));
            Assert.That(Convert.FromBase64String((string)body["photoB64"]), Is.EqualTo(png));
            var before = Fake.SentOfType("FEED_LIST").Count;
            http.Respond(call, 200, "{\"ok\":true,\"id\":\"inst-5\",\"recipients\":2,\"sentToday\":1,\"dailyLimit\":20}");
            c.Pump();
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Id, Is.EqualTo("inst-5"));
            Assert.That(r.Recipients, Is.EqualTo(2));
            Assert.That(Fake.SentOfType("FEED_LIST").Count, Is.EqualTo(before + 1), "own post shows up");

            c.PostInstant("x", png, x => r = x);
            http.Respond(http.Calls.Last(), 400, "{\"ok\":false,\"code\":\"NO_FRIENDS\"}");
            c.Pump();
            Assert.That(r.Code, Is.EqualTo("NO_FRIENDS"));
            c.PostInstant("x", png, x => r = x);
            http.Respond(http.Calls.Last(), 0, "refused");
            c.Pump();
            Assert.That(r.Code, Is.EqualTo(NetErrorCodes.NoConnection));
        }

        [Test]
        public void NewServerClearsFeedAndPhotoCache()
        {
            var c = NewSocial(out var http);
            GoOnline(c);
            Fake.Receive("INSTANT_NEW", new JObject { ["post"] = Post("inst-1", "GOOBY-CD45"), ["unseen"] = 1 });
            c.Pump();
            c.FetchInstantPhoto("instp-inst-1", (ok, b, code) => { });
            http.Respond(http.Calls.Last(), 200, new JObject { ["ok"] = true, ["photoB64"] = NetPhoto.ToBase64(Jpeg()) }.ToString());
            c.Pump();
            Assert.That(c.CachedPhoto("instp-inst-1"), Is.Not.Null);
            c.Configure(NetAddress.Parse("10.0.0.2"), "Anna", "Flausch", "");
            Assert.That(c.Feed, Is.Empty);
            Assert.That(c.InstantUnseen, Is.EqualTo(0));
            Assert.That(c.CachedPhoto("instp-inst-1"), Is.Null);
        }
    }
}
