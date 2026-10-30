using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace NaiwaDesktop
{
    internal sealed class RecycleItemResult
    {
        internal readonly string Path;
        internal bool Success, Cancelled;
        internal uint PreFlags, PostFlags;
        internal int NativeResult;
        internal bool HasRecycledItem;
        internal string Error;
        internal RecycleItemResult(string path) { Path = path; }
    }

    internal sealed class RecycleResult
    {
        internal readonly RecycleItemResult[] Items;
        internal RecycleResult(string[] paths)
        {
            Items = new RecycleItemResult[paths.Length];
            for (int i = 0; i < paths.Length; i++) Items[i] = new RecycleItemResult(paths[i]);
        }
        internal int RecycledCount { get { int n = 0; foreach (RecycleItemResult item in Items) if (item.Success) n++; return n; } }
        internal int FailedCount { get { return Items.Length - RecycledCount; } }
        internal bool Cancelled { get { foreach (RecycleItemResult item in Items) if (item.Cancelled) return true; return false; } }
        internal string Summary
        {
            get
            {
                string text = RecycledCount == Items.Length ? "已将 " + RecycledCount + " 个文件移入回收站。" : "已回收 " + RecycledCount + " 个文件，" + FailedCount + " 个未完成。";
                int shown = 0;
                foreach (RecycleItemResult item in Items)
                    if (!item.Success && shown++ < 5) text += "\n" + DisplayName(item.Path) + "：" + item.Error;
                return text;
            }
        }
        private static string DisplayName(string path)
        {
            try { return System.IO.Path.GetFileName(path) ?? "文件"; }
            catch (ArgumentException) { return "无效文件路径"; }
        }
    }

    // Ordinary local files only. There is deliberately no File.Delete/SHFileOperation fallback.
    internal static class Recycling
    {
        private const uint RecycleFlags = 0x00080000 | 0x20000000 | 0x00100000 | 0x00000400 | 0x00000004 | 0x00000010 | 0x00002000;
        // FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD | FOFX_EARLYFAILURE |
        // FOF_NOERRORUI | FOF_SILENT | FOF_NOCONFIRMATION | FOF_NO_CONNECTED_ELEMENTS.
        // The pet confirms the drop; PreDeleteItem vetoes permanent deletion even when the
        // Shell cannot recycle (disabled/full bin, unsupported item or changed settings).
        private const uint RecycleTransfer = 0x80; // TSF_DELETE_RECYCLE_IF_POSSIBLE
        private const int Abort = unchecked((int)0x80004004);
        private const int CancelledHResult = unchecked((int)0x800704C7);
        private const int RecycledWholeItem = 0x00270008; // COPYENGINE_S_DONT_PROCESS_CHILDREN

        internal static string Validate(string[] paths)
        {
            if (paths == null || paths.Length == 0) return "请拖入本地文件。";
            if (paths.Length > 100) return "一次最多喂 100 个文件。";
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string normalized, error = ValidatePath(path, out normalized);
                if (error != null) return error;
                if (!unique.Add(normalized)) return "同一个文件不能重复喂入。";
            }
            return null;
        }

        private static string ValidatePath(string path, out string normalized)
        {
            normalized = null;
            try
            {
                if (String.IsNullOrWhiteSpace(path) || path.Length < 4 || !Char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path.IndexOf(':', 2) >= 0)
                    return "只支持本地磁盘文件，不支持网络位置、虚拟项目或特殊路径。";
                normalized = System.IO.Path.GetFullPath(path);
                if (!String.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)) return "文件路径不明确，请从资源管理器重新拖入。";
                string[] parts = path.Substring(3).Split('\\');
                foreach (string part in parts)
                    if (part.Length == 0 || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)) return "不支持此文件路径。";
                string root = System.IO.Path.GetPathRoot(normalized);
                if (new DriveInfo(root).DriveType != DriveType.Fixed) return "只支持本地固定磁盘；U 盘和网络磁盘暂不能喂入。";
                string[] protectedFolders = {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    System.IO.Path.Combine(root, "$Recycle.Bin"),
                    System.IO.Path.Combine(root, "System Volume Information")
                };
                foreach (string folder in protectedFolders)
                    if (!String.IsNullOrEmpty(folder) && IsWithin(normalized, folder)) return "系统和程序安装目录中的文件不能喂入。";
                if (Directory.Exists(normalized)) return "暂时只接受文件，不接受文件夹或磁盘。";
                if (!File.Exists(normalized)) return "文件不存在或无法访问，请检查后重新拖入。";
                FileAttributes attributes = File.GetAttributes(normalized);
                if ((attributes & (FileAttributes.System | FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    return "系统文件、链接或云端占位文件不能喂入。";
                for (string folder = System.IO.Path.GetDirectoryName(normalized); !String.IsNullOrEmpty(folder) && !String.Equals(folder, root, StringComparison.OrdinalIgnoreCase); folder = System.IO.Path.GetDirectoryName(folder))
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return "链接目录中的文件暂不能喂入。";
                return null;
            }
            catch (Exception e)
            {
                if (!(e is ArgumentException || e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is System.Security.SecurityException)) throw;
                return "无法安全访问此文件：" + e.Message;
            }
        }

        private static bool IsWithin(string path, string folder)
        {
            folder = folder.TrimEnd('\\');
            return String.Equals(path, folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Must run on an STA thread. Each item gets its own operation so partial results
        // remain accurate and a failed item cannot silently turn later items into successes.
        internal static RecycleResult Recycle(string[] paths, IntPtr owner)
        { return Recycle(paths, owner, null); }

        internal static RecycleResult Recycle(string[] paths, IntPtr owner, Func<bool> cancellationRequested)
        {
            string[] snapshot = paths == null ? new string[0] : (string[])paths.Clone();
            RecycleResult result = new RecycleResult(snapshot);
            string error = Validate(snapshot);
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) error = "回收操作需要在 STA 线程运行。";
            if (error != null)
            {
                foreach (RecycleItemResult item in result.Items) item.Error = error;
                return result;
            }
            bool cancelled = false;
            foreach (RecycleItemResult item in result.Items)
            {
                if (cancelled || (cancellationRequested != null && cancellationRequested()))
                { item.Cancelled = cancelled = true; item.Error = "操作已取消。"; continue; }
                RecycleOne(item, owner, cancellationRequested);
                cancelled = item.Cancelled;
            }
            return result;
        }

        private static void RecycleOne(RecycleItemResult result, IntPtr owner, Func<bool> cancel)
        {
            IRecycleFileOperation operation = null;
            IRecycleShellItem shellItem = null;
            RecycleSink sink = new RecycleSink(result, cancel);
            try
            {
                string normalized, error = ValidatePath(result.Path, out normalized);
                if (error != null) { result.Error = error; return; }
                operation = (IRecycleFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                Check(operation.SetOperationFlags(RecycleFlags));
                Check(operation.SetOwnerWindow(owner));
                Guid iid = typeof(IRecycleShellItem).GUID;
                Check(SHCreateItemFromParsingName(normalized, IntPtr.Zero, ref iid, out shellItem));
                Check(operation.DeleteItem(shellItem, sink));
                int hr = operation.PerformOperations();
                bool aborted;
                int abortedHr = operation.GetAnyOperationsAborted(out aborted);
                if (!result.Success && result.Error == null)
                {
                    result.Cancelled = aborted || hr == Abort || hr == CancelledHResult;
                    result.Error = result.Cancelled ? "操作已取消。" : hr < 0 ? ErrorText(hr) : abortedHr < 0 ? ErrorText(abortedHr) : "Windows 未确认文件已进入回收站。";
                }
            }
            catch (Exception e)
            {
                if (result.Error == null) result.Error = "未能移入回收站：" + e.Message;
            }
            finally
            {
                if (shellItem != null) Marshal.ReleaseComObject(shellItem);
                if (operation != null) Marshal.ReleaseComObject(operation);
                GC.KeepAlive(sink);
            }
        }

        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        private static string ErrorText(int hr) { return "未能移入回收站（0x" + hr.ToString("X8") + "），请检查文件是否被占用及回收站设置。"; }

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        internal sealed class RecycleSink : IRecycleProgressSink
        {
            private readonly RecycleItemResult result;
            private readonly Func<bool> cancel;
            internal RecycleSink(RecycleItemResult item, Func<bool> cancellation) { result = item; cancel = cancellation; }
            internal int BeforeDelete(uint flags, string path)
            {
                result.PreFlags = flags;
                if (cancel != null && cancel()) { result.Cancelled = true; result.Error = "操作已取消。"; return Abort; }
                if ((flags & RecycleTransfer) == 0) { result.Error = "Windows 无法回收此文件，已阻止永久删除。"; return Abort; }
                if (!String.Equals(path, result.Path, StringComparison.OrdinalIgnoreCase)) { result.Error = "文件目标发生变化，操作已停止。"; return Abort; }
                string normalized, error = ValidatePath(path, out normalized);
                if (error != null) { result.Error = error; return Abort; }
                return 0;
            }
            internal int AfterDelete(uint flags, int hr, bool hasRecycledItem)
            {
                result.PostFlags = flags; result.NativeResult = hr; result.HasRecycledItem = hasRecycledItem;
                // Windows also reports COPYENGINE_S_DONT_PROCESS_CHILDREN when it moved
                // the whole item to the bin. S_FALSE/skip/pending are not completion.
                if ((hr == 0 || hr == RecycledWholeItem) && (flags & RecycleTransfer) != 0 && hasRecycledItem)
                { result.Success = true; result.Error = null; return 0; }
                if (result.Error == null)
                {
                    result.Cancelled = hr == Abort || hr == CancelledHResult;
                    result.Error = result.Cancelled ? "操作已取消。" : !hasRecycledItem ? "Windows 未返回回收站中的文件，操作已停止。" : ErrorText(hr);
                }
                return Abort;
            }
            public int PreDeleteItem(uint flags, IRecycleShellItem item)
            {
                IntPtr name = IntPtr.Zero;
                try
                {
                    int hr = item.GetDisplayName(0x80058000, out name); // SIGDN_FILESYSPATH
                    if (hr < 0) { result.Error = "无法确认待回收文件路径。"; return Abort; }
                    return BeforeDelete(flags, Marshal.PtrToStringUni(name));
                }
                catch (Exception) { result.Error = "无法确认待回收文件路径。"; return Abort; }
                finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); }
            }
            public int PostDeleteItem(uint flags, IRecycleShellItem item, int hr, IRecycleShellItem recycled)
            { return AfterDelete(flags, hr, recycled != null); }
            public int StartOperations() { return 0; }
            public int FinishOperations(int hr) { return 0; }
            public int PreRenameItem(uint flags, IRecycleShellItem item, string name) { return Abort; }
            public int PostRenameItem(uint flags, IRecycleShellItem item, string name, int hr, IRecycleShellItem created) { return Abort; }
            public int PreMoveItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, string name) { return Abort; }
            public int PostMoveItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, string name, int hr, IRecycleShellItem created) { return Abort; }
            public int PreCopyItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, string name) { return Abort; }
            public int PostCopyItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, string name, int hr, IRecycleShellItem created) { return Abort; }
            public int PreNewItem(uint flags, IRecycleShellItem destination, string name) { return Abort; }
            public int PostNewItem(uint flags, IRecycleShellItem destination, string name, string template, uint attributes, int hr, IRecycleShellItem created) { return Abort; }
            public int UpdateProgress(uint total, uint done) { return cancel != null && cancel() ? Abort : 0; }
            public int ResetTimer() { return 0; }
            public int PauseTimer() { return 0; }
            public int ResumeTimer() { return 0; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IRecycleShellItem item);

        internal static void RunValidationTests(string folder, List<string> log)
        {
            Directory.CreateDirectory(folder);
            string fixture = System.IO.Path.Combine(System.IO.Path.GetFullPath(folder), "naiwa-validation-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(fixture, "Naiwa recycling validation fixture. This test does not recycle or delete this file.");
            Assert(Validate(new[] { fixture }) == null, "ordinary local file accepted", log);
            Assert(Validate(new[] { fixture, fixture.ToUpperInvariant() }) != null, "duplicate files rejected", log);
            Assert(Validate(new[] { folder }) != null, "folders rejected", log);
            Assert(Validate(new[] { System.IO.Path.GetPathRoot(fixture) }) != null, "drive root rejected", log);
            Assert(Validate(new[] { "\\\\server\\share\\file.txt" }) != null, "network paths rejected", log);
            Assert(Validate(new[] { "\\\\?\\" + fixture }) != null, "device paths rejected", log);
            Assert(Validate(new[] { fixture + ":stream" }) != null, "alternate data streams rejected", log);
            Assert(Validate(new[] { "relative.txt" }) != null, "relative paths rejected", log);
            Assert(Validate(new[] { fixture + ".missing" }) != null, "missing files rejected", log);
            Assert(Validate(new[] { System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe") }) != null, "system directory rejected", log);
            RecycleItemResult blocked = new RecycleItemResult(fixture);
            RecycleSink sink = new RecycleSink(blocked, null);
            Assert(sink.BeforeDelete(0, fixture) < 0 && !blocked.Success, "permanent deletion vetoed before mutation", log);
            Assert(sink.BeforeDelete(RecycleTransfer, fixture + ".other") < 0, "unexpected item vetoed", log);
            RecycleItemResult cancelled = new RecycleItemResult(fixture);
            Assert(new RecycleSink(cancelled, delegate { return true; }).BeforeDelete(RecycleTransfer, fixture) < 0 && cancelled.Cancelled, "cancellation vetoes deletion", log);
            RecycleItemResult failed = new RecycleItemResult(fixture);
            Assert(new RecycleSink(failed, null).AfterDelete(RecycleTransfer, 0, false) < 0 && !failed.Success, "missing recycled item is not success", log);
            RecycleItemResult skipped = new RecycleItemResult(fixture);
            Assert(new RecycleSink(skipped, null).AfterDelete(RecycleTransfer, 1, true) < 0 && !skipped.Success, "skipped result is not success", log);
            RecycleItemResult complete = new RecycleItemResult(fixture);
            Assert(new RecycleSink(complete, null).AfterDelete(RecycleTransfer, RecycledWholeItem, true) == 0 && complete.Success, "whole-item recycle success code is recognized", log);
            Assert(File.Exists(fixture), "validation leaves fixture intact", log);
        }
        private static void Assert(bool value, string name, List<string> log)
        { if (!value) throw new InvalidOperationException("FAIL: recycle " + name); log.Add("PASS: recycle " + name); }
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRecycleShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IRecycleShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IRecycleShellItem other, uint hint, out int order);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRecycleProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int hr);
        [PreserveSig] int PreRenameItem(uint flags, IRecycleShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IRecycleShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IRecycleShellItem created);
        [PreserveSig] int PreMoveItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IRecycleShellItem created);
        [PreserveSig] int PreCopyItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IRecycleShellItem item, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int hr, IRecycleShellItem created);
        [PreserveSig] int PreDeleteItem(uint flags, IRecycleShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IRecycleShellItem item, int hr, IRecycleShellItem recycled);
        [PreserveSig] int PreNewItem(uint flags, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int hr, IRecycleShellItem created);
        [PreserveSig] int UpdateProgress(uint total, uint done);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRecycleFileOperation
    {
        [PreserveSig] int Advise(IRecycleProgressSink sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IRecycleShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IRecycleShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IRecycleProgressSink sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IRecycleShellItem item, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IRecycleProgressSink sink);
        [PreserveSig] int MoveItems(IntPtr items, IRecycleShellItem destination);
        [PreserveSig] int CopyItem(IRecycleShellItem item, IRecycleShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IRecycleProgressSink sink);
        [PreserveSig] int CopyItems(IntPtr items, IRecycleShellItem destination);
        [PreserveSig] int DeleteItem(IRecycleShellItem item, IRecycleProgressSink sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IRecycleShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IRecycleProgressSink sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
