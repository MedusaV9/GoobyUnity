using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    public sealed partial class RanchWeltScreen
    {
        /// <summary>Training by doing is booked every three minutes on the way and when the ride ends.</summary>
        private const double TrainingAbS = 180.0;

        private RanchPferd.Telemetrie _tele = new RanchPferd.Telemetrie();
        private double _laune = -1.0;
        private bool _regenGesagt;

        /// <summary>What the ride trains so far (QA).</summary>
        public RanchPferd.Telemetrie Telemetrie => _tele;

        /// <summary>ride_controller's telemetry: moving time, gallop metres, a happy walk — the ridden horse learns from it.</summary>
        private void SammleTraining(float dt)
        {
            if (_welt == null || !_welt.Bereit || string.IsNullOrEmpty(_pferdId)) return;
            if (_laune < 0.0)
            {
                var p = RanchHof.Pferd(State, _pferdId);
                _laune = p != null ? RanchCare.Laune(J.Dict(p["werte"]), J.NumOr(p["bindung"], 0.0)) : 0.0;
            }
            _tele.Sammle(dt, _welt.Gangart, _welt.Tempo, _laune);
            if (_tele.UnterwegsS >= TrainingAbS) BucheTraining(false);
        }

        /// <summary>
        /// Books the ride onto the horse: new training points and levels get their toast; in the rain without a riding
        /// hall the stat XP is halved (once per ride the hint says why), with the hall the training goes on dry.
        /// </summary>
        private void BucheTraining(bool ende)
        {
            if (string.IsNullOrEmpty(_pferdId) || _tele.Leer) return;
            var tele = _tele;
            _tele = new RanchPferd.Telemetrie();
            var regen = _wetter?.Zustand != null && _wetter.Zustand.Regnet;
            var halle = RanchBauEffekte.TrainingBeiRegen(State);
            var mult = regen && !halle ? RanchPferd.RegenStatMult : 1.0;
            var tag = Today;
            var pid = _pferdId;
            RanchPferd.RittBuchung b = null;
            _host.Store.Update(s => b = RanchPferd.RittAnPferd(s, pid, tele, tag, mult), GameHost.TicksMs);
            if (b == null) return;
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            var name = PferdName(pid);
            Debug.Log("[ranchwelt] training " + pid + " unterwegs=" + Mathf.RoundToInt((float)tele.UnterwegsS) + "s galopp=" + Mathf.RoundToInt((float)tele.GaloppM) + "m xp+" + Mathf.RoundToInt((float)b.Xp)
                + (b.NeuePunkte.Count > 0 ? " punkte " + string.Join(",", b.NeuePunkte.Select(kv => kv.Key + "+" + kv.Value)) : "") + (mult < 1.0 ? " (regen ×0,5)" : regen ? " (reithalle)" : ""));
            if (regen && !_regenGesagt)
            {
                _regenGesagt = true;
                _host.Ui.Toast(T(halle ? "rpferd.training.halle" : "rpferd.training.regen", ("name", name)), halle ? "home" : "rain", 4f);
            }
            foreach (var kv in b.NeuePunkte)
            {
                Sfx.PlayUi("soft_levelup");
                var wert = RanchPferd.StatWert(RanchHof.Pferd(State, pid), kv.Key);
                _host.Ui.Toast(T("rpferd.levelup.punkt", ("stat", T("rpferd.stat." + kv.Key))) + " · " + T("rpferd.training.wert", ("stat", T("rpferd.stat." + kv.Key)), ("wert", wert)), "star", 3.5f);
            }
            foreach (var level in b.LevelUps)
            {
                Sfx.PlayUi("soft_win");
                _juice.Confetti(60);
                _host.Ui.Toast(T("rpferd.levelup.titel", ("level", level)) + " " + T("rpferd.levelup.untertitel", ("name", name)), "trophy", 4f);
            }
            if (ende && b.Xp >= 1.0 && b.NeuePunkte.Count == 0 && b.LevelUps.Count == 0)
                _host.Ui.Toast(T("rpferd.training.xp", ("name", name), ("n", Mathf.RoundToInt((float)b.Xp))), "horseshoe");
        }

        private string PferdName(string id)
        {
            var p = RanchHof.Pferd(State, id);
            if (p == null) return "";
            var key = J.Str(p["nameKey"]);
            return key.Length > 0 ? T(key) : J.Str(p["name"]);
        }
    }
}
