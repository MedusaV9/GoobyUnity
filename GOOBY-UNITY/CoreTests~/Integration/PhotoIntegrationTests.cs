using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Net;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Photo letters + InstantGooby against the real GOOBY-SERVER (mail.js W13B/W13C): Alice sends Bob a letter with a
    /// photo over REST, Bob gets MAIL_NEW with a photoId and loads the very same bytes from /api/mail/blob; Alice posts
    /// to the feed, Bob gets INSTANT_NEW, lists the feed, loads the photo from /api/instant/blob, leaves a carrot
    /// (Alice gets the INSTANT_LIKE push, a second like is idempotent) and acks the unseen badge. Own server per fixture.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class PhotoIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private Recorder _ra, _rb;
        private readonly List<InstantPost> _bobArrived = new List<InstantPost>();
        private readonly List<string> _aliceLiked = new List<string>();

        [OneTimeSetUp]
        public void Setup()
        {
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            _ra = new Recorder(_alice);
            _rb = new Recorder(_bob);
            _bob.InstantArrived += p => _bobArrived.Add(p);
            _alice.InstantLiked += (id, by, name, likes) => _aliceLiked.Add(id + ":" + by + ":" + name + ":" + likes);
            ConnectAll(_alice, _bob);
        }

        [Test, Order(1)]
        public void A_PostWithoutFriendsIsRefused()
        {
            InstantPostResult res = null;
            _alice.PostInstant("allein", NetPhoto.Placeholder(64, 48, 1), r => res = r);
            PumpUntil(() => res != null, "POST /api/instant");
            Assert.That(res.Ok, Is.False);
            Assert.That(res.Code, Is.EqualTo("NO_FRIENDS"));
            MakeFriends(_alice, _ra, _bob, _rb);
        }

        [Test, Order(2)]
        public void B_PhotoLetterArrivesAndThePhotoLoads()
        {
            var photo = NetPhoto.Placeholder(320, 240, 7);
            MailSendResult sent = null;
            _alice.SendPhotoMail(_bob.FriendCode, "Guck mal, mein Garten!", null, null, 0, photo, r => sent = r);
            PumpUntil(() => sent != null, "POST /api/mail");
            Assert.That(sent.Ok, Is.True, sent.Code);
            Assert.That(sent.PhotoDropped, Is.False);
            Assert.That(sent.Id, Does.StartWith("mail-"));

            PumpUntil(() => _rb.MailArrived.Any(m => m.Id == sent.Id), "MAIL_NEW at Bob");
            var mail = _rb.MailArrived.Last();
            Assert.That(mail.Text, Is.EqualTo("Guck mal, mein Garten!"));
            Assert.That(mail.PhotoId, Does.StartWith("mailp-"));
            Assert.That(mail.FromName, Is.EqualTo("Alice"));

            byte[] got = null;
            string code = null;
            _bob.FetchMailPhoto(mail.PhotoId, (ok, bytes, c) => { got = bytes; code = c; });
            PumpUntil(() => got != null || code != null, "GET /api/mail/blob");
            Assert.That(got, Is.EqualTo(photo), code);

            // only the recipient may load it
            string aliceCode = null;
            _alice.FetchMailPhoto(mail.PhotoId, (ok, bytes, c) => aliceCode = ok ? "OK" : c);
            PumpUntil(() => aliceCode != null, "GET /api/mail/blob as sender");
            Assert.That(aliceCode, Is.EqualTo("NOT_FOUND"));

            // the listed mailbox carries the photoId too
            var list = Await(cb => _bob.RefreshMail(cb), "MAIL_LIST");
            Assert.That(list.Ok, Is.True);
            Assert.That(_bob.FindMail(sent.Id).PhotoId, Is.EqualTo(mail.PhotoId));

            // a photo-only letter (no text) is fine as well
            MailSendResult photoOnly = null;
            _alice.SendPhotoMail(_bob.FriendCode, "", null, null, 0, NetPhoto.Placeholder(64, 48, 2), r => photoOnly = r);
            PumpUntil(() => photoOnly != null, "photo-only letter");
            Assert.That(photoOnly.Ok, Is.True, photoOnly.Code);
        }

        [Test, Order(3)]
        public void C_FeedPostReachesTheFriendWhoLikesIt()
        {
            var photo = NetPhoto.Placeholder(320, 240, 11);
            InstantPostResult posted = null;
            _alice.PostInstant("Flausch im Beet 🌷", photo, r => posted = r);
            PumpUntil(() => posted != null, "POST /api/instant");
            Assert.That(posted.Ok, Is.True, posted.Code);
            Assert.That(posted.Recipients, Is.EqualTo(1));

            PumpUntil(() => _bobArrived.Count == 1, "INSTANT_NEW at Bob");
            Assert.That(_bobArrived[0].Id, Is.EqualTo(posted.Id));
            Assert.That(_bobArrived[0].Caption, Is.EqualTo("Flausch im Beet 🌷"));
            Assert.That(_bobArrived[0].FromName, Is.EqualTo("Alice"));
            Assert.That(_bob.InstantUnseen, Is.EqualTo(1));
            // the author sees her own post in her feed (PostInstant refreshed it)
            PumpUntil(() => _alice.Feed.Any(p => p.Id == posted.Id), "own post in Alice's feed");
            Assert.That(_alice.FindPost(posted.Id).Mine, Is.True);

            var feed = Await(cb => _bob.RefreshFeed(cb), "FEED_LIST");
            Assert.That(feed.Ok, Is.True, feed.Code);
            Assert.That(_bob.Feed.Select(p => p.Id), Is.EqualTo(new[] { posted.Id }));
            var post = _bob.Feed[0];
            Assert.That(post.Mine, Is.False);
            Assert.That(post.Likes, Is.EqualTo(0));

            byte[] got = null;
            string code = null;
            _bob.FetchInstantPhoto(post.PhotoId, (ok, bytes, c) => { got = bytes; code = c; });
            PumpUntil(() => got != null || code != null, "GET /api/instant/blob");
            Assert.That(got, Is.EqualTo(photo), code);

            var like = Await(cb => _bob.LikeInstant(post.Id, cb), "INSTANT_LIKE");
            Assert.That(like.Ok, Is.True, like.Code);
            Assert.That(_bob.FindPost(post.Id).Liked, Is.True);
            Assert.That(_bob.FindPost(post.Id).Likes, Is.EqualTo(1));
            PumpUntil(() => _aliceLiked.Count == 1, "INSTANT_LIKE push at Alice");
            Assert.That(_aliceLiked[0], Is.EqualTo(posted.Id + ":" + _bob.FriendCode + ":Bob:1"));
            Assert.That(_alice.FindPost(posted.Id).Likes, Is.EqualTo(1));

            var again = Await(cb => _bob.LikeInstant(post.Id, cb), "INSTANT_LIKE again");
            Assert.That(again.Ok, Is.True);
            Assert.That(again.Data["already"]?.ToObject<bool>(), Is.True);
            PumpFor(300);
            Assert.That(_aliceLiked.Count, Is.EqualTo(1), "no second push");

            var self = Await(cb => _alice.LikeInstant(posted.Id, cb), "INSTANT_LIKE own");
            Assert.That(self.Code, Is.EqualTo("SELF"));

            var ack = Await(cb => _bob.AckFeed(cb), "FEED_ACK");
            Assert.That(ack.Ok, Is.True);
            Assert.That(_bob.InstantUnseen, Is.EqualTo(0));
            var relist = Await(cb => _bob.RefreshFeed(cb), "FEED_LIST after ack");
            Assert.That(relist.Ok, Is.True);
            Assert.That(_bob.FindPost(post.Id).Liked, Is.True, "liked survives a reload");
            Assert.That(_bob.InstantUnseen, Is.EqualTo(0));
        }
    }
}
