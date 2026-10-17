using System;
using System.Collections.Generic;
using Gooby.Core.Net;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Briefkasten (mail_sheet.gd): the letters from friends as envelope cards (unread first) on the left, the opened
    /// letter on warm cream paper with a stamp corner on the right, its gift ("Annehmen" only after the server's claim
    /// OK), Antworten and Wegwerfen; "Brief schreiben" swaps the right side to the writing desk — recipient chips, the
    /// text (500 characters), an optional gift from the fridge, an optional photo ("Foto anhängen" snaps the game view
    /// behind the sheet, 320×240 JPEG; a letter with a photo travels over REST), 5 ᴳ postage. Offline letters wait in the
    /// outbox and go out on the next connect — without their photo. Received photos sit as a polaroid on the paper.
    /// </summary>
    public sealed class MailboxScreen : UiScreen
    {
        private static readonly Color TileSonne = new Color(1f, 0.93f, 0.7f);
        private static readonly Color TileRead = new Color(0.96f, 0.93f, 0.89f);
        private static readonly Color TileUnread = new Color(1f, 0.87f, 0.9f);
        private static readonly Color TileGift = new Color(1f, 0.85f, 0.85f);

        /// <summary>Gift chips / gift card: the fridge food that has its own icon, everything else gets the parcel.</summary>
        private static readonly Dictionary<string, string> FoodIcons = new Dictionary<string, string>
        {
            { "carrot", "food_carrot" }, { "weltraumMoehre", "food_carrot" }, { "apple", "food_apple" }, { "banana", "food_banana" },
            { "tomato", "food_tomato" }, { "tomate", "food_tomato" }, { "strawberry", "food_strawberry" }, { "watermelon", "food_watermelon" },
            { "melone", "food_watermelon" }, { "cheese", "food_cheese" }, { "burger", "food_burger" }, { "pizza", "food_pizza" },
            { "cake", "food_cake" }, { "cupcake", "food_cupcake" }, { "cupcakePink", "food_cupcake" }, { "donut-sprinkles", "food_donut" },
            { "ice-cream", "food_icecream" }, { "softServe", "food_icecream" }, { "sundae", "food_icecream" }, { "pilz", "food_mushroom" },
        };

        private readonly GameHost _host;
        private OnlineService Online => _host.Online;
        private string _selected;
        private bool _composing;
        private string _to;
        private (string typ, string id) _gift;
        private string _draft = "";
        private byte[] _photo;
        private bool _photoPlaceholder;
        private Texture2D _photoPreview;
        private Label _sub, _composeInfo, _tileBadge;
        private VisualElement _list, _pane, _statusDot, _composeOffline;

        public MailboxScreen(GameHost host, string composeTo = null)
        {
            _host = host;
            Id = "mailbox";
            if (!string.IsNullOrEmpty(composeTo)) { _composing = true; _to = composeTo; }
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-online");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);

            var panel = Ux.El("gb-online-panel");
            Ux.Abs(panel, 110, 32, 110, 32);
            root.Add(panel);

            // ── header: envelope tile with the unread count, title + status pill, actions ──
            var head = Ux.Row("gb-on-head");
            var tile = Ux.IconTile("mail", TileSonne);
            _tileBadge = Ux.Badge("");
            _tileBadge.style.display = DisplayStyle.None;
            tile.Add(_tileBadge);
            head.Add(tile);
            var hcol = Ux.Col("gb-on-head-text");
            hcol.Add(Ux.Text(t.T("mail.titel"), "gb-on-title"));
            var statusPill = Ux.Row("gb-on-status");
            _statusDot = Ux.El("gb-status-dot");
            statusPill.Add(_statusDot);
            _sub = Ux.Text("", "gb-nowrap");
            statusPill.Add(_sub);
            hcol.Add(statusPill);
            head.Add(hcol);
            var write = Ux.IconTextBtn("pencil", t.T("mail.schreiben"), () => { _composing = true; _selected = null; Sfx.PlayUi("soft_open"); RefreshList(); RefreshPane(); }, "gb-btn-primary", "gb-btn-sm");
            write.name = "mail-compose";
            head.Add(write);
            head.Add(Ux.IconTextBtn("redo", t.T("mail.aktualisieren"), () => Online.Client.RefreshMail(), "gb-btn-secondary", "gb-btn-sm"));
            head.Add(Ux.IconBtn("close", Close, "gb-btn-icon-sm"));
            panel.Add(head);

            var body = Ux.Row("gb-on-body");
            panel.Add(body);

            // the list comes first in the tree: QA's "letter-*" picks the first envelope, not the opened paper
            var listScroll = new ScrollView(ScrollViewMode.Vertical);
            listScroll.AddToClassList("gb-mail-list");
            _list = Ux.Col();
            _list.name = "mail-list";
            listScroll.Add(_list);
            body.Add(listScroll);

            _pane = Ux.Col("gb-grow");
            body.Add(_pane);
            RefreshList();
            RefreshPane();
        }

        public override void OnShown()
        {
            Online.Changed -= Refresh;
            Online.Changed += Refresh;
            if (Online.Client.IsOnline) Online.Client.RefreshMail();
            Refresh();
        }

        public override void OnHidden()
        {
            Online.Changed -= Refresh;
            if (!_host.Ui.IsOpen(this)) DropPhoto();
        }

        private void DropPhoto()
        {
            _photo = null;
            _photoPlaceholder = false;
            if (_photoPreview != null) UnityEngine.Object.Destroy(_photoPreview);
            _photoPreview = null;
        }

        private void Close()
        {
            Sfx.PlayUi("soft_back");
            _host.Ui.Pop();
        }

        private void Refresh()
        {
            if (_list == null) return;
            RefreshList();
            if (!_composing) RefreshPane(); // the writing desk keeps its text field (no rebuild while typing)
            else RefreshComposeInfo();
        }

        private void RefreshList()
        {
            var t = _host.Strings;
            var c = Online.Client;
            var sub = c.IsOnline ? t.T(c.Mail.Count == 1 ? "online.brief.stand_eins" : "online.brief.stand", ("neu", c.MailUnread), ("n", c.Mail.Count)) : t.T("mail.schalter.offline");
            if (c.OutboxCount > 0) sub += "  ·  " + t.T("mail.schalter.outbox", ("n", c.OutboxCount));
            _sub.text = sub;
            _statusDot.EnableInClassList("gb-status-dot--on", c.IsOnline);
            _statusDot.EnableInClassList("gb-status-dot--wait", c.Status == NetStatus.Connecting);
            _tileBadge.text = c.MailUnread > 9 ? "9+" : c.MailUnread.ToString();
            _tileBadge.style.display = c.MailUnread > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _list.Clear();
            if (c.Mail.Count == 0)
            {
                var empty = Ux.Col("gb-on-empty");
                var art = Ux.El("gb-on-empty-art", "gb-on-empty-art--sm");
                art.Add(Ux.Image("gooby_sitting", 116, 116));
                empty.Add(art);
                empty.Add(Ux.Text(c.IsOnline ? t.T("mail.leer") : Online.StatusText(), "gb-on-empty-title"));
                _list.Add(empty);
                return;
            }
            foreach (var m in c.Mail) _list.Add(LetterRow(m));
        }

        private VisualElement LetterRow(MailInfo m)
        {
            var row = Ux.El("gb-letter-row");
            row.name = "letter-" + m.Id;
            row.EnableInClassList("gb-letter-row--unread", !m.Read);
            row.EnableInClassList("gb-letter-row--selected", m.Id == _selected && !_composing);
            row.Add(Ux.IconTile(m.HasItem && !m.Claimed ? "gift" : m.PhotoId.Length > 0 ? "camera" : "mail", m.Read ? TileRead : TileUnread));
            var col = Ux.Col("gb-letter-body");
            var top = Ux.Row("gb-letter-top");
            top.Add(Ux.Text(m.FromName.Length > 0 ? m.FromName : m.From, "gb-letter-from", "gb-nowrap"));
            if (!m.Read) top.Add(Ux.Text(_host.Strings.T("mail.ungelesen"), "gb-letter-new"));
            top.Add(Ux.Text(When(m.At), "gb-letter-time"));
            col.Add(top);
            var snippet = m.Text.Replace('\n', ' ');
            if (snippet.Trim().Length == 0 && m.PhotoId.Length > 0) snippet = _host.Strings.T("online.foto.nur_foto");
            if (snippet.Length > 64) snippet = snippet.Substring(0, 62) + "…";
            col.Add(Ux.Text(snippet, "gb-letter-snippet", "gb-nowrap"));
            row.Add(col);
            row.RegisterCallback<ClickEvent>(_ => Open(m.Id));
            return row;
        }

        private void Open(string id)
        {
            _composing = false;
            _selected = id;
            Sfx.PlayUi("soft_open");
            var m = Online.Client.FindMail(id);
            if (m != null && !m.Read && Online.Client.IsOnline) Online.Client.AckMail(id);
            RefreshList();
            RefreshPane();
        }

        /// <summary>Stamp in the top-right corner of the paper plus a round "GOOBY POST" postmark over its edge.</summary>
        private VisualElement[] StampCorner(string icon)
        {
            var stamp = Ux.El("gb-letter-stamp");
            stamp.pickingMode = PickingMode.Ignore;
            var inner = Ux.El("gb-letter-stamp-inner");
            inner.pickingMode = PickingMode.Ignore;
            inner.Add(Ux.Icon(icon));
            stamp.Add(inner);
            var mark = Ux.El("gb-letter-postmark");
            mark.pickingMode = PickingMode.Ignore;
            var ml = Ux.Text(_host.Strings.T("online.brief.stempel"));
            ml.pickingMode = PickingMode.Ignore;
            mark.Add(ml);
            return new[] { stamp, mark };
        }

        private void AddStamp(VisualElement paper, string icon)
        {
            foreach (var e in StampCorner(icon)) paper.Add(e);
        }

        private void RefreshPane()
        {
            _pane.Clear();
            if (_composing) { BuildCompose(); return; }
            var t = _host.Strings;
            var m = _selected != null ? Online.Client.FindMail(_selected) : null;
            if (m == null)
            {
                var hint = Ux.El("gb-letter-paper");
                AddStamp(hint, "heart");
                var box = Ux.Col("gb-letter-hint");
                var art = Ux.El("gb-on-empty-art");
                art.Add(Ux.Image("gooby_sitting", 150, 150));
                box.Add(art);
                box.Add(Ux.Text(t.T("mail.schalter.text"), "gb-on-empty-title"));
                if (Online.Client.Mail.Count > 0) box.Add(Ux.Text(t.T("online.brief.waehlen"), "gb-on-empty-hint"));
                hint.Add(box);
                _pane.Add(hint);
                return;
            }
            var paper = Ux.El("gb-letter-paper");
            paper.name = "letter-open";
            var head = Ux.Row("gb-letter-head");
            head.Add(Ux.Portrait(54));
            var hc = Ux.Col("gb-friend-info");
            hc.Add(Ux.Text(t.T("mail.von", ("name", m.FromName.Length > 0 ? m.FromName : m.From)), "gb-letter-title", "gb-nowrap"));
            hc.Add(Ux.Text((m.FromGooby.Length > 0 ? m.FromGooby + " · " : "") + When(m.At), "gb-letter-meta", "gb-nowrap"));
            head.Add(hc);
            paper.Add(head);
            paper.Add(Ux.El("gb-letter-rule"));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-letter-scroll");
            if (m.PhotoId.Length > 0)
            {
                // the photo lies on the paper like a polaroid tucked into the envelope
                var shot = Polaroid.Create(300, m.FromGooby.Length > 0 ? m.FromGooby : "", -2.5f, "gb-letter-polaroid");
                shot.Root.name = "letter-photo";
                Polaroid.Load(_host, shot, false, m.PhotoId);
                scroll.Add(shot.Root);
            }
            var text = Ux.Text(m.Text, "gb-letter-text");
            text.name = "letter-text";
            scroll.Add(text);
            paper.Add(scroll);
            if (m.HasItem)
            {
                var gift = Ux.El("gb-letter-gift");
                gift.EnableInClassList("gb-letter-gift--claimed", m.Claimed);
                gift.Add(Ux.IconTile(GiftIcon(m.ItemTyp, m.ItemId)));
                gift.Add(Ux.Text(t.T("mail.geschenk", ("name", GiftName(m.ItemTyp, m.ItemId)), ("n", m.ItemMenge)), "gb-letter-gift-name"));
                if (m.Claimed)
                {
                    var done = Ux.Row("gb-letter-claimed");
                    done.Add(Ux.Icon("check"));
                    done.Add(Ux.Text(t.T("mail.angenommen")));
                    gift.Add(done);
                }
                else
                {
                    var claim = Ux.IconTextBtn("gift", t.T("mail.annehmen"), () =>
                    {
                        Online.ClaimGift(m, (ok, code) =>
                        {
                            if (ok) _host.Ui.Toast(t.T("mail.toast.geschenk_da", ("name", GiftName(m.ItemTyp, m.ItemId)), ("n", m.ItemMenge)), "gift");
                            else _host.Ui.Toast(Online.ErrorText(code), "gift");
                            RefreshPane();
                        });
                    }, "gb-btn-primary", "gb-btn-sm");
                    claim.name = "letter-claim";
                    claim.SetEnabled(Online.Client.IsOnline);
                    gift.Add(claim);
                }
                paper.Add(gift);
            }
            var actions = Ux.Row("gb-letter-actions");
            actions.Add(Ux.IconTextBtn("pencil", t.T("online.brief.antworten"), () => { _composing = true; _to = m.From; _selected = null; RefreshList(); RefreshPane(); }, "gb-btn-teal", "gb-btn-sm"));
            var del = Ux.IconTextBtn("trash", t.T("mail.loeschen"), () =>
            {
                Online.Client.DeleteMail(m.Id, r => { if (!r.Ok) _host.Ui.Toast(Online.ErrorText(r.Code), "mail"); });
                _selected = null;
                RefreshPane();
            }, "gb-btn-secondary", "gb-btn-sm");
            del.SetEnabled(Online.Client.IsOnline);
            actions.Add(del);
            paper.Add(actions);
            AddStamp(paper, m.HasItem ? "gift" : "heart");
            _pane.Add(paper);
        }

        // ── writing desk ─────────────────────────────────────────────────────

        private TextField _text;

        private void BuildCompose()
        {
            var t = _host.Strings;
            var c = Online.Client;
            var desk = Ux.El("gb-letter-paper");
            desk.name = "compose";
            var head = Ux.Row("gb-letter-head");
            head.Add(Ux.IconTile("pencil", TileSonne));
            var hc = Ux.Col("gb-friend-info");
            hc.Add(Ux.Text(t.T("mail.schreiben"), "gb-letter-title", "gb-nowrap"));
            hc.Add(Ux.Text(t.T("online.brief.porto_hint", ("porto", OnlineService.Porto), ("max", SocialClient.TextMax)), "gb-letter-meta", "gb-nowrap"));
            head.Add(hc);
            desk.Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-letter-scroll");
            desk.Add(scroll);

            scroll.Add(Ux.Text(t.T("mail.compose.an").ToUpperInvariant(), "gb-compose-label", "gb-compose-label--first"));
            var chips = Ux.Row("gb-compose-chips");
            if (c.Friends.Count == 0) chips.Add(Ux.Text(t.T("mail.compose.kein_freund"), "gb-on-offline-text"));
            if (_to == null && c.Friends.Count > 0) _to = c.Friends[0].Code;
            foreach (var f in c.Friends)
            {
                var code = f.Code;
                var chip = Chip(() => { _to = code; _draft = _text?.value ?? _draft; RefreshPane(); }, "gb-on-chip--to", code == _to);
                chip.name = "to-" + code;
                var dot = Ux.El("gb-status-dot");
                dot.EnableInClassList("gb-status-dot--on", f.Online);
                chip.Add(dot);
                chip.Add(ChipLabel(f.Name));
                chips.Add(chip);
            }
            scroll.Add(chips);

            _text = new TextField { multiline = true, maxLength = SocialClient.TextMax };
            _text.name = "compose-text";
            _text.AddToClassList("gb-textfield");
            _text.AddToClassList("gb-compose-text");
            _text.style.height = 104;
            _text.style.marginTop = 6;
            _text.textEdition.placeholder = t.T("mail.compose.text_hint");
            _text.value = _draft;
            _text.RegisterValueChangedCallback(e => { _draft = e.newValue ?? ""; RefreshComposeInfo(); });
            scroll.Add(_text);
            var meta = Ux.Row("gb-compose-meta");
            _composeOffline = Ux.Row("gb-compose-offline");
            _composeOffline.Add(Ux.Icon("cloud"));
            _composeOffline.Add(Ux.Text(t.T("mail.schalter.offline")));
            meta.Add(_composeOffline);
            _composeInfo = Ux.Text("", "gb-compose-count");
            meta.Add(_composeInfo);
            scroll.Add(meta);

            scroll.Add(Ux.Text(t.T("online.foto.label").ToUpperInvariant(), "gb-compose-label"));
            scroll.Add(PhotoRow());

            scroll.Add(Ux.Text(t.T("mail.compose.geschenk").ToUpperInvariant(), "gb-compose-label"));
            var gifts = Ux.Row("gb-compose-chips");
            gifts.Add(GiftChip(null, null, t.T("mail.compose.kein_geschenk")));
            var choices = Online.GiftChoices();
            if (_gift.id != null && !choices.Exists(g => g.typ == _gift.typ && g.id == _gift.id)) _gift = (null, null);
            var shown = 0;
            foreach (var (typ, id, count) in choices)
            {
                if (shown++ >= 10) break;
                gifts.Add(GiftChip(typ, id, GiftName(typ, id) + " ×" + count));
            }
            scroll.Add(gifts);

            var row = Ux.Row("gb-letter-actions");
            var send = Ux.IconTextBtn("mail", t.T("mail.compose.senden", ("porto", OnlineService.Porto)), Send, "gb-btn-primary");
            send.name = "compose-send";
            send.SetEnabled(c.Friends.Count > 0);
            row.Add(send);
            row.Add(Ux.IconTextBtn("back", t.T("mail.zurueck").TrimStart('‹', ' '), () => { _composing = false; RefreshPane(); }, "gb-btn-secondary", "gb-btn-sm"));
            desk.Add(row);
            AddStamp(desk, _gift.id != null ? "gift" : _photo != null ? "camera" : "heart");
            _pane.Add(desk);
            RefreshComposeInfo();
        }

        /// <summary>"Foto anhängen": a toggle chip that snaps the game view; attached, a small polaroid + "Neu knipsen".</summary>
        private VisualElement PhotoRow()
        {
            var t = _host.Strings;
            var row = Ux.Row("gb-compose-photo");
            var attached = _photo != null;
            var chip = Chip(TogglePhoto, "gb-on-chip--photo", attached);
            chip.name = "photo-attach";
            var icon = Ux.Icon(attached ? "check" : "camera");
            icon.pickingMode = PickingMode.Ignore;
            chip.Add(icon);
            chip.Add(ChipLabel(attached ? t.T("online.foto.angehaengt") : t.T("online.foto.anhaengen")));
            var col = Ux.Col("gb-compose-photo-side");
            col.Add(chip);
            if (attached)
            {
                col.Add(Ux.IconTextBtn("redo", t.T("online.foto.neu"), () => { Snap(); RefreshDesk(); }, "gb-btn-secondary", "gb-btn-sm", "gb-compose-photo-retake"));
                if (_photoPlaceholder) col.Add(Ux.Text(t.T("online.foto.platzhalter"), "gb-on-hint", "gb-compose-photo-note"));
            }
            else col.Add(Ux.Text(t.T("online.foto.hint"), "gb-on-hint", "gb-compose-photo-note"));
            row.Add(col);
            if (attached)
            {
                var preview = Polaroid.Create(132, "", 3f, "gb-compose-polaroid");
                preview.Root.name = "photo-preview";
                Polaroid.Show(preview, _photoPreview, t.T("online.foto.bereit"));
                row.Add(preview.Root);
            }
            return row;
        }

        private void TogglePhoto()
        {
            if (_photo != null) DropPhoto();
            else Snap();
            RefreshDesk();
        }

        private void Snap()
        {
            DropPhoto();
            _photo = Online.TakePhoto(out _photoPlaceholder);
            _photoPreview = PhotoCapture.ToTexture(_photo);
        }

        /// <summary>Rebuilds the writing desk, keeping the typed text.</summary>
        private void RefreshDesk()
        {
            _draft = _text?.value ?? _draft;
            RefreshPane();
        }

        private static Button Chip(Action onClick, string kind, bool active)
        {
            var chip = new Button(onClick) { text = "" };
            chip.ClearClassList();
            chip.AddToClassList("gb-on-chip");
            chip.AddToClassList(kind);
            chip.EnableInClassList("gb-on-chip--active", active);
            return chip;
        }

        private static Label ChipLabel(string text)
        {
            var l = Ux.Text(text, "gb-nowrap");
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private Button GiftChip(string typ, string id, string label)
        {
            var chip = Chip(() => { _gift = (typ, id); _draft = _text?.value ?? _draft; RefreshPane(); }, "gb-on-chip--gift", _gift.id == id && _gift.typ == typ);
            chip.name = "gift-" + (id ?? "none");
            if (id != null)
            {
                var icon = Ux.Icon(GiftIcon(typ, id));
                icon.pickingMode = PickingMode.Ignore;
                chip.Add(icon);
            }
            else chip.AddToClassList("gb-on-chip--plain");
            chip.Add(ChipLabel(label));
            return chip;
        }

        private void RefreshComposeInfo()
        {
            if (_composeInfo == null) return;
            var t = _host.Strings;
            var n = (_draft ?? "").Length;
            _composeInfo.text = t.T("mail.compose.zeichen", ("n", n), ("max", SocialClient.TextMax));
            _composeInfo.EnableInClassList("gb-compose-count--warn", n >= SocialClient.TextMax - 50);
            if (_composeOffline != null) _composeOffline.style.display = Online.Client.IsOnline ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Send()
        {
            var t = _host.Strings;
            var text = (_text?.value ?? "").Trim();
            if (string.IsNullOrEmpty(_to)) return;
            if (text.Length == 0 && _gift.id == null && _photo == null) { _host.Ui.Toast(t.T("mail.err.bad_mail"), "mail"); return; }
            var friend = Online.Client.FindFriend(_to);
            var name = friend != null ? friend.Name : _to;
            var withPhoto = _photo != null;
            Online.SendLetter(_to, text, _gift.typ, _gift.id, _gift.id != null ? 1 : 0, _photo, (r, code) =>
            {
                if (r == null)
                {
                    _host.Ui.Toast(code == "NO_PORTO" ? t.T("mail.toast.kein_porto", ("porto", OnlineService.Porto)) : t.T("mail.toast.kein_item"), "mail");
                    Sfx.PlayUi("soft_error");
                    return;
                }
                if (r.Ok || r.Queued)
                {
                    var msg = r.PhotoDropped && r.Queued ? t.T("online.foto.toast.ohne_foto")
                        : r.PhotoDropped ? t.T("online.foto.toast.ohne_foto_raus", ("name", name))
                        : r.Queued ? t.T("mail.toast.queued")
                        : withPhoto ? t.T("online.foto.toast.gesendet", ("name", name))
                        : t.T("mail.toast.gesendet", ("name", name));
                    _host.Ui.Toast(msg, withPhoto ? "camera" : "mail", r.PhotoDropped ? 4.5f : 3.2f);
                    Debug.Log("[mail] sent to=" + _to + " photo=" + withPhoto + " result=" + r + (r.PhotoDropped ? " (photo dropped)" : ""));
                    _draft = "";
                    _gift = (null, null);
                    DropPhoto();
                    _composing = false;
                    RefreshList();
                    RefreshPane();
                }
                else
                {
                    Sfx.PlayUi("soft_error");
                    _host.Ui.Toast(Online.ErrorText(code), "mail", 4f);
                }
            });
        }

        private string GiftName(string typ, string id)
        {
            var t = _host.Strings;
            if (typ == "food" && t.HasKey("rewards.food." + id)) return t.T("rewards.food." + id);
            if (t.HasKey("mail.item." + id)) return t.T("mail.item." + id);
            return id;
        }

        private static string GiftIcon(string typ, string id)
        {
            if (typ == "food" && id != null && FoodIcons.TryGetValue(id, out var icon)) return icon;
            if (id == "fertilizer") return "sprout";
            if (id == "medicine") return "leaf";
            return typ == "food" ? "carrot" : "gift";
        }

        private string When(long ms)
        {
            if (ms <= 0) return "";
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            return dt.Date == DateTimeOffset.Now.Date ? _host.Strings.T("online.zeit.heute", ("uhr", dt.ToString("HH:mm"))) : dt.ToString("dd.MM. HH:mm");
        }
    }
}
