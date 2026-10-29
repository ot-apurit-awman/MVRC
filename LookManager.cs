// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using MSCLoader;
using Harmony;
using Valve.VR;


namespace MVRC
{
    public class LookManager
    {
        public bool IsReady { get; private set; } = false;

        private GameObject playerObject;
        private GameObject fpsCameraObject;

        private HMDLook yawLook;   // Attached to PLAYER
        private HMDLook pitchLook; // Attached to FPSCamera
        private HMDLook rollLook;  // Attached to FPSCamera/FPSCamera
        private float yawOffset = 0f;
        private float pitchOffset = 0f;
        private float rollOffset = 0f;

        private readonly SettingsSlider pitchSensitivity;
        private readonly SettingsSlider yawSensitivity;
        private readonly SettingsCheckBox invertPitch;
        private readonly SettingsCheckBox invertYaw;
        
        private readonly TrackedDevicePose_t[] trackedPoses =
            new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        // Controller world positions (metres, Unity coordinate space)
        public static Vector3    LeftControllerPos  = Vector3.zero;
        public static Vector3    RightControllerPos = Vector3.zero;
        public static Vector3    HMDRawPos = Vector3.zero;
        public static Quaternion HMDRawRot = Quaternion.identity;
        public static Quaternion PreApplyRollLocalRotation = Quaternion.identity;
        public static bool HasPreApplyRollLocalRotation = false;

        public static Transform FPSCameraTransform = null;

        // Cache these; find them once and reuse them every frame.
        private CharacterController playerCC = null;
        private float cachedMoveSpeed = 3f; // fallback if FSM var not found
        private HutongGames.PlayMaker.FsmFloat cachedMoveSpeedVar = null;
        private const float MinMoveSpeed = 0.25f;
        private const float WalkMoveSpeed = 1.7f;
        private const float SprintMoveSpeed = 3f;
        private float nextLocomotionRefreshTime = 0f;
        private const float LocomotionRefreshInterval = 0.25f;

        // Controller world rotations
        public static Quaternion LeftControllerRot  = Quaternion.identity;
        public static Quaternion RightControllerRot = Quaternion.identity;

        // Whether each controller had a valid pose last frame
        public static bool LeftControllerValid  = false;
        public static bool RightControllerValid = false;

        [DllImport("user32.dll")] 
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP   = 0x0002;
        
        // Virtual key codes
        private const byte VK_SPACE = 0x20;  // Jump
        private const byte VK_SHIFT = 0x10;  // Sprint  
        private const byte VK_C     = 0x43;  // Crouch
        private const byte VK_W     = 0x57;
        private const byte VK_A     = 0x41;
        private const byte VK_S     = 0x53;
        private const byte VK_D     = 0x44;

        private bool rightAWasPressed    = false;
        private bool rightStickWasPressed = false;
        private bool leftStickWasPressed  = false;
        private bool moveWWasPressed = false;
        private bool moveAWasPressed = false;
        private bool moveSWasPressed = false;
        private bool moveDWasPressed = false;
        private bool sprintToggleEnabled = false;
        private bool sprintToggleRawClickWasPressed = false;

        // Device indices, needed later for GetControllerState() and haptics.
        public static uint LeftControllerIndex  = OpenVR.k_unTrackedDeviceIndexInvalid;
        public static uint RightControllerIndex = OpenVR.k_unTrackedDeviceIndexInvalid;

        private static HarmonyInstance harmony;
        private const string HarmonyId = "msc_vr.lookhijack";

        public enum PlayerMode { OnFoot, InVehicle }
        public static PlayerMode CurrentMode { get; private set; } = PlayerMode.OnFoot;

        private Vector3 positionOffset = Vector3.zero;
        private bool positionOffsetCaptured = false;
        public static Transform PlayerTransform = null;

        public LookManager(
            SettingsSlider pitchSens,
            SettingsSlider yawSens,
            SettingsCheckBox invertPitch,
            SettingsCheckBox invertYaw)
        {
            this.pitchSensitivity = pitchSens;
            this.yawSensitivity = yawSens;
            this.invertPitch = invertPitch;
            this.invertYaw = invertYaw;
        }

