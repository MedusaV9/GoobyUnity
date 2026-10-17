using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The horse a rider brings into a ranch round (PFERD): built from the farm's riding horse with its coat, looks and
    /// gear; what comes in from the room keeps only the known values.
    /// </summary>
    public class RmpPferdLookTests
    {
        private static JObject State() => JObject.Parse(@"{
            ""ranch"": {
                ""reitPferd"": ""p2"",
                ""wirtschaft"": { ""gear"": { ""owned"": [""sattel_rot"", ""halfter_gold""], ""equippedByHorse"": {
                    ""p1"": { ""sattel"": ""rot"", ""decke"": null, ""halfter"": ""gold"" },
                    ""p2"": { ""sattel"": null, ""decke"": ""schleife"", ""halfter"": null } } } },
                ""tiere"": { ""pferde"": {
                    ""p1"": { ""name"": ""Karamell"", ""farbe"": ""palomino"", ""look"": { ""flausch"": true } },
                    ""p2"": { ""name"": ""Wolke"", ""farbe"": ""weiss"", ""farbeHex"": ""#E8E0D4"", ""maehneHex"": ""#B9A88E"", ""look"": { ""fell"": ""nebel"" } } } }
            } }");

        [Test]
        public void TheLookIsTheRiddenHorseWithItsGear()
        {
            var state = State();
            var look = RmpPferdLook.Von(state, RanchHof.ReitPferd(state));
            Assert.That((string)look["name"], Is.EqualTo("Wolke"), "the horse last picked at the farm");
            Assert.That(((string)look["farbe"], (string)look["farbeHex"], (string)look["maehneHex"]), Is.EqualTo(("weiss", "#E8E0D4", "#B9A88E")));
            Assert.That(RanchBelohnungen.TraegtLook(look, RanchBelohnungen.Nebel), Is.True, "the misty coat rides along");
            Assert.That(RanchBelohnungen.TraegtLook(look, RanchBelohnungen.Flausch), Is.False);
            Assert.That((RmpPferdLook.Gear(look, "sattel"), RmpPferdLook.Gear(look, "decke"), RmpPferdLook.Gear(look, "halfter")), Is.EqualTo(((string)null, "schleife", (string)null)));

            var karamell = RmpPferdLook.Von(state, "p1");
            Assert.That((string)karamell["farbe"], Is.EqualTo("palomino"));
            Assert.That(karamell["farbeHex"], Is.Null, "no bred colours, the coat id decides");
            Assert.That(RanchBelohnungen.TraegtLook(karamell, RanchBelohnungen.Flausch), Is.True);
            Assert.That((RmpPferdLook.Gear(karamell, "sattel"), RmpPferdLook.Gear(karamell, "halfter")), Is.EqualTo(("rot", "gold")));
            Assert.That(RmpPferdLook.Von(state, "p9"), Is.Null, "no such horse");
            Assert.That(RmpPferdLook.Sauber(look).ToString(), Is.EqualTo(look.ToString()), "a clean look passes unchanged");
        }

        [Test]
        public void AReceivedLookKeepsOnlyKnownValues()
        {
            var roh = JObject.Parse(@"{
                ""name"": ""  Sternchen mit dem sehr langen Namen  "", ""farbe"": ""<b>gold</b>"", ""farbeHex"": ""red"", ""maehneHex"": ""#123456"",
                ""look"": { ""fell"": ""lava"", ""flausch"": ""ja"" },
                ""gear"": { ""sattel"": ""schleife"", ""decke"": ""kartoffel"", ""halfter"": ""lila"", ""krone"": ""gold"" },
                ""coins"": 9999 }");
            var look = RmpPferdLook.Sauber(roh);
            Assert.That(((string)look["name"]).Length, Is.EqualTo(RmpPferdLook.MaxNameLen));
            Assert.That((string)look["name"], Does.StartWith("Sternchen"), "trimmed, then cut");
            Assert.That((string)look["farbe"], Is.EqualTo("braun"), "an unknown coat id rides brown");
            Assert.That(look["farbeHex"], Is.Null, "not a colour");
            Assert.That(look["maehneHex"], Is.Null, "a mane colour only with its coat colour");
            Assert.That(look["look"], Is.Null, "neither a known coat nor a real true");
            Assert.That((RmpPferdLook.Gear(look, "sattel"), RmpPferdLook.Gear(look, "decke"), RmpPferdLook.Gear(look, "halfter")),
                Is.EqualTo(((string)null, (string)null, "lila")), "the bow belongs to the pad, the potato sack to the saddle");
            Assert.That(look["gear"]["krone"], Is.Null);
            Assert.That(look["coins"], Is.Null);
            Assert.That(RmpPferdLook.Sauber(new JArray()), Is.Null);
            Assert.That(RmpPferdLook.Sauber(JValue.CreateString("pferd")), Is.Null);
            Assert.That(RmpPferdLook.Sauber(null), Is.Null);
        }
    }
}
