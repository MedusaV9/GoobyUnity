using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Gobnom;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Net;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// GOB NOM (Doc G §5, "Cut the Rope") on the shared minigame frame — port of gobnom_game.gd as a painted candyland:
    /// level select (15 campaign + 10 coop levels, jars as stars) → puzzle → NOM or a fallen candy → next/retry/select;
    /// "Fertig" ends the session and the frame books its score. Swipe through ropes to cut them, tap bubbles, cushions
    /// and switchable fans, drag rail anchors. Coop levels tint the two screen halves: every touch acts as the player of
    /// its half (hot-seat / multi-touch), the sim refuses foreign anchors. The world is 960×540 and letterboxed into the
    /// field; the mechanics are the certified <see cref="GobnomLogic"/>.
    /// Online coop (gobnommp.js through <see cref="GobnomCoopLink"/>): the coop select carries the "Mit Freund spielen"
    /// panel (online friends to invite, incoming invites, the pairing with both level votes), a coop card tapped while
    /// paired is our vote, and the round runs in lockstep — only our half takes touches, the partner's finger shows as a
    /// purple ring with its Gooby's name, "Warte auf Partner…" while its fence is missing.
    /// </summary>
    public sealed partial class GobnomScreen : MinigameHostScreen
    {
        public const double ViewW = 960.0, ViewH = 540.0;
        private const int TrailMax = 14;

        private GobnomGame _game;
        private WorldCanvas _canvas;
        private VisualElement _select, _grid, _overlay, _hud, _actions;
        private Label _selectStars, _cutsLabel, _levelLabel;
        private WorldCanvas _jarChip;
        private readonly List<Button> _trackChips = new List<Button>();
        private readonly Dictionary<int, Touch> _touches = new Dictionary<int, Touch>();
        private readonly List<Trail> _trails = new List<Trail>();
        private float _anim, _moodHold;
        private int _cutStreak, _jarsShown;
        private string _mood = "open";

        private Button _retryBtn;
        private GobnomCoopLink _link;
        private GobnomNetClient _net;
        private VisualElement _netPanel, _netFriends, _netInvites;
        private Label _netStatus, _netVotes, _waitLabel, _cursorName, _sideOwn, _sidePartner;
        private Button _netAction;
        private bool _netPicking, _netDirty;
        private string _netNote = "";
        private bool _netNoteIsInvite;
        private float _netPoll;
        private static readonly Color PartnerTint = MgScenery.Hex(0x7A5CC6);

        private static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        private static readonly Color Cream = MgScenery.Hex(0xF9EDD6);
        private static readonly Color EarPink = MgScenery.Hex(0xF6BFC0);
        private static readonly Color Cheek = new Color(0.96f, 0.63f, 0.6f, 0.85f);
        private static readonly Color MouthRed = MgScenery.Hex(0xB5766F);
        private static readonly Color Tongue = MgScenery.Hex(0xE89A94);
        private static readonly Color Nutella = MgScenery.Hex(0x5C3A21);
        private static readonly Color NutellaLid = MgScenery.Hex(0xE8E2D8);
        private static readonly Color CandyPink = MgScenery.Hex(0xF2A0B5);
        private static readonly Color CandyWrap = MgScenery.Hex(0xFFE9F0);
        private static readonly Color RopeBrown = MgScenery.Hex(0xA9744B);
        private static readonly Color BubbleBlue = new Color(0.62f, 0.82f, 0.95f, 0.55f);
        private static readonly Color BubbleRim = new Color(0.45f, 0.68f, 0.88f, 0.9f);
        private static readonly Color CushionTeal = MgScenery.Hex(0x9FD8CF);
        private static readonly Color FanMetal = MgScenery.Hex(0x9DA6AD);
        private static readonly Color Wind = new Color(0.72f, 0.84f, 0.92f, 0.7f);
        private static readonly Color SpikeGray = MgScenery.Hex(0x8C8C94);
        private static readonly Color Wood = MgScenery.Hex(0xA9744B);
        private static readonly Color WoodDark = MgScenery.Hex(0x7C5433);
        private static readonly Color CloudPink = new Color(0.99f, 0.87f, 0.93f, 0.92f);
        private static readonly Color StarGold = MgScenery.Hex(0xFFD34D);
        private static readonly Color RailGray = MgScenery.Hex(0xC9BCA9);
        private static readonly Color TintA = new Color(0.55f, 0.74f, 0.98f, 0.13f);
        private static readonly Color TintB = new Color(0.98f, 0.62f, 0.74f, 0.13f);

        private sealed class Touch
        {
            public string Mode, Player;
            public V2 Last;
            public int Rope = -1;
            public Trail Trail;
        }

        private sealed class Trail
        {
            public readonly List<V2> Points = new List<V2>();
            public float Fade = 1f;
            public bool Live = true;
        }

        public GobnomScreen(GameHost host) : base(host, "gobnom", "gobnom") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xF4D3E4);
        protected override Color FieldColor => MgScenery.Hex(0xFDEFF5);
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;

        public GobnomGame Game => _game;

        protected override void StartGame(int seed)
        {
            GobnomProgress.RegisterSlice();
            DropNet();
            _game = new GobnomGame(seed, Host?.Store);
            _game.Event += OnEvent;
            _game.PhaseChanged += OnPhase;
            _game.Finished_ += RoundOver;
            _touches.Clear();
            _trails.Clear();
            _cutStreak = 0;
            _jarsShown = 0;
            _mood = "open";
            _netPicking = false;
            _netNote = "";
            _netDirty = true;
            _net = Host?.Online?.Gobnom;
            if (_net != null)
            {
                _link = new GobnomCoopLink(_game, _net);
                _link.Notice += OnNetNotice;
                Host.Online.Changed += MarkNetDirty;
                _game.HashOut += LogHash;
                // the guest accepted from a question card: GOB NOM opens right in the coop lobby
                if (_net.IsPaired) _game.ShowTrack(GobnomData.TrackCoop);
            }
            Sfx.Preload("game_pop");
        }

        /// <summary>The coop select for an online pairing (OnlineService opens GOB NOM here); false while no round runs.</summary>
        public bool ShowCoopLobby()
        {
            if (_game == null || _game.Finished) return false;
            if (_game.Phase == GobnomGame.PhasePlay) return true; // a level in progress finishes first
            if (_game.Phase != GobnomGame.PhaseSelect) _game.BackToSelect(); // an end card makes way for the lobby
            _game.ShowTrack(GobnomData.TrackCoop);
            _netPicking = false;
            if (_select != null) RefreshSelect();
            return true;
        }

        /// <summary>The coop select with its online panel is on screen (it lists incoming invites itself).</summary>
        public bool ShowsNetPanel => _game != null && _net != null && _game.Phase == GobnomGame.PhaseSelect && _game.Track == GobnomData.TrackCoop;

        private void MarkNetDirty() => _netDirty = true;

        private static void LogHash(int tick, string hash) => Debug.Log("[gobnom] hash t=" + tick + " h=" + hash);

        /// <summary>The round ends (results, quit, pop): a pairing is left — coop has no forfeit winner — and nothing keeps listening.</summary>
        private void DropNet()
        {
            if (_link == null) return;
            if (_net != null && _net.IsPaired) _link.Leave();
            _link.Notice -= OnNetNotice;
            if (Host?.Online != null) Host.Online.Changed -= MarkNetDirty;
            _link.Game.HashOut -= LogHash;
            _link.Dispose();
            _link = null;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            else
            {
                var scene = MgScenery.Layer(Field, "mgf-gobnom");
                MgScenery.Gradient(scene, MgScenery.Hex(0xFBE7F2), MgScenery.Hex(0xFFF3E2), 0f, 100f);
            }

            _canvas = new WorldCanvas { Paint = Paint, name = "gobnom-world" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);
            Field.RegisterCallback<PointerDownEvent>(OnPointerDown);
            Field.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            Field.RegisterCallback<PointerUpEvent>(e => EndTouch(e.pointerId));
            Field.RegisterCallback<PointerCaptureOutEvent>(e => EndTouch(e.pointerId));

            // level chip on top: "Level 4" · three jars · cuts left
            _hud = Ux.Row("gb-card");
            _hud.name = "gobnom-hud";
            _hud.pickingMode = PickingMode.Ignore;
            _hud.style.position = Position.Absolute;
            _hud.style.top = 10;
            _hud.style.left = Length.Percent(50);
            _hud.style.translate = new Translate(Length.Percent(-50), 0);
            _hud.style.alignItems = Align.Center;
            _hud.style.paddingLeft = 14; _hud.style.paddingRight = 14; _hud.style.paddingTop = 6; _hud.style.paddingBottom = 6;
            _levelLabel = Ux.Text("", "gb-bold", "gb-nowrap");
            _levelLabel.pickingMode = PickingMode.Ignore;
            _hud.Add(_levelLabel);
            _jarChip = new WorldCanvas { Paint = p => PaintJarRow(p, _game?.Run?.JarsTaken ?? 0, 3, 13f) };
            _jarChip.style.width = 96; _jarChip.style.height = 30; _jarChip.style.marginLeft = 10;
            _hud.Add(_jarChip);
            _cutsLabel = Ux.Text("", "gb-sm", "gb-bold", "gb-nowrap");
            _cutsLabel.pickingMode = PickingMode.Ignore;
            _cutsLabel.style.marginLeft = 10;
            _hud.Add(_cutsLabel);
            Field.Add(_hud);

            // retry + back to the level select (top right of the field, under the frame's pause/close pills)
            _actions = Ux.Row();
            _actions.name = "gobnom-actions";
            _actions.style.position = Position.Absolute;
            _actions.style.top = 58; _actions.style.right = 10;
            _retryBtn = Ux.IconBtn("redo", () => { Sfx.PlayUi("soft_tap"); _game?.Retry(); }, "gb-btn-icon-sm");
            _retryBtn.name = "gobnom-retry";
            _retryBtn.style.marginRight = 8;
            _actions.Add(_retryBtn);
            var back = Ux.IconBtn("grid", () =>
            {
                Sfx.PlayUi("soft_back");
                // online the way back ends the pairing for both — a silent local exit would leave the partner waiting
                if (_game != null && _game.OnlineRun && _net != null && _net.IsPaired) _link?.Leave();
                _game?.BackToSelect();
            }, "gb-btn-icon-sm");
            back.name = "gobnom-select";
            _actions.Add(back);
            Field.Add(_actions);

            // online coop: "Warte auf Partner…" under the level chip, the partner's Gooby at its finger, who owns which half
            _waitLabel = Ux.Text("", "gb-card", "gb-sm", "gb-bold", "gb-nowrap");
            _waitLabel.name = "gobnom-netz-wait";
            _waitLabel.pickingMode = PickingMode.Ignore;
            _waitLabel.style.position = Position.Absolute;
            _waitLabel.style.top = 58;
            _waitLabel.style.left = Length.Percent(50);
            _waitLabel.style.translate = new Translate(Length.Percent(-50), 0);
            _waitLabel.style.paddingLeft = 12; _waitLabel.style.paddingRight = 12; _waitLabel.style.paddingTop = 4; _waitLabel.style.paddingBottom = 4;
            _waitLabel.style.color = PartnerTint;
            _waitLabel.style.display = DisplayStyle.None;
            Field.Add(_waitLabel);
            _cursorName = Ux.Text("", "gb-sm", "gb-bold", "gb-nowrap");
            _cursorName.name = "gobnom-netz-cursor";
            _cursorName.pickingMode = PickingMode.Ignore;
            _cursorName.style.position = Position.Absolute;
            _cursorName.style.color = PartnerTint;
            _cursorName.style.display = DisplayStyle.None;
            Field.Add(_cursorName);
            _sideOwn = SideTag("gobnom-netz-side-own", MgScenery.Hex(0xE0655F));
            _sidePartner = SideTag("gobnom-netz-side-partner", PartnerTint);

            BuildSelect(t);
            _overlay = Ux.El();
            _overlay.name = "gobnom-overlay";
            Ux.Abs(_overlay, 0, 0, 0, 0);
            _overlay.style.display = DisplayStyle.None;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0.29f, 0.23f, 0.21f, 0.32f);
            Field.Add(_overlay);
            OnPhase(_game.Phase);
        }

        // ── level select ──

        private void BuildSelect(Gooby.Core.I18n.I18n t)
        {
            _select = Ux.Col();
            _select.name = "gobnom-select-screen";
            Ux.Abs(_select, 0, 0, 0, 0);
            _select.style.paddingLeft = Length.Percent(4); _select.style.paddingRight = Length.Percent(4);
            _select.style.paddingTop = 64; _select.style.paddingBottom = 18;
            _select.style.backgroundColor = new Color(1f, 0.97f, 0.94f, 0.72f);

            var head = Ux.Row();
            head.style.alignItems = Align.Center;
            head.style.justifyContent = Justify.SpaceBetween;
            var title = Ux.Text(t.T("gobnom.select.title"), "gb-h1", "gb-nowrap");
            head.Add(title);
            var chips = Ux.Row();
            _trackChips.Clear();
            foreach (var track in new[] { GobnomData.TrackCampaign, GobnomData.TrackCoop })
            {
                var chip = Ux.Btn(t.T("gobnom.select." + track), () => { Sfx.PlayUi("soft_tap"); _game?.ShowTrack(track); RefreshSelect(); }, "gb-chip");
                chip.name = "gobnom-track-" + track;
                chip.userData = track;
                chip.style.marginLeft = 8;
                chips.Add(chip);
                _trackChips.Add(chip);
            }
            head.Add(chips);
            _select.Add(head);

            _selectStars = Ux.Text("", "gb-bold", "gb-nowrap");
            _selectStars.style.marginTop = 6;
            _select.Add(_selectStars);
            BuildNetPanel();

            _grid = Ux.Row();
            _grid.name = "gobnom-levels";
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.flexGrow = 1;
            _grid.style.alignContent = Align.FlexStart;
            _grid.style.marginTop = 12;
            _select.Add(_grid);

            var foot = Ux.Row();
            foot.style.justifyContent = Justify.FlexEnd;
            var done = Ux.Btn(t.T("gobnom.select.done"), () =>
            {
                Sfx.PlayUi("soft_confirm");
                if (_net != null && _net.IsPaired) _link?.Leave();
                _game?.Finish();
            }, "gb-btn-primary");
            done.name = "gobnom-done";
            foot.Add(done);
            _select.Add(foot);
            Field.Add(_select);
        }

        private void RefreshSelect()
        {
            if (_game == null || _grid == null) return;
            var t = Host.Strings;
            var track = _game.Track;
            foreach (var chip in _trackChips)
            {
                var on = (string)chip.userData == track;
                chip.EnableInClassList("gb-chip--active", on);
            }
            var count = GobnomProgress.LevelCount(track);
            _selectStars.text = t.T("gobnom.select.stars", ("n", GobnomProgress.TotalStars(_game.Save, track)), ("max", count * 3));
            _grid.Clear();
            var open = GobnomProgress.MaxUnlocked(_game.Save, track);
            var coop = track == GobnomData.TrackCoop;
            var paired = coop && _net != null && _net.IsPaired;
            for (var id = 1; id <= count; id++)
            {
                var levelId = id;
                // online the partner can pull us to a level it already reached: its vote opens the card for ours
                var unlocked = id <= open || (paired && _net.PartnerVote == id);
                var cleared = GobnomProgress.IsCleared(_game.Save, track, id);
                var stars = GobnomProgress.LevelStars(_game.Save, track, id);
                var card = new Button(() =>
                {
                    if (coop && _net != null && _net.IsPaired)
                    {
                        if (levelId > GobnomProgress.MaxUnlocked(_game.Save, track) && _net.PartnerVote != levelId) { Sfx.PlayUi("soft_error"); return; }
                        Sfx.PlayUi("soft_confirm");
                        _link?.ChooseLevel(levelId);
                        return;
                    }
                    if (!_game.IsUnlocked(track, levelId)) { Sfx.PlayUi("soft_error"); return; }
                    Sfx.PlayUi("soft_confirm");
                    _game.Open(track, levelId);
                }) { text = "" };
                card.ClearClassList();
                card.AddToClassList("gb-card");
                card.name = "gobnom-level-" + id;
                card.style.width = Length.Percent(18.4f);
                card.style.height = Length.Percent(coop ? (_net != null ? 30f : 40f) : 28f);
                card.style.marginRight = Length.Percent(1.6f);
                card.style.marginBottom = 10;
                card.style.alignItems = Align.Center;
                card.style.justifyContent = Justify.Center;
                card.style.paddingTop = 4; card.style.paddingBottom = 4;
                if (!unlocked) card.style.opacity = 0.55f;
                if (cleared) card.style.backgroundColor = MgScenery.Hex(0xFFF6DF);
                var num = Ux.Text((track == GobnomData.TrackCoop ? "C" : "") + id, "gb-h2", "gb-bold");
                num.pickingMode = PickingMode.Ignore;
                card.Add(num);
                var art = new WorldCanvas { Paint = p => { if (unlocked) PaintJarRow(p, stars, 3, 9f); else PaintLock(p); } };
                art.style.width = 70; art.style.height = 24;
                card.Add(art);
                if (unlocked && !cleared && id == open && !paired)
                {
                    var badge = Ux.Text(t.T("gobnom.select.new"), "gb-sm", "gb-bold");
                    badge.pickingMode = PickingMode.Ignore;
                    badge.style.position = Position.Absolute;
                    badge.style.top = 4; badge.style.right = 6;
                    badge.style.color = MgScenery.Hex(0xE0655F);
                    card.Add(badge);
                }
                if (paired)
                {
                    var mine = _net.MyVote == id;
                    var theirs = _net.PartnerVote == id;
                    if (mine) card.Add(VoteBadge(t.T("nl.gobnom.du"), MgScenery.Hex(0xE0655F), true));
                    if (theirs) card.Add(VoteBadge(_net.PartnerGoobyName, PartnerTint, false));
                    if (mine || theirs)
                    {
                        var rim = mine && theirs ? MgScenery.Hex(0x6DB86B) : mine ? MgScenery.Hex(0xE0655F) : PartnerTint;
                        card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = rim;
                        card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 3;
                    }
                }
                _grid.Add(card);
            }
            RefreshNet();
        }

        /// <summary>"Du" (left) / the partner's Gooby (right) on a coop card it voted for.</summary>
        private static Label VoteBadge(string text, Color color, bool left)
        {
            var badge = Ux.Text(text, "gb-sm", "gb-bold", "gb-nowrap");
            badge.pickingMode = PickingMode.Ignore;
            badge.style.position = Position.Absolute;
            badge.style.top = 4;
            if (left) badge.style.left = 6; else badge.style.right = 6;
            badge.style.color = Color.white;
            badge.style.backgroundColor = color;
            badge.style.paddingLeft = 6; badge.style.paddingRight = 6;
            badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius = badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 9;
            return badge;
        }

        // ── online coop panel (gobnom_netz_panel.gd): gate, invites, pairing + votes ──

        private void BuildNetPanel()
        {
            _netPanel = Ux.Col("gb-card");
            _netPanel.name = "gobnom-netz";
            _netPanel.style.marginTop = 8;
            _netPanel.style.paddingLeft = 12; _netPanel.style.paddingRight = 12; _netPanel.style.paddingTop = 8; _netPanel.style.paddingBottom = 8;
            _netPanel.style.display = DisplayStyle.None;
            var row = Ux.Row();
            row.style.alignItems = Align.Center;
            var icon = Ux.Icon("friends");
            icon.style.width = 26; icon.style.height = 26;
            row.Add(icon);
            _netStatus = Ux.Text("", "gb-sm", "gb-bold");
            _netStatus.name = "gobnom-netz-status";
            _netStatus.style.flexGrow = 1; _netStatus.style.flexShrink = 1;
            _netStatus.style.whiteSpace = WhiteSpace.Normal;
            _netStatus.style.marginLeft = 8; _netStatus.style.marginRight = 8;
            row.Add(_netStatus);
            _netAction = Ux.Btn("", OnNetAction, "gb-btn-primary", "gb-btn-sm");
            _netAction.name = "gobnom-netz-action";
            row.Add(_netAction);
            _netPanel.Add(row);
            _netVotes = Ux.Text("", "gb-sm", "gb-bold");
            _netVotes.name = "gobnom-netz-votes";
            _netVotes.style.marginTop = 4;
            _netVotes.style.color = PartnerTint;
            _netPanel.Add(_netVotes);
            _netInvites = Ux.Col();
            _netInvites.name = "gobnom-netz-invites";
            _netPanel.Add(_netInvites);
            _netFriends = Ux.Row();
            _netFriends.name = "gobnom-netz-friends";
            _netFriends.style.flexWrap = Wrap.Wrap;
            _netFriends.style.marginTop = 4;
            _netPanel.Add(_netFriends);
            _select.Add(_netPanel);
        }

        private void OnNetAction()
        {
            if (_net == null) return;
            Sfx.PlayUi("soft_tap");
            if (_net.IsPaired)
            {
                _link?.Leave();
                _netNote = "";
            }
            else _netPicking = !_netPicking;
            RefreshSelect();
        }

        /// <summary>Everything the select shows of the online state (rebuild only when this changes).</summary>
        private string NetSignature()
        {
            if (_net == null || _game == null) return "";
            if (_netNoteIsInvite && _net.OutgoingInvite.Length == 0) { _netNote = ""; _netNoteIsInvite = false; }
            var sb = new System.Text.StringBuilder();
            sb.Append(_game.Track).Append('|').Append(_net.IsOnline).Append('|').Append(_net.RoomId).Append('|').Append(_net.MyVote).Append(',').Append(_net.PartnerVote)
              .Append('|').Append(_net.OutgoingInvite).Append('|').Append(_netPicking).Append('|').Append(_netNote).Append('|').Append(_link?.LastError);
            foreach (var i in _net.IncomingInvites) sb.Append("|i:").Append(i.From);
            foreach (var f in Host.Online.Client.Friends) if (f.Online) sb.Append("|f:").Append(f.Code);
            return sb.ToString();
        }

        private string _netSig = "";

        private void RefreshNet()
        {
            _netDirty = false;
            _netSig = NetSignature();
            if (_netPanel == null) return;
            var show = _net != null && _game != null && _game.Track == GobnomData.TrackCoop && _game.Phase == GobnomGame.PhaseSelect;
            _netPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            var t = Host.Strings;
            _netInvites.Clear();
            foreach (var inv in _net.IncomingInvites)
            {
                if (_net.IsPaired) break;
                var from = inv.From;
                var line = Ux.Row();
                line.style.alignItems = Align.Center;
                line.style.marginTop = 4;
                var label = Ux.Text(t.T("gobnom.netz.laedt_ein", ("name", inv.GoobyName)), "gb-sm", "gb-bold");
                label.style.flexGrow = 1; label.style.flexShrink = 1; label.style.whiteSpace = WhiteSpace.Normal;
                line.Add(label);
                var no = Ux.Btn(t.T("gobnom.netz.ablehnen"), () => { Sfx.PlayUi("soft_back"); _net.Decline(from); RefreshNet(); }, "gb-btn-secondary", "gb-btn-sm");
                no.name = "gobnom-netz-decline";
                no.style.marginRight = 6;
                line.Add(no);
                var yes = Ux.Btn(t.T("gobnom.netz.annehmen"), () => { Sfx.PlayUi("soft_confirm"); Host.Online.AcceptGobnom(from); }, "gb-btn-confirm", "gb-btn-sm");
                yes.name = "gobnom-netz-accept";
                line.Add(yes);
                _netInvites.Add(line);
            }
            if (_net.IsPaired)
            {
                _netStatus.text = t.T("gobnom.netz.verbunden", ("name", _net.PartnerGoobyName));
                _netAction.text = t.T("gobnom.netz.trennen");
                _netAction.SetEnabled(true);
                _netPicking = false;
                _netFriends.style.display = DisplayStyle.None;
                var parts = new List<string>();
                if (_net.MyVote > 0) parts.Add(t.T("gobnom.netz.eigenes_level", ("n", _net.MyVote)));
                if (_net.PartnerVote > 0) parts.Add(t.T("gobnom.netz.partner_level", ("name", _net.PartnerGoobyName), ("n", _net.PartnerVote)));
                if (_link != null && _link.LastError.Length > 0) parts.Add(t.T("gobnom.netz.fehler", ("code", Host.Online.BoardErrorText(_link.LastError))));
                _netVotes.text = string.Join(" · ", parts);
                _netVotes.style.display = parts.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                return;
            }
            _netVotes.style.display = DisplayStyle.None;
            var friends = Host.Online.Client.Friends.Where(f => f.Online).ToList();
            var gate = !_net.IsOnline ? "gobnom.netz.offline" : friends.Count == 0 ? "gobnom.netz.keine_freunde" : "";
            _netAction.text = t.T("gobnom.netz.button");
            _netAction.SetEnabled(gate.Length == 0);
            _netStatus.text = gate.Length > 0 ? t.T(gate) : _netNote.Length > 0 ? _netNote : t.T("gobnom.netz.bereit");
            _netFriends.Clear();
            var picking = _netPicking && gate.Length == 0;
            _netFriends.style.display = picking ? DisplayStyle.Flex : DisplayStyle.None;
            if (!picking) return;
            foreach (var f in friends)
            {
                var friend = f;
                var chip = Ux.Row("gb-card");
                chip.style.alignItems = Align.Center;
                chip.style.marginRight = 8; chip.style.marginTop = 4;
                chip.style.paddingLeft = 10; chip.style.paddingRight = 6; chip.style.paddingTop = 4; chip.style.paddingBottom = 4;
                chip.Add(Ux.Text(friend.Name + " · " + friend.GoobyName, "gb-sm", "gb-nowrap"));
                var invite = Ux.Btn(t.T("gobnom.netz.einladen"), () => Invite(friend), "gb-btn-primary", "gb-btn-sm");
                invite.name = "gobnom-netz-invite-" + friend.Code;
                invite.style.marginLeft = 8;
                invite.SetEnabled(_net.OutgoingInvite != friend.Code);
                chip.Add(invite);
                _netFriends.Add(chip);
            }
        }

        private void Invite(FriendInfo f)
        {
            var t = Host.Strings;
            Host.Online.InviteToGobnom(f.Code, f.GoobyName, (ok, code) =>
            {
                _netNoteIsInvite = ok;
                _netNote = ok ? t.T("gobnom.netz.eingeladen", ("name", f.GoobyName)) : t.T("gobnom.netz.fehler", ("code", Host.Online.BoardErrorText(code)));
                if (ok) _netPicking = false;
                _netDirty = true;
            });
        }

        private Label SideTag(string name, Color color)
        {
            var tag = Ux.Text("", "gb-sm", "gb-bold", "gb-nowrap");
            tag.name = name;
            tag.pickingMode = PickingMode.Ignore;
            tag.style.position = Position.Absolute;
            tag.style.color = Color.white;
            tag.style.backgroundColor = color;
            tag.style.paddingLeft = 8; tag.style.paddingRight = 8; tag.style.paddingTop = 1; tag.style.paddingBottom = 1;
            tag.style.borderTopLeftRadius = tag.style.borderTopRightRadius = tag.style.borderBottomLeftRadius = tag.style.borderBottomRightRadius = 10;
            tag.style.display = DisplayStyle.None;
            Field.Add(tag);
            return tag;
        }

        /// <summary>Online: "Du" at the badge of our half, the partner's Gooby at the other one.</summary>
        private void PlaceSideTags()
        {
            var run = _game?.Run;
            var show = _game != null && _game.OnlineRun && run != null && run.Coop && run.HasSplit && _game.Phase == GobnomGame.PhasePlay;
            _sideOwn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _sidePartner.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _sideOwn.text = Host.Strings.T("nl.gobnom.du");
            _sidePartner.text = _link != null && _link.RunPartnerGooby.Length > 0 ? _link.RunPartnerGooby : "Gooby";
            var at = (float)(double.IsNaN(run.SplitAt) ? (run.SplitAxis == "y" ? 270.0 : 480.0) : run.SplitAt);
            V2 badgeA, badgeB;
            var bOnTheRight = run.SplitAxis != "y";
            if (run.SplitAxis == "y") { badgeA = new V2(26f, at - 24f); badgeB = new V2(26f, at + 24f); }
            else { badgeA = new V2(26f, 512f); badgeB = new V2(934f, 512f); }
            var weAreB = _game.Net.Side == GobnomLogic.PlayerB;
            PlaceTag(weAreB ? _sidePartner : _sideOwn, badgeA, false);
            PlaceTag(weAreB ? _sideOwn : _sidePartner, badgeB, bOnTheRight);
        }

        private void PlaceTag(Label tag, V2 badge, bool leftOfBadge)
        {
            var c = Px(badge);
            tag.style.top = c.y - 11f;
            tag.style.left = leftOfBadge ? c.x - Pr(22) : c.x + Pr(22);
            tag.style.translate = new Translate(Length.Percent(leftOfBadge ? -100f : 0f), 0);
        }

        private void OnNetNotice(string key)
        {
            var t = Host.Strings;
            var partner = _link != null && _link.RunPartnerGooby.Length > 0 ? _link.RunPartnerGooby : _net?.PartnerGoobyName ?? "Gooby";
            Debug.Log("[gobnom] notice " + key);
            switch (key)
            {
                case "gobnom.netz.start_banner":
                    Banner(t.T("gobnom.hud.coop_level", ("n", _game?.LevelId ?? 0)) + " — " + t.T(key, ("name", partner)), GobnomGame.LevelIntroSec + 1.4);
                    break;
                case "gobnom.netz.partner_weg":
                case "gobnom.netz.partner_da":
                    Banner(t.T(key, ("name", partner)), 2.6);
                    break;
                default:
                    PlaySfx("soft_error");
                    Host.Ui.Toast(t.T(key), "candy", 3.6f);
                    _netDirty = true;
                    break;
            }
        }

        // ── phases ──

        private void OnPhase(string phase)
        {
            if (_select == null) return;
            var t = Host.Strings;
            _touches.Clear();
            _select.style.display = phase == GobnomGame.PhaseSelect ? DisplayStyle.Flex : DisplayStyle.None;
            var playing = phase != GobnomGame.PhaseSelect;
            _hud.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            _actions.style.display = phase == GobnomGame.PhasePlay ? DisplayStyle.Flex : DisplayStyle.None;
            _retryBtn.style.display = _game.OnlineRun ? DisplayStyle.None : DisplayStyle.Flex;
            _overlay.style.display = DisplayStyle.None;
            _overlay.Clear();
            switch (phase)
            {
                case GobnomGame.PhaseSelect:
                    RefreshSelect();
                    break;
                case GobnomGame.PhasePlay:
                    _mood = "open";
                    _moodHold = 0f;
                    _cutStreak = 0;
                    _jarsShown = 0;
                    _trails.Clear();
                    var tag = t.T(_game.IsCoop ? "gobnom.hud.coop_level" : "gobnom.hud.level", ("n", _game.LevelId));
                    var hintKey = "gobnom.intro." + (string)_game.Level?["intro"];
                    var hint = t.HasKey(hintKey) ? t.T(hintKey) : "";
                    Banner(hint.Length > 0 ? tag + " — " + hint : tag, GobnomGame.LevelIntroSec + 1.4);
                    break;
                case GobnomGame.PhaseWon:
                    _mood = "nom";
                    LogOnlineEnd();
                    BuildEndCard(true);
                    break;
                case GobnomGame.PhaseLost:
                    _mood = "sad";
                    LogOnlineEnd();
                    BuildEndCard(false);
                    break;
            }
        }

        /// <summary>Both players' logs end an online round with the same tick and state hash — the evidence of a clean lockstep.</summary>
        private void LogOnlineEnd()
        {
            var run = _game?.Run;
            if (_game == null || !_game.OnlineRun || run == null) return;
            Debug.Log("[gobnom] over level=" + _game.LevelId + " side=" + _game.Net.Side + " outcome=" + (run.Outcome.Length > 0 ? run.Outcome : "server") + " jars=" + run.JarsTaken + " tick=" + run.Tick + " hash=" + GobnomLogic.StateHash(run));
        }

        private void BuildEndCard(bool won)
        {
            var t = Host.Strings;
            var card = Ux.Col("gb-card");
            card.name = "gobnom-end";
            card.style.alignItems = Align.Center;
            card.style.paddingLeft = 28; card.style.paddingRight = 28; card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.minWidth = Length.Percent(42f);
            card.Add(Ux.Text(t.T(won ? "gobnom.end.win" : "gobnom.end.lose"), "gb-h2", "gb-bold"));
            if (won)
            {
                var jars = new WorldCanvas { Paint = p => PaintJarRow(p, _game?.LastStars ?? 0, 3, 18f) };
                jars.style.width = 170; jars.style.height = 48; jars.style.marginTop = 8;
                card.Add(jars);
                card.Add(Ux.Text(t.T("gobnom.end.score", ("n", _game.LastTotal)), "gb-bold"));
                if (_game.LastFirstClear)
                {
                    var first = Ux.Text(t.T("gobnom.end.first_clear"), "gb-sm", "gb-bold");
                    first.style.color = MgScenery.Hex(0xD08A1E);
                    card.Add(first);
                }
            }
            else
            {
                var hint = Ux.Text(t.T("gobnom.end.lose_hint"), "gb-sm");
                hint.style.marginTop = 6;
                card.Add(hint);
            }
            var row = Ux.Row();
            row.style.marginTop = 14;
            if (_game.OnlineRun)
            {
                // the server allows one level per pairing: another round is a fresh invite of the same friend
                var code = _link?.RunPartnerCode ?? "";
                var gooby = _link != null && _link.RunPartnerGooby.Length > 0 ? _link.RunPartnerGooby : "Gooby";
                if (code.Length > 0 && Host.Online != null)
                    row.Add(EndButton("gobnom-again-online", t.T("nl.gobnom.nochmal", ("name", gooby)), "gb-btn-primary", () =>
                    {
                        Host.Online.InviteToGobnom(code, gooby, (ok, err) =>
                        {
                            _netNoteIsInvite = ok;
                            _netNote = ok ? t.T("gobnom.netz.eingeladen", ("name", gooby)) : t.T("gobnom.netz.fehler", ("code", Host.Online.BoardErrorText(err)));
                            _netDirty = true;
                        });
                        _game.BackToSelect();
                    }));
                row.Add(EndButton("gobnom-to-select", t.T("gobnom.end.select"), code.Length > 0 ? "gb-btn" : "gb-btn-primary", () => _game.BackToSelect()));
            }
            else
            {
                if (won && _game.LevelId < GobnomProgress.LevelCount(_game.Track))
                    row.Add(EndButton("gobnom-next", t.T("gobnom.end.next"), "gb-btn-primary", () => _game.Next()));
                row.Add(EndButton("gobnom-again", t.T("gobnom.end.retry"), won ? "gb-btn" : "gb-btn-primary", () => _game.Retry()));
                row.Add(EndButton("gobnom-to-select", t.T("gobnom.end.select"), "gb-btn", () => _game.BackToSelect()));
            }
            card.Add(row);
            _overlay.Add(card);
            _overlay.style.display = DisplayStyle.Flex;
            if (FieldW > 1f) ScalePop(card, 1.1f);
        }

        private static Button EndButton(string name, string label, string cls, Action action)
        {
            var b = Ux.Btn(label, () => { Sfx.PlayUi("soft_confirm"); action(); }, cls);
            b.name = name;
            b.style.marginLeft = 6; b.style.marginRight = 6;
            return b;
        }

        // ── frame hooks ──

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            if (_game == null) return; // "Fertig" from inside a handler ended the round
            _link?.Tick(dt);
            var f = (float)dt;
            _anim += f;
            if (_moodHold > 0f) { _moodHold -= f; if (_moodHold <= 0f && _game.Phase == GobnomGame.PhasePlay) _mood = "open"; }
            for (var i = _trails.Count - 1; i >= 0; i--)
            {
                if (_trails[i].Live) continue;
                _trails[i].Fade -= f * 4f;
                if (_trails[i].Fade <= 0f) _trails.RemoveAt(i);
            }
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            TimeLabel.text = g.Phase == GobnomGame.PhaseSelect || g.Level == null
                ? t.T("gobnom.select.title")
                : t.T(g.IsCoop ? "gobnom.hud.coop_level" : "gobnom.hud.level", ("n", g.LevelId));
            ScoreLabel.text = g.LiveScore.ToString();
            if (g.Run != null)
            {
                _levelLabel.text = TimeLabel.text;
                var left = GobnomLogic.CutsLeft(g.Run);
                _cutsLabel.text = left >= 0 ? t.T("gobnom.hud.cuts", ("n", left)) : "";
                _cutsLabel.style.display = left >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (g.Run.JarsTaken != _jarsShown) { _jarsShown = g.Run.JarsTaken; ScalePop(_jarChip, 1.25f); }
                _jarChip.MarkDirtyRepaint();
            }
            RenderNet(g);
            if (_stage != null) RenderStage();
            _canvas.MarkDirtyRepaint();
        }

        private void RenderNet(GobnomGame g)
        {
            if (_net == null) return;
            var t = Host.Strings;
            if (g.Phase == GobnomGame.PhaseSelect)
            {
                // invites run out without a Changed: a slow poll keeps the panel honest; the cards are only rebuilt when
                // what they show changed (a rebuild under a finger would swallow its tap)
                _netPoll += Time.unscaledDeltaTime;
                if (_netPoll >= 0.5f || _netDirty)
                {
                    _netPoll = 0f;
                    _netDirty = false;
                    if (NetSignature() != _netSig) RefreshSelect();
                }
            }
            var rejoin = g.OnlineRun && _net.Rejoining;
            var wait = g.OnlineRun && g.Phase == GobnomGame.PhasePlay && g.LevelIntroLeft <= 0.0 && (g.NetWaiting || rejoin);
            _waitLabel.style.display = wait ? DisplayStyle.Flex : DisplayStyle.None;
            if (wait) _waitLabel.text = t.T(rejoin ? "nl.gobnom.verbinde" : "gobnom.netz.warte_partner");
            var age = _link?.PartnerCursorAge ?? 99.0;
            var cursor = g.OnlineRun && g.Phase == GobnomGame.PhasePlay && age < 1.5;
            _cursorName.style.display = cursor ? DisplayStyle.Flex : DisplayStyle.None;
            if (cursor)
            {
                var at = Px(_link.PartnerCursor);
                _cursorName.text = _link.RunPartnerGooby;
                _cursorName.style.left = at.x + Pr(20);
                _cursorName.style.top = at.y - 10f;
                _cursorName.style.opacity = Mathf.Clamp01((float)(1.5 - age));
            }
            PlaceSideTags();
        }

        protected override int TakeFinalScore()
        {
            DropNet();
            var score = _game?.FinalScore ?? 0;
            _game = null;
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            DropNet();
            _game = null;
            DropStage();
        }

        // ── input ──

        private float WorldScale => Mathf.Max(0.0001f, Mathf.Min(FieldW / (float)ViewW, FieldH / (float)ViewH));
        private Vector2 WorldOffset => new Vector2((FieldW - (float)ViewW * WorldScale) * 0.5f, (FieldH - (float)ViewH * WorldScale) * 0.5f);
        private Vector2 Px(V2 w) => WorldOffset + new Vector2(w.X, w.Y) * WorldScale;
        private float Pr(double worldLen) => (float)worldLen * WorldScale;

        private V2 ToWorld(Vector3 panelPos)
        {
            var local = Field.WorldToLocal(panelPos);
            var off = WorldOffset;
            var s = WorldScale;
            return new V2((local.x - off.x) / s, (local.y - off.y) / s);
        }

        private void OnPointerDown(PointerDownEvent e)
        {
            if (_game == null || !_game.AcceptsInput || e.target != Field) return;
            var w = ToWorld(e.position);
            var player = _game.PlayerFor(w);
            Field.CapturePointer(e.pointerId);
            if (_game.OnlineRun)
            {
                _link?.SendCursor(w);
                if (!_game.IsOwnSide(w))
                {
                    // the partner's half: a friendly no (the sim would refuse it on both devices anyway)
                    PlaySfx("soft_error");
                    if (FieldW > 1f) Float(Px(w), Host.Strings.T("gobnom.hud.wrong_side"), MgScenery.Hex(0xE0655F), 22f);
                    _touches[e.pointerId] = new Touch { Mode = "none", Player = player };
                    return;
                }
            }
            var rail = _game.RailAnchorAt(w);
            if (rail >= 0) { _touches[e.pointerId] = new Touch { Mode = "anchor", Player = player, Rope = rail }; return; }
            if (_game.Tap(w, player)) { _touches[e.pointerId] = new Touch { Mode = "none", Player = player }; return; }
            var trail = new Trail();
            trail.Points.Add(w);
            _trails.Add(trail);
            _touches[e.pointerId] = new Touch { Mode = "swipe", Player = player, Last = w, Trail = trail };
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (_game == null || !_touches.TryGetValue(e.pointerId, out var touch)) return;
            var w = ToWorld(e.position);
            if (_game.OnlineRun) _link?.SendCursor(w);
            switch (touch.Mode)
            {
                case "anchor":
                    _game.Slide(touch.Rope, _game.RailTFor(touch.Rope, w), _game.PlayerFor(w));
                    break;
                case "swipe":
                    _game.Swipe(touch.Last, w, _game.PlayerFor(touch.Last));
                    touch.Last = w;
                    touch.Trail.Points.Add(w);
                    while (touch.Trail.Points.Count > TrailMax) touch.Trail.Points.RemoveAt(0);
                    break;
            }
        }

        private void EndTouch(int pointerId)
        {
            if (_touches.TryGetValue(pointerId, out var touch) && touch.Trail != null) touch.Trail.Live = false;
            _touches.Remove(pointerId);
            if (Field != null && Field.HasPointerCapture(pointerId)) Field.ReleasePointer(pointerId);
        }

        // ── juice (gobnom_game.gd _consume_events / _on_run_over) ──

        private void OnEvent(GobnomEvent ev)
        {
            var t = Host.Strings;
            var run = _game?.Run;
            switch (ev.Kind)
            {
                case "cut":
                    _cutStreak += 1;
                    PlaySfx("game_combo", Sfx.ComboSemitones(_cutStreak), 0.8f);
                    Haptics.Tap();
                    if (FieldW > 1f) Burst(Px(ev.At), RopeBrown, 10);
                    break;
                case "jar":
                    PlaySfx("game_star", Sfx.ComboSemitones(Math.Max(1, run?.JarsTaken ?? 1)));
                    if (FieldW > 1f) { Float(Px(ev.At), t.T("gobnom.hud.jar"), StarGold, 26f); Burst(Px(ev.At), StarGold, 14); }
                    break;
                case "pop":
                    PlaySfx("game_pop", 3f);
                    if (FieldW > 1f) Burst(Px(ev.At), new Color(0.75f, 0.9f, 1f), 12);
                    break;
                case "catch":
                    PlaySfx("soft_chip");
                    break;
                case "puff":
                    PlaySfx("game_whoosh", 2f, 0.8f);
                    if (ev.Hit) { Shake(0.18f); if (run != null && FieldW > 1f) Burst(Px(run.CandyPos), Color.white, 8); }
                    break;
                case "shoot":
                    PlaySfx("game_combo", -3f, 0.7f);
                    break;
                case "fan":
                    PlaySfx("soft_toggle");
                    break;
                case "slide":
                    break;
                case "spike":
                case "fell":
                    PlaySfx("game_lose", 0f, 0.8f);
                    Haptics.Warn();
                    Shake(0.5f);
                    break;
                case "denied":
                    // online both devices run the partner's refused actions too: only our own get the feedback
                    if (_game != null && _game.OnlineRun && ev.Player != _game.Net.Side) break;
                    PlaySfx("soft_error");
                    if (run != null && run.Coop && FieldW > 1f) Float(Px(run.CandyPos), t.T("gobnom.hud.wrong_side"), MgScenery.Hex(0xE0655F), 22f);
                    break;
                case "nom":
                    PlaySfx("game_win");
                    ConfettiRain(60);
                    if (run != null && FieldW > 1f) Burst(Px(run.CandyPos), CandyPink, 20);
                    break;
                case "side":
                    break;
            }
            if (ev.Kind == "jar" || ev.Kind == "pop") { _mood = "nom"; _moodHold = 0.45f; }
            StageEvent(ev);
        }

        // ── painting (world → field) ──

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f) return;
            if (_stage == null) PaintBackdrop(p);
            var run = _game.Run;
            if (run == null) return;
            var tick = run.Tick;
            if (run.Coop && run.HasSplit) PaintSplit(p, run);
            if (_stage != null)
            {
                // the kitchen stage shows the level's pieces, the candy and Gooby: the canvas keeps the trails and the cursor
                foreach (var trail in _trails) PaintTrail(p, trail);
                PaintPartnerCursor(p);
                return;
            }
            foreach (var c in run.Clouds) PaintCloud(p, c, tick);
            foreach (var s in run.Spikes) PaintSpikes(p, s);
            foreach (var f in run.Fans) PaintFan(p, f, tick, run);
            foreach (var c in run.Cushions) PaintCushion(p, c, tick, run);
            foreach (var r in run.Ropes) if (r.Rail != null && !r.Cut) PaintRail(p, r.Rail);
            foreach (var s in run.Shooters) PaintShooter(p, s);
            foreach (var j in run.Jars) PaintJar(p, Px(j.Pos), Pr(run.Physics.JarR), tick, j.Taken, j.Pos.X);
            PaintGooby(p, Px(run.Mouth), Pr(118.0), tick, _mood);
            foreach (var r in run.Ropes) if (!r.Cut) PaintRope(p, Px(r.Anchor), Px(run.CandyPos), Pr(r.Rest));
            foreach (var r in run.Ropes) if (!r.Cut) PaintAnchor(p, Px(r.Anchor), r.Rail != null, OwnerTint(run, r.Owner));
            foreach (var b in run.Bubbles) if (!b.Popped && !b.Holds) PaintBubble(p, Px(b.Pos), Pr(b.R), tick, false);
            if (run.Outcome != "won") PaintCandy(p, Px(run.CandyPos), Pr(run.Physics.CandyR), tick);
            foreach (var b in run.Bubbles) if (!b.Popped && b.Holds) PaintBubble(p, Px(run.CandyPos), Pr(b.R), tick, true);
            foreach (var trail in _trails) PaintTrail(p, trail);
            PaintPartnerCursor(p);
        }

        /// <summary>Online: the partner's finger as a purple ring that fades 1.5 s after its last GN_CURSOR.</summary>
        private void PaintPartnerCursor(Painter2D p)
        {
            if (_link == null || !_game.OnlineRun || _link.PartnerCursorAge >= 1.5) return;
            var alpha = Mathf.Clamp01((float)(1.5 - _link.PartnerCursorAge));
            var c = Px(_link.PartnerCursor);
            Circle(p, c, Pr(15), new Color(PartnerTint.r, PartnerTint.g, PartnerTint.b, 0.22f * alpha));
            p.strokeColor = new Color(PartnerTint.r, PartnerTint.g, PartnerTint.b, alpha);
            p.lineWidth = Mathf.Max(1.5f, Pr(2.5));
            p.BeginPath();
            p.Arc(c, Pr(15), Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            Circle(p, c, Pr(4), new Color(PartnerTint.r, PartnerTint.g, PartnerTint.b, alpha));
        }

        private Color OwnerTint(GobnomRun run, string owner)
        {
            if (!run.Coop) return Color.clear;
            return owner == "a" ? new Color(0.45f, 0.65f, 0.98f, 0.45f) : owner == "b" ? new Color(0.98f, 0.5f, 0.66f, 0.45f) : Color.clear;
        }

        private void PaintBackdrop(Painter2D p)
        {
            // cotton-candy hills along the bottom and a few sugar sprinkles in the sky (world space, behind everything)
            var a = _anim;
            Blob(p, Px(new V2(160f, 560f)), Pr(260), Pr(120), new Color(0.80f, 0.93f, 0.84f, 0.85f));
            Blob(p, Px(new V2(520f, 590f)), Pr(330), Pr(150), new Color(0.98f, 0.85f, 0.91f, 0.9f));
            Blob(p, Px(new V2(880f, 565f)), Pr(250), Pr(125), new Color(0.84f, 0.88f, 0.99f, 0.85f));
            for (var i = 0; i < 18; i++)
            {
                var x = (i * 173.3f) % 960f;
                var y = 30f + (i * 97.7f) % 300f + Mathf.Sin(a * 0.6f + i) * 4f;
                var col = i % 3 == 0 ? new Color(1f, 0.82f, 0.4f, 0.55f) : i % 3 == 1 ? new Color(0.62f, 0.85f, 0.97f, 0.55f) : new Color(0.98f, 0.63f, 0.75f, 0.55f);
                Circle(p, Px(new V2(x, y)), Pr(3.2), col);
            }
        }

        private void PaintSplit(Painter2D p, GobnomRun run)
        {
            var at = (float)(double.IsNaN(run.SplitAt) ? (run.SplitAxis == "y" ? 270.0 : 480.0) : run.SplitAt);
            if (run.SplitAxis == "y")
            {
                Rect(p, Px(new V2(-200f, -200f)), Px(new V2(1160f, at)), TintA);
                Rect(p, Px(new V2(-200f, at)), Px(new V2(1160f, 740f)), TintB);
                for (var x = 0f; x < 960f; x += 28f) Rect(p, Px(new V2(x, at - 1.5f)), Px(new V2(x + 14f, at + 1.5f)), new Color(0.45f, 0.35f, 0.4f, 0.35f));
                Badge(p, Px(new V2(26f, at - 24f)), "A", new Color(0.45f, 0.65f, 0.98f));
                Badge(p, Px(new V2(26f, at + 24f)), "B", new Color(0.98f, 0.5f, 0.66f));
            }
            else
            {
                Rect(p, Px(new V2(-200f, -200f)), Px(new V2(at, 740f)), TintA);
                Rect(p, Px(new V2(at, -200f)), Px(new V2(1160f, 740f)), TintB);
                for (var y = 0f; y < 540f; y += 28f) Rect(p, Px(new V2(at - 1.5f, y)), Px(new V2(at + 1.5f, y + 14f)), new Color(0.45f, 0.35f, 0.4f, 0.35f));
                // bottom outer corners: ropes and rails live up top, the HUD chip and buttons too
                Badge(p, Px(new V2(26f, 512f)), "A", new Color(0.45f, 0.65f, 0.98f));
                Badge(p, Px(new V2(934f, 512f)), "B", new Color(0.98f, 0.5f, 0.66f));
            }
        }

        private void Badge(Painter2D p, Vector2 c, string letter, Color color)
        {
            Circle(p, c, Pr(15), Outline);
            Circle(p, c, Pr(12.5), color);
            // a letter in painter strokes: A = a tent with a bar, B = a spine with two bumps
            p.strokeColor = Color.white;
            p.lineWidth = Mathf.Max(1.5f, Pr(2.6));
            p.lineCap = LineCap.Round;
            var u = Pr(6.5);
            p.BeginPath();
            if (letter == "A")
            {
                p.MoveTo(c + new Vector2(-u * 0.8f, u)); p.LineTo(c + new Vector2(0f, -u)); p.LineTo(c + new Vector2(u * 0.8f, u));
                p.MoveTo(c + new Vector2(-u * 0.45f, u * 0.25f)); p.LineTo(c + new Vector2(u * 0.45f, u * 0.25f));
            }
            else
            {
                p.MoveTo(c + new Vector2(-u * 0.55f, u)); p.LineTo(c + new Vector2(-u * 0.55f, -u));
                p.Arc(c + new Vector2(-u * 0.05f, -u * 0.5f), u * 0.5f, Angle.Degrees(-90f), Angle.Degrees(90f));
                p.Arc(c + new Vector2(0f, u * 0.5f), u * 0.5f, Angle.Degrees(-90f), Angle.Degrees(90f));
                p.LineTo(c + new Vector2(-u * 0.55f, u));
            }
            p.Stroke();
        }

        private void PaintGooby(Painter2D p, Vector2 mouth, float s, int tick, string mood)
        {
            var wob = Mathf.Sin(tick * 0.18f) * 0.03f;
            var ow = s * 0.055f;
            var head = mouth - new Vector2(0f, s * 0.13f);
            // lop ears hang down beside the head (the board look of Gooby), swaying a little
            Ear(p, head + new Vector2(-s * 0.30f, -s * 0.22f), s, 0.36f + wob);
            Ear(p, head + new Vector2(s * 0.30f, -s * 0.22f), s, -0.36f - wob);
            Blob(p, head + new Vector2(0f, s * 0.42f), s * 0.46f + ow, s * 0.40f + ow, Outline);
            Blob(p, head + new Vector2(0f, s * 0.42f), s * 0.46f, s * 0.40f, Cream);
            Blob(p, head, s * 0.42f + ow, s * 0.38f + ow, Outline);
            Blob(p, head, s * 0.42f, s * 0.38f, Cream);
            Sprout(p, head + new Vector2(0f, -s * 0.37f), s, wob);
            var eyeY = head + new Vector2(0f, -s * 0.16f);
            p.strokeColor = Outline;
            p.lineCap = LineCap.Round;
            foreach (var side in new[] { -1f, 1f })
            {
                var e = eyeY + new Vector2(s * 0.16f * side, 0f);
                if (mood == "nom")
                {
                    p.lineWidth = s * 0.028f;
                    p.BeginPath(); p.Arc(e, s * 0.06f, Angle.Degrees(200f), Angle.Degrees(340f)); p.Stroke();
                }
                else if (mood == "sad")
                {
                    Circle(p, e, s * 0.05f, Outline);
                    p.lineWidth = s * 0.024f;
                    p.BeginPath(); p.MoveTo(e + new Vector2(-s * 0.055f * side, -s * 0.085f)); p.LineTo(e + new Vector2(s * 0.05f * side, -s * 0.05f)); p.Stroke();
                }
                else
                {
                    Circle(p, e, s * 0.055f, Outline);
                    Circle(p, e + new Vector2(-s * 0.018f, -s * 0.018f), s * 0.017f, Color.white);
                }
            }
            if (mood == "sad")
            {
                p.lineWidth = s * 0.03f;
                p.BeginPath(); p.Arc(head + new Vector2(0f, s * 0.21f), s * 0.09f, Angle.Degrees(200f), Angle.Degrees(340f)); p.Stroke();
            }
            else
            {
                // the wide-open waiting mouth IS the goal zone; NOM closes it to a chewing bulge
                var r = mood == "nom" ? s * 0.16f : s * 0.20f + Mathf.Sin(tick * 0.12f) * s * 0.01f;
                Circle(p, mouth, r + ow * 0.7f, Outline);
                Circle(p, mouth, r, MouthRed);
                Blob(p, mouth + new Vector2(0f, r * 0.5f), r * 0.6f, r * 0.3f, Tongue);
            }
            Circle(p, head + new Vector2(-s * 0.30f, s * 0.02f), s * 0.065f, Cheek);
            Circle(p, head + new Vector2(s * 0.30f, s * 0.02f), s * 0.065f, Cheek);
        }

        private void Ear(Painter2D p, Vector2 basePos, float s, float angle)
        {
            var dir = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            var mid = basePos + dir * s * 0.24f;
            RotBlob(p, mid, s * 0.11f + s * 0.055f, s * 0.27f + s * 0.055f, angle, Outline);
            RotBlob(p, mid, s * 0.11f, s * 0.27f, angle, Cream);
            RotBlob(p, mid + dir * s * 0.03f, s * 0.055f, s * 0.16f, angle, EarPink);
        }

        /// <summary>The sprout on Gooby's crown: a short stem with two leaves.</summary>
        private static void Sprout(Painter2D p, Vector2 top, float s, float wob)
        {
            var tip = top + new Vector2(wob * s * 0.6f, -s * 0.12f);
            p.strokeColor = Outline; p.lineWidth = s * 0.045f; p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(top); p.QuadraticCurveTo(top + new Vector2(s * 0.02f, -s * 0.07f), tip); p.Stroke();
            p.strokeColor = MgScenery.Hex(0x6DB86B); p.lineWidth = s * 0.022f;
            p.BeginPath(); p.MoveTo(top); p.QuadraticCurveTo(top + new Vector2(s * 0.02f, -s * 0.07f), tip); p.Stroke();
            var leaf = MgScenery.Hex(0x8ED081);
            RotBlob(p, tip + new Vector2(-s * 0.07f, -s * 0.01f), s * 0.08f + s * 0.02f, s * 0.04f + s * 0.02f, -0.45f, Outline);
            RotBlob(p, tip + new Vector2(-s * 0.07f, -s * 0.01f), s * 0.08f, s * 0.04f, -0.45f, leaf);
            RotBlob(p, tip + new Vector2(s * 0.065f, -s * 0.03f), s * 0.07f + s * 0.02f, s * 0.035f + s * 0.02f, 0.5f, Outline);
            RotBlob(p, tip + new Vector2(s * 0.065f, -s * 0.03f), s * 0.07f, s * 0.035f, 0.5f, leaf);
        }

        private void PaintCandy(Painter2D p, Vector2 c, float r, int tick)
        {
            var spin = tick * 0.06f * 0.3f;
            var axis = new Vector2(Mathf.Cos(spin), Mathf.Sin(spin));
            var perpBase = new Vector2(-axis.y, axis.x);
            foreach (var side in new[] { -1f, 1f })
            {
                var tip = c + axis * r * 1.55f * side;
                var perp = perpBase * r * 0.5f;
                var pts = new[] { c + perp * 0.4f, tip + perp, tip - perp, c - perp * 0.4f };
                Poly(p, pts, CandyWrap);
                p.strokeColor = Outline; p.lineWidth = Mathf.Max(1f, r * 0.14f); p.lineJoin = LineJoin.Round;
                p.BeginPath(); p.MoveTo(pts[0]); for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]); p.ClosePath(); p.Stroke();
            }
            Circle(p, c, r * 1.16f, Outline);
            Circle(p, c, r, CandyPink);
            p.strokeColor = CandyWrap; p.lineWidth = r * 0.22f;
            var a0 = tick * 0.06f * Mathf.Rad2Deg;
            p.BeginPath(); p.Arc(c, r * 0.62f, Angle.Degrees(a0), Angle.Degrees(a0 + 162f)); p.Stroke();
            Circle(p, c + new Vector2(-r * 0.32f, -r * 0.32f), r * 0.18f, new Color(1f, 1f, 1f, 0.55f));
        }

        private void PaintJar(Painter2D p, Vector2 pos, float r, int tick, bool taken, float seed)
        {
            if (taken) { Circle(p, pos, r * 0.3f, new Color(0.3f, 0.25f, 0.22f, 0.12f)); return; }
            var bob = Mathf.Sin(tick * 0.15f + seed * 0.05f) * r * 0.08f;
            var c = pos + new Vector2(0f, bob);
            var ow = r * 0.14f;
            Rect(p, c + new Vector2(-r * 0.72f - ow, -r * 0.55f - ow), c + new Vector2(r * 0.72f + ow, r * 0.8f + ow), Outline);
            Rect(p, c + new Vector2(-r * 0.72f, -r * 0.55f), c + new Vector2(r * 0.72f, r * 0.8f), Nutella);
            Rect(p, c + new Vector2(-r * 0.82f - ow, -r * 0.95f - ow), c + new Vector2(r * 0.82f + ow, -r * 0.45f + ow), Outline);
            Rect(p, c + new Vector2(-r * 0.82f, -r * 0.95f), c + new Vector2(r * 0.82f, -r * 0.45f), NutellaLid);
            Blob(p, c + new Vector2(0f, r * 0.05f), r * 0.5f, r * 0.32f, new Color(1f, 1f, 1f, 0.92f));
            Circle(p, c + new Vector2(0f, r * 0.05f), r * 0.14f, new Color(0.9f, 0.3f, 0.25f, 0.9f));
            Star(p, c + new Vector2(r * 0.85f, -r * 1.1f), r * 0.3f, StarGold);
        }

        private void PaintRope(Painter2D p, Vector2 anchor, Vector2 candy, float rest)
        {
            var dist = Vector2.Distance(anchor, candy);
            var slack = Mathf.Clamp01(1f - dist / Mathf.Max(rest, 1f));
            var mid = (anchor + candy) * 0.5f + new Vector2(0f, slack * rest * 0.35f);
            for (var pass = 0; pass < 2; pass++)
            {
                p.strokeColor = pass == 0 ? Outline : RopeBrown;
                p.lineWidth = pass == 0 ? Mathf.Max(2f, Pr(5.0)) : Mathf.Max(1f, Pr(2.6));
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.MoveTo(anchor);
                p.QuadraticCurveTo(mid, candy);
                p.Stroke();
            }
        }

        private void PaintAnchor(Painter2D p, Vector2 pos, bool rail, Color tint)
        {
            if (tint.a > 0f) Circle(p, pos, Pr(16), tint);
            Circle(p, pos, Pr(rail ? 13 : 11), Outline);
            Circle(p, pos, Pr(rail ? 10 : 8), rail ? MgScenery.Hex(0xF3C969) : Wood);
            Circle(p, pos, Pr(3.4), Outline);
        }

        private void PaintRail(Painter2D p, GobnomRail rail)
        {
            var a = Px(rail.From); var b = Px(rail.To);
            Line(p, a, b, Pr(7), Outline);
            Line(p, a, b, Pr(3.6), RailGray);
            Circle(p, a, Pr(5), Outline);
            Circle(p, b, Pr(5), Outline);
        }

        private void PaintShooter(Painter2D p, GobnomShooter s)
        {
            var c = Px(s.Pos);
            if (!s.Fired)
            {
                p.strokeColor = new Color(0.66f, 0.55f, 0.42f, 0.55f);
                p.lineWidth = Mathf.Max(1f, Pr(2));
                for (var i = 0; i < 16; i++)
                {
                    var a = i * 22.5f;
                    p.BeginPath(); p.Arc(c, Pr(s.R), Angle.Degrees(a), Angle.Degrees(a + 11.25f)); p.Stroke();
                }
            }
            Rect(p, c + new Vector2(-Pr(19), -Pr(16)), c + new Vector2(Pr(19), Pr(16)), Outline);
            Rect(p, c + new Vector2(-Pr(16), -Pr(13)), c + new Vector2(Pr(16), Pr(13)), s.Fired ? WoodDark : Wood);
            Circle(p, c, Pr(6.5), Outline);
            Circle(p, c, Pr(4.2), s.Fired ? Wood : Cream);
        }

        private void PaintBubble(Painter2D p, Vector2 c, float r, int tick, bool holds)
        {
            var rr = r * (1f + Mathf.Sin(tick * 0.16f) * 0.04f);
            Circle(p, c, rr, BubbleBlue);
            p.strokeColor = BubbleRim; p.lineWidth = Mathf.Max(1f, Pr(2.6));
            p.BeginPath(); p.Arc(c, rr, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
            p.strokeColor = new Color(1f, 1f, 1f, 0.8f); p.lineWidth = rr * 0.12f; p.lineCap = LineCap.Round;
            p.BeginPath(); p.Arc(c, rr * 0.68f, Angle.Degrees(-132f), Angle.Degrees(-63f)); p.Stroke();
            if (holds)
            {
                p.strokeColor = new Color(1f, 1f, 1f, 0.45f); p.lineWidth = Mathf.Max(1f, Pr(1.6));
                p.BeginPath(); p.Arc(c, rr + Pr(4), Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
            }
        }

        private void PaintCushion(Painter2D p, GobnomCushion cu, int tick, GobnomRun run)
        {
            var c = Px(cu.Pos);
            var dir = new Vector2(cu.Dir.X, cu.Dir.Y);
            var angle = Mathf.Atan2(dir.y, dir.x);
            var ready = cu.Charges != 0 && run.Tick >= cu.ReadyTick;
            var squish = 1f + (ready ? Mathf.Sin(tick * 0.2f) * 0.06f : 0f);
            var tint = OwnerTint(run, cu.Owner);
            if (tint.a > 0f) Circle(p, c, Pr(30), tint);
            // the beam it can reach, very faint, so the player sees where a puff lands
            if (ready)
            {
                var perp = new Vector2(-dir.y, dir.x);
                var hw = Pr(cu.HalfW); var len = Pr(cu.Range);
                Poly(p, new[] { c + perp * hw * 0.35f, c + dir * len + perp * hw, c + dir * len - perp * hw, c - perp * hw * 0.35f }, new Color(0.62f, 0.85f, 0.8f, 0.10f));
            }
            RotBlob(p, c, Pr(26) * squish + Pr(3), Pr(16) / squish + Pr(3), angle, Outline);
            RotBlob(p, c, Pr(26) * squish, Pr(16) / squish, angle, ready ? CushionTeal : RailGray);
            RotBlob(p, c - dir * Pr(16), Pr(12) + Pr(2.6f), Pr(12) + Pr(2.6f), angle, Outline);
            RotBlob(p, c - dir * Pr(16), Pr(12), Pr(12), angle, Wood);
            var tip = c + dir * Pr(34);
            var n = new Vector2(-dir.y, dir.x);
            Poly(p, new[] { tip + dir * Pr(12), tip + n * Pr(7), tip - n * Pr(7) }, ready ? Outline : RailGray);
        }

        private void PaintFan(Painter2D p, GobnomFan f, int tick, GobnomRun run)
        {
            var c = Px(f.Pos);
            var dir = new Vector2(f.Dir.X, f.Dir.Y);
            var tint = OwnerTint(run, f.Owner);
            if (tint.a > 0f) Circle(p, c, Pr(30), tint);
            if (f.Toggleable)
            {
                p.strokeColor = f.On ? new Color(0.35f, 0.75f, 0.45f, 0.9f) : new Color(0.85f, 0.4f, 0.4f, 0.9f);
                p.lineWidth = Mathf.Max(1.5f, Pr(3));
                p.BeginPath(); p.Arc(c, Pr(27), Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
            }
            Circle(p, c, Pr(22), Outline);
            Circle(p, c, Pr(18.5), FanMetal);
            var spin = tick * (f.On ? 0.45f : 0f);
            for (var i = 0; i < 3; i++)
            {
                var a = spin + Mathf.PI * 2f * i / 3f;
                RotBlob(p, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Pr(11), Pr(8), Pr(4), a, Cream);
            }
            Circle(p, c, Pr(4), Outline);
            if (!f.On) return;
            var perp = new Vector2(-dir.y, dir.x);
            for (var i = 0; i < 3; i++)
            {
                var off = i * 16f + (tick * 2.2f) % 16f;
                var basePos = c + dir * Pr(26 + off);
                var half = Pr(6 + i * 3);
                Line(p, basePos + perp * half, basePos - perp * half, Pr(2.4), Wind);
            }
        }

        private void PaintSpikes(Painter2D p, GobnomBox b)
        {
            var a = Px(new V2(b.X, b.Y)); var z = Px(new V2(b.X + b.W, b.Y + b.H));
            Rect(p, a - Vector2.one * Pr(2.5), z + Vector2.one * Pr(2.5), Outline);
            Rect(p, a, z, WoodDark);
            var horizontal = b.W >= b.H;
            var count = (int)Mathf.Max(2f, (float)(horizontal ? b.W : b.H) / 20f);
            for (var i = 0; i < count; i++)
            {
                var t = (i + 0.5f) / count;
                Vector2[] pts;
                if (horizontal)
                {
                    var bx = (float)(b.X + b.W * t);
                    pts = new[] { Px(new V2(bx - 8f, (float)b.Y + 2f)), Px(new V2(bx + 8f, (float)b.Y + 2f)), Px(new V2(bx, (float)b.Y - 14f)) };
                }
                else
                {
                    var by = (float)(b.Y + b.H * t);
                    pts = new[] { Px(new V2((float)b.X + 2f, by - 8f)), Px(new V2((float)b.X + 2f, by + 8f)), Px(new V2((float)b.X - 14f, by)) };
                }
                Poly(p, pts, SpikeGray);
                p.strokeColor = Outline; p.lineWidth = Mathf.Max(1f, Pr(2)); p.lineJoin = LineJoin.Round;
                p.BeginPath(); p.MoveTo(pts[0]); p.LineTo(pts[1]); p.LineTo(pts[2]); p.ClosePath(); p.Stroke();
            }
        }

        private void PaintCloud(Painter2D p, GobnomBox b, int tick)
        {
            var c = Px(new V2(b.X + b.W * 0.5, b.Y + b.H * 0.5));
            var w = Pr(b.W); var h = Pr(b.H);
            var drift = Mathf.Sin(tick * 0.06f) * w * 0.02f;
            for (var i = 0; i < 5; i++)
            {
                var t = i / 4f - 0.5f;
                Blob(p, c + new Vector2(t * w * 0.72f + drift, Mathf.Sin(t * 4f) * h * 0.16f), w * 0.24f, h * 0.38f, CloudPink);
            }
            Circle(p, c + new Vector2(-w * 0.2f, 0f), Pr(3), new Color(1f, 1f, 1f, 0.6f));
            Circle(p, c + new Vector2(w * 0.15f, -h * 0.1f), Pr(2.4), new Color(1f, 1f, 1f, 0.6f));
        }

        private void PaintTrail(Painter2D p, Trail trail)
        {
            if (trail.Points.Count < 2) return;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            for (var pass = 0; pass < 2; pass++)
            {
                p.strokeColor = pass == 0 ? new Color(0.98f, 0.6f, 0.75f, 0.35f * trail.Fade) : new Color(1f, 1f, 1f, 0.9f * trail.Fade);
                p.lineWidth = pass == 0 ? Pr(11) : Pr(4.5);
                p.BeginPath();
                p.MoveTo(Px(trail.Points[0]));
                for (var i = 1; i < trail.Points.Count; i++) p.LineTo(Px(trail.Points[i]));
                p.Stroke();
            }
        }

        /// <summary>Three jars in a row (HUD chip, level cards, end card): filled = collected.</summary>
        private void PaintJarRow(Painter2D p, int filled, int count, float r)
        {
            for (var i = 0; i < count; i++)
            {
                var c = new Vector2(r * 1.2f + i * r * 2.6f, r * 1.35f);
                var on = i < filled;
                var body = on ? Nutella : new Color(0.82f, 0.76f, 0.72f, 0.7f);
                var lid = on ? NutellaLid : new Color(0.9f, 0.87f, 0.84f, 0.7f);
                Rect(p, c + new Vector2(-r * 0.8f, -r * 0.45f), c + new Vector2(r * 0.8f, r * 0.95f), Outline);
                Rect(p, c + new Vector2(-r * 0.66f, -r * 0.32f), c + new Vector2(r * 0.66f, r * 0.82f), body);
                Rect(p, c + new Vector2(-r * 0.9f, -r * 0.95f), c + new Vector2(r * 0.9f, -r * 0.35f), Outline);
                Rect(p, c + new Vector2(-r * 0.76f, -r * 0.82f), c + new Vector2(r * 0.76f, -r * 0.48f), lid);
                if (on) Blob(p, c + new Vector2(0f, r * 0.2f), r * 0.4f, r * 0.26f, new Color(1f, 1f, 1f, 0.9f));
            }
        }

        private static void PaintLock(Painter2D p)
        {
            var c = new Vector2(35f, 13f);
            p.strokeColor = new Color(0.45f, 0.38f, 0.35f);
            p.lineWidth = 2.5f;
            p.BeginPath(); p.Arc(c + new Vector2(0f, -2f), 5f, Angle.Degrees(180f), Angle.Degrees(360f)); p.Stroke();
            Rect(p, c + new Vector2(-7f, -2f), c + new Vector2(7f, 9f), new Color(0.45f, 0.38f, 0.35f));
        }

        // ── painter primitives ──

        private static void Circle(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.Arc(c, Mathf.Max(0.3f, r), Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        private static void Blob(Painter2D p, Vector2 c, float rx, float ry, Color color) => RotBlob(p, c, rx, ry, 0f, color);

        private static void RotBlob(Painter2D p, Vector2 c, float rx, float ry, float angle, Color color)
        {
            var cos = Mathf.Cos(angle); var sin = Mathf.Sin(angle);
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i < 20; i++)
            {
                var a = Mathf.PI * 2f * i / 20f;
                var x = Mathf.Cos(a) * rx; var y = Mathf.Sin(a) * ry;
                var pt = c + new Vector2(x * cos - y * sin, x * sin + y * cos);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        private static void Rect(Painter2D p, Vector2 a, Vector2 b, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(a); p.LineTo(new Vector2(b.x, a.y)); p.LineTo(b); p.LineTo(new Vector2(a.x, b.y));
            p.ClosePath();
            p.Fill();
        }

        private static void Poly(Painter2D p, Vector2[] pts, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b, float width, Color color)
        {
            p.strokeColor = color;
            p.lineWidth = Mathf.Max(0.8f, width);
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        private static void Star(Painter2D p, Vector2 c, float r, Color color)
        {
            var pts = new Vector2[10];
            for (var i = 0; i < 10; i++)
            {
                var a = -Mathf.PI / 2f + Mathf.PI * 2f * i / 10f;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? r : r * 0.45f);
            }
            Poly(p, pts, color);
        }

        private sealed class WorldCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public WorldCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
