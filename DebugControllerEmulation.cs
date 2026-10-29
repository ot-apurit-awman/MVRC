// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using System;
using MSCLoader;
using UnityEngine;

namespace MVRC
{
    public struct DebugHandFrameState
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public bool TriggerHeld;
        public bool TriggerDown;
        public bool TriggerUp;
        public bool GripHeld;
        public bool GripDown;
        public bool GripUp;
        public bool RightClickHeld;
        public bool RightClickDown;
        public bool ButtonBHeld;
        public bool ToolModeHeld;
        public bool ToolModeDown;
        public bool DriveModeHeld;
        public bool DriveModeDown;
        public bool JumpHeld;
        public bool JumpDown;
        public bool CrouchHeld;
        public bool CrouchDown;
        public bool MenuHeld;
        public bool MenuDown;
        public bool FistHeld;
        public bool FingerHeld;
    }

    public static class DebugVrControllerEmulation
    {
        public static bool IsActive { get; private set; }

        private static DebugHandFrameState _right;
        private static DebugHandFrameState _left;

        public static DebugHandFrameState RightFrame => _right;
        public static DebugHandFrameState LeftFrame => _left;

        public static DebugHandFrameState GetFrame(VRHand.HandSide side)
        {
            return side == VRHand.HandSide.Right ? _right : _left;
        }

        public static void SetActive(bool active)
        {
            if (IsActive == active) return;
            IsActive = active;
            if (!IsActive)
            {
                _right = default(DebugHandFrameState);
                _left = default(DebugHandFrameState);
            }
        }

        public static void UpdateHand(
            VRHand.HandSide side,
            Vector3 pos,
            Quaternion rot,
            bool triggerHeld,
            bool gripHeld,
            bool rightClickHeld,
            bool buttonBHeld,
            bool toolModeHeld,
            bool driveModeHeld,
            bool jumpHeld,
            bool crouchHeld,
            bool menuHeld,
            bool fistHeld,
            bool fingerHeld)
        {
            DebugHandFrameState prev = side == VRHand.HandSide.Right ? _right : _left;

            DebugHandFrameState next = prev;
            next.Position = pos;
            next.Rotation = rot;

            next.TriggerDown = triggerHeld && !prev.TriggerHeld;
            next.TriggerUp = !triggerHeld && prev.TriggerHeld;
            next.TriggerHeld = triggerHeld;

            next.GripDown = gripHeld && !prev.GripHeld;
            next.GripUp = !gripHeld && prev.GripHeld;
            next.GripHeld = gripHeld;

            next.RightClickDown = rightClickHeld && !prev.RightClickHeld;
            next.RightClickHeld = rightClickHeld;

            next.ButtonBHeld = buttonBHeld;

            next.ToolModeDown = toolModeHeld && !prev.ToolModeHeld;
            next.ToolModeHeld = toolModeHeld;

            next.DriveModeDown = driveModeHeld && !prev.DriveModeHeld;
            next.DriveModeHeld = driveModeHeld;

            next.JumpDown = jumpHeld && !prev.JumpHeld;
            next.JumpHeld = jumpHeld;

            next.CrouchDown = crouchHeld && !prev.CrouchHeld;
            next.CrouchHeld = crouchHeld;

            next.MenuDown = menuHeld && !prev.MenuHeld;
            next.MenuHeld = menuHeld;

            next.FistHeld = fistHeld;
            next.FingerHeld = fingerHeld;

            if (side == VRHand.HandSide.Right)
                _right = next;
            else
                _left = next;
        }
    }

