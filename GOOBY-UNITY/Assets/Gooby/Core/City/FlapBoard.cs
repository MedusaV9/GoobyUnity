using System;
using System.Collections.Generic;
using System.Text;

namespace Gooby.Core.City
{
    /// <summary>
    /// The departure board in split-flap look (port of flap_board.gd): rows "Ziel | Abflug | Status" in capitals on a
    /// fixed grid; on opening or when a row changes every position clatters forward through the flap alphabet until it
    /// shows its letter, like a real Solari board. Deterministic and pure — the view feeds the time.
    /// </summary>
    public static class FlapBoard
    {
        /// <summary>The flap alphabet; letters outside it (Ä, Ü, ᴳ …) snap straight to their place.</summary>
        public const string Alphabet = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.:·-";
        public const double SchrittS = 0.035;
        public const int MaxSchritte = 64;
        public const int SpalteZiel = 17, SpalteAbflug = 15, SpalteStatus = 15;
        public const int BudgetMin = 20;

        public static readonly string[] StatusPool = { "status_boarding", "status_puenktlich", "status_mampft", "status_flauscht" };

        public static int Voll => SpalteZiel + SpalteAbflug + SpalteStatus;

        /// <summary>One flap of one position: forward through the alphabet, unknown letters snap.</summary>
        public static char ZeichenSchritt(char von, char nach)
        {
            if (von == nach) return von;
            var i = Alphabet.IndexOf(von);
            if (i < 0 || Alphabet.IndexOf(nach) < 0) return nach;
            return Alphabet[(i + 1) % Alphabet.Length];
        }

        /// <summary>One flap of a whole row (the shorter side is padded with blanks).</summary>
        public static string TextSchritt(string aktuell, string ziel)
        {
            var breite = Math.Max(aktuell.Length, ziel.Length);
            var von = aktuell.PadRight(breite);
            var nach = ziel.PadRight(breite);
            var outp = new StringBuilder(breite);
            for (var i = 0; i < breite; i++) outp.Append(ZeichenSchritt(von[i], nach[i]));
            return outp.ToString();
        }

        /// <summary>Every frame from <paramref name="von"/> to <paramref name="nach"/> (without the start, with the end).</summary>
        public static List<string> Sequenz(string von, string nach)
        {
            var frames = new List<string>();
            var breite = Math.Max(von.Length, nach.Length);
            var aktuell = von.PadRight(breite);
            var ziel = nach.PadRight(breite);
            while (aktuell != ziel && frames.Count < MaxSchritte)
            {
                aktuell = TextSchritt(aktuell, ziel);
                frames.Add(aktuell);
            }
            if (aktuell != ziel) frames.Add(ziel);
            return frames;
        }

        /// <summary>A column: capitals, cut to the width less two letters of air, padded.</summary>
        public static string Norm(string text, int breite)
        {
            if (breite <= 0) return "";
            var t = (text ?? "").ToUpperInvariant();
            var max = Math.Max(0, breite - 2);
            if (t.Length > max) t = t.Substring(0, max);
            return t.PadRight(breite);
        }

        /// <summary>The target text of a row on a grid of columns (a 0-wide column is left out).</summary>
        public static string Zeile(string ziel, string abflug, string status, int[] spalten = null)
        {
            spalten ??= new[] { SpalteZiel, SpalteAbflug, SpalteStatus };
            return Norm(ziel, spalten[0]) + Norm(abflug, spalten[1]) + Norm(status, spalten[2]);
        }

        /// <summary>The full board from 47 letters on; below, the departure column goes (Ziel | Status).</summary>
        public static int[] SpaltenFuerBudget(int budget)
        {
            if (budget >= Voll) return new[] { SpalteZiel, SpalteAbflug, SpalteStatus };
            var eng = Math.Max(BudgetMin, Math.Min(Voll, budget));
            var status = Math.Max(8, Math.Min(SpalteStatus, (int)Math.Round(eng * 0.4, MidpointRounding.AwayFromZero)));
            return new[] { eng - status, 0, status };
        }

        /// <summary>The gag status of a destination (stable per id); too expensive beats everything.</summary>
        public static string StatusKey(string zielId, bool kannZahlen)
        {
            if (!kannZahlen) return "reisepass.tafel.status_zu_teuer";
            return "reisepass.tafel." + StatusPool[Streu(zielId) % StatusPool.Length];
        }

        private static int Streu(string text)
        {
            var h = 7L;
            foreach (var c in text ?? "") h = (h * 31 + c) % 1000003;
            return (int)h;
        }
    }
}
