using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIslandMedia.Rendering
{
    /// <summary>
    /// Background glow for the expanded island: a soft colour mesh (blue, aqua and their blend) whose three
    /// points drift slowly side to side and up/down, under a single fade from the bottom edge to transparent.
    /// No edges or shapes. Rendered on the CPU at device resolution into a premultiplied bitmap; output is
    /// dithered because a slow, dark 8-bit gradient without dither quantizes into horizontal bands.
    /// </summary>
    internal sealed class AmbientGlowRenderer
    {
        private const double Tau = Math.PI * 2.0;

        // "Clear sky" palette (sRGB), chosen by the user: #5C9DFF and #4FD1D9; the third point is their blend.
        private const float BlueR = 92f, BlueG = 157f, BlueB = 255f;
        private const float AquaR = 79f, AquaG = 209f, AquaB = 217f;

        // Vertical fade: transparent above FadeTop, rising to PeakAlpha at the bottom edge
        // (before the layer's own opacity from AnimateAmbientOpacity).
        private const float FadeTop = 0.32f, FadeBottom = 1.05f, FadeCurve = 1.3f, PeakAlpha = 0.72f;

        // Point influence: inverse-square falloff, wider than tall.
        private const float SpreadX = 0.65f, SpreadY = 0.5f, Softness = 0.04f;

        private int _width, _height;
        private byte[] _pixels = Array.Empty<byte>();
        private float[] _dither = Array.Empty<float>();
        private float[] _rowAlpha = Array.Empty<float>();
        private readonly float[][] _dx = new float[3][];

        public WriteableBitmap? Bitmap { get; private set; }

        /// <summary>Creates the bitmap for the given device-pixel size. Returns true when it was (re)created.</summary>
        public bool EnsureSize(int width, int height, double dpiScaleX, double dpiScaleY)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (Bitmap != null && width == _width && height == _height &&
                Math.Abs(Bitmap.DpiX - 96.0 * dpiScaleX) < 0.01 && Math.Abs(Bitmap.DpiY - 96.0 * dpiScaleY) < 0.01)
                return false;

            _width = width;
            _height = height;
            _pixels = new byte[width * height * 4];
            for (int p = 0; p < 3; p++) _dx[p] = new float[width];

            _rowAlpha = new float[height];

            // Interleaved gradient noise in [0,1): fixed, sub-LSB, invisible except that it breaks bands.
            _dither = new float[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    _dither[y * width + x] = (float)Frac(52.9829189 * Frac(0.06711056 * x + 0.00583715 * y));

            Bitmap = new WriteableBitmap(width, height, 96.0 * dpiScaleX, 96.0 * dpiScaleY, PixelFormats.Pbgra32, null);
            return true;
        }

        public void Render(double t)
        {
            if (Bitmap == null) return;
            int w = _width, h = _height;

            // Three points on slow, incommensurate cycles (5-10.5 s): visible but calm side-to-side and up/down drift.
            Span<float> px = stackalloc float[3], py = stackalloc float[3];
            px[0] = (float)(0.24 + 0.17 * Math.Sin(Tau * t / 7.7));
            py[0] = (float)(0.95 + 0.10 * Math.Sin(Tau * t / 5.2));
            px[1] = (float)(0.76 + 0.16 * Math.Sin(Tau * t / 8.8 + 1.7));
            py[1] = (float)(0.92 + 0.10 * Math.Sin(Tau * t / 6.1 + 0.6));
            px[2] = (float)(0.50 + 0.26 * Math.Sin(Tau * t / 10.5 + 3.0));
            py[2] = (float)(0.80 + 0.09 * Math.Sin(Tau * t / 7.0 + 2.0));

            // Breathing: the whole glow rises and settles (+/-7% of the height, 6.4 s), still edgeless.
            float fadeTop = FadeTop + 0.07f * (float)Math.Sin(Tau * t / 6.4);
            for (int y = 0; y < h; y++)
            {
                float f = Math.Clamp(((y + 0.5f) / h - fadeTop) / (FadeBottom - fadeTop), 0f, 1f);
                _rowAlpha[y] = (float)Math.Pow(f * f * (3f - 2f * f), FadeCurve) * PeakAlpha;
            }

            for (int p = 0; p < 3; p++)
            {
                float[] dx = _dx[p];
                for (int x = 0; x < w; x++)
                {
                    float d = ((x + 0.5f) / w - px[p]) / SpreadX;
                    dx[x] = d * d;
                }
            }

            const float MidR = (BlueR + AquaR) * 0.5f, MidG = (BlueG + AquaG) * 0.5f, MidB = (BlueB + AquaB) * 0.5f;
            float[] dx0 = _dx[0], dx1 = _dx[1], dx2 = _dx[2];

            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                float alpha = _rowAlpha[y];
                if (alpha <= 0f)
                {
                    Array.Clear(_pixels, row, w * 4); // above the fade: transparent
                    continue;
                }

                float fy = (y + 0.5f) / h;
                float e0 = (fy - py[0]) / SpreadY, e1 = (fy - py[1]) / SpreadY, e2 = (fy - py[2]) / SpreadY;
                float dy0 = e0 * e0 + Softness, dy1 = e1 * e1 + Softness, dy2 = e2 * e2 + Softness;
                int di = y * w;

                for (int x = 0; x < w; x++)
                {
                    float q0 = dx0[x] + dy0, q1 = dx1[x] + dy1, q2 = dx2[x] + dy2;
                    float w0 = 1f / (q0 * q0), w1 = 1f / (q1 * q1), w2 = 1f / (q2 * q2);
                    float k = alpha / (w0 + w1 + w2);
                    float u = _dither[di + x];

                    int a8 = Math.Min((int)(alpha * 255f + u), 255);
                    // Premultiplied: colour channels never exceed alpha.
                    int r8 = Math.Min((int)((BlueR * w0 + AquaR * w1 + MidR * w2) * k + u), a8);
                    int g8 = Math.Min((int)((BlueG * w0 + AquaG * w1 + MidG * w2) * k + u), a8);
                    int b8 = Math.Min((int)((BlueB * w0 + AquaB * w1 + MidB * w2) * k + u), a8);
                    int i = row + x * 4;
                    _pixels[i] = (byte)b8;
                    _pixels[i + 1] = (byte)g8;
                    _pixels[i + 2] = (byte)r8;
                    _pixels[i + 3] = (byte)a8;
                }
            }

            Bitmap.WritePixels(new Int32Rect(0, 0, w, h), _pixels, w * 4, 0);
        }

        private static double Frac(double v) => v - Math.Floor(v);
    }
}
