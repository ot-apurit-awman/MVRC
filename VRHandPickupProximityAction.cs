// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using UnityEngine;
using MSCLoader;
using HutongGames.PlayMaker;
using System.Reflection;
using System.Collections.Generic;

namespace MVRC
{
    // ─────────────────────────────────────────────────────────────────────────────
    // VRHandPickupProximityAction
    //
    // Replaces the MousePick action in the PickUp FSM's "Look for object" state.
    //
    // The original MousePick stores its hit into a FsmGameObject variable called
    // "RaycastHitObject". This action does a proximity sphere scan every frame
    // and writes the nearest valid object into that SAME variable. Every downstream
    // action (GetName, GetStringLength, IntOperator, etc.) reads from that variable
    // so they all continue working with zero changes.
    //
    // Scan rules match what the original layerMask=3 (layers 0+1) and layer 12
    // (tools/bolts) cover pickable parts and tools in practice.
    //
    // The "Look for object" state also has GetMouseButtonDown (sendEvent: "FINISHED")
    // which should be disabled separately and replaced with VRTriggerPressAction.
    //
    // To use: add a rule in VRInjectionConfig targeting the PickUp FSM's
    // "Look for object" state with method = VRInteractionMethod.PickupProximity.
    // Add PickupProximity to the VRInteractionMethod enum and wire it in BuildAction.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRHandPickupProximityAction : VRInjectedAction
    {
        // Scan radius in metres. Keep it generous because the player moves while holding
        // the controller and the scan needs to stay stable.
        private const float ScanRadius = 0.20f;

        // The FsmGameObject variable we write into every frame.
        // This is what GetName, GetStringLength, IntOperator all read from.
        private FsmGameObject _raycastHitObjectVar = null;

        // Layers the original MousePick searched (layerMask=3 = layers 0 and 1)
        // plus layer 12 (tools/pickable world items). Parts are layer 19.
        private static readonly int[] ScanLayers = new int[] { 0, 1, 12, 19 };
        private int _layerMask = 0;
        private readonly Collider[] _scanHitsBuffer = new Collider[48];
        private const int ScanIntervalFrames = 1;
        private int _scanCountdown = 0;

        private int _logTimer = 0;
        private GameObject _lastLoggedNearest = null;
        private GameObject _lastNearestRight = null;
        private GameObject _lastNearestLeft = null;
        private GameObject _lastNearest = null;

        public override void OnEnter()
        {
            // Resolve the target variable once when entering the state.
            // The variable lives on the same FSM this action is part of.
            _raycastHitObjectVar = null;
            if (targetFsm != null)
            {
                _raycastHitObjectVar = targetFsm.FsmVariables
                    .GetFsmGameObject("RaycastHitObject");

                if (_raycastHitObjectVar == null)
                    PlayMakerHelpers.DebugLog("[VRPickup] WARNING: RaycastHitObject variable not found on "
                        + targetFsm.gameObject.name);
            }

            // Build combined layer mask from our scan layers
            _layerMask = 0;
            foreach (int layer in ScanLayers)
                _layerMask |= (1 << layer);

            _scanCountdown = 0;
            _logTimer = 0;
            _lastLoggedNearest = null;
            _lastNearestRight = null;
            _lastNearestLeft = null;
            _lastNearest = null;
            PlayMakerHelpers.DebugLog("[VRPickup] ProximityAction OnEnter, layerMask=" + _layerMask
                + " varFound=" + (_raycastHitObjectVar != null));
        }

        public override void OnUpdate()
        {
            bool shouldRescan = _scanCountdown <= 0;
            if (shouldRescan)
                _scanCountdown = ScanIntervalFrames;
            else
                _scanCountdown--;

            Vector3 rightPos = VRFsmActionBridge.HandPosition_R;
            Vector3 leftPos  = VRFsmActionBridge.HandPosition_L;

            bool hasRightHand = VRHand.RightHandInstance != null
                || VRProximitySphereDebug.RightHandTransform != null;
            bool hasLeftHand = VRHand.LeftHandInstance != null;

            GameObject nearestRight = _lastNearestRight;
            GameObject nearestLeft = _lastNearestLeft;

            if (shouldRescan)
            {
                nearestRight = hasRightHand ? FindNearest(rightPos) : null;
                nearestLeft  = hasLeftHand ? FindNearest(leftPos) : null;
                _lastNearestRight = nearestRight;
                _lastNearestLeft = nearestLeft;
            }

            VRFsmActionBridge.PickupCandidate_R = nearestRight;
            VRFsmActionBridge.PickupCandidate_L = nearestLeft;

            bool rightIntent = VRFsmActionBridge.GripJustPressed_R
                            || VRFsmActionBridge.GripIsHeld_R
                            || VRFsmActionBridge.TriggerJustPressed_R
                            || VRFsmActionBridge.TriggerIsHeld_R;
            bool leftIntent = VRFsmActionBridge.GripJustPressed_L
                           || VRFsmActionBridge.GripIsHeld_L
                           || VRFsmActionBridge.TriggerJustPressed_L
                           || VRFsmActionBridge.TriggerIsHeld_L;
        
            GameObject nearest = null;

            if (rightIntent && !leftIntent)
            {
                nearest = nearestRight;
            }
            else if (leftIntent && !rightIntent)
            {
                nearest = nearestLeft;
            }
            else
            {
                if (nearestRight != null && nearestLeft != null)
                {
                    float scoreR = VRFsmActionBridge.GetCenterPriorityScore(nearestRight, rightPos);
                    float scoreL = VRFsmActionBridge.GetCenterPriorityScore(nearestLeft, leftPos);
                    nearest = scoreR <= scoreL ? nearestRight : nearestLeft;
                }
                else
                {
                    nearest = nearestRight ?? nearestLeft;
                }
            }

            _lastNearest = nearest;
        
            if (_raycastHitObjectVar != null)
                _raycastHitObjectVar.Value = nearest;

            // Log target changes and periodic status
            if (nearest != _lastLoggedNearest)
            {
                PlayMakerHelpers.DebugLog("[VRPickup] Scan target → '"
                    + (nearest != null ? nearest.name : "null") + "'");
                _lastLoggedNearest = nearest;
            }
            
            _logTimer++;
            if (_logTimer % 180 == 0)
                PlayMakerHelpers.DebugLog("[VRPickup] Scan periodic: nearest='"
                    + (nearest != null ? nearest.name : "null") + "'");
        }
        private GameObject FindNearest(Vector3 handPos)
        {
            int hitCount = PhysicsCompat.OverlapSphereNonAllocIgnoreTriggers(
                handPos,
                ScanRadius,
                _scanHitsBuffer,
                _layerMask);

            GameObject nearest = null;
            float nearestScore = float.MaxValue;
            float nearestSurfaceDist = float.MaxValue;
            HashSet<int> seenCandidates = new HashSet<int>();
            
            for (int i = 0; i < hitCount; i++)
            {
                Collider c = _scanHitsBuffer[i];
                if (c == null) continue;

                bool toolHit = VRInteraction.IsPickableTool(c);
                GameObject go = toolHit
                    ? c.gameObject
                    : VRFsmActionBridge.ResolveInteractionTargetRoot(c.gameObject);
                if (go == null) continue;

                if (toolHit && IsToolContainerCandidate(go))
                    continue;

                int instanceId = go.GetInstanceID();
                if (seenCandidates.Contains(instanceId))
                    continue;
                seenCandidates.Add(instanceId);

                if (!IsValidPickupCandidate(go, toolHit))
                    continue;

                float surfaceDist = VRFsmActionBridge.GetClosestSurfaceDistanceToObject(go, handPos);
                if (surfaceDist > ScanRadius)
                    continue;

                float score = VRFsmActionBridge.GetCenterPriorityScore(go, handPos);
                if (score < nearestScore
                    || (Mathf.Abs(score - nearestScore) <= 0.0001f && surfaceDist < nearestSurfaceDist))
                {
                    nearestScore = score;
                    nearestSurfaceDist = surfaceDist;
                    nearest = go;
                }
            }
            
            return nearest;
        }

        private static bool IsValidPickupCandidate(GameObject go, bool toolHit)
        {
            if (go == null)
                return false;

            if (go == VRFsmActionBridge.HeldObject_R || go == VRFsmActionBridge.HeldObject_L)
                return false;

            if (IsAttachedToAnyHand(go))
                return false;

            if (go.name.StartsWith("MVRC_")) return false;
            if (go.name == "PlayerColl" || go.name == "Pivot"
                || go.name == "PLAYER" || go.name == "ElectricShockPoint") return false;
            if (go.name.Contains("ShockPoint") || go.name.Contains("Trigger")) return false;

            if (toolHit)
            {
                Collider col = go.GetComponent<Collider>();
                return col != null && VRInteraction.IsPickableTool(col);
            }

            if (go.GetComponent<Rigidbody>() == null)
                return false;

            if (go.GetComponent<HingeJoint>() != null) return false;
            if (go.GetComponent<FixedJoint>() != null) return false;
            if (go.GetComponent<SpringJoint>() != null) return false;

            ConfigurableJoint cj = go.GetComponent<ConfigurableJoint>();
            if (cj != null && cj.connectedBody != null)
                return false;
            
            return true;
        }

        private static bool IsAttachedToAnyHand(GameObject go)
        {
            if (go == null) return false;

            Transform rightHand = VRHand.RightHandInstance != null
                ? VRHand.RightHandInstance.transform
                : VRProximitySphereDebug.RightHandTransform;
            if (rightHand != null && go.transform.IsChildOf(rightHand))
                return true;

            Transform leftHand = VRHand.LeftHandInstance != null
                ? VRHand.LeftHandInstance.transform
                : null;
            return leftHand != null && go.transform.IsChildOf(leftHand);
        }

        private static bool IsToolContainerCandidate(GameObject go)
        {
            if (go == null)
                return false;

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
        public override void OnExit()
        {
            // Do NOT clear RaycastHitObject here.
            // When FINISHED fires and the FSM transitions to the next state (Check string,
            // Get name, etc.), those downstream states still need to read RaycastHitObject
            // to get the picked object's name and type. Clearing it in OnExit kills the
            // entire pickup chain before it can run.
            //
            // The variable will naturally be overwritten on the next entry to "Look for
            // object" (OnEnter re-resolves _raycastHitObjectVar and OnUpdate writes every frame).
            PlayMakerHelpers.DebugLog("[VRPickup] ProximityAction OnExit, last target='"
                + (_lastLoggedNearest != null ? _lastLoggedNearest.name : "null") + "'");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRPickupTriggerAction
    //
    // Replaces GetMouseButtonDown in the PickUp FSM's "Look for object" state.
    //
    // Unlike VRTriggerPressAction (which uses IsHandNear() to check if the hand
    // is near the FSM's gameObject), this action checks whether RaycastHitObject
    // has a valid value. This is critical because the PickUp FSM lives on the
    // player's hand object (1Hand_Assemble), NOT on the world object being
    // picked up, so IsHandNear() would always return false.
    //
    // Fires fsmEventName (typically "FINISHED") when the trigger is pressed AND
    // there is a valid object in RaycastHitObject (populated by
    // VRHandPickupProximityAction running in the same state).
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRPickupTriggerAction : VRInjectedAction
    {
        public static int SuppressFrames = 0;

        private FsmGameObject _raycastHitObjectVar;
        private bool _gripWasHeldR = false;
        private bool _gripWasHeldL = false;
        private int _logTimer = 0;

        public override void OnEnter()
        {
            _raycastHitObjectVar = targetFsm != null
                ? targetFsm.FsmVariables.GetFsmGameObject("RaycastHitObject")
                : null;
            _gripWasHeldR = VRFsmActionBridge.GripIsHeld_R;
            _gripWasHeldL = VRFsmActionBridge.GripIsHeld_L;
            _logTimer = 0;
            PlayMakerHelpers.DebugLog("[VRPickupTrig] OnEnter varFound=" + (_raycastHitObjectVar != null)
                + " obj='" + (targetFsm != null ? targetFsm.gameObject.name : "?") + "'");
        }

        public override void OnUpdate()
        {
            if (SuppressFrames > 0) { SuppressFrames--; return; }

            bool gripR = VRFsmActionBridge.GripIsHeld_R;
            bool gripL = VRFsmActionBridge.GripIsHeld_L;

            bool gripEdgeR = gripR && !_gripWasHeldR;
            bool gripEdgeL = gripL && !_gripWasHeldL;
            _gripWasHeldR = gripR;
            _gripWasHeldL = gripL;

            if (!gripEdgeR && !gripEdgeL) return;

            bool pickupRight;
            if (gripEdgeR && !gripEdgeL)
                pickupRight = true;
            else if (gripEdgeL && !gripEdgeR)
                pickupRight = false;
            else
                pickupRight = VRFsmActionBridge.ResolveIsRightHand(
                    targetFsm,
                    targetFsm != null ? targetFsm.gameObject : null);

            GameObject varObj = pickupRight
                ? VRFsmActionBridge.PickupCandidate_R
                : VRFsmActionBridge.PickupCandidate_L;

            if (varObj == null && _raycastHitObjectVar != null)
                varObj = _raycastHitObjectVar.Value;

            if (_raycastHitObjectVar != null)
                _raycastHitObjectVar.Value = varObj;

            VRFsmActionBridge.RegisterPickupOwner(targetFsm, pickupRight);

            if (_logTimer % 120 == 0)
                PlayMakerHelpers.DebugLog("[VRPickupTrig] varObj='"
                    + (varObj != null ? varObj.name : "null")
                    + "' hand=" + (pickupRight ? "R" : "L"));
            _logTimer++;

            if (_raycastHitObjectVar == null || varObj == null) return;

            PlayMakerHelpers.DebugLog("[VRPickupTrig] FIRING '" + fsmEventName + "' for '" + varObj.name + "'");
            FireConfiguredEvent();
        }

        public override void OnExit()
        {
            _gripWasHeldR = false;
            _gripWasHeldL = false;
            PlayMakerHelpers.DebugLog("[VRPickupTrig] OnExit");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRPickupReparentAction
    //
    // Replaces or supplements the SetParent action in "Part picked" and "Tool picked"
    // states of the PickUp FSM.
    //
    // The original SetParent parents the picked-up object to FPSCamera, which in VR
    // makes it float at the face. This action parents it to the right VR hand instead,
    // matching what VRHand.TryGrab() does for manually grabbed objects.
    //
    // The FsmGameObject variable that holds the part/tool is usually "PickedPart"
    // or "PickedTool". Confirm in the inspector and set targetObjectVarName below.
    //
    // Usage: add a separate rule targeting "Part picked" and "Tool picked" states
    // on the PickUp FSM, with method = VRInteractionMethod.PickupReparent.
    // Disable the original SetParent action (DisableActionFlags: add the SetParent flag).
    // or use stateNameContains to target only those states.
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRPickupReparentAction : VRInjectedAction
    {
        // Name of the FsmGameObject variable holding the picked object.
        // Check the inspector: in "Part picked" it's likely "RaycastHitObject"
        // or a dedicated "PickedPart" var. Set this on the rule before patching.
        public string targetObjectVarName = "RaycastHitObject";
        private bool _attachedToRightHand = true;

        public override void OnEnter()
        {
            if (targetFsm == null) return;
            // Try known variable names in order of likelihood.
            // "PickedObject" is confirmed from SetJointConnectedBody's rigidBody field.
            // "GameObject" is what SetParent's gameObject field shows in the inspector.
            string[] candidateVars = {
                targetObjectVarName,
                "PickedObject",
                "RaycastHitObject",
                "GameObject",
                "PickedPart",
                "PickedTool",
                "PickedItem",
                "HeldObject",
                "Item",
                "Part"
            };

            GameObject obj = null;
            string foundVar = null;

            foreach (string varName in candidateVars)
            {
                if (string.IsNullOrEmpty(varName)) continue;
                FsmGameObject goVar = targetFsm.FsmVariables.GetFsmGameObject(varName);
                if (goVar != null && goVar.Value != null)
                {
                    obj = goVar.Value;
                    foundVar = varName;
                    break;
                }
            }

            // Fallback: PickedObject may be stored as a rigidbody reference. Find it
            // by looking at all FsmGameObject variables that have a non-null value and
            // have a Rigidbody component (i.e. a physics object, not a pivot/camera GO).
            if (obj == null)
            {
                foreach (FsmGameObject fgo in targetFsm.FsmVariables.GameObjectVariables)
                {
                    if (fgo == null || fgo.Value == null) continue;
                    if (fgo.Value.GetComponent<Rigidbody>() != null)
                    {
                        obj = fgo.Value;
                        foundVar = fgo.Name + "(rigidbody fallback)";
                        break;
                    }
                }
            }

            if (obj == null)
            {
                PlayMakerHelpers.DebugLog("[VRPickup] ReparentAction: could not find picked object on '"
                    + targetFsm.gameObject.name + "'; tried: "
                    + string.Join(", ", candidateVars));
                return;
            }

            bool pickupRight;
            if (VRFsmActionBridge.TryGetPickupOwner(targetFsm, out pickupRight))
                _attachedToRightHand = pickupRight;
            else
                _attachedToRightHand = VRFsmActionBridge.ResolveIsRightHand(targetFsm, obj);

            if (_attachedToRightHand && VRHand.RightHandInstance == null && VRProximitySphereDebug.RightHandTransform == null)
                _attachedToRightHand = false;
            if (!_attachedToRightHand && VRHand.LeftHandInstance == null)
                _attachedToRightHand = VRFsmActionBridge.ResolveIsRightHand(targetFsm, obj);

            VRFsmActionBridge.RegisterPickupOwner(targetFsm, _attachedToRightHand);

            Transform handTransform = _attachedToRightHand
                ? (VRHand.RightHandInstance != null
                    ? VRHand.RightHandInstance.transform
                    : VRProximitySphereDebug.RightHandTransform)
                : (VRHand.LeftHandInstance != null
                    ? VRHand.LeftHandInstance.transform
                    : null);

            if (handTransform == null)
            {
                PlayMakerHelpers.DebugLog("[VRPickup] ReparentAction: no "
                    + (_attachedToRightHand ? "right" : "left") + " hand instance");
                return;
            }

            obj.transform.SetParent(handTransform, true);

            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Rigidbody[] childBodies = obj.GetComponentsInChildren<Rigidbody>(true);
            if (childBodies != null)
            {
                for (int i = 0; i < childBodies.Length; i++)
                {
                    Rigidbody childRb = childBodies[i];
                    if (childRb == null) continue;
                    childRb.isKinematic = true;
                    childRb.velocity = Vector3.zero;
                    childRb.angularVelocity = Vector3.zero;
                }
            }
            // ── Tell the dispatcher this object is now held ───────────────────────────
            if (_attachedToRightHand)
                VRFsmActionBridge.HeldObject_R = obj;
            else
                VRFsmActionBridge.HeldObject_L = obj;
            // Layer 16 is already set by SetLayer in the same state, so it need not be repeated.

            PlayMakerHelpers.DebugLog("[VRPickup] Reparented '" + obj.name
                + "' (var='" + foundVar + "') to VR "
                + (_attachedToRightHand ? "right" : "left") + " hand");
            FireEvent("FINISHED");
        }

        public override void OnUpdate() { }
        public override void OnExit()
        {
            // Keep held ownership until grip release/drop so dispatcher and attachment
            // remain stable while the object is carried across multiple FSM states.
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // VRSetParentDisabler
    //
    // Utility: disables ALL SetParent actions in a state on OnEnter.
    // Append this BEFORE VRPickupReparentAction in the state so the original
    // SetParent is silenced before it can run. Needed because SetParent fires
    // in OnEnter, and our appended action also fires in OnEnter. Order matters.
    //
    // Alternative: use the patcher's DisableActionFlags to add a SetParent flag
    // (requires adding SetParent to the DisableActionFlags enum).
    // ─────────────────────────────────────────────────────────────────────────────
    public class VRSetParentDisabler : FsmStateAction
    {
        public override void OnEnter()
        {
            if (State == null) return;
            foreach (FsmStateAction action in State.Actions)
            {
                if (action == null) continue;
                if (action.GetType().Name == "SetParent")
                {
                    action.Enabled = false;
                    PlayMakerHelpers.DebugLog("[VRSetParentDisabler] Disabled SetParent in state '"
                        + State.Name + "'");
                }
            }
        }

        public override void OnUpdate() { }
        public override void OnExit() { }
    }
}
