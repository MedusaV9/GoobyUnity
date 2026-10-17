using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The sticker album (port of album_screen.gd): a rail of the 23 pages (title, n/N, a pink dot for unseen
    /// stickers, a gold tick for a complete set), the page's progress with its set reward, and the page's stickers as
    /// cards — the art when found (rarity strip, NEU until looked at), a soft silhouette and "???" when not. A tap opens
    /// the sticker: big art, rarity, the flavour line, or how to get it (plus the swap tip) while it is missing; opening
    /// a found sticker marks it seen (<c>stickers.seen</c>, as in Godot). Set rewards are paid by the StickerService.
    /// </summary>
    public sealed partial class AlbumScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly string _startPage;
        private string _page;
        private VisualElement _rail, _grid, _pageHead, _detail;
        private Label _count;

        private static readonly Dictionary<string, Color> RarityColors = new Dictionary<string, Color>
        {
            ["haeufig"] = new Color(0.92f, 0.85f, 0.78f),
            ["selten"] = new Color(0.50f, 0.72f, 1f),
            ["episch"] = new Color(1f, 0.79f, 0.30f),
            ["geheim"] = new Color(0.72f, 0.62f, 0.95f),
        };

        public AlbumScreen(GameHost host, string page = null)
        {
            _host = host;
            _startPage = page;
            Id = "album";
        }

        private JObject State => _host.Store.State;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var sheetStyle = Resources.Load<StyleSheet>("gooby-ui/Theme/gooby-album");
            if (sheetStyle != null) root.styleSheets.Add(sheetStyle);
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var sheet = Ux.El("gb-sheet", "gb-win-sheet", "gb-alb-sheet");
            sheet.style.left = 90; sheet.style.right = 90;
            sheet.style.maxHeight = Length.Percent(86);
            var head = CounterPanel.Head("star", new Color(1f, 0.93f, 0.72f), t.T("album.titel"), "", () => _host.Ui.Pop());
            _count = head.Title;
            sheet.Add(head.Row);

            var body = Ux.Col("gb-win-body");
            var railScroll = new ScrollView(ScrollViewMode.Horizontal) { name = "album-rail" };
            railScroll.AddToClassList("gb-alb-rail-scroll");
            railScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _rail = Ux.Row("gb-alb-rail");
            railScroll.Add(_rail);
            body.Add(railScroll);
            _pageHead = Ux.Row("gb-alb-pagehead");
            body.Add(_pageHead);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _grid = Ux.El("gb-alb-grid");
            scroll.Add(_grid);
            body.Add(scroll);
            var foot = Ux.Row("gb-win-foot");
            foot.Add(Ux.Icon("friends"));
            var tip = Ux.Text(t.T("album.tausch_hinweis"));
            tip.style.whiteSpace = WhiteSpace.Normal;
            tip.style.flexShrink = 1;
            foot.Add(tip);
            body.Add(foot);
            sheet.Add(body);
            root.Add(sheet);

            var pages = StickerCatalog.Pages();
            _page = _startPage == CollectionsPage || (_startPage != null && pages.Any(p => p.Id == _startPage)) ? _startPage : FirstInterestingPage(pages);
            Refresh();
        }

        /// <summary>A set reward waiting, else the first page with an unseen sticker, else the first one not complete yet, else the first.</summary>
        private string FirstInterestingPage(IReadOnlyList<StickerPage> pages)
        {
            if (AnySetClaimable()) return CollectionsPage;
            var all = StickerCatalog.All();
            foreach (var p in pages) if (Unseen(p.Id, all) > 0) return p.Id;
            foreach (var p in pages)
            {
                var (n, total) = StickerRules.PageProgress(State, all, p.Id);
                if (n < total) return p.Id;
            }
            return pages.Count > 0 ? pages[0].Id : null;
        }

        private bool Seen(string id) => J.Path(State, "stickers", "seen") is JObject seen && seen.ContainsKey(id);

        private int Unseen(string pageId, IEnumerable<StickerDef> all) =>
            all.Count(d => d.Page == pageId && StickerRules.IsUnlocked(State, d.Id) && !Seen(d.Id));

        private void Refresh()
        {
            var t = _host.Strings;
            var all = StickerCatalog.All();
            _count.text = t.T("album.titel") + "  ·  " + t.T("album.zaehler", ("n", StickerRules.UnlockedCount(State, all)), ("total", StickerCatalog.RegularCount(all)));
            BuildRail(all);
            BuildPage(all);
        }

        private void BuildRail(IReadOnlyList<StickerDef> all)
        {
            _rail.Clear();
            AddCollectionsChip();
            foreach (var p in StickerCatalog.Pages())
            {
                var page = p;
                var (n, total) = StickerRules.PageProgress(State, all, p.Id);
                var chip = new Button(() => { if (_page == page.Id) return; _page = page.Id; Sfx.PlayUi("soft_chip"); Refresh(); }) { text = "", name = "album-page-" + p.Id };
                chip.ClearClassList();
                chip.AddToClassList("gb-alb-chip");
                chip.EnableInClassList("gb-alb-chip--active", p.Id == _page);
                chip.EnableInClassList("gb-alb-chip--done", total > 0 && n >= total);
                if (ColorUtility.TryParseHtmlString(p.Tint, out var tint)) chip.style.backgroundColor = p.Id == _page ? tint : Color.Lerp(tint, Color.white, 0.55f);
                chip.Add(Ux.Text(p.TitleDe, "gb-alb-chip-title"));
                chip.Add(Ux.Text(total > 0 && n >= total ? "✓" : n + "/" + total, "gb-alb-chip-count"));
                if (Unseen(p.Id, all) > 0) chip.Add(Ux.El("gb-alb-chip-dot"));
                _rail.Add(chip);
            }
        }

        private void BuildPage(IReadOnlyList<StickerDef> all)
        {
            var t = _host.Strings;
            _pageHead.Clear();
            _grid.Clear();
            _grid.RemoveFromClassList("gb-alb-grid--sets");
            if (_page == CollectionsPage) { BuildCollectionsPage(); return; }
            var page = StickerCatalog.Pages().FirstOrDefault(p => p.Id == _page);
            if (page == null) return;
            var (n, total) = StickerRules.PageProgress(State, all, page.Id);
            var title = Ux.Text(page.TitleDe, "gb-h3", "gb-alb-pagetitle");
            _pageHead.Add(title);
            var done = total > 0 && n >= total;
            var bar = Ux.Bar(total > 0 ? (float)n / total : 0f, out _, done ? "gb-bar-fill--sonne" : "gb-bar-fill--pfirsich");
            bar.AddToClassList("gb-alb-pagebar");
            _pageHead.Add(bar);
            _pageHead.Add(Ux.Text(done ? t.T("album.set_komplett") : t.T("album.set_fortschritt", ("n", n), ("total", total)), done ? "gb-alb-done" : "gb-alb-progress"));
            if (!done) _pageHead.Add(Ux.Text("+" + StickerService.SetRewardCoins + " ᴳ", "gb-alb-reward"));

            foreach (var def in all.Where(d => d.Page == page.Id)) _grid.Add(Card(def));
        }

        private VisualElement Card(StickerDef def)
        {
            var t = _host.Strings;
            var unlocked = StickerRules.IsUnlocked(State, def.Id);
            var card = new VisualElement { name = "album-sticker-" + def.Id };
            card.AddToClassList("gb-alb-card");
            card.EnableInClassList("gb-alb-card--locked", !unlocked);
            var strip = Ux.El("gb-alb-strip");
            strip.style.backgroundColor = RarityColors.TryGetValue(def.Rarity ?? "", out var rc) ? rc : RarityColors["haeufig"];
            card.Add(strip);
            var art = Ux.El("gb-alb-art");
            var tex = StickerTexture(def);
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            if (!unlocked)
            {
                art.style.unityBackgroundImageTintColor = new Color(0.42f, 0.31f, 0.23f, 0.16f);
                art.Add(Ux.Text("?", "gb-alb-q"));
            }
            card.Add(art);
            card.Add(Ux.Text(unlocked || !def.Secret ? (unlocked ? def.NameDe : t.T("album.unbekannt")) : t.T("album.unbekannt"), "gb-alb-name"));
            if (unlocked && !Seen(def.Id)) card.Add(Ux.Text(t.T("album.neu"), "gb-alb-new"));
            card.RegisterCallback<ClickEvent>(_ => OpenDetail(def));
            return card;
        }

        private static Texture2D StickerTexture(StickerDef def)
        {
            if (string.IsNullOrEmpty(def.Image)) return null;
            var name = System.IO.Path.GetFileNameWithoutExtension(def.Image);
            return Resources.Load<Texture2D>("gooby/content/stickers/assets/" + name);
        }

        private void OpenDetail(StickerDef def)
        {
            var t = _host.Strings;
            _detail?.RemoveFromHierarchy();
            var unlocked = StickerRules.IsUnlocked(State, def.Id);
            Sfx.PlayUi(unlocked ? "soft_sticker" : "soft_open");
            var wrap = new VisualElement { name = "album-detail", pickingMode = PickingMode.Position };
            Ux.Abs(wrap, 0, 0, 0, 0);
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => CloseDetail());
            wrap.Add(scrim);
            var card = Ux.El("gb-card", "gb-alb-detail");
            var art = Ux.El("gb-alb-detail-art");
            var tex = StickerTexture(def);
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            if (!unlocked) art.style.unityBackgroundImageTintColor = new Color(0.42f, 0.31f, 0.23f, 0.18f);
            card.Add(art);
            var name = Ux.Text(unlocked ? def.NameDe : t.T("album.unbekannt"), "gb-h2", "gb-alb-detail-name");
            card.Add(name);
            var rarity = Ux.Text(t.T("album.rarity_" + (def.Rarity ?? "haeufig")), "gb-alb-detail-rarity");
            rarity.style.backgroundColor = RarityColors.TryGetValue(def.Rarity ?? "", out var rc) ? rc : RarityColors["haeufig"];
            card.Add(rarity);
            if (unlocked)
            {
                var flavor = Ux.Text(def.FlavorDe, "gb-md", "gb-alb-detail-text");
                card.Add(flavor);
            }
            else
            {
                card.Add(Ux.Text(t.T("album.hint_label"), "gb-sm", "gb-bold", "gb-alb-detail-label"));
                card.Add(Ux.Text(def.Secret ? t.T("album.unbekannt") : def.HintDe, "gb-md", "gb-alb-detail-text"));
            }
            var ok = Ux.Btn(t.T("nl.allgemein.schliessen"), CloseDetail, "gb-btn-primary", "gb-alb-detail-ok");
            card.Add(ok);
            wrap.Add(card);
            Root.Add(wrap);
            _detail = wrap;
            if (unlocked && !Seen(def.Id))
            {
                _host.Store.Update(s => J.EnsureDict(J.EnsureDict(s, "stickers"), "seen")[def.Id] = true, GameHost.TicksMs);
                Refresh();
            }
        }

        private void CloseDetail()
        {
            if (_detail == null) return;
            Sfx.PlayUi("soft_close");
            _detail.RemoveFromHierarchy();
            _detail = null;
        }
    }
}
