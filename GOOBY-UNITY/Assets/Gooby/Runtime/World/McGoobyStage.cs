using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// McGooby's kitchen as a 3D diorama behind the shift UI (port of schicht_deko.gd / schicht_scene.gd's set): a red
    /// and sunny-yellow wall with the golden spoon arches and chalk menu boards, the long counter with the four stations —
    /// the grill (patties go from raw pink through golden brown to "charcoal style"), the assembly board (the burger
    /// grows layer by layer), the fryer (the basket dips into bubbling oil, fries go pale → golden → dark) and the shake
    /// bar (the cup fills, the fluffy crown rises, too long and it foams over). Gooby in a chef's toque works behind the
    /// counter and walks to the active station; a hungry customer waits in front and cheers when the order comes.
    /// </summary>
    public sealed class McGoobyStage
    {
        public const double ViewH = 720;

        public MgStage3D Stage { get; }
        private readonly Transform _root;
        private GoobyAvatar _chef, _customer;
        private readonly List<Transform> _patties = new List<Transform>();
        private readonly List<Transform> _stack = new List<Transform>();
        private Transform _basket, _fries, _shakeFill, _crown, _cupGlow;
        private readonly List<Transform> _foam = new List<Transform>();
        private float _chefX, _chefTargetX, _hop, _customerHop, _t;
        private bool _destroyed;

        private const float CounterTop = 1.45f;
        // the chef stands on a step behind the counter so his face clears the steel top
        private const float ChefY = 0.55f;
        private static readonly Vector3 CustomerSpot = new Vector3(-4.9f, 0f, -0.9f);
        public static readonly Dictionary<string, float> StationX = new Dictionary<string, float>
        {
            ["grill"] = -3.7f, ["belegen"] = -0.9f, ["fritteuse"] = 1.9f, ["shake"] = 4.4f,
        };

        private static Vector3 At(float x, float y, float z) => MgStage3D.At(x, y, z);

        private static Material M(string hex, float smooth = 0.25f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);

        public McGoobyStage(UnityEngine.UIElements.VisualElement field, Dictionary<string, string> outfit)
        {
            // the kitchen fills the whole screen: render at the screen's aspect so nothing is stretched sideways
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            Stage = new MgStage3D(ViewH * aspect, ViewH, ArtLibrary.Hex("#FBE3C8"));
            Stage.Bind(field);
            // a little above the counter and tilted down: the counter top sits just above the station panel and the
            // patties, the burger and the basket read from above instead of edge-on
            Stage.Camera.transform.position = At(0f, 4.4f, -13f);
            Stage.Camera.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            _root = Stage.Root.transform;
            BuildRoom();
            BuildStations();
            _chefX = _chefTargetX = StationX["grill"];
            var look = outfit != null ? new Dictionary<string, string>(outfit) : new Dictionary<string, string>();
            look["hut"] = "chefToque";
            _ = SpawnAsync(look);
        }

        private void BuildRoom()
        {
            ArtLibrary.Box(_root, "Floor", At(0f, -0.05f, 0f), new Vector3(26f, 0.1f, 14f), M("#E9D8C4", 0.3f), false);
            ArtLibrary.Box(_root, "Wall", At(0f, 4f, 4.2f), new Vector3(26f, 8f, 0.1f), M("#FFE9B0", 0.05f), false);
            ArtLibrary.Box(_root, "WallLow", At(0f, 1.4f, 4.12f), new Vector3(26f, 2.8f, 0.04f), M("#E8574C", 0.1f), false);
            for (var i = -13; i <= 13; i++)
                ArtLibrary.Box(_root, "Tile", At(i * 0.95f, 2.86f, 4.1f), new Vector3(0.45f, 0.12f, 0.03f), M(i % 2 == 0 ? "#FFF6E0" : "#F2C230", 0.3f), false);
            // the golden spoon arches with little ears, centre top
            const float archY = 3.3f;
            var gold = ArtLibrary.Glow(ArtLibrary.Hex("#FFC93C") * 1.05f);
            foreach (var cx in new[] { -0.75f, 0.75f })
            {
                for (var i = 0; i <= 16; i++)
                {
                    var a = Mathf.PI * i / 16f;
                    ArtLibrary.Sphere(_root, "Arch", At(cx + Mathf.Cos(a) * 0.72f, archY + Mathf.Sin(a) * 1.25f, 4.0f), 0.13f, gold, false);
                }
                foreach (var e in new[] { -0.18f, 0.18f })
                {
                    var ear = ArtLibrary.Sphere(_root, "Ear", At(cx + e, archY + 1.35f, 4.0f), 0.14f, gold, false);
                    ear.transform.localScale = new Vector3(0.18f, 0.42f, 0.18f);
                }
            }
            // chalk menu boards with a burger, fries and a shake drawn on
            const float boardY = 3.95f;
            foreach (var (x, a, b) in new[] { (-4.8f, "#FFD166", "#E8574C"), (4.8f, "#9FE3D4", "#FF9EBB") })
            {
                ArtLibrary.Box(_root, "Board", At(x, boardY, 4.0f), new Vector3(2.4f, 1.5f, 0.08f), M("#33463D", 0.1f), false);
                ArtLibrary.Box(_root, "BoardFrame", At(x, boardY, 4.04f), new Vector3(2.6f, 1.7f, 0.04f), M("#A87A52", 0.2f), false);
                for (var i = 0; i < 3; i++)
                {
                    ArtLibrary.Sphere(_root, "Dot", At(x - 0.8f, boardY + 0.42f - i * 0.42f, 3.95f), 0.12f, M(i == 1 ? b : a, 0.3f), false);
                    ArtLibrary.Box(_root, "Line", At(x + 0.2f, boardY + 0.42f - i * 0.42f, 3.95f), new Vector3(1.3f - i * 0.2f, 0.06f, 0.01f), M("#F4F1E6", 0.05f), false);
                }
            }
            foreach (var x in new[] { -2.6f, 2.6f })
            {
                ArtLibrary.Cylinder(_root, "Cord", At(x, 6.0f, 1.6f), 0.015f, 1.4f, M("#3B3630"), false);
                ArtLibrary.Sphere(_root, "Lamp", At(x, 5.0f, 1.6f), 0.32f, ArtLibrary.Glow(ArtLibrary.Hex("#FFD27A") * 1.2f), false);
            }
            // the counter: steel top on a red front with a yellow band
            ArtLibrary.Box(_root, "CounterBody", At(0f, CounterTop * 0.5f, 0.2f), new Vector3(13f, CounterTop, 1.2f), M("#E8574C", 0.2f));
            ArtLibrary.Box(_root, "CounterBand", At(0f, CounterTop - 0.35f, -0.41f), new Vector3(13f, 0.16f, 0.02f), M("#FFC93C", 0.3f), false);
            ArtLibrary.Box(_root, "CounterTop", At(0f, CounterTop + 0.04f, 0.2f), new Vector3(13.2f, 0.08f, 1.3f), M("#D9DEE4", 0.7f));
            Stage.Prop("stoolBar", At(-6.3f, 0f, -1.6f), 3.6f, 0f);
            Stage.Prop("stoolBar", At(5.6f, 0f, -1.4f), 3.6f, 0f);
        }

        private void BuildStations()
        {
            var top = CounterTop + 0.08f;
            // grill: a dark plate with ribs, two patty slots
            var gx = StationX["grill"];
            ArtLibrary.Box(_root, "Grill", At(gx, top + 0.06f, 0.15f), new Vector3(1.9f, 0.12f, 0.95f), M("#2F2B2A", 0.4f));
            for (var i = 0; i < 6; i++)
                ArtLibrary.Box(_root, "Rib", At(gx - 0.75f + i * 0.3f, top + 0.13f, 0.15f), new Vector3(0.04f, 0.02f, 0.85f), M("#57514E", 0.5f), false);
            for (var i = 0; i < 2; i++)
            {
                var p = ArtLibrary.Cylinder(_root, "Patty", At(gx - 0.42f + i * 0.84f, top + 0.18f, 0.15f), 0.3f, 0.12f, M("#E99A8A", 0.2f));
                p.SetActive(false);
                _patties.Add(p.transform);
            }
            // assembly board
            var bx = StationX["belegen"];
            ArtLibrary.Box(_root, "Board", At(bx, top + 0.03f, 0.15f), new Vector3(1.5f, 0.06f, 0.9f), M("#C99A6B", 0.2f));
            Stage.Prop("plate-dinner", At(bx, top + 0.07f, 0.15f), 1.6f);
            // fryer: steel tub, golden oil, a wire basket above it
            var fx = StationX["fritteuse"];
            ArtLibrary.Box(_root, "Fryer", At(fx, top + 0.3f, 0.2f), new Vector3(1.3f, 0.6f, 0.9f), M("#B9C1CA", 0.75f));
            ArtLibrary.Box(_root, "Oil", At(fx, top + 0.56f, 0.2f), new Vector3(1.1f, 0.04f, 0.7f), M("#F2B833", 0.9f), false);
            var basket = new GameObject("Basket").transform;
            basket.SetParent(_root, false);
            basket.position = At(fx, top + 0.95f, 0.2f);
            var wire = M("#4A4F55", 0.6f);
            foreach (var (dx, dz, sx, sz) in new[] { (0f, -0.3f, 0.86f, 0.03f), (0f, 0.3f, 0.86f, 0.03f), (-0.42f, 0f, 0.03f, 0.6f), (0.42f, 0f, 0.03f, 0.6f) })
                ArtLibrary.Box(basket, "Wire", basket.position + new Vector3(dx, 0f, dz), new Vector3(sx, 0.32f, sz), wire, false).transform.SetParent(basket, true);
            ArtLibrary.Box(basket, "Handle", basket.position + new Vector3(0f, 0.12f, -0.55f), new Vector3(0.08f, 0.06f, 0.5f), M("#2F2B2A"), false).transform.SetParent(basket, true);
            var fries = new GameObject("Fries").transform;
            fries.SetParent(basket, false);
            fries.position = basket.position;
            for (var i = 0; i < 14; i++)
            {
                var stick = ArtLibrary.Box(fries, "Fry", basket.position + new Vector3(-0.32f + (i % 7) * 0.1f, 0.04f + (i / 7) * 0.06f, -0.12f + (i % 3) * 0.12f), new Vector3(0.06f, 0.06f, 0.34f), M("#F4E1A1", 0.2f), false);
                stick.transform.SetParent(fries, true);
                stick.transform.rotation = Quaternion.Euler(0f, (i * 37) % 50 - 25f, 0f);
            }
            _basket = basket;
            _fries = fries;
            // shake bar: blender base, a clear cup, the pink fill and its crown
            var sx2 = StationX["shake"];
            ArtLibrary.Box(_root, "BlenderBase", At(sx2, top + 0.15f, 0.2f), new Vector3(0.6f, 0.3f, 0.6f), M("#4FA3D9", 0.4f));
            ArtLibrary.Cylinder(_root, "Cup", At(sx2, top + 0.85f, 0.2f), 0.26f, 1.1f, ArtLibrary.Transparent(new Color(0.95f, 0.97f, 1f, 0.35f)), false);
            var fill = ArtLibrary.Cylinder(_root, "Fill", At(sx2, top + 0.32f, 0.2f), 0.24f, 0.04f, M("#FF8FB1", 0.3f), false);
            _shakeFill = fill.transform;
            var crown = new GameObject("Crown").transform;
            crown.SetParent(_root, false);
            crown.position = At(sx2, top + 1.35f, 0.2f);
            for (var i = 0; i < 5; i++)
                ArtLibrary.Sphere(crown, "Fluff", crown.position + new Vector3(Mathf.Cos(i * 1.256f) * 0.13f, (i % 2) * 0.05f, Mathf.Sin(i * 1.256f) * 0.13f), 0.13f, M("#FFF6F9", 0.2f), false).transform.SetParent(crown, true);
            ArtLibrary.Sphere(crown, "Cherry", crown.position + Vector3.up * 0.16f, 0.07f, M("#E2364C", 0.6f), false).transform.SetParent(crown, true);
            crown.gameObject.SetActive(false);
            _crown = crown;
            var glow = ArtLibrary.Sphere(_root, "CupGlow", At(sx2, top + 1.5f, 0.2f), 0.06f, ArtLibrary.Glow(ArtLibrary.Hex("#FFD166") * 1.4f), false);
            glow.SetActive(false);
            _cupGlow = glow.transform;
        }

        private async Task SpawnAsync(Dictionary<string, string> chefLook)
        {
            var chef = await Stage.SpawnGoobyAsync(At(_chefX, ChefY, 1.55f), 2.2f, chefLook);
            if (chef == null || _destroyed) return;
            chef.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            chef.Play("idle-loop");
            chef.SetEmotion("happy");
            _chef = chef;
            var spot = At(CustomerSpot.x, CustomerSpot.y, CustomerSpot.z);
            var customer = await GoobyAvatar.CreateAsync(_root, spot);
            if (customer == null || _destroyed) { if (customer != null) Object.Destroy(customer.gameObject); return; }
            customer.transform.position = spot;
            customer.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            customer.transform.localScale = Vector3.one * 1.9f;
            _customer = customer;
            Customer(0);
        }

        private static readonly Dictionary<string, string>[] Customers =
        {
            new Dictionary<string, string> { ["fell"] = "caramel", ["hut"] = "cap" },
            new Dictionary<string, string> { ["fell"] = "fell_himmel", ["hut"] = "beanie", ["hals"] = "scarfRed" },
            new Dictionary<string, string> { ["fell"] = "rose", ["hut"] = "strawHat" },
            new Dictionary<string, string> { ["fell"] = "fell_minze", ["brille"] = "roundGlasses" },
            new Dictionary<string, string> { ["fell"] = "ash", ["hut"] = "beret", ["hals"] = "bowtie" },
        };

        /// <summary>The next hungry customer steps up (a new look each order).</summary>
        public void Customer(int nr)
        {
            if (_customer == null) return;
            _customer.Wear(Customers[nr % Customers.Length]);
            _customer.SetEmotion("happy");
            _customer.Play("wave");
            _customerHop = 0.4f;
        }

        public void CustomerCheers()
        {
            if (_customer == null) return;
            _customer.SetEmotion("ecstatic");
            _customer.Play("celebrate");
        }

        // the fryer basket and the blender cup stand taller than his face — he works from their left side
        private static readonly Dictionary<string, float> ChefBeside = new Dictionary<string, float>
        {
            ["fritteuse"] = -1.05f, ["shake"] = -1.0f,
        };

        /// <summary>Gooby walks over to the station that is up now.</summary>
        public void Station(string station)
        {
            if (!StationX.TryGetValue(station ?? "", out var x)) return;
            _chefTargetX = x + (ChefBeside.TryGetValue(station, out var beside) ? beside : 0f);
        }

        public void Cheer()
        {
            if (_chef == null) return;
            _chef.SetEmotion("ecstatic");
            _chef.Play("hop");
            _hop = 0.6f;
        }

        public void Wince()
        {
            if (_chef == null) return;
            _chef.SetEmotion("dizzy");
            _chef.Play("idle_lookaround");
        }

        public void Happy() => _chef?.SetEmotion("happy");

        public void Celebrate()
        {
            if (_chef == null) return;
            _chef.SetEmotion("ecstatic");
            _chef.Play("celebrate");
        }

        // ── grill ──

        public void ShowPatties(int n)
        {
            for (var i = 0; i < _patties.Count; i++) _patties[i].gameObject.SetActive(i < n);
        }

        /// <summary>A patty's look at its doneness: raw pink → golden brown (a soft glow in the window) → charcoal.</summary>
        public void Patty(int i, double fortschritt, string zustand, bool reduced)
        {
            if (i < 0 || i >= _patties.Count) return;
            var p = _patties[i];
            p.gameObject.SetActive(true);
            var r = p.GetComponent<Renderer>();
            Color c;
            if (zustand == Core.Dlc.McGoobySchicht.Kohle) c = ArtLibrary.Hex("#2E2622");
            else if (zustand == Core.Dlc.McGoobySchicht.Goldbraun) c = Color.Lerp(ArtLibrary.Hex("#B5651D"), ArtLibrary.Hex("#E3A247"), 0.5f + 0.5f * Mathf.Sin(_t * 9f));
            else c = Color.Lerp(ArtLibrary.Hex("#E99A8A"), ArtLibrary.Hex("#B5651D"), (float)fortschritt);
            if (r != null) r.material.color = c;
            if (!reduced && Random.value < 0.08f) Effects.Puff(p.position + Vector3.up * 0.08f, new Color(1f, 1f, 1f, 0.35f), 1, 0.12f, 0.4f, 0.8f, Vector3.up, 0.3f);
        }

        public void FlipPatty(int i)
        {
            if (i < 0 || i >= _patties.Count) return;
            Effects.Sparkle(_patties[i].position + Vector3.up * 0.2f, 0.2f, 10);
        }

        // ── assembly ──

        private static readonly Dictionary<string, (string hex, float h, float r)> Layer = new Dictionary<string, (string, float, float)>
        {
            ["broetchen"] = ("#E8B169", 0.16f, 0.36f), ["patty"] = ("#7A4A2E", 0.1f, 0.34f), ["kuerbis_patty"] = ("#E58A2E", 0.1f, 0.34f),
            ["gold_sosse"] = ("#F5C542", 0.03f, 0.33f), ["salat"] = ("#7BC67E", 0.04f, 0.38f), ["kaese"] = ("#FFD54A", 0.03f, 0.36f),
            ["tomate"] = ("#E2483D", 0.05f, 0.31f), ["gurke"] = ("#5E9B4C", 0.04f, 0.12f),
        };

        public void ClearStack()
        {
            foreach (var s in _stack) if (s != null) Object.Destroy(s.gameObject);
            _stack.Clear();
        }

        /// <summary>One more layer on the burger (buns are domes, pickles small discs dotted around).</summary>
        public void StackLayer(string zutat)
        {
            if (!Layer.TryGetValue(zutat ?? "", out var l)) l = ("#CCCCCC", 0.05f, 0.3f);
            var y = CounterTop + 0.2f;
            foreach (var s in _stack) y += s.localScale.y > 0.2f ? 0.08f : s.localScale.y;
            var x = StationX["belegen"];
            GameObject go;
            if (zutat == "broetchen")
            {
                go = ArtLibrary.Sphere(_root, "Bun", At(x, y + 0.06f, 0.15f), 0.36f, M(l.hex, 0.3f));
                go.transform.localScale = new Vector3(0.74f, _stack.Count == 0 ? 0.2f : 0.34f, 0.74f);
            }
            else if (zutat == "gurke")
            {
                go = ArtLibrary.Cylinder(_root, "Pickle", At(x + Random.Range(-0.18f, 0.18f), y + 0.02f, 0.15f + Random.Range(-0.15f, 0.15f)), 0.11f, 0.04f, M(l.hex, 0.4f));
            }
            else go = ArtLibrary.Cylinder(_root, zutat, At(x, y + l.h * 0.5f, 0.15f), l.r, l.h, M(l.hex, zutat == "kaese" || zutat == "tomate" ? 0.5f : 0.25f));
            if (zutat == "kaese") go.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            _stack.Add(go.transform);
            Effects.Puff(go.transform.position, new Color(1f, 0.95f, 0.8f, 0.6f), 3, 0.1f, 0.4f, 0.4f, Vector3.up, 0.6f);
        }

        // ── fryer ──

        /// <summary>The basket down in the oil (held) or up; the fries brown with the time in the oil.</summary>
        public void Basket(bool down, double fortschritt, string zustand, bool reduced)
        {
            if (_basket == null) return;
            var fx = StationX["fritteuse"];
            var target = At(fx, CounterTop + 0.08f + (down ? 0.48f : 0.95f), 0.2f);
            _basket.position = Vector3.Lerp(_basket.position, target, 0.35f);
            Color c;
            if (zustand == Core.Dlc.McGoobySchicht.Dunkel) c = ArtLibrary.Hex("#8B5A2B");
            else if (zustand == Core.Dlc.McGoobySchicht.Goldgelb) c = Color.Lerp(ArtLibrary.Hex("#F2B833"), ArtLibrary.Hex("#FFD87A"), 0.5f + 0.5f * Mathf.Sin(_t * 9f));
            else c = Color.Lerp(ArtLibrary.Hex("#F4E1A1"), ArtLibrary.Hex("#F2B833"), (float)fortschritt);
            foreach (var r in _fries.GetComponentsInChildren<Renderer>()) r.material.color = c;
            if (down && !reduced && Random.value < 0.25f)
                Effects.Puff(At(fx + Random.Range(-0.4f, 0.4f), CounterTop + 0.66f, 0.2f + Random.Range(-0.25f, 0.25f)), new Color(1f, 0.95f, 0.7f, 0.6f), 1, 0.06f, 0.3f, 0.5f, Vector3.up, 0.3f);
        }

        // ── shake ──

        /// <summary>The cup fills while spinning; the crown stands in the window; too long and it foams over.</summary>
        public void Shake(double fortschritt, string zustand, bool reduced)
        {
            if (_shakeFill == null) return;
            var top = CounterTop + 0.08f;
            var h = Mathf.Lerp(0.04f, 0.95f, (float)fortschritt);
            _shakeFill.position = At(StationX["shake"], top + 0.32f + h * 0.5f, 0.2f);
            _shakeFill.localScale = new Vector3(0.48f, h * 0.5f, 0.48f);
            var krone = zustand == Core.Dlc.McGoobySchicht.Krone;
            _crown.gameObject.SetActive(krone || zustand == Core.Dlc.McGoobySchicht.Schaum);
            _cupGlow.gameObject.SetActive(krone);
            if (krone) _cupGlow.localScale = Vector3.one * (0.12f + 0.06f * Mathf.Sin(_t * 10f));
            if (zustand == Core.Dlc.McGoobySchicht.Schaum && !reduced && Random.value < 0.3f)
                Effects.Puff(At(StationX["shake"] + Random.Range(-0.2f, 0.2f), top + 1.4f, 0.2f), new Color(1f, 0.92f, 0.95f, 0.9f), 2, 0.12f, 0.6f, 0.7f, Vector3.down + Vector3.right * Random.Range(-1f, 1f), 0.5f);
        }

        public void ResetShake()
        {
            if (_shakeFill == null) return;
            Shake(0, Core.Dlc.McGoobySchicht.Fluessig, true);
        }

        public void Tick(float dt)
        {
            _t += dt;
            if (_chef != null)
            {
                var step = Mathf.MoveTowards(_chefX, _chefTargetX, dt * 4.5f);
                var walking = Mathf.Abs(step - _chefX) > 1e-4f;
                _chefX = step;
                var y = ChefY;
                if (_hop > 0f) { _hop = Mathf.Max(0f, _hop - dt); y += Mathf.Sin((0.6f - _hop) / 0.6f * Mathf.PI) * 0.25f; }
                _chef.transform.position = At(_chefX, y, 1.55f);
                if (walking && _chef.Clip != "walk-loop") _chef.Play("walk-loop");
                else if (!walking && _chef.Clip == "walk-loop") _chef.Play("idle-loop");
            }
            if (_customer != null && _customerHop > 0f)
            {
                _customerHop = Mathf.Max(0f, _customerHop - dt);
                _customer.transform.position = At(CustomerSpot.x, Mathf.Sin(_customerHop / 0.4f * Mathf.PI) * 0.15f, CustomerSpot.z);
            }
        }

        public void Destroy()
        {
            _destroyed = true;
            Stage.Destroy();
        }
    }
}
