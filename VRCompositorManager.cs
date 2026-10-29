// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using MSCLoader;
using Valve.VR;

namespace MVRC
{
    /// <summary>
    /// Manages stereo (left + right eye) rendering via IVRCompositor.Submit.
    ///
    /// Creates two eye cameras rendered manually each frame into separate per-eye
    /// RenderTextures. Those textures are submitted to the SteamVR compositor via
    /// IVRCompositor.Submit, giving true native VR rendering with ATW support.
    ///
    /// Performance notes:
    ///   - Cameras are disabled; Camera.Render() is called manually each frame.
    ///   - WaitGetPoses is called before Submit each frame as required by OpenVR.
    ///   - No per-frame allocations. Cameras, RTs, and pose arrays are created once.
    ///   - AudioListener and all non-camera Behaviours are removed from eye GameObjects.
    ///   - depthTextureMode = Depth is set on all cameras for future reprojection.
    /// </summary>
    public class VRCompositorManager
    {
        // True once Initialize() succeeds
        public bool IsReady =>
            OpenVR.Compositor != null
            && leftCam  != null
            && rightCam != null
            && leftEyeRT  != null
            && rightEyeRT != null;

        // True when deferred texture creation has permanently failed
        public bool HasFailed { get; private set; } = false;

        // Eye cameras are disabled and rendered manually.
        private Camera      leftCam,  rightCam;
        private GameObject  leftCamGO, rightCamGO;

        // Separate per-eye RenderTextures (full resolution each)
        private RenderTexture leftEyeRT, rightEyeRT;


        private Component _srcFisheye = null;
        private Component _dstFisheyeLeft = null;
        private Component _dstFisheyeRight = null;

        // Pre-allocated pose arrays, never created per frame (hot path).
        private readonly TrackedDevicePose_t[] renderPoses =
            new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly TrackedDevicePose_t[] gamePoses =
            new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        // Pre-allocated submit structs, updated per frame and never reallocated (hot path).
        private VRTextureBounds_t submitBounds;
        private Texture_t         leftTexInfo;
        private Texture_t         rightTexInfo;

        public static Transform VRHeadTransform = null;

        // Config (read-only after Initialize)
        private readonly float renderScale;
        private readonly float ipdMeters;
        private readonly bool  enablePostProcessing;

        // Making some lights visible 
        private bool _lightsBoosted = false;
        private const float LightRangeBoostMultiplier = 5f;
        private const float MinLightRangeForVR      = 0.15f;

        // Cached native texture pointers, set once after device init and valid for the RT lifetime.
        private IntPtr leftNativePtr  = IntPtr.Zero;
        private IntPtr rightNativePtr = IntPtr.Zero;

        // Projection matrix caching. Matrices are constant between HMD configuration changes.
        private Matrix4x4 cachedLeftProj;
        private Matrix4x4 cachedRightProj;
        private bool  projectionDirty    = true;
        private float projLastRefreshTime = -1f;
        private const float ProjRefreshInterval = 2f; // seconds between GetProjectionMatrix calls

        // Cached image effects for fast syncing
        private struct EffectSyncPair
        {
            public MonoBehaviour src;
            public MonoBehaviour left;
            public MonoBehaviour right;
        }
        private EffectSyncPair[] _effectSyncPairs;

        private Transform _vrRigParent = null;
        private Transform _vrHmdFollowRoot = null;
        private Transform _vrDrunkOffsetRoot = null;
        private Transform _drunkSourceTransform = null;

        // Render-thread event ID used with GL.IssuePluginEvent
        private const int RenderEvent_CopyEyeTextures = 2;

        private int deviceInitCountdown = -1; // -1 = not waiting

        private int renderWidth, renderHeight;

        // Compositor timing feedback (sampled from OpenVR) used for lightweight
        // runtime adaptation when frames become unstable.
        private Compositor_FrameTiming frameTiming;
        private readonly uint frameTimingSize = (uint)Marshal.SizeOf(typeof(Compositor_FrameTiming));
        private uint lastDroppedFrames = 0;
        private int timingStressSamples = 0;
        private bool effectsTemporarilySuppressed = false;
        private float suppressEffectsUntil = 0f;
        private float nextTimingSampleAt = 0f;

        private float lastWaitGetPosesMs = 0f;
        private float lastRenderMs = 0f;
        private float lastSubmitMs = 0f;
        private float lastFrameMs = 0f;
        private string lastTelemetryModeTag = "idle";

        private const float TimingSampleInterval = 0.25f;
        private const float StressFrameIntervalMs = 15.5f;
        private const int StressSamplesBeforeSuppress = 4;
        private const float EffectSuppressDuration = 2.0f;

    #if MVRC_DEBUG
        private float nextDrunkSyncLogAt = 0f;
        private const float DrunkSyncLogInterval = 1.0f;
    #endif

        private bool cleanedUp = false;

