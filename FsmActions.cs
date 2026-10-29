// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using HutongGames.PlayMaker;
using MSCLoader;
using MVRC;
using System.Collections.Generic;
using System.Security.Policy;
using UnityEngine;
using UnityStandardAssets.Water;
using static CarCameras;
using static DashBoard;
using static UnityStandardAssets.ImageEffects.BloomOptimized;

namespace MVRC
{
    public static class VRProximityRegistry
    {
        // Keyed by GameObject, holds all activate/leave actions on that object
        // Populated once at patch time, never cleared except on full Reset()
        public static Dictionary<GameObject, List<VRProximityActivateAction>> ActivateActions
            = new Dictionary<GameObject, List<VRProximityActivateAction>>();
        public static Dictionary<GameObject, List<VRProximityLeaveAction>> LeaveActions
            = new Dictionary<GameObject, List<VRProximityLeaveAction>>();
        public static HashSet<GameObject> GripRequiredObjects
            = new HashSet<GameObject>();
        public static Dictionary<GameObject, PlayMakerFSM> GripRequiredFsms
            = new Dictionary<GameObject, PlayMakerFSM>();
        public static HashSet<GameObject> HeldInteractableObjects
            = new HashSet<GameObject>();
        public static Dictionary<GameObject, List<PlayMakerFSM>> HeldInteractableFsms
            = new Dictionary<GameObject, List<PlayMakerFSM>>();
        public static HashSet<PlayMakerFSM> NonSelectableInteractionFsms
            = new HashSet<PlayMakerFSM>();
        public static Dictionary<PlayMakerFSM, List<string>> NonSelectableInteractionStateNameContains
            = new Dictionary<PlayMakerFSM, List<string>>();

        public static void RegisterActivate(GameObject go, VRProximityActivateAction action)
        {
            if (!ActivateActions.ContainsKey(go))
                ActivateActions[go] = new List<VRProximityActivateAction>();
            ActivateActions[go].Add(action);
        }
    
        public static void RegisterLeave(GameObject go, VRProximityLeaveAction action)
        {
            if (!LeaveActions.ContainsKey(go))
                LeaveActions[go] = new List<VRProximityLeaveAction>();
            LeaveActions[go].Add(action);
        }

        public static void RegisterHeldInteractable(GameObject go, PlayMakerFSM fsm)
        {
            if (go == null || fsm == null) return;

            HeldInteractableObjects.Add(go);

            List<PlayMakerFSM> fsms;
            if (!HeldInteractableFsms.TryGetValue(go, out fsms))
            {
                fsms = new List<PlayMakerFSM>();
                HeldInteractableFsms[go] = fsms;
            }

            if (!fsms.Contains(fsm))
                fsms.Add(fsm);
        }
    
        public static void Reset()
        {
            ActivateActions.Clear();
            LeaveActions.Clear();
            GripRequiredObjects.Clear();
            GripRequiredFsms.Clear();
            HeldInteractableObjects.Clear();
            HeldInteractableFsms.Clear();
            NonSelectableInteractionFsms.Clear();
            NonSelectableInteractionStateNameContains.Clear();
        }
    }

    // Persistent manager: hides configured objects while they are parented to a hand
    // Works even if the object has no PlayMaker FSM; matches by GameObject name.
    public class HeldVisibilityManager : MonoBehaviour
    {
        public List<string> objectNameContains = new List<string> { "dipper(itemx)" };

        private class StoredState
        {
            public Renderer[] renderers;
            public bool[] rendererWasEnabled;
            public Collider[] colliders;
            public bool[] colliderWasEnabled;
        }

        private readonly Dictionary<GameObject, StoredState> _hidden = new Dictionary<GameObject, StoredState>();

        public static void EnsureInstance()
        {
            HeldVisibilityManager existing = FindObjectOfType<HeldVisibilityManager>();
            if (existing != null) return;
            GameObject go = new GameObject("MVRC_HeldVisibilityManager");
            DontDestroyOnLoad(go);
            var comp = go.AddComponent<HeldVisibilityManager>();
            // Seed patterns from any existing VRFsmPatcher rules
            try { comp.UpdatePatternsFromRules(); } catch { }
        }

        public void UpdatePatternsFromRules()
        {
            objectNameContains = new List<string>();
            try
            {
                foreach (var rule in VRFsmPatcher.Rules)
                {
                    if (rule == null) continue;
                    if (rule.method != VRInteractionMethod.DeactivateHeldObject) continue;
                    if (rule.objectNameContains == null) continue;
                    foreach (var s in rule.objectNameContains)
                    {
                        if (!string.IsNullOrEmpty(s) && !objectNameContains.Contains(s))
                            objectNameContains.Add(s);
                    }
                }
            }
            catch { }
        }

        void Awake()
        {
            DontDestroyOnLoad(this.gameObject);
        }

        void Update()
        {
            // Scan hands for matching children
            ScanHandTransform(VRHand.RightHandInstance != null ? VRHand.RightHandInstance.transform : VRProximitySphereDebug.RightHandTransform);
            ScanHandTransform(VRHand.LeftHandInstance != null ? VRHand.LeftHandInstance.transform : null);

            // Cleanup entries whose objects were destroyed or are no longer children of any hand
            var toRestore = new List<GameObject>();
            foreach (var kv in _hidden)
            {
                var go = kv.Key;
                if (go == null)
                {
                    toRestore.Add(go);
                    continue;
                }

                if (!IsChildOfAnyHand(go.transform))
                    toRestore.Add(go);
            }

            foreach (var g in toRestore)
            {
                if (g != null) Restore(g);
                else _hidden.Remove(g);
            }
        }

        private void ScanHandTransform(Transform hand)
        {
            if (hand == null) return;

            foreach (Transform child in hand)
            {
                if (child == null || child.gameObject == null) continue;
                string name = child.gameObject.name.ToLower();
                foreach (var pat in objectNameContains)
                {
                    if (string.IsNullOrEmpty(pat)) continue;
                    if (name.Contains(pat.ToLower()))
                    {
                        if (!_hidden.ContainsKey(child.gameObject)) Hide(child.gameObject);
                        break;
                    }
                }
            }
        }

        private bool IsChildOfAnyHand(Transform t)
        {
            if (t == null) return false;
            if (VRHand.RightHandInstance != null && IsChildOf(t, VRHand.RightHandInstance.transform)) return true;
            if (VRHand.LeftHandInstance != null && IsChildOf(t, VRHand.LeftHandInstance.transform)) return true;
            if (VRProximitySphereDebug.RightHandTransform != null && IsChildOf(t, VRProximitySphereDebug.RightHandTransform)) return true;
            return false;
        }

        private bool IsChildOf(Transform t, Transform potentialParent)
        {
            if (t == null || potentialParent == null) return false;
            Transform p = t.parent;
            while (p != null)
            {
                if (p == potentialParent) return true;
                p = p.parent;
            }
            return false;
        }

        private void Hide(GameObject go)
        {
            if (go == null) return;
            var state = new StoredState();
            state.renderers = go.GetComponentsInChildren<Renderer>(true);
            state.rendererWasEnabled = new bool[state.renderers.Length];
            for (int i = 0; i < state.renderers.Length; i++)
            {
                state.rendererWasEnabled[i] = state.renderers[i].enabled;
                state.renderers[i].enabled = false;
            }

            state.colliders = go.GetComponentsInChildren<Collider>(true);
            state.colliderWasEnabled = new bool[state.colliders.Length];
            for (int i = 0; i < state.colliders.Length; i++)
            {
                state.colliderWasEnabled[i] = state.colliders[i].enabled;
                state.colliders[i].enabled = false;
            }

            _hidden[go] = state;
        }

        private void Restore(GameObject go)
        {
            if (go == null) return;
            if (!_hidden.TryGetValue(go, out var state)) return;

            if (state.renderers != null)
            {
                for (int i = 0; i < state.renderers.Length; i++)
                {
                    if (state.renderers[i] != null) state.renderers[i].enabled = state.rendererWasEnabled[i];
                }
            }

            if (state.colliders != null)
            {
                for (int i = 0; i < state.colliders.Length; i++)
                {
                    if (state.colliders[i] != null) state.colliders[i].enabled = state.colliderWasEnabled[i];
                }
            }

            _hidden.Remove(go);
        }
    }
    public static class VRObjectHighlighter
    {
        private struct HighlightState
        {
            public Renderer renderer;
            public Material material;      // cached instance; avoids repeated .material access
            public Color    originalColor;
        }
        
        private static readonly Dictionary<GameObject, HighlightState> _highlighted
            = new Dictionary<GameObject, HighlightState>();

        public static void Highlight(GameObject go)
        {
            if (go == null || _highlighted.ContainsKey(go)) return;
            Renderer r = go.GetComponent<Renderer>()
                    ?? go.GetComponentInChildren<Renderer>();
            if (r == null) return;

            Material mat = r.material;    // ONE allocation, then cached for lifetime of highlight
            if (mat == null) return;

            Color orig = mat.color;
            _highlighted[go] = new HighlightState
            {
                renderer      = r,
                material      = mat,
                originalColor = orig
            };
            mat.color = Color.Lerp(orig, Color.yellow, 0.9f);
        }

        public static void Unhighlight(GameObject go)
        {
            if (go == null) return;
            HighlightState state;
            if (!_highlighted.TryGetValue(go, out state)) return;
            if (state.material != null)
                state.material.color = state.originalColor;
            _highlighted.Remove(go);
        }

        public static void UnhighlightAll()
        {
            foreach (KeyValuePair<GameObject, HighlightState> kvp in _highlighted)
                if (kvp.Value.material != null)
                    kvp.Value.material.color = kvp.Value.originalColor;
            _highlighted.Clear();
        }
    }
    public class VRWriteHeldObjectAction : FsmStateAction
    {
        public FsmGameObject storeResult;
        public bool everyFrame = false;

        public override void Reset()
        {
            storeResult = null;
            everyFrame = false;
        }

        public override void OnEnter()
        {
            Enabled = true;
            GameObject held = ResolveHeldItemxObject();
            if (storeResult != null)
                storeResult.Value = held;

            Finish();
        }

        private static GameObject ResolveHeldItemxObject()
        {
            GameObject itemx = FindItemxObject(VRHand.RightHandInstance);
            if (itemx != null) return itemx;

            GameObject held = VRHand.RightHeldObject;
            if (held == null) return null;

            if (NameLooksLikeItemx(held.name))
                return held;

            GameObject nestedItemx = FindItemxInTransform(held.transform);
            if (nestedItemx != null) return nestedItemx;

            return held;
        }

        private static GameObject FindItemxObject(VRHand hand)
        {
            if (hand == null) return null;

            GameObject direct = FindItemxInTransform(hand.transform);
            if (direct != null)
                return direct;

            GameObject bridgeHeld = hand.HeldObject;
            if (bridgeHeld != null && NameLooksLikeItemx(bridgeHeld.name))
                return bridgeHeld;

            return bridgeHeld;
        }

        private static GameObject FindItemxInTransform(Transform root)
        {
            if (root == null) return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            if (children == null) return null;

            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == root) continue;
                if (NameLooksLikeItemx(child.name))
                    return child.gameObject;
            }

