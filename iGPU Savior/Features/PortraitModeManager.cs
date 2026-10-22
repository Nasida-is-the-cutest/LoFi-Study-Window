using UnityEngine;
using PotatoOptimization.Core;

namespace PotatoOptimization.Features
{
    public sealed class PortraitModeManager
    {
        private Camera _camera;
        private Vector3 _position;
        private Quaternion _rotation;
        private float _fov, _size;
        private Vector3 _appliedPosition;
        private Quaternion _appliedRotation;
        private float _appliedFov, _appliedSize;
        public bool IsEnabled { get; private set; }
        public bool IsPortraitMode { get; private set; }
        public void Toggle() { SetEnabled(!IsEnabled); }
        public void SetEnabled(bool enabled)
        { IsEnabled = enabled; if (!enabled) Restore(); else Update(); }

        public void Update()
        {
            var camera = Camera.main;
            if (!IsEnabled || camera == null) return;
            if (_camera != camera) { Restore(); _camera = camera; }
            bool portrait = Screen.height > Screen.width;
            if (portrait == IsPortraitMode) return;
            if (!portrait) { Restore(); return; }
            _position = camera.transform.position; _rotation = camera.transform.rotation;
            _fov = camera.fieldOfView; _size = camera.orthographicSize;
            var rotation = _rotation.eulerAngles;
            _appliedPosition = new Vector3(_position.x * Constants.PortraitPositionXMultiplier,
                _position.y * Constants.PortraitPositionYMultiplier, _position.z * Constants.PortraitPositionZMultiplier);
            _appliedRotation = Quaternion.Euler(rotation.x * Constants.PortraitRotationXMultiplier,
                rotation.y * Constants.PortraitRotationYMultiplier, rotation.z);
            _appliedFov = _fov * Constants.PortraitFOVMultiplier; _appliedSize = _size * Constants.PortraitFOVMultiplier;
            camera.transform.position = _appliedPosition; camera.transform.rotation = _appliedRotation;
            if (camera.orthographic) camera.orthographicSize = _appliedSize; else camera.fieldOfView = _appliedFov;
            _appliedFov = camera.fieldOfView; _appliedSize = camera.orthographicSize;
            IsPortraitMode = true;
        }

        private void Restore()
        {
            if (_camera != null && IsPortraitMode)
            {
                if (_camera.transform.position == _appliedPosition) _camera.transform.position = _position;
                if (_camera.transform.rotation == _appliedRotation) _camera.transform.rotation = _rotation;
                if (Mathf.Approximately(_camera.fieldOfView, _appliedFov)) _camera.fieldOfView = _fov;
                if (Mathf.Approximately(_camera.orthographicSize, _appliedSize)) _camera.orthographicSize = _size;
            }
            _camera = null; IsPortraitMode = false;
        }
        public void Cleanup() { IsEnabled = false; Restore(); }
    }
}
