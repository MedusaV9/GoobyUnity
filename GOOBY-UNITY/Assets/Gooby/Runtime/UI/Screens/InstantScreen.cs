using System;
using System.Text;
using Gooby.Core.Net;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// InstantGooby (instant_gooby_app.gd in the online-window look): left the "Neues Foto" card — a polaroid of the
    /// game view (snapped when the screen opens, "Neu knipsen" again), a short caption (120) and "Posten" to all
    /// friends; right the feed of friends' posts (plus our own) as tilted polaroids on cream cards with author, time,
    /// carrot count and "Möhre da lassen". Opening the feed acks the unseen badge. Offline it says so and keeps the
    /// last listed posts. Stable names: <c>instant-caption</c>, <c>instant-post</c>, <c>instant-snap</c>,
    /// <c>instant-like-&lt;postId&gt;</c>, <c>instant-card-&lt;postId&gt;</c>.
    /// </summary>
    public sealed class InstantScreen : UiScreen
    {
        private static readonly Color TilePfirsich = new Color(1f, 0.89f, 0.82f);

        private readonly GameHost _host;
        private OnlineService Online => _host.Online;
        private Label _sub, _tileBadge, _count, _offlineNote;
        private VisualElement _statusDot, _feed, _previewSlot;
        private TextField _caption;
        private Button _post, _snap;
        private byte[] _photo;
        private bool _placeholder;
        private Texture2D _preview;
        private bool _posting, _acking;
        private string _feedSig;

        public InstantScreen(GameHost host)
        {
            _host = host;
            Id = "instant";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-online");
            root.AddToClassList("gb-instant");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);

            var panel = Ux.El("gb-online-panel", "gb-instant-panel");
            Ux.Abs(panel, 110, 32, 110, 32);
            root.Add(panel);

            // ── header: camera tile with the unseen count, title + status pill, reload, close ──
            var head = Ux.Row("gb-on-head");
            var tile = Ux.IconTile("camera", TilePfirsich);
            _tileBadge = Ux.Badge("");
            _tileBadge.style.display = DisplayStyle.None;
            tile.Add(_tileBadge);
            head.Add(tile);
            var hcol = Ux.Col("gb-on-head-text");
            hcol.Add(Ux.Text(t.T("online.instant.titel"), "gb-on-title"));
            var statusPill = Ux.Row("gb-on-status");
            _statusDot = Ux.El("gb-status-dot");
            statusPill.Add(_statusDot);
            _sub = Ux.Text("", "gb-nowrap");
            statusPill.Add(_sub);
            hcol.Add(statusPill);
            head.Add(hcol);
            var reload = Ux.IconTextBtn("redo", t.T("online.instant.neu_laden"), () => { if (Online.Client.IsOnline) Online.Client.RefreshFeed(_ => AckIfUnseen()); }, "gb-btn-secondary", "gb-btn-sm");
            reload.name = "instant-refresh";
            head.Add(reload);
            head.Add(Ux.IconBtn("close", Close, "gb-btn-icon-sm"));
            panel.Add(head);

            var body = Ux.Row("gb-on-body");
            panel.Add(body);

            // ── left: new post ──
            var side = Ux.Col("gb-on-side", "gb-instant-side");
            var card = Ux.El("gb-on-card", "gb-instant-compose");
            var ch = Ux.Row("gb-on-card-head");
            ch.Add(Ux.IconTile("camera", TilePfirsich));
            ch.Add(Ux.Text(t.T("online.instant.neuer_post"), "gb-on-card-title"));
            card.Add(ch);
            _previewSlot = Ux.Row("gb-instant-preview-slot");
            card.Add(_previewSlot);
            _snap = Ux.IconTextBtn("camera", t.T("online.instant.knipsen"), () => { Snap(); ShowPreview(); }, "gb-btn-teal", "gb-btn-sm", "gb-on-wide");
            _snap.name = "instant-snap";
            card.Add(_snap);
            _caption = new TextField { maxLength = NetPhoto.CaptionMax };
            _caption.name = "instant-caption";
            _caption.AddToClassList("gb-textfield");
            _caption.AddToClassList("gb-instant-caption");
            _caption.textEdition.placeholder = t.T("online.instant.caption_hint");
            _caption.RegisterValueChangedCallback(_ => RefreshCount());
            card.Add(_caption);
            _count = Ux.Text("", "gb-compose-count", "gb-instant-count");
            card.Add(_count);
            _post = Ux.IconTextBtn("sparkle", t.T("online.instant.posten"), Post, "gb-btn-primary", "gb-on-wide");
            _post.name = "instant-post";
            card.Add(_post);
            _offlineNote = Ux.Text(t.T("online.instant.offline"), "gb-on-hint", "gb-instant-offline");
            card.Add(_offlineNote);
            side.Add(card);
            body.Add(side);

            // ── right: the feed ──
            var feedScroll = new ScrollView(ScrollViewMode.Vertical);
            feedScroll.AddToClassList("gb-instant-feed-scroll");
            _feed = Ux.Row("gb-instant-feed");
            _feed.name = "instant-feed";
            feedScroll.Add(_feed);
            body.Add(feedScroll);

            Snap();
            ShowPreview();
            RefreshCount();
            Refresh();
        }

        public override void OnShown()
        {
            Online.Changed -= Refresh;
            Online.Changed += Refresh;
            var c = Online.Client;
            if (c.IsOnline) c.RefreshFeed(_ => AckIfUnseen());
            Refresh();
        }

        public override void OnHidden()
        {
            Online.Changed -= Refresh;
            if (!_host.Ui.IsOpen(this)) DropPhoto();
        }

        private void Close()
        {
            Sfx.PlayUi("soft_back");
            _host.Ui.Pop();
        }

        /// <summary>The feed is on screen: whatever is unseen counts as seen (badge off in the friends header).</summary>
        private void AckIfUnseen()
        {
            var c = Online.Client;
            if (_acking || !c.IsOnline || c.InstantUnseen <= 0 || _host.Ui.Current != this) return;
            _acking = true;
            c.AckFeed(_ => _acking = false);
        }

        // ── photo ───────────────────────────────────────────────────────────

        private void Snap()
        {
            DropPhoto();
            _photo = Online.TakePhoto(out _placeholder);
            _preview = PhotoCapture.ToTexture(_photo);
        }

        private void DropPhoto()
        {
            _photo = null;
            _placeholder = false;
            if (_preview != null) UnityEngine.Object.Destroy(_preview);
            _preview = null;
        }

        private void ShowPreview()
        {
            if (_previewSlot == null) return;
            var t = _host.Strings;
            _previewSlot.Clear();
            var p = Polaroid.Create(236, _placeholder ? t.T("online.foto.platzhalter_kurz") : "", -2f, "gb-instant-preview");
            p.Root.name = "instant-preview";
            Polaroid.Show(p, _preview, _photo != null ? t.T("online.foto.bereit") : t.T("online.instant.kein_foto"));
            _previewSlot.Add(p.Root);
            SetButtonLabel(_snap, _photo != null ? t.T("online.foto.neu") : t.T("online.instant.knipsen"));
        }

        private static void SetButtonLabel(Button b, string text)
        {
            var l = b.Q<Label>(className: "gb-btn-label") ?? b.Q<Label>();
            if (l != null) l.text = text;
        }

        private void RefreshCount()
        {
            if (_count == null) return;
            var n = (_caption?.value ?? "").Length;
            _count.text = _host.Strings.T("online.instant.zeichen", ("n", n), ("max", NetPhoto.CaptionMax));
            _count.EnableInClassList("gb-compose-count--warn", n >= NetPhoto.CaptionMax - 20);
        }

        private void Post()
        {
            var t = _host.Strings;
            var c = Online.Client;
            if (_posting) return;
            if (!c.IsOnline)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(t.T("online.instant.offline"), "cloud");
                return;
            }
            if (_photo == null) { Snap(); ShowPreview(); }
            _posting = true;
            _post.SetEnabled(false);
            Online.PostInstant(_caption.value, _photo, r =>
            {
                _posting = false;
                _post.SetEnabled(Online.Client.IsOnline);
                if (r.Ok)
                {
                    _host.Ui.Toast(t.T("online.instant.toast.gepostet", ("n", r.Recipients)), "camera");
                    _caption.value = "";
                    Snap(); // ready for the next moment
                    ShowPreview();
                }
                else
                {
                    Sfx.PlayUi("soft_error");
                    _host.Ui.Toast(Online.ErrorText(r.Code), "camera", 4f);
                }
            });
        }

        // ── feed ────────────────────────────────────────────────────────────

        private void Refresh()
        {
            if (_feed == null) return;
            var t = _host.Strings;
            var c = Online.Client;
            _sub.text = c.IsOnline
                ? t.T(c.Feed.Count == 1 ? "online.instant.stand_eins" : "online.instant.stand", ("n", c.Feed.Count), ("neu", c.InstantUnseen))
                : Online.StatusText();
            _statusDot.EnableInClassList("gb-status-dot--on", c.IsOnline);
            _statusDot.EnableInClassList("gb-status-dot--wait", c.Status == NetStatus.Connecting);
            _tileBadge.text = c.InstantUnseen > 9 ? "9+" : c.InstantUnseen.ToString();
            _tileBadge.style.display = c.InstantUnseen > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _post.SetEnabled(c.IsOnline && !_posting);
            _offlineNote.style.display = c.IsOnline ? DisplayStyle.None : DisplayStyle.Flex;
            if (c.InstantUnseen > 0) AckIfUnseen();

            var sig = FeedSignature(c);
            if (sig == _feedSig) return;
            _feedSig = sig;
            _feed.Clear();
            if (c.Feed.Count == 0)
            {
                var empty = Ux.Col("gb-on-empty", "gb-instant-empty");
                var art = Ux.El("gb-on-empty-art");
                art.Add(Ux.Image("gooby_sitting", 140, 140));
                empty.Add(art);
                empty.Add(Ux.Text(c.IsOnline ? t.T("online.instant.leer") : Online.StatusText(), "gb-on-empty-title"));
                if (c.IsOnline && c.Friends.Count == 0) empty.Add(Ux.Text(t.T("online.err.no_friends"), "gb-on-empty-hint"));
                _feed.Add(empty);
                return;
            }
            var i = 0;
            foreach (var p in c.Feed) _feed.Add(PostCard(p, i++));
        }

        private string FeedSignature(SocialClient c)
        {
            var sb = new StringBuilder();
            sb.Append(c.IsOnline ? '1' : '0').Append(c.Friends.Count > 0 ? '1' : '0');
            foreach (var p in c.Feed) sb.Append('|').Append(p.Id).Append(':').Append(p.Likes).Append(p.Liked ? 'L' : '-');
            return sb.ToString();
        }

        private VisualElement PostCard(InstantPost p, int index)
        {
            var t = _host.Strings;
            var card = Ux.Col("gb-instant-card");
            card.name = "instant-card-" + p.Id;
            card.EnableInClassList("gb-instant-card--mine", p.Mine);
            var shot = Polaroid.Create(232, p.Caption, index % 2 == 0 ? -2f : 1.8f, "gb-instant-polaroid");
            Polaroid.Load(_host, shot, true, p.PhotoId);
            card.Add(shot.Root);

            var who = Ux.Row("gb-instant-who");
            who.Add(Ux.Portrait(34));
            var wc = Ux.Col("gb-instant-who-text");
            var name = p.Mine ? t.T("online.instant.du") : (p.FromName.Length > 0 ? p.FromName : p.From);
            wc.Add(Ux.Text(name, "gb-instant-author", "gb-nowrap"));
            wc.Add(Ux.Text((p.FromGooby.Length > 0 ? p.FromGooby + " · " : "") + When(p.At), "gb-instant-meta", "gb-nowrap"));
            who.Add(wc);
            card.Add(who);

            var likes = Ux.Row("gb-instant-likes");
            var pill = Ux.Row("gb-instant-carrots");
            pill.Add(Ux.Icon("carrot"));
            var n = Ux.Text(p.Likes.ToString(), "gb-instant-carrot-n");
            n.name = "instant-likes-" + p.Id;
            pill.Add(n);
            likes.Add(pill);
            if (p.Mine)
            {
                likes.Add(Ux.Text(t.T("online.instant.dein_post"), "gb-instant-mine"));
            }
            else
            {
                var id = p.Id;
                var like = Ux.IconTextBtn("carrot", p.Liked ? t.T("online.instant.moehre_da") : t.T("online.instant.moehre"), () => Like(id),
                    p.Liked ? "gb-btn-secondary" : "gb-btn-primary", "gb-btn-sm", "gb-instant-like");
                like.name = "instant-like-" + p.Id;
                like.EnableInClassList("gb-instant-like--done", p.Liked);
                like.SetEnabled(!p.Liked && Online.Client.IsOnline);
                likes.Add(like);
            }
            card.Add(likes);
            return card;
        }

        private void Like(string id)
        {
            var c = Online.Client;
            var p = c.FindPost(id);
            if (p == null || p.Liked || p.Mine) return;
            Sfx.PlayUi("soft_collect");
            Haptics.Tap();
            c.LikeInstant(id, r =>
            {
                Debug.Log("[instant] like " + id + " → " + r);
                if (!r.Ok) _host.Ui.Toast(Online.ErrorText(r.Code), "carrot");
            });
        }

        private string When(long ms)
        {
            if (ms <= 0) return "";
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            return dt.Date == DateTimeOffset.Now.Date ? _host.Strings.T("online.zeit.heute", ("uhr", dt.ToString("HH:mm"))) : dt.ToString("dd.MM. HH:mm");
        }
    }
}
