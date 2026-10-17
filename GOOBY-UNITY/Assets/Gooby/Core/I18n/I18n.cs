using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.I18n
{
    /// <summary>Provides the raw JSON text of string files (Resources, files, tests).</summary>
    public interface IStringSource
    {
        /// <summary>Relative file names for a locale, e.g. "de.json", "de/home.json".</summary>
        IEnumerable<string> FilesFor(string locale);
        string ReadJson(string relativePath);
    }

    /// <summary>
    /// Strings table — port of scripts/ui/i18n.gd. DE is the leading locale, EN must be at parity.
    /// Nested JSON is flattened to "domain.key" paths; arrays stay arrays; key collisions are errors.
    /// </summary>
    public sealed class I18n
    {
        public const string DefaultLocale = "de";
        public static readonly string[] SupportedLocales = { "de", "en" };

        private static readonly Regex PlaceholderRx = new Regex(@"\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

        private readonly IStringSource _source;
        private readonly Dictionary<string, Dictionary<string, JToken>> _tables = new Dictionary<string, Dictionary<string, JToken>>();
        private string _locale = DefaultLocale;

        public event Action<string> LocaleChanged;
        public Action<string> OnError { get; set; } = _ => { };

        public I18n(IStringSource source)
        {
            _source = source;
        }

        public string Locale => _locale;

        public void SetLocale(string locale)
        {
            if (Array.IndexOf(SupportedLocales, locale) < 0)
            {
                OnError("Nicht unterstützte Locale: " + locale);
                return;
            }
            if (_locale == locale) return;
            _locale = locale;
            LocaleChanged?.Invoke(locale);
        }

        /// <summary>Text for a key with optional {placeholder} args. Fallback: locale → DE → key itself (+ error).</summary>
        public string T(string key, IDictionary<string, object> args = null)
        {
            var value = Lookup(key);
            if (value == null)
            {
                OnError($"Fehlender String-Key: {key} ({_locale})");
                return key;
            }
            var text = value.Type == JTokenType.String ? value.Value<string>() : value.ToString();
            return args != null && args.Count > 0 ? Format(text, args) : text;
        }

        public string T(string key, params (string name, object value)[] args)
        {
            var d = new Dictionary<string, object>();
            foreach (var (name, value) in args) d[name] = value;
            return T(key, d);
        }

        /// <summary>GDScript String.format({"a": 1}) replica for {name} placeholders.</summary>
        public static string Format(string text, IDictionary<string, object> args)
        {
            return PlaceholderRx.Replace(text, m =>
                args.TryGetValue(m.Groups[1].Value, out var v) ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : m.Value);
        }

        public JArray Items(string key)
        {
            var value = Lookup(key);
            if (value is JArray arr) return arr;
            OnError($"String-Key ist kein Array: {key} ({_locale})");
            return new JArray();
        }

        public bool HasKey(string key) => Lookup(key) != null;

        /// <summary>Complete flattened table of a locale (parity tests).</summary>
        public Dictionary<string, JToken> Table(string locale)
        {
            if (!_tables.TryGetValue(locale, out var tbl))
            {
                tbl = LoadLocale(locale);
                _tables[locale] = tbl;
            }
            return tbl;
        }

        public void ResetCache() => _tables.Clear();

        public static HashSet<string> Placeholders(string text)
        {
            var set = new HashSet<string>();
            foreach (Match m in PlaceholderRx.Matches(text)) set.Add(m.Groups[1].Value);
            return set;
        }

        private JToken Lookup(string key)
        {
            if (Table(_locale).TryGetValue(key, out var v)) return v;
            if (_locale != DefaultLocale && Table(DefaultLocale).TryGetValue(key, out var fb)) return fb;
            return null;
        }

        private Dictionary<string, JToken> LoadLocale(string locale)
        {
            var flat = new Dictionary<string, JToken>();
            foreach (var file in _source.FilesFor(locale)) MergeFile(flat, file);
            return flat;
        }

        private void MergeFile(Dictionary<string, JToken> flat, string path)
        {
            var raw = _source.ReadJson(path);
            if (raw == null) return;
            JToken parsed;
            try { parsed = JToken.Parse(raw); }
            catch (Exception e)
            {
                OnError($"Strings-Datei kaputt: {path} ({e.Message})");
                return;
            }
            if (!(parsed is JObject obj))
            {
                OnError("Strings-Datei kein Objekt: " + path);
                return;
            }
            Flatten("", obj, flat, path);
        }

        private void Flatten(string prefix, JObject node, Dictionary<string, JToken> outp, string source)
        {
            foreach (var prop in node.Properties())
            {
                var full = prefix.Length == 0 ? prop.Name : prefix + "." + prop.Name;
                if (prop.Value is JObject child)
                {
                    Flatten(full, child, outp, source);
                    continue;
                }
                if (outp.ContainsKey(full))
                {
                    OnError($"String-Key-Kollision: {full} (aus {source})");
                    continue;
                }
                outp[full] = prop.Value;
            }
        }
    }

    /// <summary>String source backed by an index (locale → file list) and a reader delegate.</summary>
    public sealed class IndexedStringSource : IStringSource
    {
        private readonly Dictionary<string, List<string>> _index;
        private readonly Func<string, string> _read;

        /// <param name="indexJson">{"de": ["de.json", "de/home.json", ...], "en": [...]}</param>
        /// <param name="read">reads a relative path → JSON text (or null when missing)</param>
        public IndexedStringSource(string indexJson, Func<string, string> read)
        {
            _index = new Dictionary<string, List<string>>();
            var obj = JObject.Parse(indexJson);
            foreach (var prop in obj.Properties())
            {
                var list = new List<string>();
                foreach (var f in (JArray)prop.Value) list.Add(f.Value<string>());
                _index[prop.Name] = list;
            }
            _read = read;
        }

        public IEnumerable<string> FilesFor(string locale) =>
            _index.TryGetValue(locale, out var l) ? l : (IEnumerable<string>)Array.Empty<string>();

        public string ReadJson(string relativePath) => _read(relativePath);
    }
}
