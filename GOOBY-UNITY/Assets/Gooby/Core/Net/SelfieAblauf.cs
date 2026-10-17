using System;
using System.Collections.Generic;

namespace Gooby.Core.Net
{
    /// <summary>
    /// "Snap A Gooby!" — the visit selfie's countdown (port of snap_a_gooby.gd's Ablauf): a short pose beat, then 3, 2, 1
    /// one second apart, then the click. Pure and time-injected: <see cref="Takt"/> returns this step's events
    /// ("zeige_3", "zeige_2", "zeige_1", "ausloesen").
    /// </summary>
    public sealed class SelfieAblauf
    {
        public const double PoseBeatS = 0.8, SekundeS = 1.0;
        public const int CountdownStart = 3;
        /// <summary>The relay emote the friend's side sees (the remote Gooby lifts the phone too).</summary>
        public const string RelayEmote = "selfie";

        /// <summary>pose → countdown → fertig</summary>
        public string Phase { get; private set; } = "pose";
        public int Countdown { get; private set; } = CountdownStart;

        private double _rest = PoseBeatS;

        public List<string> Takt(double dt)
        {
            var events = new List<string>();
            if (Phase == "fertig") return events;
            _rest -= Math.Max(dt, 0.0);
            while (_rest <= 0.0)
            {
                if (Phase == "pose")
                {
                    Phase = "countdown";
                    events.Add("zeige_" + Countdown);
                    _rest += SekundeS;
                }
                else if (Countdown > 1)
                {
                    Countdown--;
                    events.Add("zeige_" + Countdown);
                    _rest += SekundeS;
                }
                else
                {
                    Phase = "fertig";
                    events.Add("ausloesen");
                    break;
                }
            }
            return events;
        }
    }
}
