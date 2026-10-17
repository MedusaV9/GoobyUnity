using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>An InstantGooby post as the viewer sees it (postView of mail.js: likes/liked/mine depend on the viewer).</summary>
    public sealed class InstantPost
    {
        public string Id { get; internal set; } = "";
        public string From { get; internal set; } = "";
        public string FromName { get; internal set; } = "";
        public string FromGooby { get; internal set; } = "";
        /// <summary>Unix ms (server clock).</summary>
        public long At { get; internal set; }
        public string Caption { get; internal set; } = "";
        /// <summary>Loads lazily via GET /api/instant/blob/:id (<see cref="SocialClient.FetchInstantPhoto"/>).</summary>
        public string PhotoId { get; internal set; } = "";
        /// <summary>Carrots left by friends (one per friend).</summary>
        public int Likes { get; internal set; }
        /// <summary>We already left our carrot.</summary>
        public bool Liked { get; internal set; }
        /// <summary>Our own post (no like button; it sits in our feed too).</summary>
        public bool Mine { get; internal set; }

        public static InstantPost FromJson(JObject o)
        {
            if (o == null) return null;
            var id = NetJson.Str(o["id"]);
            if (id.Length == 0) return null;
            return new InstantPost
            {
                Id = id,
                From = NetJson.Str(o["from"]),
                FromName = NetJson.Str(o["fromName"]),
                FromGooby = NetJson.Str(o["fromGooby"]),
                At = NetJson.Long(o["at"]),
                Caption = NetJson.Str(o["caption"]),
                PhotoId = NetJson.Str(o["photoId"]),
                Likes = Math.Max(0, NetJson.Int(o["likes"])),
                Liked = NetJson.Bool(o["liked"]),
                Mine = NetJson.Bool(o["mine"]),
            };
        }

        public override string ToString() => Id + " " + From + " '" + Caption + "' likes=" + Likes + (Mine ? " (mine)" : "");
    }

    /// <summary>Outcome of <see cref="SocialClient.PostInstant"/>.</summary>
    public sealed class InstantPostResult
    {
        public bool Ok { get; internal set; }
        /// <summary>"" on success, else NO_PHOTO, BAD_PHOTO, PHOTO_TOO_LARGE, CAPTION_TOO_LONG, NO_FRIENDS, DAILY_LIMIT, OFFLINE…</summary>
        public string Code { get; internal set; } = "";
        public string Id { get; internal set; } = "";
        public string ClientId { get; internal set; } = "";
        public bool Dupe { get; internal set; }
        /// <summary>Friends whose feed got the post.</summary>
        public int Recipients { get; internal set; }
        public int SentToday { get; internal set; }
        public int DailyLimit { get; internal set; }

        public override string ToString() => Ok ? "OK(" + Id + " → " + Recipients + ")" : "FAIL(" + Code + ")";
    }

    /// <summary>
    /// Photos on top of the mail backend (mail.js W13B/W13C): letters WITH a photo go over REST
    /// (POST /api/mail {to, text, item?, clientId, photoB64} — a 512 KB photo does not fit the 16 KB WS frame), received
    /// photos load lazily (GET /api/mail/blob/:id → {photoB64}). InstantGooby is a friends feed: POST /api/instant
    /// {caption, photoB64, clientId} fans the post out to every friend + the author, FEED_LIST → FEED_STATE pulls it,
    /// FEED_ACK clears the unseen badge, INSTANT_LIKE leaves one carrot per friend and post; pushes INSTANT_NEW
    /// {post, unseen} to friends and INSTANT_LIKE {id, by, byName, likes} to the author. REST replies come back through
    /// <see cref="INetHttp.Pump"/>, i.e. inside <see cref="Pump"/> on the main thread like every other event.
    /// </summary>
    public sealed partial class SocialClient
    {
        public const int FeedCap = 30;
        /// <summary>Decoded photos kept in memory (letters + feed), oldest dropped first.</summary>
        public const int PhotoCacheMax = 40;

        private readonly List<InstantPost> _feed = new List<InstantPost>();
        private readonly Dictionary<string, byte[]> _photoCache = new Dictionary<string, byte[]>();
        private readonly LinkedList<string> _photoOrder = new LinkedList<string>();
        private readonly Dictionary<string, List<Action<bool, byte[], string>>> _photoWaiting = new Dictionary<string, List<Action<bool, byte[], string>>>();

        /// <summary>The feed as last listed (newest first) plus live INSTANT_NEW arrivals on top.</summary>
        public IReadOnlyList<InstantPost> Feed => _feed;
        public int FeedTotal { get; private set; }
        /// <summary>True once a FEED_LIST answered on this server.</summary>
        public bool FeedLoaded { get; private set; }

        /// <summary>A friend posted (INSTANT_NEW push); <see cref="InstantUnseen"/> is already updated.</summary>
        public event Action<InstantPost> InstantArrived;
        /// <summary>(postId, byCode, byName, likes): a friend left a carrot on one of OUR posts.</summary>
        public event Action<string, string, string, int> InstantLiked;

        // ------------------------------------------------------------------ photo letters

        /// <summary>
        /// A letter with a photo (JPEG/PNG ≤ 512 KB) over REST. Without a photo this is <see cref="SendMail"/>. Offline or
        /// when the REST call cannot reach the server the letter still goes out (or waits in the outbox) as text only
        /// — <see cref="MailSendResult.PhotoDropped"/> tells the UI; a photo-only letter then fails with OFFLINE.
        /// </summary>
        public void SendPhotoMail(string to, string text, string itemTyp, string itemId, int menge, byte[] photo, Action<MailSendResult> cb)
        {
            if (photo == null || photo.Length == 0)
            {
                SendMail(to, text, itemTyp, itemId, menge, cb);
                return;
            }
            var target = Upper(to);
            var body = text ?? "";
            var hasItem = !string.IsNullOrEmpty(itemTyp) && !string.IsNullOrEmpty(itemId);
            string localError = null;
            if (target.Length == 0) localError = "NOT_FOUND";
            else if (target == FriendCode) localError = "SELF";
            else if (body.Length > TextMax) localError = "TEXT_TOO_LONG";
            else localError = NullIfEmpty(NetPhoto.Check(photo));
            if (localError != null)
            {
                Callback(cb, new MailSendResult { Code = localError });
                return;
            }
            var clientId = NetOutbox.NewClientId();
            var payload = new JObject { ["to"] = target, ["text"] = body, ["clientId"] = clientId };
            if (hasItem)
            {
                payload["item"] = new JObject
                {
                    ["typ"] = itemTyp,
                    ["id"] = itemId,
                    ["menge"] = Math.Max(1, Math.Min(GiftQtyMax, menge)),
                };
            }
            if (!Session.IsOnline || !Endpoint.Valid)
            {
                FallbackWithoutPhoto(payload, clientId, cb);
                return;
            }
            var restBody = (JObject)payload.DeepClone();
            restBody["photoB64"] = NetPhoto.ToBase64(photo);
            _http.Send("POST", Endpoint.HttpBase + "/api/mail", Identity.Bearer, restBody.ToString(Formatting.None), (status, respText) =>
            {
                var o = NetJson.ParseObject(respText);
                if (status == 200 && o != null && NetJson.Bool(o["ok"]))
                {
                    Callback(cb, new MailSendResult
                    {
                        Ok = true,
                        Id = NetJson.Str(o["id"]),
                        ClientId = clientId,
                        Dupe = NetJson.Bool(o["dupe"]),
                        SentToday = NetJson.Int(o["sentToday"]),
                        DailyLimit = NetJson.Int(o["dailyLimit"]),
                    });
                    return;
                }
                if (status == 0)
                {
                    // server unreachable over HTTP: same clientId → the text-only retry is idempotent server-side
                    FallbackWithoutPhoto(payload, clientId, cb);
                    return;
                }
                Callback(cb, new MailSendResult
                {
                    Code = HttpCode(status, o),
                    ClientId = clientId,
                    SentToday = NetJson.Int(o?["sentToday"]),
                    DailyLimit = NetJson.Int(o?["dailyLimit"]),
                });
            });
        }

        private void FallbackWithoutPhoto(JObject payload, string clientId, Action<MailSendResult> cb)
        {
            var hasText = NetJson.Str(payload["text"]).Trim().Length > 0;
            if (!hasText && payload["item"] == null)
            {
                Callback(cb, new MailSendResult { Code = NetErrorCodes.Offline, ClientId = clientId, PhotoDropped = true });
                return;
            }
            SendMailPayload(payload, clientId, true, cb);
        }

        /// <summary>Photo of a received letter (only the recipient may load it). cb(ok, jpeg/png bytes, code).</summary>
        public void FetchMailPhoto(string photoId, Action<bool, byte[], string> cb) => FetchPhoto("/api/mail/blob/", photoId, cb);

        /// <summary>Photo of a feed post (anyone whose feed holds the post, author included). cb(ok, bytes, code).</summary>
        public void FetchInstantPhoto(string photoId, Action<bool, byte[], string> cb) => FetchPhoto("/api/instant/blob/", photoId, cb);

        /// <summary>Cached photo bytes or null (no request).</summary>
        public byte[] CachedPhoto(string photoId) => !string.IsNullOrEmpty(photoId) && _photoCache.TryGetValue(photoId, out var b) ? b : null;

        private void FetchPhoto(string path, string photoId, Action<bool, byte[], string> cb)
        {
            if (string.IsNullOrEmpty(photoId))
            {
                Invoke(cb, false, null, "NOT_FOUND");
                return;
            }
            var cached = CachedPhoto(photoId);
            if (cached != null)
            {
                Invoke(cb, true, cached, "");
                return;
            }
            if (!Endpoint.Valid)
            {
                Invoke(cb, false, null, NetErrorCodes.InvalidEndpoint);
                return;
            }
            if (_photoWaiting.TryGetValue(photoId, out var waiting))
            {
                waiting.Add(cb);
                return;
            }
            _photoWaiting[photoId] = new List<Action<bool, byte[], string>> { cb };
            _http.Send("GET", Endpoint.HttpBase + path + Uri.EscapeDataString(photoId), Identity.Bearer, null, (status, body) =>
            {
                var o = NetJson.ParseObject(body);
                byte[] bytes = null;
                var code = "";
                if (status == 200 && o != null && NetJson.Bool(o["ok"]))
                {
                    bytes = NetPhoto.FromBase64(NetJson.Str(o["photoB64"]));
                    if (bytes == null || !NetPhoto.IsImage(bytes))
                    {
                        bytes = null;
                        code = NetPhoto.BadPhoto;
                    }
                }
                else code = HttpCode(status, o);
                if (bytes != null) CachePhoto(photoId, bytes);
                _photoWaiting.TryGetValue(photoId, out var all);
                _photoWaiting.Remove(photoId);
                if (all == null) return;
                foreach (var w in all) Invoke(w, bytes != null, bytes, code);
            });
        }

        private void CachePhoto(string photoId, byte[] bytes)
        {
            if (_photoCache.ContainsKey(photoId)) return;
            _photoCache[photoId] = bytes;
            _photoOrder.AddLast(photoId);
            while (_photoOrder.Count > PhotoCacheMax)
            {
                var old = _photoOrder.First.Value;
                _photoOrder.RemoveFirst();
                _photoCache.Remove(old);
            }
        }

        // ------------------------------------------------------------------ InstantGooby feed

        /// <summary>Pulls the feed (newest first, up to 30 posts) and the unseen count.</summary>
        public void RefreshFeed(Action<NetReply> cb = null)
        {
            Session.Request("FEED_LIST", new JObject { ["offset"] = 0, ["limit"] = FeedCap }, r =>
            {
                if (r.Ok && r.Type == "FEED_STATE")
                {
                    _feed.Clear();
                    if (r.Data["posts"] is JArray arr)
                    {
                        foreach (var t in arr)
                        {
                            var p = InstantPost.FromJson(t as JObject);
                            if (p != null) _feed.Add(p);
                        }
                    }
                    FeedTotal = NetJson.Int(r.Data["total"], _feed.Count);
                    InstantUnseen = Math.Max(0, NetJson.Int(r.Data["unseen"], InstantUnseen));
                    FeedLoaded = true;
                    MarkDirty();
                }
                Callback(cb, r);
            });
        }

        /// <summary>"Alles gesehen" — clears the unseen badge (server-idempotent).</summary>
        public void AckFeed(Action<NetReply> cb = null)
        {
            Session.Request("FEED_ACK", new JObject(), r =>
            {
                if (r.Ok)
                {
                    InstantUnseen = Math.Max(0, NetJson.Int(r.Data["unseen"], 0));
                    MarkDirty();
                }
                Callback(cb, r);
            });
        }

        /// <summary>Leaves a carrot on a friend's post (one per friend; a second like answers ok + already). Own posts → SELF.</summary>
        public void LikeInstant(string postId, Action<NetReply> cb = null)
        {
            var post = FindPost(postId);
            if (post != null && post.Mine)
            {
                Callback(cb, NetReply.Fail("SELF"));
                return;
            }
            Session.Request("INSTANT_LIKE", new JObject { ["id"] = postId ?? "" }, r =>
            {
                if (r.Ok)
                {
                    var p = FindPost(postId);
                    if (p != null)
                    {
                        p.Liked = true;
                        p.Likes = Math.Max(p.Likes, NetJson.Int(r.Data["likes"], p.Likes + 1));
                    }
                    MarkDirty();
                }
                Callback(cb, r);
            });
        }

        /// <summary>
        /// Posts a photo (required, JPEG/PNG ≤ 512 KB) with a caption (≤ 120) to all friends. Needs a connection — a
        /// feed post is a moment, not a letter, so nothing waits in the outbox. Refreshes the feed on success.
        /// </summary>
        public void PostInstant(string caption, byte[] photo, Action<InstantPostResult> cb)
        {
            var text = caption ?? "";
            var localError = NullIfEmpty(NetPhoto.Check(photo));
            if (localError == null && text.Length > NetPhoto.CaptionMax) localError = "CAPTION_TOO_LONG";
            if (localError == null && (!Session.IsOnline || !Endpoint.Valid)) localError = NetErrorCodes.Offline;
            if (localError != null)
            {
                Callback(cb, new InstantPostResult { Code = localError });
                return;
            }
            var clientId = NetOutbox.NewClientId();
            var body = new JObject { ["caption"] = text, ["photoB64"] = NetPhoto.ToBase64(photo), ["clientId"] = clientId };
            _http.Send("POST", Endpoint.HttpBase + "/api/instant", Identity.Bearer, body.ToString(Formatting.None), (status, respText) =>
            {
                var o = NetJson.ParseObject(respText);
                var ok = status == 200 && o != null && NetJson.Bool(o["ok"]);
                var result = new InstantPostResult
                {
                    Ok = ok,
                    Code = ok ? "" : status == 0 ? NetErrorCodes.NoConnection : HttpCode(status, o),
                    Id = NetJson.Str(o?["id"]),
                    ClientId = clientId,
                    Dupe = NetJson.Bool(o?["dupe"]),
                    Recipients = NetJson.Int(o?["recipients"]),
                    SentToday = NetJson.Int(o?["sentToday"]),
                    DailyLimit = NetJson.Int(o?["dailyLimit"]),
                };
                if (ok && Session.IsOnline) RefreshFeed();
                Callback(cb, result);
            });
        }

        public InstantPost FindPost(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in _feed) if (p.Id == id) return p;
            return null;
        }

        private void OnInstantNew(JObject d)
        {
            InstantUnseen = Math.Max(0, NetJson.Int(d["unseen"], InstantUnseen + 1));
            MarkDirty();
            var post = InstantPost.FromJson(d["post"] as JObject);
            if (post == null) return;
            var old = FindPost(post.Id);
            if (old != null) _feed.Remove(old);
            else FeedTotal++;
            _feed.Insert(0, post);
            while (_feed.Count > FeedCap) _feed.RemoveAt(_feed.Count - 1);
            if (FeedTotal > FeedCap) FeedTotal = FeedCap;
            Raise(() => InstantArrived?.Invoke(post), "InstantArrived");
        }

        private void OnInstantLikedPush(JObject d)
        {
            var id = NetJson.Str(d["id"]);
            var by = NetJson.Str(d["by"]);
            var byName = NetJson.Str(d["byName"]);
            var likes = NetJson.Int(d["likes"]);
            var post = FindPost(id);
            if (post != null) post.Likes = Math.Max(post.Likes, likes);
            MarkDirty();
            Raise(() => InstantLiked?.Invoke(id, by, byName, likes), "InstantLiked");
        }

        private void ClearInstantState()
        {
            _feed.Clear();
            FeedTotal = 0;
            FeedLoaded = false;
            _photoCache.Clear();
            _photoOrder.Clear();
            _photoWaiting.Clear();
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static void Invoke(Action<bool, byte[], string> cb, bool ok, byte[] bytes, string code)
        {
            if (cb == null) return;
            NetLog.Guard(() => cb(ok, bytes, code), "photo callback");
        }
    }
}
