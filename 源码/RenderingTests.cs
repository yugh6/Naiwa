using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace NaiwaDesktop
{
    internal static class RenderingTests
    {
        internal static void Run(Assets assets, List<string> log)
        {
            Bitmap original = assets.Eye(0);
            using (Bitmap untouched = (Bitmap)original.Clone())
            using (RenderWorkspace workspace = new RenderWorkspace())
            {
                Bitmap warped = workspace.WarpBelly(original, 144, 211, .55);
                bool preserved = true, changed = false;
                for (int y = 0; y < original.Height; y++)
                    for (int x = 0; x < original.Width; x++)
                    {
                        bool same = original.GetPixel(x, y) == warped.GetPixel(x, y);
                        if ((y < 105 || y > 300) && !same) preserved = false;
                        if (!same) changed = true;
                    }
                Check(changed && preserved, "reused belly warp changes belly and preserves face and feet", log);
                Bitmap reset = workspace.WarpBelly(original, 144, 211, 0);
                Check(Object.ReferenceEquals(warped, reset) && Equal(reset, original), "belly buffer reused without leaving previous deformation", log);
                Check(Equal(untouched, original), "reusable warp never changes the source artwork", log);

                Motion motion = new Motion();
                Bitmap first = workspace.Render(assets, motion, 85);
                using (Bitmap idle = (Bitmap)first.Clone())
                {
                    motion.Poke(1, 144, 211); motion.Update(1.08, false, assets.Duration, assets.BlinkCount / assets.BlinkFps);
                    Bitmap poke = workspace.Render(assets, motion, 85);
                    Check(Object.ReferenceEquals(first, poke) && !Equal(idle, poke), "runtime canvas reused during belly interaction", log);
                    motion.Stop();
                    Bitmap restored = workspace.Render(assets, motion, 85);
                    Check(Equal(idle, restored), "reused canvas clears animation and transparent edge remnants", log);
                }
                Bitmap resized = workspace.Render(assets, motion, 110);
                Check(!Object.ReferenceEquals(first, resized) && resized.Width == assets.Width * 110 / 100 && resized.Height == assets.Height * 110 / 100,
                    "canvas rebuilt only for changed dimensions", log);
            }

            // Golden pixels were captured by compiling the unchanged WarpBelly
            // from commit 21ef2d3, independently of the new reusable code path.
            using (Bitmap pattern = Pattern())
            using (RenderWorkspace workspace = new RenderWorkspace())
            {
                double[,] cases = { { 144, 211, .55 }, { 120, 190, -.16 }, { 166, 240, .31 } };
                string[] expected = {
                    "67142D604D45F6B34178199E8AB95799C698FCCD1C54226B703ED2638032B534",
                    "18BAB89C468018D77917575BD41ED1B82B3DB0ABEAA3E1962FEA0CF257292762",
                    "3BA24D3513BE27760B470D0E82B300DBB7E2C757D5F63E05787FB778981EEF5A"
                };
                for (int i = 0; i < expected.Length; i++)
                    Check(Hash(workspace.WarpBelly(pattern, cases[i, 0], cases[i, 1], cases[i, 2])) == expected[i],
                        "reusable belly pixels match original implementation, case " + i, log);
            }

            int foreground = Thread.CurrentThread.ManagedThreadId, worker = foreground;
            Bitmap transferred;
            using (FramePrefetcher prefetch = new FramePrefetcher(delegate(int index)
            {
                worker = Thread.CurrentThread.ManagedThreadId;
                Bitmap frame = new Bitmap(2, 2); frame.SetPixel(0, 0, Color.FromArgb(255, index, 0, 0)); return frame;
            }))
            {
                prefetch.Request(new int[] { 1, 2, 3, 4 });
                WaitIdle(prefetch);
                Check(worker != foreground && prefetch.ReadyCount == 4, "lookahead decodes on worker with four-frame bound", log);
                transferred = prefetch.Take(2);
                Check(transferred != null && transferred.GetPixel(0, 0).R == 2 && prefetch.Take(2) == null,
                    "prefetched frame transfers once to foreground ownership", log);
                prefetch.Request(new int[] { 20, 21 });
                WaitIdle(prefetch);
                Check(prefetch.ReadyCount == 2 && prefetch.Take(1) == null, "seek discards stale prefetched frames", log);
            }
            using (transferred) Check(transferred.GetPixel(0, 0).R == 2, "worker disposal preserves transferred frame", log);

            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                Bitmap late = null;
                FramePrefetcher prefetch = new FramePrefetcher(delegate(int index)
                {
                    entered.Set(); release.WaitOne(); late = new Bitmap(2, 2); return late;
                });
                try
                {
                    prefetch.Request(new int[] { 8 });
                    Check(entered.WaitOne(2000), "background decode can be in flight during close", log);
                    prefetch.Dispose(); release.Set(); WaitIdle(prefetch);
                    Check(prefetch.ReadyCount == 0 && prefetch.Take(8) == null && Disposed(late),
                        "close discards and disposes in-flight decode without publishing it", log);
                }
                finally { release.Set(); prefetch.Dispose(); WaitIdle(prefetch); }
            }

            using (FramePrefetcher prefetch = new FramePrefetcher(delegate(int index)
            {
                if (index == 1) throw new ArgumentException("test decode failure");
                return new Bitmap(2, 2);
            }))
            {
                prefetch.Request(new int[] { 1, 2 }); WaitIdle(prefetch);
                using (Bitmap next = prefetch.Take(2))
                    Check(prefetch.Take(1) == null && next != null, "decode failure does not crash or stall later lookahead frames", log);
            }

            OutOfMemoryException unexpected = new OutOfMemoryException("Simulated decode allocation failure.");
            using (FramePrefetcher prefetch = new FramePrefetcher(delegate(int index) { throw unexpected; }))
            {
                prefetch.Request(new int[] { 1 }); WaitIdle(prefetch);
                bool surfaced = false;
                try { prefetch.Take(1); }
                catch (InvalidOperationException e) { surfaced = Object.ReferenceEquals(e.InnerException, unexpected); }
                Check(surfaced && prefetch.IsIdle, "unexpected worker fault is surfaced on foreground with original cause", log);
                surfaced = false;
                try { prefetch.Request(new int[] { 2 }); }
                catch (InvalidOperationException e) { surfaced = Object.ReferenceEquals(e.InnerException, unexpected); }
                Check(surfaced, "faulted worker does not silently restart or deadlock", log);
                prefetch.Dispose(); prefetch.Dispose();
            }
        }

        private static Bitmap Pattern()
        {
            Bitmap image = new Bitmap(288, 368, PixelFormat.Format32bppPArgb);
            byte[] bytes = new byte[288 * 368 * 4];
            for (int y = 0; y < 368; y++) for (int x = 0; x < 288; x++)
            {
                int i = (y * 288 + x) * 4, alpha = 64 + (x * 19 + y * 23) % 192;
                bytes[i] = (byte)((x * 13 + y * 17) % (alpha + 1));
                bytes[i + 1] = (byte)((x * 5 + y * 11) % (alpha + 1));
                bytes[i + 2] = (byte)((x * 7 + y * 3) % (alpha + 1));
                bytes[i + 3] = (byte)alpha;
            }
            BitmapData data = image.LockBits(new Rectangle(0, 0, 288, 368), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(bytes, 0, data.Scan0, bytes.Length); }
            finally { image.UnlockBits(data); }
            return image;
        }

        private static string Hash(Bitmap image)
        {
            byte[] bytes = new byte[image.Width * image.Height * 4];
            BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, bytes, 0, bytes.Length); }
            finally { image.UnlockBits(data); }
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        private static bool Equal(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return false;
            Rectangle bounds = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData ad = a.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                BitmapData bd = b.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    byte[] aa = new byte[a.Width * a.Height * 4], bb = new byte[aa.Length];
                    Marshal.Copy(ad.Scan0, aa, 0, aa.Length); Marshal.Copy(bd.Scan0, bb, 0, bb.Length);
                    for (int i = 0; i < aa.Length; i++) if (aa[i] != bb[i]) return false;
                    return true;
                }
                finally { b.UnlockBits(bd); }
            }
            finally { a.UnlockBits(ad); }
        }

        private static bool Disposed(Bitmap image)
        {
            if (image == null) return false;
            try { return image.Width < 0; }
            catch (ArgumentException) { return true; }
        }

        private static void WaitIdle(FramePrefetcher prefetch)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!prefetch.IsIdle && timeout.ElapsedMilliseconds < 3000) Thread.Sleep(2);
            if (!prefetch.IsIdle) throw new Exception("Lookahead worker did not become idle.");
        }

        private static void Check(bool value, string name, List<string> log)
        {
            log.Add((value ? "PASS: " : "FAIL: ") + name);
            if (!value) throw new Exception(name);
        }
    }
}
