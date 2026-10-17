using System.Collections.Generic;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The album's collections (port of collections_view.gd over <see cref="CollectionsLogic"/>): a "Sammlungen" chip
    /// leads the rail; its page shows the four sets — pond fish, garden veggies, town sights, sweets — as cards with a
    /// tile per entry (found: its picture on a pastel tile with ×n for doubles; missing: a "?" and "???", never a
    /// spoiler), the progress and the claim button. A complete set pays once: coins, 50 XP and its own piece of deco
    /// into the storage (goldfish bowl, golden watering can, toy town, candy jar) with a toast and confetti.
    /// </summary>
    public sealed partial class AlbumScreen
    {
        public const string CollectionsPage = "sammlungen";

        private static readonly Dictionary<string, string> SetIcons = new Dictionary<string, string>
        {
            ["fish"] = "fish", ["veggies"] = "food_carrot", ["landmarks"] = "map", ["treats"] = "candy",
        };

        /// <summary>The picture of a found entry (the set's icon where there is no own one).</summary>
        private static readonly Dictionary<string, string> EntryIcons = new Dictionary<string, string>
        {
            ["veggies.radish"] = "food_radish", ["veggies.carrot"] = "food_carrot", ["veggies.salad"] = "food_salad",
            ["veggies.tomato"] = "food_tomato", ["veggies.corn"] = "food_corn", ["veggies.eggplant"] = "food_eggplant",
            ["veggies.pumpkin"] = "food_pumpkin", ["veggies.watermelon"] = "food_watermelon",
            ["landmarks.shop"] = "shop", ["landmarks.vetClinic"] = "bandage", ["landmarks.fountain"] = "rain",
            ["landmarks.skyTower"] = "pin", ["landmarks.parkGazebo"] = "ride_wheel", ["landmarks.windmillCafe"] = "teacup",
            ["treats.donut-sprinkles"] = "food_donut", ["treats.cupcake"] = "food_cupcake", ["treats.ice-cream"] = "food_icecream",
            ["treats.cake"] = "food_cake", ["treats.cookie"] = "food_cookie", ["treats.candy-bar"] = "food_candybar",
            ["treats.lollypop"] = "food_lollipop", ["treats.sundae"] = "food_icecream", ["treats.chocolate"] = "food_chocolate",
            ["treats.muffin"] = "food_cupcake",
        };

        private JObject Collections => CollectionsLogic.NormalizeSlice(State["collections"]);

        /// <summary>A complete set whose reward is still waiting.</summary>
        private bool AnySetClaimable()
        {
            var c = Collections;
            foreach (var def in CollectionsLogic.Sets)
                if (CollectionsLogic.IsSetComplete(c, def.Id) && !CollectionsLogic.IsClaimed(c, def.Id)) return true;
            return false;
        }

        private void AddCollectionsChip()
        {
            var t = _host.Strings;
            var (have, total) = CollectionsLogic.TotalProgress(Collections);
            var chip = new Button(() => { if (_page == CollectionsPage) return; _page = CollectionsPage; Sfx.PlayUi("soft_chip"); Refresh(); }) { text = "", name = "album-page-" + CollectionsPage };
            chip.ClearClassList();
            chip.AddToClassList("gb-alb-chip");
            chip.AddToClassList("gb-alb-chip--sets");
            chip.EnableInClassList("gb-alb-chip--active", _page == CollectionsPage);
            chip.EnableInClassList("gb-alb-chip--done", total > 0 && have >= total);
            var tint = new Color(0.87f, 0.95f, 1f);
            chip.style.backgroundColor = _page == CollectionsPage ? tint : Color.Lerp(tint, Color.white, 0.5f);
            chip.Add(Ux.Icon("sparkle", "gb-alb-chip-icon"));
            chip.Add(Ux.Text(t.T("collections.chip"), "gb-alb-chip-title"));
            chip.Add(Ux.Text(have + "/" + total, "gb-alb-chip-count"));
            if (AnySetClaimable()) chip.Add(Ux.El("gb-alb-chip-dot"));
            _rail.Add(chip);
        }

        private void BuildCollectionsPage()
        {
            var t = _host.Strings;
            var c = Collections;
            var (have, total) = CollectionsLogic.TotalProgress(c);
            _pageHead.Add(Ux.Text(t.T("collections.titel"), "gb-h3", "gb-alb-pagetitle"));
            var bar = Ux.Bar(total > 0 ? (float)have / total : 0f, out _, have >= total ? "gb-bar-fill--sonne" : "gb-bar-fill--blau");
            bar.AddToClassList("gb-alb-pagebar");
            _pageHead.Add(bar);
            _pageHead.Add(Ux.Text(t.T("collections.gesamt", ("n", have), ("total", total)), "gb-alb-progress"));
            _grid.AddToClassList("gb-alb-grid--sets");
            foreach (var def in CollectionsLogic.Sets) _grid.Add(SetCard(def, c));
        }

        private VisualElement SetCard(CollectionsLogic.SetDef def, JObject c)
        {
            var t = _host.Strings;
            var (have, total) = CollectionsLogic.SetProgress(c, def.Id);
            var claimed = CollectionsLogic.IsClaimed(c, def.Id);
            var complete = CollectionsLogic.IsSetComplete(c, def.Id);
            var card = Ux.El("gb-alb-set");
            card.name = "album-set-" + def.Id;
            card.EnableInClassList("gb-alb-set--ready", complete && !claimed);
            card.EnableInClassList("gb-alb-set--claimed", claimed);

            var head = Ux.Row("gb-alb-set-head");
            var badge = Ux.El("gb-alb-set-badge");
            badge.Add(Ux.Icon(SetIcons.TryGetValue(def.Id, out var icon) ? icon : "star"));
            head.Add(badge);
            var texts = Ux.Col("gb-alb-set-texts");
            texts.Add(Ux.Text(t.T("collections.set." + def.Id), "gb-alb-set-title"));
            var hint = Ux.Text(t.T("collections.hinweis." + def.Id), "gb-alb-set-hint");
            texts.Add(hint);
            head.Add(texts);
            head.Add(Ux.Text(t.T("collections.fortschritt", ("n", have), ("total", total)), "gb-alb-set-count"));
            card.Add(head);

            var tiles = Ux.Row("gb-alb-set-tiles");
            foreach (var entry in def.Entries) tiles.Add(EntryTile(def.Id, entry, c));
            card.Add(tiles);

            var foot = Ux.Row("gb-alb-set-foot");
            var reward = Ux.Row("gb-alb-set-reward");
            var thumb = Ux.ItemThumb(def.Furniture, "gift");
            thumb.AddToClassList("gb-alb-set-thumb");
            reward.Add(thumb);
            reward.Add(Ux.Text(t.T("collections.reward." + def.Id) + "  ·  +" + def.Coins + " ᴳ  ·  +" + CollectionsLogic.XpSetComplete + " XP", "gb-alb-set-reward-text"));
            foot.Add(reward);
            foot.Add(Ux.El("gb-grow"));
            Button claim;
            if (claimed)
            {
                claim = Ux.IconTextBtn("check", t.T("collections.claimed"), () => { }, "gb-btn-secondary", "gb-alb-set-claim");
                claim.SetEnabled(false);
            }
            else
            {
                claim = Ux.IconTextBtn("coin", t.T("collections.claim"), () => ClaimSet(def.Id), complete ? "gb-btn-primary" : "gb-btn-secondary", "gb-alb-set-claim");
                claim.SetEnabled(complete);
            }
            claim.name = "album-claim-" + def.Id;
            foot.Add(claim);
            card.Add(foot);
            return card;
        }

        private VisualElement EntryTile(string setId, string entry, JObject c)
        {
            var t = _host.Strings;
            var count = CollectionsLogic.CountOf(c, setId, entry);
            var owned = count >= 1;
            var slot = Ux.Col("gb-alb-slot");
            slot.name = "album-slot-" + setId + "-" + entry;
            var tile = Ux.El("gb-alb-slot-tile");
            if (owned)
            {
                tile.style.backgroundColor = TintOf(setId, entry);
                tile.Add(Ux.Icon(EntryIcons.TryGetValue(setId + "." + entry, out var icon) ? icon : SetIcons[setId], "gb-alb-slot-icon"));
                if (count > 1) tile.Add(Ux.Text("×" + count, "gb-alb-slot-n"));
            }
            else
            {
                tile.AddToClassList("gb-alb-slot-tile--missing");
                tile.Add(Ux.Text("?", "gb-alb-slot-q"));
            }
            slot.Add(tile);
            slot.Add(Ux.Text(owned ? t.T("collections.eintrag." + setId + "." + entry) : t.T("collections.unbekannt"), owned ? "gb-alb-slot-name" : "gb-alb-slot-name--missing"));
            return slot;
        }

        /// <summary>collections_view.gd tint_of: a pastel per entry from the hash of "set.entry" (same tile, same colour).</summary>
        public static Color TintOf(string setId, string entry)
        {
            var s = setId + "." + entry;
            long h = 0;
            foreach (var ch in s) h = (h * 31 + ch) % 4294967296L;
            return Color.HSVToRGB((h % 360) / 360f, 0.389f, 0.894f);
        }

        private void ClaimSet(string setId)
        {
            var store = _host.Store;
            var reward = CollectionsLogic.ApplyClaim(store, setId, store.Clock.NowMs(), store.Clock.LocalDay());
            if (reward == null) return;
            store.NotifySliceChanged("home");
            var t = _host.Strings;
            Debug.Log("[album] set " + setId + " claimed: +" + reward.Coins + " coins, " + reward.Furniture);
            Sfx.PlayUi("soft_win");
            _host.Ui.Confetti(80);
            _host.Ui.Toast(t.T("collections.claim_toast", ("name", t.T("collections.set." + setId)), ("coins", reward.Coins), ("item", t.T("collections.reward." + setId))), "gift", 4.5f);
            Refresh();
        }

        /// <summary>QA "sammlung fill &lt;set&gt; [n]": the first n entries (all by default) found once.</summary>
        public static string QaFill(GameHost host, string setId, int n)
        {
            var def = CollectionsLogic.GetSet(setId);
            if (def == null) return "no set " + setId;
            host.Store.Update(s =>
            {
                for (var i = 0; i < def.Entries.Length && i < n; i++) CollectionsLogic.AwardInState(s, setId, def.Entries[i], true);
            }, GameHost.TicksMs);
            host.Store.NotifySliceChanged("collections");
            var (have, total) = CollectionsLogic.SetProgress(CollectionsLogic.NormalizeSlice(host.Store.State["collections"]), setId);
            return have + "/" + total;
        }
    }
}
