using System;

namespace PotatoOptimization.Utilities
{
    // A high precision increment for the existing timer, not a second countdown.
    // Accept records the game's actual float value; double accumulation avoids
    // repeatedly rounding elapsed time at low logic frame rates.
    public sealed class MonotonicTimerProgress
    {
        private double _previous, _elapsed;
        private float _lastApplied;
        private bool _initialized, _running;

        public void Rebase(double now, float elapsed, bool running)
        {
            _previous = now; _elapsed = elapsed; _lastApplied = elapsed;
            _running = running; _initialized = true;
        }

        public float Advance(double now, float elapsed, bool running)
        {
            if (!_initialized) { Rebase(now, elapsed, running); return elapsed; }
            if (elapsed != _lastApplied) _elapsed = elapsed;
            double seconds = Math.Max(0, now - _previous);
            _previous = Math.Max(_previous, now);
            if (running && _running) _elapsed += seconds;
            else _elapsed = elapsed;
            _running = running;
            return (float)_elapsed;
        }

        public void Accept(float elapsed) { _lastApplied = elapsed; }
    }
}
