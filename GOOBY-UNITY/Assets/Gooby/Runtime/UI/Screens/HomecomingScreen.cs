using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The reunion after a holiday (port of heimkehr_moment.gd's HeimkehrKarte, W19/MITBRINGSEL): a cream card with
    /// "Wieder zu Hause!", the souvenir out of the bag (a snow globe, a fridge magnet or a pennant of the destination),
    /// the quiet hint that the other two wait at the airport for 24 hours, and "Fest drücken!" — a hug with confetti.
    /// </summary>
    public sealed class HomecomingScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly Urlaub.Heimkehr _gift;
        private bool _closing;

        public HomecomingScreen(GameHost host, Urlaub.Heimkehr gift)
        {
            _host = host;
            _gift = gift;
            Id = "homecoming";
        }

        public static string SouvenirName(Gooby.Core.I18n.I18n t, string typ, string zielId) =>
            typ == "fund"
                ? t.T("urlaub.souvenir." + zielId)
                : t.T("heimkehr.mitbringsel.typ." + typ, ("ziel", t.T("travel.ziel." + zielId)));

        public static string SouvenirIcon(string typ) => typ == "schneekugel" ? "globe" : typ == "magnet" ? "pin" : typ == "wimpel" ? "race_flag" : "gift";

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Hug());
            root.Add(scrim);
            var card = Ux.Col("gb-card", "gb-homecoming");
            card.name = "homecoming-card";
            var medal = Ux.El("gb-cheer-medal");
            medal.Add(Ux.Icon("heart", "gb-cheer-icon"));
            card.Add(medal);
            card.Add(Ux.Text(t.T("heimkehr.titel"), "gb-cheer-title"));
            var gift = Ux.Row("gb-homecoming-gift");
            gift.Add(Ux.IconTile(SouvenirIcon(_gift.Typ), new Color(0.85f, 0.95f, 1f)));
            var name = Ux.Text(t.T("heimkehr.tuete", ("name", SouvenirName(t, _gift.Typ, _gift.ZielId))), "gb-lg", "gb-bold");
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.flexShrink = 1;
            name.style.marginLeft = 12;
            gift.Add(name);
            card.Add(gift);
            var hours = Urlaub.FensterRestH(_host.Store.State, _host.Store.Clock.NowMs());
            if (hours > 0)
            {
                var hint = Ux.Text(t.T("heimkehr.gfree.fenster", ("stunden", hours), ("ziel", t.T("travel.ziel." + _gift.ZielId))), "gb-md", "gb-mild");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.marginTop = 10;
                card.Add(hint);
            }
            var hug = Ux.IconTextBtn("heart", t.T("heimkehr.knopf.umarmen"), Hug, "gb-btn-primary");
            hug.name = "homecoming-hug";
            hug.style.marginTop = 16;
            hug.style.alignSelf = Align.Center;
            card.Add(hug);
            root.Add(card);
            Sfx.PlayUi("soft_levelup");
        }

        private void Hug()
        {
            if (_closing) return;
            _closing = true;
            Sfx.PlayUi("soft_win");
            Haptics.Success();
            _host.Ui.Confetti(110);
            var a = _host.World?.Avatar;
            if (a != null) { a.SetEmotion("ecstatic"); a.Play("celebrate"); }
            if (_host.Ui.Current == this) _host.Ui.Pop();
            Debug.Log("[heimkehr] umarmt");
        }
    }
}
