// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

#if MVRC_DEBUG
using System;
using System.Reflection;
using UnityEngine;
using MSCLoader;
using HutongGames.PlayMaker;

namespace MVRC
{
    public class VRDebugEyeView : MonoBehaviour
    {
        private const int DebugEyeExtraLayerMask = (1 << 14) | (1 << 20);

        private bool _isEnabled = false;
        
        private Camera _leftCamera;
        private Camera _rightCamera;
        private RenderTexture _leftTex;
        private RenderTexture _rightTex;
        private Camera _sourceCamera;
        private int _sourceCameraOriginalCullingMask;
        private bool _sourceCameraLayerOverrideApplied;
        
        private Component _srcFisheye = null;
        private Component _dstFisheyeLeft = null;
        private Component _dstFisheyeRight = null;

        private Transform _vrRigParent = null;
        private Transform _vrHmdFollowRoot = null;
        private Transform _vrDrunkOffsetRoot = null;
        private Transform _sourceCamTransform = null;

        private const int _rtWidth = 512;
        private const int _rtHeight = 512;

        private bool _simulateMouseHmd = false;
        private bool _hasSimulatedBase = false;
        private float _simulatedYawDeg = 0f;
        private float _simulatedPitchDeg = 0f;
        private float _simulatedRollDeg = 0f;
        private Quaternion _simulatedBaseLocalRotation = Quaternion.identity;
        private Quaternion _lastAppliedBaseRotation = Quaternion.identity;
        private Quaternion _lastAppliedExtraRotation = Quaternion.identity;

        private const float SimulatedYawSensitivity = 0.7f;
        private const float SimulatedPitchSensitivity = 0.7f;
        private const float SimulatedPitchClamp = 89f;
        private const float SimulatedRollSpeedDegPerSec = 90f;

        private void Update()
        {
            if (MVRC.debugEyeViewToggleKey != null && MVRC.debugEyeViewToggleKey.GetKeybindDown())
            {
                _isEnabled = !_isEnabled;
                if (_isEnabled)
                {
                    EnableView();
                }
                else
                {
                    DisableView();
                }
            }

            if (MVRC.debugEyeSimToggleKey != null && MVRC.debugEyeSimToggleKey.GetKeybindDown())
            {
                ToggleMouseHmdSimulation();
            }

            if (_isEnabled)
            {
                UpdateSourceCameraLayerOverride();

                if (_simulateMouseHmd)
                {
                    UpdateSimulatedHeadState();
                }

                ApplyDrunkEffect();
                UpdateFisheyeMirror();
            }
        }

        private void OnDisable()
        {
            if (_leftCamera != null)
                _leftCamera.enabled = false;
            if (_rightCamera != null)
                _rightCamera.enabled = false;
            DisableView();
        }

        private void ToggleMouseHmdSimulation()
        {
            _simulateMouseHmd = !_simulateMouseHmd;
            ResetSimulatedHeadPose(true);

            PlayMakerHelpers.DebugLog("[VRDebugEyeView] Mouse HMD simulation "
                + (_simulateMouseHmd ? "enabled." : "disabled."));
        }

        private void ResetSimulatedHeadPose(bool captureCurrentSourceRotation)
        {
            _simulatedYawDeg = 0f;
            _simulatedPitchDeg = 0f;
            _simulatedRollDeg = 0f;

            // Keep simulated pose deterministic and inject live camera effects separately.
            _simulatedBaseLocalRotation = Quaternion.identity;
            _hasSimulatedBase = true;
        }

        private void UpdateSimulatedHeadState()
        {
            if (!_hasSimulatedBase)
            {
                ResetSimulatedHeadPose(true);
            }

            if (MVRC.debugEyeSimResetPoseKey != null && MVRC.debugEyeSimResetPoseKey.GetKeybindDown())
            {
                ResetSimulatedHeadPose(true);
            }

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            _simulatedYawDeg = NormalizeSignedAngle(_simulatedYawDeg + mouseX * SimulatedYawSensitivity);
            _simulatedPitchDeg = Mathf.Clamp(
                _simulatedPitchDeg - mouseY * SimulatedPitchSensitivity,
                -SimulatedPitchClamp,
                SimulatedPitchClamp);

            float rollDirection = 0f;
            if (MVRC.debugEyeSimRollLeftKey != null && MVRC.debugEyeSimRollLeftKey.GetKeybind())
                rollDirection -= 1f;
            if (MVRC.debugEyeSimRollRightKey != null && MVRC.debugEyeSimRollRightKey.GetKeybind())
                rollDirection += 1f;

            if (rollDirection != 0f)
            {
                _simulatedRollDeg = NormalizeSignedAngle(
                    _simulatedRollDeg + rollDirection * SimulatedRollSpeedDegPerSec * Time.deltaTime);
            }
        }

