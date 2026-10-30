using System;
using System.Collections.Generic;
using System.IO;

namespace NaiwaDesktop
{
    // Separate executable entry point; never run automatically at normal pet startup.
    // Build with /main:NaiwaDesktop.RecyclingIntegrationTests and Recycling.cs.
    internal static class RecyclingIntegrationTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1) { Console.Error.WriteLine("Supply a fixture output directory."); return 2; }
            string folder = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(folder);
            List<string> log = new List<string>();
            try
            {
                Recycling.RunValidationTests(folder, log);
                string fixture = Path.Combine(folder, "naiwa-recycle-integration-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(fixture, "Disposable Naiwa recycle integration fixture.");
                log.Add("FIXTURE: " + fixture);
                RecycleResult cancelled = Recycling.Recycle(new[] { fixture }, IntPtr.Zero, delegate { return true; });
                Check(cancelled.RecycledCount == 0 && cancelled.Cancelled && File.Exists(fixture), "cancelled operation preserves generated fixture", log);
                using (FileStream locked = new FileStream(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    RecycleResult blocked = Recycling.Recycle(new[] { fixture }, IntPtr.Zero);
                    Check(blocked.RecycledCount == 0 && File.Exists(fixture), "locked fixture is not recycled or reported successful", log);
                    string other = Path.Combine(folder, "naiwa-recycle-partial-" + Guid.NewGuid().ToString("N") + ".txt");
                    File.WriteAllText(other, "Disposable Naiwa partial-batch integration fixture.");
                    RecycleResult partial = Recycling.Recycle(new[] { other, fixture }, IntPtr.Zero);
                    Check(partial.RecycledCount == 1 && partial.FailedCount == 1 && partial.Items[0].Success && !partial.Items[1].Success && !File.Exists(other) && File.Exists(fixture), "partial batch reports only the file actually recycled", log);
                }
                RecycleResult result = Recycling.Recycle(new[] { fixture }, IntPtr.Zero);
                log.Add(result.Summary);
                foreach (RecycleItemResult item in result.Items) log.Add("NATIVE: pre=" + item.PreFlags.ToString("X") + " post=" + item.PostFlags.ToString("X") + " hr=" + item.NativeResult.ToString("X") + " recycled=" + item.HasRecycledItem);
                Check(result.RecycledCount == 1 && result.FailedCount == 0 && !File.Exists(fixture), "generated fixture reaches actual Windows Recycle Bin", log);
                log.Add("RESULT: PASS");
                Console.WriteLine(String.Join(Environment.NewLine, log.ToArray()));
                File.WriteAllLines(Path.Combine(folder, "recycling-tests.txt"), log.ToArray());
                return 0;
            }
            catch (Exception e)
            {
                log.Add(e.ToString()); log.Add("RESULT: FAIL");
                Console.WriteLine(String.Join(Environment.NewLine, log.ToArray()));
                File.WriteAllLines(Path.Combine(folder, "recycling-tests.txt"), log.ToArray());
                return 1;
            }
        }
        private static void Check(bool value, string name, List<string> log)
        { if (!value) throw new InvalidOperationException("FAIL: " + name); log.Add("PASS: " + name); }
    }
}
