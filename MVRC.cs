// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using MSCLoader;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using Valve.VR;

namespace MVRC
{
    public class MVRC : Mod
    {
        public override string ID => "My Winter Car VR";
        public override string Name => "My Winter Car VR";
        public override string Author => "ot-apurit-awman";
        public override string Version => "0.6.0";
        public override string Description => "My Winter Car VR Mod";
        public override Game SupportedGames => Game.MyWinterCar;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("MVRC_OpenVR")]
        private static extern int MVRC_SetDeviceFromTexture(IntPtr nativeTex);

        [DllImport("kernel32")]
        private static extern uint GetLastError();

        private float _autostartDelay = -1f;
        // Persistent GO for FSM patcher retries (required in all builds).
        private GameObject _fsmPatcherHostGO = null;

        private VRManager vrManager;

        // --- Keybinds ---
        private SettingsKeybind vrToggleKey;
        private SettingsKeybind recenterKey;

    #if MVRC_DEBUG
        internal static SettingsKeybind keyboardSimTriggerKey;
        internal static SettingsKeybind keyboardSimGripKey;
        internal static SettingsKeybind keyboardSimRightClickKey;
        internal static SettingsKeybind debugSimToggleActiveHandKey;
        internal static SettingsKeybind debugSimModeForwardBackKey;
        internal static SettingsKeybind debugSimModeLeftRightKey;
        internal static SettingsKeybind debugSimModeUpDownKey;
        internal static SettingsKeybind debugSimModeRotateZKey;
        internal static SettingsKeybind debugSimModeRotateXKey;
        internal static SettingsKeybind debugSimModeRotateYKey;
        internal static SettingsKeybind debugSimLeftTriggerKey;
        internal static SettingsKeybind debugSimLeftGripKey;
        internal static SettingsKeybind debugSimLeftRightClickKey;
        internal static SettingsKeybind debugSimToolModeKey;
        internal static SettingsKeybind debugSimDriveModeKey;
        internal static SettingsKeybind debugSimJumpKey;
        internal static SettingsKeybind debugSimCrouchKey;

        internal static SettingsKeybind debugEyeViewToggleKey;
        internal static SettingsKeybind debugEyeSimToggleKey;
        internal static SettingsKeybind debugEyeSimRollLeftKey;
        internal static SettingsKeybind debugEyeSimRollRightKey;
        internal static SettingsKeybind debugEyeSimResetPoseKey;
        internal static SettingsKeybind debugSimFistKey;
        internal static SettingsKeybind debugSimFingerKey;
        internal static SettingsKeybind debugSimResetHandsKey;
    #endif

        // --- Look sensitivity settings ---
        private SettingsSlider pitchSensitivitySlider;
        private SettingsSlider yawSensitivitySlider;
        private SettingsCheckBox invertPitchCheckbox;
        private SettingsCheckBox invertYawCheckbox;

        // --- Rendering settings ---
        private SettingsSlider renderScaleSlider;
        private SettingsSlider ipdMetersSlider;
        private SettingsCheckBox enablePostProcessingOnStereoCheckbox;

        // --- Autostart ---
        private SettingsCheckBox autostartCheckbox;

    #if MVRC_DEBUG
        private SettingsCheckBox debugControllerEmulationCheckbox;

        private GameObject _debugEmulationHostGO = null;
        private VRCameraReparentManager cameraReparentManager = null;
    #endif


