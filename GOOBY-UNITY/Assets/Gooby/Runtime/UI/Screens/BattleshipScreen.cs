using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Net;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Schiffe versenken with a friend (battleship_scene.gd in the window kit of board 06): a cream window with a white
    /// title band (opponent, score pill, Aufgeben/Verlassen), the own fleet left and the enemy waters right as 10×10 grids
    /// of UI Toolkit cells (coordinates A–J / 1–10), and between them the opponent card, the turn banner, the setup box
    /// (Neu würfeln / Bereit!, tap a ship to turn it) and emotes + tomato. Hits, misses and sunk ships pop with sound and
    /// sparks; the result card offers the rematch. All state lives in <see cref="BoardClient"/> (Core) — the screen only
    /// draws it and forwards taps. Stable names: <c>bs-cell-x-y</c> (enemy waters), <c>bs-own-x-y</c>, <c>bs-leave</c>,
    /// <c>bs-rematch</c>, <c>bs-reroll</c>, <c>bs-ready</c>, <c>bs-tomato</c>, <c>bs-emote-&lt;id&gt;</c>,
    /// <c>bs-confirm-yes|no</c>, <c>bs-result-leave</c>, <c>bs-result-hide</c>, <c>bs-result-show</c>.
    /// </summary>
    public sealed class BattleshipScreen : UiScreen
    {
        private const int N = BattleshipLogic.Grid;
        /// <summary>Window width (panel px) below which the grids shrink (normal needs ~1330, compact ~1180).</summary>
        private const float CompactBelow = 1330f, TinyBelow = 1180f, ShortBelow = 610f;

        private static readonly Color TileBlau = new Color(0.86f, 0.92f, 1f);
        private static readonly Color HitColor = new Color(0.95f, 0.43f, 0.43f);
        private static readonly Color MissColor = new Color(0.50f, 0.72f, 1f);
        private static readonly Color SunkColor = new Color(0.72f, 0.23f, 0.23f);
        private static readonly Color HurtFlash = new Color(1f, 0.45f, 0.45f, 0.22f);

        private readonly GameHost _host;
        private readonly OnlineService _online;
        private readonly VisualElement[,] _own = new VisualElement[N, N];
        private readonly VisualElement[,] _enemy = new VisualElement[N, N];
        private readonly Juice _fx = new Juice();

        private VisualElement _win, _ownGrid, _enemyGrid, _banner, _setup, _fun, _confirm, _result, _resultCard, _resIcon, _bubble, _splat, _oppDot;
        private Label _sub, _score, _ownCount, _enemyCount, _oppName, _oppLine, _bannerTitle, _bannerSub, _pop, _bubbleLabel;
        private Label _confirmText, _resTitle, _resReason, _resLine, _statShots, _statHits, _statRate, _statSunk;
        private Button _leave, _tomato, _resultShow, _rematch;
        private Label _leaveLabel, _rematchLabel;
        private VisualElement _bubbleIcon;
        private string _resIconName = "star";
        private readonly Dictionary<string, Button> _emotes = new Dictionary<string, Button>();

        private bool _dirty = true, _wired, _resultHidden, _greeted;
        private float _popLeft, _bubbleLeft, _splatLeft, _tick;
        private float _peerDownUntil = -1f;

        public BattleshipScreen(GameHost host, OnlineService online)
        {
            _host = host;
            _online = online;
            Id = "battleship";
            Opaque = true; // the HUD hides; the world keeps rendering behind the scrim
        }

        private BoardClient Board => _online.Board;

        private string S(string key, params (string name, object value)[] args) => _host.Strings.T("online.board." + key, args);

        // ── build ───────────────────────────────────────────────────────────

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-sheets");
            root.AddToClassList("gb-bs");
            // no close on the scrim: a stray tap must never end a game
            root.Add(Ux.El("gb-scrim", "gb-bs-scrim"));

            _win = Ux.El("gb-win", "gb-bs-win");
            _win.name = "bs-window";
            Ux.Abs(_win, 40, 18, 40, 18);
            _win.RegisterCallback<GeometryChangedEvent>(e => Fit(root, e.newRect));
            root.Add(_win);

            _win.Add(BuildHead());

            var body = Ux.Row("gb-bs-body");
            _win.Add(body);

            var ownSide = Side(true, out _ownCount);
            _ownGrid = BuildGrid(own: true);
            ownSide.Add(_ownGrid);
            body.Add(ownSide);

            body.Add(BuildMiddle());

            var enemySide = Side(false, out _enemyCount);
            _enemyGrid = BuildGrid(own: false);
            enemySide.Add(_enemyGrid);
            body.Add(enemySide);

            _splat = BuildSplat();
            _win.Add(_splat);
            _confirm = BuildConfirm();
            _win.Add(_confirm);
            _result = BuildResult();
            _win.Add(_result);

            _fx.ShakeTarget = _win;
            root.Add(_fx.Layer);
            Refresh();
        }

        /// <summary>Title band: tile, "Schiffe versenken" + opponent line, score pill, Aufgeben/Verlassen.</summary>
        private VisualElement BuildHead()
        {
            var head = Ux.Row("gb-win-head");
            head.Add(Ux.IconTile("grid", TileBlau));
            var texts = Ux.Col("gb-win-texts");
            texts.Add(Ux.Text(S("titel"), "gb-win-title"));
            _sub = Ux.Text("", "gb-win-sub");
            texts.Add(_sub);
            head.Add(texts);

            var score = Ux.Row("gb-bs-score");
            score.name = "bs-score";
            score.Add(Ux.Icon("star"));
            _score = Ux.Text("", "gb-nowrap");
            score.Add(_score);
            head.Add(score);

            _leave = Ux.IconTextBtn("back", S("aufgeben"), OnLeavePressed, "gb-btn-secondary", "gb-btn-sm", "gb-bs-leave");
            _leave.name = "bs-leave";
            _leaveLabel = _leave.Q<Label>();
            head.Add(_leave);
            return head;
        }

        private VisualElement Side(bool own, out Label count)
        {
            var side = Ux.Col("gb-bs-side", own ? "gb-bs-own" : "gb-bs-enemy");
            var head = Ux.Row("gb-bs-side-head");
            head.Add(Ux.Icon(own ? "house" : "compass"));
            head.Add(Ux.Text(S(own ? "eigene" : "gegner_feld"), "gb-bs-side-title"));
            count = Ux.Text("", "gb-bs-side-count");
            head.Add(count);
            side.Add(head);
            return side;
        }

        /// <summary>10×10 cells with column letters on top and row numbers on the left (protocol notation "B4").</summary>
        private VisualElement BuildGrid(bool own)
        {
            var grid = Ux.Col("gb-bs-grid", own ? "gb-bs-grid--own" : "gb-bs-grid--enemy");
            grid.name = own ? "bs-own-grid" : "bs-enemy-grid";
            var head = Ux.Row("gb-bs-grid-row");
            head.Add(Ux.El("gb-bs-coord", "gb-bs-coord--corner"));
            for (var x = 0; x < N; x++) head.Add(Ux.Text(BattleshipLogic.Cols[x].ToString(), "gb-bs-coord"));
            grid.Add(head);
            for (var y = 0; y < N; y++)
            {
                var row = Ux.Row("gb-bs-grid-row");
                row.Add(Ux.Text((y + 1).ToString(), "gb-bs-coord", "gb-bs-coord--row"));
                for (var x = 0; x < N; x++)
                {
                    var c = new BattleshipCell(x, y);
                    var cell = Ux.El("gb-bs-cell");
                    cell.name = (own ? "bs-own-" : "bs-cell-") + x + "-" + y;
                    var mark = Ux.El("gb-bs-mark");
                    mark.pickingMode = PickingMode.Ignore;
                    cell.Add(mark);
                    if (own) cell.RegisterCallback<ClickEvent>(_ => OnOwnTap(c));
                    else cell.RegisterCallback<ClickEvent>(_ => OnEnemyTap(c));
                    row.Add(cell);
                    if (own) _own[x, y] = cell;
                    else _enemy[x, y] = cell;
                }
                grid.Add(row);
            }
            return grid;
        }

        /// <summary>Opponent card, turn banner, hit/miss pop, setup box, emotes + tomato.</summary>
        private VisualElement BuildMiddle()
        {
            var mid = Ux.Col("gb-bs-mid");

            var opp = Ux.Row("gb-bs-opp");
            opp.Add(Ux.Portrait(56));
            var texts = Ux.Col("gb-bs-opp-texts");
            texts.Add(Ux.Text(S("kicker"), "gb-bs-kicker"));
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
            _bubble.name = "bs-emote-bubble";
            _bubble.pickingMode = PickingMode.Ignore;
            _bubbleIcon = Ux.Icon("sparkle");
            _bubble.Add(_bubbleIcon);
            _bubbleLabel = Ux.Text("");
            _bubble.Add(_bubbleLabel);
            _bubble.style.display = DisplayStyle.None;
            opp.Add(_bubble);
            mid.Add(opp);

            _banner = Ux.Col("gb-bs-banner");
            _banner.name = "bs-banner";
            _bannerTitle = Ux.Text("", "gb-bs-banner-title");
            _bannerTitle.name = "bs-turn";
            _banner.Add(_bannerTitle);
            _bannerSub = Ux.Text("", "gb-bs-banner-sub");
            _banner.Add(_bannerSub);
            _resultShow = Ux.IconTextBtn("star", S("ergebnis"), () => { Sfx.PlayUi("soft_open"); _resultHidden = false; MarkDirty(); }, "gb-btn-primary", "gb-btn-sm");
            _resultShow.name = "bs-result-show";
            _banner.Add(_resultShow);
            mid.Add(_banner);

            _setup = Ux.Col("gb-bs-box", "gb-bs-setup");
            _setup.Add(Ux.Text(S("setup_hint"), "gb-bs-box-hint"));
            var setupBtns = Ux.Row("gb-bs-btns");
            var reroll = Ux.IconTextBtn("redo", S("neu_wuerfeln"), OnReroll, "gb-btn-secondary", "gb-btn-sm");
            reroll.name = "bs-reroll";
            setupBtns.Add(reroll);
            var ready = Ux.IconTextBtn("check", S("bereit"), OnReady, "gb-btn-confirm", "gb-btn-sm");
            ready.name = "bs-ready";
            setupBtns.Add(ready);
            _setup.Add(setupBtns);
            mid.Add(_setup);

            _fun = Ux.Row("gb-bs-fun");
            foreach (var id in BoardEmotes.Ids)
            {
                var emoteId = id;
                var b = Ux.IconBtn(EmoteIcon(id), () => OnEmote(emoteId), "gb-btn-icon-sm", "gb-bs-emote");
                b.name = "bs-emote-" + id;
                b.tooltip = S("emote." + id);
                _emotes[id] = b;
                _fun.Add(b);
            }
            _tomato = Ux.IconTextBtn("food_tomato", S("tomate"), OnTomato, "gb-btn-primary", "gb-btn-sm", "gb-bs-tomato");
            _tomato.name = "bs-tomato";
            _fun.Add(_tomato);
            mid.Add(_fun);

            // the big "Treffer!" sits over the middle column, below the banner
            _pop = Ux.Text("", "gb-bs-pop");
            _pop.name = "bs-pop";
            _pop.pickingMode = PickingMode.Ignore;
            _pop.style.display = DisplayStyle.None;
            mid.Add(_pop);
            return mid;
        }

        private VisualElement BuildSplat()
        {
            var splat = Ux.El("gb-bs-splat");
            splat.name = "bs-splat";
            splat.pickingMode = PickingMode.Ignore;
            for (var i = 0; i < 5; i++)
            {
                var blob = Ux.El("gb-bs-splat-blob", "gb-bs-splat-blob--" + i);
                blob.pickingMode = PickingMode.Ignore;
                splat.Add(blob);
            }
            var icon = Ux.Icon("food_tomato", "gb-bs-splat-icon");
            icon.pickingMode = PickingMode.Ignore;
            splat.Add(icon);
            splat.style.display = DisplayStyle.None;
            return splat;
        }

        private VisualElement BuildConfirm()
        {
            var layer = Ux.El("gb-bs-overlay", "gb-bs-confirm");
            layer.name = "bs-confirm";
            var wrap = Ux.El("gb-bs-confirm-wrap");
            wrap.Add(Ux.El("gb-shadow"));
            var card = Ux.Col("gb-bs-card");
            card.Add(Ux.Text(S("aufgeben_frage"), "gb-bs-card-title"));
            _confirmText = Ux.Text("", "gb-bs-card-text");
            card.Add(_confirmText);
            var btns = Ux.Row("gb-bs-card-btns");
            var no = Ux.IconTextBtn("play", S("weiterspielen"), () => { Sfx.PlayUi("soft_back"); ShowConfirm(false); }, "gb-btn-confirm", "gb-btn-sm");
            no.name = "bs-confirm-no";
            btns.Add(no);
            var yes = Ux.IconTextBtn("back", S("aufgeben"), OnConfirmSurrender, "gb-btn-cancel", "gb-btn-sm");
            yes.name = "bs-confirm-yes";
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
            layer.name = "bs-result";
            var wrap = Ux.El("gb-bs-result-wrap");
            wrap.Add(Ux.El("gb-shadow"));
            _resultCard = Ux.Col("gb-bs-card", "gb-bs-result-card");
            _resIcon = Ux.El("gb-bs-result-icon");
            _resIcon.Add(Ux.Icon("star"));
            _resultCard.Add(_resIcon);
            _resTitle = Ux.Text("", "gb-bs-result-title");
            _resTitle.name = "bs-result-title";
            _resultCard.Add(_resTitle);
            _resReason = Ux.Text("", "gb-bs-result-reason");
            _resultCard.Add(_resReason);
            var stats = Ux.Row("gb-bs-stats");
            stats.Add(Stat(S("stat.schuesse"), out _statShots));
            stats.Add(Stat(S("stat.treffer"), out _statHits));
            stats.Add(Stat(S("stat.quote"), out _statRate));
            stats.Add(Stat(S("stat.versenkt"), out _statSunk));
            _resultCard.Add(stats);
            _resLine = Ux.Text("", "gb-bs-result-line");
            _resLine.name = "bs-rematch-line";
            _resultCard.Add(_resLine);
            var btns = Ux.Row("gb-bs-card-btns");
            var boards = Ux.IconTextBtn("grid", S("bretter"), () => { Sfx.PlayUi("soft_close"); _resultHidden = true; MarkDirty(); }, "gb-btn-secondary", "gb-btn-sm");
            boards.name = "bs-result-hide";
            btns.Add(boards);
            var leave = Ux.IconTextBtn("home", S("verlassen"), Close, "gb-btn-secondary", "gb-btn-sm");
            leave.name = "bs-result-leave";
            btns.Add(leave);
            _rematch = Ux.IconTextBtn("redo", S("revanche"), OnRematch, "gb-btn-primary", "gb-btn-sm");
            _rematch.name = "bs-rematch";
            _rematchLabel = _rematch.Q<Label>();
            btns.Add(_rematch);
            _resultCard.Add(btns);
            wrap.Add(_resultCard);
            layer.Add(wrap);
            layer.style.display = DisplayStyle.None;
            return layer;
        }

        private static VisualElement Stat(string label, out Label value)
        {
            var box = Ux.Col("gb-bs-stat");
            value = Ux.Text("0", "gb-bs-stat-value");
            box.Add(value);
            box.Add(Ux.Text(label, "gb-bs-stat-label"));
            return box;
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

        /// <summary>Shrinks the grids on narrow (4:3 tablets) or short windows.</summary>
        private static void Fit(VisualElement root, Rect r)
        {
            if (r.width <= 0f) return;
            var tiny = r.width < TinyBelow || r.height < ShortBelow - 80f;
            var compact = !tiny && (r.width < CompactBelow || r.height < ShortBelow);
            root.EnableInClassList("gb-bs--compact", compact);
            root.EnableInClassList("gb-bs--tiny", tiny);
        }

        // ── lifecycle ───────────────────────────────────────────────────────

        public override void OnShown()
        {
            Wire(true);
            MarkDirty();
            if (_greeted || !Board.HasGame) return;
            // the screen opens from GameStarted itself, so the first "Du beginnst!" comes from here
            _greeted = true;
            Pop(Board.FirstCode == Board.MyCode ? S("du_beginnst") : S("beginnt", ("name", OppName())), "info");
            Sfx.Play("game_go");
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return; // only covered by a sheet — the game goes on
            Wire(false);
            // removed without "Verlassen" (PopToRoot by a visit, a boot flow …): leave the table cleanly — unless a chess
            // game took over the client (OnlineService swaps the screen then)
            if (Board.HasGame && !Board.IsChess) Board.Leave();
        }

        private void Wire(bool on)
        {
            if (on == _wired) return;
            _wired = on;
            var b = Board;
            if (on)
            {
                b.Changed += MarkDirty;
                b.GameStarted += OnGameStarted;
                b.GameResumed += OnResumed;
                b.PeerJoined += OnPeerJoined;
                b.FleetAutoLocked += OnAutoLocked;
                b.ShotResult += OnShotResult;
                b.OpponentShot += OnOpponentShot;
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
                b.FleetAutoLocked -= OnAutoLocked;
                b.ShotResult -= OnShotResult;
                b.OpponentShot -= OnOpponentShot;
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
            if (_splatLeft > 0f)
            {
                _splatLeft -= dt;
                if (_splatLeft <= 0f) _fx.FadeOut(_splat, 0.5f);
            }
            _tick += dt;
            if (_tick >= 0.5f)
            {
                _tick = 0f;
                _dirty = true; // countdowns (peer down) and the connection line
            }
            if (!Board.HasGame || Board.IsChess)
            {
                // the table is gone (left from elsewhere) or a chess game took over: close instead of showing an empty board
                if (_host.Ui.Current == this) _host.Ui.Pop();
                return;
            }
            if (_dirty) Refresh();
        }

        // ── drawing ─────────────────────────────────────────────────────────

        private string OppName()
        {
            var b = Board;
            if (b.OpponentName.Length > 0) return b.OpponentName;
            var f = _online.Client.FindFriend(b.OpponentCode);
            return f != null && f.Name.Length > 0 ? f.Name : b.OpponentCode;
        }

        private string OppGooby() => Board.OpponentGoobyName.Length > 0 ? Board.OpponentGoobyName : "Gooby";

        private void Refresh()
        {
            _dirty = false;
            if (_sub == null) return;
            var b = Board;
            var name = OppName();
            var gooby = OppGooby();
            var max = BattleshipLogic.Fleet.Length;
            _sub.text = b.HasGame ? S("gegen", ("name", name), ("gooby", gooby)) : "";
            _score.text = S("stand", ("treffer", b.HitsLanded), ("versenkt", b.ShipsSunk), ("max", max));
            _leaveLabel.text = b.IsActive ? S("aufgeben") : S("verlassen");
            _ownCount.text = S("schiffe_uebrig", ("n", b.ShipsLeft), ("max", max));
            _enemyCount.text = S("versenkt_zahl", ("n", b.ShipsSunk), ("max", max));

            _oppName.text = name;
            var peerHere = b.PeerInRoom && _online.Client.IsOnline;
            _oppDot.EnableInClassList("gb-status-dot--on", peerHere);
            _oppDot.EnableInClassList("gb-status-dot--wait", !peerHere && !b.OpponentGone);
            _oppDot.EnableInClassList("gb-status-dot--off", b.OpponentGone);
            _oppLine.text = b.OpponentGone ? S("peer_gegangen", ("name", name))
                : b.PeerDown ? S("peer_weg", ("name", name))
                : peerHere ? S("peer_da", ("gooby", gooby))
                : S("peer_wartet", ("gooby", gooby));

            RefreshOwn();
            RefreshEnemy();
            RefreshBanner(name, gooby);

            _setup.style.display = b.IsActive && !b.FleetLocked ? DisplayStyle.Flex : DisplayStyle.None;
            _fun.style.display = b.HasGame ? DisplayStyle.Flex : DisplayStyle.None;
            var canEmote = b.HasGame && _online.Client.IsOnline && b.PeerInRoom;
            foreach (var e in _emotes.Values) e.SetEnabled(canEmote);
            _tomato.SetEnabled(canEmote && b.CanThrowTomato);

            RefreshResult(name);
        }

        private static void Set(VisualElement e, string cls, bool on) => e.EnableInClassList(cls, on);

        private void RefreshOwn()
        {
            var b = Board;
            var fleet = b.Fleet;
            var shipAt = new int[N, N];
            for (var x = 0; x < N; x++) for (var y = 0; y < N; y++) shipAt[x, y] = -1;
            for (var i = 0; i < fleet.Count; i++)
                foreach (var c in fleet[i].Cells())
                    if (c.InBounds) shipAt[c.X, c.Y] = i;
            var board = b.FleetLocked ? b.Board : null;
            var editable = b.IsActive && !b.FleetLocked;
            _ownGrid.EnableInClassList("gb-bs-grid--edit", editable);
            for (var y = 0; y < N; y++)
            {
                for (var x = 0; x < N; x++)
                {
                    var c = new BattleshipCell(x, y);
                    var e = _own[x, y];
                    var i = shipAt[x, y];
                    var ship = i >= 0 ? fleet[i] : null;
                    var seg = ship != null ? ship.SegmentOf(c) : -1;
                    var shot = board != null && board.WasShot(c);
                    var sunk = shot && ship != null && board.IsSunk(board.ShipAt(c));
                    Set(e, "gb-bs-cell--ship", ship != null);
                    Set(e, "gb-bs-cell--h", ship != null && ship.Horizontal);
                    Set(e, "gb-bs-cell--v", ship != null && !ship.Horizontal);
                    Set(e, "gb-bs-cell--bow", seg == 0);
                    Set(e, "gb-bs-cell--stern", ship != null && seg == ship.Len - 1);
                    Set(e, "gb-bs-cell--hit", shot && ship != null && !sunk);
                    Set(e, "gb-bs-cell--sunk", sunk);
                    Set(e, "gb-bs-cell--miss", shot && ship == null);
                    Set(e, "gb-bs-cell--last", b.LastIncoming == c);
                }
            }
        }

        private void RefreshEnemy()
        {
            var b = Board;
            var tr = b.Tracker;
            var aim = b.MyTurn && b.PeerInRoom && _online.Client.IsOnline;
            _enemyGrid.EnableInClassList("gb-bs-grid--aim", aim);
            for (var y = 0; y < N; y++)
            {
                for (var x = 0; x < N; x++)
                {
                    var c = new BattleshipCell(x, y);
                    var e = _enemy[x, y];
                    var hit = false;
                    var shot = tr != null && tr.TryGetShot(c, out hit);
                    var sunk = tr != null && tr.IsSunkCell(c);
                    var pending = b.PendingShot == c;
                    Set(e, "gb-bs-cell--hit", shot && hit && !sunk);
                    Set(e, "gb-bs-cell--sunk", sunk);
                    Set(e, "gb-bs-cell--miss", shot && !hit);
                    Set(e, "gb-bs-cell--pending", pending);
                    Set(e, "gb-bs-cell--last", b.LastOutgoing == c && !pending);
                    Set(e, "gb-bs-cell--open", aim && !shot && !pending);
                }
            }
        }

        private void RefreshBanner(string name, string gooby)
        {
            var b = Board;
            string title, sub, mode;
            if (b.Finished)
            {
                title = b.IWon ? S("gewonnen") : b.EndReason == BoardEndReason.Expired ? S("vorbei") : S("verloren");
                sub = ReasonText(name);
                mode = b.IWon ? "win" : "lose";
            }
            else if (!_online.Client.IsOnline)
            {
                title = S("verbinde");
                sub = S("verbinde_hint");
                mode = "wait";
            }
            else if (b.PeerDown)
            {
                var left = _peerDownUntil > 0f ? Mathf.Max(0, Mathf.CeilToInt(_peerDownUntil - Time.realtimeSinceStartup)) : 0;
                title = S("peer_weg", ("name", name));
                sub = left > 0 ? S("peer_weg_hint", ("s", left)) : S("peer_weg_hint_kurz");
                mode = "wait";
            }
            else if (!b.PeerInRoom)
            {
                title = S("warte_peer", ("name", name));
                sub = S("warte_peer_hint", ("gooby", gooby));
                mode = "wait";
            }
            else if (b.AwaitingResult)
            {
                title = S("warte_ergebnis", ("feld", b.PendingShot.Ref));
                sub = S("warte_ergebnis_hint");
                mode = "mine";
            }
            else if (b.MyTurn)
            {
                title = S("dran");
                sub = b.FleetLocked ? S("dran_hint") : S("dran_hint_setup");
                mode = "mine";
            }
            else
            {
                title = S("gegner_dran", ("name", name));
                sub = S("gegner_dran_hint", ("gooby", gooby));
                mode = "theirs";
            }
            _bannerTitle.text = title;
            _bannerSub.text = sub;
            foreach (var m in new[] { "mine", "theirs", "wait", "win", "lose" }) Set(_banner, "gb-bs-banner--" + m, m == mode);
            _resultShow.style.display = b.Finished && _resultHidden ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private string ReasonText(string name)
        {
            var b = Board;
            switch (b.EndReason)
            {
                case BoardEndReason.Surrender: return b.IWon ? S("grund.aufgabe_sieg", ("name", name)) : S("grund.aufgabe_niederlage");
                case BoardEndReason.Forfeit: return b.IWon ? S("grund.forfeit", ("name", name)) : S("grund.abgebrochen");
                case BoardEndReason.Expired: return S("grund.abgelaufen");
                default: return b.IWon ? S("grund.alle_sieg") : S("grund.alle_niederlage", ("gooby", OppGooby()));
            }
        }

        private void RefreshResult(string name)
        {
            var b = Board;
            var show = b.Finished && !_resultHidden;
            var was = _result.style.display == DisplayStyle.Flex;
            _result.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!b.Finished) return;
            _result.EnableInClassList("gb-bs-result--win", b.IWon);
            _result.EnableInClassList("gb-bs-result--lose", !b.IWon);
            var icon = b.IWon ? "star" : b.EndReason == BoardEndReason.Expired ? "cloud" : "heart";
            if (icon != _resIconName)
            {
                _resIconName = icon;
                _resIcon.Clear();
                _resIcon.Add(Ux.Icon(icon));
            }
            _resTitle.text = b.IWon ? S("gewonnen") : b.EndReason == BoardEndReason.Expired ? S("vorbei") : S("verloren");
            _resReason.text = ReasonText(name);
            _statShots.text = b.ShotsFired.ToString();
            _statHits.text = b.HitsLanded.ToString();
            _statRate.text = b.AccuracyPercent + " %";
            _statSunk.text = b.ShipsSunk + "/" + BattleshipLogic.Fleet.Length;

            var online = _online.Client.IsOnline;
            if (b.OpponentGone) _resLine.text = S("revanche_weg", ("name", name));
            else if (b.RematchMine) _resLine.text = S("revanche_wartet", ("name", name));
            else if (b.RematchTheirs) _resLine.text = S("revanche_frage", ("name", name));
            else if (!online) _resLine.text = S("verbinde");
            else _resLine.text = "";
            _rematchLabel.text = b.RematchTheirs && !b.RematchMine ? S("revanche_annehmen") : S("revanche");
            _rematch.SetEnabled(online && !b.OpponentGone && !b.RematchMine && b.EndReason != BoardEndReason.Expired);
            if (show && !was) _fx.ScalePop(_resultCard, 1.06f, 0.28f);
        }

        private void ShowConfirm(bool on)
        {
            _confirm.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            _confirmText.text = S("aufgeben_text", ("name", OppName()));
            _fx.ScalePop(_confirm, 1.04f, 0.2f);
        }

        /// <summary>"Treffer!" / "Wasser …" / "Versenkt!" over the middle column, fading after a moment.</summary>
        private void Pop(string text, string kind)
        {
            _fx.CancelFade(_pop);
            _pop.text = text;
            foreach (var k in new[] { "hit", "miss", "sunk", "info" }) Set(_pop, "gb-bs-pop--" + k, k == kind);
            _pop.style.display = DisplayStyle.Flex;
            _pop.style.opacity = 1f;
            _fx.ScalePop(_pop, 1.35f, 0.3f);
            _popLeft = 1.4f;
        }

        private static Vector2 Centre(VisualElement e) => e.worldBound.center;

        // ── taps ────────────────────────────────────────────────────────────

        private void OnEnemyTap(BattleshipCell c)
        {
            var b = Board;
            if (!b.IsActive) return;
            var err = b.Shoot(c);
            if (err.Length == 0)
            {
                Sfx.PlayUi("soft_tap");
                _fx.ScalePop(_enemy[c.X, c.Y], 1.18f, 0.18f);
                Debug.Log("[board] shoot " + c.Ref);
                MarkDirty();
                return;
            }
            string hint;
            switch (err)
            {
                case BoardClient.ErrNotYourTurn: hint = b.AwaitingResult ? S("warte_ergebnis_hint") : S("nicht_dran", ("name", OppName())); break;
                case BoardClient.ErrAlreadyShot: hint = S("schon_beschossen"); break;
                case BoardClient.ErrWaitingPeer: hint = S("warte_peer", ("name", OppName())); break;
                default: hint = _online.BoardErrorText(err); break;
            }
            Sfx.PlayUi("soft_error");
            _host.Ui.Toast(hint, "grid", 1.8f);
        }

        private void OnOwnTap(BattleshipCell c)
        {
            var b = Board;
            if (!b.IsActive || b.FleetLocked) return;
            var hasShip = false;
            foreach (var s in b.Fleet) if (s.Contains(c)) hasShip = true;
            if (!hasShip) return;
            if (b.RotateShipAt(c))
            {
                Sfx.PlayUi("soft_toggle");
                _fx.ScalePop(_own[c.X, c.Y], 1.12f, 0.16f);
            }
            else
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(S("drehen_geht_nicht"), "rotate", 1.6f);
            }
        }

        private void OnReroll()
        {
            if (!Board.RerollFleet()) return;
            Sfx.Play("soft_chip", Board.Rerolls % 4);
            _fx.ScalePop(_ownGrid, 1.02f, 0.18f);
        }

        private void OnReady()
        {
            if (!Board.LockFleet()) return;
            Sfx.PlayUi("soft_confirm");
            Pop(S("flotte_steht"), "info");
        }

        private void OnEmote(string id)
        {
            if (!Board.SendEmote(id)) return;
            Sfx.PlayUi("soft_tap");
            if (_emotes.TryGetValue(id, out var btn))
            {
                _fx.ScalePop(btn, 1.2f, 0.2f);
                _fx.FloatText(Centre(btn), S("emote." + id), new Color(0.23f, 0.21f, 0.31f), 20f, 48f);
            }
        }

        private void OnTomato()
        {
            if (!Board.ThrowTomato())
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(S("tomate_limit"), "food_tomato", 1.8f);
                return;
            }
            Sfx.Play("game_whoosh");
            _fx.FloatText(Centre(_tomato), S("tomate_fliegt"), HitColor, 22f, 60f);
            MarkDirty();
        }

        private void OnLeavePressed()
        {
            var b = Board;
            if (b.IsActive)
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
            var b = Board;
            if (!b.IsActive) return;
            // at the table: give up (GAME_OVER, the rematch stays possible); nobody there yet: just leave (the server
            // pushes BOARD_FORFEIT to the other device wherever it is)
            if (b.PeerInRoom && b.Surrender()) return;
            Close();
        }

        private void OnRematch()
        {
            var b = Board;
            Sfx.PlayUi("soft_confirm");
            b.RequestRematch(r =>
            {
                if (!r.Ok) _host.Ui.Toast(S("revanche_fehler", ("grund", _online.BoardErrorText(r.Code))), "redo", 3f);
                MarkDirty();
            });
            MarkDirty();
        }

        /// <summary>Leaves the table (mid-game the server scores it as a forfeit) and closes the screen.</summary>
        private void Close()
        {
            Sfx.PlayUi("soft_back");
            if (!Board.IsChess) Board.Leave();
            if (_host.Ui.Current == this) _host.Ui.Pop();
            else if (_host.Ui.IsOpen(this)) _host.Ui.PopToRoot();
        }

        // ── board events ────────────────────────────────────────────────────

        private void OnGameStarted()
        {
            if (Board.IsChess) return; // ChessScreen takes over
            // a rematch (or a new invite) on the same screen: fresh boards, card away
            _resultHidden = false;
            _peerDownUntil = -1f;
            ShowConfirm(false);
            _fx.Clear();
            var first = Board.FirstCode == Board.MyCode ? S("du_beginnst") : S("beginnt", ("name", OppName()));
            Pop(first, "info");
            Sfx.Play("game_go");
            MarkDirty();
        }

        private void OnResumed()
        {
            _host.Ui.Toast(S("fortgesetzt"), "redo", 2.4f);
            MarkDirty();
        }

        private void OnPeerJoined()
        {
            Sfx.PlayUi("soft_toast");
            MarkDirty();
        }

        private void OnAutoLocked()
        {
            Pop(S("flotte_steht"), "info");
            MarkDirty();
        }

        private void OnShotResult(BattleshipCell cell, bool hit, bool sunk, IReadOnlyList<BattleshipCell> sunkCells)
        {
            RefreshNow();
            var e = cell.InBounds ? _enemy[cell.X, cell.Y] : _enemyGrid;
            var at = Centre(e);
            if (sunk)
            {
                Pop(S("versenkt"), "sunk");
                Sfx.Play("game_hit", 2f);
                Sfx.Play("game_combo");
                Haptics.Success();
                _fx.Burst(at, SunkColor, 26);
                _fx.Shake(0.3f);
                foreach (var c in sunkCells) if (c.InBounds) _fx.ScalePop(_enemy[c.X, c.Y], 1.22f, 0.3f);
            }
            else if (hit)
            {
                Pop(S("treffer"), "hit");
                Sfx.Play("game_hit");
                Haptics.Tap();
                _fx.Burst(at, HitColor, 16);
                _fx.ScalePop(e, 1.25f, 0.24f);
            }
            else
            {
                Pop(S("wasser"), "miss");
                Sfx.Play("game_miss");
                _fx.Burst(at, MissColor, 8);
            }
        }

        private void OnOpponentShot(BattleshipCell cell, ShotOutcome outcome)
        {
            RefreshNow();
            var e = cell.InBounds ? _own[cell.X, cell.Y] : _ownGrid;
            var at = Centre(e);
            if (outcome.Sunk)
            {
                Pop(S("gesunken"), "sunk");
                Sfx.Play("game_hit", -5f);
                Haptics.Warn();
                _fx.Shake(0.5f);
                _fx.Flash(HurtFlash, 0.18f);
                _fx.Burst(at, SunkColor, 20);
            }
            else if (outcome.Hit)
            {
                Pop(S("getroffen", ("gooby", OppGooby())), "hit");
                Sfx.Play("game_hit", -4f);
                _fx.Shake(0.25f);
                _fx.Burst(at, HitColor, 12);
            }
            else
            {
                Pop(S("verfehlt"), "miss");
                Sfx.Play("game_miss", -3f);
                _fx.Burst(at, MissColor, 6);
            }
        }

        private void OnGameOver(string winner, bool iWon, BoardEndReason reason)
        {
            _resultHidden = false;
            ShowConfirm(false);
            if (iWon)
            {
                Sfx.Play("game_win");
                Haptics.Win();
                _fx.Confetti(90);
            }
            else Sfx.Play("game_lose");
            MarkDirty();
        }

        private void OnOpponentEmote(string id)
        {
            _fx.CancelFade(_bubble);
            _bubble.Remove(_bubbleIcon);
            _bubbleIcon = Ux.Icon(EmoteIcon(id));
            _bubble.Insert(0, _bubbleIcon);
            _bubbleLabel.text = _host.Strings.HasKey("online.board.emote." + id) ? S("emote." + id) : id;
            _bubble.style.display = DisplayStyle.Flex;
            _bubble.style.opacity = 1f;
            _fx.ScalePop(_bubble, 1.2f, 0.25f);
            _bubbleLeft = 2.2f;
            Sfx.PlayUi("soft_sticker");
        }

        private void OnTomatoIncoming()
        {
            _fx.CancelFade(_splat);
            _splat.style.display = DisplayStyle.Flex;
            _splat.style.opacity = 1f;
            _fx.ScalePop(_splat, 1.3f, 0.3f);
            _fx.Shake(0.45f);
            _splatLeft = 1.3f;
            Sfx.Play("game_pop", -2f);
            _host.Ui.Toast(S("tomate_platsch", ("name", OppName())), "food_tomato", 2.2f);
        }

        private void OnTomatoRejected(string code)
        {
            _host.Ui.Toast(S("tomate_limit"), "food_tomato", 1.8f);
            MarkDirty();
        }

        private void OnRejected(string kind, string code)
        {
            if (kind == "TOMATO") return; // has its own line
            if (kind != "SHOT" && kind != "SHOT_RESULT") return;
            Sfx.PlayUi("soft_error");
            _host.Ui.Toast(S("zug_abgelehnt", ("grund", _online.BoardErrorText(code))), "grid", 2.6f);
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
            _host.Ui.Toast(S("revanche_weg", ("name", OppName())), "redo", 2.6f);
            MarkDirty();
        }

        private void OnPeerConnection(bool down, int waitMs)
        {
            _peerDownUntil = down && waitMs > 0 ? Time.realtimeSinceStartup + waitMs / 1000f : -1f;
            _host.Ui.Toast(down ? S("peer_weg", ("name", OppName())) : S("peer_zurueck", ("name", OppName())), down ? "cloud" : "friends", 2.6f);
            MarkDirty();
        }

        /// <summary>Redraw before a pop so the cell that pops already carries its new marker.</summary>
        private void RefreshNow()
        {
            if (_sub != null) Refresh();
        }
    }
}
