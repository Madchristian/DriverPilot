// Test-only OS fault injector; never compiled into the application/package.
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
public sealed class UpgradeCopyLock : IDisposable
{
    readonly FileSystemWatcher watcher;
    readonly string source;
    readonly object gate = new object();
    FileStream held;
    bool disposed;
    public readonly ManualResetEvent Acquired = new ManualResetEvent(false);
    public string Error;
    public UpgradeCopyLock(string stagingParent, string sourceFile)
    {
        source = sourceFile;
        watcher = new FileSystemWatcher(stagingParent, "A-copy-window.txt");
        watcher.IncludeSubdirectories = true;
        watcher.NotifyFilter = NotifyFilters.FileName;
        watcher.Created += OnCreated;
        watcher.EnableRaisingEvents = true;
    }
    void OnCreated(object sender, FileSystemEventArgs e)
    {
        // A real first-file copy proves source verification has completed. Its
        // size provides time to exclusively lock a later inventory entry.
        if (!Path.GetFileName(Path.GetDirectoryName(e.FullPath)).StartsWith("AppStaging-", StringComparison.Ordinal)) return;
        lock (gate)
        {
            if (held != null || disposed) return;
            try { held = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None); Acquired.Set(); }
            catch (Exception ex) { Error = ex.ToString(); }
        }
    }
    public void Dispose()
    {
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        lock (gate) { disposed = true; if (held != null) held.Dispose(); Acquired.Dispose(); }
    }
}
public static class UpgradeProcessHold
{
    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr handle);
    public static void Suspend(Process process)
    {
        int status = NtSuspendProcess(process.Handle);
        if (status != 0) throw new InvalidOperationException("NtSuspendProcess failed: " + status);
    }
}
