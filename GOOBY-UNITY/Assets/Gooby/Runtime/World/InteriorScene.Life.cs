using System;
using System.Collections.Generic;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// "Läden lebendig" for the place interiors (port of ort_leben.gd + stammkunden.gd): a few visitor Goobys stroll
    /// between the shelves on a day-seeded loop (fur and hat from a palette, a slow pace), stop to look at the goods,
    /// and now and then say a line of their place (<c>city_leben.sprueche.&lt;domain&gt;</c>). Named regulars come in
    /// their hour window with their own look and two lines (Frau Rosine at the market in the morning, Opa Hatschi at the
    /// pharmacy 9–11, Frau Fernweh at the airport, Herr Dübel at the hardware store in the afternoon). The door bell
    /// rings as you come in. Reduced motion: half the visitors and they stand still.
    /// </summary>
    public sealed partial class InteriorScene
    {
        private const float PauseMin = 2.5f, PauseMax = 6f, SpruchStart = 6f, SpruchVersatz = 9f, SpruchAlle = 24f;

        private sealed class Besucher
        {
            public GoobyAvatar A;
            public Vector3[] Punkte;
            public int Ziel;
            public float Pause, Tempo, SpruchIn;
            public string Domain, Clip = "";
            public bool Stamm;
        }

        private static readonly string[] BesucherFelle = { "caramel", "snow", "ash", "rose", "fell_minze", "fell_kakao", "fell_himmel", "cream", "golden" };
        private static readonly string[] BesucherHuete = { "cap", "beanie", "beret", "strawHat", "partyHat" };

        /// <summary>Place → line domain and visitor count (ort_leben.gd configs).</summary>
        private static readonly Dictionary<string, (string domain, int besucher)> LebenKonfig = new Dictionary<string, (string, int)>
        {
            ["mart"] = ("laden", 3), ["goobyman"] = ("drogerie", 2), ["goobytheke"] = ("apotheke", 2), ["pow"] = ("pow", 2),
            ["baumarkt"] = ("baumarkt", 3), ["post"] = ("post", 2), ["autohaus"] = ("autohaus", 2), ["tierarzt"] = ("tierarzt", 2),
            ["gouhbus"] = ("praxis", 2), ["flughafen"] = ("flughafen", 3),
        };

        /// <summary>The regulars (stammkunden.gd KATALOG; Rollo skates in the Funkelpark, which has its own life).</summary>
        private static readonly (string id, string ort, int von, int bis, string fell, string hut, float tempo)[] Stammkunden =
        {
            ("rosine", "mart", 8, 12, "rose", "beret", 0.5f),
            ("hatschi", "goobytheke", 9, 11, "ash", "hut_melone", 0.4f),
            ("fernweh", "flughafen", 11, 16, "golden", "strawHat", 0.55f),
            ("duebel", "baumarkt", 15, 18, "fell_himmel", "hut_bauhelm", 0.62f),
        };

        /// <summary>The line someone just said: world position over their head and the text (the place screen shows it).</summary>
        public event Action<Vector3, string, string> Spricht;

        private readonly List<Besucher> _besucher = new List<Besucher>();
        private bool _lebenGestartet, _lebenReduziert;

        /// <summary>Starts the place's life once (day seed and the local hour pick who comes); lines are looked up by key.</summary>
        public void StartLife(string tag, int stunde, bool reducedMotion, Func<string, IReadOnlyList<string>> zeilen)
        {
            if (_lebenGestartet || Outdoors || !LebenKonfig.TryGetValue(Place.Id, out var konfig)) return;
            _lebenGestartet = true;
            _lebenReduziert = reducedMotion;
            _zeilen = zeilen;
            var rng = new System.Random((int)(Gooby.Core.Random.GodotRng.StringHash(Place.Id + "|" + tag) & 0x7FFFFFFF));
            var n = reducedMotion ? Math.Max(1, konfig.besucher / 2) : konfig.besucher;
            var punkte = Wegpunkte();
            for (var i = 0; i < n; i++)
            {
                var fell = BesucherFelle[rng.Next(BesucherFelle.Length)];
                var hut = rng.NextDouble() < 0.6 ? BesucherHuete[rng.Next(BesucherHuete.Length)] : null;
                var start = rng.Next(punkte.Length);
                var route = new Vector3[punkte.Length];
                for (var k = 0; k < punkte.Length; k++) route[k] = punkte[(start + (i % 2 == 0 ? k : punkte.Length - k)) % punkte.Length];
                _ = SpawnBesucherAsync(fell, hut, route, 0.45f + (float)rng.NextDouble() * 0.4f, konfig.domain, SpruchStart + i * SpruchVersatz, false);
            }
            foreach (var s in Stammkunden)
                if (s.ort == Place.Id && stunde >= s.von && stunde < s.bis)
                    _ = SpawnBesucherAsync(s.fell, s.hut, Wegpunkte(true), s.tempo, "stammkunde_" + s.id, 14f, true);
            Sfx.Play("gvz_wave", 5f, 0.5f);
        }

        private Func<string, IReadOnlyList<string>> _zeilen;

        /// <summary>Spots beside the counter in front of the side shelves (behind the player's Gooby, clear of the dialog card).</summary>
        private Vector3[] Wegpunkte(bool stamm = false)
        {
            var p = Place.Id == "autohaus"
                ? new[] { new Vector3(-2.7f, 0f, -0.9f), new Vector3(-2.1f, 0f, -1.9f), new Vector3(2.8f, 0f, -1.0f), new Vector3(2.2f, 0f, -2.0f), new Vector3(-3.3f, 0f, -0.3f) }
                : new[] { new Vector3(-3.0f, 0f, -1.9f), new Vector3(-2.5f, 0f, -0.6f), new Vector3(3.0f, 0f, -1.8f), new Vector3(2.5f, 0f, -0.5f), new Vector3(-3.4f, 0f, -1.0f) };
            if (stamm) Array.Reverse(p);
            return p;
        }

        private async System.Threading.Tasks.Task SpawnBesucherAsync(string fell, string hut, Vector3[] route, float tempo, string domain, float spruchIn, bool stamm)
        {
            var at = Origin + route[0];
            var avatar = await GoobyAvatar.CreateAsync(_t, at);
            if (avatar == null || Root == null) { if (avatar != null) UnityEngine.Object.Destroy(avatar.gameObject); return; }
            avatar.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 180f, 0f));
            avatar.transform.localScale = Vector3.one * (stamm ? 0.98f : 0.9f);
            var look = new Dictionary<string, string> { ["fell"] = fell };
            if (hut != null) look["hut"] = hut;
            avatar.Wear(look);
            avatar.Play("idle-loop");
            _besucher.Add(new Besucher { A = avatar, Punkte = route, Ziel = 1 % route.Length, Pause = PauseMin, Tempo = tempo, Domain = domain, SpruchIn = spruchIn, Stamm = stamm });
        }

        private void TickLife(float dt)
        {
            foreach (var b in _besucher)
            {
                if (b.A == null) continue;
                if (!_lebenReduziert)
                {
                    if (b.Pause > 0f)
                    {
                        b.Pause -= dt;
                        Clip(b, "idle-loop");
                    }
                    else
                    {
                        var ziel = Origin + b.Punkte[b.Ziel];
                        var pos = b.A.transform.position;
                        var d = ziel - pos;
                        d.y = 0f;
                        if (d.magnitude < 0.05f)
                        {
                            b.Ziel = (b.Ziel + 1) % b.Punkte.Length;
                            b.Pause = UnityEngine.Random.Range(PauseMin, PauseMax);
                            // looking at the shelf: turned to the nearer side wall
                            b.A.transform.rotation = Quaternion.Euler(0f, ziel.x < Origin.x ? 270f : 90f, 0f);
                        }
                        else
                        {
                            b.A.transform.position = pos + d.normalized * Mathf.Min(d.magnitude, b.Tempo * dt);
                            b.A.transform.rotation = Quaternion.Slerp(b.A.transform.rotation, Quaternion.LookRotation(d.normalized), 8f * dt);
                            Clip(b, "walk-loop");
                        }
                    }
                }
                b.SpruchIn -= dt;
                if (b.SpruchIn > 0f || _zeilen == null) continue;
                b.SpruchIn = b.Stamm ? SpruchAlle * 1.8f : SpruchAlle;
                var zeilen = _zeilen("city_leben.sprueche." + b.Domain);
                if (zeilen == null || zeilen.Count == 0) continue;
                var name = b.Stamm ? _zeilen("city_leben.stammkunden." + b.Domain.Substring("stammkunde_".Length)) : null;
                Spricht?.Invoke(b.A.transform.position + Vector3.up * 1.25f, zeilen[UnityEngine.Random.Range(0, zeilen.Count)], name != null && name.Count > 0 ? name[0] : null);
                b.A.BabblePulse();
            }
        }

        private static void Clip(Besucher b, string clip)
        {
            if (b.Clip == clip) return;
            b.Clip = clip;
            b.A.Play(clip);
        }
    }
}
