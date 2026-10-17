using System;
using System.Collections.Generic;
using System.IO;
using Gooby.Core.Home;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Feeding on the stage (port of fuetter_regie.gd over the pure <see cref="FeedSequence"/>): the food comes flying in an
    /// arc to Gooby's mouth (from the fridge when one stands in the room, else up from the player's side), he takes the
    /// bites — mouth open, the food shrinks, crumbs fall, the "nom" climbs a step each bite, he bobs — swallows, and shows
    /// how he liked it: hearts for his favourite, a sugar shake for junk, a happy hop otherwise. Only the last event books
    /// the meal (the caller's callback). Reduced motion: the short version without flight and crumbs.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        /// <summary>Food id → minigame food model (Kenney food kit); ids without one get a coloured snack ball.</summary>
        private static readonly Dictionary<string, string> FoodModels = new Dictionary<string, string>
        {
            ["tomate"] = "tomato", ["melone"] = "watermelon", ["salat"] = "salad", ["pilz"] = "mushroom", ["weltraumMoehre"] = "carrot",
            ["softServe"] = "ice-cream", ["sundae"] = "ice-cream", ["cupcakePink"] = "cupcake", ["chili"] = "paprika", ["sandwich"] = "bread",
        };

        private static readonly Dictionary<string, string> FoodBallColours = new Dictionary<string, string>
        {
            ["cottonCandy"] = "#F781B0", ["ananas"] = "#F5C518", ["chocolate"] = "#7A4A2E", ["nutella"] = "#5C3A21", ["candy-bar"] = "#9B5A3C",
            ["lollypop"] = "#FF7FB0", ["grapes"] = "#9C6ADE", ["corn"] = "#F5D04A", ["pumpkin"] = "#F2913D", ["radish"] = "#E2566A", ["eggplant"] = "#7A4E9C",
        };

        private const float FoodSize = 0.3f, MouthAhead = 0.3f, ArcLift = 0.35f, LastBite = 0.18f;

        private FeedSequence _feed;
        private Action _feedBook;
        private Transform _feedFood;
        private Vector3 _feedFrom, _feedTo, _feedBaseScale = Vector3.one;
        private Quaternion _feedBaseRot = Quaternion.identity;
        private float _feedClockMs, _feedFlyMs = 1f, _feedFlyT = -1f, _feedBob, _feedShake, _feedSwallow = -1f;
        private float _feedScaleFrom = 1f, _feedScaleTo = 1f, _feedScaleT = 1f;
        private bool _feedReduced;
        private readonly List<(Transform t, Vector3 v, float life, Vector3 scale, bool gravity)> _feedFx = new List<(Transform, Vector3, float, Vector3, bool)>();

        public bool Feeding => _feed != null && _feed.Active;

        /// <summary>Plays the meal on the stage; <paramref name="book"/> runs at its last event. False while one plays or without Gooby.</summary>
        public bool PlayFeeding(string foodId, bool reduced, Action book)
        {
            if (Feeding || PlayingToy || Gooby == null) return false;
            _feed = new FeedSequence();
            _feed.Start(foodId, 0, reduced);
            _feedClockMs = 0f;
            _feedBook = book;
            _feedReduced = reduced;
            _feedBaseScale = Gooby.localScale;
            _feedBaseRot = Gooby.localRotation;
            // a nap in the crate ends for the meal (he wakes up and stays up a while)
            if (CrateNapping) WakeInCrate(10f);
            Debug.Log("[feed] start " + foodId + (reduced ? " reduced" : ""));
            return true;
        }

        private float AvatarScale => Avatar != null ? Avatar.transform.lossyScale.y : 1f;

        /// <summary>Just in front of his mouth, on the side the camera looks from.</summary>
        private Vector3 MouthPoint()
        {
            var head = Avatar != null ? Avatar.Bone("head") : null;
            var basis = head != null ? head.position : Gooby.position + Vector3.up * 0.9f * AvatarScale;
            var toCam = Camera != null ? Camera.transform.position - basis : Vector3.back;
            toCam.y = 0f;
            toCam = toCam.sqrMagnitude > 0.01f ? toCam.normalized : Vector3.back;
            // the head bone sits low in the head: up a little so the bites land at the mouth, not the belly
            return basis + toCam * MouthAhead * AvatarScale + Vector3.up * 0.14f * AvatarScale;
        }

        /// <summary>The fridge of the room when there is one, else low on the player's side in front of Gooby.</summary>
        private Vector3 FoodStart(Vector3 mouth)
        {
            if (Room != null)
                foreach (var m in Room.Markers())
                    if (m != null && m.Kind == Interactables.Kuehlschrank)
                        return new Vector3(m.Bounds.center.x, m.Bounds.max.y - 0.25f, m.Bounds.center.z) + m.Front * 0.4f;
            var toCam = Camera != null ? Camera.transform.position - mouth : Vector3.back;
            toCam.y = 0f;
            return mouth + toCam.normalized * 1.6f + Vector3.down * 0.7f;
        }

        private void SpawnFood(string foodId)
        {
            var root = new GameObject("FeedFood:" + foodId).transform;
            root.SetParent(_root.transform, false);
            root.position = _feedFrom;
            _feedFood = root;
            var colour = FoodBallColours.TryGetValue(foodId, out var hex) ? hex : "#F2B15C";
            var ball = ArtLibrary.Sphere(root, "SnackBall", root.position, FoodSize * 0.42f, ArtLibrary.Solid(ArtLibrary.Hex(colour), 0.4f), false);
            ball.transform.localScale = new Vector3(1f, 0.78f, 1f) * FoodSize * 0.84f;
            var model = FoodModels.TryGetValue(foodId, out var alias) ? alias : foodId;
            var path = Path.Combine(AssetLoader.Root, "minigame", model + ".glb");
            if (File.Exists(path)) _ = FitFoodAsync(root, ball, path);
        }

        /// <summary>The GLB in place of the snack ball, centred on the anchor and scaled to the food size.</summary>
        private static async System.Threading.Tasks.Task FitFoodAsync(Transform root, GameObject ball, string path)
        {
            var clone = await AssetLoader.InstanceAsync(path, root, "FoodModel");
            if (clone == null) return;
            if (root == null) { UnityEngine.Object.Destroy(clone); return; }
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            var renderers = clone.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            // measured in world space (the root may already be shrinking): back into root units, then centre on the anchor
            var s = Mathf.Max(1e-4f, root.lossyScale.x);
            var edge = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / s;
            if (edge < 1e-4f) return;
            var k = FoodSize / edge;
            var centre = root.InverseTransformPoint(b.center);
            clone.transform.localScale = Vector3.one * k;
            clone.transform.localPosition = -centre * k;
            if (ball != null) ball.SetActive(false);
        }

        private void TickFeeding(float dt)
        {
            TickFeedFx(dt);
            if (_feed == null) return;
            if (Gooby == null) { EndFeeding(false); return; }
            _feedClockMs += dt * 1000f;
            foreach (var e in _feed.Tick((long)_feedClockMs)) FeedEvent(e);
            if (_feedFood != null && _feedFlyT >= 0f)
            {
                _feedFlyT += dt * 1000f;
                var u = Mathf.Clamp01(_feedFlyT / _feedFlyMs);
                var apex = (_feedFrom + _feedTo) * 0.5f + Vector3.up * ArcLift * AvatarScale;
                var a = Vector3.Lerp(_feedFrom, apex, u);
                var b = Vector3.Lerp(apex, _feedTo, u);
                _feedFood.position = Vector3.Lerp(a, b, u);
                _feedFood.rotation = Quaternion.Euler(0f, u * 360f, 0f);
                if (u >= 1f) _feedFlyT = -1f;
            }
            if (_feedFood != null && _feedScaleT < 1f)
            {
                _feedScaleT = Mathf.Min(1f, _feedScaleT + dt / 0.12f);
                _feedFood.localScale = Vector3.one * Mathf.Lerp(_feedScaleFrom, _feedScaleTo, _feedScaleT);
            }
            if (_feedSwallow >= 0f && _feedFood != null)
            {
                _feedSwallow += dt / 0.18f;
                _feedFood.localScale = Vector3.one * Mathf.Lerp(_feedScaleTo, 0.01f, Mathf.Clamp01(_feedSwallow));
                if (_feedSwallow >= 1f) { UnityEngine.Object.Destroy(_feedFood.gameObject); _feedFood = null; _feedSwallow = -1f; }
            }
            if (_feedBob > 0f)
            {
                _feedBob = Mathf.Max(0f, _feedBob - dt / 0.28f);
                var squash = 1f - 0.1f * Mathf.Sin(_feedBob * Mathf.PI);
                Gooby.localScale = new Vector3(_feedBaseScale.x, _feedBaseScale.y * squash, _feedBaseScale.z);
            }
            if (_feedShake > 0f)
            {
                _feedShake = Mathf.Max(0f, _feedShake - dt);
                var wobble = _feedShake > 0f ? Mathf.Sin(_feedShake * 60f) * 3.5f : 0f;
                Gooby.localRotation = _feedBaseRot * Quaternion.Euler(0f, 0f, wobble);
                if (_feedShake <= 0f) Avatar?.SetEmotion("happy");
            }
        }

        private void FeedEvent(FeedSequence.Event e)
        {
            switch (e.Typ)
            {
                case "schwebt":
                    _feedTo = MouthPoint();
                    _feedFrom = _feedReduced ? _feedTo : FoodStart(_feedTo);
                    SpawnFood(_feed.FoodId);
                    _feedFlyMs = Mathf.Max(1f, e.DauerMs);
                    _feedFlyT = _feedReduced ? -1f : 0f;
                    _feedScaleFrom = _feedScaleTo = 1f;
                    _feedScaleT = 1f;
                    Avatar?.SetEmotion("ecstatic");
                    Sfx.Play("game_whoosh", 3f, 0.7f);
                    break;
                case "biss":
                    // the nom climbs a step with every bite (pitch 0.85 + 0.1 per bite, as semitones)
                    var pitch = 0.85f + 0.1f * e.Index;
                    Sfx.Play("nom_nom", 12f * Mathf.Log(pitch, 2f));
                    Avatar?.BabblePulse();
                    _feedScaleFrom = _feedFood != null ? _feedFood.localScale.x : 1f;
                    _feedScaleTo = Mathf.Max(1f - (float)e.Index / Mathf.Max(1, e.Von) * (1f - LastBite), LastBite);
                    _feedScaleT = _feedReduced ? 1f : 0f;
                    if (_feedReduced && _feedFood != null) _feedFood.localScale = Vector3.one * _feedScaleTo;
                    if (!_feedReduced)
                    {
                        Crumbs(_feedFood != null ? _feedFood.position : _feedTo);
                        _feedBob = 1f;
                    }
                    break;
                case "schluck":
                    Sfx.Play("pet_squish");
                    Avatar?.BabblePulse();
                    _feedSwallow = 0f;
                    break;
                case "emotion":
                    FeedFeeling(e.Art);
                    break;
                case "buchen":
                    EndFeeding(true);
                    break;
            }
        }

        private void FeedFeeling(string art)
        {
            switch (art)
            {
                case FeedSequence.Verliebt:
                    Sfx.Play("game_perfect", 1.6f);
                    Avatar?.SetEmotion("happy");
                    if (!_feedReduced) Hearts(_feedTo + Vector3.up * 0.35f * AvatarScale);
                    break;
                case FeedSequence.Zucker:
                    Sfx.Play("game_whoosh", 3.2f);
                    Avatar?.SetEmotion("dizzy");
                    _feedShake = _feedReduced ? 0.01f : 0.32f;
                    break;
                default:
                    Avatar?.SetEmotion("happy");
                    Avatar?.Play("hop");
                    break;
            }
        }

        private void EndFeeding(bool book)
        {
            if (_feedFood != null) UnityEngine.Object.Destroy(_feedFood.gameObject);
            _feedFood = null;
            if (Gooby != null)
            {
                Gooby.localScale = _feedBaseScale;
                if (_feedShake <= 0f) Gooby.localRotation = _feedBaseRot;
            }
            var callback = _feedBook;
            _feed = null;
            _feedBook = null;
            _feedFlyT = -1f;
            _feedSwallow = -1f;
            _feedBob = 0f;
            Debug.Log("[feed] " + (book ? "book" : "dropped"));
            if (book) callback?.Invoke();
        }

        private void Crumbs(Vector3 at)
        {
            var crumb = ArtLibrary.Solid(ArtLibrary.Hex("#C99666"), 0.3f);
            for (var i = 0; i < 7; i++)
            {
                var s = ArtLibrary.Sphere(_root.transform, "Crumb", at, 0.014f * AvatarScale, crumb, false);
                var a = i * 0.9f;
                AddFeedFx(s.transform, new Vector3(Mathf.Cos(a) * 0.45f, 0.6f + (i % 3) * 0.2f, Mathf.Sin(a) * 0.45f) * AvatarScale, 0.7f);
            }
        }

        private void Hearts(Vector3 at)
        {
            var red = ArtLibrary.Glow(new Color(1.5f, 0.35f, 0.45f));
            for (var i = 0; i < 6; i++)
            {
                var a = i * 1.05f;
                var heart = new GameObject("FeedHeart").transform;
                heart.SetParent(_root.transform, false);
                heart.position = at + new Vector3(Mathf.Cos(a) * 0.25f, (i % 3) * 0.08f, Mathf.Sin(a) * 0.1f) * AvatarScale;
                var size = (0.06f + (i % 2) * 0.02f) * AvatarScale;
                foreach (var side in new[] { -1f, 1f })
                    ArtLibrary.Sphere(heart, "HeartLobe", heart.position + new Vector3(side * size * 0.5f, size * 0.25f, 0f), size * 0.62f, red, false);
                var tip = ArtLibrary.Box(heart, "HeartTip", heart.position + new Vector3(0f, -size * 0.25f, 0f), Vector3.one * size * 0.95f, red, false);
                tip.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                AddFeedFx(heart, new Vector3(Mathf.Cos(a) * 0.15f, 0.7f + (i % 3) * 0.12f, 0f) * AvatarScale, 1.6f, gravity: false);
            }
        }

        private void AddFeedFx(Transform t, Vector3 velocity, float life, bool gravity = true) => _feedFx.Add((t, velocity, life, t.localScale, gravity));

        private void TickFeedFx(float dt)
        {
            for (var i = _feedFx.Count - 1; i >= 0; i--)
            {
                var (t, v, life, scale, gravity) = _feedFx[i];
                if (t == null) { _feedFx.RemoveAt(i); continue; }
                life -= dt;
                if (life <= 0f) { UnityEngine.Object.Destroy(t.gameObject); _feedFx.RemoveAt(i); continue; }
                t.position += v * dt;
                if (gravity) v += Vector3.down * 3.2f * dt;
                t.localScale = scale * Mathf.Clamp01(life * 2f);
                _feedFx[i] = (t, v, life, scale, gravity);
            }
        }
    }
}
