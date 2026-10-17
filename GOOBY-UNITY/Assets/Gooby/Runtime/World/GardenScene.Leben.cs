using System.Collections.Generic;
using Gooby.Core.Home;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The garden's own life (port of garten_leben.gd): a clothesline behind the north fence beside the cottage with a
    /// shirt, a towel and socks swaying on a real catenary, two birds circling over the lawn, three butterflies wandering
    /// over the grass and a ginger cat on the west palisade swishing its tail. Birds, butterflies and the dove are out by
    /// day only and shelter from rain and snow; the cat and the laundry stay. A fixed seed: every screenshot sees the
    /// same garden. All of it sits outside the build grid or high above it.
    /// </summary>
    public sealed partial class GardenScene
    {
        private const float LineHeight = 1.6f, LineSag = 0.2f, BirdHeight = 3.4f, BirdWingBeat = 9f, ButterflyWingBeat = 15f;

        private readonly List<Flyer> _birds = new List<Flyer>();
        private readonly List<Flyer> _butterflies = new List<Flyer>();
        private readonly List<Transform> _laundry = new List<Transform>();
        private Transform _catTail;
        private GameObject _dove;
        private float _lifeT;
        private bool _lifeOut = true;

        /// <summary>Where the cat sits: on a tall post of the west palisade north of the dove, which it keeps an eye on.</summary>
        public Vector3 CatSeat { get; private set; }

        private void BuildLife(Transform t)
        {
            var rng = new System.Random(0x6A72);
            BuildClothesline(t);
            BuildCat(t);
            for (var i = 0; i < 2; i++)
            {
                var bird = Flyer.Bird(t, i == 1, 1.5f);
                bird.Radius = LawnW * Range(rng, 0.28f, 0.4f);
                bird.Height = BirdHeight + i * 0.7f;
                bird.Speed = Range(rng, 0.35f, 0.5f) * (i == 0 ? 1f : -1f);
                bird.Phase = Range(rng, 0f, Mathf.PI * 2f);
                _birds.Add(bird);
            }
            for (var i = 0; i < 3; i++)
            {
                var b = Flyer.Butterfly(t, i, 1.35f);
                b.Anchor = Origin + new Vector3(Range(rng, LawnW * 0.25f, LawnW * 0.75f), 0f, Range(rng, LawnD * 0.35f, LawnD * 0.8f));
                b.A = Range(rng, 0.5f, 0.8f);
                b.B = Range(rng, 0.3f, 0.55f);
                b.Phase = Range(rng, 0f, Mathf.PI * 2f);
                _butterflies.Add(b);
            }
        }

        private static float Range(System.Random rng, float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        /// <summary>
        /// Two wooden posts between the cottage's east wall and the fence corner, the line hung as a catenary between
        /// their crossbars and the laundry pegged at three of its points (each swings about the line).
        /// </summary>
        private void BuildClothesline(Transform t)
        {
            var z = North + 1.7f;
            var xa = CottageX + CottageHalfWidth + 1.0f;
            var xb = Mathf.Min(xa + 3.0f, East - 0.9f);
            Scenery.Place(t, "clothes_post", new Vector3(xa, 0f, z), 90f);
            Scenery.Place(t, "clothes_post", new Vector3(xb, 0f, z), 90f);
            var points = Catenary.Points((xa, LineHeight, z), (xb, LineHeight, z), 10, LineSag);
            var line = ArtLibrary.Solid(ArtLibrary.Hex("#E6E1DA"), 0.2f);
            for (var i = 0; i + 1 < points.Length; i++)
            {
                var a = V(points[i]); var b = V(points[i + 1]);
                var seg = ArtLibrary.Cylinder(t, "Line", (a + b) * 0.5f, 0.008f, Vector3.Distance(a, b), line, false);
                seg.transform.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
            }
            var pieces = new[] { "laundry_shirt", "laundry_towel", "laundry_socks" };
            var at = new[] { 3, 5, 7 };
            for (var i = 0; i < pieces.Length; i++)
            {
                var hanger = new GameObject("Laundry").transform;
                hanger.SetParent(t, false);
                hanger.position = V(points[at[i]]);
                Scenery.Place(hanger, pieces[i], Vector3.zero, 0f);
                _laundry.Add(hanger);
            }
        }

        private static Vector3 V((double x, double y, double z) p) => new Vector3((float)p.x, (float)p.y, (float)p.z);

        /// <summary>The cat on a tall post of the west palisade (the segment north of the middle), looking at the lawn.</summary>
        private void BuildCat(Transform t)
        {
            var x0 = West - FenceInset;
            var z0 = South - FenceInset; var z1 = North + FenceInset;
            var segs = Mathf.RoundToInt((z1 - z0) / 1.5f);
            var step = (z1 - z0) / segs;
            var s = step / 1.5f;
            // log_fence: the tall posts stand 0.3 m either side of a segment's middle, their rounded heads end ≈1.07 m up
            CatSeat = new Vector3(x0, 1.06f * s, z0 + step * (segs / 2 + 1.5f) + 0.3f * s);
            var cat = Scenery.Place(t, "cat_fence", CatSeat, 115f).transform;
            _catTail = new GameObject("CatTailPivot").transform;
            _catTail.SetParent(cat, false);
            _catTail.localPosition = new Vector3(0f, 0.06f, -0.12f);
            Scenery.Place(_catTail, "cat_tail", Vector3.zero, 0f);
        }

        /// <summary>One frame of the garden's life; <paramref name="hour"/> decides whether birds and butterflies are out.</summary>
        public void TickLife(float dt, double hour)
        {
            _lifeT += dt;
            var t = _lifeT;
            var outNow = hour >= 6.5 && hour < 20.0 && !_wet;
            if (outNow != _lifeOut)
            {
                _lifeOut = outNow;
                foreach (var f in _birds) f.Root.gameObject.SetActive(outNow);
                foreach (var f in _butterflies) f.Root.gameObject.SetActive(outNow);
                if (_dove != null) _dove.SetActive(outNow);
            }
            for (var i = 0; i < _laundry.Count; i++)
                _laundry[i].localRotation = Quaternion.Euler((Mathf.Sin(t * 1.3f + i * 1.7f) * 0.09f + Mathf.Sin(t * 0.37f + i) * 0.04f) * Mathf.Rad2Deg, 0f, 0f);
            if (_catTail != null)
                _catTail.localRotation = Quaternion.Euler((0.3f + Mathf.Sin(t * 1.8f) * 0.22f) * Mathf.Rad2Deg, 0f, Mathf.Sin(t * 0.9f + 0.6f) * 0.18f * Mathf.Rad2Deg);
            if (!_lifeOut) return;
            var centre = LawnCenter;
            for (var i = 0; i < _birds.Count; i++)
            {
                var b = _birds[i];
                var a = t * b.Speed + b.Phase;
                b.Root.position = centre + new Vector3(Mathf.Cos(a) * b.Radius, b.Height + Mathf.Sin(t * 1.7f + b.Phase) * 0.2f, Mathf.Sin(a) * b.Radius);
                var dir = Mathf.Sign(b.Speed) * new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                // banked into the turn
                b.Root.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sign(b.Speed) * 14f);
                b.Flap(Mathf.Sin(t * BirdWingBeat + i) * 0.55f);
            }
            for (var i = 0; i < _butterflies.Count; i++)
            {
                var f = _butterflies[i];
                var p = f.Phase;
                var pos = f.Anchor + new Vector3(Mathf.Sin(t * f.A + p) * 1.3f, 0.62f + Mathf.Sin(t * 2.3f + p) * 0.16f, Mathf.Cos(t * f.B + p) * 1.1f);
                var vel = new Vector3(Mathf.Cos(t * f.A + p) * f.A * 1.3f, 0f, -Mathf.Sin(t * f.B + p) * f.B * 1.1f);
                f.Root.position = pos;
                if (vel.sqrMagnitude > 1e-4f) f.Root.rotation = Quaternion.LookRotation(vel, Vector3.up);
                // quick beats around raised wings (a resting butterfly holds them up), a short glide now and then
                var beat = Mathf.Sin(t * ButterflyWingBeat + i * 2.1f);
                var glide = Mathf.Sin(t * 0.8f + p) > 0.75f ? 0.25f : 1f;
                f.Flap(-0.35f + beat * 0.75f * glide);
            }
        }
    }
}
