using System;
using System.Globalization;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The postcard archive (port of postkarten_screen.gd): every card Gooby wrote on a trip, newest first, as a little
    /// postcard (the destination's colour band and stamp, a postmark, day and date, his handwritten line); the souvenir
    /// shelf with one slot per destination (visited ones lit, the Weltengooby badge once all nine are done); and the set
    /// bonus for 3/6/9 visited destinations, claimed once per tier with coins flying into the purse. Opened from Pia's
    /// archive card at the post office and by tapping the postcard wall or the souvenir shelf at home.
    /// </summary>
    public sealed class PostcardsScreen : UiScreen
    {
        public static readonly Color Tint = new Color(1f, 0.9f, 0.78f);

        /// <summary>Icon of a destination on its stamp and shelf slot.</summary>
        public static string DestIcon(string destId)
        {
            switch (destId)
            {
                case "beach": return "sun";
                case "meadowTrip": return "flower";
                case "bigCity": return "house";
                case "space": return "star";
                case "harbor": return "boat";
                case "spookGarden": return "zombie";
                case "bakery": return "food_cake";
                case "nightSky": return "moon";
                case "toyRoom": return "gamepad";
                default: return "globe";
            }
        }

        private readonly GameHost _host;
        private CounterPanel.Parts _ui;

        public PostcardsScreen(GameHost host)
        {
            _host = host;
            Id = "postkarten";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "mail", Tint, t.T("postkarten.titel"), "", () => _host.Ui.Pop());
            Refresh();
        }

        private static Color Accent(string destId) =>
            ArtLibrary.Hex(WallProps.DestAccent.TryGetValue(destId ?? "", out var hex) ? hex : "#DCCFBE");

        private void Refresh()
        {
            var t = _host.Strings;
            var state = _host.Store.State;
            _ui.Body.Clear();
            _ui.Coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));
            var col = Ux.Col("gb-pc-col");
            _ui.Body.Add(col);

            var archive = Postcards.ArchiveOf(state);
            _ui.Hint.text = t.T("postkarten.anzahl", ("n", archive.Count));
            if (archive.Count == 0) col.Add(CounterPanel.Empty(t.T("postkarten.titel"), t.T("postkarten.leer"), true));
            else
            {
                var grid = Ux.El("gb-pc-grid");
                grid.name = "postcards";
                for (var i = archive.Count - 1; i >= 0; i--)
                    if (archive[i] is JObject entry) grid.Add(Postcard(entry, archive.Count - 1 - i));
                col.Add(grid);
            }

            var visited = Postcards.SouvenirsVon(state);
            col.Add(CounterPanel.Section("globe", t.T("postkarten.souvenir.titel"),
                visited.Count == 0 ? t.T("postkarten.souvenir.leer") : t.T("postkarten.souvenir.fortschritt", ("n", visited.Count), ("gesamt", Postcards.DestIds.Length))));
            var shelf = Ux.El("gb-pc-shelf");
            shelf.name = "souvenir-shelf";
            if (Vacation.Weltengooby(Vacation.SliceOf(state)))
            {
                var badge = Ux.Row("gb-pc-badge");
                badge.name = "weltengooby";
                badge.Add(Ux.Icon("star"));
                badge.Add(Ux.Text(t.T("raumstation.weltengooby.badge")));
                col.Add(badge);
            }
            foreach (var id in Postcards.DestIds)
            {
                var seen = visited.Contains(id);
                var slot = Ux.Col("gb-pc-slot");
                slot.name = (seen ? "souvenir-" : "frei-") + id;
                if (!seen) slot.AddToClassList("gb-pc-slot--frei");
                var disc = Ux.El("gb-pc-slot-disc");
                disc.style.backgroundColor = seen ? Accent(id) : new Color(0.93f, 0.9f, 0.86f);
                disc.Add(seen ? Ux.Icon(DestIcon(id)) : Ux.Text("?", "gb-pc-slot-q"));
                slot.Add(disc);
                slot.Add(Ux.Text(seen ? t.T("travel.ziel." + id) : t.T("postkarten.souvenir.offen"), "gb-pc-slot-name"));
                shelf.Add(slot);
            }
            col.Add(shelf);

            col.Add(CounterPanel.Section("coin", t.T("postkarten.set.titel"), t.T("postkarten.set.hinweis")));
            foreach (var stufe in Postcards.SetStufenOf(state))
            {
                var row = Ux.Row("gb-pc-tier");
                row.name = "tier-" + stufe.N;
                if (stufe.Abgeholt) row.AddToClassList("gb-pc-tier--done");
                else if (stufe.Erreicht) row.AddToClassList("gb-pc-tier--ready");
                row.Add(Ux.Text(stufe.N.ToString(), "gb-pc-tier-num"));
                row.Add(Ux.Text(t.T("postkarten.set.stufe", ("n", stufe.N), ("bonus", stufe.Coins)), "gb-pc-tier-text"));
                if (stufe.Abgeholt) row.Add(Ux.Text(t.T("postkarten.set.abgeholt"), "gb-pc-tier-state"));
                else if (stufe.Erreicht)
                {
                    var n = stufe.N;
                    var claim = Ux.IconTextBtn("coin", t.T("postkarten.set.abholen", ("bonus", stufe.Coins)), null, "gb-btn-confirm", "gb-btn-sm");
                    claim.name = "claim-" + n;
                    claim.clicked += () => Claim(n, claim);
                    row.Add(claim);
                }
                else row.Add(Ux.Text(t.T("postkarten.set.noch_nicht"), "gb-pc-tier-state"));
                col.Add(row);
            }
        }

        private VisualElement Postcard(JObject entry, int index)
        {
            var t = _host.Strings;
            var dest = J.Str(entry["destId"]);
            var card = Ux.Col("gb-pc");
            card.name = "postcard-" + dest + "-" + J.Int(entry["dayIndex"]);
            card.AddToClassList(index % 3 == 0 ? "gb-pc--tilt-a" : index % 3 == 1 ? "gb-pc--tilt-b" : "gb-pc--tilt-c");
            var band = Ux.El("gb-pc-band");
            band.style.backgroundColor = Accent(dest);
            card.Add(band);
            var body = Ux.Row("gb-pc-body");
            var texts = Ux.Col("gb-pc-texts");
            texts.Add(Ux.Text(t.T("postkarten.von", ("ort", t.T("travel.ziel." + dest))), "gb-pc-from"));
            texts.Add(Ux.Text(t.T("postkarten.tag", ("tag", J.Int(entry["dayIndex"]))) + " · " + Date(J.Long(entry["atMs"])), "gb-pc-meta"));
            var key = Postcards.TextKey(entry);
            texts.Add(Ux.Text(t.HasKey(key) ? t.T(key) : "", "gb-pc-line"));
            body.Add(texts);
            var right = Ux.Col("gb-pc-right");
            var stamp = Ux.El("gb-pc-stamp");
            stamp.style.backgroundColor = Accent(dest);
            stamp.Add(Ux.Icon(DestIcon(dest)));
            right.Add(stamp);
            right.Add(Ux.Text("GOOBY\nPOST", "gb-pc-postmark"));
            body.Add(right);
            card.Add(body);
            return card;
        }

        private string Date(long ms)
        {
            if (ms <= 0) return "";
            var culture = _host.Settings != null && _host.Settings.Locale == "en" ? new CultureInfo("en-US") : new CultureInfo("de-DE");
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString(culture.Name == "en-US" ? "MMM d" : "d. MMM", culture);
        }

        /// <summary>One tier of the set bonus: the latch and the coins in one save update, coins fly into the purse.</summary>
        private void Claim(int n, VisualElement from)
        {
            var coins = 0;
            var now = _host.Store.Clock.NowMs();
            _host.Store.Update(s =>
            {
                coins = Postcards.ClaimSetBonus(s, n, now);
                if (coins > 0) Economy.Award(J.EnsureDict(s, "economy"), coins, "setBonus");
            }, GameHost.TicksMs);
            Debug.Log("[postkarten] claim " + n + " → " + coins);
            if (coins <= 0) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged("vacation");
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_coins");
            var target = Root.Q("sheet-coins");
            if (target != null && from.panel != null)
                _host.Ui.FlyIcons(from.worldBound.center, target, "coin", Mathf.Clamp(coins / 50, 3, 10));
            _host.Ui.Toast(_host.Strings.T("postkarten.set.erhalten", ("bonus", coins)), "coin");
            Refresh();
        }
    }
}
