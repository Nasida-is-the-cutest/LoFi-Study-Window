using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PotatoOptimization.Core;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Features
{
    public sealed class RenderQualityManager
    {
        private readonly WindowVisibilityChecker _visibility;
        private readonly OwnedSetting<int> _fps = new OwnedSetting<int>(() => Application.targetFrameRate, v => Application.targetFrameRate = v);
        private readonly OwnedSetting<int> _sync = new OwnedSetting<int>(() => QualitySettings.vSyncCount, v => QualitySettings.vSyncCount = v);
        private readonly OwnedSetting<int> _interval = new OwnedSetting<int>(() => OnDemandRendering.renderFrameInterval, v => OnDemandRendering.renderFrameInterval = v);
        private readonly OwnedSetting<bool> _background = new OwnedSetting<bool>(() => Application.runInBackground, v => Application.runInBackground = v);
        private readonly Dictionary<Volume, OwnedSetting<float>> _volumes = new Dictionary<Volume, OwnedSetting<float>>();
        private readonly HashSet<string> _warnings = new HashSet<string>();
        private UniversalRenderPipelineAsset _asset;
        private readonly Dictionary<string, OwnedSetting<object>> _assetSettings = new Dictionary<string, OwnedSetting<object>>();
        private bool _potato;
        private bool _learning;
        private LearningView _view;
        private float _interactionUntil;
        private float _nextVisibility;
        private float _nextApply;
        private float _nextVolumeScan;
        private int _lastFps;
        private int _lastInterval;
        private float _lastScale;
        private bool _lastHeavy;
        private bool _active;

        public bool IsPotatoMode => _potato;
        public bool IsBackgroundMode => !IsWindowVisible;
        public bool IsWindowVisible { get; private set; } = true;
        public bool CapturePending { get; set; }
        public bool FreezeScene { get; private set; }
        public string Status { get; private set; } = "Normal";

        public RenderQualityManager(WindowVisibilityChecker visibilityChecker) { _visibility = visibilityChecker; }
        public void SetPiPModeActive(bool active)
        { if (_learning == active) return; _learning = active; _nextApply = 0; }
        public void SetLearningView(LearningView view) { _view = view; }
        public void NotifyInteraction() { _interactionUntil = Time.realtimeSinceStartup + 3f; }
        public void TogglePotatoMode() { _potato = !_potato; _nextApply = 0; }
        public void OnFocusChanged(bool focused) { _nextVisibility = 0; }
        public void InvalidateScene() { _nextApply = 0; _nextVolumeScan = 0; }

        public void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _nextVisibility)
            {
                IsWindowVisible = _visibility.IsGameWindowVisible();
                _nextVisibility = now + (_learning || !IsWindowVisible ? 2f : 0.5f);
            }
            bool backgroundMode = !IsWindowVisible && PotatoPlugin.Config.CfgEnableBackgroundOptimization.Value;
            if (!_learning && !_potato && !backgroundMode)
            {
                FreezeScene = false;
                if (_active) Cleanup();
                Status = "Normal";
                return;
            }

            LearningProfile profile = _learning
                ? LearningModePolicy.Select(_view, IsWindowVisible, now < _interactionUntil || CapturePending)
                : new LearningProfile(backgroundMode ? 10 : 15, 1, backgroundMode ? 0.1f : 0.4f, false);
            FreezeScene = _learning && profile.FreezeScene && !CapturePending;
            Status = _learning ? (FreezeScene ? "Static" : "Animated") : (backgroundMode ? "Background" : "Potato");
            bool heavy = _learning || !backgroundMode;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool changed = !_active || _lastFps != profile.LogicFps || _lastInterval != profile.RenderInterval ||
                _lastScale != profile.RenderScale || _lastHeavy != heavy || asset != _asset;
            if (!changed && now < _nextApply) return;
            try
            {
                _fps.Apply(profile.LogicFps);
                _sync.Apply(0);
                _interval.Apply(profile.RenderInterval);
                if (_learning) _background.Apply(true); else _background.Restore();
                if (_asset != asset || _lastHeavy != heavy)
                {
                    RestoreAsset();
                    _asset = asset;
                }
                if (_asset != null)
                {
                    ApplyAsset("renderScale", profile.RenderScale);
                    ApplyAsset("shadowDistance", 0f);
                    if (heavy)
                    {
                        ApplyAsset("msaaSampleCount", 1);
                        ApplyAsset("supportsHDR", false);
                        ApplyAsset("supportsSoftShadows", false, "m_SoftShadowsSupported");
                        ApplyAsset("supportsMainLightShadows", false, "m_MainLightShadowsSupported");
                        ApplyAsset("supportsAdditionalLightShadows", false, "m_AdditionalLightShadowsSupported");
                        ApplyAsset("maxAdditionalLightsCount", 2);
                    }
                }
                if (heavy) DisableVolumes(now); else RestoreVolumes();
                _lastFps = profile.LogicFps; _lastInterval = profile.RenderInterval;
                _lastScale = profile.RenderScale; _lastHeavy = heavy; _active = true;
                _nextApply = now + 3f;
            }
            catch (Exception error)
            {
                Status = "Power settings failed";
                WarnOnce(error.Message);
                _nextApply = now + 5f;
                _active = true;
            }
        }

        private void ApplyAsset(string name, object value, string fieldName = null)
        {
            if (!_assetSettings.TryGetValue(name, out var setting))
            {
                var owner = _asset;
                var property = typeof(UniversalRenderPipelineAsset).GetProperty(name);
                var field = fieldName == null ? null : typeof(UniversalRenderPipelineAsset).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (property == null || (!property.CanWrite && field == null))
                { WarnOnce("URP property unavailable: " + name); return; }
                setting = new OwnedSetting<object>(() => property.GetValue(owner), v =>
                {
                    if (property.CanWrite) property.SetValue(owner, v); else field.SetValue(owner, v);
                });
                _assetSettings.Add(name, setting);
            }
            setting.Apply(value);
        }

        private void RestoreAsset()
        {
            if (_asset != null)
                foreach (var setting in _assetSettings.Values) TryRestore(setting.Restore);
            _assetSettings.Clear(); _asset = null;
        }

        private void DisableVolumes(float now)
        {
            if (now >= _nextVolumeScan)
            {
                var dead = new List<Volume>();
                foreach (var pair in _volumes) if (pair.Key == null) dead.Add(pair.Key);
                foreach (var key in dead) _volumes.Remove(key);
                foreach (var volume in UnityEngine.Object.FindObjectsOfType<Volume>())
                {
                    if (!volume.enabled || _volumes.ContainsKey(volume)) continue;
                    var owner = volume;
                    _volumes.Add(owner, new OwnedSetting<float>(() => owner.weight, v => owner.weight = v));
                }
                _nextVolumeScan = now + 5f;
            }
            foreach (var pair in _volumes) if (pair.Key != null) pair.Value.Apply(0f);
        }

        private void RestoreVolumes()
        {
            foreach (var pair in _volumes) if (pair.Key != null) TryRestore(pair.Value.Restore);
            _volumes.Clear();
        }

        public void Cleanup()
        {
            RestoreAsset(); RestoreVolumes();
            TryRestore(_fps.Restore); TryRestore(_sync.Restore);
            TryRestore(_interval.Restore); TryRestore(_background.Restore);
            _active = false;
        }

        private void TryRestore(Action restore)
        {
            try { restore(); } catch (Exception error) { WarnOnce("Restore failed: " + error.Message); }
        }
        private void WarnOnce(string message)
        { if (_warnings.Add(message)) PotatoPlugin.Log.LogWarning("[Power] " + message); }
    }
}
