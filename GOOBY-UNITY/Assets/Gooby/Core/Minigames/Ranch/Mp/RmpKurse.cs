using System;
using System.Collections.Generic;

namespace Gooby.Core.Minigames.Ranch.Mp
{
    /// <summary>
    /// Course catalogue of the ranch multiplayer games (rmp_kurse.gd, RW-6) — pure. Must stay in step with
    /// GOOBY-SERVER/src/ranchmp.js (KURSE + kursHash): MG_READY carries <see cref="KursHash"/>, a mismatch only makes the run
    /// unranked. Positions are ranch-world metres (x, z) — Godot clients ride the same lines through their open world, so a
    /// Unity rider and a Godot rider race each other on one course.
    /// </summary>
    public static class RmpKurse
    {
        public const string Version = "v1";
        public const string Besuch = "besuch", Ausritt = "ausritt", Rennen = "rennen", Fangen = "fangen", Parcours = "parcours";

        /// <summary>The instanced 2–4 player games (server MATCH_MODES), in menu order.</summary>
        public static readonly string[] MatchModi = { Rennen, Parcours, Fangen };

        /// <summary>The free rides without course or match (poses + reactions only) — <see cref="RmpFrei"/>.</summary>
        public static readonly string[] FreiModi = { Ausritt, Besuch };

        /// <summary>Everything the Ranch-Treffen menu offers, in menu order.</summary>
        public static readonly string[] MenuModi = { Rennen, Parcours, Fangen, Ausritt, Besuch };

        public static bool IstFrei(string mode) => mode == Ausritt || mode == Besuch;

        /// <summary>Gait ids of the pose relay (index = MG_POSE gait) — RanchCompGhost.GANGARTEN.</summary>
        public static readonly string[] Gangarten = { "stand", "schritt", "trab", "toelt", "galopp" };

        public static int MaxSpieler(string mode) => mode == Besuch ? 2 : 4;

        public sealed class Kurs
        {
            public string Id, Mode;
            public V2[] Checkpoints = new V2[0];
            public double Radius = 10.0;
            /// <summary>The server's minimum time between two checkpoints — faster runs only count as unranked.</summary>
            public int MinSegmentMs;
            public V2 ArenaCenter;
            public double ArenaRadius, TagRadius;
        }

        /// <summary>Live courses (mirror of ranchmp.js KURSE — never reorder the checkpoints).</summary>
        public static readonly IReadOnlyDictionary<string, Kurs> Kurse = new Dictionary<string, Kurs>
        {
            // oval grass track in the Weidetal
            ["grasbahn"] = new Kurs
            {
                Id = "grasbahn", Mode = Rennen, Radius = 14.0, MinSegmentMs = 900,
                Checkpoints = new[]
                {
                    new V2(-330f, 60f), new V2(-356f, 102f), new V2(-420f, 120f), new V2(-484f, 102f),
                    new V2(-510f, 60f), new V2(-484f, 18f), new V2(-420f, 0f), new V2(-356f, 18f),
                },
            },
            // slalom gates at the yard's riding ring
            ["hof_parcours"] = new Kurs
            {
                Id = "hof_parcours", Mode = Parcours, Radius = 10.0, MinSegmentMs = 700,
                Checkpoints = new[]
                {
                    new V2(60f, -40f), new V2(90f, 0f), new V2(60f, 40f), new V2(90f, 80f),
                    new V2(60f, 120f), new V2(20f, 150f), new V2(-20f, 120f), new V2(-40f, 80f),
                },
            },
            ["weide_fangen"] = new Kurs { Id = "weide_fangen", Mode = Fangen, ArenaCenter = new V2(-380f, 90f), ArenaRadius = 70.0, TagRadius = 3.0 },
        };

        public static string StandardKurs(string mode) =>
            mode == Rennen ? "grasbahn" : mode == Parcours ? "hof_parcours" : mode == Fangen ? "weide_fangen" : "";

        public static Kurs Get(string kursId) => kursId != null && Kurse.TryGetValue(kursId, out var k) ? k : null;

