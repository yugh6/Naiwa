using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NaiwaDesktop
{
    internal static class HeadPat
    {
        internal const double Duration = .85;
        private const int NeckBase = 140;

        // Coordinates are in the original 288 x 368 artwork, before display scaling.
        // The head zone ends above the neck; it cannot overlap the belly gesture.
        internal static bool Contains(double x, double y)
        {
            double dx = (x - 136) / 39.0, dy = (y - 80) / 35.0;
            return dx * dx + dy * dy <= 1;
        }

        // Returns a new bitmap owned by the caller. The supplied frame can be the
        // normal face, a gaze frame, or one of the original closed-eye frames.
        internal static Bitmap Render(Bitmap source, double elapsed)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source.Width != 288 || source.Height != 368)
                throw new ArgumentException("Head pat requires the original 288 x 368 artwork.", "source");

            int width = source.Width, height = source.Height;
            Bitmap result = source.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppPArgb);
            double amount = Amount(elapsed);
            if (amount == 0) return result;

            byte[] original = new byte[width * height * 4];
            BitmapData data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, original, 0, original.Length); }
            finally { result.UnlockBits(data); }
            byte[] warped = (byte[])original.Clone();

            // A soft downward press and a slight forward nod. Displacement and its
            // slope fade to zero at the neck, so the body below it never changes.
            for (int y = 0; y < NeckBase; y++)
            {
                double influence = Smooth(Math.Min(1, (NeckBase - y) / 80.0));
                double offsetY = amount * .060 * (NeckBase - y) * influence;
                double offsetX = amount * 1.2 * influence;
                for (int x = 0; x < width; x++)
                    Sample(original, warped, width, height, x + offsetX, y - offsetY, (y * width + x) * 4);
            }

            data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(warped, 0, data.Scan0, warped.Length); }
            finally { result.UnlockBits(data); }
            return result;
        }

        private static double Amount(double elapsed)
        {
            if (elapsed <= 0 || elapsed >= Duration || Double.IsNaN(elapsed)) return 0;
            const double press = .16;
            if (elapsed < press) return Smooth(elapsed / press);
            double release = elapsed - press;
            double spring = Math.Exp(-7 * release) * Math.Cos(14 * release);
            return spring * Smooth(Math.Min(1, (Duration - elapsed) / .15));
        }

        private static double Smooth(double value) { return value * value * (3 - 2 * value); }

        private static void Sample(byte[] original, byte[] result, int width, int height, double sx, double sy, int destination)
        {
            if (sx < 0 || sy < 0 || sx >= width - 1 || sy >= height - 1)
            {
                result[destination] = result[destination + 1] = result[destination + 2] = result[destination + 3] = 0;
                return;
            }
            int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
            double fx = sx - ix, fy = sy - iy;
            int a = (iy * width + ix) * 4, b = a + 4, c = a + width * 4, d = c + 4;
            for (int channel = 0; channel < 4; channel++)
                result[destination + channel] = (byte)Math.Round(
                    (original[a + channel] * (1 - fx) + original[b + channel] * fx) * (1 - fy) +
                    (original[c + channel] * (1 - fx) + original[d + channel] * fx) * fy);
        }
    }
}
