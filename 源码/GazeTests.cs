using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace NaiwaDesktop
{
    internal static class GazeTests
    {
        internal static void Run(Bitmap neutral, string folder, List<string> log)
        {
            using (Gaze gaze = new Gaze(neutral))
            {
                Rectangle bounds = new Rectangle(0, 0, neutral.Width, neutral.Height);
                PointF original = PupilCenter(neutral);
                Point[] cursors = { new Point(-2000, 68), new Point(2000, 68), new Point(124, -2000), new Point(124, 2000) };
                string[] names = { "left", "right", "up", "down" };
                for (int direction = 0; direction < cursors.Length; direction++)
                {
                    gaze.Reset();
                    for (int step = 0; step <= 120; step++) gaze.Update(step / 60.0, cursors[direction], bounds);
                    PointF current = PupilCenter(gaze.Frame);
                    bool moved = direction == 0 ? current.X < original.X - .5f :
                        direction == 1 ? current.X > original.X + .5f :
                        direction == 2 ? current.Y < original.Y - .5f : current.Y > original.Y + .5f;
                    Check(moved, "pupil follows " + names[direction] + " cursor", log);
                    Check(OutsideEyeUnchanged(neutral, gaze.Frame), "gaze preserves body and eye outline, " + names[direction], log);
                    Check(Math.Abs(gaze.Horizontal) <= 1 && Math.Abs(gaze.Vertical) <= 1, "gaze stays bounded, " + names[direction], log);
                    gaze.Frame.Save(Path.Combine(folder, "gaze-" + names[direction] + ".png"), ImageFormat.Png);
                }
                gaze.Reset();
                Check(Equal(neutral, gaze.Frame), "reset restores exact original artwork", log);
                gaze.Update(0, new Point(2000, 68), bounds);
                Check(gaze.Horizontal > 0 && gaze.Horizontal < .5, "gaze eases toward target instead of snapping", log);
                Rectangle secondMonitor = new Rectangle(-1920, 100, 288, 368);
                gaze.Reset();
                for (int step = 0; step <= 120; step++) gaze.Update(step / 60.0, new Point(-1920 - 2000, 168), secondMonitor);
                Check(PupilCenter(gaze.Frame).X < original.X - .5f, "gaze handles negative monitor coordinates", log);
                string last = gaze.VisualKey;
                Check(!gaze.Update(2, new Point(-1920 - 2000, 168), secondMonitor) && gaze.VisualKey == last, "unchanged gaze avoids rebuilding the bitmap", log);
            }
        }

        private static PointF PupilCenter(Bitmap image)
        {
            double total = 0, xSum = 0, ySum = 0;
            for (int y = 59; y <= 76; y++)
                for (int x = 116; x <= 132; x++)
                {
                    Color color = image.GetPixel(x, y);
                    double weight = Math.Max(0, 80 - color.G);
                    total += weight; xSum += x * weight; ySum += y * weight;
                }
            if (total == 0) throw new Exception("No pupil found in original eye area.");
            return new PointF((float)(xSum / total), (float)(ySum / total));
        }

        private static bool OutsideEyeUnchanged(Bitmap a, Bitmap b)
        {
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                {
                    double nx = (x - 124.0) / 8.2, ny = (y - 67.5) / 8.5;
                    if (nx * nx + ny * ny >= 1 && a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
                    if (a.GetPixel(x, y).A != b.GetPixel(x, y).A) return false;
                }
            return true;
        }

        private static bool Equal(Bitmap a, Bitmap b)
        {
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
            return true;
        }

        private static void Check(bool value, string name, List<string> log)
        {
            if (!value) throw new Exception("FAIL: " + name);
            log.Add("PASS: " + name);
        }
    }
}
