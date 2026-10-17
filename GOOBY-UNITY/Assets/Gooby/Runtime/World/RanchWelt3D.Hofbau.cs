using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.World
{
    public sealed partial class RanchWelt3D
    {
        /// <summary>The build grid's north-west corner in region metres: east of the paddock, clear of the paths and the herd.</summary>
        public const double BauX0 = 134.0, BauZ0 = 80.0;
        private const double BauKante = 48.0;

        private Transform _hofbau;

        /// <summary>Is a region point on (or right next to) the player's build ground? The farm's trees keep off it.</summary>
        public static bool AufBauplatz(double x, double z, double rand = 8.0) =>
            x > BauX0 - rand && x < BauX0 + BauKante + rand && z > BauZ0 - rand && z < BauZ0 + BauKante + rand;

        /// <summary>
        /// The ranch the player built (<see cref="RanchBauState"/>) beside the paddock: every piece at its level on the land,
        /// set down at the lowest point of its footprint so nothing floats on the slope — ride out and the riding hall, the
        /// stand, the paths and the fences are there.
        /// </summary>
        public void Hofbau(JObject bau, IReadOnlyList<int> schleifen = null)
        {
            if (_hofbau != null) UnityEngine.Object.Destroy(_hofbau.gameObject);
            _hofbau = new GameObject("Hofbau").transform;
            _hofbau.SetParent(Gruppe("hof"), false);
            if (bau == null) return;
            var grid = RanchBauState.GridAus(bau).grid;
            var zaun = RanchBauState.AnlageStufe(bau, "weidezaun");
            foreach (var item in grid.Items.Values)
            {
                double gx, gz, halbX, halbZ;
                float yaw;
                if (item.Kante != null)
                {
                    var w = RanchGridData.KanteWorld(item.At, item.Kante);
                    (gx, gz, yaw) = (w.x, w.z, (float)(w.rot * Mathf.Rad2Deg));
                    (halbX, halbZ) = item.Kante == "N" ? (1.5, 0.1) : (0.1, 1.5);
                }
                else
                {
                    var c = RanchGridData.WorldCenter(item.At, item.Def.Footprint, item.Rot);
                    var fp = RanchGridData.RotatedFootprint(item.Def.Footprint, item.Rot);
                    (gx, gz, yaw) = (c.x, c.z, item.Rot * 90f);
                    (halbX, halbZ) = (fp.X * RanchGridData.CellSize * 0.5, fp.Y * RanchGridData.CellSize * 0.5);
                }
                double x = BauX0 + gx, z = BauZ0 + gz;
                var h = RanchGelaende.Hoehe(x, z);
                foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) }) h = Math.Min(h, RanchGelaende.Hoehe(x + dx * halbX, z + dz * halbZ));
                var t = new GameObject("Bau:" + item.Def.Id).transform;
                t.SetParent(_hofbau, false);
                t.position = _root.position + L(x, h - 0.04, z);
                t.rotation = Quaternion.Euler(0f, yaw, 0f);
                if (item.Kante != null) RanchBauTeile.Zaun(t, zaun);
                else RanchBauTeile.Baue(t, item.Def, Math.Max(1, RanchBauState.AnlageStufe(bau, item.Def.Id)), schleifen);
                if (item.Def.Kategorie == "anlage" && item.Kante == null) Stamm(x, z, (float)Math.Min(halbX, halbZ));
            }
            Debug.Log("[ranchwelt] hofbau: " + grid.Items.Count + " Teile");
        }
    }
}
