using System;
using System.Collections.Generic;

namespace Gooby.Core.City
{
    /// <summary>
    /// GPS of the street drive — port of drive_hud.gd's W13B "Ziel-Chevron" (Doc E §1.4 GPS-Pfeil): a tap on a minimap
    /// pin makes that place the destination, the same pin again switches GPS off; a wedge at the screen edge points to the
    /// destination's parking spot (projected through the camera, a straight line like in Godot) and hides while the spot
    /// is on screen; the place's parking prompt clears it. These are the two pure pieces of maths behind it.
    /// </summary>
    public static class CityGps
    {
        /// <summary>Inset of the wedge from the screen edge (px).</summary>
        public const double ChevronRandPx = 36.0;

        /// <summary>How close to a pin a tap must land (minimap px).</summary>
        public const double ZielTippRadiusPx = 18.0;

        public readonly struct Lage
        {
            public readonly bool Sichtbar;
            public readonly double X, Y;
            /// <summary>Direction of the wedge in screen space (radians, 0 = right, +π/2 = down).</summary>
            public readonly double Winkel;

            public Lage(bool sichtbar, double x, double y, double winkel)
            {
                Sichtbar = sichtbar; X = x; Y = y; Winkel = winkel;
            }
        }

        /// <summary>
        /// Where the wedge sits: the projected destination (<paramref name="zielX"/>, <paramref name="zielY"/>, screen px with
        /// y down) pushed onto the inner rectangle <paramref name="randPx"/> from the edge, along the line from the screen
        /// centre. A point behind the camera is mirrored through the centre first and always needs the wedge; a point
        /// inside the rectangle does not (the place is in view).
        /// </summary>
        public static Lage ChevronPlatzierung(double schirmW, double schirmH, double zielX, double zielY, bool hinterKamera, double randPx = ChevronRandPx)
        {
            var mx = schirmW * 0.5;
            var my = schirmH * 0.5;
            var px = zielX;
            var py = zielY;
            if (hinterKamera)
            {
                px = mx * 2.0 - zielX;
                py = my * 2.0 - zielY;
            }
            var innen = px >= randPx && py >= randPx && px < schirmW - randPx && py < schirmH - randPx;
            if (!hinterKamera && innen) return new Lage(false, px, py, 0.0);
            var rx = px - mx;
            var ry = py - my;
            if (rx * rx + ry * ry < 0.000001) { rx = 0.0; ry = 1.0; }
            var skala = 1.0e9;
            if (Math.Abs(rx) > 0.0001) skala = Math.Min(skala, (mx - randPx) / Math.Abs(rx));
            if (Math.Abs(ry) > 0.0001) skala = Math.Min(skala, (my - randPx) / Math.Abs(ry));
            return new Lage(true, mx + rx * skala, my + ry * skala, Math.Atan2(ry, rx));
        }

        /// <summary>The pin nearest to a tap within <paramref name="maxD"/> px ("" when none; a tie goes to the first).</summary>
        public static string NaechsterPin(IEnumerable<(string id, double x, double y)> pins, double tippX, double tippY, double maxD = ZielTippRadiusPx)
        {
            var besteId = "";
            var besteD = maxD;
            foreach (var (id, x, y) in pins)
            {
                var d = Math.Sqrt((tippX - x) * (tippX - x) + (tippY - y) * (tippY - y));
                if (d < besteD)
                {
                    besteD = d;
                    besteId = id;
                }
            }
            return besteId;
        }
    }
}
