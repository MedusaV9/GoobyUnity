using System.Collections.Generic;
using System.Threading.Tasks;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The people of the farm (the farm half of rnpc_figur.gd + rnpc_routine.gd): Rosi the stable master, Dr. Möhrchen the
    /// vet, little Timmi and Miez the ranch cat follow their day — the stable, the trough, the paddock, the yard by the
    /// well, the hay, the meadow, the barn — and walk between their places round the paddock (through its gate, never
    /// through the fence); at home or away (the lake, the house) they are off the farm. All tappable (they give and take
    /// quests). At night everybody is home and Miez sleeps in the barn.
    /// </summary>
    public sealed partial class RanchHofStage
    {
        public static readonly string[] HofLeute = { "rosi", "moehrchen", "timmi", "miez" };

        /// <summary>The routines' places on the farm (stage local, facing); every other place is off the farm.</summary>
        private static readonly Dictionary<string, (Vector3 spot, float yaw)> HofOrte = new Dictionary<string, (Vector3, float)>
        {
            ["heulager"] = (new Vector3(5.0f, 0f, 4.2f), 215f),
            ["trog"] = (new Vector3(2.2f, 0f, 3.9f), 205f),
            ["hof_mitte"] = (new Vector3(-4.5f, 0f, 3.9f), 150f),
            ["koppel_pferde"] = (new Vector3(5.6f, 0f, 9.4f), 205f),
            ["stall"] = (new Vector3(-3.6f, 0f, 12.6f), 170f),
            ["weide"] = (new Vector3(8.6f, 0f, 12.4f), 210f),
            ["scheune"] = (new Vector3(5.8f, 0f, 16.4f), 200f),
        };

        /// <summary>Miez keeps to cat places: curled on the hay, by the trough.</summary>
        private static readonly Dictionary<string, Vector3> MiezOrte = new Dictionary<string, Vector3>
        {
            ["heulager"] = MiezSpot, ["trog"] = new Vector3(1.9f, 0f, 2.3f),
        };

        private static readonly Vector3 MiezSpot = new Vector3(4.6f, 0f, 1.2f);

        /// <summary>Where people come from and go to when their place is off the farm (the lane to the house).</summary>
        private static readonly Vector3 HofRand = new Vector3(-10.5f, 0f, 1.6f);

        private static readonly Vector3 TorVorn = new Vector3(0f, 0f, 3.9f), TorInnen = new Vector3(0f, 0f, 5.3f),
            LueckeInnen = new Vector3(StableSpot.x, 0f, 9.9f), LueckeHinten = new Vector3(StableSpot.x, 0f, 11.3f), Ost = new Vector3(7.0f, 0f, 7.6f),
            West = new Vector3(-7.0f, 0f, 7.6f);

        private const float LaufTempo = 1.3f;

        private sealed class Person
        {
            public Transform T;
            public GoobyAvatar Avatar;
            public bool Laeuft;
            public string Clip = "";
        }

        private readonly Dictionary<string, Transform> _leute = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Person> _personen = new Dictionary<string, Person>();

        /// <summary>Where a person's quest marker floats (stage local; null when not on the farm).</summary>
        public Vector3? LeuteKopf(string id)
        {
            if (!_leute.TryGetValue(id, out var t) || t == null || !t.gameObject.activeSelf) return null;
            return t.position - MgStage3D.Origin + Vector3.up * (id == "miez" ? 0.9f : 1.75f);
        }

        public IEnumerable<string> LeuteDa => _leute.Keys;

        /// <summary>Who is on the farm now and walking (QA).</summary>
        public IEnumerable<(string id, bool da, bool laeuft, Vector3 pos)> LeuteStand()
        {
            foreach (var kv in _personen)
                yield return (kv.Key, kv.Value.T != null && kv.Value.T.gameObject.activeSelf, kv.Value.Laeuft, kv.Value.T != null ? kv.Value.T.position - MgStage3D.Origin : Vector3.zero);
        }

        /// <summary>Puts the farm's people out (once; the screen hands in their looks).</summary>
        public void ZeigeLeute(Dictionary<string, Dictionary<string, string>> looks)
        {
            if (_leute.Count > 0 || _night) return;
            BaueLeute(looks);
        }

        private void BaueLeute(Dictionary<string, Dictionary<string, string>> looks)
        {
            foreach (var id in HofLeute)
            {
                if (id == "miez")
                {
                    var cat = BaueMiez();
                    _leute[id] = cat;
                    _personen[id] = new Person { T = cat };
                    continue;
                }
                var anchor = new GameObject("Leute:" + id).transform;
                anchor.SetParent(_root, false);
                anchor.position = At(HofOrte["hof_mitte"].spot);
                AddTap(anchor.gameObject, Ziel.Person, id, 0, new Vector3(0f, 1.0f, 0f), new Vector3(1.4f, 2.4f, 1.4f));
                _leute[id] = anchor;
                var person = new Person { T = anchor };
                _personen[id] = person;
                _ = SpawnPersonAsync(person, looks != null && looks.TryGetValue(id, out var look) ? look : null);
            }
        }

        private async Task SpawnPersonAsync(Person person, Dictionary<string, string> look)
        {
            var anchor = person.T;
            var avatar = await GoobyAvatar.CreateAsync(anchor, anchor.position);
            if (avatar == null || _destroyed || anchor == null) return;
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            avatar.transform.localScale = Vector3.one * 0.95f;
            if (look != null) avatar.Wear(look);
            avatar.SetEmotion("happy");
            person.Avatar = avatar;
            person.Clip = "";
            Animiere(person);
        }

        /// <summary>
        /// Everyone to where their day has them at <paramref name="stunde"/>: standing at the place, or walking there from
        /// the place before (1.3 m/s along the way round the paddock), or away.
        /// </summary>
        public void TickLeute(double stunde)
        {
            if (_night) return;
            foreach (var kv in _personen)
            {
                var p = kv.Value;
                if (p.T == null) continue;
                var routine = RanchNpcRoutine.Routine(kv.Key);
                var jetzt = RanchNpcRoutine.StationBei(routine, stunde);
                if (jetzt == null) continue;
                var vorher = RanchNpcRoutine.Vorher(routine, jetzt);
                var weg = Weg(kv.Key, vorher?.Ort, jetzt.Ort);
                var gelaufen = (float)(RanchNpcRoutine.SeitStunden(jetzt, stunde) * 3600.0) * LaufTempo;
                var laenge = Laenge(weg);
                var zielDa = Ort(kv.Key, jetzt.Ort).HasValue;
                var unterwegs = weg.Count > 1 && gelaufen < laenge;
                if (!zielDa && !unterwegs)
                {
                    if (p.T.gameObject.activeSelf) p.T.gameObject.SetActive(false);
                    continue;
                }
                if (!p.T.gameObject.activeSelf) p.T.gameObject.SetActive(true);
                var (pos, richtung) = unterwegs ? Entlang(weg, gelaufen) : (weg[weg.Count - 1], Vector3.zero);
                p.T.position = At(pos);
                if (unterwegs && richtung.sqrMagnitude > 1e-4f) p.T.rotation = Quaternion.LookRotation(richtung);
                else if (zielDa) p.T.rotation = Quaternion.Euler(0f, Ort(kv.Key, jetzt.Ort).Value.yaw, 0f);
                if (p.Laeuft != unterwegs)
                {
                    p.Laeuft = unterwegs;
                    Animiere(p);
                }
            }
        }

        private static void Animiere(Person p)
        {
            if (p.Avatar == null) return;
            var clip = p.Laeuft ? "walk-loop" : "idle-loop";
            if (p.Clip == clip) return;
            p.Clip = clip;
            p.Avatar.Play(clip);
        }

        /// <summary>A place on the farm for a person (Miez has her own); null = off the farm.</summary>
        private static (Vector3 spot, float yaw)? Ort(string id, string ort)
        {
            if (id == "miez") return MiezOrte.TryGetValue(ort ?? "", out var m) ? (m, 200f) : ((Vector3, float)?)null;
            return HofOrte.TryGetValue(ort ?? "", out var o) ? o : ((Vector3, float)?)null;
        }

        /// <summary>The way between two places: in front of, behind and inside the paddock, joined by the gate, the back gap and the east side.</summary>
        private static List<Vector3> Weg(string id, string von, string nach)
        {
            var start = Ort(id, von)?.spot ?? HofRand;
            var ziel = Ort(id, nach)?.spot ?? HofRand;
            var weg = new List<Vector3> { start };
            var a = Seite(start);
            var b = Seite(ziel);
            if (a != b)
            {
                if (a == 0 && b == 1 || a == 1 && b == 0) weg.Add(start.x + ziel.x < 0f ? West : Ost);
                else if (a == 0 && b == 2) { weg.Add(TorVorn); weg.Add(TorInnen); }
                else if (a == 2 && b == 0) { weg.Add(TorInnen); weg.Add(TorVorn); }
                else if (a == 1 && b == 2) { weg.Add(LueckeHinten); weg.Add(LueckeInnen); }
                else { weg.Add(LueckeInnen); weg.Add(LueckeHinten); }
            }
            weg.Add(ziel);
            return weg;
        }

        /// <summary>0 = in front of the paddock, 1 = behind or beside it, 2 = inside.</summary>
        private static int Seite(Vector3 p)
        {
            if (p.x > -6f && p.x < 6f && p.z > 4.6f && p.z < 10.6f) return 2;
            return p.z <= 4.6f ? 0 : 1;
        }

        private static float Laenge(List<Vector3> weg)
        {
            var l = 0f;
            for (var i = 1; i < weg.Count; i++) l += Vector3.Distance(weg[i - 1], weg[i]);
            return l;
        }

        private static (Vector3 pos, Vector3 richtung) Entlang(List<Vector3> weg, float s)
        {
            for (var i = 1; i < weg.Count; i++)
            {
                var d = Vector3.Distance(weg[i - 1], weg[i]);
                if (s <= d && d > 1e-4f) return (Vector3.Lerp(weg[i - 1], weg[i], s / d), weg[i] - weg[i - 1]);
                s -= d;
            }
            return (weg[weg.Count - 1], Vector3.zero);
        }

        /// <summary>Miez: a little grey low-poly cat, curled with her tail round her paws.</summary>
        private Transform BaueMiez()
        {
            var cat = new GameObject("Leute:miez").transform;
            cat.SetParent(_root, false);
            cat.position = At(MiezSpot);
            var fell = M("#A9A39C", 0.15f);
            var hell = M("#E9E4DD", 0.15f);
            var body = ArtLibrary.Sphere(cat, "Bauch", At(MiezSpot + new Vector3(0f, 0.18f, 0f)), 0.26f, fell);
            body.transform.localScale = new Vector3(0.62f, 0.4f, 0.48f);
            var kopf = ArtLibrary.Sphere(cat, "Kopf", At(MiezSpot + new Vector3(0.24f, 0.34f, -0.06f)), 0.15f, fell);
            ArtLibrary.Sphere(cat, "Schnauze", At(MiezSpot + new Vector3(0.36f, 0.31f, -0.08f)), 0.06f, hell, false);
            foreach (var z in new[] { -0.08f, 0.06f })
            {
                var ohr = ArtLibrary.Cylinder(cat, "Ohr", At(MiezSpot + new Vector3(0.24f, 0.49f, z - 0.03f)), 0.05f, 0.12f, fell, false);
                ohr.transform.localScale = new Vector3(0.1f, 0.07f, 0.05f);
            }
            var schwanz = ArtLibrary.Cylinder(cat, "Schwanz", At(MiezSpot + new Vector3(-0.2f, 0.12f, 0.14f)), 0.035f, 0.42f, fell, false);
            schwanz.transform.rotation = Quaternion.Euler(80f, 30f, 0f);
            kopf.transform.SetAsLastSibling();
            // a cat of the farm's scale: big enough to spot in the yard and to tap
            cat.localScale = Vector3.one * 1.7f;
            cat.position = At(MiezSpot);
            cat.rotation = Quaternion.Euler(0f, 200f, 0f);
            AddTap(cat.gameObject, Ziel.Person, "miez", 0, new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.6f, 0.9f));
            return cat;
        }
    }
}
