using System;
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
    /// The quests out riding: Luna, Knips, Herr Punktabzug and Eilfried wait in the region with their "!"/"?" markers and a
    /// "Mit … sprechen" button when you ride up; the day's horseshoes, flowers, berries, boards, the key and the wool are
    /// picked up by riding over them; the places count for "go to" goals (the lake, the pasture, the forest edge, the hill,
    /// the arena, the photo spots, the old barn, the farm gate), and the farm lap and the lake lap are ridden through four
    /// checkpoints each. The log and the pinned goal are one tap away.
    /// </summary>
    public sealed partial class RanchWeltScreen
    {
        private Label _trackerText;
        private VisualElement _tracker, _overlay;
        private readonly Dictionary<string, Label> _marken = new Dictionary<string, Label>();
        private readonly HashSet<string> _gemeldet = new HashSet<string>();
        private readonly Dictionary<string, HashSet<int>> _runden = new Dictionary<string, HashSet<int>>();
        private string _person = "";
        private float _sammelIn;

        /// <summary>Four checkpoints per lap (40 m radius): round the farm and round the lake.</summary>
        private static readonly Dictionary<string, V2d[]> Strecken = new Dictionary<string, V2d[]>
        {
            ["hof_runde"] = new[] { new V2d(-170, 20), new V2d(0, -150), new V2d(170, 20), new V2d(0, 160) },
            ["see_runde"] = new[] { new V2d(380, 270), new V2d(500, 150), new V2d(625, 270), new V2d(500, 395) },
        };

        /// <summary>Region zones and spots that count as a quest place.</summary>
        private static readonly Dictionary<string, string> ZonenOrte = new Dictionary<string, string>
        {
            ["see"] = "teich", ["weidetal"] = "weide", ["waeldchen"] = "waldrand", ["huegelkamm"] = "huegel", ["scheune_alt"] = "scheune",
        };

        private void BaueQuests(VisualElement root, VisualElement rechts)
        {
            _welt?.ZeigeLeute(RanchQuestUi.Look);
            _welt?.TickLeute(Stunde);
            _welt?.ZeigeSammeln(RanchSammeln.FuerTag(Today), RanchSammeln.Aufgesammelt(State, Today));
            var log = RundKnopf("ranchwelt-quests", "book", T("rquest.ui.knopf"), ZeigeQuests, false);
            rechts.Insert(0, log);
            _tracker = new Button(ZeigeQuests) { name = "ranchwelt-tracker" };
            _tracker.ClearClassList();
            _tracker.AddToClassList("gb-rquest-tracker");
            var pin = Ux.Icon("pin");
            pin.pickingMode = PickingMode.Ignore;
            _tracker.Add(pin);
            _trackerText = Ux.Text("", "gb-rquest-tracker-text");
            _trackerText.pickingMode = PickingMode.Ignore;
            _tracker.Add(_trackerText);
            root.Add(_tracker);
            foreach (var id in RanchWelt3D.RegionLeute)
            {
                var m = Ux.Text("", "gb-rquest-marke");
                m.name = "ranchwelt-marke-" + id;
                m.pickingMode = PickingMode.Ignore;
                m.style.visibility = Visibility.Hidden;
                root.Add(m);
                _marken[id] = m;
            }
            QuestOrt("tor");
            RefreshQuests();
        }

        /// <summary>The local hour of the game clock (the people's day).</summary>
        private double Stunde => RanchOffline.StundeLokal(Now, _host.Store.Clock.UtcOffsetMinutes(Now));

        private void RefreshQuests()
        {
            if (_tracker == null) return;
            var s = State;
            var q = RanchQuestUi.Verfolgt(s);
            _trackerText.text = RanchQuestUi.TrackerText(_host, Now);
            _tracker.EnableInClassList("gb-rquest-tracker--bereit", q != null && RanchQuests.Status(s, q) == RanchQuests.Erfuellbar);
            foreach (var kv in _marken) kv.Value.text = RanchQuestUi.Marke(s, kv.Key);
        }

        private void PlaceMarken()
        {
            if (_welt == null) return;
            var w = _field.layout.width;
            var h = _field.layout.height;
            foreach (var kv in _marken)
            {
                var kopf = _welt.PersonKopf(kv.Key);
                var show = kopf.HasValue && _overlay == null && !KarteOffen && kv.Value.text.Length > 0 && !float.IsNaN(w) && w > 0f;
                Vector2 p = default;
                if (show)
                {
                    var (x, y, z) = kopf.Value;
                    if (Math.Abs(x - _welt.X) + Math.Abs(z - _welt.Z) > 160) show = false;
                    else p = _welt.ToField(x, y, z, w, h);
                    if (p.x < -1000f) show = false;
                }
                kv.Value.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
                if (!show) continue;
                kv.Value.style.left = p.x - 17f;
                kv.Value.style.top = p.y - 34f + Mathf.Sin(Time.unscaledTime * 3f + kv.Key.Length) * 4f;
                kv.Value.EnableInClassList("gb-rquest-marke--abgabe", kv.Value.text == "?");
            }
        }

        /// <summary>Every 0.25 s: a pick-up under the hooves, the people near, places and laps.</summary>
        private void PruefeQuests(float dt)
        {
            if (_welt == null || !_welt.Bereit) return;
            if ((_sammelIn -= dt) > 0f) return;
            _sammelIn = 0.25f;
            var stelle = _welt.Aufsammelbar();
            if (stelle != null) Aufsammeln(stelle);
            _person = _welt.PersonBei(_welt.X, _welt.Z, 9.0);
            var x = _welt.X;
            var z = _welt.Z;
            if (ZonenOrte.TryGetValue(_welt.Zone, out var ort)) QuestOrt(ort);
            if (InArena(x, z)) QuestOrt("reitplatz");
            if (RanchEntdeckungen.Fundort("aussicht_see").Pos.Abstand(x, z) < 20.0) QuestOrt("foto_spot_teich");
            if (RanchEntdeckungen.Fundort("aussicht_kamm").Pos.Abstand(x, z) < 20.0) QuestOrt("foto_spot_huegel");
            foreach (var kv in Strecken)
            {
                if (!_runden.TryGetValue(kv.Key, out var besucht)) _runden[kv.Key] = besucht = new HashSet<int>();
                for (var i = 0; i < kv.Value.Length; i++)
                    if (kv.Value[i].Abstand(x, z) < 40.0) besucht.Add(i);
                if (besucht.Count < kv.Value.Length) continue;
                besucht.Clear();
                Debug.Log("[ranchwelt] runde " + kv.Key);
                if (RanchQuestUi.Melde(_host, "reite_strecke", kv.Key)) RefreshQuests();
            }
        }

        /// <summary>A quest place reached (reported once a ride, the goal may come later).</summary>
        private void QuestOrt(string ort)
        {
            if (!_gemeldet.Add(ort)) return;
            if (RanchQuestUi.Melde(_host, "gehe_zu", ort)) RefreshQuests();
        }

        private void Aufsammeln(RanchSammeln.Stelle stelle)
        {
            var tag = Today;
            var now = Now;
            var genommen = false;
            RanchQuestUi.Ereignis(_host, s =>
            {
                var r = RanchSammeln.Aufsammeln(s, tag, stelle, now);
                genommen = r != null;
                return r ?? new List<string>();
            });
            _welt.Weg(stelle.Id);
            if (!genommen) return;
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            Sfx.PlayUi("soft_collect");
            Haptics.Tap();
            var w = _field.layout.width;
            var h = _field.layout.height;
            var p = !float.IsNaN(w) && w > 0f ? _welt.ToField(stelle.X, RanchGelaende.Hoehe(stelle.X, stelle.Z) + 1.2, stelle.Z, w, h) : new Vector2(800f, 340f);
            if (p.x < -1000f) p = new Vector2(w * 0.5f, h * 0.55f);
            _host.Ui.FloatText(p, T("rquest.ui.aufgesammelt", ("n", 1), ("item", T("rquest.item." + stelle.Item))), new Color(0.98f, 0.86f, 0.4f), 30f);
            RefreshQuests();
            Debug.Log("[ranchwelt] aufgesammelt " + stelle.Id);
        }

        // ── sheets ──

        private VisualElement OpenSheet(string name, string icon, Color tint, string title, string sub)
        {
            CloseOverlay();
            StickLoslassen();
            _welt?.Anhalten();
            _overlay = Ux.El("gb-scrim", "gb-ranchhof-veil");
            _overlay.name = name + "-veil";
            _overlay.RegisterCallback<ClickEvent>(e => { if (e.target == _overlay) CloseOverlay(); });
            var sheet = Ux.Col("gb-card", "gb-ranchhof-sheet");
            sheet.name = name;
            var head = CounterPanel.Head(icon, tint, title, sub, CloseOverlay);
            sheet.Add(head.Row);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-ranchhof-sheet-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sheet.Add(scroll);
            _overlay.Add(sheet);
            Root.Add(_overlay);
            _juice.ScalePop(sheet, 1.04f, 0.2f);
            return scroll.contentContainer;
        }

        private void CloseOverlay()
        {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            RefreshQuests();
        }

        private void ZeigeQuests()
        {
            Sfx.PlayUi("soft_open");
            var kapitel = RanchQuests.Kapitel(State);
            var body = OpenSheet("ranchwelt-questlog", "book", new Color(0.86f, 0.92f, 0.98f), T("rquest.log.titel"),
                kapitel > 10 ? T("rquest.ui.reihe_fertig") : T("rquest.log.kapitel", ("n", kapitel)));
            RanchQuestUi.Log(_host, body, RefreshQuests, _coinPill, "haupt");
        }

        private void ZeigePerson(string npc)
        {
            Sfx.PlayUi("soft_open");
            var body = OpenSheet("ranchwelt-person", "user", new Color(0.98f, 0.88f, 0.76f), RanchQuestUi.Name(_host, npc), RanchQuestUi.Rolle(_host, npc));
            RanchQuestUi.Melde(_host, "sprich_mit", npc);
            RanchQuestUi.Person(_host, body, npc, () => { RefreshQuests(); RefreshHud(); }, _coinPill, n => RanchQuestUi.Melde(_host, "sprich_mit", n));
        }
    }
}