        /// <summary>Sync contract with the server: "&lt;kursId&gt;:v1:&lt;checkpoint count&gt;" ("" for unknown courses).</summary>
        public static string KursHash(string kursId)
        {
            var k = Get(kursId);
            return k == null ? "" : kursId + ":" + Version + ":" + k.Checkpoints.Length;
        }

        public static int CheckpointAnzahl(string kursId) => Get(kursId)?.Checkpoints.Length ?? 0;

        public static V2 CheckpointPos(string kursId, int idx)
        {
            var k = Get(kursId);
            return k == null || idx < 0 || idx >= k.Checkpoints.Length ? V2.Zero : k.Checkpoints[idx];
        }

        /// <summary>Close enough to checkpoint <paramref name="idx"/> (XZ distance ≤ the course radius)?</summary>
        public static bool CheckpointErreicht(string kursId, int idx, V2 pos)
        {
            var k = Get(kursId);
            if (k == null || idx < 0 || idx >= k.Checkpoints.Length) return false;
            return (pos - k.Checkpoints[idx]).Length <= k.Radius;
        }

        /// <summary>
        /// Start grid: side by side 6 m behind checkpoint 0, across the line to checkpoint 1 (3 m apart, deterministic from
        /// the place); the tag meadow puts everyone on a circle of half the arena radius.
        /// </summary>
        public static V2 StartPosition(string kursId, int platz, int anzahl)
        {
            var k = Get(kursId);
            if (k == null) return V2.Zero;
            if (k.Checkpoints.Length < 2)
            {
                var winkel = 2.0 * Math.PI * platz / Math.Max(1.0, anzahl);
                var radius = k.ArenaRadius * 0.5;
                return new V2(k.ArenaCenter.X + Math.Cos(winkel) * radius, k.ArenaCenter.Y + Math.Sin(winkel) * radius);
            }
            var start = k.Checkpoints[0];
            var richtung = (k.Checkpoints[1] - start).Normalized();
            var quer = new V2(-richtung.Y, richtung.X);
            var versatz = (platz - (anzahl - 1.0) * 0.5) * 3.0;
            return start - richtung * 6.0 + quer * versatz;
        }

        /// <summary>Heading at the start (ride controller convention: forward = −(sin h, cos h)): toward checkpoint 1, on the meadow toward its centre.</summary>
        public static double StartHeading(string kursId, int platz, int anzahl)
        {
            var k = Get(kursId);
            if (k == null) return 0.0;
            var from = StartPosition(kursId, platz, anzahl);
            var to = k.Checkpoints.Length >= 2 ? k.Checkpoints[1] : k.ArenaCenter;
            return HeadingTo(from, to);
        }

        public static double HeadingTo(V2 from, V2 to)
        {
            var d = to - from;
            return Math.Atan2(-d.X, -d.Y);
        }

        public static int GaitIndex(string gait) => Math.Max(0, Array.IndexOf(Gangarten, gait ?? ""));

        public static string GaitName(int index) => Gangarten[Math.Max(0, Math.Min(Gangarten.Length - 1, index))];

        /// <summary>The meadow around a course (checkpoints or tag arena plus <paramref name="rand"/> metres): the view paints it, the horse stays inside.</summary>
        public static (V2 center, V2 half) Feld(string kursId, double rand = 28.0)
        {
            var k = Get(kursId);
            if (k == null) return (V2.Zero, new V2(40f, 40f));
            if (k.Checkpoints.Length == 0)
            {
                var r = k.ArenaRadius + rand * 0.5;
                return (k.ArenaCenter, new V2(r, r));
            }
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var c in k.Checkpoints)
            {
                minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
                minZ = Math.Min(minZ, c.Y); maxZ = Math.Max(maxZ, c.Y);
            }
            var center = new V2((minX + maxX) * 0.5, (minZ + maxZ) * 0.5);
            return (center, new V2((maxX - minX) * 0.5 + rand, (maxZ - minZ) * 0.5 + rand));
        }
    }
}
