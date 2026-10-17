using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    public sealed partial class HomeHudScreen
    {
        /// <summary>Auto-pop after this long — a stuck Gooby never blocks a child who cannot mash fast enough.</summary>
        private const float DoorMashGiveUpS = 12f;

        private DoorLogic _door;
        private RoomDefs.Door _doorTarget;
        private VisualElement _doorVeil, _doorFill;
        private Label _doorHint;
        private bool _doorWasStuck;
        private float _doorMashAge, _doorSquash;
        private Vector3 _doorGoobyScale = Vector3.one;

        /// <summary>QA: the next door sticks for sure.</summary>
        public bool QaDoorStuck;

        /// <summary>
        /// Gooby reached a door (door_transition.gd + door_logic.gd): mostly straight through behind the curtain, now and
        /// then he gets stuck in the frame and the player taps him free.
        /// </summary>
        private void OnDoorReached(RoomDefs.Door door)
        {
            if (_host.Ui.Fading || _door != null) return;
            var logic = new DoorLogic(!_host.Settings.ReducedMotion, _doorWasStuck && !QaDoorStuck, QaDoorStuck ? 0.0 : Random.value, Random.value);
            QaDoorStuck = false;
            logic.Begin();
            logic.DoorOpened();
            if (logic.ReachedDoor() == DoorLogic.State.Stuck)
            {
                _doorWasStuck = true;
                StartDoorMash(door, logic);
                return;
            }
            _doorWasStuck = false;
            _host.Ui.FadeThrough(() => _host.World.PassDoor(door));
        }

        private void StartDoorMash(RoomDefs.Door door, DoorLogic logic)
        {
            _door = logic;
            _doorTarget = door;
            _doorMashAge = 0f;
            _host.World?.WedgeInDoor(door);
            if (_host.World?.Gooby != null) _doorGoobyScale = _host.World.Gooby.localScale;
            _doorVeil = new VisualElement { name = "tuer-mash" };
            Ux.Abs(_doorVeil, 0, 0, 0, 0);
            _doorVeil.RegisterCallback<PointerDownEvent>(_ => DoorTap());
            var card = Ux.Col("gb-card", "gb-tuer-card");
            card.pickingMode = PickingMode.Ignore;
            var title = Ux.Text(_host.Strings.T("home.tuer.stuck"), "gb-h2");
            title.pickingMode = PickingMode.Ignore;
            card.Add(title);
            var bar = Ux.Bar(0f, out _doorFill);
            bar.AddToClassList("gb-tuer-bar");
            bar.pickingMode = PickingMode.Ignore;
            _doorFill.style.backgroundColor = new Color(0.95f, 0.65f, 0.35f);
            card.Add(bar);
            _doorHint = Ux.Text(_host.Strings.T("home.tuer.mash_hint"), "gb-tuer-hint");
            _doorHint.pickingMode = PickingMode.Ignore;
            card.Add(_doorHint);
            _doorVeil.Add(card);
            Root.Add(_doorVeil);
            _host.World?.Avatar?.SetEmotion("scared");
            Sfx.PlayUi("soft_error");
            Debug.Log("[tuer] steckt fest: " + logic.TapsRequired + " Taps");
        }

        public void QaDoorTap() => DoorTap();

        private void DoorTap()
        {
            if (_door == null) return;
            var state = _door.TapMash();
            var ratio = (float)_door.MashRatio;
            Sfx.Play("soft_tick", ratio * 7f, 0.8f);
            _doorSquash = 0.25f;
            _host.World?.Avatar?.Play("hop");
            if (_door.IstLetzterTap) _doorHint.text = "!!!";
            if (state == DoorLogic.State.Popping) DoorPop();
        }

        private void DoorPop()
        {
            var door = _doorTarget;
            if (_host.World?.Gooby != null) _host.World.Gooby.localScale = _doorGoobyScale;
            _door?.PopFinished();
            _door = null;
            _doorVeil?.RemoveFromHierarchy();
            _doorVeil = null;
            Sfx.PlayUi("game_pop");
            _host.World?.Avatar?.SetEmotion("happy");
            Debug.Log("[tuer] plopp");
            Root.schedule.Execute(() => _host.Ui.FadeThrough(() => _host.World.PassDoor(door))).StartingIn(250);
        }

        /// <summary>The pressure drains (faster near the end); a squash pulse on Gooby per tap; the give-up pop.</summary>
        private void TickDoorMash(float dt)
        {
            if (_door == null) return;
            _door.MashDecay(dt);
            _doorMashAge += dt;
            if (_doorFill != null) Ux.SetBar(_doorFill, _door.MashRatio);
            var gooby = _host.World?.Gooby;
            if (gooby != null && _doorSquash > 0f)
            {
                _doorSquash = Mathf.Max(0f, _doorSquash - dt);
                var k = _doorSquash / 0.25f;
                gooby.localScale = Vector3.Scale(_doorGoobyScale, new Vector3(1f + 0.18f * k, 1f - 0.14f * k, 1f + 0.18f * k));
                if (_doorSquash <= 0f) gooby.localScale = _doorGoobyScale;
            }
            if (_doorMashAge > DoorMashGiveUpS) DoorPop();
        }
    }
}
