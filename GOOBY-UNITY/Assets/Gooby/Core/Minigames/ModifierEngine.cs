using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames
{
    /// <summary>
    /// Arcade modifier engine — port of scripts/minigames/modifier_engine.gd (the web modifierEngine.js on the v5 schema).
    /// Pure: no clock (callers pass nowMs), deterministic over the persisted mulberry32 stream. The first event comes
    /// <see cref="GraceMin"/> after the first boot, then every 50–120 min (seeded uniform); an event is one (game, type) pair,
    /// valid 45 min or its round budget. The host consumes a round at the real round start, an early quit refunds ≤ 1× per
    /// event; the bonuses run centrally through <see cref="MinigameAward"/> against the 150-coin day ledger.
    /// Slice "modifiers" = {nextAt, seed, current, lastGameId}, current = {gameId, type, startedAt, endsAt, playsLeft, refundUsed?}.
    /// </summary>
    public static class ModifierEngine
    {
        public const int GraceMin = 30, WindowMin = 45, CadenceMinLo = 50, CadenceMinHi = 120;
        public const long MsPerMin = 60000;
        public const int GlueckspilzMin = 10, GlueckspilzMax = 60;
        public const string SliceKey = "modifiers";
        public const string EventScheduled = "scheduled", EventStarted = "started", EventExpired = "expired", EventRescheduled = "rescheduled";
        /// <summary>Ticker event string of a started event: "modifierStarted:&lt;gameId&gt;:&lt;type&gt;".</summary>
        public const string StartedEventPrefix = "modifierStarted:";

        public sealed class TypeDef
        {
            public string Id, Icon, ColorHex;
            public int Plays, MinLevel;
            /// <summary>0 = the type has no such effect.</summary>
            public double CoinMult, ScoreMult, XpMult;
            public bool EnergyFree, Gluecksrolle;
            public string NameKey => "modifier.name." + Id;
            public string DescKey => "modifier.desc." + Id;
        }

        /// <summary>Stable roll order of the types (determinism of the seed stream).</summary>
        public static readonly string[] TypeIds = { "doppelGold", "muenzregen", "turbo", "lernrausch", "federleicht", "glueckspilz" };

        public static readonly IReadOnlyDictionary<string, TypeDef> Types = new Dictionary<string, TypeDef>
        {
            ["doppelGold"] = new TypeDef { Id = "doppelGold", Plays = 2, MinLevel = 2, CoinMult = 2.0, Icon = "coin", ColorHex = "#FFD44D" },
            ["muenzregen"] = new TypeDef { Id = "muenzregen", Plays = 3, MinLevel = 4, CoinMult = 1.5, Icon = "sparkle", ColorHex = "#40C9BF" },
            ["turbo"] = new TypeDef { Id = "turbo", Plays = 3, MinLevel = 6, ScoreMult = 1.5, Icon = "stopwatch", ColorHex = "#FF7A66" },
            ["lernrausch"] = new TypeDef { Id = "lernrausch", Plays = 3, MinLevel = 8, XpMult = 2.0, Icon = "book", ColorHex = "#8CBA5C" },
            ["federleicht"] = new TypeDef { Id = "federleicht", Plays = 3, MinLevel = 10, EnergyFree = true, Icon = "cloud", ColorHex = "#BAA6F0" },
            ["glueckspilz"] = new TypeDef { Id = "glueckspilz", Plays = 3, MinLevel = 12, Gluecksrolle = true, Icon = "food_mushroom", ColorHex = "#FF9ED9" },
        };

        /// <summary>Read-only view of the active event for one game — the one source for arcade badge, pregame banner and host gate.</summary>
        public sealed class Active
        {
            public string Type, GameId;
            public TypeDef Def;
            public int RemainingPlays;
            public long StartedAt, EndsAt;
            /// <summary>The launch payload for <see cref="MinigameAward.Award"/> (coin_mult, score_mult, xp_mult, energy_free, gluecksrolle).</summary>
            public JObject Params;
        }

        public static JObject DefaultSlice() => new JObject { ["nextAt"] = 0, ["seed"] = 0, ["current"] = null, ["lastGameId"] = "" };

        /// <summary>Defensive slice normalisation (save_schema registry callback in Godot).</summary>
        public static JObject NormalizeSlice(JToken raw)
        {
            var o = DefaultSlice();
            if (!(raw is JObject src)) return o;
            o["nextAt"] = Math.Max(0L, (long)Num(src["nextAt"]));
            o["seed"] = (long)Num(src["seed"]) & 0xFFFFFFFFL;
            if (src["lastGameId"]?.Type == JTokenType.String) o["lastGameId"] = (string)src["lastGameId"];
            if (src["current"] is JObject cur && Types.ContainsKey(Str(cur["type"])))
            {
                var plays = (long)Num(cur["playsLeft"]);
                var ends = (long)Num(cur["endsAt"]);
                if (plays > 0 && ends > 0 && Str(cur["gameId"]).Length > 0)
                    o["current"] = new JObject
                    {
                        ["gameId"] = Str(cur["gameId"]),
                        ["type"] = Str(cur["type"]),
                        ["startedAt"] = Math.Max(0L, (long)Num(cur["startedAt"])),
                        ["endsAt"] = ends,
                        ["playsLeft"] = plays,
                        ["refundUsed"] = IsTrue(cur["refundUsed"]),
                    };
            }
            return o;
        }

        /// <summary>Initial stream position from meta.createdAt; junk falls back to 1 so the sentinel 0 never sticks.</summary>
        public static long InitialSeed(JObject state)
        {
            var created = (long)Num(state?["meta"]?["createdAt"]);
            var seed = created & 0xFFFFFFFFL;
            return seed != 0 ? seed : 1;
        }

        /// <summary>One mulberry32 draw at the persisted stream position (web rand01).</summary>
        public static double Rand01(long seed) => new GoobyRng(seed).Next();

        public static List<string> UnlockedTypes(int level) => TypeIds.Where(id => level >= Types[id].MinLevel).ToList();

        /// <summary>Ids of all playable registry games (default candidates of a roll), in registry order.</summary>
        public static List<string> PlayableGameIds() => MinigameRegistry.Playable().Select(g => g.Id).ToList();

        /// <summary>All (game, type) pairs: type unlocked, game ≠ lastGameId; stable order (type order × game order).</summary>
        public static List<(string gameId, string type)> EligiblePairs(int level, string lastGameId = "", IList<string> gameIds = null)
        {
            var games = gameIds != null && gameIds.Count > 0 ? gameIds : PlayableGameIds();
            var pairs = new List<(string, string)>();
            foreach (var type in UnlockedTypes(level))
                foreach (var game in games)
                    if (game != lastGameId) pairs.Add((game, type));
            return pairs;
        }

        /// <summary>
        /// The scheduler tick (web §B4) — pure: never mutates <paramref name="state"/>; returns the new slice (null = no
        /// change) and the event ("", scheduled, started, expired, rescheduled).
        /// </summary>
        public static (JObject changes, string evt) Tick(JObject state, long nowMs, IList<string> gameIds = null)
        {
            JObject m;
            var changed = true;
            if (state?[SliceKey] is JObject baseSlice)
            {
                m = (JObject)baseSlice.DeepClone();
                changed = false;
            }
            else m = DefaultSlice();
            var evt = "";
            if ((long)Num(m["seed"]) == 0)
            {
                m["seed"] = InitialSeed(state);
                changed = true;
            }
            if (m["current"] is JObject cur && nowMs >= (long)Num(cur["endsAt"]))
            {
                // window over: the event goes, the no-repeat pin and the plan stay
                m["lastGameId"] = Str(cur["gameId"]);
                m["current"] = null;
                changed = true;
                evt = EventExpired;
            }
            if ((long)Num(m["nextAt"]) <= 0)
            {
                m["nextAt"] = nowMs + GraceMin * MsPerMin;
                changed = true;
                if (evt.Length == 0) evt = EventScheduled;
            }
            else if (!(m["current"] is JObject) && nowMs >= (long)Num(m["nextAt"]))
            {
                var level = Math.Max(1, (int)Num(state?["progression"]?["level"], 1.0));
                var (rolled, started) = RollEvent(m, nowMs, level, gameIds);
                m = rolled;
                changed = true;
                evt = started ? EventStarted : EventRescheduled;
            }
            return (changed ? m : null, evt);
        }

        /// <summary>Consumes one round at the REAL round start. Mutates state.modifiers; the snapshot is the event before the draw.</summary>
        public static (bool ok, JObject snapshot, bool cleared) Consume(JObject state, string gameId, long nowMs)
        {
            if (GetActiveFor(state, gameId, nowMs) == null) return (false, null, false);
            var m = (JObject)Dict(state[SliceKey]).DeepClone();
            var cur = Dict(m["current"]);
            var snapshot = (JObject)cur.DeepClone();
            var playsLeft = (long)Num(cur["playsLeft"]) - 1;
            if (playsLeft <= 0)
            {
                m["current"] = null;
                m["lastGameId"] = Str(cur["gameId"]);
            }
            else cur["playsLeft"] = playsLeft;
            state[SliceKey] = m;
            return (true, snapshot, playsLeft <= 0);
        }

        /// <summary>
        /// Early-quit refund (at most ONCE per event, anti-farming) while the original window is open — also when the last
        /// consume just emptied the event. Mutates state.modifiers.
        /// </summary>
        public static bool Refund(JObject state, JObject snapshot, long nowMs)
        {
            if (snapshot == null || snapshot.Count == 0 || IsTrue(snapshot["refundUsed"])) return false;
            if (nowMs >= (long)Num(snapshot["endsAt"])) return false;
            if (!(state[SliceKey] is JObject m) || m.Count == 0) return false;
            var o = (JObject)m.DeepClone();
            if (o["current"] is JObject cur && Str(cur["gameId"]) == Str(snapshot["gameId"]) && (long)Num(cur["startedAt"]) == (long)Num(snapshot["startedAt"]))
            {
                if (IsTrue(cur["refundUsed"])) return false;
                cur["playsLeft"] = (long)Num(cur["playsLeft"]) + 1;
                cur["refundUsed"] = true;
                state[SliceKey] = o;
                return true;
            }
            if (!(o["current"] is JObject) && Str(o["lastGameId"]) == Str(snapshot["gameId"]))
            {
                var restored = (JObject)snapshot.DeepClone();
                restored["playsLeft"] = 1;
                restored["refundUsed"] = true;
                o["current"] = restored;
                state[SliceKey] = o;
                return true;
            }
            return false;
        }

        /// <summary>The active event FOR this game, or null.</summary>
        public static Active GetActiveFor(JObject state, string gameId, long nowMs)
        {
            var cur = Dict(Dict(state?[SliceKey])["current"]);
            if (cur.Count == 0 || Str(cur["gameId"]) != gameId) return null;
            if ((long)Num(cur["playsLeft"]) <= 0 || nowMs >= (long)Num(cur["endsAt"])) return null;
            return Describe(cur);
        }

        /// <summary>The active event regardless of the game (arcade badge on its tile), or null.</summary>
        public static Active ActiveEvent(JObject state, long nowMs)
        {
            var cur = Dict(Dict(state?[SliceKey])["current"]);
            return cur.Count == 0 ? null : GetActiveFor(state, Str(cur["gameId"]), nowMs);
        }

        /// <summary>Launch payload for the host/award: plain numbers {type, coin_mult?, score_mult?, xp_mult?, energy_free?, gluecksrolle?}.</summary>
        public static JObject LaunchParams(JObject snapshot)
        {
            if (snapshot == null || !Types.TryGetValue(Str(snapshot["type"]), out var def)) return new JObject();
            var o = new JObject { ["type"] = def.Id };
            if (def.CoinMult > 0.0) o["coin_mult"] = def.CoinMult;
            if (def.ScoreMult > 0.0) o["score_mult"] = def.ScoreMult;
            if (def.XpMult > 0.0) o["xp_mult"] = def.XpMult;
            if (def.EnergyFree) o["energy_free"] = true;
            if (def.Gluecksrolle) o["gluecksrolle"] = true;
            return o;
        }

        /// <summary>Seeded luck roll (§C-SYS4.2): uniform 10–60, ONE draw (seed++). Mutates state.modifiers.seed.</summary>
        public static int RollGlueckspilz(JObject state)
        {
            var m = state[SliceKey] is JObject s && s.Count > 0 ? (JObject)s.DeepClone() : DefaultSlice();
            if ((long)Num(m["seed"]) == 0) m["seed"] = InitialSeed(state);
            var seed = (long)Num(m["seed"]);
            var span = GlueckspilzMax - GlueckspilzMin + 1;
            var bonus = GlueckspilzMin + (int)Math.Floor(Rand01(seed) * span);
            m["seed"] = (seed + 1) & 0xFFFFFFFFL;
            state[SliceKey] = m;
            return bonus;
        }

        /// <summary>QA/dev: start an event for an explicit pair NOW (level locks bypassed on purpose).</summary>
        public static bool ForceEvent(JObject state, string gameId, string type, long nowMs)
        {
            if (!Types.TryGetValue(type ?? "", out var def) || string.IsNullOrEmpty(gameId)) return false;
            var m = state[SliceKey] is JObject s && s.Count > 0 ? (JObject)s.DeepClone() : DefaultSlice();
            if ((long)Num(m["seed"]) == 0) m["seed"] = InitialSeed(state);
            m["current"] = new JObject
            {
                ["gameId"] = gameId, ["type"] = type, ["startedAt"] = nowMs, ["endsAt"] = nowMs + WindowMin * MsPerMin, ["playsLeft"] = def.Plays,
            };
            if ((long)Num(m["nextAt"]) <= nowMs) m["nextAt"] = nowMs + GraceMin * MsPerMin;
            state[SliceKey] = m;
            return true;
        }

        /// <summary>QA/dev: drop the active event; the plan (nextAt) stays.</summary>
        public static bool ClearEvent(JObject state)
        {
            if (!(state[SliceKey] is JObject m) || !(m["current"] is JObject cur)) return false;
            var o = (JObject)m.DeepClone();
            o["lastGameId"] = Str(cur["gameId"]);
            o["current"] = null;
            state[SliceKey] = o;
            return true;
        }

        /// <summary>Time left as "m:ss" for badge/banner (never negative).</summary>
        public static string CountdownText(long endsAt, long nowMs)
        {
            var restS = Math.Max(0, (int)Math.Ceiling((endsAt - nowMs) / 1000.0));
            return (restS / 60) + ":" + (restS % 60).ToString("00");
        }

        /// <summary>
        /// GoobyTicker adapter: one scheduler tick on the live tick and the offline catch-up; a started event comes back as
        /// "modifierStarted:&lt;gameId&gt;:&lt;type&gt;" (the toast).
        /// </summary>
        public sealed class Scheduler : IModifierScheduler
        {
            /// <summary>Candidate games (null = the playable registry games).</summary>
            public Func<IList<string>> GameIds { get; set; }

            public List<string> Tick(JObject state, long nowMs)
            {
                if (state == null) return new List<string>();
                if (state[SliceKey] != null) state[SliceKey] = NormalizeSlice(state[SliceKey]);
                var (changes, evt) = ModifierEngine.Tick(state, nowMs, GameIds?.Invoke());
                if (changes != null) state[SliceKey] = changes;
                if (evt != EventStarted) return new List<string>();
                var cur = Dict(Dict(state[SliceKey])["current"]);
                return new List<string> { StartedEventPrefix + Str(cur["gameId"]) + ":" + Str(cur["type"]) };
            }
        }

        /// <summary>Rolls the next event on a slice COPY (2 seed draws: pair index + cadence); no eligible pair → only re-plan.</summary>
        private static (JObject slice, bool started) RollEvent(JObject m, long nowMs, int level, IList<string> gameIds)
        {
            var o = (JObject)m.DeepClone();
            var pairs = EligiblePairs(level, Str(o["lastGameId"]), gameIds);
            var started = false;
            var seed = (long)Num(o["seed"]);
            if (pairs.Count > 0)
            {
                var (gameId, type) = pairs[(int)Math.Floor(Rand01(seed) * pairs.Count)];
                seed = (seed + 1) & 0xFFFFFFFFL;
                o["current"] = new JObject
                {
                    ["gameId"] = gameId, ["type"] = type, ["startedAt"] = nowMs, ["endsAt"] = nowMs + WindowMin * MsPerMin, ["playsLeft"] = Types[type].Plays,
                };
                started = true;
            }
            var cadence = CadenceMinLo + Rand01(seed) * (CadenceMinHi - CadenceMinLo);
            seed = (seed + 1) & 0xFFFFFFFFL;
            o["seed"] = seed;
            o["nextAt"] = (long)Math.Round(nowMs + cadence * MsPerMin, MidpointRounding.AwayFromZero);
            return (o, started);
        }

        private static Active Describe(JObject cur)
        {
            if (!Types.TryGetValue(Str(cur["type"]), out var def)) return null;
            return new Active
            {
                Type = def.Id,
                GameId = Str(cur["gameId"]),
                Def = def,
                RemainingPlays = (int)Num(cur["playsLeft"]),
                StartedAt = (long)Num(cur["startedAt"]),
                EndsAt = (long)Num(cur["endsAt"]),
                Params = LaunchParams(cur),
            };
        }

        private static JObject Dict(JToken t) => t as JObject ?? new JObject();

        private static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : "";

        private static bool IsTrue(JToken t) => t != null && t.Type == JTokenType.Boolean && (bool)t;

        /// <summary>GDScript _num: numbers as double, everything else the fallback.</summary>
        private static double Num(JToken t, double fallback = 0.0) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : fallback;
    }
}