        public override void ModSetup()
        {
            SetupFunction(Setup.ModSettings, Mod_Settings);
            SetupFunction(Setup.OnMenuLoad, Mod_OnMenuLoad);
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.PostLoad, Mod_PostLoad);
            SetupFunction(Setup.Update, Mod_Update);
        }

        public void Mod_Settings()
        {
            // Keybinds
            vrToggleKey = Keybind.Add("mvrc_toggle", "Toggle VR", KeyCode.F10);
            recenterKey = Keybind.Add("mvrc_recenter", "Recenter View", KeyCode.R);

#if MVRC_DEBUG
            Keybind.AddHeader("MVRC - Keyboard Sim");
            keyboardSimTriggerKey = Keybind.Add("mvrc_keyboardsim_trigger", "Keyboard Sim: Trigger", KeyCode.Alpha4);
            keyboardSimGripKey = Keybind.Add("mvrc_keyboardsim_grip", "Keyboard Sim: Grip", KeyCode.Alpha3);
            keyboardSimRightClickKey = Keybind.Add("mvrc_keyboardsim_button_b", "Keyboard Sim: Button B / Right Click Modifier", KeyCode.Alpha5);

            Settings.AddHeader("MVRC - Debug Controller Emulation");
            debugControllerEmulationCheckbox = Settings.AddCheckBox("mvrc_debug_ctrl_emu", "Enable Debug Controller Emulation (non-VR)", false);
            debugSimToggleActiveHandKey = Keybind.Add("mvrc_debugsim_toggle_hand", "Debug Sim: Toggle Active Hand", KeyCode.Tab);
            debugSimModeForwardBackKey = Keybind.Add("mvrc_debugsim_mode_fb", "Debug Sim: Mode Forward/Back", KeyCode.LeftBracket);
            debugSimModeLeftRightKey = Keybind.Add("mvrc_debugsim_mode_lr", "Debug Sim: Mode Left/Right", KeyCode.Alpha0);
            debugSimModeUpDownKey = Keybind.Add("mvrc_debugsim_mode_ud", "Debug Sim: Mode Up/Down", KeyCode.Equals);
            debugSimModeRotateZKey = Keybind.Add("mvrc_debugsim_mode_rz", "Debug Sim: Mode Rotate Z", KeyCode.Semicolon);
            debugSimModeRotateXKey = Keybind.Add("mvrc_debugsim_mode_rx", "Debug Sim: Mode Rotate X", KeyCode.RightBracket);
            debugSimModeRotateYKey = Keybind.Add("mvrc_debugsim_mode_ry", "Debug Sim: Mode Rotate Y", KeyCode.P);
            debugSimResetHandsKey = Keybind.Add("mvrc_debugsim_reset_hands", "Debug Sim: Reset Emulated Hands", KeyCode.Quote);
            debugSimLeftTriggerKey = Keybind.Add("mvrc_debugsim_left_trigger", "Debug Sim: Left Trigger", KeyCode.Alpha4, KeyCode.LeftShift);
            debugSimLeftGripKey = Keybind.Add("mvrc_debugsim_left_grip", "Debug Sim: Left Grip", KeyCode.Alpha3, KeyCode.LeftShift);
            debugSimLeftRightClickKey = Keybind.Add("mvrc_debugsim_left_rightclick", "Debug Sim: Left Right Click", KeyCode.Alpha5, KeyCode.LeftShift);
            debugSimToolModeKey = Keybind.Add("mvrc_debugsim_tool_mode", "Debug Sim: Tool Mode", KeyCode.U);
            debugSimDriveModeKey = Keybind.Add("mvrc_debugsim_drive_mode", "Debug Sim: Drive Mode", KeyCode.I);
            debugSimJumpKey = Keybind.Add("mvrc_debugsim_jump", "Debug Sim: Jump", KeyCode.J);
            debugSimCrouchKey = Keybind.Add("mvrc_debugsim_crouch", "Debug Sim: Crouch", KeyCode.K);
            debugEyeViewToggleKey = Keybind.Add("mvrc_debug_eye_view", "Debug Sim: Toggle Eye View", KeyCode.F9);

            Keybind.AddHeader("MVRC - Debug Eye Simulation");
            debugEyeSimToggleKey = Keybind.Add("mvrc_debug_eye_sim_toggle", "Debug Eye: Toggle Mouse HMD Simulation", KeyCode.F8);
            debugEyeSimRollLeftKey = Keybind.Add("mvrc_debug_eye_sim_roll_left", "Debug Eye: Roll Left", KeyCode.Comma);
            debugEyeSimRollRightKey = Keybind.Add("mvrc_debug_eye_sim_roll_right", "Debug Eye: Roll Right", KeyCode.Period);
            debugEyeSimResetPoseKey = Keybind.Add("mvrc_debug_eye_sim_reset", "Debug Eye: Reset Simulated Pose", KeyCode.Slash);

            debugSimFistKey = Keybind.Add("mvrc_debugsim_fist", "Debug Sim: Fist Pose", KeyCode.V);
            debugSimFingerKey = Keybind.Add("mvrc_debugsim_finger", "Debug Sim: Finger Pose", KeyCode.N);
#endif

            // Look sensitivity
            Settings.AddHeader("MVRC - Look Sensitivity");
            pitchSensitivitySlider = Settings.AddSlider("mvrc_pitch_sens", "Pitch Sensitivity", 0.1f, 3.0f, 1.0f);
            yawSensitivitySlider = Settings.AddSlider("mvrc_yaw_sens", "Yaw Sensitivity", 0.1f, 3.0f, 1.0f);
            invertPitchCheckbox = Settings.AddCheckBox("mvrc_invert_pitch", "Invert Pitch", false);
            invertYawCheckbox = Settings.AddCheckBox("mvrc_invert_yaw", "Invert Yaw", false);

            // Rendering
            Settings.AddHeader("VR - Rendering");
            renderScaleSlider = Settings.AddSlider("mvrc_render_scale", "Render Scale (0.5=fast, 1.5=quality)", 0.5f, 1.5f, 1.0f);
            ipdMetersSlider = Settings.AddSlider("mvrc_ipd_mm", "IPD (mm)", 40f, 80f, 64f);
            enablePostProcessingOnStereoCheckbox = Settings.AddCheckBox("mvrc_stereo_postfx", "Enable Post-Processing on Stereo Cameras", false);

            // Autostart
            Settings.AddHeader("MVRC - Autostart");
            autostartCheckbox = Settings.AddCheckBox("mvrc_autostart", "Auto-start VR on game load", false);
        }
        public void Mod_OnMenuLoad()
        {
            VRInputActions.EnsureVrInputAssetsUpToDate(this);
        }

        public void Mod_OnLoad()
        {
            string dllPath = System.IO.Path.Combine(
                System.IO.Path.Combine(Application.dataPath, "Plugins"),
                "MVRC_OpenVR.dll");

            IntPtr libHandle = LoadLibrary(dllPath);
            if (libHandle == IntPtr.Zero)
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                ModConsole.Print("[MVRC] MVRC_OpenVR LoadLibrary failed (Error: " + err + "). "
                    + "VR rendering disabled");
            }
            else
            {
                float ipdMeters = (ipdMetersSlider?.GetValue() ?? 64f) / 1000f;

                vrManager = new VRManager(
                    pitchSensitivitySlider, yawSensitivitySlider,
                    invertPitchCheckbox, invertYawCheckbox,
                    renderScale: renderScaleSlider?.GetValue() ?? 1.0f,
                    ipdMeters: ipdMeters,
                    enablePostProcessing: enablePostProcessingOnStereoCheckbox?.GetValue() ?? false
                );
            }
            ModConsole.Print("MVRC mod loaded.");
        }

        public void Mod_PostLoad()
        {
            VRInjectionConfig.RegisterDefaultRules();
            VRFsmRuntimePatchHook.Install();

            if (_fsmPatcherHostGO == null)
            {
                _fsmPatcherHostGO = new GameObject("MVRC_FsmPatcherHost");
                GameObject.DontDestroyOnLoad(_fsmPatcherHostGO);
                _fsmPatcherHostGO.AddComponent<VRFsmRetryHost>();
#if MVRC_DEBUG
                _fsmPatcherHostGO.AddComponent<VRBridgeKeyboardSim>();
                cameraReparentManager = new VRCameraReparentManager();
#endif
            }

            var patcherHost = _fsmPatcherHostGO.GetComponent<VRFsmRetryHost>();
            patcherHost.StartCoroutine(VRFsmPatcher.RunPatchesAfterDelay(patcherHost));
            
            VRMenuManager.Initialize();
#if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[MVRC] FSM patcher scheduled. Keyboard/controller emulator enabled for debug build.");
#else
            PlayMakerHelpers.DebugLog("[MVRC] FSM patcher scheduled. Emulator systems are disabled in this build.");
#endif

#if MVRC_DEBUG
            if (_debugEmulationHostGO == null)
            {
                _debugEmulationHostGO = new GameObject("MVRC_DebugControllerEmulationHost");
                GameObject.DontDestroyOnLoad(_debugEmulationHostGO);
                _debugEmulationHostGO.AddComponent<DebugVrEmulationHost>();
                _debugEmulationHostGO.AddComponent<VRDebugEyeView>();
            }

            var debugHost = _debugEmulationHostGO.GetComponent<DebugVrEmulationHost>();
            debugHost.Configure(
                () => vrManager != null && vrManager.IsEnabled,
                () => debugControllerEmulationCheckbox != null && debugControllerEmulationCheckbox.GetValue());
#endif

            if (vrManager == null) return;
            if (autostartCheckbox != null && autostartCheckbox.GetValue())
            {
                ModConsole.Print("MVRC: Autostart enabled");
                _autostartDelay = 1f;
            }
        }

        public void Mod_Update()
        {
            VRMenuManager.Initialize();

#if MVRC_DEBUG
            bool vrRuntimeEnabled = vrManager != null && vrManager.IsEnabled;
            DebugVrControllerEmulation.SetActive(
                debugControllerEmulationCheckbox != null && debugControllerEmulationCheckbox.GetValue() && !vrRuntimeEnabled
            );
            if (cameraReparentManager != null) cameraReparentManager.Update();
#endif

            if (_autostartDelay > 0f && vrManager != null)
            {
                _autostartDelay -= Time.deltaTime;
                if (_autostartDelay <= 0f)
                {
                    _autostartDelay = -1f;
                    ModConsole.Print("MVRC: Autostart; starting VR now.");
                    vrManager.Toggle();
                }
            }

            // ── VR-specific update (only when VR is available) ──────────────────────
            if (vrManager != null)
            {
                if (vrToggleKey != null && vrToggleKey.GetKeybindDown())
                    vrManager.Toggle();

                if (recenterKey != null && recenterKey.GetKeybindDown())
                    vrManager.LookManager?.RecenterView();

                vrManager.Update();

                // ── Init VRInputActions once VR is actually running ──────────────────
                // vrManager.Update() is only reached when vrManager != null, but the
                // action system also needs OpenVR.Input to be non-null (it comes up
                // after the headset is initialized, not at LoadLibrary time).
                if (!VRInputActions.Initialized)
                    VRInputActions.Init(this);

                HandleVrControllerRecenterAction();
            }
        }

        public void Mod_OnUnload()
        {
            VRFsmActionBridge.MenuIsOpen = false;

            vrManager?.Shutdown();
            VRFsmRuntimePatchHook.Uninstall();
            if (_fsmPatcherHostGO != null)
            {
                GameObject.Destroy(_fsmPatcherHostGO);
                _fsmPatcherHostGO = null;
            }

#if MVRC_DEBUG
            if (_debugEmulationHostGO != null)
            {
                GameObject.Destroy(_debugEmulationHostGO);
                _debugEmulationHostGO = null;
            }

            DebugVrControllerEmulation.SetActive(false);
#endif
            VRFsmPatcher.Reset();
            VRInputActions.Reset();
            PlayMakerHelpers.DebugLog("MSC VR mod unloaded.");
        }

        private void HandleVrControllerRecenterAction()
        {
            if (vrManager == null || !vrManager.IsEnabled || !VRInputActions.Initialized)
                return;

            // VR-only recenter shortcuts routed through SteamVR action chords.
            VRInputActions.UpdateState();

            bool bIsHeld = VRInputActions.GetBButtonHeld();
            if (bIsHeld != VRInputActions.BModeActive)
            {
                if (bIsHeld)
                    PlayMakerHelpers.DebugLog("[VRInput] B-mode ON");
                else
                    PlayMakerHelpers.DebugLog("[VRInput] B-mode OFF");

                VRInputActions.BModeActive = bIsHeld;
                VRInputActions.UpdateState(true);
            }

            bool recenterState;
            bool recenterChanged = VRInputActions.GetDigitalJustChanged(VRInputActions.BRecenter, out recenterState);

            if (recenterChanged && recenterState)
                vrManager.LookManager?.RecenterView();

            bool driveModeState;
            bool driveModeChanged = VRInputActions.GetDigitalJustChanged(VRInputActions.BDriveMode, out driveModeState);

            if (driveModeChanged && driveModeState)
            {
                VRInteraction.TriggerDriveMode();
                vrManager.LookManager?.RecenterView();
            }
        }
    }

