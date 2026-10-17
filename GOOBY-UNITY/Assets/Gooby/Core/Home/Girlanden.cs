using System;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// Garlands spanned under a room's ceiling (port of the girlanden part of home_state.gd and girlanden_bau.gd's rules):
    /// <c>home.rooms[id].girlanden</c> lists {"typ", "zelle_a": [x, y], "zelle_b": [x, y]} — two ceiling cells the garland
    /// hangs between as a catenary. Spanning takes one piece from the storage, taking one down puts it back. The kinds are
    /// the catalog pieces with proc "girlande" (pennants, fairy lights, pompoms). Rooms only: the lawn has no ceiling.
    /// </summary>
    public static class Girlanden
    {
        public const string Wimpel = "girlande_wimpel", Lichter = "girlande_lichter", Pompons = "girlande_pompons";
        public const string Gespannt = "gespannt", Ungueltig = "ungueltig", LagerLeer = "storage_empty", LagerVoll = "storage_full";

        public static bool Ist(string itemId) => !string.IsNullOrEmpty(itemId) && FurnitureCatalog.Def(itemId)?.Proc == "girlande";

        public static bool Ist(FurnitureDef def) => def?.Proc == "girlande";

        /// <summary>A usable entry: a kind and two cells.</summary>
        public static bool Gueltig(JToken eintrag)
        {
            if (!(eintrag is JObject e) || J.Str(e["typ"]).Length == 0) return false;
            foreach (var key in new[] { "zelle_a", "zelle_b" })
                if (!(e[key] is JArray a) || a.Count < 2) return false;
            return true;
        }

        public static JArray Liste(GameStateStore gs, string roomId) => Liste(gs?.State, roomId);

        public static JArray Liste(JObject state, string roomId) =>
            J.Path(state, "home", "rooms", roomId, "girlanden") as JArray ?? new JArray();

        public static Cell Zelle(JToken eintrag, string key) =>
            eintrag?[key] is JArray a && a.Count >= 2 ? new Cell(J.Int(a[0]), J.Int(a[1])) : new Cell(0, 0);

        /// <summary>
        /// Spans a garland of <paramref name="typ"/> between two different cells of a <paramref name="w"/> × <paramref name="h"/>
        /// room: <see cref="Gespannt"/>, <see cref="Ungueltig"/> (same cell, outside, not a garland, the lawn) or
        /// <see cref="LagerLeer"/>.
        /// </summary>
        public static string Spannen(GameStateStore gs, string roomId, string typ, Cell a, Cell b, int w, int h)
        {
            if (!Ist(typ) || roomId == "garden" || a.Equals(b)) return Ungueltig;
            if (a.X < 0 || a.Y < 0 || b.X < 0 || b.Y < 0 || a.X >= w || b.X >= w || a.Y >= h || b.Y >= h) return Ungueltig;
            if (!HomeState.TakeFromStorage(gs, typ)) return LagerLeer;
            gs.Update(state =>
            {
                var room = J.EnsureDict(J.EnsureDict(J.EnsureDict(state, HomeState.SliceId), "rooms"), roomId);
                if (!(room["girlanden"] is JArray)) room["girlanden"] = new JArray();
                ((JArray)room["girlanden"]).Add(new JObject
                {
                    ["typ"] = typ,
                    ["zelle_a"] = new JArray(a.X, a.Y),
                    ["zelle_b"] = new JArray(b.X, b.Y),
                });
            });
            gs.NotifySliceChanged(HomeState.SliceId);
            return Gespannt;
        }

        /// <summary>Takes garland <paramref name="index"/> down into the storage; false when there is none or the storage is full.</summary>
        public static bool Abnehmen(GameStateStore gs, string roomId, int index)
        {
            var liste = Liste(gs, roomId);
            if (index < 0 || index >= liste.Count || !Gueltig(liste[index])) return false;
            if (!HomeState.StoreItem(gs, J.Str(liste[index]["typ"]))) return false;
            gs.Update(state =>
            {
                if (J.Path(state, "home", "rooms", roomId, "girlanden") is JArray l && index < l.Count) l.RemoveAt(index);
            });
            gs.NotifySliceChanged(HomeState.SliceId);
            return true;
        }

        /// <summary>The garland with an end on <paramref name="cell"/> (−1: none).</summary>
        public static int AnkerBei(JArray liste, Cell cell)
        {
            for (var i = 0; i < liste.Count; i++)
                if (Gueltig(liste[i]) && (Zelle(liste[i], "zelle_a").Equals(cell) || Zelle(liste[i], "zelle_b").Equals(cell))) return i;
            return -1;
        }

        /// <summary>Load-time heal: broken entries leave every room's list, good ones stay verbatim.</summary>
        public static void Heilen(JObject home)
        {
            if (!(home?["rooms"] is JObject rooms)) return;
            foreach (var p in rooms.Properties())
            {
                if (!(p.Value is JObject room) || room["girlanden"] == null) continue;
                var sauber = new JArray();
                if (room["girlanden"] is JArray roh)
                    foreach (var e in roh)
                        if (Gueltig(e)) sauber.Add(e);
                room["girlanden"] = sauber;
            }
        }
    }
}
