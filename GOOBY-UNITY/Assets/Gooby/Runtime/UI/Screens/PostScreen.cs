using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Pia's counter (post.gd): the parcel desk hands out the day's parcel (the DailyBonus package sheet) and the
    /// postcard archive shows what Gooby sent from his trips (Vacation archive, Postcards text keys).
    /// </summary>
    public sealed class PostScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private CounterPanel.Parts _ui;

        public PostScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "counter-post";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "mail", new Color(1f, 0.93f, 0.7f), t.T("city.post.sheet_titel"), t.T("nl.post.hinweis"), () => _host.Ui.Pop());
            Refresh();
        }

        public override void OnShown() => Refresh();

        private void Refresh()
        {
            if (_ui == null) return;
            var t = _host.Strings;
            _ui.Body.Clear();
            _ui.Coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));
            // parcel desk: today's parcel or "already collected"
            var claimable = PackageScreen.Claimable(_host);
            _ui.Body.Add(CounterPanel.Card("gift", new Color(1f, 0.85f, 0.85f), t.T("nl.post.paket"), claimable ? t.T("nl.post.paket_text") : t.T("nl.post.paket_geholt"), null,
                t.T("nl.post.paket_holen"), claimable, () => _host.Ui.Push(new PackageScreen(_host, _ => { }))));
            // letters to friends (mail.schalter.*): Frau Zettel's counter opens the mailbox
            var online = _host.Online?.Client;
            var unread = online?.MailUnread ?? 0;
            var sub = online != null && online.IsOnline ? (unread > 0 ? t.T("mail.schalter.neu", ("n", unread)) : t.T("mail.schalter.text")) : t.T("mail.schalter.offline");
            if (online != null && online.OutboxCount > 0) sub += " · " + t.T("mail.schalter.outbox", ("n", online.OutboxCount));
            _ui.Body.Add(CounterPanel.Card("mail", new Color(1f, 0.87f, 0.9f), t.T("mail.schalter.titel"), sub, unread > 0 ? unread.ToString() : null,
                t.T("mail.schalter.offen"), true, () => _host.Ui.Push(new MailboxScreen(_host))));
            // postcard archive: the newest card's line, the whole archive (souvenir shelf, set bonus) one tap away
            var archive = Postcards.ArchiveOf(_host.Store.State);
            var trips = J.Int(Vacation.SliceOf(_host.Store.State)["trips"]);
            var archiveCard = CounterPanel.Ware("map", new Color(0.8f, 0.9f, 1f), t.T("nl.post.archiv"),
                archive.Count == 0 ? t.T("nl.post.archiv_leer") : t.T("nl.post.archiv_zahl", ("n", archive.Count)), trips > 0 ? t.T("nl.post.reisen", ("n", trips)) : null);
            archiveCard.name = "post-archiv";
            if (archive.Count > 0 && archive[archive.Count - 1] is JObject newest)
            {
                var key = Postcards.TextKey(newest);
                var line = Ux.Text("„" + (t.HasKey(key) ? t.T(key) : "") + "“", "gb-sm");
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.marginTop = 4;
                line.pickingMode = PickingMode.Ignore;
                archiveCard.Insert(archiveCard.childCount - 1, line);
            }
            var open = CounterPanel.ActionBtn(t.T("postkarten.ansehen"), () => _host.Ui.Push(new PostcardsScreen(_host)), true, "gb-btn-primary");
            open.name = "post-archiv-open";
            archiveCard.Add(open);
            _ui.Body.Add(archiveCard);
        }
    }
}
