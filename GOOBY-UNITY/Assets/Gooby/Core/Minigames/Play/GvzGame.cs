using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Minigames.Gvz;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Goobys vs Zombies session — the flow of gvz_game.gd around the pure <see cref="GvzLogic"/>: level select → battle
    /// → won/lost → next/retry/select until "Fertig" (one arcade round = one session; its score = the sum of the level
    /// wins via <see cref="GvzProgress.FinalScore"/>). 20-Hz fixed tick off wall-clock slices; the sim already runs
    /// during the 1.5-s intro (the first wave comes late). Cards: the level's towers (or the conveyor queue) plus the
    /// shovel; tap a card, then a cell. Blobs are collected by tapping. Wins book the "gvz" slice; every finished run
    /// books the sticker counters (gvzNutella, gvzEisEinsaetze, gvzBertEinsaetze, gvzMoehrenSchuesse, gvzZombiesGestoppt)
    /// and level 5/10/15 wins fire their sticker hooks.
    /// Online PvP (the netz half of gvz_game.gd): <see cref="StartOnline"/> builds the arena from the server seed, the sim
    /// steps through a <see cref="GvzPvpLockstep"/> only on the partner's fence; the Goobys plant/shovel/collect, the
    /// zombies summon with mud — every action is planned, frames/hashes leave through <see cref="FrameOut"/>/<see cref="HashOut"/>.
    /// A match books no campaign progress and no stickers (half of the actions are the partner's); its small coin reward
    /// comes from the server with GVZ_RESULT (<see cref="GvzProgress.BookNetResult(JObject, string, bool, int)"/>).
    /// </summary>
    public sealed class GvzGame
    {
        public const double TickSec = 0.05;
        public const double IntroSec = 1.5;
        public const string PhaseSelect = "select", PhasePlay = "play", PhaseWon = "won", PhaseLost = "lost";
        public const string Shovel = "shovel";

        private static readonly Dictionary<string, string> StatCounters = new Dictionary<string, string>
        {
            ["drops_collected"] = "gvzNutella", ["eis_placed"] = "gvzEisEinsaetze", ["bert_placed"] = "gvzBertEinsaetze", ["moehren_shots"] = "gvzMoehrenSchuesse",
        };
        private static readonly Dictionary<int, string> MilestoneHooks = new Dictionary<int, string> { [5] = "gvz_l5", [10] = "gvz_l10", [15] = "gvz_kampagne" };

        public JObject Balance { get; }
        public List<JObject> Levels { get; }
        public string Difficulty { get; }
        public string Phase { get; private set; } = PhaseSelect;
        public int LevelId { get; private set; }
        public int Attempt { get; private set; }
        public GvzRun Run { get; private set; }
        public string SelectedCard { get; private set; } = "";
        public int SessionScore { get; private set; }
        /// <summary>Rest of the establishing intro (presentation only — the sim keeps running).</summary>
        public double IntroBeat { get; private set; }
        public double PhaseTime { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public bool LastWon { get; private set; }
        public int LastStars { get; private set; }
        public int LastTotal { get; private set; }
        public bool LastFirstClear { get; private set; }

        /// <summary>The lockstep of the current online match (null for the campaign).</summary>
        public GvzPvpLockstep Net { get; private set; }
        public bool OnlineRun => Net != null;
        /// <summary>We play the zombie side of the online match (summon cards, mud, no collecting).</summary>
        public bool NetZombie => Net != null && Net.Side == GvzPvpLockstep.SideZombie;
        /// <summary>The online sim waits for the partner's fence ("Warte auf Partner…").</summary>
        public bool NetWaiting { get; private set; }
        /// <summary>PvP rules override (tests: a short survive timer — both devices must use the same); null = gvz_pvp.json.</summary>
        public JObject PvpRules { get; set; }
        /// <summary>QA bot in the select: true = handled (the online lobby confirms the start instead of opening a level).</summary>
        public Func<bool> SelectBot { get; set; }

        public event Action<GvzEvent> Event;
        public event Action<string> PhaseChanged;
        public event Action Finished_;
        public event Action<JObject> FrameOut;
        public event Action<int, string> HashOut;
        public event Action<int> NetDesync;

        private readonly long _runSeed;
        private readonly GameStateStore _store;
        private readonly JObject _localSave = new JObject();
        private double _accum;
        private bool _botOn;
        private GvzPvpBot _pvpBot;

        public GvzGame(string difficulty, long runSeed, GameStateStore store = null)
        {
            Difficulty = difficulty == "easy" || difficulty == "hard" ? difficulty : "normal";
            _runSeed = runSeed;
            _store = store;
            Balance = GvzData.LoadBalance();
            Levels = GvzData.LoadLevels();
        }

        public JObject Save => _store?.State ?? _localSave;

        public bool IsUnlocked(int id) => id >= 1 && id <= GvzProgress.MaxUnlocked(Save);

        public bool Open(int id)
        {
            if (Finished) return false;
            var level = GvzData.LevelById(Levels, id);
            if (level == null) return false;
            LevelId = id;
            Attempt += 1;
            var seed = _runSeed + id * 1009L + Attempt * 131L;
            Net = null;
            NetWaiting = false;
            Run = GvzLogic.NewRun(level, Balance, Difficulty, seed, GvzProgress.GoldiUnlocked(Save));
            SelectedCard = "";
            _accum = 0.0;
            IntroBeat = IntroSec;
            SetPhase(PhasePlay);
            return true;
        }

        /// <summary>GVZ_START: the PvP arena from the SERVER seed on our side ("gooby" = inviter, "zombie" = guest); the first fence leaves at once.</summary>
        public bool StartOnline(long seed, string side, int delay = GvzPvpLockstep.InputDelayDefault, int hashTicks = GvzPvpLockstep.HashTicksDefault)
        {
            if (Finished) return false;
            Attempt += 1;
            var net = new GvzPvpLockstep();
            net.Start(Balance, PvpRules ?? GvzData.LoadPvp(), seed, side, delay, hashTicks);
            Net = net;
            Run = net.State;
            LevelId = 0;
            SelectedCard = "";
            NetWaiting = false;
            _pvpBot = new GvzPvpBot();
            _accum = 0.0;
            IntroBeat = IntroSec;
            SetPhase(PhasePlay);
            var first = net.TakeFrame(true);
            if (first != null) FrameOut?.Invoke(first);
            return true;
        }

        /// <summary>Desync, abort or an expired session: a running match ends politely (back to the select); a finished one keeps its card.</summary>
        public void AbortOnline()
        {
            if (OnlineRun && Phase == PhasePlay) BackToSelect();
        }

        /// <summary>Books a confirmed PvP result once (coins + win/loss; idempotent over the rewardId).</summary>
        public bool BookNetResult(string rewardId, bool won, int coins) =>
            _store != null ? GvzProgress.BookNetResult(_store, rewardId, won, coins) : GvzProgress.BookNetResult(_localSave, rewardId, won, coins);

        public void Retry() { if (LevelId > 0 && !OnlineRun) Open(LevelId); }

        public void Next()
        {
            if (!OnlineRun && LevelId < GvzData.LevelCount && IsUnlocked(LevelId + 1)) Open(LevelId + 1);
            else BackToSelect();
        }

        public void BackToSelect()
        {
            Run = null;
            Net = null;
            NetWaiting = false;
            SelectedCard = "";
            SetPhase(PhaseSelect);
        }

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
            if (IntroBeat > 0.0) IntroBeat = Math.Max(0.0, IntroBeat - Math.Min(dt, 0.25));
            _accum += Math.Min(dt, 0.25);
            if (OnlineRun)
            {
                PumpNet();
                _botOn = false;
                // a desync handler may have sent us back to the select from inside the pump
                if (Run == null || Phase != PhasePlay || Net == null) return;
                if (Net.IsMatchOver) OnNetOver();
                return;
            }
            while (_accum >= TickSec && !GvzLogic.IsOver(Run))
            {
                _accum -= TickSec;
                Emit(GvzLogic.Tick(Run), 0);
                if (_botOn && !GvzLogic.IsOver(Run))
                {
                    var before = Run.Events.Count;
                    GvzBot.Act(Run);
                    Emit(Run.Events, before);
                }
            }
            _botOn = false;
            if (GvzLogic.IsOver(Run)) OnRunOver();
        }

        /// <summary>Wall-clock ticks into the lockstep (the sim only computes on the partner's fence), due frames/hashes out.</summary>
        private void PumpNet()
        {
            var stalled = false;
            while (_accum >= TickSec && !Net.IsMatchOver)
            {
                _accum -= TickSec;
                if (_botOn) _pvpBot.Plan(Net);
                var (stepped, events) = Net.Advance();
                if (stepped) Emit(events, 0);
                else stalled = true;
            }
            NetWaiting = stalled && !Net.IsMatchOver;
            // catch-up cap: waiting must not pile up a time debt
            _accum = Math.Min(_accum, TickSec * 8.0);
            if (Net.Desynced)
            {
                NetDesync?.Invoke(Net.DesyncTick);
                return;
            }
            // the last pump of a finished match forces a fence past the end tick, so the partner can always step to it too
            var frame = Net.TakeFrame(Net.IsMatchOver);
            if (frame != null) FrameOut?.Invoke(frame);
            foreach (var (t, h) in Net.TakeHashes()) HashOut?.Invoke(t, h);
        }

        /// <summary>The winner comes from the lockstep (timer or breakthrough) — no campaign progress, no stickers.</summary>
        private void OnNetOver()
        {
            if (Phase != PhasePlay) return;
            LastWon = Net.Winner == Net.Side;
            LastStars = LastWon ? 3 : 0;
            LastTotal = 0;
            LastFirstClear = false;
            SetPhase(LastWon ? PhaseWon : PhaseLost);
        }

        /// <summary>Kill score of the running level as live points (the bonuses arrive with the win).</summary>
        public int LiveScore => SessionScore + (Phase == PhasePlay && Run != null && !OnlineRun ? Run.Score : 0);

        public bool AcceptsInput => Phase == PhasePlay && Run != null && !GvzLogic.IsOver(Run) && (Net == null || (!Net.IsMatchOver && !Net.Desynced));

        /// <summary>What the counter chip shows: nutella, or the zombie side's mud.</summary>
        public int Resource => NetZombie ? Net.Matsch : Run?.Nutella ?? 0;

        /// <summary>The card strip: the level's towers (or the conveyor queue on a pure belt level) + the shovel; the zombie side summons.</summary>
        public List<string> Cards()
        {
            var output = new List<string>();
            if (Run == null) return output;
            if (NetZombie) return Net.ZombieTypes();
            if (Run.Conveyor != null && !Run.Mod("conveyor_hybrid")) output.AddRange(Run.Conveyor.Queue);
            else output.AddRange(GvzLogic.AvailableTowers(Run));
            output.Add(Shovel);
            return output;
        }

        /// <summary>Tap a card: select it, tap again to deselect.</summary>
        public void SelectCard(string card)
        {
            if (!AcceptsInput) return;
            SelectedCard = SelectedCard == card ? "" : card;
        }

        /// <summary>Use the selected card on a cell: shovel removes, a tower places. Result reason "" = done.</summary>
        public GvzLogic.Check UseCard(int lane, int col)
        {
            if (!AcceptsInput || SelectedCard == "") return new GvzLogic.Check(false, "none");
            if (OnlineRun) return UseCardOnline(lane, col);
            var before = Run.Events.Count;
            GvzLogic.Check result;
            if (SelectedCard == Shovel)
            {
                var removed = GvzLogic.RemoveTower(Run, lane, col);
                result = new GvzLogic.Check(removed, removed ? "" : "empty");
            }
            else result = GvzLogic.PlaceTower(Run, SelectedCard, lane, col);
            if (result.Ok) SelectedCard = "";
            Emit(Run.Events, before);
            return result;
        }

        /// <summary>
        /// Online: actions run NOT now but through the lockstep (on both devices at tick t). The gate is checked locally for
        /// instant feedback; the sim decides finally when the action runs.
        /// </summary>
        private GvzLogic.Check UseCardOnline(int lane, int col)
        {
            GvzLogic.Check check;
            if (NetZombie)
            {
                check = Net.CanSpawn(SelectedCard, lane);
                if (check.Ok) Net.ScheduleSpawn(SelectedCard, lane);
            }
            else if (SelectedCard == Shovel)
            {
                check = Run.Towers.ContainsKey(GvzLogic.CellKey(lane, col)) ? new GvzLogic.Check(true, "") : new GvzLogic.Check(false, "empty");
                if (check.Ok) Net.ScheduleShovel(lane, col);
            }
            else
            {
                check = GvzLogic.CanPlace(Run, SelectedCard, lane, col);
                if (check.Ok) Net.SchedulePlace(SelectedCard, lane, col);
            }
            if (check.Ok) SelectedCard = "";
            return check;
        }

        /// <summary>Collect the blob on/near a cell (the view resolves the finger to a drop id); online the collect is planned (the amount shows at once).</summary>
        public int Collect(int dropId)
        {
            if (!AcceptsInput || NetZombie) return -1;
            if (OnlineRun)
            {
                var drop = Run.Drops.Find(d => d.Id == dropId);
                if (drop == null || Net.IsCollectPlanned(dropId)) return -1;
                Net.ScheduleCollect(dropId);
                return drop.Amount;
            }
            var before = Run.Events.Count;
            var amount = GvzLogic.CollectDrop(Run, dropId);
            Emit(Run.Events, before);
            return amount;
        }

        public int CardCost(string type)
        {
            if (NetZombie) return Net.ZombieCost(type);
            return type == Shovel || Run == null || (Run.Conveyor != null && Run.Conveyor.Queue.Contains(type)) ? 0 : GvzLogic.TowerCost(Run, type);
        }

        public double CardCooldownFrac(string type)
        {
            if (NetZombie) return Math.Min(1.0, (double)Net.ZombieCooldownLeft(type) / Math.Max(1, Net.ZombieCooldownTicks(type)));
            if (type == Shovel || Run == null) return 0.0;
            var left = GvzLogic.CooldownLeft(Run, type);
            var total = Math.Max(1, GvzJson.I(Run.TowerRow(type), "cooldown_ticks", 1));
            return Math.Min(1.0, (double)left / total);
        }

        /// <summary>Fog columns of the level (L11 fog_cols = 3; legacy fog:true = 3).</summary>
        public int FogCols()
        {
            if (Run == null) return 0;
            var cols = GvzJson.I(Run.Mods, "fog_cols", 0);
            if (cols <= 0 && Run.Mod("fog")) cols = 3;
            return Math.Min(Math.Max(cols, 0), GvzLogic.Cols);
        }

        // ── QA / tests: the certified bot plays (select → next open level, end cards move on after a beat) ──

        public void ApplyBot()
        {
            if (Finished) return;
            switch (Phase)
            {
                case PhaseSelect:
                    if (SelectBot != null && SelectBot()) break;
                    Open(GvzProgress.MaxUnlocked(Save));
                    break;
                case PhasePlay: _botOn = true; break;
                // an online match's card waits for the players: a rematch is a fresh challenge
                case PhaseWon: if (!OnlineRun && PhaseTime >= 1.5) Next(); break;
                case PhaseLost: if (!OnlineRun && PhaseTime >= 1.5) Retry(); break;
            }
        }

        private void OnRunOver()
        {
            if (Phase != PhasePlay) return;
            var won = Run.Outcome == "won";
            BookStickerProgress(won);
            if (won)
            {
                var mowersUsed = GvzBot.MowersUsed(Run);
                LastWon = true;
                LastStars = GvzProgress.StarsFor(mowersUsed);
                GvzProgress.Booking booking = _store != null
                    ? GvzProgress.RecordWin(_store, LevelId, LastStars, Run.Score)
                    : GvzProgress.RecordWin(_localSave, LevelId, LastStars, Run.Score);
                LastFirstClear = booking.FirstClear;
                LastTotal = GvzProgress.FinalScore(Run.Score, LevelId, LastStars, booking.FirstClear, Balance);
                SessionScore += LastTotal;
                if (_store != null && MilestoneHooks.TryGetValue(LevelId, out var hook)) StickerRules.FireEventHook(_store, hook);
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

        private void BookStickerProgress(bool won)
        {
            void Book(JObject save)
            {
                var achievements = J.EnsureDict(save, "achievements");
                var counters = J.EnsureDict(achievements, "counters");
                foreach (var kv in StatCounters) Bump(counters, kv.Value, Run.Stats.TryGetValue(kv.Key, out var v) ? v : 0);
                Bump(counters, "gvzZombiesGestoppt", Run.Kills);
            }
            if (_store != null) _store.Update(Book);
            else Book(_localSave);
        }

        private static void Bump(JObject counters, string key, int delta)
        {
            if (delta <= 0) return;
            var raw = counters[key];
            var basis = raw != null && (raw.Type == JTokenType.Integer || raw.Type == JTokenType.Float) ? (int)raw.Value<double>() : 0;
            counters[key] = basis + delta;
        }

        private void SetPhase(string phase)
        {
            Phase = phase;
            PhaseTime = 0.0;
            PhaseChanged?.Invoke(phase);
        }

        private void Emit(List<GvzEvent> events, int from)
        {
            if (Event == null) return;
            for (var i = from; i < events.Count; i++) Event(events[i]);
        }
    }
}
