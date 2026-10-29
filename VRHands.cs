// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;
using Valve.VR;
using static MVRC.VRHand;

namespace MVRC
{
    public class VRHand : MonoBehaviour
    {
        public enum HandSide { Left, Right }
        public HandSide hand;

        // ── Hand poses ────────────────────────────────────────────────────────────
        public enum HandPose
        {
            Open = 0,
            Fist = 1,
            MiddleFinger = 2,
        }
        private const int PoseCount = 3;

        private struct HandPoseConfig
        {
            public string pathRight;
            public string pathLeft;
            public Vector3 pivotOffset;
            public Vector3 handOffset;
            public Vector3 pivotRotEuler;
            public Vector3 contRotEuler;
            public Vector3 wristLocal;
            public bool reverseWinding;
        }

        private const string HandPushBase = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Hand Push/Pivot/";
        private const string OpenPathRight = HandPushBase + "hand/hand_rigged";
        private const string OpenPathLeft = HandPushBase + "hand 1/hand_rigged";

        private const string FistPathLeft = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Fist/Pivot/hand/hand_rigged";
        private const string FistPathRight = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Fist/Pivot/hand/hand_rigged";

        private const string MiddleFingerPathLeft = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/MiddleFinger/Pivot/hand/hand_rigged";
        private const string MiddleFingerPathRight = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/MiddleFinger/Pivot/hand/hand_rigged";

        private static readonly HandPoseConfig[] PoseConfigs = new HandPoseConfig[PoseCount]
        {
            new HandPoseConfig
            {
                pathRight     = OpenPathRight,
                pathLeft      = OpenPathLeft,
                pivotOffset   = new Vector3(0.0f, 0.0f, 0.0f),
                handOffset    = new Vector3(0.0f, 0.0f, 0.0f),
                pivotRotEuler = new Vector3(2.6f, 178f, 66f),
                contRotEuler  = new Vector3(0.0f, 0.0f, 0.0f),
                wristLocal    = new Vector3(0.0f, -0.26f, -0.27f),
            },
            new HandPoseConfig
            {
                pathRight     = FistPathRight,
                pathLeft      = FistPathLeft,
                pivotOffset   = new Vector3(0.0f, 0.0f, 0.0f),
                handOffset    = new Vector3(0.0f, 0.0f, 0.0f),
                pivotRotEuler = new Vector3(2.6f, 178f, 45f),
                contRotEuler  = new Vector3(0.0f, 0.0f, 0.0f),
                wristLocal    = new Vector3(0.0f, -0.26f, -0.27f),
            },
            new HandPoseConfig
            {
                pathRight     = MiddleFingerPathRight,
                pathLeft      = MiddleFingerPathLeft,
                pivotOffset   = new Vector3(0.0f, 0.0f, 0.0f),
                handOffset    = new Vector3(0.0f, 0.0f, 0.0f),
                pivotRotEuler = new Vector3(2.6f, 27.0f, 110f),
                contRotEuler  = new Vector3(0.0f, 0.0f, 0.0f),
                wristLocal    = new Vector3(0.09f, 0.28f, 0.25f),
                reverseWinding = true,
            },
        };

        private const float HandMeshScale = 1.0f;

        // ── Joystick thresholds ───────────────────────────────────────────────────
        private const float JoyThreshold = 0.7f;
        private const float JoyDeadzone = 0.2f;
        private bool _joyPoseActive = false;

        // ── Pose runtime state ────────────────────────────────────────────────────
        private HandPose _currentPose = HandPose.Open;
        private GameObject _handPivot = null;
        private GameObject _handOffset = null;
        private GameObject[] _poseMeshGOs = new GameObject[PoseCount];
        private bool meshVisible = false;

        // ── Proximity / interaction ───────────────────────────────────────────────
        private const float ProximityRadius = 0.15f;
        private const int DefaultGrabLayerMask = 1 << 12;
        private const int DualHoldGrabLayerMask = (1 << 0) | (1 << 1) | (1 << 12) | (1 << 19);
        private GameObject nearestObject = null;
        private Renderer highlightedRenderer = null;
        private Material highlightedMaterial = null;
        private Color originalColor;
    #if MVRC_DEBUG
        private VRProximitySphereDebug _debugSphere = null;
        private const int DebugSphereScanIntervalFrames = 3;
        private int _debugSphereScanCountdown = 0;
    #endif
        private readonly Collider[] _pickupHitsBuffer = new Collider[32];
    #if MVRC_DEBUG
        private readonly Collider[] _debugHitsBuffer = new Collider[96];
    #endif

        private GameObject heldObject = null;
        private Rigidbody heldRigidbody = null;
        private const float VRThrowVelocityThreshold = 0.05f;
        private Vector3 _throwVelPrev = Vector3.zero;
        private Vector3 _throwVelCurr = Vector3.zero;
        private Vector3 _throwPosPrev = Vector3.zero;
        private bool _hasThrowPosPrev = false;

        private bool _gripPressedLastFrame = false;
        private bool _gripReleasedLastFrame = false;
        private bool _triggerPressedLastFrame = false;
        private bool _triggerReleasedLastFrame = false;

        private bool _stickClickWasPressed = false;
        private bool _aWasPressed = false;
        private bool _menuWasPressed = false;

        private bool _gripHeld = false;
        private GameObject _heldObjectAtFrameStart = null;
        private bool _bridgeHeldAtFrameStart = false;
        private bool _laserGrabIntentActive = false;
        private bool _trigHeld = false;
        private bool _fistHeld = false;
        private bool _fingerHeld = false;

         private const string HudObjectPath = "GUI/HUD";
        private static readonly Vector3 HudLeftHandLocalPosition = new Vector3(0.0826f, 0.01f, 0.383f);
        private static readonly Vector3 HudLeftHandLocalRotation = new Vector3(286f, 267f, 80f);
        private static readonly Vector3 HudLeftHandLocalScale = new Vector3(0.012f, 0.014f, 0.020f);
        private static readonly Vector3 HudFpsLocalPosition = new Vector3(-12.1f, 9.7f, 0f);
        private static readonly Vector3 HudTmpLocalPosition = new Vector3(-11.5f, 6f, 0f);
        private static readonly Vector3 HudDrtyLocalPosition = new Vector3(-11.5f, 5.625f, 0f);
        private static readonly Vector3 HudJailLocalPosition = new Vector3(-11.5f, 5.25f, 0f);
        private const float HudResolveRetrySeconds = 1f;
        private const float HudFrontFacingDotThreshold = 0.1f;
        private static Transform _hudTransform = null;
        private static float _nextHudResolveAttemptTime = 0f;
        private bool _hudPrepared = false;

        // ── Tool mode ─────────────────────────────────────────────────────────────
        private bool _xWasPressed = false;
        public static bool ToolModeActive = false;
        [System.NonSerialized] public GameObject _toolInHand = null;
        public static VRHand RightHandInstance = null;
        public static VRHand LeftHandInstance = null;
        public GameObject HeldObject { get { return heldObject; } }
        public static GameObject RightHeldObject
        {
            get
            {
                if (RightHandInstance != null)
                    return RightHandInstance.HeldObject;
                return VRFsmActionBridge.HeldObject_R;
            }
        }
        public static GameObject LeftHeldObject
        {
            get
            {
                if (LeftHandInstance != null)
                    return LeftHandInstance.HeldObject;
                return VRFsmActionBridge.HeldObject_L;
            }
        }
        [System.NonSerialized] public bool triggerHeld = false;
        private PlayMakerFSM curActive;
        private bool _rightClickMode = false;

        // ── B button ──────────────────────────────────────────────────────────────
        public static bool SharedRightBHeld = false;
        private bool _bWasPressed = false;

        // ── Knob state ────────────────────────────────────────────────────────────
        // Only used when VRFsmPatcher.PatcherActive is false.
        private PlayMakerFSM activeKnobFsm = null;
        private Quaternion prevControllerRot = Quaternion.identity;
        private float rollAccumulator = 0f;
        private const float KnobDegreesPerTick = 5f;
        private const float BoltDegreesPerTick = 5f;

        // ── Bolt tightening ───────────────────────────────────────────────────────
        private bool _boltTightenMode = false;
        private float _boltRollAccumulator = 0f;
        private Quaternion _prevBoltControllerRot = Quaternion.identity;
        private int _boltTightenLogTimer = 0;

        // ── Bridge roll delta (for VRKnobTwistAction via VRFsmActionBridge) ──────
        private Quaternion _prevBridgeRot = Quaternion.identity;

        // ── Pending tool attach ───────────────────────────────────────────────────
        private int _pendingToolAttachFrames = 0;
        private int _pendingGripDropFrames = 0;
        private const int GripDropFallbackFrames = 2;

        // ── Assembly ──────────────────────────────────────────────────────────────
        private bool _assembleReady = false;
        private GameObject _assemblyIndicator = null;
        private static readonly Vector3 AssemblyIndicatorLocalOffset = new Vector3(0f, 0.05f, 0.08f);
        private bool _lastAssemblyIndicatorVisible = false;
        private int _assemblyIndicatorLogCooldown = 0;
        private PlayMakerFSM _cachedInstallerFsm = null;
        private GameObject _cachedVinp = null;

        private GameObject _fistColl = null;
        private Transform _fistCollOriginalParent = null;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        void Start()
        {
            EnsureInitialized();
            if (_handPivot != null && !meshVisible)
                _handPivot.SetActive(false);
        }

        private bool EnsureInitialized()
        {
            if (_handPivot == null)
            {
                _handPivot = BuildAllPoseMeshes();
            }

            if (GetComponent<VRLaserPointer>() == null)
            {
                VRLaserPointer laser = gameObject.AddComponent<VRLaserPointer>();
                laser.Hand = hand;
                laser.LaserEnabled = true;
                if (hand == HandSide.Right)
                    VRLaserPointer.RightInstance = laser;
                else
                    VRLaserPointer.LeftInstance = laser;
            }

            if (_assemblyIndicator == null)
            {
                _assemblyIndicator = BuildAssemblyIndicator();
            }

#if MVRC_DEBUG
            if (_debugSphere == null)
            {
                _debugSphere = gameObject.GetComponent<VRProximitySphereDebug>();
                if (_debugSphere == null)
                    _debugSphere = gameObject.AddComponent<VRProximitySphereDebug>();
            }
#endif

            if (hand == HandSide.Right)
            {
                RightHandInstance = this;
                VRProximitySphereDebug.RightHandTransform = this.transform;
            }
            else
            {
                LeftHandInstance = this;
            }

            return _handPivot != null;
        }

        public Transform GetWristAttachmentRoot()
        {
            return _handPivot != null ? _handPivot.transform : null;
        }

