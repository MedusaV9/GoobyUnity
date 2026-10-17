using Gooby.Core.Meta;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The daily quest sheet (port of quest_panel.gd): today's three quests as cards — category tile, title and the task in
    /// a sentence, progress bar with n/goal, the reward (+coins · XP) and "Abholen" once done (a green "Erledigt!" after) —
    /// the all-three bonus line, and the once-a-day reroll of untouched quests at the foot. Coins fly from the button into
    /// the purse. Opened from the HUD's task card.
    /// </summary>
    public sealed class QuestSheetScreen : UiScreen
    {
        private readonly GameHost _host;
        private CounterPanel.Parts _ui;

        public QuestSheetScreen(GameHost host)
        {
            _host = host;
            Id = "quests";
        }

        /// <summary>Category → icon and identity colour (quest_panel.gd KATEGORIE_STYLE).</summary>
        private static (string icon, Color tint) Style(string kategorie)
        {
            switch (kategorie)
            {
                case "care": return ("heart", new Color(1f, 0.82f, 0.86f));
                case "games": return ("gamepad", new Color(0.72f, 0.93f, 0.89f));
                case "garden": return ("leaf", new Color(0.8f, 0.93f, 0.78f));
                case "economy": return ("coin", new Color(1f, 0.92f, 0.7f));
                default: return ("star", new Color(0.93f, 0.9f, 1f));
            }
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "leaf", new Color(0.84f, 0.95f, 0.86f), t.T("quests.titel"), t.T("quests.untertitel"), () => _host.Ui.Pop());
            Refresh();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            var quests = _host.Quests;
            _ui.Body.Clear();
            _ui.Coins.text = Ux.FormatNumber(Gooby.Core.City.ShopLogic.Coins(_host.Store));
            var col = Ux.Col("gb-quest-col");
            _ui.Body.Add(col);
            if (quests == null) { col.Add(CounterPanel.Empty(t.T("quests.titel"), t.T("quests.leer"), true)); return; }
            var board = quests.Board();
            if (board.Count == 0) col.Add(CounterPanel.Empty(t.T("quests.titel"), t.T("quests.leer"), true));
            var claimed = 0;
            foreach (var row in board)
            {
                if (row.Claimed) claimed++;
                col.Add(QuestCard(row));
            }

            // the all-three bonus: open with the count, or paid for today
            var bonus = Ux.Row("gb-quest-bonus");
            bonus.name = "quest-bonus";
            var paid = quests.AllClaimed();
            if (paid) bonus.AddToClassList("gb-quest-bonus--paid");
            bonus.Add(Ux.IconTile(paid ? "trophy" : "star", paid ? new Color(0.8f, 0.93f, 0.78f) : new Color(1f, 0.92f, 0.7f)));
            bonus.Add(Ux.Text(paid ? t.T("quests.bonus_bezahlt") : t.T("quests.bonus_offen", ("muenzen", DailyQuestEngine.BonusCoins), ("xp", DailyQuestEngine.BonusXp)), "gb-quest-bonus-text"));
            bonus.Add(Ux.Text(claimed + "/" + DailyQuestEngine.QuestsPerDay, "gb-quest-bonus-count"));
            col.Add(bonus);

            // once a day: swap the untouched quests
            var foot = Ux.Row("gb-quest-foot");
            foot.Add(Ux.Text(t.T("quests.reroll_hinweis"), "gb-quest-foot-hint"));
            var reroll = Ux.IconTextBtn("rotate", t.T("quests.reroll"), Reroll, "gb-btn-secondary", "gb-btn-sm");
            reroll.name = "quest-reroll";
            reroll.SetEnabled(quests.RerollAvailable());
            foot.Add(reroll);
            col.Add(foot);
        }

        private VisualElement QuestCard(DailyQuestService.Row row)
        {
            var t = _host.Strings;
            var (icon, tint) = Style(row.Def.Kategorie);
            var card = Ux.Row("gb-quest");
            card.name = "quest-" + row.Def.Id;
            if (row.Claimed) card.AddToClassList("gb-quest--done");
            card.Add(Ux.IconTile(icon, tint));
            var texts = Ux.Col("gb-quest-texts");
            texts.Add(Ux.Text(t.HasKey(row.Def.TitleKey) ? t.T(row.Def.TitleKey) : row.Def.Id, "gb-quest-title"));
            if (t.HasKey(row.Def.BodyKey)) texts.Add(Ux.Text(t.T(row.Def.BodyKey), "gb-quest-text"));
            var progress = Ux.Row("gb-quest-progress");
            var bar = Ux.Bar(row.Target > 0 ? Mathf.Clamp01((float)row.Progress / row.Target) : 0f, out _);
            bar.AddToClassList("gb-quest-bar");
            progress.Add(bar);
            progress.Add(Ux.Text(t.T("quests.fortschritt", ("n", Mathf.Min(row.Progress, row.Target)), ("ziel", row.Target)), "gb-quest-count"));
            texts.Add(progress);
            card.Add(texts);
            var side = Ux.Col("gb-quest-side");
            side.Add(Ux.Text(t.T("quests.belohnung", ("muenzen", row.Def.Muenzen), ("xp", row.Def.Xp)), "gb-quest-reward"));
            if (row.Claimed) side.Add(Ux.Text(t.T("quests.erledigt"), "gb-quest-stamp"));
            else if (row.Complete)
            {
                var id = row.Def.Id;
                Button claim = null;
                claim = Ux.IconTextBtn("coin", t.T("quests.abholen"), () => Claim(id, claim), "gb-btn-confirm", "gb-btn-sm");
                claim.name = "quest-claim-" + id;
                side.Add(claim);
            }
            card.Add(side);
            return card;
        }

        private void Claim(string id, VisualElement from)
        {
            var t = _host.Strings;
            var outcome = _host.Quests.Claim(id);
            Debug.Log("[quests] claim " + id + " → " + (outcome.Ok ? "+" + outcome.Muenzen : "refused"));
            if (!outcome.Ok) { Sfx.PlayUi("soft_error"); return; }
            Sfx.PlayUi("soft_coins");
            var target = Root.Q("sheet-coins");
            if (target != null && from != null && from.panel != null)
                _host.Ui.FlyIcons(from.worldBound.center, target, "coin", Mathf.Clamp(outcome.Muenzen / 10, 3, 8));
            _host.Ui.Toast(t.T("quests.claim_toast", ("muenzen", outcome.Muenzen), ("xp", outcome.Xp)), "trophy");
            if (outcome.BonusPaid)
            {
                _host.Ui.Toast(t.T("quests.bonus_toast", ("muenzen", DailyQuestEngine.BonusCoins)), "star");
                _host.Ui.Confetti(50);
            }
            Refresh();
        }

        private void Reroll()
        {
            if (!_host.Quests.Reroll()) { Sfx.PlayUi("soft_error"); return; }
            Sfx.PlayUi("soft_chip");
            _host.Ui.Toast(_host.Strings.T("quests.reroll_toast"), "rotate");
            Debug.Log("[quests] reroll");
            Refresh();
        }
    }
}
