using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The HUD's holiday bits: while Gooby is away a card on the right offers a visit (VacationScreen), and his first
    /// time home after the pickup is a reunion — he cheers, suitcase and souvenir bag stand beside him, he says hello
    /// and the card hands over the souvenir (port of heimkehr_moment.gd).
    /// </summary>
    public sealed partial class HomeHudScreen
    {
        private VisualElement _vacationCard;
        private Label _vacationText;
        private bool _homecomingQueued;

        private void BuildVacationCard(VisualElement root)
        {
            var t = _host.Strings;
            _vacationCard = Ux.El("gb-card", "gb-card-rosa", "gb-row");
            _vacationCard.name = "vacation-card";
            Ux.Abs(_vacationCard, null, 340, 16);
            _vacationCard.style.width = 300;
            _vacationCard.Add(Ux.IconTile("compass", new Color(0.8f, 0.9f, 1f)));
            var col = Ux.Col("gb-grow");
            col.style.marginLeft = 10;
            col.style.flexShrink = 1; col.style.minWidth = 0;
            col.Add(Ux.Text(t.T("urlaub.karte.titel"), "gb-lg", "gb-bold"));
            _vacationText = Ux.Text("", "gb-sm", "gb-mild");
            _vacationText.style.whiteSpace = WhiteSpace.Normal;
            col.Add(_vacationText);
            _vacationCard.Add(col);
            var ch = Ux.Icon("chevron");
            ch.style.width = 18; ch.style.height = 18;
            _vacationCard.Add(ch);
            _vacationCard.RegisterCallback<ClickEvent>(_ => OpenVacation());
            _vacationCard.style.display = DisplayStyle.None;
            root.Add(_vacationCard);
        }

        private void RefreshVacationCard()
        {
            if (_vacationCard == null) return;
            var now = _host.Store.Clock.NowMs();
            var can = Urlaub.Verfuegbar(_host.Store.State, now);
            _vacationCard.style.display = can ? DisplayStyle.Flex : DisplayStyle.None;
            if (can)
            {
                var t = _host.Strings;
                _vacationText.text = t.T("urlaub.karte.text", ("ziel", t.T("travel.ziel." + Urlaub.Ziel(_host.Store.State))), ("tage", Gooby.Core.City.Travel.RestTage(_host.Store, now)));
            }
            LayoutRightCards();
        }

        /// <summary>The cards under the tasks stack top-down: today's parcel, the holiday visit, a random event.</summary>
        private void LayoutRightCards()
        {
            var y = _compact ? 164f : 340f;
            foreach (var card in new[] { _giftCard, _vacationCard, _eventCard })
            {
                if (card == null || card.style.display == DisplayStyle.None) continue;
                card.style.top = y;
                y += 92f;
            }
        }

        public void OpenVacation()
        {
            var state = _host.Store.State;
            if (!Urlaub.Verfuegbar(state, _host.Store.Clock.NowMs())) { RefreshVacationCard(); return; }
            var dest = Urlaub.Ziel(state);
            Sfx.PlayUi("soft_open");
            _host.Ui.FadeThrough(() => _host.Ui.Push(new VacationScreen(_host, dest)));
        }

        /// <summary>First time home after the pickup: the reunion beat and the souvenir card (once).</summary>
        private void TryHomecoming()
        {
            if (_homecomingQueued || _host.Ui.Current != this || _away || !Onboarded) return;
            if (!Urlaub.HeimkehrAusstehend(_host.Store.State))
            {
                // after the homecoming of the ninth trip: the Weltengooby title moment, once per save
                if (WeltengoobyScreen.Due(_host))
                    Root?.schedule.Execute(() =>
                    {
                        if (_host.Ui.Current != this || !WeltengoobyScreen.Due(_host)) return;
                        WeltengoobyScreen.Latch(_host);
                        _host.Ui.Push(new WeltengoobyScreen(_host));
                    }).StartingIn(900);
                return;
            }
            _homecomingQueued = true;
            Root?.schedule.Execute(() =>
            {
                _homecomingQueued = false;
                if (_host.Ui.Current != this || _host.Ui.Fading) return;
                Urlaub.Heimkehr gift = null;
                _host.Store.Update(s => gift = Urlaub.Feiern(s), GameHost.TicksMs);
                if (gift == null || !gift.Ok) return;
                _host.Store.NotifySliceChanged("vacation");
                _host.Store.NotifySliceChanged("inventory");
                Say(_host.Strings.T(gift.SpruchKey));
                _lastLifeBubble = Time.unscaledTime;
                var w = _host.World;
                if (w != null)
                {
                    if (w.CrateNapping) w.WakeInCrate(10f);
                    w.Avatar?.SetEmotion("ecstatic");
                    w.Avatar?.Play("celebrate");
                    Hold(5f);
                    w.HomecomingProps();
                }
                HeartBurst(10);
                Debug.Log("[heimkehr] " + gift.ZielId + " → " + gift.ItemId);
                Root?.schedule.Execute(() => { if (_host.Ui.Current == this) _host.Ui.Push(new HomecomingScreen(_host, gift)); }).StartingIn(1400);
            }).StartingIn(700);
        }
    }
}
