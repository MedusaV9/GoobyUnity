using System;
using System.Collections.Generic;
using System.IO;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Ghost run format of comp_ghost.gd (RW-5) — pure encoder/decoder/interpolation, byte-compatible with the Godot saves.
    /// 10 Hz, 8 bytes per sample (80 B/s). Binary v1, little-endian: header 16 B = "G5", u8 version, u8 hz, u8 discipline,
    /// u8 flags, f32 origin x, f32 origin z, u16 count; sample = i16 dx_mm, i16 dz_mm (delta to the previous quantised
    /// position), u8 heading (0..255 = 0..2π), u8 gait | bit 3 jumping, u8 y_cm, u8 reserved.
    /// </summary>
    public static class RanchCompGhost
    {
        public const int Version = 1, Hz = 10, HeaderBytes = 16, SampleBytes = 8, BytesProS = Hz * SampleBytes, SprungBit = 8, MaxSamples = 65535;
        public const byte MagicA = 0x47, MagicB = 0x35;
        public static readonly string[] DisziplinIds = { "springen", "dressur", "gelaende", "rennen", "trail", "schau", "tonnen", "zeit" };
        public static readonly string[] Gangarten = { "stand", "schritt", "trab", "toelt", "galopp" };

        public struct RecSample
        {
            public int DxMm, DzMm, Heading8, GangFlags, YCm;
        }

        public sealed class Recorder
        {
            public string Disziplin;
            public V2 Origin;
            public double AkkuS;
            public int LetzteX, LetzteZ;
            public readonly List<RecSample> Samples = new List<RecSample>();
        }

        public struct Sample
        {
            public V2 Pos;
            public double Y, Heading;
            public string Gangart;
            public bool Springt;
        }

        public sealed class Geist
        {
            public bool Ok;
            public string Disziplin = "zeit";
            public int Hz = RanchCompGhost.Hz;
            public double DauerS;
            public readonly List<Sample> Samples = new List<Sample>();
        }

        public struct Zustand
        {
            public V2 Pos;
            public double Y, Heading;
            public string Gangart;
            public bool Springt;
        }

        public static Recorder NeuerRecorder(string disziplin, V2 origin) => new Recorder { Disziplin = disziplin, Origin = origin };

        /// <summary>Feeds one frame; samples on its own 10 Hz grid.</summary>
        public static void Tick(Recorder rec, double dt, V2 pos, double y, double heading, string gangart, bool springt = false)
        {
            rec.AkkuS += Math.Max(0.0, dt);
            const double schritt = 1.0 / Hz;
            while (rec.AkkuS >= schritt)
            {
                rec.AkkuS -= schritt;
                AddSample(rec, pos, y, heading, gangart, springt);
            }
        }

        public static double DauerS(Recorder rec) => (double)rec.Samples.Count / Hz;

        public static byte[] Encode(Recorder rec)
        {
            var anzahl = Math.Min(rec.Samples.Count, MaxSamples);
            using (var ms = new MemoryStream(HeaderBytes + anzahl * SampleBytes))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(MagicA);
                w.Write(MagicB);
                w.Write((byte)Version);
                w.Write((byte)Hz);
                w.Write((byte)Math.Max(0, Array.IndexOf(DisziplinIds, rec.Disziplin)));
                w.Write((byte)0);
                w.Write(rec.Origin.X);
                w.Write(rec.Origin.Y);
                w.Write((ushort)anzahl);
                for (var i = 0; i < anzahl; i++)
                {
                    var s = rec.Samples[i];
                    w.Write((short)s.DxMm);
                    w.Write((short)s.DzMm);
                    w.Write((byte)s.Heading8);
                    w.Write((byte)s.GangFlags);
                    w.Write((byte)s.YCm);
                    w.Write((byte)0);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static string ToB64(Recorder rec) => Convert.ToBase64String(Encode(rec));

        /// <summary>Binary v1 → ghost; broken data → <c>Ok = false</c>.</summary>
        public static Geist Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderBytes) return new Geist();
            using (var ms = new MemoryStream(bytes))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadByte() != MagicA || r.ReadByte() != MagicB || r.ReadByte() != Version) return new Geist();
                var hz = Math.Max(1, (int)r.ReadByte());
                var disziplinId = r.ReadByte();
                r.ReadByte();
                var origin = new V2(r.ReadSingle(), r.ReadSingle());
                var anzahl = r.ReadUInt16();
                if (bytes.Length < HeaderBytes + anzahl * SampleBytes) return new Geist();
                var geist = new Geist { Ok = true, Hz = hz };
                int px = 0, pz = 0;
                for (var i = 0; i < anzahl; i++)
                {
                    px += r.ReadInt16();
                    pz += r.ReadInt16();
                    var heading8 = r.ReadByte();
                    var gangFlags = r.ReadByte();
                    var yCm = r.ReadByte();
                    r.ReadByte();
                    geist.Samples.Add(new Sample
                    {
                        Pos = origin + new V2(px / 1000.0, pz / 1000.0),
                        Y = yCm / 100.0,
                        Heading = heading8 / 256.0 * 2.0 * Math.PI,
                        Gangart = Gangarten[Math.Min(gangFlags & 7, Gangarten.Length - 1)],
                        Springt = (gangFlags & SprungBit) != 0,
                    });
                }
                geist.Disziplin = disziplinId < DisziplinIds.Length ? DisziplinIds[disziplinId] : "zeit";
                geist.DauerS = (double)geist.Samples.Count / hz;
                return geist;
            }
        }

        public static Geist FromB64(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return new Geist();
            try { return Decode(Convert.FromBase64String(b64)); }
            catch (FormatException) { return new Geist(); }
        }

        /// <summary>Interpolated playback at time t (linear between samples, heading over the short way).</summary>
        public static Zustand ZustandBei(Geist geist, double t)
        {
            if (geist == null || geist.Samples.Count == 0) return new Zustand { Gangart = "stand" };
            var hz = Math.Max(1, geist.Hz);
            var f = Math.Max(0.0, Math.Min((double)(geist.Samples.Count - 1) / hz, t)) * hz;
            var i = Math.Min((int)f, geist.Samples.Count - 1);
            var j = Math.Min(i + 1, geist.Samples.Count - 1);
            var frac = f - i;
            var a = geist.Samples[i];
            var b = geist.Samples[j];
            var diff = RcompKurs.Fposmod(b.Heading - a.Heading + Math.PI, 2.0 * Math.PI) - Math.PI;
            return new Zustand
            {
                Pos = a.Pos.Lerp(b.Pos, (float)frac),
                Y = a.Y + (b.Y - a.Y) * frac,
                Heading = a.Heading + diff * frac,
                Gangart = a.Gangart,
                Springt = a.Springt,
            };
        }

        private static void AddSample(Recorder rec, V2 pos, double y, double heading, string gangart, bool springt)
        {
            if (rec.Samples.Count >= MaxSamples) return;
            var zielX = (int)Math.Round(((double)pos.X - rec.Origin.X) * 1000.0, MidpointRounding.AwayFromZero);
            var zielZ = (int)Math.Round(((double)pos.Y - rec.Origin.Y) * 1000.0, MidpointRounding.AwayFromZero);
            var dx = Math.Max(-32767, Math.Min(32767, zielX - rec.LetzteX));
            var dz = Math.Max(-32767, Math.Min(32767, zielZ - rec.LetzteZ));
            rec.LetzteX += dx;
            rec.LetzteZ += dz;
            var gang = Math.Max(0, Array.IndexOf(Gangarten, gangart));
            rec.Samples.Add(new RecSample
            {
                DxMm = dx,
                DzMm = dz,
                Heading8 = (int)Math.Round(RcompKurs.Fposmod(heading, 2.0 * Math.PI) / (2.0 * Math.PI) * 256.0, MidpointRounding.AwayFromZero) & 255,
                GangFlags = gang | (springt ? SprungBit : 0),
                YCm = Math.Max(0, Math.Min(255, (int)Math.Round(Math.Max(0.0, y) * 100.0, MidpointRounding.AwayFromZero))),
            });
        }
    }
}
