using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Minigames;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Arcade sheet in the window look of boards 02/06: a cream window with a sun-washed title band (gamepad tile, title +
    /// game count, the stars chip, round close) over a scrolling grid of equally sized white game cards — the game's
    /// pastel tile (its tint also lines the card's lip), title, a best-score chip with a star (selected difficulty), the
    /// 3-star row of that best round, a pink "Neu" pill on the tile for never played; today's spotlight card wears a
    /// sun-yellow rim and ribbon. Games with a Unity host launch their host (MinigameHostScreen.For); the rest stay
    /// dimmed with a "Bald!" pill until their hosts land (the certified sims exist). Cards pop in one after another
    /// (frame-driven, see <see cref="Update"/>) and squash under the finger; back from a round only the card of the game
    /// just played pops again.
    /// </summary>
    public sealed class ArcadeScreen : UiScreen
    {
        private readonly GameHost _host;

        public override string SoundScene => "arcade";
        public static HashSet<string> Hosted => MinigameHostScreen.Hosted;

        /// <summary>A game (or another sheet) covered the arcade: its bests/stars may have changed → rebuild on the way back.</summary>
        private bool _stale;
        private bool _built;
        /// <summary>The game whose card opened the host on top (its card pops when the arcade shows again).</summary>
        private string _lastPlayed;

        private struct Entry { public VisualElement E; public double At; public int Frame; }
        private readonly List<Entry> _entries = new List<Entry>();
        // the bonus event's ribbon (ModifierEngine) ticks its countdown once a second
        private Label _modBadge;
        private ModifierEngine.Active _mod;
        private float _modAccum;

        public ArcadeScreen(GameHost host)
        {
            // EditMode tests may build the sheet without a boot: the running GameHost, else every path copes with none
            _host = host ?? GameHost.Instance;
            Id = "arcade";
        }

        /// <summary>Stable element names (flow tests, QA driver <c>click &lt;name&gt;</c>): a card per game is "arcade-&lt;gameId&gt;".</summary>
        public static class ElementNames
        {
            public const string Close = "arcade-close", CardPrefix = "arcade-", NewPrefix = "arcade-new-", SpotlightPrefix = "arcade-spotlight-";
            /// <summary>"♛ Bob · 1.240" on a card whose top score a friend holds (online only).</summary>
            public const string FriendTopPrefix = "arc-friend-top-";
        }

        public override void OnShown()
        {
            // Mount has just built the cards on the first show; afterwards best scores changed while a game was on top
            if (_stale && Root != null)
            {
                _stale = false;
                _entries.Clear();
                Root.Clear();
                Build(Root);
            }
        }

        public override void OnHidden()
        {
            _stale = true;
            // popped: no entry animation may outlive the sheet
            if (_host == null || _host.Ui == null || !_host.Ui.IsMounted(this)) _entries.Clear();
        }

        private void Close()
        {
            Sfx.PlayUi("soft_back");
            if (_host != null && _host.Ui != null && _host.Ui.Current == this) _host.Ui.Pop();
        }

        protected override void Build(VisualElement root)
        {
            if (_host == null || _host.Store == null || _host.Strings == null)
            {
                Debug.LogWarning("[arcade] no GameHost — the sheet stays empty");
                return;
            }
            var first = !_built;
            _built = true;
            var t = _host.Strings;
            var state = _host.Store.State;
            var motion = !Juice.ReducedMotion;
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);
            // board 06 "Fenster": the cream window, its title band on top, the card grid below
            var panel = Ux.El("gb-mgw", "gb-arc-window");
            Ux.Abs(panel, 120, 40, 120, 40);
            if (motion && first) Enter(panel, 0.0);
            // the games with a host first, the "Bald!" cards after them — each group in registry order (a stable
            // sort: the cards keep their places when the sheet rebuilds after a round)
            var games = MinigameRegistry.Playable();
            var order = new Dictionary<string, int>();
            for (var i = 0; i < games.Count; i++) order[games[i].Id] = i;
            games.Sort((a, b) =>
            {
                var byHost = Hosted.Contains(b.Id).CompareTo(Hosted.Contains(a.Id));
                return byHost != 0 ? byHost : order[a.Id].CompareTo(order[b.Id]);
            });
            // the band's chip: the stars the hosted cards show (each at its selected difficulty) out of three per game
            var starsEarned = 0;
            var starsMax = 0;
            foreach (var g in games)
            {
                if (!Hosted.Contains(g.Id)) continue;
                starsMax += 3;
                starsEarned += MinigameHostScreen.BestStars(state, g, FrameworkLogic.DifficultySliceOf(state, g.Id).Selected);
            }
            var played = 0;
            foreach (var g in games) if (J.Int(J.Path(state, "minigames", "plays", g.Id)) > 0) played++;
            var starsChip = Ux.El("gb-mgw-chip");
            starsChip.pickingMode = PickingMode.Ignore;
            starsChip.Add(Ux.Icon("star"));
            starsChip.Add(Ux.Text(t.T("mgp.arcade.stars", ("sterne", starsEarned), ("max", starsMax))));
            var progress = Ux.Text(t.T(starsEarned == 1 ? "mg.arcade.fortschritt.kopf_einzahl" : "mg.arcade.fortschritt.kopf",
                ("n", played), ("total", games.Count), ("sterne", starsEarned)), "gb-sm", "gb-mild");
            var close = Ux.IconBtn("close", Close, "gb-mgw-close");
            close.name = ElementNames.Close;
            panel.Add(MinigameHostScreen.FrameBand(Ux.IconTile("gamepad", SunTile), t.T("mg.arcade.title"),
                progress.text, SunWash, starsChip, close));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-arc-scroll");
            scroll.style.flexGrow = 1;
            // the labelled category rows of the Godot arcade (W20): a title with the row's played count, then its cards —
            // equally sized cards that fill the row, as many columns as fit (5 on the 1600 × 740 reference canvas)
            var content = Ux.Col();
            var rows = new Dictionary<string, VisualElement>();
            foreach (var rowId in ArcadeRows.Order)
            {
                var inRow = games.FindAll(g => ArcadeRows.RowOf(g.Id) == rowId);
                if (inRow.Count == 0 && rowId != "denken") continue;
                var head = Ux.Row("gb-arc-rowhead");
                head.name = "arcade-row-" + rowId;
                head.style.alignItems = Align.Center;
                head.style.marginTop = rows.Count == 0 ? 2 : 14;
                head.style.marginBottom = 8;
                head.style.paddingLeft = 6;
                var dot = Ux.El();
                dot.style.width = 12; dot.style.height = 12;
                dot.style.borderTopLeftRadius = 6; dot.style.borderTopRightRadius = 6; dot.style.borderBottomLeftRadius = 6; dot.style.borderBottomRightRadius = 6;
                dot.style.backgroundColor = inRow.Count > 0 ? MinigameHostScreen.GameTint(inRow[0].Id) : SunTile;
                dot.style.marginRight = 8;
                head.Add(dot);
                head.Add(Ux.Text(t.T("mg.arcade.reihe." + rowId), "gb-h3"));
                var rowPlayed = inRow.FindAll(g => J.Int(J.Path(state, "minigames", "plays", g.Id)) > 0).Count;
                var count = Ux.Text(rowPlayed + "/" + inRow.Count, "gb-sm", "gb-mild");
                count.style.marginLeft = 10;
                head.Add(count);
                content.Add(head);
                var rowGrid = Ux.El("gb-arc-grid");
                rowGrid.name = "arcade-grid-" + rowId;
                rowGrid.RegisterCallback<GeometryChangedEvent>(_ => FitCards(rowGrid));
                content.Add(rowGrid);
                rows[rowId] = rowGrid;
            }
            VisualElement GridFor(string gameId) => rows.TryGetValue(ArcadeRows.RowOf(gameId), out var g) ? g : rows[ArcadeRows.Order[0]];
            var day = _host.Store.Clock.LocalDay();
            var spotlight = ArcadeSpotlight.SpotlightId(games, day);
            _mod = ModifierEngine.ActiveEvent(state, _host.Store.Clock.NowMs());
            _modBadge = null;
            var index = 0;
            var practiceAdded = false;
            foreach (var meta in games)
            {
                var local = meta;
                var hosted = Hosted.Contains(meta.Id);
                if (!hosted && !practiceAdded)
                {
                    // "Schach üben" sits right after the playable cards, before the "Bald!" ones
                    practiceAdded = true;
                    rows["denken"].Add(ChessPracticeCard(t, motion && first, index++));
                }
                var isSpot = meta.Id == spotlight;
                var card = Ux.El("gb-arc-card", "gb-arcade-card");
                card.name = ElementNames.CardPrefix + meta.Id;
                card.style.width = _cardW;
                if (isSpot) card.AddToClassList("gb-arc-card--spot");
                if (!hosted) card.AddToClassList("gb-arc-card--soon");
                // a white card; the game's pastel fills the tile and lines the card's lip (the spotlight keeps its sun rim)
                else if (!isSpot) card.style.borderBottomColor = MinigameHostScreen.GameTint(meta.Id);
                // the painted cover of the game fills the card's top (the Godot arcade art); title, stars and best below
                card.AddToClassList("gb-arc-card--cover");
                var tileWrap = MinigameHostScreen.GameCover(meta.Id, "gb-arc-cover");
                tileWrap.pickingMode = PickingMode.Ignore;
                tileWrap.style.height = CoverHeight(_cardW);
                card.Add(tileWrap);
                var body = Ux.Col("gb-arc-body");
                body.pickingMode = PickingMode.Ignore;
                card.Add(body);
                var title = Ux.Text(t.HasKey(meta.TitleKey) ? t.T(meta.TitleKey) : meta.Id, "gb-arc-title");
                body.Add(title);
                // the best of the difficulty the pregame will open with, and the stars of that best round
                var mode = FrameworkLogic.DifficultySliceOf(state, meta.Id).Selected;
                var best = FrameworkLogic.BestForMode(state, meta.Id, mode);
                var metaRow = Ux.Row("gb-arc-meta");
                metaRow.pickingMode = PickingMode.Ignore;
                body.Add(metaRow);
                if (hosted) metaRow.Add(MinigameHostScreen.StarRow(MinigameHostScreen.BestStars(state, meta, mode), 20f));
                if (hosted || best > 0)
                {
                    var bestText = best > 0 ? t.T("mgp.arcade.best", ("best", Ux.FormatNumber(best))) : t.T("nl.arcade.no_best");
                    if (mode != "normal") bestText += " · " + t.T("mg.diff." + mode);
                    var chip = Ux.Row("gb-arc-best");
                    chip.Add(Ux.Text(bestText));
                    if (!hosted) chip.AddToClassList("gb-arc-dim");
                    metaRow.Add(chip);
                }
                if (hosted) SetFriendTop(body, meta.Id);
                if (isSpot)
                {
                    // the sun ribbon straddling the card's top rim
                    var spot = Ux.Text(t.T("mgp.arcade.spotlight", ("prozent", ArcadeSpotlight.BonusProzent)), "gb-arc-ribbon");
                    spot.name = ElementNames.SpotlightPrefix + meta.Id;
                    spot.pickingMode = PickingMode.Ignore;
                    card.Add(spot);
                }
                if (hosted && _mod != null && _mod.GameId == meta.Id)
                {
                    // the bonus event's ribbon on the bottom rim (the top rim belongs to the spotlight), in the event's colour
                    _modBadge = Ux.Text(ModBadgeText(), "gb-arc-ribbon");
                    _modBadge.name = "arcade-modifier-" + meta.Id;
                    _modBadge.pickingMode = PickingMode.Ignore;
                    _modBadge.style.top = StyleKeyword.Auto;
                    _modBadge.style.bottom = -14;
                    if (ColorUtility.TryParseHtmlString(_mod.Def.ColorHex, out var tint)) _modBadge.style.backgroundColor = tint;
                    card.Add(_modBadge);
                }
                if (hosted && J.Int(J.Path(state, "minigames", "plays", meta.Id)) <= 0)
                {
                    // the pink "Neu" pill hangs on the tile's corner
                    var neu = Ux.Text(t.T("mgp.arcade.new"), "gb-arc-neu");
                    neu.name = ElementNames.NewPrefix + meta.Id;
                    neu.pickingMode = PickingMode.Ignore;
                    tileWrap.Add(neu);
                }
                if (!hosted)
                {
                    // dimmed tile + title, the "Bald!" pill stays readable
                    tileWrap.AddToClassList("gb-arc-dim");
                    title.AddToClassList("gb-arc-dim");
                    card.Add(Ux.Text(t.T("mg.arcade.coming_soon"), "gb-arc-soon"));
                }
                else
                {
                    // squash under the finger; a drag that turns into a scroll (capture moves to the ScrollView) lets go too
                    card.RegisterCallback<PointerDownEvent>(_ => card.AddToClassList("gb-arcade-card--pressed"));
                    card.RegisterCallback<PointerUpEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
                    card.RegisterCallback<PointerLeaveEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
                    card.RegisterCallback<PointerCancelEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
                    card.RegisterCallback<PointerCaptureOutEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
                    card.RegisterCallback<ClickEvent>(_ =>
                    {
                        card.RemoveFromClassList("gb-arcade-card--pressed");
                        if (_host.Ui == null || _host.Ui.Current != this) return;   // a second tap while the host is already on its way up
                        var game = MinigameHostScreen.For(_host, local.Id);
                        if (game == null) return;
                        Sfx.PlayUi("soft_tap");
                        _lastPlayed = local.Id;
                        _host.Ui.Push(game);
                    });
                }
                // first show: the cards pop in one after another; back from a round only the card just played pops
                if (motion && (first || meta.Id == _lastPlayed)) Enter(card, first ? 0.06 + Math.Min(index, 24) * 0.022 : 0.05);
                index++;
                GridFor(meta.Id).Add(card);
            }
            if (!practiceAdded) rows["denken"].Add(ChessPracticeCard(t, motion && first, index));
            scroll.Add(content);
            panel.Add(scroll);
            root.Add(panel);
            // a rebuild keeps the scroll position, so the card just played stays where the finger left it
            if (!first && _scroll != null && _scroll.scrollOffset.y > 0.5f)
            {
                var keep = _scroll.scrollOffset;
                EventCallback<GeometryChangedEvent> restore = null;
                restore = _ =>
                {
                    scroll.contentContainer.UnregisterCallback(restore);
                    scroll.scrollOffset = keep;
                };
                scroll.contentContainer.RegisterCallback(restore);
            }
            _scroll = scroll;
            RequestFriendTops();
        }

        private ScrollView _scroll;

        // ── friends leaderboard line (arcade.js /api/arcade/best, online only) ──

        /// <summary>Pulls the top friend per game; the reply patches the cards in place (no rebuild, no card pop).</summary>
        private void RequestFriendTops()
        {
            var online = _host?.Online;
            if (online == null || !online.Client.IsOnline) return;
            online.RefreshArcadeBest(ok =>
            {
                if (!ok || Root == null || Root.panel == null) return;
                foreach (var id in Hosted)
                {
                    var card = Root.Q(ElementNames.CardPrefix + id);
                    if (card != null) SetFriendTop(card, id);
                }
            });
        }

        /// <summary>"♛ Bob · 1.240" under the stars when a friend holds the top score of the game (else no line).</summary>
        private void SetFriendTop(VisualElement card, string gameId)
        {
            var name = ElementNames.FriendTopPrefix + gameId;
            card.Q(name)?.RemoveFromHierarchy();
            var top = _host?.Online?.ArcadeFriendTop(gameId);
            if (top == null) return;
            card = card.Q(className: "gb-arc-body") ?? card;
            var line = Ux.Row("gb-arcf-top");
            line.name = name;
            line.pickingMode = PickingMode.Ignore;
            line.Add(Ux.Text("♛", "gb-arcf-crown"));
            line.Add(Ux.Text(_host.Strings.T("online.arcade.top", ("name", top.TopName), ("score", Ux.FormatNumber(top.TopScore))), "gb-arcf-text"));
            card.Add(line);
        }

        /// <summary>Mint tile of the "Schach üben" card (the board's light squares).</summary>
        private static readonly Color ChessTile = new Color(0.72f, 0.9f, 0.78f);

        /// <summary>
        /// "Schach üben" (<see cref="ChessScreen.Practice"/>): offline against the Gooby bot — not a registry minigame (no
        /// stars/coins), so the card opens the chess window with its strength/colour setup directly.
        /// </summary>
        private VisualElement ChessPracticeCard(Gooby.Core.I18n.I18n t, bool animate, int index)
        {
            var card = Ux.El("gb-arc-card", "gb-arcade-card", "gb-arc-card--chess", "gb-arc-card--cover");
            card.name = ChessScreen.Practice;
            card.style.width = _cardW;
            card.tooltip = t.T("chess.practice_hint");
            // no painted cover for chess: the board's mint square with the big knight in the cover's place
            var cover = Ux.El("gb-arc-cover");
            cover.pickingMode = PickingMode.Ignore;
            cover.style.height = CoverHeight(_cardW);
            cover.style.backgroundColor = ChessTile;
            cover.style.justifyContent = Justify.Center;
            cover.style.alignItems = Align.Center;
            var glyph = Ux.Text("♞", "gb-arc-chess-glyph");
            glyph.pickingMode = PickingMode.Ignore;
            glyph.style.fontSize = 84;
            cover.Add(glyph);
            card.Add(cover);
            var body = Ux.Col("gb-arc-body");
            body.pickingMode = PickingMode.Ignore;
            body.Add(Ux.Text(t.T("chess.practice"), "gb-arc-title"));
            var metaRow = Ux.Row("gb-arc-meta");
            var chip = Ux.Row("gb-arc-best");
            chip.Add(Ux.Text(t.T("chess.solo.bot_name")));
            metaRow.Add(chip);
            body.Add(metaRow);
            card.Add(body);
            card.RegisterCallback<PointerDownEvent>(_ => card.AddToClassList("gb-arcade-card--pressed"));
            card.RegisterCallback<PointerUpEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
            card.RegisterCallback<PointerLeaveEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
            card.RegisterCallback<PointerCancelEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
            card.RegisterCallback<PointerCaptureOutEvent>(_ => card.RemoveFromClassList("gb-arcade-card--pressed"));
            card.RegisterCallback<ClickEvent>(_ =>
            {
                card.RemoveFromClassList("gb-arcade-card--pressed");
                if (_host.Ui == null || _host.Ui.Current != this) return;
                Sfx.PlayUi("soft_open");
                _host.Ui.Push(ChessScreen.PracticeGame(_host));
            });
            if (animate) Enter(card, 0.06 + Math.Min(index, 24) * 0.022);
            return card;
        }

        /// <summary>Title band wash and tile (Sonnengelb #FFD166), the sand tile of a "Bald!" card.</summary>
        private static readonly Color SunWash = new Color(1f, 0.953f, 0.84f);
        private static readonly Color SunTile = new Color(1f, 0.82f, 0.4f);
        private static readonly Color SoonTile = new Color(0.93f, 0.9f, 0.86f);

        /// <summary>Narrowest card and the gap after each (USS .gb-arc-card margin-right) — the grid fits whole columns of these.</summary>
        private const float CardMinW = 236f, CardGap = 14f;
        /// <summary>The fitted card width, kept across rebuilds so the cards of a rebuilt sheet start at their final size.</summary>
        private float _cardW = 244f;

        /// <summary>
        /// Equal cards filling the grid's width: as many columns of at least <see cref="CardMinW"/> as fit, the spare width
        /// shared out (5 columns on the 1600 × 740 reference canvas, fewer on 4:3 tablets). Layout only.
        /// </summary>
        private void FitCards(VisualElement grid)
        {
            var w = grid.contentRect.width;
            if (float.IsNaN(w) || w < CardMinW + CardGap) return;
            var cols = Mathf.Clamp(Mathf.FloorToInt(w / (CardMinW + CardGap)), 1, 8);
            var cardW = Mathf.Floor(w / cols - CardGap);
            if (Mathf.Abs(cardW - _cardW) < 0.5f) return;
            _cardW = cardW;
            foreach (var c in grid.Children())
            {
                c.style.width = cardW;
                var cover = c.Q(className: "gb-arc-cover");
                if (cover != null) cover.style.height = CoverHeight(cardW);
            }
        }

        /// <summary>The cover keeps a 16:10 crop of the 4:3 art at any card width.</summary>
        private static float CoverHeight(float cardW) => Mathf.Round((cardW - 6f) * 0.62f);

        /// <summary>Drops the --pre state of entering cards at their beat (never in the frame they were queued, so the USS transition plays).</summary>
        private string ModBadgeText() =>
            _host.Strings.T("modifier.badge", ("name", _host.Strings.T(_mod.Def.NameKey)), ("rest", ModifierEngine.CountdownText(_mod.EndsAt, _host.Store.Clock.NowMs())));

        public override void Update()
        {
            if (_modBadge != null && (_modAccum += Time.unscaledDeltaTime) >= 1f)
            {
                _modAccum = 0f;
                if (_host.Store.Clock.NowMs() >= _mod.EndsAt) _modBadge.style.display = DisplayStyle.None;
                else _modBadge.text = ModBadgeText();
            }
            if (_entries.Count == 0) return;
            var now = Time.unscaledTimeAsDouble;
            var frame = Time.frameCount;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.At > now || e.Frame >= frame) continue;
                _entries.RemoveAt(i);
                e.E.RemoveFromClassList("gb-pop-in--pre");
            }
        }

        /// <summary>USS pop-in: the --pre state (small, transparent) drops after <paramref name="delaySec"/> so the transition plays.</summary>
        private void Enter(VisualElement e, double delaySec)
        {
            e.AddToClassList("gb-pop-in");
            e.AddToClassList("gb-pop-in--pre");
            _entries.Add(new Entry { E = e, At = Time.unscaledTimeAsDouble + Math.Max(0.0, delaySec), Frame = Time.frameCount });
        }
    }
}
