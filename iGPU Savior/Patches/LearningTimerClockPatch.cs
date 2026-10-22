using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bulbul;
using HarmonyLib;
using UnityEngine;
using PotatoOptimization.Core;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Patches
{
    internal static class LearningTimerClock
    {
        private sealed class State
        {
            public bool Active;
            public readonly MonotonicTimerProgress Progress = new MonotonicTimerProgress();
        }
        private static ConditionalWeakTable<PomodoroTimerService, State> _states = new ConditionalWeakTable<PomodoroTimerService, State>();
        private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        internal static bool Advance(PomodoroTimerService timer)
        {
            var state = _states.GetValue(timer, _ => new State());
            double now = Now;
            bool running = timer._isRunning && !timer.IsTimerEnd() && Time.timeScale > 0;
            // Once a running session uses the learning clock, retain that clock
            // across window changes; switching time sources caused the drift.
            state.Active |= PotatoPlugin.Controller?.IsLearningActive == true;
            if (!state.Active)
            { state.Progress.Rebase(now, timer._elapsedTimeSeconds, running); return true; }
            float next = state.Progress.Advance(now, timer._elapsedTimeSeconds, running);
            if (running && next > timer._elapsedTimeSeconds)
                timer.MoveAheadTime(next - timer._elapsedTimeSeconds);
            state.Progress.Accept(timer._elapsedTimeSeconds);
            return false;
        }

        internal static void Rebase(PomodoroTimerService timer, bool reset)
        {
            var state = _states.GetValue(timer, _ => new State());
            if (reset) state.Active = false;
            state.Progress.Rebase(Now, timer._elapsedTimeSeconds, timer._isRunning && !timer.IsTimerEnd() && Time.timeScale > 0);
        }
        internal static void Cleanup() { _states = new ConditionalWeakTable<PomodoroTimerService, State>(); }
    }

    [HarmonyPatch(typeof(PomodoroTimerService), nameof(PomodoroTimerService.Update))]
    internal static class LearningTimerClockPatch
    {
        private static bool Prefix(PomodoroTimerService __instance)
        {
            try { return LearningTimerClock.Advance(__instance); }
            catch (Exception error) { PotatoPlugin.Log?.LogError("[Timer clock] " + error); return true; }
        }
    }

    [HarmonyPatch]
    internal static class LearningTimerBoundaryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            string[] names = { nameof(PomodoroTimerService.PlayTimer), nameof(PomodoroTimerService.ResetTimer),
                nameof(PomodoroTimerService.PauseTimer), nameof(PomodoroTimerService.UnpauseTimer),
                nameof(PomodoroTimerService.SkipTimer), nameof(PomodoroTimerService.SetRemainingTime) };
            foreach (string name in names) yield return AccessTools.Method(typeof(PomodoroTimerService), name);
        }
        private static void Prefix(PomodoroTimerService __instance)
        {
            try { LearningTimerClock.Advance(__instance); }
            catch (Exception error) { PotatoPlugin.Log?.LogError("[Timer boundary] " + error); }
        }
        private static void Postfix(PomodoroTimerService __instance, MethodBase __originalMethod)
        {
            try { LearningTimerClock.Rebase(__instance, __originalMethod.Name == nameof(PomodoroTimerService.ResetTimer)); }
            catch (Exception error) { PotatoPlugin.Log?.LogError("[Timer boundary] " + error); }
        }
    }
}
