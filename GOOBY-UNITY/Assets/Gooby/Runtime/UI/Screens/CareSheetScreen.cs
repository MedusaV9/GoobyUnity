using System;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Interagieren" sheet (concept board 08 action bar): the core care loop — feed from the fridge
    /// (FoodCatalog.ApplyFeed), pet, sleep/nap/wake (Sleep state helpers). Every action mutates the
    /// store through Update() so the HUD bars, weight and illness follow.
    /// </summary>
    public sealed class CareSheetScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly Action<string> _say;
        private VisualElement _cards, _toys;
        private Label _hint, _health;

        /// <summary>Gooby ate: (food id, how often he has had it in total) — his soul answers (thanks, favourite food).</summary>
        public static event Action<string, int> Fed;

        public CareSheetScreen(GameHost host, Action<string> say)
        {
            _host = host;
            _say = say;
            Id = "care-sheet";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 140; sheet.style.right = 140;
            sheet.style.maxHeight = Length.Percent(86);
            var head = CounterPanel.Head("hand", new Color(1f, 0.85f, 0.85f), t.T("nl.hud.aktion_interagieren"), t.T("nl.care.hinweis"), () => _host.Ui.Pop());
            _hint = head.Sub;
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");

            // quick actions: pet / sleep / wake (board 06 pills: sand secondary, green confirm, taupe cancel, pink primary)
            var actions = Ux.Row("gb-win-actions");
            actions.Add(Ux.IconTextBtn("hand", t.T("nl.care.streicheln"), Pet, "gb-btn-secondary", "gb-btn-sm"));
            var flat = Sleep.FlatOf(_host.Store.State);
            if (Sleep.IsSleeping(flat))
                actions.Add(Ux.IconTextBtn("sun", t.T("nl.care.wecken"), Wake, "gb-btn-cancel", "gb-btn-sm"));
            else
            {
                var sleepBtn = Ux.IconTextBtn("moon", t.T("nl.care.schlafen"), () => StartSleep(nap: false), "gb-btn-confirm", "gb-btn-sm");
                sleepBtn.SetEnabled(Sleep.CanSleep(flat));
                actions.Add(sleepBtn);
                var napBtn = Ux.IconTextBtn("cloud", t.T("nl.care.nickerchen"), () => StartSleep(nap: true), "gb-btn-secondary", "gb-btn-sm");
                napBtn.SetEnabled(Sleep.CanNap(flat));
                actions.Add(napBtn);
                // the bedtime story reads him to sleep: when the moons are full the night begins
                var storyBtn = Ux.IconTextBtn("books", t.T("nl.story.knopf"), () => _host.Ui.Push(new StoryTimeScreen(_host, _say, () => StartSleep(nap: false, quiet: true))), "gb-btn-secondary", "gb-btn-sm");
                storyBtn.name = "care-story";
                storyBtn.SetEnabled(Sleep.CanSleep(flat));
                actions.Add(storyBtn);
            }
            // washing: the bathroom's shower or bath (Gooby walks in), the hygiene bar's only way back up
            var washBtn = Ux.IconTextBtn("rain", t.T("nl.interakt.waschen"), Wash, "gb-btn-secondary", "gb-btn-sm");
            washBtn.name = "care-wash";
            washBtn.SetEnabled(!Sleep.IsSleeping(flat));
            actions.Add(washBtn);
            // health: how Gooby feels and the medicine from the GOOBYTHEKE (Health.UseMedicineState)
            var grade = Health.Grade(J.Path(_host.Store.State, "gooby", "health"));
            var medicine = J.Int(J.Path(_host.Store.State, "inventory", "items", "medicine"));
            var medBtn = Ux.IconTextBtn("heart", t.T("nl.care.medizin"), GiveMedicine, grade > 0 && medicine > 0 ? "gb-btn-primary" : "gb-btn-secondary", "gb-btn-sm");
            medBtn.SetEnabled(medicine > 0);
            actions.Add(medBtn);
            body.Add(actions);

            // health note: mint when fine, rose while ill; the medicine stock as a tag
            var healthRow = Ux.Row("gb-win-note");
            if (grade > 0) healthRow.AddToClassList("gb-win-note--warn");
            healthRow.Add(Ux.IconTile(grade > 0 ? "heart" : "sparkle"));
            _health = Ux.Text(t.T(grade >= 2 ? "nl.care.krank" : grade == 1 ? "nl.care.kraenklich" : "nl.care.gesund"), "gb-win-note-text");
            healthRow.Add(_health);
            if (medicine > 0) healthRow.Add(Ux.Text(t.T("nl.care.medizin_vorrat", ("anzahl", medicine)), "gb-win-note-tag"));
            body.Add(healthRow);

            // the fridge, then the toy box (POW! toys): both in one scroll so the sheet keeps one height
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            scroll.Add(CounterPanel.Section("inventory", t.T("nl.care.kuehlschrank")));
            _cards = Ux.El("gb-win-grid");
            scroll.Add(_cards);
            scroll.Add(CounterPanel.Section("ball_bouncy", t.T("nl.spielzeug.titel")));
            _toys = Ux.El("gb-win-grid");
            _toys.name = "toy-box";
            scroll.Add(_toys);
            body.Add(scroll);
            sheet.Add(body);
            root.Add(sheet);
            RefreshCards();
            RefreshToys();
        }

        private void RefreshToys()
        {
            _toys.Clear();
            var t = _host.Strings;
            var state = _host.Store.State;
            var kiste = Spielzeug.Kiste(state);
            if (kiste.Count == 0)
            {
                _toys.Add(CounterPanel.Empty(t.T("nl.spielzeug.leer"), t.T("nl.spielzeug.leer_hint"), row: true));
                return;
            }
            var tag = _host.Store.Clock.LocalDay();
            foreach (var (def, n) in kiste)
            {
                var id = def.Id;
                // the toy's own picture, how much fun it brings now (a third once played today), the stock of the cracker
                var spass = (int)Spielzeug.SpassFuer(state, def, tag);
                var sub = Spielzeug.HeuteGespielt(state, id, tag) ? t.T("nl.spielzeug.schon_heute") : null;
                var card = CounterPanel.Ware(CounterPanel.WareIcon(id, "star"), new Color(1f, 0.93f, 0.7f), t.T("nl.spielzeug.name." + id), sub,
                    def.Verbrauch || n > 1 ? t.T("fuettern.vorrat_badge", ("anzahl", n)) : null, "gb-ware--sm", "gb-ware--tap");
                card.name = "toy-" + id;
                card.Add(CounterPanel.Go(t.T("nl.spielzeug.spielen") + " · " + t.T("nl.spielzeug.spass", ("n", spass))));
                card.RegisterCallback<ClickEvent>(_ => PlayToy(id));
                _toys.Add(card);
            }
        }

        /// <summary>
        /// A toy tile: the sheet closes and Gooby plays with it on the stage (<see cref="World.SimpleWorld.PlayToy"/>); the
        /// fun is booked at the scene's end. Asleep or already as happy as he gets, he says so instead.
        /// </summary>
        private void PlayToy(string id)
        {
            var t = _host.Strings;
            var nein = Spielzeug.Ablehnung(_host.Store.State, id);
            if (nein == Spielzeug.Schlaeft || nein == Spielzeug.GutGelaunt)
            {
                var line = t.T(nein == Spielzeug.Schlaeft ? "nl.spielzeug.schlaeft" : "nl.spielzeug.gut_gelaunt");
                _hint.text = line;
                _say(line);
                return;
            }
            if (nein != null) return;
            var w = _host.World;
            if (w != null && (w.Feeding || w.PlayingToy)) return;
            var host = _host;
            var say = _say;
            if (w != null && w.Gooby != null && _host.Ui.Current == this)
            {
                _host.Ui.Pop();
                if (w.PlayToy(id, Spielzeug.DefVon(id).Szene, _host.Settings != null && _host.Settings.ReducedMotion, () => BookToy(host, say, id))) return;
            }
            if (BookToy(host, say, id) && _host.Ui.IsOpen(this)) RefreshToys();
        }

        /// <summary>The play is booked: fun (full once a day), his line for the toy or a bored "again?", the gain over him.</summary>
        private static bool BookToy(GameHost host, Action<string> say, string id)
        {
            var t = host.Strings;
            Spielzeug.Ergebnis r = null;
            var tag = host.Store.Clock.LocalDay();
            host.Store.Update(s => r = Spielzeug.Spielen(s, id, tag), GameHost.TicksMs);
            if (r == null || r.Grund != Spielzeug.Ok) return false;
            host.Store.NotifySliceChanged("inventory");
            host.Store.NotifySliceChanged("achievements");
            host.Store.NotifySliceChanged("gooby");
            var lines = t.Items(r.Frisch ? "nl.spielzeug.zeilen." + id : "nl.spielzeug.nochmal");
            var plays = J.Int(J.Path(host.Store.State, "achievements", "counters", "toyPlays"));
            var line = lines != null && lines.Count > 0 ? (string)lines[plays % lines.Count] : "";
            if (Spielzeug.DefVon(id).Verbrauch && r.Rest == 0) line += " " + t.T("nl.spielzeug.letztes");
            say?.Invoke(line);
            var w = host.World;
            var gain = Mathf.RoundToInt((float)r.Spass);
            if (gain > 0 && w != null && w.Gooby != null && w.Camera != null && host.Ui.Document != null && host.Ui.Document.rootVisualElement.panel != null)
            {
                var at = RuntimePanelUtils.CameraTransformWorldToPanel(host.Ui.Document.rootVisualElement.panel, w.Gooby.position + Vector3.up * 1.4f, w.Camera);
                host.Ui.FloatText(at, t.T("nl.spielzeug.spass", ("n", gain)), new Color(1f, 0.67f, 0.25f), 32f);
            }
            Debug.Log("[toy] booked " + id + " fun +" + gain + (r.Frisch ? "" : " (again)") + " rest " + r.Rest);
            return true;
        }

        private void RefreshCards()
        {
            _cards.Clear();
            var t = _host.Strings;
            var entries = FoodCatalog.InventoryEntries(_host.Store.State);
            if (entries.Count == 0)
            {
                _cards.Add(CounterPanel.Empty(t.T("fuettern.leer_titel"), t.T("fuettern.leer_tipp"), row: true));
                return;
            }
            foreach (var e in entries)
            {
                var id = e.Id;
                var kat = FoodCatalog.Kategorie(id);
                var name = t.HasKey("rewards.food." + id) ? t.T("rewards.food." + id) : id;
                // fridge tile: the food's own icon on a pastel of its category, stock tag, "Füttern" pill
                var tint = kat == "suesses" ? new Color(1f, 0.93f, 0.75f) : kat == "warm" ? new Color(1f, 0.88f, 0.8f) : new Color(0.87f, 0.96f, 0.89f);
                var card = CounterPanel.Ware(CounterPanel.FoodIcon(id, kat == "suesses" ? "star" : kat == "warm" ? "sun" : "leaf"), tint, name, null,
                    t.T("fuettern.vorrat_badge", ("anzahl", e.Count)), "gb-ware--sm", "gb-ware--tap");
                card.name = "food-" + id;
                card.Add(CounterPanel.Go(t.T("nl.care.fuettern")));
                card.RegisterCallback<ClickEvent>(_ => Feed(id));
                _cards.Add(card);
            }
        }

        /// <summary>
        /// A food tile: the sheet closes and the meal plays on the stage (<see cref="World.SimpleWorld.PlayFeeding"/> — the
        /// food flies to Gooby, three bites, a swallow, a feeling); the booking comes at its end (kuehlschrank.gd). Full or
        /// empty stops before the sequence; without a stage (tests) it books at once.
        /// </summary>
        private void Feed(string id)
        {
            var t = _host.Strings;
            var refusal = FeedSequence.Refusal(_host.Store.State, id);
            if (refusal == "satt")
            {
                _hint.text = t.T("nl.care.satt");
                _say(t.T("nl.care.satt"));
                return;
            }
            if (refusal == "leer")
            {
                _hint.text = t.T("fuettern.leer_titel");
                return;
            }
            var w = _host.World;
            if (w != null && w.Feeding) return;   // a meal is under way: the double tap
            var host = _host;
            var say = _say;
            if (w != null && w.Gooby != null && _host.Ui.Current == this)
            {
                _host.Ui.Pop();
                if (w.PlayFeeding(id, _host.Settings != null && _host.Settings.ReducedMotion, () => Book(host, say, id))) return;
            }
            if (Book(host, say, id) && _host.Ui.IsOpen(this))
            {
                Sfx.Play("nom_nom", UnityEngine.Random.Range(-1f, 1f));
                RefreshCards();
            }
        }

        /// <summary>
        /// The meal is booked (FoodCatalog.ApplyFeed checks full and stock itself, fail-closed): his soul notes it, Gooby
        /// says the next line of the food's shelf category (eight, in turn) and the hunger he gained floats up over him.
        /// </summary>
        private static bool Book(GameHost host, Action<string> say, string id)
        {
            var t = host.Strings;
            FoodCatalog.FeedResult result = null;
            var given = 0;
            host.Store.Update(s =>
            {
                result = FoodCatalog.ApplyFeed(s, id);
                if (result != null) given = Soul.NoteFoodGiven(s, id);
            }, GameHost.TicksMs);
            if (result == null) return false;
            host.Store.NotifySliceChanged("achievements");
            host.Store.NotifySliceChanged(Soul.SliceId);
            Fed?.Invoke(id, given);
            var essen = t.HasKey("rewards.food." + id) ? t.T("rewards.food." + id) : id;
            var lines = t.Items("fuettern.sprueche." + FoodCatalog.Kategorie(id));
            var i = FeedLines.Next(id, lines?.Count ?? 0);
            var line = i >= 0 ? ((string)lines[i]).Replace("{essen}", essen)
                : result.Favorit ? t.T("nl.care.lieblingsessen") : result.Junk ? t.T("nl.care.junk") : t.T("nl.care.lecker");
            say?.Invoke(line);
            var w = host.World;
            var gain = Mathf.RoundToInt((float)result.HungerGain);
            if (gain > 0 && w != null && w.Gooby != null && w.Camera != null && host.Ui.Document != null && host.Ui.Document.rootVisualElement.panel != null)
            {
                var at = RuntimePanelUtils.CameraTransformWorldToPanel(host.Ui.Document.rootVisualElement.panel, w.Gooby.position + Vector3.up * 1.4f, w.Camera);
                host.Ui.FloatText(at, "+" + gain, new Color(0.36f, 0.78f, 0.6f), 34f);
            }
            w?.Bounce();
            Debug.Log("[feed] booked " + id + " hunger +" + gain);
            return true;
        }

        /// <summary>One medicine from the bag: sick → queasy, queasy → healthy; a healthy Gooby keeps it.</summary>
        public void GiveMedicine()
        {
            var t = _host.Strings;
            var result = new Health.CareResult();
            _host.Store.Update(s => result = Health.UseMedicineState(s, _host.Store.Clock.NowMs()), GameHost.TicksMs);
            var key = result.Ok ? "health.medizin_ok" : result.Reason == "none" ? "health.medizin_keine" : "health.medizin_gesund";
            _say(t.T(key));
            if (result.Ok)
            {
                Sfx.PlayUi("soft_care_erfolg");
                _host.Store.NotifySliceChanged("inventory");
                _host.Store.NotifySliceChanged("achievements");
                _host.Store.NotifySliceChanged("gooby");
                _host.World?.Bounce();
                // rebuild so the health line, the stock badge and the button reflect the dose
                Root.Clear();
                Build(Root);
            }
            _hint.text = t.T(key);
        }

        private void Pet()
        {
            _host.Store.Update(s =>
            {
                var gooby = J.EnsureDict(s, "gooby");
                gooby["stats"] = Stats.ApplyDeltas(J.Dict(gooby["stats"]), new System.Collections.Generic.Dictionary<string, double> { { "fun", 2.0 } });
                var counters = J.EnsureDict(J.EnsureDict(s, "achievements"), "counters");
                counters["tickles"] = J.Int(counters["tickles"]) + 1;
            }, GameHost.TicksMs);
            _say(_host.Strings.T("home.gooby.watch"));
            Sfx.Play("pet_squish", UnityEngine.Random.Range(-0.8f, 0.8f));
            _host.World?.Bounce();
        }

        private void Wash()
        {
            var w = _host.World;
            if (_host.Ui.Current == this) _host.Ui.Pop();
            if (w == null) return;
            if (w.RoomId == "bathroom" || w.InGarden || w.Visiting) { w.WashHere(); return; }
            _host.Ui.FadeThrough(() =>
            {
                w.EnterRoom("bathroom");
                w.WashHere();
            });
        }

        /// <param name="quiet">The story already said goodnight: keep its line in the bubble.</param>
        private void StartSleep(bool nap, bool quiet = false)
        {
            var ok = false;
            _host.Store.Update(s => ok = Sleep.StartSleepState(s, _host.Store.Clock.NowMs(), nap), GameHost.TicksMs);
            if (!ok) return;
            if (!quiet) _say(_host.Strings.T("home.suche.schlaeft"));
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        private void Wake()
        {
            System.Collections.Generic.List<string> events = null;
            _host.Store.Update(s => events = Sleep.WakeEarlyState(s, _host.Store.Clock.NowMs()), GameHost.TicksMs);
            if (events != null && events.Count > 0) _say(_host.Strings.T("home.suche.hier"));
            _host.Ui.Pop();
        }
    }
}
