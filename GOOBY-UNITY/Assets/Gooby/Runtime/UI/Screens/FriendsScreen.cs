using System;
using System.Collections.Generic;
using Gooby.Core.Net;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Freunde (friend_list_ui.gd in the board-02/06/07 look): a cream window with white cards — left the own friend
    /// code as a green "ticket" to share, "Freund hinzufügen" by code or name, open requests (Annehmen/Ablehnen);
    /// right the friend list with online dot, Gooby tag, what their Gooby is up to, and compact pill actions Brief,
    /// Besuchen, Münzen schenken, Entfernen. Offline it says why and leads to the server setting.
    /// </summary>
    public sealed class FriendsScreen : UiScreen
    {
        public const int GiftAmount = 10;

        /// <summary>
        /// Below this window width (1380 on the 1600×740 reference, ~1040 on 4:3 tablets) the row actions collapse to
        /// round icon buttons and the side column narrows. Measured on the window, which the class does not resize.
        /// </summary>
        private const float CompactBelow = 1180f;

        private static readonly Color TileRosa = new Color(1f, 0.85f, 0.87f);
        private static readonly Color TileMint = new Color(0.87f, 0.95f, 0.89f);

        private readonly GameHost _host;
        private OnlineService Online => _host.Online;
        private Label _status, _code, _addInfo, _listTitle, _reqCount, _mailBadge, _instantBadge;
        private TextField _addField;
        private VisualElement _panel, _statusDot, _requests, _reqHead, _list, _offlineCard;
        private readonly HashSet<string> _confirmRemove = new HashSet<string>();

        public FriendsScreen(GameHost host)
        {
            _host = host;
            Id = "friends";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-online");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);

            _panel = Ux.El("gb-online-panel");
            Ux.Abs(_panel, 110, 32, 110, 32);
            _panel.RegisterCallback<GeometryChangedEvent>(e => _panel.EnableInClassList("gb-friends--compact", e.newRect.width > 0 && e.newRect.width < CompactBelow));
            root.Add(_panel);

            // ── header: tile, title + connection pill, InstantGooby (unseen badge), Briefkasten (unread badge), close ──
            var head = Ux.Row("gb-on-head");
            head.Add(Ux.IconTile("friends", TileRosa));
            var hcol = Ux.Col("gb-on-head-text");
            hcol.Add(Ux.Text(t.T("net.friends.title"), "gb-on-title"));
            var statusPill = Ux.Row("gb-on-status");
            _statusDot = Ux.El("gb-status-dot");
            statusPill.Add(_statusDot);
            _status = Ux.Text("", "gb-nowrap");
            statusPill.Add(_status);
            hcol.Add(statusPill);
            head.Add(hcol);
            var instant = Ux.IconTextBtn("camera", t.T("online.instant.oeffnen"), () => { Sfx.PlayUi("soft_open"); _host.Ui.Push(new InstantScreen(_host)); }, "gb-btn-secondary", "gb-btn-sm", "gb-instant-open");
            instant.name = "instant-open";
            _instantBadge = Ux.Badge("");
            _instantBadge.name = "instant-badge";
            _instantBadge.style.display = DisplayStyle.None;
            instant.Add(_instantBadge);
            head.Add(instant);
            var mail = Ux.IconTextBtn("mail", t.T("mail.titel"), () => _host.Ui.Push(new MailboxScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
            _mailBadge = Ux.Badge("");
            _mailBadge.style.display = DisplayStyle.None;
            mail.Add(_mailBadge);
            head.Add(mail);
            // "Schach üben": offline against the Gooby bot, works without a server too
            var practice = Ux.IconTextBtn("grid", t.T("chess.practice"), () => { Sfx.PlayUi("soft_open"); _host.Ui.Push(ChessScreen.PracticeGame(_host)); }, "gb-btn-secondary", "gb-btn-sm");
            practice.name = ChessScreen.Practice;
            practice.tooltip = t.T("chess.practice_hint");
            head.Add(practice);
            head.Add(Ux.IconBtn("close", Close, "gb-btn-icon-sm"));
            _panel.Add(head);

            var body = Ux.Row("gb-on-body");
            _panel.Add(body);

            // ── left column: own code, add, requests ──
            var left = Ux.Col("gb-on-side");
            body.Add(left);

            var codeCard = Ux.El("gb-on-card", "gb-code-card");
            codeCard.Add(Ux.Text(t.T("net.friends.my_code"), "gb-on-kicker"));
            // the whole code must stay readable in the 372-px column ("GOOBY-BW2A" was cut to "GOOBY-BW…" next to a
            // labelled button): the smaller ticket face and an icon-only copy button
            var ticket = Ux.Row("gb-code-ticket", "gb-code-ticket--sm");
            _code = Ux.Text("—", "gb-code-value", "gb-nowrap");
            _code.name = "friend-code";
            ticket.Add(_code);
            var copy = Ux.IconBtn("copy", () =>
            {
                if (Online.Client.FriendCode.Length == 0) return;
                GUIUtility.systemCopyBuffer = Online.Client.FriendCode;
                Sfx.PlayUi("soft_confirm");
                _host.Ui.Toast(t.T("net.friends.copied"), "copy", 1.6f);
            }, "gb-btn-teal");
            copy.name = "friend-code-copy";
            copy.tooltip = t.T("net.friends.copy");
            ticket.Add(copy);
            codeCard.Add(ticket);
            codeCard.Add(Ux.Text(t.T("online.settings.code_hint"), "gb-on-hint"));
            left.Add(codeCard);

            var add = Ux.El("gb-on-card", "gb-on-gap");
            var addHead = Ux.Row("gb-on-card-head");
            addHead.Add(Ux.IconTile("plus", TileMint));
            addHead.Add(Ux.Text(t.T("net.friends.add_title"), "gb-on-card-title"));
            add.Add(addHead);
            _addField = new TextField { maxLength = 24 };
            _addField.name = "friend-add";
            _addField.AddToClassList("gb-textfield");
            _addField.textEdition.placeholder = t.T("net.friends.add_placeholder");
            _addField.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SendRequest(); });
            add.Add(_addField);
            var addBtn = Ux.IconTextBtn("plus", t.T("net.friends.add_button"), SendRequest, "gb-btn-primary", "gb-btn-sm", "gb-on-wide");
            addBtn.name = "friend-add-send";
            add.Add(addBtn);
            _addInfo = Ux.Text("", "gb-on-hint");
            _addInfo.style.display = DisplayStyle.None;
            add.Add(_addInfo);
            left.Add(add);

            // ── right column: offline card, then one scroller with the open requests on top of the friends list (in
            //    the left column the Annehmen/Ablehnen buttons fell below the sheet on a 16:9-ish landscape screen) ──
            var right = Ux.Col("gb-on-main");
            _offlineCard = Ux.El("gb-on-card", "gb-on-offline");
            right.Add(_offlineCard);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-on-scroll");
            _reqHead = Ux.Row("gb-on-section", "gb-on-section--top");
            _reqHead.Add(Ux.Text(t.T("net.friends.requests_title"), "gb-nowrap"));
            _reqCount = Ux.Text("", "gb-on-count");
            _reqHead.Add(_reqCount);
            scroll.Add(_reqHead);
            _requests = Ux.Col("gb-on-requests");
            _requests.name = "friend-requests";
            scroll.Add(_requests);
            var listHead = Ux.Row("gb-on-section", "gb-on-section--top");
            _listTitle = Ux.Text("", "gb-nowrap");
            listHead.Add(_listTitle);
            scroll.Add(listHead);
            _list = Ux.Col();
            _list.name = "friend-list";
            scroll.Add(_list);
            right.Add(scroll);
            body.Add(right);

            Refresh();
        }

        public override void OnShown()
        {
            Online.Changed -= Refresh;
            Online.Changed += Refresh;
            if (Online.Client.IsOnline) Online.Client.RefreshFriends();
            Refresh();
        }

        public override void OnHidden() => Online.Changed -= Refresh;

        private void Close()
        {
            Sfx.PlayUi("soft_back");
            _host.Ui.Pop();
        }

        private void SendRequest()
        {
            var t = _host.Strings;
            var text = (_addField.value ?? "").Trim();
            if (text.Length == 0) return;
            if (!Online.Client.IsOnline) { SetAddInfo(t.T("net.friends.offline_hint"), ok: false); return; }
            SetAddInfo("…", ok: null);
            Online.Client.AddFriend(text, r =>
            {
                if (r.Ok)
                {
                    _addField.value = "";
                    SetAddInfo(NetJsonAuto(r) ? t.T("online.freunde.auto_ok") : t.T("net.friends.add_sent"), ok: true);
                    Sfx.PlayUi("soft_confirm");
                }
                else
                {
                    SetAddInfo(Online.ErrorText(r.Code), ok: false);
                    Sfx.PlayUi("soft_error");
                }
            });
        }

        /// <summary>The line under "Anfrage senden": green when it worked, strawberry for errors, hidden when empty.</summary>
        private void SetAddInfo(string text, bool? ok)
        {
            _addInfo.text = text ?? "";
            _addInfo.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            _addInfo.EnableInClassList("gb-on-hint--ok", ok == true);
            _addInfo.EnableInClassList("gb-on-hint--err", ok == false);
        }

        private static bool NetJsonAuto(NetReply r) => r.Data["autoAccepted"] != null && r.Data["autoAccepted"].Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)r.Data["autoAccepted"];

        private void Refresh()
        {
            if (_list == null) return;
            var t = _host.Strings;
            var c = Online.Client;
            _status.text = Online.StatusText();
            _statusDot.EnableInClassList("gb-status-dot--on", c.IsOnline);
            _statusDot.EnableInClassList("gb-status-dot--wait", c.Status == NetStatus.Connecting);
            _statusDot.EnableInClassList("gb-status-dot--off", c.Status == NetStatus.Offline && c.LastErrorCode.Length > 0);
            _code.text = c.FriendCode.Length > 0 ? c.FriendCode : "—";
            _mailBadge.text = c.MailUnread > 9 ? "9+" : c.MailUnread.ToString();
            _mailBadge.style.display = c.MailUnread > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _instantBadge.text = c.InstantUnseen > 9 ? "9+" : c.InstantUnseen.ToString();
            _instantBadge.style.display = c.InstantUnseen > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _offlineCard.Clear();
            _offlineCard.style.display = c.IsOnline ? DisplayStyle.None : DisplayStyle.Flex;
            if (!c.IsOnline)
            {
                _offlineCard.Add(Ux.IconTile(c.Status == NetStatus.Connecting ? "globe" : "cloud"));
                var col = Ux.Col("gb-grow");
                col.style.flexShrink = 1;
                col.Add(Ux.Text(c.Status == NetStatus.Connecting ? Online.StatusText() : t.T("online.freunde.offline", ("status", Online.StatusText().TrimEnd('.', ' '))), "gb-on-offline-text"));
                var row = Ux.Row("gb-on-offline-btns");
                row.Add(Ux.IconTextBtn("settings", t.T("online.freunde.zum_server"), () => _host.Ui.Push(new SettingsScreen(_host, "konto")), "gb-btn-secondary", "gb-btn-sm"));
                if (NetAddress.Parse(Online.ServerAddress).Valid && c.Status == NetStatus.Offline)
                    row.Add(Ux.IconTextBtn("redo", t.T("online.settings.verbinden"), () => Online.Apply(connect: true), "gb-btn-primary", "gb-btn-sm"));
                col.Add(row);
                _offlineCard.Add(col);
            }

            _requests.Clear();
            _reqHead.style.display = c.Requests.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _reqCount.text = c.Requests.Count.ToString();
            foreach (var r in c.Requests) _requests.Add(RequestRow(r));

            _list.Clear();
            var online = 0;
            foreach (var f in c.Friends) if (f.Online) online++;
            _listTitle.text = t.T("online.freunde.liste", ("n", c.Friends.Count), ("online", online));
            if (c.Friends.Count == 0)
            {
                var empty = Ux.Col("gb-on-empty");
                var art = Ux.El("gb-on-empty-art");
                art.Add(Ux.Image("gooby_sitting", 150, 150));
                empty.Add(art);
                empty.Add(Ux.Text(t.T("net.friends.empty"), "gb-on-empty-title"));
                empty.Add(Ux.Text(t.T("online.freunde.leer_hint"), "gb-on-empty-hint"));
                _list.Add(empty);
                return;
            }
            var sorted = new List<FriendInfo>(c.Friends);
            sorted.Sort((a, b) => a.Online != b.Online ? (a.Online ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var f in sorted) _list.Add(FriendRow(f));
        }

        private VisualElement RequestRow(FriendRequestInfo r)
        {
            var t = _host.Strings;
            var card = Ux.El("gb-request");
            var top = Ux.Row("gb-request-top");
            top.Add(Ux.Portrait(42));
            var col = Ux.Col("gb-friend-info");
            col.Add(Ux.Text(t.T("net.friends.request_from", ("name", r.Name), ("gooby", r.GoobyName)), "gb-friend-name", "gb-nowrap"));
            col.Add(Ux.Text(r.FromCode, "gb-friend-line", "gb-nowrap"));
            top.Add(col);
            card.Add(top);
            var btns = Ux.Row("gb-request-btns");
            btns.Add(Ux.IconTextBtn("close", t.T("net.friends.decline"), () => Online.Client.DeclineFriend(r.FromCode, _ => Online.Client.RefreshFriends()), "gb-btn-secondary", "gb-btn-sm"));
            var yes = Ux.IconTextBtn("check", t.T("net.friends.accept"), () =>
            {
                Sfx.PlayUi("soft_confirm");
                Online.Client.AcceptFriend(r.FromCode, res => { if (!res.Ok) _host.Ui.Toast(Online.ErrorText(res.Code), "friends"); });
            }, "gb-btn-confirm", "gb-btn-sm");
            yes.name = "accept-" + r.FromCode;
            btns.Add(yes);
            card.Add(btns);
            return card;
        }

        private VisualElement FriendRow(FriendInfo f)
        {
            var t = _host.Strings;
            var row = Ux.El("gb-friend-row");
            row.name = "friend-" + f.Code;
            row.EnableInClassList("gb-friend-row--offline", !f.Online);
            var main = Ux.Row("gb-friend-main");
            row.Add(main);

            var av = Ux.Portrait(56);
            if (!f.Online) av.AddToClassList("gb-friend-av--off");
            var dot = Ux.El("gb-online-dot");
            dot.EnableInClassList("gb-online-dot--on", f.Online);
            av.Add(dot);
            main.Add(av);

            // name · Gooby tag · coins, then what the Gooby does (both lines ellipsize instead of pushing the buttons)
            var info = Ux.Col("gb-friend-info");
            var nameRow = Ux.Row("gb-friend-name-row");
            nameRow.Add(Ux.Text(f.Name, "gb-friend-name", "gb-nowrap"));
            if (f.GoobyName.Length > 0) nameRow.Add(Ux.Text(f.GoobyName, "gb-friend-gooby", "gb-nowrap"));
            if (f.CoinsUpdatedAt > 0)
            {
                var coins = Ux.Row("gb-friend-coins");
                coins.Add(Ux.Icon("coin"));
                coins.Add(Ux.Text(Ux.FormatNumber(f.Coins)));
                nameRow.Add(coins);
            }
            info.Add(nameRow);
            string line;
            if (f.Online) line = f.ActivityLabel.Length > 0 ? f.GoobyName + " — " + f.ActivityLabel : t.T("net.friends.online");
            else line = f.LastSeenAt > 0 ? t.T("online.freunde.zuletzt", ("zeit", When(f.LastSeenAt))) : t.T("net.friends.offline");
            var l = Ux.Text(line, "gb-friend-line", "gb-nowrap");
            l.EnableInClassList("gb-friend-line--on", f.Online);
            info.Add(l);
            main.Add(info);

            var acts = Ux.Row("gb-friend-acts");
            var code = f.Code;
            acts.Add(RowBtn("mail", t.T("online.freunde.brief"), true, false, () => _host.Ui.Push(new MailboxScreen(_host, code)), "brief-" + code));
            acts.Add(RowBtn("house", t.T("online.freunde.besuchen"), f.Online, true, () =>
            {
                Online.RequestVisit(f, (ok, c) => { if (!ok) _host.Ui.Toast(Online.ErrorText(c), "house"); });
            }, "visit-" + code));
            if (f.Online)
            {
                // Schiffe versenken / Schach: BOARD_INVITE; "Wartet …" while the invite is open (30 s on the server),
                // one open invite per friend at a time (the server keeps one per pair)
                var pending = Online.Board.IsInvitePending(code);
                var pendingChess = Online.Board.IsInvitePending(code, BoardClient.GameChess);
                var play = RowBtn("gamepad", pending && !pendingChess ? t.T("online.board.wartet") : t.T("online.board.button"), Online.Client.IsOnline && !pending, false, () =>
                {
                    Online.InviteToBoard(f, (ok, c) => { if (!ok) _host.Ui.Toast(Online.BoardErrorText(c), "gamepad"); });
                }, "board-" + code);
                play.tooltip = t.T("online.board.button_hint", ("name", f.Name));
                acts.Add(play);
                var chess = RowBtn("grid", pendingChess ? t.T("online.board.wartet") : t.T("chess.button"), Online.Client.IsOnline && !pending, false, () =>
                {
                    Online.InviteToBoard(f, BoardClient.GameChess, (ok, c) => { if (!ok) _host.Ui.Toast(Online.BoardErrorText(c), "grid"); });
                }, "chess-" + code);
                chess.tooltip = t.T("chess.button_hint", ("name", f.Name));
                acts.Add(chess);
                // Ranch-Treffen: pick Wettrennen / Parcours / Fangen there and invite (2–4 riders)
                var ride = RowBtn("horseshoe", t.T("nl.rmp.reiten"), Online.Client.IsOnline, false, () => { Sfx.PlayUi("soft_open"); Online.OpenRanchMp(); }, "ranch-" + code);
                ride.tooltip = t.T("nl.rmp.reiten_hint", ("name", f.Name));
                acts.Add(ride);
            }
            acts.Add(RowBtn("gift", t.T("online.freunde.schenken", ("n", GiftAmount)), Online.Client.IsOnline, false, () =>
            {
                Online.GiftCoins(code, GiftAmount, (ok, c) =>
                {
                    if (ok) { Sfx.PlayUi("soft_coins"); _host.Ui.Toast(t.T("online.freunde.geschenkt", ("n", GiftAmount), ("name", f.Name)), "coin"); }
                    else _host.Ui.Toast(Online.ErrorText(c), "coin");
                });
            }, "pal-" + code));
            var confirm = _confirmRemove.Contains(code);
            var rm = Ux.IconBtn("trash", () =>
            {
                if (!_confirmRemove.Contains(code)) { _confirmRemove.Add(code); Refresh(); return; }
                _confirmRemove.Remove(code);
                Online.Client.RemoveFriend(code, r => { if (!r.Ok) _host.Ui.Toast(Online.ErrorText(r.Code), "friends"); });
            }, "gb-btn-icon-sm", "gb-friend-rm");
            if (confirm) rm.AddToClassList("gb-btn-danger");
            rm.tooltip = confirm ? t.T("online.freunde.entfernen_frage") : t.T("online.freunde.entfernen");
            acts.Add(rm);
            // the actions get their own line under name and activity: in one line six pills squeezed the name to "…"
            row.Add(acts);

            // "tap again" lives on its own strip under the row instead of squeezing the name column
            if (confirm)
            {
                var strip = Ux.Row("gb-friend-confirm");
                strip.Add(Ux.Text(t.T("online.freunde.entfernen_frage")));
                row.Add(strip);
            }
            return row;
        }

        /// <summary>Compact pill action; the highlighted one (Besuchen) turns teal only while it can be used.</summary>
        private static Button RowBtn(string icon, string label, bool enabled, bool accent, Action onClick, string name)
        {
            var b = Ux.IconTextBtn(icon, label, onClick, accent && enabled ? "gb-btn-teal" : "gb-btn-secondary", "gb-btn-sm", "gb-friend-act");
            b.name = name;
            b.tooltip = label;
            b.SetEnabled(enabled);
            return b;
        }

        private string When(long ms)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            var now = DateTimeOffset.Now;
            if (dt.Date == now.Date) return _host.Strings.T("online.zeit.heute", ("uhr", dt.ToString("HH:mm")));
            return dt.ToString("dd.MM. HH:mm");
        }
    }
}