        // -----------------------------------------------------------------------
        // MVRC_OpenVR.dll: D3D11 shared texture bridge.
        [DllImport("MVRC_OpenVR")] private static extern int   MVRC_CreateEyeTextures(int w, int h);
        [DllImport("MVRC_OpenVR")] private static extern void  MVRC_CopyToShared(IntPtr unityTexPtr, int isLeft);
        [DllImport("MVRC_OpenVR")] private static extern IntPtr MVRC_GetLeftTexturePtr();
        [DllImport("MVRC_OpenVR")] private static extern IntPtr MVRC_GetRightTexturePtr();
        [DllImport("MVRC_OpenVR")] private static extern IntPtr MVRC_GetLeftSharedHandle();
        [DllImport("MVRC_OpenVR")] private static extern IntPtr MVRC_GetRightSharedHandle();
        [DllImport("MVRC_OpenVR")] private static extern void  MVRC_Destroy();
        [DllImport("MVRC_OpenVR")] private static extern int MVRC_IsReady();
        [DllImport("MVRC_OpenVR")] private static extern int MVRC_GetLastCreateError();
        [DllImport("MVRC_OpenVR")] private static extern int MVRC_GetDiagnostics();
        [DllImport("MVRC_OpenVR")] private static extern int MVRC_SetDeviceFromTexture(IntPtr texPtr);
        // Render-thread copy infrastructure: set pending textures then issue copy via GL.IssuePluginEvent
        [DllImport("MVRC_OpenVR")] private static extern void  MVRC_SetPendingCopyLeft(IntPtr tex);
        [DllImport("MVRC_OpenVR")] private static extern void  MVRC_SetPendingCopyRight(IntPtr tex);
        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        public VRCompositorManager(float renderScale, float ipdMeters, bool enablePostProcessing)
        {
            this.renderScale          = Mathf.Clamp(renderScale, 0.5f, 1.5f);
            this.ipdMeters            = Mathf.Clamp(ipdMeters, 0.04f, 0.08f);
            this.enablePostProcessing = enablePostProcessing;

            // Texture bounds are constant: full UV range with V flipped for DX convention
            submitBounds.uMin = 0f; submitBounds.uMax = 1f;
            submitBounds.vMin = 1f; submitBounds.vMax = 0f;

            // eType and handle are updated per-frame based on available shared handle
            leftTexInfo.eColorSpace  = EColorSpace.Auto;
            rightTexInfo.eColorSpace = EColorSpace.Auto;
        }

        public float LastWaitGetPosesMs => lastWaitGetPosesMs;
        public float LastRenderMs => lastRenderMs;
        public float LastSubmitMs => lastSubmitMs;
        public float LastFrameMs => lastFrameMs;

        public string GetTelemetrySummary()
        {
            return "mode=" + lastTelemetryModeTag
                + " waitMs=" + lastWaitGetPosesMs.ToString("F2")
                + " renderMs=" + lastRenderMs.ToString("F2")
                + " submitMs=" + lastSubmitMs.ToString("F2")
                + " frameMs=" + lastFrameMs.ToString("F2");
        }

        // -----------------------------------------------------------------------
        // Initialization
        // -----------------------------------------------------------------------

