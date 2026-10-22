using System;
using Bulbul;
using UnityEngine;

namespace PotatoOptimization.Features
{
    public sealed class LearningTimerAdapter
    {
        private PomodoroTimerUI _ui;
        private PomodoroService _service;
        public bool IsReady => _ui != null && _service != null && _service._pomodoroTimerService != null && _service.CurrentLoopCount != null;
        public bool CanAutoStart
        {
            get
            {
                if (!IsReady) return false;
                try { return RoomLifetimeScope.Resolve<RoomGameManager>()?.CurrentMainState == RoomGameManager.MainState.Idle; }
                catch { return false; }
            }
        }
        public bool TryBind()
        {
            if (IsReady) return true;
            _ui = UnityEngine.Object.FindObjectOfType<PomodoroTimerUI>(true);
            _service = _ui != null ? _ui._pomodoroService : null;
            return IsReady && _service._pomodoroTimerService != null;
        }
        public void Reset() { _ui = null; _service = null; }
        public string RemainingText
        {
            get
            {
                if (!IsReady) return "--:--";
                var time = _service._pomodoroTimerService.RemainTimeSpan();
                // Match the original UI's TimeSpan seconds (truncate fractions).
                int seconds = Math.Max(0, (int)Math.Floor(time.TotalSeconds));
                return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }
        }
        public string StateText
        {
            get
            {
                if (!IsReady) return "等待计时器";
                if (!_service.IsTimerRunning()) return "未开始 · 返回游戏设置番茄钟";
                string phase = _service.IsCurrentResting() ? "休息" : "工作";
                return phase + (_service._pomodoroTimerService._isRunning ? "" : " · 已暂停") +
                    " · 第 " + _service.CurrentLoopCount.CurrentValue + " 轮";
            }
        }
        public bool CanPause => IsReady && _service.IsTimerRunning() && _service.IsActiveInput();
        public string PauseText => IsReady && _service._pomodoroTimerService._isRunning ? "暂停" : "继续";
        public void TogglePause() { if (CanPause) _service.PlayOrPausePomodoroTimer(); }
    }
}
