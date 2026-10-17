using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Ranch-Treffen" — the ranch multiplayer rounds of ranchmp.js without the open world (rmp_menu_panel.gd,
    /// rmp_lobby_panel.gd and rmp_lauf_hud.gd on the painted meadow of <see cref="RmpRideView"/>): pick Wettrennen,
    /// Parcours-Duell or Fangen and invite online friends (invitations to us wait in the list and as a question card on every
    /// screen), the lobby with everyone's "Bereit" and the server countdown, the ride (gate n/max and place, or the catcher
    /// and the time left plus the "Fangen!" button), and the result table with server times, "Revanche!" and "Fertig".
    /// The courses and coordinates are the Godot ones, so a Unity rider and a Godot rider meet on the same track.
    /// </summary>
    public sealed class RanchMpScreen : UiScreen
    {
        private readonly GameHost _host;

        public override string SoundScene => "ranch/reiten";
        private readonly OnlineService _online;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private RmpLink _link;
        private RmpRideView _view;
        private VisualElement _field, _menu, _lobby, _result, _menuFriends, _menuInvites, _lobbyPlayers, _resultRows, _tag, _toastLane, _subDot;
        private Label _title, _sub, _menuStats, _lobbyTitle, _lobbyHint, _countdown, _resultTitle, _rematchHint;
        private Button _readyBtn, _rematchBtn;
        private string _mode = RmpKurse.Rennen;
        private string _shownKey = "";
        private float _refreshAccum;
        private long _lastCountdownSec = -1;

        public RanchMpScreen(GameHost host, OnlineService online)
        {
            _host = host;
            _online = online;
            Id = "ranch-mp";
            Opaque = true;
        }

        public RanchMpClient Client => _online.RanchMp;
        public RmpLink Link => _link;
        public RmpRideView View => _view;
        /// <summary>The menu lists incoming invitations itself — OnlineService shows no extra question card then.</summary>
        public bool ShowsMenu => _menu != null && _menu.style.display == DisplayStyle.Flex;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-online");
            _field = new VisualElement { name = "rmp-field" };
            Ux.Abs(_field, 0, 0, 0, 0);
            root.Add(_field);
            _view = new RmpRideView(_host, _field, () => _clock.ElapsedMilliseconds);
            _field.Add(_view.Canvas);
            _field.Add(_view.Tags);
            _field.Add(_view.Controls);
            _link = new RmpLink(Client, () => _clock.ElapsedMilliseconds);
            _link.Notice += OnNotice;
            _link.RunStarted += OnRunStarted;
            _link.Entdeckt += OnEntdeckt;
            _view.Bind(_link);
            _view.OrtName = OrtLabel;
            Client.ReactionReceived += OnReaction;
            Client.PeerJoined += OnPeerJoined;
            Client.PeerLeft += OnPeerLeft;
            Client.Ended += OnEnded;
            _online.RanchVisitMetaChanged += OnVisitMeta;

            // top-left: board-08 capsule — RANCH, the game, what happens
            var cap = Ux.El("gb-visit-cap");
            cap.name = "rmp-cap";
            Ux.Abs(cap, 16, 12);
            cap.Add(Ux.Portrait(64));
            var col = Ux.Col("gb-visit-cap-text");
            col.Add(Ux.Text(t.T("nl.rmp.kicker"), "gb-visit-kicker"));
            _title = Ux.Text("", "gb-visit-title", "gb-nowrap");
            col.Add(_title);
            var subRow = Ux.Row("gb-visit-sub");
            _subDot = Ux.El("gb-status-dot");
            subRow.Add(_subDot);
            _sub = Ux.Text("", "gb-nowrap");
            _sub.name = "rmp-sub";
            subRow.Add(_sub);
            col.Add(subRow);
            cap.Add(col);
            root.Add(cap);

            var leave = Ux.IconTextBtn("home", t.T("ranch_mp.lobby.verlassen"), Close, "gb-btn-primary", "gb-visit-end");
            leave.name = "rmp-leave";
            _leaveLabel = leave.Q<Label>();
            Ux.Abs(leave, null, 16, 16);
            root.Add(leave);

            _countdown = Ux.Text("", "gb-h1", "gb-bold", "gb-nowrap");
            _countdown.name = "rmp-countdown";
            _countdown.pickingMode = PickingMode.Ignore;
            _countdown.style.position = Position.Absolute;
            _countdown.style.top = Length.Percent(22);
            _countdown.style.left = 0; _countdown.style.right = 0;
            _countdown.style.unityTextAlign = TextAnchor.MiddleCenter;
            _countdown.style.fontSize = 96;
            _countdown.style.color = RidePaint.Cream;
            _countdown.style.unityTextOutlineWidth = 4f;
            _countdown.style.unityTextOutlineColor = RidePaint.Outline;
            _countdown.style.display = DisplayStyle.None;
            root.Add(_countdown);

            _tag = Ux.IconTextBtn("hand", t.T("nl.rmp.fangen_knopf"), () => { if (_link.TryTag()) { Sfx.PlayUi("soft_tap"); Haptics.Tap(); } }, "gb-btn-primary");
            _tag.name = "rmp-tag";
            _tag.style.position = Position.Absolute;
            _tag.style.bottom = 30;
            _tag.style.left = Length.Percent(50);
            _tag.style.translate = new Translate(Length.Percent(-50), 0);
            _tag.style.minWidth = 220; _tag.style.height = 76;
            _tag.style.display = DisplayStyle.None;
            root.Add(_tag);

            BuildMenu(root);
            BuildBoard(root);
            BuildLobby(root);
            BuildResult(root);
            BuildFrei(root);

            _toastLane = Ux.Col("gb-notice-lane");
            Ux.Abs(_toastLane, null, 104);
            _toastLane.style.left = Length.Percent(50);
            _toastLane.style.translate = new Translate(Length.Percent(-50), 0);
            _toastLane.pickingMode = PickingMode.Ignore;
            root.Add(_toastLane);
            Refresh();
        }

        // ── cards ──

        private static VisualElement Card(string name, float width)
        {
            var card = Ux.El("gb-card", "gb-panel-weiss");
            card.name = name;
            card.style.position = Position.Absolute;
            card.style.top = 96;
            card.style.left = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), 0);
            card.style.width = width;
            card.style.maxHeight = Length.Percent(78);
            card.style.paddingLeft = 22; card.style.paddingRight = 22; card.style.paddingTop = 16; card.style.paddingBottom = 18;
            return card;
        }

        private void BuildMenu(VisualElement root)
        {
            var t = _host.Strings;
            _menu = Card("rmp-menu", 660);
            var head = Ux.Row();
            head.Add(Ux.IconTile("horseshoe", new Color(1f, 0.88f, 0.72f)));
            var hcol = Ux.Col("gb-grow");
            hcol.style.marginLeft = 12;
            hcol.Add(Ux.Text(t.T("nl.rmp.titel"), "gb-h2"));
            var intro = Ux.Text(t.T("nl.rmp.intro"), "gb-sm", "gb-mild");
            intro.style.whiteSpace = WhiteSpace.Normal;
            hcol.Add(intro);
            head.Add(hcol);
            var boardBtn = Ux.IconTextBtn("trophy", t.T("nl.rmp.bestenliste"), () => OpenBoard(), "gb-btn-secondary", "gb-btn-sm");
            boardBtn.name = "rmp-board-open";
            boardBtn.style.marginLeft = 10;
            boardBtn.style.alignSelf = Align.FlexStart;
            head.Add(boardBtn);
            _menu.Add(head);
            var gegen = Ux.Text(t.T("nl.rmp.gegeneinander"), "gb-sm", "gb-bold", "gb-mild");
            gegen.style.marginTop = 10;
            _menu.Add(gegen);
            var seg = Ux.Segment(RmpKurse.MatchModi.Select(m => (m, t.T("ranch_mp.menu." + m))).ToList(), _mode, SelectMode, out var setMatch);
            seg.name = "rmp-modes";
            seg.style.marginTop = 4;
            _menu.Add(seg);
            var mit = Ux.Text(t.T("nl.rmp.miteinander"), "gb-sm", "gb-bold", "gb-mild");
            mit.style.marginTop = 8;
            _menu.Add(mit);
            var frei = Ux.Segment(RmpKurse.FreiModi.Select(m => (m, t.T("ranch_mp.menu." + m))).ToList(), _mode, SelectMode, out var setFrei);
            frei.name = "rmp-modes-frei";
            frei.style.marginTop = 4;
            _menu.Add(frei);
            _setModeChip = id => { setMatch(id); setFrei(id); };
            _menuStats = Ux.Text("", "gb-sm", "gb-bold");
            _menuStats.name = "rmp-stats";
            _menuStats.style.marginTop = 8;
            _menu.Add(_menuStats);
            _menuInvites = Ux.Col();
            _menuInvites.name = "rmp-invites";
            _menu.Add(_menuInvites);
            var friendsHead = Ux.Text(t.T("nl.rmp.freunde_online"), "gb-sm", "gb-bold", "gb-mild");
            friendsHead.style.marginTop = 10;
            _menu.Add(friendsHead);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 220;
            _menuFriends = Ux.Col();
            _menuFriends.name = "rmp-friends";
            scroll.Add(_menuFriends);
            _menu.Add(scroll);
            root.Add(_menu);
        }

        private Action<string> _setModeChip;

        // ── friends leaderboard (ranchmp.js REST): live courses of the Ranch-Treffen + the tournament disciplines ──

        private VisualElement _board, _boardList;
        private Label _boardHint;
        private readonly Dictionary<string, Button> _boardChips = new Dictionary<string, Button>();
        private bool _boardOpen;
        private int _boardRequest;

        /// <summary>The board on show (grasbahn, hof_parcours or rw5_*).</summary>
        public string BoardKurs { get; private set; } = RanchBoards.LiveKurse[0];
        /// <summary>The last board reply for <see cref="BoardKurs"/> (null while loading or offline).</summary>
        public RanchBoard Board { get; private set; }
        public bool BoardOpen => _boardOpen;

        private void BuildBoard(VisualElement root)
        {
            var t = _host.Strings;
            _board = Card("rmp-board", 680);
            _board.style.display = DisplayStyle.None;
            var head = Ux.Row();
            head.Add(Ux.IconTile("trophy", new Color(1f, 0.93f, 0.66f)));
            var hcol = Ux.Col("gb-grow");
            hcol.style.marginLeft = 12;
            hcol.Add(Ux.Text(t.T("ranch_mp.besten.titel"), "gb-h2"));
            var hint = Ux.Text(t.T("nl.rmp.bestenliste_hint"), "gb-sm", "gb-mild");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hcol.Add(hint);
            head.Add(hcol);
            var back = Ux.IconTextBtn("back", t.T("nl.rmp.zurueck"), CloseBoard, "gb-btn-secondary", "gb-btn-sm");
            back.name = "rmp-board-close";
            back.style.alignSelf = Align.FlexStart;
            head.Add(back);
            _board.Add(head);
            _board.Add(BoardSection(t.T("nl.rmp.titel"), RanchBoards.LiveKurse));
            _board.Add(BoardSection(t.T("mg.ranchTurnier.title"), RanchBoards.Rw5Disziplinen.Select(RanchBoards.KursFuer)));
            _boardHint = Ux.Text("", "gb-sm", "gb-mild");
            _boardHint.name = "rmp-board-hint";
            _boardHint.style.whiteSpace = WhiteSpace.Normal;
            _boardHint.style.marginTop = 10;
            _board.Add(_boardHint);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 250;
            scroll.style.marginTop = 4;
            _boardList = Ux.Col();
            _boardList.name = "rmp-board-list";
            scroll.Add(_boardList);
            _board.Add(scroll);
            root.Add(_board);
        }

        private VisualElement BoardSection(string title, IEnumerable<string> kurse)
        {
            var col = Ux.Col();
            col.style.marginTop = 10;
            col.Add(Ux.Text(title, "gb-sm", "gb-bold", "gb-mild"));
            var row = Ux.Row();
            row.style.flexWrap = Wrap.Wrap;
            foreach (var k in kurse)
            {
                var kurs = k;
                var chip = new Button(() => SelectBoard(kurs)) { text = _host.Strings.T("ranch_mp.besten.kurs_" + kurs) };
                chip.ClearClassList();
                chip.AddToClassList("gb-chip");
                chip.name = "rmp-board-" + kurs;
                chip.style.height = 36;
                chip.style.fontSize = 14;
                chip.style.marginRight = 6; chip.style.marginTop = 6;
                chip.style.paddingLeft = 14; chip.style.paddingRight = 14;
                _boardChips[kurs] = chip;
                row.Add(chip);
            }
            col.Add(row);
            return col;
        }

        /// <summary>"Bestenliste" in the menu (QA: <c>rmp board [kurs]</c>).</summary>
        public void OpenBoard(string kurs = null)
        {
            if (Client.IsActive) return;
            _boardOpen = true;
            Sfx.PlayUi("soft_tap");
            SelectBoard(string.IsNullOrEmpty(kurs) ? BoardKurs : kurs);
        }

        public void CloseBoard()
        {
            if (!_boardOpen) return;
            _boardOpen = false;
            Sfx.PlayUi("soft_back");
            _shownKey = "";
            Refresh();
        }

        public void SelectBoard(string kurs)
        {
            if (RanchBoards.Richtung(kurs).Length == 0) return;
            BoardKurs = kurs;
            foreach (var kv in _boardChips) kv.Value.EnableInClassList("gb-chip--active", kv.Key == kurs);
            Board = null;
            _shownKey = "";
            Refresh();
            var t = _host.Strings;
            _boardList.Clear();
            if (!_online.Client.IsOnline)
            {
                _boardHint.text = t.T("ranch_mp.menu.offline_hint");
                return;
            }
            _boardHint.text = "…";
            var request = ++_boardRequest;
            _online.FetchRanchBoard(kurs, b =>
            {
                if (request != _boardRequest || !_boardOpen) return;
                Board = b.Ok ? b : null;
                RenderBoard(b);
            });
        }

        private void RenderBoard(RanchBoard b)
        {
            var t = _host.Strings;
            _boardList.Clear();
            if (!b.Ok)
            {
                _boardHint.text = _online.RanchErrorText(b.Code);
                return;
            }
            var disziplin = RanchBoards.Disziplin(b.Kurs);
            var geistVon = disziplin.Length > 0 ? _online.RanchFriendGhostName(disziplin) : "";
            _boardHint.text = b.Entries.Count == 0 ? t.T("ranch_mp.besten.leer")
                : geistVon.Length > 0 ? t.T("nl.rmp.geist_aktiv", ("name", geistVon))
                : RanchBoards.IstLive(b.Kurs) ? "" : RanchBoards.GeistMoeglich(b.Kurs) ? "" : t.T("nl.rmp.geist_nur_turnier");
            var platz = 0;
            foreach (var e in b.Entries)
            {
                platz++;
                var me = e.Code == b.Me;
                var card = Ux.El("gb-friend-row");
                card.name = "rmp-board-row-" + e.Code;
                card.style.marginTop = 6;
                if (me) card.style.backgroundColor = new Color(1f, 0.95f, 0.82f);
                var row = Ux.Row("gb-friend-main");
                card.Add(row);
                var rank = Ux.Text(platz + ".", "gb-h3", "gb-nowrap");
                rank.style.width = 36;
                row.Add(rank);
                var av = Ux.Portrait(40);
                row.Add(av);
                var info = Ux.Col("gb-grow");
                info.style.marginLeft = 10;
                info.Add(Ux.Text(me ? e.Name + " " + t.T("ranch_mp.besten.du") : e.Name, "gb-bold", "gb-nowrap"));
                info.Add(Ux.Text(e.Gooby, "gb-sm", "gb-mild", "gb-nowrap"));
                row.Add(info);
                var culture = _host.Strings.Locale == "de" ? new System.Globalization.CultureInfo("de-DE") : System.Globalization.CultureInfo.InvariantCulture;
                var wert = RanchBoards.Anzeige(b.Kurs, e.Wert, culture) + (RanchBoards.IstZeit(b.Kurs) ? " s" : " " + t.T("nl.rmp.punkte_kurz"));
                var value = Ux.Text(wert, "gb-h3", "gb-nowrap");
                value.name = "rmp-board-value-" + e.Code;
                value.style.marginLeft = 10;
                row.Add(value);
                if (!me && e.HatGhost && RanchBoards.GeistMoeglich(b.Kurs))
                {
                    var code = e.Code;
                    var name = e.Name;
                    var loaded = geistVon == name;
                    var ghost = Ux.IconTextBtn(loaded ? "check" : "sparkle", t.T(loaded ? "ranch_mp.besten.geist_ok" : "ranch_mp.besten.geist_laden"), () => LoadGhost(code), loaded ? "gb-btn-confirm" : "gb-btn-secondary", "gb-btn-sm");
                    ghost.name = "rmp-ghost-" + code;
                    ghost.style.marginLeft = 10;
                    row.Add(ghost);
                }
                _boardList.Add(card);
            }
        }

        /// <summary>"Geist laden" on a board line: the friend's best run becomes the ghost of the next tournament run.</summary>
        public void LoadGhost(string friendCode)
        {
            var b = Board;
            var entry = b?.Entries.FirstOrDefault(e => e.Code == friendCode);
            if (b == null || entry == null || !RanchBoards.GeistMoeglich(b.Kurs)) return;
            var t = _host.Strings;
            var kurs = b.Kurs;
            _online.LoadRanchGhost(kurs, friendCode, entry.Name, (ok, code) =>
            {
                if (!ok)
                {
                    Toast(_online.RanchErrorText(code), "sparkle");
                    return;
                }
                Sfx.PlayUi("soft_confirm");
                Toast(t.T("nl.rmp.geist_bereit", ("name", entry.Name), ("disziplin", t.T("ranch_mp.besten.kurs_" + kurs))), "sparkle");
                if (_boardOpen && BoardKurs == kurs && Board != null) RenderBoard(Board);
            });
        }

        /// <summary>
        /// The game the menu invites to (rennen | parcours | fangen, or the free rides ausritt | besuch) — the meadow shows its
        /// course, for a free ride the Weidetal.
        /// </summary>
        public void SelectMode(string mode)
        {
            if (Array.IndexOf(RmpKurse.MenuModi, mode) < 0) return;
            _mode = mode;
            _setModeChip?.Invoke(mode);
            ShowMeadow(mode, RmpKurse.StandardKurs(mode));
            Refresh();
        }

        private void ShowMeadow(string mode, string kurs)
        {
            if (_view == null) return;
            if (RmpKurse.IstFrei(mode)) _view.ShowFrei();
            else _view.ShowKurs(kurs);
        }

        public string MenuMode => _mode;

        private void BuildLobby(VisualElement root)
        {
            var t = _host.Strings;
            _lobby = Card("rmp-lobby", 560);
            _lobbyTitle = Ux.Text("", "gb-h2");
            _lobby.Add(_lobbyTitle);
            _lobbyPlayers = Ux.Col();
            _lobbyPlayers.name = "rmp-players";
            _lobbyPlayers.style.marginTop = 8;
            _lobby.Add(_lobbyPlayers);
            _lobbyHint = Ux.Text("", "gb-sm", "gb-mild");
            _lobbyHint.style.whiteSpace = WhiteSpace.Normal;
            _lobbyHint.style.marginTop = 8;
            _lobby.Add(_lobbyHint);
            var row = Ux.Row();
            row.style.marginTop = 12;
            row.style.justifyContent = Justify.FlexEnd;
            _readyBtn = Ux.IconTextBtn("check", t.T("ranch_mp.lobby.bereit_knopf"), () =>
            {
                _readyBtn.SetEnabled(false);
                Client.SetReady(r =>
                {
                    if (!r.Ok) { _readyBtn?.SetEnabled(true); Toast(_online.RanchErrorText(r.Code), "horseshoe"); }
                    Refresh();
                });
                Sfx.PlayUi("soft_confirm");
            }, "gb-btn-primary");
            _readyBtn.name = "rmp-ready";
            row.Add(_readyBtn);
            _lobby.Add(row);
            root.Add(_lobby);
        }

        private void BuildResult(VisualElement root)
        {
            var t = _host.Strings;
            _result = Card("rmp-result", 560);
            _resultTitle = Ux.Text(t.T("ranch_mp.ergebnis.titel"), "gb-h2");
            _result.Add(_resultTitle);
            _resultRows = Ux.Col();
            _resultRows.name = "rmp-result-rows";
            _resultRows.style.marginTop = 8;
            _result.Add(_resultRows);
            _rematchHint = Ux.Text("", "gb-sm", "gb-mild");
            _rematchHint.style.marginTop = 6;
            _result.Add(_rematchHint);
            var row = Ux.Row();
            row.style.marginTop = 12;
            row.style.justifyContent = Justify.FlexEnd;
            _rematchBtn = Ux.IconTextBtn("redo", t.T("ranch_mp.ergebnis.revanche"), () =>
            {
                _rematchBtn.SetEnabled(false);
                Client.RequestRematch(r =>
                {
                    if (!r.Ok) { _rematchBtn?.SetEnabled(true); Toast(_online.RanchErrorText(r.Code), "horseshoe"); }
                    Refresh();
                });
                Sfx.PlayUi("soft_confirm");
            }, "gb-btn-secondary");
            _rematchBtn.name = "rmp-rematch";
            row.Add(_rematchBtn);
            var done = Ux.IconTextBtn("check", t.T("ranch_mp.ergebnis.fertig"), Close, "gb-btn-primary");
            done.name = "rmp-done";
            done.style.marginLeft = 10;
            row.Add(done);
            _result.Add(row);
            root.Add(_result);
        }

        // ── state → view ──

        private enum Stage { Menu, Lobby, Run, Result, Frei }

        /// <summary>QA: menu | lobby | run | result | frei.</summary>
        public string StageName => Current.ToString().ToLowerInvariant();

        private Stage Current
        {
            get
            {
                if (!Client.IsActive) return Stage.Menu;
                if (Client.IsFree) return Stage.Frei;
                if (Client.Phase == RanchMpClient.PhaseDone && _link.LastResult != null) return Stage.Result;
                if (Client.Phase == RanchMpClient.PhaseCountdown || Client.Phase == RanchMpClient.PhaseRun || Client.Phase == RanchMpClient.PhaseDone) return Stage.Run;
                return Stage.Lobby;
            }
        }

        private void Refresh()
        {
            if (_title == null) return;
            var t = _host.Strings;
            var stage = Current;
            var mode = Client.IsActive ? Client.Mode : _mode;
            var kurs = Client.IsActive ? Client.Kurs : RmpKurse.StandardKurs(_mode);
            if (Client.IsActive) _activeMode = Client.Mode;
            ShowMeadow(mode, kurs);
            UpdateYard(stage, mode);
            _title.text = Title(stage, mode);
            _leaveLabel.text = t.T(stage == Stage.Frei && Client.Mode == RmpKurse.Besuch ? "ranch_mp.besuch.beenden" : "ranch_mp.lobby.verlassen");
            if (stage != Stage.Menu) _boardOpen = false;
            if (stage != Stage.Frei) _dazuOpen = false;
            // the cards sit at the top: their toasts go to the bottom edge; on the ride the top centre is free
            var riding = stage == Stage.Run || stage == Stage.Frei && !_dazuOpen;
            if (_toastLane != null)
            {
                _toastLane.style.top = riding ? new StyleLength(104f) : new StyleLength(StyleKeyword.Auto);
                _toastLane.style.bottom = riding ? new StyleLength(StyleKeyword.Auto) : new StyleLength(24f);
            }
            _menu.style.display = stage == Stage.Menu && !_boardOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _board.style.display = stage == Stage.Menu && _boardOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _lobby.style.display = stage == Stage.Lobby ? DisplayStyle.Flex : DisplayStyle.None;
            _result.style.display = stage == Stage.Result ? DisplayStyle.Flex : DisplayStyle.None;
            _freiBar.style.display = stage == Stage.Frei ? DisplayStyle.Flex : DisplayStyle.None;
            _visitCard.style.display = stage == Stage.Frei && IsVisitGuest ? DisplayStyle.Flex : DisplayStyle.None;
            _dazuCard.style.display = stage == Stage.Frei && _dazuOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _tag.style.display = stage == Stage.Run && Client.Mode == RmpKurse.Fangen && Client.IAmIt && _link.Running ? DisplayStyle.Flex : DisplayStyle.None;
            var key = stage + "|" + Client.RoomId + "|" + Client.Players.Count + "|" + Client.PeerHorses.Count + "|" + string.Join(",", Client.Ready) + "|" + _online.Client.Friends.Count(f => f.Online) + "|" + Client.IncomingInvites.Count + "|" + Client.OutgoingInvite + "|" + (_link.LastResult?.RewardId ?? "") + "|" + Client.RematchWanted.Count + "|" + _mode + "|" + _dazuOpen + "|" + (_online.RanchVisitMeta != null);
            if (key != _shownKey)
            {
                _shownKey = key;
                if (stage == Stage.Menu) RefreshMenu();
                if (stage == Stage.Lobby) RefreshLobby();
                if (stage == Stage.Result) RefreshResult();
                if (stage == Stage.Frei) RefreshFrei();
            }
            _sub.text = SubLine(stage);
            var live = (stage == Stage.Run || stage == Stage.Frei) && _link.Running;
            _subDot.EnableInClassList("gb-status-dot--on", live || stage == Stage.Lobby && Client.Players.Count >= 2);
            _subDot.EnableInClassList("gb-status-dot--wait", !live);
        }

        private string SubLine(Stage stage)
        {
            var t = _host.Strings;
            switch (stage)
            {
                case Stage.Menu:
                    return _online.Client.IsOnline ? t.T("ranch_mp.menu.title") : t.T("ranch_mp.menu.offline_hint");
                case Stage.Lobby:
                    return Client.Players.Count < 2 ? t.T("ranch_mp.lobby.leer") : t.T("ranch_mp.lobby.spieler", ("n", Client.Players.Count), ("max", RmpKurse.MaxSpieler(Client.Mode)));
                case Stage.Result:
                    return t.T("ranch_mp.lobby.beendet");
                case Stage.Frei:
                    return t.T("nl.rmp.frei_sub", ("n", Math.Max(1, Client.Players.Count)), ("k", _link.Lauf?.Entdeckt.Count ?? 0), ("max", RanchKarte.Zonen.Count));
            }
            var lauf = _link.Lauf;
            if (lauf == null) return t.T("ranch_mp.lobby.leer");
            if (Client.CountdownMs > 0) return t.T("ranch_mp.lobby.countdown", ("s", (int)Math.Ceiling(Client.CountdownMs / 1000.0)));
            if (Client.Mode == RmpKurse.Fangen)
            {
                var left = Math.Max(0, (int)Math.Ceiling((Client.EndsAtMs - Client.ServerNowMs) / 1000.0));
                var who = Client.IAmIt ? t.T("ranch_mp.lauf.du_bist_faenger") : t.T("ranch_mp.lauf.faenger", ("name", Client.NameOf(Client.ItCode)));
                return who + " · " + t.T("ranch_mp.lauf.restzeit", ("s", left));
            }
            if (lauf.InZiel) return t.T("ranch_mp.lauf.ziel") + " · " + t.T("nl.rmp.warte_ziel");
            return t.T("ranch_mp.lauf.checkpoint", ("n", Math.Min(lauf.CheckpointCount, lauf.NextCheckpoint + 1)), ("max", lauf.CheckpointCount)) + " · " + t.T("ranch_mp.lauf.platz", ("n", Platz()));
        }

        /// <summary>Live place in a race: finished riders by rank, then everyone by gates passed (the server's progress).</summary>
        private int Platz()
        {
            var me = Client.MyCode;
            if (Client.Finished.TryGetValue(me, out var fin)) return fin.rank;
            var mine = Math.Max(_link.Lauf?.NextCheckpoint ?? 0, Client.Progress.TryGetValue(me, out var p) ? p : 0);
            var vor = Client.Finished.Count;
            foreach (var kv in Client.Progress)
                if (kv.Key != me && !Client.Finished.ContainsKey(kv.Key) && !Client.Dnf.Contains(kv.Key) && kv.Value > mine) vor++;
            return vor + 1;
        }

        private void RefreshMenu()
        {
            var t = _host.Strings;
            if (_mode == RmpKurse.Ausritt) _menuStats.text = t.T("nl.rmp.ausritt_hint", ("n", RanchKarte.Zonen.Count));
            else if (_mode == RmpKurse.Besuch)
            {
                var pferde = (_yardMeta?["pferde"] as JArray)?.Count ?? 0;
                _menuStats.text = pferde > 0 ? t.T("nl.rmp.besuch_hint", ("n", pferde)) : t.T("nl.rmp.besuch_hint_leer");
            }
            else
            {
                var (teil, siege) = RmpState.Statistik(_host.Store.State, _mode);
                _menuStats.text = t.T("nl.rmp.statistik", ("siege", siege), ("teilnahmen", teil));
            }
            _menuStats.style.whiteSpace = WhiteSpace.Normal;
            _menuInvites.Clear();
            foreach (var inv in Client.IncomingInvites)
            {
                var card = Ux.El("gb-friend-row");
                card.style.marginTop = 8;
                var row = Ux.Row("gb-friend-main");
                card.Add(row);
                var spiel = t.T("ranch_mp.menu." + inv.Mode);
                var text = Ux.Text(t.T("ranch_mp.menu.invite_von", ("name", inv.Name), ("spiel", spiel)), "gb-bold", "gb-grow");
                text.style.whiteSpace = WhiteSpace.Normal;
                row.Add(text);
                var from = inv.From;
                var yes = Ux.IconTextBtn("check", t.T("ranch_mp.menu.annehmen"), () => _online.AcceptRanch(from), "gb-btn-primary", "gb-btn-sm");
                yes.name = "rmp-accept-" + from;
                row.Add(yes);
                var no = Ux.IconTextBtn("close", t.T("ranch_mp.menu.ablehnen"), () => { Client.Decline(from); Refresh(); }, "gb-btn-secondary", "gb-btn-sm");
                no.style.marginLeft = 6;
                row.Add(no);
                _menuInvites.Add(card);
            }
            _menuFriends.Clear();
            var online = _online.Client.Friends.Where(f => f.Online).ToList();
            if (online.Count == 0)
            {
                var empty = Ux.Text(t.T("ranch_mp.menu.keine_freunde"), "gb-sm", "gb-mild");
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.marginTop = 6;
                _menuFriends.Add(empty);
                return;
            }
            foreach (var f in online)
            {
                var card = Ux.El("gb-friend-row");
                card.name = "rmp-friend-" + f.Code;
                card.style.marginTop = 6;
                var row = Ux.Row("gb-friend-main");
                card.Add(row);
                var av = Ux.Portrait(44);
                var dot = Ux.El("gb-online-dot", "gb-online-dot--on");
                av.Add(dot);
                row.Add(av);
                var info = Ux.Col("gb-grow");
                info.style.marginLeft = 10;
                info.Add(Ux.Text(f.Name, "gb-bold", "gb-nowrap"));
                info.Add(Ux.Text(f.GoobyName.Length > 0 ? f.GoobyName + " — " + (f.ActivityLabel.Length > 0 ? f.ActivityLabel : t.T("net.friends.online")) : t.T("net.friends.online"), "gb-sm", "gb-mild", "gb-nowrap"));
                row.Add(info);
                var code = f.Code;
                var pending = Client.OutgoingInvite == code;
                var btn = Ux.IconTextBtn("horseshoe", pending ? t.T("ranch_mp.lobby.warten") : t.T("ranch_mp.menu.einladen"), () =>
                {
                    _online.InviteToRanch(code, _mode, (ok, c) =>
                    {
                        if (ok) Toast(t.T("ranch_mp.menu.invite_gesendet", ("name", f.Name)), "horseshoe");
                        else Toast(_online.RanchErrorText(c), "horseshoe");
                        _shownKey = "";
                        Refresh();
                    });
                }, "gb-btn-primary", "gb-btn-sm");
                btn.name = "rmp-invite-" + code;
                btn.SetEnabled(!pending && _online.Client.IsOnline);
                row.Add(btn);
                _menuFriends.Add(card);
            }
        }

        private void RefreshLobby()
        {
            var t = _host.Strings;
            _lobbyTitle.text = t.T("ranch_mp.lobby.titel", ("spiel", t.T("ranch_mp.menu." + Client.Mode)));
            _lobbyPlayers.Clear();
            foreach (var p in Client.Players)
            {
                var card = Ux.El("gb-friend-row");
                card.name = "rmp-player-" + p.Code;
                card.style.marginTop = 6;
                var row = Ux.Row("gb-friend-main");
                card.Add(row);
                row.Add(Ux.Portrait(40));
                var who = Ux.Col("gb-grow");
                who.style.marginLeft = 10;
                who.Add(Ux.Text(p.Name + (p.Code == Client.MyCode ? " " + t.T("ranch_mp.besten.du") : ""), "gb-bold", "gb-nowrap"));
                // the horse this rider brings: the own riding horse, a friend's once their PFERD came in
                var horse = p.Code == Client.MyCode ? Client.OwnHorse?.Invoke() : Client.PeerHorses.TryGetValue(p.Code, out var look) ? look : null;
                if (horse != null)
                {
                    var line = Ux.Row();
                    line.name = "rmp-player-horse-" + p.Code;
                    line.style.alignItems = Align.Center;
                    line.style.marginTop = 2;
                    var dot = Ux.El("gb-rmp-horse-dot");
                    dot.style.backgroundColor = RanchPferdLook.Fell(horse).fell;
                    dot.style.borderTopColor = dot.style.borderBottomColor = dot.style.borderLeftColor = dot.style.borderRightColor = new Color(0.36f, 0.27f, 0.22f, 0.35f);
                    line.Add(dot);
                    line.Add(Ux.Text(J.Str(horse["name"], "?"), "gb-sm", "gb-mild", "gb-nowrap"));
                    who.Add(line);
                }
                row.Add(who);
                var ready = Client.Ready.Contains(p.Code);
                if (ready)
                {
                    var tick = Ux.Icon("check");
                    tick.style.width = 22; tick.style.height = 22; tick.style.marginRight = 4;
                    row.Add(tick);
                }
                var state = Ux.Text(ready ? t.T("ranch_mp.lobby.bereit") : t.T("ranch_mp.lobby.warten"), "gb-sm", "gb-bold");
                state.style.color = ready ? MgScenery.Hex(0x4E9A5B) : MgScenery.Hex(0x9A8B80);
                row.Add(state);
                _lobbyPlayers.Add(card);
            }
            _readyBtn.SetEnabled(!Client.IAmReady && _online.Client.IsOnline);
            _lobbyHint.text = Client.Players.Count < 2 ? t.T("ranch_mp.lobby.leer") : t.T("nl.rmp.lobby_hint");
        }

        private void RefreshResult()
        {
            var t = _host.Strings;
            var r = _link.LastResult;
            _resultRows.Clear();
            if (r == null) return;
            var rows = r.All.Count > 0 ? r.All : new List<RmpResult> { r };
            foreach (var line in rows)
            {
                var card = Ux.El("gb-friend-row");
                card.name = "rmp-result-" + line.FriendCode;
                card.style.marginTop = 6;
                var row = Ux.Row("gb-friend-main");
                card.Add(row);
                var me = line.FriendCode == Client.MyCode;
                var rank = Ux.Text(line.Dnf ? "–" : line.Rank + ".", "gb-h3", "gb-bold");
                rank.style.minWidth = 40;
                rank.style.color = line.Rank == 1 && !line.Dnf ? MgScenery.Hex(0xD99A1E) : RidePaint.Outline;
                row.Add(rank);
                var name = Ux.Text(Client.NameOf(line.FriendCode) + (me ? " " + t.T("ranch_mp.besten.du") : ""), me ? "gb-bold" : "gb-md", "gb-grow", "gb-nowrap");
                row.Add(name);
                string wert;
                if (line.Dnf) wert = t.T("ranch_mp.ergebnis.dnf");
                else wert = t.T("ranch_mp.ergebnis.zeit", ("s", (line.ZeitMs / 1000.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo(_host.Strings.Locale == "en" ? "en-US" : "de-DE"))));
                if (!line.Dnf && !line.Ranked) wert += " · " + t.T("ranch_mp.ergebnis.unranked");
                row.Add(Ux.Text(wert, "gb-md", "gb-bold"));
                _resultRows.Add(card);
            }
            var waiting = Client.RematchWanted.Contains(Client.MyCode);
            _rematchBtn.SetEnabled(!waiting && Client.IsActive && Client.Players.Count >= 2);
            _rematchHint.text = waiting ? t.T("ranch_mp.ergebnis.revanche_warte") : "";
        }

        // ── the free ride: Besuch / Ausritt on the Weidetal ──

        private VisualElement _freiBar, _visitCard, _visitLines, _dazuCard, _dazuList, _herzBtn, _folgeBtn, _streichelnBtn, _fuetternBtn, _dazuBtn;
        private Label _visitTitle, _dazuHint, _folgeLabel, _leaveLabel;
        private bool _folgeMir, _dazuOpen;
        private string _activeMode = "", _yardKey = "";
        private JObject _yardMeta;
        private readonly Dictionary<string, int> _reactionsIn = new Dictionary<string, int>();

        /// <summary>QA: reactions received this session by kind (HERZ, GESTE, FOLGE_MIR, ENTDECKUNG).</summary>
        public IReadOnlyDictionary<string, int> ReactionsIn => _reactionsIn;
        public bool FolgeMir => _folgeMir;
        public bool DazuOpen => _dazuOpen;

        /// <summary>A visit to a friend's ranch (we are the guest: the host's horses, the gestures, the ranch card).</summary>
        private bool IsVisitGuest => Client.IsActive && Client.Mode == RmpKurse.Besuch && Client.Host.Length > 0 && Client.Host != Client.MyCode;

        private void BuildFrei(VisualElement root)
        {
            var t = _host.Strings;
            // bottom right, left of the gait buttons: board-08 action discs, the heart is the big one
            _freiBar = Ux.Row("gb-visit-emotes");
            _freiBar.name = "rmp-react";
            Ux.Abs(_freiBar, null, null, 118, 30);
            _dazuBtn = ActionDisc("rmp-dazu", "friends", t.T("ranch_mp.menu.einladen"), ToggleDazu);
            _streichelnBtn = ActionDisc("rmp-streicheln", "hand", t.T("ranch_mp.besuch.streicheln"), () => SendGeste("streicheln"));
            _fuetternBtn = ActionDisc("rmp-fuettern", "food_apple", t.T("ranch_mp.besuch.fuettern"), () => SendGeste("fuettern"));
            _folgeBtn = ActionDisc("rmp-folge", "race_flag", t.T("ranch_mp.ausritt.folge_mir"), ToggleFolgeMir);
            _folgeLabel = _folgeBtn.Q<Label>();
            _herzBtn = ActionDisc("rmp-herz", "heart", t.T("nl.rmp.herz"), SendHerz, main: true);
            foreach (var b in new[] { _dazuBtn, _streichelnBtn, _fuetternBtn, _folgeBtn, _herzBtn }) _freiBar.Add(b);
            _freiBar.style.display = DisplayStyle.None;
            root.Add(_freiBar);

            // a visit: the host's ranch (build-out, trophies, horses) in the right column under "Besuch beenden"
            _visitCard = Ux.Col("gb-rmp-visit");
            _visitCard.name = "rmp-visit";
            Ux.Abs(_visitCard, null, 92, 16);
            _visitCard.pickingMode = PickingMode.Ignore;
            var head = Ux.Row("gb-rmp-visit-head");
            head.Add(Ux.Icon("horseshoe"));
            _visitTitle = Ux.Text("", "gb-rmp-visit-title");
            _visitTitle.name = "rmp-visit-title";
            head.Add(_visitTitle);
            _visitCard.Add(head);
            _visitLines = Ux.Col();
            _visitLines.name = "rmp-visit-lines";
            _visitCard.Add(_visitLines);
            _visitCard.style.display = DisplayStyle.None;
            root.Add(_visitCard);

            // a trail ride takes friends at any time (ranchmp.js): the invite card over the meadow
            _dazuCard = Card("rmp-dazu-card", 540);
            var dhead = Ux.Row();
            dhead.Add(Ux.IconTile("friends", new Color(0.82f, 0.93f, 1f)));
            var dcol = Ux.Col("gb-grow");
            dcol.style.marginLeft = 12;
            dcol.Add(Ux.Text(t.T("nl.rmp.dazuholen_titel"), "gb-h2"));
            _dazuHint = Ux.Text("", "gb-sm", "gb-mild");
            _dazuHint.style.whiteSpace = WhiteSpace.Normal;
            dcol.Add(_dazuHint);
            dhead.Add(dcol);
            var close = Ux.IconTextBtn("check", t.T("ranch_mp.ergebnis.fertig"), ToggleDazu, "gb-btn-secondary", "gb-btn-sm");
            close.name = "rmp-dazu-close";
            close.style.alignSelf = Align.FlexStart;
            dhead.Add(close);
            _dazuCard.Add(dhead);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 260;
            scroll.style.marginTop = 6;
            _dazuList = Ux.Col();
            _dazuList.name = "rmp-dazu-list";
            scroll.Add(_dazuList);
            _dazuCard.Add(scroll);
            _dazuCard.style.display = DisplayStyle.None;
            root.Add(_dazuCard);
        }

        private static VisualElement ActionDisc(string name, string icon, string label, Action act, bool main = false)
        {
            var a = Ux.El("gb-action");
            if (main) a.AddToClassList("gb-action--main");
            a.name = name;
            a.Add(Ux.Icon(icon));
            var text = Ux.Text(label, "gb-nowrap");
            text.pickingMode = PickingMode.Ignore;
            a.Add(text);
            a.RegisterCallback<ClickEvent>(_ => act());
            return a;
        }

        private string Title(Stage stage, string mode)
        {
            var t = _host.Strings;
            if (stage == Stage.Frei && Client.Mode == RmpKurse.Besuch)
            {
                if (IsVisitGuest) return t.T("ranch_mp.besuch.titel", ("name", Client.NameOf(Client.Host)));
                var gast = Client.Players.FirstOrDefault(p => p.Code != Client.MyCode);
                if (gast != null) return t.T("ranch_mp.besuch.gast", ("name", gast.Name));
            }
            return t.T("ranch_mp.menu." + (mode.Length > 0 ? mode : RmpKurse.Rennen));
        }

        /// <summary>
        /// The horses standing around the yard: on a visit the host's (the guest loads them from the server), otherwise the
        /// own — a trail ride starts at everyone's own yard like the Godot riders in their own ranch worlds. The horse a
        /// rider is out on does not stand there as well.
        /// </summary>
        private void UpdateYard(Stage stage, string mode)
        {
            var frei = RmpKurse.IstFrei(mode);
            var guest = stage == Stage.Frei && IsVisitGuest;
            var meta = guest ? _online.RanchVisitMeta : null;
            var state = _host.Store.State;
            var hostReitet = guest && Client.PeerHorses.TryGetValue(Client.Host, out var hostPferd) ? J.Str(hostPferd["name"]) : "";
            var reiten = stage == Stage.Frei;
            var key = !frei ? "" : guest ? "gast:" + Client.Host + ":" + (meta != null) + ":" + hostReitet : "eigen:" + (reiten ? RanchHof.ReitPferd(state) : "");
            if (key == _yardKey) return;
            _yardKey = key;
            _yardMeta = !frei ? null : guest ? meta : RmpRanchMeta.Build(state, _online.Client.PlayerName, _online.Client.GoobyName);
            var pferde = (_yardMeta?["pferde"] as JArray ?? new JArray()).OfType<JObject>()
                .Select(p => (p["name"]?.ToString() ?? "?", p["farbe"]?.ToString() ?? "braun")).ToList();
            // the meta lists the horses in the save's order: the own riding horse by its place, the host's by the name it rides under
            var weg = !frei || !reiten ? -1 : guest
                ? hostReitet.Length == 0 ? -1 : pferde.FindIndex(p => p.Item1 == hostReitet || hostReitet.Length == RmpPferdLook.MaxNameLen && p.Item1.StartsWith(hostReitet, StringComparison.Ordinal))
                : (J.Path(state, RanchHof.SliceId, "tiere", "pferde") as JObject)?.Properties().Select(p => p.Name).ToList().IndexOf(RanchHof.ReitPferd(state)) ?? -1;
            if (weg >= 0 && weg < pferde.Count) pferde.RemoveAt(weg);
            _view.SetBesuchPferde(pferde);
            // beside the paddock stands the host's build on a visit, the own otherwise
            _view.SetHofbau(guest ? RmpRanchMeta.BauFuerGast(meta) : RanchHof.IstGekauft(state) ? RanchBauState.Lese(state) : null);
        }

        private void RefreshFrei()
        {
            var t = _host.Strings;
            var besuch = Client.Mode == RmpKurse.Besuch;
            var pferde = _view.BesuchPferdeAnzahl;
            Show(_dazuBtn, !besuch && Client.Players.Count < RmpKurse.MaxSpieler(Client.Mode));
            Show(_streichelnBtn, IsVisitGuest && pferde > 0);
            Show(_fuetternBtn, IsVisitGuest && pferde > 0);
            UpdateFolgeBtn();
            if (IsVisitGuest) RefreshVisitCard();
            if (_dazuOpen) RefreshDazu();
        }

        private static void Show(VisualElement el, bool on) => el.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        private void RefreshVisitCard()
        {
            var t = _host.Strings;
            var meta = _online.RanchVisitMeta;
            _visitTitle.text = t.T("nl.rmp.besuch_karte", ("name", Client.NameOf(Client.Host)));
            _visitLines.Clear();
            if (meta == null)
            {
                _visitLines.Add(Ux.Text(t.T("nl.rmp.besuch_laedt"), "gb-rmp-visit-line"));
                return;
            }
            var ausbau = meta["ausbau"] as JObject;
            var extras = (ausbau?["reitplatz"]?.Type == JTokenType.Boolean && (bool)ausbau["reitplatz"] ? t.T("ranch_mp.besuch.reitplatz") : "")
                + (ausbau?["weidezaun"]?.Type == JTokenType.Boolean && (bool)ausbau["weidezaun"] ? t.T("ranch_mp.besuch.weidezaun") : "");
            _visitLines.Add(Ux.Text(t.T("ranch_mp.besuch.ausbau", ("boxen", (int?)ausbau?["boxen"] ?? 1), ("extras", extras)), "gb-rmp-visit-line"));
            _visitLines.Add(Ux.Text(t.T("ranch_mp.besuch.trophaeen", ("n", (meta["trophaeen"] as JArray)?.Count ?? 0), ("n2", (int?)meta["schleifen"] ?? 0)), "gb-rmp-visit-line"));
            _visitLines.Add(Ux.Text(t.T("ranch_mp.besuch.pferde").ToUpperInvariant(), "gb-rmp-visit-kicker"));
            var pferde = (meta["pferde"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (pferde.Count == 0) _visitLines.Add(Ux.Text(t.T("nl.rmp.besuch_keine_pferde"), "gb-rmp-visit-line"));
            const int zeigen = 4;
            for (var i = 0; i < pferde.Count && i < zeigen; i++)
            {
                var p = pferde[i];
                var row = Ux.Row("gb-rmp-horse");
                row.name = "rmp-visit-horse-" + i;
                var dot = Ux.El("gb-rmp-horse-dot");
                dot.style.backgroundColor = RcompRideView.Fell.TryGetValue(p["farbe"]?.ToString() ?? "", out var fell) ? fell.hell : RcompRideView.Fell["braun"].hell;
                row.Add(dot);
                row.Add(Ux.Text(p["name"]?.ToString() ?? "?", "gb-rmp-horse-name"));
                row.Add(Ux.Text(t.T("nl.rmp.pferd_kurz", ("rasse", Rasse(p["rasse"]?.ToString())), ("level", (int?)p["level"] ?? 1)), "gb-rmp-horse-meta"));
                _visitLines.Add(row);
            }
            if (pferde.Count > zeigen) _visitLines.Add(Ux.Text(t.T("nl.rmp.besuch_mehr", ("n", pferde.Count - zeigen)), "gb-rmp-visit-line"));
        }

        private string Rasse(string id)
        {
            var t = _host.Strings;
            if (string.IsNullOrEmpty(id)) return "?";
            return t.HasKey("rpferd.rasse." + id) ? t.T("rpferd.rasse." + id) : char.ToUpperInvariant(id[0]) + id.Substring(1);
        }

        /// <summary>Landmark names: the Weidetal's own, a Godot rider's ranch zone id as a readable word.</summary>
        private string OrtLabel(string id)
        {
            var t = _host.Strings;
            if (string.IsNullOrEmpty(id)) return "?";
            if (t.HasKey("rwelt.zone." + id)) return t.T("rwelt.zone." + id);
            if (t.HasKey("nl.rmp.ort." + id)) return t.T("nl.rmp.ort." + id);
            var words = id.Replace('-', '_').Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        private void ToggleDazu()
        {
            if (Current != Stage.Frei) return;
            _dazuOpen = !_dazuOpen;
            Sfx.PlayUi(_dazuOpen ? "soft_tap" : "soft_back");
            _shownKey = "";
            Refresh();
        }

        private void RefreshDazu()
        {
            var t = _host.Strings;
            _dazuList.Clear();
            var voll = Client.Players.Count >= RmpKurse.MaxSpieler(Client.Mode);
            _dazuHint.text = voll ? t.T("nl.rmp.dazuholen_voll") : t.T("nl.rmp.dazuholen_hint");
            if (voll) return;
            var dabei = new HashSet<string>(Client.Players.Select(p => p.Code));
            var online = _online.Client.Friends.Where(f => f.Online && !dabei.Contains(f.Code)).ToList();
            if (online.Count == 0)
            {
                var empty = Ux.Text(t.T("ranch_mp.menu.keine_freunde"), "gb-sm", "gb-mild");
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.marginTop = 6;
                _dazuList.Add(empty);
                return;
            }
            foreach (var f in online)
            {
                var card = Ux.El("gb-friend-row");
                card.name = "rmp-dazu-friend-" + f.Code;
                card.style.marginTop = 6;
                var row = Ux.Row("gb-friend-main");
                card.Add(row);
                var av = Ux.Portrait(44);
                av.Add(Ux.El("gb-online-dot", "gb-online-dot--on"));
                row.Add(av);
                var info = Ux.Col("gb-grow");
                info.style.marginLeft = 10;
                info.Add(Ux.Text(f.Name, "gb-bold", "gb-nowrap"));
                info.Add(Ux.Text(f.GoobyName.Length > 0 ? f.GoobyName + " — " + (f.ActivityLabel.Length > 0 ? f.ActivityLabel : t.T("net.friends.online")) : t.T("net.friends.online"), "gb-sm", "gb-mild", "gb-nowrap"));
                row.Add(info);
                var code = f.Code;
                var name = f.Name;
                var pending = Client.OutgoingInvite == code;
                var btn = Ux.IconTextBtn("horseshoe", pending ? t.T("ranch_mp.lobby.warten") : t.T("ranch_mp.menu.einladen"), () =>
                {
                    _online.InviteToRanch(code, Client.Mode, (ok, c) =>
                    {
                        Toast(ok ? t.T("ranch_mp.menu.invite_gesendet", ("name", name)) : _online.RanchErrorText(c), "horseshoe");
                        _shownKey = "";
                        Refresh();
                    });
                }, "gb-btn-primary", "gb-btn-sm");
                btn.name = "rmp-dazu-invite-" + code;
                btn.SetEnabled(!pending && _online.Client.IsOnline);
                row.Add(btn);
                _dazuList.Add(card);
            }
        }

        /// <summary>Reactions are cosmetic and rate-limited on our side as well — per button, one tap ≈ one reaction.</summary>
        private bool CanReact(string action)
        {
            if (Current != Stage.Frei || !_link.Running) return false;
            var now = Time.unscaledTime;
            if (_reactAt.TryGetValue(action, out var at) && now - at < ReactPauseS) return false;
            _reactAt[action] = now;
            return true;
        }

        private const float ReactPauseS = 0.6f;
        private readonly Dictionary<string, float> _reactAt = new Dictionary<string, float>();

        /// <summary>"Herz" (QA: <c>click rmp-herz</c>): a heart floats up over the own rider, everyone in the room sees it.</summary>
        public void SendHerz()
        {
            if (!CanReact("herz") || !Client.SendReaction("HERZ")) return;
            _view.Herz(Client.MyCode);
            Sfx.PlayUi("soft_tap");
            Haptics.Tap();
        }

        /// <summary>"Folge mir!" on/off: a gold flag over the own rider for everyone.</summary>
        public void ToggleFolgeMir()
        {
            if (!CanReact("folge")) return;
            var an = !_folgeMir;
            if (!Client.SendReaction("FOLGE_MIR", new JObject { ["an"] = an })) return;
            _folgeMir = an;
            _view.SetFolgeMir(Client.MyCode, an);
            UpdateFolgeBtn();
            Sfx.PlayUi(an ? "soft_confirm" : "soft_back");
            if (an) Toast(_host.Strings.T("nl.rmp.folge_an"), "race_flag");
        }

        private void UpdateFolgeBtn()
        {
            _folgeBtn.EnableInClassList("gb-action--pink", _folgeMir);
            _folgeLabel.text = _host.Strings.T(_folgeMir ? "nl.rmp.folge_aus" : "ranch_mp.ausritt.folge_mir");
        }

        /// <summary>The guest strokes or feeds a horse of the host (the one close by, else the first) — a gesture, no economy.</summary>
        public void SendGeste(string id)
        {
            if (!IsVisitGuest || _view.BesuchPferdeAnzahl == 0 || !CanReact(id)) return;
            var lauf = _link.Lauf;
            var idx = lauf != null ? _view.BesuchPferdBei(lauf.Controller.Pos, 25.0) : -1;
            if (idx < 0) idx = 0;
            var pferd = _view.BesuchPferdName(idx);
            if (!Client.SendReaction("GESTE", new JObject { ["id"] = id, ["pferd"] = pferd })) return;
            _view.HerzPferd(idx);
            Sfx.PlayUi("soft_confirm");
            Toast(_host.Strings.T("nl.rmp.geste_du_" + id, ("pferd", pferd)), id == "fuettern" ? "food_apple" : "hand");
        }

        private void OnReaction(string kind, string from, JObject body)
        {
            if (!Client.IsFree) return;
            var t = _host.Strings;
            _reactionsIn[kind] = (_reactionsIn.TryGetValue(kind, out var n) ? n : 0) + 1;
            var name = Client.NameOf(from);
            switch (kind)
            {
                case "HERZ":
                    _view.Herz(from);
                    Toast(t.T("nl.rmp.herz_von", ("name", name)), "heart");
                    break;
                case "GESTE":
                {
                    var id = body["id"]?.ToString() ?? "";
                    if (id != "streicheln" && id != "fuettern") break;
                    var pferd = body["pferd"]?.ToString() ?? "";
                    _view.HerzPferd(_view.BesuchPferdIndex(pferd));
                    Toast(t.T("ranch_mp.besuch.geste_" + id, ("name", name), ("pferd", pferd)), id == "fuettern" ? "food_apple" : "hand");
                    break;
                }
                case "FOLGE_MIR":
                {
                    var an = body["an"]?.Type == JTokenType.Boolean && (bool)body["an"];
                    _view.SetFolgeMir(from, an);
                    if (an) Toast(t.T("nl.rmp.folge_mir_von", ("name", name)), "race_flag");
                    break;
                }
                case "ENTDECKUNG":
                    Toast(t.T("ranch_mp.ausritt.entdeckung", ("name", name), ("zone", OrtLabel(body["zone"]?.ToString()))), "compass");
                    break;
            }
        }

        private void OnEntdeckt(string id)
        {
            Toast(_host.Strings.T("nl.rmp.entdeckt", ("ort", OrtLabel(id))), "compass");
            Sfx.PlayUi("soft_sticker");
        }

        private void OnPeerJoined(string code, string name)
        {
            if (!Client.IsFree) return;
            var hostOfVisit = Client.Mode == RmpKurse.Besuch && !IsVisitGuest;
            Toast(_host.Strings.T(hostOfVisit ? "ranch_mp.besuch.gast" : "ranch_mp.ausritt.dabei", ("name", name)), "horseshoe");
            _shownKey = "";
        }

        private void OnPeerLeft(string code, string name)
        {
            if (!Client.IsFree) return;
            _view.SetFolgeMir(code, false);
            if (Client.Mode == RmpKurse.Ausritt) Toast(_host.Strings.T("nl.rmp.ausritt_weg", ("name", name)), "horseshoe");
            _shownKey = "";
        }

        private void OnEnded(string by)
        {
            if (_activeMode == RmpKurse.Besuch) Toast(_host.Strings.T("ranch_mp.besuch.vorbei"), "horseshoe");
            ResetFrei();
        }

        private void OnVisitMeta()
        {
            _yardKey = "";
            _shownKey = "";
            Refresh();
        }

        private void ResetFrei()
        {
            if (_folgeMir) _view.SetFolgeMir(Client.MyCode, false);
            foreach (var code in _view.FolgeMirCodes.ToList()) _view.SetFolgeMir(code, false);
            _folgeMir = false;
            _dazuOpen = false;
            _activeMode = "";
            _reactionsIn.Clear();
            _shownKey = "";
        }

        // ── per frame ──

        public override void Update()
        {
            if (_view == null) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            _link.Tick(dt);
            _view.Tick(dt);
            var running = _link.Running;
            _view.Render(running);
            if (Client.IsActive && _link.Lauf != null && Client.RoomId == _link.RunRoom) TickCountdown();
            else _countdown.style.display = DisplayStyle.None;
            _refreshAccum += Time.unscaledDeltaTime;
            if (_refreshAccum >= 0.2f) { _refreshAccum = 0f; Refresh(); }
        }

        private void TickCountdown()
        {
            var ms = Client.CountdownMs;
            if (Client.Phase == RanchMpClient.PhaseDone || ms < -900)
            {
                _countdown.style.display = DisplayStyle.None;
                return;
            }
            var sec = ms > 0 ? (long)Math.Ceiling(ms / 1000.0) : 0;
            _countdown.style.display = DisplayStyle.Flex;
            _countdown.text = sec > 0 ? sec.ToString() : _host.Strings.T("nl.rmp.los");
            if (sec == _lastCountdownSec) return;
            _lastCountdownSec = sec;
            Sfx.PlayUi(sec > 0 ? "soft_tap" : "soft_confirm");
        }

        private string _runRoom = "";

        private void OnRunStarted()
        {
            _lastCountdownSec = -1;
            if (Client.RoomId != _runRoom)
            {
                _runRoom = Client.RoomId;
                ResetFrei();
            }
            _view.Bind(_link);
            Debug.Log("[rmp] start mode=" + Client.Mode + " kurs=" + Client.Kurs + " players=" + Client.Players.Count + " it=" + Client.ItCode + " seed=" + Client.Seed);
        }

        private void OnNotice(string key, string name)
        {
            var t = _host.Strings;
            var text = t.HasKey(key) ? t.T(key, ("name", name)) : t.T("ranch_mp.fehler.unbekannt", ("code", key));
            Toast(text, key.Contains("fehler") ? "sign" : "horseshoe");
            if (key == "ranch_mp.lauf.ziel") { Sfx.PlayUi("soft_sticker"); Haptics.Success(); }
            Debug.Log("[rmp] notice " + key + (name.Length > 0 ? " " + name : ""));
        }

        private void Toast(string text, string icon)
        {
            var toast = UiRoot.BuildNotice(text, icon);
            _toastLane.Add(toast);
            UiRoot.AnimateNotice(toast, 2.4f);
        }

        /// <summary>"Verlassen"/"Fertig": leaves the session (a running round counts as given up) and closes.</summary>
        private void Close()
        {
            Sfx.PlayUi("soft_back");
            if (Client.IsActive) _link.Leave();
            _host.Ui.Pop();
        }

        /// <summary>The meadow covers the whole screen: the 3D room behind need not render (as during a minigame round).</summary>
        private void WorldCamera(bool on)
        {
            var cam = _host.World != null ? _host.World.Camera : null;
            if (cam != null && cam.enabled != on) cam.enabled = on;
        }

        public override void OnShown()
        {
            WorldCamera(false);
            _view?.SetVisible(true);
        }

        public override void OnHidden()
        {
            WorldCamera(true);
            if (_host.Ui.IsOpen(this))
            {
                _view?.SetVisible(false);
                return;
            }
            _view?.Destroy();
            // popped from outside (PopToRoot, a visit starting): no rider is left behind in a room
            if (Client.IsActive) _link.Leave();
            _link.Dispose();
            Client.ReactionReceived -= OnReaction;
            Client.PeerJoined -= OnPeerJoined;
            Client.PeerLeft -= OnPeerLeft;
            Client.Ended -= OnEnded;
            _online.RanchVisitMetaChanged -= OnVisitMeta;
        }
    }
}
