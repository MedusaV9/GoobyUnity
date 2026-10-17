using System;
using System.Collections.Generic;
using Gooby.Core.Net;

namespace Gooby.Core.Minigames.Ranch.Mp
{
    /// <summary>
    /// The own ride of a ranch multiplayer round (rmp_ausritt_controller.gd without the open world): the horse on the
    /// <see cref="RideController"/> from its start box, the optimistic checkpoint trigger of races and the parcours (the
    /// server checks order, distance and time — <see cref="CheckpointDue"/>/<see cref="FinishDue"/> ask the caller to send),
    /// the other riders as ghosts behind an <see cref="RmpInterp"/> buffer, and the tag candidate on the meadow. Pure: the
    /// caller passes dt, whether the round runs and its millisecond clock.
    /// </summary>
    public sealed class RmpLauf
    {
        /// <summary>Client-side reach for a tag: a little inside the server's tagRadius + 1 m lag allowance.</summary>
        public const double TagSpielraumM = 0.8;

        /// <summary>Open-world tempo (ranch_welt_reiter.gd: trot 8, gallop 14.5 m/s, no stamina) over the arena feel's 8.5 m/s gallop.</summary>
        public const double WeltTempo = 14.5 / 8.5;

        /// <summary>Margin on top of the course's minimum segment time, so latency never makes a fair run unranked.</summary>
        public const int SegmentReserveMs = 200;

        public sealed class Remote
        {
            public string Code = "", Name = "?";
            public int Platz;
            public readonly RmpInterp Interp = new RmpInterp();
        }

        public string Mode { get; }
        public string Kurs { get; }
        public string MyCode { get; }
        public int Platz { get; }
        public RideController Controller { get; } = new RideController();
        public Dictionary<string, Remote> Remotes { get; } = new Dictionary<string, Remote>();
        public int NextCheckpoint { get; private set; }
        public int CheckpointCount { get; }
        public bool InZiel { get; private set; }
        public bool Waiting { get; private set; }
        public double Zeit { get; private set; }

        public bool IsRace => Mode == RmpKurse.Rennen || Mode == RmpKurse.Parcours;
        /// <summary>Besuch / Ausritt: the Weidetal without a course; landmarks count as discoveries.</summary>
        public bool IsFree => RmpKurse.IstFrei(Mode);
        /// <summary>Landmarks of the free ride reached this session (the start place counts as known).</summary>
        public HashSet<string> Entdeckt { get; } = new HashSet<string>();
        /// <summary>A landmark reached for the first time — the caller shares it as ENTDECKUNG.</summary>
        public event Action<string> OrtEntdeckt;

        /// <summary>The checkpoint (index) is reached — send MG_EVENT checkpoint, then <see cref="Confirm"/> or <see cref="Reject"/>.</summary>
        public event Action<int> CheckpointDue;
        /// <summary>All checkpoints confirmed — send MG_EVENT finish, then <see cref="ConfirmFinish"/> or <see cref="Reject"/>.</summary>
        public event Action FinishDue;

        public RmpLauf(string mode, string kurs, IList<RmpPlayer> players, string myCode)
        {
            Mode = mode ?? "";
            Kurs = kurs ?? "";
            MyCode = (myCode ?? "").ToUpperInvariant();
            CheckpointCount = RmpKurse.CheckpointAnzahl(Kurs);
            var anzahl = Math.Max(1, players?.Count ?? 1);
            Platz = 0;
            if (players != null)
                for (var i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p.Code == MyCode) { Platz = i; continue; }
                    Remotes[p.Code] = new Remote { Code = p.Code, Name = p.Name, Platz = i };
                }
            if (IsFree)
            {
                Controller.Pos = RmpFrei.StartPosition(Platz, anzahl);
                Controller.Heading = RmpFrei.StartHeading;
                Controller.SetBounds(RmpFrei.FeldMitte, RmpFrei.FeldHalb);
                var start = RmpFrei.OrtBei(Controller.Pos);
                if (start.Length > 0) Entdeckt.Add(start);
            }
            else
            {
                Controller.Pos = RmpKurse.StartPosition(Kurs, Platz, anzahl);
                Controller.Heading = RmpKurse.StartHeading(Kurs, Platz, anzahl);
                var (center, half) = RmpKurse.Feld(Kurs);
                Controller.SetBounds(center, half);
            }
            Controller.TempoScale = WeltTempo;
            Controller.Ausdauerfrei = true;
            Controller.Active = false;
            _minSegmentS = (RmpKurse.Get(Kurs)?.MinSegmentMs ?? 0) / 1000.0 + SegmentReserveMs / 1000.0;
        }

