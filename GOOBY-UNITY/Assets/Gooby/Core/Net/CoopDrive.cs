using System;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>
    /// A ride along through town (port of coop_drive_logic.gd): during a house visit the host drives and the guest rides
    /// with him. The invitation travels over the visit relay (kind DRIVE), the drive itself in a <c>drive:</c> room of
    /// the server (rooms.js, two members): the car's pose as POS at 5 Hz, the radio as RADIO to both sides.
    /// </summary>
    public static class CoopDriveLogic
    {
        public const string KindDrive = "DRIVE";
        public const string KindRadio = "RADIO";
        public const string KindPos = "POS";
        public const string AnimDrive = "drive";
        public const string OpInvite = "invite";
        public const string OpDecline = "decline";
        public const string OpRadioSet = "set";
        public const string OpRadioStop = "stop";
        /// <summary>Client-side pose rate (the server throttles POS to 5 Hz as well).</summary>
        public const long PosIntervalMs = 200;

        public static string DriveRoomId(string hostCode, int nonce) => "drive:" + (hostCode ?? "") + "-" + Math.Abs(nonce);

        public static JObject InvitePayload(string room, bool radioOwned, string von) =>
            new JObject { ["op"] = OpInvite, ["room"] = room ?? "", ["radio"] = radioOwned, ["von"] = von ?? "" };

        public static JObject DeclinePayload() => new JObject { ["op"] = OpDecline };

        /// <summary>A DRIVE message: ok, op, room, radio, von (an invitation must name a drive: room).</summary>
        public static (bool ok, string op, string room, bool radio, string von) ParseDrive(JObject body)
        {
            var op = NetJson.Str(body?["op"]);
            if (op != OpInvite && op != OpDecline) return (false, "", "", false, "");
            var room = NetJson.Str(body["room"]);
            var radio = body["radio"]?.Type == JTokenType.Boolean && body["radio"].Value<bool>();
            var ok = op == OpDecline || room.StartsWith("drive:", StringComparison.Ordinal);
            return (ok, op, room, radio, NetJson.Str(body["von"]));
        }

        /// <summary>The car's pose: world x/z (mm precision), heading in degrees, speed in km/h; anim "drive" tells it from a walk.</summary>
        public static JObject CarPayload(double x, double z, double yawDeg, double kmh) => new JObject
        {
            ["pos"] = new JArray(Math.Round(x, 3), Math.Round(z, 3)),
            ["anim"] = AnimDrive,
            ["yaw"] = Math.Round(yawDeg, 1),
            ["tempo"] = Math.Round(kmh, 2),
        };

        public static (bool ok, double x, double z, double yawDeg, double kmh) ParseCar(JObject body)
        {
            if (!(body?["pos"] is JArray pos) || pos.Count < 2 || !IsNumber(pos[0]) || !IsNumber(pos[1])) return (false, 0, 0, 0, 0);
            return (true, NetJson.Num(pos[0]), NetJson.Num(pos[1]), IsNumber(body["yaw"]) ? NetJson.Num(body["yaw"]) : 0.0,
                IsNumber(body["tempo"]) ? NetJson.Num(body["tempo"]) : 0.0);
        }

        public static JObject RadioPayload(string station, string trackId, long atMs) =>
            new JObject { ["op"] = OpRadioSet, ["station"] = station ?? "", ["trackId"] = trackId ?? "", ["atMs"] = atMs };

        public static JObject RadioStopPayload(long atMs) => new JObject { ["op"] = OpRadioStop, ["atMs"] = atMs };

        /// <summary>A RADIO message: a set needs a station (the track is optional — Unity's radio picks its own).</summary>
        public static (bool ok, string op, string station, string trackId, long atMs) ParseRadio(JObject body)
        {
            var op = NetJson.Str(body?["op"]);
            if (op != OpRadioSet && op != OpRadioStop) return (false, "", "", "", 0);
            var station = NetJson.Str(body["station"]);
            var at = IsNumber(body["atMs"]) ? (long)NetJson.Num(body["atMs"]) : 0L;
            return (op == OpRadioStop || station.Length > 0, op, station, NetJson.Str(body["trackId"]), at);
        }

        /// <summary>Seconds since the other side pressed (never negative: clocks drift apart).</summary>
        public static double OffsetSec(long atMs, long nowMs) => Math.Max(0.0, (nowMs - atMs) / 1000.0);

        public static bool ShouldSendPos(long lastMs, long nowMs) => nowMs - lastMs >= PosIntervalMs;

        private static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
    }

    public sealed class CoopInvite
    {
        public string Room = "", Von = "";
        public bool Radio;
    }

    /// <summary>
    /// The ride-along session (port of coop_drive.gd): the driver (host) invites over the visit relay and joins the
    /// drive: room, then reports the car at 5 Hz; the passenger (guest) accepts, joins, follows the car and picks the
    /// radio for both. A passenger whose driver leaves gets out; a driver whose passenger leaves drives on.
    /// </summary>
    public sealed class CoopDriveClient
    {
        public const string RoleNone = "";
        public const string RoleDriver = "fahrer";
        public const string RolePassenger = "beifahrer";

        private readonly SocialClient _social;
        private readonly NetSession _session;
        private readonly Func<long> _now;
        private readonly Func<int> _nonce;
        private long _lastPosMs = long.MinValue / 2;

        public string Role { get; private set; } = RoleNone;
        public string RoomId { get; private set; } = "";
        public bool HostRadioOwned { get; private set; }
        public string PartnerName { get; private set; } = "";
        public CoopInvite Pending { get; private set; }
        public int PositionsReceived { get; private set; }
        public (double x, double z, double yawDeg, double kmh) LastCar { get; private set; }
        public string RadioStation { get; private set; } = "";
        public bool Active => Role != RoleNone;

        public event Action<CoopInvite> InviteReceived;
        public event Action InviteDeclined;
        public event Action<string> Started;
        public event Action<string> PeerJoined;
        public event Action PeerLeft;
        public event Action<string> Ended;
        public event Action<double, double, double, double> CarMoved;
        public event Action<string, double> RadioSet;
        public event Action Changed;

        public CoopDriveClient(SocialClient social, Func<long> nowMs = null, Func<int> nonce = null)
        {
            _social = social ?? throw new ArgumentNullException(nameof(social));
            _session = social.Session;
            _now = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var rng = new System.Random();
            _nonce = nonce ?? (() => rng.Next(0, 1_000_000));
            _session.Push += OnPush;
            _session.StatusChanged += OnStatus;
            _social.RoomMessage += OnVisitMessage;
        }

        /// <summary>"Ausfahrt": the invitation into the visit room, then our own seat in the drive: room.</summary>
        public void StartAsDriver(bool radioOwned, string von, Action<bool, string> done = null)
        {
            if (Active) { done?.Invoke(false, "ACTIVE"); return; }
            if (!_session.IsOnline || !_social.VisitActive) { done?.Invoke(false, "OFFLINE"); return; }
            var room = CoopDriveLogic.DriveRoomId(_social.FriendCode, _nonce());
            _social.SendRoomMessage(CoopDriveLogic.KindDrive, CoopDriveLogic.InvitePayload(room, radioOwned, von));
            _session.Request("ROOM_JOIN", new JObject { ["room"] = room }, r =>
            {
                if (r.Ok)
                {
                    RoomId = room;
                    Role = RoleDriver;
                    HostRadioOwned = radioOwned;
                    PositionsReceived = 0;
                    Started?.Invoke(Role);
                    Changed?.Invoke();
                }
                done?.Invoke(r.Ok, r.Code);
            });
        }

        /// <summary>Takes the offered seat.</summary>
        public void Accept(Action<bool, string> done = null)
        {
            var invite = Pending;
            if (invite == null) { done?.Invoke(false, "NO_INVITE"); return; }
            if (!_session.IsOnline) { done?.Invoke(false, "OFFLINE"); return; }
            _session.Request("ROOM_JOIN", new JObject { ["room"] = invite.Room }, r =>
            {
                if (r.Ok)
                {
                    RoomId = invite.Room;
                    Role = RolePassenger;
                    HostRadioOwned = invite.Radio;
                    PartnerName = invite.Von;
                    PositionsReceived = 0;
                    Pending = null;
                    Started?.Invoke(Role);
                    Changed?.Invoke();
                }
                done?.Invoke(r.Ok, r.Code);
            });
        }

        /// <summary>A friendly no over the visit relay.</summary>
        public void Decline()
        {
            if (Pending == null) return;
            _social.SendRoomMessage(CoopDriveLogic.KindDrive, CoopDriveLogic.DeclinePayload());
            Pending = null;
            Changed?.Invoke();
        }

        /// <summary>Driver: the car's pose into the room, at most every 200 ms.</summary>
        public bool ReportCar(double x, double z, double yawDeg, double kmh)
        {
            if (Role != RoleDriver) return false;
            var now = _now();
            if (!CoopDriveLogic.ShouldSendPos(_lastPosMs, now)) return false;
            _lastPosMs = now;
            Send(CoopDriveLogic.KindPos, CoopDriveLogic.CarPayload(x, z, yawDeg, kmh));
            return true;
        }

        /// <summary>Either side: a station for both radios (the relay only reaches the other side, so it is applied here too).</summary>
        public bool SetRadio(string station)
        {
            if (!Active || string.IsNullOrEmpty(station)) return false;
            Send(CoopDriveLogic.KindRadio, CoopDriveLogic.RadioPayload(station, "", _now()));
            RadioStation = station;
            RadioSet?.Invoke(station, 0.0);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Either side: both radios off.</summary>
        public bool RadioOff()
        {
            if (!Active) return false;
            Send(CoopDriveLogic.KindRadio, CoopDriveLogic.RadioStopPayload(_now()));
            RadioStation = "";
            RadioSet?.Invoke("", 0.0);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Gets out (or ends the drive as driver); the other side degrades over ROOM_PEER_LEFT.</summary>
        public void Leave()
        {
            if (!Active) return;
            if (_session.IsOnline) _session.Send("ROOM_LEAVE", new JObject { ["room"] = RoomId });
            End("selbst");
        }

        private void Send(string kind, JObject body)
        {
            if (Active && _session.IsOnline) _session.Send("ROOM_MSG", new JObject { ["room"] = RoomId, ["kind"] = kind, ["body"] = body });
        }

        private void OnVisitMessage(string kind, JObject body)
        {
            if (kind != CoopDriveLogic.KindDrive) return;
            var d = CoopDriveLogic.ParseDrive(body);
            if (!d.ok) return;
            if (d.op == CoopDriveLogic.OpInvite && !Active)
            {
                Pending = new CoopInvite { Room = d.room, Radio = d.radio, Von = d.von };
                InviteReceived?.Invoke(Pending);
                Changed?.Invoke();
            }
            else if (d.op == CoopDriveLogic.OpDecline && Role == RoleDriver)
            {
                InviteDeclined?.Invoke();
            }
        }

        private void OnPush(string type, JObject d)
        {
            var room = NetJson.Str(d?["room"]);
            if (!Active || room != RoomId) return;
            switch (type)
            {
                case "ROOM_MSG":
                {
                    var body = d["body"] as JObject;
                    var kind = NetJson.Str(d["kind"]);
                    if (kind == CoopDriveLogic.KindPos && Role == RolePassenger)
                    {
                        var car = CoopDriveLogic.ParseCar(body);
                        if (!car.ok) return;
                        PositionsReceived++;
                        LastCar = (car.x, car.z, car.yawDeg, car.kmh);
                        CarMoved?.Invoke(car.x, car.z, car.yawDeg, car.kmh);
                    }
                    else if (kind == CoopDriveLogic.KindRadio)
                    {
                        var radio = CoopDriveLogic.ParseRadio(body);
                        if (!radio.ok) return;
                        RadioStation = radio.op == CoopDriveLogic.OpRadioSet ? radio.station : "";
                        RadioSet?.Invoke(RadioStation, CoopDriveLogic.OffsetSec(radio.atMs, _now()));
                        Changed?.Invoke();
                    }
                    break;
                }
                case "ROOM_PEER_JOINED":
                    PartnerName = NetJson.Str(d["name"], PartnerName);
                    PeerJoined?.Invoke(PartnerName);
                    Changed?.Invoke();
                    break;
                case "ROOM_PEER_LEFT":
                    if (Role == RolePassenger)
                    {
                        if (_session.IsOnline) _session.Send("ROOM_LEAVE", new JObject { ["room"] = RoomId });
                        End("fahrer_weg");
                    }
                    else
                    {
                        PeerLeft?.Invoke();
                        Changed?.Invoke();
                    }
                    break;
            }
        }

        private void OnStatus(NetStatus status)
        {
            if (Active && status != NetStatus.Online) End("offline");
        }

        private void End(string reason)
        {
            Role = RoleNone;
            RoomId = "";
            RadioStation = "";
            _lastPosMs = long.MinValue / 2;
            Ended?.Invoke(reason);
            Changed?.Invoke();
        }
    }
}
