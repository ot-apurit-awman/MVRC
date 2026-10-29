// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using UnityEngine;
using MSCLoader;
using Valve.VR;

namespace MVRC
{
    // Owns OpenVR lifetime and coordinates all subsystems
    public class VRManager
    {
        public bool IsEnabled { get; private set; } = false;

        // Subsystems
        public VRCompositorManager Compositor { get; private set; }
        public LookManager LookManager { get; private set; }

        // Settings refs passed in from mod file
        private readonly SettingsSlider pitchSensitivity, yawSensitivity;
        private readonly SettingsCheckBox invertPitch, invertYaw;

        // Injected hand visual components. Created on Enable and destroyed on Disable.
        private VRHand leftVRHand  = null;
        private VRHand rightVRHand = null;
        private VRCameraReparentManager cameraReparentManager = null;

        // Compositor config
        private readonly float renderScale;
        private readonly float ipdMeters;
        private readonly bool  enablePostProcessing;

        // Retry limiting prevents infinite Disable/Enable cycling.
        private int   recreateAttempts     = 0;
        private const int maxRecreateAttempts = 3;
        private float recreateCooldown     = 0f;
        private const float recreateCooldownTime = 2f;

        private int originalCullingMask;
        private CameraClearFlags originalClearFlags;
        private Color originalBackgroundColor;
        private bool hasSavedOriginals = false;

        private int telemetryFrameIndex = 0;
        private float lastUpdateMs = 0f;
        private float lastWaitMs = 0f;
        private float lastRenderMs = 0f;
        private float lastSubmitMs = 0f;
        
        public VRManager(
            SettingsSlider pitchSensitivity, SettingsSlider yawSensitivity,
            SettingsCheckBox invertPitch, SettingsCheckBox invertYaw,
            float renderScale, float ipdMeters,
            bool enablePostProcessing)
        {
            this.pitchSensitivity = pitchSensitivity;
            this.yawSensitivity = yawSensitivity;
            this.invertPitch = invertPitch;
            this.invertYaw = invertYaw;
            this.renderScale = renderScale;
            this.ipdMeters = ipdMeters;
            this.enablePostProcessing = enablePostProcessing;
        }

        public void Toggle()
        {
            recreateAttempts = 0;
            recreateCooldown = 0f;
            if (!IsEnabled) Enable();
            else Disable();
        }

        public string GetTelemetrySummary()
        {
            if (Compositor == null)
                return "vr=off frame=" + telemetryFrameIndex + " telemetry=on";

            return "vr=" + (IsEnabled ? "on" : "off")
                + " frame=" + telemetryFrameIndex
                + " telemetry=on"
                + " updateMs=" + lastUpdateMs.ToString("F2")
                + " waitMs=" + lastWaitMs.ToString("F2")
                + " renderMs=" + lastRenderMs.ToString("F2")
                + " submitMs=" + lastSubmitMs.ToString("F2")
                + " compositor=" + Compositor.GetTelemetrySummary();
        }

        private void Enable()
        {
            if (IsEnabled) return;

            if (!OpenVR.IsHmdPresent())
            {
                ModConsole.Error("No HMD detected.");
                return;
            }

            EVRInitError err = EVRInitError.None;
            // Compositor path requires VRApplication_Scene for IVRCompositor.Submit
            OpenVR.Init(ref err, EVRApplicationType.VRApplication_Scene);
            if (err != EVRInitError.None)
            {
                ModConsole.Error("OpenVR init failed: " + err);
                return;
            }

            Compositor = new VRCompositorManager(renderScale, ipdMeters, enablePostProcessing);
            if (!Compositor.Initialize())
            {
                ModConsole.Error("VRCompositorManager failed to initialize.");
                Compositor = null;
                OpenVR.Shutdown();
                return;
            }

            LookManager = new LookManager(
                pitchSensitivity,
                yawSensitivity,
                invertPitch,
                invertYaw
            );
            if (!LookManager.Initialize())
            {
                ModConsole.Print("LookManager failed to initialize - head tracking unavailable.");
                // Don't fail completely - compositor still works without head tracking
            }

            IsEnabled = true;
            recreateAttempts = 0;
            ModConsole.Print("VR enabled.");
            SaveOriginalCameraSettings();
            Camera.main.cullingMask = 0; 
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = Color.black;
            QualitySettings.vSyncCount = 0;

            // ── Hand visuals ─────────────────────────────────────────────────────────────
            // Each hand gets its own root GameObject so VRHand.transform.position can
            // be set in world space without conflicting with any game hierarchy.
            GameObject leftHandGO  = new GameObject("MVRC_LeftHand");
            GameObject rightHandGO = new GameObject("MVRC_RightHand");

            // Do NOT parent to any game object. World space is correct here because
            // LookManager controller positions are already in Unity world space.
            // Parenting to the player or camera would apply an unwanted extra transform.
            leftVRHand  = leftHandGO.AddComponent<VRHand>();
            leftVRHand.hand = VRHand.HandSide.Left;

            rightVRHand = rightHandGO.AddComponent<VRHand>();
            rightVRHand.hand = VRHand.HandSide.Right;

            cameraReparentManager = new VRCameraReparentManager();

            PlayMakerHelpers.DebugLog("[MVRC] Hand visuals injected.");
        }