#if MVRC_DEBUG
    // ─────────────────────────────────────────────────────────────────────────────
    // VRBridgeKeyboardSim, unchanged from the original.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRBridgeKeyboardSim : MonoBehaviour
    {
        private float _simDistance = 0.5f;

        private const float DistMin = 0.1f;
        private const float DistMax = 3.0f;
        private const float ScrollScale = 0.5f;

        private GameObject _virtualHandGO = null;
        private VRProximitySphereDebug _proxDebug = null;
        private GameObject _distLabelGO = null;
        private TextMesh _distLabel = null;
        private Collider[] _rawHitsBuffer = new Collider[64];

        void Start()
        {
            _virtualHandGO = new GameObject("MVRC_KeyboardVirtualHand");
            DontDestroyOnLoad(_virtualHandGO);

            _proxDebug = _virtualHandGO.AddComponent<VRProximitySphereDebug>();
            _proxDebug.InteractionRadius = 0.15f;
            _proxDebug.PickupRadius = 0.30f;

            VRProximitySphereDebug.RightHandTransform = _virtualHandGO.transform;

            _distLabelGO = new GameObject("MVRC_KeySimDistLabel");
            DontDestroyOnLoad(_distLabelGO);

            _distLabel = _distLabelGO.AddComponent<TextMesh>();
            _distLabel.fontSize = 32;
            _distLabel.characterSize = 0.004f;
            _distLabel.anchor = TextAnchor.MiddleCenter;
            _distLabel.color = Color.cyan;
            _distLabel.text = _simDistance.ToString("F2") + " m";

            PlayMakerHelpers.DebugLog("[KeyboardSim] Ghost hand active.");
        }

        void Update()
        {
            if (DebugVrControllerEmulation.IsActive)
            {
                if (_proxDebug != null) _proxDebug.enabled = false;
                if (_distLabelGO != null) _distLabelGO.SetActive(false);
                return;
            }

            bool vrActive = (VRHand.RightHandInstance != null);

            if (_proxDebug != null) _proxDebug.enabled = !vrActive;
            if (_distLabelGO != null) _distLabelGO.SetActive(!vrActive);

            if (vrActive) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
                _simDistance = Mathf.Clamp(_simDistance - scroll * ScrollScale, DistMin, DistMax);

            Vector3 handPos = cam.transform.position
                            + cam.transform.forward * _simDistance;

            _virtualHandGO.transform.position = handPos;
            _virtualHandGO.transform.rotation = cam.transform.rotation;

            VRProximitySphereDebug.RightHandTransform = _virtualHandGO.transform;

            _distLabelGO.transform.position = handPos + Vector3.up * 0.18f;
            _distLabelGO.transform.rotation = cam.transform.rotation;
            _distLabel.text = _simDistance.ToString("F2") + " m";

            bool triggerDown = MVRC.keyboardSimTriggerKey != null && MVRC.keyboardSimTriggerKey.GetKeybindDown();
            bool triggerUp = MVRC.keyboardSimTriggerKey != null && MVRC.keyboardSimTriggerKey.GetKeybindUp();
            bool triggerHeld = MVRC.keyboardSimTriggerKey != null && MVRC.keyboardSimTriggerKey.GetKeybind();
            bool gripDown = MVRC.keyboardSimGripKey != null && MVRC.keyboardSimGripKey.GetKeybindDown();
            bool gripUp = MVRC.keyboardSimGripKey != null && MVRC.keyboardSimGripKey.GetKeybindUp();
            bool gripHeld = MVRC.keyboardSimGripKey != null && MVRC.keyboardSimGripKey.GetKeybind();

            VRFsmActionBridge.TriggerJustPressed_R = triggerDown;
            VRFsmActionBridge.TriggerJustReleased_R = triggerUp;
            VRFsmActionBridge.TriggerIsHeld_R = triggerHeld;
            VRFsmActionBridge.GripJustPressed_R = gripDown;
            VRFsmActionBridge.GripJustReleased_R = gripUp;
            VRFsmActionBridge.GripIsHeld_R = gripHeld;

            VRFsmActionBridge.RightClickJustPressed_R = Input.GetKeyDown(KeyCode.R);
            VRFsmActionBridge.LaserRightIsActive_R = Input.GetKey(KeyCode.L);
            VRFsmActionBridge.LaserLeftIsActive_L = Input.GetKey(KeyCode.K);

            VRFsmActionBridge.ControllerRollDelta_R = 0f;

            VRFsmActionBridge.HandPosition_R = handPos;

            VRFsmActionBridge.UpdateDispatcher(handPos);
            
            VRFsmPatcher.RetryPendingPatches();
            
            if (VRFsmActionBridge.ActiveInteractionTarget != null)
                VRFsmActionBridge.NearestRightHandObject =
                    VRFsmActionBridge.ActiveInteractionTarget.gameObject;
            else
                VRFsmActionBridge.NearestRightHandObject = null;

            int rawHitCount = PhysicsCompat.OverlapSphereNonAlloc(handPos, 0.25f, _rawHitsBuffer);
            int totalHits = rawHitCount;

            string interactName = VRFsmActionBridge.ActiveInteractionTarget != null
                ? VRFsmActionBridge.ActiveInteractionTarget.gameObject.name : "none";

            int patchedHits = 0;
            for (int i = 0; i < rawHitCount; i++)
            {
                Collider c = _rawHitsBuffer[i];
                if (c == null || c.gameObject == null) continue;
                Transform t = c.transform;
                while (t != null)
                {
                    PlayMakerFSM[] fsms = t.GetComponents<PlayMakerFSM>();
                    bool found = false;
                    foreach (PlayMakerFSM fsm in fsms)
                    {
                        if (fsm != null && VRFsmPatcher._patchedFsms.Contains(fsm))
                        { patchedHits++; found = true; break; }
                    }
                    if (found) break;
                    t = t.parent;
                }
            }

            // FIX: Apply to the instance, not the static class
            if (_proxDebug != null)
            {
                _proxDebug.CurrentFsmTarget = interactName;
                _proxDebug.LastHitCount = totalHits;
                _proxDebug.LastFsmHitCount = patchedHits;
            }
        }

        void OnDestroy()
        {
            if (_virtualHandGO != null) { Destroy(_virtualHandGO); _virtualHandGO = null; }
            if (_distLabelGO != null) { Destroy(_distLabelGO); _distLabelGO = null; }
            if (_proxDebug != null)
                VRProximitySphereDebug.RightHandTransform = null;
        }
    }