        public bool Initialize()
        {
            try
            {
                Camera mainCam = FindFpsCamera();
                if (mainCam == null)
                {
                    ModConsole.Error("VRCompositorManager: FPS camera not found.");
                    return false;
                }

                _drunkSourceTransform = mainCam.transform;

                // Enable depth texture on main cam (allows future reprojection without extra passes)
                mainCam.depthTextureMode = DepthTextureMode.Depth;

                int w = Mathf.RoundToInt(Screen.width  * renderScale);
                int h = Mathf.RoundToInt(Screen.height * renderScale);

                this.renderWidth  = w;
                this.renderHeight = h;

                // Separate full-resolution per-eye RTs
                leftEyeRT  = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                rightEyeRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                leftEyeRT.Create();
                rightEyeRT.Create();

                // --- Left eye camera ---
                leftCamGO = new GameObject("MSC_VR_LeftEyeCamera");
                leftCam = leftCamGO.AddComponent<Camera>();
                CopyCameraSettings(mainCam, leftCam);
                leftCam.cullingMask |= (1 << 20) | (1 << 14);
                leftCam.targetTexture = leftEyeRT;
                leftCam.rect          = new Rect(0f, 0f, 1f, 1f);  // full texture
                leftCam.enabled       = false;                       // manual render only
                leftCamGO.AddComponent<ForceOpaqueAlpha>();
                leftCamGO.AddComponent<FlareLayer>();
                DisableHeavyComponents(leftCamGO);

                // --- Right eye camera ---
                rightCamGO = new GameObject("MSC_VR_RightEyeCamera");
                rightCam = rightCamGO.AddComponent<Camera>();
                CopyCameraSettings(mainCam, rightCam);
                rightCam.cullingMask |= (1 << 20) | (1 << 14);
                rightCam.targetTexture = rightEyeRT;
                rightCam.rect          = new Rect(0f, 0f, 1f, 1f);  // full texture
                rightCam.enabled       = false;
                rightCamGO.AddComponent<ForceOpaqueAlpha>();
                rightCamGO.AddComponent<FlareLayer>();

                DisableHeavyComponents(rightCamGO);

                SyncImageEffectsFromFPSCamera();

                GameObject outerPivot = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera");
                Transform parentTransform = outerPivot != null ? outerPivot.transform : mainCam.transform;

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

                // Parent eye cameras to the middleman chain so additive drunk
                // rotation can be adjusted independently of core HMD look.
                leftCamGO.transform.SetParent(_vrDrunkOffsetRoot, false);
                rightCamGO.transform.SetParent(_vrDrunkOffsetRoot, false);
                
                // Set local position (IPD offset only). Position is now relative to the parent.
                leftCamGO.transform.localPosition  = new Vector3(-ipdMeters * 0.5f, 0f, 0f);
                rightCamGO.transform.localPosition = new Vector3( ipdMeters * 0.5f, 0f, 0f);
                leftCamGO.transform.localRotation  = Quaternion.identity;
                rightCamGO.transform.localRotation = Quaternion.identity;

                SetEyeProjectionMatrices();

                if (!InitializeCompositor())
                {
                    Cleanup();
                    return false;
                }

                // Create shared (NT handle + keyed mutex) eye textures so
                // IVRCompositor.Submit does not return SharedTexturesNotSupported.
                // First, seed the native manager with the D3D11 device extracted from
                // Unity's own RenderTexture. This handles the case where the DLL was
                // loaded manually (via LoadLibrary) after Unity's plugin-init phase and
                // therefore never received the UnitySetGraphicsDevice callback.
                
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = leftEyeRT;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = prev;

                
                // Queue the texture for device extraction on next render thread tick
                
                deviceInitCountdown = 1; // wait 1 SECOND (it seems to be a second not frame) for RT to be GPU-resident
                VRHeadTransform = leftCamGO.transform;

                InitFisheyeMirror();

                PlayMakerHelpers.DebugLog("VRCompositorManager: Waiting for render thread device init...");

                PlayMakerHelpers.DebugLog(
                    "VRCompositorManager initialized. " +
                    "RT=" + w + "x" + h +
                    " (scale=" + renderScale.ToString("F2") + ")" +
                    ", IPD=" + (ipdMeters * 1000f).ToString("F0") + "mm");
                return true;
            }
            catch (Exception e)
            {
                ModConsole.Error("VRCompositorManager.Initialize: " + e.Message);
                Cleanup();
                return false;
            }
        }

        private bool InitializeCompositor()
        {
            if (OpenVR.Compositor == null)
            {
                ModConsole.Error("VRCompositorManager: IVRCompositor not available.");
                return false;
            }
            // Signal compositor. Use an instant transparent fade so there is no black flash on entry.
            OpenVR.Compositor.FadeToColor(0f, 0f, 0f, 0f, 0f, false);
            PlayMakerHelpers.DebugLog("VRCompositorManager: Compositor initialized.");
            return true;
        }

        // -----------------------------------------------------------------------
        // Per-frame update  (called from VRManager.Update)
        // -----------------------------------------------------------------------

