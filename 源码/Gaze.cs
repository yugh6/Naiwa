using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NaiwaDesktop
{
    // The original artwork is a side profile. Move its visible pupil by deforming
    // pixels inside the iris; the outline, eyelids and every body pixel stay intact.
    internal sealed class Gaze : IDisposable
    {
        private const double EyeX = 124.0, EyeY = 67.5;
        private const double RadiusX = 8.2, RadiusY = 8.5;
        private const double TravelX = 2.6, TravelY = 2.2;
        private readonly int width, height;
        private readonly byte[] original, pixels;
        private readonly Bitmap frame;
        private double x, y, previous = -1;
        private int pixelX, pixelY;

        internal Gaze(Bitmap neutral)
        {
            if (neutral == null) throw new ArgumentNullException("neutral");
            width = neutral.Width;
            height = neutral.Height;
            // These landmarks belong to the supplied 288 x 368 original artwork.
            // Refuse unsupported artwork rather than deforming an unrelated face.
            if (width != 288 || height != 368)
                throw new ArgumentException("Gaze requires the original 288 x 368 artwork.", "neutral");
            frame = neutral.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppPArgb);
            original = new byte[width * height * 4];
            pixels = new byte[original.Length];
            BitmapData data = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, original, 0, original.Length); }
            finally { frame.UnlockBits(data); }
            VisualKey = "0,0";
        }

        // Borrowed bitmap, owned and disposed by this Gaze instance.
        internal Bitmap Frame { get { return frame; } }
        internal string VisualKey { get; private set; }
        internal double Horizontal { get { return x; } }
        internal double Vertical { get { return y; } }

        internal bool Update(double now, Point cursor, Rectangle petBounds)
        {
            if (petBounds.Width <= 0 || petBounds.Height <= 0) return false;
            double eyeScreenX = petBounds.Left + EyeX * petBounds.Width / width;
            double eyeScreenY = petBounds.Top + EyeY * petBounds.Height / height;
            double targetX = (cursor.X - eyeScreenX) / Math.Max(100.0, petBounds.Width * .75);
            double targetY = (cursor.Y - eyeScreenY) / Math.Max(80.0, petBounds.Height * .45);
            double distance = Math.Sqrt(targetX * targetX + targetY * targetY);
            if (distance > 1) { targetX /= distance; targetY /= distance; }
            double dt = previous < 0 ? 1.0 / 60 : Math.Max(0, Math.Min(.1, now - previous));
            previous = now;
            double blend = 1 - Math.Exp(-14 * dt);
            x += (targetX - x) * blend;
            y += (targetY - y) * blend;
            return SetPixels((int)Math.Round(x * TravelX * 8), (int)Math.Round(y * TravelY * 8));
        }

        internal void Reset()
        {
            previous = -1;
            x = y = 0;
            SetPixels(0, 0);
        }

        private bool SetPixels(int nextX, int nextY)
        {
            if (nextX == pixelX && nextY == pixelY) return false;
            pixelX = nextX;
            pixelY = nextY;
            VisualKey = pixelX + "," + pixelY;
            Buffer.BlockCopy(original, 0, pixels, 0, original.Length);
            if (pixelX != 0 || pixelY != 0)
            {
                double offsetX = pixelX / 8.0, offsetY = pixelY / 8.0;
                int left = (int)Math.Ceiling(EyeX - RadiusX), right = (int)Math.Floor(EyeX + RadiusX);
                int top = (int)Math.Ceiling(EyeY - RadiusY), bottom = (int)Math.Floor(EyeY + RadiusY);
                for (int py = top; py <= bottom; py++)
                    for (int px = left; px <= right; px++)
                    {
                        double nx = (px - EyeX) / RadiusX, ny = (py - EyeY) / RadiusY;
                        double radius = Math.Sqrt(nx * nx + ny * ny);
                        if (radius >= 1) continue;
                        double weight = Math.Min(1, (1 - radius) / .65);
                        weight = weight * weight * (3 - 2 * weight);
                        Sample(px - offsetX * weight, py - offsetY * weight, (py * width + px) * 4);
                    }
            }
            BitmapData data = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { frame.UnlockBits(data); }
            return true;
        }

        private void Sample(double sx, double sy, int destination)
        {
            int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
            double fx = sx - ix, fy = sy - iy;
            int a = (iy * width + ix) * 4, b = a + 4, c = a + width * 4, d = c + 4;
            for (int channel = 0; channel < 4; channel++)
                pixels[destination + channel] = (byte)Math.Round(
                    (original[a + channel] * (1 - fx) + original[b + channel] * fx) * (1 - fy) +
                    (original[c + channel] * (1 - fx) + original[d + channel] * fx) * fy);
        }

        public void Dispose() { frame.Dispose(); }
    }
}
