using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using PotatoOptimization.Features;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Core
{
    public sealed class PotatoController : MonoBehaviour
    {
        private RenderQualityManager _render;
        private CameraMirrorManager _mirror;
        private PortraitModeManager _portrait;
        private WindowStateManager _window;
        private LearningWindowManager _learning;
        private bool _desiredPiP, _transitioning, _manualWindow, _manualMirror, _manualPortrait, _roomApplied, _autoWindowAttempted, _cleaned;
        private float _nextPrepare;
        public string PowerStatus => _render?.Status;
        public bool IsLearningActive => _window?.IsSmallWindow == true;

        void Start()
        {
            var audio = new AudioManager();
            _render = new RenderQualityManager(new WindowVisibilityChecker(WindowManager.GetCurrentWindowHandle));
            _mirror = new CameraMirrorManager(audio); _portrait = new PortraitModeManager();
            _window = new WindowStateManager(PotatoPlugin.Config);
            _learning = new LearningWindowManager(this, _render, _mirror, () => RequestPiP(false, true));
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Update()
        {
            PotatoPlugin.MainThread?.Drain(error => PotatoPlugin.Log.LogError("[Main thread] " + error));
            if (_cleaned || _render == null) return;
            try
            {
                var config = PotatoPlugin.Config;
                if (Input.GetKeyDown(config.KeyPotatoMode.Value)) _render.TogglePotatoMode();
                else if (Input.GetKeyDown(config.KeyPiPMode.Value)) RequestPiP(!_desiredPiP, true);
                else if (Input.GetKeyDown(config.KeyCameraMirror.Value)) SetMirrorState(!_mirror.IsMirrored);
                else if (Input.GetKeyDown(config.KeyPortraitMode.Value))
                { _manualPortrait = true; _portrait.Toggle(); _learning.RefreshBackground(); }

                if (Time.realtimeSinceStartup >= _nextPrepare)
                {
                    _nextPrepare = Time.realtimeSinceStartup + 1f;
                    if (_learning.TryPrepare())
                    {
                        if (!_roomApplied)
                        {
                            _roomApplied = true;
                            if (!_manualMirror) _mirror.SetMirrorState(config.CfgEnableMirror.Value);
                            if (!_manualPortrait) _portrait.SetEnabled(config.CfgEnablePortraitMode.Value);
                        }
                        if (!_manualWindow && !_autoWindowAttempted && config.CfgLearningAutoStart.Value && _learning.CanAutoStart)
                        { _autoWindowAttempted = true; RequestPiP(true, false); }
                        if (_window.IsSmallWindow) _learning.SetActive(true);
                    }
                }
                _render.SetPiPModeActive(_window.IsSmallWindow && _learning.IsPrepared);
                _render.SetLearningView(config.CfgLearningView.Value);
                _render.Update(); _portrait.Update(); _mirror.CheckResolutionChange();
                _window.HandleDragLogic();
                // A recreated native window must not leave the learning canvas
                // active while its window/quality owners have already exited.
                _learning.SetActive(_window.IsSmallWindow && _learning.IsPrepared);
                _learning.Update();
            }
            catch (Exception error)
            {
                FailLearning(error);
            }
        }

        public void RequestPiP(bool enabled, bool manual)
        {
            if (manual) _manualWindow = true;
            _desiredPiP = enabled;
            if (!_transitioning) { _transitioning = true; StartCoroutine(CoroutineGuard.Run(ReconcilePiP(), FailLearning)); }
        }

        private void FailLearning(Exception error)
        {
            PotatoPlugin.Log.LogError("[Learning mode disabled] " + error);
            _desiredPiP = false; _manualWindow = true;
            SafeCleanup(() => _learning.Cleanup());
            _render.SetPiPModeActive(false);
            SafeCleanup(() => _render.Cleanup()); SafeCleanup(() => _window.Cleanup());
        }

        private static void SafeCleanup(Action action)
        { try { action(); } catch (Exception error) { PotatoPlugin.Log.LogError("[Restore] " + error); } }

        public void ResizeLearningWindow(WindowScaleRatio ratio)
        { _window?.ResizeToRatio((int)ratio); _learning?.RefreshBackground(); }

        private IEnumerator ReconcilePiP()
        {
            try
            {
                while (_desiredPiP != _window.IsSmallWindow)
                {
                    bool wanted = _desiredPiP;
                    if (wanted && !_learning.TryPrepare())
                    { _desiredPiP = false; PotatoPlugin.Log.LogWarning("[PiP] Enter the game room before opening the learning window."); yield break; }
                    if (!wanted) _learning.SetActive(false);
                    yield return _window.SetSmallWindowMode(wanted);
                    if (_window.IsSmallWindow != wanted) { _desiredPiP = _window.IsSmallWindow; yield break; }
                    _render.SetPiPModeActive(wanted);
                    _learning.SetActive(wanted);
                }
            }
            finally { _transitioning = false; }
        }

        public void SetMirrorState(bool enabled)
        { _manualMirror = true; _mirror?.SetMirrorState(enabled); _learning?.RefreshBackground(); }
        public void SetPortraitState(bool enabled)
        { _manualPortrait = true; _portrait?.SetEnabled(enabled); _learning?.RefreshBackground(); }
        void OnApplicationFocus(bool focus) { _render?.OnFocusChanged(focus); }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        { _learning?.SceneChanged(); _render?.InvalidateScene(); _roomApplied = false; _autoWindowAttempted = false; _nextPrepare = 0; }
        void OnDestroy() { Cleanup(); }
        public void Cleanup()
        {
            if (_cleaned) return; _cleaned = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            StopAllCoroutines();
            SafeCleanup(() => _learning?.Cleanup()); SafeCleanup(() => _portrait?.Cleanup());
            SafeCleanup(() => _mirror?.Cleanup()); SafeCleanup(() => _render?.Cleanup());
            SafeCleanup(() => _window?.Cleanup());
        }
    }
}
