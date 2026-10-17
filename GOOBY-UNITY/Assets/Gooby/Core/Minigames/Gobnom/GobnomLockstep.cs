using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gobnom
{
    /// <summary>
    /// The lockstep core of GOB NOM's online coop (gobnom_lockstep.gd) — pure: both clients simulate
    /// <see cref="GobnomLogic"/> identically from the server seed and the input stream; only actions travel
    /// ({t, do, id, v?}, the format of solution.actions). Local actions are planned at tick now + input delay and run on
    /// BOTH devices exactly there; fences ("my inputs up to tick X are complete" = upTo) flow on the wall clock so two
    /// waiting clients never block each other; the sim only steps while the partner confirmed the tick. Every
    /// <see cref="HashTicks"/> ticks the state hash is compared — a mismatch sets <see cref="Desynced"/>.
    /// </summary>
    public sealed class GobnomLockstep
    {
        public const int InputDelayDefault = 4, FenceTicks = 6, HashTicksDefault = 60, HashKeep = 10;

        public GobnomRun State { get; private set; }
        public string Side { get; private set; } = GobnomLogic.PlayerA;
        public int InputDelay { get; private set; } = InputDelayDefault;
        public int HashTicks { get; private set; } = HashTicksDefault;
        /// <summary>Up to which tick (inclusive) the partner confirmed its inputs.</summary>
        public int PeerConfirmed { get; private set; } = -1;
        public bool Desynced { get; private set; }
        public int DesyncTick { get; private set; } = -1;
        public int Clock => _clock;
        /// <summary>Up to which tick (inclusive) our own inputs are confirmed on the wire.</summary>
        public int SentUpTo => _sentUpTo;

        private int _clock, _sentUpTo = -100, _frameN, _peerFrameN;
        private readonly SortedDictionary<int, (List<JObject> a, List<JObject> b)> _queued = new SortedDictionary<int, (List<JObject>, List<JObject>)>();
        private readonly List<JObject> _pendingOut = new List<JObject>();
        private readonly Dictionary<int, string> _ownHashes = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _peerHashes = new Dictionary<int, string>();
        private readonly List<(int t, string h)> _hashOut = new List<(int, string)>();
        private bool _quiet;

        /// <summary>A fresh online run: level + balance like locally, the seed FROM THE SERVER.</summary>
        public void Start(JObject level, JObject balance, long seed, string side, int delay = InputDelayDefault, int hashTicks = HashTicksDefault)
        {
            State = GobnomLogic.NewRun(level, balance, seed);
            Side = side == GobnomLogic.PlayerB ? GobnomLogic.PlayerB : GobnomLogic.PlayerA;
            InputDelay = Math.Max(1, delay);
            HashTicks = Math.Max(1, hashTicks);
            PeerConfirmed = -1;
            Desynced = false;
            DesyncTick = -1;
            _clock = 0;
            _sentUpTo = -100;
            _frameN = 0;
            _peerFrameN = 0;
            _queued.Clear();
            _pendingOut.Clear();
            _ownHashes.Clear();
            _peerHashes.Clear();
            _hashOut.Clear();
        }

        public string OtherSide => Side == GobnomLogic.PlayerB ? GobnomLogic.PlayerA : GobnomLogic.PlayerB;

        public bool IsOver => State != null && GobnomLogic.IsOver(State);

        /// <summary>May the sim compute the next tick? (The partner's fence covers it.)</summary>
        public bool CanStep => State != null && !Desynced && !IsOver && PeerConfirmed >= State.Tick;

        /// <summary>One wall-clock tick of the view: advances the fence clock and — when allowed — exactly one sim tick.</summary>
        public (bool stepped, List<GobnomEvent> events) Advance()
        {
            if (State == null || Desynced || IsOver) return (false, new List<GobnomEvent>());
            _clock += 1;
            if (!CanStep) return (false, new List<GobnomEvent>());
            State.Events = new List<GobnomEvent>();
            ApplyDue();
            var events = new List<GobnomEvent>(State.Events);
            events.AddRange(GobnomLogic.Step(State));
            if (State.Tick % HashTicks == 0) RecordOwnHash(State.Tick);
            return (true, events);
        }

        /// <summary>
        /// Plans a local action (do: cut | pop | puff | fan | slide). It does NOT run now but deterministically on both
        /// devices at tick t — the sim's gates (other side, cut budget) decide only when it executes.
        /// </summary>
        public JObject Schedule(string doKind, int id, double? v = null)
        {
            if (State == null || Desynced || IsOver) return null;
            var t = Math.Max(Math.Max(_clock, State.Tick) + InputDelay, _sentUpTo + 1);
            if (doKind == "slide")
                foreach (var pending in _pendingOut)
                    if ((string)pending["do"] == "slide" && (int)pending["id"] == id && (int)pending["t"] == t)
                    {
                        pending["v"] = v ?? 0.0;
                        return pending;
                    }
            var action = new JObject { ["t"] = t, ["do"] = doKind, ["id"] = id };
            if (v.HasValue) action["v"] = v.Value;
            _pendingOut.Add(action);
            Enqueue(action, Side);
            return action;
        }

        /// <summary>Is an own action of this kind for this element still waiting for its tick? (A swipe that crosses a rope
        /// again within the input delay must not plan the same cut twice.)</summary>
        public bool IsPlanned(string doKind, int id)
        {
            foreach (var buckets in _queued.Values)
                foreach (var a in Side == GobnomLogic.PlayerB ? buckets.b : buckets.a)
                    if ((string)a["do"] == doKind && Num(a["id"], -1) == id) return true;
            return false;
        }

        /// <summary>The next input frame for the wire (null = nothing due): n (strictly increasing), upTo (fence), actions.</summary>
        public JObject TakeFrame(bool force = false)
        {
            if (State == null || Desynced) return null;
            var target = Math.Max(_clock + InputDelay - 1, 0);
            if (!force && _pendingOut.Count == 0 && target - _sentUpTo < FenceTicks) return null;
            _frameN += 1;
            var actions = new JArray();
            foreach (var a in _pendingOut) actions.Add(a.DeepClone());
            var frame = new JObject { ["n"] = _frameN, ["upTo"] = Math.Max(target, _sentUpTo), ["a"] = actions };
            _sentUpTo = (int)frame["upTo"];
            _pendingOut.Clear();
            return frame;
        }

        /// <summary>Own hash reports that are due ({t, h}).</summary>
        public List<(int t, string h)> TakeHashes()
        {
            var o = new List<(int, string)>(_hashOut);
            _hashOut.Clear();
            return o;
        }

        /// <summary>Feeds a partner frame (from GN_INPUT); false = old/duplicate.</summary>
        public bool ReceiveFrame(JObject body)
        {
            if (body == null) return false;
            var n = Num(body["n"], 0);
            if (n <= _peerFrameN) return false;
            _peerFrameN = n;
            PeerConfirmed = Math.Max(PeerConfirmed, Num(body["upTo"], -1));
            if (body["a"] is JArray arr)
                foreach (var raw in arr)
                {
                    if (!(raw is JObject a)) continue;
                    var action = (JObject)a.DeepClone();
                    // late actions (t already simulated) break the fence contract — dropped; the hash guard catches real drift
                    if (State == null || Num(action["t"], -1) < State.Tick) continue;
                    Enqueue(action, OtherSide);
                }
            return true;
        }

        /// <summary>Feeds a partner hash (from GN_HASH) → desync comparison.</summary>
        public void ReceiveHash(int tick, string hash)
        {
            if (Desynced) return;
            if (_ownHashes.TryGetValue(tick, out var own))
            {
                if (own != hash) MarkDesync(tick);
                return;
            }
            _peerHashes[tick] = hash;
        }

        /// <summary>Rejoin replay: the identical run from seed + both sides' complete frame buffers, fast-forwarded as far as both fences allow.</summary>
        public void Resume(JObject level, JObject balance, long seed, string side, JObject frames, int delay = InputDelayDefault, int hashTicks = HashTicksDefault)
        {
            Start(level, balance, seed, side, delay, hashTicks);
            _quiet = true;
            var ownUp = -1;
            if (frames?[Side] is JArray own)
                foreach (var raw in own)
                {
                    if (!(raw is JObject frame)) continue;
                    _frameN = Math.Max(_frameN, Num(frame["n"], 0));
                    ownUp = Math.Max(ownUp, Num(frame["upTo"], -1));
                    if (frame["a"] is JArray acts)
                        foreach (var entry in acts)
                            if (entry is JObject e) Enqueue((JObject)e.DeepClone(), Side);
                }
            if (frames?[OtherSide] is JArray theirs)
                foreach (var raw in theirs)
                    if (raw is JObject f) ReceiveFrame(f);
            _sentUpTo = ownUp;
            var limit = Math.Min(ownUp, PeerConfirmed);
            while (State.Tick <= limit && !GobnomLogic.IsOver(State))
            {
                State.Events = new List<GobnomEvent>();
                ApplyDue();
                GobnomLogic.Step(State);
                if (State.Tick % HashTicks == 0) RecordOwnHash(State.Tick);
            }
            _clock = Math.Max(State.Tick, ownUp - InputDelay + 1);
            _quiet = false;
        }

        /// <summary>Which uncut ropes does the swipe segment cross? (Like CutSegment but WITHOUT mutation — cuts travel as planned actions.)</summary>
        public List<int> RopesCrossed(V2 from, V2 to)
        {
            var o = new List<int>();
            if (State == null || from.IsEqualApprox(to)) return o;
            foreach (var rope in State.Ropes)
            {
                if (rope.Cut) continue;
                if (GodotMath.SegmentsIntersect(from, to, rope.Anchor, State.CandyPos)) o.Add(rope.Id);
            }
            return o;
        }

        private void Enqueue(JObject action, string fromSide)
        {
            var t = Num(action["t"], -1);
            if (t < 0) return;
            if (!_queued.TryGetValue(t, out var buckets))
            {
                buckets = (new List<JObject>(), new List<JObject>());
                _queued[t] = buckets;
            }
            (fromSide == GobnomLogic.PlayerB ? buckets.b : buckets.a).Add(action);
        }

        /// <summary>The due tick's actions in a DETERMINISTIC order: side a first, then b; within a side in send order.</summary>
        private void ApplyDue()
        {
            var t = State.Tick;
            if (!_queued.TryGetValue(t, out var buckets)) return;
            foreach (var a in buckets.a) Dispatch(a, GobnomLogic.PlayerA);
            foreach (var b in buckets.b) Dispatch(b, GobnomLogic.PlayerB);
            _queued.Remove(t);
        }

        private void Dispatch(JObject action, string fromSide)
        {
            var id = Num(action["id"], -1);
            switch ((string)action["do"] ?? "")
            {
                case "cut": GobnomLogic.CutRope(State, id, fromSide); break;
                case "pop": GobnomLogic.PopBubble(State, id, fromSide); break;
                case "puff": GobnomLogic.PuffCushion(State, id, fromSide); break;
                case "fan": GobnomLogic.ToggleFan(State, id, fromSide); break;
                case "slide": GobnomLogic.MoveAnchor(State, id, action["v"] != null ? (double)action["v"] : 0.0, fromSide); break;
            }
        }

        private void RecordOwnHash(int tick)
        {
            var hash = GobnomLogic.StateHash(State);
            _ownHashes[tick] = hash;
            _ownHashes.Remove(tick - HashKeep * HashTicks);
            if (!_quiet) _hashOut.Add((tick, hash));
            if (_peerHashes.TryGetValue(tick, out var peer))
            {
                if (peer != hash) MarkDesync(tick);
                _peerHashes.Remove(tick);
            }
        }

        private void MarkDesync(int tick)
        {
            Desynced = true;
            DesyncTick = tick;
        }

        private static int Num(JToken t, int fallback) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (int)t.Value<double>() : fallback;
    }
}