        public void Update()
        {
            // Tick the countdown down each frame
            if (deviceInitCountdown > 0)
            {
                deviceInitCountdown--;
                return;
            }
            if (!effectsTemporarilySuppressed)
                SyncEffectEnabledStates();
            
            // Countdown just hit zero. Perform the deferred device and texture initialization.
            if (deviceInitCountdown == 0)
            {
                deviceInitCountdown = -1;
        
                IntPtr ptr = leftEyeRT.GetNativeTexturePtr();
                if (ptr == IntPtr.Zero)
                {
                    ModConsole.Error("MVRC: RT ptr is zero after countdown.");
                    HasFailed = true;
                    Cleanup();
                    return;
                }
        
                int devOk = MVRC_SetDeviceFromTexture(ptr);
                if (devOk != 1)
                {
                    ModConsole.Error("MVRC: SetDeviceFromTexture failed.");
                    HasFailed = true;
                    Cleanup();
                    return;
                }
        
                int result = MVRC_CreateEyeTextures(renderWidth, renderHeight);
                if (result != 1)
                {
                    ModConsole.Error("MVRC: texture creation failed diag=0x"
                        + MVRC_GetDiagnostics().ToString("X")
                        + " hr=0x" + MVRC_GetLastCreateError().ToString("X8"));
                    HasFailed = true;
                    Cleanup();
                    return;
                }
        
                PlayMakerHelpers.DebugLog("MVRC: Shared textures created OK.");

                // Cache native texture pointers. They are stable for the lifetime of the RTs.
                leftNativePtr  = leftEyeRT.GetNativeTexturePtr();
                rightNativePtr = rightEyeRT.GetNativeTexturePtr();

                return;
            }
        
            // Normal per-frame render path
            if (!IsReady) return;
            UpdateEyeTransforms();
            ApplyDrunkEffect();
            UpdateFisheyeMirror();
            RenderAndSubmit();
            UpdateCompositorTimingFeedback();
        }
        private void ApplyDrunkEffect()
        {
            if (_vrHmdFollowRoot == null || _vrDrunkOffsetRoot == null) return;

            if (_drunkSourceTransform == null)
            {
                Camera mainCam = FindFpsCamera();
                if (mainCam == null) return;
                _drunkSourceTransform = mainCam.transform;
            }

            Quaternion hmdBaseLocalRotation = VRDrunkRotationUtility.GetHmdBaseRollLocalRotation();
            Quaternion scaledExtraRotation = VRDrunkRotationUtility.GetScaledExtraRotation(
                _drunkSourceTransform,
                hmdBaseLocalRotation);

            _vrHmdFollowRoot.localRotation = hmdBaseLocalRotation;
            _vrDrunkOffsetRoot.localRotation = scaledExtraRotation;

#if MVRC_DEBUG
            float now = Time.realtimeSinceStartup;
            if (now >= nextDrunkSyncLogAt)
            {
                nextDrunkSyncLogAt = now + DrunkSyncLogInterval;
                Vector3 baseEuler = hmdBaseLocalRotation.eulerAngles;
                Vector3 extraEuler = scaledExtraRotation.eulerAngles;
                PlayMakerHelpers.DebugLog("[MVRC] DrunkSync mult="
                    + VRDrunkRotationUtility.DrunkRotationMultiplier.ToString("F2")
                    + " base=(" + baseEuler.x.ToString("F1") + ","
                    + baseEuler.y.ToString("F1") + ","
                    + baseEuler.z.ToString("F1") + ")"
                    + " extra=(" + extraEuler.x.ToString("F1") + ","
                    + extraEuler.y.ToString("F1") + ","
                    + extraEuler.z.ToString("F1") + ")");
            }
#endif
        }
        void InitFisheyeMirror()
        {
            GameObject fpsCamera = GameObject.Find(
                "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            if (fpsCamera == null) return;

            // Find the fisheye component by name since we don't have the type directly.
            // Use case-insensitive matching because the script name can vary by build.
            foreach (Component c in fpsCamera.GetComponents<Component>())
            {
                string typeName = c.GetType().Name;
                if (typeName.Equals("Fisheye", StringComparison.OrdinalIgnoreCase)
                    || typeName.Equals("FishEye", StringComparison.OrdinalIgnoreCase)
                    || typeName.IndexOf("fisheye", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _srcFisheye = c;
                    break;
                }
            }

            // Reuse copied image effect instances when available; only add if missing.
            if (_srcFisheye != null)
            {
                System.Type fisheyeType = _srcFisheye.GetType();

                _dstFisheyeLeft = leftCamGO.GetComponent(fisheyeType);
                if (_dstFisheyeLeft == null)
                    _dstFisheyeLeft = leftCamGO.AddComponent(fisheyeType);

                _dstFisheyeRight = rightCamGO.GetComponent(fisheyeType);
                if (_dstFisheyeRight == null)
                    _dstFisheyeRight = rightCamGO.AddComponent(fisheyeType);

                UpdateFisheyeMirror();
            }
#if MVRC_DEBUG
            if (_srcFisheye != null)
            {
                foreach (var field in _srcFisheye.GetType().GetFields(
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance))
                {
                    PlayMakerHelpers.DebugLog("[Fisheye] field: " + field.Name 
                        + " = " + field.GetValue(_srcFisheye));
                }
            }
#endif
        }

        void UpdateFisheyeMirror()
        {
            if (_srcFisheye == null || _dstFisheyeLeft == null || _dstFisheyeRight == null) return;

            foreach (var field in _srcFisheye.GetType().GetFields(
                System.Reflection.BindingFlags.Public | 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance))
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

            // Keep fisheye enable state mirrored to VR eyes every frame.
            Behaviour srcBehaviour = _srcFisheye as Behaviour;
            Behaviour leftBehaviour = _dstFisheyeLeft as Behaviour;
            Behaviour rightBehaviour = _dstFisheyeRight as Behaviour;
            if (srcBehaviour != null)
            {
                if (leftBehaviour != null) leftBehaviour.enabled = srcBehaviour.enabled;
                if (rightBehaviour != null) rightBehaviour.enabled = srcBehaviour.enabled;
            }
        }
        private void UpdateEyeTransforms()
        {
            // Refresh projection matrices periodically
            float now = Time.realtimeSinceStartup;
            if (projectionDirty || now - projLastRefreshTime >= ProjRefreshInterval)
            {
                projLastRefreshTime = now;
                SetEyeProjectionMatrices();
            }
        
            // 6DOF position, applied as localPosition on the eye cameras.
            // Eye cameras are children of a middleman chain under FPSCamera. Expressing
            // HMD offset in local space keeps translation aligned with tracking.
            //
            // localOffset = Inverse(roomRot) * roomPositionDelta
            // This converts the tracking-space delta into the parent camera's local space.
            Vector3 hmdLocalOffset = Vector3.zero;
            if (HMDLook.PoseValid)
            {
                hmdLocalOffset = Quaternion.Inverse(LookManager.HMDRawRot)
                               * HMDLook.CurrentPosition;
            }
        
            leftCamGO.transform.localPosition  = new Vector3(-ipdMeters * 0.5f, 0f, 0f) + hmdLocalOffset;
            rightCamGO.transform.localPosition = new Vector3( ipdMeters * 0.5f, 0f, 0f) + hmdLocalOffset;
        }

        private void SetEyeProjectionMatrices()
        {
            if (OpenVR.System == null) return;

            float near = leftCam.nearClipPlane;
            float far  = leftCam.farClipPlane;

            // Get raw asymmetric frustum matrices from the HMD
            HmdMatrix44_t rawLeft  = OpenVR.System.GetProjectionMatrix(
                EVREye.Eye_Left,  near, far);
            HmdMatrix44_t rawRight = OpenVR.System.GetProjectionMatrix(
                EVREye.Eye_Right, near, far);

            cachedLeftProj  = HmdMatrix44ToMatrix4x4(rawLeft);
            cachedRightProj = HmdMatrix44ToMatrix4x4(rawRight);

            leftCam.projectionMatrix  = cachedLeftProj;
            rightCam.projectionMatrix = cachedRightProj;

            projectionDirty = false;
        }

        // Convert Valve row-major HmdMatrix44_t to Unity column-major Matrix4x4
        private static Matrix4x4 HmdMatrix44ToMatrix4x4(HmdMatrix44_t m)
        {
            Matrix4x4 r = new Matrix4x4();
            // Directly map Row, Column to the same index in Unity
            r[0, 0] = m.m0;  r[0, 1] = m.m1;  r[0, 2] = m.m2;  r[0, 3] = m.m3;
            r[1, 0] = m.m4;  r[1, 1] = m.m5;  r[1, 2] = m.m6;  r[1, 3] = m.m7;
            r[2, 0] = m.m8;  r[2, 1] = m.m9;  r[2, 2] = m.m10; r[2, 3] = m.m11;
            r[3, 0] = m.m12; r[3, 1] = m.m13; r[3, 2] = m.m14; r[3, 3] = m.m15;
            return r;
        }


        public TrackedDevicePose_t[] DoWaitGetPoses()
        {
            float waitStart = Time.realtimeSinceStartup;
            OpenVR.Compositor.WaitGetPoses(renderPoses, gamePoses);
            lastWaitGetPosesMs = (Time.realtimeSinceStartup - waitStart) * 1000f;
            return renderPoses;
        }
        
        private void RenderAndSubmit()
        {
            if (OpenVR.Compositor == null) return;

            float frameStart = Time.realtimeSinceStartup;
            float renderStart = frameStart;
        
            if (!OpenVR.Compositor.CanRenderScene())
            {
                ModConsole.Error("CanRenderScene=false: no scene focus, skipping submit");
                lastTelemetryModeTag = "blocked";
                lastFrameMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                return;
            }

            rightCam.Render();
            leftCam.Render();

            lastRenderMs = (Time.realtimeSinceStartup - renderStart) * 1000f;

            // Queue the eye texture copy on the render thread (event ID 2 = copy both eyes).
            // Camera.Render() synchronises the render thread, so the copy event executes after
            // all camera draw commands, avoiding the D3D11 immediate-context race condition.
            float submitStart = Time.realtimeSinceStartup;
            MVRC_SetPendingCopyLeft(leftNativePtr);
            MVRC_SetPendingCopyRight(rightNativePtr);
            GL.IssuePluginEvent(RenderEvent_CopyEyeTextures);

            if (MVRC_IsReady() == 0)
            {
                ModConsole.Error("MVRC_IsReady=0, skipping submit");
                return;
            }
            
            leftTexInfo.eType  = ETextureType.DirectX;
            leftTexInfo.handle = MVRC_GetLeftTexturePtr();
            rightTexInfo.eType  = ETextureType.DirectX;
            rightTexInfo.handle = MVRC_GetRightTexturePtr();
        
            EVRCompositorError errL = OpenVR.Compositor.Submit(
                EVREye.Eye_Left,  ref leftTexInfo,  ref submitBounds, EVRSubmitFlags.Submit_Default);
            EVRCompositorError errR = OpenVR.Compositor.Submit(
                EVREye.Eye_Right, ref rightTexInfo, ref submitBounds, EVRSubmitFlags.Submit_Default);
        
            if (errL != EVRCompositorError.None)
                ModConsole.Error("Submit Left error: " + errL);
            if (errR != EVRCompositorError.None)
                ModConsole.Error("Submit Right error: " + errR);

            lastSubmitMs = (Time.realtimeSinceStartup - submitStart) * 1000f;
            lastFrameMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
            lastTelemetryModeTag = OpenVR.Compositor.CanRenderScene() ? "vr" : "blocked";
        }

        // -----------------------------------------------------------------------
        // Cleanup
        // -----------------------------------------------------------------------

        public void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;

            try { MVRC_Destroy(); } catch (Exception) { }

            if (OpenVR.Compositor != null)
                OpenVR.Compositor.FadeToColor(0.5f, 0f, 0f, 0f, 1f, false);

            if (leftCamGO != null)
            {
                GameObject.DestroyImmediate(leftCamGO);
                leftCamGO = null;
                leftCam   = null;
            }
            if (rightCamGO != null)
            {
                GameObject.DestroyImmediate(rightCamGO);
                rightCamGO = null;
                rightCam   = null;
            }
            if (_vrRigParent != null)
            {
                GameObject.DestroyImmediate(_vrRigParent.gameObject);
                _vrRigParent = null;
            }
            _vrHmdFollowRoot = null;
            _vrDrunkOffsetRoot = null;
            if (leftEyeRT != null)
            {
                leftEyeRT.Release();
                leftEyeRT = null;
            }
            if (rightEyeRT != null)
            {
                rightEyeRT.Release();
                rightEyeRT = null;
            }

            leftNativePtr  = IntPtr.Zero;
            rightNativePtr = IntPtr.Zero;
            projectionDirty = true;
            effectsTemporarilySuppressed = false;
            timingStressSamples = 0;
            lastDroppedFrames = 0;
            suppressEffectsUntil = 0f;
            nextTimingSampleAt = 0f;
#if MVRC_DEBUG
            nextDrunkSyncLogAt = 0f;
#endif
            VRHeadTransform = null;
            _drunkSourceTransform = null;

            PlayMakerHelpers.DebugLog("VRCompositorManager cleaned up.");
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static Camera FindFpsCamera()
        {
            GameObject go = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            return go != null ? go.GetComponent<Camera>() : null;
        }

        private static void CopyCameraSettings(Camera src, Camera dst)
        {
            dst.fieldOfView      = src.fieldOfView;
            dst.nearClipPlane    = src.nearClipPlane;
            dst.farClipPlane     = src.farClipPlane;
            dst.cullingMask      = src.cullingMask;
            dst.clearFlags       = src.clearFlags;
            dst.backgroundColor  = src.backgroundColor;
            // Enable depth texture mode for reprojection support
            dst.depthTextureMode = DepthTextureMode.Depth;
        }

        /// <summary>
        /// Remove AudioListener and disable non-camera Behaviours on a cloned camera GO.
        /// Only Camera, GUILayer, and ForceOpaqueAlpha are kept active by default.
        /// When enablePostProcessing is true, ALL other Behaviours are kept as-is so that
        /// the user can opt-in to running every script/effect on the stereo cameras
        /// (at the cost of duplicate GPU/CPU work per component).
        /// </summary>
        private void DisableHeavyComponents(GameObject go)
        {
            // Only one AudioListener is allowed per scene
            var al = go.GetComponent<AudioListener>();
            if (al != null) GameObject.Destroy(al);

            // Disable game-logic and post-processing Behaviours on the clone
            foreach (var b in go.GetComponents<Behaviour>())
            {
                if (b is Camera || b is GUILayer || b is ForceOpaqueAlpha)
                    continue;   // always keep these

                // When enablePostProcessing is true, all remaining Behaviours (including
                // post-processing effects) are intentionally left enabled so the user
                // gets full visual fidelity on both eyes.
                if (enablePostProcessing)
                    continue;

                b.enabled = false;
            }
        }
        public void SyncImageEffectsFromFPSCamera()
        {
            Camera mainCam = FindFpsCamera();
            if (mainCam == null) return;
            CopyImageEffects(mainCam.gameObject, leftCamGO);
            CopyImageEffects(mainCam.gameObject, rightCamGO);

            // Cache the components so we don't do GetComponents and GameObject.Find every frame
            System.Collections.Generic.List<EffectSyncPair> pairs = new System.Collections.Generic.List<EffectSyncPair>();
            foreach (MonoBehaviour src in mainCam.gameObject.GetComponents<MonoBehaviour>())
            {
                if (src == null) continue;
                System.Type t = src.GetType();
                MonoBehaviour lc = leftCamGO.GetComponent(t) as MonoBehaviour;
                MonoBehaviour rc = rightCamGO.GetComponent(t) as MonoBehaviour;
                if (lc != null && rc != null)
                {
                    pairs.Add(new EffectSyncPair { src = src, left = lc, right = rc });
                }
            }
            _effectSyncPairs = pairs.ToArray();
        }

        private static void CopyImageEffects(GameObject src, GameObject dst)
        {
            if (src == null || dst == null) return;
            foreach (MonoBehaviour comp in src.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                System.Type t = comp.GetType();

                // Only copy components that have an OnRenderImage method (image effects)
                System.Reflection.MethodInfo m = t.GetMethod("OnRenderImage",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (m == null) continue;

                // Don't double-add if already present
                if (dst.GetComponent(t) != null) continue;

                Component copy = dst.AddComponent(t);
                if (copy == null) continue;

                // Copy all instance fields (public + private, including Unity-serialized ones)
                foreach (System.Reflection.FieldInfo fi in t.GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic))
                {
                    if (fi.IsDefined(typeof(System.NonSerializedAttribute), false)) continue;
                    try { fi.SetValue(copy, fi.GetValue(comp)); } catch { }
                }

                #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[MVRC] Copied image effect '" + t.Name + "' to " + dst.name);
                #endif
            }
        }
        public void SyncEffectEnabledStates()
        {
            if (_effectSyncPairs == null) return;
            for (int i = 0; i < _effectSyncPairs.Length; i++)
            {
                var pair = _effectSyncPairs[i];
                if (pair.src != null)
                {
                    bool enabled = pair.src.enabled;
                    pair.left.enabled = enabled;
                    pair.right.enabled = enabled;
                }
            }
        }

        private void SetStereoEffectsEnabled(bool enabled)
        {
            if (_effectSyncPairs == null) return;

            for (int i = 0; i < _effectSyncPairs.Length; i++)
            {
                EffectSyncPair pair = _effectSyncPairs[i];
                if (pair.left != null) pair.left.enabled = enabled;
                if (pair.right != null) pair.right.enabled = enabled;
            }
        }

        private void UpdateCompositorTimingFeedback()
        {
            if (OpenVR.Compositor == null) return;

            float now = Time.realtimeSinceStartup;
            if (now < nextTimingSampleAt) return;

            nextTimingSampleAt = now + TimingSampleInterval;
            frameTiming.m_nSize = frameTimingSize;

            if (!OpenVR.Compositor.GetFrameTiming(ref frameTiming, 0))
                return;

            bool droppedIncreased = frameTiming.m_nNumDroppedFrames > lastDroppedFrames;
            lastDroppedFrames = frameTiming.m_nNumDroppedFrames;

            bool frameUnderStress = frameTiming.m_flClientFrameIntervalMs >= StressFrameIntervalMs
                || frameTiming.m_nReprojectionFlags != 0
                || droppedIncreased;

            if (frameUnderStress)
                timingStressSamples++;
            else if (timingStressSamples > 0)
                timingStressSamples--;

            if (!effectsTemporarilySuppressed
                && timingStressSamples >= StressSamplesBeforeSuppress
                && _effectSyncPairs != null
                && _effectSyncPairs.Length > 0)
            {
                effectsTemporarilySuppressed = true;
                suppressEffectsUntil = now + EffectSuppressDuration;
                SetStereoEffectsEnabled(false);
            }

            if (effectsTemporarilySuppressed && now >= suppressEffectsUntil && !frameUnderStress)
            {
                effectsTemporarilySuppressed = false;
                timingStressSamples = 0;
                SyncEffectEnabledStates();
            }
        }

        private void BoostShortRangeLights()
        {
            if (_lightsBoosted) return;
            _lightsBoosted = true;
        
            // FindObjectsOfType is fine here because this runs once at initialization, not per frame.
            Light[] allLights = UnityEngine.Object.FindObjectsOfType<Light>();
            int boostedCount = 0;
        
            foreach (Light lt in allLights)
            {
                if (lt == null) continue;
                if (lt.type == LightType.Directional) continue; // never boost sun/sky lights
                if (lt.range >= MinLightRangeForVR)   continue; // already large enough
        
                float oldRange = lt.range;
                lt.range = oldRange * LightRangeBoostMultiplier;
                boostedCount++;
        
        #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[MVRC] BoostLight: '" + lt.gameObject.name + "'"
                    + " range " + oldRange.ToString("F3")
                    + " → "    + lt.range.ToString("F3"));
        #endif
            }
        
            #if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[MVRC] BoostShortRangeLights: boosted " + boostedCount
                + " / " + allLights.Length + " lights (threshold=" + MinLightRangeForVR + "m)");
            #endif
        }
        
