// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using UnityEngine;
using MSCLoader;
using HutongGames.PlayMaker;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MVRC
{
    // ─────────────────────────────────────────────────────────────────────────────
    // VRInteractionMethod
    // Defines what kind of VR interaction replaces the original mouse/keyboard input.
    // ─────────────────────────────────────────────────────────────────────────────
    public enum VRInteractionMethod
    {
        /// <summary>
        /// Trigger press fires a FSM event while hand is near.
        /// Replaces: GetMouseButtonDown (left click).
        /// </summary>
        TriggerPress,

        /// <summary>
        /// Trigger down fires pressEvent; trigger release fires releaseEvent.
        /// Replaces: GetMouseButtonDown + GetMouseButtonUp pairs.
        /// </summary>
        TriggerHold,

        /// <summary>
        /// B button + trigger fires a FSM event (right-click equivalent).
        /// Replaces: GetMouseButtonDown (right button).
        /// </summary>
        TriggerRightClick,

        /// <summary>
        /// Laser pointer click for distant UI/menu interaction.
        /// </summary>
        LaserPointerPress,

        /// <summary>
        /// Controller rotation while trigger is held fires TIGHTEN / UNTIGHTEN.
        /// Replaces: GetAxis("Mouse ScrollWheel") on knobs, dials, etc.
        /// Note: for the MVRC bolt system, VRInteraction.SendBoltTighten handles
        /// bolt-specific tightening separately. This method is for generic knobs.
        /// </summary>
        KnobTwist,

        /// <summary>
        /// Fires a FSM event automatically when a hand enters proximity.
        /// No button press required. Replaces MousePickEvent / "Wait Player" states.
        /// </summary>
        ProximityAutoActivate,

        /// <summary>
        /// Fires a FSM event immediately when the state becomes active.
        /// Useful for keybind-gated states that should auto-advance in VR.
        /// </summary>
        StateEnterEvent,

        /// <summary>
        /// Fires a FSM event once when grip is pressed while the FSM is active.
        /// Useful for equip events that should only fire on intentional grip press, not state enter.
        /// </summary>
        GripPressEvent,

        /// <summary>
        /// Fires a FSM event once when grip is released while the FSM is active.
        /// Useful for actions that should fire on intentional release.
        /// </summary>
        GripReleaseEvent,
        /// <summary>
        /// Deactivates the currently held object while the FSM is active.
        /// Useful for hiding an equipped mesh without detaching the item.
        /// </summary>
        DeactivateHeldObject,
        /// <summary>
        /// Requires grip held AND trigger pressed simultaneously.
        /// For interactions that logically need "hold the object, then use it."
        /// </summary>
        GrabAndTrigger,

        /// <summary>
        /// Replaces MousePick in the PickUp FSM's "Look for object" state.
        /// Writes nearest pickable object into RaycastHitObject every frame.
        /// </summary>
        PickupProximity,

        /// <summary>
        /// Replaces SetParent in "Part picked" / "Tool picked" states.
        /// Parents the object to the VR hand instead of FPSCamera.
        /// </summary>
        PickupReparent,
        /// <summary>
        /// Automatically maps each disabled action to its VR equivalent:
        /// MousePickEvent → ProximityAutoActivate (fires mouseOver event)
        /// GetMouseButtonDown → TriggerPress (fires sendEvent)
        /// GetMouseButtonUp → TriggerRelease (fires sendEvent)
        /// </summary>
        AutoMap,

        SelfReparent,
        /// <summary>
        /// Reparents a target GameObject to the VR hand when the state is entered.
        /// Target is resolved by walking up/down the FSM's hierarchy for reparentAncestorName.
        /// Restores the original parent when the state exits (if reparentRestoreOnExit = true).
        /// </summary>
        ReparentToHand,
        /// <summary>
        /// Disables SetVector3Value action and replaces it with a custom action that reads
        /// a configurable transform's forward direction, with optional Euler angle offset.
        /// Used to fix throw directions by reading ladle/tool transform instead of player direction.
        /// </summary>
        Vector3TransformOverride,
        /// <summary>
        /// Replaces a continuous MousePick raycast with a custom per-frame raycast action.
        /// Used for states that keep a shared FsmGameObject updated every frame.
        /// </summary>
        CustomActionInject,
        /// <summary>
        /// Replaces only GetChild(childName="spray can(itemx)") with a one-shot
        /// write of the right-hand held object into the same storeResult variable.
        /// </summary>
        SprayCanHeldObjectRedirect,
        /// <summary>
        /// Disables matched actions and injects nothing.
        /// Use when the original action simply needs to be silenced with no VR replacement.
        /// </summary>
        NoAction,

        /// <summary>
        /// Disables matched actions and appends a caller-supplied action after the disable pass.
        /// </summary>
        InjectAction,

        /// <summary>
        /// Disables matched actions and injects nothing.
        /// Use when the original action simply needs to be silenced with no VR replacement.
        /// </summary>
        DisableOnly,
    }

    public enum ReparentHandPreference
    {
        /// <summary>Uses whichever hand currently owns the ActiveInteractionTarget slot.</summary>
        ActiveTarget,
        /// <summary>Always reparents to the right hand.</summary>
        Right,
        /// <summary>Always reparents to the left hand.</summary>
        Left,
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // DisableActionFlags
    // Bitmask specifying which original action types to silence in the target state.
    // NEVER delete actions. Only disable them via action.Enabled = false.
    // ─────────────────────────────────────────────────────────────────────────────
    [System.Flags]
    public enum DisableActionFlags
    {
        None = 0,
        MousePickEvent = 1 << 0,   // MousePickEvent: hover/click event from the mouse cursor
        GetMouseButtonDown = 1 << 1,   // Left or right mouse button down
        GetMouseButtonUp = 1 << 2,   // Left or right mouse button up
        GetMouseButton = GetMouseButtonDown | GetMouseButtonUp,
        GetAxis = 1 << 3,   // GetAxis("Mouse ScrollWheel") etc.
        GetButtonDown = 1 << 4,   // GetButtonDown("Use", "Fire1", etc.)
        SetParent = 1 << 5,   // SetParent: disable to prevent reparenting to FPSCamera
        MousePick = 1 << 6,   // MousePick: raycast from the mouse that stores the hit in variables
        FloatCompare = 1 << 7,
        GetKeyDown = 1 << 8,
        SetJointConnectedBody = 1 << 9,
        GetChild = 1 << 10,
        WaitAction = 1 << 11,
        SetMainCamera = 1 << 12,
        ActivateGameObject = 1 << 13,
        SetVector3Value = 1 << 14,
        PlayAnimation = 1 << 15,
        SetVelocity = 1 << 16,
        AddForce = 1 << 17,
        FloatAdd = 1 << 18,
        FloatMultiply = 1 << 19,
        FloatOperator = 1 << 20,
        Vector3Multiply = 1 << 21,
        Vector3Normalize = 1 << 22,
        All = MousePickEvent | GetMouseButton | GetAxis | GetButtonDown | SetParent | GetKeyDown | FloatCompare | SetJointConnectedBody | GetChild | WaitAction | SetMainCamera | ActivateGameObject | MousePick | SetVector3Value | PlayAnimation | SetVelocity | AddForce | FloatAdd | FloatMultiply | FloatOperator | Vector3Multiply | Vector3Normalize,
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRFsmRule
    // One entry in the patching registry.  All filter lists are OR'd within the
    // list and AND'd across list types:
    //   objectNameContains=["door","handle"] fsmNameContains=["interact"]
    //   → match if (name has "door" OR "handle") AND (fsmName has "interact")
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRFsmRule
    {
        public class VRRaycastConfig
        {
            // Ray source
            public VRHandRaycastAction.RaySourceMode raySource
                = VRHandRaycastAction.RaySourceMode.RightHand;

            // Layer mask as a raw bitmask (e.g. 1 << 19 for Parts).
            // 0 means "use Physics.DefaultRaycastLayers"
            public int layerMask = 0;

            // Ray distance. 0 means "read PlayerRaycastDistance global at runtime"
            public float rayDistance = 0f;

            // Name of the FsmGameObject variable on the target FSM to write the
            // hit result into. Leave empty to use "PaintedPart" as default.
            public string storeResultVariableName = "PaintedPart";

            // Optional: name of FsmFloat variable to write hit distance into.
            // Leave empty to skip.
            public string storeDistanceVariableName = "";

            // Optional: name of FsmBool variable to write hit/miss bool into.
            // Leave empty to skip.
            public string storeDidHitVariableName = "";

            // Debug ray drawn in Scene view. Safe to leave true during development.
            public bool debugDrawRay = false;
            public Color debugColor = Color.red;
        }

        // ── Filters ───────────────────────────────────────────────────────────────

        /// <summary>
        /// GameObject.name must contain at least one of these strings (case-insensitive).
        /// Leave empty to skip this filter.
        /// </summary>
        public List<string> objectNameContains = new List<string>();

        /// <summary>
        /// FSM.FsmName must contain at least one of these strings (case-insensitive).
        /// Leave empty to skip this filter.
        /// </summary>
        public List<string> fsmNameContains = new List<string>();

        /// <summary>
        /// Only patch states whose name contains at least one of these strings.
        /// Leave empty to patch ALL states in the matched FSM.
        /// </summary>
        public List<string> stateNameContains = new List<string>();

        /// <summary>
        /// Full or partial hierarchy paths that the GameObject path must contain.
        /// E.g. "PLAYER/Pivot" to match only objects inside the player hierarchy.
        /// Leave empty to skip this filter.
        /// </summary>
        public List<string> hierarchyPathContains = new List<string>();

        /// <summary>
        /// Exact GameObject names to block from matching even if other filters pass.
        /// Useful to exclude one object from a broad category filter.
        /// </summary>
        public List<string> excludeObjectNames = new List<string>();

        // ── Patch behaviour ───────────────────────────────────────────────────────

        /// <summary>What VR interaction replaces the disabled action(s).</summary>
        public VRInteractionMethod method = VRInteractionMethod.TriggerPress;

        /// <summary>Which original action types to disable in matched states.</summary>
        public DisableActionFlags disableFlags =
            DisableActionFlags.MousePickEvent | DisableActionFlags.GetMouseButtonDown;

        /// <summary>
        /// Action appended after the disable pass when method == InjectAction.
        /// </summary>
        public FsmStateAction injectedAction = null;

        /// <summary>
        /// FSM event name the injected action fires.
        /// If null/empty, the patcher auto-detects the event from the disabled action's
        /// sendEvent field via reflection. Falls back to "FINISHED" if not found.
        /// </summary>
        public string eventToFire = null;

        /// <summary>
        /// For TriggerHold only: the event fired on trigger release.
        /// Auto-detected from GetMouseButtonUp if null.
        /// </summary>
        public string releaseEventToFire = null;

        /// <summary>Stop after patching the first state that matches stateNameContains.</summary>
        public bool patchFirstStateOnly = false;

        /// <summary>
        /// If true, skip this FSM if it has already been patched by an earlier rule.
        /// Prevents double-injection when multiple rules could match the same FSM.
        /// </summary>
        public bool skipIfAlreadyPatched = false;

        /// <summary>Human-readable label shown in the patch log.</summary>
        public string debugLabel = "";

        /// <summary>
        /// When true, registers this FSM's GameObject as assembly-priority so it can
        /// win dispatcher selection while grip is held.
        /// </summary>
        public bool markAsGripRequired = false;
        /// <summary>
        /// Name of the ancestor (or immediate child) to reparent.
        /// Walk UP from the FSM's GameObject until a Transform with this name is found.
        /// If not found upward, a direct child with this name is tried.
        /// Leave null/empty to reparent the FSM's own GameObject.
        /// </summary>
        public string reparentAncestorName = null;
        
        /// <summary>Local position applied to the object after reparenting.</summary>
        public Vector3 reparentLocalOffset = Vector3.zero;
        
        /// <summary>Local euler rotation applied to the object after reparenting.</summary>
        public Vector3 reparentLocalRotation = Vector3.zero;
        
        /// <summary>
        /// If true (default), restores the original parent transform when the state exits.
        /// Set false for permanent reparents (e.g. items that stay in hand until explicitly dropped).
        /// </summary>
        public bool reparentRestoreOnExit = true;
        
        /// <summary>Which hand to attach to. Default: the hand closest to the object (ActiveTarget).</summary>
        public ReparentHandPreference reparentHand = ReparentHandPreference.ActiveTarget;

        /// <summary>
        /// Registers this FSM as a held-object interaction candidate.
        /// Use for objects that are reparented to the hand but still need trigger actions
        /// while held, even when they have no MousePickEvent-based proximity actions.
        /// </summary>
        public bool allowHeldInteractable = false;

        /// <summary>
        /// If true, injected trigger/right-click actions may read controller input
        /// even when this FSM is not the current ActiveInteractionTarget.
        /// Use for held items whose Use FSM should work regardless of proximity lock.
        /// </summary>
        public bool allowInputWithoutActiveTarget = false;

        /// <summary>
        /// If true, proximity actions injected by this rule may activate when the object
        /// is inside the hand interaction radius even if it is not the strict closest target.
        /// Default false keeps single-closest behavior.
        /// </summary>
        public bool proximityAnyInRange = false;

        /// <summary>
        /// If true, this object's FSM can still run proximity actions but is excluded
        /// from ActiveInteractionTarget selection.
        /// </summary>
        public bool excludeFromActiveTargetSelection = false;

        // Populated only when method == VRInteractionMethod.CustomActionInject
        // and a raycast action is desired. Leave null for non-raycast injections.
        public VRRaycastConfig raycastConfig = null;

        /// <summary>Ray origin used by CustomActionInject.</summary>
        public VRHandRaycastAction.RaySourceMode raySource = VRHandRaycastAction.RaySourceMode.RightHand;

        /// <summary>Optional ray distance override used by CustomActionInject.</summary>
        public FsmFloat rayDistance = null;

        /// <summary>Optional raw layer-mask override used by CustomActionInject.</summary>
        public FsmInt layerMask = null;

        /// <summary>When true, CustomActionInject reads VRRaycastLayerMask from global FSM vars.</summary>
        public bool useGlobalLayerMask = false;

        /// <summary>Enables scene-view ray drawing for CustomActionInject.</summary>
        public bool debugDrawRay = true;

        /// <summary>Scene-view ray color for CustomActionInject.</summary>
        public Color debugColor = Color.red;

        /// <summary>
        /// Optional active-state name filters for exclusion. When non-empty, exclusion
        /// is applied only while the FSM active state name contains one of these
        /// values (case-insensitive). Prefix a value with '=' for exact state-name
        /// matching (for example "=Wait player"). When empty, exclusion applies in all
        /// states.
        /// </summary>

        public List<string> excludeFromActiveTargetStateNameContains = new List<string>();

        /// <summary>
        /// After patching, briefly activates the GameObject (and its ancestor chain)
        /// then restores original active states. Forces PlayMaker's first-init to run
        /// with the patch already applied, so disabled actions stay disabled.
        /// </summary>
        public bool activateAndRestoreAfterPatch = false;

        /// <summary>
        /// Optional: path relative to scene root to find the source transform.
        /// If empty or null, the patcher uses fsm.gameObject.transform (the FSM's own object).
        /// Used by Vector3TransformOverride to read the direction source.
        /// </summary>
        public string directionTransformPath = null;

        /// <summary>
        /// Euler offset applied to the source transform's forward before writing.
        /// Start at (0, 0, 0) and tune at runtime to correct the throw angle.
        /// Used by Vector3TransformOverride.
        /// </summary>
        public Vector3 directionEulerOffset = Vector3.zero;
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // VRFsmPatcher
    // Central engine.  Call VRFsmPatcher.RunAllPatches() from a Coroutine triggered
    // in your MSCLoader OnLoad() / PostLoad().
    //
    // Typical setup in your Mod class:
    //
    //   public override void OnLoad()
    //   {
    //       VRInjectionConfig.RegisterDefaultRules();   // add your rules first
    //       MelonCoroutines.Start(VRFsmPatcher.RunPatchesAfterDelay(this));
    //       // or use your own coroutine runner:
    //       // yourMonoBehaviour.StartCoroutine(VRFsmPatcher.RunPatchesAfterDelay(yourMonoBehaviour));
    //   }
    // ─────────────────────────────────────────────────────────────────────────────
    public static class VRFsmPatcher
    {
        // ── Public rule registry ──────────────────────────────────────────────────

        /// <summary>All rules applied during RunAllPatches(). Populate before calling.</summary>
        public static readonly List<VRFsmRule> Rules = new List<VRFsmRule>();
        public static bool PatcherActive { get; private set; } = false;
        // ── Internal state ────────────────────────────────────────────────────────

        private static bool _hasPatched = false;
        private static int _fsmsPatchedCount = 0;
        private static int _actionsInjected = 0;
        private static int _actionsDisabled = 0;
        private static readonly Dictionary<int, int> _runtimeAttemptFrameByFsmId = new Dictionary<int, int>();
        private static int _lastRetryFrame = -1;
        private const int MaxPendingPatchesPerFrame = 8;
        private const int MaxRetryBackoffFrames = 30;

        private static bool ShouldEnableInRangeActivation(VRFsmRule rule, GameObject go)
        {
            if (rule != null && rule.proximityAnyInRange)
                return true;

            if (go == null || string.IsNullOrEmpty(go.name))
                return false;

            string n = go.name.ToLower();
            return n.Contains("seatbelt");
        }

        // Track which FSMs have been patched to support skipIfAlreadyPatched
        public static readonly HashSet<PlayMakerFSM> _patchedFsms = new HashSet<PlayMakerFSM>();
        public static readonly HashSet<PlayMakerFSM> PatchedKnobFsms = new HashSet<PlayMakerFSM>();

        // ── Entry points ──────────────────────────────────────────────────────────
        /// <summary>
        /// Coroutine wrapper: waits 2 frames then calls RunAllPatches().
        /// This delay is required because PlayMaker FSMs are not fully initialised on
        /// the same frame as MSCLoader's OnLoad().
        /// </summary>
        public static IEnumerator RunPatchesAfterDelay(MonoBehaviour host)
        {
            yield return null;
            yield return null;
            RunAllPatches();
        }
        private struct PendingPatch
        {
            public PlayMakerFSM fsm;
            public VRFsmRule    rule;
            public int nextRetryFrame;
            public int attemptCount;
        }
        private static readonly List<PendingPatch> _pendingPatches = new List<PendingPatch>();

        private static void EnqueuePendingPatch(PlayMakerFSM fsm, VRFsmRule rule)
        {
            for (int i = 0; i < _pendingPatches.Count; i++)
            {
                PendingPatch existing = _pendingPatches[i];
                if (existing.fsm == fsm && existing.rule == rule)
                    return;
            }

            _pendingPatches.Add(new PendingPatch
            {
                fsm = fsm,
                rule = rule,
                nextRetryFrame = Time.frameCount + 1,
                attemptCount = 0,
            });
        }

        private static void RemovePendingPatchesForFsm(PlayMakerFSM fsm)
        {
            if (fsm == null || _pendingPatches.Count == 0) return;

            for (int i = _pendingPatches.Count - 1; i >= 0; i--)
            {
                if (_pendingPatches[i].fsm == fsm)
                    _pendingPatches.RemoveAt(i);
            }
        }
        /// <summary>
        /// Call once per frame (right hand only) from VRHand.Update().
        /// Drains the pending queue for any FSMs whose GameObjects are now active.
        /// Handles all objects that were inactive at startup, including the fuel nozzle, fists,
        /// held items, and anything else, with no per-object special-casing needed.
        /// </summary>
        public static void RetryPendingPatches()
        {
            if (_pendingPatches.Count == 0) return;

            int frame = Time.frameCount;
            if (_lastRetryFrame == frame) return;
            _lastRetryFrame = frame;

            // Process a stable snapshot in insertion order so specific rules run
            // before broad catch-all rules, matching startup/runtime patch order.
            PendingPatch[] snapshot = _pendingPatches.ToArray();
            _pendingPatches.Clear();

            int processedThisFrame = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                PendingPatch p = snapshot[i];

                if (processedThisFrame >= MaxPendingPatchesPerFrame)
                {
                    _pendingPatches.Add(p);
                    continue;
                }

                if (p.nextRetryFrame > frame)
                {
                    _pendingPatches.Add(p);
                    continue;
                }

                // Stale reference; the object was destroyed.
                if (p.fsm == null || p.fsm.gameObject == null)
                {
                    continue;
                }

                // Keep skip semantics consistent across startup/runtime/pending paths.
                if (p.rule != null && p.rule.skipIfAlreadyPatched && _patchedFsms.Contains(p.fsm))
                    continue;

                // Still inactive; requeue for a later frame.
                if (!p.fsm.gameObject.activeInHierarchy)
                {
                    p.attemptCount++;
                    int backoffFrames = 1 << (p.attemptCount > 4 ? 4 : p.attemptCount);
                    if (backoffFrames > MaxRetryBackoffFrames)
                        backoffFrames = MaxRetryBackoffFrames;
                    p.nextRetryFrame = frame + backoffFrames;
                    _pendingPatches.Add(p);
                    continue;
                }

                ApplyRule(p.fsm, p.rule);
                processedThisFrame++;
            }
        }

        
        private static string ReadEventField(FsmStateAction action, string fieldName)
        {
            try
            {
                FieldInfo fi = action.GetType().GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null) return null;
                object val = fi.GetValue(action);
                if (val == null) return null;
                if (val is FsmEvent) return ((FsmEvent)val).Name;
                if (val is FsmString) return ((FsmString)val).Value;
                return val.ToString();
            }
            catch { return null; }
        }

        public static FsmStateAction[] GetActionsRaw(FsmState state)
        {
            // Bypass PlayMaker's "Fsm not initialized" guard on the Actions property
            // by reading the backing field directly.
            try
            {
                FieldInfo fi = typeof(FsmState).GetField("actions",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null)
                    fi = typeof(FsmState).GetField("_actions",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null)
                    return fi.GetValue(state) as FsmStateAction[];
            }
            catch { }
            return null;
        }
        
        /// <summary>
        /// Public entry point for lazy/on-demand patching of a single FSM.
        /// Use this for objects that are inactive at startup (e.g. player-body nozzles,
        /// fists, held items) that would fail InitializeFSM() in the startup scan.
        /// </summary>
        public static bool ApplyRulePublic(PlayMakerFSM fsm, VRFsmRule rule)
        {
            if (fsm == null || rule == null) return false;
            // Don't re-patch if skipIfAlreadyPatched and already done
            if (rule.skipIfAlreadyPatched && _patchedFsms.Contains(fsm)) return false;
            if (!EnsureInitialized(fsm)) return false;
            ApplyRule(fsm, rule);
            return _patchedFsms.Contains(fsm);
        }

        /// <summary>
        /// Event-driven runtime entry point for FSMs that are created/enabled after
        /// the startup scene scan has completed.
        /// </summary>
        public static bool TryPatchRuntimeFsm(PlayMakerFSM fsm)
        {
            // Guard against startup double-injection. Runtime patching starts only
            // after the initial RunAllPatches() pass has completed.
            if (!_hasPatched) return false;
            if (fsm == null || fsm.gameObject == null) return false;
            if (Rules.Count == 0) return false;

            // If this FSM has already been patched, we are done.
            if (_patchedFsms.Contains(fsm)) return true;

            int id;
            try { id = fsm.GetInstanceID(); }
            catch { return false; }

            // Deduplicate multiple lifecycle callbacks (e.g., Awake + OnEnable)
            // firing on the same frame for the same FSM instance.
            int frame = Time.frameCount;
            int lastFrame;
            if (_runtimeAttemptFrameByFsmId.TryGetValue(id, out lastFrame) && lastFrame == frame)
                return _patchedFsms.Contains(fsm);

            _runtimeAttemptFrameByFsmId[id] = frame;

            if (!EnsureInitialized(fsm))
                return false;

            bool matchedAny = false;
            foreach (VRFsmRule rule in Rules)
            {
                if (rule == null) continue;
                if (rule.skipIfAlreadyPatched && _patchedFsms.Contains(fsm)) continue;
                if (!MatchesRule(fsm, rule)) continue;

                matchedAny = true;
                ApplyRule(fsm, rule);
            }

            bool patched = _patchedFsms.Contains(fsm);
            if (patched)
            {
                // Runtime lifecycle hooks can patch an FSM before pending retries run.
                // Clear stale pending entries for this FSM to prevent double injection.
                RemovePendingPatchesForFsm(fsm);
                PlayMakerHelpers.DebugLog("[VRFsmPatcher] Runtime patched '" + fsm.gameObject.name
                    + "' / FSM='" + fsm.FsmName + "'");
            }
            else if (matchedAny)
            {
                PlayMakerHelpers.DebugLog("[VRFsmPatcher] Runtime match found but no patch applied yet for '"
                    + fsm.gameObject.name + "' / FSM='" + fsm.FsmName + "'");
            }

            return patched;
        }
        
        /// <summary>
        /// Scan every PlayMakerFSM in the scene, apply all registered rules.
        /// Safe to call multiple times. Subsequent calls are no-ops unless Reset() was called.
        /// </summary>
        public static void RunAllPatches()
        {
            
            if (_hasPatched)
            {
                PlayMakerHelpers.DebugLog("[VRFsmPatcher] Already patched. Call Reset() to re-patch. "
                    + "PatcherActive=" + PatcherActive + " PatchedCount=" + _fsmsPatchedCount);
                return;
            }
            _hasPatched = true;
            _fsmsPatchedCount = 0;
            _actionsInjected = 0;
            _actionsDisabled = 0;
            _patchedFsms.Clear();

            if (Rules.Count == 0)
            {
                PlayMakerHelpers.DebugLog("[VRFsmPatcher] No rules registered. Add rules to VRFsmPatcher.Rules.");
                return;
            }

            PlayMakerFSM[] allFsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
            PlayMakerHelpers.DebugLog("[VRFsmPatcher] Scanning " + allFsms.Length + " FSMs (including inactive)... "
                + Rules.Count + " rule(s)...");

            foreach (PlayMakerFSM fsm in allFsms)
            {
                if (fsm == null || fsm.gameObject == null) continue;
                if (!EnsureInitialized(fsm)) continue;
                foreach (VRFsmRule rule in Rules)
                {
                    if (rule.skipIfAlreadyPatched && _patchedFsms.Contains(fsm)) continue;
                    if (MatchesRule(fsm, rule))
                        ApplyRule(fsm, rule);
                }
            }

            PlayMakerHelpers.DebugLog("[VRFsmPatcher] DONE. Rules=" + Rules.Count
                + " Patched=" + _fsmsPatchedCount
                + " Disabled=" + _actionsDisabled
                + " Injected=" + _actionsInjected);
            PatcherActive = true;


            PlayMakerHelpers.DebugLog("[VRFsmPatcher] DONE. PatcherActive = true");
        }
        public static int PendingPatchCount => _pendingPatches.Count;
        /// <summary>Allow RunAllPatches() to run again (e.g. after a scene reload).</summary>
        public static void Reset()
        {
            _hasPatched = false;
            _fsmsPatchedCount = 0;
            _patchedFsms.Clear();
            PatchedKnobFsms.Clear();
            _pendingPatches.Clear();
            _runtimeAttemptFrameByFsmId.Clear();
            _lastRetryFrame = -1;
            VRFsmActionBridge.Reset();   
        }

        // ── Rule matching ─────────────────────────────────────────────────────────

        private static bool MatchesRule(PlayMakerFSM fsm, VRFsmRule rule)
        {
            bool hasObjFilter = rule.objectNameContains.Count > 0;
            bool hasFsmFilter = rule.fsmNameContains.Count > 0;
            bool hasPathFilter = rule.hierarchyPathContains.Count > 0;
            
            // A rule with no filters at all matches nothing (safety guard)
            if (!hasObjFilter && !hasFsmFilter && !hasPathFilter) return false;

            string goName = fsm.gameObject.name;

            // Exclusion list checked first regardless of other filters
            foreach (string ex in rule.excludeObjectNames)
                if (string.Compare(goName, ex, System.StringComparison.OrdinalIgnoreCase) == 0)
                    return false;

            // Object name filter
            if (hasObjFilter && !AnyContainsCI(goName, rule.objectNameContains))
            {
                return false;
            }
            // FSM name filter
            if (hasFsmFilter && !AnyContainsCI(fsm.FsmName ?? "", rule.fsmNameContains))
            {
                return false;
            }

            // Hierarchy path filter
            if (hasPathFilter)
            {
                string path = BuildPath(fsm.gameObject);
                if (!AnyContainsCI(path, rule.hierarchyPathContains)) return false;
            }
            return true;
        }

        private static bool AnyContainsCI(string haystack, List<string> needles)
        {
            string h = haystack.ToLower();
            foreach (string n in needles)
                if (h.Contains(n.ToLower())) return true;
            return false;
        }

        public static bool EnsureInitialized(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject == null)
                return false;

            if (fsm.Fsm != null)
                return true;

            List<Transform> toRestore = new List<Transform>();
            Transform t = fsm.transform;
            while (t != null)
            {
                if (!t.gameObject.activeSelf)
                    toRestore.Add(t);
                t = t.parent;
            }

            for (int i = toRestore.Count - 1; i >= 0; i--)
                toRestore[i].gameObject.SetActive(true);

            try
            {
                fsm.InitializeFSM();
            }
            catch
            {
            }

            for (int i = 0; i < toRestore.Count; i++)
                toRestore[i].gameObject.SetActive(false);

            return fsm.Fsm != null;
        }

        private static string BuildPath(GameObject go)
        {
            string path = go.name;
            Transform t = go.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return path;
        }

        // ── Rule application ──────────────────────────────────────────────────────

        private static void ApplyRule(PlayMakerFSM fsm, VRFsmRule rule)
        {
            if (!EnsureInitialized(fsm))
                return;

            try { var _ = fsm.FsmStates; } catch { return; }
            if (fsm.Fsm == null) return;
            try { var _ = fsm.FsmName; } catch { return; } // secondary guard
            string label = string.IsNullOrEmpty(rule.debugLabel)
                ? rule.method.ToString()
                : rule.debugLabel;

            bool patched = false;

            foreach (FsmState state in fsm.FsmStates)
            {
                // State name filter
                if (rule.stateNameContains.Count > 0
                    && !AnyContainsCI(state.Name, rule.stateNameContains))
                    continue;

                if (PatchState(fsm, state, rule))
                {
                    patched = true;
                    if (rule.patchFirstStateOnly) break;
                }
            }

            if (patched)
            {
                _fsmsPatchedCount++;
                _patchedFsms.Add(fsm);
                if (rule.method == VRInteractionMethod.LaserPointerPress)
                    VRLaserPointer.RegisterTarget(fsm.gameObject);
                if (rule.excludeFromActiveTargetSelection)
                {
                    VRProximityRegistry.NonSelectableInteractionFsms.Add(fsm);
                    if (rule.excludeFromActiveTargetStateNameContains != null
                        && rule.excludeFromActiveTargetStateNameContains.Count > 0)
                    {
                        VRProximityRegistry.NonSelectableInteractionStateNameContains[fsm]
                            = new List<string>(rule.excludeFromActiveTargetStateNameContains);
                    }
                }
                if (rule.markAsGripRequired)
                {
                    VRProximityRegistry.GripRequiredObjects.Add(fsm.gameObject);
                    VRProximityRegistry.GripRequiredFsms[fsm.gameObject] = fsm;
                }
                if (rule.allowHeldInteractable)
                {
                    VRProximityRegistry.RegisterHeldInteractable(fsm.gameObject, fsm);
                }
                PlayMakerHelpers.DebugLog("[VRFsmPatcher] ✓ '" + fsm.gameObject.name
                    + "' / FSM='" + fsm.FsmName + "' [" + label + "]");
                if (fsm.Fsm != null && !fsm.Fsm.Started)
                {
                    fsm.SendEvent("MVRC_INIT");  // nonexistent event; wakes the FSM without transitioning
                    if (!fsm.Fsm.Started)
                        fsm.Fsm.Start();
                }
            }
            if (patched && rule.activateAndRestoreAfterPatch)
            {
                List<GameObject> toRestore = new List<GameObject>();
                Transform t = fsm.transform.parent;
                while (t != null)
                {
                    if (!t.gameObject.activeSelf)
                        toRestore.Add(t.gameObject);
                    t = t.parent;
                }

                for (int i = toRestore.Count - 1; i >= 0; i--)
                    toRestore[i].SetActive(true);

                bool selfWasActive = fsm.gameObject.activeSelf;
                fsm.gameObject.SetActive(true);

                fsm.gameObject.SetActive(selfWasActive);
                for (int i = 0; i < toRestore.Count; i++)
                    toRestore[i].SetActive(false);
            }
        }

        // ── State patching ────────────────────────────────────────────────────────

        private static bool PatchState(PlayMakerFSM fsm, FsmState state, VRFsmRule rule)
        {
            FsmStateAction[] actions = GetActionsRaw(state);

            if (actions == null) return false;

            bool madeChange = false;
            bool mouseButtonWasDisabled = false;
            string laserClickEvent = null;
            string laserHoverEvent = null;
            int actionIndex = 0;

            foreach (FsmStateAction action in actions)
            {
                if (action == null) continue;
                string typeName = action.GetType().Name;

                bool disable = false;

                if ((rule.disableFlags & DisableActionFlags.MousePickEvent) != 0
                    && typeName == "MousePickEvent")
                {
                    disable = true;

                    if (rule.method == VRInteractionMethod.LaserPointerPress)
                    {
                        string clickEvent = ReadEventField(action, "mouseDown")
                            ?? ReadEventField(action, "mouseDownEvent")
                            ?? ReadEventField(action, "sendEvent");
                        string hoverEvent = ReadEventField(action, "mouseOver");

                        if (!string.IsNullOrEmpty(clickEvent)
                            && clickEvent != "None"
                            && string.IsNullOrEmpty(laserClickEvent))
                        {
                            laserClickEvent = clickEvent;
                        }

                        if (!string.IsNullOrEmpty(hoverEvent)
                            && hoverEvent != "None"
                            && string.IsNullOrEmpty(laserHoverEvent))
                        {
                            laserHoverEvent = hoverEvent;
                        }
                    }

                    if (rule.method != VRInteractionMethod.LaserPointerPress)
                    {
                        string mouseOverEv = ReadEventField(action, "mouseOver");
                        string mouseOffEv = ReadEventField(action, "mouseOff")
                                          ?? ReadEventField(action, "mouseExit");

                        if (!string.IsNullOrEmpty(mouseOverEv)
                            && mouseOverEv != "None"
                            && mouseOverEv != "")
                        {
                            var pa = new VRProximityActivateAction
                            {
                                targetFsm = fsm,
                                fsmEventName = mouseOverEv,
                                AllowInRangeActivation = ShouldEnableInRangeActivation(rule, fsm != null ? fsm.gameObject : null),
                            };
                            AppendAction(state, pa);
                            VRProximityRegistry.RegisterActivate(fsm.gameObject, pa);
                            _actionsInjected++;
                        }

                        if (!string.IsNullOrEmpty(mouseOffEv)
                            && mouseOffEv != "None"
                            && mouseOffEv != "")
                        {
                            var pl = new VRProximityLeaveAction
                            {
                                targetFsm = fsm,
                                fsmEventName = mouseOffEv,
                                AllowInRangeActivation = ShouldEnableInRangeActivation(rule, fsm != null ? fsm.gameObject : null),
                            };
                            AppendAction(state, pl);
                            VRProximityRegistry.RegisterLeave(fsm.gameObject, pl);
                            _actionsInjected++;
                        }
                    }
                }

                if ((rule.disableFlags & DisableActionFlags.SetJointConnectedBody) != 0
                    && typeName == "SetJointConnectedBody")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.SetVelocity) != 0
                    && typeName == "SetVelocity")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.AddForce) != 0
                    && typeName == "AddForce")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.WaitAction) != 0
                    && (typeName == "Wait"))
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.WaitAction) != 0
                    && (typeName == "WaitRealTime"))
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.MousePick) != 0
                    && typeName == "MousePick")
                {
                    if (rule.method == VRInteractionMethod.CustomActionInject)
                    {
                        FsmStateAction customAction = BuildCustomActionInjectAction(fsm, rule);
                        if (customAction != null)
                        {
                            disable = true;
                            InsertActionAt(state, actionIndex, customAction);
                            _actionsInjected++;
                        }
                    }
                    else
                    {
                        disable = true;
                    }
                }

                if ((rule.disableFlags & DisableActionFlags.SetMainCamera) != 0
                    && typeName == "SetMainCamera")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.ActivateGameObject) != 0
                    && typeName == "ActivateGameObject")
                {
                    disable = true;
                    madeChange = true;
                }

                if ((rule.disableFlags & DisableActionFlags.GetMouseButtonDown) != 0
                    && typeName == "GetMouseButtonDown")
                {
                    disable = true;
                    mouseButtonWasDisabled = true;
                    if (rule.method == VRInteractionMethod.LaserPointerPress
                        && string.IsNullOrEmpty(laserClickEvent))
                    {
                        laserClickEvent = ReadEventField(action, "sendEvent")
                            ?? ReadEventField(action, "trueEvent");
                    }
                    if (rule.method == VRInteractionMethod.AutoMap)
                    {
                        string ev = ReadEventField(action, "sendEvent")
                                 ?? ReadEventField(action, "trueEvent")
                                 ?? "FINISHED";

                        bool isRightClick = false;
                        try
                        {
                            FieldInfo btnField = action.GetType().GetField("button",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (btnField != null)
                                isRightClick = (btnField.GetValue(action) != null
                                             && (int)btnField.GetValue(action) == 1);
                        }
                        catch { }

                        string evUpper = ev.ToUpper();
                        bool isDrop = evUpper.Contains("DROP");
                        bool isThrow = evUpper.Contains("THROW");

                        if (isThrow)
                        {
                            bool preferRightClick = false;
                            try
                            {
                                string goName = fsm != null && fsm.gameObject != null ? fsm.gameObject.name.ToLower() : "";
                                string fsmName = fsm != null ? (fsm.FsmName ?? "").ToLower() : "";
                                if (goName.Contains("saunathrow") && fsmName == "use")
                                    preferRightClick = true;
                            }
                            catch { }

                            if (!preferRightClick)
                            {
                                AppendAction(state, new VRGripReleaseAction
                                {
                                    targetFsm = fsm,
                                    fsmEventName = ev,
                                    allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                                });
                            }
                            else if (isRightClick)
                            {
                                AppendAction(state, new VRTriggerRightClickAction
                                {
                                    targetFsm = fsm,
                                    fsmEventName = ev,
                                    allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                                });
                            }
                            else
                            {
                                AppendAction(state, new VRTriggerPressAction
                                {
                                    targetFsm = fsm,
                                    fsmEventName = ev,
                                    allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                                });
                            }
                        }
                        else if (isRightClick)
                        {
                            AppendAction(state, new VRTriggerRightClickAction
                            {
                                targetFsm = fsm,
                                fsmEventName = ev,
                                allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                            });
                        }
                        else
                        {
                            AppendAction(state, new VRTriggerPressAction
                            {
                                targetFsm = fsm,
                                fsmEventName = ev,
                                allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                            });
                        }
                        _actionsInjected++;
                    }
                }

                if ((rule.disableFlags & DisableActionFlags.GetKeyDown) != 0
                    && typeName == "GetKeyDown")
                {
                    disable = true;
                }

                if ((rule.disableFlags & DisableActionFlags.GetMouseButtonUp) != 0
                    && typeName == "GetMouseButtonUp")
                {
                    disable = true;
                    mouseButtonWasDisabled = true;
                    if (rule.method == VRInteractionMethod.AutoMap)
                    {
                        string ev = ReadEventField(action, "sendEvent") ?? "FINISHED";
                        AppendAction(state, new VRTriggerReleaseAction
                        {
                            targetFsm = fsm,
                            fsmEventName = ev,
                            allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                        });
                        _actionsInjected++;
                    }
                }

                if ((rule.disableFlags & DisableActionFlags.GetAxis) != 0
                    && typeName == "GetAxis")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.GetButtonDown) != 0
                    && typeName == "GetButtonDown")
                {
                    disable = true;
                    mouseButtonWasDisabled = true;
                    if (rule.method == VRInteractionMethod.AutoMap)
                    {
                        string ev = ReadEventField(action, "sendEvent") ?? "FINISHED";
                        string buttonName = ReadEventField(action, "buttonName")
                                         ?? ReadEventField(action, "buttonString")
                                         ?? ReadEventField(action, "button");
                        string evUpper = ev.ToUpper();
                        bool isDrop = evUpper.Contains("DROP");
                        bool isThrow = evUpper.Contains("THROW");
                        bool isHitButton = !string.IsNullOrEmpty(buttonName)
                            && string.Compare(buttonName, "HIT", true) == 0;

                        if (isThrow)
                        {
                            bool preferRightClick = false;
                            try
                            {
                                string goName = fsm != null && fsm.gameObject != null ? fsm.gameObject.name.ToLower() : "";
                                string fsmName = fsm != null ? (fsm.FsmName ?? "").ToLower() : "";
                                if (goName.Contains("saunathrow") && fsmName == "use")
                                    preferRightClick = true;
                            }
                            catch { }

                            if (!preferRightClick)
                            {
                                AppendAction(state, new VRGripReleaseAction
                                {
                                    targetFsm = fsm,
                                    fsmEventName = ev,
                                    allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                                });
                            }
                            else
                            {
                                // Use right-click action for SaunaThrow THROW events
                                AppendAction(state, new VRTriggerRightClickAction
                                {
                                    targetFsm = fsm,
                                    fsmEventName = ev,
                                    allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                                });
                            }
                        }
                        else if (isHitButton)
                        {
                            AppendAction(state, new VRFistPressAction
                            {
                                targetFsm = fsm,
                                fsmEventName = ev
                            });
                        }
                        else
                        {
                            AppendAction(state, new VRTriggerPressAction
                            {
                                targetFsm = fsm,
                                fsmEventName = ev,
                                allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget,
                            });
                        }
                        _actionsInjected++;
                    }
                }
                if ((rule.disableFlags & DisableActionFlags.SetParent) != 0
                    && typeName == "SetParent")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.FloatCompare) != 0
                    && typeName == "FloatCompare")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.FloatAdd) != 0
                    && typeName == "FloatAdd")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.FloatMultiply) != 0
                    && typeName == "FloatMultiply")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.FloatOperator) != 0
                    && typeName == "FloatOperator")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.Vector3Multiply) != 0
                    && typeName == "Vector3Multiply")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.Vector3Normalize) != 0
                    && typeName == "Vector3Normalize")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.SetVector3Value) != 0
                    && typeName == "SetVector3Value")
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.PlayAnimation) != 0
                    && typeName != null
                    && typeName.ToLower().Contains("playanim"))
                    disable = true;

                if ((rule.disableFlags & DisableActionFlags.GetChild) != 0
                  && typeName == "GetChild")
                {
                    bool handledGetChild = false;

                    if (rule.method == VRInteractionMethod.SprayCanHeldObjectRedirect)
                    {
                        string childName = ReadEventField(action, "childName");
                        if (!string.IsNullOrEmpty(childName)
                            && string.Compare(childName, "spray can(itemx)", true) == 0)
                        {
                            disable = true;

                            FsmGameObject storeVar = null;
                            string varName = null;
                            try
                            {
                                FieldInfo fi = action.GetType().GetField("storeResult",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (fi != null)
                                {
                                    object val = fi.GetValue(action);
                                    if (val is FsmGameObject)
                                    {
                                        storeVar = (FsmGameObject)val;
                                        varName = storeVar != null ? storeVar.Name : null;
                                    }
                                    else if (val != null)
                                    {
                                        varName = val.ToString();
                                    }
                                }
                            }
                            catch { }

                            if (storeVar == null && fsm != null && !string.IsNullOrEmpty(varName))
                                storeVar = fsm.FsmVariables.GetFsmGameObject(varName);
                            if (storeVar == null && fsm != null)
                                storeVar = fsm.FsmVariables.GetFsmGameObject("Can");

                            InsertActionAt(state, actionIndex, new VRWriteHeldObjectAction
                            {
                                storeResult = storeVar,
                                Enabled = true,
                            });
                            _actionsInjected++;
                            handledGetChild = true;
                        }
                    }
                    else
                    {
                        disable = true;

                        if (rule.method == VRInteractionMethod.AutoMap)
                        {
                        // Read the storeResult variable name from the GetChild action
                        string varName = null;
                        try
                        {
                            FieldInfo fi = action.GetType().GetField("storeResult",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fi != null)
                            {
                                object val = fi.GetValue(action);
                                if (val is FsmGameObject) varName = ((FsmGameObject)val).Name;
                                else if (val != null) varName = val.ToString();
                            }
                        }
                        catch { }

                        AppendAction(state, new VRHandChildQueryAction
                        {
                            targetFsm = fsm,
                            fsmEventName = "",        // no event; GameObjectIsNull handles the flow
                            storeVariableName = varName ?? "Part",
                        });
                        _actionsInjected++;
                        }
                        handledGetChild = true;
                    }

                    if (handledGetChild && disable)
                        madeChange = true;
                }

                if (disable)
                {
                    action.Enabled = false;
                    madeChange = true;
                    _actionsDisabled++;
                }

                actionIndex++;
            }
            
            if (!madeChange)
            {
                if (rule.method == VRInteractionMethod.ReparentToHand
                    || rule.method == VRInteractionMethod.ProximityAutoActivate
                    || rule.method == VRInteractionMethod.StateEnterEvent)
                {
                            // Only count this state as a valid target if stateNameContains matched.
                    // that check already happened above in ApplyRule's loop, so if we're here
                    // the state name filter passed. Allow injection.
                    madeChange = true;
                }
            }
            
            if (!madeChange) return false;

            if (rule.method == VRInteractionMethod.LaserPointerPress)
            {
                if (!string.IsNullOrEmpty(laserClickEvent))
                {
                    string fireEvent = !string.IsNullOrEmpty(rule.eventToFire)
                        ? rule.eventToFire
                        : laserClickEvent;

                    FsmStateAction laserAction = BuildAction(rule.method, fsm, state, fireEvent, null, rule);
                    if (laserAction != null)
                    {
                        AppendAction(state, laserAction);
                        _actionsInjected++;
                        PlayMakerHelpers.DebugLog("[LASER] Patched: " + rule.debugLabel + " on "
                            + fsm.gameObject.name + " state " + state.Name
                            + " -> press event " + fireEvent);
                    }

                    return true;
                }

                if (!string.IsNullOrEmpty(laserHoverEvent))
                {
                    FsmStateAction hoverAction = new VRLaserPointerHoverAction
                    {
                        targetFsm = fsm,
                        eventToFire = FsmEvent.GetFsmEvent(laserHoverEvent),
                    };
                    AppendAction(state, hoverAction);
                    _actionsInjected++;
                    PlayMakerHelpers.DebugLog("[LASER] Patched: " + rule.debugLabel + " on "
                        + fsm.gameObject.name + " state " + state.Name
                        + " -> hover event " + laserHoverEvent);
                    return true;
                }

                PlayMakerHelpers.DebugLog("[LASER] SKIP state '" + state.Name + "' on "
                    + fsm.gameObject.name + " - no usable event field");
                return true;
            }

            if (rule.method != VRInteractionMethod.AutoMap
                && rule.method != VRInteractionMethod.CustomActionInject)
            {
                bool shouldInject = true;
                if (rule.method == VRInteractionMethod.TriggerPress
                    || rule.method == VRInteractionMethod.TriggerHold
                    || rule.method == VRInteractionMethod.TriggerRightClick
                    || rule.method == VRInteractionMethod.GrabAndTrigger)
                {
                    shouldInject = mouseButtonWasDisabled;
                }

                if (shouldInject)
                {
                    string fireEvent = !string.IsNullOrEmpty(rule.eventToFire)
                        ? rule.eventToFire : "FINISHED";
                    string releaseEvent = rule.releaseEventToFire;
                    FsmStateAction vrAction = BuildAction(rule.method, fsm, state, fireEvent, releaseEvent, rule);
                    if (vrAction != null)
                    {
                        VRInjectedAction injected = vrAction as VRInjectedAction;
                        if (injected != null)
                            injected.allowInputWithoutActiveTarget = rule.allowInputWithoutActiveTarget;
                        
                        AppendAction(state, vrAction);
                        _actionsInjected++;
                    }
                }
            }

            // Fallback: gate states that had no MousePickEvent at all
            if (rule.method == VRInteractionMethod.AutoMap && madeChange)
            {
                bool hasProxActivate = false;
                FsmStateAction[] finalActions = GetActionsRaw(state);
                if (finalActions != null)
                    foreach (FsmStateAction a in finalActions)
                        if (a is VRProximityActivateAction) { hasProxActivate = true; break; }

                if (!hasProxActivate)
                {
                    string sn = state.Name.ToLower();
                    bool isGateState = sn.StartsWith("wait player")
                                    || sn.StartsWith("wait1")
                                    || sn.StartsWith("not sitting")
                                    || sn.StartsWith("waiting");
                    if (isGateState)
                    {
                        {
                            var _pa = new VRProximityActivateAction
                            {
                                targetFsm = fsm,
                                fsmEventName = "FINISHED",
                                AllowInRangeActivation = ShouldEnableInRangeActivation(rule, fsm != null ? fsm.gameObject : null),
                            };
                            AppendAction(state, _pa);
                            VRProximityRegistry.RegisterActivate(fsm.gameObject, _pa);
                            _actionsInjected++;
                        }

                        string destStateName = null;

                        foreach (FsmTransition tr in state.Transitions)
                            if (tr.EventName == "FINISHED") { destStateName = tr.ToState; break; }

                        if (!string.IsNullOrEmpty(destStateName))
                        {
                            foreach (FsmState destState in fsm.FsmStates)
                            {
                                if (destState.Name != destStateName) continue;

                                bool isDispatcher = false;
                                FsmStateAction[] destActions = GetActionsRaw(destState);
                                if (destActions != null)
                                    foreach (FsmStateAction a in destActions)
                                        if (a != null && a.GetType().Name == "BoolTest")
                                        { isDispatcher = true; break; }

                                if (isDispatcher)
                                {
                                    foreach (FsmTransition tr in destState.Transitions)
                                    {
                                        if (string.IsNullOrEmpty(tr.ToState)) continue;
                                        foreach (FsmState childState in fsm.FsmStates)
                                        {
                                            if (childState.Name != tr.ToState) continue;
                                            {
                                                var _pl = new VRProximityLeaveAction
                                                {
                                                    targetFsm = fsm,
                                                    fsmEventName = "FINISHED",
                                                    AllowInRangeActivation = ShouldEnableInRangeActivation(rule, fsm != null ? fsm.gameObject : null),
                                                };
                                                AppendAction(childState, _pl);

                                                _actionsInjected++;
                                            }
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    var _pl = new VRProximityLeaveAction
                                    {
                                        targetFsm = fsm,
                                        fsmEventName = "FINISHED",
                                        AllowInRangeActivation = ShouldEnableInRangeActivation(rule, fsm != null ? fsm.gameObject : null),
                                    };
                                    AppendAction(destState, _pl);
                                    VRProximityRegistry.RegisterLeave(fsm.gameObject, _pl);
                                    _actionsInjected++;
                                }
                                break;
                            }
                        }
                    }
                }
            }

            return true;
        }

        // ── Action factory ────────────────────────────────────────────────────────

        private static FsmStateAction BuildAction(VRInteractionMethod method,
            PlayMakerFSM fsm, FsmState state, string fireEvent, string releaseEvent,
            VRFsmRule rule = null)
        {
            switch (method)
            {
                case VRInteractionMethod.TriggerPress:
                    return new VRTriggerPressAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                    };

                case VRInteractionMethod.TriggerHold:
                    return new VRTriggerHoldAction
                    {
                        targetFsm = fsm,
                        pressEventName = fireEvent,
                        releaseEventName = releaseEvent,
                    };

                case VRInteractionMethod.TriggerRightClick:
                    return new VRTriggerRightClickAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                    };

                case VRInteractionMethod.LaserPointerPress:
                    return new VRLaserPointerPressAction
                    {
                        targetFsm = fsm,
                        eventToFire = !string.IsNullOrEmpty(fireEvent)
                            ? FsmEvent.GetFsmEvent(fireEvent)
                            : null,
                    };

                case VRInteractionMethod.KnobTwist:
                    // KnobTwist ignores fireEvent. It fires TIGHTEN/UNTIGHTEN directly.
                    return new VRKnobTwistAction { targetFsm = fsm };

                case VRInteractionMethod.NoAction:
                case VRInteractionMethod.DisableOnly:
                    return null;

                case VRInteractionMethod.InjectAction:
                    if (rule == null || rule.injectedAction == null)
                        return null;

                    if (rule.injectedAction is VRInjectedAction)
                        ((VRInjectedAction)rule.injectedAction).targetFsm = fsm;
                    return rule.injectedAction;

                case VRInteractionMethod.ProximityAutoActivate:
                    return new VRProximityActivateAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                        AllowInRangeActivation = ShouldEnableInRangeActivation(rule, fsm != null ? fsm.gameObject : null),
                    };

                case VRInteractionMethod.StateEnterEvent:
                    return new VRStateEnterEventAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                    };

                case VRInteractionMethod.GripPressEvent:
                    return new VRGripPressAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                    };

                case VRInteractionMethod.GripReleaseEvent:
                    return new VRGripReleaseAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                    };

                case VRInteractionMethod.DeactivateHeldObject:
                    return new VRDeactivateHeldObjectAction
                    {
                        targetFsm = fsm,
                    };

                case VRInteractionMethod.GrabAndTrigger:
                    return new VRGrabTriggerAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                    };

                case VRInteractionMethod.PickupProximity:
                    // Inject proximity scanner first (replaces MousePick → writes to RaycastHitObject)
                    var proximityAction = new VRHandPickupProximityAction { targetFsm = fsm };
                    AppendAction(state, proximityAction);
                    // Return trigger action (replaces GetMouseButtonDown → fires event
                    // when trigger pressed and RaycastHitObject has a valid value).
                    // Unlike VRTriggerPressAction, this checks RaycastHitObject instead
                    // of IsHandNear(), because the PickUp FSM lives on the player's hand
                    // object, not on the world object being picked up.
                    return new VRPickupTriggerAction
                    {
                        targetFsm = fsm,
                        fsmEventName = fireEvent,
                    };

                case VRInteractionMethod.PickupReparent:
                    // Inject SetParent disabler FIRST so the original SetParent is silenced
                    // before VRPickupReparentAction fires its own OnEnter.
                    var disabler = new VRSetParentDisabler();
                    AppendAction(state, disabler);
                    return new VRPickupReparentAction
                    {
                        targetFsm = fsm,
                        targetObjectVarName = "RaycastHitObject",
                    };
                case VRInteractionMethod.SelfReparent:
                    AppendAction(state, new VRSetParentDisabler());
                    return new VRSelfReparentAction { targetFsm = fsm };

                case VRInteractionMethod.ReparentToHand:
                    return new VRHandReparentAction
                    {
                        targetFsm = fsm,
                        ancestorName = rule != null ? rule.reparentAncestorName : null,
                        localOffset = rule != null ? rule.reparentLocalOffset : Vector3.zero,
                        localRotation = rule != null ? rule.reparentLocalRotation : Vector3.zero,
                        restoreOnExit = rule != null ? rule.reparentRestoreOnExit : true,
                        handPreference = rule != null ? rule.reparentHand : ReparentHandPreference.ActiveTarget,
                    };

                case VRInteractionMethod.Vector3TransformOverride:
                    if (rule == null) return null;

                    Transform sourceTransform = null;
                    if (string.IsNullOrEmpty(rule.directionTransformPath))
                    {
                        sourceTransform = fsm.gameObject.transform;
                    }
                    else
                    {
                        string[] pathParts = rule.directionTransformPath.Split('/');
                        if (pathParts != null && pathParts.Length > 0)
                        {
                                string relativePath = null;
                                for (int i = 1; i < pathParts.Length; i++)
                                {
                                    if (relativePath == null)
                                        relativePath = pathParts[i];
                                    else
                                        relativePath += "/" + pathParts[i];
                                }

                                sourceTransform = VRObjectResolver.FindFromRoot(pathParts[0], relativePath);
                        }

                        if (sourceTransform == null)
                        {
                            PlayMakerHelpers.DebugLog("[VRFsmPatcher] WARNING: directionTransformPath '"
                                + rule.directionTransformPath + "' not found, Vector3TransformOverride injection skipped");
                            return null;
                        }
                    }

                    FsmStateAction[] currentActions = GetActionsRaw(state) ?? new FsmStateAction[0];
                    FsmStateAction[] newActions = new FsmStateAction[currentActions.Length + 1];
                    newActions[0] = new VRVector3OverrideAction
                    {
                        variableName = "temp_suunta",
                        source = sourceTransform,
                        eulerOffset = rule.directionEulerOffset,
                    };
                    for (int i = 0; i < currentActions.Length; i++)
                        newActions[i + 1] = currentActions[i];

                    try
                    {
                        FieldInfo fi = typeof(FsmState).GetField("actions",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? typeof(FsmState).GetField("_actions",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fi != null)
                            fi.SetValue(state, newActions);
                        else
                            state.Actions = newActions;
                    }
                    catch { state.Actions = newActions; }

                    if (state.Fsm != null && newActions[0] != null)
                        newActions[0].Init(state);

                    _actionsInjected++;
                    return null;

                case VRInteractionMethod.CustomActionInject:
                    return BuildCustomActionInjectAction(fsm, rule);

                default:
                    PlayMakerHelpers.DebugLog("[VRFsmPatcher] Unknown method: " + method);
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static VRHandRaycastAction BuildCustomActionInjectAction(PlayMakerFSM fsm, VRFsmRule rule)
        {
            VRFsmRule.VRRaycastConfig cfg = rule != null ? rule.raycastConfig : null;
            if (cfg == null)
            {
                PlayMakerHelpers.DebugLog("[VRFsmPatcher] WARNING: rule '"
                    + (rule != null ? rule.debugLabel : "<null>")
                    + "' uses CustomActionInject but has no raycastConfig. Skipping action injection.");
                return null;
            }

            string storeResultVariableName = cfg.storeResultVariableName;
            if (string.IsNullOrEmpty(storeResultVariableName))
                storeResultVariableName = "PaintedPart";

            FsmGameObject storeResult = null;
            if (fsm != null && fsm.FsmVariables != null && !string.IsNullOrEmpty(storeResultVariableName))
                storeResult = fsm.FsmVariables.GetFsmGameObject(storeResultVariableName);

            if (storeResult == null)
            {
                PlayMakerHelpers.DebugLog("[VRFsmPatcher] WARNING: rule '"
                    + rule.debugLabel + "' could not resolve storeResult variable '"
                    + storeResultVariableName + "'");
            }

            FsmFloat storeDistance = null;
            if (fsm != null && fsm.FsmVariables != null && !string.IsNullOrEmpty(cfg.storeDistanceVariableName))
                storeDistance = fsm.FsmVariables.GetFsmFloat(cfg.storeDistanceVariableName);

            FsmBool storeDidHit = null;
            if (fsm != null && fsm.FsmVariables != null && !string.IsNullOrEmpty(cfg.storeDidHitVariableName))
                storeDidHit = fsm.FsmVariables.GetFsmBool(cfg.storeDidHitVariableName);

            return new VRHandRaycastAction
            {
                targetFsm = fsm,
                storeResult = storeResult,
                storeDistance = storeDistance,
                storeDidHit = storeDidHit,
                raySource = cfg.raySource,
                layerMask = new FsmInt { Value = cfg.layerMask },
                rayDistance = new FsmFloat { Value = cfg.rayDistance },
                debugDrawRay = cfg.debugDrawRay,
                debugColor = cfg.debugColor,
                everyFrame = true,
            };
        }

        /// <summary>
        /// s an action to a state's Actions array without touching existing entries.
        /// Then calls Init() so PlayMaker knows the action belongs to this state/FSM.
        /// </summary>
        public static void AppendAction(FsmState state, FsmStateAction newAction)
        {
            FsmStateAction[] old = GetActionsRaw(state) ?? new FsmStateAction[0];
            FsmStateAction[] grown = new FsmStateAction[old.Length + 1];
            for (int i = 0; i < old.Length; i++) grown[i] = old[i];
            grown[old.Length] = newAction;

            // Write back via reflection too, for consistency
            try
            {
                FieldInfo fi = typeof(FsmState).GetField("actions",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? typeof(FsmState).GetField("_actions",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null)
                    fi.SetValue(state, grown);
                else
                    state.Actions = grown; // fallback
            }
            catch { state.Actions = grown; }

            if (state.Fsm != null)
                newAction.Init(state);

            // If this state is currently the active running state,
            // add to ActiveActions so OnUpdate ticks it immediately
            if (state.Active)
            {
                newAction.Active = true;
                newAction.Entered = true;
                state.ActiveActions.Add(newAction);
            }
        }

        public static void InsertActionAt(FsmState state, int index, FsmStateAction newAction)
        {
            FsmStateAction[] old = GetActionsRaw(state) ?? new FsmStateAction[0];
            if (index < 0) index = 0;
            if (index > old.Length) index = old.Length;

            FsmStateAction[] grown = new FsmStateAction[old.Length + 1];
            for (int i = 0; i < index; i++) grown[i] = old[i];
            grown[index] = newAction;
            for (int i = index; i < old.Length; i++) grown[i + 1] = old[i];

            try
            {
                FieldInfo fi = typeof(FsmState).GetField("actions",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? typeof(FsmState).GetField("_actions",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null)
                    fi.SetValue(state, grown);
                else
                    state.Actions = grown;
            }
            catch { state.Actions = grown; }

            if (state.Fsm != null)
                newAction.Init(state);

            if (state.Active)
            {
                newAction.Active = true;
                newAction.Entered = true;
                state.ActiveActions.Add(newAction);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRInjectionConfig
    //
    // Edit this class to define all your patching rules.
    // Call VRInjectionConfig.RegisterDefaultRules() before RunAllPatches().
    //
    // Organise rules from MOST specific to LEAST specific.
    // Use skipIfAlreadyPatched = true on broad rules so they don't double-patch
    // objects already claimed by a more specific earlier rule.
    // ─────────────────────────────────────────────────────────────────────────────
    public static class VRInjectionConfig
    {
        public static void RegisterDefaultRules()
        {
            VRFsmPatcher.Rules.Clear();
            // ── SPECIFIC rules FIRST (most specific) ───────────────────────────────────
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "MenuCamDisable",
                objectNameContains = new List<string> { "OptionsMenu" },
                fsmNameContains = new List<string> { "Setup" },
                method = VRInteractionMethod.DisableOnly,
                disableFlags = DisableActionFlags.SetMainCamera,
                activateAndRestoreAfterPatch = true,
                skipIfAlreadyPatched = false 
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "GUICamDisable",
                hierarchyPathContains = new List<string> { "Sheets" },
                method = VRInteractionMethod.DisableOnly,
                disableFlags = DisableActionFlags.SetMainCamera,
                activateAndRestoreAfterPatch = false,
                skipIfAlreadyPatched = false 
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel           = "Menu stuff (laser)",
                hierarchyPathContains = new List<string> { "" },
                fsmNameContains      = new List<string> { "button"  },
                method               = VRInteractionMethod.LaserPointerPress,
                disableFlags         = DisableActionFlags.MousePickEvent,
                eventToFire          = null,
                skipIfAlreadyPatched = true,
                patchFirstStateOnly  = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel           = "Orders",
                hierarchyPathContains = new List<string> { "OrderTriggers" },
                fsmNameContains      = new List<string> { "Buy",  },
                method               = VRInteractionMethod.LaserPointerPress,
                disableFlags         = DisableActionFlags.MousePickEvent | DisableActionFlags.GetMouseButtonDown,
                eventToFire          = null,
                skipIfAlreadyPatched = true,
                patchFirstStateOnly  = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel           = "Fuel nozzle - reparent Pivot to hand",
                objectNameContains   = new List<string> { "FuelNozzle" },
                method               = VRInteractionMethod.ReparentToHand,
                disableFlags         = DisableActionFlags.None,
                reparentAncestorName = "Pivot",         // walk up until "Pivot" found in hierarchy
                reparentLocalOffset  = new Vector3(-0.07f, -0.13f, -0.35f),
                reparentLocalRotation = new Vector3(335f, 353f, 16f),
                reparentRestoreOnExit = false,           // put it back when done fuelling
                reparentHand         = ReparentHandPreference.Right,
                allowHeldInteractable = true,
                patchFirstStateOnly  = false,
            });
            
            // Then handle the pour input as a separate rule (AutoMap covers GetMouseButtonUp)
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel           = "Fuel nozzle - pour trigger hold",
                objectNameContains   = new List<string> { "FuelNozzle" },
                method               = VRInteractionMethod.AutoMap,
                disableFlags         = DisableActionFlags.GetMouseButton,
                allowHeldInteractable = true,
                skipIfAlreadyPatched = false,
            });
            // Ignition key: very specific, fires first.
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Ignition key",
                objectNameContains = new List<string> { "ignition" },
                fsmNameContains = new List<string> { "use" },   // restrict: skip Data/Screw FSMs on VINP_IgnitionCoil
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                       | DisableActionFlags.GetMouseButton,  // advance FSM past 'Mouse off' in VR
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Flashlight toggle",
                objectNameContains = new List<string> { "flashlight(itemx)" },
                fsmNameContains = new List<string> { "Use" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetButtonDown,
                markAsGripRequired = true,
                skipIfAlreadyPatched = false
            });

            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Some item covers",
                objectNameContains = new List<string> { "Open", "Power" },
                fsmNameContains = new List<string> { "knob" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetButtonDown,
                skipIfAlreadyPatched = false
            });

            // Radio knobs
            // Handles gate state proximity (mouseOver advances FSM to "Get scroll")
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Knobs - proximity gate",
                excludeObjectNames = new List<string>{ "boltpm", "itemx" },
                fsmNameContains = new List<string> { "knob", "screw" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent,
                skipIfAlreadyPatched = true,
            });

            // Handles the scroll state itself
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Knobs - twist",
                excludeObjectNames = new List<string>{ "boltpm" },
                fsmNameContains = new List<string> { "knob", "screw" },
                stateNameContains = new List<string> { "scroll" },
                method = VRInteractionMethod.KnobTwist,
                disableFlags = DisableActionFlags.GetAxis | DisableActionFlags.FloatCompare,
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Knobs - disable wait in scroll states",
                excludeObjectNames = new List<string>{ "boltpm" },
                // NO stateNameContains: visits all states, but the hasKnobAction
                // guard in PatchState only disables Wait when GetAxis/FloatCompare
                // are also in the same state, so gate states are safe
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.WaitAction,
                skipIfAlreadyPatched = false,
            });

            // PickUp FSM rules
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "PickUp FSM - proximity scan",
                hierarchyPathContains = new List<string> { "1Hand_Assemble" },
                fsmNameContains = new List<string> { "PickUp" },
                stateNameContains = new List<string> { "Look for object" },
                method = VRInteractionMethod.PickupProximity,
                disableFlags = DisableActionFlags.MousePick
                                      | DisableActionFlags.MousePickEvent
                                      | DisableActionFlags.GetMouseButtonDown,
                patchFirstStateOnly = true,
            });

            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "PickUp FSM - reparent",
                hierarchyPathContains = new List<string> { "1Hand_Assemble" },
                fsmNameContains = new List<string> { "PickUp" },
                stateNameContains = new List<string> { "Part picked", "Tool picked", "Item picked", "Set pivot", "State 5", "State 4" },
                method = VRInteractionMethod.PickupReparent,
                disableFlags = DisableActionFlags.SetParent | DisableActionFlags.SetJointConnectedBody,
                skipIfAlreadyPatched = false,
            });

            // Hide specific held item while it's parented to the hand, then restore on release
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Hide dipper while held",
                objectNameContains = new List<string> { "dipper(itemx)", "spray can(itemx)", "ice scraper(itemx)" },
                method = VRInteractionMethod.DeactivateHeldObject,
                disableFlags = DisableActionFlags.None,
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Seatbelt handle - hold reparent",
                objectNameContains = new List<string> { "SeatbeltHandle" },
                stateNameContains = new List<string> { "Wait lock", "Locking?" },
                method = VRInteractionMethod.ReparentToHand,
                disableFlags = DisableActionFlags.SetParent,
                reparentRestoreOnExit = true,
                allowHeldInteractable = true,
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Seatbelt handle - interactions",
                objectNameContains = new List<string> { "SeatbeltHandle" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                 | DisableActionFlags.GetMouseButtonDown
                 | DisableActionFlags.GetMouseButtonUp,
                allowHeldInteractable = true,
                proximityAnyInRange = false,
                skipIfAlreadyPatched = false, // ← must be false, FSM already claimed by reparent rule
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Seatbelt lock - allow in-range proximity",
                objectNameContains = new List<string> { "SeatbeltLock", "Seatbelt lock"},
                hierarchyPathContains = new List<string> { "seatbelt" },
                excludeObjectNames = new List<string> { "SeatbeltHandle" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                                 | DisableActionFlags.GetMouseButtonDown
                                 | DisableActionFlags.GetMouseButtonUp,
                proximityAnyInRange = true,
                excludeFromActiveTargetSelection = true,
                excludeFromActiveTargetStateNameContains = new List<string> { "=Wait player", "=Wait button" },
                skipIfAlreadyPatched = false,
            });

            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "PickUp FSM - drop and throw",
                hierarchyPathContains = new List<string> { "1Hand_Assemble" },
                fsmNameContains = new List<string> { "PickUp" },
                stateNameContains = new List<string> { 
                    "Part picked", "Tool picked", "Item picked",
                    "Ragdoll picked"
                },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.GetMouseButtonDown
                             | DisableActionFlags.GetButtonDown
                             | DisableActionFlags.GetKeyDown,
                skipIfAlreadyPatched = false 
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Auto Equip",
                hierarchyPathContains = new List<string> { "1Hand_Assemble" },
                fsmNameContains = new List<string> { "PickUp" },
                stateNameContains = new List<string> { "Item picked" },
                method = VRInteractionMethod.StateEnterEvent,
                eventToFire = "EQUIP",
                disableFlags = DisableActionFlags.GetButtonDown,
                skipIfAlreadyPatched = false 
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Auto UnEquip",
                hierarchyPathContains = new List<string> { "1Hand_Assemble" },
                fsmNameContains = new List<string> { "PickUp" },
                stateNameContains = new List<string> { "Flashlight" },
                method = VRInteractionMethod.GripReleaseEvent,
                eventToFire = "FINISHED",
                disableFlags = DisableActionFlags.GetButtonDown,
                skipIfAlreadyPatched = false 
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Wiring",
                hierarchyPathContains = new List<string> { "CORRIS/Wiring/Triggers" },
                fsmNameContains = new List<string> { "Assemble" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.GetButtonDown,
                markAsGripRequired = true,
                skipIfAlreadyPatched = false
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Assembly",
                objectNameContains = new List<string> { "VINP" },
                fsmNameContains = new List<string> { "Data" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.GetMouseButtonDown
                             | DisableActionFlags.MousePickEvent,
                markAsGripRequired = true,
                skipIfAlreadyPatched = false 
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Part removal",
                objectNameContains = new List<string> { "VINXX", "VINX0" },
                fsmNameContains = new List<string> { "Data" },
                stateNameContains = new List<string> { "Mouse off", "Mouse over" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.GetMouseButtonDown
                             | DisableActionFlags.MousePickEvent,
                skipIfAlreadyPatched = false 
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Hand assembly - find held part",
                fsmNameContains = new List<string> { "Assembly" },
                stateNameContains = new List<string> { "Find correct part", "Wait for assembly" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.GetChild | DisableActionFlags.GetMouseButtonDown,
                markAsGripRequired = true,
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Spray can FSM - held object redirect",
                fsmNameContains = new List<string> { "Paint" },
                stateNameContains = new List<string> { "State 2" },
                method = VRInteractionMethod.SprayCanHeldObjectRedirect,
                disableFlags = DisableActionFlags.GetChild,
                skipIfAlreadyPatched = false,
            });
            VRFsmRule sprayCanRaycastRule = new VRFsmRule
            {
                debugLabel = "Spray can paint raycast",
                fsmNameContains = new List<string> { "Paint" },
                stateNameContains = new List<string> { "Stage 1" },
                method = VRInteractionMethod.CustomActionInject,
                disableFlags = DisableActionFlags.MousePick,
                raycastConfig = new VRFsmRule.VRRaycastConfig
                {
                    raySource = VRHandRaycastAction.RaySourceMode.RightHand,
                    layerMask = 1 << 19,
                    storeResultVariableName = "PaintedPart",
                    debugDrawRay = true,
                    debugColor = Color.red,
                },
                skipIfAlreadyPatched = false,
            };
            VRFsmPatcher.Rules.Add(sprayCanRaycastRule);
            PlayMakerHelpers.DebugLog("[VRInjectionConfig] Registered rule: " + sprayCanRaycastRule.debugLabel);
            VRFsmRule axeRaycastRule = new VRFsmRule
            {
                debugLabel = "Axe swing raycast",
                // TODO: verify FSM and state names from PlayMaker inspector
                fsmNameContains = new List<string> { "Use" },
                hierarchyPathContains = new List<string> { "Ax" },
                method = VRInteractionMethod.CustomActionInject,
                disableFlags = DisableActionFlags.MousePick,
                raycastConfig = new VRFsmRule.VRRaycastConfig
                {
                    raySource = VRHandRaycastAction.RaySourceMode.RightHand,
                    layerMask = 1 << 12,
                    storeResultVariableName = "HitObject",
                    debugDrawRay = true,
                    debugColor = Color.yellow,
                },
                skipIfAlreadyPatched = false,
            };
            VRFsmPatcher.Rules.Add(axeRaycastRule);
            PlayMakerHelpers.DebugLog("[VRInjectionConfig] Registered rule: " + axeRaycastRule.debugLabel);
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Spray can FSM - things like trigger",
                fsmNameContains = new List<string> { "Paint" },
                stateNameContains = new List<string> { "Stage 1", "Painting" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetMouseButtonDown
                            | DisableActionFlags.GetButtonDown
                            | DisableActionFlags.GetKeyDown
                            | DisableActionFlags.GetMouseButtonUp,
                skipIfAlreadyPatched = false,
                allowInputWithoutActiveTarget = true,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Scrapin icy windows",
                fsmNameContains = new List<string> { "Scrapejf9rsiucj,fnesjkcfn," },
                stateNameContains = new List<string> { "Mouse off sfdgd2", "Get scrolfdsfsal" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetMouseButtonDown
                            | DisableActionFlags.GetButtonDown
                            | DisableActionFlags.GetKeyDown
                            | DisableActionFlags.GetMouseButtonUp,
                skipIfAlreadyPatched = false,
                allowInputWithoutActiveTarget = true,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Fuse-removal",
                fsmNameContains = new List<string> { "Removal" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent | DisableActionFlags.GetMouseButtonDown,
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Paying at cashregister",
                fsmNameContains = new List<string> { "Data" },
                objectNameContains = new List<string> { "CashRegisterLogic"},
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetMouseButtonDown
                            | DisableActionFlags.GetButtonDown
                            | DisableActionFlags.GetKeyDown
                            | DisableActionFlags.GetMouseButtonUp,
                skipIfAlreadyPatched = true,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Amisauto open catalog.",
                objectNameContains = new List<string> { "parts magazine(itemx)" },
                fsmNameContains = new List<string> { "Activate" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetMouseButtonDown
                            | DisableActionFlags.GetButtonDown
                            | DisableActionFlags.GetKeyDown
                            | DisableActionFlags.GetMouseButtonUp,
                skipIfAlreadyPatched = false,  // ← CRITICAL: don't double-patch
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Shower valve",
                fsmNameContains = new List<string> { "Switch" },
                objectNameContains = new List<string> { "Valve"},
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetMouseButtonDown
                            | DisableActionFlags.GetButtonDown
                            | DisableActionFlags.GetKeyDown
                            | DisableActionFlags.GetMouseButtonUp,
                skipIfAlreadyPatched = true,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                objectNameContains = new List<string> { "SaunaThrow" },
                fsmNameContains = new List<string> { "Use" },
                stateNameContains = new List<string> { "Animate", "Anim", "Throw part" },
                method = VRInteractionMethod.DisableOnly,
                disableFlags = DisableActionFlags.PlayAnimation,  
                debugLabel = "Disable animation on Saunaladle"
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Throw: disable SetVelocity in Drop part 2",
                fsmNameContains = new List<string> { "pickup" },
                stateNameContains = new List<string> { "drop part 2" },
                hierarchyPathContains = new List<string> { "PLAYER" },
                method = VRInteractionMethod.NoAction,
                disableFlags = DisableActionFlags.SetVelocity,
                patchFirstStateOnly = true,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Throw: replace force math in Throw part",
                fsmNameContains = new List<string> { "pickup" },
                stateNameContains = new List<string> { "throw part" },
                hierarchyPathContains = new List<string> { "PLAYER" },
                method = VRInteractionMethod.InjectAction,
                disableFlags = DisableActionFlags.AddForce
                             | DisableActionFlags.GetAxis
                             | DisableActionFlags.FloatAdd
                             | DisableActionFlags.FloatMultiply
                             | DisableActionFlags.FloatOperator
                             | DisableActionFlags.Vector3Multiply
                             | DisableActionFlags.SetVector3Value
                             | DisableActionFlags.Vector3Normalize
                             | DisableActionFlags.SetJointConnectedBody,
                injectedAction = new VRApplyThrowForceAction(),
                allowHeldInteractable = true,
                allowInputWithoutActiveTarget = true,
                patchFirstStateOnly = true,
                skipIfAlreadyPatched = false,
            });
            // ── CATCH-ALL rule LAST (least specific) ──────────────────────────────────
            // This fires ONLY if no earlier rule claimed the FSM
             // SaunaThrow specific rule: allow use actions from anywhere/while held
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "Sauna ladle throw direction",
                objectNameContains = new List<string> { "SaunaThrow" },
                 fsmNameContains = new List<string> { "Use" },
                stateNameContains = new List<string> { "throw part" },
                method = VRInteractionMethod.Vector3TransformOverride,
                disableFlags = DisableActionFlags.SetVector3Value,
                directionTransformPath = "",
                directionEulerOffset = new Vector3(0f, 290f, 0f),
                allowHeldInteractable = true,
                allowInputWithoutActiveTarget = true,
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "SaunaThrow - Use (held/anywhere)",
                objectNameContains = new List<string> { "SaunaThrow" },
                fsmNameContains = new List<string> { "Use" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetMouseButtonDown
                            | DisableActionFlags.GetButtonDown
                            | DisableActionFlags.GetMouseButtonUp,
                allowHeldInteractable = true,
                allowInputWithoutActiveTarget = true,
                skipIfAlreadyPatched = false,
            });
            VRFsmPatcher.Rules.Add(new VRFsmRule
            {
                debugLabel = "All Use FSMs (catch-all)",
                fsmNameContains = new List<string> { "use", "buy","break", "changetrack", "changechannel", "playerhit" },
                method = VRInteractionMethod.AutoMap,
                disableFlags = DisableActionFlags.MousePickEvent
                            | DisableActionFlags.GetMouseButtonDown
                            | DisableActionFlags.GetButtonDown
                            | DisableActionFlags.GetKeyDown
                            | DisableActionFlags.GetMouseButtonUp,
                skipIfAlreadyPatched = true,  // ← CRITICAL: don't double-patch
            });
            // Ensure HeldVisibilityManager exists and sync patterns from rules
            try { HeldVisibilityManager.EnsureInstance();
                var hv = GameObject.Find("MVRC_HeldVisibilityManager")?.GetComponent<HeldVisibilityManager>();
                if (hv != null) hv.UpdatePatternsFromRules();
            } catch { }
        }
    }
}
