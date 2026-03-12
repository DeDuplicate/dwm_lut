using System;
using System.Globalization;
using System.Text;

namespace LutCreator
{
    public class LutParams
    {
        public double Brightness { get; set; }
        public double Contrast { get; set; }
        public double Saturation { get; set; }
        public double Vibrance { get; set; }
        public double Exposure { get; set; }
        public double Gamma { get; set; }
        public double Temperature { get; set; }
        public double Tint { get; set; }
        public double HueShift { get; set; }
        public double RedGamma { get; set; }
        public double GreenGamma { get; set; }
        public double BlueGamma { get; set; }
        public double ShadowsR { get; set; }
        public double ShadowsG { get; set; }
        public double ShadowsB { get; set; }
        public double HighlightsR { get; set; }
        public double HighlightsG { get; set; }
        public double HighlightsB { get; set; }

        public bool IsIdentity()
        {
            return Brightness == 0 && Contrast == 0 && Saturation == 0 && Vibrance == 0
                && Exposure == 0 && Gamma == 0 && Temperature == 0 && Tint == 0
                && HueShift == 0 && RedGamma == 0 && GreenGamma == 0 && BlueGamma == 0
                && ShadowsR == 0 && ShadowsG == 0 && ShadowsB == 0
                && HighlightsR == 0 && HighlightsG == 0 && HighlightsB == 0;
        }
    }

    public static class LutEngine
    {
        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

        public static void ApplyAdjustments(double r, double g, double b, LutParams p,
            out double ro, out double go, out double bo)
        {
            // Exposure (EV stops)
            if (p.Exposure != 0)
            {
                double mult = Math.Pow(2, p.Exposure / 50.0);
                r *= mult; g *= mult; b *= mult;
            }

            // Brightness (linear offset)
            if (p.Brightness != 0)
            {
                double off = p.Brightness / 200.0;
                r += off; g += off; b += off;
            }

            // Contrast (around 0.5 midpoint)
            if (p.Contrast != 0)
            {
                double c = 1.0 + p.Contrast / 100.0;
                r = (r - 0.5) * c + 0.5;
                g = (g - 0.5) * c + 0.5;
                b = (b - 0.5) * c + 0.5;
            }

            // Global gamma
            if (p.Gamma != 0)
            {
                double gm = 1.0 / (1.0 + p.Gamma / 100.0);
                r = Math.Pow(Math.Max(r, 0), gm);
                g = Math.Pow(Math.Max(g, 0), gm);
                b = Math.Pow(Math.Max(b, 0), gm);
            }

            // Per-channel gamma
            if (p.RedGamma != 0)
            {
                double gm = 1.0 / (1.0 + p.RedGamma / 100.0);
                r = Math.Pow(Math.Max(r, 0), gm);
            }
            if (p.GreenGamma != 0)
            {
                double gm = 1.0 / (1.0 + p.GreenGamma / 100.0);
                g = Math.Pow(Math.Max(g, 0), gm);
            }
            if (p.BlueGamma != 0)
            {
                double gm = 1.0 / (1.0 + p.BlueGamma / 100.0);
                b = Math.Pow(Math.Max(b, 0), gm);
            }

            // Color temperature (warm = +R -B, cool = -R +B)
            if (p.Temperature != 0)
            {
                double t = p.Temperature / 200.0;
                r += t * 0.3;
                b -= t * 0.3;
                g += t * 0.05;
            }

            // Tint (green vs magenta)
            if (p.Tint != 0)
            {
                double t = p.Tint / 200.0;
                g += t * 0.3;
                r -= t * 0.1;
                b -= t * 0.1;
            }

            // Hue shift
            if (p.HueShift != 0)
            {
                RgbToHsl(Clamp01(r), Clamp01(g), Clamp01(b), out double h, out double s, out double l);
                h = (h + p.HueShift / 360.0 + 1.0) % 1.0;
                HslToRgb(h, s, l, out r, out g, out b);
            }

            // Saturation
            if (p.Saturation != 0)
            {
                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double s = 1.0 + p.Saturation / 100.0;
                r = lum + (r - lum) * s;
                g = lum + (g - lum) * s;
                b = lum + (b - lum) * s;
            }

            // Vibrance (affects less saturated colors more)
            if (p.Vibrance != 0)
            {
                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double maxC = Math.Max(r, Math.Max(g, b));
                double minC = Math.Min(r, Math.Min(g, b));
                double sat = maxC > 0 ? (maxC - minC) / maxC : 0;
                double amt = (1.0 - sat) * (p.Vibrance / 100.0);
                r = lum + (r - lum) * (1.0 + amt);
                g = lum + (g - lum) * (1.0 + amt);
                b = lum + (b - lum) * (1.0 + amt);
            }

            // Color grading: Shadows
            if (p.ShadowsR != 0 || p.ShadowsG != 0 || p.ShadowsB != 0)
            {
                double lum = Clamp01(0.2126 * r + 0.7152 * g + 0.0722 * b);
                double sw = (1.0 - lum) * (1.0 - lum); // Quadratic falloff
                r += (p.ShadowsR / 200.0) * sw;
                g += (p.ShadowsG / 200.0) * sw;
                b += (p.ShadowsB / 200.0) * sw;
            }

            // Color grading: Highlights
            if (p.HighlightsR != 0 || p.HighlightsG != 0 || p.HighlightsB != 0)
            {
                double lum = Clamp01(0.2126 * r + 0.7152 * g + 0.0722 * b);
                double hw = lum * lum; // Quadratic
                r += (p.HighlightsR / 200.0) * hw;
                g += (p.HighlightsG / 200.0) * hw;
                b += (p.HighlightsB / 200.0) * hw;
            }

            ro = Clamp01(r);
            go = Clamp01(g);
            bo = Clamp01(b);
        }

