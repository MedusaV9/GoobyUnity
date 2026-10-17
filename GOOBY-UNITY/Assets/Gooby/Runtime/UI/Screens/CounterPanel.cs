using System;
using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The shared window kit of the place counters and the HUD sheets (boards 06 "Fenster / Panels" and 10): scrim,
    /// cream window with a white title band (icon tile, title, subtitle, coin pill, round close), a body with a
    /// wrapping grid of ware cards (white tile, pastel thumb square, name, sub line, tag, coin price / action pill)
    /// and Gooby's empty state. Styles: gooby-screens.uss (gb-win-*, gb-ware-*, gb-empty-*); every screen built on it
    /// carries .gb-sheets on its root, which scopes the hover/press rules.
    /// </summary>
    internal static class CounterPanel
    {
        public sealed class Parts
        {
            /// <summary>The window; <see cref="Content"/> is its body column (tabs go in front of <see cref="Scroll"/>).</summary>
            public VisualElement Panel, Content, Scroll, Body;
            public Label Title, Hint, Coins;
        }

        public sealed class Header
        {
            public VisualElement Row;
            public Label Title, Sub, Coins;
        }

        /// <param name="fit">
        /// True: the window sits at the foot of the screen and is only as wide and tall as its content (up to the screen), so
        /// a shelf of three wares leaves the shop and its keeper in view (board 06 "Fenster / Panels"). False: the fixed
        /// full-size window, for layouts that need the height (split chat, logs).
        /// </param>
        public static Parts Build(VisualElement root, GameHost host, string icon, Color tint, string title, string hint, Action close, bool fit = true)
        {
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => close());
            root.Add(scrim);

            var panel = Ux.El("gb-win");
            var head = Head(icon, tint, title, hint, close, ShopLogic.Coins(host.Store));
            panel.Add(head.Row);
            var content = Ux.Col("gb-win-body");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            var body = Ux.El("gb-win-grid");
            scroll.Add(body);
            content.Add(scroll);
            panel.Add(content);
            if (fit)
            {
                var frame = Ux.El("gb-win-frame");
                frame.pickingMode = PickingMode.Ignore;
                panel.AddToClassList("gb-win--fit");
                frame.Add(panel);
                root.Add(frame);
                GrowOnly(frame, panel);
            }
            else
            {
                Ux.Abs(panel, 120, 40, 120, 40);
                root.Add(panel);
            }
            return new Parts { Panel = panel, Content = content, Scroll = scroll, Body = body, Title = head.Title, Hint = head.Sub, Coins = head.Coins };
        }

        /// <summary>
        /// A fitted window keeps the largest size it has had while open: a tab with fewer wares does not make it jump. A new
        /// screen size (rotation, window resize) lets it fit afresh.
        /// </summary>
        private static void GrowOnly(VisualElement frame, VisualElement panel)
        {
            float w = 0f, h = 0f;
            var frameSize = Vector2.zero;
            frame.RegisterCallback<GeometryChangedEvent>(e =>
            {
                var size = e.newRect.size;
                if (frameSize != Vector2.zero && (Mathf.Abs(size.x - frameSize.x) > 0.5f || Mathf.Abs(size.y - frameSize.y) > 0.5f))
                {
                    w = h = 0f;
                    panel.style.minWidth = StyleKeyword.Null;
                    panel.style.minHeight = StyleKeyword.Null;
                }
                frameSize = size;
            });
            panel.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > w + 0.5f) { w = e.newRect.width; panel.style.minWidth = w; }
                if (e.newRect.height > h + 0.5f) { h = e.newRect.height; panel.style.minHeight = h; }
            });
        }

        /// <summary>The title band: icon tile, title (one line, ellipsis), subtitle (wraps), optional coin pill, round close.</summary>
        public static Header Head(string icon, Color tint, string title, string sub, Action close, int? coins = null)
        {
            var h = new Header { Row = Ux.Row("gb-win-head") };
            h.Row.Add(Ux.IconTile(icon, tint));
            var texts = Ux.Col("gb-win-texts");
            h.Title = Ux.Text(title, "gb-win-title");
            texts.Add(h.Title);
            h.Sub = Ux.Text(sub ?? "", "gb-win-sub");
            texts.Add(h.Sub);
            h.Row.Add(texts);
            if (coins.HasValue)
            {
                var pill = Ux.Pill("coin", Ux.FormatNumber(coins.Value), out var coinsLabel);
                pill.AddToClassList("gb-win-coins");
                pill.name = "sheet-coins";
                h.Coins = coinsLabel;
                h.Row.Add(pill);
            }
            if (close != null)
            {
                var x = Ux.IconBtn("close", close, "gb-win-close");
                x.name = "sheet-close";
                h.Row.Add(x);
            }
            return h;
        }

        /// <summary>A shop card: pastel tile, name, optional sub line and tag, one action pill (the verb carries the price).</summary>
        public static VisualElement Card(string icon, Color tint, string name, string sub, string badge, string button, bool enabled, Action onClick, params string[] buttonClasses)
        {
            var card = Ware(icon, tint, name, sub, badge);
            if (button != null) card.Add(ActionBtn(button, onClick, enabled, buttonClasses));
            return card;
        }

        /// <summary>Ware card without actions: white tile, pastel thumb square with the icon, name, sub, tag, spacer.</summary>
        public static VisualElement Ware(string icon, Color tint, string name, string sub, string tag, params string[] classes) =>
            Ware(Ux.Icon(icon), tint, name, sub, tag, classes);

        public static VisualElement Ware(VisualElement thumbContent, Color tint, string name, string sub, string tag, params string[] classes)
        {
            var card = Ux.El("gb-ware");
            foreach (var c in classes) card.AddToClassList(c);
            var thumb = Ux.El("gb-ware-thumb");
            thumb.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 1f);
            thumb.pickingMode = PickingMode.Ignore;
            if (thumbContent != null)
            {
                thumbContent.pickingMode = PickingMode.Ignore;
                thumb.Add(thumbContent);
            }
            card.Add(thumb);
            if (!string.IsNullOrEmpty(tag)) card.Add(Tag(tag));
            var title = Ux.Text(name ?? "", "gb-ware-name");
            title.pickingMode = PickingMode.Ignore;
            card.Add(title);
            if (!string.IsNullOrEmpty(sub))
            {
                var s = Ux.Text(sub, "gb-ware-sub");
                s.pickingMode = PickingMode.Ignore;
                card.Add(s);
            }
            var spacer = Ux.El("gb-ware-spacer");
            spacer.pickingMode = PickingMode.Ignore;
            card.Add(spacer);
            return card;
        }

        /// <summary>Buy pill: coin + price as a child label (Button.text stays empty), pink when affordable.</summary>
        public static Button Buy(int price, Action onClick, bool enabled)
        {
            var b = Ux.IconTextBtn("coin", Ux.FormatNumber(price), onClick, enabled ? "gb-btn-primary" : "gb-btn-secondary", "gb-btn-sm", "gb-ware-btn");
            b.SetEnabled(enabled);
            return b;
        }

        /// <summary>Text pill (verb or status); default pink when enabled, sand when not. Always carries gb-btn-sm.</summary>
        public static Button ActionBtn(string label, Action onClick, bool enabled, params string[] classes)
        {
            var cls = new List<string>(classes != null && classes.Length > 0 ? classes : new[] { enabled ? "gb-btn-primary" : "gb-btn-secondary" });
            if (!cls.Contains("gb-btn-sm")) cls.Add("gb-btn-sm");
            cls.Add("gb-ware-btn");
            var b = Ux.Btn(label, onClick, cls.ToArray());
            b.SetEnabled(enabled);
            return b;
        }

        /// <summary>Coin + price pill for cards whose whole tile is the button (wardrobe, beds).</summary>
        public static VisualElement Price(int price)
        {
            var p = Ux.Row("gb-ware-price");
            p.pickingMode = PickingMode.Ignore;
            p.Add(Ux.Icon("coin"));
            var l = Ux.Text(Ux.FormatNumber(price));
            l.pickingMode = PickingMode.Ignore;
            p.Add(l);
            return p;
        }

        /// <summary>Small status pill (owned / worn / level gate).</summary>
        public static Label Status(string text, string variant = null)
        {
            var l = Ux.Text(text, "gb-ware-status");
            if (!string.IsNullOrEmpty(variant)) l.AddToClassList(variant);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>The corner tag on the thumb (stock, "Neu", count).</summary>
        public static Label Tag(string text, params string[] variants)
        {
            var l = Ux.Text(text, "gb-ware-tag");
            foreach (var v in variants) l.AddToClassList(v);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>Pseudo pill at the foot of a tap card (the card itself takes the click; lights up on hover).</summary>
        public static VisualElement Go(string text)
        {
            var go = Ux.El("gb-ware-go");
            go.pickingMode = PickingMode.Ignore;
            var l = Ux.Text(text);
            l.pickingMode = PickingMode.Ignore;
            go.Add(l);
            return go;
        }

        /// <summary>Gooby on a soft sun disc with a title and a hint — the empty basket, fridge, storage, shelf.</summary>
        public static VisualElement Empty(string title, string hint, bool row = false)
        {
            var e = Ux.El("gb-empty");
            e.name = "empty-state";
            if (row) e.AddToClassList("gb-empty--row");
            var art = Ux.El("gb-empty-art");
            art.Add(row ? Ux.Image("gooby_sitting", 100, 100) : Ux.Image("gooby_sitting", 128, 128));
            e.Add(art);
            var texts = Ux.Col("gb-empty-texts");
            texts.Add(Ux.Text(title ?? "", "gb-empty-title"));
            if (!string.IsNullOrEmpty(hint)) texts.Add(Ux.Text(hint, "gb-empty-hint"));
            e.Add(texts);
            return e;
        }

        /// <summary>Section head inside a sheet body: icon, bold label, optional hint.</summary>
        public static VisualElement Section(string icon, string title, string hint = null)
        {
            var row = Ux.Row("gb-win-section");
            row.Add(Ux.Icon(icon));
            row.Add(Ux.Text(title));
            if (!string.IsNullOrEmpty(hint)) row.Add(Ux.Text(hint, "gb-win-section-hint"));
            return row;
        }

        /// <summary>Fridge food with its own icon (board 06 item icons); everything else keeps the category glyph.</summary>
        public static string FoodIcon(string id, string fallback) => id != null && Foods.TryGetValue(id, out var icon) ? icon : fallback;

        /// <summary>A shop ware's own picture (toys, chemist's and pharmacy goods, building material), else as <see cref="FoodIcon"/>.</summary>
        public static string WareIcon(string id, string fallback) => id != null && Wares.TryGetValue(id, out var icon) ? icon : FoodIcon(id, fallback);

        private static readonly Dictionary<string, string> Wares = new Dictionary<string, string>
        {
            { "kamera", "camera" }, { "flummi", "ball_bouncy" }, { "poster_gooby", "poster" }, { "knallbonbon", "cracker" },
            { "quietschhammer", "squeaky_hammer" }, { "stickerheft", "sticker_book" }, { "pluesch_moehre", "plush_carrot" },
            { "gummiente", "rubber_duck" }, { "riesenpuzzle", "puzzle" }, { "wasserpistole", "water_pistol" }, { "sammelfigur", "figurine" },
            { "lavalampe", "lava_lamp" }, { "diskokugel", "disco_ball" },
            { "zahnbuerste_standard", "toothbrush" }, { "zahnbuerste_flausch", "toothbrush" }, { "zahnbuerste_turbo", "toothbrush" },
            { "goobyman_pflaster", "bandage" }, { "pflaster", "bandage" }, { "schlafmaske", "sleep_mask" },
            { "hustensaft", "syrup" }, { "gooby_tropfen", "dropper" },
            { "bretter", "mat_holz" }, { "naegel", "mat_naegel" }, { "eisen", "mat_eisen" }, { "stoecke", "mat_stock" },
            { "farbe", "bucket" }, { "saatgut", "mat_saatgut" },
        };

        private static readonly Dictionary<string, string> Foods = new Dictionary<string, string>
        {
            { "carrot", "food_carrot" }, { "weltraumMoehre", "food_carrot" }, { "apple", "food_apple" }, { "banana", "food_banana" },
            { "tomato", "food_tomato" }, { "tomate", "food_tomato" }, { "strawberry", "food_strawberry" }, { "watermelon", "food_watermelon" },
            { "melone", "food_watermelon" }, { "cheese", "food_cheese" }, { "burger", "food_burger" }, { "burger_bau", "food_burger" },
            { "pizza", "food_pizza" }, { "cake", "food_cake" }, { "cupcake", "food_cupcake" }, { "cupcakePink", "food_cupcake" },
            { "muffin", "food_cupcake" }, { "donut-sprinkles", "food_donut" }, { "ice-cream", "food_icecream" }, { "softServe", "food_icecream" },
            { "sundae", "food_icecream" }, { "pilz", "food_mushroom" }, { "chili", "food_paprika" },
            { "cookie", "food_cookie" }, { "sandwich", "food_sandwich" },
            { "lollypop", "food_lollipop" }, { "grapes", "food_grapes" }, { "chocolate", "food_chocolate" }, { "bread", "food_bread" },
            { "corn", "food_corn" }, { "candy-bar", "food_candybar" }, { "croissant", "food_croissant" }, { "salad", "food_salad" },
            { "salat", "food_salad" }, { "fries", "food_fries" }, { "cinnamonRoll", "food_cinnamonroll" }, { "hot-dog", "food_hotdog" },
            { "pancakes", "food_pancakes" }, { "pumpkin", "food_pumpkin" }, { "nutella", "food_nutella" }, { "radish", "food_radish" },
            { "eggplant", "food_eggplant" }, { "ananas", "food_pineapple" }, { "cottonCandy", "food_cottoncandy" }, { "waffle", "food_waffle" },
        };

        /// <summary>A strong colour (car paint, stall tint) softened into a pastel thumb square.</summary>
        public static Color Pastel(Color c, float toWhite = 0.55f) => Color.Lerp(new Color(c.r, c.g, c.b, 1f), Color.white, toWhite);
    }
}