        // ── Haptics ───────────────────────────────────────────────────────────────
        private void Pulse(ushort durationMicroseconds = 1500)
        {
            uint deviceIndex = (hand == HandSide.Left)
                ? LookManager.LeftControllerIndex
                : LookManager.RightControllerIndex;
            if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid) return;
            OpenVR.System.TriggerHapticPulse(deviceIndex, 0, durationMicroseconds);
        }

        private uint GetControllerIndex()
        {
            return hand == HandSide.Left
                ? LookManager.LeftControllerIndex
                : LookManager.RightControllerIndex;
        }

        private void ResetThrowVelocityTracking()
        {
            _throwVelPrev = Vector3.zero;
            _throwVelCurr = Vector3.zero;
            _throwPosPrev = Vector3.zero;
            _hasThrowPosPrev = false;
        }

        private void UpdateThrowVelocitySample(bool useDebugEmulation, DebugHandFrameState debugFrame, bool gripHeld)
        {
            if (!gripHeld)
                return;

            _throwVelPrev = _throwVelCurr;

            Vector3 currentPosition = useDebugEmulation
                ? debugFrame.Position
                : transform.position;

            float deltaTime = Time.deltaTime;
            if (_hasThrowPosPrev && deltaTime > 0f)
                _throwVelCurr = (currentPosition - _throwPosPrev) / deltaTime;
            else
                _throwVelCurr = Vector3.zero;

            _throwPosPrev = currentPosition;
            _hasThrowPosPrev = true;
        }

        private GameObject GetBridgeHeldObjectForHand()
        {
            return hand == HandSide.Right
                ? VRFsmActionBridge.HeldObject_R
                : VRFsmActionBridge.HeldObject_L;
        }

        private void HandleGripRelease(bool isRightHand)
        {
            GameObject bridgeHeld = GetBridgeHeldObjectForHand();
            GameObject heldForRelease = heldObject != null ? heldObject : _heldObjectAtFrameStart;
            if (heldForRelease == null)
                heldForRelease = bridgeHeld;

            bool hadHeldObjectOnRelease = heldForRelease != null
                || bridgeHeld != null
                || _bridgeHeldAtFrameStart;

            if (hadHeldObjectOnRelease && heldForRelease != null)
            {
                float magnitude = _throwVelCurr.magnitude;
                if (magnitude >= VRThrowVelocityThreshold)
                {
                    VRFsmActionBridge.ThrowVelocity = _throwVelCurr;
                    PlayMakerHelpers.DebugLog("[VR-THROW] Throw: vel="
                        + magnitude.ToString("F2")
                        + " dir=" + _throwVelCurr.normalized.ToString("F2"));
                    VRPlayerGestures.SendEvent("PROCEED Throw");
                }
                else
                {
                    VRFsmActionBridge.ThrowVelocity = Vector3.zero;
                    PlayMakerHelpers.DebugLog("[VR-THROW] Drop: vel=" + magnitude.ToString("F2"));
                    VRPlayerGestures.SendEvent("PROCEED Drop");
                }
            }
            else
            {
                VRFsmActionBridge.ThrowVelocity = Vector3.zero;
            }

            if (isRightHand)
                _laserGrabIntentActive = false;

            VRFsmActionBridge.TryFireEquipFinishedForDrop();
            if (heldObject != null)
                _pendingGripDropFrames = GripDropFallbackFrames;
        }

        // ── Pose mesh construction ────────────────────────────────────────────────

        private GameObject BuildAllPoseMeshes()
        {
            HandPoseConfig openCfg = PoseConfigs[(int)HandPose.Open];

            GameObject offset = new GameObject("MVRC_" + hand + "HandOffset");
            offset.transform.localPosition = Vector3.zero;
            offset.transform.localRotation = Quaternion.identity;
            _handOffset = offset;

            GameObject pivot = new GameObject("MVRC_" + hand + "HandPivot");
            pivot.transform.SetParent(offset.transform, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one * HandMeshScale;

            _poseMeshGOs = new GameObject[PoseCount];
            for (int i = 0; i < PoseCount; i++)
            {
                _poseMeshGOs[i] = BuildSinglePose((HandPose)i, PoseConfigs[i], pivot, openCfg);
                _poseMeshGOs[i].SetActive(i == (int)HandPose.Open);
            }
            return pivot;
        }

        private GameObject BuildSinglePose(HandPose pose, HandPoseConfig cfg,
                                            GameObject parent, HandPoseConfig openCfg)
        {
            string poseName = pose.ToString();
            string meshName = "MVRC_" + hand + "_" + poseName;

            GameObject container = new GameObject(meshName + "_Container");
            container.transform.SetParent(parent.transform, false);
            container.transform.localPosition = cfg.pivotOffset - openCfg.pivotOffset;
            container.transform.localRotation = Quaternion.Euler(PoseConfigs[(int)_currentPose].contRotEuler);
            container.transform.localScale = Vector3.one;

            string path = (hand == HandSide.Right) ? cfg.pathRight : cfg.pathLeft;
            if (string.IsNullOrEmpty(path) || path.Contains("TODO"))
            {
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Pose '" + poseName + "': path not set; skipping bake");
                return container;
            }

            GameObject sourceGO = GameObject.Find(path);
            if (sourceGO == null)
            {
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Pose '" + poseName + "': source not found at '" + path + "'");
                return container;
            }

            SkinnedMeshRenderer smr = sourceGO.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
            {
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Pose '" + poseName + "': no SkinnedMeshRenderer at path");
                return container;
            }

            // Disable the source skinned renderer so only the baked VR hand mesh is visible.
            try { smr.enabled = false; } catch { }

            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);

            if (hand == HandSide.Left || cfg.reverseWinding)
            {
                int[] tris = bakedMesh.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                { int tmp = tris[i]; tris[i] = tris[i + 2]; tris[i + 2] = tmp; }
                bakedMesh.triangles = tris;
                bakedMesh.RecalculateNormals();
            }

            GameObject meshGO = new GameObject(meshName);
            meshGO.transform.SetParent(container.transform, false);
            meshGO.transform.localPosition = -cfg.wristLocal;
            meshGO.transform.localRotation = Quaternion.identity;
            meshGO.transform.localScale = Vector3.one;

            meshGO.AddComponent<MeshFilter>().mesh = bakedMesh;
            MeshRenderer mr = meshGO.AddComponent<MeshRenderer>();
            if (smr.materials.Length > 0) mr.materials = smr.materials;

            PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Pose '" + poseName + "' baked. wristLocal=" + cfg.wristLocal);

            return container;
        }

        // ── Pose switching ────────────────────────────────────────────────────────

        private void SetPose(HandPose newPose)
        {
            if (newPose == _currentPose) return;
            if (_poseMeshGOs[(int)_currentPose] != null)
                _poseMeshGOs[(int)_currentPose].SetActive(false);
            _currentPose = newPose;
            if (_poseMeshGOs[(int)_currentPose] != null)
                _poseMeshGOs[(int)_currentPose].SetActive(true);
            if (hand == HandSide.Right)
                SetFistCollActive(newPose == HandPose.Fist);

            // Keep pose-dependent pivot/offset basis in sync on the same frame as pose switch.
            ApplyCurrentPoseVisualBasis();

            PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Hand pose → " + newPose);
        }
        private void SetFistCollActive(bool attach)
        {
            if (_fistColl == null)
            {
                GameObject fist = GameObject.Find(
                    "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Fist/Pivot/FistColl");
                if (fist == null) return;
                _fistColl = fist;
                _fistCollOriginalParent = fist.transform.parent;
            }
            if (attach)
            {
                _fistColl.transform.SetParent(this.transform, false);
                _fistColl.transform.localPosition = Vector3.zero;
                _fistColl.transform.localRotation = Quaternion.identity;
            }
            else
            {
                _fistColl.transform.SetParent(_fistCollOriginalParent, false);
            }
            if (_fistColl.GetComponent<VRFistCollision>() == null)
                _fistColl.AddComponent<VRFistCollision>();
        }

        private void UpdateAutoPose(bool gripPressed, float joyY)
        {
            if (hand == HandSide.Right)
            {
                if (joyY > JoyThreshold)
                {
                    _joyPoseActive = true;
                    SetPose(HandPose.Fist);
                    return;
                }
                if (joyY < -JoyThreshold)
                {
                    _joyPoseActive = true;
                    SetPose(HandPose.MiddleFinger);
                    return;
                }
                if (_joyPoseActive && Mathf.Abs(joyY) < JoyDeadzone)
                    _joyPoseActive = false;
                if (_joyPoseActive) return;
            }
            SetPose(HandPose.Open);
        }

        // ── Misc builders ─────────────────────────────────────────────────────────