        /// <summary>
        /// Generate a .cube file string compatible with DwmLutGUI.
        /// Order: B varies slowest, G middle, R fastest.
        /// </summary>
        public static string GenerateCubeFile(LutParams parms, int size)
        {
            var sb = new StringBuilder(size * size * size * 30);
            sb.AppendLine("# Created by LUT Creator for DwmLutGUI");
            sb.AppendLine("# https://github.com/DeDuplicate/dwm_lut");
            sb.AppendLine();
            sb.Append("LUT_3D_SIZE ").AppendLine(size.ToString());
            sb.AppendLine();

            double div = size - 1;

            for (int bi = 0; bi < size; bi++)
            {
                for (int gi = 0; gi < size; gi++)
                {
                    for (int ri = 0; ri < size; ri++)
                    {
                        double r = ri / div;
                        double g = gi / div;
                        double b = bi / div;

                        ApplyAdjustments(r, g, b, parms, out double ro, out double go, out double bo);

                        sb.Append(ro.ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
                          .Append(go.ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
                          .AppendLine(bo.ToString("F6", CultureInfo.InvariantCulture));
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Write a .cube file to disk at the specified path.
        /// </summary>
        public static void WriteCubeFile(LutParams parms, int size, string filePath)
        {
            var content = GenerateCubeFile(parms, size);
            System.IO.File.WriteAllText(filePath, content);
        }

        #region HSL Conversion

        private static void RgbToHsl(double r, double g, double b,
            out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            h = 0;
            s = 0;

            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
                else if (max == g) h = ((b - r) / d + 2.0) / 6.0;
                else h = ((r - g) / d + 4.0) / 6.0;
            }
        }

        private static void HslToRgb(double h, double s, double l,
            out double r, out double g, out double b)
        {
            if (s == 0)
            {
                r = g = b = l;
                return;
            }

            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            r = Hue2Rgb(p, q, h + 1.0 / 3.0);
            g = Hue2Rgb(p, q, h);
            b = Hue2Rgb(p, q, h - 1.0 / 3.0);
        }

        private static double Hue2Rgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        #endregion
    }
}