        private static float NormalizeSignedAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private void ApplyDrunkEffect()
        {
            if (_vrHmdFollowRoot == null || _vrDrunkOffsetRoot == null || _sourceCamTransform == null)
                return;

            Quaternion hmdBaseLocalRotation;
            Quaternion scaledExtraRotation;

            if (_simulateMouseHmd)
            {
                if (!_hasSimulatedBase)
                {
                    ResetSimulatedHeadPose(true);
                }

                Quaternion simulatedHeadLocalRotation = Quaternion.Euler(
                    _simulatedPitchDeg,
                    _simulatedYawDeg,
                    _simulatedRollDeg);

                Quaternion liveHmdBaseLocalRotation = VRDrunkRotationUtility.GetHmdBaseRollLocalRotation();
                Quaternion liveSourceLocalRotation = VRDrunkRotationUtility.GetSourceLocalRotation(_sourceCamTransform);
                Quaternion liveExtraDelta = Quaternion.Inverse(liveHmdBaseLocalRotation) * liveSourceLocalRotation;

                Quaternion simulatedSourceLocalRotation = _simulatedBaseLocalRotation
                    * simulatedHeadLocalRotation
                    * liveExtraDelta;

                hmdBaseLocalRotation = VRDrunkRotationUtility.GetHmdBaseRollLocalRotation(_simulatedRollDeg);
                scaledExtraRotation = VRDrunkRotationUtility.GetScaledExtraRotation(
                    simulatedSourceLocalRotation,
                    hmdBaseLocalRotation);
            }
            else
            {
                hmdBaseLocalRotation = VRDrunkRotationUtility.GetHmdBaseRollLocalRotation();
                scaledExtraRotation = VRDrunkRotationUtility.GetScaledExtraRotation(
                    _sourceCamTransform,
                    hmdBaseLocalRotation);
            }

            _vrHmdFollowRoot.localRotation = hmdBaseLocalRotation;
            _vrDrunkOffsetRoot.localRotation = scaledExtraRotation;
            _lastAppliedBaseRotation = hmdBaseLocalRotation;
            _lastAppliedExtraRotation = scaledExtraRotation;
        }

