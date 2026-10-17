using System;
using Gooby.Core.Minigames.Gobnom;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Binds a <see cref="GobnomGame"/> to GOB NOM's online coop (<see cref="GobnomNetClient"/>) — the netz wiring of
    /// gobnom_game.gd: GOBNOM_START builds the identical run on both devices, frames/hashes/cursor travel both ways, a
    /// desync (local guard or server) or an abort ends the round politely, our own rejoin replays the server snapshot, the
    /// run's end is reported (idempotent) and the consumed session is left once the server confirms — otherwise the dead
    /// pairing would block every new invite (GAME_RUNNING). Engine-free: the screen and the two-client integration test
    /// share it. Main-thread only.
    /// </summary>
    public sealed class GobnomCoopLink : IDisposable
    {
        public const double CursorSendSec = 0.1;

        public GobnomGame Game { get; }
        public GobnomNetClient Client { get; }
        /// <summary>The room of the running (or last) online round; "" before the first start.</summary>
        public string RunRoom { get; private set; } = "";
        /// <summary>The partner of that round — still known after the consumed session was left ("Nochmal mit …").</summary>
        public string RunPartnerCode { get; private set; } = "";
        public string RunPartnerGooby { get; private set; } = "";
        public V2 PartnerCursor { get; private set; }
        /// <summary>Seconds since the partner's last cursor (the view fades it out).</summary>
        public double PartnerCursorAge { get; private set; } = 99.0;
        public GobnomNetResult LastResult { get; private set; }
        /// <summary>Our coop level vote failed or an invite went wrong: the server code (the view shows gobnom.netz.fehler).</summary>
        public string LastError { get; private set; } = "";

        /// <summary>An i18n key for the view: gobnom.netz.start_banner | partner_weg | partner_da | desync | abbruch.</summary>
        public event Action<string> Notice;

        private double _cursorWait;
        private bool _reported;
        private int _botVote;
        private bool _disposed;

        public GobnomCoopLink(GobnomGame game, GobnomNetClient client)
        {
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Client = client ?? throw new ArgumentNullException(nameof(client));
            client.GameStarted += OnStart;
            client.FrameReceived += OnFrame;
            client.HashReceived += OnHash;
            client.CursorReceived += OnCursor;
            client.DesyncReported += OnDesync;
            client.Aborted += OnAborted;
            client.PeerConnectionChanged += OnPeer;
            client.SnapshotReceived += OnSnapshot;
            client.ResultConfirmed += OnResult;
            game.FrameOut += OnFrameOut;
            game.HashOut += OnHashOut;
            game.NetDesync += OnDesync;
            game.PhaseChanged += OnPhase;
            game.SelectBot = SelectBot;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Client.GameStarted -= OnStart;
            Client.FrameReceived -= OnFrame;
            Client.HashReceived -= OnHash;
            Client.CursorReceived -= OnCursor;
            Client.DesyncReported -= OnDesync;
            Client.Aborted -= OnAborted;
            Client.PeerConnectionChanged -= OnPeer;
            Client.SnapshotReceived -= OnSnapshot;
            Client.ResultConfirmed -= OnResult;
            Game.FrameOut -= OnFrameOut;
            Game.HashOut -= OnHashOut;
            Game.NetDesync -= OnDesync;
            Game.PhaseChanged -= OnPhase;
            if (Game.SelectBot == (Func<bool>)SelectBot) Game.SelectBot = null;
        }

        /// <summary>Per frame after <see cref="GobnomGame.Tick"/>: cursor fade + send throttle.</summary>
        public void Tick(double dt)
        {
            if (dt <= 0.0) return;
            PartnerCursorAge += dt;
            _cursorWait -= dt;
        }

        /// <summary>A coop card tapped while paired: our vote for that level (both the same → GOBNOM_START).</summary>
        public void ChooseLevel(int level)
        {
            LastError = "";
            Client.ChooseLevel(level, r => { if (!r.Ok) LastError = r.Code; });
        }

        /// <summary>Own finger position for the partner (10 Hz, only during an online round).</summary>
        public void SendCursor(V2 world)
        {
            if (!Game.OnlineRun || _cursorWait > 0.0) return;
            _cursorWait = CursorSendSec;
            Client.SendCursor(world.X, world.Y);
        }

        /// <summary>"Trennen", "Fertig" or the screen closing: the session ends for both (coop has no forfeit winner).</summary>
        public void Leave()
        {
            Client.Leave();
            Game.AbortOnline();
        }

        // ── client → game ──

        private void OnStart()
        {
            if (Game.Finished) return;
            RunRoom = Client.RoomId;
            RunPartnerCode = Client.PartnerCode;
            RunPartnerGooby = Client.PartnerGoobyName;
            LastResult = null;
            _reported = false;
            _botVote = 0;
            PartnerCursorAge = 99.0;
            if (Game.StartOnline(Client.Level, Client.Seed, Client.MySide, Client.InputDelay, Client.HashEveryTicks))
                Notice?.Invoke("gobnom.netz.start_banner");
        }

        private void OnFrame(JObject body)
        {
            if (Game.OnlineRun && Client.RoomId == RunRoom) Game.Net.ReceiveFrame(body);
        }

        private void OnHash(int tick, string hash)
        {
            if (Game.OnlineRun && Client.RoomId == RunRoom) Game.Net.ReceiveHash(tick, hash);
        }

        private void OnCursor(double x, double y)
        {
            PartnerCursor = new V2((float)x, (float)y);
            PartnerCursorAge = 0.0;
        }

        private void OnDesync(int tick)
        {
            if (!Game.OnlineRun || Game.Phase != GobnomGame.PhasePlay) return;
            Client.Leave();
            Game.AbortOnline();
            Notice?.Invoke("gobnom.netz.desync");
        }

        private void OnAborted(string reason, string by)
        {
            var running = Game.OnlineRun && Game.Phase == GobnomGame.PhasePlay;
            Game.AbortOnline();
            // a finished round's card stays quiet: its result is booked, only the lobby is gone
            if (running || !Game.OnlineRun) Notice?.Invoke("gobnom.netz.abbruch");
        }

        private void OnPeer(bool down, int waitMs)
        {
            if (Game.OnlineRun && Game.Phase == GobnomGame.PhasePlay) Notice?.Invoke(down ? "gobnom.netz.partner_weg" : "gobnom.netz.partner_da");
        }

        private void OnSnapshot(JObject d)
        {
            if (Game.Finished) return;
            var level = NetJson.Int(d["level"]);
            if (NetJson.Str(d["phase"]) != "run" || level <= 0)
                return;
            if (Game.OnlineRun && Game.Phase != GobnomGame.PhasePlay)
            {
                // our sim finished before the drop: the server may still need our report (idempotent)
                Report();
                return;
            }
            RunRoom = Client.RoomId;
            RunPartnerCode = Client.PartnerCode;
            RunPartnerGooby = Client.PartnerGoobyName;
            Game.ResumeOnline(level, NetJson.Long(d["seed"]), Client.MySide, d["frames"] as JObject, Client.InputDelay, Client.HashEveryTicks);
        }

        private void OnResult(GobnomNetResult r)
        {
            // WELCOME also delivers results of older rounds — they are only acked (the client does that)
            if (r.Room.Length == 0 || r.Room != RunRoom) return;
            LastResult = r;
            _reported = true;
            if (Client.IsPaired && Client.RoomId == r.Room) Client.Leave();
            Game.ApplyServerResult(r.Outcome, r.Jars);
        }

        // ── game → client ──

        private void OnFrameOut(JObject frame)
        {
            if (Client.IsPaired && Client.RoomId == RunRoom) Client.SendFrame(frame);
        }

        private void OnHashOut(int tick, string hash)
        {
            if (Client.IsPaired && Client.RoomId == RunRoom) Client.SendHash(tick, hash);
        }

        private void OnPhase(string phase)
        {
            if (Game.OnlineRun && (phase == GobnomGame.PhaseWon || phase == GobnomGame.PhaseLost)) Report();
        }

        private void Report()
        {
            var run = Game.Run;
            if (_reported || run == null || !Client.IsPaired || Client.RoomId != RunRoom) return;
            _reported = true;
            var outcome = run.Outcome == "won" ? "won" : "lost";
            Client.ReportResult(outcome, run.JarsTaken, run.Tick, r =>
            {
                // offline right at the end: try again with the rejoin snapshot
                if (!r.Ok && r.Code != "NOT_IN_ROOM") _reported = false;
            });
        }

        /// <summary>
        /// QA bot in the select while paired: side a keeps its vote (else takes the partner's, else our highest open coop
        /// level), side b follows a — two bots that both followed each other would swap votes forever.
        /// </summary>
        private bool SelectBot()
        {
            if (!Client.IsPaired) return false;
            var own = Client.MyVote > 0 ? Client.MyVote : GobnomProgress.MaxUnlocked(Game.Save, GobnomData.TrackCoop);
            var target = Client.MySide == GobnomLogic.PlayerB
                ? (Client.PartnerVote > 0 ? Client.PartnerVote : own)
                : (Client.MyVote > 0 ? Client.MyVote : Client.PartnerVote > 0 ? Client.PartnerVote : own);
            if (Client.MyVote != target && _botVote != target)
            {
                _botVote = target;
                ChooseLevel(target);
            }
            return true;
        }
    }
}
