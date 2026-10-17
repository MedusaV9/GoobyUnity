using System;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The ranch's random events (ranch_event_host.gd): at the farm the shared event engine rolls with context "ranch"
    /// (only once the ranch is bought; one event at a time, windows, cooldowns, timeouts from the engine). The scene
    /// plays on the farm — catch the escaped horse, shoo the three crows off the hay, visit the farrier at the gate,
    /// pick up the carrots that rained onto the meadow — and the reward lands via <see cref="RanchEvents"/>.
    /// </summary>
    public sealed partial class RanchHofScreen
    {
        private JObject _eventDef;
        private int _eventKarotten;

        private static int MinutenDesTags(long nowMs) { var l = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToLocalTime(); return l.Hour * 60 + l.Minute; }

        private void RanchEreignis()
        {
            if (!_gekauft || _eventDef != null || Root == null) return;
            var now = Now;
            var fail = "";
            _host.Store.Update(s =>
            {
                RandomEvents.RollOnStart(s, RandomEvents.Defs(), now, MinutenDesTags(now), UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, RanchEvents.Context);
                fail = RandomEvents.TakeFailNotice(s);
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged(RandomEvents.SliceId);
            var def = AktivesRanchEreignis();
            if (def != null) EreignisStarten(def);
            else if (fail != "") Sag(fail);
        }

        private JObject AktivesRanchEreignis()
        {
            var active = RandomEvents.Active(State);
            if (active == null || !active.HasValues || RandomEvents.IsTimedOut(active, Now)) return null;
            var def = RandomEvents.DefById(RandomEvents.Defs(), J.Str(active["id"]));
            return def != null && J.Str(def["context"]) == RanchEvents.Context ? def : null;
        }

        private void EreignisStarten(JObject def)
        {
            _eventDef = def;
            var id = J.Str(def["id"]);
            switch (id)
            {
                case "ausgebuext":
                    var pferd = RanchHof.PferdIds(State).FirstOrDefault(p => p != _sel && RanchZucht.Reitbar(RanchZucht.Alter(RanchHof.Pferd(State, p), Now, RanchBauEffekte.AufzuchtTempo(State))));
                    if (pferd == null || _stage == null || !_stage.Ausbuexen(pferd)) { _eventDef = null; return; }
                    Sfx.Play("ranch_wiehern_b", 0f, 0.9f);
                    break;
                case "heudieb":
                    _stage?.Kraehen(Math.Max(1, J.Int(def["props"], 3)));
                    break;
                case "hufschmied":
                    _stage?.Hufschmied(true);
                    break;
                case "karottenregen":
                    _eventKarotten = 0;
                    _stage?.Karottenregen(Math.Max(1, J.Int(def["props"], 10)));
                    break;
            }
            Sfx.PlayUi("soft_open");
            _host.Ui.Toast(T("rhof.event." + id + ".start"), "sparkle");
            Sag(T("rhof.event." + id + ".hinweis"));
            Debug.Log("[ranchhof] ereignis " + id);
        }

        /// <summary>A tap on an event piece; true when it belonged to the running event.</summary>
        private bool EreignisTippen(RanchHofStage.Ziel ziel, string id, int index)
        {
            if (_eventDef == null || _stage == null) return false;
            switch (ziel)
            {
                case RanchHofStage.Ziel.Pferd when id == _stage.Ausgebuext:
                    _stage.Einfangen();
                    Sfx.Play("ranch_schnauben_a", 0f, 0.8f);
                    EreignisGeschafft(id);
                    return true;
                case RanchHofStage.Ziel.Kraehe:
                    if (!_stage.Verscheuchen(index)) return true;
                    Sfx.Play("game_whoosh", 3f, 0.7f);
                    if (_stage.KraehenUebrig == 0) EreignisGeschafft(null);
                    return true;
                case RanchHofStage.Ziel.Schmied:
                    Sfx.Play("build_hammer", 2f, 0.7f);
                    _stage.Hufschmied(false);
                    QuestMelde("pflege", "hufe", Pferde().Count);
                    EreignisGeschafft(null);
                    return true;
                case RanchHofStage.Ziel.Karotte:
                    var at = FieldPoint(_stage.KarottePunkt(index) + Vector3.up * 0.4f);
                    if (!_stage.Aufsammeln(index)) return true;
                    _eventKarotten++;
                    Sfx.PlayUi("soft_collect");
                    _host.Ui.FloatText(at, "+1", new Color(0.95f, 0.6f, 0.25f), 26f);
                    if (_stage.KarottenUebrig == 0) EreignisGeschafft(null);
                    return true;
            }
            return false;
        }

        private void EreignisGeschafft(string pferdId)
        {
            var def = _eventDef;
            if (def == null) return;
            _eventDef = null;
            var now = Now;
            var gesammelt = _eventKarotten;
            JObject res = null;
            _host.Store.Update(s =>
            {
                RandomEvents.Resolve(s, RandomEvents.Defs());
                res = RanchEvents.Belohnen(s, def, pferdId, now, gesammelt);
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged(RandomEvents.SliceId);
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("economy");
            _host.Store.NotifySliceChanged("inventory");
            var id = J.Str(def["id"]);
            _juice.Confetti(50);
            Sfx.PlayUi("soft_levelup");
            Sag(T("rhof.event." + id + ".geschafft", ("name", pferdId != null ? PferdName(pferdId) : ""), ("n", J.Int(res?["karotten"]))));
            if (J.Int(res?["muenzen"]) > 0) _host.Ui.FlyIcons(Root.worldBound.center, _coinPill, "coin", 5, _ => Sfx.PlayUi("soft_tick"));
            Debug.Log("[ranchhof] ereignis geschafft " + id + " " + res);
            RefreshAll();
        }

        /// <summary>Time ran out: the scene clears, the fail line shows.</summary>
        private void EreignisPruefen()
        {
            if (_eventDef == null) return;
            var active = RandomEvents.Active(State);
            if (active != null && active.HasValues && !RandomEvents.IsTimedOut(active, Now)) return;
            var fail = "";
            _host.Store.Update(s =>
            {
                if (RandomEvents.IsTimedOut(RandomEvents.Active(s), Now)) RandomEvents.Fail(s, RandomEvents.Defs());
                fail = RandomEvents.TakeFailNotice(s);
            }, GameHost.TicksMs);
            _eventDef = null;
            _stage?.EventAufraeumen();
            if (fail != "") Sag(fail);
        }

        /// <summary>QA: "ranch event &lt;id&gt;" starts a ranch event now.</summary>
        private void QaEreignis(string id)
        {
            var def = RandomEvents.DefById(RandomEvents.Defs(), id);
            if (def == null || J.Str(def["context"]) != RanchEvents.Context) return;
            _stage?.EventAufraeumen();
            _eventDef = null;
            var now = Now;
            _host.Store.Update(s => RandomEvents.Activate(s, def, now), GameHost.TicksMs);
            EreignisStarten(def);
        }
    }
}
