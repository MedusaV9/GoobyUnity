using System;
using System.Collections.Generic;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Riding feel maths of ride_feel.gd (RANCH-2) — pure: a gentle "vehicle" with walk/trot/gallop (tölt for horses that
    /// may), low-pass steering, asymmetric acceleration, jump physics and a stamina tank. The obstacle course and
    /// <see cref="RideController"/> consume only these functions.
    /// </summary>
    public static class RideFeel
    {
        public static readonly string[] Gangarten = { "stand", "schritt", "trab", "galopp" };

        public static readonly IReadOnlyDictionary<string, double> Tempo = new Dictionary<string, double>
        {
            ["stand"] = 0.0, ["schritt"] = 1.7, ["trab"] = 4.2, ["galopp"] = 8.5, ["toelt"] = 5.8,
        };

        public const double AccelAuf = 3.0, AccelAb = 5.5;

        public static readonly IReadOnlyDictionary<string, double> AccelJeGangart = new Dictionary<string, double>
        {
            ["schritt"] = 2.0, ["trab"] = 2.8, ["galopp"] = 3.0, ["toelt"] = 3.0,
        };

        public const double KickAccel = 4.5, KickDauerS = 0.8;
        public const double SteerSmoothTauS = 0.14, SteerRate = 1.7, SteerRateCapRadS = 100.0 * Math.PI / 180.0, SteerSpeedDamp = 0.3;
        public const double SprungVy = 4.8, Gravitation = -11.5, SprungMinTempo = 3.0;
        public const double AusdauerMax = 100.0, AusdauerGaloppProS = 7.0, AusdauerRegenProS = 9.0, AusdauerToeltProS = 3.0, AusdauerGaloppAb = 20.0;
        public const string AusdauerLeerGangart = "trab";
        public const double ErschoepftSchnaubenS = 2.0;
        public const double ZweiterWindAb = 10.0, ZweiterWindBonus = 25.0, ZweiterWindMaxTempo = 0.2;
        public const double SprungPerfektVonM = 0.9, SprungPerfektBisM = 1.3, SprungGutVonM = 0.5, SprungGutBisM = 1.9;
        public const int SprungPerfektPunkte = 15;
        public const double LandungsKickS = 0.15;
        public const double CamPosLerpK = 4.5;

        public static readonly IReadOnlyDictionary<string, double> UntergrundTempo = new Dictionary<string, double>
        {
            ["wiese"] = 1.0, ["sand"] = 1.0, ["holz"] = 1.0, ["stein"] = 1.0, ["wasser"] = 0.85, ["matsch"] = 0.9, ["schnee"] = 1.0,
        };

        /// <summary>Stride frequency (Hz) per gait — drives legs, head nod and hoof beats.</summary>
        public static readonly IReadOnlyDictionary<string, double> NickHz = new Dictionary<string, double>
        {
            ["stand"] = 0.35, ["schritt"] = 1.4, ["trab"] = 2.4, ["galopp"] = 1.9, ["toelt"] = 2.8,
        };

        public static readonly IReadOnlyDictionary<string, double> Staub = new Dictionary<string, double>
        {
            ["stand"] = 0.0, ["schritt"] = 0.0, ["trab"] = 0.35, ["galopp"] = 1.0, ["toelt"] = 0.5,
        };

        public static double FlugzeitS => 2.0 * SprungVy / -Gravitation;
        public static double HoeheM => SprungVy * SprungVy / (2.0 * -Gravitation);
        public static double WeiteM(double tempo) => Math.Max(0.0, tempo) * FlugzeitS;

        public static double WrapAngle(double a)
        {
            while (a > Math.PI) a -= 2.0 * Math.PI;
            while (a <= -Math.PI) a += 2.0 * Math.PI;
            return a;
        }

        public static string GangartHoch(string gangart)
        {
            var i = Array.IndexOf(Gangarten, gangart);
            return Gangarten[Math.Min(Gangarten.Length - 1, Math.Max(0, i) + 1)];
        }

        public static string GangartRunter(string gangart)
        {
            var i = Array.IndexOf(Gangarten, gangart);
            return Gangarten[Math.Max(0, (i >= 0 ? i : 1) - 1)];
        }

        public static double Zieltempo(string gangart, double tempoMult = 1.0)
        {
            var basis = Of(Tempo, gangart);
            return gangart == "galopp" ? basis * tempoMult : basis;
        }

        public static double StepTempo(double tempo, double ziel, double dt)
        {
            var accel = tempo < ziel ? AccelAuf : AccelAb;
            return tempo + Math.Sign(ziel - tempo) * Math.Min(Math.Abs(ziel - tempo), accel * dt);
        }

        /// <summary>One frame-rate independent low-pass step of the steering input.</summary>
        public static double SmoothSteer(double smoothed, double target, double dt) =>
            !(dt > 0.0) ? smoothed : smoothed + (target - smoothed) * (1.0 - Math.Exp(-dt / SteerSmoothTauS));

        public static double SpeedDamp(double tempo) => 1.0 - SteerSpeedDamp * Math.Min(1.0, tempo / Tempo["galopp"]);

        /// <summary>Commanded yaw rate (rad/s), capped; a standing horse does not turn on the spot.</summary>
        public static double SteerYawRate(double smoothedSteer, double tempo)
        {
            if (tempo < 0.2) return 0.0;
            return Clamp(smoothedSteer * SteerRate * SpeedDamp(tempo), -SteerRateCapRadS, SteerRateCapRadS);
        }

        public static bool KannSpringen(double tempo, double y) => tempo >= SprungMinTempo && y <= 0.001;

        /// <summary>One jump physics step {y, vy}; clamped to the ground (y = 0, vy = 0).</summary>
        public static (double y, double vy) StepSprung((double y, double vy) s, double dt)
        {
            var vy = s.vy + Gravitation * dt;
            var y = s.y + vy * dt;
            return y <= 0.0 ? (0.0, 0.0) : (y, vy);
        }

        public static double StepAusdauer(double ausdauer, string gangart, double dt, double regenMult = 1.0)
        {
            var a = ausdauer;
            if (gangart == "galopp") a -= AusdauerGaloppProS * dt;
            else if (gangart != "trab") a += AusdauerRegenProS * Math.Max(0.0, regenMult) * dt;
            return Clamp(a, 0.0, AusdauerMax);
        }

        public static string GangartNachAusdauer(string gewuenscht, double ausdauer, bool warGalopp)
        {
            if (gewuenscht != "galopp") return gewuenscht;
            if (ausdauer <= 0.0) return AusdauerLeerGangart;
            if (!warGalopp && ausdauer < AusdauerGaloppAb) return "trab";
            return "galopp";
        }

        public static double SchrittHz(string gangart) => Of(NickHz, gangart);
        public static double StaubAnteil(string gangart) => Of(Staub, gangart);
        public static double CamFollowFactor(double dt) => 1.0 - Math.Exp(-CamPosLerpK * Math.Max(0.0, dt));

        /// <summary>Soft clamp of a position into a rectangle (centre ± half, minus a 0.6 m rim).</summary>
        public static V2 ClampBounds(V2 pos, V2 center, V2 half, double rand = 0.6)
        {
            var hx = Math.Max(0.5, half.X - rand);
            var hz = Math.Max(0.5, half.Y - rand);
            return new V2(Clamp(pos.X, center.X - hx, center.X + hx), Clamp(pos.Y, center.Y - hz, center.Y + hz));
        }

        internal static double Of(IReadOnlyDictionary<string, double> table, string key) =>
            key != null && table.TryGetValue(key, out var v) ? v : 0.0;

        internal static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>
    /// Stat layer of the riding feel (ride_stats.gd, RW-2) — pure: the five training values (1–20, 10 = base) turn into
    /// target tempo, gallop kick, tölt, steering, stamina tank, jump power and jump timing. An empty stats table is the
    /// base horse every formula falls back to.
    /// </summary>
    public static class RideStats
    {
        public static readonly string[] GangartenToelt = { "stand", "schritt", "trab", "toelt", "galopp" };
        public static readonly IReadOnlyDictionary<string, double> Leer = new Dictionary<string, double>();

        public static double Stat(IReadOnlyDictionary<string, double> stats, string key) =>
            RideFeel.Clamp(stats != null && stats.TryGetValue(key, out var v) ? v : 10.0, 1.0, 20.0);

        public static string GangartHoch(string gangart, bool kannToelt = false)
        {
            if (!kannToelt) return RideFeel.GangartHoch(gangart == "toelt" ? "trab" : gangart);
            var i = Array.IndexOf(GangartenToelt, gangart);
            return GangartenToelt[Math.Min(GangartenToelt.Length - 1, Math.Max(0, i) + 1)];
        }

        public static string GangartRunter(string gangart, bool kannToelt = false)
        {
            if (!kannToelt) return RideFeel.GangartRunter(gangart == "toelt" ? "galopp" : gangart);
            var i = Array.IndexOf(GangartenToelt, gangart);
            return GangartenToelt[Math.Max(0, (i >= 0 ? i : 1) - 1)];
        }

        /// <summary>Gallop target = 8.5 · (1 + 0.015·(tempo−10)) · bond perk; the other gaits keep their base tempo.</summary>
        public static double Zieltempo(string gangart, IReadOnlyDictionary<string, double> stats, double tempoMult = 1.0)
        {
            if (gangart != "galopp") return RideFeel.Zieltempo(gangart);
            return RideFeel.Tempo["galopp"] * (1.0 + 0.015 * (Stat(stats, "tempo") - 10.0)) * Math.Max(0.0, tempoMult);
        }

        public static double AccelAuf(string gangart, double kickRestS)
        {
            if (gangart == "galopp" && kickRestS > 0.0) return RideFeel.KickAccel;
            return RideFeel.AccelJeGangart.TryGetValue(gangart ?? "", out var a) ? a : RideFeel.AccelAuf;
        }

        public static double KickDauerS(IReadOnlyDictionary<string, double> effekte) =>
            effekte != null && effekte.TryGetValue("kick_dauer_s", out var v) ? v : RideFeel.KickDauerS;

        public static double StepTempo(double tempo, double ziel, double dt, double auf, double bremsBonus = 0.0)
        {
            var accel = tempo < ziel ? auf : RideFeel.AccelAb + Math.Max(0.0, bremsBonus);
            return tempo + Math.Sign(ziel - tempo) * Math.Min(Math.Abs(ziel - tempo), accel * dt);
        }

        public static double AusdauerMax(IReadOnlyDictionary<string, double> stats) => 100.0 + 5.0 * (Stat(stats, "ausdauer") - 10.0);

        public static double GaloppVerbrauch(IReadOnlyDictionary<string, double> stats) =>
            RideFeel.AusdauerGaloppProS * (1.0 - 0.01 * (Stat(stats, "ausdauer") - 10.0));

        public static double StepAusdauer(double ausdauer, string gangart, double dt, IReadOnlyDictionary<string, double> stats, double regenMult = 1.0)
        {
            var a = ausdauer;
            if (gangart == "galopp") a -= GaloppVerbrauch(stats) * dt;
            else if (gangart == "toelt") a -= RideFeel.AusdauerToeltProS * dt;
            else if (gangart != "trab") a += RideFeel.AusdauerRegenProS * Math.Max(0.0, regenMult) * dt;
            return RideFeel.Clamp(a, 0.0, AusdauerMax(stats));
        }

        public static string GangartNachAusdauer(string gewuenscht, double ausdauer, bool warSchnell)
        {
            if (gewuenscht != "galopp" && gewuenscht != "toelt") return gewuenscht;
            if (ausdauer <= 0.0) return RideFeel.AusdauerLeerGangart;
            if (!warSchnell && ausdauer < RideFeel.AusdauerGaloppAb) return "trab";
            return gewuenscht;
        }

        /// <summary>Yaw rate with agility: STEER_RATE · (1 + 0.02·(W−10)), damping 0.3 − 0.006·(W−10), 100°/s cap.</summary>
        public static double SteerYawRate(double smoothedSteer, double tempo, IReadOnlyDictionary<string, double> stats)
        {
            if (tempo < 0.2) return 0.0;
            var w = Stat(stats, "wendigkeit");
            var rate = RideFeel.SteerRate * (1.0 + 0.02 * (w - 10.0));
            var dampStaerke = RideFeel.SteerSpeedDamp - 0.006 * (w - 10.0);
            var damp = 1.0 - dampStaerke * Math.Min(1.0, tempo / RideFeel.Tempo["galopp"]);
            return RideFeel.Clamp(smoothedSteer * rate * damp, -RideFeel.SteerRateCapRadS, RideFeel.SteerRateCapRadS);
        }

        public static double SprungVy(IReadOnlyDictionary<string, double> stats) => RideFeel.SprungVy + 0.06 * (Stat(stats, "sprungkraft") - 10.0);

        public static double ScheuChance(IReadOnlyDictionary<string, double> stats, double scheuMult = 1.0) =>
            Math.Max(0.0, 0.15 * (1.0 - 0.05 * (Stat(stats, "gelassenheit") - 10.0)) * Math.Max(0.0, scheuMult));

        /// <summary>Takeoff distance in front of the obstacle → "perfekt" | "gut" | "daneben"; a bonus widens only the perfect window.</summary>
        public static string SprungWertung(double distM, double tempo, double bonusMs = 0.0)
        {
            var extra = Math.Max(0.0, tempo) * Math.Max(0.0, bonusMs) / 1000.0 * 0.5;
            if (distM >= RideFeel.SprungPerfektVonM - extra && distM <= RideFeel.SprungPerfektBisM + extra) return "perfekt";
            if (distM >= RideFeel.SprungGutVonM && distM <= RideFeel.SprungGutBisM) return "gut";
            return "daneben";
        }

        public static double UntergrundTempoMult(string untergrund, double malusMult = 1.0)
        {
            var basis = RideFeel.UntergrundTempo.TryGetValue(untergrund ?? "", out var v) ? v : 1.0;
            return 1.0 - (1.0 - basis) * RideFeel.Clamp(malusMult, 0.0, 1.0);
        }

        public static bool ZweiterWindMoeglich(double ausdauer, double tempo, bool schonBenutzt) =>
            !schonBenutzt && ausdauer < RideFeel.ZweiterWindAb && Math.Abs(tempo) <= RideFeel.ZweiterWindMaxTempo;

        public static double Lenkassistent(double steer, double deltaIdeal, double staerke)
        {
            var s = RideFeel.Clamp(staerke, 0.0, 0.3);
            return RideFeel.Clamp(steer + (deltaIdeal - steer) * s, -1.0, 1.0);
        }

        /// <summary>Bond level 1..10 (10 points each) — level 3 unlocks the sliding stop, level 6 the bigger second wind.</summary>
        public static int BindungsLevel(double bindung) => (int)RideFeel.Clamp(1 + Math.Floor(Math.Max(0.0, bindung) / 10.0), 1, 10);

        public static double BremsBonus(double bindung) => BindungsLevel(bindung) >= 3 ? 1.5 : 0.0;

        public static double ZweiterWindBonus(double bindung) => BindungsLevel(bindung) >= 6 ? 35.0 : RideFeel.ZweiterWindBonus;
    }

    /// <summary>
    /// The ride controller of ride_controller.gd without nodes: Gooby rides like a gentle vehicle — gait up/down (stamina
    /// gates the gallop, a kick fires when galloping off), low-pass steering, jumps with timing against the obstacles in
    /// front. Forward is −(sin h, cos h) in (x, z); steering right lowers the heading. Positions are float32 like Godot's.
    /// </summary>
    public sealed class RideController
    {
        public string Gait { get; private set; } = "stand";
        public double Tempo { get; set; }
        public double Heading { get; set; }
        public double Ausdauer { get; set; } = RideFeel.AusdauerMax;
        public bool Active { get; set; } = true;
        public string Untergrund { get; set; } = "wiese";
        public V2 Pos { get; set; }
        /// <summary>Scales every gait's tempo (1 = the arena feel of the competitions).</summary>
        public double TempoScale { get; set; } = 1.0;
        /// <summary>No stamina drain — the open-world riding of ranch_welt_reiter.gd has none.</summary>
        public bool Ausdauerfrei { get; set; }
        public double Y => _sprungY;
        public bool InLuft { get; private set; }
        public double SteerTarget => _steerTarget;
        public double SteerSmoothed => _steer;
        public double ErschoepftRest => _erschoepftRest;
        public double LandungRest => _landungRest;
        public double KickRest => _kickRest;
        public double Bindung => _bindung;
        public bool ZweiterWindBenutzt => _zweiterWindBenutzt;
        public double Zeit { get; private set; }

        public event Action<string> GaitChanged;
        public event Action Jumped, Landed, Erschoepft;
        public event Action<string, int> SprungGewertet;
        public event Action<double> ZweiterWindGenutzt;

        private double _steerTarget, _steer, _sprungY, _sprungVy;
        private V2 _boundsCenter = V2.Zero, _boundsHalf = new V2(15f, 15f);
        private double _perkTempo = 1.0, _perkRegen = 1.0, _bindung = 50.0;
        private IReadOnlyDictionary<string, double> _stats = RideStats.Leer;
        private bool _kannToelt, _zweiterWindBenutzt;
        private double _kickRest, _erschoepftRest, _landungRest;
        private readonly List<V2> _hindernisse = new List<V2>();

        public void SetBounds(V2 center, V2 half)
        {
            _boundsCenter = center;
            _boundsHalf = half;
        }

        /// <summary>Bond of the ridden horse → tempo/stamina perks (RanchHorseCare.reit_perks) and the bond unlocks.</summary>
        public void SetBindung(double bindung)
        {
            _bindung = RideFeel.Clamp(bindung, 0.0, 100.0);
            var f = Math.Max(0.0, (_bindung - 45.0) / 55.0);
            _perkTempo = 1.0 + 0.06 * f;
            _perkRegen = 1.0 + 0.25 * f;
        }

        public void SetStats(IReadOnlyDictionary<string, double> stats, bool kannToelt = false)
        {
            _stats = stats ?? RideStats.Leer;
            _kannToelt = kannToelt;
            Ausdauer = RideStats.AusdauerMax(_stats);
        }

        public void SetHindernisse(IEnumerable<V2> punkte)
        {
            _hindernisse.Clear();
            if (punkte != null) _hindernisse.AddRange(punkte);
        }

        public void SteerInput(double value) => _steerTarget = RideFeel.Clamp(value, -1.0, 1.0);
        public void GaitUp() => SetzeGangart(RideStats.GangartHoch(Gait, _kannToelt));
        public void GaitDown() => SetzeGangart(RideStats.GangartRunter(Gait, _kannToelt));

        /// <summary>Forces a gait (tests, the start of a run); stamina still gates the gallop.</summary>
        public void SetGait(string gait) => SetzeGangart(gait);

        /// <summary>Takeoff — only from trot tempo and on the ground; rated against the next obstacle in front.</summary>
        public bool Jump()
        {
            if (!RideFeel.KannSpringen(Tempo, _sprungY)) return false;
            _sprungY = 0.001;
            _sprungVy = RideStats.SprungVy(_stats);
            InLuft = true;
            Jumped?.Invoke();
            WerteSprung();
            return true;
        }

        public bool ZweiterWindBereit => RideStats.ZweiterWindMoeglich(Ausdauer, Tempo, _zweiterWindBenutzt);

        /// <summary>Second wind: once per ride, tank below 10, standing — +25 (+35 from bond level 6).</summary>
        public bool ZweiterWind()
        {
            if (!ZweiterWindBereit) return false;
            _zweiterWindBenutzt = true;
            var bonus = RideStats.ZweiterWindBonus(_bindung);
            Ausdauer = RideFeel.Clamp(Ausdauer + bonus, 0.0, RideStats.AusdauerMax(_stats));
            ZweiterWindGenutzt?.Invoke(bonus);
            return true;
        }

        public double AusdauerMaxWert => RideStats.AusdauerMax(_stats);

        /// <summary>Distance (m) to the next obstacle ahead (±2 m sideways), −1 when none.</summary>
        public double NaechstesHindernisM() => HindernisAbstand();

        public V2 Vorwaerts => new V2(Math.Sin(Heading), Math.Cos(Heading)) * -1.0;

        public void Step(double dt)
        {
            if (!Active || dt <= 0.0) return;
            Zeit += dt;
            _kickRest = Math.Max(0.0, _kickRest - dt);
            _erschoepftRest = Math.Max(0.0, _erschoepftRest - dt);
            _landungRest = Math.Max(0.0, _landungRest - dt);
            var warLeer = Ausdauer <= 0.0;
            if (!Ausdauerfrei) Ausdauer = RideStats.StepAusdauer(Ausdauer, Gait, dt, _stats, _perkRegen);
            if (Ausdauer <= 0.0 && !warLeer)
            {
                _erschoepftRest = RideFeel.ErschoepftSchnaubenS;
                Erschoepft?.Invoke();
            }
            var effektiv = RideStats.GangartNachAusdauer(Gait, Ausdauer, IstSchnell(Gait));
            if (effektiv != Gait)
            {
                Gait = effektiv;
                GaitChanged?.Invoke(Gait);
            }
            _steer = RideFeel.SmoothSteer(_steer, _steerTarget, dt);
            Heading = RideFeel.WrapAngle(Heading - RideStats.SteerYawRate(_steer, Tempo, _stats) * dt);
            var ziel = RideStats.Zieltempo(Gait, _stats, _perkTempo) * RideStats.UntergrundTempoMult(Untergrund) * TempoScale;
            Tempo = RideStats.StepTempo(Tempo, ziel, dt, RideStats.AccelAuf(Gait, _kickRest), RideStats.BremsBonus(_bindung));
            var pos = Pos + Vorwaerts * Tempo * dt;
            Pos = RideFeel.ClampBounds(pos, _boundsCenter, _boundsHalf);
            if (InLuft)
            {
                var s = RideFeel.StepSprung((_sprungY, _sprungVy), dt);
                _sprungY = s.y;
                _sprungVy = s.vy;
                if (_sprungY <= 0.0)
                {
                    InLuft = false;
                    _landungRest = RideFeel.LandungsKickS;
                    Landed?.Invoke();
                }
            }
        }

        private void SetzeGangart(string neu)
        {
            var effektiv = RideStats.GangartNachAusdauer(neu, Ausdauer, IstSchnell(Gait));
            if (effektiv == Gait) return;
            if (effektiv == "galopp" && Gait != "galopp") _kickRest = RideFeel.KickDauerS;
            Gait = effektiv;
            GaitChanged?.Invoke(Gait);
        }

        private void WerteSprung()
        {
            var beste = HindernisAbstand();
            if (beste < 0.0 || beste > 6.0) return;
            var wertung = RideStats.SprungWertung(beste, Tempo);
            SprungGewertet?.Invoke(wertung, wertung == "perfekt" ? RideFeel.SprungPerfektPunkte : 0);
        }

        private double HindernisAbstand()
        {
            var vorwaerts = Vorwaerts;
            var beste = -1.0;
            foreach (var punkt in _hindernisse)
            {
                var d = punkt - Pos;
                double entlang = d.Dot(vorwaerts);
                if (entlang > 0.0 && Math.Abs(d.Cross(vorwaerts)) < 2.0f && (beste < 0.0 || entlang < beste)) beste = entlang;
            }
            return beste;
        }

        private static bool IstSchnell(string g) => g == "galopp" || g == "toelt";
    }
}
