using System;
using System.IO;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Unity.UI;
using Gooby.Unity.UI.Screens;
using Gooby.Unity.World;
using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Boot phases (port target: scripts/boot/boot_phasen.gd): cover/loading screen with real progress
    /// (settings → strings → save → world → warm-up), then the home HUD. Minimum dwell keeps the
    /// loading card readable and lets the progress bar animate instead of flashing.
    /// </summary>
    public sealed class BootFlow
    {
        /// <summary>Minimum loading-screen dwell; QA can stretch it with `-gooby-boot-seconds N` (screenshots, slow-boot checks).</summary>
        private static readonly double MinDwellSec = BootDwellFromArgs(1.6);

        private static double BootDwellFromArgs(double fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-gooby-boot-seconds" && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return Math.Max(fallback, v);
            return fallback;
        }

        private readonly GameHost _host;
        private LoadingScreen _loading;
        private double _elapsed;
        private double _progress;
        private bool _done;
        private int _step;

        private static readonly (string key, double weight)[] Steps =
        {
            ("nl.loading.phase.einstellungen", 0.15),
            ("nl.loading.phase.strings", 0.30),
            ("nl.loading.phase.save", 0.50),
            ("nl.loading.phase.welt", 0.80),
            ("nl.loading.phase.fertig", 1.00),
        };

        public BootFlow(GameHost host)
        {
            _host = host;
        }

        public bool IsDone => _done;

        public void Start()
        {
            _loading = new LoadingScreen(_host);
            _host.Ui.Show(_loading);
        }

        /// <summary>The model warm-up may hold the loading card this long past the dwell; a broken file must not hold the boot.</summary>
        private const double WarmUpCapSec = 12.0;

        public void Update(double dt)
        {
            if (_done) return;
            _elapsed += dt;
            var target = Math.Min(1.0, _elapsed / MinDwellSec);
            while (_step < Steps.Length && target >= Steps[_step].weight - 1e-6)
            {
                RunStep(_step);
                _step++;
            }
            // the last fifth of the bar is the model warm-up (step 0 started it): the card stays until the town, the hero
            // car and Gooby are imported, so the street opens complete instead of the car and the houses popping in.
            // Checked after the steps ran — a long frame may jump the bar straight to the end while imports are still out.
            var started = AssetLoader.TemplatesStarted;
            var pending = AssetLoader.TemplatesPending;
            var waiting = pending > 0 && _elapsed < MinDwellSec + WarmUpCapSec;
            if (waiting) target = Math.Min(target, 0.8 + 0.19 * (started - pending) / Math.Max(1, started));
            _progress = Mathf.Lerp((float)_progress, (float)target, (float)Math.Min(1.0, dt * 8.0));
            _loading.SetProgress(_progress, waiting ? Steps[3].key : _step < Steps.Length ? Steps[_step].key : Steps[Steps.Length - 1].key);
            if (_elapsed >= MinDwellSec + 0.35 && _step >= Steps.Length && !waiting)
            {
                if (pending > 0) Debug.LogWarning($"[boot] warm-up cap: {pending}/{started} models still importing");
                Debug.Log($"[boot] done after {_elapsed:F1} s, models warmed up {started - pending}/{started}");
                _done = true;
                var hud = new HomeHudScreen(_host);
                _host.Ui.Show(hud);
                OpenAtBoot();
                if (!MaybeOnboard()) MaybeOfferParcel(hud);
            }
        }

        /// <summary>
        /// The first start of a day brings today's parcel up by itself (Godot DailyBonusPopup.should_offer): only after the
        /// onboarding, only while it is unclaimed, not in batch mode or QA runs. "Später" keeps it on the HUD card.
        /// </summary>
        private void MaybeOfferParcel(HomeHudScreen hud)
        {
            if (Application.isBatchMode) return;
            foreach (var a in Environment.GetCommandLineArgs())
                if (a == "-gooby-qa-cmds" || a == "-gooby-open" || a == "-gooby-at" || a == "-gooby-server") return;
            if (!J.IsTrue(J.Path(_host.Store.State, "onboarding", "done"))) return;
            if (!UI.Screens.PackageScreen.Claimable(_host)) return;
            Debug.Log("[boot] daily parcel offered");
            _host.Ui.Push(new UI.Screens.PackageScreen(_host, hud.SayLine));
        }

        /// <summary>
        /// First start (onboarding.done false, Godot's slice): the welcome card asks for the player's name and Gooby's
        /// nickname. Not in batch mode (tests, headless multiplayer players) and not for QA runs; a save that already has
        /// progress (level 2+) is only marked done.
        /// </summary>
        private bool MaybeOnboard()
        {
            var store = _host.Store;
            if (J.IsTrue(J.Path(store.State, "onboarding", "done"))) return false;
            if (J.Int(J.Path(store.State, "progression", "level"), 1) >= 2)
            {
                store.Update(s => J.EnsureDict(s, "onboarding")["done"] = true, GameHost.TicksMs);
                return false;
            }
            if (Application.isBatchMode) return false;
            foreach (var a in Environment.GetCommandLineArgs())
                if (a == "-gooby-qa-cmds" || a == "-gooby-open" || a == "-gooby-at" || a == "-gooby-server") return false;
            _host.Ui.Push(new OnboardingScreen(_host));
            return true;
        }

        /// <summary>
        /// QA shortcut `-gooby-open city|garden|game:&lt;id&gt;|&lt;placeId&gt;` (screenshot tours, tools/playtest): straight onto the street, or
        /// parked at a place with its interior open — the same screens a player reaches by driving there.
        /// </summary>
        private void OpenAtBoot()
        {
            var args = Environment.GetCommandLineArgs();
            string target = null, at = null;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-gooby-open") target = args[i + 1];
                if (args[i] == "-gooby-at") at = args[i + 1];      // QA: start driving from a place's parking spot instead of home
            }
            if (string.IsNullOrEmpty(target) && string.IsNullOrEmpty(at)) return;
            // QA: straight onto the lawn (the garden room, HUD stays) — for screenshots of beds, structures and fences
            if (target == "garden") { _host.World?.EnterRoom("garden"); return; }
            // QA: a minigame's pregame card (`game:<registryId>`), as the arcade would open it
            if (target != null && target.StartsWith("game:"))
            {
                var game = UI.Screens.MinigameHostScreen.For(_host, target.Substring(5));
                if (game != null) _host.Ui.Push(game);
                else Debug.LogWarning("[boot] no Unity host for " + target);
                return;
            }
            if (target == "cutscene" || target == "travel-cutscene")
            {
                _host.Ui.Push(new UI.Screens.TravelCutsceneScreen(_host, Gooby.Core.City.TravelCutscene.ModusAbflug));
                return;
            }
            if (target == "gooberando")
            {
                _host.Ui.Push(new UI.Screens.GooberandoScreen(_host));
                return;
            }
            if (target == "raumstation")
            {
                // QA: latch the space visit so the shuttle path is testable without a 4-day trip
                _host.Store.Update(s => { J.EnsureDict(J.EnsureDict(s, "vacation"), "visited")["space"] = true; });
                _host.Store.NotifySliceChanged("vacation");
            }
            SeedGooberandoOrder();
            var drive = new UI.Screens.CityDriveScreen(_host);
            _host.Ui.Push(drive);
            // QA: hold at home so the orange van drives toward the camera on the home street
            if (UI.Screens.GooberandoScreen.PrepOverride() != null) drive.Braking = true;
            if (!string.IsNullOrEmpty(at))
            {
                var spot = Gooby.Core.City.DriveModel.PlaceById(at);
                if (spot == null) Debug.LogWarning("[boot] -gooby-at: unknown place " + at);
                else if (_host.World.Drive != null) { _host.World.Drive.X = spot.X; _host.World.Drive.Z = spot.Z; _host.World.Drive.Heading = spot.Yaw * Math.PI / 180.0 + Math.PI / 2.0; _host.World.Drive.Kmh = 0.0; }
            }
            if (string.IsNullOrEmpty(target) || target == "city") return;
            var place = Gooby.Core.City.DriveModel.PlaceById(target);
            if (place == null) { Debug.LogWarning("[boot] -gooby-open: unknown place " + target); return; }
            if (_host.World.Drive != null) { _host.World.Drive.X = place.X; _host.World.Drive.Z = place.Z; _host.World.Drive.Kmh = 0.0; }
            drive.Braking = true;
            _host.Ui.Push(UI.Screens.PlaceScreen.For(_host, place));
        }

        /// <summary>QA: <c>-gooby-gooberando-prep N</c> plants a live order so the orange van is on the street.</summary>
        private void SeedGooberandoOrder()
        {
            var prep = GooberandoScreen.PrepOverride();
            if (prep == null) return;
            _host.Store.Update(s =>
            {
                var eco = J.EnsureDict(s, "economy");
                if (J.Int(eco["coins"]) < 200) eco["coins"] = 200;
            });
            var kitchens = GooberandoRestaurants.All();
            if (kitchens.Count == 0) return;
            var kitchen = GooberandoRestaurants.ById("burger_bau") ?? kitchens[0];
            var dish = kitchen.Gerichte.Count > 0 ? kitchen.Gerichte[0] : null;
            if (dish == null) return;
            if (GooberandoLogic.Buchen(_host.Store, _host.Store.Clock.NowMs(), prep.Value, dish.Id, dish.Preis, kitchen.Id))
                Debug.Log("[boot] gooberando seed " + dish.Id + " from " + kitchen.Id + " prep=" + prep.Value + "s");
        }

        private void RunStep(int step)
        {
            switch (step)
            {
                case 0:
                    _host.World.BuildHome(_host.Store); // board 04: Gooby's living room sits behind the loading card
                    // town, garden, car and furniture models import behind the loading card, so Sonnental is complete the
                    // moment the drive screen opens instead of popping in house by house
                    AssetLoader.WarmUpCharacter();
                    AssetLoader.WarmUp(Path.Combine(AssetLoader.Root, "scenery"));
                    AssetLoader.WarmUp(Path.Combine(AssetLoader.Root, "vehicles"));
                    AssetLoader.WarmUp(Path.Combine(AssetLoader.Root, "props")); // furniture of every room + lawn items
                    break;
                case 1:
                    _host.Strings.Table(_host.Strings.Locale);
                    break;
                case 2:
                    // SAVE-TRANSFER way A: offer a Godot/web save found on this device (once)
                    if (!_host.Legacy.AlreadyImported) _host.LegacyCandidate = _host.Legacy.Find(_host.Store.Clock.NowMs());
                    break;
            }
        }
    }
}
