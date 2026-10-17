using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Gooby-Renner round — port of runner.gd's _process/_spawn/_collide/_autoplay over the certified
    /// Runner rules, engine-free. Setup does not touch the RNG; the 1.5 s intro gates the clock and swipes.
    /// Three lanes, +5 % speed every 10 s; jump cones/boxes/barriers, slide overheads, dodge cars. Coins × combo,
    /// mystery boxes (magnet / ×2 / shield). First hit stumbles, second (easy: third) wipes out.
    /// Score = floor(metres) + coin points.
    /// </summary>
    public sealed class RunnerGame
    {
        public const double IntroSec = 1.5;
        public const double SpawnZ = -88.0;
        public const double DespawnZ = 9.0;
        public const double CoinY = 0.55;
        public const double SwipeMinPx = 30.0;

        public sealed class Obstacle
        {
            public string Kind;
            public int Lane, Row;
            public double Z, Yaw;
        }

        public sealed class Coin
        {
            public int Lane;
            public double Z, Y;
        }

        public sealed class Mystery
        {
            public int Lane;
            public double Z;
        }

        public Runner.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public double Elapsed { get; private set; }
        public double Meters { get; private set; }
        public int Lane { get; private set; } = 1;
        public double LaneX { get; private set; }
        public double JumpT { get; private set; } = -1.0;
        public double SlideT { get; private set; } = -1.0;
        public int Hits { get; private set; }
        public int Coins { get; private set; }
        public int CoinStreak { get; private set; }
        public double CoinPoints { get; private set; }
        public int Powerups { get; private set; }
        public bool Shield { get; private set; }
        public double Invuln { get; private set; }
        public double MagnetT { get; private set; }
        public double X2T { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public readonly List<Coin> CoinsOnRoad = new List<Coin>();
        public readonly List<Mystery> Mysteries = new List<Mystery>();

        public event Action LaneChanged;
        public event Action Jumped;
        public event Action Slid;
        public event Action<int> CoinTaken;          // points
        public event Action<int> ComboUp;            // multiplier
        public event Action<string> MysteryTaken;    // kind
        public event Action Stumbled;
        public event Action ShieldPopped;
        public event Action<int> Milestone;          // metres
        public event Action Finished_;

        private readonly List<Runner.Row> _recent = new List<Runner.Row>();
        private Runner.Row _pending;
        private double _distSinceRow;
        private double _nextMysteryAt;
        private int _rowSeq;
        private int _autoTarget = 1, _autoHandled = -1, _autoRow = -1;
        private string _autoAction = "";
        private double _autoAtZ;

        public RunnerGame(string mode, int seed)
        {
            Tune = Runner.ApplyDifficulty(Runner.Run, mode);
            Rng = new GoobyRng(seed);
            LaneX = Tune.LaneX[Lane];
            _nextMysteryAt = Tune.MysteryFirstM;
        }

        public int Score => Runner.FinalScore(Meters, CoinPoints, Tune);
        public double Speed => Runner.SpeedAt(Elapsed, Tune);
        public bool Sliding => SlideT >= 0.0;
        public double PlayerY => JumpT < 0.0 ? 0.0 : Tune.JumpHeight * Math.Sin((JumpT / Tune.JumpSec) * Math.PI);
        public int LivesLeft => Math.Max(0, Tune.MaxHits - Hits);
        public int ComboMult => Runner.ComboMultiplier(CoinStreak, Tune);

        public int LaneNow()
        {
            var best = 0;
            for (var i = 1; i < Tune.LaneX.Length; i++)
                if (Math.Abs(LaneX - Tune.LaneX[i]) < Math.Abs(LaneX - Tune.LaneX[best])) best = i;
            return best;
        }

        public bool ChangeLane(int step)
        {
            if (Finished || IntroLeft > 0.0) return false;
            var next = Math.Max(0, Math.Min(Tune.Lanes - 1, Lane + step));
            if (next == Lane) return false;
            Lane = next;
            LaneChanged?.Invoke();
            return true;
        }

        public bool Jump()
        {
            if (Finished || IntroLeft > 0.0 || JumpT >= 0.0 || SlideT >= 0.0) return false;
            JumpT = 0.0;
            Jumped?.Invoke();
            return true;
        }

        public bool Slide()
        {
            if (Finished || IntroLeft > 0.0 || SlideT >= 0.0 || JumpT >= 0.0) return false;
            SlideT = 0.0;
            Slid?.Invoke();
            return true;
        }

        /// <summary>Godot `_resolve_swipe`: abs x &gt; abs y → lane, else up = jump / down = slide.</summary>
        public bool Swipe(double dx, double dy)
        {
            if (Finished || IntroLeft > 0.0) return false;
            if (Math.Sqrt(dx * dx + dy * dy) < SwipeMinPx) return false;
            if (Math.Abs(dx) > Math.Abs(dy)) return ChangeLane(dx > 0.0 ? 1 : -1);
            return dy < 0.0 ? Jump() : Slide();
        }

        /// <summary>Certified autoplay from runner.gd — does not touch the seeded RNG.</summary>
        public void ApplyBot()
        {
            if (Finished || IntroLeft > 0.0) return;
            AutoPlan();
            if (string.IsNullOrEmpty(_autoAction)) return;
            foreach (var ob in Obstacles)
            {
                if (ob.Row != _autoRow) continue;
                if (ob.Z < _autoAtZ) return;
                if (_autoAction == "jump") Jump(); else Slide();
                _autoAction = "";
                return;
            }
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            dt = Math.Min(dt, 0.1);
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            Invuln = Math.Max(0.0, Invuln - dt);
            MagnetT = Math.Max(0.0, MagnetT - dt);
            X2T = Math.Max(0.0, X2T - dt);
            var dz = Speed * dt;
            var prev = Meters;
            Meters += dz;
            Spawn(dz);
            MovePlayer(dt);
            Collide(dz);
            var mile = Runner.CrossedMilestone(prev, Meters);
            if (mile > 0) Milestone?.Invoke(mile);
        }

        private void Spawn(double dz)
        {
            _distSinceRow += dz;
            if (_pending == null) _pending = Runner.GenerateRow(Rng, Elapsed, _recent, Tune);
            if (_distSinceRow >= _pending.Gap)
            {
                SpawnRow(_pending);
                _recent.Add(_pending);
                if (_recent.Count > 6) _recent.RemoveAt(0);
                _distSinceRow = 0.0;
                _pending = null;
            }
            if (Meters >= _nextMysteryAt)
            {
                Mysteries.Add(new Mystery { Lane = Lane, Z = SpawnZ });
                _nextMysteryAt += Tune.MysteryGapM;
            }
        }

        private void SpawnRow(Runner.Row row)
        {
            for (var lane = 0; lane < row.Lanes.Length; lane++)
            {
                if (row.Lanes[lane] == null) continue;
                var yaw = Rng.Next() < 0.5 ? 0.0 : Math.PI;
                yaw += (Rng.Next() - 0.5) * 0.12;
                Obstacles.Add(new Obstacle { Kind = row.Lanes[lane], Lane = lane, Z = SpawnZ, Yaw = yaw, Row = _rowSeq });
            }
            _rowSeq += 1;
            if (Rng.Next() >= Tune.CoinLineChance) return;
            var pass = Runner.PassableLanes(row);
            var options = new List<int>();
            var free = new List<int>();
            for (var lane = 0; lane < row.Lanes.Length; lane++)
            {
                if (!pass[lane]) continue;
                options.Add(lane);
                if (row.Lanes[lane] == null) free.Add(lane);
            }
            if (options.Count == 0) return;
            var pick = options[Math.Min(options.Count - 1, (int)Math.Floor(Rng.Next() * options.Count))];
            if (free.Count > 0 && Rng.Next() < 0.7)
                pick = free[Math.Min(free.Count - 1, (int)Math.Floor(Rng.Next() * free.Count))];
            var kind = row.Lanes[pick];
            var overJump = kind != null && Runner.Obstacles.TryGetValue(kind, out var def) && def.Pass == "jump";
            var count = Runner.CoinLineCount(Rng, Tune);
            for (var i = 0; i < count; i++)
            {
                var zOff = (i - (count - 1) / 2.0) * 1.15;
                var y = CoinY;
                if (overJump)
                {
                    var arc = Math.Cos((zOff / 2.2) * Math.PI * 0.5);
                    y = CoinY + Tune.JumpHeight * 0.8 * arc * arc;
                }
                CoinsOnRoad.Add(new Coin { Lane = pick, Z = SpawnZ + zOff, Y = y });
            }
        }

        private void MovePlayer(double dt)
        {
            var target = Tune.LaneX[Lane];
            LaneX += (target - LaneX) * Math.Min(1.0, dt / Tune.LaneChangeSec);
            if (JumpT >= 0.0)
            {
                JumpT += dt;
                if (JumpT >= Tune.JumpSec) JumpT = -1.0;
            }
            if (SlideT >= 0.0)
            {
                SlideT += dt;
                if (SlideT >= Tune.SlideSec) SlideT = -1.0;
            }
        }

        private void Collide(double dz)
        {
            var lane = LaneNow();
            var y = PlayerY;
            var sliding = Sliding;
            for (var i = Obstacles.Count - 1; i >= 0; i--)
            {
                var ob = Obstacles[i];
                var hit = !Finished && Invuln <= 0.0
                    && Runner.SweepHitsObstacle(lane, y, sliding, ob.Lane, ob.Kind, ob.Z, dz, Tune);
                ob.Z += dz;
                if (ob.Z > DespawnZ) Obstacles.RemoveAt(i);
                if (hit) OnHit();
                if (Finished) return;
            }
            CollectCoins(dz, lane, y);
            CollectMystery(dz, lane, y);
        }

        private void CollectCoins(double dz, int lane, double y)
        {
            for (var i = CoinsOnRoad.Count - 1; i >= 0; i--)
            {
                var c = CoinsOnRoad[i];
                c.Z += dz;
                if (c.Z > DespawnZ) { CoinsOnRoad.RemoveAt(i); continue; }
                var magnet = Runner.MagnetCollects(Tune.LaneX[c.Lane], c.Y, c.Z, LaneX, y + 0.55, 0.0, MagnetT > 0.0, Tune);
                var reach = Math.Abs(c.Z) < 0.55 && c.Lane == lane && Math.Abs(y + 0.55 - c.Y) < 0.8;
                if (Finished || !(magnet || reach)) continue;
                CoinsOnRoad.RemoveAt(i);
                TakeCoin();
            }
        }

        private void TakeCoin()
        {
            Coins += 1;
            var prev = ComboMult;
            CoinStreak += 1;
            var mult = ComboMult;
            var points = Runner.MysteryCoinPoints(mult, X2T > 0.0, Tune);
            CoinPoints += points;
            CoinTaken?.Invoke(points);
            if (mult > prev) ComboUp?.Invoke(mult);
        }

        private void CollectMystery(double dz, int lane, double y)
        {
            for (var i = Mysteries.Count - 1; i >= 0; i--)
            {
                var box = Mysteries[i];
                box.Z += dz;
                if (box.Z > DespawnZ) { Mysteries.RemoveAt(i); continue; }
                var reach = Math.Abs(box.Z) < 0.7 && box.Lane == lane && y < 0.8;
                if (Finished || !reach) continue;
                Mysteries.RemoveAt(i);
                var kind = Runner.RollMysteryPower(Rng);
                var st = Runner.ActivateMysteryPower(new Runner.MysteryState { MagnetT = MagnetT, X2T = X2T, Shield = Shield }, kind, Tune);
                MagnetT = st.MagnetT; X2T = st.X2T; Shield = st.Shield;
                Powerups += 1;
                MysteryTaken?.Invoke(kind);
            }
        }

        private void OnHit()
        {
            var result = Runner.ResolveHit(new Runner.HitState { Hits = Hits, Shield = Shield, InvulnT = Invuln }, Tune);
            if (result.Outcome == "ignored") return;
            Hits = result.Hits;
            Shield = result.Shield;
            Invuln = result.InvulnT;
            CoinStreak = 0;
            if (result.Outcome == "shielded") { ShieldPopped?.Invoke(); return; }
            if (result.Outcome == "wipeout") { Stumbled?.Invoke(); Finish(); return; }
            Stumbled?.Invoke();
        }

        private void AutoPlan()
        {
            if (_autoTarget != Lane) { ChangeLane(Math.Sign(_autoTarget - Lane)); return; }
            Obstacle row = null;
            foreach (var ob in Obstacles)
            {
                if (ob.Z > -1.0 || ob.Row <= _autoHandled) continue;
                if (row == null || ob.Z > row.Z) row = ob;
            }
            Mystery box = null;
            foreach (var m in Mysteries)
            {
                if (m.Z >= -0.5) continue;
                if (box == null || m.Z > box.Z) box = m;
            }
            var boxFirst = box != null && box.Z > -Speed * 1.5 && (row == null || box.Z > row.Z + 2.0);
            if (boxFirst)
            {
                if (box.Lane != Lane) ChangeLane(Math.Sign(box.Lane - Lane));
                return;
            }
            if (row == null || row.Z < -Speed * 0.95) return;
            _autoHandled = row.Row;
            AutoPickLane(row.Row);
        }

        private void AutoPickLane(int rowId)
        {
            var lanes = new string[Tune.Lanes];
            foreach (var ob in Obstacles) if (ob.Row == rowId) lanes[ob.Lane] = ob.Kind;
            var pass = Runner.PassableLanes(new Runner.Row { Lanes = lanes, Gap = 0.0 });
            var order = new[] { Lane, Lane - 1, Lane + 1, Lane - 2, Lane + 2 };
            var target = -1;
            foreach (var l in order)
                if (l >= 0 && l < Tune.Lanes && pass[l]) { target = l; break; }
            if (target < 0) return;
            _autoTarget = target;
            if (target != Lane) ChangeLane(Math.Sign(target - Lane));
            var kind = lanes[target];
            if (kind == null) { _autoAction = ""; return; }
            var passMode = Runner.Obstacles[kind].Pass;
            if (passMode == "none") { _autoAction = ""; return; }
            var lead = passMode == "jump" ? Tune.JumpSec * 0.45 : Tune.SlideSec * 0.42;
            _autoAction = passMode;
            _autoAtZ = -Speed * lead;
            _autoRow = rowId;
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Finished_?.Invoke();
        }
    }
}
