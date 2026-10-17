using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Minigames;
using Gooby.Core.Net;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The frame every minigame host shares, in the concept look: pregame card (game tile, difficulty chips, best +
    /// stars, energy cost, sleepy gate) → the game's own play field (portrait, centred on the landscape screen, with
    /// time/score pills, streak line, banner card, pause + quit) → end moment (stamp, confetti/consolation) → results
    /// card (MinigameAward booking: coins, daily ×2, XP, level-ups, best — counted up, stars, record fanfare, quick
    /// retry). The frame also carries the game feel for all hosts (port of minigame_host.gd + juice_kit.gd): 3-2-1-LOS
    /// inside the engine's intro lock, automatic score/streak feedback read off the pills, a pause modal (auto-pause
    /// when a phone loses focus), sounds and haptics. Subclasses bring the realtime engine and its rendering.
    /// </summary>
    public abstract partial class MinigameHostScreen : UiScreen
    {
        protected readonly GameHost Host;
        protected readonly MinigameMeta Meta;
        protected string Difficulty = "normal";
        protected VisualElement Field;
        protected Label TimeLabel, ScoreLabel, ComboLabel;
        protected float FieldW, FieldH;
        /// <summary>The shared juice kit: its layer lies over the field and the HUD, the frame ticks it every frame.</summary>
        protected readonly Juice Fx = new Juice();

        private VisualElement _card, _hintLabelBox, _bannerCard, _scorePill, _pauseBtn, _quitBtn, _pauseModal, _bestStars;
        private Label _bannerLabel, _bestLabel, _countdown, _stamp;
        private double _bannerUntil;
        private bool _bannerOn;
        /// <summary>
        /// The arcade sheet this host was opened from — null for <c>-gooby-open game:&lt;id&gt;</c>, the QA driver and tests.
        /// Only while it is still open underneath does the results card offer "Zur Arcade" (a plain Pop back to it).
        /// </summary>
        private readonly ArcadeScreen _arcade;

        private enum Phase { Pregame, Playing, Ending, Results }
        private Phase _phase = Phase.Pregame;
        private bool _paused, _focusHooked, _endMomentPending;
        private double _endAt;
        /// <summary>The score the end moment read (engine FinalScore / pill) — the fallback card's number if booking fails.</summary>
        private int? _endScore;
        /// <summary>The arcade bonus event this round spent at its start (refunded once if the round is dropped) and its award payload.</summary>
        private JObject _modSnapshot, _modParams = new JObject();
        /// <summary>The round's modifier payload (empty without a bonus event) — QA and tests.</summary>
        public JObject RoundModifier => _modParams;

        /// <summary>The player's 3D Gooby beside the windows and in the round's HUD (null without a GPU).</summary>
        private World.MgBuddy3D _buddy;
        private VisualElement _buddyBadge, _buddyBadgeView, _buddyBand;
        private bool _buddyBandOk;

        /// <summary>
        /// True while the host's own 3D stage shows the player's Gooby in the round (carrot catch's basket carrier): the
        /// frame's companion then stays off the HUD until the results card.
        /// </summary>
        protected virtual bool StageHasGooby => false;

        /// <summary>The companion for host-specific reactions (a catch, a crash); null without a GPU or before the pregame.</summary>
        protected World.MgBuddy3D Buddy => _buddy;

        /// <summary>
        /// True while <see cref="TakeFinalScore"/> runs only as the cleanup of a dropped round (pause → Neustart): hosts
        /// that book extras there (album finds) must skip them — a dropped round books nothing.
        /// </summary>
        protected bool DroppingRound { get; private set; }

        /// <summary>Stable element names of the frame — flow tests and the QA driver (<c>click &lt;name&gt;</c>) find them by these.</summary>
        public static class ElementNames
        {
            public const string Pregame = "mg-pregame", Play = "mg-play", Close = "mg-close", Back = "mg-back";
            public const string Pause = "mg-pause", Quit = "mg-quit", Countdown = "mg-countdown", Stamp = "mg-stamp", ScorePill = "score-pill";
            public const string PauseModal = "mg-pause-modal", Resume = "mg-resume", Restart = "mg-restart", Sound = "mg-sound";
            public const string Help = "mg-help", HelpText = "mg-help-text", GiveUp = "mg-giveup", SleepyHint = "mg-sleepy", Spotlight = "mg-spotlight";
            public const string Results = "results", ResultsScore = "results-score", ResultsStars = "results-stars", Record = "results-record";
            public const string Again = "mg-again", Arcade = "mg-arcade", Home = "mg-home";
            /// <summary>Friends block of the results card (online only): top 3 as "mg-friends-board-&lt;i&gt;", own rank line.</summary>
            public const string FriendsBoard = "mg-friends-board", FriendsRank = "mg-friends-rank";
        }

        /// <summary>Games with a Unity host, by registry id.</summary>
        public static readonly HashSet<string> Hosted = new HashSet<string> { "carrotCatch", "bubblePop", "memoryMatch", "goobySays", "bunnyHop", "burgerBuild", "teaParty", "carrotGuard", "pancakeTower", "ghostHunt", "starHopper", "veggieChop", "basketBounce", "fishingPond", "trampoline", "goalieGooby", "danceParty", "gardenRush", "lanternFloat", "hideSeek", "miniGolf", "pipeFlow", "rocketRescue", "runner", "snailMail", "deliveryRush", "cityDrive", "harborHopper", "toyRacer", "shoppingSurf", "purblePlace", "gobnom", "gvz", "ranchHerde", "ranchParcours", "ranchTonnen", "ranchZeit", "ranchTurnier" };

        /// <summary>
        /// The host for a registry game, or null for ids without a Unity host — a stand-in game would run under the
        /// other game's title and book its score under the wrong id.
        /// </summary>
        public static MinigameHostScreen For(GameHost host, string gameId)
        {
            switch (gameId)
            {
                case "carrotCatch": return new MinigameScreen(host, "carrotCatch");
                case "bubblePop": return new BubblePopScreen(host);
                case "memoryMatch": return new MemoryMatchScreen(host);
                case "goobySays": return new GoobySaysScreen(host);
                case "bunnyHop": return new BunnyHopScreen(host);
                case "burgerBuild": return new BurgerBuildScreen(host);
                case "teaParty": return new TeaPartyScreen(host);
                case "carrotGuard": return new CarrotGuardScreen(host);
                case "pancakeTower": return new PancakeTowerScreen(host);
                case "ghostHunt": return new GhostHuntScreen(host);
                case "starHopper": return new StarHopperScreen(host);
                case "veggieChop": return new VeggieChopScreen(host);
                case "basketBounce": return new BasketBounceScreen(host);
                case "fishingPond": return new FishingPondScreen(host);
                case "trampoline": return new TrampolineScreen(host);
                case "goalieGooby": return new GoalieGoobyScreen(host);
                case "danceParty": return new DancePartyScreen(host);
                case "gardenRush": return new GardenRushScreen(host);
                case "lanternFloat": return new LanternFloatScreen(host);
                case "hideSeek": return new HideSeekScreen(host);
                case "miniGolf": return new MiniGolfScreen(host);
                case "pipeFlow": return new PipeFlowScreen(host);
                case "rocketRescue": return new RocketRescueScreen(host);
                case "runner": return new RunnerScreen(host);
                case "snailMail": return new SnailMailScreen(host);
                case "deliveryRush": return new DeliveryRushScreen(host);
                case "cityDrive": return new CityDriveArcadeScreen(host);
                case "harborHopper": return new HarborHopperScreen(host);
                case "toyRacer": return new ToyRacerScreen(host);
                case "shoppingSurf": return new ShoppingSurfScreen(host);
                case "purblePlace": return new PurblePlaceScreen(host);
                case "gobnom": return new GobnomScreen(host);
                case "gvz": return new GvzScreen(host);
                case "ranchHerde": return new RanchHerdeScreen(host);
                case "ranchParcours": return new RanchParcoursScreen(host);
                case "ranchTonnen": return new RanchTonnenScreen(host);
                case "ranchZeit": return new RanchZeitScreen(host);
                case "ranchTurnier": return new RanchTurnierScreen(host);
                default: return null;
            }
        }

        protected MinigameHostScreen(GameHost host, string gameId, string fallbackId)
        {
            // EditMode tests may build a host before (or without) the boot: fall back to the running GameHost, and every
            // lifecycle path below copes with none at all
            Host = host ?? GameHost.Instance;
            Meta = MinigameRegistry.GetGame(gameId) ?? MinigameRegistry.GetGame(fallbackId);
            Id = "minigame-" + (Meta != null ? Meta.Id : gameId);
            Opaque = true;
            // the arcade builds the host in its card's click handler, right before the push — it is still on top here
            _arcade = Ui?.Current as ArcadeScreen;
        }

        /// <summary>The screen stack (null without a GameHost or before the UI exists).</summary>
        private UiRoot Ui => Host != null ? Host.Ui : null;

        /// <summary>The arcade this host came from still lies underneath (a visit's PopToRoot may have closed it meanwhile).</summary>
        private bool ArcadeUnderneath => _arcade != null && Ui != null && Ui.IsOpen(_arcade);

        /// <summary>Reduced Motion (settings) for the hosts' own effects: no shake, fewer sparks (see <see cref="ParticleStep"/>).</summary>
        protected static bool ReducedMotion => Juice.ReducedMotion;

        /// <summary>Loop step for a host's own spark/confetti loops — every third particle under Reduced Motion (Juice does the same).</summary>
        protected static int ParticleStep => Juice.ReducedMotion ? 3 : 1;

        /// <summary>The view the engine is calibrated to (reference width/height; the field keeps that aspect).</summary>
        protected abstract (double w, double h) ViewSize { get; }
        /// <summary>True while a round runs (the frame ticks and renders it).</summary>
        protected abstract bool Running { get; }
        /// <summary>Creates the engine for a round and hooks its events (Finished → RoundOver()).</summary>
        protected abstract void StartGame(int seed);
        /// <summary>Adds the game's own pieces to the play field (background, actors) and the intro banner.</summary>
        protected abstract void BuildPlayField();
        protected abstract void TickGame(double dt);
        protected abstract void RenderGame();
        protected abstract int TakeFinalScore();
        /// <summary>Forgets the running round (quit).</summary>
        protected abstract void DropGame();

        // ── per-game knobs (defaults fit most hosts) ────────────────────────

        /// <summary>How the frame answers score changes it reads off <see cref="ScoreLabel"/>.</summary>
        protected enum ScoreFeel
        {
            /// <summary>Every gain pops the pill (+Δ, rising hit tone), every loss rattles (miss tone, shake).</summary>
            Events,
            /// <summary>Gains only — for scores that also drift down with time or taps (efficiency bonus, fuel).</summary>
            GainsOnly,
            /// <summary>For scores that tick with distance: a fanfare per <see cref="ScoreMilestone"/>, big jumps as hits.</summary>
            Milestones,
            /// <summary>No automatic score feedback.</summary>
            Off,
        }

        protected virtual ScoreFeel ScoreFeedback => ScoreFeel.Events;
        protected virtual int ScoreMilestone => 50;
        /// <summary>Phones pause the round when the app loses focus (notification centre, app switch).</summary>
        protected virtual bool AutoPauseOnFocusLoss => Application.isMobilePlatform;

        /// <summary>
        /// Seconds the engine's intro lock still runs — the frame fits its 3-2-1 into it without touching the certified
        /// timing. NaN: the engine has no intro (the frame then holds the first ~0,9 s itself). The default reads the
        /// engine in the host's <c>_game</c> field: its <c>IntroLeft</c>, or a lead-in <c>SongTime</c> below zero.
        /// </summary>
        protected virtual double IntroLeft
        {
            get
            {
                var intro = EngineNumber("IntroLeft");
                if (!double.IsNaN(intro)) return intro;
                var song = EngineNumber("SongTime");
                return double.IsNaN(song) ? double.NaN : Math.Max(0.0, -song);
            }
        }

        /// <summary>
        /// The round ended because its clock ran out (end stamp "Zeit um!" instead of "Geschafft!"). The default reads the
        /// engine's <c>TimeLeft</c> (endless modes report +∞); hosts whose TimeLeft is not the round clock override it.
        /// </summary>
        protected virtual bool RoundEndedByClock
        {
            get
            {
                var left = EngineNumber("TimeLeft");
                return !double.IsNaN(left) && left <= 1e-6;
            }
        }

        protected override void Build(VisualElement root)
        {
            if (Host == null || Host.Store == null || Host.Strings == null || Meta == null)
            {
                // no boot behind the screen (an EditMode test mounting it): nothing to show, nothing to book
                Debug.LogWarning("[mg] " + Id + ": no GameHost — the frame stays empty");
                return;
            }
            Difficulty = FrameworkLogic.EffectiveDifficulty(Meta.Id, null, null);
            var sel = FrameworkLogic.DifficultySliceOf(Host.Store.State, Meta.Id).Selected;
            if (Array.IndexOf(FrameworkLogic.DifficultyModes, sel) >= 0) Difficulty = sel;
            ShowPregame(root);
        }

        // ── pregame ─────────────────────────────────────────────────────────

        /// <summary>The sounds of the frame, decoded while the pregame card is up (the first "3" must not hitch on a load).</summary>
        private static readonly string[] FrameSounds =
        {
            "game_countdown", "game_go", "game_hit", "game_coin", "game_miss", "game_combo", "game_pop", "game_win", "game_lose",
            "game_count", "game_star", "game_record", "soft_tap", "soft_confirm", "soft_back", "soft_open", "soft_close",
        };

        private void ShowPregame(VisualElement root)
        {
            if (root == null) return;
            root.Clear();
            _timeline.Clear();
            Fx.Clear();
            _phase = Phase.Pregame;
            _paused = false;
            _pauseModal = null;
            _cd = Cd.None;
            _endMomentPending = false;
            UnhookFocus();
            WorldCamera(true);
            Sfx.Preload(FrameSounds);
            var t = Host.Strings;
            root.Add(Ux.El("gb-scrim"));
            // board 06 "Fenster": cream window, title band (game tile, title, the how-to line, round close), white cards
            _card = Ux.El("gb-mgw", "gb-pregame-card");
            _card.name = ElementNames.Pregame;
            Ux.Abs(_card, null, null);
            _card.style.left = Length.Percent(50); _card.style.top = Length.Percent(50);
            _card.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            PopIn(_card, 0.0);
            var close = Ux.IconBtn("close", () => { Sfx.PlayUi("soft_back"); Leave(); }, "gb-mgw-close");
            close.name = ElementNames.Close;
            _card.Add(FrameBand(GameTile(Meta.Id, 64f), t.T(Meta.TitleKey), HintText(), BandWash(Meta.Id), null, close));
            // the player's Gooby stands beside the cards and waves (sleepy and sitting when he is too tired to play)
            var main = Ux.Row("gb-mgw-main");
            _card.Add(main);
            var buddy = BuddyColumn("mg-buddy-pregame", 176f, 236f);
            if (buddy != null)
            {
                main.Add(buddy);
                _buddy.Rest(IsExhausted() ? "sleepy" : "happy");
                _buddy.Settle();
                At(0.35, () => _buddy?.Wave());
            }
            _card.style.width = buddy != null ? 780 : 600;
            FitWindow(_card, buddy);
            var body = Ux.El("gb-mgw-body", "gb-grow");
            body.style.flexShrink = 1;
            main.Add(body);
            // the game's painted cover over the cards (the arcade art); it steps aside when the window would not fit the screen
            var hero = GameCover(Meta.Id, "gb-pregame-hero");
            hero.name = "mg-pregame-cover";
            body.Add(hero);
            root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var free = root.resolvedStyle.height;
                var card = _card?.resolvedStyle.height ?? 0f;
                if (float.IsNaN(free) || float.IsNaN(card) || free <= 1f) return;
                var show = hero.style.display != DisplayStyle.None ? card <= free * 0.94f : card + 128f <= free * 0.94f;
                var want = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (hero.style.display != want) hero.style.display = want;
            });

            var diffCard = Ux.El("gb-mgw-card");
            diffCard.Add(Ux.Text(t.T("mg.pregame.difficulty"), "gb-mgw-kicker"));
            var chips = Ux.Row("gb-mgw-chips");
            var endless = FrameworkLogic.EndlessUnlocked(Host.Store.State, Meta.Id) && Meta.SupportsEndless;
            var chipMap = new Dictionary<string, Button>();
            foreach (var mode in FrameworkLogic.DifficultyModes)
            {
                if (mode == "endless" && !Meta.SupportsEndless) continue;
                var local = mode;
                var b = new Button(() =>
                {
                    Difficulty = local;
                    foreach (var kv in chipMap) kv.Value.EnableInClassList("gb-chip--active", kv.Key == local);
                    Sfx.PlayUi("soft_chip");
                    RefreshBest();
                    Host.Store.Update(s => J.EnsureDict(J.EnsureDict(s, "minigames"), "difficulty")[Meta.Id] = local, GameHost.TicksMs);
                }) { text = t.T("mg.diff." + mode), name = "mg-diff-" + mode };
                b.ClearClassList();
                b.AddToClassList("gb-chip");
                b.style.marginRight = 8;
                if (mode == "endless" && !endless) b.SetEnabled(false);
                chipMap[mode] = b;
                chips.Add(b);
            }
            foreach (var kv in chipMap) kv.Value.EnableInClassList("gb-chip--active", kv.Key == Difficulty);
            diffCard.Add(chips);
            body.Add(diffCard);

            // best of the chosen difficulty + its stars, then the small facts (energy, today's spotlight)
            var bestCard = Ux.El("gb-mgw-card");
            var bestRow = Ux.Row("gb-pregame-best", "gb-mgw-stat");
            _bestLabel = Ux.Text("", "gb-mgw-best");
            bestRow.Add(_bestLabel);
            _bestStars = StarRow(0, 28f);
            bestRow.Add(_bestStars);
            bestCard.Add(bestRow);
            RefreshBest();
            var tags = Ux.Row("gb-mgw-tags");
            tags.Add(FrameTag("sun", t.T("mg.pregame.energy", ("energy", Meta.EnergyCost))));
            bestCard.Add(tags);
            var spotlight = SpotlightLine();
            if (spotlight != null) bestCard.Add(spotlight);
            var bonus = ModifierLine();
            if (bonus != null) bestCard.Add(bonus);
            body.Add(bestCard);
            var extra = PregameExtra();
            if (extra != null) body.Add(extra);
            _hintLabelBox = SleepyHint();
            // too tired already: say so on the card instead of only after the first press
            if (IsExhausted()) _hintLabelBox.style.display = DisplayStyle.Flex;
            body.Add(_hintLabelBox);

            // Schließen (sand) · the big pink Spielen! with the white play triangle
            var actions = Ux.Row("gb-mgw-actions");
            var back = FramePill(null, t.T("nl.allgemein.schliessen"), () => { Sfx.PlayUi("soft_back"); Leave(); }, false, "gb-mgw-sand");
            back.name = ElementNames.Back;
            back.style.marginRight = 10;
            actions.Add(back);
            var play = FramePill("play", t.T("mg.pregame.play"), () => { if (_phase == Phase.Pregame) BeginRound(root, false, "soft_confirm"); }, false, "gb-btn-primary", "gb-mgw-pill--big", "gb-grow");
            play.name = ElementNames.Play;
            // too tired: the button stays pressable (its tap pops the hint) but reads as resting
            play.EnableInClassList(TiredClass, IsExhausted());
            actions.Add(play);
            body.Add(actions);
            root.Add(_card);
            root.Add(Fx.Layer);
        }

        /// <summary>The game's how-to line (mg.&lt;id&gt;.hint) — pregame card and the pause modal's Hilfe; mg.host.help_none without one.</summary>
        private string HintText()
        {
            var t = Host.Strings;
            var key = "mg." + Meta.Id + ".hint";
            return t.HasKey(key) ? t.T(key) : t.T("mg.host.help_none");
        }

        private void RefreshBest()
        {
            var best = FrameworkLogic.BestForMode(Host.Store.State, Meta.Id, Difficulty);
            _bestLabel.text = Host.Strings.T("mg.pregame.best", ("best", best));
            SetStars(_bestStars, BestStars(Host.Store.State, Meta, Difficulty));
        }

        /// <summary>"Spiel des Tages" on the pregame card when this game is today's arcade spotlight (+50 % once a day); null otherwise.</summary>
        private Label SpotlightLine()
        {
            try
            {
                var day = Host.Store.Clock.LocalDay();
                if (!ArcadeSpotlight.IstSpotlight(Meta.Id, day)) return null;
                var t = Host.Strings;
                var text = ArcadeSpotlight.BonusVerfuegbar(Host.Store.State, day)
                    ? t.T("mg.spotlight.pregame.aktiv", ("prozent", ArcadeSpotlight.BonusProzent))
                    : t.T("mg.spotlight.pregame.eingeloest");
                var l = Ux.Text(text, "gb-sm", "gb-bold", "gb-pregame-spotlight");
                l.name = ElementNames.Spotlight;
                l.style.marginTop = 6;
                l.style.whiteSpace = WhiteSpace.Normal;
                return l;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[mg] spotlight line: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// An arcade bonus event running for this game (ModifierEngine): a strip in the event's colour on the pregame card —
        /// title + name, what it does, rounds and time left; null otherwise.
        /// </summary>
        private VisualElement ModifierLine()
        {
            var nowMs = Host.Store.Clock.NowMs();
            var active = ModifierEngine.GetActiveFor(Host.Store.State, Meta.Id, nowMs);
            if (active == null) return null;
            var t = Host.Strings;
            var box = Ux.Row();
            box.name = "mg-modifier";
            box.style.marginTop = 8;
            box.style.paddingTop = 8; box.style.paddingBottom = 8; box.style.paddingLeft = 10; box.style.paddingRight = 12;
            box.style.borderTopLeftRadius = 16; box.style.borderTopRightRadius = 16; box.style.borderBottomLeftRadius = 16; box.style.borderBottomRightRadius = 16;
            if (ColorUtility.TryParseHtmlString(active.Def.ColorHex, out var tint)) box.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.24f);
            var icon = Ux.Icon(active.Def.Icon);
            icon.style.width = 30; icon.style.height = 30; icon.style.marginRight = 10;
            box.Add(icon);
            var col = Ux.Col("gb-grow");
            col.style.flexShrink = 1;
            col.Add(Ux.Text(t.T("modifier.pregame.titel") + " " + t.T(active.Def.NameKey), "gb-sm", "gb-bold"));
            var desc = Ux.Text(t.T(active.Def.DescKey), "gb-sm");
            desc.style.whiteSpace = WhiteSpace.Normal;
            col.Add(desc);
            var rest = ModifierEngine.CountdownText(active.EndsAt, nowMs);
            col.Add(Ux.Text(active.RemainingPlays == 1 ? t.T("modifier.pregame.runde_eine", ("rest", rest)) : t.T("modifier.pregame.runden", ("n", active.RemainingPlays), ("rest", rest)), "gb-sm", "gb-mild"));
            box.Add(col);
            return box;
        }

        /// <summary>The bonus event of a booked round on the results card: its name and what it gave (extra coins, ×1,5, ×2, roll …).</summary>
        private VisualElement ModifierResultRow(AwardResult award)
        {
            if (award.Modifier == null || !ModifierEngine.Types.TryGetValue((string)award.Modifier["type"] ?? "", out var def)) return null;
            var t = Host.Strings;
            var effects = new List<string>();
            if (award.ModifierBonusCoins > 0) effects.Add(t.T("modifier.results.bonus_coins", ("n", award.ModifierBonusCoins)));
            if (def.ScoreMult > 0.0) effects.Add(t.T("modifier.results.score_mult"));
            if (def.XpMult > 0.0) effects.Add(t.T("modifier.results.xp_mult"));
            if (def.EnergyFree) effects.Add(t.T("modifier.results.energie"));
            if (award.GluecksrolleCoins > 0) effects.Add(t.T("modifier.results.gluecksrolle", ("n", award.GluecksrolleCoins)));
            if (award.ModifierCapped) effects.Add(t.T("modifier.results.capped"));
            var text = t.T("modifier.results.aktiv", ("name", t.T(def.NameKey)));
            if (effects.Count > 0) text += " · " + string.Join(" · ", effects);
            var row = ResultRow(def.Icon, text, out var label);
            row.name = "mg-result-modifier";
            label.style.whiteSpace = WhiteSpace.Normal;
            return row;
        }

        private Label SleepyHint()
        {
            // a rosy strip inside the window (pregame, pause, results)
            var l = Ux.Text(Host.Strings.T("mg.pregame.too_sleepy"), "gb-sm", "gb-bold", "gb-mgw-sleepy");
            l.name = ElementNames.SleepyHint;
            l.style.color = new Color(0.95f, 0.43f, 0.43f);
            l.style.marginTop = 6;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.display = DisplayStyle.None;
            return l;
        }

        /// <summary>A start button while Gooby is too tired to play (dimmed, still pressable — the tap explains why).</summary>
        private const string TiredClass = "gb-btn--tired";

        private bool IsExhausted() => Host != null && Host.Store != null && J.Num(J.Path(Host.Store.State, "gooby", "stats", "energy")) <= Stats.ExhaustedAtOrBelow;

        private void ShowSleepyHint()
        {
            if (_hintLabelBox == null) return;
            _hintLabelBox.style.display = DisplayStyle.Flex;
            Fx.ScalePop(_hintLabelBox, 1.08f, 0.24f);
            Sfx.PlayUi("soft_error");
        }

        // ── play ────────────────────────────────────────────────────────────

        /// <summary>Starts a round (the pregame's Play; public for the flow tests). Ignored while a round runs or ends.</summary>
        public void StartRound(VisualElement root)
        {
            if (_phase == Phase.Playing || _phase == Phase.Ending) return;
            BeginRound(root, false, null);
        }

        /// <summary>
        /// A round from Play (full 3-2-1) or a quick retry from results/pause (engines without an intro skip the hold).
        /// Too tired: the sleepy hint pops (with its error tone) and nothing is spent — <paramref name="sound"/> only
        /// plays for a round that really starts.
        /// </summary>
        private void BeginRound(VisualElement root, bool quick, string sound)
        {
            if (root == null || Host == null || Host.Store == null) return;
            if (IsExhausted())
            {
                ShowSleepyHint();
                return;
            }
            if (sound != null) Sfx.PlayUi(sound);
            // an arcade bonus event for this game spends one of its rounds at the real start (Federleicht: no energy);
            // energy cost per round start (§C6 web parity)
            _modSnapshot = null;
            _modParams = new JObject();
            var nowMs = Host.Store.Clock.NowMs();
            Host.Store.Update(s =>
            {
                var mod = ModifierEngine.Consume(s, Meta.Id, nowMs);
                if (mod.ok)
                {
                    _modSnapshot = mod.snapshot;
                    _modParams = ModifierEngine.LaunchParams(mod.snapshot);
                }
                if (J.IsTrue(_modParams["energy_free"])) return;
                var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats");
                stats["energy"] = Stats.ClampStat(J.Num(stats["energy"]) - Meta.EnergyCost);
            }, GameHost.TicksMs);
            if (_modSnapshot != null) Debug.Log("[modifier] round of " + Meta.Id + " with " + (string)_modParams["type"]);
            var seed = Math.Max(1, UnityEngine.Random.Range(1, int.MaxValue));
            DropActor();
            Fx.Clear();
            _paused = false;
            _pauseModal = null;
            _hintLabelBox = null;
            _endMomentPending = false;
            _endScore = null;
            _cd = Cd.None;
            _cdHideAt = -1.0;
            StartGame(seed);
            BuildFrame(root);
            BuildPlayField();
            MountOverlay(root);
            _phase = Phase.Playing;
            BindBuddyForPlay();
            ResetWatchers();
            GeistStart();
            HookFocus();
            StartCountdown(quick);
        }

        /// <summary>"Nochmal" on the results card: the next round of the same difficulty with the quick countdown.</summary>
        private void Retry()
        {
            if (_phase != Phase.Results || Root == null) return;
            BeginRound(Root, true, "soft_tap");
        }

        /// <summary>
        /// The engine reports the end of the round (usually from inside its Tick): the frame freezes the round and plays
        /// the end moment on the next frame — after one last render, so the stamp reads the final clock and score — then
        /// the results card books the award. Repeated calls (a second Finished, an input handler) are ignored.
        /// </summary>
        /// <summary>
        /// QA remote (<c>mgend</c>): runs the round's own clock out at once, so the engine ends it itself and reports its
        /// real final score (the engines compute it in their own Finish); a round without a clock ends after three game
        /// minutes the hard way. End moment, booking and results follow as in play.
        /// </summary>
        public void QaEndRound()
        {
            if (_phase != Phase.Playing) return;
            if (_paused) ClosePauseModal();
            for (var i = 0; i < QaFastForwardTicks && _phase == Phase.Playing; i++) TickGame(1.0 / 30.0);
            if (_phase == Phase.Playing) RoundOver();
        }

        private const int QaFastForwardTicks = 30 * 180;

        protected void RoundOver()
        {
            if (_phase != Phase.Playing || Root == null || !Running || DroppingRound) return;
            _phase = Phase.Ending;
            if (_paused) ClosePauseModal();
            _cd = Cd.None;
            HideGeistChip();
            _endMomentPending = true;
            _endAt = double.MaxValue;
            _pauseBtn?.SetEnabled(false);
            _quitBtn?.SetEnabled(false);
        }

        /// <summary>
        /// Back to whatever lies underneath (the arcade, a place, the HUD). A host alone on the stack has nothing below
        /// it: the HUD takes its place — never an empty stack. False when the host cannot leave (no UI, covered by a sheet).
        /// </summary>
        private bool Leave()
        {
            var ui = Ui;
            if (ui == null || !ui.IsOpen(this) || ui.Current != this) return false;
            if (ui.Depth > 1) ui.Pop();
            else ui.Show(new HomeHudScreen(Host));
            return true;
        }

        /// <summary>"Nach Hause": down to the HUD (PopToRoot); a host alone on the stack is replaced by the HUD.</summary>
        private void GoHome()
        {
            var ui = Ui;
            if (ui == null || !ui.IsOpen(this)) return;
            if (ui.Depth > 1) ui.PopToRoot();
            else ui.Show(new HomeHudScreen(Host));
        }

        /// <summary>
        /// "Zur Arcade": back onto the arcade that opened this host (a plain Pop — it lies right underneath). Gone
        /// meanwhile, or never there (<c>-gooby-open game:&lt;id&gt;</c>, the QA driver): the HUD instead of a blind Pop.
        /// </summary>
        private void BackToArcade()
        {
            var ui = Ui;
            if (ui == null) return;
            if (!ArcadeUnderneath) { GoHome(); return; }
            for (var guard = 0; guard < 16 && ui.Current != _arcade && ui.Depth > 1 && ui.IsOpen(_arcade); guard++) ui.Pop();
        }

        /// <summary>
        /// The portrait field and its bands cover the whole screen during a round, so the 3D room behind the UI need
        /// not render — a real saving on phones and the difference between 2 and 10 fps in the software-rendered QA
        /// player. Back on for the results card, which sits over the room again.
        /// </summary>
        private void WorldCamera(bool on)
        {
            var cam = Host != null && Host.World != null ? Host.World.Camera : null;
            if (cam != null && cam.enabled != on) cam.enabled = on;
        }

        public override void OnShown()
        {
            OrientationLock.Apply(EffectiveOrientation);
            if (Running && Field != null && _phase == Phase.Playing) WorldCamera(false);
            AudioListener.volume = MixTrimDb.TryGetValue(Meta.Id, out var trim) ? Mathf.Pow(10f, trim / 20f) : 1f;
        }

        /// <summary>
        /// Master trim while a game is on top (dB, attenuation only), measured with tools/playtest/audio_sweep.sh: most rounds
        /// sit near −30 dBFS median, the ranch rides (hooves, crowd, beds) and a few busy arcade games came out 3–7 dB
        /// hotter — a jump you hear when going from one game to the next.
        /// </summary>
        private static readonly Dictionary<string, float> MixTrimDb = new Dictionary<string, float>
        {
            ["ranchParcours"] = -5f, ["ranchTonnen"] = -4f, ["ranchTurnier"] = -4f, ["ranchZeit"] = -3.5f, ["ranchHerde"] = -3.5f,
            ["shoppingSurf"] = -3.5f, ["snailMail"] = -2.5f, ["hideSeek"] = -2.5f, ["goobySays"] = -2f,
        };

        public override string SoundScene => "mg/" + Meta.Id;

        /// <summary>Settings choice (auto/landscape/portrait) over the game's registry orientation.</summary>
        public string EffectiveOrientation => FrameworkLogic.EffectiveOrientation(Host?.Settings?.MinigameOrientation, Meta.Orientation);

        public override void OnHidden()
        {
            WorldCamera(true);
            var ui = Ui;
            if (ui != null && ui.IsMounted(this))
            {
                // covered by a sheet (UiRoot keeps updating covered screens): the round must not run on unseen — it waits
                // in the pause modal until the player is back
                if (_phase == Phase.Playing && !_paused) PauseRound(false);
                return;
            }
            OrientationLock.AppDefault();
            AudioListener.volume = 1f;
            DropBuddy();
            DropActor();
            // popped (not just covered): a round that already ended is still booked — exactly once, like its results
            // card would have (a visit's PopToRoot may land inside the 0,9 s end moment); a round still running is
            // dropped like Aufgeben (nothing booked). Then nothing of the round may keep listening or animating.
            if (_phase == Phase.Ending && Running) BookRound();
            else if (Running) DiscardRound();
            _phase = Phase.Pregame;
            _paused = false;
            _pauseModal = null;
            _cd = Cd.None;
            _cdHideAt = -1.0;
            _endMomentPending = false;
            UnhookFocus();
            _timeline.Clear();
            Fx.Clear();
        }

        /// <summary>
        /// Drops the running round without booking it (Neustart, Aufgeben, a pop mid-round): TakeFinalScore under
        /// <see cref="DroppingRound"/> runs each host's cleanup (views, pills, caches) exactly like before a "Nochmal".
        /// </summary>
        private void DiscardRound()
        {
            if (!Running) return;
            DroppingRound = true;
            try { TakeFinalScore(); }
            catch (Exception e) { Debug.LogWarning("[mg] " + Id + ": cleanup of a dropped round failed: " + e.Message); }
            finally { DroppingRound = false; }
            if (Running) DropGame();
            DropActor();
            RefundModifier();
            GeistDrop();
        }

        /// <summary>A dropped round gives its bonus-event round back — at most once per event (the engine's anti-farming).</summary>
        private void RefundModifier()
        {
            var snap = _modSnapshot;
            _modSnapshot = null;
            _modParams = new JObject();
            if (snap == null || Host?.Store == null) return;
            var refunded = false;
            var nowMs = Host.Store.Clock.NowMs();
            Host.Store.Update(s => refunded = ModifierEngine.Refund(s, snap, nowMs), GameHost.TicksMs);
            Debug.Log("[modifier] dropped round of " + Meta.Id + (refunded ? " refunded" : " not refunded (already once)"));
        }

        private void BuildFrame(VisualElement root)
        {
            root.Clear();
            _timeline.Clear();
            WorldCamera(false);
            // portrait play field centred on the landscape screen; the side bands are the board-06 "Tisch" around it: the
            // game's backdrop colour as a soft vertical gradient with big pastel dots, a cream frame + drop shadow hugging
            // the column, and two side cards (title + tip left, best + stars right) — all pointer-transparent
            var backdrop = Ux.El();
            Ux.Abs(backdrop, 0, 0, 0, 0);
            backdrop.style.backgroundColor = BackdropColor;
            root.Add(backdrop);
            _bands = null;
            _bandsFor = Vector2.zero;
            Field = new VisualElement { pickingMode = PickingMode.Position, name = "play-field" };
            Field.style.position = Position.Absolute;
            Field.style.backgroundColor = FieldColor;
            Field.style.overflow = Overflow.Hidden;
            Field.RegisterCallback<GeometryChangedEvent>(_ => { FieldW = Field.resolvedStyle.width; FieldH = Field.resolvedStyle.height; });
            root.Add(Field);
            LayoutField(root);
            if (!ReferenceEquals(_layoutRoot, root))
            {
                _layoutRoot = root;
                root.RegisterCallback<GeometryChangedEvent>(_ => LayoutField(root));
            }
            Fx.ApplyShake = off => { if (Field != null) Field.style.translate = new Translate(off.x, off.y); };

            // HUD: the player's Gooby (round badge, like the profile circle of board 08), time + score + streak (concept pills)
            var hud = Ux.Row();
            hud.style.alignItems = Align.Center;
            Ux.Abs(hud, 12, 12);
            _buddyBadge = _buddyBadgeView = null;
            if (BuddyInstance() != null)
            {
                _buddyBadge = Ux.El("gb-mg-buddy-badge");
                _buddyBadge.name = "mg-buddy-badge";
                _buddyBadge.pickingMode = PickingMode.Ignore;
                _buddyBadgeView = Ux.El("gb-mg-buddy-badge-view");
                _buddyBadgeView.pickingMode = PickingMode.Ignore;
                _buddyBadge.Add(_buddyBadgeView);
                _buddyBadge.style.display = DisplayStyle.None;
                hud.Add(_buddyBadge);
            }
            // the host's first render writes the real clock (or its own count) — no placeholder text of the frame's own
            var timePill = Ux.Pill("sun", "", out TimeLabel);
            timePill.style.marginRight = 10;
            hud.Add(timePill);
            _scorePill = Ux.Pill("star", "0", out ScoreLabel);
            _scorePill.name = ElementNames.ScorePill;
            hud.Add(_scorePill);
            BuildGeistChip(hud);
            root.Add(hud);
            ComboLabel = Ux.Text("", "gb-h3", "gb-combo");
            ComboLabel.pickingMode = PickingMode.Ignore;
            Ux.Abs(ComboLabel, null, 70);
            ComboLabel.style.left = Length.Percent(50);
            ComboLabel.style.translate = new Translate(Length.Percent(-50), 0);
            root.Add(ComboLabel);
            _bannerLabel = Ux.Text("", "gb-h2");
            _bannerCard = Ux.El("gb-card", "gb-banner");
            Ux.Abs(_bannerCard, null, 120);
            _bannerCard.style.left = Length.Percent(50);
            _bannerCard.style.translate = new Translate(Length.Percent(-50), 0);
            _bannerCard.pickingMode = PickingMode.Ignore;
            _bannerLabel.pickingMode = PickingMode.Ignore;
            _bannerCard.Add(_bannerLabel);
            _bannerOn = false;
            root.Add(_bannerCard);
            // top right: pause and the quit X — both open the pause modal (Aufgeben there leaves the round)
            _quitBtn = Ux.IconBtn("close", OpenPause, "gb-btn-icon-sm");
            _quitBtn.name = ElementNames.Quit;
            Ux.Abs(_quitBtn, null, 12, 12);
            root.Add(_quitBtn);
            _pauseBtn = PauseButton();
            Ux.Abs(_pauseBtn, null, 12, 12 + 52 + 10);
            root.Add(_pauseBtn);

            _countdown = Ux.Text("", "gb-countdown");
            _countdown.name = ElementNames.Countdown;
            _stamp = Ux.Text("", "gb-stamp");
            _stamp.name = ElementNames.Stamp;
            foreach (var big in new[] { _countdown, _stamp })
            {
                big.pickingMode = PickingMode.Ignore;
                big.style.position = Position.Absolute;
                big.style.left = Length.Percent(50);
                big.style.width = 700;
                big.style.marginLeft = -350;
                big.style.unityTextAlign = TextAnchor.MiddleCenter;
                big.style.display = DisplayStyle.None;
            }
            _countdown.style.top = Length.Percent(30);
            _stamp.style.top = Length.Percent(34);
        }

        private VisualElement _layoutRoot;
        private VisualElement _bands;
        private Vector2 _bandsFor;

        /// <summary>
        /// Places the play field for the screen it is on: the engine's view fitted into the screen root. A screen of about
        /// the engine's shape (the phone held — or turned by <see cref="OrientationLock"/> — for this game) is covered edge
        /// to edge; a much wider screen gets the column in the middle with the board-06 bands beside it. Re-runs when the
        /// screen turns or resizes.
        /// </summary>
        private void LayoutField(VisualElement root)
        {
            if (Field == null || Field.parent != root) return;
            var size = ScreenSize(root);
            var aspect = ViewSize.w / ViewSize.h;
            var rect = FieldRect(size.x, size.y, aspect);
            // the hosts lay out in a fixed design box (the column of the reference landscape screen) and the frame scales
            // it to the screen: FieldW/FieldH, taps and engine mapping stay the same in every orientation
            var design = DesignSize(aspect);
            var s = rect.width / design.x;
            Field.style.width = design.x;
            Field.style.height = design.y;
            Field.style.left = rect.x;
            Field.style.top = rect.y;
            Field.style.transformOrigin = new TransformOrigin(0, 0);
            Field.style.scale = new Scale(new Vector3(s, s, 1f));
            if ((size - _bandsFor).sqrMagnitude <= 4f) return;
            _bands?.RemoveFromHierarchy();
            if (_buddyBand != null) _buddy?.Unbind(_buddyBand);
            _buddyBand = null;
            _buddyBandOk = false;
            _bands = BuildSideBands(root, size, rect);
            _bandsFor = size;
            if (_bands != null) _bands.PlaceBehind(Field);
            if (_phase == Phase.Playing) BindBuddyForPlay();
        }

        /// <summary>The field's layout box: full reference height (portrait engines) or full reference width (wide ones).</summary>
        public static Vector2 DesignSize(double viewAspect)
        {
            var a = (float)viewAspect;
            var w = UiRoot.ReferenceHeight * a;
            return w <= UiRoot.ReferenceWidth ? new Vector2(w, UiRoot.ReferenceHeight) : new Vector2(UiRoot.ReferenceWidth, UiRoot.ReferenceWidth / a);
        }

        private static Vector2 ScreenSize(VisualElement root)
        {
            var w = root.resolvedStyle.width;
            var h = root.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 1f || float.IsNaN(h) || h <= 1f) return new Vector2(UiRoot.ReferenceWidth, UiRoot.ReferenceHeight);
            return new Vector2(w, h);
        }

        /// <summary>
        /// The field rectangle inside a screen of <paramref name="screenW"/>×<paramref name="screenH"/>: within 15 % of the
        /// engine's aspect the field covers the screen (the overhang is the notch/home-bar margin outside the safe area,
        /// which the engines' full-phone views include); otherwise it is contained and centred.
        /// </summary>
        public static Rect FieldRect(float screenW, float screenH, double viewAspect)
        {
            var a = (float)viewAspect;
            var screenAspect = screenW / screenH;
            var cover = Mathf.Abs(screenAspect / a - 1f) <= 0.15f;
            var byHeight = cover ? screenAspect < a : screenAspect > a;
            var w = byHeight ? screenH * a : screenW;
            var h = byHeight ? screenH : screenW / a;
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        /// <summary>
        /// Board-06 dressing of the bands beside a column field: gradient + dots in the game's backdrop colour, a cream
        /// frame with shadow around the column, and two side cards. A field that fills the screen leaves no band — then
        /// nothing is built. Everything ignores the pointer; the field and HUD sit on top.
        /// </summary>
        private VisualElement BuildSideBands(VisualElement root, Vector2 screen, Rect fieldRect)
        {
            var fw = fieldRect.width;
            var band = fieldRect.x;
            if (band < 120f) return null;
            var baseCol = BackdropColor;
            Color.RGBToHSV(baseCol, out var h, out var s, out var v);
            var dark = v < 0.35f;
            var top = Color.HSVToRGB(h, s * 0.7f, Mathf.Min(1f, v * 1.12f + 0.04f));
            var bottom = Color.HSVToRGB(h, Mathf.Min(1f, s * 1.08f), v * 0.86f);

            var bands = new VisualElement { name = "mg-bands", pickingMode = PickingMode.Ignore };
            bands.AddToClassList("gb-mg-bands");
            Ux.Abs(bands, 0, 0, 0, 0);
            root.Add(bands);
            var grad = MgScenery.Gradient(bands, top, bottom);
            grad.name = "mg-bands-gradient";
            // big soft dots, mirrored left/right (fixed layout — no RNG, the same every round)
            var dot = dark ? new Color(1f, 1f, 1f, 0.06f) : new Color(1f, 1f, 1f, 0.16f);
            var spots = new[] { (0.12f, 0.16f, 120f), (0.62f, 0.08f, 64f), (0.30f, 0.52f, 180f), (0.78f, 0.62f, 90f), (0.08f, 0.86f, 110f), (0.55f, 0.92f, 150f) };
            foreach (var side in new[] { 0, 1 })
                foreach (var (x, y, d) in spots)
                {
                    var e = MgScenery.Shape(bands, dot, d * 0.5f, "gb-mg-band-dot");
                    MgScenery.Size(e, d, d);
                    var bx = x * band - d * 0.5f;
                    e.style.left = side == 0 ? bx : screen.x - band + (band - bx - d);
                    e.style.top = y * screen.y - d * 0.5f;
                }

            // cream frame + shadow hugging the column (drawn under the field, 10 px wider on each side)
            var shadow = MgScenery.Shape(bands, new Color(0.16f, 0.10f, 0.06f, dark ? 0.35f : 0.16f), 30f, "gb-mg-frame-shadow");
            shadow.style.left = band - 16f; shadow.style.width = fw + 32f; shadow.style.top = 6f; shadow.style.bottom = -26f;
            var frame = MgScenery.Shape(bands, new Color(1f, 0.973f, 0.925f), 26f, "gb-mg-frame");
            frame.style.left = band - 10f; frame.style.width = fw + 20f; frame.style.top = -12f; frame.style.bottom = -12f;
            MgScenery.Outline(frame, 3f, new Color(0.42f, 0.31f, 0.23f, 0.14f));

            // side cards: left = icon, title, tip; right = best of this mode + the star goal
            if (band < 300f) return bands;
            var t = Host.Strings;
            var cardW = Mathf.Min(300f, band - 96f);
            var left = SideCard(bands, "mg-side-left", band * 0.5f - cardW * 0.5f, cardW, dark);
            var head = Ux.Row("gb-mg-side-head");
            head.pickingMode = PickingMode.Ignore;
            var tile = Ux.IconTile(GameIcon(Meta.Id), GameTint(Meta.Id));
            tile.pickingMode = PickingMode.Ignore;
            head.Add(tile);
            var title = Ux.Text(t.T(Meta.TitleKey), "gb-mg-side-title");
            title.pickingMode = PickingMode.Ignore;
            head.Add(title);
            left.Add(head);
            var tip = Ux.Text(HintText(), "gb-mg-side-tip");
            tip.pickingMode = PickingMode.Ignore;
            left.Add(tip);
            // the player's Gooby stands on the left card and cheers the round on (sized to the room above the card)
            if (BuddyInstance() != null)
            {
                var gooby = new VisualElement { name = "mg-buddy-band", pickingMode = PickingMode.Ignore };
                gooby.AddToClassList("gb-mg-buddy-band");
                gooby.style.position = Position.Absolute;
                var gw = Mathf.Min(cardW, 280f);
                gooby.style.width = gw;
                gooby.style.left = band * 0.5f - gw * 0.5f;
                bands.Add(gooby);
                _buddyBand = gooby;
                _buddyBandOk = false;
                void Fit()
                {
                    var cardH = left.resolvedStyle.height;
                    if (float.IsNaN(cardH) || cardH <= 0f) cardH = 150f;
                    var bottom = 44f + cardH - 12f;
                    var h = Mathf.Clamp(screen.y - bottom - 96f, 0f, gw * 1.3f);
                    gooby.style.bottom = bottom;
                    gooby.style.height = h;
                    var ok = h >= 140f;
                    gooby.style.display = ok ? DisplayStyle.Flex : DisplayStyle.None;
                    var changed = ok != _buddyBandOk;
                    _buddyBandOk = ok;
                    if (_buddy != null && _buddy.BoundTo(gooby) && ok) _buddy.Bind(gooby, World.MgBuddy3D.Framing.Full, gw / h);
                    else if (changed && _phase == Phase.Playing && _buddyBand == gooby) BindBuddyForPlay();
                }
                Fit();
                left.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            }

            var right = SideCard(bands, "mg-side-right", screen.x - band * 0.5f - cardW * 0.5f, cardW, dark);
            var best = FrameworkLogic.BestForMode(Host.Store.State, Meta.Id, Difficulty);
            var bestRow = Ux.Row("gb-mg-side-best");
            bestRow.pickingMode = PickingMode.Ignore;
            var star = Ux.Icon("star", "gb-mg-side-star");
            star.pickingMode = PickingMode.Ignore;
            bestRow.Add(star);
            var bestLabel = Ux.Text(t.T("mg.pregame.best", ("best", best)), "gb-mg-side-best-text");
            bestLabel.pickingMode = PickingMode.Ignore;
            bestRow.Add(bestLabel);
            right.Add(bestRow);
            var goal = Ux.Text(Meta.Target > 0 ? t.T("mgp.side.goal", ("ziel", Meta.Target)) : t.T("mgp.side.endless"), "gb-mg-side-tip");
            goal.pickingMode = PickingMode.Ignore;
            right.Add(goal);
            return bands;
        }

        private static VisualElement SideCard(VisualElement parent, string name, float left, float width, bool dark)
        {
            var card = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            card.AddToClassList("gb-mg-side");
            if (dark) card.AddToClassList("gb-mg-side--dark");
            card.style.position = Position.Absolute;
            card.style.left = left;
            card.style.width = width;
            card.style.bottom = 44f;
            parent.Add(card);
            return card;
        }

        private Button PauseButton()
        {
            var b = new Button(OpenPause) { text = "", name = ElementNames.Pause };
            b.ClearClassList();
            b.AddToClassList("gb-btn-icon");
            b.AddToClassList("gb-btn-icon-sm");
            b.AddToClassList("gb-pause-btn");
            var bars = Ux.Row("gb-pause-bars");
            bars.pickingMode = PickingMode.Ignore;
            bars.Add(Ux.El("gb-pause-bar"));
            bars.Add(Ux.El("gb-pause-bar"));
            b.Add(bars);
            return b;
        }

        /// <summary>After the host's BuildPlayField: the juice layer, countdown and stamp over everything, the buttons on top.</summary>
        private void MountOverlay(VisualElement root)
        {
            root.Add(Fx.Layer);
            root.Add(_countdown);
            root.Add(_stamp);
            _pauseBtn.BringToFront();
            _quitBtn.BringToFront();
        }

        protected virtual Color BackdropColor => new Color(0.55f, 0.79f, 0.52f);
        protected virtual Color FieldColor => new Color(0.75f, 0.89f, 1f);

        /// <summary>The banner card under the pills: fades in, stays <paramref name="seconds"/>, fades out.</summary>
        protected void Banner(string text, double seconds)
        {
            if (_bannerLabel == null) return;
            _bannerLabel.text = text;
            _bannerUntil = Time.unscaledTimeAsDouble + seconds;
            if (!_bannerOn)
            {
                var card = _bannerCard;
                // a banner raised while the frame is still being built (the intro line) has no resolved style yet — the
                // class change would just appear: it fades in a frame later instead
                var w = card.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 0f) At(0.0, () => { if (_bannerOn && _bannerCard == card) card.AddToClassList("gb-banner--on"); });
                else card.AddToClassList("gb-banner--on");
            }
            else Fx.ScalePop(_bannerCard, 1.05f, 0.2f);
            _bannerOn = true;
        }

        private void HideBanner()
        {
            _bannerOn = false;
            _bannerCard?.RemoveFromClassList("gb-banner--on");
        }

        /// <summary>Longest frame the round follows in real time; a hitch beyond it slows the game instead of teleporting it.</summary>
        public const double MaxFrameSec = 0.25;
        /// <summary>… integrated in slices of at most this (the engines are calibrated for 30–60 Hz steps).</summary>
        public const double SubStepSec = 1.0 / 30.0;
        /// <summary>Breath between the engine's end and the results card, filled by the end moment (Godot 0,9 s).</summary>
        public const double EndMomentSec = 0.9;
        /// <summary>… under Reduced Motion: no pop, no confetti — the stamp stands still just long enough to be read.</summary>
        public const double EndMomentReducedSec = 0.5;

        public override void Update()
        {
            // the frame's own clocks (juice, countdown steps) follow real time with the round's frame cap — a 0,1 s cap
            // turned the 0,9 s countdown into 4,5 s on the 2-fps QA player
            var dt = Mathf.Min(Time.unscaledDeltaTime, (float)MaxFrameSec);
            Fx.Tick(dt);
            _buddy?.Tick(dt);
            _actor?.Tick(dt);
            RunTimeline();
            if (!Running || Field == null) return;
            if (_phase == Phase.Ending)
            {
                var now = Time.unscaledTimeAsDouble;
                if (_endMomentPending)
                {
                    // outside the engine's Tick now: one last render puts the final clock/score on the pills
                    _endMomentPending = false;
                    if (FieldW > 0f && FieldH > 0f) RenderGame();
                    PlayEndMoment();
                    _endAt = now + (Juice.ReducedMotion ? EndMomentReducedSec : EndMomentSec);
                }
                else if (now >= _endAt) ShowResults(Root);
                return;
            }
            if (_phase != Phase.Playing || _paused) return;
            var held = TickCountdown(dt);
            var gameSec = 0.0;
            if (!held)
            {
                // a slow frame (a low-end phone, the software-rendered QA player) advances the round by its real length
                // in small steps, so bubbles and carrots keep their pace instead of the whole game running in slow motion
                var left = Math.Min(MaxFrameSec, Time.unscaledDeltaTime);
                var frame = left;
                // inside the engine's intro lock the round has not started yet: the ghost's t = 0 is the GO
                var afterGo = _cd == Cd.None;
                var bot = QaBot ? BotFor(GetType()) : null;
                while (left > 1e-6 && Running && _phase == Phase.Playing)
                {
                    var h = Math.Min(SubStepSec, left);
                    bot?.Invoke(this);
                    TickGame(h);
                    left -= h;
                }
                if (afterGo) gameSec = frame - Math.Max(0.0, left);
            }
            if (!Running || _phase != Phase.Playing) return;
            if (FieldW > 0f && FieldH > 0f)
            {
                RenderGame();
                SyncActor(dt);
                WatchScore();
                WatchCombo();
            }
            GeistTick(gameSec);
            if (_bannerOn && Time.unscaledTimeAsDouble > _bannerUntil) HideBanner();
        }

        // ── countdown (3-2-1-LOS, minigame_host.gd _run_countdown) ──────────

        private enum Cd { None, Intro, Hold, Resume }
        private Cd _cd;
        private float _cdStep, _cdClock;
        private int _cdShown;
        private double _cdHideAt = -1.0;
        private const float HoldStepSec = 0.3f, ResumeStepSec = 0.35f;

        private void StartCountdown(bool quick)
        {
            _cdShown = 99;
            _cdClock = 0f;
            _cdHideAt = -1.0;
            var intro = SafeIntroLeft();
            if (!double.IsNaN(intro) && intro > 0.05)
            {
                // inside the engine's own intro lock: 3 × step, LOS exactly when the lock opens (no extra wait)
                _cd = Cd.Intro;
                _cdStep = Mathf.Clamp((float)intro / 3f, 0.3f, 0.6f);
            }
            else if (quick)
            {
                _cd = Cd.None;
                ShowGo();
            }
            else
            {
                // an engine without an intro: the frame holds it for 3 × 0,3 s (≤ 1,2 s) — never on a quick retry
                _cd = Cd.Hold;
                _cdStep = HoldStepSec;
            }
        }

        private void StartResumeCountdown()
        {
            if (_cd == Cd.Intro) return;   // still in the intro lock — its countdown just carries on
            _cd = Cd.Resume;
            _cdStep = ResumeStepSec;
            _cdClock = 0f;
            _cdShown = 99;
            // a pause right after LOS! left the label's hide pending: it must not fade the resume's "2" halfway
            _cdHideAt = -1.0;
        }

        /// <summary>Advances the countdown; true while the frame holds the engine (hold/resume steps).</summary>
        private bool TickCountdown(float dt)
        {
            if (_cdHideAt > 0.0 && Time.unscaledTimeAsDouble >= _cdHideAt)
            {
                _cdHideAt = -1.0;
                Fx.FadeOut(_countdown, 0.2f);
            }
            switch (_cd)
            {
                case Cd.Intro:
                {
                    var left = SafeIntroLeft();
                    if (double.IsNaN(left) || left <= 1e-6) { _cd = Cd.None; ShowGo(); return false; }
                    var digit = Mathf.CeilToInt((float)left / _cdStep - 1e-4f);
                    if (digit <= 3 && digit < _cdShown && digit >= 1) ShowDigit(digit);
                    return false;
                }
                case Cd.Hold:
                case Cd.Resume:
                {
                    _cdClock += dt;
                    var digit = 3 - Mathf.FloorToInt(_cdClock / _cdStep);
                    if (digit <= 0) { _cd = Cd.None; ShowGo(); return false; }
                    if (digit < _cdShown) ShowDigit(digit);
                    return true;
                }
                default:
                    return false;
            }
        }

        private void ShowDigit(int digit)
        {
            _cdShown = digit;
            ShowBig(_countdown, digit.ToString(CultureInfo.InvariantCulture));
            Fx.ScalePop(_countdown, 1.45f, 0.3f);
            Fx.Flash(new Color(1f, 1f, 1f, 0.1f), 0.06f);
            // Godot pitch ladder 0,90 → 1,02 → 1,14
            PlaySfx("game_countdown", digit == 3 ? -1.8f : digit == 2 ? 0.35f : 2.3f);
        }

        private void ShowGo()
        {
            _cdShown = 0;
            ShowBig(_countdown, Host.Strings.T("mg.host.go"));
            _countdown.AddToClassList("gb-countdown--go");
            Fx.ScalePop(_countdown, 1.7f, 0.38f);
            Fx.Flash(new Color(1f, 0.95f, 0.7f, 0.16f), 0.24f);
            Fx.Shake(0.15f);
            PlaySfx("game_go");
            _buddy?.Ready();
            _actor?.Ready();
            _cdHideAt = Time.unscaledTimeAsDouble + 0.6;
        }

        private void ShowBig(Label l, string text)
        {
            if (l == null) return;
            Fx.CancelFade(l);
            l.RemoveFromClassList("gb-countdown--go");
            l.text = text;
            l.style.display = DisplayStyle.Flex;
            l.style.opacity = 1f;
        }

        private double SafeIntroLeft()
        {
            try { return IntroLeft; }
            catch (Exception) { return double.NaN; }
        }

        private static readonly Dictionary<string, Func<object, double>> EngineProbes = new Dictionary<string, Func<object, double>>();

        /// <summary>
        /// A number off the running engine — a public int/long/float/double property of the host's <c>_game</c> field (or
        /// of the first other field that has one), found once per host type. NaN without an engine or that property.
        /// </summary>
        protected double EngineNumber(string property)
        {
            var type = GetType();
            var key = type.FullName + "/" + property;
            if (!EngineProbes.TryGetValue(key, out var probe)) EngineProbes[key] = probe = BuildEngineProbe(type, property);
            if (probe == null) return double.NaN;
            try { return probe(this); }
            catch (Exception) { return double.NaN; }
        }

        private static readonly Dictionary<Type, Action<object>> BotProbes = new Dictionary<Type, Action<object>>();

        /// <summary>
        /// QA remote (<c>mgbot on|off</c>): the engine's certified autoplay (a public parameterless <c>ApplyBot()</c> on the
        /// host's <c>_game</c>) plans every integration slice — screenshots and tours of a round in motion.
        /// </summary>
        public bool QaBot { get; set; }

        /// <summary>True when the running engine offers <c>ApplyBot()</c>.</summary>
        public bool QaBotAvailable => BotFor(GetType()) != null;

        /// <summary>
        /// QA remote (<c>fieldtap fx fy</c>): a finger at a point of the play field (fractions of the field box) — pointer down
        /// and up on the element the panel picks there, with real positions, as a touch would deliver them.
        /// </summary>
        public bool QaTapField(float fx, float fy)
        {
            if (Field == null || Field.panel == null || FieldW <= 0f || FieldH <= 0f) return false;
            var at = Field.LocalToWorld(new Vector2(fx * FieldW, fy * FieldH));
            var target = Field.panel.Pick(at) ?? Field;
            var ev = new Event { type = EventType.MouseDown, mousePosition = at, button = 0, clickCount = 1 };
            using (var down = PointerDownEvent.GetPooled(ev)) { down.target = target; target.SendEvent(down); }
            ev.type = EventType.MouseUp;
            using (var up = PointerUpEvent.GetPooled(ev)) { up.target = target; target.SendEvent(up); }
            return true;
        }

        private static Action<object> BotFor(Type type)
        {
            if (BotProbes.TryGetValue(type, out var bot)) return bot;
            const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            var field = type.GetField("_game", Fields);
            var method = field?.FieldType.GetMethod("ApplyBot", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (field != null && method != null) bot = host => { if (field.GetValue(host) is object g) method.Invoke(g, null); };
            BotProbes[type] = bot;
            return bot;
        }

        private static Func<object, double> BuildEngineProbe(Type type, string property)
        {
            try
            {
                const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
                var candidates = new List<FieldInfo>();
                var named = type.GetField("_game", Fields);
                if (named != null) candidates.Add(named);
                foreach (var f in type.GetFields(Fields)) if (f != named) candidates.Add(f);
                foreach (var f in candidates)
                {
                    var ft = f.FieldType;
                    if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;
                    var p = ft.GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
                    if (p == null || !p.CanRead || p.GetIndexParameters().Length != 0) continue;
                    var pt = p.PropertyType;
                    if (pt != typeof(double) && pt != typeof(float) && pt != typeof(int) && pt != typeof(long)) continue;
                    var field = f;
                    return host => field.GetValue(host) is object g ? Convert.ToDouble(p.GetValue(g), CultureInfo.InvariantCulture) : double.NaN;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[mg] engine probe " + property + " failed for " + type.Name + ": " + e.Message);
            }
            return null;
        }

        // ── pause (minigame_host.gd _on_pause_pressed / pause modal) ────────

        private float _unmuteVolume = 1f;

        /// <summary>
        /// Opens the pause modal (pause button, the quit X, focus loss, a sheet over the host). While it is open neither
        /// TickGame nor the countdown advance; the end moment and results never pause (their buttons are gone/disabled).
        /// </summary>
        protected void OpenPause() => PauseRound(true);

        private void PauseRound(bool withSound)
        {
            if (!Running || _phase != Phase.Playing || _paused || Root == null) return;
            _paused = true;
            if (withSound) Sfx.PlayUi("soft_open");
            _pauseModal = BuildPauseModal();
            Root.Add(_pauseModal);
        }

        private VisualElement BuildPauseModal()
        {
            var t = Host.Strings;
            var wrap = new VisualElement { name = ElementNames.PauseModal, pickingMode = PickingMode.Position };
            Ux.Abs(wrap, 0, 0, 0, 0);
            var scrim = Ux.El("gb-scrim");
            wrap.Add(scrim);
            // the same window as the pregame: title band (game tile, "Pause", the game), then big pills stacked
            var card = Ux.El("gb-mgw", "gb-pause-card");
            Ux.Abs(card, null, null);
            card.style.left = Length.Percent(50); card.style.top = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            card.style.width = 460;
            PopIn(card, 0.0);
            card.Add(FrameBand(GameTile(Meta.Id, 56f), t.T("mg.host.paused"), t.T(Meta.TitleKey), BandWash(Meta.Id)));

            var help = Ux.Text(HintText(), "gb-md", "gb-pause-help", "gb-mgw-help");
            help.name = ElementNames.HelpText;
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.display = DisplayStyle.None;

            var col = Ux.Col("gb-mgw-body");
            // Weiter: the big pink pill with the white play triangle
            var resume = FramePill("play", t.T("mg.host.resume"), Resume, false, "gb-btn-primary", "gb-mgw-pill--big");
            resume.name = ElementNames.Resume;
            col.Add(resume);
            var restart = FramePill("redo", t.T("mg.host.restart"), RestartFromPause, false, "gb-mgw-sand");
            restart.name = ElementNames.Restart;
            col.Add(restart);
            var row = Ux.Row();
            Button sound = null;
            sound = FramePill("speaker", SoundLabel(), () => ToggleSound(sound), false, "gb-mgw-sand", "gb-mgw-pill--half");
            sound.name = ElementNames.Sound;
            sound.style.marginRight = 10;
            row.Add(sound);
            var helpBtn = FramePill("bulb", t.T("mg.host.help"), () =>
            {
                var show = help.style.display == DisplayStyle.None;
                help.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                Sfx.PlayUi(show ? "soft_open" : "soft_close");
            }, false, "gb-mgw-sand", "gb-mgw-pill--half");
            helpBtn.name = ElementNames.Help;
            row.Add(helpBtn);
            col.Add(row);
            col.Add(help);
            _hintLabelBox = SleepyHint();
            col.Add(_hintLabelBox);
            // Aufgeben: the taupe "Abbrechen" pill of board 06
            var quit = FramePill("home", t.T("mgp.pause.give_up"), GiveUp, false, "gb-mgw-taupe");
            quit.name = ElementNames.GiveUp;
            col.Add(quit);
            var first = true;
            foreach (var child in col.Children())
            {
                child.style.marginTop = first ? 0 : 10;
                first = false;
            }
            card.Add(col);
            wrap.Add(card);
            return wrap;
        }

        private string SoundLabel() => Host.Strings.T(Host.Settings == null || Host.Settings.SfxVolume > 0.001 ? "mg.host.sound_on" : "mg.host.sound_off");

        /// <summary>
        /// Ton An/Aus = the sound-effects volume of the settings (0 mutes every Sfx call and cuts the voices still
        /// ringing; unmuting restores the old level and decodes the frame's clips the muted pregame skipped).
        /// </summary>
        private void ToggleSound(Button b)
        {
            var s = Host.Settings;
            if (s == null) return;
            if (s.SfxVolume > 0.001)
            {
                _unmuteVolume = (float)s.SfxVolume;
                s.SetSfxVolume(0.0);
                Sfx.StopAll();
            }
            else
            {
                s.SetSfxVolume(_unmuteVolume > 0.001f ? _unmuteVolume : 1.0);
                Sfx.Preload(FrameSounds);
                Sfx.PlayUi("soft_toggle");
            }
            var label = b?.Q<Label>();
            if (label != null) label.text = SoundLabel();
        }

        private void ClosePauseModal()
        {
            _pauseModal?.RemoveFromHierarchy();
            _pauseModal = null;
            _paused = false;
        }

        private void Resume()
        {
            if (!_paused) return;
            ClosePauseModal();
            Sfx.PlayUi("soft_close");
            if (_phase == Phase.Playing) StartResumeCountdown();
        }

        /// <summary>
        /// Neustart: the running round is dropped (no MinigameAward, no album finds — <see cref="DroppingRound"/>) and a
        /// fresh one of the same difficulty starts with the quick countdown. Too tired → the sleepy hint, the round waits.
        /// </summary>
        private void RestartFromPause()
        {
            if (_phase != Phase.Playing || !Running) return;
            if (IsExhausted()) { ShowSleepyHint(); return; }
            Sfx.PlayUi("soft_tap");
            ClosePauseModal();
            DiscardRound();
            BeginRound(Root, true, null);
        }

        /// <summary>
        /// Aufgeben: today's quit — the round is dropped (nothing booked) and the host closes to what lies underneath
        /// (the arcade, or the HUD when the game was opened straight over it).
        /// </summary>
        private void GiveUp()
        {
            if (_phase != Phase.Playing) return;
            Sfx.PlayUi("soft_back");
            ClosePauseModal();
            UnhookFocus();
            _timeline.Clear();
            Fx.Clear();
            DiscardRound();
            _phase = Phase.Pregame;
            _cd = Cd.None;
            WorldCamera(true);
            // no stack to leave (no UI, a sheet on top): the pregame again rather than a frame without a round
            if (!Leave()) ShowPregame(Root);
        }

        private void HookFocus()
        {
            if (_focusHooked) return;
            Application.focusChanged += OnFocusChanged;
            _focusHooked = true;
        }

        private void UnhookFocus()
        {
            if (!_focusHooked) return;
            Application.focusChanged -= OnFocusChanged;
            _focusHooked = false;
        }

        /// <summary>Focus lost (notification centre, app switch): the round waits in the pause modal — silently, the app is away.</summary>
        private void OnFocusChanged(bool focused)
        {
            if (!focused && AutoPauseOnFocusLoss && Root != null) PauseRound(false);
        }

        // ── automatic score / streak feedback (StageChrome.score_float, JuiceKit.show_combo) ──

        private ScoreFeel _feel;
        private bool _scoreKnown, _comboKnown;
        private string _lastScoreText, _lastComboText;
        private int _lastScore, _lastComboN, _pendingGain, _pendingLoss, _gainStreak, _changeIdx, _changeCount;
        private double _lastFxAt, _lastGainAt;
        private readonly double[] _changeTimes = new double[8];

        private static readonly Color GainColor = new Color(0.24f, 0.6f, 0.36f);
        private static readonly Color LossColor = new Color(0.9f, 0.33f, 0.33f);
        private static readonly Color MilestoneColor = new Color(1f, 0.7f, 0.2f);

        private void ResetWatchers()
        {
            _feel = ScoreFeedback;
            _scoreKnown = _comboKnown = false;
            _lastScoreText = _lastComboText = null;
            _lastScore = _lastComboN = _pendingGain = _pendingLoss = _gainStreak = _changeIdx = _changeCount = 0;
            _lastFxAt = _lastGainAt = -10.0;
        }

        private void WatchScore()
        {
            if (ScoreLabel == null) return;
            var text = ScoreLabel.text;
            if (text == _lastScoreText) { FlushScore(); return; }
            _lastScoreText = text;
            if (!TryParseScore(text, out var v)) return;
            if (!_scoreKnown) { _scoreKnown = true; _lastScore = v; return; }
            var prev = _lastScore;
            var d = v - prev;
            _lastScore = v;
            if (d == 0) return;
            GeistScoreFlowed();
            if (_feel == ScoreFeel.Events || _feel == ScoreFeel.GainsOnly)
            {
                // safety net for a score that ticks every few frames (distance, time): 8 small changes inside 1,2 s
                var now = Time.unscaledTimeAsDouble;
                var oldest = _changeTimes[_changeIdx];
                _changeTimes[_changeIdx] = now;
                _changeIdx = (_changeIdx + 1) % _changeTimes.Length;
                _changeCount++;
                if (_changeCount > _changeTimes.Length && Math.Abs(d) <= 2 && now - oldest < 1.2)
                {
                    _feel = ScoreFeel.Milestones;
                    _pendingGain = _pendingLoss = 0;
                    Debug.Log("[mg] " + Meta.Id + ": the score streams — milestone feedback");
                }
            }
            switch (_feel)
            {
                case ScoreFeel.Off:
                    return;
                case ScoreFeel.Milestones:
                    var step = Math.Max(1, ScoreMilestone);
                    if (d >= 3) _pendingGain += d;   // coins/pickups on top of the distance ticks
                    else if (d > 0 && v > 0 && v / step > prev / step) MilestoneFx(v / step * step, v / step);
                    break;
                case ScoreFeel.GainsOnly:
                    if (d > 0) _pendingGain += d;
                    break;
                default:
                    if (d > 0) _pendingGain += d; else _pendingLoss -= d;
                    break;
            }
            FlushScore();
        }

        /// <summary>Feedback at most every 90 ms — a burst of hits in one breath lands as one "+Δ".</summary>
        private void FlushScore()
        {
            if (_pendingGain == 0 && _pendingLoss == 0) return;
            var now = Time.unscaledTimeAsDouble;
            if (now - _lastFxAt < 0.09) return;
            _lastFxAt = now;
            if (_pendingGain > 0) { GainFx(_pendingGain); _pendingGain = 0; }
            if (_pendingLoss > 0) { LossFx(_pendingLoss); _pendingLoss = 0; }
        }

        private void GainFx(int d)
        {
            var now = Time.unscaledTimeAsDouble;
            _gainStreak = now - _lastGainAt < 1.5 ? _gainStreak + 1 : 1;
            _lastGainAt = now;
            Fx.ScalePop(_scorePill, 1.22f, 0.22f);
            PillFloat("+" + d, GainColor, 22f);
            PlaySfx(d >= 10 ? "game_coin" : "game_hit", Mathf.Min(_gainStreak - 1, 7));
            Haptics.Tap();
            _buddy?.Cheer(d, _gainStreak);
            _actor?.Cheer(d, _gainStreak);
        }

        private void LossFx(int d)
        {
            _gainStreak = 0;
            Fx.ScalePop(_scorePill, 0.86f, 0.22f);
            PillFloat("−" + d, LossColor, 22f);
            PlaySfx("game_miss");
            Fx.Shake(0.22f);
            if (d >= 5) Haptics.Warn();
            _buddy?.Wince(d >= 5);
            _actor?.Wince(d >= 5);
        }

        private void MilestoneFx(int value, int count)
        {
            Fx.ScalePop(_scorePill, 1.3f, 0.26f);
            PillFloat(value.ToString(CultureInfo.InvariantCulture) + "!", MilestoneColor, 26f);
            PlaySfx("game_combo", Sfx.ComboSemitones(count));
            _buddy?.Hype();
            _actor?.Hype();
            var r = _scorePill.worldBound;
            if (!float.IsNaN(r.x)) Fx.Burst(r.center, MilestoneColor, 10);
        }

        private void PillFloat(string text, Color color, float size)
        {
            if (_scorePill == null) return;
            var r = _scorePill.worldBound;
            if (float.IsNaN(r.x) || r.width <= 0f) return;
            Fx.FloatText(new Vector2(r.center.x, r.yMax + 16f), text, color, size, -16f);
        }

        private void WatchCombo()
        {
            if (ComboLabel == null) return;
            var text = ComboLabel.text ?? "";
            if (text == _lastComboText) return;
            _lastComboText = text;
            var n = FirstInt(text);
            if (!_comboKnown) { _comboKnown = true; _lastComboN = Math.Max(0, n); return; }
            if (text.Length == 0)
            {
                _lastComboN = 0;
                ComboLabel.style.fontSize = StyleKeyword.Null;
                return;
            }
            Fx.ScalePop(ComboLabel, 1.3f, 0.2f);
            if (n >= 1 && n > _lastComboN)
            {
                // the dopamine lever: +1 semitone per streak step, capped at an octave; the line grows with the streak
                PlaySfx("game_combo", Sfx.ComboSemitones(n));
                ComboLabel.style.fontSize = 20f + Mathf.Min(n, 8) * 2f;
                if (n % 5 == 0) { _buddy?.Hype(); _actor?.Hype(); }
            }
            else if (n < 0) PlaySfx("game_pop");
            _lastComboN = Math.Max(0, n);
        }

        /// <summary>The hosts write <c>int.ToString()</c> (current culture — its minus sign may not be "-").</summary>
        private static bool TryParseScore(string text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static int FirstInt(string s)
        {
            var v = -1;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c >= '0' && c <= '9') { v = (v < 0 ? 0 : v * 10) + (c - '0'); if (v > 99999) return v; }
                else if (v >= 0) return v;
            }
            return v;
        }

        // ── helpers for the hosts ───────────────────────────────────────────

        /// <summary>Play-field position → panel position (for the overlay helpers).</summary>
        protected Vector2 FieldToPanel(Vector2 fieldPos) => Field != null && Field.panel != null ? Field.LocalToWorld(fieldPos) : fieldPos;

        /// <summary>The shared float text inside the play field (clipped by it, shaking with it) at a field position.</summary>
        protected void Float(Vector2 fieldPos, string text, Color color, float size = 30f)
        {
            if (Field == null || FieldW <= 0f) return;
            // keep the centred text inside the field (it clips at the edges)
            var x = FieldW > 140f ? Mathf.Clamp(fieldPos.x, 70f, FieldW - 70f) : fieldPos.x;
            Fx.FloatTextIn(Field, new Vector2(x, fieldPos.y), text, color, size);
        }

        protected void Shake(float trauma) => Fx.Shake(trauma);
        protected void Burst(Vector2 fieldPos, Color color, int n = 14) => Fx.Burst(FieldToPanel(fieldPos), color, n);
        protected void ConfettiRain(int n = 70) => Fx.Confetti(n);
        protected void ScalePop(VisualElement e, float amount = 1.18f) => Fx.ScalePop(e, amount);
        protected void Flash(Color color, float seconds = 0.11f) => Fx.Flash(color, seconds);
        protected static void PlaySfx(string id, float semitones = 0f, float volume = 1f) => Sfx.Play(id, semitones, volume);

        // ── end moment (minigame_host.gd _schedule_end_moment / juice win_moment) ──

        /// <summary>
        /// The score the round ends with, read before TakeFinalScore: the engine's <c>FinalScore</c>, else the score pill
        /// (after the last render), else unknown (null).
        /// </summary>
        private int? EndScore()
        {
            var final = EngineNumber("FinalScore");
            if (!double.IsNaN(final)) return (int)Math.Round(final);
            return ScoreLabel != null && TryParseScore(ScoreLabel.text, out var v) ? v : (int?)null;
        }

        /// <summary>
        /// The stamp over the frozen field (Zeit um! when the clock ran out, Geschafft! with points, Runde vorbei! without),
        /// confetti + gold flash + game_win for a round with points, a soft grey flash + game_lose for none (Godot: win =
        /// score &gt; 0). Reduced Motion: no pop/confetti/flash (Juice), the stamp just stands.
        /// </summary>
        private void PlayEndMoment()
        {
            var t = Host.Strings;
            // the round's leftovers go (floats, sparks, pops); a banner the engine just raised ("Alle Möhren weg!") stays
            Fx.Clear();
            _cd = Cd.None;
            _cdHideAt = -1.0;
            if (_countdown != null) _countdown.style.display = DisplayStyle.None;
            if (ComboLabel != null) Fx.FadeOut(ComboLabel, 0.2f);
            _pauseBtn?.SetEnabled(false);
            _quitBtn?.SetEnabled(false);
            var score = EndScore();
            _endScore = score;
            var win = score == null || score.Value > 0;   // unknown counts as a win: no false "Runde vorbei!" over a good round
            _buddy?.Celebrate(win, false);
            _actor?.Celebrate(win, false);
            bool timeUp;
            try { timeUp = RoundEndedByClock; }
            catch (Exception) { timeUp = false; }
            if (_stamp != null)
            {
                ShowBig(_stamp, t.T(timeUp ? "mgp.end.time_up" : win ? "mgp.end.done" : "mgp.end.over"));
                _stamp.EnableInClassList("gb-stamp--lose", !win);
                Fx.ScalePop(_stamp, 1.4f, 0.34f);
            }
            if (win)
            {
                Fx.Confetti(70);
                Fx.Flash(new Color(1f, 0.9f, 0.5f, 0.28f), 0.3f);
                PlaySfx("game_win");
                Haptics.Win();
            }
            else
            {
                Fx.Flash(new Color(0.55f, 0.6f, 0.75f, 0.16f), 0.26f);
                PlaySfx("game_lose");
            }
        }

        // ── results ─────────────────────────────────────────────────────────

        /// <summary>
        /// Books the finished round — the only place MinigameAward.Award runs (results card, or a pop during the end
        /// moment): TakeFinalScore (the host's cleanup), then one award in one store update.
        /// </summary>
        private AwardResult BookRound()
        {
            // the phase flips first: whatever happens below, this round can never be booked a second time
            _phase = Phase.Results;
            _endMomentPending = false;
            UnhookFocus();
            _timeline.Clear();
            // the end moment's confetti keeps falling over the results card; everything else of the round goes
            Fx.Clear(keepParticles: true);
            int score;
            try { score = TakeFinalScore(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                score = Math.Max(0, _endScore ?? 0);
            }
            if (Running) DropGame();
            DropActor();
            AwardResult award = null;
            // the booked round keeps its bonus event: no refund after this point
            var modifier = _modParams ?? new JObject();
            _modSnapshot = null;
            var geistSnap = GeistBeforeBooking(score);
            try
            {
                Host.Store.Update(s =>
                {
                    award = MinigameAward.Award(s, Meta, score, Difficulty, Host.Store.Clock.LocalDay(), null, modifier);
                    GeistRekord.UebernehmeRekord(s, Meta.Id, geistSnap);
                }, GameHost.TicksMs);
                Host.Store.NotifySliceChanged("minigames");
                Host.Store.NotifySliceChanged("achievements");
                ParcoursBonus(award);
                NachAward(award);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            SubmitToFriends(award?.Score ?? Math.Max(0, score), award != null ? StarsFor(award.Score, award.BeatTarget, award.NewBest) : 0);
            return award;
        }

        /// <summary>A booked round's after-play for a host (the parcours trains the farm's horse); the game is already dropped.</summary>
        protected virtual void NachAward(AwardResult award) { }

        /// <summary>A game's own row on the pregame card under the best score (the Tanzparty's "Timing anpassen"); null for none.</summary>
        protected virtual VisualElement PregameExtra() => null;

        /// <summary>The pregame window's root (a game's overlay over the card, e.g. the dance calibration).</summary>
        protected VisualElement PregameRoot => Root;

        /// <summary>The frame's pill style for a game's pregame row.</summary>
        protected static Button PregamePill(string icon, string text, Action onClick, params string[] classes) => FramePill(icon, text, onClick, false, classes);

        /// <summary>
        /// parcours_game.gd's coin bonus of the farm's jump course (or the old riding arena): the ranch parcours pays
        /// its share on top of the round's coins.
        /// </summary>
        private void ParcoursBonus(AwardResult award)
        {
            if (Meta?.Id != "ranchParcours" || award == null || award.Coins <= 0) return;
            var bonus = 0;
            Host.Store.Update(s =>
            {
                var alt = Gooby.Core.Minigames.Ranch.RanchWirtschaft.AusbauAktiv(s, "reitplatz") ? Gooby.Core.Minigames.Ranch.RanchWirtschaft.ReitplatzParcoursCoinMult : 1.0;
                var mult = Gooby.Core.Minigames.Ranch.RanchBauEffekte.ParcoursCoinMult(s, alt);
                if (mult <= 1.0) return;
                bonus = Gooby.Core.Life.Economy.Award(Gooby.Core.Json.J.EnsureDict(s, "economy"), Math.Round(award.Coins * (mult - 1.0), MidpointRounding.AwayFromZero), "ranch_bau");
            }, GameHost.TicksMs);
            if (bonus <= 0) return;
            Host.Store.NotifySliceChanged("economy");
            Host.Ui.Toast(Host.Strings.T("rbau.ui.parcours_bonus", ("n", bonus)), "race_flag", 3f);
            Debug.Log("[ranchbau] parcours-bonus +" + bonus);
        }

        // ── friends leaderboard (arcade.js, online only) ────────────────────

        private VisualElement _friendsBox;
        private int _friendsToken;

        /// <summary>
        /// Every booked round goes to the friends leaderboard (OnlineService skips silently offline); the reply fills the
        /// results card's "Freunde" block — a round that has moved on (retry, pop) ignores it via the token.
        /// </summary>
        private void SubmitToFriends(int score, int stars)
        {
            var online = Host.Online;
            if (online == null || Meta == null) return;
            var token = ++_friendsToken;
            _friendsBox = null;
            online.SubmitArcadeRound(Meta.Id, score, stars, board =>
            {
                if (token != _friendsToken || board == null) return;
                FillFriendsBoard(board);
            });
        }

        /// <summary>The (hidden) friends block under the score card; shown once a board with at least one friend arrives.</summary>
        private VisualElement FriendsBoardBlock()
        {
            var box = Ux.El("gb-mgf");
            box.name = ElementNames.FriendsBoard;
            box.style.display = DisplayStyle.None;
            _friendsBox = box;
            return box;
        }

        private void FillFriendsBoard(ArcadeBoard board)
        {
            var box = _friendsBox;
            if (box == null || box.panel == null || !board.Ok || !board.HasFriends) return;
            var t = Host.Strings;
            box.Clear();
            var head = Ux.Row("gb-mgf-head");
            head.Add(Ux.Text(t.T("online.arcade.freunde"), "gb-mgf-title"));
            head.Add(Ux.El("gb-grow"));
            if (board.Rank > 0)
            {
                var rank = Ux.Text(board.Rank == 1
                    ? t.T("online.arcade.rang_eins")
                    : t.T("online.arcade.rang", ("rang", board.Rank)), "gb-mgf-rankline");
                rank.name = ElementNames.FriendsRank;
                if (board.Rank == 1) rank.AddToClassList("gb-mgf-rankline--top");
                head.Add(rank);
            }
            box.Add(head);
            var list = Ux.Row("gb-mgf-list");
            var top = board.Top(3);
            for (var i = 0; i < top.Count; i++)
            {
                var e = top[i];
                var mine = e.Code == board.Me;
                var pill = Ux.Row("gb-mgf-pill", "gb-mgf-pill--" + (i + 1));
                pill.name = ElementNames.FriendsBoard + "-" + i;
                if (mine) pill.AddToClassList("gb-mgf-pill--me");
                pill.Add(Ux.Text((i + 1).ToString(), "gb-mgf-rank"));
                pill.Add(Ux.Text(mine ? t.T("online.arcade.du") : e.Name, "gb-mgf-name"));
                pill.Add(Ux.Text(Ux.FormatNumber(e.Score), "gb-mgf-score"));
                list.Add(pill);
            }
            box.Add(list);
            box.style.display = DisplayStyle.Flex;
            FadeIn(box, 0.0);
        }

        private void ShowResults(VisualElement root)
        {
            if (!Running || _phase != Phase.Ending || root == null) return;
            var score = _endScore;
            var award = BookRound();
            WorldCamera(true);
            root.Clear();
            root.Add(Ux.El("gb-scrim"));
            // a booking that failed (logged) still leaves the player a way on: the score and the three buttons
            root.Add(award != null ? BuildResultsCard(award) : BuildFallbackCard(Math.Max(0, score ?? 0)));
            root.Add(Fx.Layer);
            Host.Audio?.PlayStinger("stinger-results");
        }

        /// <summary>Results card without an award (booking failed): title, the score, the buttons.</summary>
        private VisualElement BuildFallbackCard(int score)
        {
            var t = Host.Strings;
            var card = ResultsCardShell(FrameworkLogic.NormalizeDifficulty(Difficulty), out var body);
            if (_buddy != null) { _buddy.Rest("happy"); _buddy.Celebrate(score > 0, false); }
            var hero = Ux.El("gb-mgw-card", "gb-mgw-hero");
            var scoreLabel = Ux.Text(t.T("mg.results.score", ("score", score)), "gb-results-score-wert", "gb-mgw-score");
            scoreLabel.name = ElementNames.ResultsScore;
            hero.Add(scoreLabel);
            var stars = StarRow(score > 0 ? 1 : 0, 44f);
            stars.name = ElementNames.ResultsStars;
            hero.Add(stars);
            body.Add(hero);
            body.Add(FriendsBoardBlock());
            _hintLabelBox = SleepyHint();
            if (IsExhausted()) _hintLabelBox.style.display = DisplayStyle.Flex;
            body.Add(_hintLabelBox);
            body.Add(ResultActions());
            return card;
        }

        /// <summary>
        /// The results window, centred, popping in: title band (game tile, "Runde vorbei!", game · difficulty) and the
        /// body the score card, the reward cards and the pills go into (<paramref name="body"/>).
        /// </summary>
        private VisualElement ResultsCardShell(string mode, out VisualElement body)
        {
            var t = Host.Strings;
            var card = Ux.El("gb-mgw", "gb-results-card");
            card.name = ElementNames.Results;
            Ux.Abs(card, null, null);
            card.style.left = Length.Percent(50); card.style.top = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            PopIn(card, 0.0);
            card.Add(FrameBand(GameTile(Meta.Id, 56f), t.T("mg.results.title"), t.T(Meta.TitleKey) + " · " + t.T("mg.diff." + mode), BandWash(Meta.Id)));
            var main = Ux.Row("gb-mgw-main");
            card.Add(main);
            var buddy = BuddyColumn("mg-buddy-results", 176f, 250f);
            if (buddy != null) main.Add(buddy);
            card.style.width = buddy != null ? 800 : 620;
            FitWindow(card, buddy);
            body = Ux.El("gb-mgw-body", "gb-grow");
            body.style.flexShrink = 1;
            main.Add(body);
            return card;
        }

        /// <summary>
        /// Nach Hause (round house button, left) · Zur Arcade (sand pill) · Nochmal (big pink pill, redo). "Zur Arcade"
        /// only with the arcade underneath — opened via <c>-gooby-open game:&lt;id&gt;</c> (or the QA driver) there is
        /// none, and "Nach Hause" is the way out then.
        /// </summary>
        private VisualElement ResultActions()
        {
            var t = Host.Strings;
            var actions = Ux.Row("gb-results-actions", "gb-mgw-actions");
            var home = Ux.IconBtn("home", () => { Sfx.PlayUi("soft_back"); GoHome(); }, "gb-mgw-round");
            home.name = ElementNames.Home;
            actions.Add(home);
            actions.Add(Ux.El("gb-grow"));
            if (ArcadeUnderneath)
            {
                var arcade = FramePill("gamepad", t.T("mg.results.back"), () => { Sfx.PlayUi("soft_back"); BackToArcade(); }, false, "gb-mgw-sand");
                arcade.name = ElementNames.Arcade;
                arcade.style.marginRight = 10;
                actions.Add(arcade);
            }
            var again = FramePill("redo", t.T("mg.results.again"), Retry, true, "gb-btn-primary", "gb-mgw-pill--big");
            again.name = ElementNames.Again;
            again.EnableInClassList(TiredClass, IsExhausted());
            actions.Add(again);
            return actions;
        }

        private VisualElement BuildResultsCard(AwardResult award)
        {
            var t = Host.Strings;
            var card = ResultsCardShell(award.Difficulty, out var body);

            // the score card: the big number counts up, the stars stamp in after it, the record sticker last
            var hero = Ux.El("gb-mgw-card", "gb-mgw-hero", "gb-results-score");
            var scoreLabel = Ux.Text(t.T("mg.results.score", ("score", 0)), "gb-results-score-wert", "gb-mgw-score");
            scoreLabel.name = ElementNames.ResultsScore;
            hero.Add(scoreLabel);
            var earned = StarsFor(award.Score, award.BeatTarget, award.NewBest);
            var stars = StarRow(0, 44f);
            stars.name = ElementNames.ResultsStars;
            hero.Add(stars);
            body.Add(hero);
            if (_buddy != null)
            {
                // a round with points: arms up as the score counts, a dance for a record or all three stars
                _buddy.Rest("happy");
                _buddy.Celebrate(award.Score > 0, award.Score > 0 && (award.NewBest || earned >= 3));
            }
            // under the stars: the friends leaderboard, filled when the server answers (hidden offline / without friends)
            body.Add(FriendsBoardBlock());
            Label record = null;
            if (award.NewBest && award.Score > 0)
            {
                record = Ux.Text(t.T("mg.results.new_best"), "gb-record-badge", "gb-mgw-record");
                record.name = ElementNames.Record;
                record.pickingMode = PickingMode.Ignore;
                record.style.opacity = 0f;
                hero.Add(record);
            }

            var rows = new List<VisualElement>();
            Label coinsLabel;
            var geistRow = GeistResultRow();
            if (geistRow != null) rows.Add(geistRow);
            if (!award.NewBest) rows.Add(ResultRow("star", t.T("mg.results.best", ("best", award.Best)), out _));
            var coinsRow = ResultRow("coin", t.T("mg.results.coins", ("coins", 0)), out coinsLabel);
            rows.Add(coinsRow);
            if (award.FirstToday) rows.Add(ResultRow("sparkle", t.T("mg.results.daily_bonus"), out _));
            if (award.DayCapReached) rows.Add(ResultRow("moon", t.T("mg.results.day_cap"), out _));
            rows.Add(ResultRow("leaf", t.T("mg.results.xp", ("xp", award.Xp)), out _));
            VisualElement levelRow = null;
            if (award.LevelsGained > 0)
            {
                levelRow = ResultRow("star", t.T("mg.results.level_up", ("coins", award.CoinsFromLevels)), out _);
                levelRow.AddToClassList("gb-results-row--hot");
                rows.Add(levelRow);
            }
            if (award.BeatTarget) rows.Add(ResultRow("check", t.T("mg.results.beat_target"), out _));
            if (award.SpotlightBonusCoins > 0) rows.Add(ResultRow("sparkle", t.T("mg.spotlight.results.bonus", ("coins", award.SpotlightBonusCoins)), out _));
            var modRow = ModifierResultRow(award);
            if (modRow != null) rows.Add(modRow);
            // the rewards as small white cards with an icon tile, two side by side
            var rowBox = Ux.El("gb-mgw-rows");
            foreach (var r in rows) rowBox.Add(r);
            body.Add(rowBox);

            _hintLabelBox = SleepyHint();
            // the round may have used the last energy: say so right away instead of on the first "Nochmal"
            if (IsExhausted()) _hintLabelBox.style.display = DisplayStyle.Flex;
            body.Add(_hintLabelBox);

            body.Add(ResultActions());

            // ── the ceremony (results.gd _starte_zeremonie): rows 50 ms apart, count-ups, stars, record ──
            const double rowStart = 0.14, rowGap = 0.05;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var at = rowStart + i * rowGap;
                FadeIn(row, at);
                if (row == coinsRow)
                {
                    var coins = award.Coins;
                    At(at + 0.05, () => Fx.CountTo(coinsLabel, 0, coins, 0.6f, v => { if (v > 0) PlaySfx("game_coin", Mathf.Min(6f, v * 6f / Math.Max(1, coins))); }, v => t.T("mg.results.coins", ("coins", v))));
                }
                if (row == levelRow)
                    At(at + 0.35, () => { Fx.ScalePop(levelRow, 1.08f, 0.3f); Sfx.PlayUi("soft_levelup"); Host.Audio?.PlayStinger("stinger-levelup"); Haptics.Success(); });
            }
            var finalScore = award.Score;
            At(0.12, () => Fx.CountTo(scoreLabel, 0, finalScore, 0.8f, v => PlaySfx("game_count", Mathf.Lerp(-2f, 5f, finalScore > 0 ? (float)v / finalScore : 1f)), v => t.T("mg.results.score", ("score", v))));
            const double starStart = 1.0, starGap = 0.2;
            for (var i = 0; i < earned && i < 3; i++)
            {
                var idx = i;
                At(starStart + i * starGap, () =>
                {
                    var star = stars.ElementAt(idx);
                    star.AddToClassList("gb-star--on");
                    Fx.ScalePop(star, 1.6f, 0.3f);
                    PlaySfx("game_star", idx * 2f);
                    var r = star.worldBound;
                    if (!float.IsNaN(r.x)) Fx.Burst(r.center, new Color(1f, 0.82f, 0.4f), 8);
                });
            }
            if (record != null)
            {
                At(starStart + earned * starGap + 0.1, () =>
                {
                    record.style.opacity = 1f;
                    Fx.ScalePop(record, 1.35f, 0.34f);
                    PlaySfx("game_record");
                    Fx.Confetti(110);
                    Haptics.Success();
                });
            }
            if (geistRow != null)
            {
                // its own small beat after the score and the stars: a pop and a bright tone, no second confetti
                At(starStart + earned * starGap + (record != null ? 0.55 : 0.1), () =>
                {
                    Fx.ScalePop(geistRow, 1.12f, 0.3f);
                    PlaySfx("game_perfect");
                });
            }
            return card;
        }

        /// <summary>A reward as a small white card: the icon on its pastel tile (coins sun, XP leaf green, best/level rosy …), the line.</summary>
        private VisualElement ResultRow(string icon, string text, out Label label)
        {
            var row = Ux.Row("gb-results-row", "gb-mgw-row");
            var tile = Ux.El("gb-mgw-row-icon");
            tile.style.backgroundColor = RowWash(icon);
            tile.Add(Ux.Icon(icon));
            row.Add(tile);
            label = Ux.Text(text, "gb-mgw-row-text");
            row.Add(label);
            return row;
        }

        private static Color RowWash(string icon)
        {
            switch (icon)
            {
                case "coin": case "sparkle": return new Color(1f, 0.94f, 0.78f);      // Sonnengelb, washed
                case "leaf": case "check": return new Color(0.87f, 0.95f, 0.89f);     // Blattgrün, washed
                case "star": return new Color(1f, 0.89f, 0.89f);                      // Erdbeer, washed
                case "moon": return new Color(0.86f, 0.92f, 1f);                      // Himmelblau, washed
                case "ghost": return new Color(0.92f, 0.89f, 1f);                     // Flieder, washed
                default: return new Color(0.965f, 0.914f, 0.843f);                    // Sand
            }
        }

        // ── the window look of pregame / pause / results (and the arcade) ──

        /// <summary>
        /// The title band of the frame's windows (board 06 "Fenster / Panels"): icon tile, title with one line under it,
        /// an optional chip and the round close button on the right, on a pastel <paramref name="wash"/>.
        /// </summary>
        public static VisualElement FrameBand(VisualElement tile, string title, string line, Color wash, VisualElement extra = null, Button close = null)
        {
            var band = Ux.El("gb-mgw-band");
            band.style.backgroundColor = wash;
            if (tile != null) band.Add(tile);
            var texts = Ux.El("gb-mgw-titles");
            texts.Add(Ux.Text(title, "gb-mgw-title"));
            if (!string.IsNullOrEmpty(line)) texts.Add(Ux.Text(line, "gb-mgw-line"));
            band.Add(texts);
            if (extra != null) band.Add(extra);
            if (close != null) band.Add(close);
            return band;
        }

        /// <summary>The band colour of a game's windows: its tile pastel, washed towards white.</summary>
        public static Color BandWash(string gameId) => Color.Lerp(GameTint(gameId), Color.white, 0.58f);

        /// <summary>
        /// A big mobile pill (board 06 "Buttons"): optional icon — in a white disc when it would vanish on the pill
        /// (<paramref name="disc"/>) — and the label (Button.text stays empty, like Ux.IconTextBtn).
        /// </summary>
        private static Button FramePill(string icon, string text, Action onClick, bool disc, params string[] classes)
        {
            var b = new Button(onClick) { text = "" };
            b.ClearClassList();
            b.AddToClassList("gb-btn");
            b.AddToClassList("gb-mgw-pill");
            foreach (var c in classes) b.AddToClassList(c);
            if (!string.IsNullOrEmpty(icon))
            {
                var i = Ux.Icon(icon);
                i.pickingMode = PickingMode.Ignore;
                if (disc)
                {
                    var d = Ux.El("gb-mgw-disc");
                    d.pickingMode = PickingMode.Ignore;
                    d.Add(i);
                    b.Add(d);
                }
                else b.Add(i);
            }
            var label = Ux.Text(text, "gb-btn-label");
            label.pickingMode = PickingMode.Ignore;
            b.Add(label);
            return b;
        }

        /// <summary>A small fact chip inside a window card (icon + line), e.g. the energy cost.</summary>
        private static VisualElement FrameTag(string icon, string text)
        {
            var tag = Ux.Row("gb-mgw-tag");
            tag.pickingMode = PickingMode.Ignore;
            tag.Add(Ux.Icon(icon));
            var l = Ux.Text(text);
            l.pickingMode = PickingMode.Ignore;
            tag.Add(l);
            return tag;
        }

        // ── the 3D companion (the player's Gooby beside the windows and in the round) ──

        /// <summary>The companion, created on first use; null without a GPU (batch tests, headless QA) or a boot behind the screen.</summary>
        private World.MgBuddy3D BuddyInstance()
        {
            if (_buddy != null) return _buddy;
            if (!World.MgStage3D.Supported || Host == null || Host.Store == null) return null;
            try
            {
                var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(Host.Store.State["cosmetics"]));
                _buddy = new World.MgBuddy3D(outfit);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[mg] companion: " + e.Message);
                _buddy = null;
            }
            return _buddy;
        }

        /// <summary>
        /// A window never runs off the screen: it keeps 94 % of the width at most, and below 700 units (a phone turned
        /// to portrait for its game, about 590 wide at the phone UI scale) Gooby's column steps aside for the cards.
        /// </summary>
        private static void FitWindow(VisualElement card, VisualElement buddyColumn)
        {
            card.style.maxWidth = Length.Percent(94);
            if (buddyColumn == null) return;
            card.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var w = card.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 1f) return;
                var show = w >= 700f ? DisplayStyle.Flex : DisplayStyle.None;
                if (buddyColumn.style.display != show) buddyColumn.style.display = show;
            });
        }

        /// <summary>A window's Gooby column: the 3D view standing on a soft oval rug; null without a companion.</summary>
        private VisualElement BuddyColumn(string name, float w, float h)
        {
            var buddy = BuddyInstance();
            if (buddy == null) return null;
            var col = Ux.El("gb-mg-buddy-col");
            col.name = name;
            col.pickingMode = PickingMode.Ignore;
            var rug = Ux.El("gb-mg-buddy-rug");
            rug.pickingMode = PickingMode.Ignore;
            col.Add(rug);
            var view = Ux.El("gb-mg-buddy-view");
            view.name = name + "-view";
            view.pickingMode = PickingMode.Ignore;
            view.style.width = w;
            view.style.height = h;
            col.Add(view);
            buddy.Bind(view, World.MgBuddy3D.Framing.Full, w / h);
            return col;
        }

        /// <summary>
        /// The round's companion: the big figure on the left band when the bands have room for him, else the round HUD
        /// badge; nothing while the host's own stage shows Gooby.
        /// </summary>
        private void BindBuddyForPlay()
        {
            if (_buddy == null) return;
            var band = _buddyBand != null && _buddyBand.panel != null && _buddyBandOk ? _buddyBand : null;
            var own = StageHasGooby || GoobyFollowed;
            if (_buddyBadge != null) _buddyBadge.style.display = !own && band == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (own)
            {
                _buddy.Unbind(_buddyBand);
                _buddy.Unbind(_buddyBadgeView);
                return;
            }
            if (band != null)
            {
                var h = band.resolvedStyle.height;
                var w = band.resolvedStyle.width;
                _buddy.Bind(band, World.MgBuddy3D.Framing.Full, !float.IsNaN(h) && h > 1f && !float.IsNaN(w) && w > 1f ? w / h : 0.8f);
                _buddy.Unbind(_buddyBadgeView);
            }
            else if (_buddyBadgeView != null) _buddy.Bind(_buddyBadgeView, World.MgBuddy3D.Framing.Portrait, 1f);
            _buddy.Rest("happy");
            _buddy.Settle();
        }

        private void DropBuddy()
        {
            _buddy?.Destroy();
            _buddy = null;
            _buddyBadge = _buddyBadgeView = _buddyBand = null;
            _buddyBandOk = false;
        }

        // ── stars, tiles, tints (shared with the arcade) ────────────────────

        /// <summary>Godot results.gd stars: 1 for a score above zero, +1 for the beaten target, +1 for a new best.</summary>
        public static int StarsFor(int score, bool beatTarget, bool newBest) => (score > 0 ? 1 : 0) + (beatTarget ? 1 : 0) + (newBest ? 1 : 0);

        /// <summary>
        /// Stars of the saved best round of a mode: the round that set the best was a new best with a score (2 stars),
        /// +1 when that mode's target is beaten. 0 when never scored.
        /// </summary>
        public static int BestStars(JObject state, MinigameMeta meta, string mode)
        {
            if (meta == null) return 0;
            var best = FrameworkLogic.BestForMode(state, meta.Id, mode);
            if (best <= 0) return 0;
            var beaten = mode != "endless" && (J.IsTrue(FrameworkLogic.DifficultySliceOf(state, meta.Id).Beaten[mode]) || (meta.Target >= 0 && best >= meta.Target));
            return 2 + (beaten ? 1 : 0);
        }

        /// <summary>Three star icons (gb-star, earned ones gb-star--on).</summary>
        public static VisualElement StarRow(int earned, float size)
        {
            var row = Ux.Row("gb-stars");
            row.pickingMode = PickingMode.Ignore;
            for (var i = 0; i < 3; i++)
            {
                var s = Ux.Icon("star", "gb-star");
                s.style.width = size; s.style.height = size;
                s.pickingMode = PickingMode.Ignore;
                row.Add(s);
            }
            SetStars(row, earned);
            return row;
        }

        public static void SetStars(VisualElement row, int earned)
        {
            if (row == null) return;
            for (var i = 0; i < row.childCount; i++) row.ElementAt(i).EnableInClassList("gb-star--on", i < earned);
        }

        private static readonly Dictionary<string, string> GameIcons = new Dictionary<string, string>
        {
            ["carrotCatch"] = "food_carrot", ["bubblePop"] = "food_donut", ["memoryMatch"] = "grid", ["goobySays"] = "chat",
            ["bunnyHop"] = "arrow_up", ["burgerBuild"] = "food_burger", ["teaParty"] = "teacup", ["carrotGuard"] = "carrot",
            ["pancakeTower"] = "food_cake", ["ghostHunt"] = "moon", ["starHopper"] = "star", ["veggieChop"] = "food_tomato",
            ["basketBounce"] = "sun", ["fishingPond"] = "rain", ["trampoline"] = "cloud", ["goalieGooby"] = "hand",
            ["danceParty"] = "speaker", ["gardenRush"] = "flower", ["lanternFloat"] = "bulb", ["hideSeek"] = "search",
            ["miniGolf"] = "pin", ["pipeFlow"] = "path", ["rocketRescue"] = "globe", ["runner"] = "move",
            ["snailMail"] = "mail", ["deliveryRush"] = "car", ["cityDrive"] = "fuel", ["harborHopper"] = "boat",
            ["toyRacer"] = "race_flag", ["shoppingSurf"] = "shop", ["purblePlace"] = "food_cupcake", ["gobnom"] = "candy",
            ["gvz"] = "zombie", ["ranchHerde"] = "sheep", ["ranchParcours"] = "horseshoe", ["ranchTonnen"] = "barrel",
            ["ranchZeit"] = "stopwatch", ["ranchTurnier"] = "trophy",
        };

        /// <summary>The icon of a game's tile (arcade, pregame, pause, results); "gamepad" for the rest.</summary>
        public static string GameIcon(string gameId) => gameId != null && GameIcons.TryGetValue(gameId, out var icon) ? icon : "gamepad";

        /// <summary>A stable pastel per game (FNV-1a of the id → hue), the tile colour everywhere the game appears.</summary>
        public static Color GameTint(string gameId)
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var ch in gameId ?? "") { h ^= ch; h *= 16777619u; }
                return Color.HSVToRGB((h % 360u) / 360f, 0.3f, 1f);
            }
        }

        private static readonly Dictionary<string, Texture2D> Covers = new Dictionary<string, Texture2D>();

        /// <summary>The painted 4:3 cover of a game (UI/Resources/gooby-ui/Covers, the Godot arcade art); null when there is none.</summary>
        public static Texture2D CoverOf(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;
            if (Covers.TryGetValue(gameId, out var tex)) return tex;
            tex = Resources.Load<Texture2D>("gooby-ui/Covers/" + gameId);
            Covers[gameId] = tex;
            return tex;
        }

        /// <summary>
        /// The game's cover as a picture element (scale-and-crop into the box the styles give it); falls back to the pastel
        /// icon tile when a game has no cover.
        /// </summary>
        public static VisualElement GameCover(string gameId, string cls = "gb-game-cover")
        {
            var tex = CoverOf(gameId);
            if (tex == null)
            {
                var wrap = Ux.El(cls);
                wrap.style.backgroundColor = GameTint(gameId);
                wrap.style.justifyContent = Justify.Center;
                wrap.style.alignItems = Align.Center;
                wrap.Add(GameTile(gameId, 72f));
                return wrap;
            }
            var cover = Ux.El(cls);
            cover.style.backgroundImage = new StyleBackground(tex);
            cover.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            cover.pickingMode = PickingMode.Ignore;
            return cover;
        }

        public static VisualElement GameTile(string gameId, float size)
        {
            var tile = Ux.IconTile(GameIcon(gameId), GameTint(gameId));
            tile.AddToClassList("gb-game-tile");
            tile.style.width = size; tile.style.height = size;
            var r = size * 0.3f;
            tile.style.borderTopLeftRadius = r; tile.style.borderTopRightRadius = r;
            tile.style.borderBottomLeftRadius = r; tile.style.borderBottomRightRadius = r;
            return tile;
        }

        // ── the frame's little timeline (entry animations, ceremony beats) ──

        private struct Beat { public double At; public int Frame; public Action Act; }
        private readonly List<Beat> _timeline = new List<Beat>();

        /// <summary>Runs <paramref name="act"/> after <paramref name="delay"/> seconds, never in the frame it was queued.</summary>
        private void At(double delay, Action act) => _timeline.Add(new Beat { At = Time.unscaledTimeAsDouble + delay, Frame = Time.frameCount, Act = act });

        private void RunTimeline()
        {
            if (_timeline.Count == 0) return;
            var now = Time.unscaledTimeAsDouble;
            var frame = Time.frameCount;
            for (var i = 0; i < _timeline.Count;)
            {
                var b = _timeline[i];
                if (b.At <= now && b.Frame < frame) { _timeline.RemoveAt(i); b.Act(); }
                else i++;
            }
        }

        /// <summary>USS entry: gb-pop-in (scale + fade) — the --pre state drops a frame later so the transition plays.</summary>
        private void PopIn(VisualElement e, double delay)
        {
            if (Juice.ReducedMotion) return;
            e.AddToClassList("gb-pop-in");
            e.AddToClassList("gb-pop-in--pre");
            At(delay, () => e.RemoveFromClassList("gb-pop-in--pre"));
        }

        /// <summary>USS entry: gb-fade-in (slide up + fade).</summary>
        private void FadeIn(VisualElement e, double delay)
        {
            if (Juice.ReducedMotion) return;
            e.AddToClassList("gb-fade-in");
            e.AddToClassList("gb-fade-in--pre");
            At(delay, () => e.RemoveFromClassList("gb-fade-in--pre"));
        }
    }
}