        /// <summary>After a refusal the trigger rests this long — the server rate-limits MG_EVENT (4/s).</summary>
        public const double RetryPauseS = 0.5;

        private double _retryIn, _seitCheckpoint, _minSegmentS;

        /// <summary>One step: the horse moves while the round runs (also after the finish — the server ignores us then).</summary>
        public void Tick(double dt, bool running)
        {
            Controller.Active = running;
            if (!running || dt <= 0.0) return;
            Zeit += dt;
            var vorher = Controller.Pos;
            Controller.Step(dt);
            // the region's lakes and cliffs stop the horse (the courses lie on open meadow)
            if (!RmpFrei.Begehbar(Controller.Pos))
            {
                Controller.Pos = vorher;
                Controller.Tempo *= 0.5;
            }
            if (IsFree)
            {
                var zone = RmpFrei.OrtBei(Controller.Pos);
                if (zone.Length > 0 && Entdeckt.Add(zone)) OrtEntdeckt?.Invoke(zone);
                return;
            }
            _retryIn = Math.Max(0.0, _retryIn - dt);
            _seitCheckpoint += dt;
            if (!IsRace || InZiel || Waiting || _retryIn > 0.0) return;
            if (NextCheckpoint >= CheckpointCount)
            {
                Waiting = true;
                FinishDue?.Invoke();
                return;
            }
            if (_seitCheckpoint >= _minSegmentS && RmpKurse.CheckpointErreicht(Kurs, NextCheckpoint, Controller.Pos))
            {
                Waiting = true;
                CheckpointDue?.Invoke(NextCheckpoint);
            }
        }

        public void Confirm(int next)
        {
            Waiting = false;
            _seitCheckpoint = 0.0;
            NextCheckpoint = Math.Max(NextCheckpoint, Math.Min(CheckpointCount, next));
        }

        public void ConfirmFinish()
        {
            Waiting = false;
            InZiel = true;
        }

        /// <summary>The server said no (order out of step, not running yet): adopt its progress and try again from there.</summary>
        public void Reject(int serverNext = -1)
        {
            Waiting = false;
            _retryIn = RetryPauseS;
            if (serverNext >= 0) NextCheckpoint = Math.Min(CheckpointCount, serverNext);
        }

        /// <summary>Back in after a rejoin: the server's progress and finish win.</summary>
        public void Resync(int next, bool finished)
        {
            Waiting = false;
            NextCheckpoint = Math.Min(CheckpointCount, Math.Max(0, next));
            InZiel = finished;
            foreach (var r in Remotes.Values) r.Interp.Reset();
        }

        public void PushPeer(string code, long atMs, RmpPeerPose pose, string name = null)
        {
            if (string.IsNullOrEmpty(code) || code == MyCode) return;
            if (!Remotes.TryGetValue(code, out var r))
            {
                r = new Remote { Code = code, Name = name ?? code, Platz = Remotes.Count + 1 };
                Remotes[code] = r;
            }
            r.Interp.Push(atMs, pose.Pos, pose.Y, pose.Yaw, pose.Gait, pose.Jump);
        }

        /// <summary>A trail rider left the room: the ghost goes (a match keeps its riders for the result).</summary>
        public void RemovePeer(string code)
        {
            if (!string.IsNullOrEmpty(code)) Remotes.Remove(code);
        }

        public RmpInterp.Pose? PeerAt(string code, long nowMs) =>
            Remotes.TryGetValue(code ?? "", out var r) ? r.Interp.SampleAt(nowMs) : null;

        /// <summary>Tag: the nearest other rider within reach (null when nobody is close enough).</summary>
        public string TagTarget(long nowMs)
        {
            var k = RmpKurse.Get(Kurs);
            if (k == null || Mode != RmpKurse.Fangen) return null;
            string best = null;
            var bestD = k.TagRadius + TagSpielraumM;
            foreach (var r in Remotes.Values)
            {
                var pose = r.Interp.SampleAt(nowMs);
                if (pose == null || pose.Value.Stale) continue;
                var d = (pose.Value.Pos - Controller.Pos).Length;
                if (d <= bestD) { bestD = d; best = r.Code; }
            }
            return best;
        }

