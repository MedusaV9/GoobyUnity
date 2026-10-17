using System;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The parcel behind the HUD's "Neues Geschenk!" card (board 08): the daily bonus as a package to open.
    /// Shows the streak (7 days, snack from day 7), the reward preview and the grace note; opening claims through
    /// DailyBonus.Claim inside a store Update so coins and the inventory follow, then flips into the "Juhu!" state.
    /// </summary>
    public sealed class PackageScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly Action<string> _say;
        private VisualElement _sheet;

        public PackageScreen(GameHost host, Action<string> say)
        {
            _host = host;
            _say = say;
            Id = "package";
        }

        /// <summary>Whether today's parcel is still unopened.</summary>
        public static bool Claimable(GameHost host) =>
            DailyBonus.IsClaimable(J.Dict(host.Store.State["daily"]), host.Store.Clock.LocalDay());

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            _sheet = Ux.El("gb-sheet", "gb-win-sheet");
            _sheet.style.left = 300; _sheet.style.right = 300;
            root.Add(_sheet);
            BuildClosed();
        }

        private void BuildClosed()
        {
            var t = _host.Strings;
            _sheet.Clear();
            var daily = J.Dict(_host.Store.State["daily"]);
            var today = _host.Store.Clock.LocalDay();
            var claimable = DailyBonus.IsClaimable(daily, today);
            var (streak, grace) = claimable ? DailyBonus.NextStreak(daily, today) : (Math.Max(1, J.Int(daily["streak"])), false);
            var (coins, food) = DailyBonus.RewardForStreak(streak);

            _sheet.Add(CounterPanel.Head("gift", new Color(1f, 0.85f, 0.85f), t.T(claimable ? "nl.paket.titel" : "nl.paket.schon_titel"),
                t.T(claimable ? "nl.paket.untertitel" : "nl.paket.schon_geholt"), () => _host.Ui.Pop()).Row);
            var frame = Ux.Col("gb-win-body");

            // the parcel on a sun disc, the 7-day streak, the reward as a coin pill
            var body = Ux.Row("gb-pkg-body");
            var art = Ux.El("gb-pkg-art");
            art.Add(Ux.Image("ui_paket", 150, 150));
            body.Add(art);

            var info = Ux.Col("gb-pkg-info");
            info.Add(Ux.Text(t.T("nl.paket.serie", ("tag", streak)), "gb-pkg-streak"));
            var dots = Ux.Row("gb-pkg-days");
            for (var day = 1; day <= DailyBonus.RewardTable.Length; day++)
            {
                var dot = Ux.El("gb-streak-day");
                if (day < streak || (day == streak && !claimable)) dot.AddToClassList("gb-streak-day--done");
                if (day == streak && claimable) dot.AddToClassList("gb-streak-day--today");
                var label = Ux.Text(day.ToString(), "gb-sm", "gb-bold");
                if (day == DailyBonus.FoodFromDay) { label.text = ""; dot.Add(Ux.Icon("carrot")); }
                else dot.Add(label);
                dots.Add(dot);
            }
            info.Add(dots);
            var reward = Ux.Row("gb-pkg-reward");
            reward.Add(Ux.Icon("coin"));
            reward.Add(Ux.Text(t.T("nl.paket.belohnung", ("coins", coins))));
            info.Add(reward);
            if (food) info.Add(Ux.Text(t.T("nl.paket.snack"), "gb-pkg-note", "gb-pkg-note--hot"));
            if (grace) info.Add(Ux.Text(t.T("nl.paket.kulanz"), "gb-pkg-note", "gb-pkg-note--sm"));
            body.Add(info);
            frame.Add(body);

            var buttons = Ux.Row("gb-win-actions", "gb-win-actions--end");
            buttons.Add(Ux.Btn(t.T("nl.paket.spaeter"), () => _host.Ui.Pop(), "gb-btn-secondary", "gb-btn-sm"));
            var open = Ux.IconTextBtn("gift", t.T("nl.paket.oeffnen"), OpenParcel, "gb-btn-confirm");
            open.name = "open-parcel";
            open.SetEnabled(claimable);
            buttons.Add(open);
            frame.Add(buttons);
            _sheet.Add(frame);
        }

        /// <summary>Claims today's bonus (the "Paket öffnen" button) and shows what was inside.</summary>
        public void OpenParcel()
        {
            var t = _host.Strings;
            var today = _host.Store.Clock.LocalDay();
            DailyBonus.ClaimResult result = null;
            _host.Store.Update(s => result = DailyBonus.Claim(s, today), GameHost.TicksMs);
            if (result == null || !result.Ok) { BuildClosed(); return; }
            _host.Store.NotifySliceChanged("daily");
            _host.Store.NotifySliceChanged("achievements");
            _host.World?.Bounce();
            // the moment of the day: coins jingle, confetti, the stinger (bigger on the seventh day)
            Sfx.PlayUi("soft_coins");
            Sfx.Play("soft_win", 0f, 0.8f);
            Haptics.Success();
            _host.Ui.Confetti(result.StreakDay >= DailyBonus.FoodFromDay ? 110 : 60);
            if (result.StreakDay >= DailyBonus.FoodFromDay) _host.Audio?.PlayStinger("stinger-levelup");
            _say?.Invoke(t.T("nl.paket.gooby_freut"));
            BuildOpened(result);
        }

        private void BuildOpened(DailyBonus.ClaimResult result)
        {
            var t = _host.Strings;
            _sheet.Clear();
            var snack = result.FoodId.Length > 0 ? (t.HasKey("rewards.food." + result.FoodId) ? t.T("rewards.food." + result.FoodId) : result.FoodId) : "";
            _sheet.Add(CounterPanel.Head("sparkle", new Color(1f, 0.93f, 0.75f), t.T("nl.paket.geoeffnet_titel"), snack.Length > 0
                ? t.T("nl.paket.geoeffnet_snack", ("coins", result.Coins), ("snack", snack))
                : t.T("nl.paket.geoeffnet_text", ("coins", result.Coins)), () => _host.Ui.Pop()).Row);
            var frame = Ux.Col("gb-win-body");

            var body = Ux.Row("gb-pkg-body", "gb-pkg-body--center");
            var art = Ux.El("gb-pkg-art", "gb-pkg-art--sm");
            art.Add(Ux.Image("ui_paket", 128, 128));
            body.Add(art);
            var col = Ux.Col();
            var coinsRow = Ux.Row("gb-pkg-reward", "gb-pkg-reward--big");
            coinsRow.Add(Ux.Icon("coin"));
            coinsRow.Add(Ux.Text("+" + result.Coins));
            col.Add(coinsRow);
            col.Add(Ux.Text(t.T("nl.paket.serie", ("tag", result.StreakDay)), "gb-pkg-note"));
            body.Add(col);
            frame.Add(body);

            var buttons = Ux.Row("gb-win-actions", "gb-win-actions--end");
            buttons.Add(Ux.IconTextBtn("check", t.T("nl.paket.weiter"), () => _host.Ui.Pop(), "gb-btn-confirm"));
            frame.Add(buttons);
            _sheet.Add(frame);
        }
    }
}
