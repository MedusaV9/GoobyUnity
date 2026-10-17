using System.Collections;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.UI.Screens;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gooby.Tests.Flow
{
    /// <summary>
    /// PlayMode smoke + flow tests against the real boot (GameHost bootstraps itself on play): loading → HUD,
    /// every screen builds and pops, build mode places from storage into the save, a minigame round books coins.
    /// </summary>
    public class GameFlowPlayTests
    {
        /// <summary>
        /// A failing test must not leave its screens behind (the next test would start on the street or in a shop and
        /// fail for no reason of its own): back to the HUD, the minigame/visit/city stages torn down by OnHidden.
        /// </summary>
        [UnityTearDown]
        public IEnumerator CleanStackAfterEachTest()
        {
            var host = GameHost.Instance;
            if (host == null || host.Ui == null) yield break;
            host.Ui.PopToRoot();
            host.World?.SetMove(Vector2.zero);
            if (host.World != null && host.World.Visiting) host.World.LeaveVisit();
            if (host.World != null && host.World.Hosting) host.World.EndHosting();
            yield return null;
            yield return null;
        }

        private static IEnumerator WaitForBoot()
        {
            var t = 0f;
            while ((GameHost.Instance == null || GameHost.Instance.Boot == null || !GameHost.Instance.Boot.IsDone) && t < 20f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(GameHost.Instance, Is.Not.Null, "GameHost bootstraps before the first scene");
            Assert.That(GameHost.Instance.Boot.IsDone, Is.True, "boot finishes within 20 s");
        }

        [UnityTest]
        public IEnumerator EveryHostedGameOpensItsOwnHostAndUnknownIdsOpenNone()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            foreach (var id in MinigameHostScreen.Hosted)
            {
                var screen = MinigameHostScreen.For(host, id);
                Assert.That(screen, Is.Not.Null, id);
                Assert.That(screen.Id, Is.EqualTo("minigame-" + id), id + " runs under its own registry entry");
                var icon = MinigameHostScreen.GameIcon(id);
                Assert.That(icon, Is.Not.EqualTo("gamepad"), id + " has its own arcade icon");
                Assert.That(Resources.Load<Texture2D>("gooby-ui/Icons/icon_" + icon), Is.Not.Null, id + " → icon_" + icon);
            }
            Assert.That(MinigameHostScreen.For(host, "doesNotExist"), Is.Null, "no stand-in game under a foreign title");
        }

        [UnityTest]
        public IEnumerator BootReachesTheHomeHudWithAValidSave()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
            Assert.That(J.Int(host.Store.State["v"]), Is.EqualTo(5));
            Assert.That(host.World.Gooby, Is.Not.Null, "Gooby stand-in or avatar exists");
            Assert.That(host.Strings.T("nl.hud.aufgaben"), Is.Not.EqualTo("nl.hud.aufgaben"), "strings loaded");
            Assert.That(host.Quests.Board().Count, Is.EqualTo(3), "daily board rolled on boot");
        }

        [UnityTest]
        public IEnumerator EveryScreenBuildsAndPopsBackToTheHud()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var screens = new System.Func<Gooby.Unity.UI.UiScreen>[]
            {
                () => new SettingsScreen(host), () => new BuildModeScreen(host), () => new CityDriveScreen(host),
                () => new ShopScreen(host), () => new ArcadeScreen(host), () => new CareSheetScreen(host, _ => { }),
                () => new MinigameScreen(host, "carrotCatch"), () => new BubblePopScreen(host), () => new MemoryMatchScreen(host), () => new GoobySaysScreen(host), () => new BunnyHopScreen(host), () => new BurgerBuildScreen(host), () => new TeaPartyScreen(host), () => new CarrotGuardScreen(host), () => new PancakeTowerScreen(host), () => new GhostHuntScreen(host), () => new StarHopperScreen(host), () => new VeggieChopScreen(host), () => new BasketBounceScreen(host), () => new FishingPondScreen(host), () => new TrampolineScreen(host), () => new GoalieGoobyScreen(host), () => new DancePartyScreen(host), () => new GardenRushScreen(host), () => new LanternFloatScreen(host), () => new HideSeekScreen(host), () => new MiniGolfScreen(host), () => new PipeFlowScreen(host), () => new RocketRescueScreen(host), () => new RunnerScreen(host), () => new SnailMailScreen(host), () => new DeliveryRushScreen(host), () => new TalkScreen(host, _ => { }), () => new InventoryScreen(host),
                () => new PackageScreen(host, _ => { }), () => new OutfitScreen(host),
                // multiplayer (built offline: no server configured in the test data dir)
                () => new FriendsScreen(host), () => new MailboxScreen(host), () => new MailboxScreen(host, "GOOBY-TEST"),
                () => new RanchMpScreen(host, host.Online),
                () => new SettingsScreen(host, "konto"),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.Places.First(p => p.Id == "tierarzt")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.Places.First(p => p.Id == "goobytheke")),
                () => new ShopScreen(host, Gooby.Core.City.DriveModel.Places.First(p => p.Id == "goobytheke")),
                // every counter of the town: shop variants, market, dealer, post, park, travel desk — and every interior
                () => new ShopScreen(host, Gooby.Core.City.DriveModel.PlaceById("goobyman")),
                () => new ShopScreen(host, Gooby.Core.City.DriveModel.PlaceById("baumarkt")),
                () => new ShopScreen(host, Gooby.Core.City.DriveModel.PlaceById("pow")),
                () => new MarktScreen(host, Gooby.Core.City.DriveModel.PlaceById("wochenmarkt")),
                () => new AutohausScreen(host, Gooby.Core.City.DriveModel.PlaceById("autohaus")),
                () => new PostScreen(host, Gooby.Core.City.DriveModel.PlaceById("post")),
                () => new ParkScreen(host, Gooby.Core.City.DriveModel.PlaceById("funkelpark")),
                () => new TravelScreen(host, Gooby.Core.City.DriveModel.PlaceById("flughafen")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("mart")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("goobyman")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("gouhbus")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("post")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("pow")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("baumarkt")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("autohaus")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("wochenmarkt")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("funkelpark")),
                () => new PlaceScreen(host, Gooby.Core.City.DriveModel.PlaceById("flughafen")),
            };
            foreach (var make in screens)
            {
                var screen = make();
                host.Ui.Push(screen);
                yield return null;
                Assert.That(host.Ui.Current, Is.SameAs(screen));
                Assert.That(screen.Root.childCount, Is.GreaterThan(0), screen.Id + " builds elements");
                if (screen is PlaceScreen place)
                {
                    Assert.That(host.World.Interior?.Place, Is.SameAs(place.Place), screen.Id + " stages its interior");
                    Assert.That(GameObject.Find("World/Interior:" + place.Place.Id), Is.Not.Null);
                    Assert.That(host.World.Camera.transform.position.x, Is.EqualTo(Gooby.Unity.World.InteriorScene.Origin.x).Within(0.01f), "camera looks into the interior");
                }
                host.Ui.Pop();
                yield return null;
                Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"), "back on the HUD after " + screen.Id);
                Assert.That(host.World.Interior, Is.Null, "popping a place screen tears its interior down");
            }
            host.World.SetBuildCamera(false);
            host.World.SetCityScene(false);
        }

        [UnityTest]
        public IEnumerator DoorsLeadThroughTheHouseAndIntoTheGarden()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var world = host.World;
            // a sheet over the HUD detaches its door listener (fade → PassDoor), so the world logic is tested bare
            host.Ui.Push(new SettingsScreen(host));
            yield return null;
            world.EnterRoom("living"); // the save may remember another room from an earlier run
            Assert.That(world.RoomId, Is.EqualTo("living"));
            Assert.That(world.Room.RoomId, Is.EqualTo("living"));

            // living → kitchen through the north door: the kitchen is staged, Gooby stands just inside its west door
            var toKitchen = RoomDefs.GetDoor("living", "living_kueche");
            var changed = new System.Collections.Generic.List<string>();
            world.RoomChanged += changed.Add;
            Assert.That(world.PassDoor(toKitchen), Is.True);
            yield return null;
            Assert.That(world.RoomId, Is.EqualTo("kitchen"));
            Assert.That(world.Room.RoomId, Is.EqualTo("kitchen"));
            Assert.That(changed, Is.EqualTo(new[] { "kitchen" }));
            Assert.That(RoomNavigation.CurrentRoom(host.Store), Is.EqualTo("kitchen"), "current room is persisted");
            var kitchen = RoomDefs.Get("kitchen");
            var (ax, ay) = RoomNavigation.FreeArrival(kitchen, RoomDefs.GetDoor("kitchen", "kueche_living"), HomeState.LoadRoomGrid(host.Store, "kitchen"));
            var expected = SimpleWorldArrival(kitchen, world.Room.Origin, ax, ay);
            Assert.That(Vector3.Distance(new Vector3(world.Gooby.position.x, 0f, world.Gooby.position.z), expected), Is.LessThan(0.05f), "Gooby arrives inside the kitchen door");
            Assert.That(world.Room.Root.GetComponentsInChildren<Gooby.Unity.World.DoorMarker>().Length, Is.EqualTo(kitchen.Doors.Count), "every door is tappable");

            // walking back into the door zone fires the door → living room again, arriving at the kitchen door
            var fired = new System.Collections.Generic.List<RoomDefs.Door>();
            world.DoorReached += fired.Add;
            world.Gooby.position = SimpleWorldArrival(kitchen, world.Room.Origin, 0.5, 4.0); // west door zone: rows 3–4
            world.SetMove(new Vector2(-1f, 0f));
            for (var i = 0; i < 30 && fired.Count == 0; i++) { world.Tick(0.05f); yield return null; }
            world.SetMove(Vector2.zero);
            Assert.That(fired.Select(d => d.Id), Is.EqualTo(new[] { "kueche_living" }));
            Assert.That(world.RoomId, Is.EqualTo("kitchen"), "with a listener the world waits for PassDoor");
            world.DoorReached -= fired.Add;
            Assert.That(world.PassDoor(fired[0]), Is.True);
            yield return null;
            Assert.That(world.RoomId, Is.EqualTo("living"));

            // living → garden through the east door: the lawn becomes the stage, the house is hidden
            Assert.That(world.PassDoor(RoomDefs.GetDoor("living", "living_garten")), Is.True);
            yield return null;
            Assert.That(world.InGarden, Is.True);
            Assert.That(world.GardenRoot.gameObject.activeInHierarchy, Is.True);
            Assert.That(world.Room.Root.activeInHierarchy, Is.False, "house hidden while on the lawn");
            Assert.That(changed.Last(), Is.EqualTo("garden"));
            // the cottage door is tappable from the lawn like the room doors: a marker over the door step fires the door
            var cottageDoor = world.GardenRoot.GetComponentsInChildren<Gooby.Unity.World.DoorMarker>().Single();
            Assert.That(cottageDoor.Door.Id, Is.EqualTo("garten_living"));
            yield return new WaitForFixedUpdate();
            for (var i = 0; i < 30; i++) world.Tick(0.05f); // the door cooldown from the walk above runs out
            fired.Clear();
            world.DoorReached += fired.Add;
            var doorOnScreen = world.Camera.WorldToScreenPoint(cottageDoor.transform.position + new Vector3(0f, 1.1f, 0.35f));
            Assert.That(world.TapWorld(new Vector2(doorOnScreen.x, doorOnScreen.y)), Is.True, "tap on the cottage door");
            Assert.That(fired.Select(d => d.Id), Is.EqualTo(new[] { "garten_living" }));
            world.DoorReached -= fired.Add;
            Assert.That(world.PassDoor(RoomDefs.GetDoor("garden", "garten_living")), Is.True);
            yield return null;
            Assert.That(world.RoomId, Is.EqualTo("living"));
            Assert.That(world.Room.Root.activeInHierarchy, Is.True);
            world.RoomChanged -= changed.Add;
            world.EnterRoom("living");
            host.Ui.Pop();
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator OpeningTheParcelClaimsTheDailyBonusAndHidesTheCard()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var today = host.Store.Clock.LocalDay();
            // make today's parcel unopened regardless of what earlier runs left in the save
            host.Store.Update(s => { var d = J.EnsureDict(s, "daily"); d["lastClaimDay"] = ""; d["streak"] = 0; });
            host.Store.NotifySliceChanged("daily");
            yield return null;
            Assert.That(PackageScreen.Claimable(host), Is.True);
            var card = host.Ui.Current.Root.Q("gift-card");
            Assert.That(card, Is.Not.Null);
            Assert.That(card.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "parcel card shows while unopened");

            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            var screen = new PackageScreen(host, _ => { });
            host.Ui.Push(screen);
            yield return null;
            Assert.That(screen.Root.childCount, Is.GreaterThan(0));
            screen.OpenParcel();
            yield return null;
            var (expectedCoins, _) = Gooby.Core.Meta.DailyBonus.RewardForStreak(1);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.EqualTo(coinsBefore + expectedCoins), "day-1 reward booked");
            Assert.That(J.Str(host.Store.State["daily"]["lastClaimDay"]), Is.EqualTo(today));
            Assert.That(PackageScreen.Claimable(host), Is.False);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
            Assert.That(host.Ui.Current.Root.Q("gift-card").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "card gone until tomorrow");
        }

        [UnityTest]
        public IEnumerator InventoryHandsAStorageItemToBuildModeOfTheCurrentRoom()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.World.EnterRoom("living");
            var first = HomeState.Storage(host.Store).Select(r => J.Str(r["item"])).FirstOrDefault(id => FurnitureCatalog.Def(id) != null);
            Assert.That(first, Is.Not.Null.And.Not.Empty, "storage has something to place");
            var inventory = new InventoryScreen(host);
            host.Ui.Push(inventory);
            yield return null;
            Assert.That(inventory.Root.Q("chip-alle"), Is.Not.Null, "filter chips built");
            host.Ui.Pop();
            var build = new BuildModeScreen(host, host.World.RoomId, preselect: first);
            host.Ui.Push(build);
            yield return null;
            Assert.That(host.World.Build, Is.Not.Null);
            Assert.That(host.World.Build.RoomId, Is.EqualTo("living"));
            Assert.That(host.World.Build.SelectedItem, Is.EqualTo(first), "item starts in hand");
            host.Ui.Pop();
            yield return null;
            host.World.EndBuild(commit: false);
            host.World.SetRoomBuildCamera(false);
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator BuyingAHatDressesTheAvatar()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var t = 0f;
            while (host.World.Avatar == null && t < 8f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(host.World.Avatar, Is.Not.Null, "rigged Gooby loaded (glTFast)");
            Assert.That(host.World.Avatar.Bone("head"), Is.Not.Null, "rig exposes the head bone");

            var hat = Gooby.Core.Cosmetics.CosmeticsCatalog.ByKategorie("hut").OrderBy(d => d.Preis).First();
            host.Store.Update(s => s["economy"]["coins"] = 5000);
            var result = Gooby.Core.Cosmetics.CosmeticsState.ApplyToState(host.Store, (slice, econ) =>
            {
                var buy = Gooby.Core.Cosmetics.CosmeticsState.Buy(slice, econ, hat.Id, 99);
                if (buy.Ok || buy.Grund == "schon_besessen") Gooby.Core.Cosmetics.CosmeticsState.Equip(slice, hat.Id);
                return buy;
            });
            Assert.That(result.Ok || result.Grund == "schon_besessen", Is.True, result.Grund);
            yield return null;
            var worn = host.World.Avatar.GetComponentsInChildren<Transform>(true).Where(x => x.name == "Outfit:" + hat.Id).ToList();
            Assert.That(worn.Count, Is.EqualTo(1), "the hat hangs on the avatar after the cosmetics slice changed");
            Assert.That(worn[0].parent.name, Is.EqualTo("head"), "hats ride the head bone");
            Assert.That(worn[0].position.y, Is.GreaterThan(host.World.Gooby.position.y + 0.6f), "sits on top of the head");

            Gooby.Core.Cosmetics.CosmeticsState.ApplyToState(host.Store, (slice, _) => Gooby.Core.Cosmetics.CosmeticsState.Unequip(slice, "hut"));
            yield return null;
            yield return null;
            Assert.That(host.World.Avatar.GetComponentsInChildren<Transform>(true).Any(x => x.name == "Outfit:" + hat.Id), Is.False, "taken off again");
        }

        [UnityTest]
        public IEnumerator DrivingSteersStopsAtTheMartAndTurnsTheLampsOnAtNight()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var screen = new CityDriveScreen(host);
            host.Ui.Push(screen);
            yield return null;
            var drive = host.World.Drive;
            Assert.That(drive, Is.Not.Null, "the street simulation exists once the city is staged");
            var h0 = drive.Heading;
            drive.Kmh = Gooby.Core.City.DriveModel.CruiseKmh;
            for (var i = 0; i < 20; i++) host.World.DriveStep(new Gooby.Core.City.DriveModel.Input { Throttle = 1, Steer = -1 }, 0.05f);
            Assert.That(Gooby.Core.City.DriveModel.Wrap(drive.Heading - h0), Is.LessThan(0.0), "left stick turns the car left");
            var car = GameObject.Find("World/City/Car").transform;
            Assert.That(Mathf.DeltaAngle(car.eulerAngles.y, (float)drive.HeadingDeg), Is.EqualTo(0f).Within(0.01f), "the car model follows the heading");

            // roll up to the GOOBY MART and stop: the pill offers to enter
            var mart = Gooby.Core.City.DriveModel.Places.First(p => p.Id == "mart");
            drive.X = mart.X; drive.Z = mart.Z - 2.0; drive.Heading = 0.0; drive.Kmh = 0.0;
            screen.Braking = true;   // foot on the brake: the screen's auto-throttle must not roll the car off the spot
            host.World.DriveStep(new Gooby.Core.City.DriveModel.Input { Brake = true }, 0.05f);
            screen.Update();
            yield return null;
            Assert.That(Gooby.Core.City.DriveModel.Reached(drive)?.Id, Is.EqualTo("mart"));
            Assert.That(screen.Root.Q("enter-place").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "enter button shows at the mart");
            var cam = host.World.Camera.transform.position;
            Assert.That(Vector3.Distance(new Vector3(cam.x, 0f, cam.z), new Vector3((float)drive.X, 0f, (float)drive.Z)), Is.InRange(5.6f, 8.0f), "chase camera sits behind the car (6.3–7.4 m + accel lean, sky pass 16 Sep)");
            Assert.That(cam.z, Is.LessThan((float)drive.Z), "…behind, looking the way the car heads (south)");
            host.Ui.Pop();
            yield return null;

            // night: pin the clock to 22:00 local, stage the street again → dark sky, lanterns lit
            var clock = host.Store.Clock;
            var now = clock.NowMs();
            var bias = clock.UtcOffsetMinutes(now);
            var localHour = Gooby.Core.Time.DayPhase.LocalHour(now, bias);
            clock.Pin(now + (long)((22.0 - localHour) * 3600.0 * 1000.0));
            host.World.SetCityScene(true);
            yield return null;
            Assert.That(Gooby.Core.Time.DayPhase.Of(host.World.LocalHour), Is.EqualTo(Gooby.Core.Time.DayPhase.Nacht));
            var sky = host.World.Camera.backgroundColor;
            Assert.That(sky.r + sky.g + sky.b, Is.LessThan(0.6f), "night sky is dark");
            var lights = GameObject.Find("World/City/NightLights");
            Assert.That(lights, Is.Not.Null.And.Property("activeInHierarchy").True, "lantern lights on");
            Assert.That(lights.GetComponentsInChildren<Light>().Length, Is.GreaterThan(10));
            clock.Unpin();
            host.World.SetCityScene(false);
            host.World.SetCityScene(true);
            yield return null;
            host.World.SetCityScene(false);
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator TheVetWritesAPrescriptionThePharmacyFillsAndMedicineHeals()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var store = host.Store;
            var vet = Gooby.Core.City.DriveModel.Places.First(p => p.Id == "tierarzt");
            var pharmacy = Gooby.Core.City.DriveModel.Places.First(p => p.Id == "goobytheke");
            // a properly sick Gooby with money in the purse, no prescription and no medicine at home
            store.Update(s =>
            {
                J.EnsureDict(J.EnsureDict(s, "gooby"), "health")["state"] = "sick";
                s["economy"]["coins"] = 1000;
                J.EnsureDict(J.EnsureDict(s, "city"), "flags").Remove("rezept_tropfen");
                J.EnsureDict(J.EnsureDict(s, "inventory"), "items").Remove("medicine");
            });

            // the street: parked at the practice, the pill offers to go in; the vet's house wears its sign
            var drive = new CityDriveScreen(host);
            host.Ui.Push(drive);
            yield return null;
            host.World.Drive.X = vet.X; host.World.Drive.Z = vet.Z; host.World.Drive.Kmh = 0.0;
            drive.Braking = true;
            host.World.DriveStep(new Gooby.Core.City.DriveModel.Input { Brake = true }, 0.05f);
            drive.Update();
            yield return null;
            Assert.That(Gooby.Core.City.DriveModel.Reached(host.World.Drive)?.Id, Is.EqualTo("tierarzt"));
            Assert.That(drive.Root.Q("enter-place").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "enter button shows at the vet");
            var lettering = GameObject.Find("World/City").GetComponentsInChildren<Renderer>().Where(r => r.name == "Lettering").ToList();
            var signs = lettering.Select(r => r.sharedMaterial.name).ToList();
            Assert.That(signs, Does.Contain("decal:sign_tierarzt").And.Contain("decal:sign_goobytheke").And.Contain("decal:sign_gooby_mart"), "the shop fronts are lettered");
            foreach (var p in Gooby.Core.City.DriveModel.Places.Where(p => p.Side < 0))
                Assert.That(signs.Count(s => s == "decal:" + Gooby.Unity.World.CityScene.SignOf(p.Id)), Is.EqualTo(1), "exactly one lettered front for " + p.Id);
            Assert.That(lettering.All(r => r.sharedMaterial.mainTexture != null), Is.True, "every sign texture is generated (tools/art/build_art.py)");

            // Dr. Dr. Möhrchen: sick → examination (findings in the status line) → diagnosis → prescription
            var visit = new PlaceScreen(host, vet);
            host.Ui.Push(visit);
            yield return null;
            // the street keeps ticking under the sheet, but the car stays parked while we are inside
            var parkedZ = host.World.Drive.Z; var parkedX = host.World.Drive.X;
            for (var i = 0; i < 30; i++) drive.Update();
            Assert.That(host.World.Drive.Kmh, Is.EqualTo(0.0), "no cruising under an open sheet");
            Assert.That(host.World.Drive.Z, Is.EqualTo(parkedZ), "the car waits at the kerb");
            Assert.That(host.World.Drive.X, Is.EqualTo(parkedX));
            Assert.That(visit.Runner.Aktuell, Is.EqualTo("empfang"));
            Assert.That(visit.Runner.Optionen()[0].Next, Is.EqualTo("untersuchung"), "the sick branch is offered first");
            Assert.That(visit.Choose(0), Is.True);
            Assert.That(visit.Hint, Is.EqualTo(host.Strings.T("vet.untersuchung.horchen")), "the examination starts with the carrot stethoscope");
            visit.Next(); visit.Next(); visit.Next(); // three lines, then the node's own follow-up
            Assert.That(visit.Runner.Aktuell, Is.EqualTo("diagnose"));
            Assert.That(visit.Runner.Optionen().Select(o => o.Next), Is.EqualTo(new[] { "behandlung", "rezept", "ruhe" }));
            Assert.That(visit.Choose(1), Is.True);
            Assert.That(Gooby.Core.City.CityState.HasFlag(store, "rezept_tropfen"), Is.True, "the prescription is persisted in city.flags");
            Assert.That(visit.Hint, Is.EqualTo(host.Strings.T("vet.rezept_ok")));
            visit.Next(); visit.Next();
            Assert.That(visit.Runner.Aktuell, Is.EqualTo("nachsorge"));
            Assert.That(visit.Runner.IstEnde, Is.True);
            host.Ui.Pop();
            yield return null;

            // second visit: the paid cure heals completely and costs 120 coins (coinsSpent — an achievement for
            // the first cure may pay a reward on top of the balance)
            var spent = J.Int(J.Path(store.State, "economy", "coinsSpent"));
            var cure = new PlaceScreen(host, vet);
            host.Ui.Push(cure);
            yield return null;
            Assert.That(cure.Choose(0), Is.True);
            cure.Next(); cure.Next(); cure.Next();
            Assert.That(cure.Runner.Optionen().Select(o => o.Next), Is.EqualTo(new[] { "behandlung", "rezept_schon", "ruhe" }), "with a prescription the tree says so");
            Assert.That(cure.Choose(0), Is.True);
            Assert.That(J.Int(J.Path(store.State, "economy", "coinsSpent")), Is.EqualTo(spent + Gooby.Core.Life.Health.VetCurePrice), "the cure is billed");
            Assert.That(Gooby.Core.Life.Health.Grade(J.Path(store.State, "gooby", "health")), Is.EqualTo(0), "cured");
            Assert.That(cure.Hint, Is.EqualTo(host.Strings.T("vet.behandlung_ok")));
            host.Ui.Pop();
            yield return null;

            // Hilde at the GOOBYTHEKE redeems the prescription for a bottle of Gooby drops
            var hilde = new PlaceScreen(host, pharmacy);
            host.Ui.Push(hilde);
            yield return null;
            Assert.That(hilde.Runner.Optionen()[0].Next, Is.EqualTo("rezept_einloesen"));
            Assert.That(hilde.Choose(0), Is.True);
            Assert.That(Gooby.Core.City.CityState.HasFlag(store, "rezept_tropfen"), Is.False, "prescription used up");
            Assert.That(J.Int(J.Path(store.State, "inventory", "items", "medicine")), Is.EqualTo(1), "medicine in the bag");
            Assert.That(hilde.Hint, Is.EqualTo(host.Strings.T("nl.ort.item_erhalten")));
            host.Ui.Pop();
            yield return null;

            // the counter: three remedies, the drops locked without a prescription
            var counter = new ShopScreen(host, pharmacy);
            host.Ui.Push(counter);
            yield return null;
            var buttons = counter.Root.Query<Button>().ToList().Where(b => b.ClassListContains("gb-btn-sm")).ToList();
            Assert.That(buttons.Count, Is.EqualTo(3), "Hustensaft, Pflaster, Gooby-Tropfen");
            Assert.That(buttons.Last().text, Is.EqualTo(host.Strings.T("nl.shop.rezept_noetig")));
            Assert.That(buttons.Last().enabledSelf, Is.False);
            host.Ui.Pop();
            yield return null;
            host.Ui.Pop(); // the street
            yield return null;
            host.World.SetCityScene(false);

            // back home, Gooby falls ill again: the care sheet gives the medicine → queasy, bottle used
            store.Update(s => J.EnsureDict(J.EnsureDict(s, "gooby"), "health")["state"] = "sick");
            var care = new CareSheetScreen(host, _ => { });
            host.Ui.Push(care);
            yield return null;
            care.GiveMedicine();
            Assert.That(Gooby.Core.Life.Health.Grade(J.Path(store.State, "gooby", "health")), Is.EqualTo(1), "sick → queasy");
            Assert.That(J.Int(J.Path(store.State, "inventory", "items", "medicine")), Is.EqualTo(0));
            host.Ui.Pop();
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator BedsOnTheLawnShowPlantingWateringAndGrowth()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var world = host.World;
            host.Ui.Push(new SettingsScreen(host)); // keep the HUD's door/bed listeners out of the way
            yield return null;
            Assert.That(world.EnterRoom("garden"), Is.True);
            yield return null;
            Assert.That(world.Beds, Is.Not.Null);
            // start from a fresh field regardless of what earlier runs planted or built on it
            ResetGarden(host);
            world.RefreshBeds();
            yield return null;
            var tiles = world.Beds.Root.GetComponentsInChildren<Gooby.Unity.World.BedMarker>().Where(m => m.GetComponent<Renderer>() != null).ToArray();
            Assert.That(tiles.Length, Is.EqualTo(30), "a fresh garden has 6×5 beds");

            var bed = new Cell(2, 2);
            var grid = Gooby.Core.Garden.GardenState.Grid(host.Store);
            var sheet = new BedSheetScreen(host, bed, _ => { });
            host.Ui.Push(sheet);
            yield return null;
            Assert.That(sheet.Root.Q("crop-carrot"), Is.Not.Null, "empty bed offers the crops");
            host.Ui.Pop();

            var before = J.Int(J.Path(host.Store.State, "achievements", "counters", "waterings"));
            var nowS = host.Store.Clock.NowMs() / 1000.0;
            Assert.That(Gooby.Core.Garden.GardenState.Pflanzen(host.Store, bed, "carrot"), Is.True);
            Assert.That(Gooby.Core.Garden.GardenState.Giessen(host.Store, bed, nowS), Is.True);
            world.RefreshBeds();
            yield return null;
            Assert.That(J.Int(J.Path(host.Store.State, "achievements", "counters", "waterings")), Is.EqualTo(before + 1), "Gieß-Runde counter");
            var crops = world.Beds.Root.transform.Find("Crops");
            Assert.That(crops, Is.Not.Null);
            Assert.That(crops.Find("Crop:carrot"), Is.Not.Null, "the planted carrot stands on its bed");
            var tile = tiles.First(m => m.Bed.Equals(bed)).GetComponent<Renderer>();
            Assert.That(tile.sharedMaterial.color.r, Is.LessThan(0.4f), "watered soil is dark");

            sheet = new BedSheetScreen(host, bed, _ => { });
            host.Ui.Push(sheet);
            yield return null;
            Assert.That(sheet.Root.Q("water"), Is.Not.Null.And.Property("enabledSelf").False, "already watered → button disabled");
            Assert.That(sheet.Root.Q("harvest"), Is.Null, "not ripe yet");
            host.Ui.Pop();
            host.Ui.Pop();
            yield return null;
            world.EnterRoom("living");
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator StructuresStandOnTheFieldAndChangeWhatTheBedsDo()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var world = host.World;
            host.Ui.Push(new SettingsScreen(host));
            yield return null;
            Assert.That(world.EnterRoom("garden"), Is.True);
            yield return null;
            // a clean, rich player: fresh field, plenty of coins
            ResetGarden(host);
            host.Store.Update(s => s["economy"]["coins"] = 10000);
            world.RefreshBeds();
            yield return null;
            var coins = J.Int(J.Path(host.Store.State, "economy", "coins"));

            var anchor = new Cell(0, 0);
            var sheet = new BedSheetScreen(host, anchor, _ => { });
            host.Ui.Push(sheet);
            yield return null;
            var card = sheet.Root.Q("build-gewaechshaus");
            Assert.That(card, Is.Not.Null, "an empty bed offers the greenhouse");
            Assert.That(sheet.Root.Q("expand"), Is.Not.Null.And.Property("enabledSelf").True, "expansion is affordable");
            using (var e = ClickEvent.GetPooled()) { e.target = card; card.SendEvent(e); }
            yield return null;
            Assert.That(J.Int(J.Path(host.Store.State, "economy", "coins")), Is.EqualTo(coins - Gooby.Core.Garden.GardenWorld.Preis("gewaechshaus")), "the greenhouse was paid");
            var grid = Gooby.Core.Garden.GardenState.Grid(host.Store);
            Assert.That(grid.StructureAt(anchor)?.Kind, Is.EqualTo("gewaechshaus"));
            Assert.That(world.Beds.Root.transform.Find("Crops/Structure:gewaechshaus"), Is.Not.Null, "glass house stands on the field");
            host.Ui.Pop();

            // a bed under glass offers the exotics
            var inside = grid.GreenhouseCells().First();
            sheet = new BedSheetScreen(host, inside, _ => { });
            host.Ui.Push(sheet);
            yield return null;
            Assert.That(sheet.Root.Q("crop-ananas"), Is.Not.Null, "pineapple is plantable in the greenhouse");
            Assert.That(sheet.Root.Q("build-sprinkler"), Is.Null, "no building inside the greenhouse");
            host.Ui.Pop();

            // sprinkler keeps its neighbours wet, tree gives wood, field grows
            Assert.That(Gooby.Core.Garden.GardenWorld.Bauen(host.Store, "sprinkler", new Cell(4, 1)).Ok, Is.True);
            Assert.That(Gooby.Core.Garden.GardenWorld.Bauen(host.Store, "baum", new Cell(4, 4)).Ok, Is.True);
            world.RefreshBeds();
            yield return null;
            var tiles = world.Beds.Root.GetComponentsInChildren<Gooby.Unity.World.BedMarker>();
            var wet = tiles.First(m => m.Bed.Equals(new Cell(3, 2)) && m.GetComponent<Renderer>() != null).GetComponent<Renderer>();
            Assert.That(wet.sharedMaterial.color.r, Is.LessThan(0.4f), "soil next to the sprinkler reads wet");
            Assert.That(world.Beds.Root.transform.Find("Crops/Structure:sprinkler"), Is.Not.Null);
            Assert.That(world.Beds.Root.transform.Find("Crops/Structure:baum"), Is.Not.Null);
            sheet = new BedSheetScreen(host, new Cell(4, 4), _ => { });
            host.Ui.Push(sheet);
            yield return null;
            var wood = sheet.Root.Q("wood");
            Assert.That(wood, Is.Not.Null.And.Property("enabledSelf").True, "a fresh tree is ripe");
            sheet.HarvestWood(new Cell(4, 4));
            yield return null;
            Assert.That(sheet.Root.Q("wood"), Is.Not.Null.And.Property("enabledSelf").False, "the tree regrows for two days");
            Assert.That(Gooby.Core.Garden.GardenWorld.Holz(host.Store), Is.EqualTo(Gooby.Core.Garden.GardenWorld.BaumHolz));
            host.Ui.Pop();

            // fence parts from the workbench: the bed sheet sets a fence edge along the bed's north side (garden_world.gd zaun_setzen)
            sheet = new BedSheetScreen(host, new Cell(1, 3), _ => { });
            host.Ui.Push(sheet);
            yield return null;
            Assert.That(sheet.Root.Q("build-zaun"), Is.Not.Null);
            Assert.That(sheet.Root.Q("build-zaun").ClassListContains("gb-outfit-card--locked"), Is.True, "without parts the fence card is locked");
            sheet.BuildFence();
            yield return null;
            Assert.That(Gooby.Core.Garden.GardenState.Grid(host.Store).Edges.Count, Is.EqualTo(0), "no parts, no fence");
            Assert.That(HomeState.StoreItem(host.Store, Gooby.Core.Garden.GardenWorld.ZaunItem), Is.True);
            sheet.BuildFence();
            yield return null;
            var edges = Gooby.Core.Garden.GardenState.Grid(host.Store).Edges;
            Assert.That(edges.Count, Is.EqualTo(1));
            Assert.That(edges[0].From, Is.EqualTo(new Cell(1, 3)));
            Assert.That(world.Beds.Root.transform.Find("Crops/Fence:1:3:E:1"), Is.Not.Null, "the fence run stands on the field");
            Assert.That(Gooby.Core.Garden.GardenWorld.Zaunteile(host.Store), Is.EqualTo(0));
            host.Ui.Pop();
            yield return null;

            // a path cell (garden_grid.gd kind "path") is flagstone, not a bed: no tap target
            host.Store.Update(s => { var cells = J.Path(s, "home", "garden", "cells") as Newtonsoft.Json.Linq.JArray; cells.Add(new Newtonsoft.Json.Linq.JObject { ["at"] = new Newtonsoft.Json.Linq.JArray(2, 3), ["kind"] = "path" }); });
            world.RefreshBeds();
            yield return null;
            var pathTile = world.Beds.Root.GetComponentsInChildren<Gooby.Unity.World.BedMarker>(true).First(m => m.Bed.Equals(new Cell(2, 3)) && m.GetComponent<Renderer>() != null);
            Assert.That(pathTile.GetComponent<BoxCollider>().enabled, Is.False, "a path is not tappable as a bed");
            Assert.That(pathTile.GetComponent<Renderer>().sharedMaterial.color.g, Is.GreaterThan(0.6f), "flagstone, not soil");

            Assert.That(Gooby.Core.Garden.GardenWorld.Erweitern(host.Store).Ok, Is.True);
            world.RefreshBeds();
            yield return null;
            Assert.That(world.Beds.Root.GetComponentsInChildren<Gooby.Unity.World.BedMarker>().Count(m => m.GetComponent<Renderer>() != null), Is.EqualTo(48), "8×6 beds after the first expansion");
            ResetGarden(host); // leave a fresh field for the other flows (the save persists between runs)
            world.RefreshBeds();
            host.Ui.Pop();
            yield return null;
            world.EnterRoom("living");
        }

        /// <summary>Puts the GardenState slice back to a fresh 6×5 field without crops or structures.</summary>
        private static void ResetGarden(GameHost host)
        {
            host.Store.Update(s => s["home"]["garden"] = Gooby.Core.Garden.GardenState.DefaultGarden());
            host.Store.NotifySliceChanged("home");
        }

        private static Vector3 SimpleWorldArrival(RoomDefs.Room def, Vector3 origin, double gx, double gy) =>
            Gooby.Unity.World.SimpleWorld.WorldOf(def, origin, gx, gy);

        [UnityTest]
        public IEnumerator BuildModePlacesAStorageItemIntoTheSavedRoom()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var session = host.World.BeginBuild(host.Store);
            var storage = HomeState.Storage(host.Store);
            Assert.That(storage.Count, Is.GreaterThan(0), "default storage has items");
            // surface items (small plants) need a table → pick something that stands on the lawn
            var floorRow = storage.FirstOrDefault(r => FurnitureCatalog.Def(J.Str(r["item"]))?.Layer == GridLayer.Floor || FurnitureCatalog.Def(J.Str(r["item"]))?.Layer == GridLayer.Rug);
            Assert.That(floorRow, Is.Not.Null, "storage holds a floor/rug item");
            var itemId = J.Str(floorRow["item"]);
            var before = StorageLogic.CountOf(storage, itemId);
            var itemsBefore = HomeState.RoomItems(host.Store, "garden").Count;
            session.Select(itemId);
            var placed = false;
            foreach (var cell in session.Grid.FreeCells())
            {
                if (session.Place(cell)) { placed = true; break; }
            }
            Assert.That(placed, Is.True, "a free cell accepts " + itemId + " (last message: " + session.LastMessage + ", free cells: " + session.Grid.FreeCells().Count + ")");
            yield return null;
            Assert.That(StorageLogic.CountOf(HomeState.Storage(host.Store), itemId), Is.EqualTo(before - 1));
            Assert.That(HomeState.RoomItems(host.Store, "garden").Count, Is.EqualTo(itemsBefore + 1), "grid persisted immediately");
            Assert.That(session.Undo(), Is.True);
            Assert.That(StorageLogic.CountOf(HomeState.Storage(host.Store), itemId), Is.EqualTo(before), "undo returns it to storage");
            Assert.That(HomeState.RoomItems(host.Store, "garden").Count, Is.EqualTo(itemsBefore));
            host.World.EndBuild(commit: true);
            host.World.SetBuildCamera(false);
        }

        [UnityTest]
        public IEnumerator AMinigameRoundBooksCoinsAndPlays()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var game = new CarrotCatchGame("normal", 42);
            var steps = 0;
            while (!game.Finished && steps++ < 60 * 90)
            {
                CarrotCatchGame.FallingItem target = null;
                foreach (var it in game.Items) if (it.Kind == "good" && (target == null || it.Y > target.Y)) target = it;
                if (target != null) game.TargetX = target.X;
                game.Tick(1.0 / 60.0);
            }
            Assert.That(game.Finished, Is.True);
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            var playsBefore = J.Int(J.Path(host.Store.State, "minigames", "plays", "carrotCatch"));
            AwardResult award = null;
            host.Store.Update(s => award = MinigameAward.Award(s, MinigameRegistry.GetGame("carrotCatch"), game.FinalScore, "normal", host.Store.Clock.LocalDay()));
            Assert.That(award.Coins, Is.GreaterThan(0));
            // every coin row the award books: the round's pay, level-ups, modifier/Glücksrolle bonuses and the arcade spotlight's bonus
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.EqualTo(coinsBefore + award.Coins + award.CoinsFromLevels + award.ModifierBonusCoins + award.GluecksrolleCoins + award.SpotlightBonusCoins));
            Assert.That(J.Int(J.Path(host.Store.State, "minigames", "plays", "carrotCatch")), Is.EqualTo(playsBefore + 1));
            Assert.That(host.Store.SaveNow(), Is.True, "save file written");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ABubblePopRoundRunsOnItsHostAndBooksTheAward()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            // awake, and with the day's minigame coin cap untouched (the save persists between runs)
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            var screen = new BubblePopScreen(host);
            host.Ui.Push(screen);
            yield return null;
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.Play), Is.Not.Null, "pregame card with Play");
            Assert.That(MinigameHostScreen.Hosted, Does.Contain("bubblePop"));
            Assert.That(MinigameHostScreen.For(host, "bubblePop"), Is.TypeOf<BubblePopScreen>());
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null, "the round runs");
            Assert.That(screen.Root.Q("play-field"), Is.Not.Null);
            Assert.That(screen.Root.Q("target-pill"), Is.Not.Null, "the target food badge");
            // the intro beat passes on the frame's clock; then a planted target bubble under the finger pops for +2
            for (var i = 0; i < 40; i++) game.Tick(0.05);
            Assert.That(game.IntroLeft, Is.EqualTo(0.0));
            game.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = game.Target, X = 0.0, Y = 0.0 });
            Assert.That(game.TapNormalized(0.5, 0.5), Is.EqualTo("match"));
            Assert.That(game.Score, Is.EqualTo(2));
            yield return null;
            // the field shows one element per live bubble (children beyond the seabed, weeds and background dots)
            var field = screen.Root.Q("play-field");
            var childrenNow = field.childCount;
            for (var i = 0; i < 30; i++) game.Tick(0.05);   // 1.5 s → at least one spawn
            yield return null;
            Assert.That(field.childCount, Is.GreaterThanOrEqualTo(childrenNow + 1), "new bubbles get their view");
            // fast-forward to the end of the timed round → the results card books the award
            while (!game.Finished) game.Tick(0.5);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null, "results card after the round");
            Assert.That(screen.Game, Is.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThanOrEqualTo(coinsBefore + 4), "coin row floor of Blasen-Platzer is 4");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        /// <summary>Presses a frame button by its stable name the way the QA driver clicks (NavigationSubmit → clicked).</summary>
        private static void Press(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, "button " + name);
            Assert.That(button.enabledInHierarchy, Is.True, name + " is enabled");
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); }
        }

        private static IEnumerator WaitForResults(MinigameHostScreen screen)
        {
            var t = 0f;
            while (screen.Root.Q(MinigameHostScreen.ElementNames.Results) == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.Results), Is.Not.Null, "results card after the end moment");
        }

        private static int Plays(GameHost host, string gameId) => J.Int(J.Path(host.Store.State, "minigames", "plays", gameId));

        [UnityTest]
        public IEnumerator TheFramePausesRetriesAndGivesUpBackToTheArcadeBookingEachFinishedRoundOnce()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var arcade = new ArcadeScreen(host);
            host.Ui.Push(arcade);
            yield return null;
            var card = arcade.Root.Q("arcade-bubblePop");
            Assert.That(card, Is.Not.Null, "the arcade shows a card per hosted game");
            using (var e = ClickEvent.GetPooled()) { e.target = card; card.SendEvent(e); }
            yield return null;
            var screen = host.Ui.Current as BubblePopScreen;
            Assert.That(screen, Is.Not.Null, "the card opens the host's pregame over the arcade");
            var depth = host.Ui.Depth;
            var plays = Plays(host, "bubblePop");

            // Spielen! → the round starts and 3-2-1 runs inside the engine's intro lock
            Press(screen.Root, MinigameHostScreen.ElementNames.Play);
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null, "Spielen! starts the round");
            Assert.That(game.IntroLeft, Is.GreaterThan(0.0), "one frame in, the intro still runs");
            var countdown = screen.Root.Q<Label>(MinigameHostScreen.ElementNames.Countdown);
            Assert.That(countdown, Is.Not.Null);
            Assert.That(countdown.style.display.value, Is.EqualTo(DisplayStyle.Flex), "the countdown stands over the field");

            // Pause: the modal opens, the round stands still, Hilfe shows the game's hint, Weiter closes it
            Press(screen.Root, MinigameHostScreen.ElementNames.Pause);
            yield return null;
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.PauseModal), Is.Not.Null, "Pause opens the modal");
            var introAtPause = game.IntroLeft;
            var elapsedAtPause = game.Elapsed;
            for (var i = 0; i < 6; i++) yield return null;
            Assert.That(game.IntroLeft, Is.EqualTo(introAtPause), "paused: the frame does not tick the round");
            Assert.That(game.Elapsed, Is.EqualTo(elapsedAtPause));
            Press(screen.Root, MinigameHostScreen.ElementNames.Help);
            yield return null;
            var help = screen.Root.Q<Label>(MinigameHostScreen.ElementNames.HelpText);
            Assert.That(help.style.display.value, Is.EqualTo(DisplayStyle.Flex), "Hilfe opens the hint");
            Assert.That(help.text, Is.EqualTo(host.Strings.T("mg.bubblePop.hint")));
            Press(screen.Root, MinigameHostScreen.ElementNames.Resume);
            yield return null;
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.PauseModal), Is.Null, "Weiter closes the modal");

            // Pause → Neustart: a fresh round, the dropped one books nothing
            Press(screen.Root, MinigameHostScreen.ElementNames.Pause);
            yield return null;
            Press(screen.Root, MinigameHostScreen.ElementNames.Restart);
            yield return null;
            Assert.That(screen.Game, Is.Not.Null.And.Not.SameAs(game), "Neustart deals a fresh round");
            Assert.That(Plays(host, "bubblePop"), Is.EqualTo(plays), "a dropped round books nothing");
            game = screen.Game;

            // a round with points runs out of time: the end moment stamps "Zeit um!", then the results card books it once
            while (game.IntroLeft > 0.0) game.Tick(0.05);
            game.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = game.Target, X = 0.0, Y = 0.0 });
            Assert.That(game.TapNormalized(0.5, 0.5), Is.EqualTo("match"));
            while (!game.Finished) game.Tick(0.5);
            yield return null;   // exactly one frame: the end moment is up, the results card needs ≥ 0,5 s more
            var stamp = screen.Root.Q<Label>(MinigameHostScreen.ElementNames.Stamp);
            Assert.That(stamp, Is.Not.Null);
            Assert.That(stamp.style.display.value, Is.EqualTo(DisplayStyle.Flex), "the end moment stamps the frozen field");
            Assert.That(stamp.text, Is.EqualTo(host.Strings.T("mgp.end.time_up")), "the clock ran out");
            Assert.That(stamp.ClassListContains("gb-stamp--lose"), Is.False, "a round with points is celebrated");
            Assert.That(screen.Root.Q<Button>(MinigameHostScreen.ElementNames.Pause).enabledSelf, Is.False, "no pause during the end moment");
            Assert.That(Plays(host, "bubblePop"), Is.EqualTo(plays), "booked with the results card, not before");
            yield return WaitForResults(screen);
            Assert.That(Plays(host, "bubblePop"), Is.EqualTo(plays + 1), "the finished round is booked once");
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.ResultsStars), Is.Not.Null);
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.Home), Is.Not.Null);
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.Arcade), Is.Not.Null, "opened from the arcade: Zur Arcade");

            // Nochmal: the next round of the same difficulty right away, no second booking
            Press(screen.Root, MinigameHostScreen.ElementNames.Again);
            yield return null;
            Assert.That(screen.Game, Is.Not.Null.And.Not.SameAs(game), "Nochmal starts the next round");
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.Results), Is.Null);
            Assert.That(host.Ui.Current, Is.SameAs(screen));
            Assert.That(Plays(host, "bubblePop"), Is.EqualTo(plays + 1));

            // Pause → Aufgeben: the round is dropped and the host pops back onto the arcade
            Press(screen.Root, MinigameHostScreen.ElementNames.Pause);
            yield return null;
            Press(screen.Root, MinigameHostScreen.ElementNames.GiveUp);
            yield return null;
            Assert.That(host.Ui.Current, Is.SameAs(arcade), "Aufgeben goes back to the arcade");
            Assert.That(host.Ui.Depth, Is.EqualTo(depth - 1));
            Assert.That(Plays(host, "bubblePop"), Is.EqualTo(plays + 1), "a given-up round books nothing");
            Assert.That(arcade.Root.Q("arcade-bubblePop"), Is.Not.Null, "the arcade rebuilt its cards");
            host.Ui.Pop();
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AGameOpenedWithoutTheArcadeOffersNoArcadeButtonAndGoesHome()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
            // the way -gooby-open game:<id> and the QA driver open a game: straight over the HUD, no arcade underneath
            var screen = (BubblePopScreen)MinigameHostScreen.For(host, "bubblePop");
            host.Ui.Push(screen);
            yield return null;
            Press(screen.Root, MinigameHostScreen.ElementNames.Play);
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            while (!game.Finished) game.Tick(0.5);   // nothing popped: a round without points
            yield return null;
            var stamp = screen.Root.Q<Label>(MinigameHostScreen.ElementNames.Stamp);
            Assert.That(stamp.text, Is.EqualTo(host.Strings.T("mgp.end.time_up")));
            Assert.That(stamp.ClassListContains("gb-stamp--lose"), Is.True, "no points: the soft consolation stamp");
            yield return WaitForResults(screen);
            Assert.That(screen.Root.Q(MinigameHostScreen.ElementNames.Arcade), Is.Null, "no arcade underneath → no Zur Arcade");
            Press(screen.Root, MinigameHostScreen.ElementNames.Home);
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"), "Nach Hause lands on the HUD");
            Assert.That(host.Ui.Depth, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AMemoryRoundDealsCardsOnTheBlanketAndAPairStays()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "memoryMatch"), Is.TypeOf<MemoryMatchScreen>());
            var screen = new MemoryMatchScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            yield return null;   // a rendered frame lays the cards out
            // 4×4 up to level 5, 4×6 from level 6 — the save decides (it persists between runs)
            var n = game.Cards.Count;
            Assert.That(n, Is.EqualTo(game.Layout.Pairs * 2));
            Assert.That(n == 16 || n == 24, Is.True, "4×4 or 4×6");
            Assert.That(screen.Root.Q("card-0"), Is.Not.Null, "the cards lie on the blanket");
            Assert.That(screen.Root.Q("card-" + (n - 1)), Is.Not.Null);
            Assert.That(screen.Root.Q("card-" + n), Is.Null);
            Assert.That(screen.Root.Q("countdown").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "the memorise window runs");
            // past the intro and the reveal: a tap on a card flips it, its partner makes the pair
            for (var i = 0; i < 60; i++) game.Tick(0.05);
            Assert.That(game.Revealing, Is.False);
            var card0 = screen.Root.Q("card-0");
            using (var e = PointerDownEvent.GetPooled()) { e.target = card0; card0.SendEvent(e); }
            yield return null;
            Assert.That(game.Cards[0].State, Is.EqualTo(MemoryMatchGame.Up), "a pointer tap on the card flips it");
            var partner = System.Linq.Enumerable.Range(1, n - 1).First(i => game.Cards[i].Face == game.Cards[0].Face);
            Assert.That(game.Flip(partner), Is.True);
            for (var i = 0; i < 20; i++) game.Tick(0.05);
            Assert.That(game.Cards[0].State, Is.EqualTo(MemoryMatchGame.Matched));
            Assert.That(game.MatchedPairs, Is.EqualTo(1));
            yield return null;
            Assert.That(card0.resolvedStyle.opacity, Is.LessThan(0.8f), "a matched card fades on the blanket");
            // the rest of the pairs → the timed round ends and books the award
            for (var face = 0; face < game.Layout.Pairs; face++)
            {
                var first = System.Linq.Enumerable.Range(0, n).FirstOrDefault(i => game.Cards[i].Face == face && game.Cards[i].State == MemoryMatchGame.Down);
                if (game.Cards[first].State != MemoryMatchGame.Down) continue;
                game.Flip(first);
                game.Flip(System.Linq.Enumerable.Range(0, n).First(i => i != first && game.Cards[i].Face == face));
                for (var i = 0; i < 20; i++) game.Tick(0.05);
            }
            Assert.That(game.Finished, Is.True);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "Memory pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AGoobySaysRoundLightsThePadsAndTakesTheTapsBack()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "goobySays"), Is.TypeOf<GoobySaysScreen>());
            var screen = new GoobySaysScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("pad-3"), Is.Not.Null, "four pads on the stage");
            Assert.That(screen.Root.Q("state"), Is.Not.Null);
            // Gooby plays round 1 (intro, first-step delay, one step): the pad lights, then the input opens
            var lit = new System.Collections.Generic.List<int>();
            game.PadLit += (pad, partner) => lit.Add(pad);
            var guard = 0;
            while (game.Phase != GoobySaysGame.Input && guard++ < 400) game.Tick(0.05);
            Assert.That(game.Phase, Is.EqualTo(GoobySaysGame.Input));
            Assert.That(lit, Is.EqualTo(new[] { game.Sequence[0].First }));
            yield return null;
            Assert.That(screen.Root.Q<Label>("state").text, Does.StartWith(host.Strings.T("mg.goobySays.go")), "the state line hands the turn to the player");
            // a pointer tap on the right pad completes the round → +10 and a longer sequence
            var pad = screen.Root.Q("pad-" + game.Sequence[0].First);
            using (var e = PointerDownEvent.GetPooled()) { e.target = pad; pad.SendEvent(e); }
            yield return null;
            Assert.That(game.RoundsCompleted, Is.EqualTo(1));
            Assert.That(game.RoundNo, Is.EqualTo(2));
            Assert.That(game.Sequence.Count, Is.EqualTo(2));
            Assert.That(game.LiveScore, Is.GreaterThanOrEqualTo(10));
            // round 2: watch, then a wrong pad ends the round → results with the award
            guard = 0;
            while (game.Phase != GoobySaysGame.Input && guard++ < 400) game.Tick(0.05);
            var wrong = screen.Root.Q("pad-" + ((game.Sequence[0].First + 1) % 4));
            using (var e = PointerDownEvent.GetPooled()) { e.target = wrong; wrong.SendEvent(e); }
            yield return null;
            Assert.That(game.Finished, Is.True, "a wrong pad ends the round");
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "Gooby sagt pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ABunnyHopRunHopsThroughGatesAndEndsAtTheCrash()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "bunnyHop"), Is.TypeOf<BunnyHopScreen>());
            var screen = new BunnyHopScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            for (var i = 0; i < 40; i++) game.Tick(0.05);   // the intro beat
            Assert.That(game.Started, Is.False, "hovering until the first tap");
            // a pointer tap on the field starts the run and hops
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Started, Is.True, "a tap anywhere on the field hops");
            Assert.That(game.GoobyVy, Is.GreaterThan(0.0));
            // the autopilot of the Core tests flies a few gates so the frame shows hedges, then Gooby drops and crashes
            var passed = 0;
            game.GatePassed += (p, g) => passed += p;
            for (var s = 0.0; s < 12.0 && !game.Finished && game.Gates < 3; s += 1.0 / 60.0)
            {
                var next = System.Linq.Enumerable.FirstOrDefault(game.Pillars, p => p.X - game.Scroll > game.GoobyX - 0.7);
                var target = next?.GapCenterY ?? BunnyHopGame.HoverY;
                if (game.GoobyY < target - 0.15 && game.GoobyVy < 0.6) game.Hop();
                game.Tick(1.0 / 60.0);
            }
            yield return null;
            Assert.That(game.Gates, Is.GreaterThanOrEqualTo(1), "at least one gate passed");
            Assert.That(passed, Is.GreaterThanOrEqualTo(game.Gates));
            Assert.That(field.childCount, Is.GreaterThan(12), "hedge gates and coins have their elements");
            Assert.That(screen.Root.Q<Label>("gates").text, Is.EqualTo(host.Strings.T("mg.bunnyHop.gates", ("n", game.Gates))));
            while (!game.Finished) game.Tick(0.05);   // no more hops: gravity ends the run
            Assert.That(game.Crashed, Is.True);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThanOrEqualTo(coinsBefore + 3), "Hasenhüpfer's coin floor is 3");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ABurgerBuildRoundStacksTheTicketOnThePlate()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "burgerBuild"), Is.TypeOf<BurgerBuildScreen>());
            var screen = new BurgerBuildScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            yield return null;
            Assert.That(screen.Root.Q("plate"), Is.Not.Null);
            Assert.That(screen.Root.Q("ticket"), Is.Not.Null, "the order ticket hangs on the wall");
            Assert.That(screen.Root.Q("ticket-0"), Is.Not.Null, "with a row per layer");
            Assert.That(screen.Root.Q("ticket-" + (game.Ticket.Count - 1)), Is.Not.Null);
            // the plate follows the finger along the counter (the host maps the field's x to the engine)
            game.SetPlateFromNormalizedX(0.9);
            yield return null;
            yield return null;   // the style set in Update is laid out a frame later
            Assert.That(game.PlateX, Is.GreaterThan(1.5), "the plate followed the finger to the right");
            var plate = screen.Root.Q("plate");
            Assert.That(plate.resolvedStyle.left, Is.GreaterThan(screen.Root.Q("play-field").resolvedStyle.width * 0.5f), "and is drawn right of the middle");
            // the Core tests' cook lands the first layers; the stack shows a slab per placed layer
            for (var i = 0; i < 30; i++) game.Tick(0.05);   // the intro
            var guard = 0;
            while (game.Placed < 2 && !game.Finished && guard++ < 60 * 60)
            {
                var needed = game.NextNeeded;
                var wanted = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(game.Items, i => i.Id == needed), i => i.Y));
                if (wanted != null) game.SetPlateX(wanted.X); else game.SetPlateX(BurgerBuildGame.HalfW - 0.4);
                game.Tick(1.0 / 60.0);
            }
            yield return null;
            Assert.That(game.Placed, Is.GreaterThanOrEqualTo(2), "two layers landed");
            Assert.That(screen.Root.Q("layer-0"), Is.Not.Null, "the bun lies on the plate");
            Assert.That(screen.Root.Q("layer-1"), Is.Not.Null);
            Assert.That(game.Score, Is.GreaterThanOrEqualTo(2 * game.Tune.CatchPts - 4.0));
            while (!game.Finished) game.Tick(0.5);   // the 75 s run out
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "Burger-Bau pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ATeaPartyRoundPoursIntoTheBandAndServesTheCup()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "teaParty"), Is.TypeOf<TeaPartyScreen>());
            var screen = new TeaPartyScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("cup"), Is.Not.Null);
            Assert.That(screen.Root.Q("meter"), Is.Not.Null, "the meter with the band stands beside the cup");
            Assert.That(screen.Root.Q("pot"), Is.Not.Null);
            // intro and the first serve beat pass; a pointer down on the field starts the pour
            for (var i = 0; i < 50; i++) game.Tick(0.05);
            Assert.That(game.Ready, Is.True);
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Holding, Is.True, "finger down pours");
            // pour to the band's centre, then finger up serves a perfect cup
            while (game.Level + game.Tune.FillRate * 0.02 <= game.Band.Center && game.Holding) game.Tick(0.02);
            using (var e = PointerUpEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Holding, Is.False);
            Assert.That(game.Cups, Is.EqualTo(1), "the cup was served");
            Assert.That(game.Score, Is.EqualTo(game.Tune.PerfectPts), "a perfect pour pays 6");
            Assert.That(game.Streak, Is.EqualTo(1));
            yield return null;
            Assert.That(screen.Root.Q<Label>("cups").text, Is.EqualTo(host.Strings.T("mg.teaParty.cups", ("n", 1))));
            while (!game.Finished) game.Tick(0.5);   // the 60 s run out
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "the Teestube pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ACarrotGuardRoundBonksAMoleOnItsMound()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "carrotGuard"), Is.TypeOf<CarrotGuardScreen>());
            var screen = new CarrotGuardScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("mound-0"), Is.Not.Null);
            Assert.That(screen.Root.Q("mound-8"), Is.Not.Null, "nine mounds");
            Assert.That(screen.Root.Q("carrots"), Is.Not.Null);
            // the intro passes and the first mole pops up; a pointer tap on its mound bonks it for +1
            for (var i = 0; i < 40; i++) game.Tick(0.05);
            var guard = 0;
            while ((game.Moles.Count == 0 || game.Moles[0].Up < 1.0) && guard++ < 200) game.Tick(0.05);
            Assert.That(game.Moles.Count, Is.GreaterThan(0), "a mole is up");
            var hole = game.Moles[0].Hole;
            var mound = screen.Root.Q("mound-" + hole);
            using (var e = PointerDownEvent.GetPooled()) { e.target = mound; mound.SendEvent(e); }
            yield return null;
            Assert.That(game.Bonks, Is.EqualTo(1), "a tap on the mound bonks the mole");
            Assert.That(game.Score, Is.EqualTo(game.Tune.HitPoints));
            Assert.That(game.Combo, Is.EqualTo(1));
            yield return null;
            Assert.That(screen.Root.Q<Label>("carrots").text, Is.EqualTo(host.Strings.T("mg.carrotGuard.carrots", ("n", game.Carrots), ("max", game.Tune.Carrots))));
            while (!game.Finished) game.Tick(0.5);   // the 45 s run out (the idle bed loses carrots)
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "the Möhrenwache pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator APancakeTowerRoundStacksADroppedPancake()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "pancakeTower"), Is.TypeOf<PancakeTowerScreen>());
            var screen = new PancakeTowerScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("plate"), Is.Not.Null);
            Assert.That(screen.Root.Q("swing"), Is.Not.Null, "the pancake swings on the spatula");
            // the intro passes; a pointer tap on the field drops the pancake, it lands as layer 1
            for (var i = 0; i < 40; i++) game.Tick(0.05);
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Falling, Is.True, "a tap drops the pancake");
            var guard = 0;
            while (game.Falling && guard++ < 200) game.Tick(0.02);
            Assert.That(game.Layers.Count, Is.EqualTo(1), "it landed on the plate");
            Assert.That(game.Score, Is.GreaterThanOrEqualTo(game.Tune.PointsPerLayer));
            yield return null;
            Assert.That(screen.Root.Q("layer-1"), Is.Not.Null, "the first layer lies on the plate");
            Assert.That(screen.Root.Q<Label>("layers").text, Is.EqualTo(host.Strings.T("mg.pancakeTower.layers", ("n", 1))));
            // drop the rest at the far end of the swing until the tower topples → results and the award
            var rounds = 0;
            while (!game.Finished && rounds++ < 8)
            {
                guard = 0;
                while (System.Math.Abs(game.SwingX) < game.Tune.SlideAmplitude * 0.97 && guard++ < 600) game.Tick(1.0 / 60.0);
                game.Drop();
                guard = 0;
                while (game.Falling && guard++ < 600) game.Tick(1.0 / 60.0);
            }
            Assert.That(game.Finished, Is.True);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "the Pancake-Turm pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AGhostHuntRoundCatchesAGhostOnItsGrave()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "ghostHunt"), Is.TypeOf<GhostHuntScreen>());
            var screen = new GhostHuntScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("spot-0"), Is.Not.Null);
            Assert.That(screen.Root.Q("spot-11"), Is.Not.Null, "twelve spots in the graveyard");
            // the intro passes and the first ghost peeks out; a frame later its element stands, a tap on it catches
            for (var i = 0; i < 40; i++) game.Tick(0.05);
            var guard = 0;
            while (game.State.Ghosts.Count == 0 && guard++ < 100) game.Tick(0.05);
            Assert.That(game.State.Ghosts.Count, Is.GreaterThan(0), "a ghost is out");
            var ghost = game.State.Ghosts[0];
            yield return null;
            var gv = screen.Root.Q("ghost-" + ghost.Id);
            Assert.That(gv, Is.Not.Null, "the ghost has its sheet on the field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = gv; gv.SendEvent(e); }
            yield return null;
            Assert.That(game.State.Caught, Is.EqualTo(1), "a tap on the sheet catches the ghost");
            Assert.That(game.Score, Is.EqualTo(game.Tune.CatchPoints));
            yield return null;
            Assert.That(screen.Root.Q("ghost-" + ghost.Id), Is.Null, "the caught ghost is gone");
            // a tap on the empty lawn only breaks the chain
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.State.Chain, Is.EqualTo(0));
            Assert.That(game.Score, Is.EqualTo(game.Tune.CatchPoints), "a miss costs nothing");
            while (!game.Finished) game.Tick(0.5);   // the 90 s run out
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "the Geisterjagd pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AStarHopperRunHopsLanesAndEndsAtAMeteor()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "starHopper"), Is.TypeOf<StarHopperScreen>());
            var screen = new StarHopperScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("ship"), Is.Not.Null, "the rocket flies");
            for (var i = 0; i < 40; i++) game.Tick(0.05);
            Assert.That(game.Lane, Is.EqualTo(1));
            // a tap on the left half of the field hops one lane to the left
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            using (var e = PointerUpEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Lane, Is.EqualTo(0), "a tap (no swipe distance) on the left half hops left");
            Assert.That(game.Swipe("right"), Is.True);
            Assert.That(game.Lane, Is.EqualTo(2));
            game.Tick(0.05);
            yield return null;
            Assert.That(field.childCount, Is.GreaterThan(45), "stars, lanes, meteors and the ship have their elements");
            Assert.That(screen.Root.Q<Label>("distance").text, Is.EqualTo(host.Strings.T("mg.starHopper.distance", ("m", (int)game.Traveled))));
            while (!game.Finished) game.Tick(0.05);   // sooner or later a meteor sits in the lane
            Assert.That(game.Crashed || game.TimeLeft <= 0.0, Is.True);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "the Sternenhüpfer pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AVeggieChopSwipeSplitsAPieceAndThreeDroppedVeggiesEndTheRound()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "veggieChop"), Is.TypeOf<VeggieChopScreen>());
            var screen = new VeggieChopScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("miss-pill"), Is.Not.Null, "the misses pill sits under the HUD");
            var dressing = field.childCount;
            Assert.That(dressing, Is.GreaterThanOrEqualTo(13), "tiles, shelf, jars, herb, window, counter, board, pan, Gooby and the veil dress the kitchen");
            // through the intro, then into the first wave
            game.Tick(VeggieChopGame.IntroSec + 0.01);
            for (var i = 0; i < 24 && game.Items.Count == 0; i++) game.Tick(0.05);
            Assert.That(game.Items.Count, Is.GreaterThan(0));
            yield return null;
            Assert.That(field.childCount, Is.EqualTo(dressing + game.Items.Count), "one element per flying piece");
            // a finger stroke through the first veggie in the air: the knife
            VeggieChopGame.Flying veg = null;
            for (var i = 0; i < 400 && veg == null; i++)
            {
                game.Tick(0.02);
                foreach (var e in game.Items) if (e.Item.Kind == "veggie" && e.Y > VeggieChopGame.GroundY + 0.6) { veg = e; break; }
            }
            Assert.That(veg, Is.Not.Null);
            var (nx, ny) = game.ToView(veg.X, veg.Y);
            var before = game.Score;
            Assert.That(screen.Swipe((float)nx, (float)ny - 0.05f, (float)nx, (float)ny + 0.05f), Is.EqualTo(1));
            Assert.That(game.Score, Is.EqualTo(before + 2));
            yield return null;
            Assert.That(field.childCount, Is.GreaterThan(dressing + game.Items.Count), "the two halves and the juice fly");
            // let the rest fall: three uncut veggies end the round early
            while (!game.Finished) game.Tick(0.05);
            Assert.That(game.Misses, Is.EqualTo(3));
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(screen.Root.Q("miss-pill"), Is.Null, "the pill leaves with the round");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ABasketBounceFlickThrowsTheBallAndASolvedShotDropsThroughTheNet()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(MinigameHostScreen.For(host, "basketBounce"), Is.TypeOf<BasketBounceScreen>());
            var screen = new BasketBounceScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("court"), Is.Not.Null, "the court canvas is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null, "Gooby stands beside the spot");
            game.Tick(BasketBounceGame.IntroSec + 0.01);
            Assert.That(game.Aiming, Is.True);
            // a flick up the field: the finger lands through the pointer event (at the field's origin), then races upwards
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            var pressed = game.Dragging;
            Assert.That(pressed, Is.True, "the pointer down starts the drag");
            for (var i = 1; i <= 5; i++)
            {
                game.Tick(1.0 / 60.0);
                game.Drag(0.0, -i * 26.0);
            }
            Assert.That(game.EndDrag(), Is.True, "a 1560 px/s flick throws");
            Assert.That(game.Phase, Is.EqualTo(BasketBounceGame.PhaseFly));
            while (game.Phase == BasketBounceGame.PhaseFly) game.Tick(1.0 / 60.0);
            Assert.That(game.Shots, Is.EqualTo(1));
            yield return null;
            // the rules solve a shot that drops in
            for (var i = 0; i < 200 && !game.Aiming; i++) game.Tick(1.0 / 60.0);
            var baskets = game.Baskets;
            Assert.That(game.ShootAtHoop(), Is.True);
            while (game.Phase == BasketBounceGame.PhaseFly) { game.Tick(1.0 / 60.0); }
            yield return null;
            Assert.That(game.Baskets, Is.EqualTo(baskets + 1));
            Assert.That(game.Score, Is.GreaterThanOrEqualTo(3));
            Assert.That(field.childCount, Is.GreaterThanOrEqualTo(3), "court, Gooby and the +points float");
            // through the rest of the minute
            while (!game.Finished) game.Tick(0.05);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(J.Int(host.Store.State["economy"]["coins"]), Is.GreaterThan(coinsBefore), "the Korbwurf pays its coin row");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AFishingPondHoldLowersTheHookAndALandedFishGoesIntoTheAlbum()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "fishingPond"), Is.TypeOf<FishingPondScreen>());
            var screen = new FishingPondScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("pond"), Is.Not.Null, "the pond canvas is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null, "Gooby sits on the jetty");
            Assert.That(screen.Root.Q("reel-meter").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "no fight yet");
            Assert.That(game.Fish.Count, Is.EqualTo(7));
            game.Tick(FishingPondGame.IntroSec + 0.01);
            // holding the field lowers the hook, letting go raises it
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(FishingPondGame.PhaseLower));
            game.Tick(0.5);
            Assert.That(game.HookDepth, Is.GreaterThan(1.0));
            // steer the release onto a swimmer: put a small fish right under the hook, then let go
            var small = game.Fish.Find(f => f.Kind == "S" || f.Kind == "M");
            Assert.That(small, Is.Not.Null);
            small.X = game.Tune.HookX; small.Depth = game.HookDepth;
            var scoreBefore = game.Score;
            using (var e = PointerUpEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Score, Is.EqualTo(scoreBefore + (small.Kind == "S" ? 2 : 3)));
            Assert.That(game.Phase, Is.EqualTo(FishingPondGame.PhaseRaise));
            Assert.That(game.CaughtSpecies, Has.Member(small.Species));
            Assert.That(field.childCount, Is.GreaterThanOrEqualTo(3), "pond, Gooby and the +points float");
            // a big one fights: the reel meter shows and five cranks land it
            for (var i = 0; i < 300 && game.Phase != FishingPondGame.PhaseIdle; i++) game.Tick(1.0 / 60.0);
            var big = game.Fish.Find(f => f.Kind == "L");
            if (big == null) { big = game.Fish[0]; big.Kind = "L"; big.Species = "bigWhopper"; big.CollectionId = "bigWhopper"; }
            game.Press();
            game.Tick(0.3);
            big.X = game.Tune.HookX; big.Depth = game.HookDepth;
            game.Release();
            Assert.That(game.Phase, Is.EqualTo(FishingPondGame.PhaseReel));
            yield return null;
            Assert.That(screen.Root.Q("reel-meter").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "the meter is up during the fight");
            scoreBefore = game.Score;
            for (var tap = 0; tap < 5; tap++) { game.Press(); game.Tick(0.1); }
            Assert.That(game.Score, Is.EqualTo(scoreBefore + 5));
            // through the rest of the round; the album gets the species
            var before = Gooby.Core.Meta.CollectionsLogic.CountOf(host.Store.State["collections"] as JObject, "fish", small.CollectionId);
            while (!game.Finished) game.Tick(0.1);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(Gooby.Core.Meta.CollectionsLogic.CountOf(host.Store.State["collections"] as JObject, "fish", small.CollectionId), Is.GreaterThan(before), "the landed fish is in the album");
            Assert.That(Gooby.Core.Meta.CollectionsLogic.CountOf(host.Store.State["collections"] as JObject, "fish", "bigWhopper"), Is.GreaterThanOrEqualTo(1));
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ATrampolineTapInTheWindowBoostsAndASwipeInTheAirIsATrick()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "trampoline"), Is.TypeOf<TrampolineScreen>());
            var screen = new TrampolineScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("hall"), Is.Not.Null, "the hall canvas is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("mult-pill"), Is.Not.Null);
            game.Tick(TrampolineGame.IntroSec + 0.01);
            // the field's pointer down is the landing tap: fall into the window, then press
            for (var i = 0; i < 4000 && !game.InWindow; i++) game.Tick(1.0 / 120.0);
            Assert.That(game.InWindow, Is.True);
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Armed, Is.EqualTo("boost"));
            using (var e = PointerUpEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            var vyBefore = game.Vy;
            var boosted = false;
            game.Boosted += () => boosted = true;
            for (var i = 0; i < 4000 && !boosted; i++) game.Tick(1.0 / 120.0);
            Assert.That(boosted, Is.True);
            Assert.That(game.Vy, Is.GreaterThan(5.0));
            yield return null;
            // a swipe high in the air is a trick worth its points × the tier
            game.Tick(0.1);
            var scoreBefore = game.Score;
            Assert.That(game.Swipe(-120, 0), Is.EqualTo("flip"));
            Assert.That(game.Score, Is.EqualTo(scoreBefore + 2 * game.Multiplier));
            yield return null;
            Assert.That(field.childCount, Is.GreaterThanOrEqualTo(3), "hall, Gooby and the trick float");
            // through the rest of the minute
            while (!game.Finished) game.Tick(0.05);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(screen.Root.Q("mult-pill"), Is.Null, "the pill leaves with the round");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AGoalieGoobyDiveIntoTheTelegraphedLaneSavesAndThreeGoalsEndTheRound()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "goalieGooby"), Is.TypeOf<GoalieGoobyScreen>());
            var screen = new GoalieGoobyScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(field.resolvedStyle.width, Is.GreaterThan(field.resolvedStyle.height), "the goal is a landscape field");
            Assert.That(screen.Root.Q("pitch"), Is.Not.Null, "the pitch canvas is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("saves-pill"), Is.Not.Null);
            game.Tick(GoalieGoobyGame.IntroSec + 0.01);
            for (var i = 0; i < 200 && game.Current == null; i++) game.Tick(1.0 / 60.0);
            var kick = game.Current;
            Assert.That(kick, Is.Not.Null, "the first kick is telegraphed");
            // a tap (down and up on the spot) through the field is a dive to the middle
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            using (var e = PointerUpEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Dive, Is.Not.Null);
            Assert.That(game.Dive.Lane, Is.EqualTo(2));
            // the proper dive, timed inside the hold window
            while (game.ArriveT - game.Elapsed > 0.2) game.Tick(1.0 / 60.0);
            var dy = kick.Kind == "lob" ? -80.0 : kick.Kind == "roller" ? 80.0 : 20.0;
            var ratio = kick.Lane == 2 ? 0.0 : kick.Lane == 1 || kick.Lane == 3 ? System.Math.Tan(36.0 * System.Math.PI / 180.0) : System.Math.Tan(70.0 * System.Math.PI / 180.0);
            game.DiveBy((kick.Lane < 2 ? -1.0 : 1.0) * ratio * System.Math.Abs(dy), dy);
            while (game.Current == kick) game.Tick(1.0 / 60.0);
            Assert.That(game.Saves, Is.EqualTo(1));
            Assert.That(game.Score, Is.GreaterThanOrEqualTo(4));
            yield return null;
            // then nobody dives: three goals end the round early
            while (!game.Finished) game.Tick(0.05);
            Assert.That(game.Goals, Is.EqualTo(3));
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ADancePartyTapOnTheLineIsPerfectAndTheChartRunsItsSeventyFiveSeconds()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "danceParty"), Is.TypeOf<DancePartyScreen>());
            var screen = new DancePartyScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("club"), Is.Not.Null, "the club canvas is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("combo-pill"), Is.Not.Null);
            Assert.That(game.InLeadIn, Is.True);
            // a pointer down on the field is a lane tap (at the field's origin: lane 0) — nothing in reach yet
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Score, Is.EqualTo(0));
            // the first note, tapped on the line
            var first = game.Notes[0];
            while (game.SongTime < first.Time - 0.02) game.Tick(System.Math.Min(1.0 / 120.0, first.Time - 0.02 - game.SongTime));
            Assert.That(screen.TapLane(first.Lane), Is.EqualTo("perfect"));
            Assert.That(game.Score, Is.EqualTo(4));
            yield return null;
            Assert.That(field.childCount, Is.GreaterThanOrEqualTo(2), "club canvas and Gooby");
            // the rest runs down; misses cost, the chart ends at 75 s
            while (!game.Finished) game.Tick(0.05);
            Assert.That(game.SongTime, Is.GreaterThanOrEqualTo(75.0));
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            Assert.That(screen.Root.Q("combo-pill"), Is.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AGardenRushHoldOnAPotFillsTheRingAndTheReleaseWatersTheSprout()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "gardenRush"), Is.TypeOf<GardenRushScreen>());
            var screen = new GardenRushScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("bed"), Is.Not.Null, "the bed canvas is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("sprinkler").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "no sprinkler before 30 s");
            for (var i = 0; i < 8; i++) Assert.That(screen.Root.Q("pot-" + i), Is.Not.Null);
            // the first sprout; its pot's hit area takes the finger, the field's pointer up lets go
            for (var i = 0; i < 200 && game.Pots.FindIndex(p => p.State == GardenRushGame.Sprout) < 0; i++) game.Tick(1.0 / 60.0);
            var idx = game.Pots.FindIndex(p => p.State == GardenRushGame.Sprout);
            Assert.That(idx, Is.GreaterThanOrEqualTo(0));
            var hit = screen.Root.Q("pot-" + idx);
            using (var e = PointerDownEvent.GetPooled()) { e.target = hit; hit.SendEvent(e); }
            yield return null;
            Assert.That(game.Holding, Is.True);
            Assert.That(game.HoldIndex, Is.EqualTo(idx));
            for (var i = 0; i < 44; i++) game.Tick(1.0 / 60.0);
            Assert.That(game.HoldInPerfectZone, Is.True);
            using (var e = PointerUpEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Holding, Is.False);
            Assert.That(game.Score, Is.EqualTo(3), "let go in the green: perfect");
            Assert.That(field.childCount, Is.GreaterThanOrEqualTo(11), "canvas, eight hit areas, sprinkler, Gooby and the float");
            // the sprinkler shows at 30 s and fires once
            while (game.Elapsed < 30.1 && !game.Finished) game.Tick(0.05);
            yield return null;
            Assert.That(screen.Root.Q("sprinkler").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(game.FireSprinkler(), Is.True);
            yield return null;
            Assert.That(screen.Root.Q("sprinkler").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            while (!game.Finished) game.Tick(0.05);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ALanternFloatPilotSteersThroughRingsAfterTheIntroBeat()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "lanternFloat"), Is.TypeOf<LanternFloatScreen>());
            var screen = new LanternFloatScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("night-sky"), Is.Not.Null, "the night sky is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("stats-pill"), Is.Not.Null);
            Assert.That(game.IntroLeft, Is.GreaterThan(0.0));
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.SteerTarget, Is.EqualTo(0.0), "the intro still gates the drag");
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            var first = game.Rings[0];
            screen.AimAtWorldX(first.X);
            while (!first.Done && !game.Finished) game.Tick(1.0 / 60.0);
            Assert.That(game.Hits, Is.GreaterThan(0), "aimed at ring 0 as it passed");
            yield return null;
            Assert.That(field.childCount, Is.GreaterThanOrEqualTo(2), "sky canvas and Gooby");
            while (!game.Finished) game.Tick(0.05);
            Assert.That(game.Elapsed, Is.GreaterThanOrEqualTo(60.0));
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AHideSeekTapOnAHiderFindsItAndTheWaveClears()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "hideSeek"), Is.TypeOf<HideSeekScreen>());
            var screen = new HideSeekScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            var field = screen.Root.Q("play-field");
            Assert.That(screen.Root.Q("garden"), Is.Not.Null, "the garden is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("wave-pill"), Is.Not.Null);
            for (var i = 0; i < 12; i++) Assert.That(screen.Root.Q("spot-" + i), Is.Not.Null);
            Assert.That(game.Hidden.Count, Is.EqualTo(3));
            var first = System.Linq.Enumerable.First(game.Hidden);
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.FoundTotal, Is.EqualTo(0), "the intro still gates taps on the field");
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            var hit = screen.Root.Q("spot-" + first);
            using (var e = PointerDownEvent.GetPooled()) { e.target = hit; hit.SendEvent(e); }
            yield return null;
            Assert.That(game.FoundTotal, Is.EqualTo(1));
            Assert.That(game.Score, Is.EqualTo(2));
            foreach (var s in new System.Collections.Generic.List<int>(game.Hidden)) game.Tap(s);
            Assert.That(game.Phase, Is.EqualTo("serve"));
            Assert.That(game.Score, Is.EqualTo(9), "three finds + the wave bonus");
            yield return null;
            while (!game.Finished) game.Tick(0.05);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator AMiniGolfDownwardDragAcesTheStraightThenTheRoundFinishes()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "miniGolf"), Is.TypeOf<MiniGolfScreen>());
            var screen = new MiniGolfScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("course"), Is.Not.Null, "the fairway is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("hole-pill"), Is.Not.Null);
            Assert.That(game.PuttPull(0.0, -120.0), Is.False, "the intro still gates the putt");
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            Assert.That(game.PuttPull(0.0, -120.0), Is.True);
            while (game.Phase == "roll" && !game.Finished) game.Tick(1.0 / 60.0);
            Assert.That(game.Ball.Done, Is.True, "a full pull covers the opener");
            Assert.That(game.Score, Is.EqualTo(30));
            yield return null;
            while (!game.Finished)
            {
                if (game.Phase == "aim") game.PuttWorld(0.45, 0.0);
                game.Tick(1.0 / 60.0);
            }
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator APipeFlowSolverConnectsTheOpenerThenTheRoundRunsOutTheClock()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "pipeFlow"), Is.TypeOf<PipeFlowScreen>());
            var screen = new PipeFlowScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("board"), Is.Not.Null, "the pipe board is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("puzzle-pill"), Is.Not.Null);
            for (var i = 0; i < 25; i++) Assert.That(screen.Root.Q("tile-" + i), Is.Not.Null);
            Assert.That(game.Tap(0), Is.False, "the intro still gates taps");
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            var hit = screen.Root.Q("tile-0");
            using (var e = PointerDownEvent.GetPooled()) { e.target = hit; hit.SendEvent(e); }
            yield return null;
            Assert.That(game.TotalTaps, Is.GreaterThanOrEqualTo(1));
            game.ApplySolution();
            Assert.That(game.Solved, Is.GreaterThanOrEqualTo(1));
            Assert.That(game.Score, Is.GreaterThanOrEqualTo(25));
            yield return null;
            while (!game.Finished) game.Tick(0.05);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ARocketRescueBotPicksUpABunnyThenTheRoundRunsOutTheClock()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "rocketRescue"), Is.TypeOf<RocketRescueScreen>());
            var screen = new RocketRescueScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("moon"), Is.Not.Null, "the moon is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("fuel-pill"), Is.Not.Null);
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Thrust, Is.False, "the intro still gates thrust");
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            game.SetTouch(true, 0.0);
            Assert.That(game.Thrust, Is.True);
            var cap = 0;
            while (game.Engine.Rescued < 1 && !game.Finished && cap < 8000) { game.ApplyBot(); game.Tick(1.0 / 60.0); cap++; }
            Assert.That(game.Engine.Rescued, Is.GreaterThanOrEqualTo(1));
            Assert.That(game.Score, Is.GreaterThanOrEqualTo(30));
            yield return null;
            while (!game.Finished) game.Tick(0.05);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ARunnerBotRunsTheCorridorThenAIdleWipeoutClosesTheRound()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "runner"), Is.TypeOf<RunnerScreen>());
            var screen = new RunnerScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("road"), Is.Not.Null, "the corridor is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("stats-pill"), Is.Not.Null);
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Lane, Is.EqualTo(1), "the intro still gates the swipe");
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            Assert.That(game.Swipe(-80.0, 0.0), Is.True);
            Assert.That(game.Lane, Is.EqualTo(0));
            var cap = 0;
            while (game.Meters < 80.0 && !game.Finished && cap < 8000) { game.ApplyBot(); game.Tick(1.0 / 60.0); cap++; }
            Assert.That(game.Meters, Is.GreaterThan(60.0));
            Assert.That(game.Score, Is.GreaterThanOrEqualTo((int)game.Meters));
            yield return null;
            var idle = 0;
            while (!game.Finished && idle < 4000) { game.Tick(0.05); idle++; }
            Assert.That(game.Finished, Is.True, "standing still in a lane eats the remaining lives");
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ASnailMailBotDeliversThenTheRoundRunsOutTheClock()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => { var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats"); stats["energy"] = 100.0; J.EnsureDict(s, "minigames")["dayCoins"] = 0; });
            Assert.That(MinigameHostScreen.For(host, "snailMail"), Is.TypeOf<SnailMailScreen>());
            var screen = new SnailMailScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("lawn"), Is.Not.Null, "the garden is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("stats-pill"), Is.Not.Null);
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(SnailMailGame.PhaseDraw), "the intro still gates the stroke");
            Assert.That(game.Deliveries, Is.EqualTo(0));
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            Assert.That(game.ApplyBot(), Is.True);
            var cap = 0;
            while (game.Deliveries < 1 && !game.Finished && cap < 4000) { game.ApplyBot(); game.Tick(1.0 / 60.0); cap++; }
            Assert.That(game.Deliveries, Is.GreaterThanOrEqualTo(1));
            Assert.That(game.Score, Is.GreaterThanOrEqualTo(4));
            yield return null;
            var idle = 0;
            while (!game.Finished && idle < 4000) { game.Tick(0.05); idle++; }
            Assert.That(game.Finished, Is.True, "the sixty-second clock closes the round");
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ADeliveryRushBotDeliversThenTheTimeBonusClosesTheRound()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s =>
            {
                var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats");
                stats["energy"] = 100.0;
                J.EnsureDict(s, "minigames")["dayCoins"] = 0;
                J.EnsureDict(J.EnsureDict(s, "minigames"), "difficulty")["deliveryRush"] = "easy";
            });
            Assert.That(MinigameHostScreen.Hosted, Does.Contain("deliveryRush"));
            Assert.That(MinigameHostScreen.For(host, "deliveryRush"), Is.TypeOf<DeliveryRushScreen>());
            var screen = new DeliveryRushScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game, Is.Not.Null);
            Assert.That(screen.Root.Q("city"), Is.Not.Null, "the dusk town is painted");
            Assert.That(screen.Root.Q("gooby"), Is.Not.Null);
            Assert.That(screen.Root.Q("stats-pill"), Is.Not.Null);
            var field = screen.Root.Q("play-field");
            using (var e = PointerDownEvent.GetPooled()) { e.target = field; field.SendEvent(e); }
            yield return null;
            Assert.That(game.Steer, Is.EqualTo(0.0), "the intro still gates steering");
            Assert.That(game.Drops, Is.EqualTo(0));
            while (game.IntroLeft > 0.0) game.Tick(1.0 / 60.0);
            var cap = 0;
            while (game.Drops < 1 && !game.Finished && cap < 8000) { game.ApplyBot(); game.Tick(1.0 / 60.0); cap++; }
            Assert.That(game.Drops, Is.GreaterThanOrEqualTo(1), "the autopilot finds at least one ring");
            Assert.That(game.Score, Is.GreaterThan(0));
            while (!game.Finished && cap < 16000) { game.ApplyBot(); game.Tick(1.0 / 60.0); cap++; }
            Assert.That(game.Finished, Is.True, "three drops plus the time bonus end the round");
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null);
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        private static void Tap(VisualElement root, string name)
        {
            var el = root.Q(name);
            Assert.That(el, Is.Not.Null, name);
            if (el is Button b)
            {
                using (var ev = NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); }
            }
            else
            {
                using (var ev = PointerDownEvent.GetPooled()) { ev.target = el; el.SendEvent(ev); }
            }
        }

        private static IEnumerator PrepareRanchRound(GameHost host, string gameId)
        {
            host.Store.Update(s =>
            {
                J.EnsureDict(J.EnsureDict(s, "gooby"), "stats")["energy"] = 100.0;
                J.EnsureDict(s, "minigames")["dayCoins"] = 0;
                J.EnsureDict(J.EnsureDict(s, "minigames"), "difficulty")[gameId] = "normal";
            });
            yield return null;
        }

        private static IEnumerator FinishToResults(GameHost host, MinigameHostScreen screen, string doneButton)
        {
            Tap(screen.Root, doneButton);
            var t = 0f;
            while (screen.Root.Q("results") == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(screen.Root.Q("results"), Is.Not.Null, "Fertig books the session on the results card");
            host.Ui.Pop();
            yield return null;
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }

        [UnityTest]
        public IEnumerator ABarrelRaceRunRidesTheCloverleafToTheEndCardAndBooksTheArcadeProgress()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            yield return PrepareRanchRound(host, "ranchTonnen");
            Assert.That(MinigameHostScreen.For(host, "ranchTonnen"), Is.TypeOf<RanchTonnenScreen>());
            var screen = new RanchTonnenScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Session;
            Assert.That(game.Phase, Is.EqualTo(RanchCompArcadeGame.PhaseSelect));
            Assert.That(screen.Root.Q("rcomp-select-screen").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Tap(screen.Root, "rcomp-level-1");
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(RanchCompArcadeGame.PhaseIntro), "the run card opens the intro beat");
            while (game.Phase == RanchCompArcadeGame.PhaseIntro) game.Tick(1.0 / 60.0);
            yield return null;
            Assert.That(screen.Root.Q("rcomp-stage"), Is.Not.Null, "the arena is painted");
            Tap(screen.Root, "rcomp-gait-up");
            Assert.That(game.Lauf.Controller.Gait, Is.EqualTo("schritt"), "▲ shifts up a gait");
            var cap = 0;
            while (game.Phase != RanchCompArcadeGame.PhaseEnd && cap++ < 60 * 90) { game.ApplyBot(); game.Tick(1.0 / 60.0); }
            yield return null;
            Assert.That(game.LastStars, Is.GreaterThanOrEqualTo(1), "the cloverleaf beats run 1's ideal");
            Assert.That(screen.Root.Q("rcomp-end"), Is.Not.Null, "the end card shows stars and the next run");
            Assert.That(RanchCompState.ArcadeCleared(host.Store.State, "tonnen", 1), Is.True, "booked into ranch.comp.arcade");
            Assert.That((string)RanchCompState.Geist(host.Store.State, "tonnen")["b64"], Is.Not.Empty, "the best run is saved as a ghost");
            Tap(screen.Root, "rcomp-to-select");
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(RanchCompArcadeGame.PhaseSelect));
            yield return FinishToResults(host, screen, "rcomp-done");
        }

        [UnityTest]
        public IEnumerator ATimeTrialRunPassesEveryGateAndKeepsAGhostPerRoute()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            yield return PrepareRanchRound(host, "ranchZeit");
            var screen = new RanchZeitScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Session;
            Tap(screen.Root, "rcomp-level-1");
            yield return null;
            var cap = 0;
            while (game.Phase != RanchCompArcadeGame.PhaseEnd && cap++ < 60 * 200) { game.ApplyBot(); game.Tick(1.0 / 60.0); }
            yield return null;
            Assert.That(game.LastStars, Is.GreaterThanOrEqualTo(1));
            Assert.That((int)game.LastErgebnis.Detail["verpasst"], Is.EqualTo(0), "every flag gate in order");
            Assert.That(screen.Root.Q("rcomp-end"), Is.Not.Null);
            Assert.That((string)RanchCompState.Geist(host.Store.State, "zeit_k1")["b64"], Is.Not.Empty, "the ghost belongs to route 1");
            Tap(screen.Root, "rcomp-next");
            yield return null;
            Assert.That((game.Phase, game.LevelId), Is.EqualTo((RanchCompArcadeGame.PhaseIntro, 2)), "Weiter opens route 2");
            game.BackToSelect();
            yield return null;
            yield return FinishToResults(host, screen, "rcomp-done");
        }

        [UnityTest]
        public IEnumerator ATournamentShowRunGoesFromBriefingToStandingsCeremonyAndBooksGold()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            yield return PrepareRanchRound(host, "ranchTurnier");
            Assert.That(MinigameHostScreen.For(host, "ranchTurnier"), Is.TypeOf<RanchTurnierScreen>());
            var screen = new RanchTurnierScreen(host);
            host.Ui.Push(screen);
            yield return null;
            screen.StartRound(screen.Root);
            yield return null;
            yield return null;
            var game = screen.Game;
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseMenu));
            Assert.That(screen.Root.Q("turnier-liga"), Is.Not.Null, "the league panel");
            Assert.That(screen.Root.Q("turnier-klasse-holz"), Is.Not.Null);
            Tap(screen.Root, "turnier-disziplin-schau");
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseEinweisung));
            Assert.That(screen.Root.Q("turnier-einweisung"), Is.Not.Null, "the briefing lists the field");
            Assert.That(game.Bots, Has.Count.EqualTo(RanchCompTurnier.BotsJeTurnier));
            var coins = J.Int(J.Path(host.Store.State, "economy", "coins"));
            Tap(screen.Root, "turnier-los");
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseIntro));
            var cap = 0;
            while (game.Phase != RanchTurnierGame.PhaseErgebnis && cap++ < 60 * 60) { game.ApplyBot(); game.Tick(1.0 / 60.0); }
            yield return null;
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseErgebnis));
            Assert.That((int)game.LastErgebnis.Detail["treffer"], Is.EqualTo(5), "five routine taps in the window");
            Assert.That(screen.Root.Q("turnier-ergebnis"), Is.Not.Null);
            Assert.That(screen.Root.Q("turnier-du"), Is.Not.Null, "the player's row in the standings");
            Assert.That(J.Int(J.Path(host.Store.State, "economy", "coins")), Is.EqualTo(coins + game.LastBericht.Gold), "the purse lands in economy.coins");
            Tap(screen.Root, "turnier-weiter");
            yield return null;
            if (game.LastPlatz <= 3)
            {
                Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseZeremonie));
                Assert.That(screen.Root.Q("turnier-podest"), Is.Not.Null, "the podium");
                Tap(screen.Root, "turnier-zur-uebersicht");
                yield return null;
            }
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseMenu));
            Assert.That(game.OeffneEinweisung("schau"), Is.False, "the show is ridden for today's session");
            yield return FinishToResults(host, screen, "turnier-fertig");
        }

        [UnityTest]
        public IEnumerator BookedOnAFlightGoobyLeavesTheHouseAndIsBackOncePickedUp()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            var clock = host.Store.Clock;
            clock.Pin(clock.NowMs());
            try
            {
                // a clean slate whatever an earlier run left in the save: at home, with the fare in the purse
                host.Store.Update(s =>
                {
                    var v = Gooby.Core.Life.Vacation.SliceOf(s);
                    v["phase"] = Gooby.Core.Life.Vacation.PhaseNone; v["destId"] = ""; v["returnAt"] = 0; v["pickupBy"] = 0;
                    s["vacation"] = v;
                    J.EnsureDict(s, "economy")["coins"] = 5000;
                });
                host.Store.NotifySliceChanged("vacation");
                yield return null;
                Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
                Assert.That(host.World.GoobyAway, Is.False);
                Assert.That(host.World.Gooby.gameObject.activeSelf, Is.True, "at home Gooby is in the room");

                // booked at the airport: no Gooby in the room, the bubble carries the holiday note, care actions wait
                Assert.That(Gooby.Core.City.Travel.Buchen(host.Store, "beach", clock.NowMs()), Is.True);
                yield return null;
                Assert.That(host.World.GoobyAway, Is.True);
                Assert.That(host.World.Gooby.gameObject.activeSelf, Is.False, "gooby_spawn.gd: verreist → kein Home-Gooby");
                var bubble = host.Ui.Current.Root.Q<Label>("bubble-text");
                Assert.That(bubble, Is.Not.Null);
                Assert.That(bubble.text, Does.Contain(host.Strings.T("travel.weg.titel")));
                Assert.That(bubble.text, Does.Contain(host.Strings.T("travel.weg.rest", ("tage", Gooby.Core.City.Travel.RestTage(host.Store, clock.NowMs())))));

                // his days at the beach pass: waiting at the airport, then picked up → back in the room
                clock.Advance(Gooby.Core.Life.Vacation.MsPerDay * 10);
                Assert.That(Gooby.Core.City.Travel.Phase(host.Store, clock.NowMs()), Is.Not.EqualTo(Gooby.Core.Life.Vacation.PhaseAway));
                var back = Gooby.Core.City.Travel.Abholen(host.Store, clock.NowMs());
                Assert.That(back.ZielId, Is.EqualTo("beach"));
                yield return null;
                Assert.That(host.World.GoobyAway, Is.False);
                Assert.That(host.World.Gooby.gameObject.activeSelf, Is.True, "picked up → home again");
                Assert.That(host.Ui.Current.Root.Q<Label>("bubble-text").text, Is.EqualTo(host.Strings.T("travel.cutscene.karte_rueckkehr")));
            }
            finally { clock.Unpin(); }
        }

        [UnityTest]
        public IEnumerator SaveTransferImportsAGodotSaveAndBacksUpTheCurrentOne()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            // a GOOBY 5 (Godot) save with a recognisable state, offered like the device probe would
            var godot = Gooby.Core.Save.SaveSchema.DefaultState(host.Store.Clock.NowMs());
            godot["economy"]["coins"] = 7777;
            godot["progression"]["level"] = 9;
            godot["meta"]["goobyNickname"] = "Godot-Gooby";
            var coinsBefore = J.Int(host.Store.State["economy"]["coins"]);
            host.Store.SaveNow(); // the backup is taken from the file on disk, so flush what earlier tests changed
            var preview = host.Legacy.Preview(godot.ToString(), host.Store.Clock.NowMs());
            Assert.That(preview.Ok, Is.True, preview.Error);
            Assert.That(J.Int(preview.Report["coins"]), Is.EqualTo(7777));

            host.Ui.Push(new TransferScreen(host, new LegacySaves.Candidate { Source = "godot", Label = "test", Text = godot.ToString(), Preview = preview }));
            yield return null;
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("transfer"));
            Assert.That(host.Legacy.Apply(preview, out var error), Is.True, error);
            yield return null;
            // the services react to the imported state like to any other change (a fresh state at level 9
            // with 7777 coins unlocks e.g. "coins1000" +50 and the first-boot sticker), so coins may only grow
            var coinsAfter = J.Int(host.Store.State["economy"]["coins"]);
            Assert.That(coinsAfter, Is.GreaterThanOrEqualTo(7777), "state replaced");
            Assert.That(J.Int(host.Store.State["progression"]["level"]), Is.EqualTo(9));
            Assert.That(J.Str(host.Store.State["meta"]["goobyNickname"]), Is.EqualTo("Godot-Gooby"));
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(host.DataDir, LegacySaves.PreImportBackup)), Is.True, "previous save backed up");
            Assert.That(J.Int(Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(host.DataDir, LegacySaves.PreImportBackup)))["economy"]["coins"]), Is.EqualTo(coinsBefore), "backup holds the old coins");
            Assert.That(host.Legacy.AlreadyImported, Is.True);

            // the way back: the GOOBY5 code re-imports byte-for-byte
            var code = host.Legacy.ExportCode();
            var back = host.Legacy.Preview(code, host.Store.Clock.NowMs());
            Assert.That(back.Ok, Is.True, back.Error);
            Assert.That(J.Int(back.State["economy"]["coins"]), Is.EqualTo(coinsAfter));
            Assert.That(J.Str(back.State["meta"]["goobyNickname"]), Is.EqualTo("Godot-Gooby"));
            host.Ui.Pop();
            yield return null;
        }
        [UnityTest]
        public IEnumerator ParkingAtAPlaceLeadsInsideToItsCounterAndBackToTheStreet()
        {
            yield return WaitForBoot();
            var host = GameHost.Instance;
            host.Store.Update(s => s["economy"]["coins"] = 500);
            var screen = new CityDriveScreen(host);
            host.Ui.Push(screen);
            yield return null;
            var drive = host.World.Drive;
            var world = GameObject.Find("World").transform;

            // parked at the GOOBYMAN: "Anhalten" walks in through a fade; the street hides, the interior is staged
            var shop = Gooby.Core.City.DriveModel.PlaceById("goobyman");
            drive.X = shop.X; drive.Z = shop.Z; drive.Kmh = 0.0;
            screen.Braking = true;
            host.World.DriveStep(new Gooby.Core.City.DriveModel.Input { Brake = true }, 0.05f);
            screen.Update();
            yield return null;
            Assert.That(Gooby.Core.City.DriveModel.Reached(drive)?.Id, Is.EqualTo("goobyman"));
            screen.StopHere();
            Assert.That(host.Ui.Fading, Is.True, "the door is a fade");
            var t = 0f;
            while (!(host.Ui.Current is PlaceScreen) && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
            var visit = host.Ui.Current as PlaceScreen;
            Assert.That(visit, Is.Not.Null, "inside the GOOBYMAN");
            Assert.That(host.World.Interior?.Place, Is.SameAs(shop));
            Assert.That(world.Find("City").gameObject.activeSelf, Is.False, "the street is hidden while inside");
            var interior = world.Find("Interior:goobyman");
            Assert.That(interior, Is.Not.Null);
            Assert.That(interior.Find("Counter"), Is.Not.Null, "a counter in front of the shopkeeper");
            // board 10 pass: goods are SKU cartons (Sku + face/band/mark), jars and bottles
            Assert.That(interior.GetComponentsInChildren<Renderer>().Count(r => r.name == "Sku" || r.name == "Jar" || r.name == "Bottle"), Is.GreaterThan(20), "shelves full of goods");
            Assert.That(interior.GetComponentsInChildren<Renderer>().Any(r => r.name == "Lettering" && r.sharedMaterial.name == "decal:sign_goobyman"), Is.True, "the shop's sign inside");
            Assert.That(Vector3.Distance(host.World.Camera.transform.position, host.World.Interior.CameraPosition), Is.LessThan(0.01f), "camera at the door looking in");
            Assert.That(Vector3.Distance(host.World.Gooby.position, host.World.Interior.GoobySpot), Is.LessThan(0.05f), "Gooby stands at the interior spot (in front of the counter, seen from behind on board 10)");
            Assert.That(visit.Runner.Aktuell, Is.EqualTo("hallo"));
            Assert.That(visit.Runner.Sprecher, Is.EqualTo("goobyman"));

            // "Einkaufen!" → the tree's laden effect opens the drugstore counter over the interior
            Assert.That(visit.Choose(0), Is.True);
            Assert.That(visit.Runner.Aktuell, Is.EqualTo("laden"));
            t = 0f;
            while (!(host.Ui.Current is ShopScreen) && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
            var counter = host.Ui.Current as ShopScreen;
            Assert.That(counter?.Id, Is.EqualTo("shop-goobyman"));
            Assert.That(host.World.Interior, Is.Not.Null, "the counter sheet lies over the interior");
            var buttons = counter.Root.Query<Button>().ToList().Where(b => b.ClassListContains("gb-btn-sm")).ToList();
            Assert.That(buttons.Count, Is.EqualTo(5), "three brushes, the plaster, the sleep mask");
            var brush = Gooby.Core.City.ShopLogic.Find(Gooby.Core.City.ShopLogic.Waren(Gooby.Core.City.ShopLogic.GoobymanPath), "zahnbuerste_standard");
            var coins = Gooby.Core.City.ShopLogic.Coins(host.Store);
            Assert.That(Gooby.Core.City.ShopLogic.Kaufe(host.Store, brush), Is.True, "the button's purchase");
            host.Store.NotifySliceChanged("economy");
            yield return null;
            Assert.That(Gooby.Core.City.ShopLogic.Coins(host.Store), Is.EqualTo(coins - 8));
            Assert.That(counter.Root.Query<Label>().ToList().Any(l => l.text == host.Strings.T("fuettern.vorrat_badge", ("anzahl", 1))), Is.True, "the card shows the stock at home");
            host.Ui.Pop();
            yield return null;
            Assert.That(host.Ui.Current, Is.SameAs(visit));
            Assert.That(host.World.Interior, Is.Not.Null, "closing the counter keeps us inside");

            // out through the door: the street is back, the car still parked, the chase camera behind it
            visit.Leave();
            t = 0f;
            while (host.Ui.Current != screen && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.That(host.Ui.Current, Is.SameAs(screen), "back on the street");
            Assert.That(host.World.Interior, Is.Null);
            yield return null; // Object.Destroy takes effect at the end of the frame
            Assert.That(world.Find("Interior:goobyman"), Is.Null, "interior torn down");
            Assert.That(world.Find("City").gameObject.activeSelf, Is.True);
            var camBack = host.World.Camera.transform.position;
            Assert.That(Vector3.Distance(new Vector3(camBack.x, 0f, camBack.z), new Vector3((float)drive.X, 0f, (float)drive.Z)), Is.InRange(5.6f, 8.0f), "chase camera behind the parked car (6.3–7.4 m since the sky pass)");

            // Greta buys only on Saturday 8–14: on a Monday the pill says so but the square stays open for the own stall
            var markt = Gooby.Core.City.DriveModel.PlaceById("wochenmarkt");
            drive.X = markt.X; drive.Z = markt.Z; drive.Kmh = 0.0;
            screen.Braking = true;
            var clock = host.Store.Clock;
            var now = clock.NowMs();
            var (wochentag, stunde) = Gooby.Core.City.CityMap.LokaleZeit(now, clock.UtcOffsetMinutes(now));
            long At(int weekday, double hour) => now + (long)((((weekday - wochentag) % 7 + 7) % 7) * 24.0 * 3600000.0) + (long)((hour - stunde) * 3600000.0);
            clock.Pin(At(1, 10.0));
            host.World.DriveStep(new Gooby.Core.City.DriveModel.Input(), 0.05f);
            screen.Update();
            yield return null;
            Assert.That(screen.Root.Q("enter-place").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "the square is open all week");
            Assert.That(screen.Root.Q<Label>("place-title").text, Is.EqualTo(host.Strings.T("city.ort.wochenmarkt")));
            Assert.That(screen.Root.Q<Label>("place-sub").text, Is.EqualTo(host.Strings.T("nl.markt.zu_stand")), "the pill says Greta only buys on Saturdays");
            clock.Pin(At(Gooby.Core.City.CityMap.Samstag, 10.0));
            screen.Update();
            yield return null;
            Assert.That(screen.Root.Q("enter-place").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Saturday morning: open");
            screen.StopHere();
            t = 0f;
            while (!(host.Ui.Current is PlaceScreen) && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
            var greta = host.Ui.Current as PlaceScreen;
            Assert.That(greta?.Place.Id, Is.EqualTo("wochenmarkt"));
            Assert.That(host.World.Interior.Outdoors, Is.True, "the market is a square under the sky");
            var square = world.Find("Interior:wochenmarkt");
            Assert.That(Enumerable.Range(0, square.childCount).Count(i => square.GetChild(i).name.StartsWith("Scenery:market_stall")), Is.EqualTo(2), "Greta's and the red stall");
            Assert.That(square.Find("OwnStall"), Is.Not.Null, "and the player's own stall where the yellow one stood");
            Assert.That(greta.CounterScreen(), Is.TypeOf<MarktScreen>());
            clock.Unpin();
            host.Ui.PopToRoot();
            yield return null;
            Assert.That(host.World.Interior, Is.Null);
            host.World.SetCityScene(false);
            Assert.That(host.Ui.Current?.Id, Is.EqualTo("home-hud"));
        }
    }
}
