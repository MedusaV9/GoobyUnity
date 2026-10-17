using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The region's wild animals (port of ranch_wildtiere.gd over <see cref="RanchWildLogik"/>): deer at the forest
    /// clearing, foxes beyond it, hares in the Weidetal, ducks paddling by the jetty and the wild herd on its pasture graze,
    /// drink, rest and wander in a 10 Hz behaviour step, flee from the rider (the herd at a gallop) and the fox creeps up out
    /// of curiosity. The hour and the weather decide who is out. Above them two chains of birds circle over the lake,
    /// butterflies flutter over the farm meadow and the Blumenwiese, fireflies drift over the clearing at night. The
    /// "sparsam" graphics preset halves the groups and drops the swarms.
    /// </summary>
    public sealed partial class RanchWelt3D
    {
        private const float WildSimSchritt = 0.1f;

        private sealed class WildTier
        {
            public RanchWildLogik.Tier Daten;
            public Transform Knoten;
            public Animation Anim;
            public string Clip = "";
            public RanchHorse3D Pferd;
            public bool Aktiv = true;
        }

        private readonly List<WildTier> _wild = new List<WildTier>();
        private readonly List<Flyer> _schwarmVoegel = new List<Flyer>();
        private readonly List<(Flyer f, V2d anker)> _schwarmFalter = new List<(Flyer, V2d)>();
        private ParticleSystem _gluehwuermchen;
        private System.Random _wildRng;
        private float _wildSim;
        private double _stunde = 12.0;
        private string _wetterTyp = "sonne";
        private bool? _voegelAn, _falterAn, _wuermerAn;
        private V2d _vogelAnker;
        private float _vogelHoehe = 55f;

        /// <summary>QA and the screen: wild animals out right now per kind.</summary>
        public Dictionary<string, int> WildZaehlung()
        {
            var n = new Dictionary<string, int>();
            foreach (var w in _wild)
                if (w.Aktiv) n[w.Daten.Art] = (n.TryGetValue(w.Daten.Art, out var k) ? k : 0) + 1;
            n["vogel"] = _voegelAn == true ? _schwarmVoegel.Count : 0;
            n["schmetterling"] = _falterAn == true ? _schwarmFalter.Count : 0;
            n["gluehwuermchen"] = _wuermerAn == true ? 1 : 0;
            return n;
        }

        /// <summary>QA: every wild animal of a kind with its state, region spot, distance to the rider and loaded renderers.</summary>
        public IEnumerable<string> WildDetails(string art)
        {
            foreach (var w in _wild)
            {
                if (art.Length > 0 && w.Daten.Art != art) continue;
                var r = w.Knoten.GetComponentsInChildren<Renderer>(true).Length;
                yield return w.Daten.Art + " " + w.Daten.Zustand + " at " + w.Daten.Pos + " d=" + w.Daten.Pos.Abstand(_x, _z).ToString("F0")
                    + " y=" + (w.Knoten.position.y - _root.position.y).ToString("F2") + " shown=" + w.Knoten.gameObject.activeInHierarchy + " renderers=" + r;
            }
        }

        /// <summary>QA: the rider <paramref name="abstand"/> metres from the first animal of a kind that is out, facing it.</summary>
        public bool BlickAufTier(string art, double abstand)
        {
            foreach (var w in _wild)
            {
                if (w.Daten.Art != art || !w.Aktiv) continue;
                var tier = w.Daten.Pos;
                var weg = new V2d(_x, _z) - tier;
                var l = weg.Abstand(0.0, 0.0);
                weg = l > 0.1 ? weg * (1.0 / l) : new V2d(0.0, 1.0);
                var p = tier + weg * abstand;
                SpringeZu(p.X, p.Z, Mathf.Atan2((float)(tier.X - p.X), (float)-(tier.Z - p.Z)));
                return true;
            }
            return false;
        }

        private void BaueTiere()
        {
            var reduziert = GraphicsQuality.Active == "sparsam";
            var budget = RanchWildLogik.Budget(reduziert);
            _wildRng = new System.Random((int)(RanchKarte.SeedWert % 100000) + 31);
            var lichtung = RanchKarte.ZoneVon("waeldchen")?.Punkt("lichtung") ?? new V2d(-420, -400);
            var weidetal = RanchKarte.ZoneVon("weidetal");
            var see = RanchKarte.ZoneVon("see");
            var steg = see?.Punkt("steg") ?? new V2d(432, 232);
            var seeMitte = see?.Punkt("see_mitte") ?? new V2d(500, 270);
            Gruppe("reh", budget["reh"], lichtung, "waeldchen");
            Gruppe("fuchs", budget["fuchs"], new V2d(lichtung.X + 90.0, -480.0), "waeldchen");
            Gruppe("hase", budget["hase"], (weidetal?.Spawn ?? new V2d(-380, 90)) + new V2d(-60.0, 0.0), "weidetal");
            // beside the jetty halfway out, on the side away from the rowing boat: in view from the planks and the shore
            var raus = seeMitte - steg;
            raus = raus * (1.0 / Math.Max(1e-6, raus.Abstand(0.0, 0.0)));
            Gruppe("ente", budget["ente"], steg + raus * 16.0 + new V2d(-raus.Z, raus.X) * 10.0, "see");
            // the wild herd of the Weidetal (built with its pasture) now lives by the same rules
            foreach (var (pferd, heim, phase) in _herde)
            {
                if (pferd == null) continue;
                pferd.EndAction();
                _wild.Add(new WildTier { Daten = RanchWildLogik.Neu("wildpferd", heim, phase / 4.0), Knoten = pferd.transform, Pferd = pferd });
            }
            BaueSchwaerme(budget, see, lichtung);
        }

        private void Gruppe(string art, int anzahl, V2d heim, string zone)
        {
            var g = Gruppe(zone);
            for (var i = 0; i < anzahl; i++)
            {
                var winkel = _wildRng.NextDouble() * Math.PI * 2.0;
                var abstand = 3.0 + _wildRng.NextDouble() * 11.0;
                var p = heim + new V2d(Math.Cos(winkel) * abstand, Math.Sin(winkel) * abstand);
                var knoten = new GameObject("Wild:" + art).transform;
                knoten.SetParent(g, false);
                knoten.position = Boden(p.X, p.Z);
                knoten.rotation = Quaternion.Euler(0f, (float)(_wildRng.NextDouble() * 360.0), 0f);
                var w = new WildTier { Daten = RanchWildLogik.Neu(art, p, (double)i / Math.Max(1, anzahl)), Knoten = knoten };
                // a touch larger than life (Godot's 1.35 / 0.62 / 0.42 m): the chase camera sees them from 20 m and more
                if (art == "hase")
                {
                    knoten.localScale = Vector3.one * 1.6f;
                    Scenery.Place(knoten, "wild_hare", Vector3.zero, 0f);
                }
                else
                {
                    // the ranch animals' own pipeline (build_tier.py): look along −Z, idle-loop and schritt-loop
                    knoten.localScale = Vector3.one * (art == "reh" ? 1.3f : art == "fuchs" ? 0.8f : 0.8f);
                    _ = LadeTier(w, Ranch(art + "_gooby"));
                }
                _wild.Add(w);
            }
        }

        private static async System.Threading.Tasks.Task LadeTier(WildTier w, string path)
        {
            var go = await AssetLoader.InstanceAsync(path, w.Knoten, System.IO.Path.GetFileNameWithoutExtension(path), legacyAnimation: true);
            if (go == null) return;
            if (w.Knoten == null) { UnityEngine.Object.Destroy(go); return; }
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one;
            var anim = go.GetComponentInChildren<Animation>();
            if (anim == null) return;
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.BasedOnRenderers;
            foreach (AnimationState s in anim) s.wrapMode = WrapMode.Loop;
            w.Anim = anim;
            w.Clip = "";
        }

        private void BaueSchwaerme(Dictionary<string, int> budget, RanchKarte.Zone see, V2d lichtung)
        {
            var mitte = see?.Punkt("see_mitte") ?? new V2d(500, 270);
            _vogelAnker = new V2d(mitte.X - 150.0, mitte.Z - 120.0);
            var luft = Gruppe("see");
            for (var i = 0; i < budget["vogel"]; i++)
            {
                var f = Flyer.Bird(luft, i % 3 == 1, 3.2f);
                f.Phase = i * 0.7f;
                _schwarmVoegel.Add(f);
            }
            // half over the farm meadow (ranch_wildtiere.gd's anchor), half over the Blumenwiese
            var wiese = RanchKarte.ZoneVon("blumenwiese")?.Spawn ?? new V2d(-810, 90);
            for (var i = 0; i < budget["schmetterling"]; i++)
            {
                var hof = i % 2 == 0;
                var anker = hof ? new V2d(-80.0, 60.0) : new V2d(wiese.X, wiese.Z - 30.0);
                var f = Flyer.Butterfly(Gruppe(hof ? "hof" : "blumenwiese"), i, 2.6f);
                f.Phase = i;
                _schwarmFalter.Add((f, anker));
            }
            if (budget["gluehwuermchen"] > 0)
            {
                var g = Gruppe("waeldchen");
                var c = Boden(lichtung.X, lichtung.Z, 1.4);
                _gluehwuermchen = Effects.Fireflies(g, new Bounds(c, new Vector3(56f, 2.6f, 56f)));
                var em = _gluehwuermchen.emission;
                em.rateOverTimeMultiplier *= budget["gluehwuermchen"] / 12f;
                _gluehwuermchen.gameObject.SetActive(false);
            }
        }

        /// <summary>The hour and weather the animals go by (the screen's clock, the day plan's type).</summary>
        private void TiereZeit(double stunde, string wetterTyp)
        {
            _stunde = stunde;
            if (!string.IsNullOrEmpty(wetterTyp)) _wetterTyp = wetterTyp;
        }

        private void TickTiere(float dt)
        {
            if (_wild.Count == 0 && _schwarmVoegel.Count == 0 && _schwarmFalter.Count == 0) return;
            var reiter = new V2d(_x, _z);
            _wildSim += dt;
            while (_wildSim >= WildSimSchritt)
            {
                _wildSim -= WildSimSchritt;
                foreach (var w in _wild)
                {
                    var an = RanchWildLogik.Aktiv(w.Daten.Art, _stunde, _wetterTyp);
                    if (an != w.Aktiv)
                    {
                        w.Aktiv = an;
                        w.Knoten.gameObject.SetActive(an);
                    }
                    if (an) w.Daten = RanchWildLogik.Schritt(w.Daten, WildSimSchritt, reiter, _wildRng.NextDouble());
                }
            }
            foreach (var w in _wild)
                if (w.Aktiv && w.Knoten.gameObject.activeInHierarchy) StelleTier(w, dt);
            TickSchwaerme();
        }

        /// <summary>Eases the node to its simulated spot, turns it the way it walks and plays its walk or idle loop.</summary>
        private void StelleTier(WildTier w, float dt)
        {
            var d = w.Daten;
            var ziel = Boden(d.Pos.X, d.Pos.Z);
            if (d.Art == "ente" && RanchGelaende.IstWasser(d.Pos.X, d.Pos.Z))
                ziel.y = _root.position.y + (float)RanchGelaende.WasserHoehe + 0.06f;
            var vorher = w.Knoten.position;
            var p = Vector3.Lerp(vorher, ziel, Mathf.Min(1f, dt * 6f));
            var richtung = new Vector3(p.x - vorher.x, 0f, p.z - vorher.z);
            if (richtung.sqrMagnitude > 2e-5f)
            {
                var soll = Quaternion.LookRotation(richtung, Vector3.up);
                w.Knoten.rotation = Quaternion.Slerp(w.Knoten.rotation, soll, Mathf.Min(1f, dt * 7f));
            }
            var laeuft = RanchWildLogik.Laeuft(d);
            if (laeuft && w.Pferd == null && w.Anim == null)
            {
                // the hare (no rig): a bouncy hop
                p.y += Mathf.Abs(Mathf.Sin(_t * 9f + (float)d.Phase * Mathf.PI * 2f)) * 0.16f;
            }
            w.Knoten.position = p;
            if (w.Pferd != null)
            {
                if (laeuft)
                {
                    w.Pferd.EndAction();
                    w.Pferd.Gait(d.Zustand == "flucht" ? "galopp" : d.Zustand == "wandern" ? "trab" : "schritt");
                }
                else if (d.Zustand == "fressen" || d.Zustand == "trinken")
                {
                    if (!w.Pferd.InAction) w.Pferd.Action("fressen-loop", 100000f);
                }
                else
                {
                    w.Pferd.EndAction();
                    w.Pferd.Gait("stand");
                }
                return;
            }
            if (w.Anim == null) return;
            var clip = laeuft ? "schritt-loop" : "idle-loop";
            var state = w.Anim[clip];
            if (state == null) return;
            state.speed = laeuft && d.Zustand == "flucht" ? 2f : 1f;
            if (w.Clip == clip) return;
            w.Clip = clip;
            w.Anim.CrossFade(clip, 0.2f);
        }

        private void TickSchwaerme()
        {
            var t = _t;
            var voegel = RanchWildLogik.Aktiv("vogel", _stunde, _wetterTyp);
            if (_voegelAn != voegel) { _voegelAn = voegel; foreach (var f in _schwarmVoegel) f.Root.gameObject.SetActive(voegel); }
            var falter = RanchWildLogik.Aktiv("schmetterling", _stunde, _wetterTyp);
            if (_falterAn != falter) { _falterAn = falter; foreach (var (f, _) in _schwarmFalter) f.Root.gameObject.SetActive(falter); }
            var wuermer = RanchWildLogik.Aktiv("gluehwuermchen", _stunde, _wetterTyp);
            if (_wuermerAn != wuermer) { _wuermerAn = wuermer; if (_gluehwuermchen != null) _gluehwuermchen.gameObject.SetActive(wuermer); }
            if (voegel)
            {
                // two chains draw wide circles in the sky over the lake (ranch_wildtiere.gd _vogel_pos)
                for (var i = 0; i < _schwarmVoegel.Count; i++)
                {
                    var f = _schwarmVoegel[i];
                    if (!f.Root.gameObject.activeInHierarchy) break;
                    var kette = i % 2;
                    var tempo = 0.11f + kette * 0.04f;
                    var winkel = t * tempo + kette * Mathf.PI - Mathf.Floor(i / 2f) * 0.09f;
                    var radius = 180f + kette * 90f;
                    var x = _vogelAnker.X + Mathf.Cos(winkel) * radius;
                    var z = _vogelAnker.Z + Mathf.Sin(winkel) * radius;
                    var y = _vogelHoehe + Mathf.Sin(t * 0.8f + i) * 3f + kette * 14f;
                    f.Root.position = _root.position + L(x, y, z);
                    var flug = L(-Mathf.Sin(winkel), 0.0, Mathf.Cos(winkel));
                    f.Root.rotation = Quaternion.LookRotation(flug, Vector3.up) * Quaternion.Euler(0f, 0f, -12f);
                    f.Flap(Mathf.Sin(t * 7f + i * 1.3f) * 0.6f);
                }
            }
            if (falter)
            {
                for (var i = 0; i < _schwarmFalter.Count; i++)
                {
                    var (f, anker) = _schwarmFalter[i];
                    if (!f.Root.gameObject.activeInHierarchy) continue;
                    var k = (float)i;
                    var ax = 9f + (k * 3.1f) % 12f;
                    var az = 9f + (k * 2.3f) % 14f;
                    var x = anker.X + Mathf.Sin(t * 0.5f + k * 1.7f) * ax;
                    var z = anker.Z + Mathf.Cos(t * 0.4f + k * 2.3f) * az;
                    var y = RanchGelaende.Hoehe(x, z) + 0.7 + Mathf.Abs(Mathf.Sin(t * 2.2f + k)) * 1.1f;
                    f.Root.position = _root.position + L(x, y, z);
                    var flug = L(Mathf.Cos(t * 0.5f + k * 1.7f) * 0.5f * ax, 0.0, -Mathf.Sin(t * 0.4f + k * 2.3f) * 0.4f * az);
                    if (flug.sqrMagnitude > 1e-4f) f.Root.rotation = Quaternion.LookRotation(flug, Vector3.up);
                    f.Flap(-0.35f + Mathf.Sin(t * 15f + k * 2.1f) * 0.75f);
                }
            }
        }
    }
}
