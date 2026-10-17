using System;
using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Home HUD (concept board 08). Every element is bound to real state: profile capsule (nickname,
    /// level, XP ring → bar), coins + leaves, mail badge, clock + weather, left tabs, Gooby speech bubble,
    /// current tasks card, gift card, bottom action bar, joystick, talk button. Gooby's bubble fades out after a few
    /// seconds (it comes back with his next line). On a small canvas (a phone at its larger UI scale) the HUD goes
    /// compact: the two tabs that repeat other buttons (Zuhause, Einstellungen) step aside, the tasks card folds to its
    /// head row (tap opens it), the parcel card leaves its reminder to the badge on Geschenke, and the thumb discs
    /// shrink a little — nothing overlaps the joystick or the talk disc.
    /// </summary>
    public sealed partial class HomeHudScreen : UiScreen
    {
        private readonly GameHost _host;

        public override string SoundScene => "home/" + (_host.World?.RoomId ?? "living");
        private Label _name, _level, _coins, _leaves, _clock, _date, _bubbleText, _tasksCount;
        private VisualElement _taskRows;
        private VisualElement _xpFill, _weatherIcon, _statsSheet;
        private VisualElement[] _statFills = new VisualElement[4];
        private Label[] _statValues = new Label[4];
        private float _clockAccum = 9f;
        private string _activeTab = "zuhause";
        private VisualElement _bubble, _tasksCard, _tasksChevron, _joy;
        private bool _compact, _layoutKnown, _tasksOpen = true;
        private float _bubbleLeft = BubbleHoldSec;

        /// <summary>Seconds a line of Gooby stays in the bubble before it fades.</summary>
        public const float BubbleHoldSec = 9f;
        /// <summary>
        /// Below this canvas size (UI units) the HUD goes compact. Phones get there by height (about 1125×562 at their
        /// 1.25 scale); a 4:3 tablet (about 1093×820) still holds the full HUD.
        /// </summary>
        public const float CompactHeight = 660f, CompactWidth = 1040f;
        private readonly System.Collections.Generic.Dictionary<string, Button> _tabs = new System.Collections.Generic.Dictionary<string, Button>();

        public HomeHudScreen(GameHost host)
        {
            _host = host;
            Id = "home-hud";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var store = _host.Store;
            root.pickingMode = PickingMode.Ignore;
            root.AddToClassList("gb-hud"); // board 08: the in-game chrome is dark glass over the room (USS "HUD board 08")

            // tap catcher (lowest layer): a tap on a door of the room walks Gooby through it
            var catcher = new VisualElement { pickingMode = PickingMode.Position, name = "world-taps" };
            Ux.Abs(catcher, 0, 0, 0, 0);
            // a press on the living-room ball grabs it; the release flicks it (everything else stays a tap)
            catcher.RegisterCallback<PointerDownEvent>(e =>
            {
                _ballGrab = _host.World != null && _host.World.PointerDownWorld(Input.mousePosition);
                if (_ballGrab) catcher.CapturePointer(e.pointerId);
            });
            catcher.RegisterCallback<PointerMoveEvent>(_ => { if (_ballGrab) _host.World?.PointerDragWorld(Input.mousePosition); });
            catcher.RegisterCallback<PointerUpEvent>(e =>
            {
                NoteActivity();
                if (_ballGrab)
                {
                    _ballGrab = false;
                    catcher.ReleasePointer(e.pointerId);
                    _host.World?.PointerUpWorld(Input.mousePosition);
                    return;
                }
                _host.World?.TapWorld(Input.mousePosition);
            });
            root.Add(catcher);

            // board 08: "Zzz" drifting up from Gooby's head while he sleeps (positioned over the world each frame)
            _zzz = Ux.Text("z z Z", "gb-zzz");
            _zzz.name = "zzz";
            _zzz.pickingMode = PickingMode.Ignore;
            _zzz.style.position = Position.Absolute;
            _zzz.style.display = DisplayStyle.None;
            root.Add(_zzz);

            // ── top-left: profile capsule ──────────────────────────────────
            var profile = Ux.El("gb-card", "gb-row", "gb-hud-profile");
            profile.name = "hud-profile-card";
            Ux.Abs(profile, 16, 12);
            profile.style.paddingTop = 8; profile.style.paddingBottom = 8; profile.style.paddingLeft = 8; profile.style.paddingRight = 18;
            profile.style.borderTopLeftRadius = 40; profile.style.borderBottomLeftRadius = 40;
            profile.style.borderTopRightRadius = 28; profile.style.borderBottomRightRadius = 28;
            profile.Add(Ux.Portrait(64));
            var pcol = Ux.Col();
            var nameRow = Ux.Row();
            _name = Ux.Text("Gooby", "gb-h2");
            nameRow.Add(_name);
            var edit = Ux.Icon("pencil");
            edit.style.width = 18; edit.style.height = 18; edit.style.marginLeft = 6;
            nameRow.Add(edit);
            pcol.Add(nameRow);
            var lvlRow = Ux.Row();
            _level = Ux.Text("Lvl. 1", "gb-sm", "gb-bold", "gb-hud-level");
            _level.style.marginRight = 8;
            lvlRow.Add(_level);
            var xpBar = Ux.Bar(0f, out _xpFill);
            xpBar.AddToClassList("gb-hud-xp");
            xpBar.style.width = 90; xpBar.style.height = 10;
            lvlRow.Add(xpBar);
            pcol.Add(lvlRow);
            profile.Add(pcol);
            profile.RegisterCallback<ClickEvent>(_ => ToggleStatsSheet());
            // a pink dot while the album holds stickers nobody has looked at yet (the album lives behind the profile)
            _albumDot = Ux.El("gb-hud-album-dot");
            _albumDot.name = "hud-album-dot";
            _albumDot.pickingMode = PickingMode.Ignore;
            profile.Add(_albumDot);
            root.Add(profile);

            // ── stats sheet (tap profile): the four needs as bars ───────────
            _statsSheet = Ux.El("gb-card");
            Ux.Abs(_statsSheet, 16, 96);
            _statsSheet.style.width = 260;
            // it drops over the nav column: solid cream, the buttons must not show through
            _statsSheet.style.backgroundColor = new Color(1f, 0.992f, 0.973f, 1f);
            _statsSheet.style.display = DisplayStyle.None;
            var statKeys = new[] { ("hunger", "hud.stat_hunger", "gb-bar-fill--pfirsich"), ("energy", "hud.stat_energie", "gb-bar-fill--sonne"), ("hygiene", "hud.stat_hygiene", "gb-bar-fill--blau"), ("fun", "hud.stat_spass", "") };
            for (var i = 0; i < statKeys.Length; i++)
            {
                var row = Ux.Row();
                row.style.marginBottom = 6;
                var lab = Ux.Text(t.T(statKeys[i].Item2), "gb-sm", "gb-bold");
                lab.style.width = 74;
                row.Add(lab);
                var bar = statKeys[i].Item3.Length > 0 ? Ux.Bar(0f, out _statFills[i], statKeys[i].Item3) : Ux.Bar(0f, out _statFills[i]);
                bar.style.flexGrow = 1;
                row.Add(bar);
                _statValues[i] = Ux.Text("0", "gb-sm", "gb-mild");
                _statValues[i].style.width = 34;
                _statValues[i].style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(_statValues[i]);
                // a running buff on this need (hud_status_sheet.gd): "+10", the holiday's rest with a sun
                _statBuffs[i] = Ux.Text("", "gb-buff-chip");
                _statBuffs[i].name = "hud-buff-" + statKeys[i].Item1;
                _statBuffs[i].style.display = DisplayStyle.None;
                row.Add(_statBuffs[i]);
                _statsSheet.Add(row);
            }
            // the collections live behind the profile: the sticker album and the achievements
            var collect = Ux.Col();
            collect.style.marginTop = 8;
            var profilePage = Ux.IconTextBtn("user", t.T("profil.titel"), () => { ToggleStatsSheet(); _host.Ui.Push(new ProfileScreen(_host)); }, "gb-btn-secondary");
            profilePage.name = "hud-profile";
            profilePage.style.marginBottom = 8;
            collect.Add(profilePage);
            var album = Ux.IconTextBtn("star", t.T("album.titel"), () => { ToggleStatsSheet(); _host.Ui.Push(new AlbumScreen(_host)); }, "gb-btn-secondary");
            album.name = "hud-album";
            album.style.marginBottom = 8;
            collect.Add(album);
            var trophies = Ux.IconTextBtn("trophy", t.T("achievements.titel"), () => { ToggleStatsSheet(); _host.Ui.Push(new AchievementsScreen(_host)); }, "gb-btn-secondary");
            trophies.name = "hud-achievements";
            trophies.style.marginBottom = 8;
            collect.Add(trophies);
            var rename = Ux.IconTextBtn("pencil", t.T("nl.onboarding.rename_button"), () => { ToggleStatsSheet(); _host.Ui.Push(new OnboardingScreen(_host, renameOnly: true)); }, "gb-btn-secondary");
            rename.name = "hud-rename";
            collect.Add(rename);
            _statsSheet.Add(collect);
            root.Add(_statsSheet);

            // ── top-right cluster: currencies, mail, settings, clock ───────
            var topRight = Ux.Row();
            Ux.Abs(topRight, null, 12, 16);
            var coinPill = Ux.Pill("coin", "0", out _coins, () => _host.Ui.Push(new ShopScreen(_host)));
            _coinPill = coinPill;
            coinPill.style.marginRight = 10;
            topRight.Add(coinPill);
            var leafPill = Ux.Pill("leaf", "0", out _leaves, () => _host.Ui.Push(new LeafShopScreen(_host)));
            leafPill.name = "leaf-pill";
            leafPill.style.marginRight = 10;
            _leafPill = leafPill;
            topRight.Add(leafPill);
            foreach (var pill in new[] { coinPill, leafPill }) HudPlus(pill);
            // board 08: white glyphs on the dark glass discs — the phone, the post and the gear; the phone holds the apps
            // (GOOBERANDO, radio, camera, gallery, taxi …) so the top row stays as calm as on the board
            var phone = Ux.IconBtn("phone_white", () => _host.Ui.Push(new PhoneScreen(_host)), "gb-btn-icon-sm");
            phone.name = "hud-phone";
            phone.style.marginRight = 8;
            _phoneBadge = Ux.Badge("");
            _phoneBadge.style.display = DisplayStyle.None;
            phone.Add(_phoneBadge);
            topRight.Add(phone);
            var mail = Ux.IconBtn("mail_white", () => _host.Ui.Push(new MailboxScreen(_host)), "gb-btn-icon-sm");
            mail.name = "hud-mail";
            _mailBadge = Ux.Badge("");
            mail.Add(_mailBadge);
            mail.style.marginRight = 8;
            topRight.Add(mail);
            var gear = Ux.IconBtn("settings_white", () => _host.Ui.Push(new SettingsScreen(_host)), "gb-btn-icon-sm");
            gear.style.marginRight = 10;
            topRight.Add(gear);
            var clockCard = Ux.El("gb-card", "gb-row", "gb-hud-clock");
            clockCard.style.paddingTop = 6; clockCard.style.paddingBottom = 6;
            var clockCol = Ux.Col();
            _clock = Ux.Text("00:00", "gb-h3");
            _date = Ux.Text("", "gb-sm", "gb-hud-date");
            clockCol.Add(_clock);
            clockCol.Add(_date);
            clockCard.Add(clockCol);
            _weatherIcon = Ux.Icon("sun", "gb-icon-lg");
            _weatherIcon.style.marginLeft = 10;
            clockCard.Add(_weatherIcon);
            topRight.Add(clockCard);
            root.Add(topRight);

            // ── left: tab column ───────────────────────────────────────────
            var tabs = Ux.Col();
            Ux.Abs(tabs, 16, 112);
            _tabCol = tabs;
            AddTab(tabs, "zuhause", "home", t.T("nl.tabs.zuhause"), () => { });
            AddTab(tabs, "inventar", "inventory", t.T("nl.tabs.inventar"), () => _host.Ui.Push(new InventoryScreen(_host)));
            AddTab(tabs, "shop", "shop", t.T("nl.tabs.shop"), () => _host.Ui.Push(new ShopScreen(_host)), badge: "!");
            AddTab(tabs, "karte", "map", t.T("nl.tabs.karte"), () => DriveOff(_host, Say));
            AddTab(tabs, "freunde", "friends", t.T("nl.tabs.freunde"), () => _host.Ui.Push(new FriendsScreen(_host)), badge: "");
            _friendsBadge = _tabs["freunde"].Q<Label>(className: "gb-badge");
            if (_friendsBadge != null) _friendsBadge.style.display = DisplayStyle.None;
            AddTab(tabs, "einstellungen", "settings", t.T("nl.tabs.einstellungen"), () => _host.Ui.Push(new SettingsScreen(_host)));
            root.Add(tabs);

            // ── centre-top: speech bubble ──────────────────────────────────
            var bubble = Ux.El("gb-bubble");
            Ux.Abs(bubble, null, 112);
            bubble.style.left = Length.Percent(50);
            bubble.style.translate = new Translate(Length.Percent(-50), 0);
            bubble.Add(Ux.Portrait(52));
            _bubbleText = Ux.Text(t.T("nl.hud.paket_atmet"), "gb-lg", "gb-semibold");
            _bubbleText.name = "bubble-text";
            _bubbleText.style.whiteSpace = WhiteSpace.Normal;
            _bubbleText.style.flexShrink = 1;
            bubble.Add(_bubbleText);
            bubble.Add(Ux.Text(t.T("nl.marke.name").ToUpperInvariant(), "gb-bubble-name"));
            var tail = Ux.Icon("chevron_down");
            tail.style.width = 16; tail.style.height = 16; tail.style.marginLeft = 12;
            tail.style.unityBackgroundImageTintColor = new Color(1f, 0.82f, 0.4f);
            bubble.Add(tail);
            bubble.AddToClassList("gb-bubble--fading");
            bubble.RegisterCallback<ClickEvent>(_ => { RunCoach(); HideBubble(); });
            _bubble = bubble;
            root.Add(bubble);

            // ── right: tasks card (live daily quests) + gift card ──────────
            var tasks = Ux.El("gb-card");
            Ux.Abs(tasks, null, 176, 16);
            tasks.style.width = 300;
            var th = Ux.Row();
            th.Add(Ux.Icon("leaf"));
            var tTitle = Ux.Text(t.T("nl.hud.aufgaben"), "gb-lg", "gb-bold");
            tTitle.style.marginLeft = 8; tTitle.style.flexGrow = 1;
            th.Add(tTitle);
            _tasksCount = Ux.Text("0/3", "gb-lg", "gb-bold");
            th.Add(_tasksCount);
            _tasksChevron = Ux.Icon("chevron_down");
            _tasksChevron.style.width = 18; _tasksChevron.style.height = 18; _tasksChevron.style.marginLeft = 8;
            _tasksChevron.style.display = DisplayStyle.None;
            th.Add(_tasksChevron);
            // the whole board (descriptions, rewards, the all-three bonus, the daily reroll) one tap away
            var openQuests = Ux.IconBtn("chevron", () => _host.Ui.Push(new QuestSheetScreen(_host)), "gb-tasks-open");
            openQuests.name = "tasks-open";
            openQuests.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            th.Add(openQuests);
            th.RegisterCallback<ClickEvent>(_ =>
            {
                if (_compact) { _tasksOpen = !_tasksOpen; ApplyTasksOpen(); }
                else _host.Ui.Push(new QuestSheetScreen(_host));
            });
            tasks.Add(th);
            _taskRows = Ux.Col();
            tasks.Add(_taskRows);
            _tasksCard = tasks;
            root.Add(tasks);

            // the daily parcel (DailyBonus): visible until today's package has been opened
            _giftCard = Ux.El("gb-card", "gb-card-rosa", "gb-row");
            _giftCard.name = "gift-card";
            Ux.Abs(_giftCard, null, 340, 16);
            _giftCard.style.width = 300;
            _giftCard.Add(Ux.IconTile("gift", new Color(1f, 0.85f, 0.85f)));
            var gcol = Ux.Col("gb-grow");
            gcol.style.marginLeft = 10;
            gcol.style.flexShrink = 1; gcol.style.minWidth = 0;
            gcol.Add(Ux.Text(t.T("nl.hud.geschenk_titel"), "gb-lg", "gb-bold"));
            var gtext = Ux.Text(t.T("nl.hud.geschenk_text"), "gb-sm", "gb-mild");
            gtext.style.whiteSpace = WhiteSpace.Normal;
            gcol.Add(gtext);
            _giftCard.Add(gcol);
            var gch = Ux.Icon("chevron");
            gch.style.width = 18; gch.style.height = 18;
            _giftCard.Add(gch);
            _giftCard.RegisterCallback<ClickEvent>(_ => OpenPackage());
            root.Add(_giftCard);

            // a random event (RandomEvents): Gooby needs help — the card stays until it is solved or times out
            _eventCard = Ux.El("gb-card", "gb-card-rosa", "gb-row", "gb-event-card");
            _eventCard.name = "event-card";
            Ux.Abs(_eventCard, null, 340, 16);
            _eventCard.style.width = 300;
            _eventCard.Add(Ux.IconTile("heart", new Color(1f, 0.82f, 0.86f)));
            var ecol = Ux.Col("gb-grow");
            ecol.style.marginLeft = 10;
            ecol.style.flexShrink = 1; ecol.style.minWidth = 0;
            ecol.Add(Ux.Text(t.T("nl.ereignis.titel"), "gb-lg", "gb-bold"));
            _eventText = Ux.Text("", "gb-sm", "gb-mild");
            _eventText.style.whiteSpace = WhiteSpace.Normal;
            ecol.Add(_eventText);
            _eventCard.Add(ecol);
            var ech = Ux.Icon("chevron");
            ech.style.width = 18; ech.style.height = 18;
            _eventCard.Add(ech);
            _eventCard.RegisterCallback<ClickEvent>(_ => OpenEvent());
            _eventCard.style.display = DisplayStyle.None;
            root.Add(_eventCard);
            BuildVacationCard(root);

            // SAVE-TRANSFER way A: an older save was found on this device → offer the move (board 08 card style)
            if (_host.LegacyCandidate != null)
            {
                var legacy = Ux.El("gb-card", "gb-card-gruen", "gb-row");
                Ux.Abs(legacy, null, 428, 16);
                legacy.style.width = 300;
                legacy.Add(Ux.IconTile("books", new Color(0.87f, 0.96f, 0.9f)));
                var lcol = Ux.Col("gb-grow");
                lcol.style.marginLeft = 10;
                lcol.style.flexShrink = 1; lcol.style.minWidth = 0;
                lcol.Add(Ux.Text(t.T("nl.transfer.hud_karte"), "gb-lg", "gb-bold"));
                // one line does not fit the 300-px card beside the chevron: the subtitle wraps
                var ltext = Ux.Text(t.T("nl.transfer.hud_karte_text"), "gb-sm", "gb-mild");
                ltext.style.whiteSpace = WhiteSpace.Normal;
                lcol.Add(ltext);
                legacy.Add(lcol);
                var lch = Ux.Icon("chevron");
                lch.style.width = 18; lch.style.height = 18;
                legacy.Add(lch);
                legacy.RegisterCallback<ClickEvent>(_ => _host.Ui.Push(new TransferScreen(_host, _host.LegacyCandidate)));
                root.Add(legacy);
            }

            // ── bottom-left: joystick (visual; drives Gooby in the world pass) ─
            var joy = new Joystick();
            Ux.Abs(joy, 40, null, null, 28);
            joy.Changed += v =>
            {
                _host.World?.SetMove(v);
                if (v.sqrMagnitude > 0.01f) NoteActivity();
            };
            joy.style.display = _host.Settings.ShowControls ? DisplayStyle.Flex : DisplayStyle.None;
            joy.style.transformOrigin = new TransformOrigin(0, Length.Percent(100));
            _joy = joy;
            root.Add(joy);

            // ── bottom-centre: action bar ──────────────────────────────────
            var actions = Ux.Row();
            Ux.Abs(actions, null, null, null, 20);
            actions.style.left = Length.Percent(50);
            actions.style.translate = new Translate(Length.Percent(-50), 0);
            actions.style.alignItems = Align.FlexEnd;
            actions.Add(Action("toolbox", t.T("nl.hud.aktion_dekorieren"), () => _host.Ui.Push(new BuildModeScreen(_host, _host.World?.RoomId ?? "living"))));
            actions.Add(Action("shirt", t.T("nl.hud.aktion_outfits"), () => _host.Ui.Push(new OutfitScreen(_host))));
            _interact = Action("hand", t.T("nl.hud.aktion_interagieren"), () => Interact(), main: true);
            actions.Add(_interact);
            var gifts = Action("gift", t.T("nl.hud.aktion_geschenke"), OpenPackage);
            _giftBadge = Ux.Badge("!");
            gifts.Add(_giftBadge);
            actions.Add(gifts);
            // the arcade is the game's core loop: the fifth action, in reach of the thumb (Freunde stays in the tab column)
            var games = Action("gamepad", t.T("nl.hud.aktion_spiele"), () => _host.Ui.Push(new ArcadeScreen(_host)));
            games.name = "hud-spiele";
            games.AddToClassList("gb-action--spiele");
            actions.Add(games);
            root.Add(actions);

            // ── bottom-right: talk ─────────────────────────────────────────
            // board 01: the talk disc is the joystick's twin — the same 150 px, Gooby's face taking most of it,
            // the chat bubble tucked at its side, the label underneath
            var talk = Ux.El("gb-action");
            Ux.Abs(talk, null, null, 28, 28);
            talk.AddToClassList("gb-hud-talk");
            var tAvatar = Ux.Portrait(66);
            tAvatar.style.marginRight = 0;
            var talkRow = Ux.Row();
            talkRow.style.alignItems = Align.Center;
            talkRow.Add(tAvatar);
            var chat = Ux.Icon("chat");
            chat.style.marginLeft = 2;
            chat.style.width = 30; chat.style.height = 30;
            talkRow.Add(chat);
            talk.Add(talkRow);
            var talkLabel = Ux.Text(t.T("nl.hud.reden"), "gb-hud-talk-label");
            talk.Add(talkLabel);
            var up = Ux.El("gb-hud-talk-up");
            up.Add(Ux.Icon("chevron_up"));
            talk.Add(up);
            talk.RegisterCallback<ClickEvent>(_ => Talk());
            talk.style.transformOrigin = new TransformOrigin(Length.Percent(100), Length.Percent(100));
            _talk = talk;
            root.Add(talk);
            BuildSpotlight(root);

            BuildLife(root);

            // bindings
            store.CoinsChanged += OnCoins;
            store.LevelChanged += OnLevel;
            store.StatsChanged += OnStats;
            store.GoobyEvents += OnGoobyEvents;
            store.SliceChanged += OnSliceChanged;
            _host.Settings.Changed += OnSettingChanged;
            if (_host.Online != null) { _host.Online.Changed -= RefreshOnline; _host.Online.Changed += RefreshOnline; }
            HookWorld();
            Refresh();
            _layoutKnown = false;
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplyLayout());
            QueueSoulMoment(1600);
            root.schedule.Execute(TryRollEvent).StartingIn(2600);
            root.schedule.Execute(TryHomecoming).StartingIn(400);
        }

        /// <summary>Wide or compact HUD for the canvas the screen has now (re-runs when the screen turns or the UI scale changes).</summary>
        private void ApplyLayout()
        {
            if (Root == null) return;
            var w = Root.resolvedStyle.width;
            var h = Root.resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 1f || h <= 1f) return;
            var compact = h < CompactHeight || w < CompactWidth;
            if (_layoutKnown && compact == _compact) return;
            _layoutKnown = true;
            _compact = compact;
            foreach (var id in new[] { "zuhause", "einstellungen" })
                if (_tabs.TryGetValue(id, out var tab)) tab.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
            if (_tasksCard != null)
            {
                _tasksCard.style.top = compact ? 100 : 176;
                _tasksCard.style.width = compact ? 260 : 300;
            }
            _tasksOpen = !compact;
            ApplyTasksOpen();
            RefreshGift();
            var thumb = compact ? new Scale(new Vector3(0.82f, 0.82f, 1f)) : new Scale(Vector3.one);
            if (_joy != null) _joy.style.scale = thumb;
            if (_talk != null) _talk.style.scale = thumb;
            _buttonsCard?.EnableInClassList("gb-knoepfe--compact", compact);
        }

        /// <summary>The tasks card shows its rows when open; compact, it folds to the head row with a chevron.</summary>
        private void ApplyTasksOpen()
        {
            if (_taskRows == null) return;
            _taskRows.style.display = _tasksOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_tasksChevron != null)
            {
                _tasksChevron.style.display = _compact ? DisplayStyle.Flex : DisplayStyle.None;
                _tasksChevron.style.rotate = new Rotate(_tasksOpen ? 180f : 0f);
            }
        }

        private void ShowBubble()
        {
            if (_bubble == null) return;
            _bubble.style.opacity = 1f;
            _bubble.pickingMode = PickingMode.Position;
            _bubbleLeft = BubbleHoldSec;
        }

        private void HideBubble()
        {
            if (_bubble == null) return;
            _bubble.style.opacity = 0f;
            _bubble.pickingMode = PickingMode.Ignore;
            _bubbleLeft = -1f;
        }

        public override void OnHidden()
        {
            EndSpotlight();
            var store = _host.Store;
            // covered by a sheet, the coin and level pills stay live behind the scrim (building on the lawn, shop);
            // only a HUD that leaves the stack lets go of them
            if (!_host.Ui.IsMounted(this))
            {
                store.CoinsChanged -= OnCoins;
                store.LevelChanged -= OnLevel;
            }
            store.StatsChanged -= OnStats;
            store.GoobyEvents -= OnGoobyEvents;
            store.SliceChanged -= OnSliceChanged;
            _host.Settings.Changed -= OnSettingChanged;
            if (_host.Online != null) _host.Online.Changed -= RefreshOnline;
            if (_host.World != null)
            {
                _host.World.DoorReached -= OnDoorReached; _host.World.RoomChanged -= OnRoomChanged; _host.World.BedTapped -= OnBedTapped;
                _host.World.LeafPicked -= OnLeafPicked;
                _host.World.SpotPicked -= OnSpotPicked;
                UnhookInteract();
            }
            UnhookLife();
        }

        private void HookWorld()
        {
            if (_host.World == null) return;
            _host.World.DoorReached -= OnDoorReached; _host.World.DoorReached += OnDoorReached;
            _host.World.RoomChanged -= OnRoomChanged; _host.World.RoomChanged += OnRoomChanged;
            _host.World.BedTapped -= OnBedTapped; _host.World.BedTapped += OnBedTapped;
            _host.World.LeafPicked -= OnLeafPicked; _host.World.LeafPicked += OnLeafPicked;
            _host.World.SpotPicked -= OnSpotPicked; _host.World.SpotPicked += OnSpotPicked;
            HookInteract();
            HookLife();
        }

        private void OnBedTapped(Gooby.Core.Home.Cell bed)
        {
            // garden_host.gd select_cell: a tap on the garage also turns its roller door
            if (Gooby.Core.Garden.GardenState.Grid(_host.Store).StructureAt(bed)?.Kind == Gooby.Core.Garden.GarageLogic.Kind) _host.World?.ToggleGarageDoor();
            _host.Ui.Push(new BedSheetScreen(_host, bed, Say));
        }

        /// <summary>
        /// Into town: from the lawn with a garage the car leaves through its door first and the city fades in;
        /// everywhere else the drive starts at once.
        /// </summary>
        public static void DriveOff(GameHost host, System.Action<string> say = null)
        {
            if (host.World == null || !host.World.GarageInView || host.World.ReducedMotion) { host.Ui.Push(new CityDriveScreen(host)); return; }
            if (host.World.GarageLeaving) return;
            say?.Invoke(host.Strings.T("nl.garage.abfahrt"));
            host.World.LeaveByGarage(() => host.Ui.FadeThrough(() =>
            {
                host.Ui.PopToRoot();
                host.Ui.Push(new CityDriveScreen(host));
            }));
        }

        private void OnRoomChanged(string roomId)
        {
            var def = Gooby.Core.Home.RoomDefs.Get(roomId);
            Toast(_host.Strings.T(def?.NameKey ?? "home.raum." + roomId));
            QueueSoulMoment(1200);
        }

        private void OnSliceChanged(string id, Newtonsoft.Json.Linq.JToken _) 
        {
            if (id == "quests" || id == "achievements" || id == "minigames") RefreshQuests();
            if (id == "daily") RefreshGift();
            if (id == "gooby") OnStats(J.Dict(J.Dict(_host.Store.State["gooby"])["stats"])); // health changed (medicine, vet) → mood/sick look
            if (id == "vacation") RefreshAway();
            if (id == Gooby.Core.Garden.GardenLeaves.SliceId) RefreshLeaves();
            if (id == "stickers" || id == "collections") RefreshAlbumDot();
            if (id == RandomEvents.SliceId) RefreshEvent();
            if (id == GoobyBuffs.SliceId && _statsSheet != null && _statsSheet.style.display == DisplayStyle.Flex) RefreshBuffChips();
        }

        private VisualElement _leafPill, _albumDot;

        private void RefreshAlbumDot()
        {
            if (_albumDot == null) return;
            var state = _host.Store.State;
            var seen = J.Path(state, "stickers", "seen") as JObject;
            var unseen = false;
            foreach (var d in Gooby.Core.Meta.StickerCatalog.All())
                if (Gooby.Core.Meta.StickerRules.IsUnlocked(state, d.Id) && (seen == null || !seen.ContainsKey(d.Id))) { unseen = true; break; }
            // a complete collection whose reward still waits counts as news in the album too
            var c = Gooby.Core.Meta.CollectionsLogic.NormalizeSlice(state["collections"]);
            foreach (var def in Gooby.Core.Meta.CollectionsLogic.Sets)
                if (!unseen && Gooby.Core.Meta.CollectionsLogic.IsSetComplete(c, def.Id) && !Gooby.Core.Meta.CollectionsLogic.IsClaimed(c, def.Id)) unseen = true;
            _albumDot.style.display = unseen ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private long _leafCount = -1;

        /// <summary>The leaf pill follows the "leaves" slice and gives a little hop when it grows.</summary>
        private void RefreshLeaves()
        {
            var count = Gooby.Core.Garden.GardenLeaves.Count(_host.Store);
            _leaves.text = Ux.FormatNumber(count);
            if (_leafCount >= 0 && count > _leafCount && _leafPill != null && !_host.Settings.ReducedMotion)
            {
                _leafPill.style.scale = new Scale(new Vector3(1.12f, 1.12f, 1f));
                _leafPill.schedule.Execute(() => _leafPill.style.scale = new Scale(Vector3.one)).StartingIn(160);
            }
            _leafCount = count;
        }

        private void OnLeafPicked(int gained, bool golden) =>
            Toast(golden ? _host.Strings.T("nl.blaetter.golden", ("n", gained)) : _host.Strings.T("nl.blaetter.plus", ("n", gained)));

        private void OnSpotPicked(string material)
        {
            var name = Gooby.Core.Home.CraftCatalog.Material(material)?.Name(_host.Settings.Locale) ?? material;
            Toast(_host.Strings.T("nl.werkstatt.gesammelt", ("name", name)), "mat_" + material);
        }

        private VisualElement _interact, _talk;
        private bool _away;

        /// <summary>
        /// Port of gooby_spawn.gd / room_base.gd: booked onto a flight, Gooby is not at home — the bubble carries the
        /// holiday note with the sleeps until he is back (or that he waits at the airport), and the two actions that
        /// need him in the room (Interagieren, Reden) wait too. The world hides his avatar (SimpleWorld.RefreshAway).
        /// </summary>
        private void RefreshAway()
        {
            var t = _host.Strings;
            var wasAway = _away;
            _away = Travel.IstWeg(_host.Store);
            if (_away)
            {
                var rest = Travel.RestTage(_host.Store, _host.Store.Clock.NowMs());
                Say(t.T("travel.weg.titel") + "  " + (rest > 0 ? t.T("travel.weg.rest", ("tage", rest)) : t.T("travel.abholen.titel")));
            }
            else if (wasAway) Say(t.T("travel.cutscene.karte_rueckkehr"));
            foreach (var a in new[] { _interact, _talk })
                if (a != null) a.style.opacity = _away ? 0.45f : 1f;
            RefreshVacationCard();
            if (!_away) TryHomecoming();
        }

        /// <summary>True (and a toast) when an action needs Gooby in the room while he is away.</summary>
        private bool AwayBlocks()
        {
            if (!_away) return false;
            Toast(_host.Strings.T("travel.weg.titel"));
            return true;
        }

        private VisualElement _giftCard, _giftBadge;

        /// <summary>The parcel card and the "!" on Geschenke show only while today's daily bonus is unopened.</summary>
        private void RefreshGift()
        {
            var claimable = PackageScreen.Claimable(_host);
            if (_giftCard != null) _giftCard.style.display = claimable && !_compact ? DisplayStyle.Flex : DisplayStyle.None;
            if (_giftBadge != null) _giftBadge.style.display = claimable ? DisplayStyle.Flex : DisplayStyle.None;
            LayoutRightCards();
        }

        private void OpenPackage() => _host.Ui.Push(new PackageScreen(_host, Say));

        public override void OnShown()
        {
            SetTab("zuhause"); // back on the home screen → the Zuhause tab is the active one again
            HookWorld();
            var store = _host.Store;
            store.CoinsChanged -= OnCoins; store.CoinsChanged += OnCoins;
            store.LevelChanged -= OnLevel; store.LevelChanged += OnLevel;
            store.StatsChanged -= OnStats; store.StatsChanged += OnStats;
            store.GoobyEvents -= OnGoobyEvents; store.GoobyEvents += OnGoobyEvents;
            store.SliceChanged -= OnSliceChanged; store.SliceChanged += OnSliceChanged;
            _host.Settings.Changed -= OnSettingChanged; _host.Settings.Changed += OnSettingChanged;
            if (_host.Online != null) { _host.Online.Changed -= RefreshOnline; _host.Online.Changed += RefreshOnline; }
            Refresh();
            QueueSoulMoment(900);
            Root?.schedule.Execute(TryRollEvent).StartingIn(1400);
            Root?.schedule.Execute(CheckFreshItems).StartingIn(2200);
            Root?.schedule.Execute(TryDelivery).StartingIn(800);
            TryHomecoming();
            if (_host.World != null && _host.World.GarlandSpanned)
            {
                // girlanden_bau.gd: a freshly spanned garland gets a word from Gooby once he is back on screen
                _host.World.GarlandSpanned = false;
                Root?.schedule.Execute(() => Kommentar("w13.girlande")).StartingIn(1200);
            }
        }

        private void OnSettingChanged(string key)
        {
            if (key == "locale")
            {
                // rebuild with the new language
                Root.Clear();
                Build(Root);
            }
        }

        private void Refresh()
        {
            var s = _host.Store.State;
            _name.text = J.Str(J.Dict(s["meta"])["goobyNickname"], "Gooby");
            OnCoins(J.Int(J.Dict(s["economy"])["coins"]));
            OnLevel(J.Int(J.Dict(s["progression"])["level"], 1), _host.Store.XpRatio());
            OnStats(J.Dict(J.Dict(s["gooby"])["stats"]));
            RefreshLeaves();
            RefreshAlbumDot();
            RefreshQuests();
            RefreshGift();
            UpdateClock(force: true);
            RefreshAway();
            RefreshOnline();
        }

        private VisualElement _coinPill;
        private int _pillCoins = -1;

        /// <summary>
        /// Spending shows at once; earned coins fly into the pill and count it up (reward_flug.gd). While an opaque screen
        /// hides the HUD the pill keeps its old number, so coming back from a round, the parcel or a shift flies the win in.
        /// </summary>
        private void OnCoins(int coins)
        {
            if (_pillCoins < 0 || coins < _pillCoins) { ShowCoins(coins); return; }
            if (coins == _pillCoins || Root == null || Root.style.display == DisplayStyle.None || Root.panel == null) return;
            var from = _pillCoins;
            _pillCoins = coins;
            // a beat later: the screen that booked the coins has just closed and the HUD is still settling
            Root.schedule.Execute(() => FlyCoins(from, coins)).StartingIn(280);
        }

        private void ShowCoins(int coins)
        {
            _pillCoins = coins;
            _coins.text = Ux.FormatNumber(coins);
        }

        private void FlyCoins(int from, int to)
        {
            if (Root == null || Root.panel == null || _coinPill == null) { ShowCoins(to); return; }
            var delta = to - from;
            var n = Mathf.Clamp(2 + delta / 12, 3, 10);
            var screen = Root.worldBound;
            Sfx.PlayUi("soft_coins");
            // icons land in any order (each flies its own time): the pill counts landings, not flight numbers
            var landed = 0;
            _host.Ui.FlyIcons(new Vector2(screen.center.x, screen.center.y + screen.height * 0.08f), _coinPill, "coin", n, _ =>
            {
                landed++;
                if (_pillCoins != to) return;
                _coins.text = Ux.FormatNumber(landed >= n ? to : from + Mathf.RoundToInt(delta * (float)landed / n));
                Sfx.Play("game_coin", Mathf.Min(landed, 9), 0.32f);
            });
        }

        private Label _mailBadge, _friendsBadge, _phoneBadge;

        /// <summary>Unread letters on the envelope, open friend requests on the Freunde action (hidden at zero).</summary>
        private void RefreshOnline()
        {
            if (_mailBadge == null || _host.Online == null) return;
            var c = _host.Online.Client;
            var unread = c.MailUnread;
            _mailBadge.text = unread > 9 ? "9+" : unread.ToString();
            _mailBadge.style.display = unread > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var req = c.Requests.Count;
            if (_friendsBadge != null)
            {
                _friendsBadge.text = req.ToString();
                _friendsBadge.style.display = req > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_phoneBadge != null)
            {
                var apps = unread + req;
                _phoneBadge.text = apps > 9 ? "9+" : apps.ToString();
                _phoneBadge.style.display = apps > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnLevel(int level, double ratio)
        {
            _level.text = _host.Strings.T("nl.hud.level", ("level", level));
            Ux.SetBar(_xpFill, ratio);
        }

        private void OnStats(JObject stats)
        {
            var keys = Stats.Keys;
            for (var i = 0; i < keys.Length && i < _statFills.Length; i++)
            {
                var v = J.Num(stats[keys[i]]);
                Ux.SetBar(_statFills[i], v / 100.0);
                _statValues[i].text = Mathf.RoundToInt((float)v).ToString();
            }
            var state = _host.Store.State;
            _sleeping = Sleep.IsSleeping(Sleep.FlatOf(state));
            var sick = J.Str(J.Path(state, "gooby", "health", "state")) == "sick";
            // asleep, waking up or napping in the crate the world sets the pose; awake, the lasting mood sets his face
            if (_sleeping || _sleepPose || (_host.World != null && _host.World.CrateNapping)) _host.World?.SetMood(_moodWert, _sleeping, sick);
            else ApplyRestingFace();
            _sleepPose = _sleeping;
            CheckNeglect(stats);
        }

        private bool _sleepPose;

        private Label _zzz;
        private bool _sleeping;
        private float _zzzPhase;

        /// <summary>Keeps the "Zzz" over Gooby's head (world → panel space), bobbing and breathing in opacity.</summary>
        private void TickZzz(float dt)
        {
            var world = _host.World;
            var show = (_sleeping || (world != null && world.CrateNapping)) && !_away && world?.Gooby != null && world.Camera != null && _host.Ui.Current == this;
            if (!show)
            {
                if (_zzz.style.display != DisplayStyle.None) _zzz.style.display = DisplayStyle.None;
                return;
            }
            _zzzPhase += dt;
            var bob = Mathf.Sin(_zzzPhase * 1.6f) * 0.08f;
            Vector3 head;
            var bone = world.Avatar != null ? world.Avatar.Bone("head") : null;
            if (bone != null) head = bone.position + new Vector3(0.04f, 0.34f + bob, 0.06f);
            else
            {
                var lift = world.CrateNapping ? 0.82f : 1.25f;
                head = world.Gooby.position + new Vector3(0.04f, lift + bob, 0.08f);
            }
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(_zzz.panel, head, world.Camera);
            _zzz.style.display = DisplayStyle.Flex;
            _zzz.style.left = p.x;
            _zzz.style.top = p.y - 40f;
            _zzz.style.opacity = 0.65f + 0.35f * Mathf.Sin(_zzzPhase * 2.4f);
        }

        private void OnGoobyEvents(System.Collections.Generic.List<string> events)
        {
            foreach (var e in events)
            {
                if (e == "wokeUp")
                {
                    Gooby.Core.Home.BadState.MarkWokeUp(_host.Store);
                    Say(_host.Strings.T("home.suche.hier") + " " + _host.Strings.T("bad.zahnputz.warte"));
                }
                else if (e.StartsWith("statLow:")) Say(_host.Strings.T("home.suche.akt.absicht_" + StatToAbsicht(e.Substring(8))));
                // health turns (pflege_runner.gd _on_gooby_events): a line, a face, and the look follows at once
                else if (e == "tummyWarning") Say(_host.Strings.T("health.warnung_bauch"));
                else if (e == "becameQueasy" || e == "becameSick" || e == "recovered")
                {
                    Say(_host.Strings.T(e == "becameQueasy" ? "health.kraenklich" : e == "becameSick" ? "health.krank" : "health.erholt"));
                    _host.World?.RefreshCare();
                    if (_host.World?.Avatar != null && !_sleeping) _host.World.Avatar.SetEmotion(e == "recovered" ? "happy" : "sad");
                }
            }
        }

        private static string StatToAbsicht(string stat)
        {
            switch (stat)
            {
                case "hunger": return "hunger";
                case "energy": return "muede";
                case "hygiene": return "dreckig";
                default: return "langeweile";
            }
        }

        private float _recapCheckIn = 3f;

        public override void Update()
        {
            TickInteractUi(Time.unscaledDeltaTime);
            TickDoorMash(Time.unscaledDeltaTime);
            TickGuide(Mathf.Min(Time.unscaledDeltaTime, 0.25f));
            TickButtonsCard(Mathf.Min(Time.unscaledDeltaTime, 0.25f));
            TickLife(Mathf.Min(Time.unscaledDeltaTime, 0.25f));
            TickSpotlight(Mathf.Min(Time.unscaledDeltaTime, 0.25f));
            // a waiting look back plays once Gooby is home and the level-up card is gone
            _recapCheckIn -= Time.unscaledDeltaTime;
            if (_recapCheckIn <= 0f)
            {
                _recapCheckIn = 2f;
                var pending = Gooby.Core.Meta.Recap.PendingLevel(_host.Store);
                if (pending > 0 && _host.Ui.Current == this && !_host.Ui.Celebrating && !_host.Ui.Fading && (_host.World == null || _host.World.Routine == "")) _host.Ui.Push(new RecapScreen(_host, pending));
            }
            _clockAccum += Time.unscaledDeltaTime;
            if (_clockAccum >= 10f) UpdateClock(force: false);
            TickZzz(Time.unscaledDeltaTime);
            if (_bubbleLeft > 0f && _host.Ui.Current == this)
            {
                _bubbleLeft -= Time.unscaledDeltaTime;
                if (_bubbleLeft <= 0f) HideBubble();
            }
        }

        private void UpdateClock(bool force)
        {
            _clockAccum = 0f;
            // bedtime comes and goes with the clock, not only with a stat tick
            OnStats(J.Dict(J.Dict(_host.Store.State["gooby"])["stats"]));
            var now = DateTimeOffset.FromUnixTimeMilliseconds(_host.Store.Clock.NowMs()).ToLocalTime();
            _clock.text = now.ToString("HH:mm");
            var culture = _host.Strings.Locale == "en" ? new System.Globalization.CultureInfo("en-GB") : new System.Globalization.CultureInfo("de-DE");
            _date.text = now.ToString("ddd, d. MMM", culture).Replace(".,", ",");
            var icon = WeatherIcon(now);
            var tex = Ux.IconTexture(icon);
            if (tex != null) _weatherIcon.style.backgroundImage = new StyleBackground(tex);
        }

        /// <summary>
        /// The clock's weather icon: this hour's home weather (SoulWetter, deterministic per day) — sun or moon when clear,
        /// cloud for clouds and fog, rain for drizzle/rain/thunder, snow when winter rain falls as snow.
        /// </summary>
        public static string WeatherIcon(DateTimeOffset local)
        {
            var lage = Gooby.Core.Life.SoulWetter.Zustand(local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), local.Hour + local.Minute / 60.0);
            switch (lage.Typ)
            {
                case "schnee": return "snow";
                case "niesel": case "regen": case "gewitter": return "rain";
                case "wolken": case "nebel": return "cloud";
                default: return local.Hour >= 21 || local.Hour < 6 ? "moon" : "sun";
            }
        }

        private void ToggleStatsSheet()
        {
            var open = _statsSheet.style.display == DisplayStyle.None;
            if (open)
            {
                RefreshBuffChips();
                // a drop-down from the profile card: over the left nav column, not under it
                _statsSheet.BringToFront();
            }
            _statsSheet.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private readonly Label[] _statBuffs = new Label[4];

        /// <summary>The buffs running on each need right now (the holiday's rest is synced in first).</summary>
        private void RefreshBuffChips()
        {
            var now = _host.Store.Clock.NowMs();
            GoobyBuffs.SyncErholt(_host.Store, now);
            var slice = GoobyBuffs.SliceOf(_host.Store.State);
            var keys = Stats.Keys;
            var rested = GoobyBuffs.Active(slice, now).Exists(b => J.Str(b["id"]) == GoobyBuffs.Erholt);
            for (var i = 0; i < keys.Length && i < _statBuffs.Length; i++)
            {
                var chip = _statBuffs[i];
                if (chip == null) continue;
                var bonus = GoobyBuffs.StatBonus(slice, keys[i], now);
                chip.style.display = bonus > 0.0 ? DisplayStyle.Flex : DisplayStyle.None;
                chip.text = (keys[i] == "energy" && rested ? "☀ " : "") + "+" + Mathf.RoundToInt((float)bonus);
            }
        }

        private void Interact() { if (!AwayBlocks()) _host.Ui.Push(new CareSheetScreen(_host, Say)); }

        private void Talk() { if (!AwayBlocks()) _host.Ui.Push(new TalkScreen(_host, Say)); }

        /// <summary>Gooby says a line in his bubble (sheets opened from elsewhere, e.g. today's parcel at boot).</summary>
        public void SayLine(string text) { if (_bubbleText != null) Say(text); }

        private void Say(string text)
        {
            var again = _bubbleText.text == text;
            ShowBubble();
            if (again) return;
            _bubbleText.text = text;
            _host.Audio?.Say(text, GoobyMoodVoice());
        }

        // ── random events (random_events.gd: rolled on start and on coming back) ──

        private VisualElement _eventCard;
        private Label _eventText;
        private float _lastEventRoll = -9999f;
        private const float EventRollGapSec = 1200f;

        /// <summary>
        /// On boot and when the home comes back (at most every 20 minutes of play): a timed-out event fails and Gooby says
        /// so, or a new one may start (its notification as a toast, the card on the right).
        /// </summary>
        private void TryRollEvent()
        {
            if (_host.Ui.Current != this || Root == null || Root.panel == null) return;
            var store = _host.Store;
            if (!J.IsTrue(J.Path(store.State, "onboarding", "done"))) { RefreshEvent(); return; }
            if (Time.realtimeSinceStartup - _lastEventRoll < EventRollGapSec) { RefreshEvent(); return; }
            _lastEventRoll = Time.realtimeSinceStartup;
            var now = store.Clock.NowMs();
            var local = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
            JObject started = null;
            var fail = "";
            store.Update(s =>
            {
                started = RandomEvents.RollOnStart(s, RandomEvents.Defs(), now, local.Hour * 60 + local.Minute, UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);
                fail = RandomEvents.TakeFailNotice(s);
            }, GameHost.TicksMs);
            store.NotifySliceChanged(RandomEvents.SliceId);
            if (fail != "") Say(fail);
            if (started != null)
            {
                Toast(J.Str(started["notification_text_de"]));
                Sfx.PlayUi("soft_open");
                Debug.Log("[ereignis] started " + J.Str(started["id"]));
            }
            RefreshEvent();
        }

        private void RefreshEvent()
        {
            if (_eventCard == null) return;
            var state = _host.Store.State;
            var active = J.Path(state, RandomEvents.SliceId, "active") as JObject;
            var def = active != null && active.HasValues && !RandomEvents.IsTimedOut(active, _host.Store.Clock.NowMs())
                ? RandomEvents.DefById(RandomEvents.Defs(), J.Str(active["id"])) : null;
            if (def != null && J.Str(def["context"], "home") != "home") def = null; // ranch events wait at the ranch
            _eventCard.style.display = def != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (def != null) _eventText.text = J.Str(def["notification_text_de"]);
            LayoutRightCards();
        }

        private void OpenEvent()
        {
            var active = J.Path(_host.Store.State, RandomEvents.SliceId, "active") as JObject;
            var def = RandomEvents.DefById(RandomEvents.Defs(), J.Str(active?["id"]));
            if (def == null || RandomEvents.IsTimedOut(active, _host.Store.Clock.NowMs())) { RefreshEvent(); return; }
            Sfx.PlayUi("soft_open");
            _host.Ui.Push(new EventRescueScreen(_host, def, Say));
        }

        // ── soul moments (soul_service.gd via gooby_reactions.gd _run_enter) ──

        private bool _soulPending = true;

        /// <summary>Asks for the entering moment a beat after the home shows (boot, a closed sheet, another room).</summary>
        private void QueueSoulMoment(long delayMs)
        {
            _soulPending = true;
            Root?.schedule.Execute(TrySoulMoment).StartingIn(delayMs);
        }

        /// <summary>
        /// The one moment on entering (Soul.DecideEnter): a ritual, a greeting or a weather line in Gooby's bubble with
        /// its emotion, clip, sound and confetti. Only on top of the stack, after the onboarding, never while he sleeps.
        /// </summary>
        private void TrySoulMoment()
        {
            if (!_soulPending || _host.Ui.Current != this || Root == null || Root.panel == null || _bubbleText == null) return;
            var store = _host.Store;
            if (!J.IsTrue(J.Path(store.State, "onboarding", "done")) || Sleep.IsSleeping(Sleep.FlatOf(store.State))) return;
            _soulPending = false;
            // back from a holiday the reunion is the greeting; while he is away nobody greets from the empty room
            if (_away || Urlaub.HeimkehrAusstehend(store.State) || _homecomingQueued) return;
            var now = store.Clock.NowMs();
            var local = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
            var lage = SoulWetter.Zustand(local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), local.Hour + local.Minute / 60.0);
            Soul.Moment moment = null;
            long gap = 0;
            store.Update(s =>
            {
                gap = Soul.TouchVisit(s, now);
                var ctx = new Soul.Ctx
                {
                    NowMs = now, GapMs = gap, Year = local.Year, Month = local.Month, Day = local.Day, Hour = local.Hour,
                    WetterTyp = lage.Typ, Schnee = lage.Schnee,
                    PlayerName = J.Str(J.Path(s, "meta", "playerName")), Nickname = J.Str(J.Path(s, "meta", "goobyNickname"), "Gooby"),
                };
                var slice = Soul.Slice(s);
                moment = Soul.DecideEnter(s, slice, Soul.Defs(), ctx, UnityEngine.Random.value, _host.Strings);
                if (moment != null) Soul.BookEnter(slice, moment, ctx);
            }, GameHost.TicksMs);
            store.NotifySliceChanged(Soul.SliceId);
            if (moment == null || string.IsNullOrEmpty(moment.TextKey))
            {
                AfterEnterMoment(null, gap);
                return;
            }
            var args = new List<(string name, object value)>();
            foreach (var kv in moment.Args) args.Add((kv.Key, kv.Value));
            Say(_host.Strings.T(moment.TextKey, args.ToArray()));
            _lastLifeBubble = Time.unscaledTime;
            var avatar = _host.World?.Avatar;
            if (avatar != null && !(_host.World?.CrateNapping ?? false))
            {
                avatar.SetEmotion(moment.Emotion);
                Hold(4f);
                var clip = ClipOf(moment.Clip);
                if (clip != "")
                {
                    avatar.Play(clip);
                    if (clip.EndsWith("-loop")) HoldClip(clip, 6f);
                }
            }
            var sfx = SfxOf(moment.Sfx);
            if (sfx != "") Sfx.PlayUi(sfx);
            if (moment.Aktion == "konfetti") _host.Ui.Confetti(90);
            Debug.Log("[soul] moment " + moment.Id + " (" + moment.TextKey + ")");
            AfterEnterMoment(moment, gap);
        }

        /// <summary>The babble's emotion from Gooby's mood band (sleepy while asleep).</summary>
        private string GoobyMoodVoice()
        {
            var state = _host.Store.State;
            var flat = Sleep.FlatOf(state);
            if (Sleep.IsSleeping(flat)) return "sleepy";
            var band = Stats.MoodBand(Sleep.CurrentMood(GoobyTicker.FlatView(state), _host.Store.Clock.NowMs()));
            return band == "happy" || band == "ecstatic" ? "happy" : band == "grumpy" || band == "miserable" ? "sad" : "neutral";
        }

        /// <summary>HUD messages share the global notice lane: one place, two at a time, in order (no second stack under it).</summary>
        private void Toast(string text, string icon = "leaf") => _host.Ui.Toast(text, icon, 2.6f);

        private void AddTab(VisualElement parent, string id, string icon, string label, Action onClick, string badge = null)
        {
            var b = new Button(() => { SetTab(id); onClick(); }) { text = "" };
            b.ClearClassList();
            b.AddToClassList("gb-tab");
            if (id == _activeTab) b.AddToClassList("gb-tab--active");
            b.Add(Ux.IconTile(icon));
            b.Add(Ux.Text(label));
            if (badge != null) b.Add(Ux.Badge(badge));
            _tabs[id] = b;
            parent.Add(b);
        }

        private void SetTab(string id)
        {
            _activeTab = id;
            foreach (var kv in _tabs) kv.Value.EnableInClassList("gb-tab--active", kv.Key == id);
        }

        private void RefreshQuests()
        {
            if (_taskRows == null || _host.Quests == null) return;
            var t = _host.Strings;
            _taskRows.Clear();
            var board = _host.Quests.Board();
            var claimed = 0;
            foreach (var row in board)
            {
                if (row.Claimed) claimed++;
                var title = t.HasKey(row.Def.TitleKey) ? t.T(row.Def.TitleKey) : row.Def.Id;
                var line = CheckRow(title, row.Claimed);
                if (row.Complete && !row.Claimed)
                {
                    line.AddToClassList("gb-checkrow--claimable");
                    line.Add(Ux.Badge("+" + row.Def.Muenzen));
                    var id = row.Def.Id;
                    line.RegisterCallback<ClickEvent>(_ => ClaimQuest(id));
                }
                _taskRows.Add(line);
                if (!row.Claimed && row.Target > 1)
                {
                    var prog = Ux.Row();
                    prog.style.marginLeft = 34;
                    var bar = Ux.Bar((float)row.Progress / row.Target, out _);
                    bar.style.width = 150; bar.style.height = 8;
                    prog.Add(bar);
                    var txt = Ux.Text(row.Progress + "/" + row.Target, "gb-sm", "gb-mild");
                    txt.style.marginLeft = 8;
                    prog.Add(txt);
                    _taskRows.Add(prog);
                }
            }
            _tasksCount.text = claimed + "/" + Gooby.Core.Meta.DailyQuestEngine.QuestsPerDay;
        }

        private void ClaimQuest(string id)
        {
            var outcome = _host.Quests.Claim(id);
            if (!outcome.Ok) return;
            Say(_host.Strings.T("nl.quests.abgeholt", ("muenzen", outcome.Muenzen), ("xp", outcome.Xp)));
            if (outcome.BonusPaid) Say(_host.Strings.T("quests.bonus_toast", ("muenzen", Gooby.Core.Meta.DailyQuestEngine.BonusCoins)));
            _host.World?.Bounce();
            RefreshQuests();
        }

        private static VisualElement CheckRow(string text, bool done)
        {
            var row = Ux.El("gb-checkrow");
            var box = Ux.El("gb-checkbox");
            if (done) { box.AddToClassList("gb-checkbox--done"); box.Add(Ux.Icon("check")); }
            row.Add(box);
            var l = Ux.Text(text, "gb-md", "gb-semibold");
            if (done) l.AddToClassList("gb-mild");
            row.Add(l);
            return row;
        }

        /// <summary>Board 08: the "+" of the coin and leaf pills is a sun-yellow disc with a dark cross.</summary>
        private static void HudPlus(VisualElement pill)
        {
            var plus = pill.Q<Button>();
            if (plus == null) return;
            plus.AddToClassList("gb-hud-plus");
            plus.style.backgroundColor = new Color(1f, 0.82f, 0.4f);
            plus.style.width = 32; plus.style.height = 32;
            // the plus icon is a white cross on its own green disc — board 08 wants a dark cross on sun yellow instead
            var icon = plus.Q(className: "gb-icon");
            if (icon != null) icon.style.display = DisplayStyle.None;
            var cross = Ux.Text("+", "gb-hud-plus-mark");
            cross.pickingMode = PickingMode.Ignore;
            plus.Add(cross);
        }

        private static VisualElement Action(string icon, string label, Action onClick, bool main = false)
        {
            var a = Ux.El("gb-action");
            if (main) a.AddToClassList("gb-action--main");
            a.Add(Ux.Icon(icon));
            a.Add(Ux.Text(label));
            a.RegisterCallback<ClickEvent>(_ => onClick());
            return a;
        }
    }
}
