// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using System;
using System.Reflection;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MSCLoader;

namespace MVRC
{
    public static class VRInteraction
    {
        private static readonly string[] MouseOffOrOverStates = new[]
        {
            "Mouse off", "Mouse over", "Mouse over 1", "Mouse over 2", "Mouse over 3",
        };

        // =========================================================================
        // BOLT PROXIMITY & TIGHTENING
        // =========================================================================
        //
        // How the game's bolt pipeline works (per FSM inspection / guide):
        //
        //   Raycast FSM "State 1": MousePick every frame -> stores hit in Bolt (local)
        //                         SetFsmGameObject -> copies Bolt back into itself (everyFrame)
        //
        //   Check FSM "Wait bolt": GameObjectChanged on Bolt -> FINISHED (0.1s debounce)
        //                        → "Check bolt Name" → name=="BoltPM"? → BOLT
        //                        → "Check tool" → reads bolt.Screw.Boltsize, compares
        //                           to global ToolWrenchSize → FINISHED (match) or RED/YELLOW
        //                        → "Set Material" → visual highlight
        //                        → "Ratchet?" → reads global PlayerHasRatchet
        //                            false → "Get Mouse scroll"
        //                            true  → "Get Mouse scroll 2"
        //
        //   "Get Mouse scroll": GetAxis Mouse ScrollWheel -> Scroll (overwrites EVERY FRAME)
        //                      FloatCompare: Scroll>0 -> TIGHTEN, <0 -> UNTIGHTEN
        //                      GameObjectChanged/Compare -> OFF -> "Set Material back"
        //
        //   "Tighten/Untighten" → SendEvent TIGHTEN/UNTIGHTEN to Bolt's Screw FSM
        //                       → "Delay" (plays bolt_screw audio, waits 0.28s)
        //                       → back to "Get Mouse scroll"
        //
        // VR approach:
        //   1. Disable MousePick in Raycast FSM's "State 1" (once).
        //   2. Every frame in tool mode (not tightening): proximity sphere layer 12,
        //      write nearest bolt into Check FSM's "Bolt" variable.
        //   3. When accumulator threshold crossed: send TIGHTEN or UNTIGHTEN event
        //      DIRECTLY to the Check FSM, re-arming scroll state atomically.
        //   4. Set global ToolWrenchSize on tool equip so "Check tool" passes.
        //   5. Set global PlayerHasRatchet on tool equip so Ratchet? routes correctly.
        //
        // =========================================================================

        private static PlayMakerFSM _raycastFsm = null;
        private static PlayMakerFSM _checkFsm = null;
        private static bool _boltSystemPatched = false;
        private static bool _hasNearestBolt = false;
        private static GameObject _nearestBolt = null;

        private static Component _characterMotor = null;
        private static FieldInfo _inputJumpField = null;
        private static FieldInfo _jumpingField = null;
        private static FieldInfo _holdingJumpButtonField = null;

        private static int _boltLogTimer = 0;
        private const int BoltLogInterval = 90;
        private const int BoltLayerMask = 1 << 12;
        private static readonly Collider[] _boltHitsBuffer = new Collider[64];
        private static int _boltScanThrottle = 0;
        private const int BoltScanThrottleFrames = 4;

        private static string _lastBoltName = "UNSET";
        private static string _lastCheckState = "UNSET";

        private static GameObject _lastPickedWorldObject = null;
        private static float _lastPickedToolSize = 0f;

        private static readonly System.Collections.Generic.Dictionary<string, Vector3> ToolPosOffsets
            = new System.Collections.Generic.Dictionary<string, Vector3>
        {
            { "Spanner",         new Vector3( 0.02f,  0.0f,  0.0f) },
            { "Ratchet",         new Vector3( 0.015f, 0.045f, -0.03f) },
            { "Screwdriver",     new Vector3( 0.01f,  0.01f, 0.13f) },
            { "SparkplugWrench", new Vector3( 0.01f,  0.0f,  0.0f) },
            { "Ruler",           new Vector3( 0.03f,  0.05f, 0.0f) },
        };

        private static readonly System.Collections.Generic.Dictionary<string, Vector3> ToolRotOffsets
            = new System.Collections.Generic.Dictionary<string, Vector3>
        {
            { "Spanner",         new Vector3(0f,  0f,   285f) },
            { "Ratchet",         new Vector3(0f,  0f,    10f) },
            { "Screwdriver",     new Vector3(0f, 90f,     0f) },
            { "SparkplugWrench", new Vector3(0f, 180f,    0f) },
            { "Ruler",           new Vector3(0f,  0f,     0f) },
        };

        private static string _lastPickedWorldName = null;
        private static PlayMakerFSM _crouchFsm = null;
        private static readonly System.Collections.Generic.Dictionary<Rigidbody, bool> _toolRigidbodyKinematicBackup
            = new System.Collections.Generic.Dictionary<Rigidbody, bool>();

        private static string _lastAssemblyProximityReason = string.Empty;
        private static bool _lastAssemblyProximityResult = false;
        private static int _assemblyProximityLogCooldown = 0;
        private const int AssemblyProximityLogCooldownFrames = 190;

        public static bool HasNearestBolt() { return _hasNearestBolt; }
        public static PlayMakerFSM GetBoltCheckFsm() { return _checkFsm; }

        private static string DescribeActiveInteractionTargets()
        {
            string right = VRFsmActionBridge.ActiveInteractionTarget != null
                ? VRFsmActionBridge.ActiveInteractionTarget.gameObject.name
                    + "." + VRFsmActionBridge.ActiveInteractionTarget.FsmName
                : "none";
            string left = VRFsmActionBridge.ActiveInteractionTargetLeft != null
                ? VRFsmActionBridge.ActiveInteractionTargetLeft.gameObject.name
                    + "." + VRFsmActionBridge.ActiveInteractionTargetLeft.FsmName
                : "none";
            return "activeTarget[R=" + right + ",L=" + left + "]";
        }

        private static bool LogAssemblyProximityDecision(
            bool result,
            string reason,
            GameObject heldObject,
            GameObject vinp,
            PlayMakerFSM installerFsm)
        {
            if (_assemblyProximityLogCooldown > 0)
                _assemblyProximityLogCooldown--;

            string safeReason = reason ?? string.Empty;
            bool changed = result != _lastAssemblyProximityResult
                || !string.Equals(safeReason, _lastAssemblyProximityReason, StringComparison.Ordinal);

            if (changed || _assemblyProximityLogCooldown <= 0)
            {
                string heldName = heldObject != null ? heldObject.name : "null";
                string vinpName = vinp != null ? vinp.name : "null";
                string installerName = installerFsm != null
                    ? installerFsm.gameObject.name + "." + installerFsm.FsmName
                    : "null";
                string installerState = installerFsm != null
                    ? (installerFsm.ActiveStateName ?? "null")
                    : "null";

                PlayMakerHelpers.DebugLog("[VRInteraction] CheckAssemblyProximity: result=" + result
                    + " reason='" + safeReason + "'"
                    + " held='" + heldName + "'"
                    + " vinp='" + vinpName + "'"
                    + " installer='" + installerName + "'"
                    + " installerState='" + installerState + "'");

                _lastAssemblyProximityReason = safeReason;
                _lastAssemblyProximityResult = result;
                _assemblyProximityLogCooldown = AssemblyProximityLogCooldownFrames;
            }

            return result;
        }

        // =========================================================================
        // MOVEMENT / INPUT STUBS
        // =========================================================================


        public static void TriggerDriveMode()
        {
            PlayMakerFSM[] all = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
            int sentCount = 0;
            foreach (PlayMakerFSM fsm in all)
            {
                if (fsm == null || fsm.gameObject == null) continue;
                string goName = fsm.gameObject.name;
                bool isDriveTrigger = string.Equals(goName, "DriveTrigger", StringComparison.Ordinal)
                    || (goName.StartsWith("DriveTrigger", StringComparison.Ordinal)
                        && goName.Length > "DriveTrigger".Length);
                if (!isDriveTrigger) continue;
                if (!string.Equals(fsm.FsmName, "PlayerTrigger", StringComparison.Ordinal)) continue;
                if (!FsmHasEvent(fsm, "Key DOWN")) continue;

                fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("Key DOWN"));
                sentCount++;
            }

