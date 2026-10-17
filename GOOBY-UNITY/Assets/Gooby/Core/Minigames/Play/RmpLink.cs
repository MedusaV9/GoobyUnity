using System;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Binds the ride of a ranch multiplayer round (<see cref="RmpLauf"/>) to the server (<see cref="RanchMpClient"/>):
    /// MG_START builds the run from the course and the start grid, the own pose goes out at 10 Hz, peer poses feed the
    /// ghosts, reached checkpoints and the finish travel as MG_EVENT (the server times the finish), a tag goes to the
    /// nearest rider within reach, a rejoin snapshot resyncs progress, and the round's MG_RESULT is kept for the result card.
    /// Booking + ACK of results belong to the caller (OnlineService books every result once, also those from the WELCOME).
    /// Engine-free: the screen and the integration test share it. Main-thread only.
    /// </summary>
    public sealed class RmpLink : IDisposable
    {
        public RanchMpClient Client { get; }
        public RmpLauf Lauf { get; private set; }
        public string RunRoom { get; private set; } = "";
        public RmpResult LastResult { get; private set; }
        public string LastError { get; private set; } = "";
        /// <summary>QA/tests: the bot steers the own horse (<see cref="RmpBot"/>).</summary>
        public bool Bot { get; set; }

        /// <summary>(i18n key, name) for the view: ranch_mp.lauf.* / ranch_mp.fehler.*.</summary>
        public event Action<string, string> Notice;
        public event Action RunStarted;
        /// <summary>Free ride: the own rider reached a landmark for the first time (already shared as ENTDECKUNG).</summary>
        public event Action<string> Entdeckt;

        private readonly Func<long> _now;
        private bool _disposed;

        public RmpLink(RanchMpClient client, Func<long> nowMs)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            _now = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            client.MatchStarted += OnStart;
            client.PeerPose += OnPeerPose;
            client.StateUpdated += OnState;
            client.SnapshotReceived += OnSnapshot;
            client.ResultReceived += OnResult;
            client.PeerConnectionChanged += OnPeer;
            client.SessionReady += OnSessionReady;
            client.PeerLeft += OnPeerLeft;
            // opened after the start shot (accepted from a question card mid-countdown, rejoin): build the run right away;
            // a visit or trail ride rides from the moment the room is there
            if (client.IsMatch && (client.Phase == RanchMpClient.PhaseCountdown || client.Phase == RanchMpClient.PhaseRun)) OnStart();
            else if (client.IsActive && client.IsFree) OnStart();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Client.MatchStarted -= OnStart;
            Client.PeerPose -= OnPeerPose;
            Client.StateUpdated -= OnState;
            Client.SnapshotReceived -= OnSnapshot;
            Client.ResultReceived -= OnResult;
            Client.PeerConnectionChanged -= OnPeer;
            Client.SessionReady -= OnSessionReady;
            Client.PeerLeft -= OnPeerLeft;
        }

        private void OnSessionReady()
        {
            if (Client.IsFree) OnStart();
        }

        private void OnPeerLeft(string code, string name)
        {
            if (Lauf != null && Client.RoomId == RunRoom && Client.IsFree) Lauf.RemovePeer(code);
        }

        /// <summary>The run of the current room is on (countdown over, not booked yet).</summary>
        public bool Running => Lauf != null && Client.RoomId == RunRoom && Client.Running && Client.Phase != RanchMpClient.PhaseDone;

        public void Tick(double dt)
        {
            var lauf = Lauf;
            if (lauf == null || Client.RoomId != RunRoom) return;
            var running = Running;
            if (Bot && running) RmpBot.Drive(lauf, Client.ItCode, _now());
            lauf.Tick(dt, running);
            var c = lauf.Controller;
            Client.SendPose(c.Pos, c.Y, c.Heading, c.Tempo, RmpKurse.GaitIndex(c.Gait));
            _botTagIn = Math.Max(0.0, _botTagIn - dt);
            if (Bot && running && Client.IAmIt && _botTagIn <= 0.0 && lauf.TagTarget(_now()) != null)
            {
                _botTagIn = BotTagPauseS;
                TryTag();
            }
        }

        /// <summary>The bot's catcher tries again after this pause (the server keeps a fresh catch immune for 2 s).</summary>
        public const double BotTagPauseS = 0.6;

        private double _botTagIn;

        /// <summary>The catcher's button: the nearest rider within reach (the server checks distance and immunity).</summary>
        public bool TryTag()
        {
            var lauf = Lauf;
            if (lauf == null || !Client.IAmIt || !Running) return false;
            var target = lauf.TagTarget(_now());
            if (target == null)
            {
                Notice?.Invoke("ranch_mp.fehler.tag_weit", "");
                return false;
            }
            Client.SendTag(target, r =>
            {
                if (r.Ok) return;
                LastError = r.Code;
                Notice?.Invoke("ranch_mp.fehler." + r.Code.ToLowerInvariant(), "");
            });
            return true;
        }

        /// <summary>"Verlassen"/"Fertig": the session ends for us (a running round counts as given up).</summary>
        public void Leave()
        {
            Client.Leave();
            Lauf = null;
            RunRoom = "";
        }

        // ── client → run ──

        private void OnStart()
        {
            RunRoom = Client.RoomId;
            LastResult = null;
            LastError = "";
            var lauf = new RmpLauf(Client.Mode, Client.Kurs, Client.Players, Client.MyCode);
            lauf.CheckpointDue += idx => SendCheckpoint(lauf, idx);
            lauf.FinishDue += () => SendFinish(lauf);
            lauf.OrtEntdeckt += id =>
            {
                if (lauf != Lauf) return;
                Client.SendReaction("ENTDECKUNG", new JObject { ["zone"] = id });
                Entdeckt?.Invoke(id);
            };
            Lauf = lauf;
            RunStarted?.Invoke();
            if (Client.Mode == RmpKurse.Fangen && Client.ItCode.Length > 0) AnnounceCatcher();
        }

        private void SendCheckpoint(RmpLauf lauf, int idx)
        {
            Client.SendCheckpoint(idx, r =>
            {
                if (lauf != Lauf) return;
                if (r.Ok)
                {
                    lauf.Confirm(NetJson.Int(r.Data["next"], idx + 1));
                    return;
                }
                LastError = r.Code;
                lauf.Reject(Client.Progress.TryGetValue(Client.MyCode, out var next) ? next : -1);
            });
        }

        private void SendFinish(RmpLauf lauf)
        {
            Client.SendFinish(r =>
            {
                if (lauf != Lauf) return;
                if (r.Ok)
                {
                    lauf.ConfirmFinish();
                    Notice?.Invoke("ranch_mp.lauf.ziel", "");
                    return;
                }
                LastError = r.Code;
                if (r.Code == "ALREADY_DONE") lauf.ConfirmFinish();
                else lauf.Reject(Client.Progress.TryGetValue(Client.MyCode, out var next) ? next : -1);
            });
        }

        private void OnPeerPose(string code, RmpPeerPose pose)
        {
            if (Lauf != null && Client.RoomId == RunRoom) Lauf.PushPeer(code, _now(), pose, Client.NameOf(code));
        }

        private void OnState(JObject d)
        {
            if (Lauf == null || Client.RoomId != RunRoom) return;
            if (d["tag"] is JObject) AnnounceCatcher();
        }

        private void AnnounceCatcher()
        {
            if (Client.IAmIt) Notice?.Invoke("ranch_mp.lauf.du_bist_faenger", "");
            else Notice?.Invoke("ranch_mp.lauf.faenger", Client.NameOf(Client.ItCode));
        }

        private void OnSnapshot(JObject d)
        {
            if (!Client.IsMatch) return;
            var phase = Client.Phase;
            if (phase != RanchMpClient.PhaseCountdown && phase != RanchMpClient.PhaseRun) return;
            if (Lauf == null || RunRoom != Client.RoomId) OnStart();
            var me = Client.MyCode;
            Lauf.Resync(Client.Progress.TryGetValue(me, out var next) ? next : 0, Client.Finished.ContainsKey(me));
        }

        private void OnResult(RmpResult r)
        {
            if (r.Room.Length == 0 || r.Room != RunRoom) return;
            LastResult = r;
        }

        private void OnPeer(string code, bool down)
        {
            if (Lauf == null || Client.RoomId != RunRoom) return;
            Notice?.Invoke(down ? "ranch_mp.lauf.peer_weg" : "ranch_mp.lauf.peer_da", Client.NameOf(code));
        }
    }
}