        // Optional: call this from Update() if lights are added at runtime
        public void ReboostLightsIfNeeded()
        {
            _lightsBoosted = false;
            BoostShortRangeLights();
        }
    }

    internal static class VRDrunkRotationUtility
    {
        private const float MinDrunkMultiplier = 0f;
        private const float MaxDrunkMultiplier = 2f;

        public static float DrunkRotationMultiplier = 1.5f;

        public static Quaternion GetHmdBaseRollLocalRotation()
        {
            float roll = 0f;
            if (HMDLook.PoseValid && !float.IsNaN(HMDLook.CurrentRoll) && !float.IsInfinity(HMDLook.CurrentRoll))
                roll = HMDLook.CurrentRoll;

            return GetHmdBaseRollLocalRotation(roll);
        }

        public static Quaternion GetHmdBaseRollLocalRotation(float rollDegrees)
        {
            if (float.IsNaN(rollDegrees) || float.IsInfinity(rollDegrees))
                rollDegrees = 0f;

            return Quaternion.Euler(0f, 0f, rollDegrees);
        }

        public static Quaternion GetScaledExtraRotation(Transform sourceTransform, Quaternion hmdBaseLocalRotation)
        {
            Quaternion sourceLocalRotation = GetSourceLocalRotation(sourceTransform);

            return GetScaledExtraRotation(sourceLocalRotation, hmdBaseLocalRotation);
        }

