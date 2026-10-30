using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace NaiwaDesktop
{
    // Owned by one PetWindow. Returned bitmaps are borrowed until the next call;
    // the window uploads them before rendering again and disposes this workspace.
    internal sealed class RenderWorkspace : IDisposable
    {
        private Bitmap canvas, belly;
        private byte[] source, result;
        private bool disposed;

        internal Bitmap Render(Assets assets, Motion motion, int scale, Bitmap gazeFrame = null)
        {
            if (disposed) throw new ObjectDisposedException("RenderWorkspace");
            int width = assets.Width * scale / 100, height = assets.Height * scale / 100;
            if (canvas == null || canvas.Width != width || canvas.Height != height)
            {
                Bitmap replacement = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                if (canvas != null) canvas.Dispose();
                canvas = replacement;
            }
            Renderer.RenderInto(canvas, assets, motion, scale, gazeFrame, this);
            return canvas;
        }

        internal Bitmap WarpBelly(Bitmap original, double x, double y, double amount)
        {
            if (disposed) throw new ObjectDisposedException("RenderWorkspace");
            int width = original.Width, height = original.Height;
            if (belly == null || belly.Width != width || belly.Height != height)
            {
                Bitmap replacement = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                if (belly != null) belly.Dispose();
                belly = replacement;
                source = new byte[width * height * 4];
                result = new byte[source.Length];
            }
            Renderer.WarpBellyInto(original, x, y, amount, source, result, belly);
            return belly;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (canvas != null) canvas.Dispose();
            if (belly != null) belly.Dispose();
            canvas = belly = null;
            source = result = null;
        }
    }

    // The worker exclusively owns decoding results until Take transfers ownership
    // to the UI cache. It never reads or evicts frames currently being painted.
    internal sealed class FramePrefetcher : IDisposable
    {
        private readonly object gate = new object();
        private readonly Func<int, Bitmap> decode;
        private readonly Dictionary<int, Bitmap> ready = new Dictionary<int, Bitmap>();
        private readonly HashSet<int> failed = new HashSet<int>();
        private int[] requested = new int[0];
        private bool running, disposed;
        private Exception fault;

        internal FramePrefetcher(Func<int, Bitmap> decodeFrame)
        {
            if (decodeFrame == null) throw new ArgumentNullException("decodeFrame");
            decode = decodeFrame;
        }

        internal int ReadyCount { get { lock (gate) return ready.Count; } }
        internal bool IsIdle { get { lock (gate) return !running; } }

        internal void Request(int[] indices)
        {
            if (indices == null) throw new ArgumentNullException("indices");
            if (indices.Length > 4) throw new ArgumentException("At most four frames may be prefetched.", "indices");
            lock (gate)
            {
                if (disposed) return;
                ThrowIfFaulted();
                bool changed = indices.Length != requested.Length;
                for (int i = 0; !changed && i < indices.Length; i++) changed = indices[i] != requested[i];
                if (!changed) return;
                requested = (int[])indices.Clone();
                failed.Clear();
                List<int> stale = new List<int>();
                foreach (int index in ready.Keys) if (Array.IndexOf(requested, index) < 0) stale.Add(index);
                foreach (int index in stale) { ready[index].Dispose(); ready.Remove(index); }
                if (!running && requested.Length > 0)
                {
                    running = true;
                    ThreadPool.QueueUserWorkItem(delegate { Work(); });
                }
            }
        }

        internal Bitmap Take(int index)
        {
            lock (gate)
            {
                Bitmap image;
                if (disposed) return null;
                ThrowIfFaulted();
                if (!ready.TryGetValue(index, out image)) return null;
                ready.Remove(index);
                // Do not decode this frame a second time between Take and Request.
                List<int> remaining = new List<int>(requested);
                remaining.Remove(index);
                requested = remaining.ToArray();
                return image;
            }
        }

        private void Work()
        {
            while (true)
            {
                int index = -1;
                lock (gate)
                {
                    if (!disposed)
                        foreach (int candidate in requested)
                            if (!ready.ContainsKey(candidate) && !failed.Contains(candidate)) { index = candidate; break; }
                    if (index < 0) { running = false; return; }
                }
                Bitmap decoded = null;
                try { decoded = decode(index); }
                // Invalid PNG data is retried on the foreground path, where the
                // existing error handler can identify the requested asset.
                catch (ArgumentException) { }
                catch (ExternalException) { }
                catch (InvalidDataException) { }
                catch (Exception unexpected)
                {
                    // Preserve unexpected failures instead of silently treating
                    // allocation or programming faults as a cache miss. Throwing
                    // directly on a ThreadPool thread would terminate the process.
                    lock (gate) { if (!disposed) fault = unexpected; running = false; }
                    return;
                }
                lock (gate)
                {
                    if (disposed || Array.IndexOf(requested, index) < 0)
                    {
                        if (decoded != null) decoded.Dispose();
                    }
                    else if (decoded != null) ready.Add(index, decoded);
                    else failed.Add(index);
                }
            }
        }

        private void ThrowIfFaulted()
        {
            if (fault != null) throw new InvalidOperationException("动作后台解码失败。", fault);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                requested = new int[0];
                foreach (Bitmap image in ready.Values) image.Dispose();
                ready.Clear();
                // A decode already in flight completes independently, then disposes
                // its result. Closing the pet never waits for PNG decoding.
            }
        }
    }
}
