using Gooby.Core.Random;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    public class GoobyRngTests
    {
        [Test]
        public void GoldenU32IsBitIdenticalToWeb()
        {
            var fixture = Fixtures.LoadObject("expected/rng.json");
            var table = (JObject)fixture["u32"];
            Assert.That(table.Count, Is.GreaterThanOrEqualTo(5));
            foreach (var prop in table.Properties())
            {
                var rng = new GoobyRng(long.Parse(prop.Name));
                var want = (JArray)prop.Value;
                for (var i = 0; i < want.Count; i++)
                    Assert.That(rng.NextU32(), Is.EqualTo(want[i].Value<uint>()), $"seed={prop.Name} i={i}");
            }
        }

        [Test]
        public void GoldenFloatsMatchExactly()
        {
            var fixture = Fixtures.LoadObject("expected/rng.json");
            var table = (JObject)fixture["floats"];
            foreach (var prop in table.Properties())
            {
                var rng = new GoobyRng(long.Parse(prop.Name));
                var want = (JArray)prop.Value;
                for (var i = 0; i < want.Count; i++)
                    Assert.That(rng.Next(), Is.EqualTo(want[i].Value<double>()), $"seed={prop.Name} i={i}");
            }
        }

        [Test]
        public void NextIsExactU32Division()
        {
            var a = new GoobyRng(7);
            var b = new GoobyRng(7);
            for (var i = 0; i < 64; i++)
                Assert.That(a.Next(), Is.EqualTo(b.NextU32() / 4294967296.0));
        }

        [Test]
        public void DeterministicAndSeedSensitive()
        {
            var a = new GoobyRng(99);
            var b = new GoobyRng(99);
            var c = new GoobyRng(100);
            var same = true;
            var differs = false;
            for (var i = 0; i < 16; i++)
            {
                var x = a.NextU32();
                same &= x == b.NextU32();
                differs |= x != c.NextU32();
            }
            Assert.That(same, Is.True);
            Assert.That(differs, Is.True);
        }

        [Test]
        public void RangeHelpersStayInBounds()
        {
            var rng = new GoobyRng(3);
            for (var i = 0; i < 500; i++)
            {
                var f = rng.RangeF(2.0, 5.0);
                Assert.That(f, Is.GreaterThanOrEqualTo(2.0).And.LessThan(5.0));
                var n = rng.RangeI(1, 6);
                Assert.That(n, Is.InRange(1, 6));
            }
        }
    }
}
