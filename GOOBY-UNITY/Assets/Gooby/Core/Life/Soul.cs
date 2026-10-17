using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Gooby's soul moments on entering a room (port of soul_service.gd + soul_triggers.gd + soul_state.gd, FB-6/SEELE):
    /// at most ONE moment per entry, never stacked, never pushy. First match wins: a ritual (Gooby's birthday — the day
    /// he moved in —, an anniversary of days together, a holiday, the first snow of the season; once per day/season, no
    /// ambient cap) › a greeting (after an absence: delighted / miffed / missed, else by the time of day; once a day,
    /// the wording warms up with the days known) › a weather comment (rain, storm, snow; once a day). Greetings and
    /// comments share the frequency brake (90 s apart, 12 a day). Defs come from the content pack content/soul
    /// (domain "soul"); a missing def silently skips its moment. Pure statics — state, slice, defs, time and the roll
    /// are handed in, the bookkeeping is separate (<see cref="BookEnter"/>), so decide → book → decide gates.
    /// </summary>
    public static class Soul
    {
        public const string SliceId = "soul";
        public const string DefsPath = "content/soul/data/soul";
        public const long MsPerHour = 3_600_000L, MsPerDay = 86_400_000L;
        public static readonly int[] Milestones = { 7, 30, 50, 100, 200, 365, 500, 730, 1000 };
        public const long AmbientMinGapMs = 90_000L;
        public const int AmbientMaxPerDay = 12;
        public const double AbsenceHappyH = 6.0, AbsenceMiffedH = 48.0, AbsenceMissedH = 168.0;

        /// <summary>What the moment needs to know about now (local calendar, hour, the gap since the last visit, weather).</summary>
        public sealed class Ctx
        {
            public long NowMs, GapMs;
            public int Year, Month, Day, Hour;
            public string WetterTyp = "";
            public bool Schnee;
            public string PlayerName = "", Nickname = "Gooby";
            public string Today => DayString(Year, Month, Day);
        }

        public sealed class Moment
        {
            public string Id = "", Kind = "", TextKey = "", Emotion = "happy", Clip = "", Sfx = "", Aktion = "";
            public string GateKey, GateStamp;
            /// <summary>The memory shown (kind "erinnerung") or the wish formed / fulfilled (kinds "wunsch", "wunsch_erfuellt").</summary>
            public string MemoryId, WunschId;
            public readonly Dictionary<string, object> Args = new Dictionary<string, object>();
        }

        /// <summary>How often a memory comes instead of silence when there is room (roll gate).</summary>
        public const double MemoryChance = 0.35;
        /// <summary>Share of the memory slot that announces a NEW small wish (wishes stay rare, the payoff is certain).</summary>
        public const double WunschAnteil = 0.3;
        public const int WunschMinDays = 3;
        public const int AskBirthdayMinDays = 3;
        public const long AskBirthdayRetryMs = 7 * MsPerDay;

        public static JArray Defs() => ContentSource.LoadItems(DefsPath);

        // ── triggers (soul_triggers.gd) ──────────────────────────────────────

        public static string DayString(int year, int month, int day) => year.ToString("0000") + "-" + month.ToString("00") + "-" + day.ToString("00");

        public static string Daypart(int hour) =>
            hour >= 5 && hour <= 10 ? "morgen" : hour >= 11 && hour <= 14 ? "mittag" : hour >= 15 && hour <= 17 ? "nachmittag" : hour >= 18 && hour <= 22 ? "abend" : "nacht";

        public static string AbsenceKind(long gapMs)
        {
            var hours = gapMs / (double)MsPerHour;
            return hours >= AbsenceMissedH ? "vermisst" : hours >= AbsenceMiffedH ? "eingeschnappt" : hours >= AbsenceHappyH ? "gefreut" : "";
        }

        public static int DaysKnown(long firstMetMs, long nowMs) => firstMetMs <= 0 || nowMs < firstMetMs ? 0 : (int)((nowMs - firstMetMs) / MsPerDay);

        public static string BeziehungStufe(int days) => days >= 50 ? "beste_freunde" : days >= 7 ? "vertraut" : "neu";

        public static int AnniversaryMilestone(int days) => Array.IndexOf(Milestones, days) >= 0 ? days : 0;

        /// <summary>Month/day (UTC) of a unix-ms stamp — Gooby's birthday is the day he moved in.</summary>
        public static (int month, int day) BirthdayFromMs(long ms)
        {
            var d = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            return (d.Month, d.Day);
        }

        public static bool IsBirthday(int bMonth, int bDay, int month, int day) => bMonth > 0 && bDay > 0 && month == bMonth && day == bDay;

        /// <summary>Gauss/Meeus Easter Sunday (soul_triggers.gd easter_month_day).</summary>
        public static (int month, int day) EasterMonthDay(int year)
        {
            var a = year % 19;
            var b = year / 100;
            var c = year % 100;
            var d = b / 4;
            var e = b % 4;
            var f = (b + 8) / 25;
            var g = (b - f + 1) / 3;
            var h = (19 * a + b - d - g + 15) % 30;
            var i = c / 4;
            var k = c % 4;
            var l = (32 + 2 * e + 2 * i - h - k) % 7;
            var m = (a + 11 * h + 22 * l) / 451;
            return ((h + l - 7 * m + 114) / 31, (h + l - 7 * m + 114) % 31 + 1);
        }

        public static string HolidayFor(int year, int month, int day)
        {
            if (month == 1 && day == 1) return "neujahr";
            if (month == 10 && day == 31) return "halloween";
            if (month == 12 && day == 6) return "nikolaus";
            if (month == 12 && day == 24) return "heiligabend";
            if (month == 12 && (day == 25 || day == 26)) return "weihnachten";
            if (month == 12 && day == 31) return "silvester";
            var (em, ed) = EasterMonthDay(year);
            return month == em && day == ed ? "ostern" : "";
        }

        /// <summary>The winter a date belongs to (December counts toward the next January).</summary>
        public static string SnowSeason(int year, int month) => (month == 12 ? year : year - 1).ToString();

        public static bool AmbientAllowed(JObject ambient, long nowMs, string today)
        {
            var last = J.Long(ambient?["lastAt"]);
            if (last > 0 && nowMs - last < AmbientMinGapMs) return false;
            var count = J.Str(ambient?["day"]) == today ? J.Int(ambient?["count"]) : 0;
            return count < AmbientMaxPerDay;
        }

        public static JObject NoteAmbient(JObject ambient, long nowMs, string today)
        {
            var count = J.Str(ambient?["day"]) == today ? J.Int(ambient?["count"]) : 0;
            return new JObject { ["day"] = today, ["count"] = count + 1, ["lastAt"] = nowMs };
        }

        public static bool CelebratedToday(JObject celebrated, string key, string stamp) => J.Str(celebrated?[key]) == stamp;

        // ── slice (soul_state.gd) ────────────────────────────────────────────

        /// <summary>The soul slice of the save with every field in place (a missing or broken one gets its default).</summary>
        public static JObject Slice(JObject state)
        {
            var s = J.EnsureDict(state, SliceId);
            foreach (var key in new[] { "firstMetAt", "lastVisitAt", "askedBirthdayAt", "totalMoments" })
                if (!J.IsNum(s[key])) s[key] = 0L;
            foreach (var key in new[] { "lastGreetDay", "lastGreetKind", "favFurniture" })
                if (!J.IsString(s[key])) s[key] = "";
            foreach (var key in new[] { "celebrated", "memoryShownAt", "surpriseAt", "wunsch", "wunschErfuellt", "knownItems", "foodGiven", "gespraeche", "linien" })
                if (!(s[key] is JObject)) s[key] = new JObject();
            var bm = J.Int(s["playerBirthday"]?["month"]);
            var bd = J.Int(s["playerBirthday"]?["day"]);
            if (!(s["playerBirthday"] is JObject) || bm < 1 || bm > 12 || bd < 1 || bd > 31) s["playerBirthday"] = new JObject { ["month"] = 0, ["day"] = 0 };
            if (!(s["ambient"] is JObject)) s["ambient"] = new JObject { ["day"] = "", ["count"] = 0, ["lastAt"] = 0L };
            s["stimmung"] = SoulMood.Normalize(s["stimmung"]);
            s["feelings"] = SoulFeelings.Normalize(s["feelings"]);
            return s;
        }

        /// <summary>
        /// First setup + visit stamp: firstMetAt once (falling back to meta.createdAt — the day Gooby moved in), lastVisitAt
        /// now. Returns the gap since the previous visit in ms (0 the very first time).
        /// </summary>
        public static long TouchVisit(JObject state, long nowMs)
        {
            var s = Slice(state);
            var last = J.Long(s["lastVisitAt"]);
            var gap = last > 0 ? nowMs - last : 0L;
            if (J.Long(s["firstMetAt"]) <= 0)
            {
                var created = J.Long(J.Path(state, "meta", "createdAt"));
                s["firstMetAt"] = created > 0 ? created : nowMs;
            }
            s["lastVisitAt"] = nowMs;
            return Math.Max(0L, gap);
        }

        // ── decide / book (soul_service.gd) ──────────────────────────────────

        /// <summary>The one moment on entering a room without the save at hand (no memories or wishes).</summary>
        public static Moment DecideEnter(JObject slice, JArray defs, Ctx ctx, double roll) => DecideEnter(null, slice, defs, ctx, roll);

        /// <summary>
        /// The one moment on entering a room (null: silence is fine too): ritual › greeting › weather › a fulfilled
        /// wish (always celebrated) › a memory or a new wish (35 % of the remaining entries).
        /// </summary>
        public static Moment DecideEnter(JObject state, JObject slice, JArray defs, Ctx ctx, double roll, Gooby.Core.I18n.I18n strings = null)
        {
            var ritual = DecideRitual(slice, defs, ctx, roll);
            if (ritual != null) return ritual;
            if (!AmbientAllowed(slice["ambient"] as JObject, ctx.NowMs, ctx.Today)) return null;
            var m = DecideGreeting(slice, defs, ctx, roll) ?? DecideWeather(slice, defs, ctx, roll);
            if (m != null || state == null) return m;
            return DecideWunschErfuellt(state, slice, defs, ctx, roll) ?? DecideMemory(state, slice, defs, ctx, roll, strings);
        }

        private static Moment DecideMemory(JObject state, JObject slice, JArray defs, Ctx ctx, double roll, Gooby.Core.I18n.I18n strings)
        {
            if (roll >= MemoryChance) return null;
            var pickRoll = roll / MemoryChance;
            if (pickRoll < WunschAnteil)
            {
                var wunsch = DecideWunschNeu(state, slice, defs, ctx, pickRoll / WunschAnteil);
                if (wunsch != null) return wunsch;
            }
            var memory = SoulMemories.Pick(SoulMemories.Candidates(state, strings), slice["memoryShownAt"] as JObject, ctx.NowMs, pickRoll);
            if (memory == null) return null;
            var def = DefById(defs, "erinnerung");
            if (def == null) return null;
            var moment = FromDef(def, "erinnerung", slice, ctx, pickRoll);
            foreach (var kv in memory.Args) moment.Args[kv.Key] = kv.Value;
            moment.TextKey = memory.TextKey;
            moment.MemoryId = memory.Id;
            return moment;
        }

        /// <summary>A new small wish ("I always wanted to …"): only without an active one and after a few days known.</summary>
        private static Moment DecideWunschNeu(JObject state, JObject slice, JArray defs, Ctx ctx, double roll)
        {
            if (J.Str((slice["wunsch"] as JObject)?["id"]) != "") return null;
            if (DaysKnown(J.Long(slice["firstMetAt"]), ctx.NowMs) < WunschMinDays) return null;
            var offen = SoulMemories.OffeneWuensche(state, slice);
            if (offen.Count == 0) return null;
            var def = DefById(defs, "wunsch");
            if (def == null) return null;
            var id = offen[(int)(J.Clamp(roll, 0.0, 0.999999) * offen.Count)];
            var moment = FromDef(def, "wunsch", slice, ctx, roll);
            moment.TextKey = "soul.wunsch." + id;
            moment.WunschId = id;
            return moment;
        }

        /// <summary>A fulfilled wish: Gooby's goal REALLY happened (real data) — he refers back and celebrates.</summary>
        private static Moment DecideWunschErfuellt(JObject state, JObject slice, JArray defs, Ctx ctx, double roll)
        {
            var id = J.Str((slice["wunsch"] as JObject)?["id"]);
            if (id == "" || !SoulMemories.WunschErfuellt(state, id)) return null;
            var def = DefById(defs, "wunsch_erfuellt");
            if (def == null) return null;
            var moment = FromDef(def, "wunsch_erfuellt", slice, ctx, roll);
            moment.TextKey = "soul.wunsch." + id + "_erfuellt";
            moment.WunschId = id;
            return moment;
        }

        /// <summary>
        /// Bookkeeping AFTER showing (mutates the slice): rituals book their celebrated key, ambient moments the
        /// frequency brake (and a day gate when they carry one).
        /// </summary>
        public static void BookEnter(JObject slice, Moment moment, Ctx ctx)
        {
            if (moment == null) return;
            slice["totalMoments"] = J.Long(slice["totalMoments"]) + 1;
            var celebrated = J.EnsureDict(slice, "celebrated");
            if (moment.Kind == "ritual")
            {
                celebrated[moment.GateKey] = moment.GateStamp;
                return;
            }
            slice["ambient"] = NoteAmbient(slice["ambient"] as JObject, ctx.NowMs, ctx.Today);
            if (moment.GateKey != null) celebrated[moment.GateKey] = moment.GateStamp;
            switch (moment.Kind)
            {
                case "gruss":
                    slice["lastGreetDay"] = ctx.Today;
                    slice["lastGreetKind"] = moment.Id;
                    break;
                case "erinnerung":
                    J.EnsureDict(slice, "memoryShownAt")[moment.MemoryId ?? moment.Id] = ctx.NowMs;
                    break;
                case "wunsch":
                    slice["wunsch"] = new JObject { ["id"] = moment.WunschId, ["seitMs"] = ctx.NowMs };
                    break;
                case "wunsch_erfuellt":
                    J.EnsureDict(slice, "wunschErfuellt")[moment.WunschId] = ctx.NowMs;
                    slice["wunsch"] = new JObject();
                    break;
            }
        }

        // ── background life (soul_service.gd pick_idle / pick_surprise, gooby_reactions.gd) ──

        /// <summary>What the idle pick needs to know: the room's means (TV, radio playing, favourite piece) and the mood.</summary>
        public sealed class IdleCtx
        {
            public long NowMs;
            public bool HatTv, RadioAn, HatFav;
            public string LauneBand = "happy";

            public bool Has(string requirement)
            {
                switch (requirement)
                {
                    case "": case null: return true;
                    case "hat_tv": return HatTv;
                    case "radio_an": return RadioAn;
                    case "hat_fav": return HatFav;
                    default: return false;
                }
            }
        }

        public const long IdleActCooldownMs = 180_000;

        /// <summary>An idle act weighted by the defs (null: keep dozing). Cooldowns are the caller's in-memory map.</summary>
        public static JObject PickIdle(JArray defs, IdleCtx ctx, IDictionary<string, long> cooldowns, double roll)
        {
            var available = new List<JObject>();
            var total = 0.0;
            foreach (var t in defs ?? new JArray())
            {
                if (!(t is JObject def) || J.Str(def["kind"]) != "idle") continue;
                if (cooldowns != null && cooldowns.TryGetValue(J.Str(def["id"]), out var until) && ctx.NowMs < until) continue;
                if (!ctx.Has(J.Str(def["braucht"]))) continue;
                if (!SoulMood.DefErlaubt(def, ctx.LauneBand)) continue;
                available.Add(def);
                total += Math.Max(0.0, J.NumOr(def["weight"], 1.0));
            }
            if (available.Count == 0 || total <= 0.0) return null;
            var at = J.Clamp(roll, 0.0, 0.999999) * total;
            foreach (var def in available)
            {
                var w = Math.Max(0.0, J.NumOr(def["weight"], 1.0));
                if (at < w) return def;
                at -= w;
            }
            return available[available.Count - 1];
        }

        /// <summary>Surprises: very rare, a long slice cooldown per id, the def's chance against the gate roll.</summary>
        public static Moment PickSurprise(JObject slice, JArray defs, Ctx ctx, double rollPick, double rollGate)
        {
            var available = new List<JObject>();
            foreach (var t in defs ?? new JArray())
            {
                if (!(t is JObject def) || J.Str(def["kind"]) != "ueberraschung") continue;
                var last = J.Long((slice["surpriseAt"] as JObject)?[J.Str(def["id"])]);
                var cooldownMs = (long)(J.NumOr(def["cooldown_h"], 72.0) * MsPerHour);
                if (last <= 0 || ctx.NowMs - last >= cooldownMs) available.Add(def);
            }
            if (available.Count == 0) return null;
            var chosen = available[(int)(J.Clamp(rollPick, 0.0, 0.999999) * available.Count)];
            if (rollGate >= J.NumOr(chosen["chance"], 0.1)) return null;
            return FromDef(chosen, "ueberraschung", slice, ctx, rollPick);
        }

        public static void BookSurprise(JObject slice, Moment moment, long nowMs)
        {
            if (moment == null) return;
            J.EnsureDict(slice, "surpriseAt")[moment.Id] = nowMs;
            slice["totalMoments"] = J.Long(slice["totalMoments"]) + 1;
        }

        /// <summary>A moment from any def (comments, intents, idle acts): text by roll, the def's face/clip/sound.</summary>
        public static Moment MomentOf(JObject def, JObject slice, Ctx ctx, double roll) =>
            def == null ? null : FromDef(def, J.Str(def["kind"], "kommentar"), slice, ctx, roll);

        /// <summary>Ask for the player's birthday? Never pushy: after a few days known, then at most every 7 days.</summary>
        public static bool ShouldAskBirthday(JObject slice, Ctx ctx)
        {
            if (J.Int((slice["playerBirthday"] as JObject)?["month"]) > 0) return false;
            if (DaysKnown(J.Long(slice["firstMetAt"]), ctx.NowMs) < AskBirthdayMinDays) return false;
            var asked = J.Long(slice["askedBirthdayAt"]);
            if (asked > 0 && ctx.NowMs - asked < AskBirthdayRetryMs) return false;
            return AmbientAllowed(slice["ambient"] as JObject, ctx.NowMs, ctx.Today);
        }

        public static bool SetPlayerBirthday(JObject slice, int month, int day)
        {
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month)) return false;
            slice["playerBirthday"] = new JObject { ["month"] = month, ["day"] = day };
            return true;
        }

        // ── taps and pets (gooby_reactions.gd tap_stage / pet_pitch / pet_bonus_due) ──

        public const float TapWindowS = 4f;
        public const int PetBonusJeder = 10;
        public const double PetPitchSchritt = 0.04;
        public const int PetBonusCoins = 1;
        public const int SofaFundCoins = 3;
        public const int MiniFundCoins = 1;
        public const int FavFoodMin = 3;

        /// <summary>Giggle stage per tap count: 1 = giggle, 3 = ticklish, 6+ = dizzy (and the count starts over).</summary>
        public static string TapStage(int count) => count == 1 ? "tipp_kicher" : count == 3 ? "tipp_kitzlig" : count >= 6 ? "tipp_schwindelig" : "";

        /// <summary>Petting pitch: rises step by step towards the bonus mark, then starts again.</summary>
        public static double PetPitch(int petsToday) => 1.0 + PetPitchSchritt * (Math.Max(petsToday - 1, 0) % PetBonusJeder);

        public static bool PetBonusDue(int petsToday) => petsToday > 0 && petsToday % PetBonusJeder == 0;

        /// <summary>Counts a pet in achievements.counters (per day) and returns today's count.</summary>
        public static int CountPet(JObject state, string today)
        {
            var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
            if (J.Str(counters["petsDay"]) != today)
            {
                counters["petsDay"] = today;
                counters["petsToday"] = 0;
            }
            counters["petsToday"] = J.Int(counters["petsToday"]) + 1;
            return J.Int(counters["petsToday"]);
        }

        // ── food, furniture ──

        /// <summary>One feeding of <paramref name="foodId"/> into soul.foodGiven; returns its new count.</summary>
        public static int NoteFoodGiven(JObject state, string foodId)
        {
            var given = J.EnsureDict(Slice(state), "foodGiven");
            given[foodId] = J.Int(given[foodId]) + 1;
            return J.Int(given[foodId]);
        }

        /// <summary>Gooby's favourite piece, picked once from the home's items by a hash of the day you met (stays forever).</summary>
        public static string PickFavFurniture(JObject slice, IEnumerable<string> itemIds)
        {
            var current = J.Str(slice["favFurniture"]);
            if (current != "") return current;
            var ids = new SortedSet<string>(itemIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (ids.Count == 0) return current;
            var list = new List<string>(ids);
            var pick = list[(int)(Math.Abs(J.Long(slice["firstMetAt"])) % list.Count)];
            slice["favFurniture"] = pick;
            return pick;
        }

        /// <summary>Items new since the last look (knownItems), then remembers the current set.</summary>
        public static List<string> FreshItems(JObject slice, IEnumerable<string> itemIds)
        {
            var known = J.EnsureDict(slice, "knownItems");
            var hadAny = known.HasValues;
            var fresh = new List<string>();
            var current = new JObject();
            foreach (var id in itemIds ?? Array.Empty<string>())
            {
                if (current[id] != null) continue;
                current[id] = true;
                if (known[id] == null) fresh.Add(id);
            }
            slice["knownItems"] = current;
            return hadAny ? fresh : new List<string>();
        }

        public static JObject DefById(JArray defs, string id)
        {
            if (defs == null) return null;
            foreach (var t in defs)
                if (t is JObject o && J.Str(o["id"]) == id) return o;
            return null;
        }

        private static Moment DecideRitual(JObject slice, JArray defs, Ctx ctx, double roll)
        {
            var today = ctx.Today;
            var celebrated = slice["celebrated"] as JObject;
            var birthday = slice["playerBirthday"] as JObject;
            if (IsBirthday(J.Int(birthday?["month"]), J.Int(birthday?["day"]), ctx.Month, ctx.Day) && !CelebratedToday(celebrated, "geburtstag_spieler", today))
                return Ritual(defs, "ritual_geburtstag_spieler", "geburtstag_spieler", today, slice, ctx, roll);
            var firstMet = J.Long(slice["firstMetAt"]);
            if (firstMet > 0)
            {
                var (bm, bd) = BirthdayFromMs(firstMet);
                var days = DaysKnown(firstMet, ctx.NowMs);
                // the move-in day itself is no birthday yet ("remember when I moved in?" needs a year behind it)
                var movedInBefore = DateTimeOffset.FromUnixTimeMilliseconds(firstMet).UtcDateTime.Year < ctx.Year;
                if (movedInBefore && IsBirthday(bm, bd, ctx.Month, ctx.Day) && !CelebratedToday(celebrated, "geburtstag_gooby", today))
                    return Ritual(defs, "ritual_geburtstag_gooby", "geburtstag_gooby", today, slice, ctx, roll);
                var milestone = AnniversaryMilestone(days);
                if (milestone > 0 && !CelebratedToday(celebrated, "jubilaeum_" + milestone, today))
                {
                    var m = Ritual(defs, "ritual_jubilaeum", "jubilaeum_" + milestone, today, slice, ctx, roll);
                    if (m != null) m.Args["tage"] = milestone;
                    return m;
                }
            }
            var holiday = HolidayFor(ctx.Year, ctx.Month, ctx.Day);
            if (holiday != "" && !CelebratedToday(celebrated, "feiertag_" + holiday, today))
                return Ritual(defs, "ritual_" + holiday, "feiertag_" + holiday, today, slice, ctx, roll);
            if (ctx.Schnee)
            {
                var season = SnowSeason(ctx.Year, ctx.Month);
                if (!CelebratedToday(celebrated, "erster_schnee", season))
                    return Ritual(defs, "ritual_erster_schnee", "erster_schnee", season, slice, ctx, roll);
            }
            return null;
        }

        private static Moment DecideGreeting(JObject slice, JArray defs, Ctx ctx, double roll)
        {
            if (J.Str(slice["lastGreetDay"]) == ctx.Today) return null;
            var absence = AbsenceKind(ctx.GapMs);
            var def = DefById(defs, "gruss_" + (absence != "" ? absence : Daypart(ctx.Hour)));
            if (def == null) return null;
            var days = DaysKnown(J.Long(slice["firstMetAt"]), ctx.NowMs);
            return FromDef(def, "gruss", slice, ctx, roll, BeziehungStufe(days));
        }

        private static Moment DecideWeather(JObject slice, JArray defs, Ctx ctx, double roll)
        {
            var typ = ctx.WetterTyp ?? "";
            if (typ != "regen" && typ != "gewitter" && typ != "schnee") return null;
            if (CelebratedToday(slice["celebrated"] as JObject, "wetter_" + typ, ctx.Today)) return null;
            var def = DefById(defs, "wetter_" + typ);
            if (def == null) return null;
            var m = FromDef(def, "wetter", slice, ctx, roll);
            m.GateKey = "wetter_" + typ;
            m.GateStamp = ctx.Today;
            return m;
        }

        private static Moment Ritual(JArray defs, string defId, string gateKey, string stamp, JObject slice, Ctx ctx, double roll)
        {
            var def = DefById(defs, defId);
            if (def == null) return null;
            var m = FromDef(def, "ritual", slice, ctx, roll);
            m.GateKey = gateKey;
            m.GateStamp = stamp;
            return m;
        }

        /// <summary>Text pool of a def: the base variants plus the ones of the relationship stage ("text_keys_stufe").</summary>
        public static List<string> TextPool(JObject def, string stufe = "")
        {
            var pool = new List<string>();
            if (def?["text_keys"] is JArray keys) foreach (var k in keys) pool.Add(J.Str(k));
            if (stufe != "" && def?["text_keys_stufe"] is JObject stufen && stufen[stufe] is JArray extra)
                foreach (var k in extra) pool.Add(J.Str(k));
            return pool;
        }

        private static Moment FromDef(JObject def, string kind, JObject slice, Ctx ctx, double roll, string stufe = "")
        {
            var keys = TextPool(def, stufe);
            var m = new Moment
            {
                Id = J.Str(def["id"]),
                Kind = kind,
                TextKey = keys.Count > 0 ? keys[(int)(Math.Min(Math.Max(roll, 0.0), 0.999999) * keys.Count)] : "",
                Emotion = J.Str(def["emotion"], "happy"),
                Clip = J.Str(def["clip"]),
                Sfx = J.Str(def["sfx"]),
                Aktion = J.Str(def["aktion"]),
            };
            var days = DaysKnown(J.Long(slice["firstMetAt"]), ctx.NowMs);
            m.Args["name"] = ctx.PlayerName ?? "";
            m.Args["namek"] = string.IsNullOrEmpty(ctx.PlayerName) ? "" : ", " + ctx.PlayerName;
            m.Args["gooby"] = string.IsNullOrEmpty(ctx.Nickname) ? "Gooby" : ctx.Nickname;
            m.Args["tage"] = days;
            return m;
        }
    }
}
