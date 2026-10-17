using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// What the ranch quests' unlocks become (a Unity addition — in Godot they stayed trophies): three pieces of
    /// decoration money cannot buy go into the build store, the potato-sack saddle and the saddle pad with a bow into the
    /// gear cupboard, the misty coat and the fluffy look become looks for any horse (care sheet), the bronze ribbon hangs
    /// on the ribbon wall. Each unlock is delivered exactly once (<c>ranch.quests.geliefert</c>) — also for saves that
    /// finished their quests before the rewards existed, which get theirs the next time the farm opens.
    /// </summary>
    public static class RanchBelohnungen
    {
        public const string Deko = "deko", Gear = "gear", Look = "look", Trophae = "trophae";
        public const string Nebel = "nebel", Flausch = "flausch";
        public const string Ok = "ok", Gesperrt = "gesperrt", Unbekannt = "unbekannt";

        public sealed class Def
        {
            public string Id, Art, Ziel;
        }

        public static readonly IReadOnlyList<Def> Alle = new[]
        {
            new Def { Id = "deko_mistgabel_gold", Art = Deko, Ziel = "deko_mistgabel_gold" },
            new Def { Id = "deko_schleifenwand", Art = Deko, Ziel = "deko_schleifenwand" },
            new Def { Id = "girlande_ranchfest", Art = Deko, Ziel = "girlande_ranchfest" },
            new Def { Id = "sattel_kartoffel", Art = Gear, Ziel = "sattel_kartoffel" },
            new Def { Id = "schabracke_schleife", Art = Gear, Ziel = "decke_schleife" },
            new Def { Id = "fellmuster_nebel", Art = Look, Ziel = Nebel },
            new Def { Id = "fohlen_flauschig", Art = Look, Ziel = Flausch },
            new Def { Id = "schleife_bronze", Art = Trophae, Ziel = "schleife_bronze" },
        };

        public static Def Fuer(string id) => Alle.FirstOrDefault(d => d.Id == id);

        /// <summary>The unlock behind a build piece, a gear id or a look (null = none).</summary>
        public static Def VonZiel(string ziel) => Alle.FirstOrDefault(d => d.Ziel == ziel);

        public static bool Hat(JObject state, string unlockId) => RanchQuests.Freigeschaltet(state).Contains(unlockId);

        /// <summary>The quest that unlocks it (its giver and title tell the player where to earn it).</summary>
        public static RanchQuestKatalog.Quest Quelle(string unlockId) => RanchQuestKatalog.Alle.FirstOrDefault(q => q.Freischalten.Contains(unlockId));

        /// <summary>Delivers every unlocked reward not delivered yet; returns what arrived now.</summary>
        public static List<string> Liefern(JObject state)
        {
            var neu = new List<string>();
            if (state == null) return neu;
            var frei = RanchQuests.Freigeschaltet(state);
            if (frei.Count == 0) return neu;
            var slice = RanchQuests.Slice(state);
            var geliefert = (JArray)slice["geliefert"];
            foreach (var id in frei)
            {
                if (geliefert.Any(t => J.Str(t) == id)) continue;
                var def = Fuer(id);
                if (def == null) continue;
                if (def.Art == Deko) RanchBauState.LagerHinzu(RanchBauState.Im(state), def.Ziel);
                else if (def.Art == Gear) RanchWirtschaft.GearSchenken(state, def.Ziel);
                geliefert.Add(id);
                neu.Add(id);
            }
            return neu;
        }

        // ── looks ──

        /// <summary>The look unlock a look id needs.</summary>
        public static string LookUnlock(string look) => look == Nebel ? "fellmuster_nebel" : look == Flausch ? "fohlen_flauschig" : null;

        public static bool TraegtLook(JObject pferd, string look) =>
            look == Nebel ? J.Str(J.Path(pferd, "look", "fell")) == Nebel : look == Flausch && J.IsTrue(J.Path(pferd, "look", Flausch));

        /// <summary>The coat id a horse shows: the misty coat when it wears it, else its own colour.</summary>
        public static string FellId(JObject pferd, string fallback = "braun") =>
            TraegtLook(pferd, Nebel) ? Nebel : J.Str(pferd?["farbe"], fallback);

        /// <summary>Puts a look on a horse or takes it off (<c>pferd.look</c>; the coat genes stay untouched).</summary>
        public static string SetzeLook(JObject state, string pferdId, string look, bool an)
        {
            var unlock = LookUnlock(look);
            var pferd = RanchHof.Pferd(state, pferdId);
            if (unlock == null || pferd == null) return Unbekannt;
            if (an && !Hat(state, unlock)) return Gesperrt;
            var l = pferd["look"] as JObject ?? new JObject();
            if (look == Nebel)
            {
                if (an) l["fell"] = Nebel;
                else l.Remove("fell");
            }
            else if (an) l[Flausch] = true;
            else l.Remove(Flausch);
            if (l.Count > 0) pferd["look"] = l;
            else pferd.Remove("look");
            return Ok;
        }

        // ── the ribbon wall ──

        /// <summary>
        /// The rosettes on the ribbon wall: one per tournament ribbon the farm has won (1 gold, 2 silver, 3 bronze — best
        /// first), then the quest's bronze ribbon (0 = it, shown with a little star).
        /// </summary>
        public static List<int> Wandschleifen(JObject state)
        {
            var o = (J.Path(state, RanchHof.SliceId, "comp", "schleifen") as JObject ?? new JObject()).Properties()
                .Select(p => J.Int(p.Value)).Where(p => p >= 1 && p <= 3).OrderBy(p => p).ToList();
            if (Hat(state, "schleife_bronze")) o.Add(0);
            return o;
        }
    }
}
