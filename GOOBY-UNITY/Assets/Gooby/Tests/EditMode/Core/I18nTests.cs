using System.Collections.Generic;
using System.IO;
using Gooby.Core.I18n;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_ui_strings.gd: DE leads, EN must be at parity (keys, arrays, placeholders).</summary>
    public class I18nTests
    {
        private static string StringsRoot
        {
            get
            {
                var dir = new DirectoryInfo(Fixtures.Root);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
                return Path.Combine(dir.FullName, "Assets", "Gooby", "Content", "Resources", "gooby", "strings");
            }
        }

        private static I18n NewI18n(List<string> errors = null)
        {
            var root = StringsRoot;
            var source = new IndexedStringSource(File.ReadAllText(Path.Combine(root, "index.json")),
                rel => File.Exists(Path.Combine(root, rel)) ? File.ReadAllText(Path.Combine(root, rel)) : null);
            var i18n = new I18n(source);
            if (errors != null) i18n.OnError = errors.Add;
            return i18n;
        }

        [Test]
        public void TablesLoadWithoutCollisions()
        {
            var errors = new List<string>();
            var i18n = NewI18n(errors);
            var de = i18n.Table("de");
            var en = i18n.Table("en");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(de.Count, Is.GreaterThan(4000), "DE table size");
            Assert.That(en.Count, Is.EqualTo(de.Count), "EN/DE key count parity");
        }

        [Test]
        public void EnglishHasParityWithGerman()
        {
            var i18n = NewI18n();
            var de = i18n.Table("de");
            var en = i18n.Table("en");
            var missing = new List<string>();
            var extra = new List<string>();
            var placeholderMismatch = new List<string>();
            var typeMismatch = new List<string>();
            foreach (var kv in de)
            {
                if (!en.TryGetValue(kv.Key, out var ev)) { missing.Add(kv.Key); continue; }
                if (kv.Value.Type != ev.Type && !(kv.Value.Type == JTokenType.Integer && ev.Type == JTokenType.Float)) typeMismatch.Add(kv.Key);
                if (kv.Value.Type == JTokenType.String)
                {
                    var pd = I18n.Placeholders(kv.Value.Value<string>());
                    var pe = I18n.Placeholders(ev.Value<string>());
                    if (!pd.SetEquals(pe)) placeholderMismatch.Add(kv.Key);
                }
            }
            foreach (var k in en.Keys) if (!de.ContainsKey(k)) extra.Add(k);
            Assert.That(missing, Is.Empty, "EN missing: " + string.Join(", ", missing));
            Assert.That(extra, Is.Empty, "EN extra: " + string.Join(", ", extra));
            Assert.That(placeholderMismatch, Is.Empty, "placeholders: " + string.Join(", ", placeholderMismatch));
            Assert.That(typeMismatch, Is.Empty, "types: " + string.Join(", ", typeMismatch));
        }

        [Test]
        public void LookupFormatsAndFallsBack()
        {
            var errors = new List<string>();
            var i18n = NewI18n(errors);
            Assert.That(i18n.T("hud.level_pill", ("level", 12)), Is.EqualTo("Lv 12"));
            Assert.That(i18n.T("build.lager", ("used", 100), ("cap", 500)), Is.EqualTo("Lager 100/500"));
            i18n.SetLocale("en");
            Assert.That(i18n.Locale, Is.EqualTo("en"));
            Assert.That(i18n.T("hud.einstellungen"), Is.EqualTo("Settings"));
            Assert.That(i18n.T("nope.missing"), Is.EqualTo("nope.missing"));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(i18n.Items("news.items").Count, Is.GreaterThan(0));
            i18n.SetLocale("xx");
            Assert.That(i18n.Locale, Is.EqualTo("en"));
        }
    }
}
