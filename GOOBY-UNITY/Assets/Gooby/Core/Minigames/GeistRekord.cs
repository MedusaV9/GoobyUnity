using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames
{
    /// <summary>
    /// The record ghost of the arcade games (port of geist_rekord.gd): records the score-over-time curve of a round and
    /// compares a running round live against the saved best run of the game. Time is the frame's game time (injected
    /// deltas — it only runs while the game really ticks: after the GO, not in pause or a countdown), no clock, no random.
    /// Save budget: one best-run curve per game under <c>minigames.geist.&lt;gameId&gt;</c> ({score, schritt_sec,
    /// dauer_sec, kurve}), sampled at 1 Hz; past <see cref="MaxStuetzen"/> samples every second one is dropped and the
    /// step doubles, so a curve never holds more than 160 integers. The ghost lives in the raw live score the player sees;
    /// the curve is replaced exactly when a regularly finished round with a live score beats the saved score (a tie does
    /// not), rounds without a live score never record one.
    /// </summary>
    public sealed class GeistRekord
    {
        public const double SchrittSec = 1.0;
        public const int MaxStuetzen = 160;
        public const int ScoreMax = 9_999_999;

        private List<int> _kurve = new List<int> { 0 };
        private double _schritt = SchrittSec, _naechste = SchrittSec;
        private JObject _referenz;

        /// <summary>The recorded round time in seconds.</summary>
        public double Zeit { get; private set; }

        /// <summary>The game reported a live score this round — the gate for the chip and for keeping a curve.</summary>
        public bool HatLiveScore { get; private set; }

        public bool HatReferenz => _referenz != null;

        /// <summary>A fresh round; <paramref name="referenz"/> is the saved best run (<see cref="RekordVon"/>), invalid ones are dropped.</summary>
        public void Starte(JObject referenz)
        {
            _kurve = new List<int> { 0 };
            _schritt = SchrittSec;
            Zeit = 0.0;
            _naechste = _schritt;
            HatLiveScore = false;
            _referenz = IstGueltig(referenz) ? referenz : null;
        }

        /// <summary>Advances the round time and keeps the live score at every sample boundary passed.</summary>
        public void Tick(double deltaSec, int score)
        {
            if (deltaSec <= 0.0) return;
            Zeit += deltaSec;
            while (Zeit >= _naechste)
            {
                _kurve.Add(ClampScore(score));
                _naechste += _schritt;
                if (_kurve.Count > MaxStuetzen) Verdichte();
            }
        }

        public void MeldeLiveScore() => HatLiveScore = true;

        /// <summary>Player minus ghost at the current round time (+ = ahead of the best run).</summary>
        public int DeltaAktuell(int score) => DeltaFuer(_referenz, Zeit, score);

        /// <summary>There was a best run and the final score beats it.</summary>
        public bool Geschlagen(int finalScore) => HatReferenz && ClampScore(finalScore) > RekordScore(_referenz);

        /// <summary>The round's best-run candidate for the save; null without a live score.</summary>
        public JObject Snapshot(int finalScore)
        {
            if (!HatLiveScore) return null;
            return new JObject
            {
                ["score"] = ClampScore(finalScore),
                ["schritt_sec"] = _schritt,
                ["dauer_sec"] = Zeit,
                ["kurve"] = new JArray(_kurve),
            };
        }

        /// <summary>The saved best run of a game or null (never throws).</summary>
        public static JObject RekordVon(JObject state, string gameId)
        {
            var rekord = J.Path(state, "minigames", "geist", gameId) as JObject;
            return IstGueltig(rekord) ? rekord : null;
        }

        public static bool IstGueltig(JObject rekord)
        {
            if (rekord == null || !(rekord["kurve"] is JArray kurve) || kurve.Count == 0) return false;
            return J.Num(rekord["schritt_sec"]) > 0.0;
        }

        public static int RekordScore(JObject rekord) => ClampScore((int)Math.Floor(Math.Min(J.Num(rekord?["score"]), ScoreMax)));

        /// <summary>
        /// The ghost's value at <paramref name="tSec"/>: linear between the samples; after the last sample the curve runs
        /// to the final score and stays there from dauer_sec on (a JSON round trip turns ints into floats — read both).
        /// </summary>
        public static double WertBei(JObject rekord, double tSec)
        {
            if (!IstGueltig(rekord)) return 0.0;
            var kurve = (JArray)rekord["kurve"];
            var schritt = J.NumOr(rekord["schritt_sec"], SchrittSec);
            var final = (double)RekordScore(rekord);
            var letzteZeit = (kurve.Count - 1) * schritt;
            var dauer = Math.Max(J.Num(rekord["dauer_sec"]), letzteZeit);
            if (tSec <= 0.0) return Math.Max(0.0, J.Num(kurve[0]));
            if (tSec >= dauer) return final;
            if (tSec >= letzteZeit)
            {
                var span = dauer - letzteZeit;
                if (span <= 0.0) return final;
                return Lerp(Math.Max(0.0, J.Num(kurve[kurve.Count - 1])), final, (tSec - letzteZeit) / span);
            }
            var pos = tSec / schritt;
            var i = (int)Math.Floor(pos);
            var a = Math.Max(0.0, J.Num(kurve[i]));
            var b = Math.Max(0.0, J.Num(kurve[Math.Min(i + 1, kurve.Count - 1)]));
            return Lerp(a, b, pos - i);
        }

        /// <summary>Player minus ghost at <paramref name="tSec"/> (no record = the delta to zero; callers gate on <see cref="HatReferenz"/>).</summary>
        public static int DeltaFuer(JObject rekord, double tSec, int score) =>
            score - (int)Math.Round(WertBei(rekord, tSec), MidpointRounding.AwayFromZero);

        /// <summary>
        /// Replaces <c>minigames.geist.&lt;gameId&gt;</c> exactly when <paramref name="snap"/> is valid and beats the saved
        /// score (or there is none yet). Mutates <paramref name="state"/> — call inside the store's update.
        /// </summary>
        public static bool UebernehmeRekord(JObject state, string gameId, JObject snap)
        {
            if (!IstGueltig(snap) || !(state?["minigames"] is JObject mg)) return false;
            var alt = RekordVon(state, gameId);
            if (alt != null && RekordScore(snap) <= RekordScore(alt)) return false;
            if (!(mg["geist"] is JObject)) mg["geist"] = new JObject();
            ((JObject)mg["geist"])[gameId] = snap.DeepClone();
            return true;
        }

        /// <summary>The chip's text: "+12" / "−5" (a real minus, U+2212) / "±0".</summary>
        public static string DeltaText(int delta) =>
            delta > 0 ? "+" + delta : delta < 0 ? "\u2212" + Math.Abs(delta) : "±0";

        /// <summary>Budget: every second sample goes, the step doubles — the time axis stays (sample i sits at i × step).</summary>
        private void Verdichte()
        {
            var neu = new List<int>();
            for (var i = 0; i < _kurve.Count; i += 2) neu.Add(_kurve[i]);
            _kurve = neu;
            _schritt *= 2.0;
            _naechste = _kurve.Count * _schritt;
        }

        private static int ClampScore(int score) => Math.Max(0, Math.Min(ScoreMax, score));

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
