using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Bulbul;
using UnityEngine;
using UnityEngine.UI;
using PotatoOptimization.Core;
using PotatoOptimization.UI;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Features
{
    public sealed class LearningWindowManager
    {
        private readonly MonoBehaviour _runner;
        private readonly RenderQualityManager _render;
        private readonly CameraMirrorManager _mirror;
        private readonly Action _exit;
        private readonly LearningTimerAdapter _timer = new LearningTimerAdapter();
        private readonly Dictionary<Canvas, OwnedSetting<bool>> _nativeUi = new Dictionary<Canvas, OwnedSetting<bool>>();
        private readonly Dictionary<Camera, OwnedSetting<bool>> _cameras = new Dictionary<Camera, OwnedSetting<bool>>();
        private GameObject _root;
        private Canvas _canvas;
        private TMP_FontAsset _font;
        private RawImage _background;
        private TMP_Text _time, _state, _pauseLabel, _modeLabel;
        private Button _pause;
        private Texture2D _snapshot;
        private Coroutine _capture;
        private bool _active, _frozen, _refresh = true;
        private float _nextUiScan, _resizeAfter;
        private int _width, _height;
        private string _previousState;
        public bool IsPrepared => _timer.IsReady;
        public bool CanAutoStart => _timer.CanAutoStart;

        public LearningWindowManager(MonoBehaviour runner, RenderQualityManager render,
            CameraMirrorManager mirror, Action exit)
        { _runner = runner; _render = render; _mirror = mirror; _exit = exit; }
        public bool TryPrepare() { return _timer.TryBind(); }

        public void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            if (active)
            {
                try { BuildUi(); _refresh = true; _nextUiScan = 0; }
                catch { Cleanup(); throw; }
            }
            else Cleanup();
        }

        public void Update()
        {
            if (!_active) return;
            if (!_timer.IsReady) { SceneChanged(); return; }
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonDown(1) || Input.anyKeyDown)
                _render.NotifyInteraction();
            UpdateText();
            float now = Time.realtimeSinceStartup;
            if (now >= _nextUiScan)
            { HideNativeUi(); if (_frozen) FreezeCameras(); _nextUiScan = now + 2f; }
            if (!_render.IsWindowVisible && _capture != null)
            {
                _runner.StopCoroutine(_capture); _capture = null;
                _render.CapturePending = false;
                if (_canvas != null) _canvas.enabled = true;
                FreezeCameras(); _refresh = true;
            }
            if (_width != Screen.width || _height != Screen.height)
            { _width = Screen.width; _height = Screen.height; _resizeAfter = now + 0.5f; _refresh = true; }
            bool freeze = _render.FreezeScene;
            if (freeze && (!_frozen || _refresh) && _capture == null && now >= _resizeAfter)
            {
                if (!_render.IsWindowVisible) { if (!_frozen) FreezeCameras(); return; }
                _render.CapturePending = true;
                _capture = _runner.StartCoroutine(CoroutineGuard.Run(CaptureBackground(), error =>
                { PotatoPlugin.Log.LogError("[Learning capture] " + error); _render.CapturePending = false; _capture = null; FreezeCameras(); }));
            }
            else if (!freeze && !_render.CapturePending && _frozen) UnfreezeCameras();
        }

        private IEnumerator CaptureBackground()
        {
            try
            {
                UnfreezeCameras();
                // Allow camera/portrait/mirror managers to rebind before this rendered frame.
                yield return null;
                HideNativeUi(); _canvas.enabled = false;
                yield return new WaitForEndOfFrame();
                if (!_active) yield break;
                Texture2D next = null;
                try { next = ScreenCapture.CaptureScreenshotAsTexture(); }
                catch (Exception error) { PotatoPlugin.Log.LogWarning("[Learning] Background capture failed: " + error.Message); }
                if (_snapshot != null) UnityEngine.Object.Destroy(_snapshot);
                if (next != null) next.name = "iGPU LearningSnapshot";
                _snapshot = next; _background.texture = next;
                _background.color = next != null ? Color.white : new Color(0.10f, 0.12f, 0.17f);
                _background.gameObject.SetActive(true);
                FreezeCameras(); _refresh = false;
            }
            finally
            {
                if (_canvas != null) _canvas.enabled = _active;
                _render.CapturePending = false; _capture = null;
            }
        }

        private void HideNativeUi()
        {
            var dead = new List<Canvas>();
            foreach (var pair in _nativeUi) if (pair.Key == null) dead.Add(pair.Key);
            foreach (var key in dead) _nativeUi.Remove(key);
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == _canvas || canvas.gameObject.name == "MirrorCanvas") continue;
                if (!_nativeUi.TryGetValue(canvas, out var setting))
                { var owner = canvas; setting = new OwnedSetting<bool>(() => owner.enabled, value => owner.enabled = value); _nativeUi.Add(owner, setting); }
                setting.Apply(false);
            }
        }

        private void FreezeCameras()
        {
            _mirror.SetSuspended(true);
            foreach (var camera in Camera.allCameras)
            {
                if (camera.cameraType != CameraType.Game) continue;
                if (!_cameras.TryGetValue(camera, out var setting))
                { var owner = camera; setting = new OwnedSetting<bool>(() => owner.enabled, value => owner.enabled = value); _cameras.Add(owner, setting); }
                setting.Apply(false);
            }
            _frozen = true;
            if (_background != null) _background.gameObject.SetActive(true);
        }

        private void UnfreezeCameras()
        {
            foreach (var pair in _cameras) if (pair.Key != null) Restore(pair.Value.Restore);
            _cameras.Clear(); _frozen = false;
            _mirror.SetSuspended(false);
            if (_background != null) _background.gameObject.SetActive(false);
        }

        public void RefreshBackground() { _refresh = true; _resizeAfter = Time.realtimeSinceStartup + 0.2f; }
        public void SceneChanged() { Cleanup(); _timer.Reset(); }
        public void Cleanup()
        {
            _active = false;
            if (_capture != null) { _runner.StopCoroutine(_capture); _capture = null; }
            _render.CapturePending = false;
            UnfreezeCameras();
            foreach (var pair in _nativeUi) if (pair.Key != null) Restore(pair.Value.Restore);
            _nativeUi.Clear();
            if (_snapshot != null) UnityEngine.Object.Destroy(_snapshot);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _snapshot = null; _root = null; _canvas = null; _previousState = null; _refresh = true;
            _font = null;
        }

        private static void Restore(Action action)
        { try { action(); } catch (Exception error) { PotatoPlugin.Log.LogWarning("[Learning restore] " + error.Message); } }

        private void UpdateText()
        {
            string time = _timer.RemainingText, state = _timer.StateText;
            if (_time.text != time) _time.text = time;
            if (_state.text != state)
            {
                _state.text = state;
                if (_previousState != null && state != _previousState) _render.NotifyInteraction();
                _previousState = state;
            }
            _pause.interactable = _timer.CanPause;
            if (_pauseLabel.text != _timer.PauseText) _pauseLabel.text = _timer.PauseText;
            string view = PotatoPlugin.Config.CfgLearningView.Value == LearningView.Static ? "动画" : "静态";
            if (_modeLabel.text != view) _modeLabel.text = view;
        }

        private void BuildUi()
        {
            // A timer font can contain digits only. Reuse a game font with Chinese
            // glyphs before creating labels, keeping the game's own fallbacks.
            foreach (var nativeText in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
                if (nativeText.font != null && nativeText.font.HasCharacter('学', true, true))
                { _font = nativeText.font; break; }
            _root = new GameObject("iGPU LearningWindow", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _canvas = _root.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 32000;
            var scaler = _root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(480, 270);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = Element("Background", _root.transform, Vector2.zero, new Vector2(480, 270));
            Stretch(background);
            _background = background.AddComponent<RawImage>(); _background.raycastTarget = false;
            _background.color = new Color(0.10f, 0.12f, 0.17f);
            var shade = Element("Shade", _root.transform, Vector2.zero, new Vector2(480, 270));
            Stretch(shade);
            var shadeImage = shade.AddComponent<Image>();
            shadeImage.color = new Color(0.03f, 0.04f, 0.07f, 0.58f);
            shadeImage.raycastTarget = false;
            _time = Text("--:--", new Vector2(0, 25), new Vector2(460, 90), 72);
            _state = Text("", new Vector2(0, -36), new Vector2(440, 32), 16);
            _state.color = new Color(1, 1, 1, 0.86f);
            _pause = Button("PauseButton", "暂停", 0, () => { _timer.TogglePause(); _render.NotifyInteraction(); }, out _pauseLabel);
            Button("ViewButton", "动画", -148, () =>
            {
                var config = PotatoPlugin.Config;
                config.CfgLearningView.Value = config.CfgLearningView.Value == LearningView.Static ? LearningView.Animated : LearningView.Static;
                _render.NotifyInteraction(); _refresh = true;
            }, out _modeLabel);
            Button("RestoreButton", "原窗口", 148, _exit, out _);
            UpdateText();
        }

        private TMP_Text Text(string value, Vector2 position, Vector2 size, int fontSize, Transform parent = null)
        {
            var element = Element("Text", parent != null ? parent : _root.transform, position, size);
            var text = element.AddComponent<TextMeshProUGUI>();
            var native = UnityEngine.Object.FindObjectOfType<PomodoroTimerUI>(true);
            text.font = _font != null ? _font : (native != null && native._timeText != null ? native._timeText.font : TMP_Settings.defaultFontAsset);
            text.text = value; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white; text.raycastTarget = false;
            return text;
        }

        private Button Button(string name, string value, float x, Action action, out TMP_Text label)
        {
            var element = Element(name, _root.transform, new Vector2(x, 38), new Vector2(136, 42));
            var rect = element.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            var surface = element.AddComponent<LearningButtonGraphic>();
            var button = element.AddComponent<Button>(); button.targetGraphic = surface;
            var colors = button.colors;
            colors.normalColor = new Color(0.08f, 0.13f, 0.18f, 0.42f);
            colors.highlightedColor = new Color(0.08f, 0.13f, 0.18f, 0.60f);
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = new Color(0.08f, 0.13f, 0.18f, 0.56f);
            colors.disabledColor = new Color(0.08f, 0.13f, 0.18f, 0.24f);
            colors.fadeDuration = 0;
            button.colors = colors;
            button.onClick.AddListener(() => action());
            label = Text(value, Vector2.zero, new Vector2(120, 34), 16, element.transform);
            label.color = new Color(0.96f, 0.97f, 0.98f, 1);
            return button;
        }

        private static GameObject Element(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var element = new GameObject(name, typeof(RectTransform)); element.transform.SetParent(parent, false);
            var rect = element.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; return element;
        }

        private static void Stretch(GameObject element)
        {
            var rect = element.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
