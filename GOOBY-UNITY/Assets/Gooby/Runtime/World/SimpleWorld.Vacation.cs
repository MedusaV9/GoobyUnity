using System.Collections.Generic;
using Gooby.Core.Cosmetics;
using Gooby.Core.Json;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Visiting Gooby on holiday as a stage of the world (VacationScene): the home stays where it is, the holiday scene is
    /// built far off, Gooby — away from home, so hidden there — stands in it in his holiday clothes, lit at the
    /// destination's own hour. Leaving puts his outfit back and hides him again until he is picked up.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private VacationScene _vacation;

        public VacationScene Vacation => _vacation;

        /// <summary>Builds the holiday scene of <paramref name="destId"/> and puts Gooby into it. Call inside a fade.</summary>
        public void EnterVacation(string destId)
        {
            if (_vacation != null || Camera == null) return;
            EndRoutine();
            CancelAutoWalk();
            // a place we came from (the airport desk) stays built underneath and comes back on leaving
            _vacationFromStreet = _interior == null && _city != null && _city.Root.activeSelf;
            _city?.Root.SetActive(false);
            _vacation = new VacationScene(_root.transform, destId);
            if (destId == "space") SceneDressing.SetSpace(Look);
            else
            {
                SceneDressing.SetIndoor(Look, false);
                SceneDressing.SetTimeOfDay(Look, _vacation.Hour, _vacation.Sky);
            }
            SceneDressing.SetWindowCookie(Look, false);
            SceneDressing.SetFocus(Look, 9f, 40f);
            SceneDressing.SetHaze(false, _vacation.Sky);
            Camera.backgroundColor = _vacation.Sky;
            Camera.transform.SetPositionAndRotation(_vacation.CameraPosition, _vacation.CameraRotation);
            Camera.fieldOfView = 44f;
            if (Gooby != null) Gooby.gameObject.SetActive(true);
            PlaceGoobyAt(_vacation.GoobySpot, "idle-loop");
            if (Gooby != null) Gooby.rotation = Quaternion.Euler(0f, 180f, 0f);
            if (Avatar != null)
            {
                var fell = CosmeticsState.EquippedMap(J.Dict(_gs.State["cosmetics"])).TryGetValue("fell", out var f) ? f : null;
                Avatar.Wear(_vacation.GoobyHolidayLook(fell));
                Avatar.SetEmotion("ecstatic");
            }
            _ = _vacation.SpawnTouristsAsync();
        }

        /// <summary>Back home: the scene goes, Gooby's own outfit comes back and he is "away" again.</summary>
        public void LeaveVacation()
        {
            if (_vacation == null) return;
            _vacation.Destroy();
            _vacation = null;
            ApplyOutfit();
            if (_interior != null) ApplyInteriorView();
            else PlaceGooby(InGarden);
            RefreshAway();
            if (_interior != null) return;
            if (_vacationFromStreet && _city != null) SetCityScene(true);
            else if (InGarden) { EnsureGarden(); _buildCam = 0; ApplyBuildCamera(); }
            else SetHomeCamera();
        }

        private bool _vacationFromStreet;

        /// <summary>The holiday spot a screen tap hits (-1: none).</summary>
        public int TapVacationSpot(Vector2 screenPos)
        {
            if (_vacation == null || Camera == null) return -1;
            var ray = Camera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 60f)) return -1;
            var marker = hit.collider.GetComponentInParent<VacationSpotMarker>();
            return marker != null ? marker.Index : -1;
        }

        /// <summary>A tap in the holiday scene that lands on Gooby.</summary>
        public bool TapVacationGooby(Vector2 screenPos)
        {
            if (_vacation == null || Camera == null) return false;
            var ray = Camera.ScreenPointToRay(screenPos);
            return Physics.Raycast(ray, out var hit, 60f) && hit.collider.GetComponentInParent<GoobyMarker>() != null;
        }

        private void TickVacation(float dt)
        {
            if (_vacation == null) return;
            _vacation.Tick(dt, ReducedMotion);
        }
    }
}
