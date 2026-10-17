using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Layout presets "Raum speichern" (port of the preset tests in test_w13c_garage.gd).</summary>
    public class LayoutPresetsTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(1_791_158_400_000L);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            return store;
        }

        private static JArray WohnLayout() => JArray.Parse(@"[
            {""uid"":""i-000001"",""item"":""chair"",""at"":[5,5],""rot"":0},
            {""uid"":""i-000002"",""item"":""sideTable"",""at"":[7,5],""rot"":0},
            {""uid"":""i-000003"",""item"":""plantSmall1"",""at"":[7,5],""rot"":0},
            {""uid"":""i-000004"",""item"":""lampSquareCeiling"",""at"":[5,3],""rot"":0}]");

        private static List<string> Signatur(JToken items) =>
            (items as JArray ?? new JArray()).Select(e => J.Str(e["item"]) + "@" + J.Int(e["at"]?[0]) + "," + J.Int(e["at"]?[1]) + " r" + J.Int(e["rot"])).OrderBy(s => s).ToList();

        private static void SetRoom(GameStateStore gs, JArray items) => gs.Update(s => s["home"]["rooms"]["living"]["items"] = items);

        [Test]
        public void SaveAndApplyRoundTripRestampsUidsAndRespansGarlands()
        {
            var gs = Store();
            SetRoom(gs, WohnLayout());
            gs.Update(s => s["home"]["nextUid"] = 10);
            HomeState.StoreItem(gs, Girlanden.Wimpel);
            Assert.That(Girlanden.Spannen(gs, "living", Girlanden.Wimpel, new Cell(2, 2), new Cell(6, 2), 12, 10), Is.EqualTo(Girlanden.Gespannt));
            Assert.That(LayoutPresets.Speichern(gs, "living", 0, "  Wohnstube  "), Is.EqualTo(""));
            var slot = LayoutPresets.SlotsOf(gs, "living")[0];
            Assert.That(J.Str(slot["name"]), Is.EqualTo("Wohnstube"), "trimmed");
            Assert.That(LayoutPresets.Zusammenfassung(slot), Is.EqualTo((4, 1)));
            var vorher = Signatur(HomeState.RoomItems(gs, "living"));
            var weights = HomeState.DefsAsJson();
            var punkte = StorageLogic.PointsUsed(HomeState.Storage(gs), weights);
            var plan = LayoutPresets.Anwenden(gs, "living", 0);
            Assert.That(plan.Ok, Is.True);
            Assert.That(plan.Fehlend, Is.EqualTo(0));
            var nachher = HomeState.RoomItems(gs, "living");
            Assert.That(Signatur(nachher), Is.EqualTo(vorher), "the same grid on every layer");
            foreach (var e in nachher) Assert.That(int.Parse(J.Str(e["uid"]).Substring(2)), Is.GreaterThanOrEqualTo(10), "fresh uids");
            Assert.That(J.Int(J.Path(gs.State, "home", "nextUid")), Is.GreaterThanOrEqualTo(14));
            var g = Girlanden.Liste(gs, "living");
            Assert.That(g.Count, Is.EqualTo(1), "the garland hangs again");
            Assert.That(Girlanden.Zelle(g[0], "zelle_a"), Is.EqualTo(new Cell(2, 2)));
            Assert.That(StorageLogic.PointsUsed(HomeState.Storage(gs), weights), Is.EqualTo(punkte), "storage unchanged on balance");
        }

        [Test]
        public void PiecesNoLongerOwnedStayAway()
        {
            var gs = Store();
            SetRoom(gs, JArray.Parse(@"[{""uid"":""i-000001"",""item"":""chair"",""at"":[5,5],""rot"":0},{""uid"":""i-000002"",""item"":""sideTable"",""at"":[7,5],""rot"":0}]"));
            Assert.That(LayoutPresets.Speichern(gs, "living", 1, "Duo"), Is.EqualTo(""));
            SetRoom(gs, JArray.Parse(@"[{""uid"":""i-000001"",""item"":""chair"",""at"":[5,5],""rot"":0}]"));
            var storageTables = StorageLogic.CountOf(HomeState.Storage(gs), "sideTable");
            var plan = LayoutPresets.Anwenden(gs, "living", 1);
            Assert.That(plan.Ok, Is.True);
            Assert.That(plan.Fehlend, Is.EqualTo(storageTables > 0 ? 0 : 1), "the table that is gone is counted missing");
            if (storageTables == 0)
            {
                Assert.That(Signatur(HomeState.RoomItems(gs, "living")), Is.EqualTo(new[] { "chair@5,5 r0" }));
                Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), "sideTable"), Is.EqualTo(0), "no phantom piece");
            }
        }

        [Test]
        public void AStorageOverflowChangesNothing()
        {
            var gs = Store();
            SetRoom(gs, JArray.Parse(@"[{""uid"":""i-000001"",""item"":""chair"",""at"":[5,5],""rot"":0}]"));
            Assert.That(LayoutPresets.Speichern(gs, "living", 0, "NurStuhl"), Is.EqualTo(""));
            var voll = JArray.Parse(@"[{""uid"":""i-000001"",""item"":""chair"",""at"":[5,5],""rot"":0},{""uid"":""i-000002"",""item"":""sideTable"",""at"":[7,5],""rot"":0}]");
            SetRoom(gs, (JArray)voll.DeepClone());
            gs.Update(s => s["home"]["storageCapacity"] = 0);
            var uid = J.Int(J.Path(gs.State, "home", "nextUid"));
            var lager = HomeState.Storage(gs).ToString();
            var plan = LayoutPresets.Anwenden(gs, "living", 0);
            Assert.That(plan.Ok, Is.False);
            Assert.That(plan.Reason, Is.EqualTo(LayoutPresets.LagerVoll));
            Assert.That(Signatur(HomeState.RoomItems(gs, "living")), Is.EqualTo(Signatur(voll)), "room untouched");
            Assert.That(HomeState.Storage(gs).ToString(), Is.EqualTo(lager), "storage untouched");
            Assert.That(J.Int(J.Path(gs.State, "home", "nextUid")), Is.EqualTo(uid));
        }

        [Test]
        public void SlotsNamesAndDeleting()
        {
            var gs = Store();
            Assert.That(LayoutPresets.SlotsOf(gs, "living").Count, Is.EqualTo(LayoutPresets.Slots));
            Assert.That(LayoutPresets.Speichern(gs, "living", -1, "X"), Is.EqualTo(LayoutPresets.SlotUngueltig));
            Assert.That(LayoutPresets.Speichern(gs, "living", LayoutPresets.Slots, "X"), Is.EqualTo(LayoutPresets.SlotUngueltig));
            Assert.That(LayoutPresets.Speichern(gs, "living", 0, "   "), Is.EqualTo(LayoutPresets.NameLeer));
            Assert.That(LayoutPresets.Speichern(gs, "living", 2, new string('X', LayoutPresets.NameMax + 10)), Is.EqualTo(""));
            Assert.That(J.Str(LayoutPresets.SlotsOf(gs, "living")[2]["name"]).Length, Is.EqualTo(LayoutPresets.NameMax), "name capped");
            Assert.That(LayoutPresets.Anwenden(gs, "living", 0).Reason, Is.EqualTo(LayoutPresets.PresetLeer));
            Assert.That(LayoutPresets.Loeschen(gs, "living", 2), Is.EqualTo(""));
            Assert.That(LayoutPresets.Frei(LayoutPresets.SlotsOf(gs, "living")[2]), Is.True);
            Assert.That(LayoutPresets.Loeschen(gs, "living", 9), Is.EqualTo(LayoutPresets.SlotUngueltig));
        }

        [Test]
        public void LoadingHealsBrokenPresets()
        {
            var healed = HomeState.NormalizeSlice(JObject.Parse(@"{""presets"":{
                ""living"":[
                    {""name"":""Ok"",""items"":[{""item"":""chair"",""at"":[1,1]},""müll"",{""kein_item"":1}],
                     ""girlanden"":[{""typ"":""girlande_wimpel"",""zelle_a"":[1,1],""zelle_b"":[4,1]},{""typ"":"""",""zelle_a"":[0,0],""zelle_b"":[1,1]}]},
                    {""items"":""kein_array""}, ""müll"", {""name"":""ZuViel""}],
                ""bedroom"":""kein_array""}}"));
            var living = (JArray)healed["presets"]["living"];
            Assert.That(living.Count, Is.EqualTo(LayoutPresets.Slots), "extra slots cut");
            Assert.That(J.Str(living[0]["name"]), Is.EqualTo("Ok"));
            Assert.That(((JArray)living[0]["items"]).Count, Is.EqualTo(1));
            Assert.That(((JArray)living[0]["girlanden"]).Count, Is.EqualTo(1));
            Assert.That(LayoutPresets.Frei((JObject)living[1]), Is.True, "no name: free");
            var bedroom = (JArray)healed["presets"]["bedroom"];
            Assert.That(bedroom.Count, Is.EqualTo(LayoutPresets.Slots));
            Assert.That(bedroom.All(s => ((JObject)s).Count == 0), Is.True);
            Assert.That(HomeState.NormalizeSlice(JObject.Parse(@"{""presets"":""müll""}"))["presets"], Is.InstanceOf<JObject>());
        }

        [Test]
        public void ThePlanTouchesNoneOfItsInputs()
        {
            var defs = FurnitureCatalog.Defs();
            var preset = JObject.Parse(@"{""name"":""P"",""items"":[{""uid"":""i-000001"",""item"":""chair"",""at"":[2,2],""rot"":0}],
                ""girlanden"":[{""typ"":""girlande_wimpel"",""zelle_a"":[0,0],""zelle_b"":[3,0]}]}");
            var placed = JArray.Parse(@"[{""uid"":""i-000009"",""item"":""sideTable"",""at"":[4,4],""rot"":0}]");
            var garlands = new JArray();
            var storage = JArray.Parse(@"[{""item"":""chair"",""count"":1,""variant"":""default""}]");
            var copies = new[] { preset.ToString(), placed.ToString(), garlands.ToString(), storage.ToString() };
            var plan = LayoutPresets.PlanApply(preset, placed, garlands, storage, 100, defs, 8, 8, null, null, null, null, 50);
            Assert.That(plan.Ok, Is.True);
            Assert.That(plan.Fehlend, Is.EqualTo(1), "the garland owned by no one is missing — the furniture swap still runs");
            Assert.That(plan.NextUid, Is.EqualTo(51));
            Assert.That(new[] { preset.ToString(), placed.ToString(), garlands.ToString(), storage.ToString() }, Is.EqualTo(copies), "pure");
            var outside = LayoutPresets.PlanApply(JObject.Parse(@"{""name"":""G"",""items"":[],""girlanden"":[{""typ"":""girlande_wimpel"",""zelle_a"":[0,0],""zelle_b"":[99,0]}]}"),
                new JArray(), JArray.Parse(@"[{""typ"":""girlande_wimpel"",""zelle_a"":[0,0],""zelle_b"":[3,0]}]"), new JArray(), 100, defs, 8, 8, null, null, null, null, 1);
            Assert.That(outside.Ok, Is.True);
            Assert.That(outside.Fehlend, Is.EqualTo(1), "a garland outside the room stays away");
            Assert.That(outside.Girlanden.Count, Is.EqualTo(0));
        }
    }
}