        private void EnableView()
        {
            GameObject sourceCamGO = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            if (sourceCamGO == null)
            {
                PlayMakerHelpers.DebugLog("module not found: PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
                _isEnabled = false;
                return;
            }

            Camera sourceCam = sourceCamGO.GetComponent<Camera>();
            if (sourceCam == null)
            {
                PlayMakerHelpers.DebugLog("module not found: Camera on FPSCamera");
                _isEnabled = false;
                return;
            }

            _sourceCamera = sourceCam;
            _sourceCameraOriginalCullingMask = sourceCam.cullingMask;
            _sourceCameraLayerOverrideApplied = false;

            _sourceCamTransform = sourceCam.transform;
            GameObject outerPivot = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera");
            Transform parentTransform = outerPivot != null ? outerPivot.transform : sourceCam.transform;

            GameObject vrEffectsRootGO = new GameObject("VREffectsRoot");
            vrEffectsRootGO.transform.SetParent(parentTransform, false);
            vrEffectsRootGO.transform.localPosition = Vector3.zero;
            vrEffectsRootGO.transform.localRotation = Quaternion.identity;
            _vrRigParent = vrEffectsRootGO.transform;

            GameObject vrHmdFollowGO = new GameObject("VRHmdFollowRoot");
            vrHmdFollowGO.transform.SetParent(_vrRigParent, false);
            vrHmdFollowGO.transform.localPosition = Vector3.zero;
            vrHmdFollowGO.transform.localRotation = Quaternion.identity;
            _vrHmdFollowRoot = vrHmdFollowGO.transform;

            GameObject vrDrunkOffsetGO = new GameObject("VRDrunkOffsetRoot");
            vrDrunkOffsetGO.transform.SetParent(_vrHmdFollowRoot, false);
            vrDrunkOffsetGO.transform.localPosition = Vector3.zero;
            vrDrunkOffsetGO.transform.localRotation = Quaternion.identity;
            _vrDrunkOffsetRoot = vrDrunkOffsetGO.transform;

            _leftTex = new RenderTexture(_rtWidth, _rtHeight, 24);
            _rightTex = new RenderTexture(_rtWidth, _rtHeight, 24);

            _leftCamera = CreateEyeCamera("VRDebugEyeLeft", sourceCam, _vrDrunkOffsetRoot, _leftTex, -0.03f);
            _rightCamera = CreateEyeCamera("VRDebugEyeRight", sourceCam, _vrDrunkOffsetRoot, _rightTex, 0.03f);

            UpdateSourceCameraLayerOverride();

            if (_simulateMouseHmd)
            {
                ResetSimulatedHeadPose(true);
            }

            InitFisheyeMirror();
        }

        private Camera CreateEyeCamera(string name, Camera sourceCam, Transform parent, RenderTexture targetTex, float xOffset)
        {
            GameObject debugCamGO = new GameObject(name);
            debugCamGO.transform.SetParent(parent, false);
            debugCamGO.transform.localPosition = new Vector3(xOffset, 0, 0);
            debugCamGO.transform.localRotation = Quaternion.identity;

            Camera debugCamera = debugCamGO.AddComponent<Camera>();
            debugCamera.CopyFrom(sourceCam);
            debugCamera.cullingMask |= (1 << 20) | (1 << 14);
            debugCamera.depthTextureMode = DepthTextureMode.Depth;
            debugCamera.targetTexture = targetTex;

            debugCamGO.AddComponent<ForceOpaqueAlpha>();
            debugCamGO.AddComponent<FlareLayer>();

            CopyImageEffects(sourceCam.gameObject, debugCamGO);
            DisableHeavyComponents(debugCamGO);

            return debugCamera;
        }

        private static void DisableHeavyComponents(GameObject go)
        {
            foreach (var b in go.GetComponents<Behaviour>())
            {
                if (b == null) continue;
                string name = b.GetType().Name;
                if (name != "Camera" && name != "FlareLayer" && name != "ForceOpaqueAlpha" && name != "Fisheye")
                {
                    try { b.enabled = false; } catch { }
                }
            }
        }

        private static void CopyImageEffects(GameObject src, GameObject dst)
        {
            if (src == null || dst == null) return;
            foreach (MonoBehaviour comp in src.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                Type t = comp.GetType();
                
                MethodInfo m = t.GetMethod("OnRenderImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) continue;

                if (dst.GetComponent(t) != null) continue;

                Component copy = dst.AddComponent(t);
                if (copy == null) continue;

                foreach (FieldInfo fi in t.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fi.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    try { fi.SetValue(copy, fi.GetValue(comp)); } catch { }
                }

                PlayMakerHelpers.DebugLog("[VRDebugEyeView] Copied image effect '" + t.Name + "' to " + dst.name);
            }
        }

        private void DisableView()
        {
            RestoreSourceCameraLayerOverride();

            if (_leftCamera != null)
            {
                _leftCamera.enabled = false;
                Destroy(_leftCamera.gameObject);
                _leftCamera = null;
            }
            if (_rightCamera != null)
            {
                _rightCamera.enabled = false;
                Destroy(_rightCamera.gameObject);
                _rightCamera = null;
            }
            if (_vrRigParent != null)
            {
                Destroy(_vrRigParent.gameObject);
                _vrRigParent = null;
            }
            _vrHmdFollowRoot = null;
            _vrDrunkOffsetRoot = null;
            _sourceCamTransform = null;
            _sourceCamera = null;
            _sourceCameraOriginalCullingMask = 0;
            _sourceCameraLayerOverrideApplied = false;
            _hasSimulatedBase = false;
            _simulatedBaseLocalRotation = Quaternion.identity;
            _simulatedYawDeg = 0f;
            _simulatedPitchDeg = 0f;
            _simulatedRollDeg = 0f;
            _lastAppliedBaseRotation = Quaternion.identity;
            _lastAppliedExtraRotation = Quaternion.identity;

            _srcFisheye = null;
            _dstFisheyeLeft = null;
            _dstFisheyeRight = null;

            if (_leftTex != null)
            {
                _leftTex.Release();
                Destroy(_leftTex);
                _leftTex = null;
            }

            if (_rightTex != null)
            {
                _rightTex.Release();
                Destroy(_rightTex);
                _rightTex = null;
            }
        }

        private void UpdateSourceCameraLayerOverride()
        {
            if (_sourceCamera == null)
                return;

            if (DebugVrControllerEmulation.IsActive)
            {
                int desiredCullingMask = _sourceCameraOriginalCullingMask | DebugEyeExtraLayerMask;
                if (_sourceCamera.cullingMask != desiredCullingMask)
                {
                    _sourceCamera.cullingMask = desiredCullingMask;
                    _sourceCameraLayerOverrideApplied = true;
                }
            }
            else if (_sourceCameraLayerOverrideApplied || _sourceCamera.cullingMask != _sourceCameraOriginalCullingMask)
            {
                _sourceCamera.cullingMask = _sourceCameraOriginalCullingMask;
                _sourceCameraLayerOverrideApplied = false;
            }
        }

        private void RestoreSourceCameraLayerOverride()
        {
            if (_sourceCamera == null)
                return;

            _sourceCamera.cullingMask = _sourceCameraOriginalCullingMask;
            _sourceCameraLayerOverrideApplied = false;
        }

        void InitFisheyeMirror()
        {
            GameObject fpsCamera = GameObject.Find(
                "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            if (fpsCamera == null) return;

            // Find the fisheye component by name since we don't have the type directly
            foreach (Component c in fpsCamera.GetComponents<Component>())
            {
                if (c.GetType().Name == "Fisheye")
                {
                    _srcFisheye = c;
                    break;
                }
            }

            // Reuse copied image effect instances when available; only add if missing.
            if (_srcFisheye != null && _leftCamera != null && _rightCamera != null)
            {
                Type fisheyeType = _srcFisheye.GetType();

                _dstFisheyeLeft = _leftCamera.gameObject.GetComponent(fisheyeType);
                if (_dstFisheyeLeft == null)
                    _dstFisheyeLeft = _leftCamera.gameObject.AddComponent(fisheyeType);

                _dstFisheyeRight = _rightCamera.gameObject.GetComponent(fisheyeType);
                if (_dstFisheyeRight == null)
                    _dstFisheyeRight = _rightCamera.gameObject.AddComponent(fisheyeType);

                UpdateFisheyeMirror();
            }
        }

        void UpdateFisheyeMirror()
        {
            if (_srcFisheye == null || _dstFisheyeLeft == null || _dstFisheyeRight == null) return;

            foreach (var field in _srcFisheye.GetType().GetFields(
                BindingFlags.Public | 
                BindingFlags.NonPublic | 
                BindingFlags.Instance))
            {
                object value;
                try
                {
                    value = field.GetValue(_srcFisheye);
                }
                catch
                {
                    continue;
                }

                try { field.SetValue(_dstFisheyeLeft, value); } catch { }
                try { field.SetValue(_dstFisheyeRight, value); } catch { }
            }
        }

        private void OnGUI()
        {
            if (!_isEnabled) return;

            if (_leftTex != null)
            {
                GUI.DrawTexture(new Rect(10, 10, _rtWidth, _rtHeight), _leftTex, ScaleMode.StretchToFill, false);
            }
            if (_rightTex != null)
            {
                GUI.DrawTexture(new Rect(10 + _rtWidth + 10, 10, _rtWidth, _rtHeight), _rightTex, ScaleMode.StretchToFill, false);
            }

            float hudY = 10f + _rtHeight + 8f;
            GUI.Label(
                new Rect(10f, hudY, 980f, 20f),
                _simulateMouseHmd
                    ? "Mouse HMD Sim: ON (F8)"
                    : "Mouse HMD Sim: OFF (F8)");

            Vector3 baseEuler = _lastAppliedBaseRotation.eulerAngles;
            Vector3 extraEuler = _lastAppliedExtraRotation.eulerAngles;
            string appliedState = "Applied Base Roll: " + NormalizeSignedAngle(baseEuler.z).ToString("F1")
                + " | Extra XYZ: "
                + NormalizeSignedAngle(extraEuler.x).ToString("F1") + ", "
                + NormalizeSignedAngle(extraEuler.y).ToString("F1") + ", "
                + NormalizeSignedAngle(extraEuler.z).ToString("F1")
                + " | Mult: " + VRDrunkRotationUtility.DrunkRotationMultiplier.ToString("F2");
            GUI.Label(new Rect(10f, hudY + 18f, 980f, 20f), appliedState);

            if (_simulateMouseHmd)
            {
                string simulatedState = "Sim Y/P/R: "
                    + _simulatedYawDeg.ToString("F1") + " / "
                    + _simulatedPitchDeg.ToString("F1") + " / "
                    + _simulatedRollDeg.ToString("F1")
                    + " | Roll Left/Right: , / . | Reset: /";
                GUI.Label(new Rect(10f, hudY + 36f, 980f, 20f), simulatedState);
            }
            else
            {
                GUI.Label(
                    new Rect(10f, hudY + 36f, 980f, 20f),
                    "Live Source Mode: debug eyes follow the FPS camera chain. Toggle simulation with F8.");
            }
        }

        private void OnDestroy()
        {
            DisableView();
        }
    }
}
#endif
