using System;
using System.Collections.Generic;

namespace PotatoOptimization.Utilities
{
    // Restore only our own value; remember external changes observed while active.
    public sealed class OwnedSetting<T>
    {
        private readonly Func<T> _read;
        private readonly Action<T> _write;
        private T _original;
        private T _applied;
        private bool _owns;

        public OwnedSetting(Func<T> read, Action<T> write) { _read = read; _write = write; }

        public void Apply(T value)
        {
            T current = _read();
            if (!_owns || !EqualityComparer<T>.Default.Equals(current, _applied)) _original = current;
            try { _write(value); }
            catch
            {
                // A setter can mutate first and then fail. Keep the value we
                // actually changed recoverable during the caller's cleanup.
                try { _applied = _read(); _owns = true; } catch { }
                throw;
            }
            _applied = _read();
            _owns = true;
            if (!EqualityComparer<T>.Default.Equals(value, _applied))
                throw new InvalidOperationException("The setting did not accept its requested value.");
        }

        public void Restore()
        {
            if (_owns && EqualityComparer<T>.Default.Equals(_read(), _applied)) _write(_original);
            _owns = false;
        }
    }
}
