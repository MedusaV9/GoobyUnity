using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Gobnom;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// GOB NOM session — the flow of gobnom_game.gd around the pure <see cref="GobnomLogic"/>: level select (campaign +
    /// coop) → puzzle → won/lost → next/retry/select, until the player taps "Fertig" (one arcade round = one session,
    /// its score = the sum of the level wins). The sim runs on a fixed 60-Hz tick off wall-clock slices; every level
    /// opens with a 1.5-s establish beat during which neither sim nor input move. Wins book into the "gobnom" save
    /// slice (stars/best/cleared) and add <see cref="GobnomProgress.FinalScore"/> to the session.
    /// Online coop (the netz half of gobnom_game.gd): <see cref="StartOnline"/> builds the coop level from the server seed,
    /// the sim steps through a <see cref="GobnomLockstep"/> only while the partner's fence covers the tick, touches on the
    /// own half become planned actions and frames/hashes leave through <see cref="FrameOut"/>/<see cref="HashOut"/>.
    /// </summary>
    public sealed class GobnomGame
    {
        public const double LevelIntroSec = 1.5;
        public const double TickSec = 1.0 / GobnomLogic.Tps;
        public const double TapRadius = 34.0;
        /// <summary>How long a stalled online sim waits once the server confirmed the partner's result before it takes that result.</summary>
        public const double ServerResultGraceSec = 2.0;
        public const string PhaseSelect = "select", PhasePlay = "play", PhaseWon = "won", PhaseLost = "lost";

        public JObject Balance { get; }
        public List<JObject> Campaign { get; }
        public List<JObject> CoopLevels { get; }
        public string Phase { get; private set; } = PhaseSelect;
        public string Track { get; private set; } = GobnomData.TrackCampaign;
        public int LevelId { get; private set; }
        public int Attempt { get; private set; }
        public JObject Level { get; private set; }
        public GobnomRun Run { get; private set; }
        public int SessionScore { get; private set; }
        /// <summary>Rest of the level's establish beat (deliberately not called IntroLeft: the frame would count down).</summary>
        public double LevelIntroLeft { get; private set; }
        /// <summary>Seconds since the phase last changed (end overlays wait a beat before the bot moves on).</summary>
        public double PhaseTime { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        /// <summary>The last finished level: won?, stars, the score it added and whether it was the first clear.</summary>
        public bool LastWon { get; private set; }
        public int LastStars { get; private set; }
        public int LastTotal { get; private set; }
        public bool LastFirstClear { get; private set; }

        /// <summary>The lockstep of the current online run (null for campaign and hot-seat coop).</summary>
        public GobnomLockstep Net { get; private set; }
        public bool OnlineRun => Net != null;
        /// <summary>The online sim waits for the partner's fence (the view says "Warte auf Partner…").</summary>
        public bool NetWaiting { get; private set; }
        /// <summary>QA bot in the select: true = handled (the online lobby votes instead of opening a local level).</summary>
        public Func<bool> SelectBot { get; set; }

        public event Action<GobnomEvent> Event;
        public event Action<string> PhaseChanged;
        public event Action Finished_;
        /// <summary>A lockstep input frame for the wire (GN_INPUT).</summary>
        public event Action<JObject> FrameOut;
        /// <summary>An own state hash for the wire (GN_HASH: tick, hash).</summary>
        public event Action<int, string> HashOut;
        /// <summary>The local hash guard found a different partner hash (tick).</summary>
        public event Action<int> NetDesync;

        private readonly long _runSeed;
        private readonly GameStateStore _store;
        private readonly JObject _localSave = new JObject();
        private double _accum;
        private List<JObject> _plan = new List<JObject>();
        private int _planNext;
        private bool _botOn;
        private string _serverOutcome;
        private int _serverJars;
        private double _serverWait;

        public GobnomGame(long runSeed, GameStateStore store = null)
        {
            _runSeed = runSeed;
            _store = store;
            Balance = GobnomData.LoadBalance();
            Campaign = GobnomData.LoadCampaign();
            CoopLevels = GobnomData.LoadCoop();
        }

        /// <summary>The save the progress reads from (the live store, or a session-local stand-in in tests).</summary>
        public JObject Save => _store?.State ?? _localSave;

        public bool IsCoop => Run != null && Run.Coop;

        public void ShowTrack(string track)
        {
            if (Phase != PhaseSelect) return;
            Track = track == GobnomData.TrackCoop ? GobnomData.TrackCoop : GobnomData.TrackCampaign;
        }

        public bool IsUnlocked(string track, int id) => id >= 1 && id <= GobnomProgress.MaxUnlocked(Save, track);

        /// <summary>Opens a level (seed after gobnom_game.gd: run seed + id·1009 + attempt·131, coop +500 017).</summary>
        public bool Open(string track, int id)
        {
            if (Finished) return false;
            var levels = track == GobnomData.TrackCoop ? CoopLevels : Campaign;
            var level = GobnomData.LevelById(levels, id);
            if (level == null) return false;
            Track = track == GobnomData.TrackCoop ? GobnomData.TrackCoop : GobnomData.TrackCampaign;
            LevelId = id;
            Attempt += 1;
            var seed = _runSeed + id * 1009L + Attempt * 131L;
            if (Track == GobnomData.TrackCoop) seed += 500_017L;
            Level = level;
            Net = null;
            NetWaiting = false;
            Run = GobnomLogic.NewRun(level, Balance, seed);
            _accum = 0.0;
            LevelIntroLeft = LevelIntroSec;
            _plan = GobnomSolver.SortedActions(level);
            _planNext = 0;
            SetPhase(PhasePlay);
            return true;
        }

        /// <summary>
        /// GOBNOM_START: the coop level from the SERVER seed, on our side ("a" = inviter, "b" = guest) — both devices build
        /// the identical run. The first fence leaves at once so the partner may compute the first ticks.
        /// </summary>
        public bool StartOnline(int id, long seed, string side, int delay = GobnomLockstep.InputDelayDefault, int hashTicks = GobnomLockstep.HashTicksDefault)
        {
            if (Finished) return false;
            var level = GobnomData.LevelById(CoopLevels, id);
            if (level == null) return false;
            Attempt += 1;
            var net = new GobnomLockstep();
            net.Start(level, Balance, seed, side, delay, hashTicks);
            EnterOnline(level, id, net, LevelIntroSec);
            var first = net.TakeFrame(true);
            if (first != null) FrameOut?.Invoke(first);
            return true;
        }

        /// <summary>Own rejoin mid-run (GOBNOM_SNAPSHOT): the run replayed from seed + both sides' frame buffers, no intro beat.</summary>
        public bool ResumeOnline(int id, long seed, string side, JObject frames, int delay = GobnomLockstep.InputDelayDefault, int hashTicks = GobnomLockstep.HashTicksDefault)
        {
            if (Finished) return false;
            var level = GobnomData.LevelById(CoopLevels, id);
            if (level == null) return false;
            var net = new GobnomLockstep();
            net.Resume(level, Balance, seed, side, frames, delay, hashTicks);
            EnterOnline(level, id, net, 0.0);
            while (_planNext < _plan.Count && GobnomSolver.PlanTick(_plan[_planNext]) <= net.SentUpTo) _planNext += 1;
            return true;
        }

        private void EnterOnline(JObject level, int id, GobnomLockstep net, double intro)
        {
            Track = GobnomData.TrackCoop;
            LevelId = id;
            Level = level;
            Net = net;
            Run = net.State;
            NetWaiting = false;
            _serverOutcome = null;
            _serverWait = 0.0;
            _accum = 0.0;
            LevelIntroLeft = intro;
            _plan = GobnomSolver.SortedActions(level);
            _planNext = 0;
            SetPhase(PhasePlay);
        }

        /// <summary>The server booked the coop result (GOBNOM_RESULT): kept as the fallback for a sim that cannot reach the end.</summary>
        public void ApplyServerResult(string outcome, int jars)
        {
            if (!OnlineRun || Phase != PhasePlay) return;
            _serverOutcome = outcome == "won" ? "won" : "lost";
            _serverJars = Math.Max(0, Math.Min(3, jars));
            _serverWait = 0.0;
        }

        /// <summary>Desync, abort or an expired session: a running online round ends politely (back to the select); a finished one keeps its card.</summary>
        public void AbortOnline()
        {
            if (OnlineRun && Phase == PhasePlay) BackToSelect();
        }

        public void Retry() { if (Level != null && !OnlineRun) Open(Track, LevelId); }

        /// <summary>The next level of the track, back to the select after the last one (and after every online round).</summary>
        public void Next()
        {
            var count = GobnomProgress.LevelCount(Track);
            if (!OnlineRun && LevelId < count && IsUnlocked(Track, LevelId + 1)) Open(Track, LevelId + 1);
            else BackToSelect();
        }

        public void BackToSelect()
        {
            Run = null;
            Level = null;
            Net = null;
            NetWaiting = false;
            _serverOutcome = null;
            SetPhase(PhaseSelect);
        }

        /// <summary>"Fertig": ends the session — the frame books the round with the session score.</summary>
        public void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = SessionScore;
            Run = null;
            Net = null;
            NetWaiting = false;
            Finished_?.Invoke();
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            PhaseTime += dt;
            if (Phase != PhasePlay || Run == null) return;
            if (LevelIntroLeft > 0.0)
            {
                LevelIntroLeft = Math.Max(0.0, LevelIntroLeft - Math.Min(dt, 0.25));
                return;
            }
            _accum += Math.Min(dt, 0.25);
            if (OnlineRun)
            {
                PumpNet();
                _botOn = false;
                // a desync handler may have sent us back to the select from inside the pump
                if (Run == null || Phase != PhasePlay) return;
                if (_serverOutcome != null && !GobnomLogic.IsOver(Run))
                {
                    _serverWait = NetWaiting ? _serverWait + Math.Min(dt, 0.25) : 0.0;
                    if (_serverWait >= ServerResultGraceSec) EndRun(_serverOutcome == "won", _serverJars);
                    return;
                }
            }
            else
            {
                while (_accum >= TickSec && !GobnomLogic.IsOver(Run))
                {
                    _accum -= TickSec;
                    if (_botOn) RunPlan();
                    Emit(GobnomLogic.Step(Run), 0);
                }
                _botOn = false;
            }
            if (GobnomLogic.IsOver(Run)) OnRunOver();
        }

        /// <summary>Wall-clock ticks into the lockstep (the sim only computes on the partner's fence), due frames/hashes out.</summary>
        private void PumpNet()
        {
            var stalled = false;
            while (_accum >= TickSec && !GobnomLogic.IsOver(Run))
            {
                _accum -= TickSec;
                if (_botOn) PlanBotActions();
                var (stepped, events) = Net.Advance();
                if (stepped) Emit(events, 0);
                else stalled = true;
            }
            NetWaiting = stalled && !GobnomLogic.IsOver(Run);
            // catch-up cap: waiting must not pile up a time debt
            _accum = Math.Min(_accum, TickSec * 8.0);
            if (Net.Desynced)
            {
                NetDesync?.Invoke(Net.DesyncTick);
                return;
            }
            // the last pump of a finished run forces a fence past the end tick, so the partner can always step to it too
            var frame = Net.TakeFrame(GobnomLogic.IsOver(Run));
            if (frame != null) FrameOut?.Invoke(frame);
            foreach (var (t, h) in Net.TakeHashes()) HashOut?.Invoke(t, h);
        }

        /// <summary>Jars of the running level as live points (the rest arrives with the win).</summary>
        public int LiveScore => SessionScore + (Phase == PhasePlay && Run != null ? Run.JarsTaken * GobnomLogic.Int(Balance["score"] as JObject, "jar_bonus", 25) : 0);

        // ── input (the view converts screen → world and calls these) ──

        public bool AcceptsInput => Phase == PhasePlay && Run != null && LevelIntroLeft <= 0.0 && !GobnomLogic.IsOver(Run) && (Net == null || !Net.Desynced);

        /// <summary>Coop: every touch acts as the player of its half; online every touch is ours; solo levels need no gate.</summary>
        public string PlayerFor(V2 world) =>
            OnlineRun ? Net.Side : Run != null && Run.Coop ? GobnomLogic.SideOf(Run, world) : GobnomLogic.PlayerSolo;

        /// <summary>Online only the own half takes touches (the sim would refuse foreign elements on both devices anyway).</summary>
        public bool IsOwnSide(V2 world) => !OnlineRun || Run == null || !Run.Coop || GobnomLogic.SideOf(Run, world) == Net.Side;

        public List<int> Swipe(V2 from, V2 to, string player)
        {
            if (!AcceptsInput) return new List<int>();
            if (OnlineRun)
            {
                // cuts travel as planned actions: the rope only falls at tick now + delay, on both devices
                var planned = new List<int>();
                foreach (var id in Net.RopesCrossed(from, to))
                    if (!Net.IsPlanned("cut", id) && Net.Schedule("cut", id) != null) planned.Add(id);
                return planned;
            }
            var before = Run.Events.Count;
            var cut = GobnomLogic.CutSegment(Run, from, to, player);
            Emit(Run.Events, before);
            return cut;
        }

        /// <summary>Tap on a bubble (pop), cushion (puff) or toggleable fan; true when something was under the finger.</summary>
        public bool Tap(V2 world, string player)
        {
            if (!AcceptsInput) return false;
            var before = Run.Events.Count;
            var hit = TapElement(world, player);
            if (!OnlineRun) Emit(Run.Events, before);
            return hit;
        }

        private bool TapElement(V2 world, string player)
        {
            foreach (var bubble in Run.Bubbles)
            {
                if (bubble.Popped) continue;
                var at = bubble.Holds ? Run.CandyPos : bubble.Pos;
                if (world.DistanceTo(at) <= bubble.R + 18.0) { Act("pop", bubble.Id, player); return true; }
            }
            foreach (var cushion in Run.Cushions)
                if (world.DistanceTo(cushion.Pos) <= TapRadius) { Act("puff", cushion.Id, player); return true; }
            foreach (var fan in Run.Fans)
                if (fan.Toggleable && world.DistanceTo(fan.Pos) <= TapRadius) { Act("fan", fan.Id, player); return true; }
            return false;
        }

        /// <summary>An element action: straight into the sim, online as a planned lockstep action.</summary>
        private void Act(string kind, int id, string player)
        {
            if (OnlineRun) { Net.Schedule(kind, id); return; }
            switch (kind)
            {
                case "pop": GobnomLogic.PopBubble(Run, id, player); break;
                case "puff": GobnomLogic.PuffCushion(Run, id, player); break;
                case "fan": GobnomLogic.ToggleFan(Run, id, player); break;
            }
        }

        /// <summary>Rope id of a grabbable rail anchor under the point (-1 = none).</summary>
        public int RailAnchorAt(V2 world)
        {
            if (Run == null) return -1;
            foreach (var rope in Run.Ropes)
                if (!rope.Cut && rope.Rail != null && world.DistanceTo(rope.Anchor) <= 30.0) return rope.Id;
            return -1;
        }

        /// <summary>Rail parameter t (0..1) of a world point (projection onto the rail).</summary>
        public double RailTFor(int ropeId, V2 world)
        {
            var rope = Run != null ? GobnomLogic.RopeById(Run, ropeId) : null;
            if (rope?.Rail == null) return 0.0;
            var span = rope.Rail.To - rope.Rail.From;
            if (span.LengthSquared < 0.001f) return 0.0;
            return Math.Min(Math.Max((double)((world - rope.Rail.From).Dot(span) / span.LengthSquared), 0.0), 1.0);
        }

        public void Slide(int ropeId, double t, string player)
        {
            if (!AcceptsInput) return;
            if (OnlineRun)
            {
                Net.Schedule("slide", ropeId, t);
                return;
            }
            var before = Run.Events.Count;
            GobnomLogic.MoveAnchor(Run, ropeId, t, player);
            Emit(Run.Events, before);
        }

        // ── QA / tests: the certified plan plays (select → next open level, end overlays move on after a beat) ──

        public void ApplyBot()
        {
            if (Finished) return;
            switch (Phase)
            {
                case PhaseSelect:
                    if (SelectBot != null && SelectBot()) break;
                    Open(Track, GobnomProgress.MaxUnlocked(Save, Track));
                    break;
                case PhasePlay:
                    _botOn = true;
                    break;
                // an online round's card waits for the players: another round is a fresh invite
                case PhaseWon:
                    if (!OnlineRun && PhaseTime >= 1.5) Next();
                    break;
                case PhaseLost:
                    if (!OnlineRun && PhaseTime >= 1.5) Retry();
                    break;
            }
        }

        private void RunPlan()
        {
            var now = (double)Run.Tick / GobnomLogic.Tps;
            while (_planNext < _plan.Count && GobnomLogic.Num(_plan[_planNext], "t", 0.0) <= now)
            {
                var before = Run.Events.Count;
                GobnomSolver.Execute(Run, _plan[_planNext]);
                Emit(Run.Events, before);
                _planNext += 1;
            }
        }

        /// <summary>Online bot: the own side's plan actions, each planned so that it runs exactly on its plan tick.</summary>
        private void PlanBotActions()
        {
            while (_planNext < _plan.Count)
            {
                var action = _plan[_planNext];
                if (GobnomSolver.PlanTick(action) > Net.Clock + Net.InputDelay) break;
                _planNext += 1;
                if (GobnomLogic.Str(action, "player", "") != Net.Side) continue;
                var (kind, id, v) = GobnomSolver.ToLockstep(action);
                if (id >= 0) Net.Schedule(kind, id, v);
            }
        }

        private void OnRunOver()
        {
            if (Phase != PhasePlay) return;
            EndRun(Run.Outcome == "won", Run.JarsTaken);
        }

        private void EndRun(bool won, int jars)
        {
            if (won)
            {
                LastWon = true;
                LastStars = GobnomLogic.StarsFor(jars);
                LastFirstClear = !GobnomProgress.IsCleared(Save, Track, LevelId);
                LastTotal = GobnomProgress.FinalScore(LevelId, jars, LastFirstClear, Balance);
                if (_store != null) GobnomProgress.RecordWin(_store, Track, LevelId, LastStars, LastTotal);
                else GobnomProgress.RecordWin(_localSave, Track, LevelId, LastStars, LastTotal);
                SessionScore += LastTotal;
                SetPhase(PhaseWon);
            }
            else
            {
                LastWon = false;
                LastStars = 0;
                LastTotal = 0;
                LastFirstClear = false;
                SetPhase(PhaseLost);
            }
        }

        private void SetPhase(string phase)
        {
            Phase = phase;
            PhaseTime = 0.0;
            PhaseChanged?.Invoke(phase);
        }

        private void Emit(List<GobnomEvent> events, int from)
        {
            if (Event == null) return;
            for (var i = from; i < events.Count; i++) Event(events[i]);
        }
    }
}