        public bool Initialize()
        {
            try
            {
                // Find game objects
                playerObject = GameObject.Find("PLAYER");
                if (playerObject == null)
                {
                    ModConsole.Error("LookManager: PLAYER not found.");
                    return false;
                }

                fpsCameraObject = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera");
                if (fpsCameraObject == null)
                {
                    ModConsole.Error("LookManager: FPSCamera not found.");
                    return false;
                }
                playerCC = playerObject.GetComponent<CharacterController>();
                RefreshLocomotionReferences(true);
                // Patch MouseLook
                //PatchMouseLook();
                
                LookManager.FPSCameraTransform = fpsCameraObject.transform;
                LookManager.PlayerTransform = playerObject.transform;

                // Inject HMDLook components
                yawLook = playerObject.AddComponent<HMDLook>();
                yawLook.axis = HMDLook.Axis.Yaw;

                pitchLook = fpsCameraObject.AddComponent<HMDLook>();
                pitchLook.axis = HMDLook.Axis.Pitch;

                GameObject rollCameraObject = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
                if (rollCameraObject != null)
                {
                    rollLook = rollCameraObject.AddComponent<HMDLook>();
                    rollLook.axis = HMDLook.Axis.Roll;
                }
                else
                {
                    ModConsole.Error("LookManager: FPSCamera/FPSCamera not found; roll tracking unavailable.");
                }

                IsReady = true;
                //PlayMakerHelpers.DebugLog("LookManager initialized; MouseLook hijacked.");
                return true;
            }
            catch (Exception e)
            {
                ModConsole.Error("LookManager.Initialize: " + e.Message);
                return false;
            }
        }

