using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace DynamicIslandMedia.Helpers
{
    public static class AlbumColorExtractor
    {
        // High quality fallback colors when album artwork is unavailable
        public static readonly Color FallbackColor1 = Color.FromRgb(59, 70, 160);  // Rich Indigo
        public static readonly Color FallbackColor2 = Color.FromRgb(162, 58, 140); // Deep Violet/Pink

        private sealed class ColorCluster
        {
            public int R { get; set; }
            public int G { get; set; }
            public int B { get; set; }
            public double H { get; set; }
            public double S { get; set; }
            public double L { get; set; }
            public int PixelCount { get; set; }

            public Color ToColor()
            {
                // If it's a pastel color (very high luminance but noticeable saturation, like pastel pink or soft cyan),
                // tune L slightly (to ~0.78) so the true hue remains clearly visible and vibrant in ambient glow
                if (this.S >= 0.20 && this.L > 0.85)
                {
                    return HslToRgbColor(this.H, Math.Clamp(this.S * 1.15, 0.45, 0.85), 0.78);
                }

                // If it's very dark and saturated, lift L slightly so it doesn't vanish into pure black
                if (this.S >= 0.20 && this.L < 0.20)
                {
                    return HslToRgbColor(this.H, this.S, 0.32);
                }

                return Color.FromRgb((byte)this.R, (byte)this.G, (byte)this.B);
            }
        }

        /// <summary>
        /// Accurately extracts the most dominant color (Color 1) and second most color / key visual accent (Color 2)
        /// from the album artwork without arbitrarily discarding white, grays, or pastels.
        /// </summary>
        public static (Color DominantColor, Color SecondaryColor) ExtractColors(ImageSource? imageSource)
        {
            if (imageSource is not BitmapSource bitmapSource || bitmapSource.PixelWidth <= 0 || bitmapSource.PixelHeight <= 0)
            {
                return (FallbackColor1, FallbackColor2);
            }

            try
            {
                // 1. Fast downsampling to 48x48 (2,304 pixels)
                int sampleSize = 48;
                var scale = new ScaleTransform(
                    (double)sampleSize / bitmapSource.PixelWidth,
                    (double)sampleSize / bitmapSource.PixelHeight);

                var scaled = new TransformedBitmap(bitmapSource, scale);
                var formatted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

                int stride = sampleSize * 4;
                byte[] pixels = new byte[sampleSize * stride];
                formatted.CopyPixels(pixels, stride, 0);

                // 2. 3D RGB Color Bucketing (step = 24)
                const int step = 24;
                var bucketAccumulators = new Dictionary<int, (long SumR, long SumG, long SumB, int Count)>();

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];

                    if (a < 64) continue; // Skip transparent pixels

                    int key = ((r / step) << 16) | ((g / step) << 8) | (b / step);

                    if (!bucketAccumulators.TryGetValue(key, out var acc))
                    {
                        bucketAccumulators[key] = (r, g, b, 1);
                    }
                    else
                    {
                        bucketAccumulators[key] = (acc.SumR + r, acc.SumG + g, acc.SumB + b, acc.Count + 1);
                    }
                }

                if (bucketAccumulators.Count == 0)
                {
                    return (FallbackColor1, FallbackColor2);
                }

                // 3. Build clusters and compute HSL
                var clusters = new List<ColorCluster>(bucketAccumulators.Count);
                foreach (var kvp in bucketAccumulators)
                {
                    var acc = kvp.Value;
                    if (acc.Count < 4) continue; // Ignore tiny noise

                    int avgR = (int)(acc.SumR / acc.Count);
                    int avgG = (int)(acc.SumG / acc.Count);
                    int avgB = (int)(acc.SumB / acc.Count);

                    RgbToHsl((byte)avgR, (byte)avgG, (byte)avgB, out double h, out double s, out double l);

                    clusters.Add(new ColorCluster
                    {
                        R = avgR,
                        G = avgG,
                        B = avgB,
                        H = h,
                        S = s,
                        L = l,
                        PixelCount = acc.Count
                    });
                }

                if (clusters.Count == 0)
                {
                    return (FallbackColor1, FallbackColor2);
                }

                // Sort by pixel count descending (most dominant first)
                clusters.Sort((a, b) => b.PixelCount.CompareTo(a.PixelCount));

                // Dominant Color (Color 1): most frequent cluster
                var c1 = clusters[0];

                // Candidates for Secondary Color (Color 2): must be distinct from Color 1
                var distinctCandidates = clusters
                    .Where(c => IsDistinct(c, c1))
                    .ToList();

                ColorCluster c2;
                if (c1.S < 0.18)
                {
                    // Case A: Dominant is neutral (White, Off-white, Gray, Black).
                    // Prioritize the most frequent chromatic/vibrant cluster (e.g. Pink watercolor)!
                    var chromatic = distinctCandidates
                        .Where(c => c.S >= 0.18)
                        .OrderByDescending(c => c.PixelCount)
                        .FirstOrDefault();

                    if (chromatic != null)
                    {
                        c2 = chromatic;
                    }
                    else if (distinctCandidates.Count > 0)
                    {
                        // Pure monochrome artwork (black & white): pick highest contrast neutral
                        c2 = distinctCandidates.OrderByDescending(c => ColorDistance(c, c1)).First();
                    }
                    else
                    {
                        c2 = c1;
                    }
                }
                else
                {
                    // Case B: Dominant is chromatic (e.g. Blue, Purple, Red, Green).
                    // Prefer the highest count distinct cluster
                    if (distinctCandidates.Count > 0)
                    {
                        c2 = distinctCandidates[0];
                    }
                    else
                    {
                        c2 = c1;
                    }
                }

                Color finalColor1 = c1.ToColor();
                Color finalColor2;

                if (ReferenceEquals(c2, c1) || ColorDistance(c1, c2) < 20)
                {
                    // Harmonious fallback if image is 100% monochrome flat color
                    double derivedH = (c1.H + 35.0) % 360.0;
                    double derivedS = Math.Clamp(c1.S * 0.9, 0.35, 0.75);
                    double derivedL = c1.L > 0.5 ? Math.Max(0.35, c1.L - 0.25) : Math.Min(0.70, c1.L + 0.25);
                    finalColor2 = HslToRgbColor(derivedH, derivedS, derivedL);
                }
                else
                {
                    finalColor2 = c2.ToColor();
                }

                return (finalColor1, finalColor2);
            }
            catch
            {
                return (FallbackColor1, FallbackColor2);
            }
        }

        private static bool IsDistinct(ColorCluster a, ColorCluster b)
        {
            double dist = ColorDistance(a, b);
            if (dist >= 38.0) return true;

            // One is neutral and the other has noticeable saturation
            if (Math.Abs(a.S - b.S) >= 0.18 && dist >= 16.0) return true;

            // Both have noticeable saturation but distinct hue
            if (a.S >= 0.18 && b.S >= 0.18)
            {
                double hueDiff = Math.Abs(a.H - b.H);
                if (hueDiff > 180.0) hueDiff = 360.0 - hueDiff;
                if (hueDiff >= 25.0 && dist >= 18.0) return true;
            }

            return false;
        }

        private static double ColorDistance(ColorCluster a, ColorCluster b)
        {
            double dr = a.R - b.R;
            double dg = a.G - b.G;
            double db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static Color HslToRgbColor(double h, double s, double l)
        {
            HslToRgb(h, s, l, out byte r, out byte g, out byte b);
            return Color.FromRgb(r, g, b);
        }

        private static void RgbToHsl(byte rByte, byte gByte, byte bByte, out double h, out double s, out double l)
        {
            double r = rByte / 255.0;
            double g = gByte / 255.0;
            double b = bByte / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            l = (max + min) / 2.0;

            if (delta == 0)
            {
                h = 0;
                s = 0;
            }
            else
            {
                s = (l > 0.5) ? (delta / (2.0 - max - min)) : (delta / (max + min));

                if (max == r)
                {
                    h = ((g - b) / delta) % 6.0;
                }
                else if (max == g)
                {
                    h = ((b - r) / delta) + 2.0;
                }
                else
                {
                    h = ((r - g) / delta) + 4.0;
                }

                h *= 60.0;
                if (h < 0)
                {
                    h += 360.0;
                }
            }
        }

        private static void HslToRgb(double h, double s, double l, out byte rByte, out byte gByte, out byte bByte)
        {
            if (s == 0)
            {
                byte val = (byte)Math.Clamp((int)Math.Round(l * 255.0), 0, 255);
                rByte = val;
                gByte = val;
                bByte = val;
                return;
            }

            double q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
            double p = (2.0 * l) - q;
            double hk = h / 360.0;

            double tr = hk + (1.0 / 3.0);
            double tg = hk;
            double tb = hk - (1.0 / 3.0);

            rByte = (byte)Math.Clamp((int)Math.Round(ColorCalc(p, q, tr) * 255.0), 0, 255);
            gByte = (byte)Math.Clamp((int)Math.Round(ColorCalc(p, q, tg) * 255.0), 0, 255);
            bByte = (byte)Math.Clamp((int)Math.Round(ColorCalc(p, q, tb) * 255.0), 0, 255);
        }

        private static double ColorCalc(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < (1.0 / 6.0)) return p + ((q - p) * 6.0 * t);
            if (t < 0.5) return q;
            if (t < (2.0 / 3.0)) return p + ((q - p) * ((2.0 / 3.0) - t) * 6.0);
            return p;
        }
    }
}
