using System;
using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    /// <summary>Growth model (water × wind × shade × greenhouse) — port of garden_growth.gd.</summary>
    public static class GardenGrowth
    {
        public const double WasserTrocken = 0.0;
        public const double WasserNass = 1.0;
        public const double WindMalus = 0.85;
        public const double WindMalusEmpfindlich = 0.7;
        public const int WindRing = 2;
        public const double SchattenSonne = 0.75;
        public const double SchattenSchatten = 1.1;
        public const double Neutral = 1.0;
        public const double GewaechshausBonus = 1.25;
        public const double GiessStunden = 8.0;
        public const int SchattenTiefe = 2;

        public static double WasserFaktor(double wateredUntil, double jetztS, bool regen, bool sprinkler, bool imGewaechshaus)
        {
            if (wateredUntil > jetztS) return WasserNass;
            if (sprinkler) return WasserNass;
            if (regen && !imGewaechshaus) return WasserNass;
            return WasserTrocken;
        }

        public static double WindFaktor(Cell cell, int w, int h, HashSet<Cell> geschuetzt, bool imGewaechshaus, bool empfindlich = false)
        {
            if (imGewaechshaus || (geschuetzt != null && geschuetzt.Contains(cell))) return Neutral;
            var randAbstand = Math.Min(Math.Min(cell.X, cell.Y), Math.Min(w - 1 - cell.X, h - 1 - cell.Y));
            if (randAbstand >= WindRing) return Neutral;
            return empfindlich ? WindMalusEmpfindlich : WindMalus;
        }

        public static double SchattenFaktor(bool imSchatten, string licht)
        {
            if (!imSchatten) return Neutral;
            switch (licht)
            {
                case "sonne": return SchattenSonne;
                case "schatten": return SchattenSchatten;
            }
            return Neutral;
        }

        public static double GewaechshausFaktor(bool imGewaechshaus) => imGewaechshaus ? GewaechshausBonus : Neutral;

        public static double Rate(double wasser, double wind, double schatten, double gewaechshaus) => Math.Max(0.0, wasser * wind * schatten * gewaechshaus);

        public struct Faktoren
        {
            public double Wasser, Wind, Schatten, Gewaechshaus, Rate;
        }

        public static Faktoren FaktorenOf(GardenGrid grid, Cell at, double jetztS, bool regen, HashSet<Cell> schattenZellen)
        {
            var data = grid.Cell(at);
            var imGh = grid.GreenhouseCells().Contains(at);
            var crop = GardenCrops.Crop(J.Str(data["crop"]));
            var licht = crop?.Licht ?? "neutral";
            var wasser = WasserFaktor(J.Num(data["watered_until"]), jetztS, regen, grid.SprinklerCells().Contains(at), imGh);
            var wind = WindFaktor(at, grid.Width, grid.Height, grid.FenceShieldedCells(), imGh, crop?.WindEmpfindlich ?? false);
            var schatten = SchattenFaktor(schattenZellen != null && schattenZellen.Contains(at) && !imGh, licht);
            var gh = GewaechshausFaktor(imGh);
            return new Faktoren { Wasser = wasser, Wind = wind, Schatten = schatten, Gewaechshaus = gh, Rate = Rate(wasser, wind, schatten, gh) };
        }

        /// <summary>Structures (except sprinklers) throw shade SchattenTiefe cells to the north (−y).</summary>
        public static HashSet<Cell> SchattenZellen(GardenGrid grid)
        {
            var outp = new HashSet<Cell>();
            foreach (var s in grid.Structures)
            {
                if (s.Kind == "sprinkler") continue;
                foreach (var cell in GardenGrid.StructureCells(s.Kind, s.At, s.Rot))
                    for (var d = 1; d <= SchattenTiefe; d++)
                    {
                        var shaded = new Cell(cell.X, cell.Y - d);
                        if (grid.InBounds(shaded)) outp.Add(shaded);
                    }
            }
            return outp;
        }

        public static int Advance(JObject cellData, double minuten, double growthRate)
        {
            var crop = GardenCrops.Crop(J.Str(cellData["crop"]));
            if (crop == null || minuten <= 0.0) return J.Int(cellData["stage"]);
            var progress = J.Num(cellData["progress_min"]) + minuten * Math.Max(0.0, growthRate);
            var stufen = crop.Stufen;
            double proStufe = crop.MinutenProStufe;
            var stage = Math.Min(stufen, (int)Math.Floor(progress / proStufe));
            cellData["progress_min"] = Math.Min(progress, stufen * proStufe);
            cellData["stage"] = stage;
            return stage;
        }

        public static bool IstErntereif(JObject cellData)
        {
            var crop = GardenCrops.Crop(J.Str(cellData["crop"]));
            return crop != null && J.Int(cellData["stage"]) >= crop.Stufen;
        }
    }
}