        public void Apply(TrackedDevicePose_t[] poses)
        {
            if (!IsReady || OpenVR.System == null)
            {
                ReleaseLocomotionFallbackInputs();
                return;
            }
            VRInputActions.UpdateState();
            UpdatePlayerMode();

            try
            {                   
                ref TrackedDevicePose_t hmdPose = ref poses[OpenVR.k_unTrackedDeviceIndex_Hmd];
                if (!hmdPose.bPoseIsValid)
                {
                    HMDLook.PoseValid = false;
                    ReleaseLocomotionFallbackInputs();
                    return;
                }

                // Convert OpenVR matrix to Unity matrix
                HmdMatrix34_t mat = hmdPose.mDeviceToAbsoluteTracking;
        
                // Build Unity Matrix4x4 from OpenVR matrix with coordinate system conversion
                // OpenVR: right-handed, Y-up, -Z forward
                // Unity: left-handed, Y-up, Z forward
                Matrix4x4 m = new Matrix4x4();
                m.m00 = mat.m0;  m.m01 = mat.m1;  m.m02 = -mat.m2;  m.m03 = mat.m3;
                m.m10 = mat.m4;  m.m11 = mat.m5;  m.m12 = -mat.m6;  m.m13 = mat.m7;
                m.m20 = -mat.m8; m.m21 = -mat.m9; m.m22 = mat.m10;  m.m23 = -mat.m11;
                m.m30 = 0f;      m.m31 = 0f;      m.m32 = 0f;       m.m33 = 1f;

                // Extract rotation as quaternion, then convert to euler
                Quaternion rot = QuaternionFromMatrix(m);
                Vector3 euler = rot.eulerAngles;

                // Extract raw position from the OpenVR matrix (flip Z for Unity)
                Vector3 rawPos = new Vector3(m.m03, m.m13, m.m23);
                
                // On first valid frame after a recenter, capture the offset
                if (!positionOffsetCaptured)
                {
                    positionOffset = rawPos;
                    positionOffsetCaptured = true;
                }
                
                // Delta from room-origin to now, relative to where you were when you recentered
                HMDLook.CurrentPosition = rawPos - positionOffset;
                
                // Extract pitch, yaw, and roll
                float rawPitch = euler.x;
                float rawYaw = euler.y;
                float rawRoll = euler.z;

                // Normalize to -180..180
                if (rawPitch > 180f) rawPitch -= 360f;
                if (rawYaw > 180f) rawYaw -= 360f;
                if (rawRoll > 180f) rawRoll -= 360f;

                // Apply sensitivity
                float pitchMult = (pitchSensitivity?.GetValue() ?? 1.0f);
                float yawMult = (yawSensitivity?.GetValue() ?? 1.0f);

                // Apply inversion
                if (invertPitch?.GetValue() ?? false) pitchMult *= -1f;
                if (invertYaw?.GetValue() ?? false) yawMult *= -1f;

                

                // Apply offsets (for recentering) and sensitivity
                HMDLook.CurrentYaw = (rawYaw - yawOffset) * yawMult;
                HMDLook.CurrentPitch = (rawPitch - pitchOffset) * pitchMult;
                // Keep roll sign aligned with the tracked HMD rotation.
                HMDLook.CurrentRoll = (rawRoll - rollOffset);
                HMDRawPos = new Vector3(m.m03, m.m13, m.m23);
                HMDRawRot = rot;

                HMDLook.PoseValid = true;

                // ── Controller poses ────────────────────────────────────────────────────────
                // Reset valid flags each frame; they are set true only when a valid pose exists.
                LeftControllerValid  = false;
                RightControllerValid = false;

                LeftControllerIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
                RightControllerIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);

                if (LeftControllerIndex != OpenVR.k_unTrackedDeviceIndexInvalid && poses[LeftControllerIndex].bPoseIsValid)
                {
                    HmdMatrix34_t cm = poses[LeftControllerIndex].mDeviceToAbsoluteTracking;
                    Matrix4x4 c = new Matrix4x4();
                    c.m00 =  cm.m0;  c.m01 =  cm.m1;  c.m02 = -cm.m2;  c.m03 =  cm.m3;
                    c.m10 =  cm.m4;  c.m11 =  cm.m5;  c.m12 = -cm.m6;  c.m13 =  cm.m7;
                    c.m20 = -cm.m8;  c.m21 = -cm.m9;  c.m22 =  cm.m10; c.m23 = -cm.m11;
                    c.m30 =  0f;     c.m31 =  0f;     c.m32 =  0f;     c.m33 =  1f;

                    LeftControllerPos = new Vector3(c.m03, c.m13, c.m23);
                    LeftControllerRot = QuaternionFromMatrix(c);
                    LeftControllerValid = true;
                }

                if (RightControllerIndex != OpenVR.k_unTrackedDeviceIndexInvalid && poses[RightControllerIndex].bPoseIsValid)
                {
                    HmdMatrix34_t cm = poses[RightControllerIndex].mDeviceToAbsoluteTracking;
                    Matrix4x4 c = new Matrix4x4();
                    c.m00 =  cm.m0;  c.m01 =  cm.m1;  c.m02 = -cm.m2;  c.m03 =  cm.m3;
                    c.m10 =  cm.m4;  c.m11 =  cm.m5;  c.m12 = -cm.m6;  c.m13 =  cm.m7;
                    c.m20 = -cm.m8;  c.m21 = -cm.m9;  c.m22 =  cm.m10; c.m23 = -cm.m11;
                    c.m30 =  0f;     c.m31 =  0f;     c.m32 =  0f;     c.m33 =  1f;

                    RightControllerPos = new Vector3(c.m03, c.m13, c.m23);
                    RightControllerRot = QuaternionFromMatrix(c);
                    RightControllerValid = true;
                }
                    // ── Left thumbstick locomotion ────────────────────────────────────────────
                    if (LeftControllerValid && LookManager.CurrentMode == PlayerMode.OnFoot)
                    {
                        RefreshLocomotionReferences(false);

                        VRControllerState_t leftState = default;
                        bool hasLeftState = OpenVR.System.GetControllerState(LeftControllerIndex, ref leftState,
                            (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t)));

                        Vector2 leftStick = VRInputActions.Initialized
                            ? VRInputActions.GetAnalog2D(VRInputActions.Move)
                            : Vector2.zero;

                        // Fallback: some bindings expose stick axes only through raw controller state.
                        if (leftStick == Vector2.zero && hasLeftState)
                            leftStick = new Vector2(leftState.rAxis2.x, leftState.rAxis2.y);

                        const ulong BTN_A = 1UL << 7;
                        const ulong BTN_STICK_CLICK = 1UL << 32;

                        bool sprintPressedThisFrame = false;
                        if (VRInputActions.Initialized)
                        {
                            bool sprintState;
                            bool sprintChanged = VRInputActions.GetDigitalJustChanged(VRInputActions.Sprint, out sprintState);
                            sprintPressedThisFrame = sprintChanged && sprintState;
                        }
                        else if (hasLeftState)
                        {
                            bool rawSprintPressed = (leftState.ulButtonPressed & BTN_STICK_CLICK) != 0;
                            sprintPressedThisFrame = rawSprintPressed && !sprintToggleRawClickWasPressed;
                            sprintToggleRawClickWasPressed = rawSprintPressed;
                        }

                        if (sprintPressedThisFrame)
                            sprintToggleEnabled = !sprintToggleEnabled;

                        float moveX = leftStick.x;
                        float moveZ = leftStick.y;

                        // Deadzone prevents drift when the stick is not perfectly centered.
                        if (Mathf.Abs(moveX) < 0.15f) moveX = 0f;
                        if (Mathf.Abs(moveZ) < 0.15f) moveZ = 0f;

                        bool allowLocomotion = !VRFsmActionBridge.MenuIsOpen;
                        if (!allowLocomotion)
                        {
                            moveX = 0f;
                            moveZ = 0f;
                        }

                        if (moveX != 0f || moveZ != 0f)
                        {
                            // Build direction relative to player's current yaw
                            // (playerObject.transform.forward already has HMD yaw applied)
                            Vector3 forward = playerObject.transform.forward;
                            Vector3 right   = playerObject.transform.right;
                            forward.y = 0f; forward.Normalize();
                            right.y   = 0f; right.Normalize();

                            Vector3 moveInput = forward * moveZ + right * moveX;
                            float inputMagnitude = Mathf.Clamp01(new Vector2(moveX, moveZ).magnitude);
                            Vector3 moveDir = moveInput.sqrMagnitude > 0.0001f
                                ? moveInput.normalized
                                : Vector3.zero;
                            float activeMoveSpeed = sprintToggleEnabled ? SprintMoveSpeed : WalkMoveSpeed;
                            Vector3 motion = moveDir * activeMoveSpeed * inputMagnitude * Time.deltaTime;

                            if (playerCC != null)
                            {
                                // Do not touch Y. CharacterController handles grounding.
                                playerCC.Move(new Vector3(motion.x, 0f, motion.z));
                            }
                        }

                        bool useKeyboardMovementFallback = (playerCC == null);
                        if (useKeyboardMovementFallback)
                            ApplyMovementFallbackKeys(moveX, moveZ);
                        else
                            ApplyMovementFallbackKeys(0f, 0f);

                        SetVirtualKeyState(VK_SHIFT, allowLocomotion && useKeyboardMovementFallback && sprintToggleEnabled, ref leftStickWasPressed);

                        if (RightControllerValid)
                        {
                            VRControllerState_t rightState = default;
                            bool hasRightState = OpenVR.System.GetControllerState(RightControllerIndex, ref rightState,
                                (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t)));

                            Vector2 rightStick = VRInputActions.Initialized
                                ? VRInputActions.GetAnalog2D(VRInputActions.Turn)
                                : Vector2.zero;

                            if (rightStick == Vector2.zero && hasRightState)
                                rightStick = new Vector2(rightState.rAxis2.x, rightState.rAxis2.y);

                            float turnX = rightStick.x;
                            if (Mathf.Abs(turnX) < 0.15f) turnX = 0f;

                            if (turnX != 0f)
                                yawOffset -= turnX * 90f * Time.deltaTime;
                        }

