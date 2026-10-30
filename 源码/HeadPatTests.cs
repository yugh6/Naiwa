using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace NaiwaDesktop
{
    internal static class HeadPatTests
    {
        internal static void Run(Bitmap neutral, string folder, List<string> log)
        {
            Check(HeadPat.Contains(136, 50) && HeadPat.Contains(124, 68) && HeadPat.Contains(108, 83), "head pat accepts crown, eye and cheek", log);
            Check(!HeadPat.Contains(144, 211) && !HeadPat.Contains(144, 144) && !HeadPat.Contains(144, 320), "head pat excludes belly and feet", log);
            Check(!HeadPat.Contains(60, 68) && !HeadPat.Contains(136, 20), "head pat excludes surrounding transparent area", log);
            double[] times = { 0, .08, .16, .32, .45, .65, HeadPat.Duration };
            for (int i = 0; i < times.Length; i++)
                using (Bitmap frame = HeadPat.Render(neutral, times[i]))
                {
                    Check(Equal(neutral, frame, 140, neutral.Height), "head pat preserves lower body at " + times[i].ToString("F2"), log);
                    if (times[i] == 0 || times[i] == HeadPat.Duration)
                        Check(Equal(neutral, frame, 0, neutral.Height), "head pat restores original at " + times[i].ToString("F2"), log);
                    if (times[i] == .16)
                    {
                        Check(!Equal(neutral, frame, 40, 115), "head pat visibly deforms head at peak", log);
                        Check(Top(frame) >= Top(neutral) + 3 && Top(frame) <= Top(neutral) + 7, "head pat gently lowers crown without collapsing face", log);
                    }
                    frame.Save(Path.Combine(folder, "head-pat-" + i + ".png"), ImageFormat.Png);
                }
            using (Bitmap ended = HeadPat.Render(neutral, 10))
                Check(Equal(neutral, ended, 0, neutral.Height), "head pat remains settled after completion", log);
        }

        private static int Top(Bitmap bitmap)
        {
            for (int y = 0; y < 140; y++)
                for (int x = 90; x < 185; x++)
                    if (bitmap.GetPixel(x, y).A > 128) return y;
            return 140;
        }

        private static bool Equal(Bitmap a, Bitmap b, int fromY, int toY)
        {
            for (int y = fromY; y < toY; y++)
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
