using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Angelteich round — port of fishing_pond.gd's _process/_swim/_step_respawns/_step_hook/_release/
    /// _resolve_reel/_land_catch over the certified FishingPond rules, engine-free. Seven swimmers patrol the pond
    /// (x within ±1.8 m, depth 0.55–3.7 m); HOLD lowers the hook (2.1 m/s), RELEASE hooks the nearest swimmer within
    /// 0.55 m and raises it (3.4 m/s). Small/medium fish land at once (+2/+3), a big one (+5) has to be reeled with
    /// five taps inside two seconds or the line breaks; a boot costs 3 and shows up at the earliest 14 s after the
    /// last. Every hooked swimmer is replaced 1.2 s later. 90 s; endless until three line breaks or boots.
    /// </summary>
    public sealed class FishingPondGame
    {
        public const double IntroSec = 1.5;
        public const string PhaseIdle = "idle", PhaseLower = "lower", PhaseRaise = "raise", PhaseReel = "reel";

        public sealed class Swimmer
        {
            public string Kind, Species, CollectionId;
            public bool Rare;
            public double X, Depth, Dir, Speed, Scale, Wiggle;
            public bool IsBoot => Kind == "boot";
        }

        public FishingPond.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public double Elapsed { get; private set; }
        public double HookDepth { get; private set; }
        public string Phase { get; private set; } = PhaseIdle;
        public Swimmer Hooked { get; private set; }
        public int ReelTaps { get; private set; }
        public double ReelElapsed { get; private set; }
        public double SinceBoot { get; private set; }
        public int Failures { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public bool RareSetAwarded { get; private set; }
        public readonly List<Swimmer> Fish = new List<Swimmer>();
        public readonly List<string> CaughtSpecies = new List<string>();
        private readonly List<double> _respawn = new List<double>();
        private readonly Func<double> _stream;

        public event Action Cast;                         // the hook starts down
        public event Action MissedCast;                   // released with nothing in reach
        public event Action<Swimmer> HookedFish;          // something on the line (a reel fight starts for L)
        public event Action<Swimmer, int> Landed;         // fish or boot in the bucket, points delta
        public event Action<int> RareSet;                 // the three rares are in: bonus
        public event Action LineBroke;
        public event Action Finished_;

        public FishingPondGame(string mode, int seed)
        {
            Tune = FishingPond.ApplyDifficulty(FishingPond.Fishing, mode);
            Rng = new GoobyRng(seed);
            _stream = () => Rng.Next();
            for (var i = 0; i < Tune.FishCount; i++) Fish.Add(SpawnFish());
        }

        public bool Running => IntroLeft <= 0.0 && !Finished;
        public bool HasCatch => Hooked != null;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        /// <summary>The water column shown: the deepest the hook goes plus a little bottom.</summary>
        public double DepthSpan => Tune.MaxDepth + 0.35;
        public (double x, double depth) HookAt => (Tune.HookX, HookDepth);
        public double ReelWindowLeft => Phase == PhaseReel ? Math.Max(0.0, Tune.ReelWindowSec - ReelElapsed) : 0.0;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                Swim(dt);
                return;
            }
            Elapsed += dt;
            SinceBoot += dt;
            Swim(dt);
            StepRespawns(dt);
            StepHook(dt);
            if (!Finished && !Tune.Endless && Elapsed >= Tune.DurationSec) Finish();
        }

        // ── input ──────────────────────────────────────────────────────────

        /// <summary>Finger down: casts (idle) or cranks the reel once (reel fight).</summary>
        public void Press()
        {
            if (!Running) return;
            if (Phase == PhaseReel) { ReelTaps++; return; }
            if (Phase != PhaseIdle) return;
            Phase = PhaseLower;
            Cast?.Invoke();
        }

        /// <summary>Finger up while the hook sinks: it snaps shut around the nearest swimmer in reach and comes up.</summary>
        public void Release()
        {
            if (!Running || Phase != PhaseLower) return;
            Phase = PhaseRaise;
            var index = NearestCatch();
            if (index < 0) { MissedCast?.Invoke(); return; }
            Hooked = Fish[index];
            Fish.RemoveAt(index);
            _respawn.Add(Tune.RespawnSec);
            HookedFish?.Invoke(Hooked);
            if (FishingPond.NeedsReel(Hooked.Kind))
            {
                Phase = PhaseReel;
                ReelTaps = 0;
                ReelElapsed = 0.0;
                return;
            }
            LandCatch();
        }

        /// <summary>fishing_pond_logic.gd nearest_catch: index of the closest swimmer within the catch radius, −1 for none.</summary>
        public int NearestCatch()
        {
            var best = -1; var bestDist = double.PositiveInfinity;
            for (var i = 0; i < Fish.Count; i++)
            {
                var dx = Fish[i].X - Tune.HookX; var dd = Fish[i].Depth - HookDepth;
                var d = Math.Sqrt(dx * dx + dd * dd);
                if (d <= Tune.CatchRadius && d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }

        // ── the pond ───────────────────────────────────────────────────────

        private Swimmer SpawnFish()
        {
            var half = Tune.PondHalfW;
            if (FishingPond.ShouldSpawnBoot(_stream, SinceBoot, Tune))
            {
                SinceBoot = 0.0;
                return new Swimmer
                {
                    Kind = "boot", Species = "boot", CollectionId = "boot", X = Rng.Next() < 0.5 ? -half : half,
                    Depth = 0.7 + Rng.Next() * 0.5, Dir = 1.0, Speed = Tune.BootSpeed, Scale = 0.42, Wiggle = Rng.Next() * Math.PI * 2.0,
                };
            }
            var kind = FishingPond.RollFishKind(_stream);
            var (species, collectionId, rare) = FishingPond.RollSpeciesDetail(kind, _stream);
            var scale = 0.34;
            foreach (var s in FishingPond.Sizes) if (s.Kind == kind) scale = s.Scale;
            return new Swimmer
            {
                Kind = kind, Species = species, CollectionId = collectionId, Rare = rare,
                X = -half + Rng.Next() * half * 2.0,
                Depth = Tune.FishDepthMin + Rng.Next() * (Tune.FishDepthMax - Tune.FishDepthMin),
                Dir = Rng.Next() < 0.5 ? 1.0 : -1.0,
                Speed = FishingPond.FishSpeedFor(kind, _stream),
                Scale = scale, Wiggle = Rng.Next() * Math.PI * 2.0,
            };
        }

        private void Swim(double dt)
        {
            var half = Tune.PondHalfW;
            foreach (var f in Fish)
            {
                f.X += f.Dir * f.Speed * dt;
                f.Wiggle += dt * 6.0;
                if (f.X > half) { f.X = half; f.Dir = -1.0; }
                else if (f.X < -half) { f.X = -half; f.Dir = 1.0; }
            }
        }

        private void StepRespawns(double dt)
        {
            for (var i = _respawn.Count - 1; i >= 0; i--)
            {
                _respawn[i] -= dt;
                if (_respawn[i] > 0.0) continue;
                _respawn.RemoveAt(i);
                Fish.Add(SpawnFish());
            }
        }

        private void StepHook(double dt)
        {
            switch (Phase)
            {
                case PhaseLower:
                    HookDepth = FishingPond.LowerDepth(HookDepth, dt, Tune);
                    break;
                case PhaseRaise:
                    HookDepth = Math.Max(0.0, HookDepth - Tune.RaiseSpeed * dt);
                    if (HookDepth <= 0.0) { Phase = PhaseIdle; Hooked = null; }
                    break;
                case PhaseReel:
                    ReelElapsed = FishingPond.AdvanceReelElapsed(ReelElapsed, dt, Tune);
                    var verdict = FishingPond.ReelResolve(ReelTaps, ReelElapsed, Tune);
                    if (verdict != "reeling") ResolveReel(verdict);
                    break;
            }
        }

        private void ResolveReel(string verdict)
        {
            Phase = PhaseRaise;
            if (verdict == "caught") { LandCatch(); return; }
            Hooked = null;
            LineBroke?.Invoke();
            RecordFailure();
        }

        private void LandCatch()
        {
            var fish = Hooked;
            var value = FishingPond.CatchValue(fish.Kind);
            Score = FishingPond.ApplyCatch(Score, value);
            if (!fish.IsBoot)
            {
                CaughtSpecies.Add(fish.Species);
                var bonus = FishingPond.RareSetBonusFor(CaughtSpecies);
                if (bonus > 0 && !RareSetAwarded)
                {
                    RareSetAwarded = true;
                    Score += bonus;
                    RareSet?.Invoke(bonus);
                }
            }
            Landed?.Invoke(fish, value);
            Hooked = null;
            if (fish.IsBoot) RecordFailure();
        }

        /// <summary>Endless: a line break or a boot is a failure, three end the round (fishing_pond_logic.gd record_failure).</summary>
        private void RecordFailure()
        {
            if (!Tune.Endless) return;
            Failures++;
            if (Failures >= Tune.EndlessFailureLimit) Finish();
        }

        /// <summary>The album entries this round earned ("fish" set ids; rares map onto their common cousin).</summary>
        public List<string> CollectionIds()
        {
            var ids = new List<string>();
            foreach (var s in CaughtSpecies) ids.Add(FishingPond.SpeciesCollectionId(s));
            return ids;
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Math.Max(0, Score);
            Finished_?.Invoke();
        }
    }
}
