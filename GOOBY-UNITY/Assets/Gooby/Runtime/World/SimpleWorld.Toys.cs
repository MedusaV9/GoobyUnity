using System;
using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Gooby plays with a POW! toy on the stage (<see cref="Gooby.Core.Life.Spielzeug"/> books it when the scene ends): a
    /// short scene per toy, built from primitives at chest height in front of him (the floor in front of him is often
    /// behind the sofa from the camera) — the bouncy ball bounces away beside him and back into his paws, the cracker
    /// wiggles and pops into confetti, the squeaky hammer bonks him twice (stars, a dizzy moment), stickers fly out of the
    /// book onto him, he squeezes the rubber duck till it quacks, two giant puzzle pieces click together, the water pistol
    /// squirts over his head and he shakes himself dry. He hops out of his crate first and turns to the player. Reduced
    /// motion: the toy, its sound and his face, without flights.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private static readonly string[] Confetti = { "#FF8B8B", "#FFD166", "#7FCF9B", "#7FB8FF", "#FF98C1", "#98E7E1" };

        /// <summary>Toys are drawn larger than life: the home camera looks at the whole room.</summary>
        private const float ToyLarge = 1.6f;

        private string _toyId, _toyScene;
        private float _toyT, _toyPrevT, _toyLen, _toySquash, _toyShake;
        private bool _toyReduced;
        private Action _toyBook;
        private Transform _toyRoot;
        private readonly Dictionary<string, Transform> _toyParts = new Dictionary<string, Transform>();
        private readonly List<(Transform t, Vector3 from, Vector3 to, float t0, float t1, float lift)> _toyFlights =
            new List<(Transform, Vector3, Vector3, float, float, float)>();
        private Vector3 _toyBase, _toyFront, _toyRight, _toyGoobyScale = Vector3.one;
        private Quaternion _toyGoobyRot = Quaternion.identity;
        private float _toyHeadTop;

        public bool PlayingToy => _toyId != null;

        /// <summary>Plays the toy's scene; <paramref name="book"/> runs when it ends. False while a scene or a meal plays.</summary>
        public bool PlayToy(string toyId, string scene, bool reduced, Action book)
        {
            if (PlayingToy || Feeding || Gooby == null || Camera == null) return false;
            if (InCrate) HopOutOfCrate();
            _toyId = toyId;
            _toyScene = scene;
            _toyReduced = reduced;
            _toyBook = book;
            _toyT = _toyPrevT = 0f;
            _toyLen = reduced ? 1.4f : SceneLength(scene);
            _toySquash = _toyShake = 0f;
            _toyBase = Gooby.position;
            var toCam = Camera.transform.position - _toyBase;
            toCam.y = 0f;
            _toyFront = toCam.sqrMagnitude > 0.01f ? toCam.normalized : Vector3.back;
            // he turns to the player: the game happens between him and the camera
            _goobyYaw = Quaternion.LookRotation(_toyFront, Vector3.up).eulerAngles.y;
            ApplyWalkPose();
            _toyGoobyScale = Gooby.localScale;
            _toyGoobyRot = Gooby.localRotation;
            _toyRight = Camera.transform.right;
            _toyRight.y = 0f;
            _toyRight = _toyRight.sqrMagnitude > 0.01f ? _toyRight.normalized : Vector3.right;
            _toyHeadTop = GoobyTop();
            _toyRoot = new GameObject("Toy:" + toyId).transform;
            _toyRoot.SetParent(_root.transform, false);
            _toyParts.Clear();
            _toyFlights.Clear();
            BuildToy(scene);
            Avatar?.SetEmotion("ecstatic");
            if (reduced) Sfx.Play(MainSound(scene));
            Debug.Log("[toy] play " + toyId + " (" + scene + ") scale " + S.ToString("0.00") + " top " + (_toyHeadTop - _toyBase.y).ToString("0.00") + (reduced ? " reduced" : ""));
            return true;
        }

        private static float SceneLength(string scene)
        {
            switch (scene)
            {
                case "huepfen": return 3.2f;
                case "knall": return 2.9f;
                case "quietschen": return 2.6f;
                case "sticker": return 3.0f;
                case "ente": return 3.2f;
                case "puzzle": return 2.8f;
                default: return 3.0f;
            }
        }

        private static string MainSound(string scene)
        {
            switch (scene)
            {
                case "huepfen": return "toy_boing";
                case "knall": return "toy_knall";
                case "quietschen": return "toy_squeak";
                case "sticker": return "soft_sticker";
                case "ente": return "toy_quack";
                case "puzzle": return "game_perfect";
                default: return "toy_spritz";
            }
        }

        private float S => AvatarScale;

        /// <summary>The toys need floor in front of him: out of the crate he hops, onto the nearest free spot before the rug.</summary>
        private void HopOutOfCrate()
        {
            if (Room?.Def == null) return;
            var front = Room.Center + new Vector3(-0.85f, 0f, -0.85f);
            var cell = (float)GridData.CellSize;
            var gx = (front.x - Room.Origin.x) / cell;
            var gy = (Room.Origin.z + Room.Def.GridH * cell - front.z) / cell;
            var (fx, fy) = RoomNavigation.NearestFree(Room.Def, HomeState.LoadRoomGrid(HouseStore, RoomId), gx, gy);
            PlaceGoobyAt(WorldOf(Room.Def, Room.Origin, fx, fy), "idle-loop");
            Avatar?.Play("hop");
            Effects.LandingRing(Gooby.position, FloorDust);
        }

        /// <summary>A point in Gooby's frame: `side` to the camera's right, `fwd` towards the camera, `up` above the floor.</summary>
        private Vector3 ToyAt(float side, float fwd, float up) => _toyBase + (_toyRight * side + _toyFront * fwd) * S + Vector3.up * up * S;

        private float GoobyTop()
        {
            var renderers = Gooby.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return Gooby.position.y + 1.2f * S;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b.max.y;
        }

        private bool ToyHit(float at) => _toyPrevT < at && _toyT >= at;

        private Transform ToyPart(string key, Vector3 at)
        {
            var t = new GameObject(key).transform;
            t.SetParent(_toyRoot, false);
            t.position = at;
            t.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up);
            _toyParts[key] = t;
            return t;
        }

        /// <summary>A primitive under <paramref name="parent"/> at a local position (metres × Gooby's scale).</summary>
        private GameObject ToyPiece(Transform parent, PrimitiveType type, Vector3 local, Vector3 size, string hex, Quaternion? rot = null, float alpha = 1f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "ToyPiece";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local * S * ToyLarge;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size * S * ToyLarge;
            var r = go.GetComponent<Renderer>();
            var c = ArtLibrary.Hex(hex);
            r.sharedMaterial = alpha < 1f ? ArtLibrary.Transparent(new Color(c.r, c.g, c.b, alpha)) : ArtLibrary.Solid(c, 0.55f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            return go;
        }

        // ── the toys ──

        private void BuildToy(string scene)
        {
            var cyl = PrimitiveType.Cylinder;
            var sph = PrimitiveType.Sphere;
            var box = PrimitiveType.Cube;
            var lie = Quaternion.Euler(0f, 0f, 90f);
            switch (scene)
            {
                case "huepfen":
                {
                    var ball = ToyPart("ball", ToyAt(0.32f, 0.58f, 0.6f));
                    ToyPiece(ball, sph, Vector3.zero, Vector3.one * 0.18f, "#FF8B8B");
                    ToyPiece(ball, cyl, Vector3.zero, new Vector3(0.184f, 0.014f, 0.184f), "#FFD166");
                    ToyPiece(ball, cyl, Vector3.zero, new Vector3(0.184f, 0.014f, 0.184f), "#7FB8FF", Quaternion.Euler(90f, 0f, 0f));
                    ToyPiece(ball, sph, new Vector3(-0.035f, 0.045f, -0.06f), Vector3.one * 0.04f, "#FFF8EE");
                    break;
                }
                case "knall":
                {
                    var cracker = ToyPart("cracker", ToyAt(0f, 0.6f, 0.66f));
                    foreach (var side in new[] { -1f, 1f })
                    {
                        var half = new GameObject(side < 0 ? "left" : "right").transform;
                        half.SetParent(cracker, false);
                        _toyParts[half.name] = half;
                        ToyPiece(half, cyl, new Vector3(side * 0.05f, 0f, 0f), new Vector3(0.09f, 0.05f, 0.09f), "#FF8FB8", lie);
                        ToyPiece(half, cyl, new Vector3(side * 0.06f, 0f, 0f), new Vector3(0.094f, 0.007f, 0.094f), "#FFD166", lie);
                        ToyPiece(half, cyl, new Vector3(side * 0.125f, 0f, 0f), new Vector3(0.05f, 0.025f, 0.05f), "#FF8FB8", lie);
                        ToyPiece(half, sph, new Vector3(side * 0.165f, 0f, 0f), new Vector3(0.05f, 0.075f, 0.075f), "#FFD166");
                    }
                    cracker.localScale = Vector3.one * 0.2f;
                    break;
                }
                case "quietschen":
                {
                    // the hand: right of the head, the handle reaching left to above his head, the head of the hammer at its end
                    var hand = ToyPart("hammer", new Vector3(_toyBase.x, _toyHeadTop + 0.12f * S, _toyBase.z) + _toyRight * 0.36f * ToyLarge * S + _toyFront * 0.08f * S);
                    ToyPiece(hand, cyl, new Vector3(-0.16f, 0f, 0f), new Vector3(0.036f, 0.17f, 0.036f), "#FFD166", lie);
                    ToyPiece(hand, cyl, new Vector3(-0.36f, 0f, 0f), new Vector3(0.12f, 0.085f, 0.12f), "#FF8B8B");
                    ToyPiece(hand, cyl, new Vector3(-0.36f, -0.075f, 0f), new Vector3(0.122f, 0.012f, 0.122f), "#FFF8EE");
                    ToyPiece(hand, cyl, new Vector3(-0.36f, 0.075f, 0f), new Vector3(0.122f, 0.012f, 0.122f), "#FFF8EE");
                    hand.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(0f, 0f, -70f);
                    break;
                }
                case "sticker":
                {
                    var book = ToyPart("book", ToyAt(0f, 0.48f, 0.6f));
                    book.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(-25f, 0f, 0f);
                    foreach (var side in new[] { -1f, 1f })
                    {
                        var page = new GameObject(side < 0 ? "pageL" : "pageR").transform;
                        page.SetParent(book, false);
                        _toyParts[page.name] = page;
                        ToyPiece(page, box, new Vector3(side * 0.07f, 0f, 0f), new Vector3(0.14f, 0.18f, 0.012f), "#7FCF9B");
                        ToyPiece(page, box, new Vector3(side * 0.07f, 0f, -0.008f), new Vector3(0.125f, 0.165f, 0.006f), "#FFFDF8");
                        page.localRotation = Quaternion.Euler(0f, side * -85f, 0f);
                    }
                    ToyPiece(book, box, new Vector3(0.04f, 0.03f, -0.014f), new Vector3(0.04f, 0.04f, 0.004f), "#FFD166", Quaternion.Euler(0f, 0f, 45f));
                    break;
                }
                case "ente":
                {
                    var duck = ToyPart("duck", ToyAt(0f, 0.5f, 0.58f));
                    ToyPiece(duck, sph, Vector3.zero, new Vector3(0.17f, 0.12f, 0.13f), "#FFD166");
                    ToyPiece(duck, sph, new Vector3(-0.085f, 0.03f, 0f), new Vector3(0.05f, 0.06f, 0.05f), "#FFD166");
                    ToyPiece(duck, sph, new Vector3(0.05f, 0.095f, 0f), Vector3.one * 0.095f, "#FFD166");
                    ToyPiece(duck, sph, new Vector3(0.1f, 0.085f, 0f), new Vector3(0.06f, 0.025f, 0.05f), "#F2913D");
                    ToyPiece(duck, box, new Vector3(0.065f, 0.105f, -0.045f), new Vector3(0.06f, 0.022f, 0.01f), "#3B364F");
                    ToyPiece(duck, box, new Vector3(0.065f, 0.105f, 0.045f), new Vector3(0.06f, 0.022f, 0.01f), "#3B364F");
                    ToyPiece(duck, sph, new Vector3(-0.01f, 0.02f, -0.06f), new Vector3(0.09f, 0.05f, 0.03f), "#F5C34A");
                    break;
                }
                case "puzzle":
                {
                    var blue = ToyPart("pieceL", ToyAt(-0.65f, 0.48f, 0.62f));
                    ToyPiece(blue, box, Vector3.zero, new Vector3(0.18f, 0.18f, 0.035f), "#7FB8FF");
                    ToyPiece(blue, cyl, new Vector3(0.1f, 0f, 0f), new Vector3(0.06f, 0.0175f, 0.06f), "#7FB8FF", Quaternion.Euler(90f, 0f, 0f));
                    var pink = ToyPart("pieceR", ToyAt(0.65f, 0.48f, 0.62f));
                    ToyPiece(pink, box, Vector3.zero, new Vector3(0.18f, 0.18f, 0.035f), "#FF8B8B");
                    ToyPiece(pink, cyl, new Vector3(0f, 0.1f, 0f), new Vector3(0.06f, 0.0175f, 0.06f), "#FF8B8B", Quaternion.Euler(90f, 0f, 0f));
                    break;
                }
                default:
                {
                    var gun = ToyPart("pistol", ToyAt(0.42f, 0.45f, 0.62f));
                    ToyPiece(gun, box, Vector3.zero, new Vector3(0.15f, 0.06f, 0.055f), "#98E7E1");
                    ToyPiece(gun, box, new Vector3(0.045f, -0.06f, 0f), new Vector3(0.04f, 0.08f, 0.04f), "#7FB8FF");
                    ToyPiece(gun, sph, new Vector3(0.01f, 0.055f, 0f), Vector3.one * 0.07f, "#7FB8FF");
                    ToyPiece(gun, cyl, new Vector3(-0.095f, 0f, 0f), new Vector3(0.024f, 0.02f, 0.024f), "#FFD166", lie);
                    // aimed over his head: the nozzle (−x) tips up towards him
                    gun.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(0f, 0f, -35f);
                    break;
                }
            }
        }

        // ── the tick ──

        private void TickToys(float dt)
        {
            if (_toyId == null) return;
            if (Gooby == null || _toyRoot == null) { EndToy(false); return; }
            // he was steered away mid-game: the play counts, the scene clears
            var away = Gooby.position - _toyBase;
            away.y = 0f;
            if (away.sqrMagnitude > 0.8f * 0.8f) { EndToy(true); return; }
            _toyPrevT = _toyT;
            _toyT += dt;
            if (!_toyReduced) Animate(_toyScene);
            TickToyFlights();
            if (_toySquash > 0f)
            {
                _toySquash = Mathf.Max(0f, _toySquash - dt / 0.16f);
                var k = 1f - 0.12f * Mathf.Sin(_toySquash * Mathf.PI);
                Gooby.localScale = new Vector3(_toyGoobyScale.x / Mathf.Sqrt(k), _toyGoobyScale.y * k, _toyGoobyScale.z / Mathf.Sqrt(k));
            }
            if (_toyShake > 0f)
            {
                _toyShake = Mathf.Max(0f, _toyShake - dt);
                Gooby.localRotation = _toyGoobyRot * Quaternion.Euler(0f, 0f, _toyShake > 0f ? Mathf.Sin(_toyShake * 70f) * 6f : 0f);
            }
            if (_toyT >= _toyLen) EndToy(true);
        }

        private void Animate(string scene)
        {
            var t = _toyT;
            switch (scene)
            {
                case "huepfen":
                {
                    if (ToyHit(0.2f)) Avatar?.Play("hop");
                    // throw, three bounces away and back, up into his paws
                    var ball = _toyParts["ball"];
                    var hops = new[]
                    {
                        (0.3f, 0.75f, ToyAt(0.32f, 0.58f, 0.6f), ToyAt(0.85f, 0.15f, 0.15f), 0.65f),
                        (0.75f, 1.15f, ToyAt(0.85f, 0.15f, 0.15f), ToyAt(1.05f, -0.3f, 0.15f), 0.5f),
                        (1.15f, 1.5f, ToyAt(1.05f, -0.3f, 0.15f), ToyAt(0.65f, -0.05f, 0.15f), 0.38f),
                        (1.5f, 2.05f, ToyAt(0.65f, -0.05f, 0.15f), ToyAt(0.05f, 0.55f, 0.66f), 0.55f),
                    };
                    for (var i = 0; i < hops.Length; i++)
                    {
                        var (t0, t1, from, to, lift) = hops[i];
                        if (t >= t0 && t < t1)
                        {
                            var u = (t - t0) / (t1 - t0);
                            ball.position = Vector3.Lerp(from, to, u) + Vector3.up * lift * S * 4f * u * (1f - u);
                            ball.Rotate(_toyFront, -540f * Time.unscaledDeltaTime, Space.World);
                        }
                        if (i < 3 && ToyHit(t1))
                        {
                            Sfx.Play("toy_boing", i * 1.5f, 0.85f);
                            ball.localScale = new Vector3(1.2f, 0.72f, 1.2f);
                            Effects.Sparkle(ball.position, 0.12f * S, 4);
                        }
                    }
                    ball.localScale = Vector3.Lerp(ball.localScale, Vector3.one, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                    if (ToyHit(0.3f)) Sfx.Play("game_whoosh", 4f, 0.5f);
                    if (ToyHit(2.05f)) { Avatar?.Play("celebrate"); Avatar?.SetEmotion("happy"); _toySquash = 1f; }
                    if (t > 2.7f) ball.localScale = Vector3.one * Mathf.Clamp01((_toyLen - t) / 0.5f);
                    break;
                }
                case "knall":
                {
                    var cracker = _toyParts["cracker"];
                    if (t < 0.95f)
                    {
                        cracker.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, Mathf.SmoothStep(0f, 1f, t / 0.35f));
                        var wiggle = Mathf.Sin(t * Mathf.Lerp(10f, 34f, t)) * Mathf.Lerp(4f, 14f, t);
                        cracker.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(0f, 0f, wiggle);
                    }
                    if (ToyHit(0.45f)) Sfx.Play("pet_squish", 3f, 0.5f);
                    if (ToyHit(0.95f))
                    {
                        Sfx.Play("toy_knall");
                        Sfx.Play("game_pop", -2f, 0.6f);
                        foreach (var side in new[] { -1f, 1f })
                        {
                            var half = _toyParts[side < 0 ? "left" : "right"];
                            half.SetParent(_root.transform, true);
                            AddFeedFx(half, (_toyRight * side * 1.5f + Vector3.up * 1.4f + _toyFront * 0.3f) * S, 1.1f);
                        }
                        var rng = new System.Random(_toyId.GetHashCode() ^ (int)(Time.unscaledTime * 10f));
                        for (var i = 0; i < 40; i++)
                        {
                            var piece = ArtLibrary.Box(_root.transform, "Confetti", cracker.position, new Vector3(0.03f, 0.03f, 0.005f) * S,
                                ArtLibrary.Solid(ArtLibrary.Hex(Confetti[i % Confetti.Length]), 0.3f), false);
                            piece.transform.rotation = UnityEngine.Random.rotation;
                            var a = (float)rng.NextDouble() * Mathf.PI * 2f;
                            var v = (_toyRight * Mathf.Cos(a) + _toyFront * Mathf.Sin(a) * 0.6f) * (0.6f + (float)rng.NextDouble() * 1.2f) + Vector3.up * (1.6f + (float)rng.NextDouble() * 1.4f);
                            AddFeedFx(piece.transform, v * S, 1.4f + (float)rng.NextDouble() * 0.8f);
                        }
                        Avatar?.SetEmotion("scared");
                        Avatar?.Play("hop");
                        _toySquash = 1f;
                    }
                    if (ToyHit(1.35f)) { Avatar?.SetEmotion("ecstatic"); Avatar?.Play("celebrate"); }
                    break;
                }
                case "quietschen":
                {
                    // raised at −70°, down on his head at 0°: two bonks, then the hammer drops away
                    var hand = _toyParts["hammer"];
                    float Swing(float t0) => t < t0 ? Mathf.Lerp(-70f, 0f, Mathf.Pow(Mathf.Clamp01((t - (t0 - 0.28f)) / 0.28f), 2f))
                        : Mathf.Lerp(0f, -70f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - t0) / 0.3f)));
                    var angle = t < 0.95f ? Swing(0.6f) : Swing(1.4f);
                    if (t > 1.9f) angle = Mathf.Lerp(-70f, -120f, Mathf.Clamp01((t - 1.9f) / 0.5f));
                    hand.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(0f, 0f, angle);
                    if (t > 2.1f) hand.localScale = Vector3.one * Mathf.Clamp01((_toyLen - t) / 0.4f);
                    foreach (var (at, pitch) in new[] { (0.6f, 0f), (1.4f, 2.5f) })
                    {
                        if (!ToyHit(at)) continue;
                        Sfx.Play("toy_squeak", pitch, 0.8f);
                        _toySquash = 1f;
                        Avatar?.SetEmotion(at > 1f ? "dizzy" : "happy");
                        var top = new Vector3(_toyBase.x, _toyHeadTop, _toyBase.z);
                        for (var i = 0; i < 4; i++)
                        {
                            var a = i * Mathf.PI * 0.5f + at;
                            var star = ArtLibrary.Sphere(_root.transform, "BonkStar", top + Vector3.up * 0.04f * S, 0.045f * S, ArtLibrary.Glow(new Color(1.6f, 1.25f, 0.35f)), false);
                            AddFeedFx(star.transform, (_toyRight * Mathf.Cos(a) * 0.5f + _toyFront * Mathf.Sin(a) * 0.3f + Vector3.up * 0.45f) * S, 0.8f, gravity: false);
                        }
                    }
                    if (ToyHit(2.0f)) { Avatar?.SetEmotion("ecstatic"); Avatar?.Play("hop"); }
                    break;
                }
                case "sticker":
                {
                    var open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.45f));
                    foreach (var side in new[] { -1f, 1f })
                        _toyParts[side < 0 ? "pageL" : "pageR"].localRotation = Quaternion.Euler(0f, side * Mathf.Lerp(-85f, -8f, open), 0f);
                    var book = _toyParts["book"];
                    for (var i = 0; i < 5; i++)
                    {
                        var at = 0.6f + i * 0.3f;
                        if (!ToyHit(at)) continue;
                        // a star leaves the page and sticks on him: chest, arms, belly in turn
                        var star = new GameObject("Sticker").transform;
                        star.SetParent(_toyRoot, false);
                        star.position = book.position;
                        star.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up);
                        ToyPiece(star, PrimitiveType.Cube, Vector3.zero, new Vector3(0.045f, 0.045f, 0.004f), "#FFD166", Quaternion.Euler(0f, 0f, 45f));
                        ToyPiece(star, PrimitiveType.Cube, Vector3.zero, new Vector3(0.045f, 0.045f, 0.004f), "#FFC94A");
                        var spots = new[] { (-0.18f, 0.6f), (0.18f, 0.68f), (-0.04f, 0.45f), (0.1f, 0.86f), (-0.22f, 0.84f) };
                        var to = ToyAt(spots[i].Item1, 0.4f, spots[i].Item2);
                        _toyFlights.Add((star, book.position, to, at, at + 0.35f, 0.18f));
                        Sfx.Play("soft_sticker", 1f + i, 0.8f);
                    }
                    if (ToyHit(0.9f)) Avatar?.Play("wave");
                    if (ToyHit(2.2f)) { Avatar?.Play("celebrate"); Effects.Sparkle(ToyAt(0f, 0.3f, 0.7f), 0.3f * S, 10); }
                    if (t > 2.55f) _toyRoot.localScale = Vector3.one * Mathf.Clamp01((_toyLen - t) / 0.45f);
                    break;
                }
                case "ente":
                {
                    var duck = _toyParts["duck"];
                    var held = ToyAt(Mathf.Sin(t * 1.3f) * 0.06f, 0.5f, 0.58f);
                    duck.position = held + Vector3.up * Mathf.Sin(t * 5f) * 0.025f * S;
                    duck.rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(0f, Mathf.Sin(t * 0.9f) * 25f, Mathf.Sin(t * 5f) * 6f);
                    foreach (var at in new[] { 0.7f, 1.8f })
                    {
                        if (!ToyHit(at)) continue;
                        Sfx.Play("toy_quack", at > 1f ? 2f : 0f);
                        duck.localScale = new Vector3(1.1f, 0.82f, 1.1f);
                        Avatar?.Play("hop");
                        for (var i = 0; i < 7; i++)
                        {
                            var a = i * 0.9f;
                            var drop = ArtLibrary.Sphere(_root.transform, "Drop", duck.position, 0.018f * S, ArtLibrary.Solid(ArtLibrary.Hex("#8FD3FF"), 0.8f), false);
                            AddFeedFx(drop.transform, (_toyRight * Mathf.Cos(a) * 0.5f + _toyFront * Mathf.Sin(a) * 0.4f + Vector3.up * 1.2f) * S, 0.6f);
                        }
                    }
                    duck.localScale = Vector3.Lerp(duck.localScale, Vector3.one, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
                    if (ToyHit(2.5f)) Avatar?.Play("celebrate");
                    if (t > 2.75f) _toyRoot.localScale = Vector3.one * Mathf.Clamp01((_toyLen - t) / 0.45f);
                    break;
                }
                case "puzzle":
                {
                    var join = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.2f) / 1.05f));
                    var wobble = (1f - join) * Mathf.Sin(t * 9f) * 10f;
                    _toyParts["pieceL"].position = Vector3.Lerp(ToyAt(-0.65f, 0.48f, 0.62f), ToyAt(-0.15f, 0.48f, 0.62f), join);
                    _toyParts["pieceR"].position = Vector3.Lerp(ToyAt(0.65f, 0.48f, 0.62f), ToyAt(0.145f, 0.48f, 0.62f), join);
                    _toyParts["pieceL"].rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(0f, 0f, wobble);
                    _toyParts["pieceR"].rotation = Quaternion.LookRotation(-_toyFront, Vector3.up) * Quaternion.Euler(0f, 0f, -wobble);
                    if (ToyHit(1.25f))
                    {
                        Sfx.Play("game_hit", 6f, 0.5f);
                        Sfx.Play("game_perfect", 1f, 0.8f);
                        Effects.Sparkle(ToyAt(0f, 0.48f, 0.62f), 0.35f * S, 16);
                        _toyRoot.localScale = Vector3.one * 1.12f;
                        Avatar?.Play("celebrate");
                    }
                    if (t > 1.25f && t < 2.3f) _toyRoot.localScale = Vector3.Lerp(_toyRoot.localScale, Vector3.one, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
                    if (t > 2.3f) _toyRoot.localScale = Vector3.one * Mathf.Clamp01((_toyLen - t) / 0.5f);
                    break;
                }
                default:
                {
                    // the stream: one drop every 40 ms in an arc over his head, two squirts; then he shakes himself dry
                    var gun = _toyParts["pistol"];
                    var nozzle = gun.TransformPoint(new Vector3(-0.11f, 0f, 0f) * S * ToyLarge);
                    var squirting = (t > 0.5f && t < 0.95f) || (t > 1.1f && t < 1.5f);
                    if (squirting && Mathf.FloorToInt(t / 0.04f) != Mathf.FloorToInt(_toyPrevT / 0.04f))
                    {
                        var target = new Vector3(_toyBase.x, _toyHeadTop + 0.02f, _toyBase.z) + _toyRight * (UnityEngine.Random.value - 0.5f) * 0.2f * S;
                        const float flight = 0.42f;
                        var v = (target - nozzle) / flight + Vector3.up * 0.5f * 3.2f * flight;
                        var drop = ArtLibrary.Sphere(_root.transform, "Squirt", nozzle, 0.02f * S, ArtLibrary.Solid(ArtLibrary.Hex("#8FD3FF"), 0.85f), false);
                        AddFeedFx(drop.transform, v, flight + 0.05f);
                    }
                    if (ToyHit(0.5f)) Sfx.Play("toy_spritz");
                    if (ToyHit(1.1f)) Sfx.Play("toy_spritz", 1.5f);
                    if (ToyHit(0.85f)) { Avatar?.SetEmotion("scared"); _toySquash = 1f; }
                    if (ToyHit(1.7f))
                    {
                        _toyShake = 0.5f;
                        Sfx.Play("care_wasser", 2f, 0.35f);
                        for (var i = 0; i < 12; i++)
                        {
                            var a = i * Mathf.PI / 6f;
                            var drop = ArtLibrary.Sphere(_root.transform, "Fling", _toyBase + Vector3.up * 0.6f * S, 0.018f * S, ArtLibrary.Solid(ArtLibrary.Hex("#8FD3FF"), 0.85f), false);
                            AddFeedFx(drop.transform, (_toyRight * Mathf.Cos(a) + _toyFront * Mathf.Sin(a) * 0.5f + Vector3.up * 0.8f) * 1.2f * S, 0.7f);
                        }
                    }
                    if (ToyHit(2.2f)) { Avatar?.SetEmotion("ecstatic"); Avatar?.Play("hop"); }
                    if (t > 2.4f) gun.localScale = Vector3.one * Mathf.Clamp01((_toyLen - t) / 0.5f);
                    break;
                }
            }
        }

        private void TickToyFlights()
        {
            for (var i = _toyFlights.Count - 1; i >= 0; i--)
            {
                var (t, from, to, t0, t1, lift) = _toyFlights[i];
                if (t == null) { _toyFlights.RemoveAt(i); continue; }
                var u = Mathf.Clamp01((_toyT - t0) / (t1 - t0));
                t.position = Vector3.Lerp(from, to, u) + Vector3.up * lift * S * 4f * u * (1f - u);
                if (u >= 1f) _toyFlights.RemoveAt(i);
            }
        }

        private void EndToy(bool book)
        {
            if (_toyRoot != null) UnityEngine.Object.Destroy(_toyRoot.gameObject);
            _toyRoot = null;
            _toyParts.Clear();
            _toyFlights.Clear();
            if (Gooby != null)
            {
                Gooby.localScale = _toyGoobyScale;
                Gooby.localRotation = _toyGoobyRot;
            }
            Avatar?.SetEmotion("happy");
            var callback = _toyBook;
            var id = _toyId;
            _toyId = null;
            _toyScene = null;
            _toyBook = null;
            Debug.Log("[toy] " + (book ? "done " : "dropped ") + id);
            if (book) callback?.Invoke();
        }
    }
}
