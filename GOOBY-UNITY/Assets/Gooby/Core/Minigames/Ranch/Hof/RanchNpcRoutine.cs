using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The ranch people's day (port of rnpc_routine.gd over the routines of content/ranch_quests ranch_npcs.json): two to
    /// four stations a day ({von, ort, taetigkeit}); before the first station of the day the last one of the evening still
    /// holds (night wrap). The scenes decide which places they show and how long the walk from the station before takes.
    /// </summary>
    public static class RanchNpcRoutine
    {
        public const string Pfad = "content/ranch_quests/data/ranch_npcs";

        public sealed class Station
        {
            public double Von;
            public string Ort = "", Taetigkeit = "";
        }

        private static Dictionary<string, List<Station>> _alle;

        public static void ResetCache() => _alle = null;

        /// <summary>A person's stations in the order of the day (empty for someone without a routine).</summary>
        public static List<Station> Routine(string npc)
        {
            if (_alle == null)
            {
                _alle = new Dictionary<string, List<Station>>();
                foreach (var e in ContentSource.LoadItems(Pfad).OfType<JObject>())
                {
                    var stationen = (e["routine"] as JArray ?? new JArray()).OfType<JObject>()
                        .Select(r => new Station { Von = J.NumOr(r["von"], 0.0), Ort = J.Str(r["ort"]), Taetigkeit = J.Str(r["taetigkeit"]) })
                        .OrderBy(s => s.Von).ToList();
                    _alle[J.Str(e["id"])] = stationen;
                }
            }
            return _alle.TryGetValue(npc ?? "", out var r) ? r : new List<Station>();
        }

        /// <summary>The station at an hour (0..24); before the day's first one the evening's last still holds.</summary>
        public static Station StationBei(IReadOnlyList<Station> routine, double stunde)
        {
            if (routine == null || routine.Count == 0) return null;
            var aktiv = routine[routine.Count - 1];
            foreach (var s in routine)
                if (stunde >= s.Von) aktiv = s;
            return aktiv;
        }

        /// <summary>The station before <paramref name="jetzt"/> (where the walk comes from); null with a single station.</summary>
        public static Station Vorher(IReadOnlyList<Station> routine, Station jetzt)
        {
            if (routine == null || routine.Count < 2) return null;
            var i = routine.ToList().IndexOf(jetzt);
            return i < 0 ? null : routine[(i - 1 + routine.Count) % routine.Count];
        }

        /// <summary>The station after the one at this hour (the next change, for "from 2 pm at the paddock").</summary>
        public static Station Naechste(IReadOnlyList<Station> routine, double stunde)
        {
            var jetzt = StationBei(routine, stunde);
            if (jetzt == null || routine.Count < 2) return null;
            var i = routine.ToList().IndexOf(jetzt);
            return routine[(i + 1) % routine.Count];
        }

        /// <summary>Hours since the station began (wrapping over midnight).</summary>
        public static double SeitStunden(Station s, double stunde)
        {
            if (s == null) return 0.0;
            var seit = stunde - s.Von;
            return seit < 0.0 ? seit + 24.0 : seit;
        }

        public static Station StationBei(string npc, double stunde) => StationBei(Routine(npc), stunde);
    }
}
