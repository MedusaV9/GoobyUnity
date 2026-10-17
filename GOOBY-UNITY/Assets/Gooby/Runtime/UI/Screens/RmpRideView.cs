using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The view of a ranch multiplayer round. With a GPU it is the open ranch region in 3D (<see cref="RanchWelt3D"/>, like
    /// Godot's rmp_ausritt_controller.gd): the own horse follows the round's controller, the friends ride with their Gooby in
    /// the saddle and name tags over them, the course stands as numbered gates (the next one glows under a beam), the tag
    /// paddock as a fence ring with the catcher's red ring, a free ride starts on the yard ring in front of the farm with a
    /// visit's horses grazing there and the host's own build beside the paddock; before the start the camera looks down on
    /// the course. Without a GPU the painted top-down meadow stands in. Controls: left half = floating stick (only X
    /// steers), ▲/▼ for the gait.
    /// </summary>
    public sealed class RmpRideView : IRideCam
    {
        public const float StickRadiusPx = 64f, StickDeadzone = 0.12f;

        public readonly P.Canvas Canvas;
        public readonly VisualElement Controls, Tags;
        public RmpLink Link { get; private set; }
        public float Steer => _steer;

        private readonly GameHost _host;
        private readonly VisualElement _field, _stickZone, _gaitUp, _gaitDown;
        private readonly P.Canvas _stickCanvas;
        private readonly Func<long> _now;
        private readonly Dictionary<string, Label> _tags = new Dictionary<string, Label>();
        private V2 _cam;
        private double _camH, _anim, _span = 60.0;
        private bool _snap = true;
        private int _stickPointer = -1;
        private Vector2 _stickStart, _stickNow;
        private float _steer;
        private string _kurs = "";
        private (V2 pos, float scale)[] _trees = new (V2, float)[0];
        // the free ride (Besuch / Ausritt) on the Weidetal: landmarks instead of a course
        private bool _frei;
        private readonly HashSet<string> _folgeMir = new HashSet<string>();
        private readonly Dictionary<string, double> _herzen = new Dictionary<string, double>();
        private readonly List<(string name, string farbe)> _besuchPferde = new List<(string, string)>();
        private readonly Dictionary<string, Label> _ortTags = new Dictionary<string, Label>();
        private static readonly Color Water = MgScenery.Hex(0x7EC8E3), WaterLight = MgScenery.Hex(0xA9DDEE), Reed = MgScenery.Hex(0x4E8A45);
        private static readonly Color Roof = MgScenery.Hex(0xC9605A), Wall = MgScenery.Hex(0xF4E3C5), Hill = MgScenery.Hex(0x86BF62), Apple = MgScenery.Hex(0xE04848);

        /// <summary>Besuch / Ausritt: the Weidetal (also as the menu's preview of a free mode).</summary>
        public bool Frei => _frei;
        /// <summary>The landmark names for the tags over the Weidetal (id → label).</summary>
        public Func<string, string> OrtName { get; set; }

        private static readonly Color Grass = MgScenery.Hex(0x8FC66B), GrassLight = MgScenery.Hex(0x9BD177), GrassDark = MgScenery.Hex(0x76AD57);
        private static readonly Color Sand = MgScenery.Hex(0xE2C79A), SandDark = MgScenery.Hex(0xCFAF80);
        private static readonly Color Wood = MgScenery.Hex(0xC98C59), WoodDark = MgScenery.Hex(0x8C613D), Rail = MgScenery.Hex(0xF4EAD5);
        private static readonly Color Gold = MgScenery.Hex(0xFFD34D), Rose = MgScenery.Hex(0xE98CA0), Berry = MgScenery.Hex(0xE8676B);
        private static readonly Color Leaf = MgScenery.Hex(0x5E9F4E), LeafLight = MgScenery.Hex(0x7DBE5F), Trunk = MgScenery.Hex(0x8C613D);
        private static readonly Color[] Jacken = { MgScenery.Hex(0xE8676B), MgScenery.Hex(0x5C8CC7), MgScenery.Hex(0xFFD34D), MgScenery.Hex(0x7FCF9B) };
        private static readonly string[] Felle = { "braun", "schecke", "palomino", "rauchgrau" };

        public RmpRideView(GameHost host, VisualElement field, Func<long> nowMs)
        {
            _host = host;
            _field = field;
            _now = nowMs;
            Canvas = new P.Canvas { Paint = Paint, name = "rmp-stage" };
            Ux.Abs(Canvas, 0, 0, 0, 0);
            Tags = new VisualElement { name = "rmp-tags", pickingMode = PickingMode.Ignore };
            Ux.Abs(Tags, 0, 0, 0, 0);

            Controls = new VisualElement { name = "rmp-controls", pickingMode = PickingMode.Ignore };
            Ux.Abs(Controls, 0, 0, 0, 0);
            _stickZone = new VisualElement { name = "rmp-stick-zone" };
            _stickZone.style.position = Position.Absolute;
            _stickZone.style.left = 0; _stickZone.style.top = Length.Percent(20); _stickZone.style.bottom = 0; _stickZone.style.width = Length.Percent(50);
            _stickZone.RegisterCallback<PointerDownEvent>(OnStickDown);
            _stickZone.RegisterCallback<PointerMoveEvent>(OnStickMove);
            _stickZone.RegisterCallback<PointerUpEvent>(OnStickUp);
            _stickZone.RegisterCallback<PointerCaptureOutEvent>(_ => ReleaseStick());
            Controls.Add(_stickZone);
            _stickCanvas = new P.Canvas { Paint = PaintStick, name = "rmp-stick" };
            _stickCanvas.pickingMode = PickingMode.Ignore;
            Ux.Abs(_stickCanvas, 0, 0, 0, 0);
            Controls.Add(_stickCanvas);
            _gaitUp = RoundButton("rmp-gait-up", "arrow_up", 72f, () => Link?.Lauf?.Controller.GaitUp());
            _gaitUp.style.right = 26; _gaitUp.style.bottom = 120;
            Controls.Add(_gaitUp);
            _gaitDown = RoundButton("rmp-gait-down", "arrow_down", 72f, () => Link?.Lauf?.Controller.GaitDown());
            _gaitDown.style.right = 26; _gaitDown.style.bottom = 36;
            Controls.Add(_gaitDown);
            if (MgStage3D.Supported) Baue3D();
        }

        // ── the open region in 3D ──

        private RanchWelt3D _drei;

        /// <summary>The round rides through the 3D region (false: the painted meadow, no GPU).</summary>
        public bool Drei => _drei != null;
        public RanchWelt3D Welt => _drei;

        private void Baue3D()
        {
            var state = _host.Store.State;
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(state["cosmetics"]));
            var pferdId = RanchHof.ReitPferd(state);
            var (fell, maehne) = RanchPferdLook.Fell(RanchHof.Pferd(state, pferdId), "palomino");
            var now = _host.Store.Clock.NowMs();
            var nacht = RanchOffline.IstNacht(RanchOffline.StundeLokal(now, _host.Store.Clock.UtcOffsetMinutes(now)));
            _drei = new RanchWelt3D(_field, outfit, nacht, "hof", fell, maehne) { Extern = true };
            RanchPferdLook.Kleide(_drei.Pferd, state, pferdId);
            if (RanchHof.IstGekauft(state)) _drei.Hofbau(RanchBauState.Lese(state), RanchBelohnungen.Wandschleifen(state));
        }

        /// <summary>The build beside the paddock: the host's on a visit, the own otherwise (null: none).</summary>
        public void SetHofbau(JObject bau) => _drei?.Hofbau(bau);

        /// <summary>Covered by another screen: the region rests (parked, no camera).</summary>
        public void SetVisible(bool on) => _drei?.SetVisible(on);

        public void Destroy()
        {
            _drei?.Destroy();
            _drei = null;
        }

        private static float NennTempo(string gang) => gang == "schritt" ? RanchWelt3D.TempoSchritt : gang == "trab" || gang == "toelt" ? RanchWelt3D.TempoTrab : gang == "galopp" ? RanchWelt3D.TempoGalopp : 0f;

        /// <summary>The 3D follows the round: the own pose (the controller's heading turns the other way round), the friends, the next gate, the catcher.</summary>
        private void TickDrei(double dt)
        {
            var lauf = Link?.Lauf;
            if (lauf != null && Link.Client.RoomId == Link.RunRoom)
            {
                var c = lauf.Controller;
                _drei.Ueberblick(null);
                _drei.SetzePose(c.Pos.X, c.Pos.Y, (float)-c.Heading, (float)c.Tempo, c.Gait, (float)c.Y);
                var now = _now();
                var codes = new HashSet<string>();
                foreach (var r in lauf.Remotes.Values)
                {
                    var pose = r.Interp.SampleAt(now);
                    if (pose == null) continue;
                    codes.Add(r.Code);
                    var (fell, maehne) = FellVon(r);
                    var gang = RmpKurse.GaitName(pose.Value.Gait);
                    _drei.Mitreiten(r.Code, pose.Value.Pos.X, pose.Value.Pos.Y, (float)-pose.Value.Yaw, gang, NennTempo(gang), (float)pose.Value.Y, fell, maehne);
                    KleideMitreiter(r.Code);
                }
                _drei.BehalteMitreiter(codes);
                foreach (var code in _angezogen.Keys.Where(c => !codes.Contains(c)).ToList()) _angezogen.Remove(code);
                if (lauf.IsRace) _drei.NaechstesTor(lauf.InZiel ? -1 : lauf.NextCheckpoint);
                var it = Link.Client.ItCode ?? "";
                var eigener = it.Length > 0 && it == Link.Client.MyCode;
                _drei.Faenger(eigener ? "" : it, eigener);
            }
            else
            {
                var (center, half) = _frei ? (RmpFrei.HofMitte, new V2(45f, 45f)) : RmpKurse.Feld(_kurs);
                _drei.Ueberblick(new V2d(center.X, center.Y), Mathf.Max(half.X, half.Y));
            }
            _drei.Tick((float)dt);
        }

        /// <summary>The horse a friend sent (PFERD), null until it arrives.</summary>
        private JObject Look(string code) => Link?.Client.PeerHorses.TryGetValue(code ?? "", out var look) == true ? look : null;

        /// <summary>A friend's coat: their own horse's, by start place until its look arrives.</summary>
        private (Color fell, Color maehne) FellVon(RmpLauf.Remote r)
        {
            var look = Look(r.Code);
            return look != null ? RanchPferdLook.Fell(look) : RcompRideView.FellVon(Felle[r.Platz % Felle.Length]);
        }

        private readonly Dictionary<string, JObject> _angezogen = new Dictionary<string, JObject>();

        /// <summary>A friend's 3D horse in their coat, gear and fluff, once per look that came in.</summary>
        private void KleideMitreiter(string code)
        {
            var look = Look(code);
            if (look == null || _angezogen.TryGetValue(code, out var schon) && ReferenceEquals(schon, look)) return;
            var pferd = _drei.MitreiterPferd(code);
            if (pferd == null) return;
            var (fell, maehne) = RanchPferdLook.Fell(look);
            pferd.SetCoat(fell, maehne);
            foreach (var slot in RanchHof.GearSlots) pferd.SetGear(slot, RmpPferdLook.Gear(look, slot));
            pferd.SetFlausch(RanchBelohnungen.TraegtLook(look, RanchBelohnungen.Flausch));
            _angezogen[code] = look;
            Debug.Log("[rmp] dressed " + code + " " + J.Str(look["name"]) + " coat=" + RanchBelohnungen.FellId(look)
                + " gear=" + string.Join("/", RanchHof.GearSlots.Select(s => RmpPferdLook.Gear(look, s) ?? "-")));
        }

        /// <summary>A world point (region metres, a height over the ground or over a rider) on the field; false off screen.</summary>
        private bool Bildschirm(V2 w, double hoehe, string reiter, out Vector2 at)
        {
            if (_drei == null)
            {
                at = Px(w) - new Vector2(0f, M(hoehe));
                return true;
            }
            if (reiter != null && _drei.ReiterPunkt(reiter == "@" ? null : reiter, out var lokal))
                at = _drei.ToField(lokal.x, lokal.y + hoehe, -lokal.z, W, H);
            else at = _drei.ToField(w.X, RanchGelaende.ReitHoehe(w.X, w.Y) + hoehe, w.Y, W, H);
            return at.x > -9000f && at.x > 0f && at.x < W && at.y > 0f && at.y < H;
        }

        public void Bind(RmpLink link)
        {
            Link = link;
            _snap = true;
            ReleaseStick();
        }

        /// <summary>
        /// The free ride (Besuch, Ausritt): the yard ring in front of the farm — in 3D out in the region, painted: a meadow
        /// round the yard with a ring of trees, groves, single trees, flowering bushes and flower patches.
        /// </summary>
        public void ShowFrei()
        {
            if (_frei) return;
            _frei = true;
            _kurs = "";
            _snap = true;
            _drei?.ZeigeKurs(null);
            _drei?.ZeigeHofRing(true);
            if (_drei != null) return;
            var rng = new Gooby.Core.Random.GoobyRng(4711);
            var trees = new List<(V2, float)>();
            var center = RmpFrei.HofMitte;
            var half = FreiHalb;
            var umfang = 2.0 * (half.X + half.Y);
            var anzahl = (int)Math.Min(110, umfang / 9.0);
            for (var i = 0; i < anzahl; i++)
            {
                var t = (double)i / anzahl + rng.Next() * 0.4 / anzahl;
                trees.Add((Rand(center, half, t, 6.0 + rng.Next() * 10.0), (float)(0.8 + rng.Next() * 0.7)));
            }
            V2 Irgendwo() => new V2(center.X + (rng.Next() * 2.0 - 1.0) * half.X * 0.94, center.Y + (rng.Next() * 2.0 - 1.0) * half.Y * 0.94);
            for (int i = 0, haine = 0; i < 600 && haine < 30; i++)
            {
                var mitte = Irgendwo();
                if (!FreiePflanzstelle(mitte, 9.0)) continue;
                haine++;
                var n = 3 + (int)(rng.Next() * 4.0);
                for (var k = 0; k < n; k++)
                {
                    var pos = mitte + new V2((rng.Next() * 2.0 - 1.0) * 7.5, (rng.Next() * 2.0 - 1.0) * 7.5);
                    if (FreiePflanzstelle(pos, 4.0)) trees.Add((pos, (float)(0.6 + rng.Next() * 0.6)));
                }
            }
            for (int i = 0, einzeln = 0; i < 600 && einzeln < 60; i++)
            {
                var pos = Irgendwo();
                if (!FreiePflanzstelle(pos, 5.0)) continue;
                einzeln++;
                trees.Add((pos, (float)(0.55 + rng.Next() * 0.5)));
            }
            _trees = trees.ToArray();
            var bushes = new List<(V2, float, int)>();
            for (var i = 0; i < 1200 && bushes.Count < 120; i++)
            {
                var pos = Irgendwo();
                if (FreiePflanzstelle(pos, 2.5)) bushes.Add((pos, (float)(0.7 + rng.Next() * 0.6), rng.Next() < 0.45 ? (int)(rng.Next() * Bluete.Length) : -1));
            }
            _bushes = bushes.ToArray();
            var blumen = new List<V2>();
            for (var i = 0; i < 1200 && blumen.Count < 90; i++)
            {
                var pos = Irgendwo();
                if (FreiePflanzstelle(pos, 2.0)) blumen.Add(pos);
            }
            _blumen = blumen.ToArray();
        }

        private (V2 pos, float scale, int bluete)[] _bushes = new (V2, float, int)[0];
        private V2[] _blumen = new V2[0];
        private static readonly Color BushDark = MgScenery.Hex(0x4E8F45);
        private static readonly Color[] Bluete = { MgScenery.Hex(0xF2A7B8), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFFD34D), MgScenery.Hex(0xC9A4E8) };

        /// <summary>The painted free ride's meadow round the yard (half size).</summary>
        private static readonly V2 FreiHalb = new V2(220f, 180f);

        private static bool FreiePflanzstelle(V2 pos, double abstand) => (pos - RmpFrei.HofMitte).Length > RmpFrei.HofRingRadius + 14.0 + abstand;

        /// <summary>"Folge mir" of a rider on/off: a gold flag rides over the horse.</summary>
        public void SetFolgeMir(string code, bool an)
        {
            if (an) _folgeMir.Add(code ?? "");
            else _folgeMir.Remove(code ?? "");
        }

        /// <summary>A heart floats up over a rider for two seconds (HERZ; the own code for the own heart).</summary>
        public void Herz(string code) => _herzen[code ?? ""] = 2.0;

        /// <summary>The host's horses on a visit (name, coat) — they stand around the yard ring.</summary>
        public void SetBesuchPferde(IEnumerable<(string name, string farbe)> pferde)
        {
            _besuchPferde.Clear();
            if (pferde != null) _besuchPferde.AddRange(pferde);
            _drei?.ZeigeBesuchPferde(_besuchPferde.Select(p => RcompRideView.FellVon(p.farbe ?? "braun")).ToList());
        }

        public int BesuchPferdeAnzahl => _besuchPferde.Count;
        public IEnumerable<string> FolgeMirCodes => _folgeMir;

        /// <summary>The yard horse closest to <paramref name="pos"/> within <paramref name="maxM"/> (-1: none that close).</summary>
        public int BesuchPferdBei(V2 pos, double maxM)
        {
            var best = -1;
            var bestD = maxM;
            for (var i = 0; i < _besuchPferde.Count; i++)
            {
                var d = (BesuchPferdPos(i) - pos).Length;
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public string BesuchPferdName(int i) => i >= 0 && i < _besuchPferde.Count ? _besuchPferde[i].name : "";

        public int BesuchPferdIndex(string name) => _besuchPferde.FindIndex(p => p.name == name);

        /// <summary>A heart floats up over a yard horse (stroked or fed on a visit).</summary>
        public void HerzPferd(int i)
        {
            if (i >= 0 && i < _besuchPferde.Count) _herzen["pferd:" + i] = 2.0;
        }

        /// <summary>Which course the meadow shows (also in the lobby, before any run exists).</summary>
        public void ShowKurs(string kursId)
        {
            if (kursId == _kurs && !_frei) return;
            _frei = false;
            _kurs = kursId ?? "";
            _snap = true;
            _drei?.ZeigeHofRing(false);
            _drei?.ZeigeKurs(_kurs);
            var rng = new Gooby.Core.Random.GoobyRng(_kurs.Length * 7919 + 13);
            var (center, half) = RmpKurse.Feld(_kurs);
            var trees = new List<(V2, float)>();
            // a ring of trees around the meadow, a few groves inside clear of the course
            var umfang = 2.0 * (half.X + half.Y);
            var anzahl = (int)Math.Min(90, Math.Max(24, umfang / 9.0));
            for (var i = 0; i < anzahl; i++)
            {
                var t = (double)i / anzahl + rng.Next() * 0.4 / anzahl;
                var pos = Rand(center, half, t, 6.0 + rng.Next() * 10.0);
                trees.Add((pos, (float)(0.8 + rng.Next() * 0.7)));
            }
            var k = RmpKurse.Get(_kurs);
            for (var i = 0; i < 140 && trees.Count < anzahl + 26; i++)
            {
                var pos = new V2(center.X + (rng.Next() * 2.0 - 1.0) * half.X * 0.95, center.Y + (rng.Next() * 2.0 - 1.0) * half.Y * 0.95);
                if (!FreiVomKurs(k, pos)) continue;
                trees.Add((pos, (float)(0.55 + rng.Next() * 0.5)));
            }
            _trees = trees.ToArray();
        }

        private static V2 Rand(V2 center, V2 half, double t, double aussen)
        {
            // walk the meadow's rectangle edge (t 0..1), then step outward
            var w = half.X * 2.0; var h = half.Y * 2.0;
            var d = t * 2.0 * (w + h);
            double x, z;
            if (d < w) { x = -half.X + d; z = -half.Y - aussen; }
            else if (d < w + h) { x = half.X + aussen; z = -half.Y + (d - w); }
            else if (d < 2 * w + h) { x = half.X - (d - w - h); z = half.Y + aussen; }
            else { x = -half.X - aussen; z = half.Y - (d - 2 * w - h); }
            return new V2(center.X + x, center.Y + z);
        }

        private static bool FreiVomKurs(RmpKurse.Kurs k, V2 pos)
        {
            if (k == null) return true;
            if (k.Checkpoints.Length == 0) return (pos - k.ArenaCenter).Length > k.ArenaRadius + 8.0;
            for (var i = 0; i < k.Checkpoints.Length; i++)
            {
                if ((pos - k.Checkpoints[i]).Length < k.Radius + 10.0) return false;
                var a = k.Checkpoints[i];
                var b = k.Checkpoints[(i + 1) % k.Checkpoints.Length];
                if (DistToSegment(pos, a, b) < 12.0) return false;
            }
            return true;
        }

        private static double DistToSegment(V2 p, V2 a, V2 b)
        {
            var ab = b - a;
            var len2 = ab.LengthSquared;
            var t = len2 <= 0f ? 0.0 : Math.Max(0.0, Math.Min(1.0, (p - a).Dot(ab) / len2));
            return (p - (a + ab * t)).Length;
        }

        public void Tick(double dt)
        {
            _anim += dt;
            if (_herzen.Count > 0)
                foreach (var code in new List<string>(_herzen.Keys))
                    if ((_herzen[code] -= dt) <= 0.0) _herzen.Remove(code);
            var lauf = Link?.Lauf;
            if (lauf != null && Link.Client.RoomId == Link.RunRoom)
            {
                var c = lauf.Controller;
                if (_snap) _camH = c.Heading;
                else _camH += RideFeel.WrapAngle(c.Heading - _camH) * RideFeel.CamFollowFactor(dt) * 1.6;
                _cam = c.Pos;
                // the free ride looks a little further: the yard with the host's horses ahead at the start, more Weidetal
                var ziel = (_frei ? 40.0 : 30.0) + 16.0 * Math.Min(1.0, c.Tempo / 14.5);
                _span = _snap ? ziel : _span + (ziel - _span) * RideFeel.CamFollowFactor(dt);
                if (Link.Running && !Link.Bot) c.SteerInput(_steer);
            }
            else
            {
                // lobby / result: the whole course from above, north up (after the first layout)
                if (W <= 1f || H <= 1f) return;
                var (center, half) = _frei ? (RmpFrei.HofMitte, new V2(60f, 50f)) : RmpKurse.Feld(_kurs);
                _cam = center;
                _camH = 0.0;
                var ziel = Math.Max(half.X * 2.0 * H / Math.Max(1f, W), half.Y * 2.0) * 1.05;
                _span = _snap ? ziel : _span + (ziel - _span) * RideFeel.CamFollowFactor(dt);
            }
            _snap = false;
            if (_drei != null) TickDrei(dt);
        }

        public void Render(bool controls)
        {
            Controls.style.display = controls ? DisplayStyle.Flex : DisplayStyle.None;
            if (!controls) ReleaseStick();
            if (_drei == null) Canvas.MarkDirtyRepaint();
            _stickCanvas.MarkDirtyRepaint();
            PlaceTags();
        }

        // ── camera ──

        // before the first layout contentRect is NaN — a NaN zoom would stick forever
        private float W => float.IsNaN(_field.contentRect.width) ? 0f : _field.contentRect.width;
        private float H => float.IsNaN(_field.contentRect.height) ? 0f : _field.contentRect.height;
        private float Ppm => _span > 0.0 && H > 1f ? Mathf.Max(1.5f, H / (float)_span) : 1.5f;
        private bool Riding => Link?.Lauf != null && Link.Client.RoomId == Link.RunRoom;
        private Vector2 Anchor => Riding ? new Vector2(W * 0.5f, H * 0.62f) : new Vector2(W * 0.5f, H * 0.5f);

        public Vector2 Px(V2 w)
        {
            var dx = w.X - _cam.X;
            var dz = w.Y - _cam.Y;
            var sin = Math.Sin(_camH);
            var cos = Math.Cos(_camH);
            var side = dx * cos - dz * sin;
            var fwd = -dx * sin - dz * cos;
            var a = Anchor;
            return new Vector2(a.x + (float)side * Ppm, a.y - (float)fwd * Ppm);
        }

        public float M(double metres) => (float)metres * Ppm;
        public float ScreenAngle(double heading) => (float)Math.Atan2(-Math.Cos(_camH - heading), Math.Sin(_camH - heading));
        private float ViewRadiusM => Mathf.Sqrt(W * W + H * H) / Ppm * 0.75f + 4f;
        public bool Near(V2 w, double margin) => (w - _cam).Length < ViewRadiusM + margin;
        public double Anim => _anim;

        // ── painting ──

        private void Paint(Painter2D p)
        {
            if (W <= 1f || _drei != null) return;
            P.Rect(p, Vector2.zero, new Vector2(W, H), Grass);
            var r = ViewRadiusM;
            const double band = 10.0;
            var x0 = Math.Floor((_cam.X - r) / band) * band;
            for (var x = x0; x < _cam.X + r; x += band)
                if (((int)Math.Floor(x / band) & 1) == 0) Quad(p, x, _cam.Y - r, x + band * 0.5, _cam.Y + r, GrassLight);
            if (_frei)
            {
                PaintWeidetalGround(p);
                PaintTufts(p, null);
                PaintBlumen(p);
                PaintWeidetalBuilt(p);
                PaintBushes(p);
                PaintTrees(p);
                PaintBesuchPferde(p);
                PaintRiders(p);
                PaintEdgeMarkers(p);
                return;
            }
            var k = RmpKurse.Get(_kurs);
            if (k != null && k.Checkpoints.Length == 0) PaintPaddockFloor(p, k);
            PaintTufts(p, k);
            if (k != null && k.Checkpoints.Length > 0) PaintCourse(p, k);
            else if (k != null) PaintPaddock(p, k);
            PaintTrees(p);
            PaintRiders(p);
            PaintEdgeMarkers(p);
        }

        // ── the Weidetal of the free ride ──

        private Vector2[] EllipseW(V2 c, double rx, double rz, int n = 36)
        {
            var pts = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                var a = 2.0 * Math.PI * i / n;
                pts[i] = Px(new V2(c.X + Math.Cos(a) * rx, c.Y + Math.Sin(a) * rz));
            }
            return pts;
        }

        private void EllipseRingW(Painter2D p, V2 c, double rx, double rz, float width, Color color, int n = 40)
        {
            var pts = EllipseW(c, rx, rz, n);
            for (var i = 0; i < n; i++) P.Line(p, pts[i], pts[(i + 1) % n], width, color);
        }

        private void PaintBushes(Painter2D p)
        {
            foreach (var (pos, scale, bluete) in _bushes)
            {
                if (!Near(pos, 4.0)) continue;
                var c = Px(pos);
                var r = M(1.25 * scale);
                P.Circle(p, c + new Vector2(M(0.5), M(0.6)), r * 1.05f, RidePaint.Shadow);
                P.Circle(p, c, r + M(0.14), RidePaint.Outline);
                P.Circle(p, c, r, BushDark);
                P.Circle(p, c + new Vector2(-r * 0.28f, -r * 0.3f), r * 0.55f, Leaf);
                if (bluete < 0) continue;
                for (var j = 0; j < 5; j++)
                {
                    var a = j * 1.26f + scale * 3f;
                    P.Circle(p, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.58f, M(0.26), Bluete[bluete]);
                }
            }
        }

        /// <summary>Little flower patches on the open meadow (white, rose, gold).</summary>
        private void PaintBlumen(Painter2D p)
        {
            if (M(0.3) < 1.2f) return;
            for (var i = 0; i < _blumen.Length; i++)
            {
                var w = _blumen[i];
                if (!Near(w, 4.0)) continue;
                for (var k = 0; k < 9; k++)
                {
                    var a = k * 2.4 + i;
                    var r = 0.5 + (k % 3) * 0.75;
                    var pt = Px(w + new V2(Math.Cos(a) * r, Math.Sin(a) * r));
                    var f = (k + i) % 3;
                    P.Circle(p, pt, M(0.3), f == 0 ? Rose : f == 1 ? Color.white : Gold);
                    P.Circle(p, pt, M(0.1), f == 2 ? Color.white : Gold);
                }
            }
        }

        /// <summary>The yard ring's sand.</summary>
        private void PaintWeidetalGround(Painter2D p)
        {
            var hof = RmpFrei.HofMitte;
            if (!Near(hof, RmpFrei.HofRingRadius + 12.0)) return;
            P.Circle(p, Px(hof), M(RmpFrei.HofRingRadius + 1.2), SandDark);
            P.Circle(p, Px(hof), M(RmpFrei.HofRingRadius), Sand);
            P.Ring(p, Px(hof), M(RmpFrei.HofRingRadius * 0.55), M(0.3), new Color(1f, 1f, 1f, 0.35f));
        }

        /// <summary>The yard's fence and the stable north of the ring.</summary>
        private void PaintWeidetalBuilt(Painter2D p)
        {
            var hof = RmpFrei.HofMitte;
            if (!Near(hof, RmpFrei.HofRingRadius + 28.0)) return;
            Fence(p, hof, RmpFrei.HofRingRadius + 0.6, 0.5);
            var s = hof + new V2(0f, -(float)RmpFrei.HofRingRadius - 9.0f);
            var corners = new[] { s + new V2(-11f, -5f), s + new V2(11f, -5f), s + new V2(11f, 5f), s + new V2(-11f, 5f) };
            P.Poly(p, new[] { Px(corners[0] + new V2(0.8f, -1.2f)), Px(corners[1] + new V2(0.8f, -1.2f)), Px(corners[2] + new V2(0.8f, -1.2f)), Px(corners[3] + new V2(0.8f, -1.2f)) }, RidePaint.Shadow);
            P.Poly(p, new[] { Px(corners[0]), Px(corners[1]), Px(corners[2]), Px(corners[3]) }, RidePaint.Outline);
            var inner = new[] { s + new V2(-10.4f, -4.4f), s + new V2(10.4f, -4.4f), s + new V2(10.4f, 4.4f), s + new V2(-10.4f, 4.4f) };
            P.Poly(p, new[] { Px(inner[0]), Px(inner[1]), Px(inner[2]), Px(inner[3]) }, Roof);
            for (var i = -3; i <= 3; i++) P.Line(p, Px(s + new V2(i * 3f, -4.4f)), Px(s + new V2(i * 3f, 4.4f)), M(0.25), new Color(0.55f, 0.25f, 0.22f, 0.45f));
            P.Line(p, Px(s + new V2(-10.4f, 0f)), Px(s + new V2(10.4f, 0f)), M(0.6), Wall);
            P.Line(p, Px(s + new V2(-1.6f, 4.9f)), Px(s + new V2(1.6f, 4.9f)), M(1.0), WoodDark);
        }

        private void Fence(Painter2D p, V2 center, double radius, double postR)
        {
            var c = Px(center);
            P.Ring(p, c, M(radius), M(0.55), WoodDark);
            P.Ring(p, c, M(radius), M(0.3), Wood);
            var posts = (int)Math.Max(16, radius * 2.0 * Math.PI / 5.0);
            for (var i = 0; i < posts; i++)
            {
                var a = 2.0 * Math.PI * i / posts;
                var pt = Px(center + new V2(Math.Cos(a), Math.Sin(a)) * radius);
                P.Circle(p, pt, M(postR + 0.08), RidePaint.Outline);
                P.Circle(p, pt, M(postR), Wood);
            }
        }

        /// <summary>A visit: the host's horses stand around the yard ring, heads to the middle.</summary>
        private void PaintBesuchPferde(Painter2D p)
        {
            var hof = RmpFrei.HofMitte;
            if (_besuchPferde.Count == 0 || !Near(hof, RmpFrei.HofRingRadius + 10.0)) return;
            for (var i = 0; i < _besuchPferde.Count; i++)
            {
                var pos = BesuchPferdPos(i);
                var heading = RmpKurse.HeadingTo(pos, hof) + Math.Sin(_anim * 0.4 + i) * 0.15;
                var fell = RcompRideView.Fell.TryGetValue(_besuchPferde[i].farbe ?? "", out var f) ? f : RcompRideView.Fell["braun"];
                RidePaint.Horse(p, this, pos, heading, 0.0, fell, "stand", 1f, false);
                RiderSigns(p, "pferd:" + i, pos);
            }
        }

        private V2 BesuchPferdPos(int i) => RmpFrei.BesuchPferdPos(i, _besuchPferde.Count);

        private void Quad(Painter2D p, double x0, double z0, double x1, double z1, Color color) =>
            P.Poly(p, new[] { Px(new V2(x0, z0)), Px(new V2(x1, z0)), Px(new V2(x1, z1)), Px(new V2(x0, z1)) }, color);

        private void PaintTufts(Painter2D p, RmpKurse.Kurs k)
        {
            var r = ViewRadiusM;
            const double cell = 4.5;
            var cx0 = (int)Math.Floor((_cam.X - r) / cell);
            var cz0 = (int)Math.Floor((_cam.Y - r) / cell);
            var n = Math.Min(44, (int)Math.Ceiling(2.0 * r / cell));
            if (M(0.3) < 1.2f) return;
            for (var i = 0; i <= n; i++)
            for (var j = 0; j <= n; j++)
            {
                var cx = cx0 + i;
                var cz = cz0 + j;
                var h = (uint)(cx * 73856093) ^ (uint)(cz * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                var w = new V2((cx + (h & 0xFF) / 255.0) * cell, (cz + ((h >> 8) & 0xFF) / 255.0) * cell);
                if (OnTrack(k, w) || _frei && FreiBoden(w)) continue;
                var a = Px(w);
                if ((h >> 16) % 11 == 0) P.Circle(p, a, M(0.14), (h >> 20) % 3 == 0 ? Rose : (h >> 20) % 3 == 1 ? Color.white : Gold);
                else
                {
                    P.Line(p, a, a + new Vector2(M(0.06), -M(0.3)), M(0.07), GrassDark);
                    P.Line(p, a + new Vector2(M(0.14), 0f), a + new Vector2(M(0.25), -M(0.26)), M(0.07), GrassDark);
                }
            }
        }

        private void PaintCourse(Painter2D p, RmpKurse.Kurs k)
        {
            var cps = k.Checkpoints;
            var loop = k.Mode == RmpKurse.Rennen;
            var segments = loop ? cps.Length : cps.Length - 1;
            // the sand track first, under the gates
            for (var i = 0; i < segments; i++)
            {
                var a = cps[i];
                var b = cps[(i + 1) % cps.Length];
                P.Line(p, Px(a), Px(b), M(9.0), SandDark);
            }
            for (var i = 0; i < cps.Length; i++) P.Circle(p, Px(cps[i]), M(4.4), SandDark);
            for (var i = 0; i < segments; i++)
            {
                var a = cps[i];
                var b = cps[(i + 1) % cps.Length];
                P.Line(p, Px(a), Px(b), M(7.6), Sand);
            }
            for (var i = 0; i < cps.Length; i++) P.Circle(p, Px(cps[i]), M(3.7), Sand);
            var lauf = Link?.Lauf;
            var next = lauf != null && Riding ? lauf.NextCheckpoint : -1;
            var puls = 0.5f + 0.5f * Mathf.Sin((float)_anim * 5f);
            for (var i = 0; i < cps.Length; i++)
            {
                var c = Px(cps[i]);
                var passed = next > i || lauf != null && lauf.InZiel;
                var isNext = i == next && !(lauf?.InZiel ?? false);
                var ringColor = isNext ? Color.Lerp(Gold, Color.white, puls * 0.35f) : passed ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.75f);
                P.Ring(p, c, M(k.Radius), isNext ? M(0.9) : M(0.45), ringColor);
                if (isNext) P.Circle(p, c, M(k.Radius), new Color(Gold.r, Gold.g, Gold.b, 0.12f + 0.08f * puls));
                // the gate: two poles across the line through it, the flag with the number on the right one
                var dir = (cps[Math.Min(cps.Length - 1, i + 1)] - cps[Math.Max(0, i - 1)]).Normalized();
                var quer = new V2(-dir.Y, dir.X);
                var halb = (float)Math.Min(k.Radius * 0.55, 6.0);
                var l = Px(cps[i] + quer * halb);
                var rr = Px(cps[i] - quer * halb);
                var pole = passed ? new Color(0.85f, 0.8f, 0.72f) : Rail;
                P.Line(p, l, rr, M(0.18), new Color(1f, 1f, 1f, passed ? 0.25f : 0.55f));
                foreach (var pt in new[] { l, rr })
                {
                    P.Circle(p, pt + new Vector2(M(0.15), M(0.2)), M(0.5), RidePaint.Shadow);
                    P.Circle(p, pt, M(0.45), RidePaint.Outline);
                    P.Circle(p, pt, M(0.34), pole);
                }
                var flag = isNext ? Gold : passed ? new Color(0.8f, 0.78f, 0.74f) : (i == cps.Length - 1 ? Berry : MgScenery.Hex(0x5C8CC7));
                P.RoundRect(p, l + new Vector2(-M(1.1), -M(2.6)), l + new Vector2(M(1.1), -M(0.9)), M(0.35), RidePaint.Outline);
                P.RoundRect(p, l + new Vector2(-M(0.95), -M(2.45)), l + new Vector2(M(0.95), -M(1.05)), M(0.3), flag);
                PaintDigit(p, l + new Vector2(0f, -M(1.75)), i + 1, M(1.0), passed ? RidePaint.Outline : Color.white);
            }
        }

        /// <summary>A small seven-segment number on the gate flag (Painter2D has no text).</summary>
        private static void PaintDigit(Painter2D p, Vector2 c, int n, float h, Color color)
        {
            var s = n.ToString();
            var w = h * 0.5f;
            var gap = h * 0.18f;
            var total = s.Length * w + (s.Length - 1) * gap;
            var x = c.x - total * 0.5f;
            foreach (var ch in s)
            {
                var d = ch - '0';
                var segs = Segs[d];
                var t = h * 0.14f;
                Vector2 A(float fx, float fy) => new Vector2(x + fx * w, c.y - h * 0.5f + fy * h);
                if (segs[0]) P.Line(p, A(0f, 0f), A(1f, 0f), t, color);
                if (segs[1]) P.Line(p, A(1f, 0f), A(1f, 0.5f), t, color);
                if (segs[2]) P.Line(p, A(1f, 0.5f), A(1f, 1f), t, color);
                if (segs[3]) P.Line(p, A(0f, 1f), A(1f, 1f), t, color);
                if (segs[4]) P.Line(p, A(0f, 0.5f), A(0f, 1f), t, color);
                if (segs[5]) P.Line(p, A(0f, 0f), A(0f, 0.5f), t, color);
                if (segs[6]) P.Line(p, A(0f, 0.5f), A(1f, 0.5f), t, color);
                x += w + gap;
            }
        }

        private static readonly bool[][] Segs =
        {
            new[] { true, true, true, true, true, true, false }, new[] { false, true, true, false, false, false, false },
            new[] { true, true, false, true, true, false, true }, new[] { true, true, true, true, false, false, true },
            new[] { false, true, true, false, false, true, true }, new[] { true, false, true, true, false, true, true },
            new[] { true, false, true, true, true, true, true }, new[] { true, true, true, false, false, false, false },
            new[] { true, true, true, true, true, true, true }, new[] { true, true, true, true, false, true, true },
        };

        /// <summary>The yard ring's sand carries no grass tufts.</summary>
        private static bool FreiBoden(V2 w) => (w - RmpFrei.HofMitte).Length < RmpFrei.HofRingRadius + 1.5;

        /// <summary>No grass tufts on the sand track (gates and the lines between them).</summary>
        private static bool OnTrack(RmpKurse.Kurs k, V2 w)
        {
            if (k == null || k.Checkpoints.Length == 0) return false;
            var cps = k.Checkpoints;
            var segments = k.Mode == RmpKurse.Rennen ? cps.Length : cps.Length - 1;
            for (var i = 0; i < segments; i++)
                if (DistToSegment(w, cps[i], cps[(i + 1) % cps.Length]) < 4.8) return true;
            return false;
        }

        private void PaintPaddockFloor(Painter2D p, RmpKurse.Kurs k)
        {
            var c = Px(k.ArenaCenter);
            P.Circle(p, c, M(k.ArenaRadius + 2.0), GrassDark);
            P.Circle(p, c, M(k.ArenaRadius), MgScenery.Hex(0x9ACF73));
            P.Ring(p, c, M(k.ArenaRadius * 0.5), M(0.35), new Color(1f, 1f, 1f, 0.35f));
        }

        private void PaintPaddock(Painter2D p, RmpKurse.Kurs k)
        {
            var c = Px(k.ArenaCenter);
            // a log fence on posts around the paddock
            P.Ring(p, c, M(k.ArenaRadius + 0.6), M(0.55), WoodDark);
            P.Ring(p, c, M(k.ArenaRadius + 0.6), M(0.3), Wood);
            var posts = (int)Math.Max(24, k.ArenaRadius * 2.0 * Math.PI / 6.0);
            for (var i = 0; i < posts; i++)
            {
                var a = 2.0 * Math.PI * i / posts;
                var w = k.ArenaCenter + new V2(Math.Cos(a), Math.Sin(a)) * (k.ArenaRadius + 0.6);
                if (!Near(w, 2.0)) continue;
                var pt = Px(w);
                P.Circle(p, pt + new Vector2(M(0.12), M(0.16)), M(0.42), RidePaint.Shadow);
                P.Circle(p, pt, M(0.4), RidePaint.Outline);
                P.Circle(p, pt, M(0.3), Wood);
            }
        }

        private void PaintTrees(Painter2D p)
        {
            foreach (var (pos, scale) in _trees)
            {
                if (!Near(pos, 6.0)) continue;
                var c = Px(pos);
                var r = M(2.4 * scale);
                P.Circle(p, c + new Vector2(M(0.8), M(1.0)), r * 1.05f, RidePaint.Shadow);
                P.Circle(p, c, M(0.5 * scale), Trunk);
                P.Circle(p, c, r + M(0.18), RidePaint.Outline);
                P.Circle(p, c, r, Leaf);
                P.Circle(p, c + new Vector2(-r * 0.25f, -r * 0.28f), r * 0.62f, LeafLight);
            }
        }

        private void PaintRiders(Painter2D p)
        {
            var link = Link;
            var lauf = link?.Lauf;
            if (lauf == null || !Riding) return;
            var now = _now();
            var it = link.Client.ItCode;
            var immun = link.Client.ImmunityUntilMs > link.Client.ServerNowMs;
            foreach (var r in lauf.Remotes.Values)
            {
                var pose = r.Interp.SampleAt(now);
                if (pose == null) continue;
                var v = pose.Value;
                var fell = FellVon(r);
                var alpha = v.Stale ? 0.45f : immun && r.Code == it ? 0.6f + 0.4f * Mathf.Sin((float)_anim * 18f) : 1f;
                if (r.Code == it) CatcherRing(p, v.Pos);
                RidePaint.Horse(p, this, v.Pos, v.Yaw, v.Y, fell, RmpKurse.GaitName(v.Gait), alpha, false);
                RidePaint.Rider(p, this, v.Pos, v.Yaw, Jacken[r.Platz % Jacken.Length]);
                RiderSigns(p, r.Code, v.Pos);
            }
            var c = lauf.Controller;
            if (it == lauf.MyCode) CatcherRing(p, c.Pos);
            var state = _host.Store.State;
            RidePaint.Horse(p, this, c.Pos, c.Heading, c.Y, RanchPferdLook.Fell(RanchHof.Pferd(state, RanchHof.ReitPferd(state))), c.Gait, 1f, true);
            RiderSigns(p, lauf.MyCode, c.Pos);
        }

        /// <summary>Over a rider: the gold "Folge mir" flag and a heart floating up after HERZ.</summary>
        private void RiderSigns(Painter2D p, string code, V2 pos)
        {
            var top = Px(pos) + new Vector2(0f, -M(3.6));
            if (_folgeMir.Contains(code))
            {
                var wave = Mathf.Sin((float)_anim * 6f) * M(0.25);
                P.Line(p, top + new Vector2(0f, M(1.4)), top + new Vector2(0f, -M(1.6)), M(0.22), RidePaint.Outline);
                var flag = new[] { top + new Vector2(0f, -M(1.6)), top + new Vector2(M(2.2), -M(1.1) + wave), top + new Vector2(0f, -M(0.5)) };
                P.Poly(p, new[] { flag[0] + new Vector2(-M(0.12), -M(0.12)), flag[1] + new Vector2(M(0.2), 0f), flag[2] + new Vector2(-M(0.12), M(0.12)) }, RidePaint.Outline);
                P.Poly(p, flag, Gold);
            }
            if (_herzen.TryGetValue(code, out var left))
            {
                var t = (float)(2.0 - left);
                var h = top + new Vector2(Mathf.Sin(t * 5f) * M(0.4), -M(1.4) - t * M(2.6));
                var s = M(0.9) * (0.8f + 0.25f * Mathf.Sin(t * 9f));
                var a = Mathf.Clamp01((float)left * 1.2f);
                var col = new Color(Berry.r, Berry.g, Berry.b, a);
                P.Circle(p, h + new Vector2(-s * 0.5f, 0f), s * 0.62f, col);
                P.Circle(p, h + new Vector2(s * 0.5f, 0f), s * 0.62f, col);
                P.Poly(p, new[] { h + new Vector2(-s * 1.08f, s * 0.15f), h + new Vector2(s * 1.08f, s * 0.15f), h + new Vector2(0f, s * 1.3f) }, col);
            }
        }

        /// <summary>Riders outside the view: a disc in their jacket colour at the screen edge, pointing their way.</summary>
        private void PaintEdgeMarkers(Painter2D p)
        {
            var lauf = Link?.Lauf;
            if (lauf == null || !Riding || W <= 1f) return;
            var now = _now();
            foreach (var r in lauf.Remotes.Values)
            {
                var pose = r.Interp.SampleAt(now);
                if (pose == null) continue;
                var sp = Px(pose.Value.Pos);
                if (!EdgePoint(sp, out var edge, out var dir)) continue;
                var catcher = r.Code == Link.Client.ItCode;
                var fill = catcher ? Berry : Jacken[r.Platz % Jacken.Length];
                P.Circle(p, edge + new Vector2(2f, 3f), 21f, RidePaint.Shadow);
                P.Circle(p, edge, 20f, RidePaint.Outline);
                P.Circle(p, edge, 17f, fill);
                var side = new Vector2(-dir.y, dir.x);
                var tip = edge + dir * 30f;
                P.Poly(p, new[] { tip, edge + dir * 17f + side * 9f, edge + dir * 17f - side * 9f }, RidePaint.Outline);
                P.Poly(p, new[] { tip - dir * 3f, edge + dir * 18f + side * 6f, edge + dir * 18f - side * 6f }, fill);
                RidePaint.Gooby(p, edge, -Mathf.PI * 0.5f, 22f, 0f);
            }
        }

        private const float EdgeInset = 44f;

        /// <summary>True when <paramref name="sp"/> lies off screen: the point on the inset screen edge toward it and the direction.</summary>
        private bool EdgePoint(Vector2 sp, out Vector2 edge, out Vector2 dir)
        {
            edge = sp;
            dir = Vector2.up;
            if (sp.x >= EdgeInset && sp.x <= W - EdgeInset && sp.y >= EdgeInset && sp.y <= H - EdgeInset) return false;
            var a = Anchor;
            var d = sp - a;
            if (d.sqrMagnitude < 1f) return false;
            dir = d.normalized;
            var tx = dir.x > 0f ? (W - EdgeInset - a.x) / dir.x : dir.x < 0f ? (EdgeInset - a.x) / dir.x : float.MaxValue;
            var ty = dir.y > 0f ? (H - EdgeInset - a.y) / dir.y : dir.y < 0f ? (EdgeInset - a.y) / dir.y : float.MaxValue;
            edge = a + dir * Mathf.Min(tx, ty);
            return true;
        }

        private void CatcherRing(Painter2D p, V2 pos)
        {
            var k = RmpKurse.Get(_kurs);
            var reach = (k?.TagRadius ?? 3.0) + RmpLauf.TagSpielraumM;
            P.Circle(p, Px(pos), M(reach), new Color(Berry.r, Berry.g, Berry.b, 0.16f));
            P.Ring(p, Px(pos), M(reach), M(0.25), new Color(Berry.r, Berry.g, Berry.b, 0.8f));
        }

        /// <summary>Name tags over the friends' horses (labels follow the painted riders).</summary>
        private void PlaceTags()
        {
            var lauf = Link?.Lauf;
            var seen = new HashSet<string>();
            if (lauf != null && Riding && W > 1f)
            {
                var now = _now();
                foreach (var r in lauf.Remotes.Values)
                {
                    var pose = r.Interp.SampleAt(now);
                    if (pose == null) continue;
                    seen.Add(r.Code);
                    if (!_tags.TryGetValue(r.Code, out var tag))
                    {
                        tag = Ux.Text("", "gb-bold", "gb-nowrap");
                        tag.name = "rmp-tag-" + r.Code;
                        tag.pickingMode = PickingMode.Ignore;
                        tag.style.position = Position.Absolute;
                        tag.style.backgroundColor = new Color(1f, 1f, 1f, 0.92f);
                        tag.style.color = RidePaint.Outline;
                        tag.style.paddingLeft = 10; tag.style.paddingRight = 10; tag.style.paddingTop = 2; tag.style.paddingBottom = 2;
                        tag.style.borderTopLeftRadius = 12; tag.style.borderTopRightRadius = 12; tag.style.borderBottomLeftRadius = 12; tag.style.borderBottomRightRadius = 12;
                        tag.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
                        tag.style.fontSize = 18;
                        Tags.Add(tag);
                        _tags[r.Code] = tag;
                    }
                    var pferd = J.Str(Look(r.Code)?["name"]);
                    var wer = pferd.Length > 0 ? _host.Strings.T("nl.rmp.reiter_auf", ("name", r.Name), ("pferd", pferd)) : r.Name;
                    var text = (r.Code == Link.Client.ItCode ? "! " : "") + (_herzen.ContainsKey(r.Code) ? "\u2665 " : "") + wer + (pose.Value.Stale ? " …" : "");
                    if (tag.text != text) tag.text = text;
                    tag.style.backgroundColor = _folgeMir.Contains(r.Code) ? new Color(1f, 0.86f, 0.42f, 0.96f) : new Color(1f, 1f, 1f, 0.92f);
                    if (_drei != null)
                    {
                        // in the region the tag hangs over the rider's head; behind the camera or off screen it hides
                        if (Bildschirm(pose.Value.Pos, 2.9, r.Code, out var oben))
                        {
                            tag.style.left = oben.x;
                            tag.style.top = oben.y;
                            tag.style.display = DisplayStyle.Flex;
                        }
                        else tag.style.display = DisplayStyle.None;
                        continue;
                    }
                    var at = Px(pose.Value.Pos);
                    if (EdgePoint(at, out var edge, out var dir))
                    {
                        // off screen: the name rides next to its edge marker, toward the middle
                        var inward = edge - dir * 46f;
                        tag.style.left = inward.x;
                        tag.style.top = inward.y + 12f;
                    }
                    else
                    {
                        tag.style.left = at.x;
                        tag.style.top = at.y - M(2.6);
                    }
                    tag.style.display = DisplayStyle.Flex;
                }
            }
            foreach (var kv in _tags)
                if (!seen.Contains(kv.Key)) kv.Value.style.display = DisplayStyle.None;
            PlaceOrtTags();
        }

        /// <summary>The own heart (HERZ sent) floats as a tag over the own rider for its two seconds.</summary>
        private void PlaceEigenesHerz(HashSet<string> shown)
        {
            var mein = Link?.Client?.MyCode ?? "";
            if (mein.Length == 0 || !_herzen.TryGetValue(mein, out var rest) || Link?.Lauf == null) return;
            if (!Bildschirm(Link.Lauf.Controller.Pos, 3.0 + (2.0 - rest) * 0.8, "@", out var at)) return;
            shown.Add("herz:eigen");
            OrtTag("herz:eigen", "\u2665", at, new Color(1f, 0.85f, 0.9f, 0.95f), 22);
        }

        /// <summary>Free ride: a small sign under every landmark on screen and the names of the host's horses.</summary>
        private void PlaceOrtTags()
        {
            var shown = new HashSet<string>();
            if (_frei && W > 1f)
            {
                var hofAt = RmpFrei.HofMitte + new V2(0f, -(float)RmpFrei.HofRingRadius - 3.0f);
                if (_drei == null && Bildschirm(hofAt, 0.0, null, out var hofPx) && hofPx.x > 0f && hofPx.x < W && hofPx.y > 0f && hofPx.y < H)
                {
                    shown.Add("ort:hof");
                    OrtTag("ort:hof", OrtName?.Invoke("hof") ?? "hof", hofPx, new Color(1f, 0.97f, 0.9f, 0.88f), 15);
                }
                for (var i = 0; i < _besuchPferde.Count; i++)
                {
                    if (!Bildschirm(BesuchPferdPos(i), 2.4, null, out var at)) continue;
                    if (at.x < 0f || at.x > W || at.y < 0f || at.y > H) continue;
                    var key = "pferd:" + i;
                    shown.Add(key);
                    OrtTag(key, _besuchPferde[i].name, at, new Color(1f, 0.93f, 0.66f, 0.95f), 14);
                }
            }
            if (W > 1f) PlaceEigenesHerz(shown);
            // in the region the next gate carries its number over the banner
            var lauf = Link?.Lauf;
            if (_drei != null && W > 1f && lauf != null && lauf.IsRace && !lauf.InZiel && Riding && _drei.TorPunkt(lauf.NextCheckpoint) is Vector3 tor)
            {
                var at = _drei.ToField(tor.x, tor.y, -tor.z, W, H);
                if (at.x > 0f && at.x < W && at.y > 0f && at.y < H)
                {
                    shown.Add("tor");
                    OrtTag("tor", (lauf.NextCheckpoint + 1).ToString(), at, new Color(1f, 0.83f, 0.3f, 0.96f), 22);
                }
            }
            foreach (var kv in _ortTags)
                if (!shown.Contains(kv.Key)) kv.Value.style.display = DisplayStyle.None;
        }

        private void OrtTag(string key, string text, Vector2 at, Color bg, int size)
        {
            if (!_ortTags.TryGetValue(key, out var tag))
            {
                tag = Ux.Text("", "gb-bold", "gb-nowrap");
                tag.name = "rmp-ort-" + key.Replace(':', '-');
                tag.pickingMode = PickingMode.Ignore;
                tag.style.position = Position.Absolute;
                tag.style.color = RidePaint.Outline;
                tag.style.paddingLeft = 8; tag.style.paddingRight = 8; tag.style.paddingTop = 1; tag.style.paddingBottom = 1;
                tag.style.borderTopLeftRadius = 10; tag.style.borderTopRightRadius = 10; tag.style.borderBottomLeftRadius = 10; tag.style.borderBottomRightRadius = 10;
                tag.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                Tags.Add(tag);
                _ortTags[key] = tag;
            }
            tag.style.backgroundColor = bg;
            tag.style.fontSize = size;
            if (tag.text != text) tag.text = text;
            tag.style.left = at.x;
            tag.style.top = at.y;
            tag.style.display = DisplayStyle.Flex;
        }

        // ── controls ──

        private void OnStickDown(PointerDownEvent e)
        {
            if (Link == null || !Link.Running) return;
            _stickPointer = e.pointerId;
            _stickStart = _stickNow = _field.WorldToLocal(e.position);
            _stickZone.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnStickMove(PointerMoveEvent e)
        {
            if (e.pointerId != _stickPointer) return;
            _stickNow = _field.WorldToLocal(e.position);
            _steer = RcompRideView.StickLenkung(_stickStart, _stickNow);
        }

        private void OnStickUp(PointerUpEvent e)
        {
            if (e.pointerId != _stickPointer) return;
            if (_stickZone.HasPointerCapture(e.pointerId)) _stickZone.ReleasePointer(e.pointerId);
            ReleaseStick();
        }

        private void ReleaseStick()
        {
            _stickPointer = -1;
            _steer = 0f;
        }

        private void PaintStick(Painter2D p)
        {
            if (_stickPointer < 0) return;
            P.Circle(p, _stickStart, StickRadiusPx + 4f, new Color(1f, 1f, 1f, 0.22f));
            P.Ring(p, _stickStart, StickRadiusPx, 3f, new Color(1f, 1f, 1f, 0.7f));
            var knob = new Vector2(_stickStart.x + _steer * StickRadiusPx, _stickStart.y);
            P.Circle(p, knob, 22f, RidePaint.Outline);
            P.Circle(p, knob, 19f, RidePaint.Cream);
        }

        private VisualElement RoundButton(string name, string icon, float size, Action act)
        {
            var b = Ux.El("gb-card");
            b.name = name;
            b.style.position = Position.Absolute;
            b.style.width = size; b.style.height = size;
            b.style.borderTopLeftRadius = size * 0.5f; b.style.borderTopRightRadius = size * 0.5f;
            b.style.borderBottomLeftRadius = size * 0.5f; b.style.borderBottomRightRadius = size * 0.5f;
            b.style.alignItems = Align.Center; b.style.justifyContent = Justify.Center;
            var img = Ux.Icon(icon);
            img.style.width = size * 0.5f; img.style.height = size * 0.5f;
            img.pickingMode = PickingMode.Ignore;
            b.Add(img);
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                if (Link == null || !Link.Running) return;
                act();
                Sfx.Play("soft_tap", 0f, 0.7f);
            });
            return b;
        }
    }
}