#endif

    public class VRFsmRetryHost : MonoBehaviour
    {
        private bool _patchingComplete = false;

        void Update()
        {
            if (_patchingComplete) return;

            VRFsmPatcher.RetryPendingPatches();

            // Stop polling once the patcher has completed all startup patches.
            // PatcherActive is only set true at the very end of RunAllPatches().
            if (VRFsmPatcher.PatcherActive)
            {
                _patchingComplete = true;
    #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[MVRC] All FSM patches applied; retry loop stopped.");
    #endif
            }
        }

        // Allow reset on scene reload (called by VRFsmPatcher.Reset() or MVRC.Reset())
        public void ResetPatchingState()
        {
            _patchingComplete = false;
        }
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // VRInputActions
    // ─────────────────────────────────────────────────────────────────────────────
    public static class VRInputActions
    {
        private const string VrInputAssetsRevision = "";

        public static ulong SetHandle;
        public static ulong BModeSet;
        public static ulong InteractRight;
        public static ulong InteractLeft;
        public static ulong GrabRight;
        public static ulong GrabLeft;
        public static ulong Jump;
        public static ulong ToolMode;
        public static ulong Crouch;
        public static ulong Sprint;
        public static ulong BHeld;
        public static ulong BModeHeld;
        public static ulong BMenu;
        public static ulong BRightClickRight;
        public static ulong BRightClickLeft;
        public static ulong BLaserRight;
        public static ulong BLaserLeft;
        public static ulong BDriveMode;
        public static ulong BRatchetToggle;
        public static ulong BRecenter;
        public static ulong Turn;
        public static ulong Move;
        public static ulong Fist;
        public static ulong Finger;
        public static ulong BMode;
        private static bool _bModeActive = false;
        private static int _lastBModeChangeFrame = -1;
        public static bool BModeActive
        {
            get { return _bModeActive; }
            set
            {
                if (_bModeActive == value) return;
                int frame = Time.frameCount;
                if (frame == _lastBModeChangeFrame) return;
                _bModeActive = value;
                _lastBModeChangeFrame = frame;
            }
        }
        private static int _lastActionStateFrame = -1;

        public static bool Initialized { get; private set; } = false;

        // Prevents retrying after a hard failure (file missing, API error, exception).
        // Does NOT block retries when OpenVR.Input is simply not ready yet.
        private static bool _initFailed = false;

        public static void Init(Mod mod)
        {
            if (_initFailed) return;  // hard failure already logged; stop retrying

            try
            {
                // OpenVR.Input comes up after the headset is fully initialized.
                // Return silently (not a hard failure) so we retry next frame.
                if (OpenVR.Input == null)
                    return;

                string manifestPath = System.IO.Path.Combine(
                    ModLoader.GetModAssetsFolder(mod), "actions.json");

                ModConsole.Print("[VRInput] Manifest path: " + manifestPath);

                if (!System.IO.File.Exists(manifestPath))
                {
                    ModConsole.Print("[VRInput] actions.json not found; VR input bindings disabled.");
                    _initFailed = true;
                    return;
                }

                EVRInputError err = OpenVR.Input.SetActionManifestPath(manifestPath);
                // MismatchedActionManifest means it was already set; treat that as OK.
                if (err != EVRInputError.None && err != EVRInputError.MismatchedActionManifest)
                {
                    ModConsole.Print("[VRInput] SetActionManifestPath failed: " + err);
                    _initFailed = true;
                    return;
                }

                OpenVR.Input.GetActionSetHandle("/actions/mvrc", ref SetHandle);
                OpenVR.Input.GetActionSetHandle("/actions/mvrc_b", ref BModeSet);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/interact_right", ref InteractRight);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/interact_left", ref InteractLeft);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/grab_right", ref GrabRight);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/grab_left", ref GrabLeft);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/jump", ref Jump);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/tool_mode", ref ToolMode);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/crouch", ref Crouch);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/sprint", ref Sprint);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/b_held", ref BHeld);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/b_held", ref BModeHeld);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/menu", ref BMenu);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/right_click_right", ref BRightClickRight);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/right_click_left", ref BRightClickLeft);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/laser_right", ref BLaserRight);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/laser_left", ref BLaserLeft);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/drive_mode", ref BDriveMode);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/ratchet_toggle", ref BRatchetToggle);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/recenter", ref BRecenter);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/turn", ref Turn);
                OpenVR.Input.GetActionHandle("/actions/mvrc/in/move", ref Move);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/fist", ref Fist);
                OpenVR.Input.GetActionHandle("/actions/mvrc_b/in/finger", ref Finger);

                Initialized = true;
                ModConsole.Print("[VRInput] Initialized successfully.");
            }
            catch (Exception e)
            {
                ModConsole.Print("[VRInput] Init exception: " + e.Message);
                _initFailed = true;
            }
        }

        /// <summary>Call once per frame BEFORE reading any action state.</summary>
        public static void UpdateState(bool force = false)
        {
            if (!Initialized || OpenVR.Input == null) return;

            int frame = Time.frameCount;
            if (!force && _lastActionStateFrame == frame) return;

            // Build a list of all action sets that should be active this frame
            var activeSets = new List<VRActiveActionSet_t>();

            // 1. Main set is ALWAYS active. This keeps your 'Move' action alive constantly.
            activeSets.Add(new VRActiveActionSet_t
            {
                ulActionSet = SetHandle,
                ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle,
                ulSecondaryActionSet = 0,
                nPriority = 0, // Baseline priority
            });

            // 2. If B-Mode (secondary) is active, layer it right on top of the main set
            if (BModeActive)
            {
                activeSets.Add(new VRActiveActionSet_t
                {
                    ulActionSet = BModeSet,
                    ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle,
                    ulSecondaryActionSet = 0,
                    nPriority = 1, // Higher priority! This forces A (Menu) to override A (Jump)
                });
            }

            // Pass the array of active sets to SteamVR
            OpenVR.Input.UpdateActionState(
                activeSets.ToArray(),
                (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRActiveActionSet_t)));

            _lastActionStateFrame = frame;
        }

        public static bool GetDigital(ulong handle)
        {
            var data = new InputDigitalActionData_t();
            OpenVR.Input.GetDigitalActionData(handle, ref data,
                (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t)),
                OpenVR.k_ulInvalidInputValueHandle);
            return data.bActive && data.bState;
        }

        public static bool GetBButtonHeld()
        {
            return BModeActive
                ? GetDigital(BModeHeld)
                : GetDigital(BHeld);
        }

        public static bool GetDigitalJustChanged(ulong handle, out bool state)
        {
            var data = new InputDigitalActionData_t();
            OpenVR.Input.GetDigitalActionData(handle, ref data,
                (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t)),
                OpenVR.k_ulInvalidInputValueHandle);
            state = data.bActive && data.bState;
            return data.bActive && data.bChanged;
        }

        public static Vector2 GetAnalog2D(ulong handle)
        {
            var data = new InputAnalogActionData_t();
            OpenVR.Input.GetAnalogActionData(handle, ref data,
                (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t)),
                OpenVR.k_ulInvalidInputValueHandle);
            return data.bActive ? new Vector2(data.x, data.y) : Vector2.zero;
        }

        /// <summary>
        /// Call on mod unload / VR shutdown so Init() can run again cleanly
        /// if VR is toggled back on in the same session.
        /// </summary>
        public static void Reset()
        {
            Initialized = false;
            _initFailed = false;
            SetHandle = 0;
            InteractRight = 0;
            InteractLeft = 0;
            GrabRight = 0;
            GrabLeft = 0;
            Jump = 0;
            ToolMode = 0;
            Crouch = 0;
            Sprint = 0;
            BHeld = 0;
            BMenu = 0;
            BRightClickRight = 0;
            BRightClickLeft = 0;
            BLaserRight = 0;
            BLaserLeft = 0;
            BDriveMode = 0;
            BRatchetToggle = 0;
            BRecenter = 0;
            Turn = 0;
            Move = 0;
            Fist = 0;
            Finger = 0;
            BModeSet = 0;
            _lastBModeChangeFrame = -1;
            BModeActive = false;
            _lastActionStateFrame = -1;
        }
        public static void EnsureVrInputAssetsUpToDate(Mod mod)
        {
            string assetsFolder = ModLoader.GetModAssetsFolder(mod);
            string currentVersion = mod.Version; // or mod.Info.Version if that's what your API exposes
            string versionStamp = currentVersion + VrInputAssetsRevision;
            string versionFilePath = System.IO.Path.Combine(assetsFolder, "vrinput_version.txt");

            if (System.IO.File.Exists(versionFilePath))
            {
                string savedVersion = System.IO.File.ReadAllText(versionFilePath).Trim();

                if (savedVersion == versionStamp && AreVrInputAssetsPresent(assetsFolder))
                {
                    ModConsole.Print($"[MVRC] VRInput assets already up to date ({versionStamp}).");
                    return;
                }
            }

            ModConsole.Print($"[MVRC] VRInput assets out of date or missing. Updating to {versionStamp}...");
            ExtractVrInputAssets(mod);

            System.IO.File.WriteAllText(versionFilePath, versionStamp);
        }

        private static bool AreVrInputAssetsPresent(string assetsFolder)
        {
            string[] jsonFiles =
            {
                "actions.json",
                "bindings_oculus_touch.json",
                "bindings_vive.json",
                "bindings_knuckles.json",
            };

            for (int i = 0; i < jsonFiles.Length; i++)
            {
                string path = System.IO.Path.Combine(assetsFolder, jsonFiles[i]);
                if (!System.IO.File.Exists(path))
                    return false;
            }

            return true;
        }
        
        public static void ExtractVrInputAssets(Mod mod)
        {
            string assetsFolder = ModLoader.GetModAssetsFolder(mod);
        
            string[] jsonFiles =
            {
                "actions.json",
                "bindings_oculus_touch.json",
                "bindings_vive.json",
                "bindings_knuckles.json",
            };
        
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
        
            foreach (string file in jsonFiles)
            {
                string resourceName = $"MyWinterCarVR.{file}";
                string outputPath = System.IO.Path.Combine(assetsFolder, file);
        
                using (System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        ModConsole.Print($"[MVRC] Missing embedded resource: {resourceName}");
                        continue;
                    }
        
                    using (System.IO.FileStream fs = System.IO.File.Create(outputPath))
                    {
                        // Use 'stream' as the source
                        byte[] buffer = new byte[81920]; // 80 KB buffer
                        int bytesRead;
                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fs.Write(buffer, 0, bytesRead);
                        }
                    }
                }
            }
        
            ModConsole.Print("[MVRC] VRInput assets extracted.");
        }
    }
}