            if (sentCount == 0)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] DriveTrigger: no eligible FSM found");
            }
        }

        public static void TriggerCrouch()
        {
            PlayMakerFSM fsm = GetCrouchFsm();
            if (fsm == null)
            {

                PlayMakerHelpers.DebugLog("[MOVEMENT] TriggerCrouch: Crouch FSM not found");

                return;
            }

            string state = fsm.ActiveStateName ?? "";
            // "Wait key" = waiting for crouch press (standing or already crouched but back up)
            // Fire FINISHED to advance to "Move down 1" or whatever the next state is.
            if (state == "Wait key" || state == "Crouched 1" || state == "Crouched 2")
            {
                fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("FINISHED"));

                PlayMakerHelpers.DebugLog("[MOVEMENT] Crouch: FINISHED → from '" + state + "'");

            }
            else
            {

                PlayMakerHelpers.DebugLog("[MOVEMENT] Crouch: ignored (state='" + state + "')");

            }
        }
        private static PlayMakerFSM GetCrouchFsm()
        {
            if (_crouchFsm != null) return _crouchFsm;
            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return null;
            // The Crouch FSM is directly on PLAYER (confirmed in screenshot: "Crouch (Enabled) (Active)").
            foreach (PlayMakerFSM fsm in player.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName == "Crouch" && FsmHasState(fsm, "Wait key"))
                { _crouchFsm = fsm; return fsm; }
            }

            PlayMakerHelpers.DebugLog("[MOVEMENT] Crouch FSM not found on PLAYER");

            return null;
        }

        private static bool InitCharacterMotor()
        {
            if (_characterMotor != null) return true;
            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return false;
            foreach (Component c in player.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == "CharacterMotor")
                {
                    _characterMotor = c;
                    System.Type t = c.GetType();
                    _inputJumpField = t.GetField("inputJump",
                        BindingFlags.Public | BindingFlags.Instance);
                    _jumpingField = t.GetField("jumping",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (_jumpingField != null)
                        _holdingJumpButtonField = _jumpingField.FieldType.GetField("holdingJumpButton",
                            BindingFlags.Public | BindingFlags.Instance);
                    PlayMakerHelpers.DebugLog("[MOVEMENT] CharacterMotor found via reflection");
                    return true;
                }
            }
            return false;
        }
        public static void TriggerJump()
        {
            // ── FSM path: rise from crouch ────────────────────────────────────────
            PlayMakerFSM crouchFsm = GetCrouchFsm();
            if (crouchFsm != null)
            {
                string state = crouchFsm.ActiveStateName ?? "";
                if (state == "Crouched 1" || state == "Crouched 2")
                {
                    crouchFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("JUMP"));
                    PlayMakerHelpers.DebugLog("[MOVEMENT] Jump: rising from crouch state '" + state + "'");
                    return; // don't also jump while crouched
                }
            }

            // ── CharacterMotor path: actual jump ──────────────────────────────────
            if (!InitCharacterMotor())
            {
                PlayMakerHelpers.DebugLog("[MOVEMENT] TriggerJump: CharacterMotor not found");
                return;
            }
            try
            {
                if (_inputJumpField != null)
                    _inputJumpField.SetValue(_characterMotor, true);
                if (_jumpingField != null && _holdingJumpButtonField != null)
                {
                    object jumpingObj = _jumpingField.GetValue(_characterMotor);
                    _holdingJumpButtonField.SetValue(jumpingObj, true);
                    _jumpingField.SetValue(_characterMotor, jumpingObj); // write back (struct)
                }
                PlayMakerHelpers.DebugLog("[MOVEMENT] Jump: inputJump set");
            }
            catch (System.Exception e)
            {
                PlayMakerHelpers.DebugLog("[MOVEMENT] Jump reflection error: " + e.Message);
            }
        }

        public static void UpdateJumpHeld(bool held)
        {
            if (!held || _characterMotor == null) return;
            try
            {
                if (_jumpingField != null && _holdingJumpButtonField != null)
                {
                    object jumpingObj = _jumpingField.GetValue(_characterMotor);
                    _holdingJumpButtonField.SetValue(jumpingObj, true);
                    _jumpingField.SetValue(_characterMotor, jumpingObj);
                }
            }
            catch { }
        }


        // =========================================================================
        // MOUSE PICK RESTORE
        // Bypassed when VRFsmPatcher.PatcherActive is true. The new patcher rules
        // disable only the specific actions they inject, and VRFsmActionBridge owns
        // the enable/disable lifecycle for patched objects.
        // =========================================================================

        public static void RestoreMousePickEvents(GameObject obj)
        {
            if (obj == null) return;
            Transform t = obj.transform;
            while (t != null)
            {
                foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (VRFsmPatcher._patchedFsms.Contains(fsm)) continue;
                    if (fsm == null || fsm.FsmStates == null) continue;
                    foreach (var state in fsm.FsmStates)
                        foreach (var action in state.Actions)
                            if (action.GetType().Name == "MousePickEvent" && !action.Enabled)
                                action.Enabled = true;
                }
                t = t.parent;
            }
        }

        private static bool HasScrewFsm(GameObject go)
        {
            foreach (PlayMakerFSM fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Screw") return true;
            return false;
        }

        // =========================================================================
        // BOLT SYSTEM, STEP 1: Patch
        // =========================================================================

        public static void EnsureBoltRaycastPatched()
        {
            if (_boltSystemPatched) return;

            GameObject player = GameObject.Find("PLAYER");
            if (player == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-1] PLAYER not found; cannot patch");

                return;
            }

            string[] path = { "Pivot", "AnimPivot", "Camera", "FPSCamera", "2Spanner", "Raycast" };
            Transform t = player.transform;
            foreach (string step in path)
            {
                t = t.Find(step);
                if (t == null)
                {

                    PlayMakerHelpers.DebugLog("[BOLT-1] Path broken at step '" + step + "'");

                    return;
                }
            }

            PlayMakerHelpers.DebugLog("[BOLT-1] Found Raycast GameObject: " + t.gameObject.name);


            PlayMakerFSM[] allFsms = t.GetComponents<PlayMakerFSM>();

            PlayMakerHelpers.DebugLog("[BOLT-1] FSMs on Raycast object: " + allFsms.Length);

            foreach (PlayMakerFSM fsm in allFsms)
            {

                PlayMakerHelpers.DebugLog("[BOLT-1]   FSM: '" + fsm.FsmName + "' enabled=" + fsm.enabled);

                if (fsm.FsmName == "Raycast") _raycastFsm = fsm;
                if (fsm.FsmName == "Check") _checkFsm = fsm;
            }

            if (_raycastFsm == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-1] ERROR: 'Raycast' FSM not found; cannot disable MousePick");

                return;
            }

            PlayMakerHelpers.DebugLog("[BOLT-1] Found Raycast FSM: " + _raycastFsm.FsmName);


            if (_checkFsm == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-1] ERROR: 'Check' FSM not found; cannot inject bolt");

                return;
            }

            PlayMakerHelpers.DebugLog("[BOLT-1] Found Check FSM: " + _checkFsm.FsmName);

            if (!VRFsmPatcher.EnsureInitialized(_raycastFsm) || !VRFsmPatcher.EnsureInitialized(_checkFsm))
            {
                PlayMakerHelpers.DebugLog("[BOLT-1] FSMs not initialized yet. Retrying next frame.");
                return;
            }

            PlayMakerHelpers.DebugLog("[BOLT-1] Raycast FSM states:");
            foreach (var state in _raycastFsm.FsmStates)
            {
                FsmStateAction[] dbgActions = VRFsmPatcher.GetActionsRaw(state);
                PlayMakerHelpers.DebugLog("[BOLT-1]   state: '" + state.Name + "' actions=" + (dbgActions != null ? dbgActions.Length : 0));
            }

            PlayMakerHelpers.DebugLog("[BOLT-1] Check FSM states:");
            foreach (var state in _checkFsm.FsmStates)
                PlayMakerHelpers.DebugLog("[BOLT-1]   state: '" + state.Name + "'");


            // Disable ALL actions in Raycast FSM State 1.
            // MousePick (action 0) finds the bolt via mouse raycast.
            // SetFsmGameObject (action 1) copies it into Check FSM's Bolt every frame.
            // If only MousePick is disabled, SetFsmGameObject still overwrites our write
            // with null every frame, so "Wait bolt" never advances.
            int disabledCount = 0;
            foreach (var state in _raycastFsm.FsmStates)
            {
                if (state.Name != "State 1") continue;
                FsmStateAction[] rawActions = VRFsmPatcher.GetActionsRaw(state);
                if (rawActions == null) continue;
                foreach (var action in rawActions)
                {
                    if (action == null) continue;

                    PlayMakerHelpers.DebugLog("[BOLT-1]   State 1 action: " + action.GetType().Name + " enabled=" + action.Enabled);

                    action.Enabled = false;
                    disabledCount++;

                    PlayMakerHelpers.DebugLog("[BOLT-1]   DISABLED: " + action.GetType().Name);

                }
            }

            PlayMakerHelpers.DebugLog("[BOLT-1] Raycast FSM State 1 all actions disabled: " + disabledCount);


            FsmGameObject boltVar = _checkFsm.FsmVariables.GetFsmGameObject("Bolt");

            PlayMakerHelpers.DebugLog("[BOLT-1] Check FSM 'Bolt' variable found: " + (boltVar != null)
                + (boltVar != null ? " current=" + (boltVar.Value != null ? boltVar.Value.name : "null") : ""));


            _boltSystemPatched = true;

            PlayMakerHelpers.DebugLog("[BOLT-1] PATCH COMPLETE");

        }

        // =========================================================================
        // BOLT SYSTEM, STEP 2: Proximity scan
        // =========================================================================

        public static void UpdateNearestBolt(Vector3 handPos)
        {
            float perfStart = Time.realtimeSinceStartup;
            if (_checkFsm == null) return;

            if (!_hasNearestBolt)
            {
                _boltScanThrottle++;
                if (_boltScanThrottle < BoltScanThrottleFrames)
                    return;
                _boltScanThrottle = 0;
            }

            float scanRadius = DebugVrControllerEmulation.IsActive ? 0.18f : 0.12f;
            int hitCount = PhysicsCompat.OverlapSphereNonAlloc(handPos, scanRadius, _boltHitsBuffer, BoltLayerMask);
            GameObject nearest = null;
            float closestSqr = float.MaxValue;
            float closest = 0f;

            for (int i = 0; i < hitCount; i++)
            {
                Collider c = _boltHitsBuffer[i];
                if (c == null) continue;
                GameObject cgo = c.gameObject;
                if (cgo == null) continue;
                bool isBolt = cgo.name == "BoltPM";
                bool isSparkPlug = !isBolt && cgo.name == "spark plug(Clone)";
                if (!isBolt && !isSparkPlug) continue;
                float dSqr = (c.bounds.center - handPos).sqrMagnitude;
                if (dSqr < closestSqr)
                {
                    closestSqr = dSqr;
                    closest = Mathf.Sqrt(dSqr);
                    nearest = cgo;
                }
            }

            bool hadBolt = _hasNearestBolt;
            _hasNearestBolt = (nearest != null);
            _nearestBolt = nearest;
            if (nearest != null)
                _boltScanThrottle = 0;

            string newBoltName = nearest != null ? nearest.name : "null";
            if (newBoltName != _lastBoltName)
            {
                _lastBoltName = newBoltName;
                if (nearest != null)
                {

                    PlayMakerHelpers.DebugLog("[BOLT-2] Bolt found: '" + nearest.name
                        + "' layer=" + nearest.layer
                        + " tag=" + nearest.tag
                        + " dist=" + closest.ToString("F3"));

                }
                else
                {

                    PlayMakerHelpers.DebugLog("[BOLT-2] Bolt lost: Check FSM state='"
                        + (_checkFsm.ActiveStateName ?? "null") + "'");

                }
            }

            float perfMs = (Time.realtimeSinceStartup - perfStart) * 1000f;
            if (perfMs > 1f)
                PlayMakerHelpers.DebugLog("[PERF] UpdateNearestBolt: " + perfMs.ToString("F2") + "ms");

            FsmGameObject boltVar = _checkFsm.FsmVariables.GetFsmGameObject("Bolt");
            if (boltVar == null)
            {
                if (_lastBoltName != "VAR_MISSING")
                {

                    PlayMakerHelpers.DebugLog("[BOLT-2] ERROR: Check FSM has no 'Bolt' FsmGameObject variable!");

                    _lastBoltName = "VAR_MISSING";
                }
                return;
            }
            boltVar.Value = nearest;

            string checkState = _checkFsm.ActiveStateName ?? "";

            if (nearest != null && checkState == "Wait bolt")
            {
                float boltSize = GetBoltSize(nearest);
                float toolSize = _lastPickedToolSize;

                bool isSpWrench = (_lastPickedWorldName != null
                    && _lastPickedWorldName.ToLower().Contains("sparkplug"))
                    || IsSparkplugWrenchEquipped();
                bool sizeMatch = (isSpWrench && boltSize == 0f)
                    || (boltSize > 0f && toolSize > 0f && Mathf.Abs(toolSize - boltSize) < 0.05f);

                PlayMakerHelpers.DebugLog("[BOLT-2] Size check: tool=" + toolSize.ToString("F2")
                    + " bolt=" + boltSize.ToString("F2")
                    + " spWrench=" + isSpWrench + " match=" + sizeMatch);


                if (sizeMatch)
                {
                    FsmBool ratchetGlobal = FsmVariables.GlobalVariables.GetFsmBool("PlayerHasRatchet");
                    bool hasRatchet = (ratchetGlobal != null && ratchetGlobal.Value);
                    string scrollState = hasRatchet ? "Get Mouse scroll 2" : "Get Mouse scroll";

                    PlayMakerHelpers.DebugLog("[BOLT-2] Forcing directly to '" + scrollState
                        + "' (ratchet=" + hasRatchet + ")");

                    PlayMakerHelpers.ForceState(_checkFsm, scrollState);
                }
            }
            else if (nearest == null && hadBolt)
            {
                if (checkState == "Get Mouse scroll" || checkState == "Get Mouse scroll 2"
                    || checkState == "Delay" || checkState == "Delay 2"
                    || checkState == "Tighten" || checkState == "Untighten"
                    || checkState == "Tighten 2" || checkState == "Untighten 2"
                    || checkState == "Set Material" || checkState == "Set Material back")
                {

                    PlayMakerHelpers.DebugLog("[BOLT-2] Bolt lost from '" + checkState + "' → forcing 'Wait bolt'");

                    PlayMakerHelpers.ForceState(_checkFsm, "Wait bolt");
                }
            }

            if (checkState != _lastCheckState)
            {
                _lastCheckState = checkState;

                PlayMakerHelpers.DebugLog("[BOLT-2] Check FSM state → '" + checkState + "'");


                if (checkState == "Check tool")
                {
                    FsmFloat toolSizeGlobal = FsmVariables.GlobalVariables.GetFsmFloat("ToolWrenchSize");
                    string toolSizeStr = toolSizeGlobal != null ? toolSizeGlobal.Value.ToString("F3") : "NOT FOUND";
                    float boltSize = -1f;
                    bool hasScrewFsm = false;
                    if (nearest != null)
                    {
                        foreach (PlayMakerFSM fsm in nearest.GetComponents<PlayMakerFSM>())
                        {
                            if (fsm.FsmName != "Screw") continue;
                            hasScrewFsm = true;
                            FsmFloat bs = fsm.FsmVariables.GetFsmFloat("Boltsize");
                            if (bs != null) { boltSize = bs.Value; break; }
                        }
                    }

                    PlayMakerHelpers.DebugLog("[BOLT-2] Check tool:"
                        + " bolt='" + (nearest != null ? nearest.name : "null") + "'"
                        + " hasScrewFSM=" + hasScrewFsm
                        + " BoltBoltsize=" + boltSize.ToString("F3")
                        + " ToolWrenchSize=" + toolSizeStr
                        + " (must be within 0.02 to proceed → 'Set Material')");

                }
            }

            _boltLogTimer++;
            if (_boltLogTimer >= BoltLogInterval)
            {
                _boltLogTimer = 0;

                PlayMakerHelpers.DebugLog("[BOLT-2] Heartbeat: nearest='"
                    + (nearest != null ? nearest.name : "null")
                    + "' CheckState='" + checkState + "'");

            }
        }

        // =========================================================================
        // BOLT SYSTEM, STEP 3: Send tighten/untighten
        // =========================================================================

        public static void SendBoltTighten(bool tighten)
        {
            if (_checkFsm == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-3] DROPPED: _checkFsm is null");

                return;
            }
            if (_nearestBolt == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-3] DROPPED: no nearest bolt");

                return;
            }

            float boltSz = GetBoltSize(_nearestBolt);
            float toolSz = _lastPickedToolSize;
            bool spWrench3 = (_lastPickedWorldName != null
                && _lastPickedWorldName.ToLower().Contains("sparkplug"))
                || IsSparkplugWrenchEquipped();
            bool szOk = (spWrench3 && boltSz == 0f)
                     || (boltSz > 0f && toolSz > 0f && Mathf.Abs(toolSz - boltSz) < 0.05f);
            if (!szOk)
            {

                PlayMakerHelpers.DebugLog("[BOLT-3] DROPPED: size mismatch tool=" + toolSz.ToString("F2")
                    + " bolt=" + boltSz.ToString("F2") + " spWrench=" + spWrench3);

                return;
            }

            string state = _checkFsm.ActiveStateName ?? "null";

            if (state == "Delay" || state == "Delay 2"
             || state == "Tighten" || state == "Untighten"
             || state == "Tighten 2" || state == "Untighten 2")
            {

                PlayMakerHelpers.DebugLog("[BOLT-3] DROPPED: cycle in progress, state='" + state + "'");

                return;
            }

            FsmBool hasRatchet = FsmVariables.GlobalVariables.GetFsmBool("PlayerHasRatchet");
            bool isRatchet = hasRatchet != null && hasRatchet.Value;
            string scrollState = isRatchet ? "Get Mouse scroll 2" : "Get Mouse scroll";

            if (state == "Wait bolt" || state != scrollState)
            {
                FsmGameObject boltVar = _checkFsm.FsmVariables.GetFsmGameObject("Bolt");
                if (boltVar != null) boltVar.Value = _nearestBolt;
                PlayMakerHelpers.ForceState(_checkFsm, scrollState);
            }

            if (isRatchet)
            {
                PlayMakerHelpers.ForceState(_checkFsm, "Direction");

                PlayMakerHelpers.DebugLog("[BOLT-3] DIRECTION forced → Check FSM");

            }
            else
            {
                string evt = tighten ? "TIGHTEN" : "UNTIGHTEN";
                _checkFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(evt));

                PlayMakerHelpers.DebugLog("[BOLT-3] " + evt + " sent → Check FSM (state was '" + state + "')");

            }
        }

        // =========================================================================
        // BOLT SYSTEM, STEP 4: Tool size globals
        // =========================================================================

        public static void SetToolWrenchSizeForTool(GameObject toolObject)
        {
            if (toolObject == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-4] SetToolWrenchSize: toolObject is null");

                return;
            }

            FsmFloat toolSizeGlobal = FsmVariables.GlobalVariables.GetFsmFloat("ToolWrenchSize");
            if (toolSizeGlobal == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-4] ERROR: 'ToolWrenchSize' global float not found");

                return;
            }

            PlayMakerFSM[] toolFsms = toolObject.GetComponents<PlayMakerFSM>();

            PlayMakerHelpers.DebugLog("[BOLT-4] Scanning '" + toolObject.name + "': FSM count=" + toolFsms.Length
                + " | ToolWrenchSize global currently=" + toolSizeGlobal.Value);
            foreach (PlayMakerFSM fsm in toolFsms)
            {
                PlayMakerHelpers.DebugLog("[BOLT-4]   FSM: '" + fsm.FsmName + "'");
                if (fsm.FsmVariables != null && fsm.FsmVariables.FloatVariables != null)
                {
                    foreach (FsmFloat fv in fsm.FsmVariables.FloatVariables)
                        PlayMakerHelpers.DebugLog("[BOLT-4]     float: '" + fv.Name + "' = " + fv.Value);
                }
            }


            string[] sizeVarNames = { "WrenchSize", "ToolSize", "Size", "Boltsize" };
            foreach (PlayMakerFSM fsm in toolFsms)
            {
                foreach (string varName in sizeVarNames)
                {
                    FsmFloat sizeVar = fsm.FsmVariables.GetFsmFloat(varName);
                    if (sizeVar != null)
                    {
                        if (sizeVar.Value > 0f)
                        {
                            toolSizeGlobal.Value = sizeVar.Value;

                            PlayMakerHelpers.DebugLog("[BOLT-4] SET ToolWrenchSize from "
                                + toolObject.name + "." + fsm.FsmName + "." + varName
                                + " = " + sizeVar.Value);

                        }
                        else
                        {

                            PlayMakerHelpers.DebugLog("[BOLT-4] SKIP ToolWrenchSize from "
                                + toolObject.name + "." + fsm.FsmName + "." + varName
                                + " = 0 (keeping existing value " + toolSizeGlobal.Value + ")");

                        }
                        return;
                    }
                }
            }


            PlayMakerHelpers.DebugLog("[BOLT-4] No local size var found on '" + toolObject.name
                + "'; ToolWrenchSize=" + toolSizeGlobal.Value + " (set by game's Pick FSM)");

        }

        private static bool IsSparkplugWrenchEquipped()
        {
            FsmBool g = FsmVariables.GlobalVariables.GetFsmBool("PlayerHasSparkplugWrench");
            return g != null && g.Value;
        }

        public static void SetPlayerHasSparkplugWrench(bool has)
        {
            FsmBool g = FsmVariables.GlobalVariables.GetFsmBool("PlayerHasSparkplugWrench");

            if (g != null) { g.Value = has; PlayMakerHelpers.DebugLog("[BOLT-4] SetPlayerHasSparkplugWrench: " + has); }

        }

        public static void SetPlayerHasRatchet(bool hasRatchet)
        {
            FsmBool ratchetGlobal = FsmVariables.GlobalVariables.GetFsmBool("PlayerHasRatchet");
            if (ratchetGlobal == null)
            {

                PlayMakerHelpers.DebugLog("[BOLT-4] ERROR: 'PlayerHasRatchet' global bool not found");

                return;
            }
            ratchetGlobal.Value = hasRatchet;

            PlayMakerHelpers.DebugLog("[BOLT-4] SetPlayerHasRatchet: " + hasRatchet);

        }

        private static float GetBoltSize(GameObject bolt)
        {
            if (bolt == null) return 0f;
            foreach (PlayMakerFSM fsm in bolt.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Screw") continue;
                FsmFloat bs = fsm.FsmVariables.GetFsmFloat("Boltsize");
                if (bs != null) return bs.Value;
            }
            return 0f;
        }

        // =========================================================================
        // RATCHET DIRECTION TOGGLE
        // =========================================================================

        private static PlayMakerFSM FindSwitchFsm(GameObject tool)
        {
            if (tool == null) return null;
            foreach (PlayMakerFSM fsm in tool.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm.FsmName == "Switch" && FsmHasState(fsm, "Switch"))
                    return fsm;
            }
            Transform p = tool.transform.parent;
            while (p != null)
            {
                foreach (PlayMakerFSM fsm in p.GetComponents<PlayMakerFSM>())
                {
                    if (fsm.FsmName == "Switch" && FsmHasState(fsm, "Switch"))
                        return fsm;
                }
                p = p.parent;
            }
            if (_spanner2Root != null)
            {
                foreach (PlayMakerFSM fsm in _spanner2Root.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (fsm.FsmName == "Switch" && FsmHasState(fsm, "Switch"))
                        return fsm;
                }
            }
            return null;
        }

        public static void ToggleRatchetDirection()
        {
            if (VRHand.RightHandInstance == null)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] ToggleRatchetDirection: no right hand instance!");
                return;
            }

            GameObject tool = VRHand.RightHandInstance._toolInHand;
            if (tool == null)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] ToggleRatchetDirection: no tool in hand!");
                return;
            }

            PlayMakerFSM fsm = FindSwitchFsm(tool);
            if (fsm == null)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] ToggleRatchetDirection: Switch FSM not found in '"
                    + tool.name + "'");
                return;
            }

            string state = fsm.ActiveStateName ?? "";

            // If the previous toggle cycle hasn't completed yet, snap back to Input first.
            // This avoids a stuck FSM if the button is pressed twice quickly.
            if (state != "Input")
                PlayMakerHelpers.ForceState(fsm, "Input");

            // Advance Input → Switch via ProcessEvent (NOT ForceState) so that the
            // BoolTest action in Switch runs normally and routes to On or Off by itself.
            fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("FINISHED"));

            PlayMakerHelpers.DebugLog("[VRInteraction] ToggleRatchetDirection: toggled (was state='"
                + state + "')");
        }

        // =========================================================================
        // ACTION ENABLE/DISABLE HELPERS
        // =========================================================================

        private static void DisableActionsInState(PlayMakerFSM fsm, string stateName, string typeName)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            foreach (var state in fsm.FsmStates)
            {
                if (state.Name != stateName) continue;
                foreach (var action in state.Actions)
                    if (action.GetType().Name == typeName)
                        action.Enabled = false;
            }
        }

        private static void EnableActionsInState(PlayMakerFSM fsm, string stateName, string typeName)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            foreach (var state in fsm.FsmStates)
            {
                if (state.Name != stateName) continue;
                foreach (var action in state.Actions)
                    if (action.GetType().Name == typeName)
                        action.Enabled = true;
            }
        }

        // =========================================================================
        // ADVANCE "WAIT PLAYER N" STATES
        // Bypassed when VRFsmPatcher.PatcherActive is true. VRProximityActivateAction
        // injects directly into patched FSMs. This method is still called for objects
        // that have no patcher rule.
        // =========================================================================

        public static void AdvanceWaitPlayerState(GameObject obj)
        {
            if (obj == null) return;
            Transform t = obj.transform;
            while (t != null)
            {
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.FsmStates == null) continue;

                    string active = fsm.ActiveStateName;
                    if (string.IsNullOrEmpty(active)) continue;

                    if (!active.StartsWith("Wait player", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var state in fsm.FsmStates)
                    {
                        if (state.Name != active) continue;

                        string eventToFire = null;
                        foreach (var action in state.Actions)
                        {
                            if (action.GetType().Name != "MousePickEvent") continue;

                            try
                            {
                                FieldInfo fi = action.GetType().GetField("mouseOver");
                                if (fi != null)
                                {
                                    FsmEvent fsmEv = fi.GetValue(action) as FsmEvent;
                                    if (fsmEv != null && !string.IsNullOrEmpty(fsmEv.Name))
                                        eventToFire = fsmEv.Name;
                                }
                            }
                            catch { }

                            if (string.IsNullOrEmpty(eventToFire))
                                eventToFire = "FINISHED";

                            break;
                        }

                        if (!string.IsNullOrEmpty(eventToFire))
                        {

                            PlayMakerHelpers.DebugLog("[VRInteraction] AdvanceWaitPlayer: FSM='"
                                + fsm.gameObject.name + "' state='" + active
                                + "' → firing '" + eventToFire + "'");

                            fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(eventToFire));
                        }
                        break;
                    }
                }
                t = t.parent;
            }
        }

        // =========================================================================
        // GENERIC MOUSE ACTION INJECTION
        // FindMouseActionEvents / SendMouseActionDown / SendMouseActionUp are bypassed
        // when VRFsmPatcher.PatcherActive is true. VRTriggerPressAction and
        // VRTriggerHoldAction handle those objects via injected FSM actions.
        // These methods remain for objects with no patcher rule.
        // =========================================================================

        public struct MouseActionEvents
        {
            public PlayMakerFSM fsm;
            public string waitStateName;
            public string downEvent;
            public string heldStateName;
            public string upEvent;
            public string upStateName;
            public bool rightClick;
        }

        public static bool HasDispatcherState(GameObject obj)
        {
            if (obj == null) return false;
            string[] dispatcherNames = { "Check position", "Position" };
            Transform t = obj.transform;
            while (t != null)
            {
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.FsmStates == null) continue;
                    foreach (var state in fsm.FsmStates)
                    {
                        bool nameMatches = false;
                        foreach (var d in dispatcherNames)
                            if (state.Name == d) { nameMatches = true; break; }
                        if (!nameMatches) continue;
                        foreach (var action in state.Actions)
                            if (action is HutongGames.PlayMaker.Actions.BoolTest) return true;
                    }
                }
                t = t.parent;
            }
            return false;
        }

        public static MouseActionEvents? FindMouseActionEvents(
            GameObject obj, bool rightClick = false)
        {
            if (obj == null) return null;
            MouseButton btn = rightClick ? MouseButton.Right : MouseButton.Left;

            Transform t = obj.transform;
            while (t != null)
            {
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.FsmStates == null) continue;

                    var activeResult = TryExtractMouseEvents(fsm, fsm.ActiveStateName, btn, rightClick);
                    if (activeResult.HasValue) return activeResult;

                    foreach (var state in fsm.FsmStates)
                    {
                        if (state.Name == fsm.ActiveStateName) continue;
                        var result = TryExtractMouseEvents(fsm, state.Name, btn, rightClick);
                        if (result.HasValue) return result;
                    }
                }
                t = t.parent;
            }
            return null;
        }

        private static MouseActionEvents? TryExtractMouseEvents(
            PlayMakerFSM fsm, string stateName, MouseButton btn, bool rightClick)
        {
            foreach (var state in fsm.FsmStates)
            {
                if (state.Name != stateName) continue;

                string downEvent = null;
                foreach (var action in state.Actions)
                {
                    var md = action as GetMouseButtonDown;
                    if (md != null && md.button == btn)
                    { downEvent = md.sendEvent?.Name; break; }
                }
                if (string.IsNullOrEmpty(downEvent)) return null;

                string heldStateName = state.Name;
                foreach (var tr in state.Transitions)
                    if (tr.EventName == downEvent) { heldStateName = tr.ToState; break; }

                string upEvent = null, upStateName = null;
                foreach (var anyState in fsm.FsmStates)
                {
                    foreach (var action in anyState.Actions)
                    {
                        var mu = action as GetMouseButtonUp;
                        if (mu != null && mu.button == btn
                            && !string.IsNullOrEmpty(mu.sendEvent?.Name))
                        { upEvent = mu.sendEvent.Name; upStateName = anyState.Name; break; }
                    }
                    if (upEvent != null) break;
                }

                return new MouseActionEvents
                {
                    fsm = fsm,
                    waitStateName = state.Name,
                    downEvent = downEvent,
                    heldStateName = heldStateName,
                    upEvent = upEvent,
                    upStateName = upStateName,
                    rightClick = rightClick,
                };
            }
            return null;
        }

        public static void SendMouseActionDown(MouseActionEvents ev)
        {
            SetMousePickActionsEnabled(ev.fsm, ev.waitStateName, false);
            if (ev.heldStateName != ev.waitStateName)
                SetMousePickActionsEnabled(ev.fsm, ev.heldStateName, false);

            PlayMakerHelpers.ForceState(ev.fsm, ev.waitStateName);
            ev.fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(ev.downEvent));


            PlayMakerHelpers.DebugLog("[VRInteraction] MouseActionDown ("
                + (ev.rightClick ? "RIGHT" : "LEFT") + ")"
                + ": FSM='" + ev.fsm.gameObject.name
                + "' waitState='" + ev.waitStateName
                + "' downEvent='" + ev.downEvent
                + "' heldState='" + ev.heldStateName
                + "' upEvent='" + (ev.upEvent ?? "none") + "'");

        }

        public static void SendMouseActionUp(MouseActionEvents ev)
        {
            SetMousePickActionsEnabled(ev.fsm, ev.waitStateName, true);
            if (ev.heldStateName != ev.waitStateName)
                SetMousePickActionsEnabled(ev.fsm, ev.heldStateName, true);

            if (!string.IsNullOrEmpty(ev.upEvent)
                && !string.IsNullOrEmpty(ev.upStateName)
                && ev.fsm.ActiveStateName == ev.upStateName)
            {
                ev.fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(ev.upEvent));
            }


            PlayMakerHelpers.DebugLog("[VRInteraction] MouseActionUp ("
                + (ev.rightClick ? "RIGHT" : "LEFT") + ")"
                + ": FSM='" + ev.fsm.gameObject.name
                + "' activeState='" + ev.fsm.ActiveStateName
                + "' upStateName='" + (ev.upStateName ?? "none")
                + "' upEvent='" + (ev.upEvent ?? "none") + "'");

        }

        // =========================================================================
        // PUBLIC ENTRY POINTS
        // =========================================================================

        public static void TryUseHeld(GameObject heldObject)
        {
            if (heldObject == null) return;
            TryInteract(heldObject);
        }

        public static void TryInteract(GameObject target)
        {
            if (target == null) return;

            PlayMakerFSM useFsm = FindInteractFsm(target);
            if (useFsm != null)
            {
                if (TryFollowUseTransition(useFsm)) return;
                if (TryDispatcherState(useFsm)) return;

                if (IsInMouseOffOrOverState(useFsm))
                {
                    if (TryUniversalClick(useFsm)) return;
                    if (TryHardcodedDoorState(useFsm)) return;

                    PlayMakerHelpers.DebugLog("[VRInteraction] Could not interact with '"
                        + useFsm.gameObject.name + "': no click action found");

                    return;
                }


                PlayMakerHelpers.DebugLog("[VRInteraction] Fallback USE on '"
                    + useFsm.gameObject.name + "' (state: '" + useFsm.ActiveStateName + "')");

                useFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("USE"));
                return;
            }

            TrySendOpenClose(target);
        }

        // =========================================================================
        // TOOL MODE
        // =========================================================================

        private static PlayMakerFSM _selectItemFsm = null;
        private static GameObject _spanner2Root = null;

        public static void ToggleToolMode(bool skipAttach = false)
        {
            PlayMakerFSM selFsm = GetSelectItemFsm();
            if (selFsm == null)
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] ToggleToolMode: SelectItem FSM not found");

                return;
            }

            VRHand.ToolModeActive = !VRHand.ToolModeActive;

            if (VRHand.ToolModeActive)
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] ToggleToolMode → Tools");

                PlayMakerHelpers.ForceState(selFsm, "Tools");
                if (!skipAttach)
                    AttachToolToHand();
            }
            else
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] ToggleToolMode → Hand");

                DetachToolFromHand();
                PlayMakerHelpers.ForceState(selFsm, "Hand");
            }
        }

        private static PlayMakerFSM GetPickUpFsm()
        {
            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return null;
            string[] path = { "Pivot", "AnimPivot", "Camera", "FPSCamera", "2Spanner", "Pick" };
            Transform t = player.transform;
            foreach (string step in path) { t = t.Find(step); if (t == null) return null; }
            foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "PickUp") return fsm;
            return null;
        }

        public static void AttachToolToHand()
        {
            if (VRHand.RightHandInstance == null) return;

            if (VRHand.RightHandInstance._toolInHand != null)
                DetachToolFromHand();

            PlayMakerFSM pickUpFsm = GetPickUpFsm();
            if (pickUpFsm == null)
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: PickUp FSM not found");

                return;
            }

            string[] varNames = {
                "HandSpanner", "HandScrewdriver", "HandSocket",
                "HandSocketplug", "HandSparkplug", "Ratchet"
            };

            string priorityVar = null;
            if (_lastPickedWorldName != null)
            {
                string wname = _lastPickedWorldName.ToLower();
                if (wname == "sparkplug socket(clone)")
                    priorityVar = "HandSocketplug";
                else if (wname == "lug wrench(clone)" || wname == "lug wrench")
                    priorityVar = "HandSpanner";
                else
                {
                    string stripped = wname;
                    if (stripped.EndsWith("(clone)"))
                        stripped = stripped.Substring(0, stripped.Length - 7).TrimEnd();
                    string prefix7 = stripped.Length >= 7 ? stripped.Substring(0, 7) : stripped;
                    if (prefix7 == "spanner")
                        priorityVar = "HandSpanner";
                    else if (prefix7 == "socket(")
                        priorityVar = "HandSocket";
                    else if (prefix7 == "screwdr")
                        priorityVar = "HandScrewdriver";
                    else if (prefix7 == "sparkpl")
                        priorityVar = "HandSparkplug";
                    else if (stripped == "ruler" || prefix7 == "ruler")
                        priorityVar = "HandSpanner";
                    else if (wname.Contains("socket"))
                        priorityVar = "HandSocket";
                    else if (wname.Contains("ratchet"))
                        priorityVar = "HandSocket";
                    else if (wname.Contains("screwdriver"))
                        priorityVar = "HandScrewdriver";
                    else if (wname.Contains("spanner") || wname.Contains("wrench"))
                        priorityVar = "HandSpanner";
                }
            }

            GameObject tool = null;
            string foundVarName = null;

            if (priorityVar != null)
            {
                FsmGameObject goVar = pickUpFsm.FsmVariables.GetFsmGameObject(priorityVar);
                if (goVar != null && goVar.Value != null)
                {
                    tool = goVar.Value;
                    foundVarName = priorityVar;

                    PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: active tool='"
                        + tool.name + "' via var '" + priorityVar + "' (name-matched '"
                        + _lastPickedWorldName + "')");

                }
            }

            if (tool == null)
            {
                foreach (string varName in varNames)
                {
                    FsmGameObject goVar = pickUpFsm.FsmVariables.GetFsmGameObject(varName);
                    if (goVar == null || goVar.Value == null) continue;
                    if (goVar.Value.activeSelf)
                    {
                        tool = goVar.Value;
                        foundVarName = varName;

                        PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: active tool='"
                            + tool.name + "' via var '" + varName + "' (activeSelf)");

                        break;
                    }
                }
            }

            if (tool == null)
            {
                GameObject pivot = getSpanner2Pivot();
                if (pivot != null)
                    for (int i = 0; i < pivot.transform.childCount; i++)
                    {
                        GameObject child = pivot.transform.GetChild(i).gameObject;
                        if (child.activeSelf) { tool = child; break; }
                    }
            }

            if (tool == null)
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: no active tool found");

                return;
            }

            GameObject pivot2 = getSpanner2Pivot();
            if (pivot2 != null)
            {
                Transform t = tool.transform;
                while (t.parent != null && t.parent.gameObject != pivot2)
                    t = t.parent;
                tool = t.gameObject;

                PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: pivot root resolved to '" + tool.name + "'");

            }

            if (pivot2 != null)
            {
                for (int i = 0; i < pivot2.transform.childCount; i++)
                {
                    GameObject child = pivot2.transform.GetChild(i).gameObject;
                    bool shouldBeActive = (child == tool);
                    if (child.activeSelf != shouldBeActive)
                        child.SetActive(shouldBeActive);
                }
            }
            else if (!tool.activeSelf)
            {
                tool.SetActive(true);
            }

            if (tool.name != null && tool.name.ToLower().Contains("ratchet"))
            {
                bool wantSocket = (foundVarName == "HandSocket");
                bool wantPlugSocket = (foundVarName == "HandSocketplug");
                for (int i = 0; i < tool.transform.childCount; i++)
                {
                    Transform rch = tool.transform.GetChild(i);
                    string rname = rch.name.ToLower();
                    if (rname == "socket")
                        rch.gameObject.SetActive(wantSocket);
                    else if (rname == "plugsocket")
                        rch.gameObject.SetActive(wantPlugSocket);
                }

                PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: Ratchet sub-children set"
                    + " Socket=" + wantSocket + " PlugSocket=" + wantPlugSocket);

            }

            if (_lastPickedToolSize > 0f)
            {
                for (int i = 0; i < tool.transform.childCount; i++)
                {
                    Transform mch = tool.transform.GetChild(i);
                    if (mch.name == "mesh")
                    {
                        mch.localScale = new Vector3(_lastPickedToolSize, _lastPickedToolSize, _lastPickedToolSize);

                        PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: scaled mesh to "
                            + _lastPickedToolSize);

                        break;
                    }
                }
                FsmFloat twGlobal = FsmVariables.GlobalVariables.GetFsmFloat("ToolWrenchSize");
                if (twGlobal != null)
                {
                    twGlobal.Value = _lastPickedToolSize;

                    PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: ToolWrenchSize set to "
                        + _lastPickedToolSize);

                }
            }

            Transform controllerTransform = VRHand.RightHandInstance.transform;
            tool.transform.SetParent(controllerTransform, false);
            CacheToolRigidbodiesAndSetKinematic(tool);

            Vector3 posOff = Vector3.zero;
            Vector3 rotOff = Vector3.zero;
            if (ToolPosOffsets.ContainsKey(tool.name)) posOff = ToolPosOffsets[tool.name];
            if (ToolRotOffsets.ContainsKey(tool.name)) rotOff = ToolRotOffsets[tool.name];

            tool.transform.localPosition = posOff;
            tool.transform.localRotation = Quaternion.Euler(rotOff);

            VRHand.RightHandInstance._toolInHand = tool;

            if (_lastPickedWorldObject != null)
            {
                Transform wMesh = _lastPickedWorldObject.transform.Find("mesh");
                if (wMesh != null && wMesh.gameObject.activeSelf)
                    wMesh.gameObject.SetActive(false);
            }

            PlayMakerHelpers.DebugLog("[VRInteraction] AttachToolToHand: '" + tool.name
                + "' parented to right controller");


            SetToolWrenchSizeForTool(tool);
            bool isRatchet = (foundVarName == "Ratchet")
                          || (tool.name != null && tool.name.ToLower().Contains("ratchet"));
            SetPlayerHasRatchet(isRatchet);
        }

        private static void DetachToolFromHand()
        {
            if (VRHand.RightHandInstance == null) return;
            GameObject tool = VRHand.RightHandInstance._toolInHand;
            if (tool == null) return;

            RestoreToolRigidbodies();

            GameObject pivot = getSpanner2Pivot();
            if (pivot != null)
                tool.transform.SetParent(pivot.transform, true);
            else
                tool.transform.SetParent(null, true);

            VRHand.RightHandInstance._toolInHand = null;

            if (_lastPickedWorldObject != null)
            {
                Transform prevMesh = _lastPickedWorldObject.transform.Find("mesh");
                if (prevMesh != null)
                    prevMesh.gameObject.SetActive(true);
            }

            PlayMakerHelpers.DebugLog("[VRInteraction] DetachToolFromHand: '" + tool.name + "' returned to Pivot");

        }

        private static void CacheToolRigidbodiesAndSetKinematic(GameObject tool)
        {
            _toolRigidbodyKinematicBackup.Clear();
            if (tool == null) return;

            Rigidbody[] bodies = tool.GetComponentsInChildren<Rigidbody>(true);
            if (bodies == null) return;

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody rb = bodies[i];
                if (rb == null) continue;
                if (!_toolRigidbodyKinematicBackup.ContainsKey(rb))
                    _toolRigidbodyKinematicBackup.Add(rb, rb.isKinematic);

                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private static void RestoreToolRigidbodies()
        {
            if (_toolRigidbodyKinematicBackup.Count == 0) return;

            foreach (System.Collections.Generic.KeyValuePair<Rigidbody, bool> kvp in _toolRigidbodyKinematicBackup)
            {
                if (kvp.Key != null)
                    kvp.Key.isKinematic = kvp.Value;
            }

            _toolRigidbodyKinematicBackup.Clear();
        }

        private static PlayMakerFSM GetSelectItemFsm()
        {
            if (_selectItemFsm != null) return _selectItemFsm;
            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return null;
            string[] path = { "Pivot", "AnimPivot", "Camera", "FPSCamera", "SelectItem" };
            Transform t = player.transform;
            foreach (string step in path) { t = t.Find(step); if (t == null) return null; }
            foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
                if (FsmHasState(fsm, "Hand") && FsmHasState(fsm, "Tools"))
                { _selectItemFsm = fsm; return fsm; }
            return null;
        }

        private static GameObject getSpanner2Pivot()
        {
            if (_spanner2Root != null)
            {
                Transform piv = _spanner2Root.transform.Find("Pivot");
                if (piv != null) return piv.gameObject;
            }
            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return null;
            string[] path = { "Pivot", "AnimPivot", "Camera", "FPSCamera", "2Spanner", "Pivot" };
            Transform t = player.transform;
            foreach (string step in path) { t = t.Find(step); if (t == null) return null; }
            _spanner2Root = t.parent.gameObject;
            return t.gameObject;
        }

        // =========================================================================
        // FLAT-MODE HAND FSM
        // =========================================================================

        private static PlayMakerFSM _handPickupFsm = null;
        private static readonly string[] HandPickupObjectVarNames =
        {
            "RaycastHitObject",
            "RaycastHitTarget",
            "PickedObject",
            "PickedPart",
            "PickedTool",
            "PickedItem",
            "GameObject",
            "HeldObject",
            "Item",
            "Part",
        };

        public static void DisableFlatHandFsm()
        {
            PlayMakerFSM fsm = GetHandPickupFsm();
            if (fsm == null) return;
            fsm.enabled = false;

            PlayMakerHelpers.DebugLog("[VRInteraction] Flat Hand PickUp FSM disabled.");

        }

        private static PlayMakerFSM GetHandPickupFsm()
        {
            if (_handPickupFsm != null) return _handPickupFsm;
            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return null;
            string[] path = { "Pivot", "AnimPivot", "Camera", "FPSCamera", "1Hand_Assemble", "Hand" };
            Transform t = player.transform;
            foreach (string step in path) { t = t.Find(step); if (t == null) return null; }
            foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "PickUp" && FsmHasState(fsm, "Look for object"))
                { _handPickupFsm = fsm; return fsm; }
            return null;
        }

        public static void ResetHandPickupFsmStaleObject(GameObject staleObject)
        {
            PlayMakerFSM fsm = GetHandPickupFsm();
            if (fsm == null || fsm.FsmVariables == null)
                return;

            FsmGameObject[] vars = fsm.FsmVariables.GameObjectVariables;
            if (vars == null || vars.Length == 0)
                return;

            bool clearedAny = false;
            for (int i = 0; i < vars.Length; i++)
            {
                FsmGameObject fsmVar = vars[i];
                if (fsmVar == null) continue;

                GameObject value = fsmVar.Value;
                if (value == null) continue;

                bool matchesStale = staleObject != null && value == staleObject;
                bool inactive = !value.activeInHierarchy;
                if (!matchesStale && !inactive)
                    continue;

                bool isKnownPickupVar = false;
                for (int n = 0; n < HandPickupObjectVarNames.Length; n++)
                {
                    if (!string.Equals(fsmVar.Name, HandPickupObjectVarNames[n], StringComparison.Ordinal))
                        continue;

                    isKnownPickupVar = true;
                    break;
                }

                if (!matchesStale && !isKnownPickupVar)
                    continue;

                fsmVar.Value = null;
                clearedAny = true;

#if MVRC_DEBUG
                PlayMakerHelpers.DebugLog("[VRInteraction] Cleared PickUp var '" + fsmVar.Name
                    + "' on 1Hand_Assemble.PickUp");
#endif
            }

            if (!clearedAny)
                return;

            VRFsmActionBridge.PickupCandidate_R = null;
            VRFsmActionBridge.PickupCandidate_L = null;
            VRFsmActionBridge.ClearPickupOwner(fsm);

            string state = fsm.ActiveStateName ?? string.Empty;
            bool pickedLikeState = state == "Part picked"
                || state == "Tool picked"
                || state == "Item picked"
                || state == "Ragdoll picked"
                || state == "Set pivot"
                || state == "State 5"
                || state == "State 4";

            if (pickedLikeState)
            {
                if (FsmHasEvent(fsm, "PROCEED Drop"))
                    fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("PROCEED Drop"));
                else if (FsmHasState(fsm, "Look for object"))
                    PlayMakerHelpers.ForceState(fsm, "Look for object");

                PlayMakerHelpers.DebugLog("[VRInteraction] Reset 1Hand_Assemble.PickUp from state '"
                    + state + "' after stale object cleanup");
            }
        }

        // =========================================================================
        // TOOL PICKUP FROM WORLD
        // =========================================================================

        public static bool IsPickableTool(Collider c)
        {
            if (c == null) return false;
            if (c.gameObject.layer != 12) return false;
            if (c.gameObject.tag != "Untagged") return false;
            if (c.gameObject.name == "BoltPM") return false;
            return true;
        }

        public static bool TryPickUpTool(GameObject toolObject)
        {
            if (toolObject == null) return false;

            PlayMakerFSM pickUpFsm = GetPickUpFsm();
            if (pickUpFsm == null)
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] TryPickUpTool: PickUp FSM not found");

                return false;
            }

            string fullName = toolObject.name;

            PlayMakerHelpers.DebugLog("[VRInteraction] TryPickUpTool: object='" + fullName + "'");


            if (_lastPickedWorldObject != null && _lastPickedWorldObject != toolObject)
            {
                Transform prevMesh = _lastPickedWorldObject.transform.Find("mesh");
                if (prevMesh != null)
                    prevMesh.gameObject.SetActive(true);
            }
            {
                Transform newMesh = toolObject.transform.Find("mesh");
                if (newMesh != null)
                    newMesh.gameObject.SetActive(false);
            }

            _lastPickedWorldName = fullName;
            _lastPickedWorldObject = toolObject;
            _lastPickedToolSize = 0f;
            for (int ci = 0; ci < toolObject.transform.childCount; ci++)
            {
                Transform child = toolObject.transform.GetChild(ci);
                if (child.name == "mesh")
                {
                    _lastPickedToolSize = child.localScale.x;

                    PlayMakerHelpers.DebugLog("[VRInteraction] TryPickUpTool: mesh child scale="
                        + _lastPickedToolSize + " from '" + fullName + "'");

                    break;
                }
            }

            FsmGameObject pickedToolVar = pickUpFsm.FsmVariables.GetFsmGameObject("PickedTool");
            if (pickedToolVar != null) pickedToolVar.Value = toolObject;

            FsmString toolFullNameVar = pickUpFsm.FsmVariables.GetFsmString("ToolFullName");
            if (toolFullNameVar != null) toolFullNameVar.Value = fullName;

            if (pickUpFsm.ActiveStateName != "Look for object")
                PlayMakerHelpers.ForceState(pickUpFsm, "Look for object");

            pickUpFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("FINISHED"));

            PlayMakerHelpers.DebugLog("[VRInteraction] TryPickUpTool: FINISHED fired on PickUp FSM");

            return true;
        }

        // =========================================================================
        // ASSEMBLY SYSTEM
        // =========================================================================

        public static void NotifyHandFsmGrab(GameObject grabbed)
        {
            if (grabbed == null)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] NotifyHandFsmGrab: grabbed is null");
                return;
            }
            DebugVinpSearch(grabbed);

            GameObject vinp = FindTargetVinp(grabbed);
            if (vinp == null)
            {
                PlayMakerFSM partData = FindFsmNamed(grabbed, "Data");
                if (partData != null && FsmHasState(partData, "Far")
                    && FsmHasEvent(partData, "CHECK"))
                {

                    PlayMakerHelpers.DebugLog("[VRInteraction] NotifyHandFsmGrab: CHECK → own Data FSM of '"
                        + grabbed.name + "'");

                    partData.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("CHECK"));
                }
                else
                {

                    PlayMakerHelpers.DebugLog("[VRInteraction] NotifyHandFsmGrab: no VINP for '"
                        + grabbed.name + "'");

                }
                return;
            }

            PlayMakerFSM vinpData = GetDataFsm(vinp);
            if (vinpData == null)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] NotifyHandFsmGrab: VINP '"
                    + vinp.name + "' has no Data FSM for part '" + grabbed.name + "'");
                return;
            }

            FsmGameObject activePart = vinpData.FsmVariables.GetFsmGameObject("ActivePart");
            if (activePart != null)
            {
                activePart.Value = grabbed;

                PlayMakerHelpers.DebugLog("[VRInteraction] NotifyHandFsmGrab: ActivePart='"
                    + grabbed.name + "' on '" + vinp.name + "'");

            }


            PlayMakerHelpers.DebugLog("[VRInteraction] NotifyHandFsmGrab: CHECK → '" + vinp.name + "'");

            vinpData.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("CHECK"));
        }

        public static void NotifyHandFsmDrop(GameObject dropped)
        {
            if (dropped == null) return;

            GameObject vinp = FindTargetVinp(dropped);
            if (vinp != null)
            {
                PlayMakerFSM vinpData = GetDataFsm(vinp);
                if (vinpData != null)
                {
                    string s = vinpData.ActiveStateName;
                    if (s == "Far" || s == "Near" || s == "Allow install?" || s == "Check parent")
                    {
                        vinpData.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("BACK"));

                        PlayMakerHelpers.DebugLog("[VRInteraction] NotifyHandFsmDrop: BACK → '" + vinp.name + "'");

                    }
                    return;
                }
            }

            PlayMakerFSM partData = FindFsmNamed(dropped, "Data");
            if (partData != null)
            {
                string s = partData.ActiveStateName;
                if (s == "Far" || s == "Near" || s == "Another part?")
                    partData.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("STOP"));
            }
        }

        public static void ActivateAssembly(GameObject part, out PlayMakerFSM installerFsm, out GameObject vinp)
        {
            installerFsm = null;
            vinp = null;
            if (part == null)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: part is null "
                    + DescribeActiveInteractionTargets());
                return;
            }

            vinp = FindTargetVinp(part);
            if (vinp != null)
            {
                PlayMakerFSM vinpData = GetDataFsm(vinp);
                if (vinpData != null)
                {
                    FsmGameObject activePartVar = vinpData.FsmVariables.GetFsmGameObject("ActivePart");
                    if (activePartVar != null) activePartVar.Value = part;

                    FsmBool allowInstall = vinpData.FsmVariables.GetFsmBool("AllowInstall");
                    if (allowInstall != null) allowInstall.Value = true;

                    if (FsmHasEvent(vinpData, "CHECK"))
                        vinpData.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("CHECK"));
                    else
                        PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: VINP '"
                            + vinp.name + "' missing CHECK event in Data FSM");


                    PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: CHECK → VINP '"
                        + vinp.name + "' for '" + part.name + "' "
                        + DescribeActiveInteractionTargets());

                    installerFsm = vinpData;
                    return;
                }

                PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: VINP '"
                    + vinp.name + "' found for '" + part.name
                    + "' but Data FSM is missing");
            }

            PlayMakerFSM partData = FindFsmNamed(part, "Data");
            if (partData != null)
            {
                bool hasNearState = FsmHasState(partData, "Near");
                bool hasCheckEvent = FsmHasEvent(partData, "CHECK");
                if (hasNearState)
                {
                    if (hasCheckEvent)
                        partData.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("CHECK"));
                    else
                        PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: part Data FSM has Near state but no CHECK event for '"
                            + part.name + "'");

                    installerFsm = partData;
                    PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: using part Data FSM for '"
                        + part.name + "' state='" + (partData.ActiveStateName ?? "null") + "'");
                    return;
                }

                PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: part Data FSM for '"
                    + part.name + "' has no Near state (active='"
                    + (partData.ActiveStateName ?? "null") + "')");
            }
            else
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: part '"
                    + part.name + "' has no Data FSM");
            }


            PlayMakerHelpers.DebugLog("[VRInteraction] ActivateAssembly: no installer for '"
                + part.name + "' " + DescribeActiveInteractionTargets());

        }

        public static void DeactivateAssembly(PlayMakerFSM installerFsm, GameObject vinp)
        {
            PlayMakerFSM target = installerFsm;
            if (target == null && vinp != null) target = GetDataFsm(vinp);
            if (target == null)
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] DeactivateAssembly: no target FSM"
                    + " installer='" + (installerFsm != null
                        ? installerFsm.gameObject.name + "." + installerFsm.FsmName
                        : "null") + "'"
                    + " vinp='" + (vinp != null ? vinp.name : "null") + "'");
                return;
            }

            string s = target.ActiveStateName;
            if (s == "Far" || s == "Near" || s == "Allow install?" || s == "Check parent")
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] DeactivateAssembly: BACK → '"
                    + target.gameObject.name + "." + target.FsmName + "'");

                target.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("BACK"));
            }
            else
            {
                PlayMakerHelpers.DebugLog("[VRInteraction] DeactivateAssembly: not sending BACK because state='"
                    + (s ?? "null") + "' on '" + target.gameObject.name + "." + target.FsmName + "'");
            }
        }

        public static bool CheckAssemblyProximity(PlayMakerFSM installerFsm, GameObject vinp,
            GameObject heldObject = null)
        {
            string unusedReason;
            return CheckAssemblyProximity(installerFsm, vinp, heldObject, out unusedReason);
        }

        public static bool CheckAssemblyProximity(PlayMakerFSM installerFsm, GameObject vinp,
            GameObject heldObject, out string reason)
        {
            reason = string.Empty;
            if (heldObject == null)
            {
                reason = "heldObject is null";
                return LogAssemblyProximityDecision(false, reason, heldObject, vinp, installerFsm);
            }

            if (vinp != null)
            {
                float dist = Vector3.Distance(heldObject.transform.position, vinp.transform.position);
                bool inRange = dist < 0.20f;
                reason = inRange
                    ? "VINP distance OK (" + dist.ToString("F3") + " < 0.20)"
                    : "VINP too far (" + dist.ToString("F3") + " >= 0.20)";
                return LogAssemblyProximityDecision(inRange, reason, heldObject, vinp, installerFsm);
            }

            if (installerFsm != null)
            {
                FsmBool allowInstall = installerFsm.FsmVariables.GetFsmBool("AllowInstall");
                if (allowInstall != null && allowInstall.Value)
                {
                    reason = "installer AllowInstall is true";
                    return LogAssemblyProximityDecision(true, reason, heldObject, vinp, installerFsm);
                }

                string state = installerFsm.ActiveStateName ?? string.Empty;
                if (state == "Near"
                    || state == "Allow install?"
                    || state == "Another part?"
                    || state == "Check parent")
                {
                    reason = "installer state allows assembly ('" + state + "')";
                    return LogAssemblyProximityDecision(true, reason, heldObject, vinp, installerFsm);
                }

                reason = "installer state blocks assembly ('" + state + "') and AllowInstall is false";
                return LogAssemblyProximityDecision(false, reason, heldObject, vinp, installerFsm);
            }

            reason = "no VINP and no installer FSM";
            return LogAssemblyProximityDecision(false, reason, heldObject, vinp, installerFsm);
        }

        public static bool TryAssemble(GameObject heldObject,
            PlayMakerFSM installerFsm, GameObject vinp)
        {
            if (heldObject != null)
            {
                if (VRFsmActionBridge.HeldObject_R == heldObject)
                    VRFsmActionBridge.HeldObject_R = null;
                if (VRFsmActionBridge.HeldObject_L == heldObject)
                    VRFsmActionBridge.HeldObject_L = null;

                Transform parent = heldObject.transform.parent;
                while (parent != null)
                {
                    if (string.Equals(parent.name, "PLAYER", StringComparison.OrdinalIgnoreCase))
                    {
                        heldObject.transform.SetParent(null, true);
                        break;
                    }
                    parent = parent.parent;
                }

                Rigidbody rb = heldObject.GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic)
                    rb.isKinematic = false;
            }

            if (vinp != null)
            {
                if (heldObject != null)
                {
                    PlayMakerFSM partData = FindFsmNamed(heldObject, "Data");
                    if (partData != null)
                    {
                        foreach (var state in partData.FsmStates)
                        {
                            if (state.Name != "Install 1") continue;
                            foreach (var action in state.Actions)
                                if (action.GetType().Name == "ArrayListGet")
                                    action.Enabled = false;
                        }
                    }
                }

                PlayMakerFSM installer = FindAssembyDatabaseInstaller();
                if (installer != null)
                {
                    FsmGameObject ap = installer.FsmVariables.GetFsmGameObject("ActivePart");
                    if (ap != null) ap.Value = heldObject;
                    FsmGameObject apoint = installer.FsmVariables.GetFsmGameObject("AssemblyPoint");
                    if (apoint != null) apoint.Value = vinp;
                }
                else
                {
                    PlayMakerHelpers.DebugLog("[VRInteraction] TryAssemble: AssembyDatabase installer missing while VINP='"
                        + vinp.name + "'");
                }

                PlayMakerFSM vinpData = GetDataFsm(vinp);
                if (vinpData != null)
                {
                    PlayMakerHelpers.ForceState(vinpData, "Near");
                    vinpData.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("PROCEED"));

                    PlayMakerHelpers.DebugLog("[VRInteraction] TryAssemble: PROCEED → VINP '"
                        + vinp.name + "' for '" + (heldObject != null ? heldObject.name : "null") + "'");

                    return true;
                }

                PlayMakerHelpers.DebugLog("[VRInteraction] TryAssemble: VINP '"
                    + vinp.name + "' has no Data FSM for part '"
                    + (heldObject != null ? heldObject.name : "null") + "'");
            }

            if (installerFsm != null)
            {
                installerFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("PROCEED"));

                PlayMakerHelpers.DebugLog("[VRInteraction] TryAssemble: PROCEED → installerFsm '"
                    + installerFsm.FsmName + "' state='"
                    + (installerFsm.ActiveStateName ?? "null") + "'");

                return true;
            }


            PlayMakerHelpers.DebugLog("[VRInteraction] TryAssemble: no target for held='"
                + (heldObject != null ? heldObject.name : "null") + "' vinp='"
                + (vinp != null ? vinp.name : "null") + "' "
                + DescribeActiveInteractionTargets());

            return false;
        }

        private static GameObject FindTargetVinp(GameObject part)
        {
            if (part == null) return null;
            return FindNearestVinp(part, part.transform.position);
        }

        private static GameObject FindNearestVinp(GameObject part, Vector3 nearPosition)
        {
            if (part == null) return null;

            var candidates = new System.Collections.Generic.List<GameObject>();

            PlayMakerFSM dataFsm = FindFsmNamed(part, "Data");
            if (dataFsm != null)
            {
                foreach (var state in dataFsm.FsmStates)
                {
                    if (state.Name != "Install 1") continue;
                    foreach (var action in state.Actions)
                    {
                        string typeName = action.GetType().Name;

                        if (typeName == "SetFsmGameObject")
                        {
                            try
                            {
                                foreach (var field in action.GetType().GetFields(
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    object val = field.GetValue(action);
                                    if (val == null) continue;
                                    FsmGameObject fsmGo = val as FsmGameObject;
                                    if (fsmGo != null && fsmGo.Value != null
                                        && fsmGo.Value.name.StartsWith("VINP_"))
                                    {
                                        if (!candidates.Contains(fsmGo.Value))
                                            candidates.Add(fsmGo.Value);
                                    }
                                }
                            }
                            catch { }
                        }

                        if (typeName == "ArrayListGet")
                        {
                            try
                            {
                                string listName = null;
                                FieldInfo refField = action.GetType().GetField("reference",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (refField != null)
                                {
                                    object refVal = refField.GetValue(action);
                                    if (refVal is string) listName = (string)refVal;
                                    else if (refVal != null)
                                    {
                                        PropertyInfo valProp = refVal.GetType().GetProperty("Value");
                                        if (valProp != null) listName = valProp.GetValue(refVal, null) as string;
                                        if (listName == null) listName = refVal.ToString();
                                    }
                                }


                                PlayMakerHelpers.DebugLog("[VINP] ArrayListGet: list name='" + (listName ?? "null") + "'");

                                if (string.IsNullOrEmpty(listName)) continue;

                                GameObject corrisGo = GameObject.Find("CORRIS");
                                if (corrisGo == null) continue;
                                Transform db = corrisGo.transform.Find("AssembyDatabase");
                                if (db == null) continue;

                                Component[] allComps = db.GetComponents<Component>();
                                foreach (Component comp in allComps)
                                {
                                    if (comp == null) continue;
                                    if (comp.GetType().Name != "PlayMakerArrayListProxy") continue;

                                    FieldInfo nameField = comp.GetType().GetField("referenceName",
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (nameField == null) continue;
                                    string refName = nameField.GetValue(comp) as string;
                                    if (refName != listName) continue;

                                    FieldInfo listField = comp.GetType().GetField("_arrayList",
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (listField == null) continue;
                                    System.Collections.IList list =
                                        listField.GetValue(comp) as System.Collections.IList;
                                    if (list == null) continue;


                                    PlayMakerHelpers.DebugLog("[VINP] Found proxy '" + listName
                                        + "' with " + list.Count + " entries");

                                    for (int li = 0; li < list.Count; li++)
                                    {
                                        GameObject go = list[li] as GameObject;
                                        if (go != null && go.name.StartsWith("VINP_")
                                            && !candidates.Contains(go))
                                            candidates.Add(go);
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {

                                PlayMakerHelpers.DebugLog("[VINP] ArrayListGet reflection error: " + ex.Message);

                            }
                        }
                    }
                }
            }

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                PlayMakerFSM targetFsm = GetDataFsm(candidates[i]);
                if (targetFsm == null) continue;
                FsmBool installed = targetFsm.FsmVariables.GetFsmBool("Installed");
                if (installed != null && installed.Value)
                {

                    PlayMakerHelpers.DebugLog("[VINP] Skipping '" + candidates[i].name + "' (already installed)");

                    candidates.RemoveAt(i);
                }
            }

            if (candidates.Count == 0)
            {
                GameObject corrisFb = GameObject.Find("CORRIS");
                if (corrisFb != null)
                {
                    Transform dbFb = corrisFb.transform.Find("AssembyDatabase");
                    Transform vinpsChild = dbFb != null ? dbFb.Find("VINPS") : null;
                    if (vinpsChild != null)
                    {
                        string raw = part.name;
                        int paren = raw.IndexOf('(');
                        string fragment = (paren > 0 ? raw.Substring(0, paren) : raw)
                            .Trim().Replace(" ", "");

                        foreach (Component comp in vinpsChild.GetComponents<Component>())
                        {
                            if (comp == null || comp.GetType().Name != "PlayMakerArrayListProxy") continue;
                            FieldInfo lf = comp.GetType().GetField("_arrayList",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            System.Collections.IList list = lf != null
                                ? lf.GetValue(comp) as System.Collections.IList : null;
                            if (list == null) continue;

                            PlayMakerHelpers.DebugLog("[VINP] Master proxy: " + list.Count + " entries, fragment='" + fragment + "'");

                            for (int li = 0; li < list.Count; li++)
                            {
                                GameObject go = list[li] as GameObject;
                                if (go != null && go.name.StartsWith("VINP_")
                                    && go.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0
                                    && !candidates.Contains(go))
                                    candidates.Add(go);
                            }
                        }
                    }
                }
            }

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            GameObject nearest = null;
            float minDist = float.MaxValue;
            foreach (GameObject vinp in candidates)
            {
                if (vinp == null) continue;
                float d = Vector3.Distance(nearPosition, vinp.transform.position);
                if (d < minDist) { minDist = d; nearest = vinp; }
            }

            PlayMakerHelpers.DebugLog("[VINP] " + candidates.Count + " slots for '" + part.name
                + "', nearest='" + (nearest != null ? nearest.name : "null")
                + "' dist=" + minDist.ToString("F2"));

            return nearest;
        }

        private static void CollectVinpsByFragment(Transform root, string fragment,
            System.Collections.Generic.List<GameObject> results)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name.StartsWith("VINP_") &&
                    child.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!results.Contains(child.gameObject))
                        results.Add(child.gameObject);
                }
                CollectVinpsByFragment(child, fragment, results);
            }
        }

        private static PlayMakerFSM FindAssembyDatabaseInstaller()
        {
            GameObject corris = GameObject.Find("CORRIS");
            if (corris == null) return null;
            Transform db = corris.transform.Find("AssembyDatabase");
            if (db == null) return null;
            foreach (PlayMakerFSM fsm in db.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Installer") return fsm;
            return null;
        }

        private static PlayMakerFSM GetDataFsm(GameObject go)
        {
            foreach (PlayMakerFSM fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Data") return fsm;
            return null;
        }

        public static bool TryDisassemble(GameObject target)
        {
            if (target == null) return false;

            Transform t = target.transform;
            while (t != null)
            {
                foreach (PlayMakerFSM fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (fsm.FsmName != "Data") continue;
                    if (!FsmHasState(fsm, "Mouse over")) continue;
                    if (!FsmHasState(fsm, "Remove")) continue;

                    SetMousePickActionsEnabled(fsm, "Mouse over", false);
                    PlayMakerHelpers.ForceState(fsm, "Mouse over");
                    fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("PROCEED"));
                    SetMousePickActionsEnabled(fsm, "Mouse over", true);


                    PlayMakerHelpers.DebugLog("[VRInteraction] TryDisassemble: PROCEED on "
                        + fsm.gameObject.name);

                    return true;
                }
                t = t.parent;
            }
            return false;
        }

        // =========================================================================
        // KNOB INTERACTION
        // Bypassed when VRFsmPatcher.PatcherActive is true. VRKnobTwistAction manages
        // the entire knob lifecycle (StartKnobInteraction, tick, EndKnobInteraction)
        // for all patched knob objects. These methods remain for knobs with no rule.
        // =========================================================================

        public static PlayMakerFSM FindKnobFsm(GameObject target)
        {
            Transform t = target.transform;
            while (t != null)
            {
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                    if (IsKnobFsm(fsm)) return fsm;
                t = t.parent;
            }
            return null;
        }

        public static bool IsKnobFsm(PlayMakerFSM fsm)
        {
            foreach (var state in fsm.FsmStates)
            {
                if (!string.Equals(state.Name, "Get scroll", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var tr in state.Transitions)
                    if (tr.EventName == "TIGHTEN" || tr.EventName == "UNTIGHTEN")
                        return true;
            }
            return false;
        }

        public static void StartKnobInteraction(PlayMakerFSM fsm)
        {

            PlayMakerHelpers.DebugLog("[VRInteraction] Knob start: '" + fsm.gameObject.name + "'");

            SetMousePickActionsEnabled(fsm, "Get scroll", false);
            PlayMakerHelpers.ForceState(fsm, "Get scroll");
        }

        public static void TickKnob(PlayMakerFSM fsm, bool tighten)
        {
            fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(tighten ? "TIGHTEN" : "UNTIGHTEN"));
        }

        public static void ForceKnobToGetScroll(PlayMakerFSM fsm)
        {
            if (fsm.ActiveStateName != "Get scroll")
                PlayMakerHelpers.ForceState(fsm, "Get scroll");
        }

        public static void EndKnobInteraction(PlayMakerFSM fsm)
        {

            PlayMakerHelpers.DebugLog("[VRInteraction] Knob end: '" + fsm.gameObject.name + "'");

            SetMousePickActionsEnabled(fsm, "Get scroll", true);
            fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("OFF"));
        }

        private static void SetMousePickActionsEnabled(
            PlayMakerFSM fsm, string stateName, bool enabled)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            foreach (var state in fsm.FsmStates)
            {
                if (state.Name != stateName) continue;
                foreach (var action in state.Actions)
                    if (action.GetType().Name == "MousePickEvent")
                        action.Enabled = enabled;
            }
        }

        // =========================================================================
        // PRIORITY 1: Follow the USE transition.
        // =========================================================================

        private static bool TryFollowUseTransition(PlayMakerFSM fsm)
        {
            foreach (var state in fsm.FsmStates)
            {
                if (!string.Equals(state.Name, "Wait button", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var tr in state.Transitions)
                {
                    if (tr.EventName != "USE") continue;

                    PlayMakerHelpers.DebugLog("[VRInteraction] FollowUse '" + fsm.gameObject.name
                        + "': 'Wait button' → USE → '" + tr.ToState + "'");

                    PlayMakerHelpers.ForceState(fsm, tr.ToState);
                    return true;
                }
            }

            foreach (var state in fsm.FsmStates)
                foreach (var tr in state.Transitions)
                {
                    if (tr.EventName != "USE") continue;

                    PlayMakerHelpers.DebugLog("[VRInteraction] FollowUse '" + fsm.gameObject.name
                        + "': '" + state.Name + "' → USE → '" + tr.ToState + "'");

                    PlayMakerHelpers.ForceState(fsm, tr.ToState);
                    return true;
                }

            return false;
        }

        // =========================================================================
        // PRIORITY 2: Dispatcher state.
        // =========================================================================

        private static bool TryDispatcherState(PlayMakerFSM fsm)
        {
            string[] dispatcherNames = { "Check position", "Position" };
            foreach (var stateName in dispatcherNames)
            {
                FsmState dispatchState = null;
                foreach (var s in fsm.FsmStates)
                    if (s.Name == stateName) { dispatchState = s; break; }
                if (dispatchState == null) continue;

                foreach (var action in dispatchState.Actions)
                {
                    var boolTest = action as HutongGames.PlayMaker.Actions.BoolTest;
                    if (boolTest == null) continue;

                    bool currentValue = boolTest.boolVariable != null && boolTest.boolVariable.Value;
                    string isTrueName = boolTest.isTrue?.Name ?? "";
                    string isFalseName = boolTest.isFalse?.Name ?? "";

                    bool isRestore = IsPositiveEventName(isTrueName) && IsNegativeEventName(isFalseName);
                    string eventName = (currentValue ^ isRestore) ? isTrueName : isFalseName;
                    if (string.IsNullOrEmpty(eventName)) continue;

                    foreach (var transition in dispatchState.Transitions)
                    {
                        if (transition.EventName != eventName) continue;
                        string targetState = transition.ToState;

                        if (IsMouseOverStateName(targetState))
                        {
                            string deeper = FollowMouseClickFrom(fsm, targetState);
                            if (deeper != null)
                            {

                                PlayMakerHelpers.DebugLog("[VRInteraction] Dispatcher '"
                                    + fsm.gameObject.name + "': bool=" + currentValue
                                    + " → (via '" + targetState + "') → '" + deeper + "'");

                                PlayMakerHelpers.ForceState(fsm, deeper);
                                return true;
                            }
                        }


                        PlayMakerHelpers.DebugLog("[VRInteraction] Dispatcher '"
                            + fsm.gameObject.name + "': bool=" + currentValue
                            + " → '" + targetState + "'");

                        PlayMakerHelpers.ForceState(fsm, targetState);
                        return true;
                    }


                    PlayMakerHelpers.DebugLog("[VRInteraction] Dispatcher '"
                        + fsm.gameObject.name + "': bool=" + currentValue
                        + " → ProcessEvent '" + eventName + "'");

                    fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(eventName));
                    return true;
                }
            }
            return false;
        }

        // =========================================================================
        // PRIORITY 3: Universal click.
        // =========================================================================

        private static bool TryUniversalClick(PlayMakerFSM fsm)
        {
            foreach (var state in fsm.FsmStates)
            {
                string eventToSend = null;
                foreach (var action in state.Actions)
                {
                    var mouseAction = action as GetMouseButtonDown;
                    if (mouseAction != null && mouseAction.button == MouseButton.Left)
                    { eventToSend = mouseAction.sendEvent?.Name; break; }

                    var btnAction = action as GetButtonDown;
                    if (btnAction != null && btnAction.buttonName?.Value == "Use")
                    { eventToSend = btnAction.sendEvent?.Name; break; }
                }
                if (string.IsNullOrEmpty(eventToSend)) continue;

                foreach (var transition in state.Transitions)
                {
                    if (transition.EventName != eventToSend) continue;

                    PlayMakerHelpers.DebugLog("[VRInteraction] UniversalClick '" + fsm.gameObject.name
                        + "' state '" + state.Name + "' → '" + eventToSend
                        + "' → '" + transition.ToState + "'");

                    PlayMakerHelpers.ForceState(fsm, transition.ToState);
                    return true;
                }
            }
            return false;
        }

        // =========================================================================
        // HARDCODED DOOR FALLBACK
        // =========================================================================

        private static bool TryHardcodedDoorState(PlayMakerFSM fsm)
        {
            FsmBool openVar = fsm.FsmVariables.GetFsmBool("Open");
            bool isOpen = openVar != null && openVar.Value;
            string targetState = isOpen ? "Close door" : "Open door";
            if (!FsmHasState(fsm, targetState))
                targetState = isOpen ? "Open door" : "Close door";
            if (FsmHasState(fsm, targetState))
            {

                PlayMakerHelpers.DebugLog("[VRInteraction] HardcodedDoor '" + fsm.gameObject.name
                    + "': forcing '" + targetState + "'");

                PlayMakerHelpers.ForceState(fsm, targetState);
                return true;
            }
            return false;
        }

        // =========================================================================
        // OPEN/CLOSE FALLBACK
        // =========================================================================

        private static bool TrySendOpenClose(GameObject target)
        {
            PlayMakerFSM[] allFsms = target.GetComponentsInParent<PlayMakerFSM>();
            foreach (var fsm in allFsms)
            {
                bool hasOpen = FsmHasEvent(fsm, "OPEN");
                bool hasClose = FsmHasEvent(fsm, "CLOSE");
                if (!hasOpen && !hasClose) continue;

                FsmBool openVar = fsm.FsmVariables.GetFsmBool("Open");
                bool isOpen = openVar != null && openVar.Value;
                string eventName = isOpen ? "CLOSE" : "OPEN";
                bool canSend = isOpen ? hasClose : hasOpen;
                if (canSend)
                {

                    PlayMakerHelpers.DebugLog("[VRInteraction] OpenClose: sending " + eventName
                        + " to '" + fsm.FsmName + "' on '" + fsm.gameObject.name + "'");

                    fsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent(eventName));
                    return true;
                }
            }
            return false;
        }

        // =========================================================================
        // FSM SEARCH
        // =========================================================================

        private static PlayMakerFSM FindInteractFsm(GameObject target)
        {
            string[] knownNames = { "Use", "Switch", "Starter" };
            foreach (var name in knownNames)
            {
                PlayMakerFSM found = FindFsmNamed(target, name);
                if (found != null) return found;
            }

            Transform t = target.transform;
            while (t != null)
            {
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                    foreach (var e in fsm.FsmEvents)
                        if (e.Name == "USE") return fsm;
                t = t.parent;
            }
            return null;
        }

        // =========================================================================
        // HELPERS
        // =========================================================================

        private static string FollowMouseClickFrom(PlayMakerFSM fsm, string stateName)
        {
            foreach (var state in fsm.FsmStates)
            {
                if (state.Name != stateName) continue;
                foreach (var action in state.Actions)
                {
                    string eventToSend = null;
                    var mouseAction = action as GetMouseButtonDown;
                    if (mouseAction != null && mouseAction.button == MouseButton.Left)
                        eventToSend = mouseAction.sendEvent?.Name;
                    if (eventToSend == null)
                    {
                        var btnAction = action as GetButtonDown;
                        if (btnAction != null && btnAction.buttonName?.Value == "Use")
                            eventToSend = btnAction.sendEvent?.Name;
                    }
                    if (string.IsNullOrEmpty(eventToSend)) continue;
                    foreach (var transition in state.Transitions)
                        if (transition.EventName == eventToSend) return transition.ToState;
                }
            }
            return null;
        }

        private static bool IsPositiveEventName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            string u = n.ToUpper();
            return u == "ON" || u == "OPEN" || u.StartsWith("ON_") || u.StartsWith("OPEN_");
        }

        private static bool IsNegativeEventName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            string u = n.ToUpper();
            return u == "OFF" || u == "CLOSE" || u == "CLOSED"
                || u.StartsWith("OFF_") || u.StartsWith("CLOSE_");
        }

        private static bool IsMouseOverStateName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var s in MouseOffOrOverStates)
                if (string.Equals(name, s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static PlayMakerFSM FindFsmNamed(GameObject target, string fsmName)
        {
            Transform t = target.transform;
            while (t != null)
            {
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == fsmName) return fsm;
                t = t.parent;
            }
            return null;
        }

        private static bool IsInMouseOffOrOverState(PlayMakerFSM fsm)
        {
            string active = fsm.ActiveStateName;
            foreach (var s in MouseOffOrOverStates)
                if (string.Equals(active, s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool FsmHasState(PlayMakerFSM fsm, string stateName)
        {
            foreach (var state in fsm.FsmStates)
                if (state.Name == stateName) return true;
            return false;
        }

        private static bool FsmHasEvent(PlayMakerFSM fsm, string eventName)
        {
            foreach (var e in fsm.FsmEvents)
                if (e.Name == eventName) return true;
            return false;
        }

        public static void DebugVinpSearch(GameObject part)
        {

            PlayMakerHelpers.DebugLog("[VINP-DBG] === searching for '" + part.name + "' ===");


            PlayMakerFSM dataFsm = FindFsmNamed(part, "Data");

            PlayMakerHelpers.DebugLog("[VINP-DBG] Data FSM found: " + (dataFsm != null));

            if (dataFsm != null)
            {
                bool hasInstall1 = FsmHasState(dataFsm, "Install 1");

                PlayMakerHelpers.DebugLog("[VINP-DBG] Has 'Install 1' state: " + hasInstall1);

                if (hasInstall1)
                {
                    foreach (var state in dataFsm.FsmStates)
                    {
                        if (state.Name != "Install 1") continue;
                        foreach (var action in state.Actions)
                        {

                            PlayMakerHelpers.DebugLog("[VINP-DBG]   Install 1 action: " + action.GetType().Name);

                        }
                    }
                }
            }

            GameObject corris = GameObject.Find("CORRIS");

            PlayMakerHelpers.DebugLog("[VINP-DBG] CORRIS found: " + (corris != null));

            if (corris != null)
            {
                Transform db = corris.transform.Find("AssembyDatabase");

                PlayMakerHelpers.DebugLog("[VINP-DBG] AssembyDatabase found: " + (db != null));

                if (db != null)
                {

                    PlayMakerHelpers.DebugLog("[VINP-DBG] AssembyDatabase child count: " + db.childCount);
                    for (int i = 0; i < Mathf.Min(db.childCount, 10); i++)
                        PlayMakerHelpers.DebugLog("[VINP-DBG]   child: '" + db.GetChild(i).name + "'");


                    string raw = part.name;
                    int paren = raw.IndexOf('(');
                    string fragment = (paren > 0 ? raw.Substring(0, paren) : raw).Trim().Replace(" ", "");


                    PlayMakerHelpers.DebugLog("[VINP-DBG] Fragment to search: '" + fragment + "'");


                    int found = 0;
                    for (int i = 0; i < db.childCount; i++)
                    {
                        string cname = db.GetChild(i).name;
                        if (cname.StartsWith("VINP_") && cname.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {

                            PlayMakerHelpers.DebugLog("[VINP-DBG]   MATCH: '" + cname + "'");

                            found++;
                        }
                    }

                    PlayMakerHelpers.DebugLog("[VINP-DBG] Total matches: " + found);

                }
            }
        }
    }

}