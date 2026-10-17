using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Random;

namespace Gooby.Core.Audio
{
    /// <summary>
    /// The car radio (music_director.gd radio_play): a station's tracks the player's level unlocks, shuffled with the
    /// injected RNG, played through and reshuffled — never the same track twice in a row across a reshuffle.
    /// </summary>
    public sealed class RadioQueue
    {
        private readonly GoobyRng _rng;
        private readonly List<string> _queue = new List<string>();
        private int _pos;

        public string Station { get; }
        public int Level { get; }
        public string Current { get; private set; }

        public RadioQueue(string station, int level, GoobyRng rng)
        {
            Station = station;
            Level = Math.Max(1, level);
            _rng = rng;
        }

        public static bool StationUnlocked(string station, int level) =>
            MusicRegistry.Stations().Any(s => s.id == station && s.unlockLevel <= Math.Max(1, level));

        /// <summary>The playable tracks of the station at this level (sorted).</summary>
        public List<string> Playable() =>
            MusicRegistry.StationTrackIds(Station).Where(id => (MusicRegistry.Get(id)?.UnlockLevel ?? 1) <= Level).ToList();

        /// <summary>The next track ("" when the station has nothing for this level).</summary>
        public string Next()
        {
            if (_pos >= _queue.Count) Reshuffle();
            if (_queue.Count == 0) return Current = "";
            Current = _queue[_pos++];
            return Current;
        }

        private void Reshuffle()
        {
            _queue.Clear();
            _queue.AddRange(Playable());
            for (var i = _queue.Count - 1; i > 0; i--)
            {
                var j = (int)Math.Floor(_rng.Next() * (i + 1));
                if (j > i) j = i;
                (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
            }
            if (_queue.Count > 1 && _queue[0] == Current) (_queue[0], _queue[1]) = (_queue[1], _queue[0]);
            _pos = 0;
        }
    }

    /// <summary>
    /// Gooby's Animal-Crossing babble (gooby_voice.gd): one baked syllable per two letters, chosen by a hash of the
    /// letter (the same word always sounds the same), ~11 syllables a second, ±12 % pitch jitter from the injected RNG,
    /// the emotion lifts or lowers the base pitch, a sentence falls at its end and a question rises.
    /// </summary>
    public static class GoobyBabble
    {
        public static readonly string[] Syllables = { "ba", "bi", "bo", "bu", "da", "do", "du", "gu", "ma", "mi", "na", "pa", "po", "wa" };
        public const double Rate = 11.0;
        public const double PitchJitter = 0.12;
        public const int MaxSyllables = 18;
        public const int MinSyllables = 4;

        public static readonly IReadOnlyDictionary<string, double> EmotionPitch = new Dictionary<string, double>
        {
            ["neutral"] = 1.0, ["happy"] = 1.15, ["ecstatic"] = 1.2, ["sad"] = 0.8, ["sleepy"] = 0.85,
            ["angry"] = 0.9, ["scared"] = 1.1, ["dizzy"] = 0.95,
        };

        public readonly struct Syllable
        {
            public readonly string Clip;
            public readonly double At;
            public readonly double Pitch;

            public Syllable(string clip, double at, double pitch)
            {
                Clip = clip;
                At = at;
                Pitch = pitch;
            }
        }

        public static List<Syllable> Plan(string text, string emotion, GoobyRng rng)
        {
            var letters = (text ?? "").Where(char.IsLetter).Select(char.ToLowerInvariant).ToList();
            var list = new List<Syllable>();
            if (letters.Count == 0) return list;
            var count = Math.Max(MinSyllables, Math.Min(MaxSyllables, (letters.Count + 1) / 2));
            var basePitch = emotion != null && EmotionPitch.TryGetValue(emotion, out var p) ? p : 1.0;
            var tempo = emotion == "sleepy" || emotion == "sad" ? 0.8 : emotion == "happy" || emotion == "ecstatic" ? 1.15 : 1.0;
            var question = (text ?? "").TrimEnd().EndsWith("?", StringComparison.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var c = letters[(i * 2) % letters.Count];
                var clip = Syllables[(c * 31 + 7) % Syllables.Length];
                var jitter = 1.0 + (rng.Next() * 2.0 - 1.0) * PitchJitter;
                var arc = 1.0;
                var tail = count - i;
                if (tail <= 3) arc = question ? 1.0 + 0.07 * (4 - tail) : 1.0 - 0.05 * (4 - tail);
                list.Add(new Syllable(clip, i / (Rate * tempo), basePitch * jitter * arc));
            }
            return list;
        }
    }
}
