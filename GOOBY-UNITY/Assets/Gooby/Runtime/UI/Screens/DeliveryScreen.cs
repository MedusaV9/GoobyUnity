using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The furniture delivery (port of delivery_cutscene.gd): back home after a furniture order, Gooby stands at the
    /// driveway of his cottage with a clipboard, the delivery van drives up, he waves it in, the boxes come off (one
    /// per piece, at most five), he signs, everything goes into the storage, the van drives off and he cheers —
    /// "n Möbel im Lager!". "Überspringen" books the delivery at once. Without a GPU the same steps run unseen.
    /// </summary>
    public sealed class DeliveryScreen : UiScreen
    {
        private const float WarteS = 1.0f, AnfahrtS = 2.6f, PauseS = 1.1f, AbfahrtS = 2.0f, Strecke = 18f;
        /// <summary>The van stops on the road just left of the driveway, so Gooby and the boxes on the driveway stay in view.</summary>
        private static readonly Vector3 Einfahrt = new Vector3(-3.0f, 0f, 2.2f);
        private static readonly Vector3 Auffahrt = new Vector3(1.0f, 0f, 4.4f);
        private static readonly Vector3 Posten = Auffahrt + new Vector3(1.6f, 0f, -0.9f);

        private readonly GameHost _host;
        private readonly Juice _juice = new Juice();
        private MgStage3D _stage;
        private Transform _van;
        private GoobyAvatar _gooby;
        private VisualElement _field, _bubble;
        private Label _bubbleText;
        private float _t;
        private int _beat, _stueck, _menge;
        private bool _fertig;

        public DeliveryScreen(GameHost host)
        {
            _host = host;
            Id = "lieferung";
            Opaque = true;
        }

        public override string SoundScene => "home";

        private string T(string key, params (string, object)[] args) => _host.Strings.T(key, args);

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-lieferung");
            _field = new VisualElement { name = "lieferung-field" };
            Ux.Abs(_field, 0, 0, 0, 0);
            _field.style.backgroundColor = ArtLibrary.Hex("#BFE3F7");
            root.Add(_field);
            _stueck = Lieferung.Stueck(_host.Store.State);
            if (MgStage3D.Supported) BuildStage();

            _bubble = Ux.Row("gb-card", "gb-ranchhof-bubble");
            _bubble.name = "lieferung-sagt";
            _bubble.pickingMode = PickingMode.Ignore;
            _bubbleText = Ux.Text("", "gb-ranchhof-bubble-text");
            _bubble.Add(_bubbleText);
            root.Add(_bubble);
            var skip = Ux.Btn(T("lieferung.ueberspringen"), Ueberspringen, "gb-btn-secondary");
            skip.name = "lieferung-skip";
            Ux.Abs(skip, null, 16, 16);
            root.Add(skip);
            root.Add(_juice.Layer);
            Sag(T("lieferung.warten"));
            Debug.Log("[lieferung] auf: stueck=" + _stueck);
        }

        private void BuildStage()
        {
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            _stage = new MgStage3D(720f * aspect, 720f, ArtLibrary.Hex("#BFE3F7"));
            _stage.Bind(_field);
            _stage.Aim(new Vector3(0.9f, 3.0f, -7.8f), 12f, 40f);
            var root = _stage.Root.transform;
            Vector3 At(Vector3 p) => MgStage3D.Origin + p;
            Material M(string hex, float s = 0.2f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), s);
            _stage.Ground(0f, ArtLibrary.Hex("#A8D58A"), "grass", 90f, 70f, 20f);
            ArtLibrary.Box(root, "Road", At(new Vector3(0f, 0.015f, Einfahrt.z)), new Vector3(46f, 0.03f, 3.6f), M("#6B6E76", 0.1f), false);
            for (var x = -22f; x < 22f; x += 3f)
                ArtLibrary.Box(root, "Dash", At(new Vector3(x, 0.035f, Einfahrt.z)), new Vector3(1.4f, 0.01f, 0.14f), M("#F2EFE6", 0.1f), false);
            foreach (var z in new[] { Einfahrt.z - 2.1f, Einfahrt.z + 2.1f })
                ArtLibrary.Box(root, "Kerb", At(new Vector3(0f, 0.05f, z)), new Vector3(46f, 0.1f, 0.6f), M("#D9D2C3", 0.1f), false);
            ArtLibrary.Box(root, "Driveway", At(new Vector3(Auffahrt.x, 0.02f, 6.4f)), new Vector3(3.6f, 0.04f, 5.6f), M("#D8C7A4", 0.05f), false);
            _stage.PlaceScenery("cottage", At(new Vector3(1.0f, 0f, 11.2f)), 180f);
            _stage.PlaceScenery("mailbox_red", At(new Vector3(-1.4f, 0f, 4.9f)), 180f, 0.95f);
            _stage.PlaceScenery("hedge_long", At(new Vector3(-6.2f, 0f, 5.0f)), 0f);
            _stage.PlaceScenery("hedge_long", At(new Vector3(8.0f, 0f, 5.0f)), 0f);
            _stage.PlaceScenery("tree_round_big", At(new Vector3(-8.5f, 0f, 10.5f)), 20f, 1.2f);
            _stage.PlaceScenery("tree_round_big", At(new Vector3(9.5f, 0f, 10f)), 110f, 1.1f);
            _stage.Hills(0f, 34f, 7, ArtLibrary.Hex("#9CC98C"), 7f, 90f, 0f, 3);
            _stage.Clouds(4, 12f, 40f, 60f, 2f, 5);
            _stage.Sun(new Vector3(-18f, 18f, 50f), 2.6f);
            _van = _stage.Glb(AssetLoader.VehiclePath("van_delivery"), At(Einfahrt + new Vector3(-Strecke, 0.32f, 0f)), 1.15f, 90f);
            _ = SpawnGoobyAsync();
        }

        private async System.Threading.Tasks.Task SpawnGoobyAsync()
        {
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"]));
            var avatar = await _stage.SpawnGoobyAsync(MgStage3D.Origin + Posten, 1f, outfit);
            if (avatar == null || _stage == null) return;
            _gooby = avatar;
            avatar.transform.rotation = Quaternion.Euler(0f, 205f, 0f);
            var board = ArtLibrary.Box(avatar.transform, "Clipboard", avatar.transform.TransformPoint(new Vector3(0.22f, 0.62f, 0.3f)), new Vector3(0.26f, 0.34f, 0.03f), ArtLibrary.Solid(ArtLibrary.Hex("#B98A5E"), 0.2f), false);
            board.transform.rotation = avatar.transform.rotation * Quaternion.Euler(-20f, 0f, 0f);
            ArtLibrary.Box(avatar.transform, "Paper", board.transform.TransformPoint(new Vector3(0f, -0.04f, -0.8f)), new Vector3(0.22f, 0.26f, 0.01f), ArtLibrary.Solid(ArtLibrary.Hex("#FFFDF6"), 0.1f), false)
                .transform.rotation = board.transform.rotation;
        }

        private void Sag(string text)
        {
            _bubbleText.text = text;
            _juice.ScalePop(_bubble, 1.06f, 0.18f);
        }

        // ── the steps ──

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            if (_fertig) return;
            _t += dt;
            var anfahrt = WarteS;
            var da = anfahrt + AnfahrtS;
            var abladen = da + PauseS;
            var einlagern = abladen + PauseS;
            var weg = einlagern + AbfahrtS;
            if (_van != null)
            {
                if (_t < da) _van.position = MgStage3D.Origin + Einfahrt + new Vector3(-Strecke * Ease(1f - Mathf.Clamp01((_t - anfahrt) / AnfahrtS)), 0.32f, 0f);
                else if (_t >= einlagern) _van.position = MgStage3D.Origin + Einfahrt + new Vector3(Strecke * Mathf.Pow(Mathf.Clamp01((_t - einlagern) / AbfahrtS), 2f), 0.32f, 0f);
            }
            if (_beat == 0 && _t >= anfahrt) { _beat = 1; Sfx.Play("travel_whoosh_auf", 0f, 0.6f); }
            else if (_beat == 1 && _t >= da)
            {
                _beat = 2;
                _gooby?.Play("wave");
                _gooby?.SetEmotion("happy");
                Sag(T("lieferung.einweisen"));
            }
            else if (_beat == 2 && _t >= abladen)
            {
                _beat = 3;
                Abladen();
                Sag(T("lieferung.abhaken"));
            }
            else if (_beat == 3 && _t >= einlagern)
            {
                _beat = 4;
                Einlagern();
                Sfx.Play("travel_whoosh_zu", 0f, 0.6f);
            }
            else if (_beat == 4 && _t >= weg) Fertig();
        }

        private static float Ease(float k) => k * k * (3f - 2f * k);

        private void Abladen()
        {
            var n = Mathf.Clamp(_stueck, 1, 5);
            for (var i = 0; i < n; i++)
            {
                var k = i;
                Root.schedule.Execute(() =>
                {
                    if (_stage == null) return;
                    var at = MgStage3D.Origin + Auffahrt + new Vector3(-1.3f + (k % 3) * 0.62f, 0.25f + (k / 3) * 0.5f, (k / 3) * 0.1f);
                    var box = ArtLibrary.Box(_stage.Root.transform, "Karton", at, new Vector3(0.5f, 0.5f, 0.5f), ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.15f));
                    box.transform.rotation = Quaternion.Euler(0f, k * 17f - 20f, 0f);
                    var tape = ArtLibrary.Box(_stage.Root.transform, "Tape", at + Vector3.up * 0.252f, new Vector3(0.12f, 0.01f, 0.51f), ArtLibrary.Solid(ArtLibrary.Hex("#E8D6AA"), 0.3f), false);
                    tape.transform.rotation = box.transform.rotation;
                    Effects.Puff(at + Vector3.up * 0.3f, new Color(0.9f, 0.82f, 0.6f, 0.9f), 5, 0.06f, 0.9f, 0.4f, Vector3.up, 0.8f);
                    Sfx.PlayUi("game_pop");
                }).StartingIn(150 * k);
            }
        }

        private void Einlagern()
        {
            var n = 0;
            _host.Store.Update(s => n = Lieferung.Einlagern(s), GameHost.TicksMs);
            _menge = n;
            _host.Store.NotifySliceChanged(HomeState.SliceId);
            Debug.Log("[lieferung] eingelagert " + n);
        }

        private void Fertig()
        {
            if (_fertig) return;
            _fertig = true;
            if (_beat < 4) Einlagern();
            _gooby?.Play("celebrate");
            _gooby?.SetEmotion("ecstatic");
            Sag(T("lieferung.fertig", ("menge", _menge)));
            _juice.Confetti(70);
            Sfx.PlayUi("soft_levelup");
            Root.schedule.Execute(Schliessen).StartingIn(2400);
        }

        private void Ueberspringen()
        {
            Sfx.PlayUi("soft_chip");
            Fertig();
            Schliessen();
        }

        private void Schliessen()
        {
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
