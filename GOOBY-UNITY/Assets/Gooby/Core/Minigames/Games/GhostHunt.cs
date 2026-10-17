using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Geisterjagd — port of GOOBY/src/minigames/games/ghostHunt.logic.js (PLAN3 §C10.1 #2): sheet ghosts peek
    /// out of graves/pumpkins/the crypt, the visible window ramps 2.2 s → 0.9 s over 90 s, tap = catch +3,
    /// chain +1 per catch within 1.5 s (cap +5), pumpkin decoy −2, every 25 s a boo wave of 5 ghosts (≥ 4 caught
    /// = +10), lantern (3 s reveal) and net (3 automatic chain catches) as pickups. The scene and the bot share
    /// this exact state machine.
    /// </summary>
    public static class GhostHunt
    {
        public sealed class Tune
        {
            public double DurationSec = 90.0, VisibleStartSec = 2.2, VisibleEndSec = 0.9;
            public int CatchPoints = 3;
            public double ChainWindowSec = 1.5;
            public int ChainBonusCap = 5, DecoyPenalty = -2;
            public double DecoyFlickerSec = 1.8, DecoyChanceStart = 0.12, DecoyChanceEnd = 0.28;
            public double BooEverySec = 25.0;
            public int BooCount = 5, BooCatchMin = 4, BooBonus = 10;
            public double BooMinVisibleSec = 1.6, LanternSec = 3.0, LanternRevealBonusSec = 0.4;
            public int NetCatches = 3;
            public double TokenVisibleSec = 5.0;
            public (string kind, double from, double to)[] TokenWindows = { ("lantern", 12.0, 18.0), ("net", 30.0, 36.0), ("lantern", 52.0, 58.0), ("net", 68.0, 74.0) };
            public double SpawnStartSec = 2.8, SpawnEndSec = 1.5, FirstSpawnSec = 0.8;
            public double BotReactSec = 0.2, BotMinGapSec = 0.5, BotEngage = 0.4, BotWaveEngage = 0.62, RiseFrac = 0.16, SinkFrac = 0.16;
            public bool Endless = false;
            public int EndlessEscapeLimit = 3;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Hunt = new Tune();

        public struct Spot
        {
            public int Id;
            public string Kind;
            public double X, Z;
        }

        public static readonly Spot[] Spots =
        {
            new Spot { Id = 0, Kind = "grave", X = -2.1, Z = -1.7 }, new Spot { Id = 1, Kind = "pumpkin", X = -0.7, Z = -1.5 },
            new Spot { Id = 2, Kind = "grave", X = 0.8, Z = -1.7 }, new Spot { Id = 3, Kind = "grave", X = 2.1, Z = -1.6 },
            new Spot { Id = 4, Kind = "pumpkin", X = -2.3, Z = -3.4 }, new Spot { Id = 5, Kind = "grave", X = -0.9, Z = -3.5 },
            new Spot { Id = 6, Kind = "grave", X = 1.0, Z = -3.3 }, new Spot { Id = 7, Kind = "pumpkin", X = 2.3, Z = -3.5 },
            new Spot { Id = 8, Kind = "grave", X = -1.7, Z = -5.3 }, new Spot { Id = 9, Kind = "crypt", X = 0.1, Z = -6.4 },
            new Spot { Id = 10, Kind = "grave", X = 1.8, Z = -5.2 }, new Spot { Id = 11, Kind = "pumpkin", X = 0.0, Z = -4.9 },
        };

        public static readonly (int id, double x, double z)[] DecoySpots = { (0, -1.5, -2.5), (1, 1.6, -2.5), (2, -2.3, -4.5), (3, 2.3, -4.4) };
        public static readonly (double x, double z)[] TokenAnchors = { (-1.1, -2.1), (1.1, -2.1), (-1.1, -2.1), (1.1, -2.1) };

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double interval, windows, duration, botEngage, botWaveEngage;
            bool endless;
            switch (mode)
            {
                case "easy": interval = 1.2; windows = 1.25; duration = 1.2; botEngage = 0.44; botWaveEngage = 0.66; endless = false; break;
                case "hard": interval = 0.85; windows = 0.8; duration = 1.0; botEngage = 0.36; botWaveEngage = 0.58; endless = false; break;
                case "endless": interval = 0.85; windows = 0.8; duration = 1.0; botEngage = 0.36; botWaveEngage = 0.58; endless = true; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * duration;
            o.SpawnStartSec = tune.SpawnStartSec * interval;
            o.SpawnEndSec = tune.SpawnEndSec * interval;
            o.VisibleStartSec = tune.VisibleStartSec * windows;
            o.VisibleEndSec = tune.VisibleEndSec * windows;
            o.BooMinVisibleSec = tune.BooMinVisibleSec * windows;
            o.BotEngage = botEngage;
            o.BotWaveEngage = botWaveEngage;
            o.Endless = endless;
            o.Mode = mode;
            return o;
        }

        private static double RampT(double elapsed, Tune tune) => Math.Min(1.0, Math.Max(0.0, elapsed / tune.DurationSec));

        public static double VisibleDurAt(double elapsed, Tune tune) => tune.VisibleStartSec + (tune.VisibleEndSec - tune.VisibleStartSec) * RampT(elapsed, tune);
        public static double SpawnIntervalAt(double elapsed, Tune tune) => tune.SpawnStartSec + (tune.SpawnEndSec - tune.SpawnStartSec) * RampT(elapsed, tune);
        public static double DecoyChanceAt(double elapsed, Tune tune) => tune.DecoyChanceStart + (tune.DecoyChanceEnd - tune.DecoyChanceStart) * RampT(elapsed, tune);

        public static int ChainBonus(int chain, Tune tune) => Math.Min(tune.ChainBonusCap, Math.Max(0, chain - 1));

        public static List<double> BooWaveTimes(Tune tune)
        {
            var times = new List<double>();
            var every = tune.BooEverySec;
            var t = every;
            while (t <= tune.DurationSec - 10.0)
            {
                times.Add(t);
                t += every;
            }
            return times;
        }

        // ── state ──────────────────────────────────────────────────────────

        public sealed class Ghost
        {
            public int Id, Spot;
            public double SpawnT, Dur;
            public int? Wave;
            public bool Revealed;
        }

        public sealed class Flicker
        {
            public int Decoy;
            public double StartT;
        }

        public sealed class Token
        {
            public int Window;
            public string Kind;
            public double StartT;
        }

        public sealed class BooWave
        {
            public int Idx, Total, Caught, Resolved;
        }

        public sealed class Event
        {
            public string Type;
            public JObject Data = new JObject();
        }

        public sealed class Bot
        {
            public GoobyRng Rng;
            public double NextFreeT;
            public Dictionary<int, bool> Marks = new Dictionary<int, bool>();
        }

        public sealed class State
        {
            public int Seed;
            public Tune Tune;
            public GoobyRng Rng;
            public double T;
            public int Score, Chain;
            public double LastCatchT = -99.0;
            public int NetLeft;
            public double LanternT;
            public List<Ghost> Ghosts = new List<Ghost>();
            public List<Flicker> Flickers = new List<Flicker>();
            public List<Token> Tokens = new List<Token>();
            public int NextGhostId = 1;
            public double NextSpawnT;
            public List<double> BooTimes;
            public int BooIdx;
            public BooWave BooActive;
            public bool[] TokensSpawned;
            public double[] TokenAt;
            public int Caught, Missed, DecoysTapped, BooBonuses, EscapedWaves;
            public List<Event> Events = new List<Event>();
            public bool Ended;
            public Bot Bot;
        }

        public static State CreateHunt(int seed, Tune tune)
        {
            var n = tune.TokenWindows.Length;
            var s = new State
            {
                Seed = seed, Tune = tune,
                Rng = new GoobyRng(unchecked((uint)(seed ^ 0x60db15c3))),
                NextSpawnT = tune.FirstSpawnSec,
                BooTimes = BooWaveTimes(tune),
                TokensSpawned = new bool[n],
                TokenAt = new double[n],
            };
            for (var i = 0; i < n; i++) s.TokenAt[i] = -1.0;
            return s;
        }

        private static void Emit(State s, string type, Action<JObject> fill = null)
        {
            var e = new Event { Type = type };
            fill?.Invoke(e.Data);
            s.Events.Add(e);
        }

        public static void StepHunt(State s, double dt)
        {
            if (s.Ended) return;
            var tune = s.Tune;
            s.T += dt;
            var t = s.T;
            if (!tune.Endless && t >= tune.DurationSec)
            {
                s.Ended = true;
                Emit(s, "end");
                return;
            }
            s.LanternT = Math.Max(0.0, s.LanternT - dt);
            ExpireEntities(s, t, tune);
            SpawnTokens(s, t, tune);
            if (tune.Endless && s.BooIdx >= s.BooTimes.Count)
            {
                var last = s.BooTimes.Count > 0 ? s.BooTimes[s.BooTimes.Count - 1] : 0.0;
                s.BooTimes.Add(last + tune.BooEverySec);
            }
            RunBooWave(s, t, tune);
            RunSpawner(s, t, tune);
        }

        public struct Tap
        {
            public string Kind; // ghost | decoy | token | ""
            public int Id;
        }

        public static (string kind, int points) TapHunt(State s, Tap target)
        {
            if (s.Ended) return ("ended", 0);
            switch (target.Kind)
            {
                case "ghost": return TapGhost(s, target.Id);
                case "decoy": return TapDecoy(s, target.Id);
                case "token": return TapToken(s, target.Id);
            }
            s.Chain = 0;
            return ("miss", 0);
        }

        /// <summary>§C10.1 bot: taps real ghosts at spawn + 200 ms, avoids decoys, always collects pickups.</summary>
        public static List<Tap> BotStep(State s)
        {
            var tune = s.Tune;
            if (s.Bot == null) s.Bot = new Bot { Rng = new GoobyRng(unchecked((uint)(s.Seed ^ 0x7f4a7c15))), NextFreeT = 0.0 };
            var bot = s.Bot;
            var taps = new List<Tap>();
            if (s.Ended) return taps;
            var marks = bot.Marks;
            var rng = bot.Rng;
            foreach (var g in s.Ghosts)
            {
                if (marks.ContainsKey(g.Id)) continue;
                var engage = g.Wave.HasValue ? tune.BotWaveEngage : tune.BotEngage;
                marks[g.Id] = rng.Next() < engage;
            }
            var now = s.T;
            foreach (var token in s.Tokens)
            {
                if (now >= token.StartT + 0.3 && now >= bot.NextFreeT)
                {
                    taps.Add(new Tap { Kind = "token", Id = token.Window });
                    bot.NextFreeT = now + tune.BotMinGapSec;
                }
            }
            foreach (var g in s.Ghosts)
            {
                if (!marks.TryGetValue(g.Id, out var go) || !go) continue;
                if (now < g.SpawnT + tune.BotReactSec) continue;
                if (now < bot.NextFreeT) break;
                taps.Add(new Tap { Kind = "ghost", Id = g.Id });
                bot.NextFreeT = now + tune.BotMinGapSec;
            }
            return taps;
        }

        public static int HuntScore(State s) => Math.Max(0, (int)Math.Round((double)s.Score, MidpointRounding.AwayFromZero));

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1, double maxSec = 900.0)
        {
            var tune = ApplyDifficulty(Hunt, mode);
            var s = CreateHunt(seed, tune);
            var dt = 1.0 / 30.0;
            while (!s.Ended && s.T < maxSec)
            {
                StepHunt(s, dt);
                if (s.Ended) break;
                foreach (var tap in BotStep(s)) TapHunt(s, tap);
                s.Events.Clear();
            }
            return new JObject
            {
                ["score"] = HuntScore(s), ["caught"] = s.Caught, ["missed"] = s.Missed,
                ["escapedWaves"] = s.EscapedWaves, ["booBonuses"] = s.BooBonuses, ["time"] = s.T,
            };
        }

        public static JObject RunMeta(State s) => new JObject { ["ghostsCaught"] = s.Caught, ["decoysTapped"] = s.DecoysTapped, ["booBonuses"] = s.BooBonuses };

        private static List<int> FreeSpots(State s)
        {
            var busy = new HashSet<int>();
            foreach (var g in s.Ghosts) busy.Add(g.Spot);
            var free = new List<int>();
            foreach (var sp in Spots) if (!busy.Contains(sp.Id)) free.Add(sp.Id);
            return free;
        }

        private static Ghost SpawnGhost(State s, int? wave, double forcedDur)
        {
            var tune = s.Tune;
            var free = FreeSpots(s);
            if (free.Count == 0) return null;
            var spot = free[(int)Math.Floor(s.Rng.Next() * free.Count)];
            var dur = forcedDur;
            if (dur < 0.0)
            {
                dur = VisibleDurAt(s.T, tune);
                if (s.LanternT > 0.0) dur += tune.LanternRevealBonusSec;
            }
            var ghost = new Ghost { Id = s.NextGhostId, Spot = spot, SpawnT = s.T, Dur = dur, Wave = wave, Revealed = s.LanternT > 0.0 };
            s.NextGhostId += 1;
            s.Ghosts.Add(ghost);
            Emit(s, "ghostSpawn", d => { d["id"] = ghost.Id; d["spot"] = spot; d["wave"] = wave.HasValue ? (JToken)wave.Value : JValue.CreateNull(); d["revealed"] = ghost.Revealed; });
            return ghost;
        }

        private static void ResolveWaveGhost(State s, Ghost ghost, bool wasCaught)
        {
            var wave = s.BooActive;
            if (wave == null || !ghost.Wave.HasValue || ghost.Wave.Value != wave.Idx) return;
            wave.Resolved += 1;
            if (wasCaught) wave.Caught += 1;
            if (wave.Resolved < wave.Total) return;
            var tune = s.Tune;
            if (wave.Caught >= tune.BooCatchMin)
            {
                s.Score += tune.BooBonus;
                s.BooBonuses += 1;
                Emit(s, "booBonus", d => { d["caught"] = wave.Caught; d["bonus"] = tune.BooBonus; });
            }
            else
            {
                s.EscapedWaves += 1;
                Emit(s, "booEnd", d => { d["caught"] = wave.Caught; d["escaped"] = s.EscapedWaves; });
                if (tune.Endless && s.EscapedWaves >= tune.EndlessEscapeLimit)
                {
                    s.Ended = true;
                    Emit(s, "end", d => d["reason"] = "escapes");
                }
            }
            s.BooActive = null;
        }

        private static void ExpireEntities(State s, double t, Tune tune)
        {
            for (var i = s.Ghosts.Count - 1; i >= 0; i--)
            {
                var g = s.Ghosts[i];
                if (t - g.SpawnT < g.Dur) continue;
                s.Ghosts.RemoveAt(i);
                s.Missed += 1;
                Emit(s, "ghostGone", d => { d["id"] = g.Id; d["spot"] = g.Spot; });
                ResolveWaveGhost(s, g, false);
            }
            for (var i = s.Flickers.Count - 1; i >= 0; i--)
            {
                if (t - s.Flickers[i].StartT >= tune.DecoyFlickerSec)
                {
                    var f = s.Flickers[i];
                    Emit(s, "flickerEnd", d => d["decoy"] = f.Decoy);
                    s.Flickers.RemoveAt(i);
                }
            }
            for (var i = s.Tokens.Count - 1; i >= 0; i--)
            {
                if (t - s.Tokens[i].StartT >= tune.TokenVisibleSec)
                {
                    var tk = s.Tokens[i];
                    Emit(s, "tokenGone", d => d["kind"] = tk.Kind);
                    s.Tokens.RemoveAt(i);
                }
            }
        }

        private static void SpawnTokens(State s, double t, Tune tune)
        {
            var windows = tune.TokenWindows;
            for (var w = 0; w < windows.Length; w++)
            {
                if (s.TokensSpawned[w]) continue;
                var win = windows[w];
                if (s.TokenAt[w] < 0.0 && t >= win.from - 5.0) s.TokenAt[w] = win.from + s.Rng.Next() * (win.to - win.from);
                if (s.TokenAt[w] >= 0.0 && t >= s.TokenAt[w])
                {
                    s.TokensSpawned[w] = true;
                    s.Tokens.Add(new Token { Window = w, Kind = win.kind, StartT = t });
                    var kind = win.kind;
                    var idx = w;
                    Emit(s, "tokenSpawn", d => { d["kind"] = kind; d["window"] = idx; });
                }
            }
        }

        private static void RunBooWave(State s, double t, Tune tune)
        {
            var idx = s.BooIdx;
            if (idx >= s.BooTimes.Count || t < s.BooTimes[idx]) return;
            s.BooIdx = idx + 1;
            while (FreeSpots(s).Count < tune.BooCount && s.Ghosts.Count > 0)
            {
                var g = s.Ghosts[0];
                s.Ghosts.RemoveAt(0);
                Emit(s, "ghostGone", d => { d["id"] = g.Id; d["spot"] = g.Spot; });
            }
            var dur = Math.Max(VisibleDurAt(t, tune), tune.BooMinVisibleSec);
            var spawned = 0;
            for (var i = 0; i < tune.BooCount; i++)
                if (SpawnGhost(s, idx, dur) != null) spawned += 1;
            s.BooActive = new BooWave { Idx = idx, Total = spawned, Caught = 0, Resolved = 0 };
            Emit(s, "booWave", d => { d["idx"] = idx; d["count"] = spawned; });
        }

        private static void RunSpawner(State s, double t, Tune tune)
        {
            if (t < s.NextSpawnT) return;
            s.NextSpawnT = t + SpawnIntervalAt(t, tune);
            var busy = new HashSet<int>();
            foreach (var f in s.Flickers) busy.Add(f.Decoy);
            var freeDecoys = new List<int>();
            foreach (var d in DecoySpots) if (!busy.Contains(d.id)) freeDecoys.Add(d.id);
            if (s.Rng.Next() < DecoyChanceAt(t, tune) && freeDecoys.Count > 0)
            {
                var decoy = freeDecoys[(int)Math.Floor(s.Rng.Next() * freeDecoys.Count)];
                s.Flickers.Add(new Flicker { Decoy = decoy, StartT = t });
                Emit(s, "flicker", d => d["decoy"] = decoy);
            }
            else SpawnGhost(s, null, -1.0);
        }

        private static (string, int) TapGhost(State s, int id)
        {
            var tune = s.Tune;
            var index = s.Ghosts.FindIndex(g => g.Id == id);
            if (index < 0) return ("miss", 0);
            var ghost = s.Ghosts[index];
            s.Ghosts.RemoveAt(index);
            var t = s.T;
            var auto = s.NetLeft > 0;
            var chained = auto || t - s.LastCatchT <= tune.ChainWindowSec;
            s.Chain = chained ? s.Chain + 1 : 1;
            if (auto) s.NetLeft -= 1;
            var points = tune.CatchPoints + ChainBonus(s.Chain, tune);
            s.Score += points;
            s.LastCatchT = t;
            s.Caught += 1;
            Emit(s, "catch", d => { d["id"] = ghost.Id; d["spot"] = ghost.Spot; d["points"] = points; d["chain"] = s.Chain; d["auto"] = auto; });
            ResolveWaveGhost(s, ghost, true);
            return ("ghost", points);
        }

        private static (string, int) TapDecoy(State s, int decoy)
        {
            var tune = s.Tune;
            var index = s.Flickers.FindIndex(f => f.Decoy == decoy);
            if (index < 0) return ("miss", 0);
            s.Flickers.RemoveAt(index);
            s.Score = Math.Max(0, s.Score + tune.DecoyPenalty);
            s.Chain = 0;
            s.LastCatchT = -99.0;
            s.DecoysTapped += 1;
            Emit(s, "decoy", d => { d["decoy"] = decoy; d["points"] = tune.DecoyPenalty; });
            return ("decoy", tune.DecoyPenalty);
        }

        private static (string, int) TapToken(State s, int window)
        {
            var tune = s.Tune;
            var index = s.Tokens.FindIndex(tk => tk.Window == window);
            if (index < 0) return ("miss", 0);
            var token = s.Tokens[index];
            s.Tokens.RemoveAt(index);
            if (token.Kind == "lantern") s.LanternT = tune.LanternSec;
            else s.NetLeft = tune.NetCatches;
            Emit(s, "powerup", d => d["kind"] = token.Kind);
            return ("token", 0);
        }
    }
}
