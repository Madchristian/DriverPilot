using System;
using System.Threading;
#if NET
#pragma warning disable CS8603 // Shared C# 5 test harness; null means acquisition was refused.
#endif
namespace DriverPilot
{
    // Thread-affine: acquire/release on the WPF dispatcher, including after awaited work.
    public sealed class LifecycleGate : IDisposable
    {
        private readonly Mutex mutex;
        private bool disposed;
        private LifecycleGate(Mutex value) { mutex = value; }
        public static LifecycleGate TryEnter(string name)
        {
            var guard = new Mutex(false, name);
            try
            {
                if (guard.WaitOne(0)) return new LifecycleGate(guard);
            }
            catch (AbandonedMutexException)
            {
                // Do not silently continue a possibly interrupted installation/removal.
                guard.ReleaseMutex();
            }
            catch { guard.Dispose(); throw; }
            guard.Dispose();
            return null;
        }
        public void Dispose()
        {
            if (disposed) return;
            mutex.ReleaseMutex();
            mutex.Dispose();
            disposed = true;
        }
    }
}
