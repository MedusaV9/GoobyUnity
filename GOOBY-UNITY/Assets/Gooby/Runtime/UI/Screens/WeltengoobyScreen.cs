using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The title moment for all nine destinations (port of urlaubs_bonus.gd's WeltengoobyKarte): once per save, back home
    /// after the ninth trip, a centred card over the dimmed room — the globe medal, "Weltengooby!", the line about Gooby
    /// being at home everywhere, "Weiter geht's!" — with the reward jingle, confetti and Gooby cheering. The latch is
    /// vacation.weltengoobyGefeiert.
    /// </summary>
    public sealed class WeltengoobyScreen : UiScreen
    {
        private readonly GameHost _host;
        private bool _closing;

        public WeltengoobyScreen(GameHost host)
        {
            _host = host;
            Id = "weltengooby";
        }

        /// <summary>All nine reached and not yet celebrated.</summary>
        public static bool Due(GameHost host)
        {
            var v = Gooby.Core.Life.Vacation.SliceOf(host.Store.State);
            return Gooby.Core.Life.Vacation.Weltengooby(v) && !J.IsTrue(v["weltengoobyGefeiert"]);
        }

        /// <summary>Marks the moment as celebrated (once per save) right when the card goes up.</summary>
        public static void Latch(GameHost host)
        {
            host.Store.Update(s => { if (s["vacation"] is Newtonsoft.Json.Linq.JObject vac) vac["weltengoobyGefeiert"] = true; }, GameHost.TicksMs);
            host.Store.NotifySliceChanged("vacation");
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);
            var card = Ux.Col("gb-card", "gb-homecoming");
            card.name = "weltengooby-card";
            var medal = Ux.El("gb-cheer-medal");
            medal.Add(Ux.Icon("globe", "gb-cheer-icon"));
            card.Add(medal);
            card.Add(Ux.Text(t.T("g4travel.feier.titel"), "gb-cheer-title"));
            var text = Ux.Text(t.T("raumstation.weltengooby.toast"), "gb-lg");
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.marginTop = 6;
            card.Add(text);
            var go = Ux.IconTextBtn("plane", t.T("g4travel.feier.weiter"), Close, "gb-btn-primary");
            go.name = "weltengooby-weiter";
            go.style.marginTop = 16;
            go.style.alignSelf = Align.Center;
            card.Add(go);
            root.Add(card);
            Sfx.PlayUi("soft_sticker");
            Haptics.Success();
            _host.Ui.Confetti(120);
            var a = _host.World?.Avatar;
            if (a != null) { a.SetEmotion("ecstatic"); a.Play("celebrate"); }
            Debug.Log("[weltengooby] celebrated");
        }

        private void Close()
        {
            if (_closing) return;
            _closing = true;
            Sfx.PlayUi("soft_win");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }
    }
}
