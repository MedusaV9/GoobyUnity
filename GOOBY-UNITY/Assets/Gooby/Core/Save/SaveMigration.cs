using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Save
{
    /// <summary>
    /// Migration web v0..v4 → v5 — port of scripts/state/migration_v4.gd (Doc H §5.2 mapping table).
    /// <see cref="MigrateAny"/> takes a parsed web save (Umzugskoffer, iOS NSUserDefaults reader), replays the
    /// web migration chain (save.js migrations[0..3]) plus the validate() clamps, then maps v4 → v5.
    /// Highlights: coins verbatim + 250 moving bonus; level 1:1, XP → 0; stickers/outfits/skins/achievements
    /// verbatim; furniture.placed cannot map onto the free grid → all owned items into storage + movingDay;
    /// a running vacation is cancelled with a refund; losses are listed honestly in migration.lost.
    /// </summary>
    public static class SaveMigration
    {
        public const int MovingBonusCoins = 250;
        public const int WebVersion = 4;
        private static readonly int[] UiScaleStops = { 85, 100, 115, 130 };

        public struct MigrationResult
        {
            public bool Ok;
            public JObject State;
            public string Error;
            public JObject Report;
        }

        public static MigrationResult MigrateAny(JToken raw, long nowMs)
        {
            if (!(raw is JObject rawObj)) return Err("save is not an object");
            var v = 0;
            var vRaw = rawObj["v"];
            if (vRaw != null && vRaw.Type != JTokenType.Null)
            {
                if (!J.IsNum(vRaw) || vRaw.Value<double>() != Math.Floor(vRaw.Value<double>()) || vRaw.Value<double>() < 0.0)
                    return Err($"absurd save version {vRaw}");
                v = (int)vRaw.Value<double>();
                if (v > WebVersion) return Err($"version {v} is not a web save (expected 0..4)");
            }
            var state = J.CloneObj(rawObj);
            while (v < WebVersion)
            {
                switch (v)
                {
                    case 0: state = MigrateV0ToV1(state); break;
                    case 1: state = MigrateV1ToV2(state); break;
                    case 2: state = MigrateV2ToV3(state); break;
                    case 3: state = MigrateV3ToV4(state); break;
                }
                var next = state["v"];
                if (!J.IsNum(next) || (int)next.Value<double>() <= v) return Err($"migration from v{v} did not advance");
                v = (int)next.Value<double>();
            }
            var validated = ValidateV4(state, nowMs);
            if (!validated.Ok) return Err(validated.Error);
            return MapV4ToV5(validated.State, nowMs);
        }

        // ── web migrations[0..3] ports ─────────────────────────────────────

        private static JObject MigrateV0ToV1(JObject state)
        {
            var o = J.CloneObj(state);
            o["v"] = 1;
            return o;
        }

        private static bool NullOrObject(JToken t) => t == null || t.Type == JTokenType.Null || t is JObject;

        private static JObject ObjOrEmpty(JToken t) => t is JObject o ? J.CloneObj(o) : new JObject();

        private static JObject MigrateV1ToV2(JObject state)
        {
            var o = SpreadDefaultsFirst(V2SliceDefaults(), state);
            o["v"] = 2;
            MergeCountersDefaultsFirst(o, V2CounterDefaults());
            if (NullOrObject(o["onboarding"]))
            {
                var onboarding = ObjOrEmpty(o["onboarding"]);
                onboarding["whatsNew2Seen"] = false;
                o["onboarding"] = onboarding;
            }
            return o;
        }

        private static JObject MigrateV2ToV3(JObject state)
        {
            var o = SpreadDefaultsFirst(V3SliceDefaults(), state);
            o["v"] = 3;
            if (NullOrObject(o["settings"])) o["settings"] = SpreadDefaultsFirst(V3SettingsDefaults(), ObjOrEmpty(o["settings"]));
            if (o["outfits"] is JObject outfits && NullOrObject(outfits["equipped"]))
            {
                var equipped = ObjOrEmpty(outfits["equipped"]);
                if (equipped["back"] == null) equipped["back"] = JValue.CreateNull();
                var copy = J.CloneObj(outfits);
                copy["equipped"] = equipped;
                o["outfits"] = copy;
            }
            MergeCountersDefaultsFirst(o, V3CounterDefaults());
            if (NullOrObject(o["onboarding"]))
            {
                var onboarding = ObjOrEmpty(o["onboarding"]);
                onboarding["whatsNew3Seen"] = false;
                o["onboarding"] = onboarding;
            }
            return o;
        }

        private static JObject MigrateV3ToV4(JObject state)
        {
            var o = SpreadDefaultsFirst(V4SliceDefaults(), state);
            o["v"] = 4;
            if (NullOrObject(o["settings"])) o["settings"] = SpreadDefaultsFirst(V4SettingsDefaults(), ObjOrEmpty(o["settings"]));
            if (o["minigames"] is JObject mg) o["minigames"] = SpreadDefaultsFirst(V4MinigameDefaults(), mg);
            MergeCountersDefaultsFirst(o, V4CounterDefaults());
            if (o["furniture"] is JObject furnitureRaw)
            {
                var furniture = J.CloneObj(furnitureRaw);
                if (furniture["owned"] is JArray owned && !Contains(owned, "radio")) owned.Add("radio");
                if (furniture["placed"] is JObject placed && placed["living:shelf1"] == null) placed["living:shelf1"] = "radio";
                o["furniture"] = furniture;
            }
            if (NullOrObject(o["onboarding"]))
            {
                var onboarding = ObjOrEmpty(o["onboarding"]);
                onboarding["whatsNew4Seen"] = false;
                o["onboarding"] = onboarding;
            }
            return o;
        }

        private static bool Contains(JArray arr, string value)
        {
            foreach (var t in arr) if (t.Type == JTokenType.String && t.Value<string>() == value) return true;
            return false;
        }

        // ── web validate() port (subset guarding the v5-mapped fields) ─────

        private static SaveSchema.NormalizeResult ValidateV4(JObject state, long nowMs)
        {
            var merged = SaveSchema.MergeDefaults(V4DefaultState(nowMs), state, "web-save");
            if (!merged.Ok) return new SaveSchema.NormalizeResult { Ok = false, State = new JObject(), Error = merged.Error };
            var s = (JObject)merged.Value;
            var stats = (JObject)s["stats"];
            foreach (var kv in SaveSchema.DefaultStats)
            {
                var n = J.LeafNum(stats[kv.Key]);
                stats[kv.Key] = J.Finite(n) ? Math.Min(100.0, Math.Max(0.0, n)) : kv.Value;
            }
            s["coins"] = Math.Max(0, (int)Math.Floor(J.LeafNumOrZero(s["coins"])));
            s["xp"] = Math.Max(0.0, J.LeafNumOrZero(s["xp"]));
            s["level"] = J.ClampI((int)Math.Floor(J.LeafNumOr(s["level"], 1.0)), 1, Leveling.MaxLevel);
            var sleep = (JObject)s["sleep"];
            if (!J.IsNum(sleep["startedAt"]) || !J.IsNum(sleep["wakeAt"]))
                s["sleep"] = new JObject { ["sleeping"] = false, ["startedAt"] = 0, ["wakeAt"] = 0 };
            else
                sleep["sleeping"] = J.IsTrue(sleep["sleeping"]);
            if (state["inventory"] is JObject inv) s["inventory"] = J.CloneObj(inv);
            var weight = (JObject)s["weight"];
            var w = J.LeafNum(weight["value"]);
            weight["value"] = J.Finite(w) ? Math.Min(95.0, Math.Max(5.0, w)) : 50.0;
            var health = (JObject)s["health"];
            var hs = J.Str(health["state"], null);
            if (hs != "healthy" && hs != "queasy" && hs != "sick") health["state"] = "healthy";
            var garden = (JObject)s["garden"];
            var plotsRaw = garden["plots"] as JArray ?? new JArray();
            var plots = new JArray();
            for (var i = 0; i < 6; i++)
            {
                var plot = i < plotsRaw.Count ? plotsRaw[i] : null;
                if (plot is JObject po)
                {
                    var mergedPlot = SaveSchema.DefaultPlot();
                    mergedPlot.Merge(po, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                    plots.Add(mergedPlot);
                }
                else plots.Add(SaveSchema.DefaultPlot());
            }
            garden["plots"] = plots;
            var settings = (JObject)s["settings"];
            var uiScaleRaw = J.LeafNum(settings["uiScale"]);
            var uiScale = !double.IsNaN(uiScaleRaw) ? (int)uiScaleRaw : -1;
            if (uiScale != uiScaleRaw || Array.IndexOf(UiScaleStops, uiScale) < 0) uiScale = 100;
            settings["uiScale"] = uiScale;
            var volDefaults = (JObject)V3SettingsDefaults()["volumes"];
            var volumes = J.EnsureDict(settings, "volumes");
            foreach (var p in volDefaults.Properties())
            {
                var vol = volumes[p.Name];
                volumes[p.Name] = J.IsNum(vol) ? J.ClampI((int)Math.Round(vol.Value<double>(), MidpointRounding.AwayFromZero), 0, 100) : p.Value.Value<int>();
            }
            settings["devUnlocked"] = J.IsTrue(settings["devUnlocked"]);
            if (state["furniture"] is JObject rawFurniture && rawFurniture["placed"] is JObject placed)
                ((JObject)s["furniture"])["placed"] = J.CloneObj(placed);
            var radio = (JObject)s["radio"];
            var station = J.Str(radio["station"], null);
            if (station == null || Array.IndexOf(SaveSchema.StationIds, station) < 0) radio["station"] = "bordmusik";
            radio["playing"] = J.IsTrue(radio["playing"]);
            if (radio["shuffle"]?.Type != JTokenType.Boolean) radio["shuffle"] = true;
            if (radio["replaceContext"]?.Type != JTokenType.Boolean) radio["replaceContext"] = true;
            if (!J.IsString(radio["lastTrack"])) radio["lastTrack"] = "";
            var trims = new JObject();
            if (radio["trims"] is JObject rawTrims)
            {
                foreach (var p in rawTrims.Properties())
                {
                    if (!(p.Value is JObject row)) continue;
                    var vol = J.LeafNum(row["vol"]);
                    var trim = J.CloneObj(row);
                    trim["vol"] = !double.IsNaN(vol) ? J.ClampI((int)Math.Round(vol, MidpointRounding.AwayFromZero), 0, 150) : 100;
                    trim["on"] = !IsFalse(row["on"]);
                    trims[p.Name] = trim;
                }
            }
            radio["trims"] = trims;
            if (radio["recapHeard"] is JObject recapHeard)
            {
                foreach (var p in new List<JProperty>(recapHeard.Properties()))
                {
                    var at = J.LeafNumOrZero(p.Value);
                    recapHeard[p.Name] = at > 0.0 ? (JToken)at : 1;
                }
            }
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
            var gallery = (JObject)s["gallery"];
            var count = J.LeafNum(gallery["count"]);
            gallery["count"] = !double.IsNaN(count) ? J.ClampI((int)Math.Floor(count), 0, 40) : 0;
            return new SaveSchema.NormalizeResult { Ok = true, State = s, Error = "" };
        }

        // ── v4 → v5 field mapping (Doc H §5.2) ────────────────────────────

        private static MigrationResult MapV4ToV5(JObject v4, long nowMs)
        {
            var s = SaveSchema.DefaultState(nowMs);
            var notes = new JArray();
            var lost = new JArray();

            var meta = (JObject)s["meta"];
            meta["createdAt"] = v4["createdAt"] ?? nowMs;
            meta["importedFrom"] = "web-v4";
            meta["importedAt"] = nowMs;

            var gooby = (JObject)s["gooby"];
            gooby["stats"] = J.Clone(v4["stats"]);
            gooby["sleep"] = J.Clone(v4["sleep"]);
            gooby["grumpyUntil"] = v4["grumpyUntil"] ?? 0;
            gooby["lastTickAt"] = v4["lastTickAt"] ?? nowMs;
            gooby["health"] = J.Clone(v4["health"]);
            gooby["weight"] = J.Clone(v4["weight"]["value"]);

            var econ = (JObject)s["economy"];
            econ["coins"] = (int)J.LeafNumOrZero(v4["coins"]) + MovingBonusCoins;
            var profile = J.Dict(v4["profile"]);
            econ["coinsEarned"] = (int)J.LeafNumOrZero(profile["coinsEarned"]);
            econ["coinsSpent"] = (int)J.LeafNumOrZero(profile["coinsSpent"]);
            notes.Add($"coins: +{MovingBonusCoins} Umzugsbonus");

            var prog = (JObject)s["progression"];
            prog["level"] = (int)J.LeafNumOr(v4["level"], 1.0);
            prog["xp"] = 0;
            lost.Add("xp: Rest-XP verschenkt (neue Multiplayer-Kurve, Level bleibt 1:1)");

            var inv = (JObject)s["inventory"];
            inv["food"] = J.Clone(v4["inventory"]);
            inv["items"] = J.Clone(v4["items"]);

            var boxed = 0;
            var owned = J.Dict(v4["furniture"])["owned"] as JArray ?? new JArray();
            var storage = new JArray();
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var item in owned)
            {
                if (item.Type != JTokenType.String) continue;
                var id = item.Value<string>();
                if (!counts.ContainsKey(id)) { counts[id] = 0; order.Add(id); }
                counts[id] += 1;
            }
            foreach (var id in order)
            {
                storage.Add(new JObject { ["item"] = id, ["variant"] = "default", ["count"] = counts[id] });
                boxed += counts[id];
            }
            var home = (JObject)s["home"];
            home["storage"] = storage;
            home["movingDay"] = true;
            lost.Add("furniture.placed: Slot-Layout nicht aufs Grid mappbar — Umzugstag-Flow");
            notes.Add($"furniture: {boxed} Items im Umzugskarton (home.storage)");

            s["decor"] = J.Clone(v4["decor"]);
            var cosmetics = (JObject)s["cosmetics"];
            cosmetics["outfits"] = J.Clone(v4["outfits"]);
            cosmetics["fur"] = J.Clone(v4["skins"]);
            s["stickers"] = J.Clone(v4["stickers"]);

            var garden = (JObject)s["garden"];
            var v4Garden = (JObject)v4["garden"];
            garden["plotsOwned"] = v4Garden["plotsOwned"] ?? 4;
            garden["grid"] = J.Clone(v4Garden["plots"]);
            garden["lastTickAt"] = v4Garden["lastTickAt"] ?? 0;

            var vac = Vacation.SliceOf(v4);
            JToken interrupted = JValue.CreateNull();
            if (J.Str(vac["phase"]) != Vacation.PhaseNone)
            {
                var refund = Vacation.TryGetDestination(J.Str(vac["destId"]), out var dest) ? dest.Price : 0;
                interrupted = new JObject
                {
                    ["destId"] = vac["destId"], ["phase"] = vac["phase"],
                    ["remainingMs"] = Vacation.RemainingMs(new JObject { ["vacation"] = vac }, nowMs),
                    ["refund"] = refund, ["postcards"] = vac["postcards"],
                };
                econ["coins"] = J.Int(econ["coins"]) + refund;
                notes.Add($"vacation: laufende Reise ({J.Str(vac["destId"])}) abgebrochen, {refund} Coins erstattet");
                vac["phase"] = Vacation.PhaseNone;
                vac["destId"] = "";
                vac["bookedAt"] = 0;
                vac["returnAt"] = 0;
                vac["pickupBy"] = 0;
            }
            s["vacation"] = vac;
            ((JObject)s["migration"])["interruptedVacation"] = interrupted;

            if (v4["themePark"] is JObject park) s["park"] = SpreadDefaultsFirst((JObject)s["park"], park);

            var v4Mg = (JObject)v4["minigames"];
            var mg = (JObject)s["minigames"];
            mg["plays"] = J.Clone(v4Mg["plays"] ?? new JObject());
            mg["legacy"] = new JObject
            {
                ["best"] = J.Clone(v4Mg["best"] ?? new JObject()),
                ["bestByDiff"] = J.Clone(v4Mg["bestByDiff"] ?? new JObject()),
                ["endlessBest"] = J.Clone(v4Mg["endlessBest"] ?? new JObject()),
                ["beaten"] = J.Clone(v4Mg["beaten"] ?? new JObject()),
                ["lastPlayDay"] = J.Clone(v4Mg["lastPlayDay"] ?? new JObject()),
            };
            mg["difficulty"] = v4Mg["difficulty"] is JObject diff ? J.CloneObj(diff) : new JObject();
            notes.Add("minigames: Bestwerte als Web-Rekorde in minigames.legacy");

            var ach = (JObject)s["achievements"];
            var v4Ach = (JObject)v4["achievements"];
            ach["unlocked"] = J.Clone(v4Ach["unlocked"]);
            var counters = SaveSchema.DefaultCounters();
            counters.Merge(v4Ach["counters"], new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            ach["counters"] = counters;

            s["daily"] = J.Clone(v4["daily"]);
            s["collections"] = J.Clone(v4["collections"]);
            ((JObject)s["quests"])["completedTotal"] = J.Dict(v4["quests"])["completedTotal"] ?? 0;
            lost.Add("quests.active: neues Quest-System (completedTotal bleibt)");
            s["quickDelivery"] = J.IsTrue(v4["quickDelivery"]);

            var radio = J.CloneObj((JObject)v4["radio"]);
            radio["owned"] = Contains(owned, "radio");
            s["radio"] = radio;
            s["codes"] = J.Clone(v4["codes"]);
            lost.Add("modifiers: System wird neu ausgerollt (seed neu)");

            var recapHist = J.Dict(v4["recap"])["history"];
            ((JObject)s["recap"])["history"] = recapHist is JArray ? J.Clone(recapHist) : new JArray();
            lost.Add("recap.baseline: wird nach dem Import neu gesnapshottet");
            lost.Add("recap.pendingLevel: offene Recap-Anzeige verfaellt (History bleibt)");

            var care = J.Dict(v4["care"]);
            var toiletAt = ClampStamp(care["toiletAt"], nowMs);
            if (toiletAt > 0.0)
            {
                s["bad"] = new JObject { ["kloLastMs"] = (long)toiletAt };
                notes.Add("care.toiletAt: Klo-Cooldown uebernommen (bad.kloLastMs)");
            }
            if (J.LeafNumOrZero(care["sickNotifyAt"]) > 0.0) lost.Add("care.sickNotifyAt: Notification-Planung wird in Godot neu aufgebaut");
            var cutscenesSeen = J.Dict(v4["cutscenes"])["seen"] as JObject;
            if (cutscenesSeen != null && cutscenesSeen.HasValues) lost.Add("cutscenes.seen: Replay-Skip-Latch entfaellt (Godot-Cutscenes skippen ab 2 s)");

            var v4Gallery = (JObject)v4["gallery"];
            ((JObject)s["gallery"])["legacyCount"] = v4Gallery["count"] ?? 0;
            var photos = J.LeafNumOrZero(profile["photos"]);
            ((JObject)s["camera"])["owned"] = J.LeafNumOrZero(v4Gallery["count"]) > 0.0 || photos > 0.0;
            lost.Add("photos: IndexedDB-Blobs nicht migrierbar — Galerie startet leer (Zaehler bleibt)");

            ((JObject)s["easterEggs"])["nougat"] = J.Clone(v4["nougat"]);

            var sProfile = (JObject)s["profile"];
            sProfile["playtimeMin"] = profile["playtimeMin"] ?? 0;
            sProfile["distanceM"] = profile["distanceM"] ?? 0;
            sProfile["photos"] = profile["photos"] ?? 0;

            var settings = (JObject)v4["settings"];
            ((JObject)s["settings"])["imported"] = new JObject
            {
                ["lang"] = settings["lang"] ?? "auto",
                ["haptics"] = settings["haptics"] ?? true,
                ["notifications"] = settings["notifications"] ?? "unasked",
                ["volumes"] = J.Clone(settings["volumes"] ?? new JObject()),
                ["gyro"] = J.IsTrue(settings["gyro"]),
                ["controls"] = J.Clone(settings["controls"] ?? new JObject()),
                ["devUnlocked"] = J.IsTrue(settings["devUnlocked"]),
                ["uiScale"] = settings["uiScale"] ?? 100,
                ["sfxMuted"] = IsFalse(settings["sfx"]),
                ["musicMuted"] = IsFalse(settings["music"]),
            };
            lost.Add("settings.goobyWeltQuality: Gooby-Welt entfernt (Doc H §A)");

            var onboarding = (JObject)s["onboarding"];
            onboarding["done"] = J.IsTrue(J.Dict(v4["onboarding"])["done"]);
            onboarding["whatsNew5Seen"] = false;

            var migration = (JObject)s["migration"];
            migration["notes"] = notes;
            migration["lost"] = lost;

            var normalized = SaveSchema.Normalize(s, nowMs);
            if (!normalized.Ok) return Err("mapped state failed normalize: " + normalized.Error);
            var report = new JObject
            {
                ["importedFrom"] = "web-v4",
                ["level"] = prog["level"],
                ["coins"] = econ["coins"],
                ["stickers"] = ((JObject)s["stickers"])["unlocked"] is JObject su ? su.Count : 0,
                ["outfits"] = ((JObject)cosmetics["outfits"])["owned"] is JArray oo ? oo.Count : 0,
                ["furnitureBoxed"] = boxed,
                ["interruptedVacation"] = interrupted,
            };
            return new MigrationResult { Ok = true, State = normalized.State, Error = "", Report = report };
        }

        // ── web slice-default factories (exact §B1/§B2 values) ─────────────

        private static JObject V2SliceDefaults()
        {
            var plots = new JArray();
            for (var i = 0; i < 6; i++) plots.Add(SaveSchema.DefaultPlot());
            return new JObject
            {
                ["garden"] = new JObject { ["plotsOwned"] = 4, ["plots"] = plots, ["lastTickAt"] = 0 },
                ["health"] = new JObject { ["state"] = "healthy", ["junkScore"] = 0, ["neglectMin"] = 0, ["recoverMin"] = 0, ["since"] = 0 },
                ["weight"] = new JObject { ["value"] = 50 },
                ["quests"] = new JObject { ["day"] = "", ["active"] = new JArray(), ["rerolledDay"] = "", ["completedTotal"] = 0 },
                ["collections"] = new JObject { ["entries"] = new JObject(), ["claimedSets"] = new JObject() },
                ["skins"] = new JObject { ["owned"] = new JArray("cream"), ["equipped"] = "cream" },
                ["items"] = new JObject { ["medicine"] = 0, ["fertilizer"] = 0 },
                ["profile"] = new JObject { ["playtimeMin"] = 0, ["coinsEarned"] = 0, ["coinsSpent"] = 0, ["distanceM"] = 0, ["photos"] = 0 },
            };
        }

        private static JObject Counters(params string[] keys)
        {
            var o = new JObject();
            foreach (var k in keys) o[k] = 0;
            return o;
        }

        private static JObject V2CounterDefaults() => Counters("harvests", "plantings", "waterings", "sells", "cures", "vetTrips", "deliveries", "questsDone", "photosTaken", "nightPlays", "medsGiven", "balls");

        private static JObject V3SliceDefaults() => new JObject
        {
            ["stickers"] = new JObject { ["unlocked"] = new JObject(), ["seen"] = new JObject() },
            ["nougat"] = new JObject { ["lastGlobAt"] = 0, ["installed"] = false },
        };

        private static JObject V3SettingsDefaults() => new JObject
        {
            ["uiScale"] = 100,
            ["volumes"] = new JObject { ["master"] = 80, ["sfx"] = 100, ["music"] = 70, ["voice"] = 100, ["ambience"] = 80 },
            ["devUnlocked"] = false,
        };

        private static JObject V3CounterDefaults() => Counters("nougatGlobs", "cakesServed", "perfectCakes", "surfRuns", "surfDistanceM", "races", "ghostsCaught", "rescues", "cratesShipped");

        private static JObject V4SliceDefaults() => new JObject
        {
            ["radio"] = new JObject
            {
                ["station"] = "bordmusik", ["playing"] = false, ["shuffle"] = true, ["replaceContext"] = true,
                ["lastTrack"] = "", ["trims"] = new JObject(), ["recapHeard"] = new JObject(),
            },
            ["codes"] = new JObject { ["redeemed"] = new JObject(), ["lockUntil"] = 0, ["buffs"] = new JObject { ["doubleCoinsUntil"] = 0 } },
            ["modifiers"] = new JObject { ["nextAt"] = 0, ["seed"] = 0, ["current"] = JValue.CreateNull(), ["lastGameId"] = "", ["dayCoins"] = 0, ["dayCoinsDay"] = "" },
            ["recap"] = new JObject { ["lastRecapLevel"] = 0, ["baseline"] = new JObject(), ["baselineAt"] = 0, ["pendingLevel"] = 0, ["history"] = new JArray() },
            ["gallery"] = new JObject { ["count"] = 0, ["lastAddedAt"] = 0, ["hintShown"] = false },
        };

        private static JObject V4SettingsDefaults() => new JObject
        {
            ["gyro"] = false,
            ["controls"] = new JObject { ["invertX"] = false, ["invertY"] = false },
            ["goobyWeltQuality"] = "high",
        };

        private static JObject V4MinigameDefaults() => new JObject
        {
            ["difficulty"] = new JObject(), ["beaten"] = new JObject(), ["bestByDiff"] = new JObject(), ["endlessBest"] = new JObject(),
        };

        private static JObject V4CounterDefaults() => Counters("codesRedeemed", "modifierPlays", "recapsSeen", "radioMinutes", "galleryPhotos");

        /// <summary>Web defaultState() port (schema v4) — merge base for the validate step.</summary>
        private static JObject V4DefaultState(long nowMs)
        {
            var counters = new JObject { ["feeds"] = 0, ["washes"] = 0, ["sleeps"] = 0, ["trips"] = 0, ["tickles"] = 0, ["petsToday"] = 0, ["petsDay"] = "" };
            counters.Merge(V2CounterDefaults());
            counters.Merge(V3CounterDefaults());
            counters.Merge(V4CounterDefaults());
            var minigames = new JObject { ["best"] = new JObject(), ["plays"] = new JObject(), ["lastPlayDay"] = new JObject() };
            minigames.Merge(V4MinigameDefaults());
            var settings = new JObject { ["lang"] = "auto", ["sfx"] = true, ["music"] = true, ["haptics"] = true, ["notifications"] = "unasked" };
            settings.Merge(V3SettingsDefaults());
            settings.Merge(V4SettingsDefaults());
            var food = new JObject();
            foreach (var kv in SaveSchema.StarterFood) food[kv.Key] = kv.Value;
            var state = new JObject
            {
                ["v"] = WebVersion,
                ["createdAt"] = nowMs,
                ["lastTickAt"] = nowMs,
                ["stats"] = SaveSchema.DefaultStatsObj(),
                ["sleep"] = new JObject { ["sleeping"] = false, ["startedAt"] = 0, ["wakeAt"] = 0 },
                ["grumpyUntil"] = 0,
                ["coins"] = 100,
                ["xp"] = 0,
                ["level"] = 1,
                ["inventory"] = food,
                ["furniture"] = new JObject { ["owned"] = new JArray("radio"), ["placed"] = new JObject { ["living:shelf1"] = "radio" } },
                ["decor"] = new JObject { ["wallpaper"] = new JObject(), ["floor"] = new JObject() },
                ["outfits"] = new JObject
                {
                    ["owned"] = new JArray(),
                    ["equipped"] = new JObject { ["hat"] = JValue.CreateNull(), ["glasses"] = JValue.CreateNull(), ["neck"] = JValue.CreateNull(), ["back"] = JValue.CreateNull() },
                },
                ["minigames"] = minigames,
                ["achievements"] = new JObject { ["unlocked"] = new JObject(), ["counters"] = counters },
                ["daily"] = new JObject { ["lastClaimDay"] = "", ["streak"] = 0 },
                ["quickDelivery"] = false,
                ["settings"] = settings,
                ["onboarding"] = new JObject { ["done"] = false, ["step"] = 0, ["whatsNew2Seen"] = true, ["whatsNew3Seen"] = true, ["whatsNew4Seen"] = true },
            };
            state.Merge(V2SliceDefaults());
            state.Merge(V3SliceDefaults());
            state.Merge(V4SliceDefaults());
            return state;
        }

        // ── helpers ────────────────────────────────────────────────────────

        /// <summary>{…defaults, …state}: keys from state win; missing keys get defaults.</summary>
        private static JObject SpreadDefaultsFirst(JObject defaults, JObject state)
        {
            var o = J.CloneObj(defaults);
            foreach (var p in state.Properties()) o[p.Name] = p.Value.DeepClone();
            return o;
        }

        private static void MergeCountersDefaultsFirst(JObject o, JObject defaults)
        {
            if (!(o["achievements"] is JObject achievements)) return;
            var counters = achievements["counters"];
            if (counters == null || counters.Type == JTokenType.Null || counters is JObject)
            {
                var merged = J.CloneObj(defaults);
                if (counters is JObject c) foreach (var p in c.Properties()) merged[p.Name] = p.Value.DeepClone();
                var copy = J.CloneObj(achievements);
                copy["counters"] = merged;
                o["achievements"] = copy;
            }
        }

        private static double ClampStamp(JToken value, long nowMs)
        {
            var n = J.LeafNumOrZero(value);
            if (n <= 0.0) return 0.0;
            return Math.Min(n, nowMs + (double)SaveSchema.FutureStampSlackMs);
        }

        private static bool IsFalse(JToken value) => value != null && value.Type == JTokenType.Boolean && !value.Value<bool>();

        private static MigrationResult Err(string message) => new MigrationResult { Ok = false, State = new JObject(), Error = message, Report = new JObject() };
    }
}
