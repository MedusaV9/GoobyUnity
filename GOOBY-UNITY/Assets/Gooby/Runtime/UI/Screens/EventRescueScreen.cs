using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Helping Gooby out of a random event (the interaction of event_runner.gd, as a sheet): the notification as the
    /// title, Gooby in 3D on his rug looking sorry for himself, and the mess of the event scattered over a little scene —
    /// shards, socks, flour clouds, toilet-paper layers, the fridge's spilled food … each a big round button. Tapping one
    /// pops it with sparks; the last one ends the event (<see cref="RandomEvents.Resolve"/>): the reward lands on his
    /// stat with a toast, confetti, a happy Gooby and his thanks in the bubble. Closing the sheet leaves the event open
    /// until its timeout.
    /// </summary>
    public sealed partial class EventRescueScreen : UiScreen
    {
        /// <summary>Per scene setup: the props to tap (icon ids) and how many of them.</summary>
        private static readonly Dictionary<string, (string[] icons, int count)> Mess = new Dictionary<string, (string[], int)>
        {
            ["marienkaefer"] = (new[] { "hand" }, 3),
            ["kuehlschrank"] = (new[] { "food_apple", "food_cheese", "food_carrot", "food_cake", "food_tomato" }, 5),
            ["glas_scherben"] = (new[] { "sparkle" }, 3),
            ["teller_scherben"] = (new[] { "sparkle" }, 3),
            ["nutella_nacht"] = (new[] { "candy" }, 2),
            ["sockensuche"] = (new[] { "shirt" }, 3),
            ["robo_jagd"] = (new[] { "spiky" }, 3),
            ["kleber_stuhl"] = (new[] { "sparkle" }, 4),
            ["wurm_freund"] = (new[] { "sprout" }, 2),
            ["fernbedienung"] = (new[] { "speaker" }, 3),
            ["karton_gooby"] = (new[] { "gift" }, 3),
            ["gewitter_angst"] = (new[] { "heart" }, 3),
            ["mehl_unfall"] = (new[] { "cloud" }, 5),
            ["klopapier_mumie"] = (new[] { "redo" }, 4),
        };

        private readonly GameHost _host;
        private readonly JObject _def;
        private readonly Action<string> _say;
        private readonly Juice _fx = new Juice();
        private World.MgBuddy3D _buddy;
        private VisualElement _scene;
        private int _left;
        private bool _done;

        public EventRescueScreen(GameHost host, JObject def, Action<string> say)
        {
            _host = host;
            _def = def;
            _say = say;
            Id = "ereignis";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => { if (!_done) _host.Ui.Pop(); });
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet", "gb-event-sheet");
            sheet.name = "event-sheet";
            sheet.style.left = 180; sheet.style.right = 180;
            sheet.style.maxHeight = Length.Percent(90);
            var head = CounterPanel.Head("heart", new Color(1f, 0.82f, 0.86f), t.T("nl.ereignis.titel"), J.Str(_def["notification_text_de"]), () => { if (!_done) _host.Ui.Pop(); });
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");
            var row = Ux.Row();
            row.style.alignItems = Align.Stretch;
            if (World.MgStage3D.Supported)
            {
                try
                {
                    var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"]));
                    _buddy = new World.MgBuddy3D(outfit, slot: 2);
                    var col = Ux.El("gb-mg-buddy-col");
                    col.pickingMode = PickingMode.Ignore;
                    col.Add(Ux.El("gb-mg-buddy-rug"));
                    var view = Ux.El("gb-mg-buddy-view");
                    view.style.width = 170; view.style.height = 230;
                    col.Add(view);
                    _buddy.Bind(view, World.MgBuddy3D.Framing.Full, 170f / 230f);
                    _buddy.Rest(J.Str(_def["szene_setup"]) == "gewitter_angst" ? "scared" : "sad");
                    row.Add(col);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[ereignis] 3D Gooby: " + e.Message);
                    _buddy = null;
                }
            }
            _scene = Ux.El("gb-card", "gb-event-scene");
            _scene.name = "event-scene";
            _scene.style.flexGrow = 1;
            _scene.style.height = 260;
            _scene.style.backgroundColor = new Color(1f, 0.96f, 0.9f);
            row.Add(_scene);
            body.Add(row);
            var hint = Ux.Text(t.T("nl.ereignis.hinweis"), "gb-md", "gb-mild");
            hint.style.marginTop = 10;
            body.Add(hint);
            sheet.Add(body);
            root.Add(sheet);
            root.Add(_fx.Layer);
            _hint = hint;
            _buddyCol = row.childCount > 1 ? row[0] : null;
            switch (J.Str(_def["szene_setup"]))
            {
                case "gewitter_angst": BuildStorm(); break;
                case "klopapier_mumie": BuildMummy(); break;
                default: Scatter(); break;
            }
        }

        /// <summary>The event's mess at random spots over the scene card (percent positions, so any sheet width works).</summary>
        private void Scatter()
        {
            var setup = J.Str(_def["szene_setup"]);
            var (icons, count) = Mess.TryGetValue(setup, out var m) ? m : (new[] { "sparkle" }, 3);
            if (_def["props"] != null && J.Int(_def["props"]) > 0) count = Mathf.Clamp(J.Int(_def["props"]), 1, 6);
            _left = count;
            var used = new List<Vector2>();
            for (var i = 0; i < count; i++)
            {
                Vector2 at;
                var tries = 0;
                do at = new Vector2(UnityEngine.Random.Range(8f, 78f), UnityEngine.Random.Range(10f, 64f));
                while (++tries < 30 && used.Exists(u => (u - at).magnitude < 20f));
                used.Add(at);
                var prop = new Button { text = "", name = "event-prop-" + i };
                prop.ClearClassList();
                prop.AddToClassList("gb-event-prop");
                prop.style.position = Position.Absolute;
                prop.style.left = Length.Percent(at.x);
                prop.style.top = Length.Percent(at.y);
                prop.style.rotate = new Rotate(UnityEngine.Random.Range(-18f, 18f));
                var icon = Ux.Icon(icons[i % icons.Length]);
                icon.pickingMode = PickingMode.Ignore;
                icon.style.width = 46; icon.style.height = 46;
                prop.Add(icon);
                var local = prop;
                prop.clicked += () => Collect(local);
                _scene.Add(prop);
            }
        }

        private void Collect(Button prop)
        {
            if (_done || prop.parent == null) return;
            var center = prop.worldBound.center;
            _fx.Burst(center, new Color(1f, 0.83f, 0.4f), 16);
            prop.RemoveFromHierarchy();
            Sfx.PlayUi("soft_collect");
            Haptics.Tap();
            _left--;
            if (_left > 0) { _buddy?.Cheer(1, 0); return; }
            Finish();
        }

        private void Finish()
        {
            _done = true;
            JObject def = null;
            _host.Store.Update(s => def = RandomEvents.Resolve(s, RandomEvents.Defs()), GameHost.TicksMs);
            _host.Store.NotifySliceChanged(RandomEvents.SliceId);
            _host.Store.NotifySliceChanged("gooby");
            var t = _host.Strings;
            _buddy?.Celebrate(true, false);
            _say?.Invoke(t.T(_thanksKey ?? "nl.ereignis.danke"));
            _host.Ui.Confetti(70);
            Sfx.PlayUi("soft_levelup");
            if (def?["reward"] is JObject reward)
            {
                _host.Ui.Toast(t.T("nl.ereignis.belohnung", ("wert", J.Int(reward["wert"])), ("stat", t.T("nl.stat." + J.Str(reward["stat"], "fun")))), "heart");
                // random_events.gd: the reward also shows as a timed buff on the stats sheet
                Gooby.Core.Life.GoobyBuffs.Grant(_host.Store, J.Str(reward["buff_id"], "buff"), J.Str(reward["stat"], "fun"), J.Num(reward["wert"]),
                    J.NumOr(reward["dauer_h"], 1.0), _host.Store.Clock.NowMs());
            }
            Debug.Log("[ereignis] resolved " + J.Str(_def["id"]));
            Root.schedule.Execute(() => { if (_host.Ui.Current == this) _host.Ui.Pop(); }).StartingIn(1500);
        }

        public override void Update()
        {
            base.Update();
            _fx.Tick(Time.unscaledDeltaTime);
            TickScenes(Time.unscaledDeltaTime);
        }

        public override void OnHidden()
        {
            base.OnHidden();
            _buddy?.Destroy();
            _buddy = null;
        }
    }
}