#if MVRC_DEBUG
    public class DebugVrEmulationHost : MonoBehaviour
    {
        private enum AxisMode
        {
            ForwardBack,
            LeftRight,
            UpDown,
            RotateZ,
            RotateX,
            RotateY,
        }

        private Func<bool> _isVrEnabled;
        private Func<bool> _isFeatureEnabled;

        private GameObject _rightGo;
        private GameObject _leftGo;
        private VRHand _rightHand;
        private VRHand _leftHand;

        // Edit these values to change where Reset Emulated Hands places each hand.
        private static readonly Vector3 ResetRightLocalPos = new Vector3(0.46f, -0.27f, 0.65f);
        private static readonly Vector3 ResetLeftLocalPos = new Vector3(-0.46f, -0.27f, 0.65f);
        private static readonly Vector3 ResetRightLocalEuler = Vector3.zero;
        private static readonly Vector3 ResetLeftLocalEuler = Vector3.zero;

        private Vector3 _rightLocalPos = ResetRightLocalPos;
        private Vector3 _leftLocalPos = ResetLeftLocalPos;
        private Vector3 _rightLocalEuler = ResetRightLocalEuler;
        private Vector3 _leftLocalEuler = ResetLeftLocalEuler;


        private bool _activeRightHand = true;
        private AxisMode _mode = AxisMode.ForwardBack;

        private const float MoveScrollSpeed = 0.60f;
        private const float RotateScrollSpeed = 85f;

        private Rect _hudRect = new Rect(12f, 12f, 420f, 320f);
        private GUIStyle _hudLabelStyle;
        private GUIStyle _hudButtonStyle;

        public void Configure(Func<bool> isVrEnabled, Func<bool> isFeatureEnabled)
        {
            _isVrEnabled = isVrEnabled;
            _isFeatureEnabled = isFeatureEnabled;
        }

        private bool ShouldRun()
        {
            bool vrEnabled = _isVrEnabled != null && _isVrEnabled();
            bool featureEnabled = _isFeatureEnabled != null && _isFeatureEnabled();
            return featureEnabled && !vrEnabled;
        }

        void Update()
        {
            bool run = ShouldRun();
            DebugVrControllerEmulation.SetActive(run);

            if (!run)
            {
                TeardownHands();
                return;
            }

            EnsureHands();

            Camera cam = Camera.main;
            if (cam == null) return;

            HandleModeAndTransformInput();

            if (MVRC.debugSimResetHandsKey != null && MVRC.debugSimResetHandsKey.GetKeybindDown())
                ResetHandsToConfiguredOffsets();

            Quaternion camRot = cam.transform.rotation;
            Vector3 camPos = cam.transform.position;

            Vector3 rightWorldPos = camPos + camRot * _rightLocalPos;
            Vector3 leftWorldPos = camPos + camRot * _leftLocalPos;

            Quaternion rightWorldRot = camRot * Quaternion.Euler(_rightLocalEuler);
            Quaternion leftWorldRot = camRot * Quaternion.Euler(_leftLocalEuler);

            bool rightTriggerHeld = MVRC.keyboardSimTriggerKey != null && MVRC.keyboardSimTriggerKey.GetKeybind();
            bool rightGripHeld = MVRC.keyboardSimGripKey != null && MVRC.keyboardSimGripKey.GetKeybind();
            bool rightClickHeld = Input.GetKey(KeyCode.R);

            bool leftTriggerHeld = MVRC.debugSimLeftTriggerKey != null && MVRC.debugSimLeftTriggerKey.GetKeybind();
            bool leftGripHeld = MVRC.debugSimLeftGripKey != null && MVRC.debugSimLeftGripKey.GetKeybind();
            bool leftClickHeld = MVRC.debugSimLeftRightClickKey != null && MVRC.debugSimLeftRightClickKey.GetKeybind();

            bool toolModeHeld = MVRC.debugSimToolModeKey != null && MVRC.debugSimToolModeKey.GetKeybind();
            bool driveModeHeld = MVRC.debugSimDriveModeKey != null && MVRC.debugSimDriveModeKey.GetKeybind();
            bool jumpHeld = MVRC.debugSimJumpKey != null && MVRC.debugSimJumpKey.GetKeybind();
            bool crouchHeld = MVRC.debugSimCrouchKey != null && MVRC.debugSimCrouchKey.GetKeybind();
            bool menuHeld = Input.GetKey(KeyCode.M);
            bool fistHeld = MVRC.debugSimFistKey != null && MVRC.debugSimFistKey.GetKeybind();
            bool fingerHeld = MVRC.debugSimFingerKey != null && MVRC.debugSimFingerKey.GetKeybind();

            bool rightButtonBHeld = Input.GetKey(KeyCode.B);
            bool leftButtonBHeld = leftClickHeld;

            DebugVrControllerEmulation.UpdateHand(
                VRHand.HandSide.Right,
                rightWorldPos,
                rightWorldRot,
                rightTriggerHeld,
                rightGripHeld,
                rightClickHeld,
                rightButtonBHeld,
                toolModeHeld,
                driveModeHeld,
                jumpHeld,
                crouchHeld,
                menuHeld,
                fistHeld,
                fingerHeld);

            DebugVrControllerEmulation.UpdateHand(
                VRHand.HandSide.Left,
                leftWorldPos,
                leftWorldRot,
                leftTriggerHeld,
                leftGripHeld,
                leftClickHeld,
                leftButtonBHeld,
                toolModeHeld,
                driveModeHeld,
                jumpHeld,
                crouchHeld,
                false,
                false,
                false);

            if (_rightHand != null)
                _rightHand.OnDebugEmulatedUpdate(DebugVrControllerEmulation.RightFrame);
            if (_leftHand != null)
                _leftHand.OnDebugEmulatedUpdate(DebugVrControllerEmulation.LeftFrame);

            VRFsmPatcher.RetryPendingPatches();
        }

        private void HandleModeAndTransformInput()
        {
            if (MVRC.debugSimToggleActiveHandKey != null && MVRC.debugSimToggleActiveHandKey.GetKeybindDown())
                _activeRightHand = !_activeRightHand;

            if (MVRC.debugSimModeForwardBackKey != null && MVRC.debugSimModeForwardBackKey.GetKeybindDown()) _mode = AxisMode.ForwardBack;
            if (MVRC.debugSimModeLeftRightKey != null && MVRC.debugSimModeLeftRightKey.GetKeybindDown()) _mode = AxisMode.LeftRight;
            if (MVRC.debugSimModeUpDownKey != null && MVRC.debugSimModeUpDownKey.GetKeybindDown()) _mode = AxisMode.UpDown;
            if (MVRC.debugSimModeRotateZKey != null && MVRC.debugSimModeRotateZKey.GetKeybindDown()) _mode = AxisMode.RotateZ;
            if (MVRC.debugSimModeRotateXKey != null && MVRC.debugSimModeRotateXKey.GetKeybindDown()) _mode = AxisMode.RotateX;
            if (MVRC.debugSimModeRotateYKey != null && MVRC.debugSimModeRotateYKey.GetKeybindDown()) _mode = AxisMode.RotateY;

            float signedStep = 0f;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                signedStep += scroll;

            if (Mathf.Abs(signedStep) < 0.0001f)
                return;

            if (_activeRightHand)
                ApplyStep(ref _rightLocalPos, ref _rightLocalEuler, signedStep);
            else
                ApplyStep(ref _leftLocalPos, ref _leftLocalEuler, signedStep);
        }

        private void ResetHandsToConfiguredOffsets()
        {
            _rightLocalPos = ResetRightLocalPos;
            _leftLocalPos = ResetLeftLocalPos;
            _rightLocalEuler = ResetRightLocalEuler;
            _leftLocalEuler = ResetLeftLocalEuler;
            PlayMakerHelpers.DebugLog("[DebugSim] Emulated hand offsets reset to configured defaults.");
        }

        private static string ModeLabel(AxisMode mode)
        {
            switch (mode)
            {
                case AxisMode.ForwardBack: return "Forward/Back (6)";
                case AxisMode.LeftRight: return "Left/Right (7)";
                case AxisMode.UpDown: return "Up/Down (8)";
                case AxisMode.RotateZ: return "Rotate Z (9)";
                case AxisMode.RotateX: return "Rotate X (-)";
                case AxisMode.RotateY: return "Rotate Y (=)";
                default: return mode.ToString();
            }
        }

        private void EnsureHudStyles()
        {
            if (_hudLabelStyle != null && _hudButtonStyle != null)
                return;

            _hudLabelStyle = new GUIStyle(GUI.skin.label);
            _hudLabelStyle.fontSize = 13;
            _hudLabelStyle.normal.textColor = Color.white;

            _hudButtonStyle = new GUIStyle(GUI.skin.button);
            _hudButtonStyle.fontSize = 13;
        }

        void OnGUI()
        {
            if (!ShouldRun())
                return;

            EnsureHudStyles();

            Vector3 activePos = _activeRightHand ? _rightLocalPos : _leftLocalPos;
            Vector3 activeEuler = _activeRightHand ? _rightLocalEuler : _leftLocalEuler;
            string activeHandLabel = _activeRightHand ? "Right" : "Left";

            GUILayout.BeginArea(_hudRect, GUI.skin.box);
            GUILayout.Label("Debug Emulated Hands", _hudLabelStyle);
            GUILayout.Label("Active Hand: " + activeHandLabel + "  (Tab toggles)", _hudLabelStyle);
            GUILayout.Label("Current Mode: " + ModeLabel(_mode), _hudLabelStyle);
            GUILayout.Space(6f);

            if (GUILayout.Button("Reset Emulated Hands (0)", _hudButtonStyle, GUILayout.Height(26f)))
                ResetHandsToConfiguredOffsets();

            GUILayout.Space(8f);
            GUILayout.Label(string.Format("FB-Offset (6): {0:F3}", activePos.z), _hudLabelStyle);
            GUILayout.Label(string.Format("LR-Offset (7): {0:F3}", activePos.x), _hudLabelStyle);
            GUILayout.Label(string.Format("UD-Offset (8): {0:F3}", activePos.y), _hudLabelStyle);
            GUILayout.Label(string.Format("R-Z (9): {0:F1}", activeEuler.z), _hudLabelStyle);
            GUILayout.Label(string.Format("R-X (-): {0:F1}", activeEuler.x), _hudLabelStyle);
            GUILayout.Label(string.Format("R-Y (=): {0:F1}", activeEuler.y), _hudLabelStyle);

            GUILayout.Space(8f);
            GUILayout.Label(
                string.Format(
                    "Right Pos/Rot: ({0:F3}, {1:F3}, {2:F3}) / ({3:F1}, {4:F1}, {5:F1})",
                    _rightLocalPos.x, _rightLocalPos.y, _rightLocalPos.z,
                    _rightLocalEuler.x, _rightLocalEuler.y, _rightLocalEuler.z),
                _hudLabelStyle);
            GUILayout.Label(
                string.Format(
                    "Left Pos/Rot: ({0:F3}, {1:F3}, {2:F3}) / ({3:F1}, {4:F1}, {5:F1})",
                    _leftLocalPos.x, _leftLocalPos.y, _leftLocalPos.z,
                    _leftLocalEuler.x, _leftLocalEuler.y, _leftLocalEuler.z),
                _hudLabelStyle);
            GUILayout.EndArea();
        }

        private void ApplyStep(ref Vector3 localPos, ref Vector3 localEuler, float signedStep)
        {
            switch (_mode)
            {
                case AxisMode.ForwardBack:
                    localPos.z = Mathf.Clamp(localPos.z + signedStep * MoveScrollSpeed, 0.05f, 2.50f);
                    break;
                case AxisMode.LeftRight:
                    localPos.x = Mathf.Clamp(localPos.x + signedStep * MoveScrollSpeed, -2.0f, 2.0f);
                    break;
                case AxisMode.UpDown:
                    localPos.y = Mathf.Clamp(localPos.y + signedStep * MoveScrollSpeed, -2.0f, 2.0f);
                    break;
                case AxisMode.RotateZ:
                    localEuler.z += signedStep * RotateScrollSpeed;
                    break;
                case AxisMode.RotateX:
                    localEuler.x += signedStep * RotateScrollSpeed;
                    break;
                case AxisMode.RotateY:
                    localEuler.y += signedStep * RotateScrollSpeed;
                    break;
            }
        }

        private void EnsureHands()
        {
            if (_rightHand != null && _leftHand != null)
                return;

            if (_leftGo == null)
            {
                _leftGo = new GameObject("MVRC_DebugLeftHand");
                DontDestroyOnLoad(_leftGo);
            }
            if (_rightGo == null)
            {
                _rightGo = new GameObject("MVRC_DebugRightHand");
                DontDestroyOnLoad(_rightGo);
            }

            if (_leftHand == null)
            {
                _leftHand = _leftGo.AddComponent<VRHand>();
                _leftHand.hand = VRHand.HandSide.Left;
            }
            if (_rightHand == null)
            {
                _rightHand = _rightGo.AddComponent<VRHand>();
                _rightHand.hand = VRHand.HandSide.Right;
            }
        }

        private void TeardownHands()
        {
            if (_leftGo != null)
            {
                GameObject.Destroy(_leftGo);
                _leftGo = null;
                _leftHand = null;
            }
            if (_rightGo != null)
            {
                GameObject.Destroy(_rightGo);
                _rightGo = null;
                _rightHand = null;
            }
        }

        void OnDestroy()
        {
            TeardownHands();
            DebugVrControllerEmulation.SetActive(false);
        }
    }
#else
    public class DebugVrEmulationHost : MonoBehaviour
    {
        public void Configure(Func<bool> isVrEnabled, Func<bool> isFeatureEnabled)
        {
        }
    }
#endif
}
