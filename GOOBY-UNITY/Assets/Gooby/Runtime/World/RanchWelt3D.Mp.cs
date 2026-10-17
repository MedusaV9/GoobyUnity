using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The ranch multiplayer in the open region (rmp_ausritt_controller.gd, rmp_remote_rider.gd): the own rider follows the
    /// round's ride controller instead of the stick, the friends ride as horses with their Gooby in the saddle, the course
    /// stands as numbered gates (the next one glows gold with a beam), the tag paddock as a fence ring with the catcher's red
    /// ring under the horse, a visit's horses graze on the yard ring. Before the start the camera looks down on the course.
    /// </summary>
    public sealed partial class RanchWelt3D
    {
        /// <summary>The own rider moves by <see cref="SetzePose"/> (the multiplayer round's controller), not by the stick.</summary>
        public bool Extern { get; set; }

        private float _sprungY;
        private (V2d mitte, float halb)? _ueberblick;
        private readonly Dictionary<string, Mitreiter> _mitreiter = new Dictionary<string, Mitreiter>();
        private Transform _kurs, _strahl, _faengerRing, _hofRing, _besuch;
        private readonly List<(Transform tor, Renderer band)> _tore = new List<(Transform, Renderer)>();
        private int _naechstesTor = -1;
        private string _faenger = "";

        private sealed class Mitreiter
        {
            public RanchHorse3D Pferd;
            public GoobyAvatar Gooby;
            public bool Weg;
            public float Pitch;
        }

        /// <summary>The own pose from the round (region metres, heading clockwise from north, tempo m/s, gait, jump height).</summary>
        public void SetzePose(double x, double z, float heading, float tempo, string gangart, float sprungY)
        {
            _x = x;
            _z = z;
            _heading = heading;
            _tempo = tempo;
            _sprungY = Mathf.Max(0f, sprungY);
            Gangart = gangart == "toelt" ? "trab" : gangart ?? "stand";
            Pferd.Gait(Gangart, GangTempo(Gangart, tempo));
        }

        private static float GangTempo(string gangart, float tempo)
        {
            if (gangart == "stand") return 1f;
            var basis = gangart == "schritt" ? TempoSchritt : gangart == "trab" ? TempoTrab : TempoGalopp;
            return Mathf.Clamp(Mathf.Abs(tempo) / basis, 0.5f, 1.3f);
        }

        /// <summary>The lobby's look from high above a point (null: back to the chase camera behind the own rider).</summary>
        public void Ueberblick(V2d? mitte, float halb = 60f)
        {
            if (mitte.HasValue == _ueberblick.HasValue && (!mitte.HasValue || (mitte.Value.Abstand(_ueberblick.Value.mitte.X, _ueberblick.Value.mitte.Z) < 0.5 && Mathf.Abs(halb - _ueberblick.Value.halb) < 0.5f))) return;
            _ueberblick = mitte.HasValue ? (mitte.Value, halb) : ((V2d, float)?)null;
            _camSnap = true;
        }

        private void UeberblickKamera()
        {
            var cam = Stage.Camera;
            if (cam == null || !_ueberblick.HasValue) return;
            var (mitte, halb) = _ueberblick.Value;
            var boden = (float)RanchGelaende.Hoehe(mitte.X, mitte.Z);
            var ziel = _root.position + L(mitte.X, boden, mitte.Z);
            var dist = Mathf.Clamp(halb * 1.45f, 40f, 190f);
            var pos = ziel + new Vector3(0f, dist * 0.78f, -dist * 0.62f);
            _camPos = _camSnap ? pos : Vector3.Lerp(_camPos, pos, Mathf.Min(1f, Time.unscaledDeltaTime * 3f));
            _camSnap = false;
            cam.transform.position = _camPos;
            cam.transform.rotation = Quaternion.LookRotation(ziel - _camPos);
        }

        // ── the friends ──

        /// <summary>A friend's rider, made on first sight (coat by start place); the Gooby climbs into the saddle when loaded.</summary>
        public void Mitreiten(string code, double x, double z, float heading, string gangart, float tempo, float sprungY, Color fell, Color maehne)
        {
            if (string.IsNullOrEmpty(code)) return;
            if (!_mitreiter.TryGetValue(code, out var m))
            {
                m = new Mitreiter { Pferd = RanchHorse3D.Create(_root, "Mitreiter:" + code, fell, maehne) };
                _mitreiter[code] = m;
                _ = MitreiterGoobyAsync(m);
            }
            var fx = Mathf.Sin(heading) * HufVor;
            var fz = -Mathf.Cos(heading) * HufVor;
            var vorn = RanchGelaende.ReitHoehe(x + fx, z + fz);
            var hinten = RanchGelaende.ReitHoehe(x - fx, z - fz);
            var y = Math.Max(RanchGelaende.ReitHoehe(x, z), Math.Max(vorn, hinten) - 0.2) + 0.05 + Math.Max(0f, sprungY);
            m.Pitch = Mathf.Lerp(m.Pitch, Mathf.Clamp(Mathf.Atan((float)(vorn - hinten) / (HufVor * 2f)) * Mathf.Rad2Deg * 0.55f, -17f, 17f), 0.2f);
            m.Pferd.transform.localPosition = L(x, y, z);
            m.Pferd.transform.localRotation = Quaternion.Euler(-m.Pitch, heading * Mathf.Rad2Deg, 0f);
            var gang = gangart == "toelt" ? "trab" : gangart ?? "stand";
            m.Pferd.Gait(gang, GangTempo(gang, tempo));
        }

        private async Task MitreiterGoobyAsync(Mitreiter m)
        {
            var avatar = await GoobyAvatar.CreateAsync(_root, m.Pferd.transform.position);
            if (avatar == null) return;
            if (_destroyed || m.Weg || m.Pferd == null) { UnityEngine.Object.Destroy(avatar.gameObject); return; }
            avatar.SetEmotion("happy");
            avatar.transform.localScale = Vector3.one * RiderScale;
            m.Pferd.Mount(avatar.transform);
            avatar.Play("sit-loop", 0f);
            m.Gooby = avatar;
        }

        /// <summary>Everyone not in <paramref name="codes"/> rides off the set (a friend left the room).</summary>
        public void BehalteMitreiter(ICollection<string> codes)
        {
            var weg = new List<string>();
            foreach (var kv in _mitreiter) if (!codes.Contains(kv.Key)) weg.Add(kv.Key);
            foreach (var code in weg)
            {
                var m = _mitreiter[code];
                m.Weg = true;
                if (m.Gooby != null) UnityEngine.Object.Destroy(m.Gooby.gameObject);
                if (m.Pferd != null) UnityEngine.Object.Destroy(m.Pferd.gameObject);
                _mitreiter.Remove(code);
            }
        }

        /// <summary>A friend's horse on the set (null before their first pose).</summary>
        public RanchHorse3D MitreiterPferd(string code) =>
            !string.IsNullOrEmpty(code) && _mitreiter.TryGetValue(code, out var m) ? m.Pferd : null;

        /// <summary>Region point over a friend's (or the own) horse, for the name tags.</summary>
        public bool ReiterPunkt(string code, out Vector3 lokal)
        {
            lokal = default;
            if (code == null) { lokal = Pferd.transform.localPosition; return true; }
            if (!_mitreiter.TryGetValue(code, out var m) || m.Pferd == null) return false;
            lokal = m.Pferd.transform.localPosition;
            return true;
        }

        // ── the course ──

        /// <summary>Numbered gates at the course's checkpoints, or the tag paddock's fence ring; null clears.</summary>
        public void ZeigeKurs(string kursId)
        {
            if (_kurs != null) UnityEngine.Object.Destroy(_kurs.gameObject);
            _kurs = null;
            _tore.Clear();
            _strahl = null;
            _naechstesTor = -1;
            if (_koppel != null) _koppel.gameObject.SetActive(kursId != "hof_parcours");
            var k = RmpKurse.Get(kursId);
            if (k == null) return;
            _kurs = new GameObject("Kurs:" + kursId).transform;
            _kurs.SetParent(_root, false);
            var pfosten = M("#FFFFFF", 0.3f);
            var rot = M("#E8574C", 0.3f);
            if (k.Checkpoints.Length > 0)
            {
                for (var i = 0; i < k.Checkpoints.Length; i++)
                {
                    var c = k.Checkpoints[i];
                    var vor = k.Checkpoints[(i + k.Checkpoints.Length - 1) % k.Checkpoints.Length];
                    var nach = k.Checkpoints[(i + 1) % k.Checkpoints.Length];
                    var richtung = (nach - vor).Normalized();
                    var tor = new GameObject("Tor" + (i + 1)).transform;
                    tor.SetParent(_kurs, false);
                    tor.localPosition = L(c.X, RanchGelaende.Hoehe(c.X, c.Y), c.Y);
                    tor.localRotation = Quaternion.Euler(0f, Mathf.Atan2(richtung.X, -richtung.Y) * Mathf.Rad2Deg, 0f);
                    var breite = (float)k.Radius * 0.85f;
                    foreach (var s in new[] { -1f, 1f })
                    {
                        for (var b = 0; b < 4; b++)
                            Teil(tor, new Vector3(s * breite, 0.4f + b * 0.8f, 0f), new Vector3(0.35f, 0.8f, 0.35f), b % 2 == 0 ? pfosten : rot);
                        Kugel(tor, new Vector3(s * breite, 3.4f, 0f), 0.32f, M("#F2C230", 0.4f));
                    }
                    var band = Teil(tor, new Vector3(0f, 3.0f, 0f), new Vector3(breite * 2f, 0.55f, 0.12f), M("#5FA8A0", 0.3f));
                    _tore.Add((tor, band.GetComponent<Renderer>()));
                }
                _strahl = ArtLibrary.Cylinder(_kurs, "Strahl", _kurs.position, 0.9f, 40f, ArtLibrary.Transparent(new Color(1f, 0.93f, 0.55f, 0.38f)), false).transform;
                NaechstesTor(0);
            }
            else
            {
                var n = Mathf.Max(24, (int)(k.ArenaRadius * 2.0 * Math.PI / 5.0));
                for (var i = 0; i < n; i++)
                {
                    var a0 = i * Mathf.PI * 2f / n;
                    var a1 = (i + 1) * Mathf.PI * 2f / n;
                    var p0 = new V2d(k.ArenaCenter.X + Math.Cos(a0) * k.ArenaRadius, k.ArenaCenter.Y + Math.Sin(a0) * k.ArenaRadius);
                    var p1 = new V2d(k.ArenaCenter.X + Math.Cos(a1) * k.ArenaRadius, k.ArenaCenter.Y + Math.Sin(a1) * k.ArenaRadius);
                    var h0 = RanchGelaende.Hoehe(p0.X, p0.Z);
                    var h1 = RanchGelaende.Hoehe(p1.X, p1.Z);
                    Teil(_kurs, L(p0.X, h0 + 0.6, p0.Z), new Vector3(0.22f, 1.2f, 0.22f), pfosten);
                    var a = L(p0.X, h0 + 0.95, p0.Z);
                    var b = L(p1.X, h1 + 0.95, p1.Z);
                    var rail = Teil(_kurs, (a + b) * 0.5f, new Vector3(0.1f, 0.12f, Vector3.Distance(a, b)), pfosten);
                    rail.transform.localRotation = Quaternion.LookRotation(b - a);
                }
            }
        }

        /// <summary>The next gate glows gold under a beam; passed gates turn grey, the rest stay teal.</summary>
        public void NaechstesTor(int idx)
        {
            if (_tore.Count == 0 || idx == _naechstesTor) return;
            _naechstesTor = idx;
            for (var i = 0; i < _tore.Count; i++)
                _tore[i].band.sharedMaterial = i == idx ? ArtLibrary.Glow(new Color(1.4f, 1.1f, 0.35f)) : i < idx ? M("#B9B4AC", 0.2f) : M("#5FA8A0", 0.3f);
            if (_strahl == null) return;
            _strahl.gameObject.SetActive(idx >= 0 && idx < _tore.Count);
            if (idx >= 0 && idx < _tore.Count) _strahl.localPosition = _tore[idx].tor.localPosition + Vector3.up * 20f;
        }

        public Vector3? TorPunkt(int idx) => idx >= 0 && idx < _tore.Count ? _tore[idx].tor.localPosition + Vector3.up * 4.2f : (Vector3?)null;

        /// <summary>The catcher of the tag paddock (own code or a friend's; "" nobody) — a red ring under the horse.</summary>
        public void Faenger(string code, bool eigener)
        {
            _faenger = eigener ? "@" : code ?? "";
            if (_faenger.Length == 0)
            {
                if (_faengerRing != null) _faengerRing.gameObject.SetActive(false);
                return;
            }
            if (_faengerRing == null)
            {
                _faengerRing = ArtLibrary.Cylinder(_root, "Faenger", _root.position, 2.2f, 0.06f, ArtLibrary.Transparent(new Color(0.95f, 0.25f, 0.25f, 0.55f)), false).transform;
            }
            _faengerRing.gameObject.SetActive(true);
        }

        private void FaengerRingFolgen()
        {
            if (_faengerRing == null || !_faengerRing.gameObject.activeSelf) return;
            var horse = _faenger == "@" ? Pferd != null ? Pferd.transform : null : _mitreiter.TryGetValue(_faenger, out var m) ? m.Pferd?.transform : null;
            if (horse == null) return;
            _faengerRing.localPosition = horse.localPosition + Vector3.up * 0.08f;
        }

        // ── the yard ──

        /// <summary>The yard ring of a free ride: sand with a low fence, a gap toward the riders' start.</summary>
        public void ZeigeHofRing(bool an)
        {
            if (!an)
            {
                if (_hofRing != null) _hofRing.gameObject.SetActive(false);
                return;
            }
            if (_hofRing != null) { _hofRing.gameObject.SetActive(true); return; }
            _hofRing = new GameObject("HofRing").transform;
            _hofRing.SetParent(_root, false);
            var m = RmpFrei.HofMitte;
            var r = RmpFrei.HofRingRadius;
            var h = RanchGelaende.Hoehe(m.X, m.Y);
            var sand = ArtLibrary.Cylinder(_hofRing, "Sand", _root.position + L(m.X, h + 0.02, m.Y), (float)r, 0.08f, M("#E8D6AA", 0.05f), false);
            sand.transform.localPosition = L(m.X, h + 0.02, m.Y);
            var holz = M("#B58A5F", 0.2f);
            const int n = 28;
            for (var i = 0; i < n; i++)
            {
                var a = i * Math.PI * 2.0 / n;
                if (Math.Sin(a) > 0.92) continue;
                var x = m.X + Math.Cos(a) * (r + 0.6);
                var z = m.Y + Math.Sin(a) * (r + 0.6);
                Teil(_hofRing, L(x, RanchGelaende.Hoehe(x, z) + 0.5, z), new Vector3(0.16f, 1.0f, 0.16f), holz);
            }
        }

        /// <summary>A visit's horses grazing on the north side of the yard ring (name, coat colours).</summary>
        public void ZeigeBesuchPferde(IList<(Color fell, Color maehne)> felle)
        {
            if (_besuch != null) UnityEngine.Object.Destroy(_besuch.gameObject);
            _besuch = null;
            if (felle == null || felle.Count == 0) return;
            _besuch = new GameObject("BesuchPferde").transform;
            _besuch.SetParent(_root, false);
            for (var i = 0; i < felle.Count; i++)
            {
                var p = RmpFrei.BesuchPferdPos(i, felle.Count);
                var h = RanchHorse3D.Create(_besuch, "Besuch" + i, felle[i].fell, felle[i].maehne);
                h.transform.localPosition = L(p.X, RanchGelaende.ReitHoehe(p.X, p.Y) + 0.05, p.Y);
                var zurMitte = RmpFrei.HofMitte - p;
                h.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(zurMitte.X, -zurMitte.Y) * Mathf.Rad2Deg, 0f);
                if (i % 2 == 0) h.Action("fressen-loop", 100000f);
            }
        }

        private GameObject Teil(Transform parent, Vector3 lokal, Vector3 groesse, Material m)
        {
            var go = ArtLibrary.Box(parent, "Teil", parent.position, groesse, m, false);
            go.transform.localPosition = lokal;
            return go;
        }

        private GameObject Kugel(Transform parent, Vector3 lokal, float r, Material m)
        {
            var go = ArtLibrary.Sphere(parent, "Kugel", parent.position, r, m, false);
            go.transform.localPosition = lokal;
            return go;
        }

        /// <summary>Called from <see cref="Tick"/>: the multiplayer bits that follow the riders.</summary>
        private void TickMp()
        {
            FaengerRingFolgen();
        }
    }
}
