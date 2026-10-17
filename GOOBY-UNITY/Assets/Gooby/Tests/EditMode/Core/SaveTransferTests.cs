using System.Text;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Port of tests/unit/test_state_moving_box.gd: GOOBY5 code round trip, strict decode errors, raw JSON import
    /// for v5 (normalize) and web v4 (migration chain), CRC32 golden (IEEE, identical to Godot/zlib).
    /// </summary>
    public class SaveTransferTests
    {
        private const long NowMs = 1_768_478_400_000L;

        [Test]
        public void Crc32MatchesZlibGolden()
        {
            // zlib.crc32(b"123456789") == 0xCBF43926 — the check value every CRC-32/IEEE implementation must hit
            Assert.That(SaveTransfer.Crc32(Encoding.ASCII.GetBytes("123456789")), Is.EqualTo(0xCBF43926u));
            Assert.That(SaveTransfer.Crc32(new byte[0]), Is.EqualTo(0u));
        }

        [Test]
        public void ExportCodeRoundTripsAV5State()
        {
            var state = SaveSchema.DefaultState(NowMs);
            state["economy"]["coins"] = 4321;
            state["progression"]["level"] = 7;
            state["meta"]["goobyNickname"] = "Möhrchen";
            var code = SaveTransfer.ExportCode(state);
            Assert.That(code, Does.StartWith("GOOBY5."));
            Assert.That(code.Split('.').Length, Is.EqualTo(3));
            Assert.That(code, Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="), "base64url alphabet");

            var res = SaveTransfer.ImportText(code, NowMs);
            Assert.That(res.Ok, Is.True, res.Error);
            Assert.That(J.Int(res.State["economy"]["coins"]), Is.EqualTo(4321));
            Assert.That(J.Int(res.State["progression"]["level"]), Is.EqualTo(7));
            Assert.That(J.Str(res.State["meta"]["goobyNickname"]), Is.EqualTo("Möhrchen"));
            Assert.That(J.Str(res.Report["importedFrom"]), Is.EqualTo("godot-v5"));
            Assert.That(J.Int(res.Report["level"]), Is.EqualTo(7));
        }

        [Test]
        public void DecodeRejectsTamperedAndMalformedCodes()
        {
            var code = SaveTransfer.ExportCode(SaveSchema.DefaultState(NowMs));
            var parts = code.Split('.');
            Assert.That(SaveTransfer.DecodeCode(parts[0] + "." + parts[1] + ".deadbeef").Error, Does.Contain("CRC"));
            Assert.That(SaveTransfer.DecodeCode("GOOBY5.abc").Error, Does.Contain("malformed"));
            Assert.That(SaveTransfer.DecodeCode("GOOBY4." + parts[1] + "." + parts[2]).Error, Does.Contain("malformed"));
            Assert.That(SaveTransfer.DecodeCode("GOOBY5.$$$$." + parts[2]).Error, Does.Contain("base64url"));
            Assert.That(SaveTransfer.ImportText("", NowMs).Error, Is.EqualTo("empty input"));
            Assert.That(SaveTransfer.ImportText("{not json", NowMs).Error, Does.StartWith("invalid JSON"));
            Assert.That(SaveTransfer.ImportText("[1,2,3]", NowMs).Error, Is.EqualTo("save is not an object"));
            Assert.That(SaveTransfer.ImportText("{\"v\": 9}", NowMs).Error, Does.Contain("forward version"));
        }

        [Test]
        public void RawV5JsonIsNormalizedNotMigrated()
        {
            var state = SaveSchema.DefaultState(NowMs);
            state["economy"]["coins"] = 99;
            var res = SaveTransfer.ImportText(state.ToString(), NowMs);
            Assert.That(res.Ok, Is.True, res.Error);
            Assert.That(J.Int(res.State["economy"]["coins"]), Is.EqualTo(99));
            Assert.That(J.Int(res.State["v"]), Is.EqualTo(SaveSchema.SchemaVersion));
        }

        [Test]
        public void RawWebV4JsonRunsThroughTheMigrationChain()
        {
            var v4 = Fixtures.LoadObject("migration/v4_fresh.json");
            var res = SaveTransfer.ImportText(v4.ToString(), NowMs);
            Assert.That(res.Ok, Is.True, res.Error);
            Assert.That(J.Int(res.State["v"]), Is.EqualTo(SaveSchema.SchemaVersion));
            Assert.That(J.Int(res.State["economy"]["coins"]), Is.EqualTo(J.Int(v4["coins"]) + SaveMigration.MovingBonusCoins));
            Assert.That(J.IsTrue(res.State["home"]["movingDay"]), Is.True);
        }

        [Test]
        public void GodotCodeFixtureDecodes()
        {
            // produced by `godot --headless --script` with GOOBY-GODOT MovingBoxImport.export_code({"v":5,"economy":{"coins":12}})
            const string godotCode = "GOOBY5.H4sIAAAAAAAAA6tWSk3Oz8vPrVSyqlZKzs_MK1ayMjSq1VEqU7IyrQUAtTXF3h4AAAA.20f6fcf7";
            var decoded = SaveTransfer.DecodeCode(godotCode);
            Assert.That(decoded.Ok, Is.True, decoded.Error);
            Assert.That(JObject.Parse(decoded.Json)["economy"]["coins"].Value<int>(), Is.EqualTo(12));
        }
    }
}
