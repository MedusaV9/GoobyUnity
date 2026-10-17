using System;
using System.Collections.Generic;
using System.Text;
using Gooby.Core.Minigames;
using Gooby.Core.Net;
using Gooby.Core.Random;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Schach — with a friend over the board relay (<see cref="BoardClient"/> with <see cref="BoardClient.IsChess"/>,
    /// chess_scene.gd) or offline "Schach üben" against <see cref="ChessAi"/>. Window kit of board 06/07 like
    /// <see cref="BattleshipScreen"/>: a cream window with a white title band (glyph tile, opponent line, colour/move pill,
    /// Aufgeben/Verlassen), left the 8×8 board of pastel mint/cream squares with the pieces as big glyphs of the fallback
    /// font (own colour at the bottom), right the opponent card, the turn banner with the check chip, the move list (SAN
    /// with localized piece letters) and emotes + tomato. Tap a piece → its legal targets light up → tap a target
    /// (promotion picker for pawns on the last rank); the last move and a king in check are marked. The result card offers
    /// Revanche/Verlassen (practice: Neue Partie). All online state lives in <see cref="BoardClient"/> — the screen only
    /// draws it and forwards taps.
    /// Stable names: <c>ch-sq-&lt;file&gt;&lt;rank&gt;</c> (e.g. <c>ch-sq-e2</c>), <c>ch-promo-q|r|b|n</c>, <c>ch-surrender</c>,
    /// <c>ch-confirm-yes|no</c>, <c>ch-result-leave</c>, <c>ch-result-hide|show</c>, <c>ch-rematch</c>, <c>ch-solo-1|2|3</c>,
    /// <c>ch-solo-white|black</c>, <c>ch-solo-start</c>, <c>ch-emote-&lt;id&gt;</c>, <c>ch-tomato</c>, <c>ch-turn</c>, <c>ch-moves</c>
    /// (the entry button of the friends screen is <c>ch-practice</c>).
    /// </summary>
    public sealed class ChessScreen : UiScreen
    {
        public const string SquarePrefix = "ch-sq-", PromoPrefix = "ch-promo-", Surrender = "ch-surrender", ConfirmYes = "ch-confirm-yes",
            ConfirmNo = "ch-confirm-no", ResultLeave = "ch-result-leave", Rematch = "ch-rematch", Practice = "ch-practice",
            SoloPrefix = "ch-solo-", SoloStart = "ch-solo-start";

        /// <summary>Bot "thinking" pause before its move appears (the search itself takes a few ms).</summary>
        public const float BotDelay = 0.65f;
        public const int DefaultStrength = 2;

        // filled glyph per piece kind (index = ChessLogic.Pawn..King); both colours use the filled set, CSS colours them
        private const string Glyphs = " ♟♞♝♜♛♚";
        private static readonly Color TileMint = new Color(0.87f, 0.95f, 0.89f);
        private static readonly Color CaptureColor = new Color(0.95f, 0.43f, 0.43f);
        private static readonly Color MoveColor = new Color(0.37f, 0.72f, 0.52f);
        private static readonly Color HurtFlash = new Color(1f, 0.45f, 0.45f, 0.22f);

        private readonly GameHost _host;
        private readonly OnlineService _online;
        private readonly bool _practice;
        private readonly Juice _fx = new Juice();
        private readonly VisualElement[,] _cells = new VisualElement[8, 8];
        private readonly Label[,] _pieces = new Label[8, 8];
        private readonly Label[] _fileLabels = new Label[8], _rankLabels = new Label[8];
        private readonly Dictionary<string, Button> _emotes = new Dictionary<string, Button>();
        private readonly Button[] _soloStrength = new Button[4];
        private Button _soloWhite, _soloBlack;

        private VisualElement _win, _board, _banner, _fun, _confirm, _result, _resultCard, _resIcon, _promo, _solo, _bubble, _oppDot, _moveRows;
        private Label _sub, _pill, _oppKicker, _oppName, _oppLine, _bannerTitle, _bannerSub, _check, _pop, _bubbleLabel, _confirmText;
        private Label _resTitle, _resReason, _resMoves, _resLine, _leaveLabel, _rematchLabel, _movesEmpty;
        private Button _leave, _tomato, _resultShow, _rematch;
        private ScrollView _moveScroll;
        private string _resIconName = "star";

        private bool _dirty = true, _wired, _resultHidden, _greeted;
        private int _orientation; // colour at the bottom (0 = not laid out yet)
        private int _shownMoves = -1;
        private float _cellPx;
        private float _popLeft, _bubbleLeft, _tick;
        private float _peerDownUntil = -1f;

        // selection
        private int _sel = -1;
        private List<int> _targets = new List<int>();
        private int _promoFrom = -1, _promoTo = -1;

        // practice (offline vs ChessAi)
        private ChessLogic _pLogic;
        private ChessAi _ai;
        private int _pStrength = DefaultStrength;
        private int _pColor = ChessLogic.White;
        private bool _pSetup;
        private string _pResult = "";
        private bool _pIWon;
        private float _botAt = -1f;

        /// <summary>Online table for the running chess game of <see cref="OnlineService.Board"/>.</summary>
        public ChessScreen(GameHost host, OnlineService online)
        {
            _host = host;
            _online = online;
            Id = "chess";
            Opaque = true;
        }

        private ChessScreen(GameHost host, OnlineService online, int strength, int color)
        {
            _host = host;
            _online = online;
            _practice = true;
            Id = "chess-practice";
            Opaque = true;
            _pSetup = strength <= 0;
            if (strength > 0) _pStrength = Mathf.Clamp(strength, ChessAi.MinStrength, ChessAi.MaxStrength);
            if (color != 0) _pColor = color > 0 ? ChessLogic.White : ChessLogic.Black;
            if (!_pSetup) NewPractice();
        }

        /// <summary>"Schach üben": offline against the Gooby bot. strength ≤ 0 = show the setup card first.</summary>
        public static ChessScreen PracticeGame(GameHost host, int strength = 0, int color = ChessLogic.White) =>
            new ChessScreen(host, host.Online, strength, color);

        public bool IsPractice => _practice;

        private BoardClient Board => _online.Board;

        // ── unified view on the two modes (QA reads these too) ─────────────

        /// <summary>The position on screen (null while the practice setup is open or without a game).</summary>
        public ChessLogic Logic => _practice ? _pLogic : Board.Chess;
        public int PlayerColor => _practice ? _pColor : Board.MyColor;
        public bool Over => _practice ? _pResult.Length > 0 : Board.Finished;
        public string ResultId => _practice ? _pResult : Board.ResultId;
        public bool IWon => _practice ? _pIWon : Board.IWon;
        public bool IsDraw => _practice ? _pResult.Length > 0 && !_pIWon && _pResult != ChessLogic.ResultCheckmate && _pResult != "surrender" : Board.IsDraw;
        public bool SetupOpen => _practice && _pSetup;
        /// <summary>We may move now (our colour to move, nothing pending, table complete).</summary>
        public bool MyTurnNow
        {
            get
            {
                if (_practice) return _pLogic != null && !_pSetup && _pResult.Length == 0 && _pLogic.ToMove == _pColor;
                var b = Board;
                return b.IsChess && b.MyTurn && b.PeerInRoom && _online.Client.IsOnline;
            }
        }
        public bool PromoOpen => _promo != null && _promo.style.display == DisplayStyle.Flex;

        private string S(string key, params (string name, object value)[] args) => _host.Strings.T("chess." + key, args);
        private string B(string key, params (string name, object value)[] args) => _host.Strings.T("online.board." + key, args);

        // ── build ───────────────────────────────────────────────────────────

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-sheets");
            root.AddToClassList("gb-bs");
            root.AddToClassList("gb-ch");
            root.Add(Ux.El("gb-scrim", "gb-bs-scrim")); // no close on the scrim: a stray tap must never end a game

            _win = Ux.El("gb-win", "gb-bs-win", "gb-ch-win");
            _win.name = "ch-window";
            Ux.Abs(_win, 40, 18, 40, 18);
            root.Add(_win);
            _win.Add(BuildHead());

            var body = Ux.Row("gb-ch-body");
            body.RegisterCallback<GeometryChangedEvent>(e => Fit(e.newRect));
            _win.Add(body);
            body.Add(BuildBoard());
            body.Add(BuildSide());

            _promo = BuildPromo();
            _win.Add(_promo);
            _confirm = BuildConfirm();
            _win.Add(_confirm);
            _result = BuildResult();
            _win.Add(_result);
            if (_practice)
            {
                _solo = BuildSolo();
                _win.Add(_solo);
            }

            _fx.ShakeTarget = _win;
            root.Add(_fx.Layer);
            Refresh();
        }

        private VisualElement BuildHead()
        {
            var head = Ux.Row("gb-win-head");
            var tile = Ux.El("gb-icon-tile", "gb-ch-tile");
            tile.style.backgroundColor = TileMint;
            tile.Add(Ux.Text("♞", "gb-ch-tile-glyph"));
            head.Add(tile);
            var texts = Ux.Col("gb-win-texts");
            texts.Add(Ux.Text(_practice ? S("practice") : S("title"), "gb-win-title"));
            _sub = Ux.Text("", "gb-win-sub");
            texts.Add(_sub);
            head.Add(texts);

            var pill = Ux.Row("gb-bs-score", "gb-ch-pill");
            pill.name = "ch-pill";
            var dot = Ux.El("gb-ch-pill-dot");
            pill.Add(dot);
            _pill = Ux.Text("", "gb-nowrap");
            pill.Add(_pill);
            head.Add(pill);

            _leave = Ux.IconTextBtn("back", S("surrender"), OnLeavePressed, "gb-btn-secondary", "gb-btn-sm", "gb-bs-leave");
            _leave.name = Surrender;
            _leaveLabel = _leave.Q<Label>();
            head.Add(_leave);
            return head;
        }

        /// <summary>8×8 squares with rank numbers left and file letters below; names follow the square, not the slot.</summary>
        private VisualElement BuildBoard()
        {
            var card = Ux.Col("gb-ch-board-card");
            var frame = Ux.Row("gb-ch-frame");
            var ranks = Ux.Col("gb-ch-ranks");
            for (var r = 0; r < 8; r++)
            {
                _rankLabels[r] = Ux.Text("", "gb-ch-coord", "gb-ch-coord--rank");
                ranks.Add(_rankLabels[r]);
            }
            frame.Add(ranks);
            _board = Ux.Col("gb-ch-board");
            _board.name = "ch-board";
            for (var r = 0; r < 8; r++)
            {
                var row = Ux.Row("gb-ch-row");
                for (var c = 0; c < 8; c++)
                {
                    var rr = r;
                    var cc = c;
                    var cell = Ux.El("gb-ch-sq");
                    if (r == 0 && c == 0) cell.AddToClassList("gb-ch-sq--tl");
                    if (r == 0 && c == 7) cell.AddToClassList("gb-ch-sq--tr");
                    if (r == 7 && c == 0) cell.AddToClassList("gb-ch-sq--bl");
                    if (r == 7 && c == 7) cell.AddToClassList("gb-ch-sq--br");
                    var mark = Ux.El("gb-ch-mark");
                    mark.pickingMode = PickingMode.Ignore;
                    cell.Add(mark);
                    var piece = Ux.Text("", "gb-ch-piece");
                    piece.pickingMode = PickingMode.Ignore;
                    cell.Add(piece);
                    cell.RegisterCallback<ClickEvent>(_ => OnSquareTap(SquareAt(rr, cc)));
                    _cells[r, c] = cell;
                    _pieces[r, c] = piece;
                    row.Add(cell);
                }
                _board.Add(row);
            }
            frame.Add(_board);
            card.Add(frame);
            var files = Ux.Row("gb-ch-files");
            files.Add(Ux.El("gb-ch-coord--corner"));
            for (var c = 0; c < 8; c++)
            {
                _fileLabels[c] = Ux.Text("", "gb-ch-coord", "gb-ch-coord--file");
                files.Add(_fileLabels[c]);
            }
            card.Add(files);
            return card;
        }

        private VisualElement BuildSide()
        {
            var side = Ux.Col("gb-ch-side");

            var opp = Ux.Row("gb-bs-opp");
            opp.Add(Ux.Portrait(52));
            var texts = Ux.Col("gb-bs-opp-texts");
            _oppKicker = Ux.Text(_practice ? S("solo.kicker") : B("kicker"), "gb-bs-kicker");
            texts.Add(_oppKicker);
            _oppName = Ux.Text("", "gb-bs-opp-name");
            texts.Add(_oppName);
            var line = Ux.Row("gb-bs-opp-line");
            _oppDot = Ux.El("gb-status-dot");
            line.Add(_oppDot);
            _oppLine = Ux.Text("");
            line.Add(_oppLine);
            texts.Add(line);
            opp.Add(texts);
            _bubble = Ux.Row("gb-bs-bubble");
            _bubble.name = "ch-emote-bubble";
            _bubble.pickingMode = PickingMode.Ignore;
            _bubble.Add(Ux.Icon("sparkle"));
            _bubbleLabel = Ux.Text("");
            _bubble.Add(_bubbleLabel);
            _bubble.style.display = DisplayStyle.None;
            opp.Add(_bubble);
            side.Add(opp);

            _banner = Ux.Col("gb-bs-banner", "gb-ch-banner");
            _banner.name = "ch-banner";
            _check = Ux.Text(S("check"), "gb-ch-check");
            _check.name = "ch-check";
            _check.style.display = DisplayStyle.None;
            _banner.Add(_check);
            _bannerTitle = Ux.Text("", "gb-bs-banner-title");
            _bannerTitle.name = "ch-turn";
            _banner.Add(_bannerTitle);
            _bannerSub = Ux.Text("", "gb-bs-banner-sub");
            _banner.Add(_bannerSub);
            _resultShow = Ux.IconTextBtn("star", S("show_result"), () => { Sfx.PlayUi("soft_open"); _resultHidden = false; MarkDirty(); }, "gb-btn-primary", "gb-btn-sm");
            _resultShow.name = "ch-result-show";
            _banner.Add(_resultShow);
            side.Add(_banner);

            var moves = Ux.Col("gb-ch-moves-card");
            var mhead = Ux.Row("gb-bs-side-head");
            mhead.Add(Ux.Icon("book"));
            mhead.Add(Ux.Text(S("moves"), "gb-bs-side-title"));
            moves.Add(mhead);
            _moveScroll = new ScrollView(ScrollViewMode.Vertical);
            _moveScroll.name = "ch-moves";
            _moveScroll.AddToClassList("gb-ch-moves");
            _moveRows = Ux.Col("gb-ch-move-rows");
            _moveScroll.Add(_moveRows);
            _movesEmpty = Ux.Text(S("no_moves"), "gb-ch-moves-empty");
            _moveRows.Add(_movesEmpty);
            moves.Add(_moveScroll);
            side.Add(moves);

            _fun = Ux.Row("gb-bs-fun", "gb-ch-fun");
            foreach (var id in BoardEmotes.Ids)
            {
                var emoteId = id;
                var b = Ux.IconBtn(EmoteIcon(id), () => OnEmote(emoteId), "gb-btn-icon-sm", "gb-bs-emote");
                b.name = "ch-emote-" + id;
                b.tooltip = B("emote." + id);
                _emotes[id] = b;
                _fun.Add(b);
            }
            _tomato = Ux.IconTextBtn("food_tomato", B("tomate"), OnTomato, "gb-btn-primary", "gb-btn-sm", "gb-bs-tomato");
            _tomato.name = "ch-tomato";
            _fun.Add(_tomato);
            if (_practice) _fun.style.display = DisplayStyle.None;
            side.Add(_fun);

            _pop = Ux.Text("", "gb-bs-pop", "gb-ch-pop");
            _pop.name = "ch-pop";
            _pop.pickingMode = PickingMode.Ignore;
            _pop.style.display = DisplayStyle.None;
            side.Add(_pop);
            return side;
        }

        private VisualElement BuildPromo()
        {
            var layer = Ux.El("gb-bs-overlay", "gb-bs-confirm", "gb-ch-promo");
            layer.name = "ch-promo";
            var wrap = Ux.El("gb-ch-promo-wrap");
            wrap.Add(Ux.El("gb-shadow"));
            var card = Ux.Col("gb-bs-card");
            card.Add(Ux.Text(S("promo.title"), "gb-bs-card-title"));
            var row = Ux.Row("gb-ch-promo-row");
            foreach (var (letter, kind, key) in new[] { ("q", ChessLogic.Queen, "queen"), ("r", ChessLogic.Rook, "rook"), ("b", ChessLogic.Bishop, "bishop"), ("n", ChessLogic.Knight, "knight") })
            {
                var l = letter;
                var btn = new Button(() => OnPromo(l)) { text = "" };
                btn.ClearClassList();
                btn.AddToClassList("gb-ch-promo-btn");
                btn.name = PromoPrefix + letter;
                var glyph = Ux.Text(Glyphs[kind].ToString(), "gb-ch-promo-glyph");
                glyph.pickingMode = PickingMode.Ignore;
                btn.Add(glyph);
                var name = Ux.Text(S("promo." + key), "gb-ch-promo-name");
                name.pickingMode = PickingMode.Ignore;
                btn.Add(name);
                row.Add(btn);
            }
            card.Add(row);
            var btns = Ux.Row("gb-bs-card-btns");
            var cancel = Ux.IconTextBtn("close", B("weiterspielen"), () => { Sfx.PlayUi("soft_back"); ShowPromo(false); }, "gb-btn-secondary", "gb-btn-sm");
            cancel.name = "ch-promo-cancel";
            btns.Add(cancel);
            card.Add(btns);
            wrap.Add(card);
            layer.Add(wrap);
            layer.style.display = DisplayStyle.None;
            return layer;
        }

        private VisualElement BuildConfirm()
        {
            var layer = Ux.El("gb-bs-overlay", "gb-bs-confirm");
            layer.name = "ch-confirm";
            var wrap = Ux.El("gb-bs-confirm-wrap");
            wrap.Add(Ux.El("gb-shadow"));
            var card = Ux.Col("gb-bs-card");
            card.Add(Ux.Text(S("surrender_title"), "gb-bs-card-title"));
            _confirmText = Ux.Text("", "gb-bs-card-text");
            card.Add(_confirmText);
            var btns = Ux.Row("gb-bs-card-btns");
            var no = Ux.IconTextBtn("play", S("keep_playing"), () => { Sfx.PlayUi("soft_back"); ShowConfirm(false); }, "gb-btn-confirm", "gb-btn-sm");
            no.name = ConfirmNo;
            btns.Add(no);
            var yes = Ux.IconTextBtn("back", S("surrender"), OnConfirmSurrender, "gb-btn-cancel", "gb-btn-sm");
            yes.name = ConfirmYes;
            btns.Add(yes);
            card.Add(btns);
            wrap.Add(card);
            layer.Add(wrap);
            layer.style.display = DisplayStyle.None;
            return layer;
        }

        private VisualElement BuildResult()
        {
            var layer = Ux.El("gb-bs-overlay", "gb-bs-result");
            layer.name = "ch-result";
            var wrap = Ux.El("gb-bs-confirm-wrap", "gb-ch-result-wrap");
            wrap.Add(Ux.El("gb-shadow"));
            _resultCard = Ux.Col("gb-bs-card", "gb-bs-result-card");
            _resIcon = Ux.El("gb-bs-result-icon");
            _resIcon.Add(Ux.Icon("star"));
            _resultCard.Add(_resIcon);
            _resTitle = Ux.Text("", "gb-bs-result-title");
            _resTitle.name = "ch-result-title";
            _resultCard.Add(_resTitle);
            _resReason = Ux.Text("", "gb-bs-result-reason");
            _resultCard.Add(_resReason);
            _resMoves = Ux.Text("", "gb-ch-result-moves");
            _resultCard.Add(_resMoves);
            _resLine = Ux.Text("", "gb-bs-result-line");
            _resLine.name = "ch-rematch-line";
            _resultCard.Add(_resLine);
            var btns = Ux.Row("gb-bs-card-btns");
            var boardBtn = Ux.IconTextBtn("grid", S("show_board"), () => { Sfx.PlayUi("soft_close"); _resultHidden = true; MarkDirty(); }, "gb-btn-secondary", "gb-btn-sm");
            boardBtn.name = "ch-result-hide";
            btns.Add(boardBtn);
            var leave = Ux.IconTextBtn("home", B("verlassen"), Close, "gb-btn-secondary", "gb-btn-sm");
            leave.name = ResultLeave;
            btns.Add(leave);
            _rematch = Ux.IconTextBtn("redo", _practice ? S("new_game") : S("rematch.button"), OnRematch, "gb-btn-primary", "gb-btn-sm");
            _rematch.name = Rematch;
            _rematchLabel = _rematch.Q<Label>();
            btns.Add(_rematch);
            _resultCard.Add(btns);
            wrap.Add(_resultCard);
            layer.Add(wrap);
            layer.style.display = DisplayStyle.None;
            return layer;
        }

        /// <summary>Practice setup: strength (Gemütlich/Munter/Knifflig) and colour, then "Los geht's!".</summary>
        private VisualElement BuildSolo()
        {
            var layer = Ux.El("gb-bs-overlay", "gb-bs-result", "gb-ch-solo");
            layer.name = "ch-solo";
            var wrap = Ux.El("gb-bs-confirm-wrap", "gb-ch-result-wrap");
            wrap.Add(Ux.El("gb-shadow"));
            var card = Ux.Col("gb-bs-card");
            card.Add(Ux.Text(S("solo.title"), "gb-bs-card-title"));
            card.Add(Ux.Text(S("solo.subtitle"), "gb-bs-card-text"));
            card.Add(Ux.Text(S("solo.strength"), "gb-ch-solo-label"));
            var strength = Ux.Row("gb-ch-solo-row");
            for (var i = 1; i <= 3; i++)
            {
                var level = i;
                var b = Ux.Btn(S("solo.strength" + i), () => { Sfx.PlayUi("soft_toggle"); _pStrength = level; MarkDirty(); }, "gb-btn-secondary", "gb-btn-sm", "gb-ch-solo-opt");
                b.name = SoloPrefix + i;
                _soloStrength[i] = b;
                strength.Add(b);
            }
            card.Add(strength);
            card.Add(Ux.Text(S("solo.color"), "gb-ch-solo-label"));
            var color = Ux.Row("gb-ch-solo-row");
            _soloWhite = Ux.Btn(S("solo.white"), () => { Sfx.PlayUi("soft_toggle"); _pColor = ChessLogic.White; MarkDirty(); }, "gb-btn-secondary", "gb-btn-sm", "gb-ch-solo-opt");
            _soloWhite.name = SoloPrefix + "white";
            color.Add(_soloWhite);
            _soloBlack = Ux.Btn(S("solo.black"), () => { Sfx.PlayUi("soft_toggle"); _pColor = ChessLogic.Black; MarkDirty(); }, "gb-btn-secondary", "gb-btn-sm", "gb-ch-solo-opt");
            _soloBlack.name = SoloPrefix + "black";
            color.Add(_soloBlack);
            card.Add(color);
            var btns = Ux.Row("gb-bs-card-btns");
            var leave = Ux.IconTextBtn("back", B("verlassen"), Close, "gb-btn-secondary", "gb-btn-sm");
            leave.name = "ch-solo-leave";
            btns.Add(leave);
            var start = Ux.IconTextBtn("play", S("solo.start"), () => { Sfx.PlayUi("soft_confirm"); NewPractice(); Sfx.Play("game_go"); MarkDirty(); }, "gb-btn-primary", "gb-btn-sm");
            start.name = SoloStart;
            btns.Add(start);
            card.Add(btns);
            wrap.Add(card);
            layer.Add(wrap);
            layer.style.display = DisplayStyle.None;
            return layer;
        }

        private static string EmoteIcon(string id)
        {
            switch (id)
            {
                case "dance": return "sparkle";
                case "angry": return "spiky";
                case "laugh": return "sun";
                case "sleep": return "moon";
                default: return "heart";
            }
        }

        /// <summary>Square size from the body: the board takes the height, the side column keeps ≥ 360 px.</summary>
        private void Fit(Rect body)
        {
            if (body.width <= 0f || body.height <= 0f) return;
            // vertical chrome around the 8 rows: body padding 26 + card padding 20 + card border 4 + board padding 8 +
            // board border 6 + file-letter row 22 (+ a little air) — 64 cut the a–h row off at 740 px
            var byHeight = (body.height - 104f) / 8f;
            var byWidth = (body.width - 420f) / 8f;
            var px = Mathf.Floor(Mathf.Clamp(Mathf.Min(byHeight, byWidth), 34f, 78f));
            if (Mathf.Approximately(px, _cellPx)) return;
            _cellPx = px;
            for (var r = 0; r < 8; r++)
            {
                for (var c = 0; c < 8; c++)
                {
                    _cells[r, c].style.width = px;
                    _cells[r, c].style.height = px;
                    _pieces[r, c].style.fontSize = Mathf.Round(px * 0.76f);
                }
                _rankLabels[r].style.height = px;
                _fileLabels[r].style.width = px;
            }
        }

        // ── lifecycle ───────────────────────────────────────────────────────

        public override void OnShown()
        {
            Wire(true);
            MarkDirty();
            if (_greeted) return;
            _greeted = true;
            if (_practice) return;
            if (!Board.HasGame) return;
            Pop(Board.MyColor == ChessLogic.White ? S("you_began") : S("they_begin", ("name", OppName())), "info");
            Sfx.Play("game_go");
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return; // only covered by a sheet — the game goes on
            Wire(false);
            // removed without "Verlassen" (PopToRoot by a visit …): leave the chess table cleanly
            if (!_practice && Board.HasGame && Board.IsChess) Board.Leave();
        }

        private void Wire(bool on)
        {
            if (_practice || on == _wired) return;
            _wired = on;
            var b = Board;
            if (on)
            {
                b.Changed += MarkDirty;
                b.GameStarted += OnGameStarted;
                b.GameResumed += OnResumed;
                b.PeerJoined += OnPeerJoined;
                b.OpponentMoved += OnOpponentMoved;
                b.OpponentEmote += OnOpponentEmote;
                b.TomatoIncoming += OnTomatoIncoming;
                b.TomatoRejected += OnTomatoRejected;
                b.SendRejected += OnRejected;
                b.GameOver += OnGameOver;
                b.RematchRequestedByOpponent += OnRematchAsked;
                b.RematchDeclined += OnRematchDeclined;
                b.PeerConnectionChanged += OnPeerConnection;
                _online.Changed += MarkDirty;
            }
            else
            {
                b.Changed -= MarkDirty;
                b.GameStarted -= OnGameStarted;
                b.GameResumed -= OnResumed;
                b.PeerJoined -= OnPeerJoined;
                b.OpponentMoved -= OnOpponentMoved;
                b.OpponentEmote -= OnOpponentEmote;
                b.TomatoIncoming -= OnTomatoIncoming;
                b.TomatoRejected -= OnTomatoRejected;
                b.SendRejected -= OnRejected;
                b.GameOver -= OnGameOver;
                b.RematchRequestedByOpponent -= OnRematchAsked;
                b.RematchDeclined -= OnRematchDeclined;
                b.PeerConnectionChanged -= OnPeerConnection;
                _online.Changed -= MarkDirty;
            }
        }

        private void MarkDirty() => _dirty = true;

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _fx.Tick(dt);
            if (_popLeft > 0f)
            {
                _popLeft -= dt;
                if (_popLeft <= 0f) _fx.FadeOut(_pop, 0.3f);
            }
            if (_bubbleLeft > 0f)
            {
                _bubbleLeft -= dt;
                if (_bubbleLeft <= 0f) _fx.FadeOut(_bubble, 0.3f);
            }
            _tick += dt;
            if (_tick >= 0.5f)
            {
                _tick = 0f;
                _dirty = true; // countdowns (peer down) and the connection line
            }
            if (_practice) BotTick();
            else if (!Board.HasGame || !Board.IsChess)
            {
                // the table is gone (left from elsewhere) or another game took over: close
                if (_host.Ui.Current == this) _host.Ui.Pop();
                return;
            }
            if (_dirty) Refresh();
        }

        // ── practice ────────────────────────────────────────────────────────

        private void NewPractice()
        {
            _pLogic = new ChessLogic();
            _ai = new ChessAi(new GoobyRng(Environment.TickCount));
            _pSetup = false;
            _pResult = "";
            _pIWon = false;
            _resultHidden = false;
            ClearSelection();
            _shownMoves = -1;
            _botAt = _pLogic.ToMove != _pColor ? Time.realtimeSinceStartup + BotDelay : -1f;
            MarkDirty();
        }

        private void BotTick()
        {
            if (_pLogic == null || _pSetup || _pResult.Length > 0 || _pLogic.ToMove == _pColor) return;
            if (_botAt < 0f) _botAt = Time.realtimeSinceStartup + BotDelay;
            if (Time.realtimeSinceStartup < _botAt) return;
            _botAt = -1f;
            var m = _ai.PickMove(_pLogic, _pStrength);
            if (m == 0) return;
            var capture = _pLogic.PieceOn(ChessLogic.MoveTo(m)) != 0;
            _pLogic.PlayMove(m);
            Debug.Log("[chess] bot " + ChessLogic.MoveToUci(m));
            AfterMove(m, mine: false, capture);
        }

        /// <summary>Practice bookkeeping after any move: result check, bot scheduling, effects.</summary>
        private void AfterMove(int m, bool mine, bool capture)
        {
            ClearSelection();
            if (_practice)
            {
                var res = _pLogic.Result();
                if (res.Length > 0)
                {
                    _pResult = res;
                    _pIWon = res == ChessLogic.ResultCheckmate && -_pLogic.ToMove == _pColor;
                    OnGameOver(_pIWon ? "me" : "", _pIWon, BoardEndReason.None);
                }
                else if (_pLogic.ToMove != _pColor) _botAt = Time.realtimeSinceStartup + BotDelay;
            }
            MoveFx(m, mine, capture);
            MarkDirty();
        }

        // ── drawing ─────────────────────────────────────────────────────────

        private string OppName()
        {
            if (_practice) return S("solo.bot_name");
            var b = Board;
            if (b.OpponentName.Length > 0) return b.OpponentName;
            var f = _online.Client.FindFriend(b.OpponentCode);
            return f != null && f.Name.Length > 0 ? f.Name : b.OpponentCode;
        }

        private string OppGooby() => _practice ? S("solo.bot_name") : Board.OpponentGoobyName.Length > 0 ? Board.OpponentGoobyName : "Gooby";

        /// <summary>Square shown at a display slot: own colour at the bottom.</summary>
        private int SquareAt(int row, int col) =>
            PlayerColor == ChessLogic.Black ? ChessLogic.Square(7 - col, row) : ChessLogic.Square(col, 7 - row);

        private void Refresh()
        {
            _dirty = false;
            if (_sub == null) return;
            var logic = Logic;
            var name = OppName();
            var gooby = OppGooby();
            var running = logic != null && !Over && !SetupOpen;

            if (_practice) _sub.text = S("solo.title") + " · " + S("solo.strength" + _pStrength);
            else _sub.text = Board.HasGame ? B("gegen", ("name", name), ("gooby", gooby)) : "";
            var colorText = PlayerColor == ChessLogic.White ? S("you_white") : S("you_black");
            _pill.text = logic != null ? colorText + " · " + S("move_no", ("n", logic.Fullmove)) : colorText;
            _pill.parent.EnableInClassList("gb-ch-pill--black", PlayerColor == ChessLogic.Black);
            _leaveLabel.text = running ? S("surrender") : B("verlassen");

            RefreshOpponent(name, gooby);
            RefreshBoard(logic);
            RefreshBanner(logic, name, gooby);
            RefreshMoves(logic);

            if (!_practice)
            {
                var b = Board;
                _fun.style.display = b.HasGame ? DisplayStyle.Flex : DisplayStyle.None;
                var canEmote = b.HasGame && _online.Client.IsOnline && b.PeerInRoom;
                foreach (var e in _emotes.Values) e.SetEnabled(canEmote);
                _tomato.SetEnabled(canEmote && b.CanThrowTomato);
            }
            if (_solo != null)
            {
                _solo.style.display = _pSetup ? DisplayStyle.Flex : DisplayStyle.None;
                for (var i = 1; i <= 3; i++) _soloStrength[i].EnableInClassList("gb-ch-solo-opt--on", _pStrength == i);
                _soloWhite.EnableInClassList("gb-ch-solo-opt--on", _pColor == ChessLogic.White);
                _soloBlack.EnableInClassList("gb-ch-solo-opt--on", _pColor == ChessLogic.Black);
            }
            if (PromoOpen && !MyTurnNow) ShowPromo(false);
            RefreshResult(name);
        }

        private void RefreshOpponent(string name, string gooby)
        {
            _oppName.text = name;
            if (_practice)
            {
                _oppDot.EnableInClassList("gb-status-dot--on", true);
                _oppLine.text = S("solo.ready", ("name", name)) + " · " + S("solo.strength" + _pStrength);
                return;
            }
            var b = Board;
            var peerHere = b.PeerInRoom && _online.Client.IsOnline;
            _oppDot.EnableInClassList("gb-status-dot--on", peerHere);
            _oppDot.EnableInClassList("gb-status-dot--wait", !peerHere && !b.OpponentGone);
            _oppDot.EnableInClassList("gb-status-dot--off", b.OpponentGone);
            var colour = b.MyColor == ChessLogic.White ? S("black") : S("white");
            _oppLine.text = b.OpponentGone ? B("peer_gegangen", ("name", name))
                : b.PeerDown ? B("peer_weg", ("name", name))
                : peerHere ? B("peer_da", ("gooby", gooby)) + " · " + colour
                : B("peer_wartet", ("gooby", gooby));
        }

        private void RefreshBoard(ChessLogic logic)
        {
            var color = PlayerColor;
            if (color != _orientation)
            {
                _orientation = color;
                for (var r = 0; r < 8; r++)
                    for (var c = 0; c < 8; c++)
                    {
                        var sq = SquareAt(r, c);
                        _cells[r, c].name = SquarePrefix + ChessLogic.SquareName(sq);
                        var dark = (ChessLogic.FileOf(sq) + ChessLogic.RankOf(sq)) % 2 == 0;
                        _cells[r, c].EnableInClassList("gb-ch-sq--dark", dark);
                        _cells[r, c].EnableInClassList("gb-ch-sq--light", !dark);
                    }
                for (var i = 0; i < 8; i++)
                {
                    _rankLabels[i].text = ((char)('1' + ChessLogic.RankOf(SquareAt(i, 0)))).ToString();
                    _fileLabels[i].text = ((char)('a' + ChessLogic.FileOf(SquareAt(7, i)))).ToString();
                }
            }
            var last = logic?.LastMove ?? 0;
            var lastFrom = last != 0 ? ChessLogic.MoveFrom(last) : -1;
            var lastTo = last != 0 ? ChessLogic.MoveTo(last) : -1;
            var checkSq = logic != null && logic.InCheck() ? logic.KingSquare(logic.ToMove) : -1;
            var canAct = MyTurnNow;
            _board.EnableInClassList("gb-ch-board--mine", canAct);
            for (var r = 0; r < 8; r++)
            {
                for (var c = 0; c < 8; c++)
                {
                    var sq = SquareAt(r, c);
                    var cell = _cells[r, c];
                    var piece = logic?.PieceOn(sq) ?? 0;
                    var label = _pieces[r, c];
                    var glyph = piece == 0 ? "" : Glyphs[Math.Abs(piece)].ToString();
                    if (label.text != glyph) label.text = glyph;
                    label.EnableInClassList("gb-ch-piece--w", piece > 0);
                    label.EnableInClassList("gb-ch-piece--b", piece < 0);
                    var target = false;
                    foreach (var m in _targets) if (ChessLogic.MoveTo(m) == sq) { target = true; break; }
                    cell.EnableInClassList("gb-ch-sq--sel", sq == _sel);
                    cell.EnableInClassList("gb-ch-sq--target", target && piece == 0);
                    cell.EnableInClassList("gb-ch-sq--capture", target && piece != 0);
                    cell.EnableInClassList("gb-ch-sq--last", sq == lastFrom || sq == lastTo);
                    cell.EnableInClassList("gb-ch-sq--check", sq == checkSq);
                    cell.EnableInClassList("gb-ch-sq--own", canAct && piece != 0 && Math.Sign(piece) == PlayerColor);
                }
            }
        }

        private void RefreshBanner(ChessLogic logic, string name, string gooby)
        {
            string title, sub, mode;
            var inCheck = logic != null && !Over && logic.InCheck();
            if (SetupOpen)
            {
                title = S("solo.title");
                sub = S("solo.subtitle");
                mode = "wait";
            }
            else if (Over)
            {
                title = ResultTitle();
                sub = ReasonText(name);
                mode = IWon ? "win" : IsDraw ? "wait" : "lose";
            }
            else if (_practice)
            {
                if (MyTurnNow)
                {
                    title = S("your_turn");
                    sub = inCheck ? S("check_you") : _sel >= 0 ? S("hint_target") : S("hint_pick");
                    mode = "mine";
                }
                else
                {
                    title = S("thinking", ("name", name));
                    sub = inCheck ? S("check_them", ("name", name)) : S("hint_wait", ("gooby", gooby));
                    mode = "theirs";
                }
            }
            else
            {
                var b = Board;
                if (!_online.Client.IsOnline)
                {
                    title = B("verbinde");
                    sub = B("verbinde_hint");
                    mode = "wait";
                }
                else if (b.PeerDown)
                {
                    var left = _peerDownUntil > 0f ? Mathf.Max(0, Mathf.CeilToInt(_peerDownUntil - Time.realtimeSinceStartup)) : 0;
                    title = B("peer_weg", ("name", name));
                    sub = left > 0 ? B("peer_weg_hint", ("s", left)) : B("peer_weg_hint_kurz");
                    mode = "wait";
                }
                else if (!b.PeerInRoom)
                {
                    title = B("warte_peer", ("name", name));
                    sub = B("warte_peer_hint", ("gooby", gooby));
                    mode = "wait";
                }
                else if (b.AwaitingResult)
                {
                    var moves = logic?.SanMoves;
                    title = S("sending", ("move", moves != null && moves.Count > 0 ? LocalSan(moves[moves.Count - 1]) : "…"));
                    sub = S("hint_wait", ("gooby", gooby));
                    mode = "mine";
                }
                else if (b.MyTurn)
                {
                    title = S("your_turn");
                    sub = inCheck ? S("check_you") : _sel >= 0 ? S("hint_target") : S("hint_pick");
                    mode = "mine";
                }
                else
                {
                    title = S("their_turn", ("name", name));
                    sub = inCheck ? S("check_them", ("name", name)) : S("hint_wait", ("gooby", gooby));
                    mode = "theirs";
                }
            }
            _bannerTitle.text = title;
            _bannerSub.text = sub;
            foreach (var m in new[] { "mine", "theirs", "wait", "win", "lose" }) _banner.EnableInClassList("gb-bs-banner--" + m, m == mode);
            _check.style.display = inCheck ? DisplayStyle.Flex : DisplayStyle.None;
            _resultShow.style.display = Over && _resultHidden ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>"1. e4 e5" rows, rebuilt when the move count changes; newest at the bottom.</summary>
        private void RefreshMoves(ChessLogic logic)
        {
            var san = logic?.SanMoves;
            var count = san?.Count ?? 0;
            if (count == _shownMoves) return;
            _shownMoves = count;
            _moveRows.Clear();
            if (count == 0)
            {
                _moveRows.Add(_movesEmpty);
                return;
            }
            // both modes start from the initial position: white opens every row
            Label lastLabel = null;
            for (var i = 0; i < count; i += 2)
            {
                var number = i / 2 + 1;
                var row = Ux.Row("gb-ch-move-row");
                row.Add(Ux.Text(number + ".", "gb-ch-move-no"));
                lastLabel = Ux.Text(LocalSan(san[i]), "gb-ch-move");
                row.Add(lastLabel);
                if (i + 1 < count)
                {
                    lastLabel = Ux.Text(LocalSan(san[i + 1]), "gb-ch-move");
                    row.Add(lastLabel);
                }
                row.EnableInClassList("gb-ch-move-row--odd", number % 2 == 1);
                _moveRows.Add(row);
            }
            lastLabel?.AddToClassList("gb-ch-move--last");
            var newest = lastLabel;
            _moveScroll.schedule.Execute(() => { if (newest != null && newest.panel != null) _moveScroll.ScrollTo(newest); }).StartingIn(30);
        }

        /// <summary>SAN with the localized piece letters (chess.pieces.*: K D T L S in German).</summary>
        private string LocalSan(string san)
        {
            if (string.IsNullOrEmpty(san)) return "";
            var sb = new StringBuilder(san.Length);
            foreach (var ch in san)
            {
                switch (ch)
                {
                    case 'K': sb.Append(S("pieces.king")); break;
                    case 'Q': sb.Append(S("pieces.queen")); break;
                    case 'R': sb.Append(S("pieces.rook")); break;
                    case 'B': sb.Append(S("pieces.bishop")); break;
                    case 'N': sb.Append(S("pieces.knight")); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private string ResultTitle()
        {
            if (IsDraw) return S("draw");
            if (IWon) return S("win");
            if (!_practice && Board.EndReason == BoardEndReason.Expired) return B("vorbei");
            return S("lose");
        }

        private string ReasonText(string name)
        {
            var id = ResultId;
            switch (id)
            {
                case "surrender": return IWon ? B("grund.aufgabe_sieg", ("name", name)) : B("grund.aufgabe_niederlage");
                case "forfeit": return IWon ? B("grund.forfeit", ("name", name)) : B("grund.abgebrochen");
                case "":
                    return "";
                default:
                    return _host.Strings.HasKey("chess.reason." + id) ? S("reason." + id) : id;
            }
        }

        private void RefreshResult(string name)
        {
            var over = Over;
            var show = over && !_resultHidden;
            var was = _result.style.display == DisplayStyle.Flex;
            _result.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!over) return;
            var draw = IsDraw;
            _result.EnableInClassList("gb-bs-result--win", IWon);
            _result.EnableInClassList("gb-bs-result--lose", !IWon);
            var icon = IWon ? "star" : draw ? "sun" : ResultId == "expired" ? "cloud" : "heart";
            if (icon != _resIconName)
            {
                _resIconName = icon;
                _resIcon.Clear();
                _resIcon.Add(Ux.Icon(icon));
            }
            _resTitle.text = ResultTitle();
            _resReason.text = ReasonText(name);
            _resMoves.text = S("result_moves", ("n", Logic?.Moves.Count ?? 0));
            if (_practice)
            {
                _resLine.text = "";
                _rematchLabel.text = S("new_game");
                _rematch.SetEnabled(true);
            }
            else
            {
                var b = Board;
                var online = _online.Client.IsOnline;
                if (b.OpponentGone) _resLine.text = B("revanche_weg", ("name", name));
                else if (b.RematchMine) _resLine.text = S("rematch.waiting", ("name", name));
                else if (b.RematchTheirs) _resLine.text = S("rematch.incoming", ("name", name));
                else if (!online) _resLine.text = B("verbinde");
                else _resLine.text = "";
                _rematchLabel.text = b.RematchTheirs && !b.RematchMine ? B("revanche_annehmen") : S("rematch.button");
                _rematch.SetEnabled(online && !b.OpponentGone && !b.RematchMine && b.EndReason != BoardEndReason.Expired);
            }
            if (show && !was) _fx.ScalePop(_resultCard, 1.06f, 0.28f);
        }

        private void ShowConfirm(bool on)
        {
            _confirm.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            _confirmText.text = S("surrender_confirm", ("name", OppName()));
            _fx.ScalePop(_confirm, 1.04f, 0.2f);
        }

        private void ShowPromo(bool on)
        {
            _promo.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on)
            {
                _promoFrom = _promoTo = -1;
                return;
            }
            foreach (var btn in _promo.Query<Button>(className: "gb-ch-promo-btn").ToList())
            {
                var glyph = btn.Q<Label>(className: "gb-ch-promo-glyph");
                glyph?.EnableInClassList("gb-ch-piece--w", PlayerColor == ChessLogic.White);
                glyph?.EnableInClassList("gb-ch-piece--b", PlayerColor == ChessLogic.Black);
            }
            _fx.ScalePop(_promo, 1.04f, 0.2f);
        }

        private void Pop(string text, string kind)
        {
            _fx.CancelFade(_pop);
            _pop.text = text;
            foreach (var k in new[] { "hit", "miss", "sunk", "info" }) _pop.EnableInClassList("gb-bs-pop--" + k, k == kind);
            _pop.style.display = DisplayStyle.Flex;
            _pop.style.opacity = 1f;
            _fx.ScalePop(_pop, 1.35f, 0.3f);
            _popLeft = 1.4f;
        }

        private VisualElement CellOf(int sq)
        {
            for (var r = 0; r < 8; r++)
                for (var c = 0; c < 8; c++)
                    if (SquareAt(r, c) == sq) return _cells[r, c];
            return _board;
        }

        private static Vector2 Centre(VisualElement e) => e.worldBound.center;

        // ── taps ────────────────────────────────────────────────────────────

        private void ClearSelection()
        {
            _sel = -1;
            _targets = new List<int>();
            if (_promo != null && PromoOpen) ShowPromo(false);
        }

        private void OnSquareTap(int sq)
        {
            var logic = Logic;
            if (logic == null || Over || SetupOpen || PromoOpen) return;
            var piece = logic.PieceOn(sq);
            if (_sel >= 0)
            {
                var moves = new List<int>();
                foreach (var m in _targets) if (ChessLogic.MoveTo(m) == sq) moves.Add(m);
                if (moves.Count > 0)
                {
                    if (ChessLogic.MovePromo(moves[0]) != 0)
                    {
                        _promoFrom = _sel;
                        _promoTo = sq;
                        Sfx.PlayUi("soft_open");
                        ShowPromo(true);
                        return;
                    }
                    Commit(moves[0]);
                    return;
                }
            }
            if (piece != 0 && Math.Sign(piece) == PlayerColor)
            {
                if (!MyTurnNow)
                {
                    Sfx.PlayUi("soft_error");
                    _host.Ui.Toast(NotNowHint(), "grid", 1.8f);
                    return;
                }
                if (sq == _sel)
                {
                    ClearSelection();
                    Sfx.PlayUi("soft_toggle");
                    MarkDirty();
                    return;
                }
                _sel = sq;
                _targets = logic.LegalMovesFrom(sq);
                if (_targets.Count == 0) Sfx.PlayUi("soft_error");
                else Sfx.PlayUi("soft_tap");
                _fx.ScalePop(CellOf(sq), 1.08f, 0.16f);
                MarkDirty();
                return;
            }
            if (_sel >= 0)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(S("illegal"), "grid", 1.6f);
                ClearSelection();
                MarkDirty();
                return;
            }
            if (piece != 0)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(S("not_yours"), "grid", 1.6f);
            }
        }

        private string NotNowHint()
        {
            if (_practice) return S("thinking", ("name", OppName()));
            var b = Board;
            if (!_online.Client.IsOnline) return B("verbinde");
            if (!b.PeerInRoom) return B("warte_peer", ("name", OppName()));
            if (b.AwaitingResult) return S("hint_wait", ("gooby", OppGooby()));
            return S("their_turn", ("name", OppName()));
        }

        private void OnPromo(string letter)
        {
            if (_promoFrom < 0 || _promoTo < 0) return;
            var from = _promoFrom;
            var to = _promoTo;
            ShowPromo(false);
            var logic = Logic;
            if (logic == null) return;
            var m = logic.UciToMove(ChessLogic.SquareName(from) + ChessLogic.SquareName(to) + letter);
            if (m == 0) return;
            Commit(m);
        }

        private void Commit(int m)
        {
            var logic = Logic;
            var capture = logic.PieceOn(ChessLogic.MoveTo(m)) != 0 || (ChessLogic.MoveFlags(m) & ChessLogic.FlagEp) != 0;
            var uci = ChessLogic.MoveToUci(m);
            if (_practice)
            {
                if (!MyTurnNow || !_pLogic.PlayMove(m)) return;
                Debug.Log("[chess] move " + uci);
                AfterMove(m, mine: true, capture);
                return;
            }
            var err = Board.SendMove(uci);
            if (err.Length == 0)
            {
                Debug.Log("[chess] move " + uci);
                ClearSelection();
                MoveFx(m, mine: true, capture);
                MarkDirty();
                return;
            }
            ClearSelection();
            Sfx.PlayUi("soft_error");
            string hint;
            switch (err)
            {
                case BoardClient.ErrNotYourTurn: hint = NotNowHint(); break;
                case BoardClient.ErrWaitingPeer: hint = B("warte_peer", ("name", OppName())); break;
                case BoardClient.ErrIllegalMove: hint = S("illegal"); break;
                default: hint = _online.BoardErrorText(err); break;
            }
            _host.Ui.Toast(hint, "grid", 1.8f);
            MarkDirty();
        }

        /// <summary>Pop + sound for a move that is on the board now (check / capture / quiet).</summary>
        private void MoveFx(int m, bool mine, bool capture)
        {
            RefreshNow();
            var logic = Logic;
            var cell = CellOf(ChessLogic.MoveTo(m));
            _fx.ScalePop(cell, 1.14f, 0.22f);
            if (Over) return; // the game-over fx take it from here
            if (logic != null && logic.InCheck())
            {
                Pop(S("check"), "hit");
                Sfx.Play("game_hit", mine ? 2f : -3f);
                if (!mine)
                {
                    Haptics.Warn();
                    _fx.Shake(0.3f);
                    _fx.Flash(HurtFlash, 0.16f);
                }
                else Haptics.Tap();
            }
            else if (capture)
            {
                Sfx.Play("game_hit", mine ? 0f : -4f);
                _fx.Burst(Centre(cell), CaptureColor, mine ? 16 : 10);
            }
            else
            {
                Sfx.Play("game_pop", mine ? 0f : -3f);
                _fx.Burst(Centre(cell), MoveColor, 6);
            }
        }

        private void OnEmote(string id)
        {
            if (_practice || !Board.SendEmote(id)) return;
            Sfx.PlayUi("soft_tap");
            if (_emotes.TryGetValue(id, out var btn))
            {
                _fx.ScalePop(btn, 1.2f, 0.2f);
                _fx.FloatText(Centre(btn), B("emote." + id), new Color(0.23f, 0.21f, 0.31f), 20f, 48f);
            }
        }

        private void OnTomato()
        {
            if (_practice) return;
            if (!Board.ThrowTomato())
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(B("tomate_limit"), "food_tomato", 1.8f);
                return;
            }
            Sfx.Play("game_whoosh");
            _fx.FloatText(Centre(_tomato), B("tomate_fliegt"), CaptureColor, 22f, 60f);
            MarkDirty();
        }

        private void OnLeavePressed()
        {
            if (Logic != null && !Over && !SetupOpen)
            {
                Sfx.PlayUi("soft_open");
                ShowConfirm(true);
                return;
            }
            Close();
        }

        private void OnConfirmSurrender()
        {
            ShowConfirm(false);
            if (Over) return;
            if (_practice)
            {
                if (_pLogic == null) return;
                _pResult = "surrender";
                _pIWon = false;
                _botAt = -1f;
                ClearSelection();
                OnGameOver("", false, BoardEndReason.Surrender);
                return;
            }
            var b = Board;
            if (!b.IsActive) return;
            // at the table: give up (GAME_OVER, the rematch stays possible); nobody there yet: just leave
            if (b.PeerInRoom && b.Surrender()) return;
            Close();
        }

        private void OnRematch()
        {
            Sfx.PlayUi("soft_confirm");
            if (_practice)
            {
                NewPractice();
                Sfx.Play("game_go");
                return;
            }
            Board.RequestRematch(r =>
            {
                if (!r.Ok) _host.Ui.Toast(S("rematch.failed", ("code", _online.BoardErrorText(r.Code))), "redo", 3f);
                MarkDirty();
            });
            MarkDirty();
        }

        /// <summary>Leaves the table (mid-game the server scores it as a forfeit) and closes the screen.</summary>
        private void Close()
        {
            Sfx.PlayUi("soft_back");
            if (!_practice && Board.IsChess) Board.Leave();
            if (_host.Ui.Current == this) _host.Ui.Pop();
            else if (_host.Ui.IsOpen(this)) _host.Ui.PopToRoot();
        }

        // ── board events ────────────────────────────────────────────────────

        private void OnGameStarted()
        {
            if (!Board.IsChess) return;
            // a rematch on the same screen: fresh board (colours swapped), card away
            _resultHidden = false;
            _peerDownUntil = -1f;
            ShowConfirm(false);
            ClearSelection();
            _shownMoves = -1;
            _fx.Clear();
            Pop(Board.MyColor == ChessLogic.White ? S("you_began") : S("they_begin", ("name", OppName())), "info");
            Sfx.Play("game_go");
            MarkDirty();
        }

        private void OnResumed()
        {
            _host.Ui.Toast(S("resumed"), "redo", 2.4f);
            ClearSelection();
            _shownMoves = -1;
            MarkDirty();
        }

        private void OnPeerJoined()
        {
            Sfx.PlayUi("soft_toast");
            MarkDirty();
        }

        private void OnOpponentMoved(string uci)
        {
            var logic = Board.Chess;
            ClearSelection();
            if (logic == null || logic.LastMove == 0) { MarkDirty(); return; }
            var m = logic.LastMove;
            // the capture flag is gone after the move — the SAN tells it
            var san = logic.SanMoves.Count > 0 ? logic.SanMoves[logic.SanMoves.Count - 1] : "";
            MoveFx(m, mine: false, san.Contains("x"));
        }

        private void OnGameOver(string winner, bool iWon, BoardEndReason reason)
        {
            _resultHidden = false;
            ShowConfirm(false);
            ClearSelection();
            RefreshNow();
            if (iWon)
            {
                Sfx.Play("game_win");
                Haptics.Win();
                _fx.Confetti(90);
            }
            else if (IsDraw) Sfx.Play("game_combo");
            else Sfx.Play("game_lose");
            if (ResultId == ChessLogic.ResultCheckmate) Pop(S("reason.checkmate"), iWon ? "info" : "sunk");
            MarkDirty();
        }

        private void OnOpponentEmote(string id)
        {
            _fx.CancelFade(_bubble);
            _bubbleLabel.text = _host.Strings.HasKey("online.board.emote." + id) ? B("emote." + id) : id;
            _bubble.style.display = DisplayStyle.Flex;
            _bubble.style.opacity = 1f;
            _fx.ScalePop(_bubble, 1.2f, 0.25f);
            _bubbleLeft = 2.2f;
            Sfx.PlayUi("soft_sticker");
        }

        private void OnTomatoIncoming()
        {
            _fx.Shake(0.45f);
            _fx.Flash(HurtFlash, 0.18f);
            _fx.Burst(Centre(_board), CaptureColor, 26);
            Sfx.Play("game_pop", -2f);
            _host.Ui.Toast(B("tomate_platsch", ("name", OppName())), "food_tomato", 2.2f);
        }

        private void OnTomatoRejected(string code)
        {
            _host.Ui.Toast(B("tomate_limit"), "food_tomato", 1.8f);
            MarkDirty();
        }

        private void OnRejected(string kind, string code)
        {
            if (kind != "SHOT" && kind != "SHOT_RESULT") return;
            Sfx.PlayUi("soft_error");
            _host.Ui.Toast(S("rejected", ("grund", _online.BoardErrorText(code))), "grid", 2.6f);
            ClearSelection();
            _shownMoves = -1;
            MarkDirty();
        }

        private void OnRematchAsked()
        {
            Sfx.PlayUi("soft_toast");
            if (_rematch != null) _fx.ScalePop(_rematch, 1.15f, 0.3f);
            MarkDirty();
        }

        private void OnRematchDeclined()
        {
            _host.Ui.Toast(S("rematch.declined", ("name", OppName())), "redo", 2.6f);
            MarkDirty();
        }

        private void OnPeerConnection(bool down, int waitMs)
        {
            _peerDownUntil = down && waitMs > 0 ? Time.realtimeSinceStartup + waitMs / 1000f : -1f;
            _host.Ui.Toast(down ? S("peer.down", ("name", OppName())) : S("peer.up", ("name", OppName())), down ? "cloud" : "friends", 2.6f);
            MarkDirty();
        }

        private void RefreshNow()
        {
            if (_sub != null) Refresh();
        }
    }
}