        private GameObject BuildAssemblyIndicator()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "MVRC_AssembleIndicator_" + hand;
            Collider col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);

            int vrHandsLayer = LayerMask.NameToLayer("Character");
            if (vrHandsLayer >= 0)
                sphere.layer = vrHandsLayer;
            else
                sphere.layer = 20;
            sphere.transform.localScale = Vector3.one * 0.045f;

            Renderer r = sphere.GetComponent<Renderer>();
            if (r != null)
            {
                Material mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = new Color(0.2f, 1f, 0.2f, 0.92f);
                r.material = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            AttachAssemblyIndicatorToHandAnchor(sphere.transform);
            sphere.SetActive(false);
            return sphere;
        }

        private void AttachAssemblyIndicatorToHandAnchor(Transform indicatorTransform)
        {
            if (indicatorTransform == null) return;

            Transform anchor = _handOffset != null ? _handOffset.transform : transform;
            if (anchor == null) return;

            if (indicatorTransform.parent != anchor)
                indicatorTransform.SetParent(anchor, false);

            indicatorTransform.localPosition = AssemblyIndicatorLocalOffset;
            indicatorTransform.localRotation = Quaternion.identity;
        }

        // ── Input ─────────────────────────────────────────────────────────────────

        private void CheckInput()
        {
            bool useDebugEmulation = DebugVrControllerEmulation.IsActive;
            if (!useDebugEmulation && !VRInputActions.Initialized) return;

            DebugHandFrameState debugFrame = default(DebugHandFrameState);
            if (useDebugEmulation)
                debugFrame = DebugVrControllerEmulation.GetFrame(hand);

            if (hand == HandSide.Right)
            {
                if (_gripPressedLastFrame) { VRFsmActionBridge.GripJustPressed_R = false; _gripPressedLastFrame = false; }
                if (_gripReleasedLastFrame) { VRFsmActionBridge.GripJustReleased_R = false; _gripReleasedLastFrame = false; }
                if (_triggerPressedLastFrame) { VRFsmActionBridge.TriggerJustPressed_R = false; _triggerPressedLastFrame = false; }
                if (_triggerReleasedLastFrame) { VRFsmActionBridge.TriggerJustReleased_R = false; _triggerReleasedLastFrame = false; }
            }
            else
            {
                if (_gripPressedLastFrame) { VRFsmActionBridge.GripJustPressed_L = false; _gripPressedLastFrame = false; }
                if (_gripReleasedLastFrame) { VRFsmActionBridge.GripJustReleased_L = false; _gripReleasedLastFrame = false; }
                if (_triggerPressedLastFrame) { VRFsmActionBridge.TriggerJustPressed_L = false; _triggerPressedLastFrame = false; }
                if (_triggerReleasedLastFrame) { VRFsmActionBridge.TriggerJustReleased_L = false; _triggerReleasedLastFrame = false; }
            }

            // UpdateActionState is idempotent per frame in VRInputActions.
            // Call from both hands so left-hand interactions never depend on update order.
            if (!useDebugEmulation)
                VRInputActions.UpdateState();

            bool bRawPressed = useDebugEmulation
                ? debugFrame.ButtonBHeld
                : VRInputActions.GetBButtonHeld();   // ← not GetDigital(BHeld) directly

            // Only the right hand drives the toggle, so it fires exactly once per press.
            if (hand == HandSide.Right)
            {
                bool bEdgeDown = bRawPressed && !_bWasPressed;
                if (bEdgeDown)
                {
                    bool newMode = !VRInputActions.BModeActive;
                    PlayMakerHelpers.DebugLog("[VRInput] B-mode " + (newMode ? "ON" : "OFF"));
                    VRInputActions.BModeActive = newMode;
                    VRInputActions.UpdateState(true);
                }
                _bWasPressed = bRawPressed;
            }

            // Alias. Read the possibly updated state once for use in all guards below.
            bool bModeActive = VRInputActions.BModeActive;

            // ── Grip ─────────────────────────────────────────────────────────────────
            bool gripState = useDebugEmulation
                ? debugFrame.GripHeld
                : VRInputActions.GetDigital(hand == HandSide.Right ? VRInputActions.GrabRight : VRInputActions.GrabLeft);

            bool gripEdgeDown = useDebugEmulation
                ? debugFrame.GripDown
                : (gripState && !_gripHeld);
            bool gripEdgeUp = useDebugEmulation
                ? debugFrame.GripUp
                : (!gripState && _gripHeld);
            _gripHeld = gripState;

            if (hand == HandSide.Right)
            {
                VRFsmActionBridge.GripIsHeld_R = gripState;
                VRFsmActionBridge.GripJustPressed_R = gripEdgeDown;
                VRFsmActionBridge.GripJustReleased_R = gripEdgeUp;
                if (gripEdgeDown)
                    ResetThrowVelocityTracking();
                UpdateThrowVelocitySample(useDebugEmulation, debugFrame, gripState);
                if (gripEdgeUp)
                    HandleGripRelease(true);
            }
            else
            {
                VRFsmActionBridge.GripIsHeld_L = gripState;
                VRFsmActionBridge.GripJustPressed_L = gripEdgeDown;
                VRFsmActionBridge.GripJustReleased_L = gripEdgeUp;
                if (gripEdgeDown)
                    ResetThrowVelocityTracking();
                UpdateThrowVelocitySample(useDebugEmulation, debugFrame, gripState);
                if (gripEdgeUp)
                    HandleGripRelease(false);
            }
            // ── Trigger ───────────────────────────────────────────────────────────────
            bool trigState = useDebugEmulation
                ? debugFrame.TriggerHeld
                : VRInputActions.GetDigital(hand == HandSide.Right ? VRInputActions.InteractRight : VRInputActions.InteractLeft);

            bool trigEdgeDown = useDebugEmulation
                ? debugFrame.TriggerDown
                : (trigState && !_trigHeld);
            bool trigEdgeUp = useDebugEmulation
                ? debugFrame.TriggerUp
                : (!trigState && _trigHeld);
            _trigHeld = trigState;
            triggerHeld = trigState;
            bool thisHandBridgeHeld = hand == HandSide.Right
                ? VRFsmActionBridge.HeldObject_R != null
                : VRFsmActionBridge.HeldObject_L != null;
            bool otherHandBridgeHeld = hand == HandSide.Right
                ? VRFsmActionBridge.HeldObject_L != null
                : VRFsmActionBridge.HeldObject_R != null;

            bool grabbedOnThisGripPress = false;
            if (!bModeActive && gripEdgeDown && heldObject == null && !thisHandBridgeHeld)
            {
                bool grabbed = false;

                // The base game PickUp FSM is shared. When one hand already holds an
                // item, allow the free hand to directly grab nearby rigidbodies so
                // both hands can carry independently.
                if (otherHandBridgeHeld)
                {
                    grabbed = TryGrabNearby(DualHoldGrabLayerMask);
                    if (grabbed)
                        VRPickupTriggerAction.SuppressFrames = 20;
                }

                if (!grabbed)
                    grabbed = TryGrabNearby(DefaultGrabLayerMask);

                grabbedOnThisGripPress = grabbed;
            }

            if (hand == HandSide.Right)
            {
                if (gripEdgeDown)
                    _laserGrabIntentActive = !grabbedOnThisGripPress;

                if (!gripState || heldObject != null || VRFsmActionBridge.HeldObject_R != null)
                    _laserGrabIntentActive = false;
            }

            bool rightClickState = useDebugEmulation
                ? debugFrame.RightClickHeld
                : VRInputActions.GetDigital(hand == HandSide.Right ? VRInputActions.BRightClickRight : VRInputActions.BRightClickLeft);
            bool rightClickEdgeDown = useDebugEmulation
                ? debugFrame.RightClickDown
                : VRInputActions.GetDigitalJustChanged(hand == HandSide.Right ? VRInputActions.BRightClickRight : VRInputActions.BRightClickLeft, out rightClickState) && rightClickState;

            bool laserRightState = useDebugEmulation
                ? Input.GetKey(KeyCode.L) && hand == HandSide.Right
                : VRInputActions.GetDigital(VRInputActions.BLaserRight);   // ← reads b_mode set action
            bool laserLeftState = useDebugEmulation
                ? Input.GetKey(KeyCode.K)
                : VRInputActions.GetDigital(VRInputActions.BLaserLeft);

            VRFsmActionBridge.LaserRightIsActive_R = laserRightState;
            VRFsmActionBridge.LaserLeftIsActive_L = laserLeftState;

            if (hand == HandSide.Right)
            {
                VRFsmActionBridge.TriggerIsHeld_R = trigState;
                VRFsmActionBridge.TriggerJustPressed_R = trigEdgeDown;
                VRFsmActionBridge.TriggerJustReleased_R = trigEdgeUp;
            }
            else
            {
                VRFsmActionBridge.TriggerIsHeld_L = trigState;
                VRFsmActionBridge.TriggerJustPressed_L = trigEdgeDown;
                VRFsmActionBridge.TriggerJustReleased_L = trigEdgeUp;
            }

            // ── Fist (breakable trigger path) ──────────────────────────────────────
            bool fistState = useDebugEmulation
                ? debugFrame.FistHeld
                : (hand == HandSide.Right && VRInputActions.GetDigital(VRInputActions.Fist));

            bool fistEdgeDown = fistState && !_fistHeld;
            _fistHeld = fistState;

            if (hand == HandSide.Right)
            {
                VRFsmActionBridge.FistIsHeld_R = fistState;
                VRFsmActionBridge.FistJustPressed_R = fistEdgeDown;
            }
            else
            {
                VRFsmActionBridge.FistIsHeld_L = fistState;
                VRFsmActionBridge.FistJustPressed_L = fistEdgeDown;
            }

            bool fingerState = useDebugEmulation
                ? debugFrame.FingerHeld
                : (hand == HandSide.Right && VRInputActions.GetDigital(VRInputActions.Finger));

            bool fingerEdgeDown = fingerState && !_fingerHeld;
            _fingerHeld = fingerState;

            VRMenuManager.SyncFromGame();
            bool menuState = useDebugEmulation
                ? debugFrame.MenuHeld
                : VRInputActions.GetDigital(VRInputActions.BMenu);
            bool menuEdgeDown = menuState && !_menuWasPressed;
            if (hand == HandSide.Right && menuEdgeDown)
                VRMenuManager.ToggleMenu();
            _menuWasPressed = menuState;

            if (trigEdgeDown)
            {
                bool allowTriggerAction = !bModeActive || rightClickEdgeDown;
                if (allowTriggerAction)
                    OnTriggerDown(rightClickEdgeDown, true);
            }
            if (trigEdgeUp) OnTriggerUp(true);

            // ── Right-click (B + trigger) ───────────────────────────────────────────
            if (hand == HandSide.Right)
            {
                SharedRightBHeld = bModeActive;
                VRFsmActionBridge.RightClickJustPressed_R = rightClickEdgeDown;
            }
            else
            {
                SharedRightBHeld = bModeActive;
                VRFsmActionBridge.RightClickJustPressed_L = rightClickEdgeDown;
            }

            // ── Right hand only buttons ───────────────────────────────────────────────
            if (hand == HandSide.Right)
            {
                bool rightClickChanged = false;
                bool ratchetState = false;
                if (useDebugEmulation)
                {
                    ratchetState = debugFrame.RightClickDown;
                }
                else
                {
                    rightClickChanged = VRInputActions.GetDigitalJustChanged(VRInputActions.BRatchetToggle, out rightClickState);
                    ratchetState = rightClickChanged && rightClickState;
                }
                if (ratchetState)
                {
                    if (ToolModeActive)
                        VRInteraction.ToggleRatchetDirection();
                    else
                        PlayMakerHelpers.DebugLog("[VRHand] Ratchet toggle IGNORED because ToolModeActive is false.");
                }



                // --- A button (trigger on PRESS) ---
                bool aState = useDebugEmulation ? debugFrame.JumpHeld : VRInputActions.GetDigital(VRInputActions.Jump);

                // Detect press (false → true)
                if (aState && !_aWasPressed && !bModeActive)
                {
                    VRInteraction.TriggerJump();
                }

                _aWasPressed = aState;


                // --- Stick click (trigger on PRESS) ---
                bool stickState = useDebugEmulation ? debugFrame.CrouchHeld : VRInputActions.GetDigital(VRInputActions.Crouch);

                if (stickState && !_stickClickWasPressed && !bModeActive)
                {
                    VRInteraction.TriggerCrouch();
                }
                _stickClickWasPressed = stickState;
            }

            // ── Left hand only buttons ────────────────────────────────────────────────
            if (hand == HandSide.Left)
            {
                bool xState = useDebugEmulation ? debugFrame.ToolModeHeld : VRInputActions.GetDigital(VRInputActions.ToolMode);
                if (xState && !_xWasPressed && !bModeActive)
                {
                    VRInteraction.ToggleToolMode();
                }

                _xWasPressed = xState;
            }

            // ── Joystick (pose / auto-pose) ───────────────────────────────────────────
            if (hand == HandSide.Right && _toolInHand == null && !bModeActive)
            {
                if (fistState)
                {
                    SetPose(HandPose.Fist);
                    VRPlayerGestures.Fist();
                }
                else if (fingerState)
                {
                    SetPose(HandPose.MiddleFinger);
                    if (fingerEdgeDown)
                        VRPlayerGestures.Finger();
                }
                else
                    SetPose(HandPose.Open);
            }

            ProcessPendingGripDropFallback();
        }

        private void ProcessPendingGripDropFallback()
        {
            if (_pendingGripDropFrames <= 0)
                return;

            if (_gripHeld)
            {
                _pendingGripDropFrames = 0;
                return;
            }

            if (heldObject == null)
            {
                _pendingGripDropFrames = 0;
                return;
            }

            if (!heldObject.transform.IsChildOf(transform))
            {
                _pendingGripDropFrames = 0;
                return;
            }

            _pendingGripDropFrames--;
            if (_pendingGripDropFrames > 0)
                return;

            Drop();
        }

        private void OnTriggerDown(bool bHeld, bool triggerEdgeDown)
        {

            _rightClickMode = bHeld;
            if (hand == HandSide.Right)
            {
                VRFsmActionBridge.TriggerJustPressed_R = triggerEdgeDown;
            }
            else
            {
                VRFsmActionBridge.TriggerJustPressed_L = triggerEdgeDown;
                // left hand B+T also fires right click because RightBHeld is shared
            }
            if (hand == HandSide.Right && ToolModeActive && !_rightClickMode)
            {
                if (TryStartBoltTighten()) return;
            }
            curActive = hand == HandSide.Right
                ? VRFsmActionBridge.ActiveInteractionTarget
                : VRFsmActionBridge.ActiveInteractionTargetLeft;
            PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Trigger down: near="
                + (curActive != null ? curActive.gameObject.name : "none")
                + " held=" + (heldObject != null ? heldObject.name : "none")
                + " assembleReady=" + _assembleReady
                + " rightClick=" + _rightClickMode
                + " toolMode=" + ToolModeActive);

            Pulse();

            // Held-object interactions are source-agnostic: VR and emulation share this path.
            if (heldObject != null)
            {
                if (_assembleReady && !_rightClickMode)
                {
                    GameObject partToInstall = heldObject;
                    partToInstall.transform.SetParent(null, true);

                    if (heldRigidbody != null)
                        heldRigidbody.isKinematic = false;

                    heldObject = null;
                    heldRigidbody = null;
                    ClearBridgeHeldObject(partToInstall);

                    _assembleReady = false;
                    VRInteraction.TryAssemble(partToInstall, _cachedInstallerFsm, _cachedVinp);
                    _cachedInstallerFsm = null;
                    _cachedVinp = null;
                }
                else if (!_rightClickMode)
                {
                    VRInteraction.TryUseHeld(heldObject);
                    SyncHeldOwnershipIfInvalid();
                }
                return;
            }

            //if (nearestObject == null)
            //{
            //    if (hand == HandSide.Right && ToolModeActive && !_rightClickMode)
            //        TryStartBoltTighten();
            //    return;
            //}

            if (_rightClickMode && nearestObject != null && !VRFsmPatcher.PatcherActive)
            {
                if (VRInteraction.TryDisassemble(nearestObject)) return;
            }



            // AdvanceWaitPlayerState is handled by VRProximityActivateAction when the
            // patcher is active; skip the old direct call to avoid double-firing.
            //if (!VRFsmPatcher.PatcherActive)
            //    VRInteraction.AdvanceWaitPlayerState(nearestObject);

            // Old knob interaction, bypassed when the patcher is active.
            // The new VRKnobTwistAction handles knobs via an injected FSM action.
            //if (!VRFsmPatcher.PatcherActive && !_rightClickMode)
            //{
            //    PlayMakerFSM knobFsm = VRInteraction.FindKnobFsm(nearestObject);
            //    if (knobFsm != null)
            //    {
            //        activeKnobFsm = knobFsm;
            //        rollAccumulator = 0f;
            //        prevControllerRot = transform.rotation;
            //        VRInteraction.StartKnobInteraction(activeKnobFsm);
            //        return;
            //    }
            //}

            // Old mouse-action injection, bypassed when the patcher is active.
            // AutoMap rules in VRFsmPatcher handle these objects via injected actions.
            //if (!VRFsmPatcher.PatcherActive)
            //{
            //    if (!VRInteraction.HasDispatcherState(nearestObject))
            //    {
            //        var mouseEv = VRInteraction.FindMouseActionEvents(nearestObject, _rightClickMode);
            //        if (mouseEv.HasValue)
            //        {
            //            _mouseActionEvents = mouseEv;
            //            VRInteraction.SendMouseActionDown(mouseEv.Value);
            //            return;
            //        }
            //    }
            //}

            // Generic interact fallback, bypassed when the patcher is active.
            //if (!VRFsmPatcher.PatcherActive)
            //    VRInteraction.TryInteract(nearestObject);
        }

        private bool TryStartBoltTighten()
        {
            if (_boltTightenMode) return true; // already active; do not reset the accumulator

            if (!VRInteraction.HasNearestBolt())
            {
                PlayMakerHelpers.DebugLog("[BOLT-5] TryStartBoltTighten: no bolt in range");
                return false;
            }
            PlayMakerFSM checkFsm = VRInteraction.GetBoltCheckFsm();
            string checkState = checkFsm != null ? (checkFsm.ActiveStateName ?? "null") : "FSM_NULL";

            PlayMakerHelpers.DebugLog("[BOLT-5] TryStartBoltTighten: Check FSM state='" + checkState + "'");

            _boltTightenMode = true;
            _boltRollAccumulator = 0f;
            _prevBoltControllerRot = transform.rotation;

            PlayMakerHelpers.DebugLog("[BOLT-5] Bolt tighten mode STARTED");

            return true;
        }

        private void UpdateBoltTighten()
        {
            if (!_boltTightenMode) return;
            // End tighten mode if bolt is no longer in range
            if (!VRInteraction.HasNearestBolt())
            {
                _boltTightenMode = false;
                _boltRollAccumulator = 0f;
                _boltTightenLogTimer = 0;
                PlayMakerHelpers.DebugLog("[BOLT-5] Bolt tighten mode ENDED: bolt out of range");
                return;
            }
            Vector3 forward = transform.forward;
            Vector3 prevProj = Vector3.ProjectOnPlane(_prevBoltControllerRot * Vector3.up, forward);
            Vector3 currProj = Vector3.ProjectOnPlane(transform.rotation * Vector3.up, forward);

            float roll = 0f;
            if (prevProj.sqrMagnitude > 0.001f && currProj.sqrMagnitude > 0.001f)
            {
                roll = Vector3.Angle(prevProj, currProj);
                if (Vector3.Dot(Vector3.Cross(prevProj, currProj), forward) < 0f) roll = -roll;
            }

            _prevBoltControllerRot = transform.rotation;
            _boltRollAccumulator += roll;

            _boltTightenLogTimer++;
            if (_boltTightenLogTimer >= 30)
            {
                _boltTightenLogTimer = 0;
                PlayMakerHelpers.DebugLog("[BOLT-5] Accumulator=" + _boltRollAccumulator.ToString("F2")
                    + " roll=" + roll.ToString("F3"));
            }

            if (_boltRollAccumulator > BoltDegreesPerTick)
            {
                _boltRollAccumulator -= BoltDegreesPerTick;
                VRInteraction.SendBoltTighten(false);
            }
            else if (_boltRollAccumulator < -BoltDegreesPerTick)
            {
                _boltRollAccumulator += BoltDegreesPerTick;
                VRInteraction.SendBoltTighten(true);
            }
        }

        private void OnTriggerUp(bool triggerEdgeUp)
        {
            if (hand == HandSide.Right)
                VRFsmActionBridge.TriggerJustReleased_R = triggerEdgeUp;
            else
                VRFsmActionBridge.TriggerJustReleased_L = triggerEdgeUp;
            if (_boltTightenMode)
            {
                _boltTightenMode = false;
                _boltRollAccumulator = 0f;
                _boltTightenLogTimer = 0;
                PlayMakerHelpers.DebugLog("[BOLT-5] Bolt tighten mode ENDED");
            }

            //// Old knob cleanup, bypassed when the patcher is active.
            //// VRKnobTwistAction manages its own state on trigger release.
            //if (!VRFsmPatcher.PatcherActive && activeKnobFsm != null)
            //{
            //    VRInteraction.EndKnobInteraction(activeKnobFsm);
            //    activeKnobFsm = null;
            //    rollAccumulator = 0f;
            //}

            //// Old mouse-action-up, bypassed when the patcher is active.
            //// The injected VRTriggerHoldAction fires its release event directly.
            //if (!VRFsmPatcher.PatcherActive && _mouseActionEvents.HasValue)
            //{
            //    VRInteraction.SendMouseActionUp(_mouseActionEvents.Value);
            //    _mouseActionEvents = null;
            //}

            _rightClickMode = false;
        }

        private bool TryGrab(GameObject target)
        {
            return TryGrab(target, null);
        }

        private bool TryGrab(GameObject target, Collider sourceCollider)
        {
            if (target == null) return false;

            bool sourceIsTool = sourceCollider != null && VRInteraction.IsPickableTool(sourceCollider);
            if (!sourceIsTool)
            {
                GameObject targetRoot = VRFsmActionBridge.ResolveInteractionTargetRoot(target);
                if (targetRoot != null)
                    target = targetRoot;
            }

            if (heldObject != null) return false;
            if (target == VRFsmActionBridge.HeldObject_R || target == VRFsmActionBridge.HeldObject_L)
                return false;

            if (target == _fistColl) return false;
            Collider col = target.GetComponent<Collider>();
            if (sourceIsTool || (col != null && VRInteraction.IsPickableTool(col)))
            {
                VRPickupTriggerAction.SuppressFrames = 30;
                VRInteraction.TryPickUpTool(target);
                if (!ToolModeActive) VRInteraction.ToggleToolMode(true);
                _pendingToolAttachFrames = 2;
                return true;
            }

            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (rb == null) return false;
            if (target.GetComponent<HingeJoint>() != null) return false;
            if (target.GetComponent<FixedJoint>() != null) return false;
            if (target.GetComponent<SpringJoint>() != null) return false;
            ConfigurableJoint cj = target.GetComponent<ConfigurableJoint>();
            if (cj != null && cj.connectedBody != null) return false;

            VRPickupTriggerAction.SuppressFrames = 30;

            heldObject = target;
            heldRigidbody = rb;
            rb.isKinematic = true;
            target.transform.SetParent(this.transform, true);

            if (hand == HandSide.Right)
                VRFsmActionBridge.HeldObject_R = target;
            else
                VRFsmActionBridge.HeldObject_L = target;

            RemoveHighlight();

            int wheelLayer = LayerMask.NameToLayer("Wheel");
            if (wheelLayer >= 0) target.layer = wheelLayer;

            VRInteraction.NotifyHandFsmGrab(target);
            VRInteraction.ActivateAssembly(target, out _cachedInstallerFsm, out _cachedVinp);
            return true;
        }

        private bool TryGrabNearby(int layerMask)
        {
            int hitCount = PhysicsCompat.OverlapSphereNonAllocIgnoreTriggers(
                transform.position,
                ProximityRadius,
                _pickupHitsBuffer,
                layerMask);

            GameObject bestCandidate = null;
            Collider bestSourceCollider = null;
            float bestScore = float.MaxValue;
            float bestSurfaceDist = float.MaxValue;
            System.Collections.Generic.HashSet<int> seenCandidates
                = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < hitCount; i++)
            {
                Collider c = _pickupHitsBuffer[i];
                if (c == null) continue;

                GameObject rawGo = c.gameObject;
                if (rawGo == null)
                    continue;

                bool toolHit = VRInteraction.IsPickableTool(c);
                GameObject scoreTarget = toolHit
                    ? rawGo
                    : VRFsmActionBridge.ResolveInteractionTargetRoot(rawGo);
                if (scoreTarget == null)
                    continue;

                if (toolHit && IsToolContainerCandidate(scoreTarget))
                    continue;

                if (!IsCandidateEligibleForGrab(scoreTarget, toolHit))
                    continue;

                if (!seenCandidates.Add(scoreTarget.GetInstanceID()))
                    continue;

                float surfaceDist = VRFsmActionBridge.GetClosestSurfaceDistanceToObject(scoreTarget, transform.position);
                if (surfaceDist > ProximityRadius)
                    continue;

                float score = VRFsmActionBridge.GetCenterPriorityScore(scoreTarget, transform.position);
                if (score < bestScore
                    || (Mathf.Abs(score - bestScore) <= 0.0001f && surfaceDist < bestSurfaceDist))
                {
                    bestScore = score;
                    bestSurfaceDist = surfaceDist;
                    bestCandidate = scoreTarget;
                    bestSourceCollider = c;
                }
            }

            if (bestCandidate == null)
                return false;

            PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Grab candidate='" + bestCandidate.name
                + "' score=" + bestScore.ToString("F3")
                + " surfaceDist=" + bestSurfaceDist.ToString("F3"));

            return TryGrab(bestCandidate, bestSourceCollider);
        }

        private bool IsCandidateEligibleForGrab(GameObject go, bool toolHit)
        {
            if (go == null) return false;

            GameObject candidate = toolHit ? go : VRFsmActionBridge.ResolveInteractionTargetRoot(go);
            if (candidate == null)
                candidate = go;

            if (candidate == _fistColl) return false;
            if (candidate == VRFsmActionBridge.HeldObject_R || candidate == VRFsmActionBridge.HeldObject_L)
                return false;
            if (candidate.transform.IsChildOf(transform)) return false;
            if (IsAttachedToOtherHand(candidate)) return false;

            if (toolHit)
            {
                Collider toolCollider = candidate.GetComponent<Collider>();
                return toolCollider != null && VRInteraction.IsPickableTool(toolCollider);
            }

            Collider col = candidate.GetComponent<Collider>();
            if (col != null && VRInteraction.IsPickableTool(col))
                return true;

            Rigidbody rb = candidate.GetComponent<Rigidbody>();
            if (rb == null) return false;
            if (candidate.GetComponent<HingeJoint>() != null) return false;
            if (candidate.GetComponent<FixedJoint>() != null) return false;
            if (candidate.GetComponent<SpringJoint>() != null) return false;

            ConfigurableJoint cj = candidate.GetComponent<ConfigurableJoint>();
            if (cj != null && cj.connectedBody != null) return false;

            return true;
        }

        private static bool IsToolContainerCandidate(GameObject go)
        {
            if (go == null) return false;

            Collider[] childColliders = go.GetComponentsInChildren<Collider>(true);
            if (childColliders == null || childColliders.Length == 0)
                return false;

            int pickableChildColliders = 0;
            for (int i = 0; i < childColliders.Length; i++)
            {
                Collider col = childColliders[i];
                if (col == null || col.gameObject == go)
                    continue;

                if (!VRInteraction.IsPickableTool(col))
                    continue;

                pickableChildColliders++;
                if (pickableChildColliders >= 2)
                    return true;
            }

            return false;
        }

        private bool IsAttachedToOtherHand(GameObject go)
        {
            if (go == null) return false;

            Transform otherHand = hand == HandSide.Right
                ? (LeftHandInstance != null ? LeftHandInstance.transform : null)
                : (RightHandInstance != null
                    ? RightHandInstance.transform
                    : VRProximitySphereDebug.RightHandTransform);

            return otherHand != null && go.transform.IsChildOf(otherHand);
        }

        private void Drop()
        {
            if (heldObject == null) return;
            _pendingGripDropFrames = 0;
            Pulse();
            // Send FINISHED to the PickUp FSM on the Hand to trigger unequip sequence
            VRFsmActionBridge.TryFireEquipFinishedForDrop();
            VRInteraction.NotifyHandFsmDrop(heldObject);
            VRInteraction.ResetHandPickupFsmStaleObject(heldObject);
            VRInteraction.DeactivateAssembly(_cachedInstallerFsm, _cachedVinp);
            _cachedInstallerFsm = null;
            _cachedVinp = null;

            int partsLayer = LayerMask.NameToLayer("Parts");
            if (partsLayer >= 0) heldObject.layer = partsLayer;
            heldObject.transform.SetParent(null, true);
            if (heldRigidbody != null) { heldRigidbody.isKinematic = false; heldRigidbody = null; }
            GameObject dropped = heldObject;
            PlayMakerHelpers.DebugLog("[VR-THROW-DIAG] _heldItem cleared (was "
                + (heldObject != null ? heldObject.name : "null") + ")");
            heldObject = null;
            ClearBridgeHeldObject(dropped);
            ResetThrowVelocityTracking();
            _assembleReady = false;
        }

        // ── Assembly proximity ────────────────────────────────────────────────────

        private static bool IsVinpIndicatorTarget(PlayMakerFSM targetFsm)
        {
            if (targetFsm == null) return false;

            GameObject targetGo = targetFsm.gameObject;
            string targetName = targetGo != null ? targetGo.name : string.Empty;
            return !string.IsNullOrEmpty(targetName)
                && targetName.IndexOf("VINP", global::System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private PlayMakerFSM GetActiveInteractionTargetForHand()
        {
            return hand == HandSide.Right
                ? VRFsmActionBridge.ActiveInteractionTarget
                : VRFsmActionBridge.ActiveInteractionTargetLeft;
        }

        private bool HasLikelyAttachedAssemblyPart()
        {
            Transform root = transform;
            if (root == null) return false;

            Rigidbody[] childBodies = root.GetComponentsInChildren<Rigidbody>(true);
            if (childBodies == null) return false;

            for (int i = 0; i < childBodies.Length; i++)
            {
                Rigidbody rb = childBodies[i];
                if (rb == null) continue;

                GameObject go = rb.gameObject;
                if (go == null || go == gameObject || go == _assemblyIndicator || go == _fistColl)
                    continue;

                Transform t = go.transform;
                if (_handPivot != null && t.IsChildOf(_handPivot.transform)) continue;
                if (_handOffset != null && t.IsChildOf(_handOffset.transform)) continue;

                string n = go.name;
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("VINP", global::System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                if (n.IndexOf("(VIN", global::System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("itemx", global::System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void UpdateAssemblyIndicatorFromActiveTarget()
        {
            if (_assemblyIndicator == null) return;

            AttachAssemblyIndicatorToHandAnchor(_assemblyIndicator.transform);

            PlayMakerFSM activeTarget = GetActiveInteractionTargetForHand();
            GameObject handHeldObject = heldObject;
            if (handHeldObject == null)
            {
                handHeldObject = hand == HandSide.Right
                    ? VRFsmActionBridge.HeldObject_R
                    : VRFsmActionBridge.HeldObject_L;
            }

            bool hasHeldObject = handHeldObject != null;
            bool hasAttachedAssemblyPart = HasLikelyAttachedAssemblyPart();
            bool thisHandHasAssemblyPart = hasHeldObject || hasAttachedAssemblyPart;

            VRHand otherHand = hand == HandSide.Right ? LeftHandInstance : RightHandInstance;
            GameObject otherHeldObject = hand == HandSide.Right
                ? VRFsmActionBridge.HeldObject_L
                : VRFsmActionBridge.HeldObject_R;
            bool otherHandHasAssemblyPart = otherHeldObject != null;

            if (otherHand != null)
                otherHandHasAssemblyPart = otherHandHasAssemblyPart
                    || otherHand.heldObject != null
                    || otherHand.HasLikelyAttachedAssemblyPart();

            bool hasVinpTarget = IsVinpIndicatorTarget(activeTarget);

            bool thisHandWinsByDistance = hand == HandSide.Right
                ? VRFsmActionBridge.ActiveInteractionDist <= VRFsmActionBridge.ActiveInteractionDistLeft
                : VRFsmActionBridge.ActiveInteractionDistLeft < VRFsmActionBridge.ActiveInteractionDist;

            bool ownershipAllows = thisHandHasAssemblyPart
                && (!otherHandHasAssemblyPart || thisHandWinsByDistance);

            bool shouldShow = hasVinpTarget && ownershipAllows;

            if (_assemblyIndicatorLogCooldown > 0)
                _assemblyIndicatorLogCooldown--;

            if (shouldShow != _lastAssemblyIndicatorVisible)
            {
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Indicator visible=" + shouldShow
                    + " held='" + (handHeldObject != null ? handHeldObject.name : "none") + "'"
                    + " attachedPart=" + hasAttachedAssemblyPart
                    + " thisHasPart=" + thisHandHasAssemblyPart
                    + " otherHasPart=" + otherHandHasAssemblyPart
                    + " winByDist=" + thisHandWinsByDistance
                    + " target='" + (activeTarget != null && activeTarget.gameObject != null
                        ? activeTarget.gameObject.name
                        : "none") + "'"
                    + " targetFsm='" + (activeTarget != null ? activeTarget.FsmName : "none") + "'"
                    + " cachedVinp='" + (_cachedVinp != null ? _cachedVinp.name : "none") + "'");
                _lastAssemblyIndicatorVisible = shouldShow;
            }
            else if (!shouldShow && hasVinpTarget && _assemblyIndicatorLogCooldown == 0)
            {
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Indicator blocked"
                    + " held='" + (handHeldObject != null ? handHeldObject.name : "none") + "'"
                    + " attachedPart=" + hasAttachedAssemblyPart
                    + " thisHasPart=" + thisHandHasAssemblyPart
                    + " otherHasPart=" + otherHandHasAssemblyPart
                    + " winByDist=" + thisHandWinsByDistance
                    + " target='" + (activeTarget != null && activeTarget.gameObject != null
                        ? activeTarget.gameObject.name
                        : "none") + "'"
                    + " targetFsm='" + (activeTarget != null ? activeTarget.FsmName : "none") + "'"
                    + " reason='ownership gate rejected'"
                    + " cachedInstaller='" + (_cachedInstallerFsm != null
                        ? _cachedInstallerFsm.gameObject.name + "." + _cachedInstallerFsm.FsmName
                        : "none") + "'"
                    + " cachedVinp='" + (_cachedVinp != null ? _cachedVinp.name : "none") + "'");
                _assemblyIndicatorLogCooldown = 60;
            }

            _assemblyIndicator.SetActive(shouldShow);
        }

        private void UpdateAssemblyState()
        {
            if (heldObject == null)
            {
                if (_assembleReady)
                {
                    _assembleReady = false;
                }
                return;
            }

            bool wasReady = _assembleReady;
            string assemblyReason;
            _assembleReady = VRInteraction.CheckAssemblyProximity(
                _cachedInstallerFsm,
                _cachedVinp,
                heldObject,
                out assemblyReason);

            if (_assembleReady != wasReady)
            {
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Assembly ready: " + _assembleReady
                    + " (holding " + heldObject.name + ")"
                    + " reason='" + assemblyReason + "'");
            }
        }

        // ── Knob rotation ─────────────────────────────────────────────────────────
        // Only runs when VRFsmPatcher.PatcherActive is false (gated in Update).

        private void UpdateKnob()
        {
            if (activeKnobFsm == null) return;
            VRInteraction.ForceKnobToGetScroll(activeKnobFsm);

            Vector3 forward = transform.forward;
            Vector3 prevProj = Vector3.ProjectOnPlane(prevControllerRot * Vector3.up, forward);
            Vector3 currProj = Vector3.ProjectOnPlane(transform.rotation * Vector3.up, forward);

            float roll = 0f;
            if (prevProj.sqrMagnitude > 0.001f && currProj.sqrMagnitude > 0.001f)
            {
                roll = Vector3.Angle(prevProj, currProj);
                if (Vector3.Dot(Vector3.Cross(prevProj, currProj), forward) < 0f) roll = -roll;
            }

            prevControllerRot = transform.rotation;
            rollAccumulator += roll;

            while (rollAccumulator >= KnobDegreesPerTick)
            { VRInteraction.TickKnob(activeKnobFsm, true); rollAccumulator -= KnobDegreesPerTick; }
            while (rollAccumulator <= -KnobDegreesPerTick)
            { VRInteraction.TickKnob(activeKnobFsm, false); rollAccumulator += KnobDegreesPerTick; }
        }

        private void MaintainBridgeHeldAttachment()
        {
            GameObject heldByBridge = hand == HandSide.Right
                ? VRFsmActionBridge.HeldObject_R
                : VRFsmActionBridge.HeldObject_L;

            if (heldByBridge == null) return;

            // If some other system has reparented this object away from the player rig
            // (for example successful install), release hand ownership instead of reattaching.
            Transform heldTransform = heldByBridge.transform;
            if (heldTransform.parent != null && !IsTransformUnderPlayerRig(heldTransform))
            {
                ClearBridgeHeldObject(heldByBridge);
                return;
            }

            if (heldByBridge.transform.IsChildOf(transform)) return;

            heldByBridge.transform.SetParent(transform, true);
            Rigidbody rb = heldByBridge.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }

        private void SyncHeldOwnershipIfInvalid()
        {
            if (heldObject == null)
            {
                GameObject bridgeHeld = hand == HandSide.Right
                    ? VRFsmActionBridge.HeldObject_R
                    : VRFsmActionBridge.HeldObject_L;

                if (bridgeHeld != null)
                {
                    if (bridgeHeld.activeInHierarchy && bridgeHeld.transform.IsChildOf(transform))
                    {
                        heldObject = bridgeHeld;
                        if (heldRigidbody == null)
                            heldRigidbody = bridgeHeld.GetComponent<Rigidbody>();
                        return;
                    }

                    bool notifyDrop = bridgeHeld.activeInHierarchy
                        && !bridgeHeld.transform.IsChildOf(transform);
                    ReleaseHeldOwnership(bridgeHeld, notifyDrop);
                    return;
                }

                if (bridgeHeld == null
                    && heldRigidbody == null
                    && !_assembleReady
                    && _cachedInstallerFsm == null
                    && _cachedVinp == null)
                {
                    return;
                }

#if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Cleared stale held state (null held object)");
#endif
                VRInteraction.ResetHandPickupFsmStaleObject(null);
                heldRigidbody = null;
                _assembleReady = false;
                _cachedInstallerFsm = null;
                _cachedVinp = null;
                ClearBridgeHeldObject(null);
                return;
            }

            GameObject staleHeldObject = heldObject;

            if (!staleHeldObject.activeInHierarchy)
            {
#if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Cleared stale held state (inactive): "
                    + staleHeldObject.name);
#endif
                ReleaseHeldOwnership(staleHeldObject, false);
                return;
            }

            if (staleHeldObject.transform.IsChildOf(transform))
                return;

#if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[MVRC " + hand + "] Cleared stale held state (detached): "
                + staleHeldObject.name);
#endif
            ReleaseHeldOwnership(staleHeldObject, true);
        }

        private void ReleaseHeldOwnership(GameObject staleHeldObject, bool notifyDrop)
        {
            _pendingGripDropFrames = 0;

            if (notifyDrop && staleHeldObject != null)
            {
                // Send FINISHED to the PickUp FSM on the Hand to trigger unequip sequence
                VRFsmActionBridge.TryFireEquipFinishedForDrop();
                VRInteraction.NotifyHandFsmDrop(staleHeldObject);
            }

            if (staleHeldObject != null)
                VRInteraction.ResetHandPickupFsmStaleObject(staleHeldObject);

            PlayMakerHelpers.DebugLog("[VR-THROW-DIAG] _heldItem cleared (was "
                + (heldObject != null ? heldObject.name : "null") + ")");
            heldObject = null;
            heldRigidbody = null;
            _assembleReady = false;
            _cachedInstallerFsm = null;
            _cachedVinp = null;
            ClearBridgeHeldObject(staleHeldObject);
            ResetThrowVelocityTracking();
        }

        private void ClearBridgeHeldObject(GameObject target)
        {
            if (target == null)
            {
                if (hand == HandSide.Right)
                    VRFsmActionBridge.HeldObject_R = null;
                else
                    VRFsmActionBridge.HeldObject_L = null;
                return;
            }

            if (VRFsmActionBridge.HeldObject_R == target)
                VRFsmActionBridge.HeldObject_R = null;
            if (VRFsmActionBridge.HeldObject_L == target)
                VRFsmActionBridge.HeldObject_L = null;
        }

        private bool IsTransformUnderPlayerRig(Transform t)
        {
            while (t != null)
            {
                if (string.Equals(t.name, "PLAYER", global::System.StringComparison.OrdinalIgnoreCase))
                    return true;
                t = t.parent;
            }
            return false;
        }

        // ── Main update ───────────────────────────────────────────────────────────

        private void ApplyCurrentPoseVisualBasis()
        {
            HandPoseConfig poseCfg = PoseConfigs[(int)_currentPose];

            if (_handOffset != null)
            {
                _handOffset.transform.position = transform.position + poseCfg.handOffset;
                _handOffset.transform.rotation = Quaternion.Euler(poseCfg.pivotRotEuler);
            }

            if (_handPivot != null)
            {
                Vector3 euler = poseCfg.pivotRotEuler;
                if (hand == HandSide.Left) { euler.z = -euler.z; euler.y = -euler.y; }
                _handPivot.transform.rotation = transform.rotation * Quaternion.Euler(euler);
            }
        }

        private void ApplyPoseTransforms(Vector3 worldPos, Quaternion worldRot)
        {
            transform.position = worldPos;
            transform.rotation = worldRot;

            ApplyCurrentPoseVisualBasis();
        }

        private void RunSharedHandFrame()
        {
            if (hand == HandSide.Left)
                TryAttachHudToLeftHand();
            
            Vector3 fwd = transform.rotation * Vector3.forward;
            Vector3 prevProj = Vector3.ProjectOnPlane(_prevBridgeRot * Vector3.up, fwd);
            Vector3 currProj = Vector3.ProjectOnPlane(transform.rotation * Vector3.up, fwd);
            float rollDelta = 0f;
            if (prevProj.sqrMagnitude > 0.001f && currProj.sqrMagnitude > 0.001f)
            {
                rollDelta = Vector3.Angle(prevProj, currProj);
                if (Vector3.Dot(Vector3.Cross(prevProj, currProj), fwd) < 0f)
                    rollDelta = -rollDelta;
            }
            _prevBridgeRot = transform.rotation;

            if (hand == HandSide.Right)
                VRFsmActionBridge.ControllerRollDelta_R = rollDelta;
            else
                VRFsmActionBridge.ControllerRollDelta_L = rollDelta;

            if (hand == HandSide.Right && ToolModeActive)
            {
                VRInteraction.EnsureBoltRaycastPatched();
                VRInteraction.UpdateNearestBolt(transform.position);
            }

            if (hand == HandSide.Right && _pendingToolAttachFrames > 0)
            {
                _pendingToolAttachFrames--;
                if (_pendingToolAttachFrames == 0)
                    VRInteraction.AttachToolToHand();
            }

            _heldObjectAtFrameStart = heldObject;
            _bridgeHeldAtFrameStart = GetBridgeHeldObjectForHand() != null;

            SyncHeldOwnershipIfInvalid();
            MaintainBridgeHeldAttachment();
            UpdateAssemblyState();
            UpdateAssemblyIndicatorFromActiveTarget();
            SyncHeldOwnershipIfInvalid();
            CheckInput();

            if (hand == HandSide.Right)
            {
                VRFsmActionBridge.HandPosition_R = transform.position;
                VRFsmActionBridge.UpdateDispatcher(transform.position, true);
                if (VRFsmActionBridge.ActiveInteractionTarget != null)
                    VRFsmActionBridge.NearestRightHandObject = VRFsmActionBridge.ActiveInteractionTarget.gameObject;
                else
                    VRFsmActionBridge.NearestRightHandObject = null;
            }
            else
            {
                VRFsmActionBridge.HandPosition_L = transform.position;
                VRFsmActionBridge.UpdateDispatcher(transform.position, false);
                if (VRFsmActionBridge.ActiveInteractionTargetLeft != null)
                    VRFsmActionBridge.NearestLeftHandObject = VRFsmActionBridge.ActiveInteractionTargetLeft.gameObject;
                else
                    VRFsmActionBridge.NearestLeftHandObject = null;
            }

            UpdateLaserPointerState();
            UpdateBoltTighten();
#if MVRC_DEBUG
            UpdateDebugSphere();
#endif

            GameObject highlightTarget = null;
            if (hand == HandSide.Right && VRFsmActionBridge.ActiveInteractionTarget != null)
                highlightTarget = VRFsmActionBridge.ActiveInteractionTarget.gameObject;
            else if (hand == HandSide.Left && VRFsmActionBridge.ActiveInteractionTargetLeft != null)
                highlightTarget = VRFsmActionBridge.ActiveInteractionTargetLeft.gameObject;
            nearestObject = highlightTarget;
        }

        private void UpdateLaserPointerState()
        {
            VRLaserPointer laser = GetComponent<VRLaserPointer>();
            if (laser == null)
                return;

            if (hand == HandSide.Right)
                laser.LaserEnabled = VRFsmActionBridge.LaserRightIsActive_R;
            else
                laser.LaserEnabled = VRFsmActionBridge.LaserLeftIsActive_L;
        }

        private void TryAttachHudToLeftHand()
        {
            Transform leftHandRoot = GetWristAttachmentRoot();
            if (leftHandRoot == null)
                return;

            if (_hudTransform == null)
            {
                if (Time.unscaledTime < _nextHudResolveAttemptTime)
                    return;

                GameObject hudGo = GameObject.Find(HudObjectPath);
                PlayMakerHelpers.DebugLog("[WristHUD] Find(GUI/HUD) = " + (hudGo == null ? "NULL" : hudGo.name));
                if (hudGo == null)
                {
                    _nextHudResolveAttemptTime = Time.unscaledTime + HudResolveRetrySeconds;
                    return;
                }

                _hudTransform = hudGo.transform;
                _nextHudResolveAttemptTime = 0f;
            }

            if (_hudTransform.parent != leftHandRoot)
                _hudTransform.SetParent(leftHandRoot, false);

            _hudTransform.localPosition = HudLeftHandLocalPosition;
            _hudTransform.localEulerAngles = HudLeftHandLocalRotation;
            _hudTransform.localScale = HudLeftHandLocalScale;

            if (!_hudPrepared)
                PrepareHudInPlace(_hudTransform);
        }

        private void PrepareHudInPlace(Transform hud)
        {
            PlayMakerHelpers.DebugLog("[WristHUD] PrepareHudInPlace called, already prepared=" + _hudPrepared);

            if (hud == null)
                return;

            if (_hudPrepared)
                return;
            _hudPrepared = true;

            Transform fps = hud.Find("FPS");
            if (fps != null)
            {
                fps.localPosition = HudFpsLocalPosition;
            }
            Transform tmp = hud.Find("Temp");
            if (tmp != null)
            {
                tmp.localPosition = HudTmpLocalPosition;
                tmp.gameObject.SetActive(true);
            }
            Transform drty = hud.Find("Dirty");
            if (drty != null)
            {
                drty.localPosition = HudDrtyLocalPosition;
                drty.gameObject.SetActive(true);
            }
            Transform jail = hud.Find("Jailtime");
            if (jail != null)
            {
                jail.localPosition = HudJailLocalPosition;
            }
        }

        private void UpdateHudVisibilityFromCamera()
        {
            if (_hudTransform == null) return;

            Transform viewTransform = null;
            if (VRCompositorManager.VRHeadTransform != null)
                viewTransform = VRCompositorManager.VRHeadTransform;
            else if (LookManager.FPSCameraTransform != null)
                viewTransform = LookManager.FPSCameraTransform;
            else if (Camera.main != null)
                viewTransform = Camera.main.transform;

            if (viewTransform == null) return;

            Vector3 toCamera = (viewTransform.position - _hudTransform.position).normalized;
            bool frontFacing = Vector3.Dot(-_hudTransform.forward, toCamera) > 0.3f;

            if (_hudTransform.gameObject.activeSelf != frontFacing)
                _hudTransform.gameObject.SetActive(frontFacing);
        }
        void Update()
        {
#if MVRC_DEBUG
            float perfStart = Time.realtimeSinceStartup;
#endif
            UpdateHudVisibilityFromCamera();
#if MVRC_DEBUG
            float perfElapsedMs = (Time.realtimeSinceStartup - perfStart) * 1000f;
            if (perfElapsedMs > 1f)
                PlayMakerHelpers.DebugLog("[PERF] VRHand.Update(" + hand + "): " + perfElapsedMs.ToString("F2") + "ms");
#endif
        }

        public void OnVRUpdate()
        {
#if MVRC_DEBUG
            float perfStart = Time.realtimeSinceStartup;
            if (!EnsureInitialized())
            {
                float perfElapsedEarlyMs = (Time.realtimeSinceStartup - perfStart) * 1000f;
                if (perfElapsedEarlyMs > 1f)
                    PlayMakerHelpers.DebugLog("[PERF] VRHand.OnVRUpdate(" + hand + "): " + perfElapsedEarlyMs.ToString("F2") + "ms");
                return;
            }
#endif

            bool valid = (hand == HandSide.Left)
                ? LookManager.LeftControllerValid
                : LookManager.RightControllerValid;

            if (!valid)
            {
                if (meshVisible) { _handPivot.SetActive(false); meshVisible = false; }
                if (_assemblyIndicator != null) _assemblyIndicator.SetActive(false);
                RemoveHighlight();
                //CleanupActiveInteractions();
                return;
            }

            if (!meshVisible)
            {
                _handPivot.SetActive(true);
                meshVisible = true;
            }

            Vector3 rawPos = (hand == HandSide.Left) ? LookManager.LeftControllerPos : LookManager.RightControllerPos;
            Quaternion rawRot = (hand == HandSide.Left) ? LookManager.LeftControllerRot : LookManager.RightControllerRot;

            if (LookManager.FPSCameraTransform == null) return;
            if (VRCompositorManager.VRHeadTransform == null) return;

            Vector3 anchorPos = VRCompositorManager.VRHeadTransform.position;
            Vector3 controllerFromHMD = rawPos - LookManager.HMDRawPos;
            Quaternion roomToWorld = VRCompositorManager.VRHeadTransform.rotation
                                               * Quaternion.Inverse(LookManager.HMDRawRot);

            Vector3 worldPos = anchorPos + roomToWorld * controllerFromHMD;
            Quaternion worldRot = roomToWorld * rawRot;

            ApplyPoseTransforms(worldPos, worldRot);
            RunSharedHandFrame();
#if MVRC_DEBUG
            float perfElapsedFinalMs = (Time.realtimeSinceStartup - perfStart) * 1000f;
            if (perfElapsedFinalMs > 1f)
                PlayMakerHelpers.DebugLog("[PERF] VRHand.OnVRUpdate(" + hand + "): " + perfElapsedFinalMs.ToString("F2") + "ms");
#endif
        }

        public void OnDebugEmulatedUpdate(DebugHandFrameState frame)
        {
#if MVRC_DEBUG
            float perfStart = Time.realtimeSinceStartup;
            if (!EnsureInitialized())
            {
                float perfElapsedEarlyMs = (Time.realtimeSinceStartup - perfStart) * 1000f;
                if (perfElapsedEarlyMs > 1f)
                    PlayMakerHelpers.DebugLog("[PERF] VRHand.OnDebugEmulatedUpdate(" + hand + "): " + perfElapsedEarlyMs.ToString("F2") + "ms");
                return;
            }
#endif

            if (!meshVisible)
            {
                _handPivot.SetActive(true);
                meshVisible = true;
            }

            ApplyPoseTransforms(frame.Position, frame.Rotation);
            RunSharedHandFrame();
#if MVRC_DEBUG
            float perfElapsedFinalMs = (Time.realtimeSinceStartup - perfStart) * 1000f;
            if (perfElapsedFinalMs > 1f)
                PlayMakerHelpers.DebugLog("[PERF] VRHand.OnDebugEmulatedUpdate(" + hand + "): " + perfElapsedFinalMs.ToString("F2") + "ms");
#endif
        }

#if MVRC_DEBUG
        private void UpdateDebugSphere()
        {
            if (_debugSphere == null) return;
            if (!VRProximitySphereDebug.ShowDebug) return;

            string interactName = "none";
            if (hand == HandSide.Right && VRFsmActionBridge.ActiveInteractionTarget != null)
                interactName = VRFsmActionBridge.ActiveInteractionTarget.gameObject.name;
            else if (hand == HandSide.Left && VRFsmActionBridge.ActiveInteractionTargetLeft != null)
                interactName = VRFsmActionBridge.ActiveInteractionTargetLeft.gameObject.name;

            _debugSphere.CurrentFsmTarget = interactName;

            if (_debugSphereScanCountdown > 0)
            {
                _debugSphereScanCountdown--;
                return;
            }
            _debugSphereScanCountdown = DebugSphereScanIntervalFrames - 1;

            // FIX: Use _debugSphere.PickupRadius instead of making it static
            int rawHitCount = PhysicsCompat.OverlapSphereNonAlloc(
                transform.position, _debugSphere.PickupRadius, _debugHitsBuffer);
            _debugSphere.LastHitCount = rawHitCount;

            int patchedHits = 0;
            GameObject bestPickable = null;
            float bestPickDist = float.MaxValue;

            for (int i = 0; i < rawHitCount; i++)
            {
                Collider c = _debugHitsBuffer[i];
                if (c == null || c.gameObject == null) continue;
                GameObject go = c.gameObject;

                Transform t = c.transform;
                bool isPatched = false;
                while (t != null)
                {
                    PlayMakerFSM[] fsms = t.GetComponents<PlayMakerFSM>();
                    foreach (PlayMakerFSM fsm in fsms)
                    {
                        if (fsm != null && VRFsmPatcher._patchedFsms.Contains(fsm))
                        {
                            patchedHits++;
                            isPatched = true;
                            break;
                        }
                    }
                    if (isPatched) break;
                    t = t.parent;
                }

                if (go == heldObject || go == _handPivot || go == _fistColl) continue;
                if (go.name.StartsWith("MVRC_") || go.name == "PlayerColl" || go.name == "Pivot" || go.name == "PLAYER") continue;

                bool pickable = false;
                if (VRInteraction.IsPickableTool(c)) pickable = true;
                else if (go.GetComponent<Rigidbody>() != null
                    && go.GetComponent<HingeJoint>() == null
                    && go.GetComponent<FixedJoint>() == null
                    && go.GetComponent<SpringJoint>() == null)
                {
                    ConfigurableJoint cj = go.GetComponent<ConfigurableJoint>();
                    if (cj == null || cj.connectedBody == null) pickable = true;
                }

                if (pickable)
                {
                    float dist = Vector3.Distance(transform.position, c.ClosestPointOnBounds(transform.position));
                    if (dist < bestPickDist)
                    {
                        bestPickDist = dist;
                        bestPickable = go;
                    }
                }
            }

            _debugSphere.CurrentPickTarget = bestPickable != null ? bestPickable.name : "none";
            _debugSphere.LastFsmHitCount = patchedHits;
        }
#endif
        void OnDestroy()
        {
            CleanupActiveInteractions();
            RemoveHighlight();
            Drop();
            if (hand == HandSide.Left)
            {
                _hudTransform = null;
                _nextHudResolveAttemptTime = 0f;
                _hudPrepared = false;
            }
            if (_fistColl != null && _fistCollOriginalParent != null)
                _fistColl.transform.SetParent(_fistCollOriginalParent, false);
            if (_handPivot != null) { Destroy(_handPivot); _handPivot = null; }
            if (_assemblyIndicator != null) { Destroy(_assemblyIndicator); _assemblyIndicator = null; }
            if (_handOffset != null) { Destroy(_handOffset); _handOffset = null; }
        }

        private void CleanupActiveInteractions()
        {
            if (_boltTightenMode)
            { _boltTightenMode = false; _boltRollAccumulator = 0f; }
            if (activeKnobFsm != null)
            { VRInteraction.EndKnobInteraction(activeKnobFsm); activeKnobFsm = null; rollAccumulator = 0f; }
            _rightClickMode = false;
            _assembleReady = false;
            if (_assemblyIndicator != null) _assemblyIndicator.SetActive(false);
        }

        // ── Highlight ─────────────────────────────────────────────────────────────

        private void ApplyHighlight(GameObject target)
        {
            Renderer r = target.GetComponent<Renderer>()
                      ?? target.GetComponentInChildren<Renderer>();
            if (r == null) { highlightedRenderer = null; highlightedMaterial = null; return; }
            Material material = r.material;
            if (material == null) { highlightedRenderer = null; highlightedMaterial = null; return; }
            highlightedRenderer = r;
            highlightedMaterial = material;
            originalColor = material.color;
            material.color = Color.Lerp(originalColor, Color.yellow, 0.9f);
        }

        private void RemoveHighlight()
        {
            if (highlightedMaterial != null)
            {
                highlightedMaterial.color = originalColor;
                highlightedMaterial = null;
            }
            highlightedRenderer = null;
            nearestObject = null;
        }
    }

    internal static class SteamVR_Controller
    {
        internal sealed class Device
        {
            private readonly int _controllerIndex;

            internal Device(int controllerIndex)
            {
                _controllerIndex = controllerIndex;
            }

            internal Vector3 velocity
            {
                get
                {
                    if (_controllerIndex < 0 || OpenVR.System == null)
                        return Vector3.zero;

                    VRControllerState_t controllerState = default(VRControllerState_t);
                    TrackedDevicePose_t trackedPose = default(TrackedDevicePose_t);
                    bool hasState = OpenVR.System.GetControllerStateWithPose(
                        ETrackingUniverseOrigin.TrackingUniverseStanding,
                        (uint)_controllerIndex,
                        ref controllerState,
                        (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t)),
                        ref trackedPose);

                    if (!hasState || !trackedPose.bPoseIsValid)
                        return Vector3.zero;

                    return new Vector3(
                        trackedPose.vVelocity.v0,
                        trackedPose.vVelocity.v1,
                        trackedPose.vVelocity.v2);
                }
            }
        }

        internal static Device Input(int controllerIndex)
        {
            return new Device(controllerIndex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRFistCollision, unchanged.
    // ─────────────────────────────────────────────────────────────────────────────

    public class VRFistCollision : MonoBehaviour
    {
        void OnCollisionEnter(Collision col) { TryBreak(col.gameObject); }
        void OnTriggerEnter(Collider col) { TryBreak(col.gameObject); }

        private void TryBreak(GameObject hit)
        {
            if (hit == null) return;
            Transform t = hit.transform;
            while (t != null)
            {
                foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (fsm == null) continue;
                    bool hasBreak = false, hasHit = false;
                    foreach (var s in fsm.FsmStates) if (s.Name == "Break") { hasBreak = true; break; }
                    foreach (var ev in fsm.FsmEvents) if (ev.Name == "HIT") { hasHit = true; break; }
                    if (hasBreak && hasHit)
                    {
                        fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("HIT"));
                        PlayMakerHelpers.DebugLog("[MVRC] FistColl HIT → '" + fsm.gameObject.name + "'");
                        return;
                    }
                }
                t = t.parent;
            }
        }
    }
#if MVRC_DEBUG
    public class VRProximitySphereDebug : MonoBehaviour
    {
        public float InteractionRadius = 0.15f;
        public float PickupRadius = 0.15f;
        public static bool ShowDebug = true;

        // MUST remain static for VRFsmActions.cs fallback
        public static Transform RightHandTransform = null;

        // Instance variables
        public string CurrentFsmTarget = "none";
        public string CurrentPickTarget = "none";
        public int LastHitCount = 0;
        public int LastFsmHitCount = 0;

        private GameObject _interactionSphere;
        private GameObject _pickupSphere;
        private GameObject _labelGO;
        private TextMesh _label;

        void Start()
        {
            _interactionSphere = CreateWireSphere(InteractionRadius, Color.yellow);
            _pickupSphere = CreateWireSphere(PickupRadius, Color.cyan);

            _labelGO = new GameObject("MVRC_ProxLabel");
            _labelGO.transform.SetParent(this.transform, false);
            _labelGO.transform.localPosition = new Vector3(0f, 0.15f, 0f);

            _label = _labelGO.AddComponent<TextMesh>();
            _label.fontSize = 24;
            _label.characterSize = 0.004f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.color = Color.white;

            SetVisible(false);
        }

        void Update()
        {
            bool shouldBeVisible = ShowDebug;
            if (_interactionSphere.activeSelf != shouldBeVisible)
            {
                SetVisible(shouldBeVisible);
            }

            if (!shouldBeVisible) return;

            Vector3 pos = transform.position;
            _interactionSphere.transform.position = pos;
            _pickupSphere.transform.position = pos;

            if (_label != null)
            {
                _label.transform.position = pos + Vector3.up * 0.15f;
                if (Camera.main != null)
                    _label.transform.rotation = Camera.main.transform.rotation;

                string fsmName = CurrentFsmTarget.Length > 16
                    ? CurrentFsmTarget.Substring(0, 16) : CurrentFsmTarget;
                string pickName = CurrentPickTarget.Length > 16
                    ? CurrentPickTarget.Substring(0, 16) : CurrentPickTarget;

                _label.text = "FSM: " + fsmName + "\nGrab: " + pickName
                    + "\nF:" + LastFsmHitCount + " P:" + LastHitCount;
            }
        }

        private void SetVisible(bool vis)
        {
            if (_interactionSphere != null) _interactionSphere.SetActive(vis);
            if (_pickupSphere != null) _pickupSphere.SetActive(vis);
            if (_labelGO != null) _labelGO.SetActive(vis);
        }

        private GameObject CreateWireSphere(float radius, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "MVRC_ProxSphere_" + radius.ToString("F2");

            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.localScale = Vector3.one * radius * 2f;

            Renderer r = go.GetComponent<Renderer>();
            Material mat = new Material(Shader.Find("Transparent/Diffuse"));
            color.a = 0.12f;
            mat.color = color;
            r.material = mat;

            return go;
        }

        void OnDestroy()
        {
            if (_interactionSphere != null) Destroy(_interactionSphere);
            if (_pickupSphere != null) Destroy(_pickupSphere);
            if (_labelGO != null) Destroy(_labelGO);
        }
    }
#else
    // Release build compatibility stub: keeps fallback references valid without spawning debug visuals.
    public class VRProximitySphereDebug : MonoBehaviour
    {
        public float InteractionRadius = 0.15f;
        public float PickupRadius = 0.30f;
        public static bool ShowDebug = false;

        // MUST remain static for VRFsmActions.cs fallback
        public static Transform RightHandTransform = null;

        public string CurrentFsmTarget = "none";
        public string CurrentPickTarget = "none";
        public int LastHitCount = 0;
        public int LastFsmHitCount = 0;
    }
#endif
    public static class VRPlayerGestures
    {
        private static PlayMakerFSM _playerFunctionsFsm;

        private static PlayMakerFSM GetFsm()
        {
            if (_playerFunctionsFsm != null) return _playerFunctionsFsm;

            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return null;

            Transform t = player.transform;
            string[] path = { "Pivot", "AnimPivot", "Camera", "FPSCamera" };
            foreach (string step in path)
            {
                t = t.Find(step);
                if (t == null)
                {
                    PlayMakerHelpers.DebugLog("[VRGestures] Path broken at: " + step);
                    return null;
                }
            }

            foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName == "PlayerFunctions")
                {
                    _playerFunctionsFsm = fsm;
                    PlayMakerHelpers.DebugLog("[VRGestures] PlayerFunctions FSM found.");
                    return _playerFunctionsFsm;
                }
            }

            PlayMakerHelpers.DebugLog("[VRGestures] PlayerFunctions FSM not found on FPSCamera.");
            return null;
        }

        public static void SendEvent(string eventName)
        {
            PlayMakerFSM fsm = GetFsm();
            if (fsm == null)
            {
                PlayMakerHelpers.DebugLog("[VRGestures] Cannot send event: FSM is null.");
                return;
            }
            PlayMakerHelpers.DebugLog("[VRGestures] Sending event: " + eventName);
            fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(eventName));
        }

        // Convenience methods for known gestures
        public static void Fist()   { SendEvent("FIST"); }
        public static void Finger() { SendEvent("MIDDLEFINGER"); }
    }
}
