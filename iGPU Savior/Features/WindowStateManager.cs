using System;
using System.Collections;
using UnityEngine;
using PotatoOptimization.Core;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Features
{
    public sealed class WindowStateManager
    {
        private readonly Configuration.ConfigurationManager _config;
        private IntPtr _window, _style, _exStyle;
        private int _width, _height;
        private FullScreenMode _mode;
        private WindowManager.RECT _bounds;
        private bool _changing, _originalSaved;
        private bool _rightDragging;
        private WindowManager.POINT _dragStart;
        private WindowManager.RECT _dragBounds;
        private float _nextMaintenance, _nextSave;
        public bool IsSmallWindow { get; private set; }
        public WindowStateManager(Configuration.ConfigurationManager config) { _config = config; }
        public IEnumerator TogglePiPMode() { return SetSmallWindowMode(!IsSmallWindow); }

        public IEnumerator SetSmallWindowMode(bool small)
        {
            if (_changing || small == IsSmallWindow) yield break;
            _changing = true;
            try
            {
                if (small)
                {
                    _window = WindowManager.GetCurrentWindowHandle();
                    if (!WindowManager.IsOwnedWindow(_window)) yield break;
                    _width = Screen.width; _height = Screen.height; _mode = Screen.fullScreenMode;
                    _style = WindowManager.GetWindowStyle(_window, Constants.GWL_STYLE);
                    _exStyle = WindowManager.GetWindowStyle(_window, Constants.GWL_EXSTYLE);
                    if (!WindowManager.GetWindowBounds(_window, out _bounds)) yield break;
                    _originalSaved = true;
                    var placement = GetPlacement();
                    Screen.SetResolution(placement.Width, placement.Height, FullScreenMode.Windowed);
                    // Unity applies resolution and native-frame changes later.
                    // Two logic frames can finish before its window update,
                    // especially while OnDemandRendering skips rendered frames.
                    float deadline = Time.realtimeSinceStartup + 3f;
                    do { yield return null; }
                    while ((Screen.width != placement.Width || Screen.height != placement.Height ||
                        Screen.fullScreenMode != FullScreenMode.Windowed) && Time.realtimeSinceStartup < deadline);
                    yield return new WaitForSecondsRealtime(0.5f);
                    bool success = false;
                    try
                    {
                        WindowManager.SetPiPWindowStyle(_window);
                        success = WindowManager.ResizeWindow(_window, placement.Width, placement.Height) &&
                            WindowManager.MoveWindow(_window, placement.X, placement.Y) && WindowManager.EnsureTopmost(_window);
                    }
                    catch (Exception error) { PotatoPlugin.Log.LogError("[PiP] " + error); }
                    if (!success) { RestoreOriginal(); yield break; }
                    // A final queued Unity window update may use the old frame
                    // margins. Reapply the requested client size and position.
                    yield return new WaitForSecondsRealtime(0.5f);
                    success = WindowManager.ResizeWindow(_window, placement.Width, placement.Height) &&
                        WindowManager.MoveWindow(_window, placement.X, placement.Y) && WindowManager.EnsureTopmost(_window);
                    if (!success) { RestoreOriginal(); yield break; }
                    IsSmallWindow = true;
                }
                else
                {
                    Remember();
                    RestoreOriginal();
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (_mode == FullScreenMode.Windowed && WindowManager.IsOwnedWindow(_window))
                    {
                        RestoreBounds(_window, _bounds);
                        yield return new WaitForSecondsRealtime(0.5f);
                        RestoreBounds(_window, _bounds);
                    }
                }
            }
            finally { _changing = false; }
        }

        private WindowPlacement GetPlacement()
        {
            double dpi = WindowManager.DpiScale(_window);
            var area = WindowManager.WorkingArea(_window);
            bool first = _config.CfgPiPX.Value == int.MinValue;
            if (!first) area = WindowManager.SavedWorkingArea(_config.CfgPiPX.Value, _config.CfgPiPY.Value, out dpi);
            double savedScale = Math.Max(1, _config.CfgPiPDpi.Value) / 96.0;
            int width = _config.CfgPiPWidth.Value > 0 ? (int)(_config.CfgPiPWidth.Value * dpi / savedScale) : (int)(480 * dpi);
            int height = _config.CfgPiPHeight.Value > 0 ? (int)(_config.CfgPiPHeight.Value * dpi / savedScale) : (int)(270 * dpi);
            return WindowGeometry.Clamp(_config.CfgPiPX.Value, _config.CfgPiPY.Value, width, height,
                area.Left, area.Top, area.Right, area.Bottom, dpi, first);
        }

        public void ResizeToRatio(int ratio)
        {
            if (!IsSmallWindow || _changing || ratio < 1) return;
            var area = WindowManager.WorkingArea(_window);
            WindowManager.ResizeWindow(_window, Math.Max(320, (area.Right-area.Left)/ratio),
                Math.Max(180, (area.Bottom-area.Top)/ratio));
            Remember();
        }

        public void HandleDragLogic()
        {
            if (!IsSmallWindow || _changing) return;
            if (!WindowManager.IsOwnedWindow(_window)) { Cleanup(); return; }
            float now = Time.realtimeSinceStartup;
            if (now >= _nextMaintenance)
            {
                _nextMaintenance = now + 2f;
                if (!WindowManager.EnsureTopmost(_window)) PotatoPlugin.Log.LogWarning("[PiP] Unable to restore topmost state.");
                if (!WindowManager.IsMinimized(_window) && WindowManager.GetWindowBounds(_window, out var rect))
                {
                    var area = WindowManager.WorkingArea(_window);
                    var clamp = WindowGeometry.Clamp(rect.Left, rect.Top, rect.Right-rect.Left, rect.Bottom-rect.Top,
                        area.Left, area.Top, area.Right, area.Bottom, WindowManager.DpiScale(_window), false);
                    if (clamp.X != rect.Left || clamp.Y != rect.Top) WindowManager.MoveWindow(_window, clamp.X, clamp.Y);
                    if (clamp.Width != rect.Right-rect.Left || clamp.Height != rect.Bottom-rect.Top)
                        WindowManager.ResizeWindow(_window, clamp.Width, clamp.Height);
                }
            }
            var method = _config.CfgDragMode.Value;
            bool modifier = method == DragMode.Ctrl_LeftClick
                ? Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                : Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (method != DragMode.RightClick_Hold && modifier && Input.GetMouseButtonDown(0)) WindowManager.StartSystemDrag();
            if (method == DragMode.RightClick_Hold)
            {
                if (Input.GetMouseButtonDown(1))
                    _rightDragging = WindowManager.GetCursorScreenPosition(out _dragStart) && WindowManager.GetWindowBounds(_window, out _dragBounds);
                if (Input.GetMouseButtonUp(1)) _rightDragging = false;
                if (_rightDragging && WindowManager.GetCursorScreenPosition(out var position))
                    WindowManager.MoveWindow(_window, _dragBounds.Left + position.X-_dragStart.X, _dragBounds.Top + position.Y-_dragStart.Y);
            }
            if (now >= _nextSave && !Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            { _nextSave = now + 2f; Remember(); }
        }

        private void Remember()
        {
            if (!WindowManager.IsOwnedWindow(_window) || WindowManager.IsMinimized(_window) ||
                !WindowManager.GetWindowBounds(_window, out var rect)) return;
            int width = rect.Right-rect.Left, height = rect.Bottom-rect.Top;
            if (_config.CfgPiPX.Value == rect.Left && _config.CfgPiPY.Value == rect.Top &&
                _config.CfgPiPWidth.Value == width && _config.CfgPiPHeight.Value == height) return;
            bool autoSave = _config.File.SaveOnConfigSet;
            _config.File.SaveOnConfigSet = false;
            try
            {
                _config.CfgPiPX.Value = rect.Left; _config.CfgPiPY.Value = rect.Top;
                _config.CfgPiPWidth.Value = width; _config.CfgPiPHeight.Value = height;
                _config.CfgPiPDpi.Value = (int)(WindowManager.DpiScale(_window) * 96); _config.Save();
            }
            finally { _config.File.SaveOnConfigSet = autoSave; }
        }

        private void RestoreOriginal()
        {
            if (!_originalSaved) return;
            // Returning from a visible learning panel must leave the game usable,
            // even if its launcher originally created a hidden bootstrap window.
            if (WindowManager.IsOwnedWindow(_window)) WindowManager.RestoreWindowStyle(_window, _style, _exStyle, true);
            Screen.SetResolution(_width, _height, _mode);
            if (_mode == FullScreenMode.Windowed && WindowManager.IsOwnedWindow(_window))
            {
                RestoreBounds(_window, _bounds);
            }
            IsSmallWindow = false;
            _originalSaved = false;
        }
        private static void RestoreBounds(IntPtr window, WindowManager.RECT bounds)
        {
            if (!WindowManager.IsOwnedWindow(window)) return;
            var area = WindowManager.SavedWorkingArea(bounds.Left, bounds.Top, out double dpi);
            var placement = WindowGeometry.Clamp(bounds.Left, bounds.Top, bounds.Right-bounds.Left,
                bounds.Bottom-bounds.Top, area.Left, area.Top, area.Right, area.Bottom, dpi, false);
            WindowManager.ResizeWindow(window, placement.Width, placement.Height);
            WindowManager.MoveWindow(window, placement.X, placement.Y);
        }
        public void Cleanup()
        {
            if (!_originalSaved) return;
            if (IsSmallWindow) Remember();
            var window = _window; var bounds = _bounds; bool windowed = _mode == FullScreenMode.Windowed;
            RestoreOriginal();
            if (!windowed) return;
            // The owner is being destroyed, but Unity's queued resolution change
            // still needs to settle before the original placement is final.
            var recovery = new GameObject("iGPU WindowRestore");
            recovery.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(recovery);
            recovery.AddComponent<WindowRestoreRunner>().Run(() =>
            { if (!_originalSaved && !IsSmallWindow) RestoreBounds(window, bounds); });
        }
    }

    internal sealed class WindowRestoreRunner : MonoBehaviour
    {
        internal void Run(Action restore)
        { StartCoroutine(CoroutineGuard.Run(Recover(restore), error => PotatoPlugin.Log.LogError("[Window restore] " + error))); }
        private IEnumerator Recover(Action restore)
        {
            try
            {
                yield return new WaitForSecondsRealtime(0.5f);
                restore();
                yield return new WaitForSecondsRealtime(0.5f);
                restore();
            }
            finally { UnityEngine.Object.Destroy(gameObject); }
        }
    }
}
