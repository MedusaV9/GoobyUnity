using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Save
{
    /// <summary>
    /// Save v5 schema — port of scripts/state/save_schema.gd: defaults, deep merge with the
    /// corruption contract, leaf clamps and the additive slice registry (packs register slices
    /// without touching this file). Keys are camelCase VERBATIM (byte-compatible with Godot/web saves).
    /// </summary>
    public static class SaveSchema
    {
        public const int SchemaVersion = 5;

        public static readonly IReadOnlyDictionary<string, double> DefaultStats = new Dictionary<string, double>
        {
            { "hunger", 80.0 }, { "energy", 90.0 }, { "hygiene", 85.0 }, { "fun", 70.0 },
        };

        public static readonly IReadOnlyDictionary<string, int> StarterFood = new Dictionary<string, int>
        {
            { "carrot", 3 }, { "apple", 1 }, { "cupcake", 1 },
        };

        public static readonly string[] StationIds = { "bordmusik", "gooby-fm", "recap-fm", "game-fm", "alle" };
        public const long FutureStampSlackMs = 86400000L;
        public static readonly string[] ParkRideIds = { "coaster", "wheel" };
        public const int ParkMaxCount = 99999;
        public const int GardenPlots = 6;

        public sealed class SliceRegistration
        {
            public Func<JObject> Default;
            public Func<JToken, JObject> Normalize;
        }

        private static readonly Dictionary<string, SliceRegistration> ExtraSlices = new Dictionary<string, SliceRegistration>();

        /// <summary>Register an additive top-level slice (re-registering replaces).</summary>
        public static void RegisterSlice(string id, Func<JObject> defaultFactory, Func<JToken, JObject> normalize = null)
        {
            ExtraSlices[id] = new SliceRegistration { Default = defaultFactory, Normalize = normalize };
        }

        public static void UnregisterSlice(string id) => ExtraSlices.Remove(id);

        public static IEnumerable<string> RegisteredSliceIds() => ExtraSlices.Keys;

        public static JObject DefaultPlot() => new JObject
        {
            ["crop"] = JValue.CreateNull(), ["plantedAt"] = 0, ["progressMin"] = 0,
            ["wateredUntil"] = 0, ["waterings"] = 0, ["fertilized"] = false,
        };

        public static JObject DefaultCounters()
        {
            var c = new JObject { ["feeds"] = 0, ["washes"] = 0, ["sleeps"] = 0, ["trips"] = 0, ["tickles"] = 0 };
            c["petsToday"] = 0;
            c["petsDay"] = "";
            foreach (var k in new[]
                     {
                         "harvests", "plantings", "waterings", "sells", "cures", "vetTrips", "deliveries", "questsDone",
                         "photosTaken", "nightPlays", "medsGiven", "balls", "nougatGlobs", "cakesServed", "perfectCakes",
                         "surfRuns", "surfDistanceM", "races", "ghostsCaught", "rescues", "cratesShipped", "codesRedeemed",
                         "modifierPlays", "recapsSeen", "radioMinutes", "galleryPhotos",
                     })
                c[k] = 0;
            return c;
        }

        public static JObject DefaultStatsObj()
        {
            var o = new JObject();
            foreach (var kv in DefaultStats) o[kv.Key] = kv.Value;
            return o;
        }

        /// <summary>Fresh save-state per schema v5. nowMs stamps createdAt/lastTickAt.</summary>
        public static JObject DefaultState(long nowMs)
        {
            var food = new JObject();
            foreach (var kv in StarterFood) food[kv.Key] = kv.Value;
            var grid = new JArray();
            for (var i = 0; i < GardenPlots; i++) grid.Add(DefaultPlot());
            var state = new JObject
            {
                ["v"] = SchemaVersion,
                ["meta"] = new JObject
                {
                    ["createdAt"] = nowMs,
                    ["playerName"] = "",
                    ["goobyNickname"] = "Gooby",
                    ["charMorphs"] = new JObject { ["eyes_apart"] = 0.0, ["eye_scale"] = 1.0, ["ear_len"] = 1.0, ["chubby"] = 0.0 },
                    ["importedFrom"] = "",
                    ["importedAt"] = 0,
                },
                ["gooby"] = new JObject
                {
                    ["stats"] = DefaultStatsObj(),
                    ["sleep"] = new JObject { ["sleeping"] = false, ["startedAt"] = 0, ["wakeAt"] = 0 },
                    ["grumpyUntil"] = 0,
                    ["lastTickAt"] = nowMs,
                    ["health"] = new JObject
                    {
                        ["state"] = "healthy", ["junkScore"] = 0, ["neglectMin"] = 0, ["recoverMin"] = 0, ["since"] = 0,
                    },
                    ["weight"] = 50.0,
                },
                ["economy"] = Economy.DefaultSlice(),
                ["progression"] = new JObject { ["level"] = 1, ["xp"] = 0 },
                ["inventory"] = new JObject
                {
                    ["food"] = food,
                    ["items"] = new JObject { ["medicine"] = 0, ["fertilizer"] = 0 },
                },
                ["home"] = new JObject
                {
                    ["v"] = 1,
                    ["rooms"] = new JObject(),
                    ["unlockedRooms"] = new JArray("hall", "living", "kitchen", "bathroom", "bedroom"),
                    ["storage"] = new JArray(),
                    ["storageCapacity"] = 100,
                    ["movingDay"] = false,
                },
                ["decor"] = new JObject { ["wallpaper"] = new JObject(), ["floor"] = new JObject() },
                ["cosmetics"] = new JObject
                {
                    ["outfits"] = new JObject
                    {
                        ["owned"] = new JArray(),
                        ["equipped"] = new JObject
                        {
                            ["hat"] = JValue.CreateNull(), ["glasses"] = JValue.CreateNull(),
                            ["neck"] = JValue.CreateNull(), ["back"] = JValue.CreateNull(),
                        },
                    },
                    ["fur"] = new JObject { ["owned"] = new JArray("cream"), ["equipped"] = "cream" },
                },
                ["stickers"] = new JObject { ["unlocked"] = new JObject(), ["seen"] = new JObject() },
                ["garden"] = new JObject { ["plotsOwned"] = 4, ["grid"] = grid, ["lastTickAt"] = 0 },
                ["vacation"] = Vacation.DefaultSlice(),
                ["gooberando"] = Gooby.Core.City.GooberandoLogic.DefaultSlice(),
                ["park"] = new JObject
                {
                    ["visits"] = 0, ["nightVisit"] = false,
                    ["rides"] = new JObject { ["coaster"] = 0, ["wheel"] = 0 },
                    ["handsUp"] = 0, ["candyBought"] = 0,
                },
                ["minigames"] = new JObject
                {
                    ["plays"] = new JObject(),
                    ["legacy"] = new JObject
                    {
                        ["best"] = new JObject(), ["bestByDiff"] = new JObject(), ["endlessBest"] = new JObject(),
                        ["beaten"] = new JObject(), ["lastPlayDay"] = new JObject(),
                    },
                },
                ["achievements"] = new JObject { ["unlocked"] = new JObject(), ["counters"] = DefaultCounters() },
                ["daily"] = new JObject { ["lastClaimDay"] = "", ["streak"] = 0 },
                ["collections"] = new JObject { ["entries"] = new JObject(), ["claimedSets"] = new JObject() },
                ["quests"] = new JObject { ["completedTotal"] = 0 },
                ["quickDelivery"] = false,
                ["radio"] = new JObject
                {
                    ["owned"] = false, ["station"] = "bordmusik", ["playing"] = false, ["shuffle"] = true,
                    ["replaceContext"] = true, ["lastTrack"] = "", ["trims"] = new JObject(), ["recapHeard"] = new JObject(),
                },
                ["codes"] = new JObject
                {
                    ["redeemed"] = new JObject(), ["lockUntil"] = 0,
                    ["buffs"] = new JObject { ["doubleCoinsUntil"] = 0 },
                },
                ["recap"] = new JObject { ["history"] = new JArray() },
                ["gallery"] = new JObject { ["legacyCount"] = 0 },
                ["camera"] = new JObject { ["owned"] = false },
                ["easterEggs"] = new JObject { ["nougat"] = new JObject { ["lastGlobAt"] = 0, ["installed"] = false } },
                ["profile"] = new JObject { ["playtimeMin"] = 0, ["distanceM"] = 0, ["photos"] = 0 },
                ["settings"] = new JObject { ["managedBy"] = "AppSettings", ["imported"] = new JObject() },
                ["onboarding"] = new JObject { ["done"] = false, ["whatsNew5Seen"] = false },
                ["migration"] = new JObject
                {
                    ["notes"] = new JArray(), ["lost"] = new JArray(), ["interruptedVacation"] = JValue.CreateNull(),
                },
            };
            foreach (var kv in ExtraSlices) state[kv.Key] = kv.Value.Default();
            return state;
        }

        public struct NormalizeResult
        {
            public bool Ok;
            public JObject State;
            public string Error;
        }

        /// <summary>Validate/normalize a parsed v5 payload. ok=false == CORRUPT (recovery case).</summary>
        public static NormalizeResult Normalize(JToken raw, long nowMs)
        {
            if (!(raw is JObject rawObj)) return Err("save is not an object");
            var v = rawObj["v"];
            if (!J.IsNum(v) || Math.Floor(v.Value<double>()) != v.Value<double>() || v.Value<double>() < 0)
                return Err($"absurd save version {v?.ToString() ?? "null"}");
            var vi = (int)v.Value<double>();
            if (vi > SchemaVersion) return Err($"forward version {vi} > {SchemaVersion}");
            if (vi < SchemaVersion) return Err($"version {vi} needs migration first (SaveMigration)");
            var merged = MergeDefaults(DefaultState(nowMs), rawObj, "save");
            if (!merged.Ok) return Err(merged.Error);
            var s = (JObject)merged.Value;
            ClampCore(s, rawObj, nowMs);
            foreach (var kv in ExtraSlices)
                if (kv.Value.Normalize != null) s[kv.Key] = kv.Value.Normalize(s[kv.Key]);
            return new NormalizeResult { Ok = true, State = s, Error = "" };
        }

        public struct MergeResult
        {
            public bool Ok;
            public JToken Value;
            public string Error;
        }

        /// <summary>
        /// Deep-merge src over defaults (web mergeDefaults + F2 contract): every schema key exists;
        /// structural type mismatches make the payload corrupt; null never clobbers a structured default.
        /// </summary>
        public static MergeResult MergeDefaults(JObject defaults, JObject src, string path = "save")
        {
            var outObj = J.CloneObj(defaults);
            foreach (var prop in src.Properties())
            {
                var k = prop.Name;
                var v = prop.Value;
                var d = defaults[k];
                var here = path + "." + k;
                if (d is JObject dObj)
                {
                    if (v == null || v.Type == JTokenType.Null) continue;
                    if (!(v is JObject vObj)) return new MergeResult { Ok = false, Error = here + " must be an object" };
                    var sub = MergeDefaults(dObj, vObj, here);
                    if (!sub.Ok) return sub;
                    outObj[k] = sub.Value;
                }
                else if (d is JArray)
                {
                    if (v == null || v.Type == JTokenType.Null) continue;
                    if (!(v is JArray)) return new MergeResult { Ok = false, Error = here + " must be an array" };
                    outObj[k] = v.DeepClone();
                }
                else
                {
                    if (d != null && d.Type != JTokenType.Null && (v is JObject || v is JArray))
                        return new MergeResult { Ok = false, Error = $"{here} must be a {TypeName(d)}" };
                    outObj[k] = v.DeepClone();
                }
            }
            return new MergeResult { Ok = true, Value = outObj, Error = "" };
        }

        private static string TypeName(JToken d)
        {
            switch (d.Type)
            {
                case JTokenType.Integer: return "int";
                case JTokenType.Float: return "float";
                case JTokenType.String: return "String";
                case JTokenType.Boolean: return "bool";
                default: return d.Type.ToString();
            }
        }

        /// <summary>Leaf clamps (port of the web validate() rules guarding the core paths).</summary>
        private static void ClampCore(JObject s, JObject raw, long nowMs)
        {
            var gooby = (JObject)s["gooby"];
            var stats = (JObject)gooby["stats"];
            foreach (var kv in DefaultStats)
            {
                var n = J.LeafNum(stats[kv.Key]);
                stats[kv.Key] = J.Finite(n) ? Math.Min(100.0, Math.Max(0.0, n)) : kv.Value;
            }
            var sleep = (JObject)gooby["sleep"];
            if (!J.IsNum(sleep["startedAt"]) || !J.IsNum(sleep["wakeAt"]))
                gooby["sleep"] = new JObject { ["sleeping"] = false, ["startedAt"] = 0, ["wakeAt"] = 0 };
            else
                sleep["sleeping"] = J.IsTrue(sleep["sleeping"]);
            gooby["grumpyUntil"] = Math.Max(0.0, J.LeafNumOrZero(gooby["grumpyUntil"]));
            gooby["lastTickAt"] = Math.Max(0.0, J.LeafNumOrZero(gooby["lastTickAt"]));
            var w = J.LeafNum(gooby["weight"]);
            gooby["weight"] = J.Finite(w) ? Math.Min(95.0, Math.Max(5.0, w)) : 50.0;
            var health = (JObject)gooby["health"];
            var hs = J.Str(health["state"], null);
            if (hs != "healthy" && hs != "queasy" && hs != "sick") health["state"] = "healthy";

            var econ = (JObject)s["economy"];
            foreach (var key in new[] { "coins", "coinsEarned", "coinsSpent", "dayCoins", "endlessCoins" })
                econ[key] = Math.Max(0, (int)Math.Floor(J.LeafNumOrZero(econ[key])));
            foreach (var key in new[] { "dayCoinsDay", "endlessCoinsDay" })
                if (!J.IsString(econ[key])) econ[key] = "";

            var prog = (JObject)s["progression"];
            prog["level"] = J.ClampI((int)Math.Floor(J.LeafNumOr(prog["level"], 1.0)), 1, Leveling.MaxLevel);
            prog["xp"] = Math.Max(0.0, J.LeafNumOrZero(prog["xp"]));

            // inventory maps are taken VERBATIM when present (consumed food must stay consumed).
            if (raw["inventory"] is JObject rawInv)
            {
                if (rawInv["food"] is JObject f) ((JObject)s["inventory"])["food"] = f.DeepClone();
                if (rawInv["items"] is JObject it) ((JObject)s["inventory"])["items"] = it.DeepClone();
            }

            var garden = (JObject)s["garden"];
            var grid = garden["grid"] as JArray ?? new JArray();
            var plots = new JArray();
            for (var i = 0; i < GardenPlots; i++)
            {
                var plot = i < grid.Count ? grid[i] : null;
                if (plot is JObject po)
                {
                    var merged = DefaultPlot();
                    merged.Merge(po, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    plots.Add(merged);
                }
                else plots.Add(DefaultPlot());
            }
            garden["grid"] = plots;
            garden["plotsOwned"] = J.ClampI((int)J.LeafNumOr(garden["plotsOwned"], 4.0), 0, 6);

            s["vacation"] = Vacation.SliceOf(s);
            s["gooberando"] = Gooby.Core.City.GooberandoLogic.SliceOf(s);
            ClampPark(s);

            var radio = (JObject)s["radio"];
            radio["owned"] = J.IsTrue(radio["owned"]);
            var station = J.Str(radio["station"], null);
            if (station == null || Array.IndexOf(StationIds, station) < 0) radio["station"] = "bordmusik";
            radio["playing"] = J.IsTrue(radio["playing"]);

            var codes = (JObject)s["codes"];
            codes["lockUntil"] = ClampStamp(codes["lockUntil"], nowMs);
            var buffs = (JObject)codes["buffs"];
            buffs["doubleCoinsUntil"] = ClampStamp(buffs["doubleCoinsUntil"], nowMs);
            if (codes["redeemed"] is JObject redeemed)
            {
                foreach (var p in new List<JProperty>(redeemed.Properties()))
                {
                    var at = J.LeafNumOrZero(p.Value);
                    redeemed[p.Name] = at > 0.0 ? (JToken)at : 1;
                }
            }

            var daily = (JObject)s["daily"];
            if (!J.IsString(daily["lastClaimDay"])) daily["lastClaimDay"] = "";
            daily["streak"] = Math.Max(0, (int)Math.Floor(J.LeafNumOrZero(daily["streak"])));

            ((JObject)s["gallery"])["legacyCount"] = J.ClampI((int)Math.Floor(J.LeafNumOrZero(((JObject)s["gallery"])["legacyCount"])), 0, 40);
            ((JObject)s["camera"])["owned"] = J.IsTrue(((JObject)s["camera"])["owned"]);
            s["quickDelivery"] = J.IsTrue(s["quickDelivery"]);
            var onboarding = (JObject)s["onboarding"];
            onboarding["done"] = J.IsTrue(onboarding["done"]);
            onboarding["whatsNew5Seen"] = J.IsTrue(onboarding["whatsNew5Seen"]);

            var meta = (JObject)s["meta"];
            var morphs = (JObject)meta["charMorphs"];
            morphs["eyes_apart"] = J.Clamp(J.LeafNumOrZero(morphs["eyes_apart"]), -1.0, 1.0);
            morphs["eye_scale"] = J.Clamp(J.LeafNumOr(morphs["eye_scale"], 1.0), 0.7, 1.4);
            morphs["ear_len"] = J.Clamp(J.LeafNumOr(morphs["ear_len"], 1.0), 0.7, 1.4);
            morphs["chubby"] = J.Clamp(J.LeafNumOrZero(morphs["chubby"]), 0.0, 1.0);
            if (!J.IsString(meta["playerName"])) meta["playerName"] = "";
            if (!J.IsString(meta["goobyNickname"])) meta["goobyNickname"] = "Gooby";
        }

        private static void ClampPark(JObject s)
        {
            var park = (JObject)s["park"];
            park["visits"] = ClampCount(park["visits"]);
            park["nightVisit"] = J.IsTrue(park["nightVisit"]);
            var rawRides = park["rides"] as JObject ?? new JObject();
            var rides = new JObject();
            foreach (var id in ParkRideIds) rides[id] = ClampCount(rawRides[id]);
            park["rides"] = rides;
            park["handsUp"] = ClampCount(park["handsUp"]);
            park["candyBought"] = ClampCount(park["candyBought"]);
        }

        private static int ClampCount(JToken value)
        {
            var n = J.LeafNum(value);
            if (!J.Finite(n)) return 0;
            return J.ClampI((int)Math.Floor(n), 0, ParkMaxCount);
        }

        private static double ClampStamp(JToken value, long nowMs)
        {
            var n = J.LeafNumOrZero(value);
            if (n <= 0.0) return 0.0;
            return Math.Min(n, nowMs + (double)FutureStampSlackMs);
        }

        private static NormalizeResult Err(string message) => new NormalizeResult { Ok = false, State = new JObject(), Error = message };
    }
}
