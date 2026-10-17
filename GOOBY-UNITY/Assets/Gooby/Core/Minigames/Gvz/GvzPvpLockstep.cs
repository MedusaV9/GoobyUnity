using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// The lockstep core of GvZ's online PvP (gvz_pvp_lockstep.gd, template GobnomLockstep) — pure: both clients simulate
    /// <see cref="GvzLogic"/> identically from the server seed and the input stream; only actions travel ({t, do, …}).
    /// The sim is only CALLED: the Gooby side plans place/shovel/collect, the zombie side summons through
    /// <see cref="GvzZombies.Spawn"/>. The PvP economy (mud drip + escalation, cost/cooldown per zombie type) and the survive
    /// timer live here and come from gvz_pvp.json. Local actions run on BOTH devices at tick now + input delay; fences flow
    /// on the wall clock; the sim only steps while the partner confirmed the tick; every <see cref="HashTicks"/> ticks the
    /// match hash (sim hash + mud + cooldowns + winner) is compared.
    /// </summary>
    public sealed class GvzPvpLockstep
    {
        public const string SideGooby = "gooby", SideZombie = "zombie";
        public const int InputDelayDefault = 4, FenceTicks = 6, HashTicksDefault = 60, HashKeep = 10;

        public GvzRun State { get; private set; }
        public string Side { get; private set; } = SideGooby;
        public int InputDelay { get; private set; } = InputDelayDefault;
        public int HashTicks { get; private set; } = HashTicksDefault;
        /// <summary>The zombie side's PvP resource (Doc G §4.5) — part of the drift hash.</summary>
        public int Matsch { get; private set; }
        /// <summary>Gooby wins once the sim reaches this tick (match.gooby_survive_sec × ticks per second).</summary>
        public int SurviveTicks { get; private set; } = 4200;
        /// <summary>"" while the match runs, else <see cref="SideGooby"/> | <see cref="SideZombie"/>.</summary>
        public string Winner { get; private set; } = "";
        public int PeerConfirmed { get; private set; } = -1;
        public bool Desynced { get; private set; }
        public int DesyncTick { get; private set; } = -1;
        public int Clock => _clock;
        public int SentUpTo => _sentUpTo;
        public JObject Pvp => _pvp;

        private JObject _pvp = new JObject();
        private readonly Dictionary<string, int> _zombieCooldowns = new Dictionary<string, int>();
        private int _clock, _sentUpTo = -100, _frameN, _peerFrameN;
        private readonly SortedDictionary<int, (List<JObject> gooby, List<JObject> zombie)> _queued = new SortedDictionary<int, (List<JObject>, List<JObject>)>();
        private readonly List<JObject> _pendingOut = new List<JObject>();
        private readonly Dictionary<int, string> _ownHashes = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _peerHashes = new Dictionary<int, string>();
        private readonly List<(int t, string h)> _hashOut = new List<(int, string)>();

        /// <summary>
        /// The PvP arena: all five lanes, every standard tower (no code gate), NO scripted waves or bosses. A guard spawn in the
        /// far future keeps the sim's outcome check open (an empty spawn plan would be an instant win in the first tick) —
        /// the match ends before, deterministically, by the survive timer or a breakthrough.
        /// </summary>
        public static JObject ArenaLevel(JObject balance)
        {
            var towers = new JArray();
            if (balance?["towers"] is JObject rows)
                foreach (var p in rows.Properties())
                    if (!GvzJson.B(p.Value as JObject, "code_gate", false)) towers.Add(p.Name);
            return new JObject
            {
                ["id"] = 0,
                ["lanes"] = new JArray(0, 1, 2, 3, 4),
                ["unlock_towers"] = towers,
                ["spawns"] = new JArray(new JObject { ["t"] = 999000.0, ["lane"] = 0, ["type"] = "schlurfi" }),
                ["waves"] = new JArray(),
                ["mods"] = new JObject(),
            };
        }

        /// <summary>A fresh online match: balance/PvP rules like locally, the seed FROM THE SERVER; difficulty is always "normal".</summary>
        public void Start(JObject balance, JObject pvp, long seed, string side, int delay = InputDelayDefault, int hashTicks = HashTicksDefault)
        {
            _pvp = pvp ?? new JObject();
            State = GvzLogic.NewRun(ArenaLevel(balance), balance, "normal", seed);
            // Doc G §4.5: no mowers in PvP — marked as spent (the identical setup mutation on both devices)
            if (!GvzJson.B(_pvp["match"] as JObject, "mowers_enabled", false))
                foreach (var lane in State.Mowers.Keys) State.Mowers[lane].Used = true;
            Side = side == SideZombie ? SideZombie : SideGooby;
            InputDelay = Math.Max(1, delay);
            HashTicks = Math.Max(1, hashTicks);
            Matsch = GvzJson.I(_pvp["matsch"] as JObject, "start", 0);
            _zombieCooldowns.Clear();
            SurviveTicks = GvzJson.I(_pvp["match"] as JObject, "gooby_survive_sec", 210) * Tps;
            Winner = "";
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

        public string OtherSide => Side == SideGooby ? SideZombie : SideGooby;
        public bool IsMatchOver => Winner.Length > 0;
        public int Tps => State != null ? GvzLogic.Tps(State) : 20;

        /// <summary>May the sim compute the next tick? (The partner's fence covers it.)</summary>
        public bool CanStep => State != null && !Desynced && !IsMatchOver && PeerConfirmed >= State.Tick;

        /// <summary>One wall-clock tick of the view: advances the fence clock and — when allowed — exactly one sim tick.</summary>
        public (bool stepped, List<GvzEvent> events) Advance()
        {
            if (State == null || Desynced || IsMatchOver) return (false, new List<GvzEvent>());
            _clock += 1;
            if (!CanStep) return (false, new List<GvzEvent>());
            State.Events = new List<GvzEvent>();
            ApplyDue();
            // GvzLogic.Tick starts a fresh event list: keep the actions' events first
            var events = new List<GvzEvent>(State.Events);
            events.AddRange(GvzLogic.Tick(State));
            MatschTick();
            CheckWinner();
            if (State.Tick % HashTicks == 0) RecordOwnHash(State.Tick);
            return (true, events);
        }

        // ── planning (runs deterministically at tick t on BOTH sims; the sim gates decide at execution) ──

        public JObject SchedulePlace(string type, int lane, int col) => Schedule(new JObject { ["do"] = "place", ["type"] = type, ["lane"] = lane, ["col"] = col });
        public JObject ScheduleShovel(int lane, int col) => Schedule(new JObject { ["do"] = "shovel", ["lane"] = lane, ["col"] = col });
        public JObject ScheduleCollect(int dropId) => Schedule(new JObject { ["do"] = "collect", ["id"] = dropId });
        public JObject ScheduleSpawn(string type, int lane) => Schedule(new JObject { ["do"] = "spawn", ["type"] = type, ["lane"] = lane });

        /// <summary>Is an own collect for this drop still waiting for its tick? (Taps on a blob within the delay plan it once.)</summary>
        public bool IsCollectPlanned(int dropId)
        {
            foreach (var buckets in _queued.Values)
                foreach (var a in Side == SideZombie ? buckets.zombie : buckets.gooby)
                    if ((string)a["do"] == "collect" && Num(a["id"], -1) == dropId) return true;
            return false;
        }

        // ── zombie side: mud costs and cooldowns ──

        /// <summary>Summonable zombie types in data order (light ones first).</summary>
        public List<string> ZombieTypes() => (_pvp["zombie_costs"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();

        public int ZombieCost(string type) => GvzJson.I(_pvp["zombie_costs"] as JObject, type, 0);

        public int ZombieCooldownTicks(string type) => GvzJson.I(_pvp["zombie_cooldown_ticks"] as JObject, type, 0);

        /// <summary>Summon cooldown left in ticks (0 = ready).</summary>
        public int ZombieCooldownLeft(string type) =>
            State == null ? 0 : Math.Max(0, (_zombieCooldowns.TryGetValue(type, out var ready) ? ready : 0) - State.Tick);

        /// <summary>Summon check WITHOUT side effects. Reasons: outcome | unknown_zombie | lane | cooldown | matsch.</summary>
        public GvzLogic.Check CanSpawn(string type, int lane)
        {
            if (State == null || IsMatchOver) return new GvzLogic.Check(false, "outcome");
            if ((_pvp["zombie_costs"] as JObject)?[type ?? ""] == null) return new GvzLogic.Check(false, "unknown_zombie");
            if (!State.Lanes.Contains(lane)) return new GvzLogic.Check(false, "lane");
            if (ZombieCooldownLeft(type) > 0) return new GvzLogic.Check(false, "cooldown");
            if (Matsch < ZombieCost(type)) return new GvzLogic.Check(false, "matsch");
            return new GvzLogic.Check(true, "");
        }

        /// <summary>Match time left in seconds (Gooby wins at 0).</summary>
        public int SecondsLeft() => State == null ? 0 : Math.Max(0, (SurviveTicks - State.Tick + Tps - 1) / Tps);

        // ── wire channels (frames/hashes — exactly the GOB NOM pattern) ──

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

        public List<(int t, string h)> TakeHashes()
        {
            var o = new List<(int, string)>(_hashOut);
            _hashOut.Clear();
            return o;
        }

        /// <summary>Feeds a partner frame (GP_INPUT); false = old/duplicate.</summary>
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
                    // late actions (t already simulated) break the fence contract — dropped; the hash guard catches real drift
                    if (State == null || Num(a["t"], -1) < State.Tick) continue;
                    Enqueue((JObject)a.DeepClone(), OtherSide);
                }
            return true;
        }

        /// <summary>Feeds a partner hash (GP_HASH) → desync comparison.</summary>
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

        /// <summary>The drift hash of the whole match: sim hash + mud + summon cooldowns + winner (FNV-1a, 16 hex — the server takes ≤ 64).</summary>
        public string StateHashText()
        {
            var sb = new StringBuilder(64);
            sb.Append(GvzLogic.StateHash(State)).Append('|').Append(Matsch).Append('|');
            foreach (var kv in _zombieCooldowns.OrderBy(kv => kv.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            sb.Append('|').Append(Winner);
            ulong h = 14695981039346656037UL;
            foreach (var ch in sb.ToString()) { h ^= ch; h *= 1099511628211UL; }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        // ── internals ──

        private JObject Schedule(JObject action)
        {
            if (State == null || Desynced || IsMatchOver) return null;
            action["t"] = Math.Max(Math.Max(_clock, State.Tick) + InputDelay, _sentUpTo + 1);
            _pendingOut.Add(action);
            Enqueue(action, Side);
            return action;
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
            (fromSide == SideZombie ? buckets.zombie : buckets.gooby).Add(action);
        }

        /// <summary>The due tick's actions in a DETERMINISTIC order: Gooby side first, then the zombies; within a side in send order.</summary>
        private void ApplyDue()
        {
            var t = State.Tick;
            if (!_queued.TryGetValue(t, out var buckets)) return;
            foreach (var a in buckets.gooby) Dispatch(a, SideGooby);
            foreach (var z in buckets.zombie) Dispatch(z, SideZombie);
            _queued.Remove(t);
        }

        /// <summary>The side gate lives in the dispatch: a foreign side's actions are dropped — identically on both devices.</summary>
        private void Dispatch(JObject action, string fromSide)
        {
            switch ((string)action["do"] ?? "")
            {
                case "place":
                    if (fromSide == SideGooby) GvzLogic.PlaceTower(State, (string)action["type"] ?? "", Num(action["lane"], -1), Num(action["col"], -1));
                    break;
                case "shovel":
                    if (fromSide == SideGooby) GvzLogic.RemoveTower(State, Num(action["lane"], -1), Num(action["col"], -1));
                    break;
                case "collect":
                    if (fromSide == SideGooby) GvzLogic.CollectDrop(State, Num(action["id"], -1));
                    break;
                case "spawn":
                    if (fromSide == SideZombie) SpawnZombie((string)action["type"] ?? "", Num(action["lane"], -1));
                    break;
            }
        }

        private void SpawnZombie(string type, int lane)
        {
            if (!CanSpawn(type, lane).Ok) return;
            Matsch -= ZombieCost(type);
            _zombieCooldowns[type] = State.Tick + ZombieCooldownTicks(type);
            GvzZombies.Spawn(State, type, lane, GvzLogic.SpawnX);
        }

        /// <summary>Mud drip (Doc G §4.5): 15 every 10 s, +5 escalation every 45 s — tick-based, so both sims book the same amounts.</summary>
        private void MatschTick()
        {
            var rules = _pvp["matsch"] as JObject;
            var interval = Math.Max(1, GvzJson.I(rules, "drip_interval_sec", 10) * Tps);
            var tick = State.Tick;
            if (tick % interval != 0) return;
            var escInterval = Math.Max(1, GvzJson.I(rules, "escalation_interval_sec", 45) * Tps);
            Matsch += GvzJson.I(rules, "drip_amount", 15);
            Matsch += GvzJson.I(rules, "escalation_bonus", 5) * (tick / escInterval);
        }

        /// <summary>After every tick: a breakthrough (sim "lost") wins for the zombies, the expired timer for the Goobys.</summary>
        private void CheckWinner()
        {
            if (Winner.Length > 0) return;
            if (State.Outcome == "lost") Winner = SideZombie;
            else if (State.Outcome == "won" || State.Tick >= SurviveTicks) Winner = SideGooby;
        }

        private void RecordOwnHash(int tick)
        {
            var hash = StateHashText();
            _ownHashes[tick] = hash;
            _ownHashes.Remove(tick - HashKeep * HashTicks);
            _hashOut.Add((tick, hash));
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
