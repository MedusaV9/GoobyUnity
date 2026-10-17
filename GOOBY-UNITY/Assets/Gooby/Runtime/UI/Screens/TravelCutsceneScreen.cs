using Gooby.Core.City;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// 5-shot departure / shortened return (reise_cutscene.gd). Reduced motion shows the authored text cards;
    /// otherwise the world stages a taxi-and-plane reel. Skip after two seconds.
    /// </summary>
    public sealed class TravelCutsceneScreen : UiScreen
    {
        private readonly GameHost _host;

        public override string SoundScene => "vacation";
        private readonly string _modus;
        private readonly bool _reduced;
        private Label _card;
        private VisualElement _skip;
        private float _t;
        private bool _skipping, _done;
        private string _shot;

        public TravelCutsceneScreen(GameHost host, string modus)
        {
            _host = host;
            _modus = modus == TravelCutscene.ModusRueckkehr ? TravelCutscene.ModusRueckkehr : TravelCutscene.ModusAbflug;
            _reduced = host.Settings != null && host.Settings.ReducedMotion;
            Id = "travel-cutscene";
            Opaque = true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.style.backgroundColor = _reduced ? new Color(1f, 0.96f, 0.92f) : Color.clear;
            if (_reduced)
            {
                _card = Ux.Text("", "gb-h2");
                _card.name = "cutscene-card";
                _card.style.unityTextAlign = TextAnchor.MiddleCenter;
                _card.style.color = new Color(0.29f, 0.23f, 0.21f);
                _card.style.whiteSpace = WhiteSpace.Normal;
                Ux.Abs(_card, 80, 80, 80, 80);
                root.Add(_card);
                ShowCard();
            }
            _skip = Ux.Btn(t.T("travel.cutscene.skip"), Skip, "gb-btn-secondary");
            _skip.name = "skip-cutscene";
            _skip.style.display = DisplayStyle.None;
            Ux.Abs(_skip, null, null, null, 24);
            _skip.style.left = Length.Percent(40);
            _skip.style.right = Length.Percent(40);
            root.Add(_skip);
            if (!_reduced) _host.World?.EnterTravelCutscene(_modus);
            Debug.Log("[cutscene] start modus=" + _modus + " reduced=" + _reduced);
        }

        public override void Update()
        {
            if (_done) return;
            _t += Time.unscaledDeltaTime;
            if (TravelCutscene.SkipVisible(_t) && _skip.style.display == DisplayStyle.None)
                _skip.style.display = DisplayStyle.Flex;
            if (_reduced) ShowCard();
            else
            {
                var shot = TravelCutscene.At(_modus, _t, _skipping);
                if (shot != null && shot.Id != _shot)
                {
                    _shot = shot.Id;
                    _host.World?.TravelCutsceneShot(shot.Id);
                    Debug.Log("[cutscene] shot=" + shot.Id + " t=" + _t.ToString("F1"));
                }
                _host.World?.TickTravelCutscene(Time.unscaledDeltaTime);
            }
            if (TravelCutscene.Finished(_modus, _t, _skipping, _reduced)) Finish();
        }

        public override void OnHidden()
        {
            if (!_reduced) _host.World?.LeaveTravelCutscene();
        }

        private void ShowCard()
        {
            if (_card == null) return;
            var keys = TravelCutscene.CardKeys(_modus);
            var i = TravelCutscene.CardIndex(_t, keys.Length);
            _card.text = _host.Strings.T(keys[i]);
        }

        private void Skip()
        {
            if (_t < TravelCutscene.SkipAfterS) return;
            _skipping = true;
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            Debug.Log("[cutscene] done modus=" + _modus);
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }
    }
}