        private void Disable()
        {
            if (!IsEnabled) return;

            // Destroy hand visuals before LookManager cleanup
            if (leftVRHand != null)
            {
                GameObject.DestroyImmediate(leftVRHand.gameObject);
                leftVRHand = null;
            }
            if (rightVRHand != null)
            {
                GameObject.DestroyImmediate(rightVRHand.gameObject);
                rightVRHand = null;
            }

            if (cameraReparentManager != null)
            {
                cameraReparentManager.Cleanup(true);
                cameraReparentManager = null;
            }

            LookManager?.Cleanup();
            Compositor?.Cleanup();

            LookManager = null;
            Compositor  = null;

            OpenVR.Shutdown();
            IsEnabled = false;
            Camera.main.cullingMask = originalCullingMask; 
            Camera.main.clearFlags = originalClearFlags;
            Camera.main.backgroundColor = originalBackgroundColor;
            ModConsole.Print("VR disabled.");
        }

        public void Shutdown() => Disable();

        void SaveOriginalCameraSettings()
        {
            if (!hasSavedOriginals && Camera.main != null)
            {
                originalCullingMask = Camera.main.cullingMask;
                originalClearFlags = Camera.main.clearFlags;
                originalBackgroundColor = Camera.main.backgroundColor;
                
                hasSavedOriginals = true; // Prevents overwriting with "blinded" values later
            }
        }

        public void Update()
        {
            if (!IsEnabled) return;

            float frameStart = Time.realtimeSinceStartup;

            if (!Compositor.IsReady)
            {
                // Permanent failure, such as native texture creation failure. Stop retrying.
                if (Compositor.HasFailed || recreateAttempts >= maxRecreateAttempts)
                {
                    if (recreateAttempts >= maxRecreateAttempts)
                        ModConsole.Error("Compositor failed after " + recreateAttempts + " attempts; disabling VR.");
                    else
                        ModConsole.Error("Compositor init failed permanently; disabling VR.");
                    Disable();
                    return;
                }

                // Cooldown between recreation attempts
                if (recreateCooldown > 0f)
                {
                    recreateCooldown -= Time.deltaTime;
                    return;
                }

                recreateAttempts++;
                ModConsole.Print("Compositor not ready; recreating (attempt " + recreateAttempts + "/" + maxRecreateAttempts + ").");
                Disable();
                Enable();
                recreateCooldown = recreateCooldownTime;
                return;
            }

            var poses = Compositor.DoWaitGetPoses(); // blocks until right time, returns predicted poses
            LookManager?.Apply(poses);    // Fetch latest HMD pose → update CurrentYaw/Pitch
            LookManager?.ApplyNow(); 

            if (leftVRHand != null) leftVRHand.OnVRUpdate();
            if (rightVRHand != null) rightVRHand.OnVRUpdate();
            if (cameraReparentManager != null) cameraReparentManager.Update();

            Compositor.Update();     // Render both eyes with current-frame pose

            telemetryFrameIndex++;
            lastUpdateMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
            lastWaitMs = Compositor.LastWaitGetPosesMs;
            lastRenderMs = Compositor.LastRenderMs;
            lastSubmitMs = Compositor.LastSubmitMs;
        }
    }
}