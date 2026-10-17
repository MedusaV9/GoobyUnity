using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The quests on the farm (rnpc_manager.gd + rquest_log_ui.gd): the farm's people stand in the yard with a "!" when
    /// they have something for you and a "?" when you can hand something in; a tap opens their sheet (hearts, a line,
    /// chat, gifts, their quests). The header's quest chip opens the log, the tracker under it shows the pinned goal.
    /// Farm events (care, mowing, picking, the stable, the trough, the hay, patting a horse) count for the quests.
    /// </summary>
    public sealed partial class RanchHofScreen
    {
        private Button _questChip;
        private Label _questBadge, _trackerText;
        private VisualElement _tracker;
        private readonly Dictionary<string, Label> _marken = new Dictionary<string, Label>();

        private void BaueQuests(VisualElement head, VisualElement root)
        {
            _questChip = new Button(ZeigeQuests) { name = "ranchhof-quests" };
            _questChip.ClearClassList();
            _questChip.AddToClassList("gb-ranchhof-tagechip");
            _questChip.AddToClassList("gb-ranchhof-questchip");
            var ic = Ux.Icon("book");
            ic.pickingMode = PickingMode.Ignore;
            _questChip.Add(ic);
            _questBadge = Ux.Text("", "gb-ranchhof-tagechip-text");
            _questBadge.pickingMode = PickingMode.Ignore;
            _questChip.Add(_questBadge);
            head.Add(_questChip);
            _tracker = new Button(ZeigeQuests) { name = "ranchhof-tracker" };
            _tracker.ClearClassList();
            _tracker.AddToClassList("gb-rquest-tracker");
            var pin = Ux.Icon("pin");
            pin.pickingMode = PickingMode.Ignore;
            _tracker.Add(pin);
            _trackerText = Ux.Text("", "gb-rquest-tracker-text");
            _trackerText.pickingMode = PickingMode.Ignore;
            _tracker.Add(_trackerText);
            root.Add(_tracker);
            foreach (var npc in new[] { "rosi", "moehrchen", "timmi", "miez" })
            {
                var m = Ux.Text("", "gb-rquest-marke");
                m.name = "ranchhof-marke-" + npc;
                m.pickingMode = PickingMode.Ignore;
                m.style.visibility = Visibility.Hidden;
                root.Add(m);
                _marken[npc] = m;
            }
            _stage?.ZeigeLeute(RanchQuestUi.Look);
        }

        private void RefreshQuests()
        {
            if (_questChip == null) return;
            var s = State;
            var bereit = RanchQuests.MitStatus(s, RanchQuests.Erfuellbar).Count;
            var offen = RanchQuests.MitStatus(s, RanchQuests.Verfuegbar).Count;
            _questBadge.text = bereit > 0 ? bereit + "!" : offen.ToString();
            _questChip.EnableInClassList("gb-ranchhof-tagechip--bereit", bereit > 0);
            _questChip.style.display = _gekauft ? DisplayStyle.Flex : DisplayStyle.None;
            var q = RanchQuestUi.Verfolgt(s);
            _trackerText.text = RanchQuestUi.TrackerText(_host, Now);
            _tracker.style.display = _gekauft ? DisplayStyle.Flex : DisplayStyle.None;
            _tracker.EnableInClassList("gb-rquest-tracker--bereit", q != null && RanchQuests.Status(s, q) == RanchQuests.Erfuellbar);
            foreach (var kv in _marken) kv.Value.text = _gekauft ? RanchQuestUi.Marke(s, kv.Key) : "";
        }

        /// <summary>The markers float over their people (hidden under a sheet and at night).</summary>
        private void PlaceMarken()
        {
            if (_stage == null) return;
            foreach (var kv in _marken)
            {
                var kopf = _stage.LeuteKopf(kv.Key);
                var show = kopf.HasValue && _overlay == null && kv.Value.text.Length > 0;
                kv.Value.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
                if (!show) continue;
                var p = FieldPoint(kopf.Value);
                var bob = Reduced ? 0f : Mathf.Sin(Time.unscaledTime * 3f + kv.Key.Length) * 4f;
                kv.Value.style.left = p.x - 17f;
                kv.Value.style.top = p.y - 34f + bob;
                kv.Value.EnableInClassList("gb-rquest-marke--abgabe", kv.Value.text == "?");
            }
        }

        private void ZeigeQuests()
        {
            if (!_gekauft) { ZeigeTor(); return; }
            Sfx.PlayUi("soft_open");
            var kapitel = RanchQuests.Kapitel(State);
            var body = OpenSheet("ranchhof-questlog", "book", new Color(0.86f, 0.92f, 0.98f), T("rquest.log.titel"),
                kapitel > 10 ? T("rquest.ui.reihe_fertig") : T("rquest.log.kapitel", ("n", kapitel)));
            RanchQuestUi.Log(_host, body, RefreshQuests, _coinPill, "haupt");
        }

        private void ZeigePerson(string npc)
        {
            if (!_gekauft) { ZeigeTor(); return; }
            Sfx.PlayUi("soft_open");
            var body = OpenSheet("ranchhof-person", npc == "miez" ? "heart" : "user", new Color(0.98f, 0.88f, 0.76f), RanchQuestUi.Name(_host, npc), RanchQuestUi.Rolle(_host, npc));
            QuestMelde("sprich_mit", npc);
            RanchQuestUi.Person(_host, body, npc, () => { RefreshQuests(); RefreshAll(); }, _coinPill, n => QuestMelde("sprich_mit", n));
        }

        /// <summary>A farm event for the quests ("pflege striegeln", "gehe_zu stall", "sammle apfel 2").</summary>
        private void QuestMelde(string typ, string wert, int n = 1)
        {
            if (!_gekauft) return;
            if (RanchQuestUi.Melde(_host, typ, wert, n)) RefreshQuests();
        }

        private bool _hofGemeldet;

        /// <summary>Being on the farm counts for "go to the farm yard / the ranch house" — once a visit.</summary>
        private void HofBesucht()
        {
            if (_hofGemeldet || !_gekauft) return;
            _hofGemeldet = true;
            QuestMelde("gehe_zu", "hof_mitte");
            QuestMelde("gehe_zu", "haus");
        }
    }
}
