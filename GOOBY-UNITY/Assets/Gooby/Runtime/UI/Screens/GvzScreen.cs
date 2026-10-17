using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Gvz;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Net;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Goobys vs Zombies (Doc G §4) on the shared minigame frame — port of gvz_game.gd as a painted lawn diorama:
    /// level select (15 campaign levels, stars by panic Goobys used) → battle → won/lost → next/retry/select; "Fertig"
    /// ends the session and the frame books its score. Tap a card (bottom strip: the level's Goobys, or the conveyor
    /// belt, plus the shovel), then a lawn cell; tap the nutella blobs to collect them. The house is on the left, the
    /// zombies shuffle in from the right; the panic Gooby of each lane saves it once. Mechanics: certified
    /// <see cref="GvzLogic"/> (20 Hz, int arithmetic); the frame's difficulty chips pick easy/normal/hard.
    /// Online PvP (gvzmp.js through <see cref="GvzPvpLink"/>): the select carries the "PvP übers Netz" panel (challenge an
    /// online friend, accept/decline, the pairing with both sides and the start handshake); the inviter defends, the guest
    /// summons zombies with mud (cards = zombie types, tap a lane) — the clock pill counts the survive timer down.
    /// </summary>
    public sealed partial class GvzScreen : MinigameHostScreen
    {
        public const double ViewW = 960.0, ViewH = 540.0;
        private const float LawnX = 78f, LawnY = 96f, LawnW = 874f, LawnH = 360f;
        private const float StripTop = 462f, CardW = 68f, CardH = 72f, StripX = 96f;

        private GvzGame _game;
        private VisualElement _door;
        private P.Canvas _canvas;
        private VisualElement _select, _grid, _overlay, _strip, _counter;
        private Label _nutellaLabel, _selectStars;
        private readonly Dictionary<string, VisualElement> _cardButtons = new Dictionary<string, VisualElement>();
        private string _cardKey = "", _selected = "";
        private float _anim;
        private int _shownNutella = -1;

        private GvzPvpLink _link;
        private GvzNetClient _net;
        private VisualElement _netPanel, _netFriends, _netInvites;
        private Label _netStatus, _netRecord, _waitLabel, _coinsLabel;
        private Button _netAction, _netStart;
        private bool _netPicking, _netDirty, _netNoteIsInvite;
        private string _netNote = "", _netSig = "";
        private float _netPoll;
        private static readonly Color Mud = MgScenery.Hex(0x7A5A3A);
        private static readonly Color PartnerTint = MgScenery.Hex(0x7A5CC6);

        private static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        private static readonly Color Cream = MgScenery.Hex(0xF9EDD6);
        private static readonly Color EarPink = MgScenery.Hex(0xF6BFC0);
        private static readonly Color Cheek = new Color(0.96f, 0.63f, 0.6f, 0.85f);
        private static readonly Color Nutella = MgScenery.Hex(0x5C3A21);
        private static readonly Color NutellaLid = MgScenery.Hex(0xE8E2D8);
        private static readonly Color Carrot = MgScenery.Hex(0xF29A3A);
        private static readonly Color Leaf = MgScenery.Hex(0x6DB86B);
        private static readonly Color Frost = MgScenery.Hex(0x9ED8F5);
        private static readonly Color Gold = MgScenery.Hex(0xFFD34D);
        private static readonly Color Berry = MgScenery.Hex(0xE0455A);
        private static readonly Color ZombieSkin = MgScenery.Hex(0xA9D6A0);
        private static readonly Color ZombieDark = MgScenery.Hex(0x6E9C6A);

        public GvzScreen(GameHost host) : base(host, "gvz", "gvz") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE3B4);
        protected override Color FieldColor => MgScenery.Hex(0xEAF7E2);
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;

        public GvzGame Game => _game;

        protected override void StartGame(int seed)
        {
            GvzProgress.RegisterSlice();
            DropNet();
            _game = new GvzGame(Difficulty, seed, Host?.Store);
            _game.Event += OnEvent;
            _game.PhaseChanged += OnPhase;
            _game.Finished_ += RoundOver;
            _cardKey = "";
            _selected = "";
            _shownNutella = -1;
            _netPicking = false;
            _netNote = "";
            _netDirty = true;
            _net = Host?.Online?.Gvz;
            if (_net != null)
            {
                _link = new GvzPvpLink(_game, _net);
                _link.Notice += OnNetNotice;
                Host.Online.Changed += MarkNetDirty;
                _game.HashOut += LogHash;
            }
            Sfx.Preload("game_pop");
        }

        /// <summary>The select with the PvP panel for an online pairing (OnlineService opens GvZ here); false while no round runs.</summary>
        public bool ShowNetLobby()
        {
            if (_game == null || _game.Finished) return false;
            if (_game.Phase == GvzGame.PhasePlay) return true; // a level in progress finishes first
            if (_game.Phase != GvzGame.PhaseSelect) _game.BackToSelect();
            _netPicking = false;
            if (_select != null) RefreshSelect();
            return true;
        }

        /// <summary>The select with its PvP panel is on screen (it lists incoming challenges itself).</summary>
        public bool ShowsNetPanel => _game != null && _net != null && _game.Phase == GvzGame.PhaseSelect;

        private void MarkNetDirty() => _netDirty = true;

        private static void LogHash(int tick, string hash) => Debug.Log("[gvz] hash t=" + tick + " h=" + hash);

        /// <summary>The round ends (results, quit, pop): a pairing is left — no forfeit winner — and nothing keeps listening.</summary>
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
            _canvas = new P.Canvas { Paint = Paint, name = "gvz-lawn" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);
            Field.RegisterCallback<PointerDownEvent>(OnPointerDown);
            // the player's 3D Gooby keeps watch in the house doorway (cheers a cleared wave, flinches at a breach)
            _door = new VisualElement { name = "gvz-door-gooby", pickingMode = PickingMode.Ignore };
            _door.style.position = Position.Absolute;
            Field.Add(_door);
            if (_stage == null) FollowGooby(_door, 1.45f, grounded: true);

            // nutella counter (bottom left) and the card strip beside it
            _counter = Ux.Row("gb-card");
            _counter.name = "gvz-nutella";
            _counter.pickingMode = PickingMode.Ignore;
            Place(_counter, 6f, StripTop + 4f, 84f, CardH - 6f);
            _counter.style.alignItems = Align.Center;
            _counter.style.justifyContent = Justify.Center;
            var jar = new P.Canvas { Paint = p => { if (_game != null && _game.NetZombie) PaintMudIcon(p, new Vector2(12f, 16f), 10f); else PaintJarIcon(p, new Vector2(12f, 15f), 10f); } };
            jar.name = "gvz-counter-icon";
            jar.style.width = 24; jar.style.height = 30;
            _counter.Add(jar);
            _nutellaLabel = Ux.Text("0", "gb-h3", "gb-bold", "gb-nowrap");
            _nutellaLabel.pickingMode = PickingMode.Ignore;
            _counter.Add(_nutellaLabel);
            Field.Add(_counter);
            _strip = new VisualElement { name = "gvz-cards", pickingMode = PickingMode.Ignore };
            Ux.Abs(_strip, 0, 0, 0, 0);
            Field.Add(_strip);

            // online: "Warte auf Partner…" under the frame pills
            _waitLabel = Ux.Text("", "gb-card", "gb-sm", "gb-bold", "gb-nowrap");
            _waitLabel.name = "gvz-netz-wait";
            _waitLabel.pickingMode = PickingMode.Ignore;
            _waitLabel.style.position = Position.Absolute;
            _waitLabel.style.top = 58;
            _waitLabel.style.left = Length.Percent(50);
            _waitLabel.style.translate = new Translate(Length.Percent(-50), 0);
            _waitLabel.style.paddingLeft = 12; _waitLabel.style.paddingRight = 12; _waitLabel.style.paddingTop = 4; _waitLabel.style.paddingBottom = 4;
            _waitLabel.style.color = PartnerTint;
            _waitLabel.style.display = DisplayStyle.None;
            Field.Add(_waitLabel);

            BuildSelect(t);
            _overlay = Ux.El();
            _overlay.name = "gvz-overlay";
            Ux.Abs(_overlay, 0, 0, 0, 0);
            _overlay.style.display = DisplayStyle.None;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0.29f, 0.23f, 0.21f, 0.32f);
            Field.Add(_overlay);
            OnPhase(_game.Phase);
        }

        /// <summary>Absolute placement in view units (the field keeps the view's aspect, so percent is exact).</summary>
        private static void Place(VisualElement e, float x, float y, float w, float h)
        {
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(x / (float)ViewW * 100f);
            e.style.top = Length.Percent(y / (float)ViewH * 100f);
            e.style.width = Length.Percent(w / (float)ViewW * 100f);
            e.style.height = Length.Percent(h / (float)ViewH * 100f);
        }

        // ── level select ──

        private void BuildSelect(Gooby.Core.I18n.I18n t)
        {
            _select = Ux.Col();
            _select.name = "gvz-select-screen";
            Ux.Abs(_select, 0, 0, 0, 0);
            _select.style.paddingLeft = Length.Percent(4); _select.style.paddingRight = Length.Percent(4);
            _select.style.paddingTop = 60; _select.style.paddingBottom = 16;
            _select.style.backgroundColor = new Color(0.98f, 0.97f, 0.92f, 0.86f);
            var head = Ux.Row();
            head.style.alignItems = Align.Center;
            head.style.justifyContent = Justify.SpaceBetween;
            head.Add(Ux.Text(t.T("gvz.select.title"), "gb-h1", "gb-nowrap"));
            _selectStars = Ux.Text("", "gb-bold", "gb-nowrap");
            head.Add(_selectStars);
            _select.Add(head);
            BuildNetPanel();
            _grid = Ux.Row();
            _grid.name = "gvz-levels";
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.flexGrow = 1;
            _grid.style.alignContent = Align.FlexStart;
            _grid.style.marginTop = 12;
            _select.Add(_grid);
            var foot = Ux.Row();
            foot.style.justifyContent = Justify.FlexEnd;
            var done = Ux.Btn(t.T("gvz.select.done"), () =>
            {
                Sfx.PlayUi("soft_confirm");
                if (_net != null && _net.IsPaired) _link?.Leave();
                _game?.Finish();
            }, "gb-btn-primary");
            done.name = "gvz-done";
            foot.Add(done);
            _select.Add(foot);
            Field.Add(_select);
        }

        private void RefreshSelect()
        {
            if (_game == null || _grid == null) return;
            var t = Host.Strings;
            _selectStars.text = t.T("gvz.select.stars", ("n", GvzProgress.TotalStars(_game.Save)), ("max", GvzData.LevelCount * 3));
            _grid.Clear();
            var open = GvzProgress.MaxUnlocked(_game.Save);
            foreach (var level in _game.Levels)
            {
                var id = GvzJson.I(level, "id", 0);
                var unlocked = id <= open;
                var cleared = GvzProgress.IsCleared(_game.Save, id);
                var stars = GvzProgress.LevelStars(_game.Save, id);
                var setting = GvzJson.S(level, "setting", "vorgarten");
                var card = new Button(() =>
                {
                    if (!_game.IsUnlocked(id)) { Sfx.PlayUi("soft_error"); return; }
                    Sfx.PlayUi("soft_confirm");
                    _game.Open(id);
                }) { text = "" };
                card.ClearClassList();
                card.AddToClassList("gb-card");
                card.name = "gvz-level-" + id;
                card.style.width = Length.Percent(18.4f);
                card.style.height = Length.Percent(_net != null ? 23f : 29f);
                card.style.marginRight = Length.Percent(1.6f);
                card.style.marginBottom = 10;
                card.style.alignItems = Align.Center;
                card.style.justifyContent = Justify.FlexStart;
                card.style.paddingTop = 14;
                card.style.overflow = Overflow.Hidden;
                if (!unlocked) card.style.opacity = 0.55f;
                var art = new P.Canvas { Paint = p => PaintLevelChip(p, setting, unlocked, stars, card.contentRect.width, card.contentRect.height) };
                Ux.Abs(art, 0, 0, 0, 0);
                card.Add(art);
                var num = Ux.Text(id.ToString(), "gb-h2", "gb-bold");
                num.pickingMode = PickingMode.Ignore;
                card.Add(num);
                if (unlocked && !cleared && id == open)
                {
                    var badge = Ux.Text(t.T("gvz.select.new"), "gb-sm", "gb-bold");
                    badge.pickingMode = PickingMode.Ignore;
                    badge.style.position = Position.Absolute;
                    badge.style.top = 4; badge.style.right = 6;
                    badge.style.color = MgScenery.Hex(0xE0655F);
                    card.Add(badge);
                }
                _grid.Add(card);
            }
            RefreshNet();
        }

        // ── online PvP panel (gvz_netz_panel.gd): gate, challenges, pairing + start handshake ──

        private void BuildNetPanel()
        {
            _netPanel = Ux.Col("gb-card");
            _netPanel.name = "gvz-netz";
            _netPanel.style.marginTop = 8;
            _netPanel.style.paddingLeft = 12; _netPanel.style.paddingRight = 12; _netPanel.style.paddingTop = 8; _netPanel.style.paddingBottom = 8;
            _netPanel.style.display = DisplayStyle.None;
            var row = Ux.Row();
            row.style.alignItems = Align.Center;
            var icon = Ux.Icon("zombie");
            icon.style.width = 26; icon.style.height = 26;
            row.Add(icon);
            _netStatus = Ux.Text("", "gb-sm", "gb-bold");
            _netStatus.name = "gvz-netz-status";
            _netStatus.style.flexGrow = 1; _netStatus.style.flexShrink = 1;
            _netStatus.style.whiteSpace = WhiteSpace.Normal;
            _netStatus.style.marginLeft = 8; _netStatus.style.marginRight = 8;
            row.Add(_netStatus);
            _netStart = Ux.Btn("", OnNetStart, "gb-btn-confirm", "gb-btn-sm");
            _netStart.name = "gvz-netz-start";
            _netStart.style.marginRight = 6;
            row.Add(_netStart);
            _netAction = Ux.Btn("", OnNetAction, "gb-btn-primary", "gb-btn-sm");
            _netAction.name = "gvz-netz-action";
            row.Add(_netAction);
            _netPanel.Add(row);
            _netRecord = Ux.Text("", "gb-sm");
            _netRecord.name = "gvz-netz-record";
            _netRecord.style.marginTop = 2;
            _netPanel.Add(_netRecord);
            _netInvites = Ux.Col();
            _netInvites.name = "gvz-netz-invites";
            _netPanel.Add(_netInvites);
            _netFriends = Ux.Row();
            _netFriends.name = "gvz-netz-friends";
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
            RefreshNet();
        }

        private void OnNetStart()
        {
            if (_net == null || !_net.IsPaired || _net.StartRequested) return;
            Sfx.PlayUi("soft_confirm");
            _link?.RequestStart();
            _netDirty = true;
        }

        /// <summary>Everything the panel shows of the online state (rebuild only when this changes).</summary>
        private string NetSignature()
        {
            if (_net == null || _game == null) return "";
            if (_netNoteIsInvite && _net.OutgoingInvite.Length == 0) { _netNote = ""; _netNoteIsInvite = false; }
            var sb = new System.Text.StringBuilder();
            sb.Append(_net.IsOnline).Append('|').Append(_net.RoomId).Append('|').Append(_net.StartRequested).Append('|').Append(_net.OutgoingInvite)
              .Append('|').Append(_netPicking).Append('|').Append(_netNote).Append('|').Append(_link?.LastError);
            foreach (var i in _net.IncomingInvites) sb.Append("|i:").Append(i.From);
            foreach (var f in Host.Online.Client.Friends) if (f.Online) sb.Append("|f:").Append(f.Code);
            return sb.ToString();
        }

        private void RefreshNet()
        {
            _netDirty = false;
            _netSig = NetSignature();
            if (_netPanel == null) return;
            var show = _net != null && _game != null && _game.Phase == GvzGame.PhaseSelect;
            _netPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            var t = Host.Strings;
            var wins = GvzProgress.NetWins(_game.Save);
            var losses = GvzProgress.NetLosses(_game.Save);
            _netRecord.text = t.T("nl.gvz.bilanz", ("siege", wins), ("niederlagen", losses));
            _netRecord.style.display = wins + losses > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _netInvites.Clear();
            foreach (var inv in _net.IncomingInvites)
            {
                if (_net.IsPaired) break;
                var from = inv.From;
                var line = Ux.Row();
                line.style.alignItems = Align.Center;
                line.style.marginTop = 4;
                var label = Ux.Text(t.T("gvz.netz.laedt_ein", ("name", inv.GoobyName)), "gb-sm", "gb-bold");
                label.style.flexGrow = 1; label.style.flexShrink = 1; label.style.whiteSpace = WhiteSpace.Normal;
                line.Add(label);
                var no = Ux.Btn(t.T("gvz.netz.ablehnen"), () => { Sfx.PlayUi("soft_back"); _net.Decline(from); RefreshNet(); }, "gb-btn-secondary", "gb-btn-sm");
                no.name = "gvz-netz-decline";
                no.style.marginRight = 6;
                line.Add(no);
                var yes = Ux.Btn(t.T("gvz.netz.annehmen"), () => { Sfx.PlayUi("soft_confirm"); Host.Online.AcceptGvz(from); }, "gb-btn-confirm", "gb-btn-sm");
                yes.name = "gvz-netz-accept";
                line.Add(yes);
                _netInvites.Add(line);
            }
            if (_net.IsPaired)
            {
                var side = t.T(_net.MySide == GvzNetClient.SideZombie ? "gvz.netz.seite_zombie" : "gvz.netz.seite_gooby");
                var status = t.T("gvz.netz.verbunden", ("name", _net.PartnerGoobyName), ("seite", side));
                if (_link != null && _link.LastError.Length > 0) status += " · " + t.T("gvz.netz.fehler", ("code", Host.Online.BoardErrorText(_link.LastError)));
                _netStatus.text = status;
                _netStart.style.display = DisplayStyle.Flex;
                _netStart.text = _net.StartRequested ? t.T("gvz.netz.warte_start", ("name", _net.PartnerGoobyName)) : t.T("gvz.netz.los");
                _netStart.SetEnabled(!_net.StartRequested);
                _netAction.text = t.T("gvz.netz.trennen");
                _netAction.SetEnabled(true);
                _netPicking = false;
                _netFriends.style.display = DisplayStyle.None;
                return;
            }
            _netStart.style.display = DisplayStyle.None;
            var friends = Host.Online.Client.Friends.Where(f => f.Online).ToList();
            var gate = !_net.IsOnline ? "gvz.netz.offline" : friends.Count == 0 ? "gvz.netz.keine_freunde" : "";
            _netAction.text = t.T("gvz.netz.button");
            _netAction.SetEnabled(gate.Length == 0);
            _netStatus.text = gate.Length > 0 ? t.T(gate) : _netNote.Length > 0 ? _netNote : t.T("gvz.netz.bereit");
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
                var invite = Ux.Btn(t.T("gvz.netz.einladen"), () => Challenge(friend.Code, friend.GoobyName), "gb-btn-primary", "gb-btn-sm");
                invite.name = "gvz-netz-invite-" + friend.Code;
                invite.style.marginLeft = 8;
                invite.SetEnabled(_net.OutgoingInvite != friend.Code);
                chip.Add(invite);
                _netFriends.Add(chip);
            }
        }

        private void Challenge(string code, string gooby)
        {
            var t = Host.Strings;
            Host.Online.InviteToGvz(code, gooby, (ok, err) =>
            {
                _netNoteIsInvite = ok;
                _netNote = ok ? t.T("gvz.netz.eingeladen", ("name", gooby)) : t.T("gvz.netz.fehler", ("code", Host.Online.BoardErrorText(err)));
                if (ok) _netPicking = false;
                _netDirty = true;
            });
        }

        private void OnNetNotice(string key)
        {
            var t = Host.Strings;
            var partner = _link != null && _link.RunPartnerGooby.Length > 0 ? _link.RunPartnerGooby : _net?.PartnerGoobyName ?? "Gooby";
            Debug.Log("[gvz] notice " + key);
            switch (key)
            {
                case "gvz.netz.start_banner":
                    var role = _game != null && _game.NetZombie
                        ? t.T("nl.gvz.rolle_zombie")
                        : t.T("nl.gvz.rolle_gooby", ("zeit", Clock(_game?.Net?.SecondsLeft() ?? 0)));
                    Banner(t.T(key, ("name", partner)) + " " + role, GvzGame.IntroSec + 2.4);
                    break;
                case "gvz.netz.partner_weg":
                case "gvz.netz.partner_da":
                    Banner(t.T(key, ("name", partner)), 2.6);
                    break;
                default:
                    PlaySfx("soft_error");
                    Host.Ui.Toast(t.T(key), "zombie", 3.6f);
                    _netDirty = true;
                    break;
            }
        }

        private static string Clock(int seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");

        private static void PaintLevelChip(Painter2D p, string setting, bool unlocked, int stars, float w, float h)
        {
            if (w <= 1f || h <= 1f) return;
            var (a, _) = LawnColors(setting);
            P.Rect(p, new Vector2(0f, 0f), new Vector2(w, 10f), a);
            var y = h * 0.74f;
            if (!unlocked)
            {
                var c = new Vector2(w * 0.5f, y);
                P.Arc(p, c + new Vector2(0f, -3f), 6f, 180f, 360f, 3f, new Color(0.45f, 0.38f, 0.35f));
                P.Rect(p, c + new Vector2(-8f, -3f), c + new Vector2(8f, 9f), new Color(0.45f, 0.38f, 0.35f));
                return;
            }
            for (var i = 0; i < 3; i++)
                P.Star(p, new Vector2(w * 0.5f + (i - 1) * 20f, y), 8f, i < stars ? Gold : new Color(0.85f, 0.8f, 0.74f));
        }

        // ── phases ──

        private void OnPhase(string phase)
        {
            if (_select == null) return;
            var t = Host.Strings;
            _select.style.display = phase == GvzGame.PhaseSelect ? DisplayStyle.Flex : DisplayStyle.None;
            var playing = phase != GvzGame.PhaseSelect;
            _counter.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            _strip.style.display = phase == GvzGame.PhasePlay ? DisplayStyle.Flex : DisplayStyle.None;
            _overlay.style.display = DisplayStyle.None;
            _overlay.Clear();
            _cardKey = "";
            switch (phase)
            {
                case GvzGame.PhaseSelect:
                    RefreshSelect();
                    break;
                case GvzGame.PhasePlay:
                    // online the link's start banner names the partner and our role
                    if (!_game.OnlineRun) Banner(t.T("gvz.intro.ziel", ("n", _game.LevelId)), GvzGame.IntroSec + 1.2);
                    break;
                case GvzGame.PhaseWon:
                    BuildEndCard(true);
                    break;
                case GvzGame.PhaseLost:
                    BuildEndCard(false);
                    break;
            }
        }

        private void BuildEndCard(bool won)
        {
            if (_game.OnlineRun)
            {
                BuildNetEndCard(won);
                return;
            }
            var t = Host.Strings;
            var card = Ux.Col("gb-card");
            card.name = "gvz-end";
            card.style.alignItems = Align.Center;
            card.style.paddingLeft = 28; card.style.paddingRight = 28; card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.minWidth = Length.Percent(42f);
            card.Add(Ux.Text(t.T(won ? "gvz.end.win" : "gvz.end.lose"), "gb-h2", "gb-bold"));
            if (won)
            {
                var stars = new P.Canvas { Paint = p => { for (var i = 0; i < 3; i++) P.Star(p, new Vector2(26f + i * 46f, 24f), 19f, i < (_game?.LastStars ?? 0) ? Gold : new Color(0.85f, 0.8f, 0.74f)); } };
                stars.style.width = 144; stars.style.height = 48; stars.style.marginTop = 8;
                card.Add(stars);
                card.Add(Ux.Text(t.T("gvz.end.score", ("n", _game.LastTotal)), "gb-bold"));
                if (_game.LastFirstClear)
                {
                    var first = Ux.Text(t.T("gvz.end.first_clear"), "gb-sm", "gb-bold");
                    first.style.color = MgScenery.Hex(0xD08A1E);
                    card.Add(first);
                }
            }
            else
            {
                var hint = Ux.Text(t.T("gvz.end.lose_hint"), "gb-sm");
                hint.style.marginTop = 6;
                card.Add(hint);
            }
            var row = Ux.Row();
            row.style.marginTop = 14;
            if (won && _game.LevelId < GvzData.LevelCount) row.Add(EndButton("gvz-next", t.T("gvz.end.next"), "gb-btn-primary", () => _game.Next()));
            row.Add(EndButton("gvz-again", t.T("gvz.end.retry"), won ? "gb-btn" : "gb-btn-primary", () => _game.Retry()));
            row.Add(EndButton("gvz-to-select", t.T("gvz.end.select"), "gb-btn", () => _game.BackToSelect()));
            card.Add(row);
            _overlay.Add(card);
            _overlay.style.display = DisplayStyle.Flex;
            if (FieldW > 1f) ScalePop(card, 1.1f);
        }

        /// <summary>The PvP card (gvz_overlay.gd build_netz_end): beat / beaten by the partner, the server's coins, rematch.</summary>
        private void BuildNetEndCard(bool won)
        {
            var t = Host.Strings;
            var partner = _link != null && _link.RunPartnerGooby.Length > 0 ? _link.RunPartnerGooby : "Gooby";
            Debug.Log("[gvz] over side=" + _game.Net.Side + " winner=" + _game.Net.Winner + " tick=" + _game.Run.Tick + " hash=" + _game.Net.StateHashText());
            var card = Ux.Col("gb-card");
            card.name = "gvz-end";
            card.style.alignItems = Align.Center;
            card.style.paddingLeft = 28; card.style.paddingRight = 28; card.style.paddingTop = 18; card.style.paddingBottom = 18;
            card.style.minWidth = Length.Percent(42f);
            card.Add(Ux.Text(t.T(won ? "gvz.end.netz_win" : "gvz.end.netz_lose", ("name", partner)), "gb-h2", "gb-bold"));
            if (won)
            {
                var stars = new P.Canvas { Paint = p => { for (var i = 0; i < 3; i++) P.Star(p, new Vector2(26f + i * 46f, 24f), 19f, Gold); } };
                stars.style.width = 144; stars.style.height = 48; stars.style.marginTop = 8;
                card.Add(stars);
            }
            _coinsLabel = Ux.Text("", "gb-bold");
            _coinsLabel.name = "gvz-netz-coins";
            _coinsLabel.style.color = MgScenery.Hex(0xD08A1E);
            _coinsLabel.style.marginTop = 4;
            card.Add(_coinsLabel);
            var hint = Ux.Text(t.T("gvz.end.netz_hint", ("name", partner)), "gb-sm");
            hint.style.marginTop = 6;
            card.Add(hint);
            var row = Ux.Row();
            row.style.marginTop = 14;
            var code = _link?.RunPartnerCode ?? "";
            if (code.Length > 0 && Host.Online != null)
                row.Add(EndButton("gvz-again-online", t.T("nl.gvz.revanche", ("name", partner)), "gb-btn-primary", () =>
                {
                    Challenge(code, partner);
                    _game.BackToSelect();
                }));
            row.Add(EndButton("gvz-to-select", t.T("gvz.end.select"), code.Length > 0 ? "gb-btn" : "gb-btn-primary", () => _game.BackToSelect()));
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

        // ── cards ──

        private void SyncCards()
        {
            var cards = _game.Cards();
            var key = string.Join(",", cards);
            if (key != _cardKey)
            {
                _cardKey = key;
                _strip.Clear();
                _cardButtons.Clear();
                // late levels unlock 12 Goobys + the shovel: the cards shrink to fit the strip instead of running off it
                var pitch = Mathf.Min(CardW + 4f, ((float)ViewW - StripX - 6f) / Mathf.Max(1, cards.Count));
                for (var i = 0; i < cards.Count; i++)
                {
                    var type = cards[i];
                    if (_cardButtons.ContainsKey(type)) continue;
                    var card = Ux.El("gb-card");
                    card.name = "gvz-card-" + type;
                    card.pickingMode = PickingMode.Position;
                    Place(card, StripX + i * pitch, StripTop, pitch - 4f, CardH);
                    card.style.paddingLeft = 0; card.style.paddingRight = 0; card.style.paddingTop = 0; card.style.paddingBottom = 0;
                    card.style.overflow = Overflow.Hidden;
                    var icon = new P.Canvas { Paint = p => PaintCardIcon(p, type, card.contentRect) };
                    Ux.Abs(icon, 0, 0, 0, 0);
                    card.Add(icon);
                    var cd = Ux.El();
                    cd.name = "cooldown";
                    cd.pickingMode = PickingMode.Ignore;
                    cd.style.position = Position.Absolute;
                    cd.style.left = 0; cd.style.right = 0; cd.style.top = 0;
                    cd.style.backgroundColor = new Color(0.25f, 0.2f, 0.18f, 0.45f);
                    card.Add(cd);
                    var cost = Ux.Text("", "gb-sm", "gb-bold", "gb-nowrap");
                    cost.name = "cost";
                    cost.pickingMode = PickingMode.Ignore;
                    cost.style.position = Position.Absolute;
                    cost.style.bottom = 2; cost.style.left = 0; cost.style.right = 0;
                    cost.style.unityTextAlign = TextAnchor.MiddleCenter;
                    card.Add(cost);
                    card.RegisterCallback<PointerDownEvent>(e =>
                    {
                        e.StopPropagation();
                        if (_game == null || !_game.AcceptsInput) return;
                        _game.SelectCard(type);
                        Sfx.PlayUi(_game.SelectedCard == "" ? "soft_back" : "soft_tap");
                        if (_game.SelectedCard != "") Banner(CardName(type), 1.0);
                    });
                    _cardButtons[type] = card;
                    _strip.Add(card);
                }
            }
            var t = Host.Strings;
            foreach (var kv in _cardButtons)
            {
                var type = kv.Key;
                var card = kv.Value;
                var costValue = _game.CardCost(type);
                var free = type != GvzGame.Shovel && costValue == 0;
                var costLabel = card.Q<Label>("cost");
                costLabel.text = type == GvzGame.Shovel ? "" : free ? t.T("gvz.hud.free") : costValue.ToString();
                var frac = (float)_game.CardCooldownFrac(type);
                card.Q("cooldown").style.height = Length.Percent(frac * 100f);
                var affordable = free || type == GvzGame.Shovel || _game.Resource >= costValue;
                card.style.opacity = affordable ? 1f : 0.55f;
                var on = _game.SelectedCard == type;
                var edge = on ? MgScenery.Hex(0xF2B630) : new Color(0f, 0f, 0f, 0f);
                card.style.borderTopColor = edge; card.style.borderBottomColor = edge; card.style.borderLeftColor = edge; card.style.borderRightColor = edge;
                var w = on ? 3f : 0f;
                card.style.borderTopWidth = w; card.style.borderBottomWidth = w; card.style.borderLeftWidth = w; card.style.borderRightWidth = w;
                card.style.translate = new Translate(0, on ? -6 : 0);
            }
        }

        private string CardName(string type) => Host.Strings.T((_game != null && _game.NetZombie ? "nl.gvz.zombie." : "gvz.tower.") + type);

        // ── frame hooks ──

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _anim += (float)dt;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            TimeLabel.text = g.Phase == GvzGame.PhaseSelect || g.Run == null ? t.T("gvz.select.title")
                : g.OnlineRun ? t.T("nl.gvz.timer", ("zeit", Clock(g.Net.SecondsLeft())))
                : t.T("gvz.hud.level", ("n", g.LevelId));
            ScoreLabel.text = g.LiveScore.ToString();
            if (g.Run != null)
            {
                var resource = g.Resource;
                if (resource != _shownNutella)
                {
                    if (_shownNutella >= 0 && resource > _shownNutella) ScalePop(_counter, 1.12f);
                    _shownNutella = resource;
                    _nutellaLabel.text = resource.ToString();
                    _counter.Q<P.Canvas>("gvz-counter-icon")?.MarkDirtyRepaint();
                }
                if (g.Phase == GvzGame.PhasePlay) SyncCards();
            }
            RenderNet(g);
            PlaceDoorGooby(g);
            if (_stage != null) RenderStage();
            _canvas.MarkDirtyRepaint();
            foreach (var card in _cardButtons.Values) card.Q<P.Canvas>()?.MarkDirtyRepaint();
        }

        private void RenderNet(GvzGame g)
        {
            if (_net == null) return;
            if (g.Phase == GvzGame.PhaseSelect)
            {
                // challenges run out without a Changed: a slow poll keeps the panel honest; rebuild only on a visible change
                _netPoll += Time.unscaledDeltaTime;
                if (_netPoll >= 0.5f || _netDirty)
                {
                    _netPoll = 0f;
                    _netDirty = false;
                    if (NetSignature() != _netSig) RefreshNet();
                }
            }
            var wait = g.OnlineRun && g.Phase == GvzGame.PhasePlay && g.NetWaiting;
            _waitLabel.style.display = wait ? DisplayStyle.Flex : DisplayStyle.None;
            if (wait) _waitLabel.text = Host.Strings.T("gvz.netz.warte_partner");
            if (_coinsLabel != null && _link?.LastResult != null && _coinsLabel.text.Length == 0)
            {
                _coinsLabel.text = Host.Strings.T("nl.gvz.belohnung", ("n", _link.LastResult.Coins));
                ScalePop(_coinsLabel, 1.2f);
            }
        }

        /// <summary>The doorway figure's box (46 view px, feet on the door sill), shown while defending the house.</summary>
        private void PlaceDoorGooby(GvzGame g)
        {
            if (_door == null) return;
            var show = g.Run != null && g.Phase != GvzGame.PhaseSelect && !g.NetZombie && _stage == null;
            _door.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show || FieldW <= 1f) return;
            var at = Px(13f, LawnY + LawnH * 0.5f + 30f - 46f);
            _door.style.left = at.x;
            _door.style.top = at.y;
            _door.style.width = S(46f);
            _door.style.height = S(46f);
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

        /// <summary>Health bars of hurt towers and zombies, drawn over their 3D figures (projected head height).</summary>
        private void PaintBars3D(Painter2D p, GvzRun run)
        {
            foreach (var key in run.Towers.Keys)
            {
                var t = run.Towers[key];
                if (t.Hp >= t.MaxHp || t.MaxHp <= 0) continue;
                var c = _stage.ToField(CellCentre3D(t.Lane, t.Col) + Vector3.up * CellMetres(t.Lane) * 1.05f, FieldW, FieldH);
                Bar(p, c, S(CellW * 0.5f), t.Hp / (float)t.MaxHp, true);
            }
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Hp >= z.MaxHp || z.MaxHp <= 0 || z.State == "dig") continue;
                var x = XToView(z.X);
                if (x > LawnX + LawnW + 40f) continue;
                var c = _stage.ToField(_stage.GroundPoint(x, LawnY + (z.Lane + 0.5f) * LaneH) + Vector3.up * CellMetres(z.Lane) * (z.Flying ? 1.9f : 1.2f), FieldW, FieldH);
                Bar(p, c, S(CellW * 0.42f), z.Hp / (float)z.MaxHp, false);
            }
        }

        private void Bar(Painter2D p, Vector2 c, float w, float frac, bool tower)
        {
            frac = Mathf.Clamp01(frac);
            var h = S(5f);
            P.Rect(p, c + new Vector2(-w * 0.5f, -h * 0.5f), c + new Vector2(w * 0.5f, h * 0.5f), new Color(0.2f, 0.15f, 0.15f, 0.55f));
            var col = tower ? (frac > 0.5f ? MgScenery.Hex(0x7CCB86) : frac > 0.25f ? MgScenery.Hex(0xF6C343) : Berry) : Berry;
            P.Rect(p, c + new Vector2(-w * 0.5f, -h * 0.5f), c + new Vector2(-w * 0.5f + w * frac, h * 0.5f), col);
        }

        // ── input ──

        private float Scale => Mathf.Max(0.0001f, Mathf.Min(FieldW / (float)ViewW, FieldH / (float)ViewH));
        private Vector2 Offset => new Vector2((FieldW - (float)ViewW * Scale) * 0.5f, (FieldH - (float)ViewH * Scale) * 0.5f);
        private Vector2 Px(float x, float y) => Offset + new Vector2(x, y) * Scale;
        private float S(float v) => v * Scale;
        private float LaneH => LawnH / 5f;
        private float CellW => LawnW / GvzLogic.Cols;
        private Vector2 CellCenter(int lane, int col) => Px(LawnX + (col + 0.5f) * CellW, LawnY + (lane + 0.5f) * LaneH);
        private float XToView(int xMm) => LawnX + xMm / (float)(GvzLogic.Cols * GvzLogic.CellMm) * LawnW;

        private void OnPointerDown(PointerDownEvent e)
        {
            if (_game == null || !_game.AcceptsInput || e.target != Field) return;
            var local = Field.WorldToLocal(e.position);
            var v = (new Vector2(local.x, local.y) - Offset) / Scale;
            // blobs first (they lie on the lawn and the finger means them) — the PvP zombie side collects none
            if (!_game.NetZombie)
            {
                var bestId = -1;
                var bestD = 44f;
                foreach (var drop in _game.Run.Drops)
                {
                    var c = new Vector2(LawnX + (drop.Col + 0.5f) * CellW, LawnY + (drop.Lane + 0.5f) * LaneH);
                    var d = Vector2.Distance(c, v);
                    if (d < bestD) { bestD = d; bestId = drop.Id; }
                }
                if (bestId >= 0)
                {
                    var amount = _game.Collect(bestId);
                    // online the collect is planned: the "+25" shows now, the nutella lands on its tick
                    if (amount > 0 && _game.OnlineRun && FieldW > 1f)
                    {
                        var dr = _game.Run.Drops.Find(x => x.Id == bestId);
                        if (dr != null) Float(CellCenter(dr.Lane, dr.Col), "+" + amount, MgScenery.Hex(0xB07A4E), 22f);
                    }
                    return;
                }
            }
            if (_game.SelectedCard == "") return;
            if (v.x < LawnX || v.x >= LawnX + LawnW || v.y < LawnY || v.y >= LawnY + LawnH) return;
            var col = Mathf.Clamp((int)((v.x - LawnX) / CellW), 0, GvzLogic.Cols - 1);
            var lane = Mathf.Clamp((int)((v.y - LawnY) / LaneH), 0, 4);
            var card = _game.SelectedCard;
            var result = _game.UseCard(lane, col);
            if (result.Ok)
            {
                PlaySfx(card == GvzGame.Shovel ? "soft_back" : "game_pop", card == GvzGame.Shovel ? 0f : -2f);
                Haptics.Tap();
                return;
            }
            PlaySfx("soft_error");
            var key = "gvz.hud.reason_" + result.Reason;
            if (Host.Strings.HasKey(key) && FieldW > 1f) Float(CellCenter(lane, col) - new Vector2(0f, S(30f)), Host.Strings.T(key), Berry, 20f);
        }

        // ── juice (gvz_game.gd _consume_events) ──

        private void OnEvent(GvzEvent ev)
        {
            if (FieldW <= 1f || _game?.Run == null) return;
            var t = Host.Strings;
            switch (ev.Kind)
            {
                case "place":
                    Burst(CellCenter(ev.Lane, ev.Col), new Color(0.75f, 0.9f, 0.6f), 8);
                    break;
                case "collect":
                    PlaySfx("game_coin", 2f, 0.8f);
                    Float(CellCenter(ev.Lane, ev.Col), "+" + ev.Amount, MgScenery.Hex(0xB07A4E), 22f);
                    break;
                case "shoot":
                    if (ev.Proj == "melon") PlaySfx("game_whoosh", -3f, 0.4f);
                    break;
                case "die":
                    PlaySfx("game_pop", -4f, 0.6f);
                    Burst(Px(XToView(ev.X), LawnY + (ev.Lane + 0.5f) * LaneH), ZombieSkin, 10);
                    break;
                case "blast":
                    PlaySfx("game_hit", -5f);
                    Shake(0.3f);
                    Burst(CellCenter(ev.Lane, ev.Col), new Color(1f, 0.6f, 0.25f), 22);
                    Flash(new Color(1f, 0.8f, 0.4f, 0.18f), 0.12f);
                    break;
                case "pop":
                    PlaySfx("game_pop", 4f, 0.8f);
                    break;
                case "mower":
                    PlaySfx("game_whoosh", 0f, 0.9f);
                    Haptics.Warn();
                    break;
                case "wave":
                    PlaySfx("game_countdown", ev.Huge ? -3f : 0f);
                    Banner(t.T(ev.Huge ? "gvz.hud.huge_wave" : "gvz.hud.wave", ("n", ev.N)), 2.0);
                    break;
                case "boss_enter":
                    PlaySfx("game_lose", -6f, 0.8f);
                    Shake(0.4f);
                    Banner(t.T("gvz.hud.boss"), 2.2);
                    break;
                case "boss_phase":
                    Banner(t.T("gvz.hud.boss_phase", ("n", ev.Phase)), 2.0);
                    Shake(0.35f);
                    break;
                case "boss_toss":
                    PlaySfx("game_hit", -2f, 0.8f);
                    Burst(CellCenter(ev.Lane, ev.Col), new Color(0.6f, 0.6f, 0.65f), 12);
                    break;
                case "tower_gone":
                    if (ev.Cause == "eaten") PlaySfx("game_miss", -2f, 0.7f);
                    break;
                case "knolle":
                    Shake(0.25f);
                    break;
                case "lost":
                    PlaySfx("game_lose");
                    Shake(0.7f);
                    break;
                case "won":
                    PlaySfx("game_win");
                    ConfettiRain(70);
                    break;
            }
            StageEvent(ev);
        }

        // ── painting ──

        private static (Color a, Color b) LawnColors(string setting)
        {
            switch (setting)
            {
                case "nacht": return (MgScenery.Hex(0x4F7364), MgScenery.Hex(0x476A5C));
                case "garage": return (MgScenery.Hex(0xCFC8BD), MgScenery.Hex(0xC3BCB1));
                case "garten": return (MgScenery.Hex(0xA9DA80), MgScenery.Hex(0x98CD72));
                case "dach": return (MgScenery.Hex(0xE7A274), MgScenery.Hex(0xDB9467));
                case "strasse": return (MgScenery.Hex(0x9298A2), MgScenery.Hex(0x878D97));
                default: return (MgScenery.Hex(0x9ED37A), MgScenery.Hex(0x8FC66B));
            }
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f) return;
            var run = _game.Run;
            var setting = run != null ? GvzJson.S(run.Level, "setting", "vorgarten") : "vorgarten";
            var night = run != null && run.Mod("night");
            if (_stage != null)
            {
                // the yard, its figures and the house are 3D: the canvas keeps the hints, bars, weather and meters
                P.Rect(p, Px(0f, StripTop - 6f), Px(960f, 540f), new Color(0.98f, 0.95f, 0.88f, 0.9f));
                if (run == null) return;
                if (_game.SelectedCard != "" && _game.AcceptsInput) PaintPlacementHints(p, run);
                PaintBars3D(p, run);
                if (run.Mod("rain")) PaintRain(p);
                var fog3d = _game.FogCols();
                if (fog3d > 0) PaintFog(p, fog3d);
                if (night) P.Rect(p, Px(0f, 0f), Px(960f, 540f), new Color(0.08f, 0.1f, 0.25f, 0.2f));
                if (_game.OnlineRun) PaintTimerMeter(p, run);
                else PaintWaveMeter(p, run);
                return;
            }
            PaintBackdrop(p, setting, night);
            PaintLawn(p, setting);
            PaintHouse(p, night);
            if (run == null) return;
            if (_game.SelectedCard != "" && _game.AcceptsInput) PaintPlacementHints(p, run);
            foreach (var key in run.Towers.Keys) PaintTower(p, run.Towers[key], run);
            foreach (var m in run.Mowers.Keys) PaintMower(p, m, run.Mowers[m]);
            var zombies = new List<GvzZombie>(run.Zombies);
            zombies.Sort((a, b) => a.Lane != b.Lane ? a.Lane.CompareTo(b.Lane) : b.X.CompareTo(a.X));
            foreach (var z in zombies) PaintZombie(p, z, run);
            if (run.Boss != null && run.Boss.Hp > 0) PaintBoss(p, run.Boss);
            foreach (var pr in run.Projectiles) PaintProjectile(p, pr);
            foreach (var d in run.Drops) PaintDrop(p, d, run.Tick);
            if (run.Mod("rain")) PaintRain(p);
            var fog = _game.FogCols();
            if (fog > 0) PaintFog(p, fog);
            if (night) P.Rect(p, Px(0f, 0f), Px(960f, 540f), new Color(0.08f, 0.1f, 0.25f, 0.12f));
            if (_game.OnlineRun) PaintTimerMeter(p, run);
            else PaintWaveMeter(p, run);
        }

        /// <summary>PvP: the survive timer as a bar (top right of the lawn) — a zombie head walks toward the house as time runs out.</summary>
        private void PaintTimerMeter(Painter2D p, GvzRun run)
        {
            var total = Mathf.Max(1, _game.Net.SurviveTicks);
            var frac = Mathf.Clamp01(run.Tick / (float)total);
            var a = Px(LawnX + LawnW - 220f, LawnY - 22f);
            var b = Px(LawnX + LawnW - 8f, LawnY - 10f);
            P.RoundRect(p, a, b, S(6f), new Color(0.3f, 0.24f, 0.2f, 0.5f));
            var fill = new Vector2(Mathf.Lerp(b.x, a.x, frac), b.y);
            P.RoundRect(p, new Vector2(fill.x, a.y), b, S(6f), _game.NetZombie ? MgScenery.Hex(0xE0655F) : MgScenery.Hex(0x8BC34A));
            P.OutlinedBlob(p, new Vector2(fill.x, (a.y + b.y) * 0.5f), S(8f), S(8f), S(1.5f), Gold, Outline);
        }

        private void PaintBackdrop(Painter2D p, string setting, bool night)
        {
            var skyTop = night ? MgScenery.Hex(0x2E3A5C) : setting == "garage" ? MgScenery.Hex(0xB9B2A8) : MgScenery.Hex(0xBFE6FF);
            var skyLow = night ? MgScenery.Hex(0x4A5A80) : setting == "garage" ? MgScenery.Hex(0xD6D0C6) : MgScenery.Hex(0xEAF8FF);
            for (var i = 0; i < 6; i++)
                P.Rect(p, Px(0f, i * 16f), Px(960f, (i + 1) * 16f + 1f), Color.Lerp(skyTop, skyLow, i / 5f));
            if (night)
            {
                P.Circle(p, Px(860f, 34f), S(18f), MgScenery.Hex(0xFFF3C4));
                P.Circle(p, Px(868f, 30f), S(16f), skyTop);
                for (var i = 0; i < 14; i++) P.Circle(p, Px(40f + i * 61f % 900f, 12f + i * 37f % 60f), S(1.6f), new Color(1f, 1f, 0.9f, 0.8f));
            }
            else if (setting != "garage")
            {
                P.Blob(p, Px(200f, 32f), S(46f), S(14f), new Color(1f, 1f, 1f, 0.85f));
                P.Blob(p, Px(232f, 26f), S(30f), S(12f), new Color(1f, 1f, 1f, 0.85f));
                P.Blob(p, Px(640f, 40f), S(52f), S(15f), new Color(1f, 1f, 1f, 0.8f));
            }
            switch (setting)
            {
                case "strasse":
                    for (var i = 0; i < 7; i++)
                    {
                        var x = 120f + i * 128f;
                        var h = 38f + (i * 23f % 30f);
                        P.Rect(p, Px(x, 92f - h), Px(x + 96f, 92f), Color.Lerp(MgScenery.Hex(0xF2C9B6), MgScenery.Hex(0xBFD8E8), i % 3 / 2f));
                        for (var w = 0; w < 3; w++) P.Rect(p, Px(x + 12f + w * 28f, 92f - h + 10f), Px(x + 26f + w * 28f, 92f - h + 22f), new Color(1f, 1f, 1f, 0.7f));
                    }
                    break;
                case "garage":
                    for (var i = 0; i < 9; i++) P.Rect(p, Px(100f + i * 100f, 30f), Px(170f + i * 100f, 36f), new Color(0.55f, 0.5f, 0.46f, 0.5f));
                    break;
                case "dach":
                    for (var i = 0; i < 6; i++) P.Poly(p, new[] { Px(100f + i * 150f, 92f), Px(160f + i * 150f, 52f), Px(220f + i * 150f, 92f) }, new Color(0.7f, 0.42f, 0.32f, 0.55f));
                    break;
                default:
                    // garden fence + bushes along the horizon
                    for (var x = 90f; x < 960f; x += 30f)
                    {
                        P.Rect(p, Px(x, 62f), Px(x + 16f, 94f), night ? MgScenery.Hex(0x8C8A99) : MgScenery.Hex(0xFFF6E8));
                        P.Poly(p, new[] { Px(x, 62f), Px(x + 8f, 54f), Px(x + 16f, 62f) }, night ? MgScenery.Hex(0x8C8A99) : MgScenery.Hex(0xFFF6E8));
                    }
                    P.Rect(p, Px(90f, 74f), Px(960f, 79f), night ? MgScenery.Hex(0x7C7A88) : MgScenery.Hex(0xEADBC6));
                    for (var i = 0; i < 6; i++) P.Blob(p, Px(140f + i * 150f, 88f), S(40f), S(16f), night ? MgScenery.Hex(0x3F5E50) : MgScenery.Hex(0x7FBF6A));
                    break;
            }
        }

        private void PaintLawn(Painter2D p, string setting)
        {
            var (a, b) = LawnColors(setting);
            P.Rect(p, Px(LawnX - 4f, LawnY - 4f), Px(LawnX + LawnW + 4f, LawnY + LawnH + 4f), Color.Lerp(b, Color.black, 0.15f));
            for (var lane = 0; lane < 5; lane++)
            {
                var active = _game.Run == null || _game.Run.Lanes.Contains(lane);
                for (var col = 0; col < GvzLogic.Cols; col++)
                {
                    var c = (lane + col) % 2 == 0 ? a : b;
                    if (!active) c = Color.Lerp(c, MgScenery.Hex(0xB9A78F), 0.65f);
                    P.Rect(p, Px(LawnX + col * CellW, LawnY + lane * LaneH), Px(LawnX + (col + 1) * CellW, LawnY + (lane + 1) * LaneH), c);
                }
            }
            // the strip under the cards
            P.Rect(p, Px(0f, StripTop - 6f), Px(960f, 540f), new Color(0.98f, 0.95f, 0.88f, 0.9f));
        }

        private void PaintHouse(Painter2D p, bool night)
        {
            var wall = night ? MgScenery.Hex(0xC9B9D6) : MgScenery.Hex(0xFFE6CF);
            P.Rect(p, Px(0f, LawnY - 30f), Px(LawnX - 6f, LawnY + LawnH), wall);
            P.Poly(p, new[] { Px(-10f, LawnY - 26f), Px((LawnX - 6f) * 0.5f, LawnY - 66f), Px(LawnX + 4f, LawnY - 26f) }, MgScenery.Hex(0xE07A6A));
            P.RoundRect(p, Px(16f, LawnY + LawnH * 0.5f - 34f), Px(LawnX - 22f, LawnY + LawnH * 0.5f + 30f), S(6f), MgScenery.Hex(0x9A6A4A));
            P.Circle(p, Px(LawnX - 30f, LawnY + LawnH * 0.5f), S(2.5f), Gold);
            for (var i = 0; i < 2; i++)
            {
                var y = LawnY + 22f + i * (LawnH - 80f);
                P.Rect(p, Px(14f, y), Px(LawnX - 22f, y + 34f), night ? MgScenery.Hex(0xFFE8A0) : MgScenery.Hex(0xBFE6FF));
                P.Rect(p, Px((LawnX - 8f) * 0.5f - 1f, y), Px((LawnX - 8f) * 0.5f + 1f, y + 34f), Color.white);
            }
        }

        private void PaintPlacementHints(Painter2D p, GvzRun run)
        {
            var pulse = 0.12f + 0.06f * Mathf.Sin(_anim * 5f);
            if (_game.NetZombie)
            {
                // zombies enter at the right edge of the tapped lane
                foreach (var lane in run.Lanes)
                {
                    var a = Px(LawnX + (GvzLogic.Cols - 3) * CellW + 3f, LawnY + lane * LaneH + 3f);
                    var b = Px(LawnX + LawnW - 3f, LawnY + (lane + 1) * LaneH - 3f);
                    P.RoundRect(p, a, b, S(8f), new Color(0.55f, 0.85f, 0.5f, pulse + 0.08f));
                }
                return;
            }
            var shovel = _game.SelectedCard == GvzGame.Shovel;
            foreach (var lane in run.Lanes)
                for (var col = 0; col < GvzLogic.Cols; col++)
                {
                    var occupied = run.Towers.ContainsKey(GvzLogic.CellKey(lane, col));
                    if (occupied != shovel) continue;
                    var a = Px(LawnX + col * CellW + 3f, LawnY + lane * LaneH + 3f);
                    var b = Px(LawnX + (col + 1) * CellW - 3f, LawnY + (lane + 1) * LaneH - 3f);
                    P.RoundRect(p, a, b, S(8f), shovel ? new Color(0.9f, 0.4f, 0.3f, pulse) : new Color(1f, 1f, 1f, pulse + 0.06f));
                }
        }

        private void PaintGoobyBody(Painter2D p, Vector2 c, float s, Color body, bool sleepy = false)
        {
            var ow = s * 0.06f;
            foreach (var side in new[] { -1f, 1f })
            {
                P.RotBlob(p, c + new Vector2(side * s * 0.42f, s * 0.02f), s * 0.14f + ow, s * 0.3f + ow, side * 0.35f, Outline);
                P.RotBlob(p, c + new Vector2(side * s * 0.42f, s * 0.02f), s * 0.14f, s * 0.3f, side * 0.35f, body);
                P.RotBlob(p, c + new Vector2(side * s * 0.43f, s * 0.06f), s * 0.07f, s * 0.17f, side * 0.35f, EarPink);
            }
            P.OutlinedBlob(p, c, s * 0.46f, s * 0.42f, ow, body, Outline);
            var eye = c + new Vector2(0f, -s * 0.06f);
            foreach (var side in new[] { -1f, 1f })
            {
                var e = eye + new Vector2(side * s * 0.16f, 0f);
                if (sleepy) P.Arc(p, e, s * 0.06f, 20f, 160f, s * 0.035f, Outline);
                else
                {
                    P.Circle(p, e, s * 0.06f, Outline);
                    P.Circle(p, e + new Vector2(-s * 0.02f, -s * 0.02f), s * 0.02f, Color.white);
                }
                P.Circle(p, c + new Vector2(side * s * 0.28f, s * 0.08f), s * 0.07f, Cheek);
            }
        }

        private void PaintTower(Painter2D p, GvzTower t, GvzRun run)
        {
            var c = CellCenter(t.Lane, t.Col);
            var s = S(Mathf.Min(CellW, LaneH) * 0.86f);
            var bob = Mathf.Sin(_anim * 3f + t.Id) * s * 0.02f;
            DrawTowerArt(p, t.Type, c + new Vector2(0f, bob), s, run, t);
            if (t.Hp < t.MaxHp && t.MaxHp > 0)
            {
                var w = s * 0.7f;
                var frac = Mathf.Clamp01(t.Hp / (float)t.MaxHp);
                P.Rect(p, c + new Vector2(-w * 0.5f, s * 0.48f), c + new Vector2(w * 0.5f, s * 0.54f), new Color(0.3f, 0.2f, 0.18f, 0.6f));
                P.Rect(p, c + new Vector2(-w * 0.5f, s * 0.48f), c + new Vector2(-w * 0.5f + w * frac, s * 0.54f), frac > 0.5f ? MgScenery.Hex(0x7CCB86) : frac > 0.25f ? MgScenery.Hex(0xF6C343) : Berry);
            }
        }

        /// <summary>Tower art (also the card icons): a cream Gooby with the tool of its job.</summary>
        private void DrawTowerArt(Painter2D p, string type, Vector2 c, float s, GvzRun run, GvzTower t)
        {
            switch (type)
            {
                case "schnarch_knolle":
                {
                    var armed = t == null || run == null || run.Tick >= (t.ArmedAt ?? 0);
                    P.OutlinedBlob(p, c + new Vector2(0f, s * 0.12f), s * 0.36f, s * 0.26f, s * 0.05f, MgScenery.Hex(0xC79A62), Outline);
                    P.Circle(p, c + new Vector2(-s * 0.1f, s * 0.08f), s * 0.03f, Outline);
                    P.Circle(p, c + new Vector2(s * 0.1f, s * 0.08f), s * 0.03f, Outline);
                    if (armed) P.Circle(p, c + new Vector2(0f, -s * 0.18f), s * 0.07f, Mathf.Sin(_anim * 8f) > 0f ? Berry : MgScenery.Hex(0xFF9AA8));
                    else P.Arc(p, c + new Vector2(s * 0.2f, -s * 0.25f), s * 0.06f, 0f, 300f, s * 0.03f, MgScenery.Hex(0x7B62C9));
                    return;
                }
                case "boom_beere":
                {
                    var flash = t != null && t.FuseAt.HasValue && Mathf.Sin(_anim * 22f) > 0f;
                    var red = flash ? MgScenery.Hex(0xFF7A7A) : Berry;
                    P.Line(p, c + new Vector2(0f, -s * 0.3f), c + new Vector2(-s * 0.14f, -s * 0.05f), s * 0.05f, Leaf);
                    P.Line(p, c + new Vector2(0f, -s * 0.3f), c + new Vector2(s * 0.16f, -s * 0.02f), s * 0.05f, Leaf);
                    P.OutlinedBlob(p, c + new Vector2(-s * 0.16f, s * 0.08f), s * 0.2f, s * 0.2f, s * 0.04f, red, Outline);
                    P.OutlinedBlob(p, c + new Vector2(s * 0.18f, s * 0.12f), s * 0.2f, s * 0.2f, s * 0.04f, red, Outline);
                    P.Circle(p, c + new Vector2(-s * 0.22f, s * 0.02f), s * 0.05f, new Color(1f, 1f, 1f, 0.7f));
                    P.Circle(p, c + new Vector2(s * 0.12f, s * 0.06f), s * 0.05f, new Color(1f, 1f, 1f, 0.7f));
                    return;
                }
                case "trampolin_gooby":
                {
                    P.Line(p, c + new Vector2(-s * 0.32f, s * 0.12f), c + new Vector2(-s * 0.32f, s * 0.36f), s * 0.05f, Outline);
                    P.Line(p, c + new Vector2(s * 0.32f, s * 0.12f), c + new Vector2(s * 0.32f, s * 0.36f), s * 0.05f, Outline);
                    P.OutlinedBlob(p, c + new Vector2(0f, s * 0.12f), s * 0.4f, s * 0.1f, s * 0.04f, MgScenery.Hex(0x6FA8E8), Outline);
                    PaintGoobyBody(p, c + new Vector2(0f, -s * 0.12f + Mathf.Abs(Mathf.Sin(_anim * 5f)) * -s * 0.08f), s * 0.6f, Cream);
                    var charges = t?.Charges ?? 3;
                    for (var i = 0; i < charges; i++) P.Circle(p, c + new Vector2(-s * 0.12f + i * s * 0.12f, s * 0.36f), s * 0.035f, Gold);
                    return;
                }
                case "dicker_bert":
                {
                    P.OutlinedBlob(p, c + new Vector2(0f, s * 0.02f), s * 0.42f, s * 0.46f, s * 0.06f, MgScenery.Hex(0xE9D3B0), Outline);
                    var hurt = t != null && t.Hp < t.MaxHp * 0.5f;
                    P.Circle(p, c + new Vector2(-s * 0.14f, -s * 0.08f), s * 0.05f, Outline);
                    P.Circle(p, c + new Vector2(s * 0.14f, -s * 0.08f), s * 0.05f, Outline);
                    P.Arc(p, c + new Vector2(0f, s * 0.1f), s * 0.1f, hurt ? 200f : 20f, hurt ? 340f : 160f, s * 0.03f, Outline);
                    if (hurt) P.Line(p, c + new Vector2(-s * 0.3f, -s * 0.25f), c + new Vector2(-s * 0.1f, -s * 0.32f), s * 0.05f, MgScenery.Hex(0xF3E3C3));
                    return;
                }
                case "melonen_meier":
                {
                    P.Rect(p, c + new Vector2(-s * 0.34f, s * 0.2f), c + new Vector2(s * 0.34f, s * 0.32f), MgScenery.Hex(0x9A6A4A));
                    P.Line(p, c + new Vector2(-s * 0.1f, s * 0.2f), c + new Vector2(s * 0.28f, -s * 0.28f), s * 0.07f, MgScenery.Hex(0x9A6A4A));
                    P.OutlinedBlob(p, c + new Vector2(s * 0.28f, -s * 0.3f), s * 0.17f, s * 0.15f, s * 0.04f, MgScenery.Hex(0x5FB55A), Outline);
                    P.Line(p, c + new Vector2(s * 0.18f, -s * 0.3f), c + new Vector2(s * 0.38f, -s * 0.3f), s * 0.025f, MgScenery.Hex(0x2F7A32));
                    PaintGoobyBody(p, c + new Vector2(-s * 0.16f, s * 0.02f), s * 0.52f, Cream);
                    return;
                }
                case "shovel":
                    P.Line(p, c + new Vector2(-s * 0.18f, -s * 0.28f), c + new Vector2(s * 0.08f, s * 0.12f), s * 0.07f, MgScenery.Hex(0x9A6A4A));
                    P.Poly(p, new[] { c + new Vector2(s * 0.02f, s * 0.06f), c + new Vector2(s * 0.24f, s * 0.04f), c + new Vector2(s * 0.3f, s * 0.34f), c + new Vector2(s * 0.1f, s * 0.38f) }, MgScenery.Hex(0xA9B2BA));
                    return;
            }
            var body = type == "eis_gooby" ? MgScenery.Hex(0xDDF2FB) : type == "goldi" ? MgScenery.Hex(0xFFE58A) : Cream;
            PaintGoobyBody(p, c, s * 0.82f, body);
            switch (type)
            {
                case "moehrenschuetze":
                case "doppelmoehre":
                case "eis_gooby":
                {
                    var tip = type == "eis_gooby" ? Frost : Carrot;
                    var n = type == "doppelmoehre" ? 2 : 1;
                    for (var i = 0; i < n; i++)
                    {
                        var y = c.y + s * 0.12f - i * s * 0.16f;
                        P.Poly(p, new[] { new Vector2(c.x + s * 0.18f, y - s * 0.07f), new Vector2(c.x + s * 0.52f, y), new Vector2(c.x + s * 0.18f, y + s * 0.07f) }, tip);
                        P.Line(p, new Vector2(c.x + s * 0.14f, y), new Vector2(c.x + s * 0.06f, y - s * 0.06f), s * 0.04f, Leaf);
                    }
                    if (type == "eis_gooby") P.Star(p, c + new Vector2(0f, -s * 0.36f), s * 0.1f, Color.white, 0.4f);
                    break;
                }
                case "nutella_sammler":
                case "goldi":
                    if (type == "goldi") P.Star(p, c + new Vector2(0f, -s * 0.42f), s * 0.11f, Gold);
                    PaintJarIcon(p, c + new Vector2(s * 0.26f, s * 0.14f), s * 0.16f);
                    break;
                case "magnet_gooby":
                    P.Arc(p, c + new Vector2(s * 0.32f, -s * 0.04f), s * 0.13f, -90f, 90f, s * 0.08f, Berry);
                    P.Rect(p, c + new Vector2(s * 0.24f, -s * 0.21f), c + new Vector2(s * 0.32f, -s * 0.13f), MgScenery.Hex(0xD6DCE2));
                    P.Rect(p, c + new Vector2(s * 0.24f, s * 0.05f), c + new Vector2(s * 0.32f, s * 0.13f), MgScenery.Hex(0xD6DCE2));
                    break;
                case "pust_gooby":
                    P.Circle(p, c + new Vector2(s * 0.2f, s * 0.12f), s * 0.08f, Cheek);
                    for (var i = 0; i < 3; i++)
                        P.Arc(p, c + new Vector2(s * (0.46f + i * 0.08f), s * 0.06f), s * (0.06f + i * 0.04f), -60f, 60f, s * 0.025f, new Color(0.6f, 0.8f, 0.95f, 0.9f - i * 0.2f));
                    break;
                case "sternchen_gooby":
                    P.Star(p, c + new Vector2(s * 0.34f, -s * 0.06f), s * 0.16f, Gold);
                    break;
            }
        }

        private void PaintJarIcon(Painter2D p, Vector2 c, float r)
        {
            P.Rect(p, c + new Vector2(-r * 0.8f, -r * 0.45f), c + new Vector2(r * 0.8f, r * 0.95f), Outline);
            P.Rect(p, c + new Vector2(-r * 0.66f, -r * 0.32f), c + new Vector2(r * 0.66f, r * 0.82f), Nutella);
            P.Rect(p, c + new Vector2(-r * 0.9f, -r * 0.95f), c + new Vector2(r * 0.9f, -r * 0.35f), Outline);
            P.Rect(p, c + new Vector2(-r * 0.76f, -r * 0.82f), c + new Vector2(r * 0.76f, -r * 0.48f), NutellaLid);
            P.Blob(p, c + new Vector2(0f, r * 0.2f), r * 0.4f, r * 0.26f, new Color(1f, 1f, 1f, 0.9f));
        }

        private void PaintCardIcon(Painter2D p, string type, Rect r)
        {
            if (r.width <= 1f) return;
            var s = Mathf.Min(r.width, r.height) * 0.78f;
            if (_game != null && _game.NetZombie)
            {
                DrawZombieCardArt(p, type, new Vector2(r.width * 0.5f, r.height * 0.44f), s);
                return;
            }
            DrawTowerArt(p, type, new Vector2(r.width * 0.5f, r.height * 0.42f), s, _game?.Run, null);
        }

        /// <summary>A summon card of the PvP zombie side: the zombie with its armour from the balance row.</summary>
        private void DrawZombieCardArt(Painter2D p, string type, Vector2 c, float s)
        {
            var row = _game?.Run?.Balance?["zombies"]?[type] as JObject;
            var z = new GvzZombie
            {
                Id = type.Length, Type = type, Hp = 1, MaxHp = 1,
                Armor = GvzJson.S(row, "armor", ""), ArmorHp = GvzJson.I(row, "armor_hp", 0),
                Flying = GvzJson.B(row, "flying", false), Jumps = GvzJson.I(row, "jumps", 0),
            };
            DrawZombieArt(p, z, c + new Vector2(s * 0.06f, z.Flying ? s * 0.22f : 0f), s * 0.78f, false);
        }

        /// <summary>The zombie side's mud counter icon: a brown splat with two drops.</summary>
        private static void PaintMudIcon(Painter2D p, Vector2 c, float r)
        {
            P.OutlinedBlob(p, c + new Vector2(0f, r * 0.25f), r * 1.0f, r * 0.62f, r * 0.14f, Mud, Outline);
            P.OutlinedBlob(p, c + new Vector2(-r * 0.55f, -r * 0.45f), r * 0.26f, r * 0.26f, r * 0.1f, Mud, Outline);
            P.OutlinedBlob(p, c + new Vector2(r * 0.5f, -r * 0.62f), r * 0.2f, r * 0.2f, r * 0.08f, Mud, Outline);
            P.Circle(p, c + new Vector2(-r * 0.3f, r * 0.08f), r * 0.16f, new Color(1f, 1f, 1f, 0.35f));
        }

        private void PaintMower(Painter2D p, int lane, GvzMower m)
        {
            if (m.Used && !m.Active) return;
            var x = m.Active ? XToView(m.X) : LawnX - 30f;
            var c = Px(x, LawnY + (lane + 0.5f) * LaneH + 6f);
            var s = S(34f);
            P.OutlinedBlob(p, c + new Vector2(0f, s * 0.42f), s * 0.6f, s * 0.16f, s * 0.05f, MgScenery.Hex(0xFF8B8B), Outline);
            P.Circle(p, c + new Vector2(-s * 0.36f, s * 0.6f), s * 0.12f, Outline);
            P.Circle(p, c + new Vector2(s * 0.36f, s * 0.6f), s * 0.12f, Outline);
            PaintGoobyBody(p, c, s * 0.9f, Cream, sleepy: !m.Active);
            if (m.Active) for (var i = 0; i < 3; i++) P.Line(p, c + new Vector2(-s * (0.7f + i * 0.2f), -s * 0.1f + i * s * 0.18f), c + new Vector2(-s * (1.1f + i * 0.2f), -s * 0.1f + i * s * 0.18f), s * 0.05f, new Color(1f, 1f, 1f, 0.8f));
        }

        private void PaintZombie(Painter2D p, GvzZombie z, GvzRun run)
        {
            var y = LawnY + (z.Lane + 0.5f) * LaneH;
            var x = XToView(z.X);
            if (x > LawnX + LawnW + 40f) return;
            var s = S(LaneH * 0.82f);
            var c = Px(x, y + (z.Flying ? -LaneH * 0.28f : 0f));
            if (z.State == "dig")
            {
                P.OutlinedBlob(p, Px(x, y + LaneH * 0.25f), s * 0.3f, s * 0.12f, s * 0.04f, MgScenery.Hex(0x8C6A4A), Outline);
                P.Circle(p, Px(x - 6f, y + LaneH * 0.12f), s * 0.05f, MgScenery.Hex(0xA98262));
                return;
            }
            DrawZombieArt(p, z, c, s, z.SlowUntil > run.Tick && !z.Flying);
        }

        /// <summary>Zombie art (also the summon cards of the PvP zombie side).</summary>
        private void DrawZombieArt(Painter2D p, GvzZombie z, Vector2 c, float s, bool slowed)
        {
            var walk = Mathf.Sin(_anim * 7f + z.Id) * 0.08f;
            var skin = z.Raged ? MgScenery.Hex(0xE7A0A0) : slowed ? Color.Lerp(ZombieSkin, Frost, 0.55f) : ZombieSkin;
            var big = z.Type == "brocken" ? 1.35f : z.Type == "sprinter" ? 0.9f : 1f;
            s *= big;
            if (z.Flying)
            {
                P.Line(p, c + new Vector2(0f, -s * 0.38f), c + new Vector2(0f, -s * 0.72f), s * 0.025f, Outline);
                P.OutlinedBlob(p, c + new Vector2(0f, -s * 0.95f), s * 0.26f, s * 0.32f, s * 0.035f, MgScenery.Hex(0xFF8FB0), Outline);
                P.Circle(p, c + new Vector2(-s * 0.08f, -s * 1.06f), s * 0.06f, new Color(1f, 1f, 1f, 0.7f));
            }
            var shirt = z.Type == "sprinter" ? MgScenery.Hex(0xF2C14E) : z.Type == "tuersteher" ? MgScenery.Hex(0x5A6070) : z.Type == "brocken" ? MgScenery.Hex(0x8A6A9E) : MgScenery.Hex(0x7FA7D9);
            // body (leaning toward the house), arms reaching left
            P.OutlinedBlob(p, c + new Vector2(0f, s * 0.2f), s * 0.24f, s * 0.26f, s * 0.04f, shirt, Outline);
            var reach = z.State == "eat" ? Mathf.Sin(_anim * 12f) * s * 0.05f : 0f;
            P.Line(p, c + new Vector2(-s * 0.08f, s * 0.08f), c + new Vector2(-s * 0.42f + reach, s * (0.05f + walk)), s * 0.09f, Outline);
            P.Line(p, c + new Vector2(-s * 0.08f, s * 0.08f), c + new Vector2(-s * 0.4f + reach, s * (0.05f + walk)), s * 0.06f, skin);
            if (!z.Flying)
            {
                P.Line(p, c + new Vector2(-s * 0.08f, s * 0.42f), c + new Vector2(-s * (0.12f + walk), s * 0.56f), s * 0.08f, Outline);
                P.Line(p, c + new Vector2(s * 0.08f, s * 0.42f), c + new Vector2(s * (0.1f - walk), s * 0.56f), s * 0.08f, Outline);
            }
            var head = c + new Vector2(-s * 0.04f, -s * 0.16f);
            P.OutlinedBlob(p, head, s * 0.24f, s * 0.22f, s * 0.04f, skin, Outline);
            P.Circle(p, head + new Vector2(-s * 0.1f, -s * 0.02f), s * 0.055f, Color.white);
            P.Circle(p, head + new Vector2(-s * 0.1f, -s * 0.02f), s * 0.025f, Outline);
            P.Circle(p, head + new Vector2(s * 0.05f, -s * 0.03f), s * 0.04f, Color.white);
            P.Circle(p, head + new Vector2(s * 0.05f, -s * 0.03f), s * 0.018f, Outline);
            P.Arc(p, head + new Vector2(-s * 0.04f, s * 0.1f), s * 0.06f, 200f, 340f, s * 0.025f, ZombieDark);
            if (z.ArmorHp > 0)
            {
                switch (z.Armor)
                {
                    case "huetchen":
                        P.Poly(p, new[] { head + new Vector2(-s * 0.16f, -s * 0.12f), head + new Vector2(s * 0.14f, -s * 0.12f), head + new Vector2(-s * 0.01f, -s * 0.5f) }, MgScenery.Hex(0xFF9A3C));
                        P.Rect(p, head + new Vector2(-s * 0.1f, -s * 0.28f), head + new Vector2(s * 0.08f, -s * 0.24f), Color.white);
                        break;
                    case "eimer":
                        P.Poly(p, new[] { head + new Vector2(-s * 0.22f, -s * 0.08f), head + new Vector2(s * 0.2f, -s * 0.08f), head + new Vector2(s * 0.15f, -s * 0.42f), head + new Vector2(-s * 0.17f, -s * 0.42f) }, MgScenery.Hex(0xA9B2BA));
                        P.Rect(p, head + new Vector2(-s * 0.18f, -s * 0.3f), head + new Vector2(s * 0.16f, -s * 0.26f), MgScenery.Hex(0x8A939B));
                        break;
                    case "zeitung":
                        P.Rect(p, c + new Vector2(-s * 0.46f, -s * 0.06f), c + new Vector2(-s * 0.1f, s * 0.36f), MgScenery.Hex(0xF4F0E6));
                        for (var i = 0; i < 4; i++) P.Rect(p, c + new Vector2(-s * 0.42f, s * (0.0f + i * 0.08f)), c + new Vector2(-s * 0.14f, s * (0.02f + i * 0.08f)), new Color(0.5f, 0.5f, 0.55f, 0.6f));
                        break;
                    case "schild":
                        P.RoundRect(p, c + new Vector2(-s * 0.56f, -s * 0.3f), c + new Vector2(-s * 0.3f, s * 0.5f), s * 0.05f, MgScenery.Hex(0xB5885E));
                        P.Rect(p, c + new Vector2(-s * 0.52f, s * 0.05f), c + new Vector2(-s * 0.34f, s * 0.09f), MgScenery.Hex(0xE6C25A));
                        break;
                }
            }
            if (z.Jumps > 0) P.Line(p, c + new Vector2(s * 0.22f, -s * 0.3f), c + new Vector2(s * 0.3f, s * 0.56f), s * 0.05f, MgScenery.Hex(0x9A6A4A));
            if (z.Type == "maulwurf") P.Blob(p, head + new Vector2(0f, -s * 0.2f), s * 0.12f, s * 0.05f, MgScenery.Hex(0x8C6A4A));
            if (z.Hp < z.MaxHp && z.MaxHp > 0)
            {
                var w = s * 0.5f;
                var frac = Mathf.Clamp01(z.Hp / (float)z.MaxHp);
                P.Rect(p, head + new Vector2(-w * 0.5f, -s * 0.52f), head + new Vector2(w * 0.5f, -s * 0.47f), new Color(0.2f, 0.15f, 0.15f, 0.5f));
                P.Rect(p, head + new Vector2(-w * 0.5f, -s * 0.52f), head + new Vector2(-w * 0.5f + w * frac, -s * 0.47f), Berry);
            }
        }

        private void PaintBoss(Painter2D p, GvzBoss boss)
        {
            var c = Px(XToView(boss.X), LawnY + (boss.Lane + 0.5f) * LaneH);
            var s = S(LaneH * 1.25f);
            var shake = boss.Phase >= 3 ? Mathf.Sin(_anim * 30f) * s * 0.01f : 0f;
            c += new Vector2(shake, 0f);
            P.RoundRect(p, c + new Vector2(-s * 0.55f, -s * 0.42f), c + new Vector2(s * 0.6f, s * 0.3f), s * 0.06f, MgScenery.Hex(0x6E9C6A));
            P.RoundRect(p, c + new Vector2(-s * 0.85f, -s * 0.18f), c + new Vector2(-s * 0.5f, s * 0.3f), s * 0.05f, MgScenery.Hex(0xA9D6A0));
            P.Rect(p, c + new Vector2(-s * 0.8f, -s * 0.12f), c + new Vector2(-s * 0.58f, s * 0.04f), new Color(0.8f, 0.92f, 1f, 0.9f));
            foreach (var x in new[] { -0.6f, 0.0f, 0.42f })
            {
                P.Circle(p, c + new Vector2(s * x, s * 0.34f), s * 0.13f, Outline);
                P.Circle(p, c + new Vector2(s * x, s * 0.34f), s * 0.06f, MgScenery.Hex(0xA9B2BA));
            }
            // King Knurps on top with his crown
            var head = c + new Vector2(0f, -s * 0.58f);
            P.OutlinedBlob(p, head, s * 0.2f, s * 0.18f, s * 0.03f, ZombieSkin, Outline);
            P.Poly(p, new[] { head + new Vector2(-s * 0.16f, -s * 0.12f), head + new Vector2(-s * 0.12f, -s * 0.3f), head + new Vector2(-s * 0.04f, -s * 0.18f), head + new Vector2(0f, -s * 0.32f), head + new Vector2(s * 0.05f, -s * 0.18f), head + new Vector2(s * 0.13f, -s * 0.3f), head + new Vector2(s * 0.16f, -s * 0.12f) }, Gold);
            P.Circle(p, head + new Vector2(-s * 0.07f, 0f), s * 0.04f, Outline);
            P.Circle(p, head + new Vector2(s * 0.06f, 0f), s * 0.04f, Outline);
            if (boss.Phase >= 2) for (var i = 0; i < 3; i++) P.Circle(p, c + new Vector2(s * (0.5f + i * 0.1f), -s * (0.5f + i * 0.14f + (_anim * 0.4f + i * 0.3f) % 0.4f)), s * (0.06f + i * 0.02f), new Color(0.5f, 0.5f, 0.55f, 0.45f));
            var w = s * 1.1f;
            var frac = Mathf.Clamp01(boss.Hp / (float)Mathf.Max(1, boss.MaxHp));
            P.RoundRect(p, c + new Vector2(-w * 0.5f, -s * 0.98f), c + new Vector2(w * 0.5f, -s * 0.9f), s * 0.03f, new Color(0.2f, 0.15f, 0.15f, 0.6f));
            P.RoundRect(p, c + new Vector2(-w * 0.5f, -s * 0.98f), c + new Vector2(-w * 0.5f + w * frac, -s * 0.9f), s * 0.03f, Berry);
        }

        private void PaintProjectile(Painter2D p, GvzProjectile pr)
        {
            var y = LawnY + (pr.Lane + 0.5f) * LaneH - 6f;
            var x = XToView(pr.X);
            if (x > LawnX + LawnW + 10f) return;
            var c = Px(x, y);
            var s = S(14f);
            switch (pr.Kind)
            {
                case "frost":
                    P.Circle(p, c, s * 0.62f, Frost);
                    P.Star(p, c, s * 0.42f, Color.white, 0.4f);
                    break;
                case "star":
                    P.Star(p, c, s * 0.8f, Gold);
                    break;
                case "melon":
                    var arc = Mathf.Sin(((pr.X % 2000) / 2000f) * Mathf.PI) * S(18f);
                    P.OutlinedBlob(p, c - new Vector2(0f, arc), s * 0.75f, s * 0.65f, s * 0.12f, MgScenery.Hex(0x5FB55A), Outline);
                    break;
                default:
                    P.Poly(p, new[] { c + new Vector2(-s * 0.7f, -s * 0.32f), c + new Vector2(s * 0.8f, 0f), c + new Vector2(-s * 0.7f, s * 0.32f) }, Carrot);
                    P.Line(p, c + new Vector2(-s * 0.7f, 0f), c + new Vector2(-s * 1.05f, -s * 0.25f), s * 0.18f, Leaf);
                    break;
            }
        }

        private void PaintDrop(Painter2D p, GvzDrop d, int tick)
        {
            var c = CellCenter(d.Lane, d.Col) + new Vector2(0f, Mathf.Sin(_anim * 3f + d.Id) * S(3f));
            var left = d.Expires - tick;
            if (left < 40 && Mathf.Sin(_anim * 18f) < 0f) return;
            var s = S(17f);
            P.Circle(p, c + new Vector2(0f, s * 0.9f), s * 0.6f, new Color(0f, 0f, 0f, 0.12f));
            P.Poly(p, new[] { c + new Vector2(0f, -s * 1.05f), c + new Vector2(s * 0.62f, -s * 0.1f), c + new Vector2(-s * 0.62f, -s * 0.1f) }, Nutella);
            P.Circle(p, c + new Vector2(0f, s * 0.12f), s * 0.68f, Nutella);
            P.Circle(p, c + new Vector2(-s * 0.22f, -s * 0.12f), s * 0.18f, new Color(1f, 1f, 1f, 0.55f));
        }

        private void PaintRain(Painter2D p)
        {
            for (var i = 0; i < 46; i++)
            {
                var x = (i * 97.3f + _anim * 260f) % 1000f - 20f;
                var y = (i * 53.7f + _anim * 520f) % 470f;
                P.Line(p, Px(x, y), Px(x - 6f, y + 16f), S(1.6f), new Color(0.7f, 0.82f, 0.95f, 0.55f));
            }
        }

        private void PaintFog(Painter2D p, int cols)
        {
            var x0 = LawnX + (GvzLogic.Cols - cols) * CellW;
            P.Rect(p, Px(x0, LawnY - 6f), Px(LawnX + LawnW + 6f, LawnY + LawnH + 6f), new Color(0.93f, 0.95f, 0.97f, 0.86f));
            for (var i = 0; i < 9; i++)
                P.Blob(p, Px(x0 + (i % 3) * CellW * 1.1f + Mathf.Sin(_anim * 0.5f + i) * 8f, LawnY + 30f + i * 38f), S(70f), S(30f), new Color(1f, 1f, 1f, 0.55f));
        }

        /// <summary>Wave meter (top right of the lawn): how much of the level's spawn plan has arrived, flags per wave.</summary>
        private void PaintWaveMeter(Painter2D p, GvzRun run)
        {
            var total = Mathf.Max(1, run.SpawnPlan.Count);
            var frac = run.SpawnIdx / (float)total;
            var a = Px(LawnX + LawnW - 220f, LawnY - 22f);
            var b = Px(LawnX + LawnW - 8f, LawnY - 10f);
            P.RoundRect(p, a, b, S(6f), new Color(0.3f, 0.24f, 0.2f, 0.5f));
            var fill = new Vector2(Mathf.Lerp(b.x, a.x, frac), b.y);
            P.RoundRect(p, new Vector2(fill.x, a.y), b, S(6f), MgScenery.Hex(0x8BC34A));
            var last = run.SpawnPlan.Count > 0 ? run.SpawnPlan[run.SpawnPlan.Count - 1].Tick : 1;
            foreach (var wave in run.WavePlan)
            {
                var wx = Mathf.Lerp(b.x, a.x, Mathf.Clamp01(wave.Tick / (float)Mathf.Max(1, last)));
                P.Line(p, new Vector2(wx, a.y - S(10f)), new Vector2(wx, b.y), S(2f), Outline);
                P.Poly(p, new[] { new Vector2(wx, a.y - S(10f)), new Vector2(wx + S(10f), a.y - S(6f)), new Vector2(wx, a.y - S(2f)) }, wave.Huge ? Berry : MgScenery.Hex(0xFF8B8B));
            }
            P.OutlinedBlob(p, new Vector2(fill.x, (a.y + b.y) * 0.5f), S(8f), S(8f), S(1.5f), ZombieSkin, Outline);
        }
    }
}
