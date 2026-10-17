using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Audio
{
    public sealed class MusicTrack
    {
        public string Id, File, Category, Title, Context, Variant;
        public double DurationSec, GainTrim = 1.0;
        public int UnlockLevel = 1;
    }

    /// <summary>
    /// Port of music_registry.gd — the one table of "which track plays where". The data is exported from the Godot
    /// registry (tools/audio/export_music_registry.py → audio/music_registry.json); audio/game_music.json gives the
    /// minigames without a Godot track their own piece instead of all sharing the arcade loop.
    /// </summary>
    public static class MusicRegistry
    {
        public const string DataPath = "audio/music_registry";
        /// <summary>Unity additions: {"contexts": {context: track}, "games": {gameId: track}}.</summary>
        public const string SceneMusicPath = "audio/scene_music";
        public const double StingerMaxSec = 10.0;

        private static Dictionary<string, MusicTrack> _tracks;
        private static JObject _data;

        public static void ResetCache()
        {
            _tracks = null;
            _data = null;
            _loudness = null;
        }

        private static JObject Data => _data ?? (_data = ContentSource.LoadObject(DataPath));

        public static IReadOnlyDictionary<string, MusicTrack> Tracks
        {
            get
            {
                if (_tracks != null) return _tracks;
                _tracks = new Dictionary<string, MusicTrack>();
                if (Data["tracks"] is JObject rows)
                    foreach (var p in rows.Properties())
                    {
                        if (!(p.Value is JObject row)) continue;
                        _tracks[p.Name] = new MusicTrack
                        {
                            Id = p.Name,
                            File = J.Str(row["file"], ""),
                            Category = J.Str(row["category"], ""),
                            Title = J.Str(row["title"], p.Name),
                            Context = J.Str(row["context"], ""),
                            Variant = J.Str(row["variant"], ""),
                            DurationSec = J.NumOr(row["duration_sec"], 0),
                            GainTrim = J.NumOr(row["gain_trim"], 1.0),
                            UnlockLevel = J.Int(row["unlock_level"], 1),
                        };
                    }
                return _tracks;
            }
        }

        public static MusicTrack Get(string id) => id != null && Tracks.TryGetValue(id, out var t) ? t : null;

        /// <summary>Resources path of a track (null = unknown): "gooby-audio/music/&lt;category&gt;/&lt;id&gt;".</summary>
        public static string ResourcePath(string id)
        {
            var t = Get(id);
            if (t == null || t.File.Length == 0) return null;
            var file = t.File.EndsWith(".ogg", StringComparison.Ordinal) ? t.File.Substring(0, t.File.Length - 4) : t.File;
            return "gooby-audio/music/" + file;
        }

        /// <summary>Measured loudness per track (tools/audio/measure_loudness.py): {"target_lufs", "tracks": {id: {lufs, peak_db}}}.</summary>
        public const string LoudnessPath = "audio/music_loudness";
        /// <summary>Most a quiet track is lifted; it never goes past −1 dB true peak either.</summary>
        public const double MaxLiftDb = 2.0;

        private static JObject _loudness;

        /// <summary>Web gainTrim as dB (linear, floored at 0.05) plus the level match towards the common target.</summary>
        public static double TrimDb(string id)
        {
            var t = Get(id);
            return 20.0 * Math.Log10(Math.Max(0.05, t?.GainTrim ?? 1.0)) + LoudnessTrimDb(id);
        }

        /// <summary>
        /// The trim that brings a track's measured loudness to the target (−18 LUFS): the measured tracks span
        /// −19 … −14 LUFS, so without it a room change could jump by 5 dB. Louder tracks come down; quieter ones come up
        /// by at most <see cref="MaxLiftDb"/> and only as far as their peak allows. Stingers keep their punch.
        /// </summary>
        public static double LoudnessTrimDb(string id)
        {
            var t = Get(id);
            if (t == null || t.Category == "Stinger") return 0.0;
            var data = _loudness ?? (_loudness = ContentSource.LoadObject(LoudnessPath));
            if (!(data?["tracks"]?[id] is JObject m) || m["lufs"] == null) return 0.0;
            var target = J.NumOr(data["target_lufs"], -18.0);
            var diff = target - J.Num(m["lufs"]);
            if (diff <= 0.0) return diff;
            var headroom = -1.0 - J.NumOr(m["peak_db"], -1.0);
            return Math.Max(0.0, Math.Min(diff, Math.Min(MaxLiftDb, headroom)));
        }

        /// <summary>Web isStinger: category Stinger or shorter than ten seconds.</summary>
        public static bool IsStinger(string id)
        {
            var t = Get(id);
            if (t == null) return false;
            if (t.Category == "Stinger") return true;
            return t.DurationSec > 0 && t.DurationSec < StingerMaxSec;
        }

        public static bool TrackBelongsTo(string id, string station)
        {
            var t = Get(id);
            if (t == null || IsStinger(id)) return false;
            switch (station)
            {
                case "alle": return true;
                case "bordmusik": return t.Category == "Bordmusik";
                case "gooby-fm": return t.Category == "Radio";
                case "recap-fm": return t.Category == "Recap";
                case "game-fm": return t.Category == "Game";
                default: return false;
            }
        }

        /// <summary>Member tracks of a station, sorted (level-locked ones included — the queue filters by level).</summary>
        public static List<string> StationTrackIds(string station) =>
            Tracks.Keys.Where(id => TrackBelongsTo(id, station)).OrderBy(id => id, StringComparer.Ordinal).ToList();

        /// <summary>The stations with tracks: (id, name key, unlock level), in the registry order.</summary>
        public static List<(string id, string nameKey, int unlockLevel)> Stations()
        {
            var list = new List<(string, string, int)>();
            if (!(Data["stations"] is JArray defs)) return list;
            foreach (var d in defs.OfType<JObject>())
            {
                var id = J.Str(d["id"], "");
                if (id.Length == 0 || StationTrackIds(id).Count == 0) continue;
                list.Add((id, J.Str(d["name_key"], ""), J.Int(d["unlock_level"], 1)));
            }
            return list;
        }

        /// <summary>
        /// Web trackFor: the real track behind a scene context ("" = none). Godot aliases (home, home_night, garden, city,
        /// shop, vet, arcade, ranch…), extra contexts (vacation, park, space), "game:&lt;id&gt;" with the Godot game tracks
        /// first, then audio/game_music.json, then the arcade loop. <paramref name="sleeping"/> picks the bedroom's
        /// sleeping variant.
        /// </summary>
        public static string TrackFor(string context, bool sleeping = false)
        {
            if (string.IsNullOrEmpty(context)) return "";
            if (Data["extra_context_tracks"] is JObject extra && extra[context] != null) return J.Str(extra[context], "");
            var unity = ContentSource.LoadObject(SceneMusicPath);
            if (unity["contexts"] is JObject own && own[context] != null) return J.Str(own[context], "");
            if (context == "home_night") sleeping = true;
            var token = Data["context_aliases"] is JObject aliases && aliases[context] != null ? J.Str(aliases[context], context) : context;
            if (token.Length == 0) return "";
            var matches = Tracks.Values.Where(t => t.Context == token && !IsStinger(t.Id)).Select(t => t.Id)
                .OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (matches.Count == 0 && token.StartsWith("game:", StringComparison.Ordinal))
            {
                var mapped = unity["games"] is JObject games ? J.Str(games[token.Substring(5)], "") : "";
                if (mapped.Length > 0 && Get(mapped) != null) return mapped;
                return TrackFor("arcade");
            }
            if (matches.Count == 0) return "";
            if (token == "room:bedroom")
            {
                var want = sleeping ? "sleeping" : "awake";
                foreach (var id in matches)
                    if (Get(id).Variant == want) return id;
            }
            return matches[0];
        }
    }
}
