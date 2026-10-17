using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The ranch's random events on the farm (ranch_event_host.gd's scene hooks): a horse breaks out past the bales and
    /// waits to be caught, three crows sit on the hay stack until they are shooed off one by one, the travelling farrier
    /// stops by the yard with his anvil, and carrots rain onto the yard to be picked up.
    /// </summary>
    public sealed partial class RanchHofStage
    {
        private static readonly Vector3 AusbuexSpot = new Vector3(5.4f, 0f, 0.9f);
        private static readonly Vector3 HinterBallen = new Vector3(5.6f, 0f, 3.9f);
        private static readonly Vector3 SchmiedSpot = new Vector3(-4.4f, 0f, 0.4f);

        private sealed class Flug
        {
            public Transform T;
            public Vector3 V;
            public float Life;
        }

        private readonly List<Transform> _kraehen = new List<Transform>();
        private readonly List<Flug> _fluege = new List<Flug>();
        private readonly List<Transform> _karotten = new List<Transform>();
        private readonly List<float> _karottenVy = new List<float>();
        private Transform _schmied;
        private string _ausgebuext;

        public string Ausgebuext => _ausgebuext;

        /// <summary>A horse slips out of the paddock gate and stands by the bales; tapping it catches it.</summary>
        public bool Ausbuexen(string id)
        {
            if (!_horses.TryGetValue(id ?? "", out var h) || id == _selected) return false;
            _ausgebuext = id;
            if (h.Sleeping) { h.Sleeping = false; h.SleepShown = false; h.View.Action("idle-loop", 0.1f); }
            WalkTo(h, new[] { GateSpot + new Vector3(0.45f, 0f, 0.9f), GateSpot + new Vector3(2.4f, 0f, -0.5f), HinterBallen, AusbuexSpot }, 200f, () => h.View.Action("kopfschuetteln"));
            return true;
        }

        public void Einfangen()
        {
            if (_ausgebuext == null || !_horses.TryGetValue(_ausgebuext, out var h)) { _ausgebuext = null; return; }
            WalkTo(h, new[] { HinterBallen, GateSpot + new Vector3(2.4f, 0f, -0.5f), GateSpot + new Vector3(0.45f, 0f, 0.9f), h.Home }, h.HomeYaw, () => LieDown(h));
            h.View.Action("kopfschuetteln");
            GoobyBeat("celebrate");
            _ausgebuext = null;
        }

        /// <summary>Crows on the hay stack (tap targets 0..n−1), pecking.</summary>
        public void Kraehen(int n)
        {
            var black = M("#2E2A33", 0.4f);
            var beak = M("#F2A33A", 0.3f);
            for (var i = 0; i < n; i++)
            {
                var crow = new GameObject("Kraehe" + i).transform;
                crow.SetParent(_root, false);
                crow.position = At(BalesSpot + new Vector3(-0.9f + i * 0.9f, 1.05f, 0.1f * i));
                crow.rotation = Quaternion.Euler(0f, 160f + i * 40f, 0f);
                ArtLibrary.Sphere(crow, "Body", crow.position + Vector3.up * 0.2f, 0.2f, black);
                ArtLibrary.Sphere(crow, "Head", crow.TransformPoint(new Vector3(0f, 0.42f, 0.16f)), 0.12f, black);
                var b = ArtLibrary.Box(crow, "Beak", crow.TransformPoint(new Vector3(0f, 0.4f, 0.3f)), new Vector3(0.05f, 0.05f, 0.14f), beak, false);
                b.transform.rotation = crow.rotation;
                foreach (var s in new[] { -1f, 1f })
                {
                    var wing = ArtLibrary.Box(crow, "Wing", crow.TransformPoint(new Vector3(0.17f * s, 0.24f, -0.02f)), new Vector3(0.05f, 0.16f, 0.3f), black, false);
                    wing.transform.rotation = crow.rotation * Quaternion.Euler(0f, 0f, 20f * s);
                }
                AddTap(crow.gameObject, Ziel.Kraehe, "", i, new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.9f, 0.8f));
                _kraehen.Add(crow);
            }
        }

        public bool Verscheuchen(int i)
        {
            if (i < 0 || i >= _kraehen.Count || _kraehen[i] == null) return false;
            var crow = _kraehen[i];
            _kraehen[i] = null;
            if (crow.TryGetComponent<Tap>(out var tap)) tap.Ziel = Ziel.Nichts;
            _fluege.Add(new Flug { T = crow, V = new Vector3(Random.Range(-2f, 2f), 4.5f, Random.Range(2f, 4f)), Life = 1.8f });
            Effects.Puff(crow.position + Vector3.up * 0.3f, new Color(0.2f, 0.18f, 0.22f, 0.9f), 6, 0.05f, 1.2f, 0.6f, Vector3.up, 1f);
            GoobyBeat("hop");
            return true;
        }

        public int KraehenUebrig
        {
            get
            {
                var n = 0;
                foreach (var c in _kraehen) if (c != null) n++;
                return n;
            }
        }

        /// <summary>The farrier at the gate: a Gooby in a cap beside his anvil (one tap target).</summary>
        public void Hufschmied(bool da)
        {
            if (!da) { if (_schmied != null) Object.Destroy(_schmied.gameObject); _schmied = null; return; }
            if (_schmied != null) return;
            var root = new GameObject("Hufschmied").transform;
            root.SetParent(_root, false);
            root.position = At(SchmiedSpot);
            ArtLibrary.Box(root, "Anvil", root.position + new Vector3(0.9f, 0.32f, 0f), new Vector3(0.7f, 0.34f, 0.34f), M("#5D6470", 0.6f));
            ArtLibrary.Box(root, "AnvilFoot", root.position + new Vector3(0.9f, 0.1f, 0f), new Vector3(0.36f, 0.2f, 0.3f), M("#4A4F59", 0.5f));
            ArtLibrary.Box(root, "Toolbox", root.position + new Vector3(-0.7f, 0.18f, 0.2f), new Vector3(0.6f, 0.36f, 0.36f), M("#B5523B", 0.3f));
            AddTap(root.gameObject, Ziel.Schmied, "", 0, new Vector3(0f, 0.9f, 0f), new Vector3(2.6f, 2f, 1.4f));
            _schmied = root;
            _ = SpawnSchmiedAsync(root);
        }

        private async System.Threading.Tasks.Task SpawnSchmiedAsync(Transform root)
        {
            var avatar = await GoobyAvatar.CreateAsync(root, root.position);
            if (avatar == null || root == null || _destroyed) { if (avatar != null) Object.Destroy(avatar.gameObject); return; }
            avatar.transform.SetPositionAndRotation(root.position, Quaternion.Euler(0f, 200f, 0f));
            avatar.Wear(new Dictionary<string, string> { ["fell"] = "ash", ["hut"] = "cap" });
            avatar.Play("wave");
            avatar.SetEmotion("happy");
        }

        /// <summary>Carrots rain onto the yard in front of the paddock and lie there to be picked up (tap targets 0..n−1).</summary>
        public void Karottenregen(int n)
        {
            var orange = M("#F29A3A", 0.3f);
            var green = M("#6FB05A", 0.2f);
            var rng = new System.Random(n * 7 + 3);
            for (var i = 0; i < n; i++)
            {
                var p = new Vector3(-5f + (float)rng.NextDouble() * 11.5f, 7f + (float)rng.NextDouble() * 5f, -1.2f + (float)rng.NextDouble() * 1.6f + (i % 3) * 1.6f);
                var carrot = new GameObject("Karotte" + i).transform;
                carrot.SetParent(_root, false);
                carrot.position = At(p);
                var body = Stage.Cone(carrot.position, 0.13f, 0.6f, orange, false);
                body.transform.SetParent(carrot, true);
                ArtLibrary.Box(carrot, "Leaf", carrot.position + Vector3.down * 0.05f, new Vector3(0.18f, 0.18f, 0.04f), green, false);
                carrot.rotation = Quaternion.Euler(180f, (float)rng.NextDouble() * 360f, 0f);
                AddTap(carrot.gameObject, Ziel.Karotte, "", i, Vector3.zero, new Vector3(0.9f, 0.9f, 0.9f));
                _karotten.Add(carrot);
                _karottenVy.Add(0f);
            }
        }

        public bool Aufsammeln(int i)
        {
            if (i < 0 || i >= _karotten.Count || _karotten[i] == null) return false;
            var carrot = _karotten[i];
            _karotten[i] = null;
            Effects.Puff(carrot.position, new Color(1f, 0.7f, 0.35f, 0.9f), 6, 0.06f, 1f, 0.5f, Vector3.up, 0.8f);
            Object.Destroy(carrot.gameObject);
            GoobyBeat("hop");
            return true;
        }

        public int KarottenUebrig
        {
            get
            {
                var n = 0;
                foreach (var c in _karotten) if (c != null) n++;
                return n;
            }
        }

        public Vector3 KarottePunkt(int i) => i >= 0 && i < _karotten.Count && _karotten[i] != null ? _karotten[i].position - MgStage3D.Origin : CareSpot;

        /// <summary>Any scene of an event is cleared (time ran out or it was solved).</summary>
        public void EventAufraeumen()
        {
            foreach (var c in _kraehen) if (c != null) Object.Destroy(c.gameObject);
            _kraehen.Clear();
            foreach (var c in _karotten) if (c != null) Object.Destroy(c.gameObject);
            _karotten.Clear();
            _karottenVy.Clear();
            Hufschmied(false);
            if (_ausgebuext != null && _horses.TryGetValue(_ausgebuext, out var h))
            {
                WalkTo(h, new[] { HinterBallen, GateSpot + new Vector3(2.4f, 0f, -0.5f), GateSpot + new Vector3(0.45f, 0f, 0.9f), h.Home }, h.HomeYaw, () => LieDown(h));
                _ausgebuext = null;
            }
        }

        private void TickEvents(float dt)
        {
            for (var i = _fluege.Count - 1; i >= 0; i--)
            {
                var f = _fluege[i];
                if (f.T == null || (f.Life -= dt) <= 0f)
                {
                    if (f.T != null) Object.Destroy(f.T.gameObject);
                    _fluege.RemoveAt(i);
                    continue;
                }
                f.V += new Vector3(0f, 1.5f, 0f) * dt;
                f.T.position += f.V * dt;
                f.T.rotation = Quaternion.LookRotation(new Vector3(f.V.x, 0f, f.V.z).normalized + Vector3.forward * 0.001f);
            }
            for (var i = 0; i < _kraehen.Count; i++)
            {
                var c = _kraehen[i];
                if (c == null) continue;
                var peck = Mathf.Repeat(_t * 1.3f + i * 0.37f, 1f) < 0.18f;
                c.localRotation = Quaternion.Euler(peck ? 25f : 0f, c.localEulerAngles.y, 0f);
            }
            for (var i = 0; i < _karotten.Count; i++)
            {
                var c = _karotten[i];
                if (c == null) continue;
                var local = c.position - MgStage3D.Origin;
                if (local.y <= 0.12f) continue;
                _karottenVy[i] -= 9.8f * dt;
                local.y = Mathf.Max(0.12f, local.y + _karottenVy[i] * dt);
                c.position = MgStage3D.Origin + local;
                if (local.y <= 0.12f)
                {
                    c.rotation = Quaternion.Euler(0f, c.eulerAngles.y, 90f);
                    Effects.Puff(c.position, new Color(0.85f, 0.75f, 0.45f, 0.8f), 3, 0.05f, 0.6f, 0.4f, Vector3.up, 1f);
                }
            }
        }
    }
}
