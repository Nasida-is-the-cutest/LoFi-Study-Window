using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PotatoOptimization.Utilities
{
    // Construct on Unity's thread; no worker callback may touch Unity directly.
    public sealed class MainThreadQueue
    {
        private readonly int _owner = Thread.CurrentThread.ManagedThreadId;
        private readonly ConcurrentQueue<Action> _pending = new ConcurrentQueue<Action>();
        private readonly object _gate = new object();
        private volatile bool _stopped;

        public void Post(Action action)
        {
            if (action == null) return;
            lock (_gate)
            {
                if (_stopped) return;
                _pending.Enqueue(action);
            }
        }

        public void Drain(Action<Exception> onError, int limit = 64)
        {
            if (Thread.CurrentThread.ManagedThreadId != _owner)
                throw new InvalidOperationException("Only the Unity thread may drain callbacks.");
            for (int i = 0; i < limit && _pending.TryDequeue(out Action action); i++)
            {
                if (_stopped) return;
                try { action(); }
                catch (Exception error) { onError?.Invoke(error); }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _stopped = true;
                while (_pending.TryDequeue(out _)) { }
            }
        }
    }
}
