using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using PotatoOptimization.Core;

namespace PotatoOptimization.Features
{
    public sealed class CameraMirrorManager
    {
        private readonly AudioManager _audioManager;
        private bool _isMirrored;
        private bool _suspended;
        private Camera _camera;
        private RenderTexture _previousTarget;
        private RenderTexture _texture;
        private GameObject _canvas;
        private RawImage _image;
        private float _nextCheck;
        private UniversalRenderPipelineAsset _asset;
        private float _scale;
        private bool _hdr;
        public bool IsMirrored => _isMirrored;

        public CameraMirrorManager(AudioManager audioManager) { _audioManager = audioManager; }
        public void Toggle() { SetMirrorState(!_isMirrored); }
        public void SetMirrorState(bool enabled)
        {
            _isMirrored = enabled;
            Patches.InputMousePositionPatch.IsInputMirrored = enabled;
            if (enabled) _audioManager.EnableChannelSwap(); else _audioManager.DisableChannelSwap();
            CheckResolutionChange(true);
        }
        public void SetSuspended(bool suspended)
        {
            if (_suspended == suspended) return;
            _suspended = suspended;
            CheckResolutionChange(true);
        }
        public void CheckResolutionChange() { CheckResolutionChange(false); }
        private void CheckResolutionChange(bool force)
        {
            if (!_isMirrored || _suspended) { ReleaseResources(); return; }
            if (!force && Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = Time.realtimeSinceStartup + 0.5f;
            var camera = Camera.main;
            if (camera == null) { ReleaseResources(); return; }
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            // URP applies renderScale to this full output target itself; do not scale twice.
            int width = Mathf.Max(64, Screen.width);
            int height = Mathf.Max(64, Screen.height);
            int samples = camera.allowMSAA ? (asset != null ? asset.msaaSampleCount : Mathf.Max(1, QualitySettings.antiAliasing)) : 1;
            float scale = asset != null ? asset.renderScale : 1f;
            bool hdr = camera.allowHDR && (asset == null || asset.supportsHDR);
            if (_texture != null && _camera == camera && _texture.width == width &&
                _texture.height == height && _texture.antiAliasing == samples &&
                _asset == asset && Mathf.Approximately(_scale, scale) && _hdr == hdr && camera.targetTexture == _texture) return;
            ReleaseResources();
            try
            {
                _camera = camera; _previousTarget = camera.targetTexture;
                _asset = asset; _scale = scale; _hdr = hdr;
                _texture = new RenderTexture(width, height, 24, hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default)
                { name = "iGPU MirrorRT", antiAliasing = samples, filterMode = FilterMode.Bilinear };
                if (!_texture.Create()) throw new InvalidOperationException("Mirror texture creation failed.");
                camera.targetTexture = _texture;
                _canvas = new GameObject("MirrorCanvas", typeof(RectTransform), typeof(Canvas));
                UnityEngine.Object.DontDestroyOnLoad(_canvas);
                var canvas = _canvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -100;
                var image = new GameObject("MirrorDisplay", typeof(RectTransform), typeof(RawImage));
                image.transform.SetParent(_canvas.transform, false);
                _image = image.GetComponent<RawImage>(); _image.texture = _texture;
                _image.uvRect = new Rect(1, 0, -1, 1); _image.raycastTarget = false;
                var rect = image.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            catch (Exception error)
            {
                ReleaseResources(); _isMirrored = false;
                Patches.InputMousePositionPatch.IsInputMirrored = false;
                _audioManager.DisableChannelSwap();
                PotatoPlugin.Log.LogError("[Mirror] " + error);
            }
        }

        private void ReleaseResources()
        {
            if (_camera != null && _camera.targetTexture == _texture) _camera.targetTexture = _previousTarget;
            if (_image != null) _image.texture = null;
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
            if (_texture != null) { _texture.Release(); UnityEngine.Object.Destroy(_texture); }
            _camera = null; _previousTarget = null; _texture = null; _canvas = null; _image = null; _asset = null;
        }
        public void Cleanup() { SetMirrorState(false); ReleaseResources(); }
    }
}
