using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The open ranch region in 3D (port of ranch_region_scene.gd and ranch_welt_reiter.gd): the 2 × 2.3 km world of
    /// ranch_karte.json on the Godot height model (<see cref="RanchGelaende"/>) — terrain chunks painted per zone mood with
    /// the paths, trails, shores, rock and snow, the lake, the bay, the brook, the mountain lake and the moor ponds as water,
    /// each zone dressed and streamed by distance, the sixteen discoveries as landmarks — and the free rider: the palomino
    /// with Gooby in the saddle, steered by a stick (or the keyboard) or an autopilot along the paths, walk/trot/gallop,
    /// stopped by deep water except at the ford and the bridges, a chase camera, the weather riding along. Region (x, z)
    /// is local (x, h, −z) under a root far above every other set, so north is the camera's forward at heading 0.
    /// </summary>
    public sealed partial class RanchWelt3D
    {
        public const float ViewH = 720f;
        /// <summary>ranch_welt_reiter.gd: walk, trot, gallop (m/s) and the turn rate (rad/s, a little over half at speed).</summary>
        public const float TempoSchritt = 3.6f, TempoTrab = 8f, TempoGalopp = 14.5f, DrehTempo = 1.9f, Beschleunigung = 9f;
        public const float Sichtweite = 470f, StreamAbstand = 380f;
        private const float HufVor = 1.05f, RiderScale = 0.72f;
        private static readonly Vector3 Basis = new Vector3(0f, 4000f, 0f);

        public MgStage3D Stage { get; }
        public RanchHorse3D Pferd { get; private set; }
        public GoobyAvatar Gooby { get; private set; }

        /// <summary>Region position (m, x east, z south) and heading (rad, 0 = north, clockwise).</summary>
        public double X => _x;
        public double Z => _z;
        public float Heading => _heading;
        public float Tempo => _tempo;
        /// <summary>The zone under the rider ("" between the zones).</summary>
        public string Zone => _zone;
        public string Gangart { get; private set; } = "stand";
        public string Untergrund { get; private set; } = "wiese";
        /// <summary>The steering stick: x turns, y rides on (up) or backs off (down); −1…1.</summary>
        public Vector2 Stick { get; set; }
        /// <summary>The HUD's gallop switch: forward means gallop instead of trot.</summary>
        public bool Galopp { get; set; }
        public bool Autopilot => _route.Count > 0;
        /// <summary>The terrain is in (until then the screen shows its saddling card).</summary>
        public bool Bereit => _chunksFertig;
        public float BauFortschritt => _chunkTotal <= 0 ? (_daten == null ? 0.05f : 0.5f) : 0.5f + 0.5f * _chunkNext / _chunkTotal;

        /// <summary>A new zone under the hooves.</summary>
        public event Action<string> ZoneGewechselt;
        /// <summary>The autopilot reached its goal.</summary>
        public event Action Angekommen;

        private readonly Transform _root;
        private readonly bool _night;
        private readonly List<V2d> _route = new List<V2d>();
        private double _x, _z;
        private float _heading, _tempo, _pitch, _zoneIn, _groundIn, _camDist = 9f, _camHoch = 4.2f;
        private string _zone = "";
        private Vector3 _camPos;
        private bool _camSnap = true, _destroyed, _mounted, _active = true;
        private WeatherFx _fx;

        private static Vector3 L(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

        public RanchWelt3D(UnityEngine.UIElements.VisualElement field, Dictionary<string, string> outfit, bool night, string spawnZone, Color fell, Color maehne)
        {
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            _night = night;
            Stage = new MgStage3D(ViewH * aspect, ViewH, Himmel);
            Stage.Bind(field);
            Stage.Camera.farClipPlane = Sichtweite;
            Stage.Camera.nearClipPlane = 0.4f;
            Stage.Camera.fieldOfView = 58f;
            _root = new GameObject("RanchWelt").transform;
            _root.SetParent(Stage.Root.transform, false);
            _root.localPosition = Basis;
            if (night) MgStudio.Night();
            // the height model's caches fill on the main thread before the terrain worker reads them
            RanchGelaende.ReitHoehe(0.0, 0.0);
            RanchGelaende.BachKerbe(0.0, 0.0);
            var spawn = RanchKarte.ZoneVon(spawnZone)?.Spawn ?? RanchKarte.ZoneVon("hof").Spawn;
            _x = spawn.X;
            _z = spawn.Z;
            _heading = spawn.Z > 0.0 ? 0f : Mathf.PI;
            Pferd = RanchHorse3D.Create(_root, "Reiter", fell, maehne);
            Pferd.Hufe = "wiese";
            Place();
            _fx = new WeatherFx(_root, 913);
            _fx.Quiet = true;
            _fx.Place(WeatherFx.Stage.Street, _root.position + L(_x, RanchGelaende.ReitHoehe(_x, _z), _z), new Vector3(26f, 1f, 26f), 16f, 1.5f);
            StarteGelaende();
            BaueDeko();
            BaueWaelder();
            _stunde = night ? 22.0 : 12.0;
            BaueTiere();
            _ = SpawnGoobyAsync(outfit);
        }

        public Color Himmel => _night ? new Color(0.2f, 0.25f, 0.42f) : new Color(0.71f, 0.85f, 0.95f);

        /// <summary>Covered by another screen: parked, its fog and weather off; back: the region's light again.</summary>
        public void SetVisible(bool on)
        {
            _active = on;
            Stage.Park(!on);
            if (on)
            {
                if (_night) MgStudio.Night();
                ApplyFog();
            }
            else
            {
                if (_night) MgStudio.Day();
                RenderSettings.fog = false;
            }
            _fx.Root.SetActive(on);
        }

        private void ApplyFog()
        {
            // the build keeps only the exp² fog variants: a soft distance haze that melts the far land into the sky
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(Himmel, Color.white, _night ? 0.05f : 0.18f);
            RenderSettings.fogDensity = _nebel > 0.05f ? Mathf.Lerp(0.0026f, 0.0058f, _nebel) : 0.0021f;
        }

        private float _nebel;

        /// <summary>The weather of the moment (the screen's day plan): rain and snow ride along, fog thickens the haze.</summary>
        public void Wetter(Gooby.Core.Life.WetterFx.Plan plan)
        {
            _fx.Apply(plan, GraphicsQuality.ParticleFactor);
            TiereZeit(_stunde, plan.Typ);
            _nebel = (float)plan.Nebel;
            // the studio ambience is tuned for small sets; the open land wants deeper shade under the same key light
            MgStudio.Weather((float)plan.SonnenFaktor, (float)plan.AmbientFaktor * 0.8f, 0f, (float)plan.Grau);
            if (Stage.Camera != null) Stage.Camera.backgroundColor = Color.Lerp(Himmel, new Color(0.66f, 0.7f, 0.76f), (float)plan.Grau * (_night ? 0.3f : 0.75f));
            if (_active) ApplyFog();
            RenderSettings.fogColor = Color.Lerp(Stage.Camera.backgroundColor, Color.white, _night ? 0.05f : 0.16f);
        }

        // ── the rider ──

        private async System.Threading.Tasks.Task SpawnGoobyAsync(Dictionary<string, string> outfit)
        {
            var avatar = await GoobyAvatar.CreateAsync(_root, Pferd.transform.position);
            if (avatar == null || _destroyed) return;
            if (outfit != null) avatar.Wear(outfit);
            avatar.SetEmotion("happy");
            Gooby = avatar;
            Gooby.transform.localScale = Vector3.one * RiderScale;
            Pferd.Mount(Gooby.transform);
            Gooby.Play("sit-loop", 0f);
            _mounted = true;
        }

        /// <summary>Jump to a zone's spawn (QA, the map's "Dahin" for far zones), facing the way into it.</summary>
        public void SpringeZu(string zoneId)
        {
            var zone = RanchKarte.ZoneVon(zoneId);
            if (zone == null) return;
            SpringeZu(zone.Spawn.X, zone.Spawn.Z, zone.Spawn.Z > 0.0 ? 0f : Mathf.PI);
        }

        public void SpringeZu(double x, double z, float heading)
        {
            _x = x;
            _z = z;
            _heading = heading;
            _tempo = 0f;
            _route.Clear();
            _camSnap = true;
            Place();
        }

        /// <summary>Ride along these region points (the map's "Dahin reiten"); a stick move takes the reins back.</summary>
        public void Reite(IEnumerable<V2d> punkte)
        {
            _route.Clear();
            _route.AddRange(punkte);
        }

        public void Anhalten() => _route.Clear();

        public void Tick(float dt)
        {
            if (_destroyed || dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            BaueWeiter();
            if (!_active) return;
            if (!Extern) Steuern(dt);
            Place();
            if (_ueberblick.HasValue) UeberblickKamera();
            else Kamera(dt);
            TickMp();
            _fx.Tick(dt, Stage.Camera, null);
            TickDeko(dt);
            TickTiere(dt);
            if (_chunksFertig) StreuTick();
            if ((_zoneIn -= dt) <= 0f)
            {
                _zoneIn = 0.4f;
                var zone = RanchKarte.ZoneBei(_x, _z);
                if (zone.Length > 0 && zone != _zone)
                {
                    _zone = zone;
                    ZoneGewechselt?.Invoke(zone);
                }
            }
            if ((_groundIn -= dt) <= 0f)
            {
                _groundIn = 0.3f;
                Untergrund = UntergrundBei(_x, _z);
                Pferd.Hufe = Untergrund;
            }
        }

        private void Steuern(float dt)
        {
            var stick = Stick;
            var keys = Tasten();
            if (keys.sqrMagnitude > 0.01f) stick = keys;
            if (stick.sqrMagnitude > 0.04f && _route.Count > 0) _route.Clear();
            float lenken, vor;
            if (_route.Count > 0)
            {
                var punkt = _route[0];
                var dx = punkt.X - _x;
                var dz = punkt.Z - _z;
                var dist = Math.Sqrt(dx * dx + dz * dz);
                if (dist < (_route.Count == 1 ? 6.0 : 10.0))
                {
                    _route.RemoveAt(0);
                    if (_route.Count == 0) Angekommen?.Invoke();
                }
                var soll = Mathf.Atan2((float)dx, (float)-dz);
                var diff = Mathf.DeltaAngle(_heading * Mathf.Rad2Deg, soll * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                lenken = Mathf.Clamp(diff * 2.2f, -1f, 1f);
                vor = _route.Count == 0 ? 0f : Mathf.Abs(diff) > 1.2f ? 0.4f : 1f;
            }
            else
            {
                lenken = Mathf.Abs(stick.x) < 0.12f ? 0f : stick.x;
                vor = stick.y;
            }
            var drehen = DrehTempo * dt * (_tempo > TempoTrab ? 0.55f : 1f) * (Mathf.Abs(_tempo) < 0.3f ? 0.6f : 1f);
            _heading = Mathf.Repeat(_heading + lenken * drehen, Mathf.PI * 2f);
            var sollTempo = 0f;
            if (vor > 0.55f) sollTempo = Galopp ? TempoGalopp : TempoTrab;
            else if (vor > 0.15f) sollTempo = TempoSchritt;
            else if (vor < -0.3f) sollTempo = -TempoSchritt * 0.5f;
            _tempo = Mathf.MoveTowards(_tempo, sollTempo, dt * Beschleunigung);
            if (Mathf.Abs(_tempo) > 0.05f)
            {
                var fx = Mathf.Sin(_heading) * _tempo * dt;
                var fz = -Mathf.Cos(_heading) * _tempo * dt;
                if (Frei2(_x + fx, _z + fz)) { _x += fx; _z += fz; }
                else if (Frei2(_x + fx, _z)) { _x += fx; _tempo *= 0.96f; }
                else if (Frei2(_x, _z + fz)) { _z += fz; _tempo *= 0.96f; }
                else _tempo = 0f;
            }
            var t = Mathf.Abs(_tempo);
            Gangart = t < 0.4f ? "stand" : t < TempoSchritt + 0.8f ? "schritt" : t < TempoTrab + 1.5f ? "trab" : "galopp";
            var basis = Gangart == "schritt" ? TempoSchritt : Gangart == "trab" ? TempoTrab : TempoGalopp;
            Pferd.Gait(Gangart, Gangart == "stand" ? 1f : Mathf.Clamp(t / basis, 0.5f, 1.3f));
        }

        /// <summary>The ground takes the horse here: walkable, and no trunk within a body's width.</summary>
        private bool Frei2(double x, double z) => RanchKarte.IstBegehbar(x, z) && StammAbstand(x, z, 3.0) > 0.9;

        private static Vector2 Tasten()
        {
            var k = new Vector2(0f, 0f);
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) k.x -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) k.x += 1f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) k.y += 1f;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) k.y -= 1f;
            return k;
        }

        /// <summary>The horse on the ground: front and back hooves sampled, the body pitched with the slope (ranch_welt_reiter.gd).</summary>
        private void Place()
        {
            var fx = Mathf.Sin(_heading) * HufVor;
            var fz = -Mathf.Cos(_heading) * HufVor;
            var vorn = RanchGelaende.ReitHoehe(_x + fx, _z + fz);
            var hinten = RanchGelaende.ReitHoehe(_x - fx, _z - fz);
            var mitte = RanchGelaende.ReitHoehe(_x, _z);
            var steigung = (float)(vorn - hinten) / (HufVor * 2f);
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(Mathf.Atan(steigung) * Mathf.Rad2Deg * 0.55f, -17f, 17f), 0.2f);
            var y = Math.Max(mitte, Math.Max(vorn, hinten) - Math.Abs(Math.Sin(_pitch * Mathf.Deg2Rad)) * HufVor) + 0.05 + _sprungY;
            Pferd.transform.localPosition = L(_x, y, _z);
            Pferd.transform.localRotation = Quaternion.Euler(-_pitch, _heading * Mathf.Rad2Deg, 0f);
        }

        /// <summary>Behind and above the rider, a little farther at speed; never under the ground (ranch_welt_reiter.gd).</summary>
        private void Kamera(float dt)
        {
            var cam = Stage.Camera;
            if (cam == null) return;
            var horse = Pferd.transform.position;
            var back = new Vector3(-Mathf.Sin(_heading), 0f, -Mathf.Cos(_heading));
            // at a landmark the camera rises and comes closer, looking over the stones and chests instead of through them
            var nah = false;
            foreach (var o in RanchEntdeckungen.AlleOrte)
                if (o.Pos.Abstand(_x, _z) < 16.0) { nah = true; break; }
            var soll = nah ? 6.8f : 8.6f + Mathf.Abs(_tempo) * 0.28f;
            // under the trees the camera comes in (and a little down) until no crown stands where it would be
            var frei = soll;
            for (var d = 2.6f; d <= soll + 0.01f; d += 0.7f)
            {
                var px = _x - Mathf.Sin(_heading) * d;
                var pz = _z + Mathf.Cos(_heading) * d;
                if (StammAbstand(px, pz, 4.0) < 3.1) { frei = Mathf.Max(2.6f, d - 0.7f); break; }
            }
            var eng = frei < soll - 0.1f;
            _camHoch = Mathf.Lerp(_camHoch, nah ? 7.2f : eng ? Mathf.Lerp(2.5f, 4.2f, (frei - 2.6f) / 6f) : 4.2f, Mathf.Min(1f, dt * 3f));
            _camDist = Mathf.Lerp(_camDist, frei, Mathf.Min(1f, dt * (eng ? 6f : 2f)));
            var ziel = horse + back * _camDist + Vector3.up * _camHoch;
            var rx = ziel.x - _root.position.x;
            var rz = -(ziel.z - _root.position.z);
            var boden = (float)RanchGelaende.Hoehe(rx, rz) + _root.position.y + 1.6f;
            if (ziel.y < boden) ziel.y = boden;
            _camPos = _camSnap ? ziel : Vector3.Lerp(_camPos, ziel, Mathf.Min(1f, dt * 4f));
            _camSnap = false;
            cam.transform.position = _camPos;
            cam.transform.rotation = Quaternion.LookRotation(horse + Vector3.up * 1.9f - _camPos);
        }

        /// <summary>The ground under the hooves for their sound: a path is sand, the bridges wood, the ford water, high rock stone.</summary>
        public static string UntergrundBei(double x, double z)
        {
            if (RanchGelaende.ReitHoehe(x, z) > RanchGelaende.Hoehe(x, z) + 0.2) return "holz";
            if (RanchKarte.Bruecke.Abstand(x, z) < 5.0) return "holz";
            if (RanchGelaende.BachKerbe(x, z) > 0.4) return "wasser";
            var d = RanchGelaende.WegAbstand(x, z, out var halb);
            if (d <= halb + 0.5) return "sand";
            if (RanchGelaende.Hoehe(x, z) > 70.0) return "stein";
            return "wiese";
        }

        /// <summary>A region point on the field (px, y down) — the screen's tags and floats.</summary>
        public Vector2 ToField(double x, double y, double z, float fieldW, float fieldH)
        {
            var cam = Stage.Camera;
            if (cam == null) return new Vector2(-9999f, -9999f);
            var vp = cam.WorldToViewportPoint(_root.position + L(x, y, z));
            if (vp.z <= 0f) return new Vector2(-9999f, -9999f);
            return new Vector2(vp.x * fieldW, (1f - vp.y) * fieldH);
        }

        public void Destroy()
        {
            _destroyed = true;
            if (_night) MgStudio.Day();
            MgStudio.Weather(1f, 1f);
            RenderSettings.fog = false;
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            Stage.Destroy();
        }

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
    }
}
