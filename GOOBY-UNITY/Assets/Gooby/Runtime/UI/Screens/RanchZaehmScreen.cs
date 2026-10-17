using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The taming encounter at the forest edge (port of zaehm_szene.gd on <see cref="RanchZaehmen"/>): the day's wild
    /// horse grazes in the meadow; hold "Schleichen" to creep closer (crouch with "Ducken" — the attention bar stays
    /// low), and once close the horse snorts in its rhythm: tap "Beruhigen" on each snort until the calm bar is full. Too
    /// much attention or calm at zero and it trots off — it comes back after a little while. Tamed, it moves to the
    /// ranch.
    /// </summary>
    public sealed class RanchZaehmScreen : UiScreen
    {
        private static readonly Vector3 PferdSpot = new Vector3(0f, 0f, 9f);
        private static readonly Vector3 Start = new Vector3(0f, 0f, -2f);
        private const float SchleichSpeed = 1.3f, DuckSpeed = 0.75f;

        private readonly GameHost _host;
        private readonly Juice _juice = new Juice();
        private readonly string _tag;
        private readonly RanchZaehmen _z;
        private readonly JObject _wild;
        private MgStage3D _stage;
        private RanchWetterView _wetter;
        private RanchHorse3D _pferd;
        private GoobyAvatar _gooby;
        private Transform _ring;
        private Vector3 _goobyPos = Start, _pferdPos = PferdSpot;
        private float _ringPuls, _pferdWeg;
        private bool _schleicht, _geduckt, _fertig, _night;
        private string _goobyClip = "idle-loop";
        private VisualElement _field, _barFill, _bubble, _schleichBtn, _duckBtn, _ruheBtn;
        private Label _barLabel, _bubbleText;

        public RanchZaehmScreen(GameHost host, string tag)
        {
            _host = host;
            _tag = tag;
            _z = new RanchZaehmen(RanchZaehmen.TagesSeed(tag));
            _wild = RanchZaehmen.Wildpferd(_z.Seed);
            Id = "zaehmen";
            Opaque = true;
        }

        public override string SoundScene => _wetter?.Scene ?? "ranch/reiten";

        /// <summary>Puddles beside the line Gooby sneaks along.</summary>
        private static readonly Vector3[] Pfuetzen =
        {
            new Vector3(-4.2f, 0f, 0.8f), new Vector3(4.4f, 0f, 3.2f), new Vector3(-3.6f, 0f, 6.4f), new Vector3(3.2f, 0f, 10.5f), new Vector3(-5.5f, 0f, 11.5f),
        };

        public string Phase => _z.Phase;

        private string T(string key, params (string, object)[] args) => _host.Strings.T(key, args);

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-ranchhof");
            _field = new VisualElement { name = "zaehmen-field" };
            Ux.Abs(_field, 0, 0, 0, 0);
            var now = _host.Store.Clock.NowMs();
            _night = RanchOffline.IstNacht(RanchOffline.StundeLokal(now, _host.Store.Clock.UtcOffsetMinutes(now)));
            _field.style.backgroundColor = ArtLibrary.Hex(_night ? "#3A4A78" : "#C4E6F8");
            root.Add(_field);
            if (MgStage3D.Supported) BuildStage();
            _wetter = new RanchWetterView(_host, "weidetal", "reiten", _stage != null ? new StageWeather(_stage, new Vector3(0f, 0f, 6f), new Vector3(14f, 1f, 11f), 1f, Pfuetzen) : null, _field);

            var back = Ux.IconBtn("back_white", () => { if (_host.Ui.Current == this) _host.Ui.Pop(); }, "gb-place-back");
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.Col("gb-card", "gb-zaehm-head");
            head.pickingMode = PickingMode.Ignore;
            _barLabel = Ux.Text(T("rhof.zaehmen.aufmerksam"), "gb-sm", "gb-bold");
            head.Add(_barLabel);
            var bar = Ux.Bar(0f, out _barFill);
            bar.AddToClassList("gb-zaehm-bar");
            head.Add(bar);
            root.Add(head);

            _bubble = Ux.Row("gb-card", "gb-ranchhof-bubble");
            _bubble.pickingMode = PickingMode.Ignore;
            _bubbleText = Ux.Text(T("rhof.zaehmen.start"), "gb-ranchhof-bubble-text");
            _bubble.Add(_bubbleText);
            root.Add(_bubble);

            var dock = Ux.Row("gb-zaehm-dock");
            _duckBtn = Knopf("zaehmen-ducken", "arrow_down", T("rhof.zaehmen.ducken"), ToggleDucken);
            dock.Add(_duckBtn);
            _schleichBtn = Knopf("zaehmen-schleichen", "arrow_up", T("rhof.zaehmen.schleichen"), null);
            _schleichBtn.RegisterCallback<PointerDownEvent>(_ => _schleicht = true, TrickleDown.TrickleDown);
            _schleichBtn.RegisterCallback<PointerUpEvent>(_ => _schleicht = false);
            _schleichBtn.RegisterCallback<PointerLeaveEvent>(_ => _schleicht = false);
            dock.Add(_schleichBtn);
            _ruheBtn = Knopf("zaehmen-beruhigen", "heart", T("rhof.zaehmen.beruhigen"), Beruhigen);
            _ruheBtn.style.display = DisplayStyle.None;
            dock.Add(_ruheBtn);
            root.Add(dock);
            root.Add(_juice.Layer);
            Debug.Log("[zaehmen] auf: " + J.Str(_wild["rasse"]) + " " + J.Str(_wild["farbe"]));
        }

        private VisualElement Knopf(string name, string icon, string text, System.Action klick)
        {
            var b = new Button(klick) { name = name };
            b.ClearClassList();
            b.AddToClassList("gb-ranchhof-action");
            b.AddToClassList("gb-zaehm-knopf");
            var disc = Ux.El("gb-ranchhof-action-disc");
            disc.pickingMode = PickingMode.Ignore;
            var ic = Ux.Icon(icon);
            ic.pickingMode = PickingMode.Ignore;
            disc.Add(ic);
            b.Add(disc);
            var l = Ux.Text(text, "gb-ranchhof-action-label");
            l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            return b;
        }

        private void BuildStage()
        {
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            _stage = new MgStage3D(720f * aspect, 720f, ArtLibrary.Hex(_night ? "#3A4A78" : "#C4E6F8"));
            _stage.Bind(_field);
            if (_night) MgStudio.Night();
            var root = _stage.Root.transform;
            Vector3 At(Vector3 p) => MgStage3D.Origin + p;
            string Model(string n) => System.IO.Path.Combine(AssetLoader.Root, "ranch", n + ".glb");
            _stage.Ground(0f, ArtLibrary.Hex("#9FD08A"), "grass", 90f, 80f, 20f);
            _stage.Meadow(0f, -14f, 14f, -4f, 20f, 120, 30, 41);
            _stage.Row("tree_round_big", 0f, 17f, -22f, 22f, 12, 1.7f, 0f, 2.5f, 42);
            _stage.Row("tree_poplar", 0f, 22f, -26f, 26f, 9, 1.6f, 0f, 3f, 43);
            _stage.Hills(0f, 40f, 7, ArtLibrary.Hex("#8FC07E"), 8f, 100f, 0f, 44);
            if (_night) _stage.Sun(new Vector3(-20f, 20f, 60f), 2.4f, new Color(0.92f, 0.95f, 1.1f));
            else
            {
                _stage.Clouds(5, 13f, 45f, 70f, 2.2f, 45);
                _stage.Sun(new Vector3(-20f, 20f, 60f), 3f);
            }
            foreach (var (x, z, s) in new[] { (-6f, 6f, 3.8f), (6.5f, 11f, 4.2f), (-8f, 13f, 4.4f), (8f, 4f, 3.4f) })
                _ = RanchHofStage.TintWhenLoaded(_stage.Glb(Model("tree_oak"), At(new Vector3(x, 0f, z)), s, x * 17f), ArtLibrary.Hex("#7DBE63"), "leaf");
            foreach (var (x, z) in new[] { (-3.5f, 3f), (4f, 7f), (-2.5f, 12f), (2.8f, 1.5f) })
                _ = RanchHofStage.TintWhenLoaded(_stage.Glb(Model("plant_bushLarge"), At(new Vector3(x, 0f, z)), 3.2f, x * 21f), ArtLibrary.Hex("#7FBF5E"));
            var (fell, maehne) = RcompRideView.FellVon(J.Str(_wild["farbe"], "braun"));
            _pferd = RanchHorse3D.Create(root, "Wild", fell, maehne);
            _pferd.transform.SetPositionAndRotation(At(_pferdPos), Quaternion.Euler(0f, 250f, 0f));
            _pferd.transform.localScale = Vector3.one * (float)J.NumOr(_wild["groesse"], 1.0);
            _pferd.Action("fressen-loop", 100000f);
            _ring = ArtLibrary.Cylinder(root, "Puls", At(_pferdPos + Vector3.up * 0.03f), 1.4f, 0.02f, ArtLibrary.Transparent(new Color(1f, 0.85f, 0.4f, 0.55f)), false).transform;
            _ring.gameObject.SetActive(false);
            _ = SpawnGoobyAsync();
            Kamera();
        }

        private async System.Threading.Tasks.Task SpawnGoobyAsync()
        {
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"]));
            var avatar = await _stage.SpawnGoobyAsync(MgStage3D.Origin + _goobyPos, 1f, outfit);
            if (avatar == null || _stage == null) return;
            _gooby = avatar;
            avatar.transform.rotation = Quaternion.identity;
        }

        private void Kamera()
        {
            if (_stage == null) return;
            _stage.Aim(_goobyPos + new Vector3(1.2f, 2.8f, -4.6f), 14f, 42f);
        }

        private void Sag(string text)
        {
            _bubbleText.text = text;
            _juice.ScalePop(_bubble, 1.05f, 0.16f);
        }

        // ── controls ──

        private void ToggleDucken()
        {
            _geduckt = !_geduckt;
            _duckBtn.EnableInClassList("gb-ranchhof-action--an", _geduckt);
            Sfx.PlayUi("soft_chip");
            if (_gooby != null) _gooby.transform.localScale = new Vector3(1.1f, _geduckt ? 0.78f : 1f, 1.1f);
        }

        private void Beruhigen()
        {
            if (_z.Phase != RanchZaehmen.Beruhigen) return;
            _z.BeruhigenTap();
            Ereignis();
        }

        /// <summary>QA: taps right on every snort.</summary>
        public bool QaAutoTap;

        public void QaSchleichen(bool an) => _schleicht = an;
        public void QaDucken() => ToggleDucken();
        public void QaBeruhigen() => Beruhigen();
        public double QaTaktRest => RanchZaehmen.TaktS - _z.SeitSchnaubenS;

        // ── per frame ──

        public override void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            _juice.Tick(dt);
            _wetter?.Tick(dt);
            if (_fertig) return;
            switch (_z.Phase)
            {
                case RanchZaehmen.Anschleichen:
                    if (_schleicht)
                    {
                        var ziel = _pferdPos - new Vector3(0f, 0f, (float)RanchZaehmen.BeruehrAbstandM * 0.9f);
                        _goobyPos = Vector3.MoveTowards(_goobyPos, ziel, (_geduckt ? DuckSpeed : SchleichSpeed) * dt);
                    }
                    _z.StepAnschleichen(dt, Vector3.Distance(_goobyPos, _pferdPos), _schleicht, _geduckt);
                    break;
                case RanchZaehmen.Beruhigen:
                    _z.StepBeruhigen(dt);
                    if (QaAutoTap && _z.Event == "schnauben")
                    {
                        Ereignis();
                        _z.BeruhigenTap();
                    }
                    break;
                case RanchZaehmen.Davongetrabt:
                    _z.StepCooldown(dt);
                    break;
            }
            Ereignis();
            Darstellen(dt);
        }

        private void Ereignis()
        {
            switch (_z.Event)
            {
                case "bereit":
                    _schleicht = false;
                    _schleichBtn.style.display = DisplayStyle.None;
                    _duckBtn.style.display = DisplayStyle.None;
                    _ruheBtn.style.display = DisplayStyle.Flex;
                    _barLabel.text = T("rhof.zaehmen.ruhe");
                    _pferd?.Action("idle-loop", 0.1f);
                    Sag(T("rhof.zaehmen.bereit"));
                    Sfx.Play("ranch_schnauben_b", 0f, 0.7f);
                    break;
                case "schnauben":
                    _ringPuls = 1f;
                    Sfx.Play("ranch_schnauben_a", 0f, 0.55f);
                    if (_stage != null) Effects.Puff(MgStage3D.Origin + _pferdPos + new Vector3(0.9f, 1.3f, -0.4f), new Color(1f, 1f, 1f, 0.7f), 4, 0.05f, 0.7f, 0.5f, Vector3.left, 0.6f);
                    break;
                case "treffer":
                    _juice.Burst(new Vector2(Root.worldBound.center.x, Root.worldBound.height * 0.42f), new Color(1f, 0.55f, 0.66f), 10);
                    Sfx.PlayUi("soft_tick");
                    break;
                case "daneben":
                    _pferd?.Action("kopfschuetteln");
                    Sfx.PlayUi("soft_error");
                    break;
                case "davongetrabt":
                    _pferdWeg = 1f;
                    _schleicht = false;
                    _pferd?.Gait("trab", 1f);
                    _ruheBtn.style.display = DisplayStyle.None;
                    _schleichBtn.style.display = DisplayStyle.None;
                    _duckBtn.style.display = DisplayStyle.None;
                    Sag(T("rhof.zaehmen.weg"));
                    Sfx.Play("ranch_wiehern_b", 0f, 0.8f);
                    break;
                case "wieder_da":
                    _pferdWeg = 0f;
                    _pferdPos = PferdSpot;
                    _goobyPos = Start;
                    _pferd?.Gait("stand");
                    _pferd?.Action("fressen-loop", 100000f);
                    _schleichBtn.style.display = DisplayStyle.Flex;
                    _duckBtn.style.display = DisplayStyle.Flex;
                    _barLabel.text = T("rhof.zaehmen.aufmerksam");
                    Sag(T("rhof.zaehmen.wieder"));
                    break;
                case "gezaehmt":
                    Gezaehmt();
                    break;
            }
        }

        private void Gezaehmt()
        {
            _fertig = true;
            string id = null;
            var tag = _tag;
            _host.Store.Update(s => id = RanchZaehmen.Aufnehmen(s, tag), GameHost.TicksMs);
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _ruheBtn.style.display = DisplayStyle.None;
            _pferd?.Action("kopfschuetteln");
            _gooby?.Play("celebrate");
            _juice.Confetti(120);
            Sfx.PlayUi("soft_levelup");
            Sfx.Play("ranch_wiehern_a", 2f, 0.9f);
            var name = id != null ? J.Str(RanchHof.Pferd(_host.Store.State, id)?["name"]) : "";
            Sag(T(id != null ? "rhof.zaehmen.gezaehmt" : "rhof.zaehmen.kein_platz", ("name", name)));
            if (id != null) _host.Ui.Toast(T("rhof.zaehmen.gezaehmt", ("name", name)), "horseshoe");
            Debug.Log("[zaehmen] gezaehmt → " + id);
            Root.schedule.Execute(() => { if (_host.Ui.Current == this) _host.Ui.Pop(); }).StartingIn(4000);
        }

        private void Darstellen(float dt)
        {
            var aufmerksam = _z.Phase == RanchZaehmen.Anschleichen || _z.Phase == RanchZaehmen.Davongetrabt;
            var wert = aufmerksam ? _z.Aufmerksamkeit / RanchZaehmen.AufmerksamMax : _z.Ruhe / RanchZaehmen.RuheZiel;
            Ux.SetBar(_barFill, wert);
            _barFill.style.backgroundColor = aufmerksam ? Color.Lerp(new Color(0.55f, 0.8f, 0.55f), new Color(0.92f, 0.36f, 0.32f), (float)wert) : new Color(0.95f, 0.55f, 0.66f);
            if (_stage == null) return;
            if (_pferdWeg > 0f)
            {
                _pferdPos = Vector3.MoveTowards(_pferdPos, new Vector3(9f, 0f, 20f), 4f * dt);
                if (_pferd != null) _pferd.transform.rotation = Quaternion.Slerp(_pferd.transform.rotation, Quaternion.LookRotation(new Vector3(9f, 0f, 20f) - _pferdPos + Vector3.forward * 0.01f), 6f * dt);
            }
            if (_pferd != null) _pferd.transform.position = MgStage3D.Origin + _pferdPos;
            if (_gooby != null)
            {
                _gooby.transform.position = MgStage3D.Origin + _goobyPos;
                var clip = _schleicht && _z.Phase == RanchZaehmen.Anschleichen ? "walk-loop" : "idle-loop";
                if (clip != _goobyClip && _z.Phase != RanchZaehmen.Gezaehmt) { _gooby.Play(clip); _goobyClip = clip; }
            }
            if (_ring != null)
            {
                _ring.gameObject.SetActive(_z.Phase == RanchZaehmen.Beruhigen);
                _ringPuls = Mathf.Max(0f, _ringPuls - dt * 2.2f);
                var s = 1f + (1f - _ringPuls) * 0.6f;
                _ring.position = MgStage3D.Origin + _pferdPos + Vector3.up * 0.03f;
                _ring.localScale = new Vector3(2.8f * s, 0.02f, 2.8f * s);
            }
            Kamera();
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this))
            {
                _wetter?.SetActive(false);
                return;
            }
            _wetter?.Destroy();
            _wetter = null;
            if (_night && _stage != null) MgStudio.Day();
            _stage?.Destroy();
            _stage = null;
        }

        public override void OnShown() => _wetter?.SetActive(true);

        /// <summary>QA: force a weather at the forest edge ("regen", "nebel", "plan" …).</summary>
        public void QaWetter(string typ) => _wetter?.Erzwingen(typ);
    }
}