        /// <summary>Distance to the nearest other rider (∞ when none is known).</summary>
        public double NearestPeerM(long nowMs)
        {
            var best = double.PositiveInfinity;
            foreach (var r in Remotes.Values)
            {
                var pose = r.Interp.SampleAt(nowMs);
                if (pose != null) best = Math.Min(best, (pose.Value.Pos - Controller.Pos).Length);
            }
            return best;
        }
    }

    /// <summary>
    /// A steady rider for tests and the QA player: races steer for the next checkpoint (gallop while there is breath, trot
    /// near the gate), on the tag meadow the catcher chases the nearest rider and the others keep away from the catcher.
    /// </summary>
    public static class RmpBot
    {
        public static void Drive(RmpLauf lauf, string itCode, long nowMs)
        {
            var c = lauf.Controller;
            if (!c.Active) return;
            V2? ziel = null;
            var hetzen = true;
            if (lauf.IsRace)
            {
                if (!lauf.InZiel) ziel = RmpKurse.CheckpointPos(lauf.Kurs, Math.Min(lauf.NextCheckpoint, lauf.CheckpointCount - 1));
            }
            else if (lauf.IsFree)
            {
                // the free ride: along the paths into the trail's zones, then home to the yard at a trot
                foreach (var zone in RmpFrei.BotZiele)
                    if (!lauf.Entdeckt.Contains(zone)) { ziel = RmpFrei.Weiter(RmpFrei.BotRoute(zone), c.Pos); break; }
                if (ziel == null && (RmpFrei.HofMitte - c.Pos).Length > RmpFrei.HofRingRadius)
                {
                    var heim = RmpFrei.BotRoute(RmpFrei.BotZiele[RmpFrei.BotZiele.Length - 1]);
                    heim.Reverse();
                    ziel = RmpFrei.Weiter(heim, c.Pos);
                    hetzen = false;
                }
            }
            else if (lauf.Mode == RmpKurse.Fangen)
            {
                var k = RmpKurse.Get(lauf.Kurs);
                if (itCode == lauf.MyCode)
                {
                    var best = double.PositiveInfinity;
                    foreach (var r in lauf.Remotes.Values)
                    {
                        var pose = r.Interp.SampleAt(nowMs);
                        if (pose == null) continue;
                        var d = (pose.Value.Pos - c.Pos).Length;
                        if (d < best) { best = d; ziel = pose.Value.Pos; }
                    }
                }
                else if (itCode.Length > 0 && lauf.PeerAt(itCode, nowMs) is RmpInterp.Pose it)
                {
                    var weg = c.Pos - it.Pos;
                    var fern = weg.Length;
                    hetzen = fern < 18.0;
                    var richtung = fern > 0.01 ? weg / fern : new V2(1f, 0f);
                    var flucht = c.Pos + richtung * 20.0;
                    // keep inside the meadow: lean toward the centre near the fence
                    var mitte = k.ArenaCenter - c.Pos;
                    if (mitte.Length > k.ArenaRadius * 0.6) flucht = flucht + mitte * 0.6;
                    ziel = flucht;
                }
            }
            if (ziel == null)
            {
                c.SteerInput(0.0);
                while (c.Gait != "stand" && c.Gait != "schritt") { var g = c.Gait; c.GaitDown(); if (c.Gait == g) break; }
                return;
            }
            var delta = RideFeel.WrapAngle(RmpKurse.HeadingTo(c.Pos, ziel.Value) - c.Heading);
            c.SteerInput(RideFeel.Clamp(-delta * 2.2, -1.0, 1.0));
            var abstand = (ziel.Value - c.Pos).Length;
            var wunsch = !hetzen ? "trab" : Math.Abs(delta) > 1.1 ? "trab" : c.Ausdauer > 30.0 || abstand > 40.0 ? "galopp" : "trab";
            for (var i = 0; i < 4 && c.Gait != wunsch; i++)
            {
                var vorher = c.Gait;
                if (Rang(wunsch) > Rang(vorher)) c.GaitUp();
                else c.GaitDown();
                if (c.Gait == vorher) break;
            }
        }

        private static int Rang(string gait) => Array.IndexOf(RideFeel.Gangarten, gait);
    }
}
