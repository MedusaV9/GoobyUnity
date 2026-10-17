using System;
using System.IO;
using Gooby.Core.I18n;
using Gooby.Core.Content;
using Gooby.Core.Home;
using Gooby.Core.Life;
using Gooby.Core.Meta;
using Gooby.Core.State;
using Gooby.Unity.UI;
using Gooby.Unity.World;
using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Boot root — the Unity equivalent of the Godot autoload set (GameState, AppSettings, I18n,
    /// SceneRouter, ...). Created from code before the first scene loads, so the Boot scene stays empty.
    /// </summary>
    public sealed class GameHost : MonoBehaviour
    {
        public static GameHost Instance { get; private set; }

        public GameStateStore Store { get; private set; }
        public AchievementsService Achievements { get; private set; }
        public StickerService Stickers { get; private set; }
        public DailyQuestService Quests { get; private set; }
        public AppSettings Settings { get; private set; }
        public I18n Strings { get; private set; }
        public UiRoot Ui { get; private set; }
        public SimpleWorld World { get; private set; }
        public BootFlow Boot { get; private set; }
        public LegacySaves Legacy { get; private set; }
        /// <summary>Multiplayer (friends, letters, GoobyPal, visits) against the server the player typed in.</summary>
        public OnlineService Online { get; private set; }
        /// <summary>Music, ambience beds, stingers, Gooby's voice and the car radio.</summary>
        public GoobyAudio Audio { get; private set; }
        /// <summary>QA remote (<c>-gooby-qa-cmds file</c>) for scripted multi-client tours; null otherwise.</summary>
        public QaDriver Qa { get; private set; }
        /// <summary>Older save found on this device at boot (offered once on the HUD); null when there is none.</summary>
        public LegacySaves.Candidate LegacyCandidate { get; set; }

        /// <summary>Monotonic milliseconds since start (the "ticks" the store's debounce expects).</summary>
        public static long TicksMs => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

        /// <summary>Save/settings folder; `GOOBY_DATA_DIR` overrides it (isolated test runs, QA profiles).</summary>
        public string DataDir => Environment.GetEnvironmentVariable("GOOBY_DATA_DIR") is string dir && dir.Length > 0 ? dir : Path.Combine(Application.persistentDataPath, "gooby");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("GameRoot");
            DontDestroyOnLoad(go);
            go.AddComponent<GameHost>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Application.targetFrameRate = 60;
            OrientationLock.AppDefault();
            // desktop: keep ticking without focus — the online socket (friends, letters, visits) stays pumped and a
            // second client window on the same machine does not freeze the first (mobile pauses anyway)
            if (!Application.isMobilePlatform) Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Settings = new AppSettings();
            Settings.Load(DataDir);

            Strings = new I18n(new ResourcesStringSource());
            Strings.OnError = msg => Debug.LogWarning("[i18n] " + msg);
            Strings.SetLocale(Settings.Locale);

            ContentSource.Install(rel =>
            {
                var asset = Resources.Load<TextAsset>("gooby/" + rel);
                return asset != null ? asset.text : null;
            });

            HomeState.RegisterSlice();
            Gooby.Core.Home.BadState.RegisterSlice();
            Gooby.Core.Life.GoobyBuffs.RegisterSlice();
            Gooby.Core.Garden.GardenLeaves.RegisterSlice();
            Gooby.Core.Minigames.Gobnom.GobnomProgress.RegisterSlice();
            Gooby.Core.Minigames.Gvz.GvzProgress.RegisterSlice();
            Postcards.Install();
            // arcade bonus events (modifier_engine.gd): the ticker schedules them (also in the boot catch-up), the award
            // rolls the Glückspilz on the same persisted stream
            GoobyTicker.Modifiers = new Gooby.Core.Minigames.ModifierEngine.Scheduler();
            // home weather (soul_wetter.gd): rain or snow outside makes a clammy Gooby freeze
            GoobyTicker.Weather = new SoulWetter.Oracle();
            Gooby.Core.Minigames.MinigameAward.GluecksrolleRoller = Gooby.Core.Minigames.ModifierEngine.RollGlueckspilz;
            Store = new GameStateStore();
            Store.Ticks = () => TicksMs;
            Store.Initialize(Path.Combine(DataDir, "save_v5.json"), TicksMs);
            HomeState.EnsureInitialized(Store); // first move-in: default room layouts + start storage
            if (Gooby.Core.City.PowLogic.DekoWartet(Store.State)) Store.Update(s => Gooby.Core.City.PowLogic.DekoNachholen(s));
            FoodCatalog.TreatAwarder = (s, id) => CollectionsLogic.AwardInState(s, "treats", CollectionsLogic.TreatEntryForFood(id));
            Achievements = new AchievementsService(Store);
            Achievements.Unlocked += AnnounceAchievement;
            Achievements.Attach();
            Stickers = new StickerService(Store);
            Stickers.Unlocked += AnnounceSticker;
            Stickers.PageCompleted += AnnounceStickerSet;
            Stickers.Attach();
            Quests = new DailyQuestService(Store, () => new QuestCtx
            {
                Level = Store.GetValue("progression.level", 1),
                Minigames = new System.Collections.Generic.HashSet<string>(System.Linq.Enumerable.Select(Gooby.Core.Minigames.MinigameSims.All, m => m.Id)),
                Garden = true,
            });
            Quests.EnsureRoll();
            Store.SliceChanged += (id, _) => { if (id == "achievements" || id == "minigames" || id == "economy") Quests.EnsureRoll(); };

            Legacy = new LegacySaves(this);
            Ui = UiRoot.Create(gameObject, Settings);
            Audio = GoobyAudio.Create(this);
            World = SimpleWorld.Create();
            GraphicsQuality.Apply(Settings.GraphicsPreset, World.Camera, World.Look);
            Settings.Changed += key => { if (key == "graphicsPreset") GraphicsQuality.Apply(Settings.GraphicsPreset, World.Camera, World.Look); };
            ColorVisionFilter.Apply(Settings, transform, Ui.Document != null ? Ui.Document.rootVisualElement : null);
            Settings.Changed += key =>
            {
                if (key == "colorVision" || key == "highContrast") ColorVisionFilter.Apply(Settings, transform, Ui.Document != null ? Ui.Document.rootVisualElement : null);
            };
            Online = new OnlineService(this);
            Online.Apply(connect: false);
            Store.GoobyEvents += AnnounceModifierEvents;
            _shownLevel = Gooby.Core.Json.J.Int(Gooby.Core.Json.J.Path(Store.State, "progression", "level"), 1);
            Store.LevelChanged += CelebrateLevel;
            Qa = QaDriver.FromArgs(this);
            Boot = new BootFlow(this);
            Boot.Start();
        }

        private void Update()
        {
            DevTools.Tick(Store, Time.unscaledDeltaTime);
            Store?.Process(Time.unscaledDeltaTime, TicksMs);
            Boot?.Update(Time.unscaledDeltaTime);
            if (World != null)
            {
                World.ReducedMotion = Settings != null && Settings.ReducedMotion;
                Effects.Reduced = World.ReducedMotion;
                World.Tick(Time.unscaledDeltaTime);
            }
            Online?.Update(Time.unscaledDeltaTime);
            Qa?.Update(Time.unscaledDeltaTime);
            Ui?.Update();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Store?.OnPauseOrQuit();
            else Store?.OnResume(TicksMs);
            Online?.OnPause(paused);
        }

        private void OnApplicationQuit()
        {
            Store?.OnPauseOrQuit();
            Online?.Client.Disconnect();
        }

        public void SaveAll()
        {
            Store?.SaveNow();
            Settings?.Save();
        }

        private int _shownLevel;

        /// <summary>A new sticker over every screen (RewardHub in Godot); build mode shows its own banner for it.</summary>
        private void AnnounceSticker(StickerDef def)
        {
            Debug.Log("[stickers] unlocked " + def.Id);
            Store.NotifySliceChanged("stickers");   // the HUD's unseen-sticker dot
            if (Ui == null || Strings == null || Ui.Current is Gooby.Unity.UI.Screens.BuildModeScreen) return;
            var key = def.Rarity == "episch" ? "album.unlock_toast_episch" : def.Rarity == "selten" ? "album.unlock_toast_selten" : "album.unlock_toast";
            Ui.Toast(Strings.T(key, ("name", def.NameDe)), "star", 4f);
            Sfx.PlayUi("soft_sticker");
        }

        /// <summary>A complete sticker set: the big card (the StickerService has paid its coins).</summary>
        private void AnnounceStickerSet(StickerPage page, int coins)
        {
            Debug.Log("[stickers] set complete " + page.Id + " +" + coins);
            if (Ui == null || Strings == null) return;
            Ui.Celebrate(Strings.T("album.set_komplett"), Strings.T("album.set_belohnung", ("title", page.TitleDe), ("coins", coins)), "star");
            Audio?.PlayStinger("stinger-levelup");
        }

        private void AnnounceAchievement(AchievementDef def)
        {
            Debug.Log("[achievements] unlocked " + def.Id + " (+" + def.Coins + ")");
            if (Ui == null || Strings == null) return;
            var nameKey = "achievements.defs." + def.Id + ".name";
            var name = Strings.HasKey(nameKey) ? Strings.T(nameKey) : def.Id;
            Ui.Toast(Strings.T("achievements.unlock_toast", ("name", name), ("coins", def.Coins)), "coin", 4f);
            Sfx.PlayUi("soft_coins");
        }

        /// <summary>
        /// A level gained anywhere — care, the garden, the city — gets its moment over every screen: the celebration card
        /// with confetti and the level-up stinger. Minigames celebrate it on their own results card, so not over them.
        /// </summary>
        private void CelebrateLevel(int level, double _)
        {
            if (level <= _shownLevel) { _shownLevel = level; return; }
            var prev = _shownLevel;
            _shownLevel = level;
            // every fifth level queues Gooby's look back; it plays the next time he is home (HomeHudScreen)
            Gooby.Core.Meta.Recap.QueueIfMilestone(Store, prev, level);
            if (Ui == null || Ui.Current is Gooby.Unity.UI.Screens.MinigameHostScreen || Strings == null) return;
            Debug.Log("[levelup] " + level);
            Ui.Celebrate(Strings.T("nl.levelup.titel", ("level", level)), Strings.T("nl.levelup.text"));
            Audio?.PlayStinger("stinger-levelup");
            Haptics.Success();
        }

        /// <summary>"modifierStarted:&lt;gameId&gt;:&lt;type&gt;" from the ticker → the toast over every screen (RewardHub in Godot).</summary>
        private void AnnounceModifierEvents(System.Collections.Generic.List<string> events)
        {
            foreach (var e in events)
            {
                if (!e.StartsWith(Gooby.Core.Minigames.ModifierEngine.StartedEventPrefix)) continue;
                var parts = e.Substring(Gooby.Core.Minigames.ModifierEngine.StartedEventPrefix.Length).Split(':');
                if (parts.Length < 2 || !Gooby.Core.Minigames.ModifierEngine.Types.TryGetValue(parts[1], out var def)) continue;
                var meta = Gooby.Core.Minigames.MinigameRegistry.GetGame(parts[0]);
                var game = meta != null && Strings.HasKey(meta.TitleKey) ? Strings.T(meta.TitleKey) : parts[0];
                Debug.Log("[modifier] started " + parts[1] + " in " + parts[0]);
                Ui?.Toast(Strings.T("modifier.start", ("name", Strings.T(def.NameKey)), ("game", game)), def.Icon, 4.5f);
            }
        }
    }

    /// <summary>String files come from Resources/gooby/strings (index.json lists the files per locale).</summary>
    public sealed class ResourcesStringSource : IStringSource
    {
        private readonly IndexedStringSource _inner;

        public ResourcesStringSource()
        {
            var index = Resources.Load<TextAsset>("gooby/strings/index");
            _inner = new IndexedStringSource(index != null ? index.text : "{}", rel =>
            {
                var path = "gooby/strings/" + rel.Substring(0, rel.Length - ".json".Length);
                var asset = Resources.Load<TextAsset>(path);
                return asset != null ? asset.text : null;
            });
        }

        public System.Collections.Generic.IEnumerable<string> FilesFor(string locale) => _inner.FilesFor(locale);
        public string ReadJson(string relativePath) => _inner.ReadJson(relativePath);
    }
}