                        // ── Controller button bindings ────────────────────────────────────────────
                        // A button (right controller lower) = bit 7
                        // Right thumbstick click            = bit 32

                        // Right controller buttons
                        if (RightControllerValid)
                        {
                            VRControllerState_t rs = default;
                            OpenVR.System.GetControllerState(RightControllerIndex, ref rs,
                                (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t)));

                            // A button = Jump
                            bool aPressed = (rs.ulButtonPressed & BTN_A) != 0;
                            if (aPressed && !rightAWasPressed) keybd_event(VK_SPACE, 0, KEYEVENTF_KEYDOWN, 0);
                            if (!aPressed && rightAWasPressed) keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, 0);
                            rightAWasPressed = aPressed;

                            // Right stick click = Crouch
                            bool rClickPressed = (rs.ulButtonPressed & BTN_STICK_CLICK) != 0;
                            if (rClickPressed && !rightStickWasPressed) keybd_event(VK_C, 0, KEYEVENTF_KEYDOWN, 0);
                            if (!rClickPressed && rightStickWasPressed) keybd_event(VK_C, 0, KEYEVENTF_KEYUP, 0);
                            rightStickWasPressed = rClickPressed;
                        }
                    }
                    else
                    {
                        ReleaseLocomotionFallbackInputs();
                    }
            }
            catch (Exception e)
            {
                ModConsole.Error("LookManager.Apply: " + e.Message);
                HMDLook.PoseValid = false;
                ReleaseLocomotionFallbackInputs();
            }
        }

        // Add this helper method to extract quaternion from matrix
        private Quaternion QuaternionFromMatrix(Matrix4x4 m)
        {
            Quaternion q = new Quaternion();
            q.w = Mathf.Sqrt(Mathf.Max(0, 1 + m.m00 + m.m11 + m.m22)) / 2;
            q.x = Mathf.Sqrt(Mathf.Max(0, 1 + m.m00 - m.m11 - m.m22)) / 2;
            q.y = Mathf.Sqrt(Mathf.Max(0, 1 - m.m00 + m.m11 - m.m22)) / 2;
            q.z = Mathf.Sqrt(Mathf.Max(0, 1 - m.m00 - m.m11 + m.m22)) / 2;
            q.x *= Mathf.Sign(q.x * (m.m21 - m.m12));
            q.y *= Mathf.Sign(q.y * (m.m02 - m.m20));
            q.z *= Mathf.Sign(q.z * (m.m10 - m.m01));
            return q;
        }

        private void RefreshLocomotionReferences(bool force)
        {
            if (!force && Time.time < nextLocomotionRefreshTime)
                return;

            nextLocomotionRefreshTime = Time.time + LocomotionRefreshInterval;

            if (playerObject == null)
                playerObject = GameObject.Find("PLAYER");

            if (playerObject == null)
                return;

            if (playerCC == null)
                playerCC = playerObject.GetComponent<CharacterController>();

            if (cachedMoveSpeedVar == null)
            {
                foreach (var fsm in playerObject.GetComponents<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.FsmVariables == null)
                        continue;

                    var speedVar = fsm.FsmVariables.GetFsmFloat("Speed");
                    if (speedVar == null)
                        continue;

                    cachedMoveSpeedVar = speedVar;
                    break;
                }
            }

            if (cachedMoveSpeedVar == null)
                return;

            float liveSpeed = cachedMoveSpeedVar.Value;
            if (!float.IsNaN(liveSpeed) && !float.IsInfinity(liveSpeed) && liveSpeed > MinMoveSpeed)
                cachedMoveSpeed = liveSpeed;
        }

        private void ApplyMovementFallbackKeys(float moveX, float moveZ)
        {
            // When direct CharacterController movement is unavailable or ineffective,
            // mirror stick intent to WASD so vanilla movement still works.
            const float keyThreshold = 0.35f;

            SetVirtualKeyState(VK_W, moveZ > keyThreshold, ref moveWWasPressed);
            SetVirtualKeyState(VK_S, moveZ < -keyThreshold, ref moveSWasPressed);
            SetVirtualKeyState(VK_A, moveX < -keyThreshold, ref moveAWasPressed);
            SetVirtualKeyState(VK_D, moveX > keyThreshold, ref moveDWasPressed);
        }

        private void ReleaseLocomotionFallbackInputs()
        {
            ApplyMovementFallbackKeys(0f, 0f);
            SetVirtualKeyState(VK_SHIFT, false, ref leftStickWasPressed);
            sprintToggleRawClickWasPressed = false;
        }

        private void SetVirtualKeyState(byte vk, bool shouldPress, ref bool wasPressed)
        {
            if (shouldPress && !wasPressed)
                keybd_event(vk, 0, KEYEVENTF_KEYDOWN, 0);
            else if (!shouldPress && wasPressed)
                keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);

            wasPressed = shouldPress;
        }
        
        private void UpdatePlayerMode()
        {
            // If the PLAYER object has a parent, they have been parented to the car.
            // This is significantly more reliable than checking string camera names.
            if (playerObject != null && playerObject.transform.parent != null)
            {
                CurrentMode = PlayerMode.InVehicle;
            }
            else
            {
                CurrentMode = PlayerMode.OnFoot;
            }
        }

        /// <summary>
        /// Immediately re-applies the current HMD rotation to both game-camera transforms
        /// without reading new mouse input or fetching a new HMD pose.
        /// Call right after Apply() so cameras are correctly oriented before Camera.Render().
        /// </summary>
        public void ApplyNow()
        {
            if (!IsReady) return;

            // Snapshot roll target before HMD re-apply so compositor-side middleman
            // can preserve additional game-driven offsets (e.g. drunk FSM) safely.
            if (rollLook != null)
            {
                PreApplyRollLocalRotation = rollLook.transform.localRotation;
                HasPreApplyRollLocalRotation = true;
            }
            else
            {
                HasPreApplyRollLocalRotation = false;
            }

            yawLook?.ApplyCurrentRotation();
            pitchLook?.ApplyCurrentRotation();
            rollLook?.ApplyCurrentRotation();
        }

        public void RecenterView()
        {
            if (!IsReady || OpenVR.System == null) return;
        
            try
            {
                OpenVR.System.GetDeviceToAbsoluteTrackingPose(
                    ETrackingUniverseOrigin.TrackingUniverseSeated,
                    0f,
                    trackedPoses
                );
        
                ref TrackedDevicePose_t hmdPose = ref trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd];
        
                if (!hmdPose.bPoseIsValid)
                {
                    ModConsole.Print("Cannot recenter - HMD pose invalid");
                    return;
                }
        
                // Use same conversion as Apply()
                HmdMatrix34_t mat = hmdPose.mDeviceToAbsoluteTracking;
                
                Matrix4x4 m = new Matrix4x4();
                m.m00 = mat.m0;  m.m01 = mat.m1;  m.m02 = -mat.m2;  m.m03 = mat.m3;
                m.m10 = mat.m4;  m.m11 = mat.m5;  m.m12 = -mat.m6;  m.m13 = mat.m7;
                m.m20 = -mat.m8; m.m21 = -mat.m9; m.m22 = mat.m10;  m.m23 = -mat.m11;
                m.m30 = 0f;      m.m31 = 0f;      m.m32 = 0f;       m.m33 = 1f;
        
                Quaternion rot = QuaternionFromMatrix(m);
                Vector3 euler = rot.eulerAngles;
                positionOffsetCaptured = false;
                HMDLook.CurrentPosition = Vector3.zero;
        
                yawOffset = euler.y;
                if (yawOffset > 180f) yawOffset -= 360f;
                
                // Do NOT recenter pitch/roll
                pitchOffset = 0f;
                rollOffset = 0f;
        
                // Also reset mouse rotation in HMDLook
                HMDLook.RecenterView();

                ModConsole.Print($"View recentered");
                PlayMakerHelpers.DebugLog($"View recentered (HMD offsets: yaw={yawOffset:F1}°, pitch={pitchOffset:F1}°, roll={rollOffset:F1}°)");
            }
            catch (Exception e)
            {
                ModConsole.Error("RecenterView error: " + e.Message);
            }
        }
        public void PrintRotation()
        {
            ModConsole.Print(
                        $"HMD Delta Mode: Valid: {HMDLook.PoseValid}, " +
                $"Mode: {CurrentMode}"
            );
        }

        /// <summary>
        /// Prints the current controller poses to the mod console.
        /// Called from the debug keybind in MVRC.cs.
        /// </summary>
        //public void PrintControllerPose()
        //{
            //ModConsole.Print(
                //$"[MVRC Controllers] " +
                //$"L: valid={LeftControllerValid} pos={LeftControllerPos:F3} rot={LeftControllerRot.eulerAngles:F1} idx={LeftControllerIndex} | " +
                //$"R: valid={RightControllerValid} pos={RightControllerPos:F3} rot={RightControllerRot.eulerAngles:F1} idx={RightControllerIndex}"
            //);
        //}

        public void Cleanup()
        {
            // Remove injected components
            if (yawLook != null)
            {
                UnityEngine.Object.DestroyImmediate(yawLook);
                yawLook = null;
            }
            if (pitchLook != null)
            {
                UnityEngine.Object.DestroyImmediate(pitchLook);
                pitchLook = null;
            }
            if (rollLook != null)
            {
                UnityEngine.Object.DestroyImmediate(rollLook);
                rollLook = null;
            }
            // Unpatch harmony
            UnpatchMouseLook();
        
            // Reset shared state
            HMDLook.PoseValid = false;
            HasPreApplyRollLocalRotation = false;
            PreApplyRollLocalRotation = Quaternion.identity;
            // REMOVE THIS LINE - DeltaRotation doesn't exist anymore:
            // HMDLook.DeltaRotation = Quaternion.identity;

            // Reset controller state
            LeftControllerValid  = false;
            RightControllerValid = false;
            LeftControllerIndex  = OpenVR.k_unTrackedDeviceIndexInvalid;
            RightControllerIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
            LeftControllerPos    = Vector3.zero;
            RightControllerPos   = Vector3.zero;
            LeftControllerRot    = Quaternion.identity;
            RightControllerRot   = Quaternion.identity;

            playerObject = null;
            fpsCameraObject = null;
            IsReady = false;
            LookManager.FPSCameraTransform = null;
            LookManager.PlayerTransform = null;
            playerCC = null;
            cachedMoveSpeedVar = null;
            nextLocomotionRefreshTime = 0f;
            keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_C,     0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_W,     0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_A,     0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_S,     0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_D,     0, KEYEVENTF_KEYUP, 0);
            rightAWasPressed     = false;
            rightStickWasPressed = false;
            leftStickWasPressed  = false;
            moveWWasPressed = false;
            moveAWasPressed = false;
            moveSWasPressed = false;
            moveDWasPressed = false;
            sprintToggleEnabled = false;
            sprintToggleRawClickWasPressed = false;

            PlayMakerHelpers.DebugLog("LookManager cleaned up; MouseLook restored.");
        }
        // === Harmony patching ===

        private static void PatchMouseLook()
        {
            if (harmony != null) return;

            harmony = HarmonyInstance.Create(HarmonyId);
            harmony.Patch(
                AccessTools.Method(typeof(MouseLook), "Update"),
                prefix: new HarmonyMethod(typeof(LookManager), "MouseLook_Update_Prefix")
            );

            PlayMakerHelpers.DebugLog("LookManager: MouseLook.Update patched.");
        }

        private static void UnpatchMouseLook()
        {
            if (harmony == null) return;

            harmony.UnpatchAll();
            harmony = null;

            PlayMakerHelpers.DebugLog("LookManager: MouseLook.Update unpatched.");
        }

        public static bool MouseLook_Update_Prefix()
        {
            return false;
        }
    }
}
