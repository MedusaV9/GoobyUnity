using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// What a tap on a placed piece of furniture does — port of interactables_host.gd's rescan: lamps switch, the
    /// toilet / shower / bath run the bathroom routines, the sink brushes teeth, the mirror shapes Gooby's face, beds
    /// put him to sleep, fridges feed him, radios play, TVs show GOB.TY and seats let him sit.
    /// </summary>
    public static class Interactables
    {
        public const string Licht = "licht";
        public const string Klo = "klo";
        public const string Dusche = "dusche";
        public const string Spiegel = "spiegel";
        public const string Zahnputz = "zahnputz";
        public const string Bett = "bett";
        public const string Kuehlschrank = "kuehlschrank";
        public const string Radio = "radio";
        public const string Fernseher = "fernseher";
        public const string Sitzen = "sitzen";
        public const string Nougat = "nougat";
        /// <summary>Postcard wall and souvenir shelf open the postcard archive (postkarten_wand.gd).</summary>
        public const string Postkarten = "postkarten";

        private static readonly string[] DuschIds = { "shower", "showerRound", "bathtub" };
        private static readonly string[] RadioIds = { "radio", "radioRetro", "speaker" };
        private static readonly string[] TvIds = { "televisionModern", "televisionVintage" };

        /// <summary>The interaction of a furniture piece ("" = none).</summary>
        public static string Kind(FurnitureDef def)
        {
            if (def == null) return "";
            var id = def.Id ?? "";
            if (def.CanToggleLight) return Licht;
            if (id == "toilet") return Klo;
            if (id == Life.Nougat.FurnitureId) return Nougat;
            if (id == "postkartenWand" || id == "souvenirRegal") return Postkarten;
            if (Array.IndexOf(DuschIds, id) >= 0) return Dusche;
            if (id == "bathroomMirror") return Spiegel;
            if (id == "bathroomSink") return Zahnputz;
            if (id.StartsWith("bed", StringComparison.Ordinal) && def.Kategorie == "schlafen") return Bett;
            if (id.StartsWith("kitchenFridge", StringComparison.Ordinal)) return Kuehlschrank;
            if (Array.IndexOf(RadioIds, id) >= 0) return Radio;
            if (Array.IndexOf(TvIds, id) >= 0) return Fernseher;
            if (def.Kategorie == "sitzen" || id.StartsWith("loungeSofa", StringComparison.Ordinal) || id.StartsWith("loungeChair", StringComparison.Ordinal)) return Sitzen;
            return "";
        }
    }

    /// <summary>
    /// The bathroom state (port of bad_state.gd, save slice "bad"): toilet need over four hours, toothbrushing due after
    /// waking up, the shower start for the curtain-peek gag, broken brushes, and the on/off state of every lamp by its
    /// uid (default on). Time always comes in from outside. Washing and brushing also count toward the stickers
    /// (washes, teeth_brushed) and give hygiene back — the only ways besides the vet to raise it.
    /// </summary>
    public static class BadState
    {
        public const string SliceId = "bad";
        public const long KloFullMs = 4L * 3_600_000L;
        public const long ShowerPeekMs = 45_000L;
        public const double DuschHygiene = 30.0;
        public const double PutzHygiene = 8.0;
        public const double KloFun = 2.0;

        public static void RegisterSlice() => Save.SaveSchema.RegisterSlice(SliceId, DefaultSlice, NormalizeSlice);

        public static JObject DefaultSlice() => new JObject
        {
            ["kloLastMs"] = 0L, ["needsBrushing"] = false, ["brushBrokenCount"] = 0, ["lights"] = new JObject(), ["showerStartedMs"] = 0L,
        };

        public static JObject NormalizeSlice(JToken raw)
        {
            var slice = raw as JObject ?? DefaultSlice();
            slice["kloLastMs"] = Math.Max(0L, J.Long(slice["kloLastMs"]));
            slice["needsBrushing"] = slice["needsBrushing"]?.Type == JTokenType.Boolean && slice["needsBrushing"].Value<bool>();
            slice["brushBrokenCount"] = Math.Max(0, J.Int(slice["brushBrokenCount"]));
            if (!(slice["lights"] is JObject lights)) slice["lights"] = lights = new JObject();
            foreach (var p in new List<JProperty>(lights.Properties()))
                lights[p.Name] = p.Value?.Type == JTokenType.Boolean && p.Value.Value<bool>();
            slice["showerStartedMs"] = Math.Max(0L, J.Long(slice["showerStartedMs"]));
            return slice;
        }

        /// <summary>Toilet need 0..1 (0 = just been, 1 = must go).</summary>
        public static double KloNeed01(long lastMs, long nowMs) => lastMs <= 0 ? 0.0 : J.Clamp((nowMs - lastMs) / (double)KloFullMs, 0.0, 1.0);

        public static bool KloDue(long lastMs, long nowMs) => lastMs > 0 && KloNeed01(lastMs, nowMs) >= 1.0;

        public static bool ShowerPeekDue(long startedMs, long nowMs) => startedMs > 0 && nowMs - startedMs >= ShowerPeekMs;

        public static long KloLastMs(GameStateStore gs) => J.Long(J.Path(gs?.State, SliceId, "kloLastMs"));

        public static bool NeedsBrushing(GameStateStore gs) => J.IsTrue(J.Path(gs?.State, SliceId, "needsBrushing"));

        /// <summary>After waking up brushing is due (Doc F §3.2).</summary>
        public static void MarkWokeUp(GameStateStore gs) => Write(gs, s => s["needsBrushing"] = true);

        /// <summary>First contact with the bathroom starts the toilet timer.</summary>
        public static void EnsureKloTimer(GameStateStore gs, long nowMs)
        {
            if (KloLastMs(gs) <= 0) Write(gs, s => s["kloLastMs"] = nowMs);
        }

        public static void MarkKloDone(GameStateStore gs, long nowMs)
        {
            Write(gs, s => s["kloLastMs"] = nowMs);
            Stat(gs, "fun", KloFun, null);
        }

        public static void SetShowerStarted(GameStateStore gs, long startedMs) => Write(gs, s => s["showerStartedMs"] = startedMs);

        /// <summary>Showered or bathed: hygiene up, washes counter up (stickers). Returns the hygiene really gained.</summary>
        public static double MarkWashed(GameStateStore gs, double hygiene = DuschHygiene)
        {
            Write(gs, s => s["showerStartedMs"] = 0L);
            return Stat(gs, "hygiene", hygiene, "washes");
        }

        /// <summary>Teeth brushed: the duty is gone, teeth_brushed counts, a little hygiene; broke = the brush gave up.</summary>
        public static double MarkBrushed(GameStateStore gs, bool broke)
        {
            Write(gs, s =>
            {
                s["needsBrushing"] = false;
                if (broke) s["brushBrokenCount"] = J.Int(s["brushBrokenCount"]) + 1;
            });
            return Stat(gs, "hygiene", PutzHygiene, "teeth_brushed");
        }

        public static bool LightOn(GameStateStore gs, string uid)
        {
            var v = J.Path(gs?.State, SliceId, "lights", uid ?? "");
            return v == null || v.Type != JTokenType.Boolean || v.Value<bool>();
        }

        public static void SetLightOn(GameStateStore gs, string uid, bool on)
        {
            if (string.IsNullOrEmpty(uid)) return;
            Write(gs, s => J.EnsureDict(s, "lights")[uid] = on);
        }

        private static double Stat(GameStateStore gs, string key, double delta, string counter)
        {
            if (gs == null) return 0.0;
            var gained = 0.0;
            gs.Update(state =>
            {
                var gooby = J.EnsureDict(state, "gooby");
                var before = J.Dict(gooby["stats"]);
                var was = Stats.ClampStat(before[key]);
                gooby["stats"] = Stats.ApplyDeltas(before, new Dictionary<string, double> { { key, delta } });
                gained = Stats.ClampStat(J.Dict(gooby["stats"])[key]) - was;
                if (counter == null) return;
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                counters[counter] = J.Int(counters[counter]) + 1;
            });
            gs.NotifySliceChanged("gooby");
            if (counter != null) gs.NotifySliceChanged("achievements");
            return gained;
        }

        private static void Write(GameStateStore gs, Action<JObject> mutate)
        {
            if (gs == null) return;
            gs.Update(state =>
            {
                var slice = NormalizeSlice(state[SliceId]);
                mutate(slice);
                state[SliceId] = slice;
            });
            gs.NotifySliceChanged(SliceId);
        }
    }
}
