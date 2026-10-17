using System;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Binds a <see cref="GvzGame"/> to GvZ's online PvP (<see cref="GvzNetClient"/>) — the netz wiring of gvz_game.gd:
    /// GVZ_START builds the identical arena on both devices, GP_INPUT/GP_HASH travel both ways, a desync (local guard or
    /// server) or an abort ends the match politely, the winner is reported (idempotent), the server's small coin reward is
    /// booked once per rewardId and the consumed session is left — otherwise the dead pairing would block every new
    /// challenge (GAME_RUNNING). Engine-free: the screen and the two-client integration test share it. Main-thread only.
    /// </summary>
    public sealed class GvzPvpLink : IDisposable
    {
        public GvzGame Game { get; }
        public GvzNetClient Client { get; }
        public string RunRoom { get; private set; } = "";
        /// <summary>The partner of the running (or last) match — still known after the session was left ("Revanche").</summary>
        public string RunPartnerCode { get; private set; } = "";
        public string RunPartnerGooby { get; private set; } = "";
        public GvzNetResult LastResult { get; private set; }
        public string LastError { get; private set; } = "";

        /// <summary>An i18n key for the view: gvz.netz.start_banner | partner_weg | partner_da | desync | abbruch.</summary>
        public event Action<string> Notice;

        private bool _reported, _disposed;

        public GvzPvpLink(GvzGame game, GvzNetClient client)
        {
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Client = client ?? throw new ArgumentNullException(nameof(client));
            client.GameStarted += OnStart;
            client.FrameReceived += OnFrame;
            client.HashReceived += OnHash;
            client.DesyncReported += OnDesync;
            client.Aborted += OnAborted;
            client.PeerConnectionChanged += OnPeer;
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
            Client.DesyncReported -= OnDesync;
            Client.Aborted -= OnAborted;
            Client.PeerConnectionChanged -= OnPeer;
            Client.ResultConfirmed -= OnResult;
            Game.FrameOut -= OnFrameOut;
            Game.HashOut -= OnHashOut;
            Game.NetDesync -= OnDesync;
            Game.PhaseChanged -= OnPhase;
            if (Game.SelectBot == (Func<bool>)SelectBot) Game.SelectBot = null;
        }

        /// <summary>"Los geht's!" in the lobby: our start confirmation (both confirmed → GVZ_START).</summary>
        public void RequestStart()
        {
            LastError = "";
            Client.RequestStart(r => { if (!r.Ok) LastError = r.Code; });
        }

        /// <summary>"Trennen", "Fertig", the way back or the screen closing: the session ends for both.</summary>
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
            if (Game.StartOnline(Client.Seed, Client.MySide, Client.InputDelay, Client.HashEveryTicks))
                Notice?.Invoke("gvz.netz.start_banner");
        }

        private void OnFrame(JObject body)
        {
            if (Game.OnlineRun && Client.RoomId == RunRoom) Game.Net.ReceiveFrame(body);
        }

        private void OnHash(int tick, string hash)
        {
            if (Game.OnlineRun && Client.RoomId == RunRoom) Game.Net.ReceiveHash(tick, hash);
        }

        private void OnDesync(int tick)
        {
            if (!Game.OnlineRun || Game.Phase != GvzGame.PhasePlay) return;
            Client.Leave();
            Game.AbortOnline();
            Notice?.Invoke("gvz.netz.desync");
        }

        private void OnAborted(string reason, string by)
        {
            var running = Game.OnlineRun && Game.Phase == GvzGame.PhasePlay;
            Game.AbortOnline();
            if (running || !Game.OnlineRun) Notice?.Invoke("gvz.netz.abbruch");
        }

        private void OnPeer(bool down, int waitMs)
        {
            if (Game.OnlineRun && Game.Phase == GvzGame.PhasePlay) Notice?.Invoke(down ? "gvz.netz.partner_weg" : "gvz.netz.partner_da");
        }

        private void OnResult(GvzNetResult r)
        {
            // every confirmed result books once (also older ones from the WELCOME); only ours ends this pairing
            Game.BookNetResult(r.RewardId, r.Won, r.Coins);
            if (r.Room.Length == 0 || r.Room != RunRoom) return;
            LastResult = r;
            _reported = true;
            if (Client.IsPaired && Client.RoomId == r.Room) Client.Leave();
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
            if (!Game.OnlineRun || (phase != GvzGame.PhaseWon && phase != GvzGame.PhaseLost)) return;
            var net = Game.Net;
            if (_reported || net == null || !Client.IsPaired || Client.RoomId != RunRoom) return;
            _reported = true;
            Client.ReportResult(net.Winner, net.State.Tick, r => { if (!r.Ok && r.Code != "NOT_IN_ROOM") _reported = false; });
        }

        /// <summary>QA bot in the select while paired: confirm the start once.</summary>
        private bool SelectBot()
        {
            if (!Client.IsPaired) return false;
            if (!Client.StartRequested && !Client.Running) RequestStart();
            return true;
        }
    }
}