        public static Quaternion GetSourceLocalRotation(Transform sourceTransform)
        {
            if (LookManager.HasPreApplyRollLocalRotation)
                return LookManager.PreApplyRollLocalRotation;

            if (sourceTransform != null)
                return sourceTransform.localRotation;

            return Quaternion.identity;
        }

        public static Quaternion GetScaledExtraRotation(Quaternion sourceLocalRotation, Quaternion hmdBaseLocalRotation)
        {
            if (float.IsNaN(sourceLocalRotation.x) || float.IsInfinity(sourceLocalRotation.x)
                || float.IsNaN(sourceLocalRotation.y) || float.IsInfinity(sourceLocalRotation.y)
                || float.IsNaN(sourceLocalRotation.z) || float.IsInfinity(sourceLocalRotation.z)
                || float.IsNaN(sourceLocalRotation.w) || float.IsInfinity(sourceLocalRotation.w))
            {
                sourceLocalRotation = Quaternion.identity;
            }

            // sourceLocalRotation is captured before HMD roll re-apply, so treat it as
            // additive game-driven offset (drunk/wobble) instead of subtracting HMD base again.
            Quaternion extraDelta = sourceLocalRotation;
            float multiplier = Mathf.Clamp(DrunkRotationMultiplier, MinDrunkMultiplier, MaxDrunkMultiplier);
            return Quaternion.Slerp(Quaternion.identity, extraDelta, multiplier);
        }
    }