            return null;
        }

        private static bool NameLooksLikeItemx(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("itemx", global::System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // VRFsmActionBridge
    //
    // A lightweight static bus that VRHand writes to every frame, and that the
    // injected FsmStateAction subclasses read from.
    //
    // VRHand MUST populate this in its Update() / CheckInput(). See the
    // "Wiring VRHand" section at the bottom of this file for the exact additions.
    // ─────────────────────────────────────────────────────────────────────────────
    public static class VRFsmActionBridge
    {
        private delegate int OverlapSphereNonAllocWithMaskDelegate(Vector3 position, float radius, Collider[] results, int layerMask);

        private static readonly OverlapSphereNonAllocWithMaskDelegate _broadPhaseOverlapSphereNonAllocWithMask =
            CreateOverlapSphereNonAllocWithMaskDelegate();

        // ── Right-controller input ────────────────────────────────────────────────
        public static bool TriggerJustPressed_R = false;
        public static bool TriggerJustReleased_R = false;
        public static bool TriggerIsHeld_R = false;
        public static bool FistJustPressed_R = false;
        public static bool FistIsHeld_R = false;
        public static bool RightClickJustPressed_R = false;
        public static bool LaserRightIsActive_R = false;
        public static bool GripIsHeld_R = false;
        public static bool GripJustPressed_R = false;
        public static bool GripJustReleased_R = false;
        public static float ControllerRollDelta_R = 0f;
        public static Vector3 HandPosition_R = Vector3.zero;
        public static GameObject HeldObject_R = null;
        public static Vector3 ThrowVelocity = Vector3.zero;
        public const float VRThrowForceScale = 0.8f;

        // ── Left-controller input ─────────────────────────────────────────────────
        public static bool TriggerJustPressed_L = false;
        public static bool TriggerJustReleased_L = false;
        public static bool TriggerIsHeld_L = false;
        public static bool FistJustPressed_L = false;
        public static bool FistIsHeld_L = false;
        public static bool RightClickJustPressed_L = false;
        public static bool LaserLeftIsActive_L = false;
        public static bool GripIsHeld_L = false;
        public static bool GripJustPressed_L = false;
        public static bool GripJustReleased_L = false;
        public static float ControllerRollDelta_L = 0f;
        public static Vector3 HandPosition_L = Vector3.zero;
        public static GameObject HeldObject_L  = null;
        public static bool MenuIsOpen = false;

        // Hand-scoped pickup candidates gathered each frame by VRHandPickupProximityAction.
        public static GameObject PickupCandidate_R = null;
        public static GameObject PickupCandidate_L = null;

        private static readonly Dictionary<PlayMakerFSM, bool> PickupOwnerByFsm
            = new Dictionary<PlayMakerFSM, bool>();

        // Track EQUIP auto-fire so drop can send FINISHED to the same FSM.
        private static readonly HashSet<PlayMakerFSM> _equipFinishFsms
            = new HashSet<PlayMakerFSM>();
        private static readonly HashSet<PlayMakerFSM> _equipAlreadyFiredFsms
            = new HashSet<PlayMakerFSM>();

        public static bool LastPickupHandIsRight = true; // simpler than HandSide enum

        public static void RegisterPickupOwner(PlayMakerFSM fsm, bool isRightHand)
        {
            LastPickupHandIsRight = isRightHand;
            if (fsm != null)
                PickupOwnerByFsm[fsm] = isRightHand;
        }

        public static bool TryGetPickupOwner(PlayMakerFSM fsm, out bool isRightHand)
        {
            if (fsm != null && PickupOwnerByFsm.TryGetValue(fsm, out isRightHand))
                return true;

            if (fsm != null)
            {
                bool rightMatch = IsRightTarget(fsm);
                bool leftMatch = IsLeftTarget(fsm);
                if (rightMatch && !leftMatch)
                {
                    isRightHand = true;
                    return true;
                }

                if (leftMatch && !rightMatch)
                {
                    isRightHand = false;
                    return true;
                }
            }

            isRightHand = LastPickupHandIsRight;
            return false;
        }

        public static void ClearPickupOwner(PlayMakerFSM fsm)
        {
            if (fsm != null)
                PickupOwnerByFsm.Remove(fsm);
        }

        public static void RegisterEquipFsm(PlayMakerFSM fsm)
        {
            if (fsm == null) return;
            _equipFinishFsms.Add(fsm);
        }

        public static bool CanFireEquipForFsm(PlayMakerFSM fsm)
        {
            if (fsm == null) return true;
            return !_equipAlreadyFiredFsms.Contains(fsm);
        }

        public static void MarkEquipFiredForFsm(PlayMakerFSM fsm)
        {
            if (fsm != null)
                _equipAlreadyFiredFsms.Add(fsm);
        }

        public static void TryFireEquipFinishedForDrop()
        {
            if (_equipFinishFsms.Count == 0) return;

            foreach (PlayMakerFSM equipFsm in _equipFinishFsms)
            {
                FireFsmEvent(equipFsm, "FINISHED");
                if (equipFsm != null)
                    _equipAlreadyFiredFsms.Remove(equipFsm);
            }

            _equipFinishFsms.Clear();
        }

        private static void FireFsmEvent(PlayMakerFSM fsm, string eventName)
        {
            if (fsm == null || string.IsNullOrEmpty(eventName)) return;
            if (fsm.Fsm == null) return;

            FsmEvent ev = FsmEvent.GetFsmEvent(eventName);
            if (ev == null) return;

            if (!fsm.Fsm.Started)
                fsm.Fsm.Start();

            fsm.Fsm.ProcessEvent(ev);
        }

        // ── Side-aware helpers (pick the right-hand or left-hand state based on which
        //    active-target slot the given FSM occupies this frame) ──────────────────
        private static bool IsRightTarget(PlayMakerFSM fsm)
        {
            return fsm != null && fsm == ActiveInteractionTarget;
        }

        private static bool IsLeftTarget(PlayMakerFSM fsm)
        {
            return fsm != null && fsm == ActiveInteractionTargetLeft;
        }

        private static bool ResolveBoolByTarget(PlayMakerFSM fsm, bool rightValue, bool leftValue)
        {
            bool rightMatch = IsRightTarget(fsm);
            bool leftMatch = IsLeftTarget(fsm);

            if (rightMatch && leftMatch)
            {
                bool isRight = ResolveIsRightHand(fsm, fsm != null ? fsm.gameObject : null);
                return isRight ? rightValue : leftValue;
            }
            if (rightMatch)
                return rightValue;
            if (leftMatch)
                return leftValue;
            return false;
        }

        private static bool HasRightInteractionIntent()
        {
            return TriggerJustPressed_R || TriggerIsHeld_R || FistJustPressed_R || FistIsHeld_R || GripIsHeld_R || RightClickJustPressed_R;
        }

        private static bool HasLeftInteractionIntent()
        {
            return TriggerJustPressed_L || TriggerIsHeld_L || FistJustPressed_L || FistIsHeld_L || GripIsHeld_L || RightClickJustPressed_L;
        }

        public static bool GetTriggerJustPressed(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, TriggerJustPressed_R, TriggerJustPressed_L);
        }
        public static bool GetTriggerJustReleased(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, TriggerJustReleased_R, TriggerJustReleased_L);
        }
        public static bool GetTriggerIsHeld(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, TriggerIsHeld_R, TriggerIsHeld_L);
        }
        public static bool GetFistJustPressed(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, FistJustPressed_R, FistJustPressed_L);
        }
        public static bool GetFistIsHeld(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, FistIsHeld_R, FistIsHeld_L);
        }
        public static bool GetRightClickJustPressed(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, RightClickJustPressed_R, RightClickJustPressed_L);
        }
        public static bool GetGripIsHeld(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, GripIsHeld_R, GripIsHeld_L);
        }
        public static bool GetGripJustPressed(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, GripJustPressed_R, GripJustPressed_L);
        }
        public static bool GetGripJustReleased(PlayMakerFSM fsm)
        {
            return ResolveBoolByTarget(fsm, GripJustReleased_R, GripJustReleased_L);
        }

        public static bool ResolveIsRightHand(PlayMakerFSM fsm, GameObject targetObject)
        {
            if (fsm != null)
            {
                bool rightMatch = IsRightTarget(fsm);
                bool leftMatch = IsLeftTarget(fsm);

                if (rightMatch && !leftMatch) return true;
                if (leftMatch && !rightMatch) return false;

                if (rightMatch && leftMatch)
                {
                    bool rightIntent = HasRightInteractionIntent();
                    bool leftIntent = HasLeftInteractionIntent();
                    if (rightIntent && !leftIntent) return true;
                    if (leftIntent && !rightIntent) return false;
                }

                // If both slots point at the same FSM, continue with hierarchy/distance
                // heuristics instead of forcing right-hand ownership.
            }

            Transform rightHand = VRHand.RightHandInstance != null
                ? VRHand.RightHandInstance.transform
                : VRProximitySphereDebug.RightHandTransform;
            Transform leftHand = VRHand.LeftHandInstance != null
                ? VRHand.LeftHandInstance.transform
                : null;

            if (targetObject != null)
            {
                Transform targetTransform = targetObject.transform;
                if (IsTransformUnder(targetTransform, rightHand)) return true;
                if (IsTransformUnder(targetTransform, leftHand)) return false;

                if (rightHand != null && leftHand != null)
                {
                    float rightDist = GetClosestSurfaceDistanceToObject(targetObject, rightHand.position);
                    float leftDist = GetClosestSurfaceDistanceToObject(targetObject, leftHand.position);
                    if (Mathf.Abs(rightDist - leftDist) > 0.0001f)
                        return rightDist <= leftDist;
                }
            }

            if (rightHand != null && leftHand == null) return true;
            if (leftHand != null && rightHand == null) return false;

            return LastPickupHandIsRight;
        }

        private static bool IsTransformUnder(Transform child, Transform parent)
        {
            if (child == null || parent == null) return false;
            while (child != null)
            {
                if (child == parent) return true;
                child = child.parent;
            }
            return false;
        }
        public static float GetControllerRollDelta(PlayMakerFSM fsm)
        {
            bool rightMatch = IsRightTarget(fsm);
            bool leftMatch = IsLeftTarget(fsm);

            if (rightMatch && leftMatch)
                return ResolveIsRightHand(fsm, fsm != null ? fsm.gameObject : null)
                    ? ControllerRollDelta_R
                    : ControllerRollDelta_L;
            if (rightMatch) return ControllerRollDelta_R;
            if (leftMatch) return ControllerRollDelta_L;
            return 0f; 
        }

        // ── Legacy nearest-object (still used by VRHandPickupProximityAction) ────
        public static GameObject NearestRightHandObject = null;
        public static GameObject NearestLeftHandObject = null;

         // Dispatcher targets, set once per frame by UpdateDispatcher.
        /// <summary>
        /// The single patched Use-FSM closest to the right hand this frame.
        /// Only the VRTriggerPressAction/VRTriggerHoldAction/VRProximityActivateAction
        /// whose targetFsm matches this reference is allowed to fire.
        /// </summary>
        public static PlayMakerFSM ActiveInteractionTarget = null;
        public static float ActiveInteractionDist = float.MaxValue;
        public static PlayMakerFSM ActiveInteractionTargetLeft = null;
        public static float ActiveInteractionDistLeft = float.MaxValue;
        private static PlayMakerFSM _lastActiveInteractionTargetRight = null;
        private static PlayMakerFSM _lastActiveInteractionTargetLeft = null;
        private static string _lastSelectionSourceRight = string.Empty;
        private static string _lastSelectionSourceLeft = string.Empty;
        private static string _lastNoSelectableReasonRight = string.Empty;
        private static string _lastNoSelectableReasonLeft = string.Empty;
        private static int _dispatcherMismatchLogCooldownRight = 0;
        private static int _dispatcherMismatchLogCooldownLeft = 0;
        private static int _multiCandidateSelectionLogCooldownRight = 0;
        private static int _multiCandidateSelectionLogCooldownLeft = 0;
        private static int _registryPruneCooldownFrames = 0;
        private static int _dispatcherSkipCounterRight = 0;
        private static int _dispatcherSkipCounterLeft = 0;
        private static readonly Collider[] _broadPhaseBuffer = new Collider[128];
        private static readonly HashSet<GameObject> _activateBroadPhaseKeysRight = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _activateBroadPhaseKeysLeft = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _leaveBroadPhaseKeysRight = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _leaveBroadPhaseKeysLeft = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _gripBroadPhaseKeysRight = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> _gripBroadPhaseKeysLeft = new HashSet<GameObject>();
        private static bool _loggedBroadPhaseBufferSaturated = false;
        private const int DispatcherMismatchLogCooldownFrames = 30;
        private const int MultiCandidateSelectionLogCooldownFrames = 15;
        private const int RegistryPruneIntervalFrames = 120;
        private const int IDLE_SCAN_INTERVAL = 4;

        /// <summary>
        /// The single patched knob-FSM closest to the right hand this frame.
        /// Only the VRKnobTwistAction whose targetFsm matches this is active.
        /// </summary>

        // ── Dispatcher scan radius ─────────────────────────────────────────────────
        private const float BroadPhaseRadius = 2.0f;
        private const float InteractionRadius = 0.15f;  // shrink if still too broad
        private const float HeldInteractionRadius = 0.45f;
        private const float AssemblyInteractionRadius = 1.0f;
        private const float KnobRadius = 0.15f;
        private const float CenterPriorityWeight = 1000f;
        private const float CandidateScoreTieEpsilon = 0.0001f;

        /// <summary>
        /// Call once per frame from VRHand.Update() (right hand only).
        /// Scans nearby colliders, resolves their FSMs against the patcher's
        /// patched sets, and sets ActiveInteractionTarget / ActiveKnobTarget.
        /// </summary>
        public static void UpdateDispatcher(Vector3 handPos, bool isRightHand = true)
        {
            bool hasTarget = isRightHand
                ? ActiveInteractionTarget != null
                : ActiveInteractionTargetLeft != null;

            if (!hasTarget)
            {
                if (isRightHand)
                {
                    _dispatcherSkipCounterRight++;
                    if (_dispatcherSkipCounterRight < IDLE_SCAN_INTERVAL)
                        return;
                    _dispatcherSkipCounterRight = 0;
                }
                else
                {
                    _dispatcherSkipCounterLeft++;
                    if (_dispatcherSkipCounterLeft < IDLE_SCAN_INTERVAL)
                        return;
                    _dispatcherSkipCounterLeft = 0;
                }
            }
            else
            {
                if (isRightHand)
                    _dispatcherSkipCounterRight = 0;
                else
                    _dispatcherSkipCounterLeft = 0;
            }

            int hitCount = OverlapSphereBroadPhase(handPos, BroadPhaseRadius, _broadPhaseBuffer);
            if (hitCount == _broadPhaseBuffer.Length && !_loggedBroadPhaseBufferSaturated)
            {
                _loggedBroadPhaseBufferSaturated = true;
                PlayMakerHelpers.DebugLog("[PERF] BroadPhase buffer saturated - consider increasing size");
            }

            if (isRightHand)
            {
                if (_dispatcherMismatchLogCooldownRight > 0)
                    _dispatcherMismatchLogCooldownRight--;
                if (_multiCandidateSelectionLogCooldownRight > 0)
                    _multiCandidateSelectionLogCooldownRight--;
            }
            else
            {
                if (_dispatcherMismatchLogCooldownLeft > 0)
                    _dispatcherMismatchLogCooldownLeft--;
                if (_multiCandidateSelectionLogCooldownLeft > 0)
                    _multiCandidateSelectionLogCooldownLeft--;
            }

            TickRegistryPrune(isRightHand);

            PlayMakerFSM stickyFsm = null;
            GameObject closestGO;
            float closestDist;
            string selectionSource = "none";
            string noSelectableReason = string.Empty;

            // Held-interactable objects (for example the fuel nozzle) take priority
            // while they are physically attached to this hand.
            FindClosestHeldInteractableObject(
                handPos,
                isRightHand,
                HeldInteractionRadius,
                out closestGO,
                out closestDist);
            if (closestGO != null)
                selectionSource = "held-interactable";

            if (closestGO == null)
            {
                FindClosestRegisteredObjectFromBroadPhase(
                    hitCount,
                    handPos,
                    false,
                    false,
                    isRightHand,
                    InteractionRadius,
                    out closestGO,
                    out closestDist);
                if (closestGO != null)
                    selectionSource = "registered";

                // Keep normal non-held interactions highest priority. If none are in range,
                // fall back to a held target so held objects (for example fuel nozzle)
                // remain trigger-interactable while attached to the player rig.
                if (closestGO == null)
                {
                    if (TryResolveStickyHeldTarget(
                        handPos,
                        isRightHand,
                        out stickyFsm,
                        out closestGO,
                        out closestDist)
                        && closestGO != null)
                    {
                        selectionSource = AppendSelectionSource(selectionSource, "sticky-held");
                    }
                }

                if (closestGO == null)
                {
                    FindClosestRegisteredObjectFromBroadPhase(
                        hitCount,
                        handPos,
                        false,
                        true,
                        isRightHand,
                        HeldInteractionRadius,
                        out closestGO,
                        out closestDist);
                    if (closestGO != null)
                        selectionSource = AppendSelectionSource(selectionSource, "held-fallback");
                }

                // State-aware priority: if any GripRequired FSM is currently in an
                // actionable state (contains VRTriggerPressAction), it wins regardless
                // of distance. The game's own physics already confirmed it is ready.
                // This runs regardless of grip state so fuse boards etc. work too.
                GameObject stateDrivenGO = null;
                float stateDrivenDist = float.MaxValue;
                float stateDrivenScore = float.MaxValue;

                foreach (KeyValuePair<GameObject, PlayMakerFSM> kvp
                        in VRProximityRegistry.GripRequiredFsms)
                {
                    GameObject go = kvp.Key;
                    PlayMakerFSM fsm = kvp.Value;

                    if (go == null || fsm == null) continue;
                    if (IsHeldByHand(go)) continue;
                    if (go == HeldObject_R || go == HeldObject_L) continue;
                    if (fsm.Fsm == null || fsm.Fsm.ActiveState == null) continue;
                    string activeStateName = fsm.Fsm.ActiveState.Name.ToLower();
                    bool isInstallReadyState = activeStateName.Contains("near")
                                            || activeStateName.Contains("wait for assembly")
                                            || activeStateName.Contains("install 1")
                                            || activeStateName.Contains("assemble")
                                            || activeStateName.Contains("allow install");
                    if (!isInstallReadyState) continue;
                    // Check if current active state has an injectable trigger action
                    bool hasActionableState = false;
                    FsmStateAction[] activeActions = VRFsmPatcher.GetActionsRaw(fsm.Fsm.ActiveState);
                    if (activeActions == null) continue;
                    foreach (FsmStateAction a in activeActions)
                    {
                        if (a == null || !a.Enabled) continue;
                        if (a is VRTriggerPressAction || a is VRGrabTriggerAction)
                        {
                            hasActionableState = true;
                            break;
                        }
                    }
                    if (!hasActionableState) continue;

                    // Hand must still be within the interaction sphere
                    float dist = GetClosestSurfaceDistanceToObject(go, handPos);
                    if (dist >= AssemblyInteractionRadius) continue;

                    float score = GetInteractionPriorityScore(go, handPos);
                    if (!IsBetterCandidate(score, dist, stateDrivenScore, stateDrivenDist))
                        continue;

                    stateDrivenGO = go;
                    stateDrivenDist = dist;
                    stateDrivenScore = score;
                }

                if (stateDrivenGO != null)
                {
                    closestGO = stateDrivenGO;
                    closestDist = stateDrivenDist;
                    // Force FSM remap to the state-driven winner instead of keeping stale sticky-held FSM.
                    stickyFsm = null;
                    selectionSource = AppendSelectionSource(selectionSource, "grip-required-state");
                    // closestFsm resolved below via existing fallback path
                }
            }

            UpdateBroadPhaseActionFlags(
                hitCount,
                handPos,
                closestGO,
                isRightHand);

            // Write IsClosest to all registered actions (per-side to avoid overwriting
            // the other hand's result when both controllers run UpdateDispatcher each frame)
            foreach (KeyValuePair<GameObject, List<VRProximityActivateAction>> activateEntry in VRProximityRegistry.ActivateActions)
            {
                GameObject regGO = activateEntry.Key;
                List<VRProximityActivateAction> activateActions = activateEntry.Value;
                if (regGO == null || activateActions == null)
                    continue;

                bool isClosest = (regGO == closestGO);
                bool isInRange = false;
                foreach (VRProximityActivateAction a in activateActions)
                {
                    if (a == null) continue;

                    if (a.AllowInRangeActivation && !isInRange)
                        isInRange = IsObjectWithinHandProximity(regGO, handPos, isRightHand);

                    if (isRightHand)
                    {
                        a.IsClosestRight = isClosest;
                        a.IsInRangeRight = a.AllowInRangeActivation && isInRange;
                    }
                    else
                    {
                        a.IsClosestLeft = isClosest;
                        a.IsInRangeLeft = a.AllowInRangeActivation && isInRange;
                    }
                }
            }
            foreach (KeyValuePair<GameObject, List<VRProximityLeaveAction>> leaveEntry in VRProximityRegistry.LeaveActions)
            {
                GameObject regGO = leaveEntry.Key;
                List<VRProximityLeaveAction> leaveActions = leaveEntry.Value;
                if (regGO == null || leaveActions == null)
                    continue;

                bool isIt = (regGO == closestGO);
                bool isInRange = false;
                foreach (VRProximityLeaveAction a in leaveActions)
                {
                    if (a == null) continue;

                    if (a.AllowInRangeActivation && !isInRange)
                        isInRange = IsObjectWithinHandProximity(regGO, handPos, isRightHand);

                    if (isRightHand)
                    {
                        a.IsClosestRight = isIt;
                        a.IsInRangeRight = a.AllowInRangeActivation && isInRange;
                    }
                    else
                    {
                        a.IsClosestLeft = isIt;
                        a.IsInRangeLeft = a.AllowInRangeActivation && isInRange;
                    }
                }
            }
            // ── 3. Expose closest for trigger/grip actions ────────────────────────
            // Derive FSM from closestGO for ActiveInteractionTarget
            PlayMakerFSM closestFsm = stickyFsm;

            if (closestGO != null
                && closestFsm != null
                && closestFsm.gameObject != null
                && !IsRelated(closestFsm.gameObject, closestGO))
            {
                LogDispatcherMismatch(
                    isRightHand,
                    handPos,
                    "stale-sticky-fsm",
                    closestGO,
                    closestFsm,
                    selectionSource);

                closestFsm = null;
                noSelectableReason = string.Empty;
                selectionSource = AppendSelectionSource(selectionSource, "remap-after-mismatch");
            }

            if (closestGO != null)
            {
                if (closestFsm == null)
                {
                    string selectionDiagnostics;
                    TryGetFirstSelectableRegisteredFsm(
                        closestGO,
                        out closestFsm,
                        out noSelectableReason,
                        out selectionDiagnostics);
                    LogMultiCandidateSelection(
                        isRightHand,
                        handPos,
                        closestGO,
                        selectionDiagnostics);
                }
                // Fallback for GripRequired objects not in either registry
                if (closestFsm == null)
                {
                    PlayMakerFSM gripFsm;
                    if (VRProximityRegistry.GripRequiredFsms.TryGetValue(closestGO, out gripFsm)
                        && gripFsm != null)
                    {
                        closestFsm = gripFsm;
                        noSelectableReason = string.Empty;
                        selectionSource = AppendSelectionSource(selectionSource, "grip-required-map");
                    }
                }

                if (closestFsm == null)
                {
                    PlayMakerFSM heldFsm;
                    if (TryGetHeldInteractableFsm(closestGO, out heldFsm) && heldFsm != null)
                    {
                        closestFsm = heldFsm;
                        noSelectableReason = string.Empty;
                        selectionSource = AppendSelectionSource(selectionSource, "held-map");
                    }
                }

                if (closestFsm != null
                    && closestFsm.gameObject != null
                    && !IsRelated(closestFsm.gameObject, closestGO))
                {
                    LogDispatcherMismatch(
                        isRightHand,
                        handPos,
                        "final-fsm-go-mismatch",
                        closestGO,
                        closestFsm,
                        selectionSource);

                    closestFsm = null;
                    noSelectableReason = "resolved fsm unrelated to closestGO";
                    selectionSource = AppendSelectionSource(selectionSource, "forced-none");
                }
            }

            if (isRightHand)
            {
                ActiveInteractionTarget = closestFsm;
                ActiveInteractionDist   = closestDist;
            }
            else
            {
                ActiveInteractionTargetLeft = closestFsm;
                ActiveInteractionDistLeft   = closestDist;
            }

            LogDispatcherTargetChange(
                isRightHand,
                handPos,
                closestGO,
                closestDist,
                closestFsm,
                selectionSource,
                noSelectableReason);

            // ── 5. Periodic debug log ─────────────────────────────────────────────
            _dispatcherLogTimer++;
            if (_dispatcherLogTimer >= 90)
            {
                _dispatcherLogTimer = 0;
                string side    = isRightHand ? "R" : "L";
                string cur     = closestGO != null ? closestGO.name : "none";
                PlayMakerHelpers.DebugLog("[VRDispatcher:" + side + "] closest='" + cur
                    + "' dist=" + (closestDist < float.MaxValue ? closestDist.ToString("F3") : "n/a")
                    + " score=" + (closestGO != null ? GetInteractionPriorityScore(closestGO, handPos).ToString("F3") : "n/a")
                    + " source='" + selectionSource + "'"
                    + " fsm='" + DescribeFsmForLog(closestFsm) + "'"
                    + (!string.IsNullOrEmpty(noSelectableReason)
                        ? " noSelectable='" + noSelectableReason + "'"
                        : "")
                    + " activateReg=" + VRProximityRegistry.ActivateActions.Count
                    + " leaveReg="     + VRProximityRegistry.LeaveActions.Count
                    + " heldReg="      + VRProximityRegistry.HeldInteractableObjects.Count);
            }
        }

        private static void TickRegistryPrune(bool isRightHand)
        {
            if (!isRightHand && VRHand.RightHandInstance != null)
                return;

            if (_registryPruneCooldownFrames > 0)
            {
                _registryPruneCooldownFrames--;
                return;
            }

            _registryPruneCooldownFrames = RegistryPruneIntervalFrames;

            int activateKeysRemoved;
            int activateActionsRemoved;
            PruneActionRegistry(
                VRProximityRegistry.ActivateActions,
                out activateKeysRemoved,
                out activateActionsRemoved);

            int leaveKeysRemoved;
            int leaveActionsRemoved;
            PruneActionRegistry(
                VRProximityRegistry.LeaveActions,
                out leaveKeysRemoved,
                out leaveActionsRemoved);

            if (activateKeysRemoved == 0
                && activateActionsRemoved == 0
                && leaveKeysRemoved == 0
                && leaveActionsRemoved == 0)
            {
                return;
            }

            PlayMakerHelpers.DebugLog(
                "[VRDispatcher] Pruned stale registry entries"
                + " activateKeys=" + activateKeysRemoved
                + " activateActions=" + activateActionsRemoved
                + " leaveKeys=" + leaveKeysRemoved
                + " leaveActions=" + leaveActionsRemoved);
        }

        private static void PruneActionRegistry<TAction>(
            Dictionary<GameObject, List<TAction>> registry,
            out int removedKeys,
            out int removedActions)
            where TAction : class
        {
            removedKeys = 0;
            removedActions = 0;

            if (registry == null || registry.Count == 0)
                return;

            List<GameObject> keysToRemove = null;

            try
            {
                foreach (KeyValuePair<GameObject, List<TAction>> entry in registry)
                {
                    GameObject go = entry.Key;
                    List<TAction> actions = entry.Value;

                    if (go == null || actions == null)
                    {
                        if (keysToRemove == null)
                            keysToRemove = new List<GameObject>();
                        keysToRemove.Add(go);
                        continue;
                    }

                    for (int i = actions.Count - 1; i >= 0; i--)
                    {
                        if (actions[i] != null)
                            continue;

                        actions.RemoveAt(i);
                        removedActions++;
                    }

                    if (actions.Count == 0)
                    {
                        if (keysToRemove == null)
                            keysToRemove = new List<GameObject>();
                        keysToRemove.Add(go);
                    }
                }
            }
            catch (System.InvalidOperationException)
            {
                // Runtime FSM patching can mutate registries mid-prune; retry next tick.
                removedActions = 0;
                return;
            }

            if (keysToRemove == null)
                return;

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                if (registry.Remove(keysToRemove[i]))
                    removedKeys++;
            }
        }

        private static void FindClosestHeldInteractableObject(
            Vector3 handPos,
            bool isRightHand,
            float maxRadius,
            out GameObject closestGO,
            out float closestDist)
        {
            closestGO = null;
            closestDist = float.MaxValue;
            float closestScore = float.MaxValue;

            bool otherHand = !isRightHand;

            foreach (GameObject go in VRProximityRegistry.HeldInteractableObjects)
            {
                if (go == null) continue;

                // Important: activation is based on actual hand parenting, not on
                // HeldObject_R/L grip bookkeeping, so reparented objects work even
                // when no hold button is used.
                if (!IsHeldByHandSide(go, isRightHand)) continue;
                if (IsHeldByHandSide(go, otherHand)) continue;

                PlayMakerFSM heldFsm;
                if (!TryGetHeldInteractableFsm(go, out heldFsm)) continue;

                float dist = GetClosestSurfaceDistanceToObject(go, handPos);
                if (dist >= maxRadius)
                    continue;

                float score = GetInteractionPriorityScore(go, handPos);
                if (IsBetterCandidate(score, dist, closestScore, closestDist))
                {
                    closestScore = score;
                    closestDist = dist;
                    closestGO = go;
                }
            }
        }

        private static bool IsObjectWithinHandProximity(GameObject go, Vector3 handPos, bool isRightHand)
        {
            if (go == null) return false;

            if (IsHeldByHandSide(go, !isRightHand))
                return false;

            float dist = GetClosestSurfaceDistanceToObject(go, handPos);
            return dist < InteractionRadius;
        }

        private static bool TryGetHeldInteractableFsm(GameObject go, out PlayMakerFSM heldFsm)
        {
            heldFsm = null;
            if (go == null) return false;

            List<PlayMakerFSM> fsms;
            if (!VRProximityRegistry.HeldInteractableFsms.TryGetValue(go, out fsms) || fsms == null)
                return false;

            foreach (PlayMakerFSM fsm in fsms)
            {
                if (fsm == null || fsm.gameObject == null) continue;
                if (!ActiveStateHasTriggerAction(fsm)) continue;

                heldFsm = fsm;
                return true;
            }

            return false;
        }

        private static void FindClosestRegisteredObject(
            Vector3 handPos,
            bool gripRequiredOnly,
            bool includeHeldForHand,
            bool isRightHand,
            float maxRadius,
            out GameObject closestGO,
            out float closestDist)
        {
            int hitCount = OverlapSphereBroadPhase(handPos, BroadPhaseRadius, _broadPhaseBuffer);
            if (hitCount == _broadPhaseBuffer.Length && !_loggedBroadPhaseBufferSaturated)
            {
                _loggedBroadPhaseBufferSaturated = true;
                PlayMakerHelpers.DebugLog("[PERF] BroadPhase buffer saturated - consider increasing size");
            }

            FindClosestRegisteredObjectFromBroadPhase(
                hitCount,
                handPos,
                gripRequiredOnly,
                includeHeldForHand,
                isRightHand,
                maxRadius,
                out closestGO,
                out closestDist);
        }

        private static void FindClosestRegisteredObjectFromBroadPhase(
            int hitCount,
            Vector3 handPos,
            bool gripRequiredOnly,
            bool includeHeldForHand,
            bool isRightHand,
            float maxRadius,
            out GameObject closestGO,
            out float closestDist)
        {
            closestGO = null;
            closestDist = float.MaxValue;
            float closestScore = float.MaxValue;

            if (hitCount <= 0)
                return;

            if (gripRequiredOnly)
            {
                EvaluateBroadPhaseRegistryHits(
                    hitCount,
                    handPos,
                    VRProximityRegistry.GripRequiredObjects,
                    gripRequiredOnly,
                    includeHeldForHand,
                    isRightHand,
                    maxRadius,
                    ref closestGO,
                    ref closestDist,
                    ref closestScore);
                return;
            }

            EvaluateBroadPhaseRegistryHits(
                hitCount,
                handPos,
                VRProximityRegistry.ActivateActions,
                gripRequiredOnly,
                includeHeldForHand,
                isRightHand,
                maxRadius,
                ref closestGO,
                ref closestDist,
                ref closestScore);

            EvaluateBroadPhaseRegistryHits(
                hitCount,
                handPos,
                VRProximityRegistry.LeaveActions,
                gripRequiredOnly,
                includeHeldForHand,
                isRightHand,
                maxRadius,
                ref closestGO,
                ref closestDist,
                ref closestScore);
        }

        private static void UpdateBroadPhaseActionFlags(
            int hitCount,
            Vector3 handPos,
            GameObject closestGO,
            bool isRightHand)
        {
            if (isRightHand)
            {
                UpdateBroadPhaseActionFlagsForRegistry(
                    hitCount,
                    handPos,
                    closestGO,
                    isRightHand,
                    VRProximityRegistry.ActivateActions,
                    _activateBroadPhaseKeysRight,
                    _activateBroadPhaseKeysLeft);

                UpdateBroadPhaseActionFlagsForRegistry(
                    hitCount,
                    handPos,
                    closestGO,
                    isRightHand,
                    VRProximityRegistry.LeaveActions,
                    _leaveBroadPhaseKeysRight,
                    _leaveBroadPhaseKeysLeft);
                return;
            }

            UpdateBroadPhaseActionFlagsForRegistry(
                hitCount,
                handPos,
                closestGO,
                isRightHand,
                VRProximityRegistry.ActivateActions,
                _activateBroadPhaseKeysLeft,
                _activateBroadPhaseKeysRight);

            UpdateBroadPhaseActionFlagsForRegistry(
                hitCount,
                handPos,
                closestGO,
                isRightHand,
                VRProximityRegistry.LeaveActions,
                _leaveBroadPhaseKeysLeft,
                _leaveBroadPhaseKeysRight);
        }

        private static void UpdateBroadPhaseActionFlagsForRegistry<TAction>(
            int hitCount,
            Vector3 handPos,
            GameObject closestGO,
            bool isRightHand,
            Dictionary<GameObject, List<TAction>> registry,
            HashSet<GameObject> currentKeys,
            HashSet<GameObject> previousKeys)
            where TAction : class
        {
            if (registry == null || registry.Count == 0)
            {
                ClearBroadPhaseActionFlags(previousKeys, registry, isRightHand, null);
                currentKeys.Clear();
                previousKeys.Clear();
                return;
            }

            currentKeys.Clear();

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _broadPhaseBuffer[i];
                if (hit == null)
                    continue;

                GameObject regGO;
                if (!TryResolveBroadPhaseRegistryObject(hit, registry, out regGO))
                    continue;

                if (regGO == null || !currentKeys.Add(regGO))
                    continue;

                List<TAction> actions;
                if (!registry.TryGetValue(regGO, out actions) || actions == null)
                    continue;

                bool isClosest = regGO == closestGO;
                bool isInRange = false;
                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    TAction action = actions[actionIndex];
                    if (action == null)
                        continue;

                    if (!isInRange && IsAllowInRangeActivation(action))
                        isInRange = IsObjectWithinHandProximity(regGO, handPos, isRightHand);

                    SetActionFlags(action, isRightHand, isClosest, isInRange);
                }
            }

            ClearBroadPhaseActionFlags(previousKeys, registry, isRightHand, currentKeys);
            previousKeys.Clear();
            previousKeys.UnionWith(currentKeys);
        }

        private static void ClearBroadPhaseActionFlags<TAction>(
            HashSet<GameObject> previousKeys,
            Dictionary<GameObject, List<TAction>> registry,
            bool isRightHand,
            HashSet<GameObject> currentKeys)
            where TAction : class
        {
            if (previousKeys == null || previousKeys.Count == 0 || registry == null || registry.Count == 0)
                return;

            foreach (GameObject go in previousKeys)
            {
                if (go == null)
                    continue;

                if (currentKeys != null && currentKeys.Contains(go))
                    continue;

                List<TAction> actions;
                if (!registry.TryGetValue(go, out actions) || actions == null)
                    continue;

                for (int i = 0; i < actions.Count; i++)
                {
                    TAction action = actions[i];
                    if (action == null)
                        continue;

                    SetActionFlags(action, isRightHand, false, false);
                }
            }
        }

        private static bool IsAllowInRangeActivation<TAction>(TAction action)
            where TAction : class
        {
            if (action is VRProximityActivateAction activateAction)
                return activateAction.AllowInRangeActivation;

            if (action is VRProximityLeaveAction leaveAction)
                return leaveAction.AllowInRangeActivation;

            return false;
        }

        private static void SetActionFlags<TAction>(TAction action, bool isRightHand, bool isClosest, bool isInRange)
            where TAction : class
        {
            if (action is VRProximityActivateAction activateAction)
            {
                if (isRightHand)
                {
                    activateAction.IsClosestRight = isClosest;
                    activateAction.IsInRangeRight = activateAction.AllowInRangeActivation && isInRange;
                }
                else
                {
                    activateAction.IsClosestLeft = isClosest;
                    activateAction.IsInRangeLeft = activateAction.AllowInRangeActivation && isInRange;
                }

                return;
            }

            if (action is VRProximityLeaveAction leaveAction)
            {
                if (isRightHand)
                {
                    leaveAction.IsClosestRight = isClosest;
                    leaveAction.IsInRangeRight = leaveAction.AllowInRangeActivation && isInRange;
                }
                else
                {
                    leaveAction.IsClosestLeft = isClosest;
                    leaveAction.IsInRangeLeft = leaveAction.AllowInRangeActivation && isInRange;
                }
            }
        }

        private static void FindClosestGripRequiredStateFromBroadPhase(
            int hitCount,
            Vector3 handPos,
            bool isRightHand,
            ref GameObject stateDrivenGO,
            ref float stateDrivenDist,
            ref float stateDrivenScore)
        {
            HashSet<GameObject> candidateKeys = isRightHand ? _gripBroadPhaseKeysRight : _gripBroadPhaseKeysLeft;
            candidateKeys.Clear();

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _broadPhaseBuffer[i];
                if (hit == null)
                    continue;

                GameObject go;
                if (!TryResolveBroadPhaseRegistryObject(hit, VRProximityRegistry.GripRequiredFsms, out go))
                    continue;

                if (go == null || !candidateKeys.Add(go))
                    continue;
                if (IsHeldByHand(go) || go == HeldObject_R || go == HeldObject_L)
                    continue;

                PlayMakerFSM fsm;
                if (!VRProximityRegistry.GripRequiredFsms.TryGetValue(go, out fsm) || fsm == null)
                    continue;

                if (fsm.Fsm == null || fsm.Fsm.ActiveState == null)
                    continue;

                string activeStateName = fsm.Fsm.ActiveState.Name.ToLower();
                bool isInstallReadyState = activeStateName.Contains("near")
                                        || activeStateName.Contains("wait for assembly")
                                        || activeStateName.Contains("install 1")
                                        || activeStateName.Contains("assemble")
                                        || activeStateName.Contains("allow install");
                if (!isInstallReadyState)
                    continue;

                bool hasActionableState = false;
                FsmStateAction[] activeActions = VRFsmPatcher.GetActionsRaw(fsm.Fsm.ActiveState);
                if (activeActions == null)
                    continue;

                foreach (FsmStateAction a in activeActions)
                {
                    if (a == null || !a.Enabled)
                        continue;

                    if (a is VRTriggerPressAction || a is VRGrabTriggerAction)
                    {
                        hasActionableState = true;
                        break;
                    }
                }

                if (!hasActionableState)
                    continue;

                float dist = GetClosestSurfaceDistanceToObject(go, handPos);
                if (dist >= AssemblyInteractionRadius)
                    continue;

                float score = GetInteractionPriorityScore(go, handPos);
                if (!IsBetterCandidate(score, dist, stateDrivenScore, stateDrivenDist))
                    continue;

                stateDrivenGO = go;
                stateDrivenDist = dist;
                stateDrivenScore = score;
            }
        }

        private static int OverlapSphereBroadPhase(Vector3 position, float radius, Collider[] results)
        {
            if (results == null || results.Length == 0)
                return 0;

            return PhysicsCompat.OverlapSphereNonAlloc(position, radius, results, PhysicsCompat.QueryTriggerCollide);
        }

        private static OverlapSphereNonAllocWithMaskDelegate CreateOverlapSphereNonAllocWithMaskDelegate()
        {
            System.Reflection.MethodInfo method = typeof(Physics).GetMethod(
                "OverlapSphereNonAlloc",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new System.Type[] { typeof(Vector3), typeof(float), typeof(Collider[]), typeof(int) },
                null);

            if (method == null)
                return null;

            try
            {
                return (OverlapSphereNonAllocWithMaskDelegate)System.Delegate.CreateDelegate(
                    typeof(OverlapSphereNonAllocWithMaskDelegate),
                    method);
            }
            catch
            {
                return null;
            }
        }

        private static void EvaluateBroadPhaseRegistryHits<TAction>(
            int hitCount,
            Vector3 handPos,
            Dictionary<GameObject, List<TAction>> registry,
            bool gripRequiredOnly,
            bool includeHeldForHand,
            bool isRightHand,
            float maxRadius,
            ref GameObject closestGO,
            ref float closestDist,
            ref float closestScore)
            where TAction : class
        {
            if (registry == null || registry.Count == 0)
                return;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _broadPhaseBuffer[i];
                GameObject regGO;
                if (!TryResolveBroadPhaseRegistryObject(hit, registry, out regGO))
                    continue;

                if (!CanConsiderRegistryObject(regGO, gripRequiredOnly, includeHeldForHand, isRightHand))
                    continue;

                float dist = GetClosestSurfaceDistanceToObject(regGO, handPos);
                if (dist >= maxRadius)
                    continue;

                float score = GetInteractionPriorityScore(regGO, handPos);
                if (IsBetterCandidate(score, dist, closestScore, closestDist))
                {
                    closestScore = score;
                    closestDist = dist;
                    closestGO = regGO;
                }
            }
        }

        private static void EvaluateBroadPhaseRegistryHits(
            int hitCount,
            Vector3 handPos,
            HashSet<GameObject> registry,
            bool gripRequiredOnly,
            bool includeHeldForHand,
            bool isRightHand,
            float maxRadius,
            ref GameObject closestGO,
            ref float closestDist,
            ref float closestScore)
        {
            if (registry == null || registry.Count == 0)
                return;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _broadPhaseBuffer[i];
                GameObject regGO;
                if (!TryResolveBroadPhaseRegistryObject(hit, registry, out regGO))
                    continue;

                if (!CanConsiderRegistryObject(regGO, gripRequiredOnly, includeHeldForHand, isRightHand))
                    continue;

                float dist = GetClosestSurfaceDistanceToObject(regGO, handPos);
                if (dist >= maxRadius)
                    continue;

                float score = GetInteractionPriorityScore(regGO, handPos);
                if (IsBetterCandidate(score, dist, closestScore, closestDist))
                {
                    closestScore = score;
                    closestDist = dist;
                    closestGO = regGO;
                }
            }
        }

        private static bool TryResolveBroadPhaseRegistryObject<TValue>(
            Collider collider,
            Dictionary<GameObject, TValue> registry,
            out GameObject registryGO)
        {
            registryGO = null;
            if (collider == null || registry == null || registry.Count == 0)
                return false;

            GameObject go = collider.gameObject;
            if (TryResolveBroadPhaseRegistryObject(go, registry, out registryGO))
                return true;

            Rigidbody attachedRigidbody = collider.attachedRigidbody;
            if (attachedRigidbody != null && attachedRigidbody.gameObject != null)
            {
                if (attachedRigidbody.gameObject != go
                    && TryResolveBroadPhaseRegistryObject(attachedRigidbody.gameObject, registry, out registryGO))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveBroadPhaseRegistryObject<TValue>(
            GameObject go,
            Dictionary<GameObject, TValue> registry,
            out GameObject registryGO)
        {
            registryGO = null;
            if (go == null || registry == null || registry.Count == 0)
                return false;

            Transform t = go.transform;
            int depth = 0;
            while (t != null && depth <= 4)
            {
                GameObject candidate = t.gameObject;
                if (candidate != null && registry.ContainsKey(candidate))
                {
                    registryGO = candidate;
                    return true;
                }

                t = t.parent;
                depth++;
            }

            return false;
        }

        private static bool TryResolveBroadPhaseRegistryObject(
            Collider collider,
            HashSet<GameObject> registry,
            out GameObject registryGO)
        {
            registryGO = null;
            if (collider == null || registry == null || registry.Count == 0)
                return false;

            GameObject go = collider.gameObject;
            if (TryResolveBroadPhaseRegistryObject(go, registry, out registryGO))
                return true;

            Rigidbody attachedRigidbody = collider.attachedRigidbody;
            if (attachedRigidbody != null && attachedRigidbody.gameObject != null)
            {
                if (attachedRigidbody.gameObject != go
                    && TryResolveBroadPhaseRegistryObject(attachedRigidbody.gameObject, registry, out registryGO))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveBroadPhaseRegistryObject(
            GameObject go,
            HashSet<GameObject> registry,
            out GameObject registryGO)
        {
            registryGO = null;
            if (go == null || registry == null || registry.Count == 0)
                return false;

            Transform t = go.transform;
            int depth = 0;
            while (t != null && depth <= 4)
            {
                GameObject candidate = t.gameObject;
                if (candidate != null && registry.Contains(candidate))
                {
                    registryGO = candidate;
                    return true;
                }

                t = t.parent;
                depth++;
            }

            return false;
        }

        private static bool IsBetterCandidate(float score, float dist, float bestScore, float bestDist)
        {
            if (score + CandidateScoreTieEpsilon < bestScore)
                return true;

            if (Mathf.Abs(score - bestScore) <= CandidateScoreTieEpsilon
                && dist + CandidateScoreTieEpsilon < bestDist)
                return true;

            return false;
        }

        private static bool CanConsiderRegistryObject(
            GameObject go,
            bool gripRequiredOnly,
            bool includeHeldForHand,
            bool isRightHand)
        {
            if (go == null) return false;
            if (gripRequiredOnly && !VRProximityRegistry.GripRequiredObjects.Contains(go)) return false;
            if (!gripRequiredOnly && !HasSelectableRegisteredFsm(go)) return false;

            bool heldByRight = (go == HeldObject_R) || IsHeldByHandSide(go, true);
            bool heldByLeft = (go == HeldObject_L) || IsHeldByHandSide(go, false);

            bool heldByThisHand = isRightHand ? heldByRight : heldByLeft;
            bool heldByOtherHand = isRightHand ? heldByLeft : heldByRight;

            if (heldByOtherHand) return false;

            if (includeHeldForHand)
                return heldByThisHand;

            return !heldByThisHand;
        }

        private static bool IsSelectableInteractionFsm(PlayMakerFSM fsm)
        {
            if (fsm == null) return false;
            if (!VRProximityRegistry.NonSelectableInteractionFsms.Contains(fsm)) return true;

            List<string> stateFilters;
            if (!VRProximityRegistry.NonSelectableInteractionStateNameContains.TryGetValue(fsm, out stateFilters)
                || stateFilters == null
                || stateFilters.Count == 0)
                return false; // no filters means "always excluded" for this FSM

            string activeStateName = null;
            try
            {
                activeStateName = (fsm.Fsm != null && fsm.Fsm.ActiveState != null)
                    ? fsm.Fsm.ActiveState.Name
                    : fsm.ActiveStateName;
            }
            catch { }

            if (string.IsNullOrEmpty(activeStateName))
                return false;

            // Exclude only when the current state matches one of the configured filters.
            return !AnyContainsCI(activeStateName, stateFilters);
        }

        private static bool AnyContainsCI(string haystack, List<string> needles)
        {
            if (string.IsNullOrEmpty(haystack) || needles == null || needles.Count == 0)
                return false;

            string h = haystack.ToLower();
            foreach (string n in needles)
            {
                if (string.IsNullOrEmpty(n)) continue;

                // Prefix '=' means exact state-name match (case-insensitive).
                // Without '=', matching remains substring-based for compatibility.
                string needle = n;
                bool exact = needle.StartsWith("=");
                if (exact) needle = needle.Substring(1);
                if (string.IsNullOrEmpty(needle)) continue;

                if (exact)
                {
                    if (string.Equals(haystack, needle, System.StringComparison.OrdinalIgnoreCase))
                        return true;
                    continue;
                }

                if (h.Contains(needle.ToLower())) return true;
            }
            return false;
        }

        private sealed class SelectableRegisteredFsmCandidate
        {
            public PlayMakerFSM Fsm;
            public int ActivateRefs;
            public int LeaveRefs;
            public bool HasAnyTriggerAction;
            public bool HasPressIntentAction;
        }

        private static bool TryGetFirstSelectableRegisteredFsm(GameObject go, out PlayMakerFSM fsm)
        {
            string unusedReason;
            string unusedDiagnostics;
            return TryGetFirstSelectableRegisteredFsm(go, out fsm, out unusedReason, out unusedDiagnostics);
        }

        private static bool TryGetFirstSelectableRegisteredFsm(
            GameObject go,
            out PlayMakerFSM fsm,
            out string reason)
        {
            string unusedDiagnostics;
            return TryGetFirstSelectableRegisteredFsm(go, out fsm, out reason, out unusedDiagnostics);
        }

        private static bool TryGetFirstSelectableRegisteredFsm(
            GameObject go,
            out PlayMakerFSM fsm,
            out string reason,
            out string selectionDiagnostics)
        {
            fsm = null;
            reason = string.Empty;
            selectionDiagnostics = string.Empty;
            if (go == null)
            {
                reason = "target object is null";
                return false;
            }

            int candidateRefs = 0;
            int nullTargetRefs = 0;
            int filteredRefs = 0;
            string firstFiltered = null;
            List<SelectableRegisteredFsmCandidate> candidates
                = new List<SelectableRegisteredFsmCandidate>();

            List<VRProximityActivateAction> acts;
            if (VRProximityRegistry.ActivateActions.TryGetValue(go, out acts) && acts != null)
            {
                foreach (VRProximityActivateAction act in acts)
                {
                    if (act == null) continue;
                    candidateRefs++;
                    PlayMakerFSM targetFsm = act.targetFsm;
                    if (targetFsm == null)
                    {
                        nullTargetRefs++;
                        continue;
                    }
                    if (!IsSelectableInteractionFsm(targetFsm))
                    {
                        filteredRefs++;
                        if (firstFiltered == null)
                            firstFiltered = DescribeFsmForLog(targetFsm);
                        continue;
                    }

                    SelectableRegisteredFsmCandidate candidate
                        = GetOrCreateSelectableRegisteredCandidate(candidates, targetFsm);
                    candidate.ActivateRefs++;

                    if (!candidate.HasAnyTriggerAction)
                        candidate.HasAnyTriggerAction = ActiveStateHasTriggerAction(targetFsm);
                    if (!candidate.HasPressIntentAction)
                        candidate.HasPressIntentAction = ActiveStateHasPressIntentAction(targetFsm);
                }
            }

            List<VRProximityLeaveAction> leaveActs;
            if (VRProximityRegistry.LeaveActions.TryGetValue(go, out leaveActs) && leaveActs != null)
            {
                foreach (VRProximityLeaveAction act in leaveActs)
                {
                    if (act == null) continue;
                    candidateRefs++;
                    PlayMakerFSM targetFsm = act.targetFsm;
                    if (targetFsm == null)
                    {
                        nullTargetRefs++;
                        continue;
                    }
                    if (!IsSelectableInteractionFsm(targetFsm))
                    {
                        filteredRefs++;
                        if (firstFiltered == null)
                            firstFiltered = DescribeFsmForLog(targetFsm);
                        continue;
                    }

                    SelectableRegisteredFsmCandidate candidate
                        = GetOrCreateSelectableRegisteredCandidate(candidates, targetFsm);
                    candidate.LeaveRefs++;

                    if (!candidate.HasAnyTriggerAction)
                        candidate.HasAnyTriggerAction = ActiveStateHasTriggerAction(targetFsm);
                    if (!candidate.HasPressIntentAction)
                        candidate.HasPressIntentAction = ActiveStateHasPressIntentAction(targetFsm);
                }
            }

            if (candidates.Count > 0)
            {
                SelectableRegisteredFsmCandidate selected = candidates[0];
                for (int i = 1; i < candidates.Count; i++)
                {
                    if (CompareSelectableRegisteredCandidates(candidates[i], selected) < 0)
                        selected = candidates[i];
                }

                fsm = selected.Fsm;

                if (candidates.Count > 1)
                {
                    string details = string.Empty;
                    foreach (SelectableRegisteredFsmCandidate candidate in candidates)
                    {
                        if (details.Length > 0) details += " | ";
                        details += (candidate == selected ? "*" : string.Empty)
                            + DescribeSelectableRegisteredCandidateForLog(candidate);
                    }

                    selectionDiagnostics = "registry-candidates=" + candidates.Count
                        + " winner='" + DescribeFsmForLog(selected.Fsm) + "'"
                        + " details=[" + details + "]";
                }

                return true;
            }

            if (candidateRefs == 0)
            {
                reason = "no activate/leave actions registered for object";
            }
            else if (nullTargetRefs == candidateRefs)
            {
                reason = "all registered action target FSM refs are null";
            }
            else if (filteredRefs > 0 && (filteredRefs + nullTargetRefs) == candidateRefs)
            {
                reason = "all registered FSMs filtered by active-state exclusions"
                    + (firstFiltered != null ? " (first='" + firstFiltered + "')" : string.Empty);
            }
            else
            {
                reason = "registered actions found but no selectable FSM"
                    + " (refs=" + candidateRefs
                    + ", filtered=" + filteredRefs
                    + ", null=" + nullTargetRefs + ")";
            }

            return false;
        }

        private static SelectableRegisteredFsmCandidate GetOrCreateSelectableRegisteredCandidate(
            List<SelectableRegisteredFsmCandidate> candidates,
            PlayMakerFSM targetFsm)
        {
            foreach (SelectableRegisteredFsmCandidate candidate in candidates)
            {
                if (candidate.Fsm == targetFsm)
                    return candidate;
            }

            SelectableRegisteredFsmCandidate created = new SelectableRegisteredFsmCandidate
            {
                Fsm = targetFsm,
                ActivateRefs = 0,
                LeaveRefs = 0,
                HasAnyTriggerAction = false,
                HasPressIntentAction = false,
            };
            candidates.Add(created);
            return created;
        }

        private static int CompareSelectableRegisteredCandidates(
            SelectableRegisteredFsmCandidate left,
            SelectableRegisteredFsmCandidate right)
        {
            int leftScore = ComputeSelectableRegisteredCandidateScore(left);
            int rightScore = ComputeSelectableRegisteredCandidateScore(right);
            if (leftScore != rightScore)
                return leftScore < rightScore ? -1 : 1;

            string leftKey = GetStableSelectableRegisteredKey(left != null ? left.Fsm : null);
            string rightKey = GetStableSelectableRegisteredKey(right != null ? right.Fsm : null);
            int stableCompare = string.Compare(
                leftKey,
                rightKey,
                global::System.StringComparison.OrdinalIgnoreCase);
            if (stableCompare != 0)
                return stableCompare;

            int leftId = left != null && left.Fsm != null ? left.Fsm.GetInstanceID() : int.MaxValue;
            int rightId = right != null && right.Fsm != null ? right.Fsm.GetInstanceID() : int.MaxValue;
            if (leftId == rightId) return 0;
            return leftId < rightId ? -1 : 1;
        }

        private static int ComputeSelectableRegisteredCandidateScore(
            SelectableRegisteredFsmCandidate candidate)
        {
            if (candidate == null) return int.MaxValue;

            int score = 0;

            // Prefer active states that currently expose trigger-intended actions.
            if (!candidate.HasPressIntentAction)
                score += 1000;

            // Keep generic trigger-ready states above pure proximity-only states.
            if (!candidate.HasAnyTriggerAction)
                score += 100;

            // Prefer candidates sourced from activate registry over leave-only entries.
            if (candidate.ActivateRefs <= 0 && candidate.LeaveRefs > 0)
                score += 10;

            int activateBonus = candidate.ActivateRefs;
            if (activateBonus > 9) activateBonus = 9;
            score -= activateBonus;

            int totalRefs = candidate.ActivateRefs + candidate.LeaveRefs;
            if (totalRefs > 9) totalRefs = 9;
            score -= totalRefs;

            return score;
        }

        private static string DescribeSelectableRegisteredCandidateForLog(
            SelectableRegisteredFsmCandidate candidate)
        {
            if (candidate == null) return "null";

            return DescribeFsmForLog(candidate.Fsm)
                + "{score=" + ComputeSelectableRegisteredCandidateScore(candidate)
                + ",pressIntent=" + candidate.HasPressIntentAction
                + ",triggerAny=" + candidate.HasAnyTriggerAction
                + ",activateRefs=" + candidate.ActivateRefs
                + ",leaveRefs=" + candidate.LeaveRefs
                + "}";
        }

        private static string GetStableSelectableRegisteredKey(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject == null)
                return "~none";

            return (fsm.gameObject.name ?? string.Empty)
                + "."
                + (fsm.FsmName ?? string.Empty);
        }

        private static bool ActiveStateHasPressIntentAction(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.Fsm == null || fsm.Fsm.ActiveState == null) return false;
            FsmStateAction[] actions = VRFsmPatcher.GetActionsRaw(fsm.Fsm.ActiveState);
            if (actions == null) return false;

            foreach (FsmStateAction action in actions)
            {
                if (action == null || !action.Enabled) continue;
                if (action is VRTriggerPressAction
                    || action is VRTriggerHoldAction
                    || action is VRTriggerRightClickAction
                    || action is VRGrabTriggerAction
                    || action is VRFistPressAction)
                    return true;
            }

            return false;
        }

        private static bool HasSelectableRegisteredFsm(GameObject go)
        {
            PlayMakerFSM selectable;
            return TryGetFirstSelectableRegisteredFsm(go, out selectable);
        }

        private static string AppendSelectionSource(string existing, string extra)
        {
            if (string.IsNullOrEmpty(extra)) return existing;
            if (string.IsNullOrEmpty(existing) || existing == "none") return extra;
            if (existing.Contains(extra)) return existing;
            return existing + "+" + extra;
        }

        private static string DescribeFsmForLog(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject == null) return "none";
            string stateName = "null";
            try
            {
                stateName = fsm.Fsm != null && fsm.Fsm.ActiveState != null
                    ? fsm.Fsm.ActiveState.Name
                    : (fsm.ActiveStateName ?? "null");
            }
            catch
            {
                stateName = "error";
            }

            return fsm.gameObject.name + "." + fsm.FsmName + "@" + stateName;
        }

        private static string GetObjectPath(GameObject go)
        {
            if (go == null) return "none";

            string path = go.name;
            Transform t = go.transform.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }

            return path;
        }

        private static void LogDispatcherTargetChange(
            bool isRightHand,
            Vector3 handPos,
            GameObject closestGO,
            float closestDist,
            PlayMakerFSM closestFsm,
            string selectionSource,
            string noSelectableReason)
        {
            string source = string.IsNullOrEmpty(selectionSource) ? "none" : selectionSource;
            string noSelectable = noSelectableReason ?? string.Empty;

            PlayMakerFSM previousTarget = isRightHand
                ? _lastActiveInteractionTargetRight
                : _lastActiveInteractionTargetLeft;
            string previousSource = isRightHand
                ? _lastSelectionSourceRight
                : _lastSelectionSourceLeft;
            string previousNoSelectable = isRightHand
                ? _lastNoSelectableReasonRight
                : _lastNoSelectableReasonLeft;

            bool changed = previousTarget != closestFsm
                || !string.Equals(previousSource, source, global::System.StringComparison.Ordinal)
                || !string.Equals(previousNoSelectable, noSelectable, global::System.StringComparison.Ordinal);
            if (!changed) return;

            string side = isRightHand ? "R" : "L";
            string closestName = closestGO != null ? closestGO.name : "none";
            string distText = closestDist < float.MaxValue ? closestDist.ToString("F3") : "n/a";
            string scoreText = closestGO != null
                ? GetInteractionPriorityScore(closestGO, handPos).ToString("F3")
                : "n/a";

            PlayMakerHelpers.DebugLog("[VRDispatcher:" + side + "] targetChange"
                + " source='" + source + "'"
                + " closestGO='" + closestName + "'"
                + " path='" + GetObjectPath(closestGO) + "'"
                + " dist=" + distText
                + " score=" + scoreText
                + " fsm='" + DescribeFsmForLog(closestFsm) + "'"
                + (!string.IsNullOrEmpty(noSelectable)
                    ? " noSelectable='" + noSelectable + "'"
                    : string.Empty)
                + " handPos=" + handPos.ToString("F3"));

            if (isRightHand)
            {
                _lastActiveInteractionTargetRight = closestFsm;
                _lastSelectionSourceRight = source;
                _lastNoSelectableReasonRight = noSelectable;
            }
            else
            {
                _lastActiveInteractionTargetLeft = closestFsm;
                _lastSelectionSourceLeft = source;
                _lastNoSelectableReasonLeft = noSelectable;
            }
        }

        private static void LogMultiCandidateSelection(
            bool isRightHand,
            Vector3 handPos,
            GameObject closestGO,
            string selectionDiagnostics)
        {
            if (string.IsNullOrEmpty(selectionDiagnostics)) return;

            int cooldown = isRightHand
                ? _multiCandidateSelectionLogCooldownRight
                : _multiCandidateSelectionLogCooldownLeft;
            if (cooldown > 0) return;

            string side = isRightHand ? "R" : "L";
            PlayMakerHelpers.DebugLog("[VRDispatcher:" + side + "] selector"
                + " closestGO='" + (closestGO != null ? closestGO.name : "none") + "'"
                + " path='" + GetObjectPath(closestGO) + "'"
                + " " + selectionDiagnostics
                + " handPos=" + handPos.ToString("F3"));

            if (isRightHand)
                _multiCandidateSelectionLogCooldownRight = MultiCandidateSelectionLogCooldownFrames;
            else
                _multiCandidateSelectionLogCooldownLeft = MultiCandidateSelectionLogCooldownFrames;
        }

        private static void LogDispatcherMismatch(
            bool isRightHand,
            Vector3 handPos,
            string stage,
            GameObject closestGO,
            PlayMakerFSM closestFsm,
            string selectionSource)
        {
            int cooldown = isRightHand
                ? _dispatcherMismatchLogCooldownRight
                : _dispatcherMismatchLogCooldownLeft;
            if (cooldown > 0) return;

            string side = isRightHand ? "R" : "L";
            string source = string.IsNullOrEmpty(selectionSource) ? "none" : selectionSource;
            PlayMakerHelpers.DebugLog("[VRDispatcher:" + side + "] mismatch"
                + " stage='" + stage + "'"
                + " source='" + source + "'"
                + " closestGO='" + (closestGO != null ? closestGO.name : "none") + "'"
                + " closestPath='" + GetObjectPath(closestGO) + "'"
                + " fsm='" + DescribeFsmForLog(closestFsm) + "'"
                + " handPos=" + handPos.ToString("F3"));

            if (isRightHand)
                _dispatcherMismatchLogCooldownRight = DispatcherMismatchLogCooldownFrames;
            else
                _dispatcherMismatchLogCooldownLeft = DispatcherMismatchLogCooldownFrames;
        }

        private static bool TryResolveStickyHeldTarget(
            Vector3 handPos,
            bool isRightHand,
            out PlayMakerFSM stickyFsm,
            out GameObject stickyGo,
            out float stickyDist)
        {
            stickyFsm = isRightHand ? ActiveInteractionTarget : ActiveInteractionTargetLeft;
            stickyGo = null;
            stickyDist = float.MaxValue;

            if (stickyFsm == null || stickyFsm.gameObject == null) return false;
            if (!IsAttachedToPlayerRig(stickyFsm.gameObject)) return false;
            if (!ActiveStateHasTriggerAction(stickyFsm)) return false;

            stickyGo = stickyFsm.gameObject;
            stickyDist = GetClosestSurfaceDistanceToObject(stickyGo, handPos);
            if (stickyDist > HeldInteractionRadius) return false;
            return true;
        }

        private static bool IsAttachedToPlayerRig(GameObject go)
        {
            if (go == null) return false;
            Transform t = go.transform;
            while (t != null)
            {
                if (string.Equals(t.name, "PLAYER", System.StringComparison.OrdinalIgnoreCase))
                    return true;
                t = t.parent;
            }
            return false;
        }

        private static bool ActiveStateHasTriggerAction(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.Fsm == null || fsm.Fsm.ActiveState == null) return false;
            FsmStateAction[] actions = VRFsmPatcher.GetActionsRaw(fsm.Fsm.ActiveState);
            if (actions == null) return false;

            foreach (FsmStateAction action in actions)
            {
                if (action == null || !action.Enabled) continue;
                if (action is VRTriggerPressAction
                    || action is VRTriggerHoldAction
                    || action is VRTriggerRightClickAction
                    || action is VRGrabTriggerAction
                    || action is VRTriggerReleaseAction
                    || action is VRKnobTwistAction)
                    return true;
            }

            return false;
        }

        private static bool IsHeldByHandSide(GameObject go, bool isRightHand)
        {
            if (go == null) return false;
            Transform hand = isRightHand
                ? (VRHand.RightHandInstance != null ? VRHand.RightHandInstance.transform : VRProximitySphereDebug.RightHandTransform)
                : (VRHand.LeftHandInstance != null ? VRHand.LeftHandInstance.transform : null);
            if (hand == null) return false;

            Transform t = go.transform;
            while (t != null)
            {
                if (t == hand) return true;
                t = t.parent;
            }
            return false;
        }

        public static GameObject ResolveInteractionTargetRoot(GameObject go)
        {
            if (go == null) return null;

            Collider col = go.GetComponent<Collider>();
            if (col != null && col.attachedRigidbody != null)
                return col.attachedRigidbody.gameObject;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
                return rb.gameObject;

            return go;
        }

        public static float GetCenterPriorityScore(GameObject go, Vector3 handPos)
        {
            GameObject root = ResolveInteractionTargetRoot(go);
            if (root == null) return float.MaxValue;

            float centerDist = GetCenterDistanceToObject(root, handPos, true);
            float surfaceDist = GetClosestSurfaceDistanceToObject(root, handPos, true);
            return centerDist * CenterPriorityWeight + surfaceDist;
        }

        public static float GetInteractionPriorityScore(GameObject go, Vector3 handPos)
        {
            GameObject root = ResolveInteractionTargetRoot(go);
            if (root == null) return float.MaxValue;

            // Dispatcher interactions should rank by the closest collider surface,
            // not by object center distance.
            return GetClosestSurfaceDistanceToObject(root, handPos, true);
        }

        public static float GetClosestSurfaceDistanceToObject(GameObject go, Vector3 handPos)
        {
            return GetClosestSurfaceDistanceToObject(go, handPos, true);
        }

        private static float GetClosestDistanceToObject(GameObject go, Vector3 handPos)
        {
            return GetClosestSurfaceDistanceToObject(go, handPos, false);
        }

        private static float GetClosestSurfaceDistanceToObject(GameObject go, Vector3 handPos, bool ignoreTriggers)
        {
            if (go == null)
                return float.MaxValue;

            float distSqr = float.MaxValue;
            Collider[] cols = GetCachedColliders(go);
            foreach (Collider col in cols)
            {
                if (col == null) continue;
                if (ignoreTriggers && col.isTrigger) continue;

                Vector3 closest = col.ClosestPointOnBounds(handPos);
                float dSqr = (closest - handPos).sqrMagnitude;
                if (dSqr < distSqr) distSqr = dSqr;
            }

            // Fallback for objects with no colliders (e.g. VINP marker objects)
            if (distSqr == float.MaxValue)
                distSqr = (handPos - go.transform.position).sqrMagnitude;
            return Mathf.Sqrt(distSqr);
        }

        private static float GetCenterDistanceToObject(GameObject go, Vector3 handPos, bool ignoreTriggers)
        {
            if (go == null)
                return float.MaxValue;

            Vector3 center = go.transform.position;
            Collider[] cols = GetCachedColliders(go);
            bool hasBounds = false;
            Bounds mergedBounds = new Bounds(center, Vector3.zero);

            for (int i = 0; i < cols.Length; i++)
            {
                Collider col = cols[i];
                if (col == null) continue;
                if (ignoreTriggers && col.isTrigger) continue;

                if (!hasBounds)
                {
                    mergedBounds = col.bounds;
                    hasBounds = true;
                    continue;
                }

                mergedBounds.Encapsulate(col.bounds);
            }

            if (hasBounds)
                center = mergedBounds.center;

            return Vector3.Distance(handPos, center);
        }

        private static Collider[] GetCachedColliders(GameObject go)
        {
            Collider[] cols;
            if (_objectColliderCache.TryGetValue(go, out cols))
            {
                bool stale = cols == null;
                if (!stale)
                {
                    for (int i = 0; i < cols.Length; i++)
                    {
                        if (cols[i] == null)
                        {
                            stale = true;
                            break;
                        }
                    }
                }

                if (!stale)
                    return cols;
            }

            cols = go.GetComponentsInChildren<Collider>();
            if (cols == null)
                cols = _emptyColliders;

            _objectColliderCache[go] = cols;
            return cols;
        }

        private static bool IsHeldByHand(GameObject go)
        {
            if (go == null) return false;
            Transform t = go.transform;
            while (t != null)
            {
                if (VRHand.RightHandInstance != null && t == VRHand.RightHandInstance.transform) return true;
                if (VRHand.LeftHandInstance != null && t == VRHand.LeftHandInstance.transform) return true;
                t = t.parent;
            }
            return false;
        }
       
        private static int _dispatcherLogTimer = 0;
        private static readonly Dictionary<GameObject, Collider[]> _objectColliderCache
            = new Dictionary<GameObject, Collider[]>();
        private static readonly Collider[] _emptyColliders = new Collider[0];

        // ── Legacy helper (still used by VRHandPickupProximityAction) ─────────────
        public static bool IsHandNearObject(GameObject target)
        {
            if (target == null) return false;
            return IsRelated(NearestRightHandObject, target)
                || IsRelated(NearestLeftHandObject, target);
        }

        private static bool IsRelated(GameObject near, GameObject target)
        {
            if (near == null || target == null) return false;
            if (near == target) return true;
            if (near.transform.IsChildOf(target.transform)) return true;
            if (target.transform.IsChildOf(near.transform)) return true;
            // Sibling: ONLY check if they share the exact same immediate parent
            // Do NOT use IsChildOf here. That matches the entire car hierarchy.
            Transform nearParent = near.transform.parent;
            Transform targetParent = target.transform.parent;
            if (nearParent != null && nearParent == targetParent)
                return true;
            return false;
        }

        private static bool ObjectMatchesOrIsParent(GameObject near, GameObject target)
        {
            if (near == null || target == null) return false;
            if (near == target) return true;
            Transform t = near.transform.parent;
            while (t != null) { if (t.gameObject == target) return true; t = t.parent; }
            t = target.transform.parent;
            while (t != null) { if (t.gameObject == near) return true; t = t.parent; }
            return false;
        }
        public static void Reset()
        {
            ActiveInteractionTarget     = null;
            ActiveInteractionTargetLeft = null;
            _lastActiveInteractionTargetRight = null;
            _lastActiveInteractionTargetLeft = null;
            _lastSelectionSourceRight = string.Empty;
            _lastSelectionSourceLeft = string.Empty;
            _lastNoSelectableReasonRight = string.Empty;
            _lastNoSelectableReasonLeft = string.Empty;
            _dispatcherMismatchLogCooldownRight = 0;
            _dispatcherMismatchLogCooldownLeft = 0;
            _multiCandidateSelectionLogCooldownRight = 0;
            _multiCandidateSelectionLogCooldownLeft = 0;
            _registryPruneCooldownFrames = 0;
            NearestRightHandObject      = null;
            NearestLeftHandObject       = null;
            TriggerJustPressed_R  = false;
            TriggerJustReleased_R = false;
            TriggerIsHeld_R       = false;
            FistJustPressed_R     = false;
            FistIsHeld_R          = false;
            RightClickJustPressed_R = false;
            LaserRightIsActive_R  = false;
            GripIsHeld_R          = false;
            GripJustPressed_R     = false;
            GripJustReleased_R    = false;
            ControllerRollDelta_R = 0f;
            HandPosition_R        = Vector3.zero;
            HeldObject_R          = null;

            TriggerJustPressed_L  = false;
            TriggerJustReleased_L = false;
            TriggerIsHeld_L       = false;
            FistJustPressed_L     = false;
            FistIsHeld_L          = false;
            RightClickJustPressed_L = false;
            LaserLeftIsActive_L   = false;
            GripIsHeld_L          = false;
            GripJustPressed_L     = false;
            GripJustReleased_L    = false;
            ControllerRollDelta_L = 0f;
            HandPosition_L        = Vector3.zero;
            HeldObject_L          = null;

            PickupCandidate_R     = null;
            PickupCandidate_L     = null;
            LastPickupHandIsRight = true;
            PickupOwnerByFsm.Clear();
            _equipFinishFsms.Clear();
            _equipAlreadyFiredFsms.Clear();

            _objectColliderCache.Clear();
            VRProximityRegistry.Reset();
            VRLaserPointer.ResetRegisteredTargets();
        }
    }

    public class VRApplyThrowForceAction : FsmStateAction
    {
        public override void OnEnter()
        {
            FsmGameObject goVar = Fsm.Variables.GetFsmGameObject("PickedObject");

            if (goVar == null || goVar.Value == null)
            {
                PlayMakerHelpers.DebugLog("[VR-THROW] VRApplyThrowForceAction: FSM var 'PickedObject' is null");
                Finish();
                return;
            }

            Rigidbody rb = goVar.Value.GetComponent<Rigidbody>();

            if (rb == null)
            {
                PlayMakerHelpers.DebugLog("[VR-THROW] VRApplyThrowForceAction: no Rigidbody on " + goVar.Value.name);
                Finish();
                return;
            }

            if (rb.isKinematic)
            {
                PlayMakerHelpers.DebugLog("[VR-THROW] VRApplyThrowForceAction: Rigidbody is kinematic on " + goVar.Value.name);
                Finish();
                return;
            }

            Vector3 force = VRFsmActionBridge.ThrowVelocity
                          * VRFsmActionBridge.VRThrowForceScale;
            rb.AddForce(force, ForceMode.VelocityChange);

            PlayMakerHelpers.DebugLog("[VR-THROW] Force applied to " + goVar.Value.name
                + " magnitude=" + force.magnitude.ToString("F2"));

            Finish();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRInjectedAction  (abstract base)
    //
    // Shared plumbing for all injected actions.
    // Subclasses implement OnEnter / OnUpdate / OnExit.
    // ─────────────────────────────────────────────────────────────────────────────
    public abstract class VRInjectedAction : FsmStateAction
    {
        // Set by VRFsmPatcher.BuildAction() before Init() is called
        [HideInInspector] public PlayMakerFSM targetFsm;
        [HideInInspector] public string fsmEventName;
        [HideInInspector] public bool allowInputWithoutActiveTarget = false;

        // ── Convenience wrappers ──────────────────────────────────────────────────
        // All trigger/grip helpers are side-aware: they return right-controller state
        // when targetFsm is the right hand's active target, and left-controller state
        // when targetFsm is the left hand's active target.

        protected bool IsHandNear() => VRFsmActionBridge.IsHandNearObject(targetFsm != null ? targetFsm.gameObject : null);
        protected bool TriggerJustPressed  => VRFsmActionBridge.GetTriggerJustPressed(targetFsm);
        protected bool TriggerJustReleased => VRFsmActionBridge.GetTriggerJustReleased(targetFsm);
        protected bool TriggerIsHeld       => VRFsmActionBridge.GetTriggerIsHeld(targetFsm);
        protected bool FistJustPressed => VRFsmActionBridge.GetFistJustPressed(targetFsm);
        protected bool FistIsHeld => VRFsmActionBridge.GetFistIsHeld(targetFsm);
        protected bool RightClickJustPressed => VRFsmActionBridge.GetRightClickJustPressed(targetFsm);
        protected bool GripIsHeld    => VRFsmActionBridge.GetGripIsHeld(targetFsm);
        protected bool GripJustPressed => VRFsmActionBridge.GetGripJustPressed(targetFsm);
        protected bool GripJustReleased => VRFsmActionBridge.GetGripJustReleased(targetFsm);
        protected float ControllerRollDelta => VRFsmActionBridge.GetControllerRollDelta(targetFsm);


        protected bool IsActiveTarget()
        {
            return targetFsm == VRFsmActionBridge.ActiveInteractionTarget
                || targetFsm == VRFsmActionBridge.ActiveInteractionTargetLeft;
        }

        // Resolve trigger presses strictly by active hand slot to avoid cross-hand
        // activation where one controller can drive the other controller's target.
        protected bool TryGetTriggerPressingHand(out bool isRightHand)
        {
            bool rightPress = targetFsm != null
                && targetFsm == VRFsmActionBridge.ActiveInteractionTarget
                && VRFsmActionBridge.TriggerJustPressed_R;

            bool leftPress = targetFsm != null
                && targetFsm == VRFsmActionBridge.ActiveInteractionTargetLeft
                && VRFsmActionBridge.TriggerJustPressed_L;

            if (rightPress && !leftPress)
            {
                isRightHand = true;
                return true;
            }

            if (leftPress && !rightPress)
            {
                isRightHand = false;
                return true;
            }

            if (rightPress && leftPress)
            {
                isRightHand = VRFsmActionBridge.ResolveIsRightHand(
                    targetFsm,
                    targetFsm != null ? targetFsm.gameObject : null);
                return true;
            }

            if (allowInputWithoutActiveTarget)
            {
                bool rightAny = VRFsmActionBridge.TriggerJustPressed_R;
                bool leftAny = VRFsmActionBridge.TriggerJustPressed_L;

                if (rightAny && !leftAny)
                {
                    isRightHand = true;
                    return true;
                }

                if (leftAny && !rightAny)
                {
                    isRightHand = false;
                    return true;
                }

                if (rightAny && leftAny)
                {
                    isRightHand = VRFsmActionBridge.ResolveIsRightHand(
                        targetFsm,
                        targetFsm != null ? targetFsm.gameObject : null);
                    return true;
                }
            }

            isRightHand = true;
            return false;
        }

        protected bool TryGetFistPressingHand(out bool isRightHand)
        {
            bool rightPress = targetFsm != null
                && targetFsm == VRFsmActionBridge.ActiveInteractionTarget
                && VRFsmActionBridge.FistJustPressed_R;

            bool leftPress = targetFsm != null
                && targetFsm == VRFsmActionBridge.ActiveInteractionTargetLeft
                && VRFsmActionBridge.FistJustPressed_L;

            if (rightPress && !leftPress)
            {
                isRightHand = true;
                return true;
            }

            if (leftPress && !rightPress)
            {
                isRightHand = false;
                return true;
            }

            if (rightPress && leftPress)
            {
                isRightHand = VRFsmActionBridge.ResolveIsRightHand(
                    targetFsm,
                    targetFsm != null ? targetFsm.gameObject : null);
                return true;
            }

            isRightHand = true;
            return false;
        }

        protected static bool IsGripHeldForHand(bool isRightHand)
        {
            return isRightHand
                ? VRFsmActionBridge.GripIsHeld_R
                : VRFsmActionBridge.GripIsHeld_L;
        }

        protected bool TryGetTriggerHoldingHand(out bool isRightHand)
        {
            bool rightHeld = targetFsm != null
                && targetFsm == VRFsmActionBridge.ActiveInteractionTarget
                && VRFsmActionBridge.TriggerIsHeld_R;

            bool leftHeld = targetFsm != null
                && targetFsm == VRFsmActionBridge.ActiveInteractionTargetLeft
                && VRFsmActionBridge.TriggerIsHeld_L;

            if (rightHeld && !leftHeld)
            {
                isRightHand = true;
                return true;
            }

            if (leftHeld && !rightHeld)
            {
                isRightHand = false;
                return true;
            }

            if (rightHeld && leftHeld)
            {
                isRightHand = VRFsmActionBridge.ResolveIsRightHand(
                    targetFsm,
                    targetFsm != null ? targetFsm.gameObject : null);
                return true;
            }

            if (allowInputWithoutActiveTarget)
            {
                bool rightAny = VRFsmActionBridge.TriggerIsHeld_R;
                bool leftAny = VRFsmActionBridge.TriggerIsHeld_L;

                if (rightAny && !leftAny)
                {
                    isRightHand = true;
                    return true;
                }

                if (leftAny && !rightAny)
                {
                    isRightHand = false;
                    return true;
                }

                if (rightAny && leftAny)
                {
                    isRightHand = VRFsmActionBridge.ResolveIsRightHand(
                        targetFsm,
                        targetFsm != null ? targetFsm.gameObject : null);
                    return true;
                }
            }

            isRightHand = true;
            return false;
        }

        protected static bool IsTriggerHeldForHand(bool isRightHand)
        {
            return isRightHand
                ? VRFsmActionBridge.TriggerIsHeld_R
                : VRFsmActionBridge.TriggerIsHeld_L;
        }

        protected static bool IsTriggerReleasedForHand(bool isRightHand)
        {
            return isRightHand
                ? VRFsmActionBridge.TriggerJustReleased_R
                : VRFsmActionBridge.TriggerJustReleased_L;
        }

        /// <summary>Send a named event to this action's owning FSM.</summary>
        protected void FireEvent(string evName)
        {
            if (string.IsNullOrEmpty(evName)) return;
            HutongGames.PlayMaker.Fsm fsmToUse = (targetFsm != null) ? targetFsm.Fsm : Fsm;
            if (fsmToUse == null)
            {
        #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRAction] FireEvent FAILED (null Fsm) event='" + evName
                    + "' obj='" + (targetFsm != null ? targetFsm.gameObject.name : "?")
                    + "' targetFsm.Fsm=" + (targetFsm != null ? (targetFsm.Fsm != null ? "OK" : "NULL") : "N/A")
                    + " base.Fsm=" + (Fsm != null ? "OK" : "NULL"));
        #endif
                return;
            }
            FsmEvent ev = FsmEvent.GetFsmEvent(evName);
            if (ev == null) return;

            if (!fsmToUse.Started)
            {
        #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[FireEvent] FSM NOT STARTED: calling Start() obj='"
                    + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
        #endif
                fsmToUse.Start();
            }

        #if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[VRAction] FireEvent '" + evName
                + "' obj='" + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' fsm='" + (targetFsm != null ? targetFsm.FsmName : "?")
                + "' state='" + (fsmToUse.ActiveState != null ? fsmToUse.ActiveState.Name : "?")
                + "' enabled=" + (targetFsm != null ? targetFsm.enabled.ToString() : "?") + "'");
        #endif

            fsmToUse.ProcessEvent(ev);
        }

        /// <summary>Send a named event to this action's owning FSM (using fsmEventName field).</summary>
        protected void FireConfiguredEvent() => FireEvent(fsmEventName);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRTriggerPressAction
    //
    // Fires fsmEventName once when the right trigger is pressed while a VR hand
    // is near this FSM's gameObject.
    //
    // Replaces: GetMouseButtonDown (left click)
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRTriggerPressAction : VRInjectedAction
    {
        private int _blockedPressLogCooldown = 0;

        public override void OnEnter()
        {
            _blockedPressLogCooldown = 0;
        }

        public override void OnUpdate()
        {
            if (_blockedPressLogCooldown > 0)
                _blockedPressLogCooldown--;

            bool pressedByRight;
            if (!TryGetTriggerPressingHand(out pressedByRight))
            {
                #if MVRC_DEBUG
                bool anyTriggerPress = VRFsmActionBridge.TriggerJustPressed_R
                    || VRFsmActionBridge.TriggerJustPressed_L;
                bool isGripRequiredTarget = targetFsm != null
                    && targetFsm.gameObject != null
                    && VRProximityRegistry.GripRequiredFsms.ContainsKey(targetFsm.gameObject);

                if (anyTriggerPress && isGripRequiredTarget && _blockedPressLogCooldown <= 0)
                {
                    string targetLabel = targetFsm != null
                        ? targetFsm.gameObject.name + "." + targetFsm.FsmName : "null";
                    string activeR = VRFsmActionBridge.ActiveInteractionTarget != null
                        ? VRFsmActionBridge.ActiveInteractionTarget.gameObject.name + "."
                            + VRFsmActionBridge.ActiveInteractionTarget.FsmName : "none";
                    string activeL = VRFsmActionBridge.ActiveInteractionTargetLeft != null
                        ? VRFsmActionBridge.ActiveInteractionTargetLeft.gameObject.name + "."
                            + VRFsmActionBridge.ActiveInteractionTargetLeft.FsmName : "none";
                    PlayMakerHelpers.DebugLog("[VRTriggerPress] BLOCKED grip-required target='"
                        + targetLabel + "' state='" + (targetFsm != null ? (targetFsm.ActiveStateName ?? "null") : "null")
                        + "' triggerPress[R=" + VRFsmActionBridge.TriggerJustPressed_R
                        + ",L=" + VRFsmActionBridge.TriggerJustPressed_L + "]"
                        + " activeTarget[R=" + activeR + ",L=" + activeL + "]");
                    _blockedPressLogCooldown = 15;
                }
                #endif
                return;
            }
            #if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[VRTriggerPress] FIRE on '"
                + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' state='" + (targetFsm != null ? (targetFsm.ActiveStateName ?? "null") : "null")
                + "' hand=" + (pressedByRight ? "R" : "L"));
            #endif
            FireConfiguredEvent();
            if (VRProximityRegistry.GripRequiredFsms.ContainsKey(targetFsm.gameObject))
            {
                VRGripReleaseAction.SuppressFrames = 5;
                VRPickupTriggerAction.SuppressFrames = 10; // suppress re-grab for 1 second
                ResetPickupFsmAfterAssembly();
            }
        }
        private static PlayMakerFSM _cachedAssembleHandFsm = null;

        private static void ResetPickupFsmAfterAssembly()
        {
            // Use cached FSM reference; re-resolve only if it became null (scene reload)
            if (_cachedAssembleHandFsm == null || _cachedAssembleHandFsm.gameObject == null)
            {
                _cachedAssembleHandFsm = null;
                GameObject player = GameObject.Find("PLAYER");
                if (player == null) return;
                string[] path = { "Pivot", "AnimPivot", "Camera",
                        "FPSCamera", "1Hand_Assemble", "Hand" };
                Transform t = player.transform;
                foreach (string step in path)
                {
                    t = t.Find(step);
                    if (t == null) return;
                }
                foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (fsm.FsmName == "PickUp") { _cachedAssembleHandFsm = fsm; break; }
                }
            }

            if (_cachedAssembleHandFsm == null) return;

            string state = _cachedAssembleHandFsm.ActiveStateName ?? "";
            if (state == "Part picked" || state == "Tool picked" || state == "Item picked")
            {
                _cachedAssembleHandFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("PROCEED Drop"));
        #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRAssembly] Reset PickUp FSM from '" + state + "'");
        #endif
            }
        }
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // VRFistPressAction
    //
    // Fires fsmEventName once when fist pose is pressed while this FSM owns
    // the active interaction target slot for the relevant hand.
    //
    // Replaces: GetButtonDown("HIT") style trigger actions on breakables.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRFistPressAction : VRInjectedAction
    {
        private bool _firedWhileHeld = false;

        public override void OnEnter() { _firedWhileHeld = false; }

        public override void OnUpdate()
        {
            // Reset once the fist pose is fully released so a new hold can fire again.
            bool anyFistHeld = VRFsmActionBridge.FistIsHeld_R || VRFsmActionBridge.FistIsHeld_L;
            if (!anyFistHeld)
                _firedWhileHeld = false;

            bool pressedByRight;
            if (TryGetFistPressingHand(out pressedByRight))
            {
                _firedWhileHeld = true;
                PlayMakerHelpers.DebugLog("[VRFistPress] FIRE(edge) on '"
                    + (targetFsm != null ? targetFsm.gameObject.name : "?")
                    + "' state='" + (targetFsm != null ? (targetFsm.ActiveStateName ?? "null") : "null")
                    + "' hand=" + (pressedByRight ? "R" : "L"));
                FireConfiguredEvent();
                return;
            }

            // If proximity/selection activates after fist is already held,
            // fire once immediately instead of waiting for a new down-edge.
            if (!FistIsHeld || _firedWhileHeld)
                return;

            bool isRight = targetFsm == VRFsmActionBridge.ActiveInteractionTarget;
            bool isLeft = targetFsm == VRFsmActionBridge.ActiveInteractionTargetLeft;
            if (!isRight && !isLeft)
                return;
            if (isRight && isLeft)
            {
                isRight = VRFsmActionBridge.ResolveIsRightHand(
                    targetFsm,
                    targetFsm != null ? targetFsm.gameObject : null);
            }

            _firedWhileHeld = true;
            PlayMakerHelpers.DebugLog("[VRFistPress] FIRE(held) on '"
                + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' state='" + (targetFsm != null ? (targetFsm.ActiveStateName ?? "null") : "null")
                + "' hand=" + (isRight ? "R" : "L"));
            FireConfiguredEvent();
        }

        public override void OnExit() { _firedWhileHeld = false; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRTriggerHoldAction
    //
    // Fires pressEventName on trigger down, releaseEventName on trigger up.
    // The action tracks its own held state so only one press/release cycle fires
    // per proximity visit.
    //
    // Replaces: GetMouseButtonDown + GetMouseButtonUp pairs (e.g. ignition)
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRTriggerHoldAction : VRInjectedAction
    {
        [HideInInspector] public string pressEventName;
        [HideInInspector] public string releaseEventName;

        private bool _holding = false;
        private bool _holdingRightHand = true;

        public override void OnEnter()
        {
            _holding = false;
            _holdingRightHand = true;
        }

        public override void OnUpdate()
        {
            if (!_holding)
            {
                bool pressRight;
                if (!TryGetTriggerPressingHand(out pressRight)) return;

                _holding = true;
                _holdingRightHand = pressRight;
                #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRTriggerHold] PRESS on '"
                    + (targetFsm != null ? targetFsm.gameObject.name : "?")
                    + "' hand=" + (_holdingRightHand ? "R" : "L"));
                #endif
                FireEvent(pressEventName);
            }
            else
            {
                if (IsTriggerHeldForHand(_holdingRightHand)) return;

                _holding = false;
                #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRTriggerHold] RELEASE on '"
                    + (targetFsm != null ? targetFsm.gameObject.name : "?")
                    + "' hand=" + (_holdingRightHand ? "R" : "L"));
                #endif
                FireEvent(releaseEventName);
            }
        }

        public override void OnExit() { _holding = false; _holdingRightHand = true; }
    }


    //─────────────────────────────────────────────────────────────────────────────
    //VRTriggerRightClickAction
    // Fires fsmEventName when B+Trigger are pressed together (right-click analogue).
    // Replaces: GetMouseButtonDown with button == MouseButton.Right
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRTriggerRightClickAction : VRInjectedAction
    {
        private int _debugLogCooldown = 0;

        public override void OnEnter()
        {
            _debugLogCooldown = 0;
            PlayMakerHelpers.DebugLog("[VRRightClick] OnEnter '" + fsmEventName + "' obj='"
                + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
        }

        public override void OnUpdate()
        {
            if (_debugLogCooldown > 0)
                _debugLogCooldown--;

            if (allowInputWithoutActiveTarget)
            {
                // Bypass active-target check: respond to raw right-click input from either hand
                bool rightClickPressed = VRFsmActionBridge.RightClickJustPressed_R
                                      || VRFsmActionBridge.RightClickJustPressed_L;

                if (_debugLogCooldown <= 0)
                {
                    PlayMakerHelpers.DebugLog("[VRRightClick] BYPASS mode: RightClickJustPressed_R="
                        + VRFsmActionBridge.RightClickJustPressed_R
                        + " RightClickJustPressed_L=" + VRFsmActionBridge.RightClickJustPressed_L
                        + " obj='" + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
                    _debugLogCooldown = 30;
                }

                if (!rightClickPressed) return;

                PlayMakerHelpers.DebugLog("[VRRightClick] FIRING (bypass) '" + fsmEventName + "' on '"
                    + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
                FireConfiguredEvent();
                return;
            }

            // Normal path: requires active target (either hand)
            bool isRightTarget = targetFsm != null && targetFsm == VRFsmActionBridge.ActiveInteractionTarget;
            bool isLeftTarget  = targetFsm != null && targetFsm == VRFsmActionBridge.ActiveInteractionTargetLeft;
            if (!isRightTarget && !isLeftTarget) return;

            if (!RightClickJustPressed) return;

            PlayMakerHelpers.DebugLog("[VRRightClick] FIRING '" + fsmEventName + "' on '"
                + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
            FireConfiguredEvent();
        }

        public override void OnExit()
        {
            _debugLogCooldown = 0;
        }

    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRKnobTwistAction
    //
    // Converts right-controller roll rotation (while trigger held) into the
    // TIGHTEN / UNTIGHTEN events that knob/dial FSMs already handle.
    // An accumulator ensures you need a deliberate twist, not any tiny jitter.
    //
    // Replaces: GetAxis("Mouse ScrollWheel") → FloatCompare → TIGHTEN/UNTIGHTEN
    //
    // Note: For MVRC's bolt system, use VRInteraction.SendBoltTighten instead.
    //       This action is for generic game knobs like radio volume, choke, etc.
    // ─────────────────────────────────────────────────────────────────────────────
    class VRKnobTwistAction : VRInjectedAction
    {
        public float DegreesPerTick = 2f;
        private float _accumulator = 0f;

        private static readonly string[] PositiveEvents = { "TIGHTEN", "VOLDOWN", "UP" };
        private static readonly string[] NegativeEvents = { "UNTIGHTEN", "VOLUP", "DOWN" };

        public override void OnEnter()
        {
            _accumulator = 0f;
        }

        public override void OnUpdate()
        {
            bool isRight = targetFsm == VRFsmActionBridge.ActiveInteractionTarget;
            bool isLeft = targetFsm == VRFsmActionBridge.ActiveInteractionTargetLeft;
            if (!isRight && !isLeft) { _accumulator = 0f; return; }

            bool held = isRight ? VRFsmActionBridge.TriggerIsHeld_R : VRFsmActionBridge.TriggerIsHeld_L;
            if (!held) { _accumulator = 0f; return; }

            float roll = isRight ? VRFsmActionBridge.ControllerRollDelta_R : VRFsmActionBridge.ControllerRollDelta_L;
            _accumulator += roll;

            while (_accumulator >= DegreesPerTick)
            {
                _accumulator -= DegreesPerTick;
                FireAll(PositiveEvents);
            }
            while (_accumulator <= -DegreesPerTick)
            {
                _accumulator += DegreesPerTick;
                FireAll(NegativeEvents);
            }
        }
        private void FireAll(string[] events)
        {
            foreach (string ev in events)
                FireEvent(ev);
        }

        public override void OnExit() { _accumulator = 0f; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRProximityActivateAction
    //
    // Fires fsmEventName once the moment a VR hand enters proximity.
    // No trigger press needed. Fires once per state entry to avoid spam.
    //
    // Replaces: MousePickEvent in "Wait Player N" gate states where the original
    //           logic fires on hover rather than click.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRProximityActivateAction : VRInjectedAction
    {
        // Per-side closest flags, set independently by UpdateDispatcher for each
        // controller so neither hand's result overwrites the other's.
        // OR logic is intentional: activate when ANY hand is near the object so
        // that either controller can trigger proximity-activated states.
        public bool IsClosestRight = false;
        public bool IsClosestLeft  = false;
        public bool IsClosest => IsClosestRight || IsClosestLeft;
        public bool AllowInRangeActivation = false;
        public bool IsInRangeRight = false;
        public bool IsInRangeLeft = false;
        public bool IsInRange => IsInRangeRight || IsInRangeLeft;
    
        private bool _wasActive = false;

        public override void OnEnter()
        {
            _wasActive = false;
        }
    
        public override void OnUpdate()
        {
            bool active = IsClosest || (AllowInRangeActivation && IsInRange);
            bool edge = active && !_wasActive;
            _wasActive = active;
            if (!edge) return;
            if (edge)
            {
                VRObjectHighlighter.Highlight(targetFsm != null ? targetFsm.gameObject : null);
                FireConfiguredEvent();
            }
        }
    
        public override void OnExit() { _wasActive = false; }
    }
    public class VRProximityLeaveAction : VRInjectedAction
    {
        public bool IsClosestRight = false;
        public bool IsClosestLeft = false;
        public bool IsClosest => IsClosestRight || IsClosestLeft;
        public bool AllowInRangeActivation = false;
        public bool IsInRangeRight = false;
        public bool IsInRangeLeft = false;
        public bool IsInRange => IsInRangeRight || IsInRangeLeft;

        private bool _wasActive = false;

        public override void OnEnter()
        {
            _wasActive = false; // don't fire on entry unless hand was already near
        }

        public override void OnUpdate()
        {
            bool active = IsClosest || (AllowInRangeActivation && IsInRange);
            if (active)
            {
                _wasActive = true; // hand is near; arm the leave trigger
                return;
            }

            if (!_wasActive) return; // hand was never near this state; do not fire

            // Hand just left. Fire once and disarm.
            _wasActive = false;
            VRObjectHighlighter.Unhighlight(targetFsm != null ? targetFsm.gameObject : null);
            PlayMakerHelpers.DebugLog("[VRProxLeave] FIRE '" + fsmEventName + "' on '"
                + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
            FireConfiguredEvent();
        }

        public override void OnExit() 
        {
            // Safety net: if the state exits without a leave edge (e.g. FSM forced elsewhere),
            // clean up highlight anyway.
            VRObjectHighlighter.Unhighlight(targetFsm != null ? targetFsm.gameObject : null);
            _wasActive = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRStateEnterEventAction
    //
    // Fires fsmEventName immediately when the state becomes active.
    // Useful for keybind-gated states that should auto-advance in VR.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRStateEnterEventAction : VRInjectedAction
    {
        private bool _fired = false;

        public override void OnEnter()
        {
            _fired = false;
            FireOnce();
        }

        public override void OnUpdate()
        {
            FireOnce();
        }

        public override void OnExit()
        {
            _fired = false;
        }

        private void FireOnce()
        {
            if (_fired) return;

            if (string.Equals(fsmEventName, "EQUIP", System.StringComparison.OrdinalIgnoreCase)
                && !VRFsmActionBridge.CanFireEquipForFsm(targetFsm))
            {
                return;
            }

            _fired = true;
        #if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[VRStateEnter] FIRE '" + fsmEventName + "' on '"
                + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' state='" + (targetFsm != null ? (targetFsm.ActiveStateName ?? "null") : "null") + "'");
        #endif
            FireConfiguredEvent();
            if (string.Equals(fsmEventName, "EQUIP", System.StringComparison.OrdinalIgnoreCase))
            {
                VRFsmActionBridge.MarkEquipFiredForFsm(targetFsm);
                VRFsmActionBridge.RegisterEquipFsm(targetFsm);
            }
        }
    }
    
        // VRGripPressAction
        //
        // Fires fsmEventName once when grip is pressed while the FSM is active.
        // Unlike StateEnterEvent which fires on state enter, this only fires on actual grip input.
        // Useful for equip events that should require intentional player action.
        // ─────────────────────────────────────────────────────────────────────────────
        public class VRGripPressAction : VRInjectedAction
        {
            private bool _armed = false;

            public override void OnEnter()
            {
                _armed = true;
            }

            public override void OnUpdate()
            {
                if (!_armed) return;
                if (!GripJustPressed) return;

                _armed = false;
            #if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRGripPress] FIRE '" + fsmEventName + "' on '"
                    + (targetFsm != null ? targetFsm.gameObject.name : "?")
                    + "' state='" + (targetFsm != null ? (targetFsm.ActiveStateName ?? "null") : "null") + "'");
            #endif
                FireConfiguredEvent();
                if (string.Equals(fsmEventName, "EQUIP", System.StringComparison.OrdinalIgnoreCase))
                    VRFsmActionBridge.RegisterEquipFsm(targetFsm);
            }

            public override void OnExit()
            {
                _armed = false;
            }
        }
        // ─────────────────────────────────────────────────────────────────────────────
    // VRGrabTriggerAction
    //
    // Fires fsmEventName when grip IS held AND trigger is pressed.
    // Intended for interactions where holding an object and pressing trigger
    // should trigger a secondary use (e.g. applying a held item).
    //
    // Replaces nothing specific. Add to states that should require grip+trigger.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRGrabTriggerAction : VRInjectedAction
    {
        #if MVRC_DEBUG
        private int _logTimer = 0;
        #endif

        public override void OnEnter()
        {
        #if MVRC_DEBUG
            _logTimer = 0;
            PlayMakerHelpers.DebugLog("[VRGrabTrigger] OnEnter obj='"
                + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' event='" + fsmEventName + "'");
        #endif
        }

        public override void OnUpdate()
        {
        #if MVRC_DEBUG
            _logTimer++;
            if (_logTimer % 120 == 0)
                PlayMakerHelpers.DebugLog("[VRGrabTrigger] grip=" + GripIsHeld
                    + " trigJust=" + TriggerJustPressed
                    + " obj='" + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
        #endif
            bool pressedByRight;
            if (!TryGetTriggerPressingHand(out pressedByRight)) return;
            if (!IsGripHeldForHand(pressedByRight)) return;

            PlayMakerHelpers.DebugLog("[VRGrabTrigger] FIRING '" + fsmEventName + "' on '"
                + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' hand=" + (pressedByRight ? "R" : "L"));
            FireConfiguredEvent();
        }

        public override void OnExit() 
        {
            #if MVRC_DEBUG
            _logTimer = 0;
            #endif
        }
    }
        
    public class VRTriggerReleaseAction : VRInjectedAction
    {
        // Armed once this action sees trigger held while the FSM is active target.
        // Release event then tracks that same hand until the trigger is actually released,
        // even if active target focus shifts while dragging/holding nearby objects.
        private bool _armed = false;
        private bool _armedRightHand = true;

        public override void OnEnter()
        {
            _armed = false;
            _armedRightHand = true;
            PlayMakerHelpers.DebugLog("[VRTrigRelease] OnEnter obj='"
                + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' event='" + fsmEventName + "'");
        }

        public override void OnUpdate()
        {
            if (!_armed)
            {
                bool pressRight;
                if (TryGetTriggerPressingHand(out pressRight))
                {
                    _armed = true;
                    _armedRightHand = pressRight;
                    return;
                }

                bool heldRight;
                if (!TryGetTriggerHoldingHand(out heldRight)) return;

                _armed = true;
                _armedRightHand = heldRight;
                return;
            }

            if (IsTriggerHeldForHand(_armedRightHand)) return;

            _armed = false;
            #if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[VRTrigRelease] FIRING '" + fsmEventName + "' on '"
                + (targetFsm != null ? targetFsm.gameObject.name : "?")
                + "' hand=" + (_armedRightHand ? "R" : "L"));
            #endif
            FireConfiguredEvent();
        }

        public override void OnExit()
        {
            _armed = false;
            _armedRightHand = true;
        }
    }
    public class VRGripReleaseAction : VRInjectedAction
    {
        public static int SuppressFrames = 0;
        public override void OnEnter() { }

        public override void OnUpdate()
        {
            if (SuppressFrames > 0) { SuppressFrames--; return; }

            bool ownerIsRight;
            if (!VRFsmActionBridge.TryGetPickupOwner(targetFsm, out ownerIsRight))
                ownerIsRight = VRFsmActionBridge.ResolveIsRightHand(
                    targetFsm,
                    targetFsm != null ? targetFsm.gameObject : null);

            bool released = ownerIsRight
                ? VRFsmActionBridge.GripJustReleased_R
                : VRFsmActionBridge.GripJustReleased_L;

            if (!released) return;

            Transform ownerHand = ownerIsRight
                ? (VRHand.RightHandInstance != null
                    ? VRHand.RightHandInstance.transform
                    : VRProximitySphereDebug.RightHandTransform)
                : (VRHand.LeftHandInstance != null
                    ? VRHand.LeftHandInstance.transform
                    : null);

            if (!HandHoldsObject(ownerHand)) return;
            FireConfiguredEvent();
        }

        private static bool HandHoldsObject(Transform hand)
        {
            if (hand == null) return false;
            foreach (Transform child in hand)
            {
                if (child.GetComponent<Rigidbody>() != null)
                    return true;
            }
            return false;
        }

        public override void OnExit() { }

    }
    public class VRDeactivateHeldObjectAction : VRInjectedAction
    {
        private bool _deactivated = false;

        public override void OnEnter()
        {
            _deactivated = false;
            Apply(true);
        }

        public override void OnUpdate()
        {
            if (!_deactivated)
                Apply(true);
        }

        public override void OnExit()
        {
            Apply(false);
            _deactivated = false;
        }

        private void Apply(bool deactivate)
        {
            if (TryFindHeldObject(out GameObject held))
            {
                held.SetActive(!deactivate);
                _deactivated = deactivate;
            }
        }

        private static bool TryFindHeldObject(out GameObject result)
        {
            result = ScanHand(VRHand.RightHandInstance);
            if (result != null) return true;

            result = ScanHand(VRHand.LeftHandInstance);
            if (result != null) return true;

            // Fallback: debug-hand transforms used in non-VR/debug mode
            try
            {
                var debugRight = VRProximitySphereDebug.RightHandTransform;
                if (debugRight != null)
                {
                    result = ScanHandTransform(debugRight);
                    if (result != null) return true;
                }
            }
            catch { }

            result = null;
            return false;
        }

        private static GameObject ScanHand(VRHand hand)
        {
            if (hand == null) return null;
            return ScanHandTransform(hand.transform);
        }

        private static GameObject ScanHandTransform(Transform handTransform)
        {
            if (handTransform == null) return null;

            foreach (Transform child in handTransform)
            {
                Rigidbody rb = child.GetComponent<Rigidbody>();
                if (rb != null)
                    return child.gameObject;
            }

            return null;
        }
    }
    public class VRHandChildQueryAction : VRInjectedAction
    {
        [HideInInspector] public string storeVariableName;

        public override void OnEnter() { }

        public override void OnUpdate()
        {
            GameObject held = FindHeldPart();

            if (!string.IsNullOrEmpty(storeVariableName) && targetFsm != null)
            {
                FsmGameObject fsmVar = targetFsm.FsmVariables
                    .GetFsmGameObject(storeVariableName);
                if (fsmVar != null)
                    fsmVar.Value = held;
            }
        }

        public override void OnExit() { }

        private static GameObject FindHeldPart()
        {
            GameObject result = ScanHandChildren(VRHand.RightHandInstance);
            if (result != null) return result;
            return ScanHandChildren(VRHand.LeftHandInstance);
        }

        private static GameObject ScanHandChildren(VRHand hand)
        {
            if (hand == null) return null;
            foreach (Transform child in hand.transform)
            {
                if (child.gameObject.tag == "PART")
                    return child.gameObject;
            }
            return null;
        }
    }
    class VRSelfReparentAction : VRInjectedAction
    {
        public override void OnEnter()
        {
            if (targetFsm == null) return;

            bool isRight = VRFsmActionBridge.ResolveIsRightHand(targetFsm, targetFsm.gameObject);

            Transform hand = isRight
                ? (VRHand.RightHandInstance != null
                    ? VRHand.RightHandInstance.transform
                    : VRProximitySphereDebug.RightHandTransform)
                : (VRHand.LeftHandInstance != null
                    ? VRHand.LeftHandInstance.transform
                    : null);


            if (hand == null)
            {
                PlayMakerHelpers.DebugLog("[VRSelfReparent] No hand transform available");
                return;
            }

            targetFsm.gameObject.transform.SetParent(hand, true);
            PlayMakerHelpers.DebugLog("[VRSelfReparent] '" + targetFsm.gameObject.name + "' → hand");
        }

        public override void OnUpdate() { }
        public override void OnExit() { }
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // VRHandReparentAction
    //
    // Reparents a target GameObject to a VR hand transform on state enter,
    // and restores its original parent on state exit (when restoreOnExit = true).
    //
    // Target resolution (reparentAncestorName):
    //   null/empty → targetFsm.gameObject itself
    //   non-empty  → walk UP the hierarchy from targetFsm looking for that name;
    //                if not found upward, check immediate children;
    //                if still not found, log a warning and fall back to FSM's GO.
    //
    // Hand resolution (handPreference):
    //   ActiveTarget → whichever hand's dispatcher slot owns this FSM this frame
    //   Right / Left → always that side, regardless of dispatcher
    //
    // Replaces: VRSelfReparentAction for objects that need ancestor resolution,
    //           configurable offsets, or restore-on-exit behaviour.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRHandReparentAction : VRInjectedAction
    {
        [HideInInspector] public string ancestorName;
        [HideInInspector] public Vector3 localOffset;
        [HideInInspector] public Vector3 localRotation;
        [HideInInspector] public bool restoreOnExit;
        [HideInInspector] public ReparentHandPreference handPreference;
    
        // Remembered at entry so OnExit can undo exactly what OnEnter did
        private Transform _originalParent;
        private Vector3   _originalLocalPos;
        private Quaternion _originalLocalRot;
        private GameObject _target;
        private Rigidbody[] _targetRigidbodies;
        private bool[] _targetRigidbodiesWereKinematic;
        private SkinnedMeshRenderer[] _hiddenHandRenderers;
        private bool[] _hiddenHandRenderersWereEnabled;
        private Quaternion _cachedLocalRot;           // ADD: pre-baked Euler → Quaternion
        private bool _fuelNozzleAdjusted = false;     // ADD: guard against repeated adjustment
        private static readonly Vector3 FuelNozzleColliderCenter = new Vector3(0.1f, 0f, -0.1f);
    
        public override void OnEnter()
        {
            _cachedLocalRot = Quaternion.Euler(localRotation);
            _fuelNozzleAdjusted = false;
            TryApplyReparent(true);
        }
    
        public override void OnUpdate()
        {
            if (_target == null)
            {
                TryApplyReparent(false);
                return;
            }

            Transform hand = ResolveHand(_target);
            if (hand == null) return;

            // One-time collider adjustment. Run only until it succeeds.
            if (!_fuelNozzleAdjusted)
                _fuelNozzleAdjusted = TryAdjustFuelNozzleCapsuleColliderCenter();

            if (_target.transform.parent != hand)
                _target.transform.SetParent(hand, true);

            _target.transform.localPosition = localOffset;
            _target.transform.localRotation = _cachedLocalRot;  // use pre-baked value
        }
    
        public override void OnExit()
        {
            if (restoreOnExit && _target != null)
            {
                _target.transform.SetParent(_originalParent, true);
                _target.transform.localPosition = _originalLocalPos;
                _target.transform.localRotation = _originalLocalRot;
                RestoreTargetRigidbodiesKinematic();
                RestoreHiddenHandRenderers();

                PlayMakerHelpers.DebugLog("[VRReparent] '" + _target.name + "' restored to '"
                    + (_originalParent != null ? _originalParent.name : "scene root") + "'");
            }

            if (!restoreOnExit)
            {
                _targetRigidbodies = null;
                _targetRigidbodiesWereKinematic = null;
                _hiddenHandRenderers = null;
                _hiddenHandRenderersWereEnabled = null;
            }

            _target = null;
        }

        private void TryApplyReparent(bool logFailures)
        {
            if (_target != null) return;

            GameObject resolvedTarget = ResolveTarget();
            if (resolvedTarget == null)
            {
                if (logFailures)
                    PlayMakerHelpers.DebugLog("[VRReparent] OnEnter: target not resolved, skipping");
                return;
            }

            Transform hand = ResolveHand(resolvedTarget);
            if (hand == null)
            {
                if (logFailures)
                    PlayMakerHelpers.DebugLog("[VRReparent] OnEnter: hand transform not available");
                return;
            }

            _target = resolvedTarget;

            // Snapshot original transform so we can restore it
            _originalParent = _target.transform.parent;
            _originalLocalPos = _target.transform.localPosition;
            _originalLocalRot = _target.transform.localRotation;

            _target.transform.SetParent(hand, true);   // worldPositionStays = true
            _target.transform.localPosition = localOffset;
            _target.transform.localRotation = Quaternion.Euler(localRotation);
            CaptureAndSetTargetRigidbodiesKinematic();
            CaptureAndHideHandRenderers();
            _fuelNozzleAdjusted = TryAdjustFuelNozzleCapsuleColliderCenter();

            PlayMakerHelpers.DebugLog("[VRReparent] '" + _target.name
                + "' → " + hand.name
                + " offset=" + localOffset + " rot=" + localRotation);
        }

        private void CaptureAndSetTargetRigidbodiesKinematic()
        {
            _targetRigidbodies = _target != null
                ? _target.GetComponentsInChildren<Rigidbody>(true)
                : null;

            if (_targetRigidbodies == null || _targetRigidbodies.Length == 0)
            {
                _targetRigidbodiesWereKinematic = null;
                return;
            }

            _targetRigidbodiesWereKinematic = new bool[_targetRigidbodies.Length];
            for (int i = 0; i < _targetRigidbodies.Length; i++)
            {
                Rigidbody rb = _targetRigidbodies[i];
                if (rb == null) continue;

                _targetRigidbodiesWereKinematic[i] = rb.isKinematic;
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void CaptureAndHideHandRenderers()
        {
            _hiddenHandRenderers = null;
            _hiddenHandRenderersWereEnabled = null;

            if (_target == null) return;

            SkinnedMeshRenderer[] renderers = _target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            List<SkinnedMeshRenderer> toHide = new List<SkinnedMeshRenderer>();
            List<bool> previousEnabled = new List<bool>();

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer smr = renderers[i];
                if (!ShouldHideSkinnedHandRenderer(smr)) continue;

                toHide.Add(smr);
                previousEnabled.Add(smr.enabled);
                smr.enabled = false;

                PlayMakerHelpers.DebugLog("[VRReparent] Hiding skinned mesh '" + BuildHierarchyPath(smr.transform) + "'");
            }

            if (toHide.Count == 0) return;

            _hiddenHandRenderers = toHide.ToArray();
            _hiddenHandRenderersWereEnabled = previousEnabled.ToArray();
        }

        private void RestoreHiddenHandRenderers()
        {
            if (_hiddenHandRenderers == null || _hiddenHandRenderersWereEnabled == null) return;

            int count = Mathf.Min(_hiddenHandRenderers.Length, _hiddenHandRenderersWereEnabled.Length);
            for (int i = 0; i < count; i++)
            {
                SkinnedMeshRenderer smr = _hiddenHandRenderers[i];
                if (smr == null) continue;

                smr.enabled = _hiddenHandRenderersWereEnabled[i];
            }

            _hiddenHandRenderers = null;
            _hiddenHandRenderersWereEnabled = null;
        }

        private bool ShouldHideSkinnedHandRenderer(SkinnedMeshRenderer smr)
        {
            if (smr == null || smr.gameObject == null) return false;

            string name = smr.gameObject.name.ToLower();
            if (name.Contains("hand_rigged")) return true;

            string path = BuildHierarchyPath(smr.transform).ToLower();
            if (path.Contains("/pivot/hand/") || path.Contains("/hand/hand_rigged"))
                return true;

            return false;
        }

        private bool TryAdjustFuelNozzleCapsuleColliderCenter()
        {
            if (_target == null) return true;   // nothing to adjust, mark complete
            CapsuleCollider[] colliders = _target.GetComponentsInChildren<CapsuleCollider>(true);
            if (colliders == null || colliders.Length == 0) return true;  // not a nozzle, done

            CapsuleCollider fuelCollider = null;
            for (int i = 0; i < colliders.Length; i++)
            {
                CapsuleCollider cc = colliders[i];
                if (cc == null || cc.gameObject == null) continue;
                string name = cc.gameObject.name != null ? cc.gameObject.name.ToLower() : "";
                if (name.Contains("fuelnozzle")) { fuelCollider = cc; break; }
            }

            if (fuelCollider == null) return true;  // no nozzle collider present, done

            if ((fuelCollider.center - FuelNozzleColliderCenter).sqrMagnitude <= 0.000001f)
                return true;  // already correct

            fuelCollider.center = FuelNozzleColliderCenter;
        #if MVRC_DEBUG
            PlayMakerHelpers.DebugLog("[VRReparent] FuelNozzle CapsuleCollider center set on '"
                + BuildHierarchyPath(fuelCollider.transform) + "' -> " + FuelNozzleColliderCenter);
        #endif
            return true;
        }

        private static string BuildHierarchyPath(Transform t)
        {
            if (t == null) return "null";

            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private void RestoreTargetRigidbodiesKinematic()
        {
            if (_targetRigidbodies == null || _targetRigidbodiesWereKinematic == null) return;

            int count = Mathf.Min(_targetRigidbodies.Length, _targetRigidbodiesWereKinematic.Length);
            for (int i = 0; i < count; i++)
            {
                Rigidbody rb = _targetRigidbodies[i];
                if (rb == null) continue;
                rb.isKinematic = _targetRigidbodiesWereKinematic[i];
            }

            _targetRigidbodies = null;
            _targetRigidbodiesWereKinematic = null;
        }
    
        // ── Helpers ───────────────────────────────────────────────────────────────
    
        private GameObject ResolveTarget()
        {
            if (targetFsm == null) return null;
            if (string.IsNullOrEmpty(ancestorName)) return targetFsm.gameObject;
    
            // Walk up the hierarchy
            Transform t = targetFsm.transform;
            while (t != null)
            {
                if (t.name == ancestorName) return t.gameObject;
                t = t.parent;
            }
    
            // Try a direct child
            Transform child = targetFsm.transform.Find(ancestorName);
            if (child != null) return child.gameObject;
    
            PlayMakerHelpers.DebugLog("[VRReparent] WARNING: ancestor '" + ancestorName
                + "' not found from '" + targetFsm.gameObject.name
                + "', falling back to FSM's own GO");
            return targetFsm.gameObject;
        }
    
        private Transform ResolveHand(GameObject resolvedTarget)
        {
            switch (handPreference)
            {
                case ReparentHandPreference.Right:
                    return VRHand.RightHandInstance != null
                        ? VRHand.RightHandInstance.transform
                        : VRProximitySphereDebug.RightHandTransform;
    
                case ReparentHandPreference.Left:
                    return VRHand.LeftHandInstance != null
                        ? VRHand.LeftHandInstance.transform
                        : null;
    
                default:
                    bool isRight = VRFsmActionBridge.ResolveIsRightHand(targetFsm, resolvedTarget);
                    if (isRight)
                        return VRHand.RightHandInstance != null
                            ? VRHand.RightHandInstance.transform
                            : VRProximitySphereDebug.RightHandTransform;
                    else
                        return VRHand.LeftHandInstance != null
                            ? VRHand.LeftHandInstance.transform
                            : null;
            }

        }
    }
    public class VRHandRaycastAction : VRInjectedAction
    {
        public enum RaySourceMode
        {
            RightHand,
            LeftHand,
            FPSCamera
        }

        public FsmGameObject storeResult;
        public FsmFloat storeDistance;
        public FsmBool storeDidHit;
        public RaySourceMode raySource = RaySourceMode.RightHand;
        public FsmFloat rayDistance;
        public FsmInt layerMask;
        public bool useGlobalLayerMask = false;
        public bool debugDrawRay = false;
        public Color debugColor = Color.red;
        public bool everyFrame = true;

        private FsmGameObject _storeResultVar;
        private float _raycastDistance = 0f;
        private Transform _cachedRayOrigin = null;
        private RaySourceMode _cachedRaySource = RaySourceMode.RightHand;
        private bool _loggedRayOriginWarning = false;
        private bool _loggedSceneLayer19Scan = false;
        private bool _loggedStartupDiagnostics = false;

        public override void OnEnter()
        {
            ResolveStoreResult();
            ResolveRaycastDistance();
#if MVRC_DEBUG
            LogStartupDiagnostics();
            LogLayer19Colliders();
#endif
            DoRaycast();

            if (!everyFrame)
                Finish();
        }

        public override void OnUpdate()
        {
            if (everyFrame)
                DoRaycast();
        }

        private void ResolveStoreResult()
        {
            _storeResultVar = storeResult;

            if (_storeResultVar == null && targetFsm != null)
                _storeResultVar = targetFsm.FsmVariables.GetFsmGameObject("PaintedPart");

            if (_storeResultVar == null)
            {
#if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRHandRaycastAction] WARNING: PaintedPart variable not found on "
                    + (targetFsm != null && targetFsm.gameObject != null ? targetFsm.gameObject.name : "<null>"));
#endif
            }
        }

        private void ResolveRaycastDistance()
        {
            float resolvedDistance = 0f;

            try
            {
                if (rayDistance != null && rayDistance.Value > 0f)
                {
                    resolvedDistance = rayDistance.Value;
                }
                else
                {
                    FsmFloat distanceVar = FsmVariables.GlobalVariables != null
                        ? FsmVariables.GlobalVariables.GetFsmFloat("PlayerRaycastDistance")
                        : null;

                    if (distanceVar != null && distanceVar.Value > 0f)
                        resolvedDistance = distanceVar.Value;
                    else
                        resolvedDistance = 5f;
                }
            }
            catch { }

            _raycastDistance = Mathf.Max(0f, resolvedDistance);
        }

        private Transform ResolveRayOrigin()
        {
            if (_cachedRayOrigin != null && _cachedRaySource == raySource)
                return _cachedRayOrigin;

            Transform resolved = null;

            if (raySource == RaySourceMode.LeftHand)
            {
                if (VRHand.LeftHandInstance != null)
                    resolved = VRHand.LeftHandInstance.transform;
            }
            else if (raySource == RaySourceMode.FPSCamera)
            {
                GameObject player = GameObject.Find("PLAYER");
                if (player != null)
                {
                    Transform fpsCamera = FindChildRecursive(player.transform, "FPSCamera");
                    if (fpsCamera != null)
                        resolved = fpsCamera;
                }
            }
            else
            {
                if (VRHand.RightHandInstance != null)
                    resolved = VRHand.RightHandInstance.transform;
                else
                    resolved = VRProximitySphereDebug.RightHandTransform;
            }

            if (resolved != null)
            {
                _cachedRayOrigin = resolved;
                _cachedRaySource = raySource;
                _loggedRayOriginWarning = false;

#if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRHandRaycastAction] Ray origin selected='" + resolved.name
                    + "' path='" + BuildHierarchyPath(resolved) + "' mode=" + raySource);
#endif
                return resolved;
            }

            if (!_loggedRayOriginWarning)
            {
                _loggedRayOriginWarning = true;
#if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRHandRaycastAction] WARNING: Ray origin not resolved for mode=" + raySource
                    + " fsm='" + GetFsmName() + "' state='" + GetStateName() + "'");
#endif
            }

            return null;
        }

        private void DoRaycast()
        {
            if (_storeResultVar == null)
                ResolveStoreResult();

            if (_storeResultVar == null && targetFsm != null)
                _storeResultVar = targetFsm.FsmVariables.GetFsmGameObject("PaintedPart");

            Transform rayOrigin = ResolveRayOrigin();
            if (rayOrigin == null)
                return;

            float resolvedDistance = _raycastDistance > 0f ? _raycastDistance : 5f;
            int mask = ResolveLayerMask();

            if (debugDrawRay)
                Debug.DrawRay(rayOrigin.position, rayOrigin.forward * resolvedDistance, debugColor);

            RaycastHit hit;
            bool didHit = Physics.Raycast(rayOrigin.position, rayOrigin.forward, out hit, resolvedDistance, mask);

            if (_storeResultVar != null)
                _storeResultVar.Value = didHit && hit.collider != null ? hit.collider.gameObject : null;

            if (storeDistance != null)
                storeDistance.Value = didHit ? hit.distance : 0f;

            if (storeDidHit != null)
                storeDidHit.Value = didHit;

            string rayInfo = string.Format(
                "[VRRaycast] origin={0} dir={1} dist={2} mask={3}",
                rayOrigin.position, rayOrigin.forward, resolvedDistance, mask);

            if (didHit)
            {
                PlayMakerHelpers.DebugLog(rayInfo + " HIT=" + hit.collider.gameObject.name
                    + " layer=" + hit.collider.gameObject.layer
                    + " tag=" + hit.collider.gameObject.tag);
            }
            else
            {
                // TODO: remove after diagnosis
                PlayMakerHelpers.DebugLog(rayInfo + " NO HIT");
            }
        }

        private void LogStartupDiagnostics()
        {
            if (_loggedStartupDiagnostics)
                return;

            _loggedStartupDiagnostics = true;

            PlayMakerHelpers.DebugLog("[VRHandRaycastAction] Startup diagnostics fsm='" + GetFsmName()
                + "' state='" + GetStateName() + "' storeResult=" + (_storeResultVar != null ? "resolved" : "null")
                + " rayDistance=" + _raycastDistance.ToString("0.###"));
        }

        private void LogLayer19Colliders()
        {
            if (_loggedSceneLayer19Scan)
                return;

            _loggedSceneLayer19Scan = true;

            Collider[] colliders = Object.FindObjectsOfType<Collider>();
            int count = 0;
            int limit = 10;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.gameObject == null)
                    continue;

                if (collider.gameObject.layer != 19)
                    continue;

                if (count < limit)
                {
                    PlayMakerHelpers.DebugLog("[VRHandRaycastAction] layer19[" + count + "] name='"
                        + collider.gameObject.name + "' layer=" + collider.gameObject.layer
                        + " tag='" + collider.gameObject.tag + "' hasCollider=true");
                }

                count++;
            }

            PlayMakerHelpers.DebugLog("[VRHandRaycastAction] layer19 collider count=" + count);
        }

        private int ResolveLayerMask()
        {
            if (useGlobalLayerMask)
            {
                try
                {
                    FsmInt globalMask = FsmVariables.GlobalVariables != null
                        ? FsmVariables.GlobalVariables.GetFsmInt("VRRaycastLayerMask")
                        : null;
                    if (globalMask != null)
                        return globalMask.Value;
                }
                catch { }
            }

            if (layerMask != null && layerMask.Value != 0)
                return layerMask.Value;

            return Physics.DefaultRaycastLayers;
        }

        private Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
                return null;

            if (root.name == childName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static string BuildHierarchyPath(Transform t)
        {
            if (t == null)
                return "null";

            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        private string GetFsmName()
        {
            if (targetFsm == null)
                return "<null>";

            if (!string.IsNullOrEmpty(targetFsm.FsmName))
                return targetFsm.FsmName;

            if (targetFsm.gameObject != null)
                return targetFsm.gameObject.name;

            return "<unnamed>";
        }

        private string GetStateName()
        {
            if (targetFsm == null)
                return "<null>";

            if (targetFsm.Fsm != null && targetFsm.Fsm.ActiveState != null)
                return targetFsm.Fsm.ActiveState.Name;

            if (!string.IsNullOrEmpty(targetFsm.ActiveStateName))
                return targetFsm.ActiveStateName;

            return "<null>";
        }
    }
    /// <summary>
    /// Custom FsmStateAction that reads a source transform's forward direction
    /// and writes it (optionally rotated by eulerOffset) into an FSM variable.
    /// </summary>
    public class VRVector3OverrideAction : FsmStateAction
    {
        public string variableName = "temp_suunta";
        public Transform source = null;
        public Vector3 eulerOffset = Vector3.zero;

        public override void OnEnter()
        {
            if (source != null)
            {
                Vector3 dir = Quaternion.Euler(eulerOffset) * source.forward;
                FsmVector3 vector3 = Fsm.Variables.GetFsmVector3(variableName);
                if (vector3 != null)
                    vector3.Value = dir;
            }

            Finish();
        }
    }
}