    /// <summary>
    /// MonoBehaviour that paints the alpha channel solid white after each camera render.
    /// Prevents SteamVR from treating transparent/dark pixels as holes in the scene.
    /// Attached to each eye camera GameObject by VRCompositorManager.
    /// </summary>
    public class ForceOpaqueAlpha : MonoBehaviour
    {
        private Material alphaMat;

        // Fallback shader used when Hidden/Internal-Colored is unavailable (e.g. stripped build).
        // ColorMask A ensures only the alpha channel is written.
        private const string FallbackShaderSource =
            "Shader \"Hidden/MVRC_ForceAlpha\" {" +
            "  SubShader {" +
            "    ZTest Always ZWrite Off Cull Off Fog { Mode Off } ColorMask A" +
            "    Pass {" +
            "      CGPROGRAM" +
            "      #pragma vertex vert" +
            "      #pragma fragment frag" +
            "      #include \"UnityCG.cginc\"" +
            "      struct AppData { float4 vertex : POSITION; fixed4 color : COLOR; };" +
            "      struct V2F { float4 pos : SV_POSITION; fixed4 color : COLOR; };" +
            "      V2F vert(AppData v) { V2F o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; return o; }" +
            "      fixed4 frag(V2F i) : SV_Target { return i.color; }" +
            "      ENDCG" +
            "    }" +
            "  }" +
            "}";

        void OnEnable()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                enabled = false;  // just disable, no fallback
                return;
            }
            alphaMat = new Material(shader);
            alphaMat.SetInt("_ColorMask", 8);
            alphaMat.SetInt("_ZWrite", 0);
        }

        void OnPostRender()
        {
            if (alphaMat == null) return;

            GL.PushMatrix();
            GL.LoadOrtho();
            alphaMat.SetPass(0);
            GL.Begin(GL.QUADS);
            // Paint the alpha channel solid (1.0)
            GL.Color(new Color(1f, 1f, 1f, 1f));
            GL.Vertex3(0, 0, 0);
            GL.Vertex3(0, 1, 0);
            GL.Vertex3(1, 1, 0);
            GL.Vertex3(1, 0, 0);
            GL.End();
            GL.PopMatrix();
        }

        void OnDisable()
        {
            if (alphaMat != null) Destroy(alphaMat);
        }
    }
}
