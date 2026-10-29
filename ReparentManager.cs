// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using UnityEngine;
using MSCLoader;
using HutongGames.PlayMaker;

namespace MVRC
{
    /// <summary>
    /// Keeps configured scene objects parented to camera rig anchors with deterministic local transforms.
    /// Runs from VRManager update so it executes once per frame and stays independent from hand logic.
    /// </summary>
    public class VRCameraReparentManager
    {
        private struct CameraReparentItem
        {
            public string targetPath;
            public string parentPath;
            public bool playerRelative;
            public Vector3 localPosition;
            public Vector3 localEuler;
            public Vector3 localScale;
            public float resolveRetrySeconds;
        }

        private sealed class CameraReparentState
        {
            public CameraReparentItem item;
            public Transform target;
            public Transform parent;
            public Transform originalParent;
            public Vector3 originalLocalPosition;
            public Quaternion originalLocalRotation;
            public Vector3 originalLocalScale;
            public float nextTargetResolveTime;
            public float nextParentResolveTime;
            public bool originalCaptured;
        }

        private static readonly CameraReparentItem[] Items = new CameraReparentItem[]
        {
            new CameraReparentItem
            {
                targetPath = "Systems/OptionsMenu",
                parentPath = "PLAYER",
                playerRelative = false,
                localPosition = new Vector3(0.4f, 1.4f, 1.1f),
                localEuler = new Vector3(0f, 0f, 0f),
                localScale = new Vector3(0.05f, 0.05f, 0.06f),
                resolveRetrySeconds = 0.1f,
            },
            new CameraReparentItem
            {
                targetPath = "Sheets",
                parentPath = "MVRC_LeftHandOffset/MVRC_LeftHandPivot",
                playerRelative = false,
                localPosition = new Vector3(-1.121971f, 4.577742f, 0.1589609f),
                localEuler = new Vector3(48, 0f, 180f),
                localScale = new Vector3(0.5f, 0.5f, 0.5f),
                resolveRetrySeconds = 0.1f,
            },
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Watch/Animate/BreathAnim/WristwatchHand",
                parentPath = "MVRC_LeftHandOffset/MVRC_LeftHandPivot",
                playerRelative = false,
                localPosition = new Vector3(-0.003f, 0.004f, 0.1f),
                localEuler = new Vector3(270f, 255f, 5f),
                localScale = new Vector3(0.6f, 0.6f, 0.7f),
                resolveRetrySeconds = 0f,
            },
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/SaunaThrow",
                parentPath = "MVRC_RightHandOffset/MVRC_RightHandPivot",
                playerRelative = false,
                localPosition = new Vector3(0.08f, -0.09f, 0.25f),
                localEuler = new Vector3(331f, 163f, 98f),
                localScale = new Vector3(1.0f, 1.0f, 1.0f),
                resolveRetrySeconds = 0f,
            },          
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/SprayCan",
                parentPath = "MVRC_RightHandOffset/MVRC_RightHandPivot",
                playerRelative = false,
                localPosition = new Vector3(-0.1f, -0.083f, -0.44f),
                localEuler = new Vector3(324f, 24f, 280f),
                localScale = new Vector3(1.0f, 1.0f, 1.0f),
                resolveRetrySeconds = 0f,
            },        
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/SprayCan/Spray",
                parentPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/SprayCan",
                playerRelative = false,
                localPosition = new Vector3(0.18f, 0.11f, 0.181f),
                localEuler = new Vector3(0f, 0f, 0f),
                localScale = new Vector3(1.0f, 1.0f, 1.0f),
                resolveRetrySeconds = 0f,
            },            
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/IceScraper",
                parentPath = "MVRC_RightHandOffset/MVRC_RightHandPivot",
                playerRelative = false,
                localPosition = new Vector3(0.23f, -0.04f, 0.37f),
                localEuler = new Vector3(11f, 180f, 70f),
                localScale = new Vector3(1.0f, 1.0f, 1.0f),
                resolveRetrySeconds = 0f,
            },            
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/SaunaThrow/Raycast",
                parentPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/SaunaThrow",
                playerRelative = false,
                localPosition = new Vector3(0.01f, -0.4f, 0.5f),
                localEuler = new Vector3(300f, 270f, 90f),
                localScale = new Vector3(1.0f, 1.0f, 1.0f), 
                resolveRetrySeconds = 0f,
            },
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Flashlight",
                parentPath = "CABIN",
                playerRelative = false,
                localPosition = new Vector3(0f, -80f, 0.2f),
                localEuler = new Vector3(0f, 124f, 58f),
                localScale = new Vector3(1.0f, 1.0f, 1.0f),
                resolveRetrySeconds = 0f,
            },
            new CameraReparentItem
            {
                targetPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Dipstick",
                parentPath = "MVRC_RightHandOffset/MVRC_RightHandPivot/Dipstick",
                playerRelative = false,
                localPosition = new Vector3(0.21f, -0.075f, 0.148f),
                localEuler = new Vector3(332f, 199f, 50f),
                localScale = new Vector3(1.0f, 1.0f, 1.0f),
                resolveRetrySeconds = 0f,
            },
        };
        private readonly CameraReparentState[] _states;
        // ── Water launch point ────────────────────────────────────────────────────────
        // An empty child of SaunaThrow that marks where thrown water spawns.
        // Tune localPosition here until water exits from the ladle tip.
        // Other code reads this by name: transform.Find(WaterLaunchPointName)
        public const string WaterLaunchPointName = "WaterLaunchPoint";
        private static readonly Vector3 WaterLaunchLocalPosition = new Vector3(-0.06f, -0.06f, 0.5f);
        private static readonly Vector3 WaterLaunchLocalEuler    = new Vector3(0f, 0f, 0f);
        private bool _waterLaunchPointCreated;
        private Transform _waterLaunchPoint;
        private Transform _waterTransform;
        private bool _waterWasInactiveLastFrame;
        // ─────────────────────────────────────────────────────────────────────────────

        private const float PositionToleranceSqr = 0.000001f;
        private const float ScaleToleranceSqr = 0.000001f;
        private const float EulerTolerance = 0.05f;

        public VRCameraReparentManager()
        {
            _states = new CameraReparentState[Items.Length];
            for (int i = 0; i < Items.Length; i++)
            {
                _states[i] = new CameraReparentState
                {
                    item = Items[i],
                    nextParentResolveTime = 0f,
                    nextTargetResolveTime = 0f,
                };
            }
        }

        public void Update()
        {
            float perfStart = Time.realtimeSinceStartup;
            float now = Time.unscaledTime;

            for (int i = 0; i < _states.Length; i++)
            {
                CameraReparentState state = _states[i];
                ProcessState(state, now);
            }

            TryCreateWaterLaunchPoint();
            TryReparentWaterOnInactiveEntry();

            float perfMs = (Time.realtimeSinceStartup - perfStart) * 1000f;
            if (perfMs > 1f)
                PlayMakerHelpers.DebugLog("[PERF] VRCameraReparentManager.Update: " + perfMs.ToString("F2") + "ms");
        }

        // ── Water launch point creation ───────────────────────────────────────────────
        // Runs every frame until it succeeds. It is safe to call repeatedly because it exits immediately
        // once created. SaunaThrow is _states[3].target after the reparent system
        // resolves it, so no separate find is needed.
        private void TryCreateWaterLaunchPoint()
        {
            if (_waterLaunchPointCreated)
                return;

            // _states[3] is the SaunaThrow entry. Reuse the reference already found by the reparent system.
            Transform saunaThrow = _states[3].target;
            if (saunaThrow == null)
                return;

            // Don't double-create across hot reloads
            if (saunaThrow.Find(WaterLaunchPointName) != null)
            {
                _waterLaunchPoint = saunaThrow.Find(WaterLaunchPointName);
                _waterLaunchPointCreated = true;
                return;
            }

            GameObject launchPoint = new GameObject(WaterLaunchPointName);
            launchPoint.transform.SetParent(saunaThrow, false);
            launchPoint.transform.localPosition = WaterLaunchLocalPosition;
            launchPoint.transform.localEulerAngles = WaterLaunchLocalEuler;
            launchPoint.transform.localScale = Vector3.one;

            _waterLaunchPoint = launchPoint.transform;
            _waterLaunchPointCreated = true;
        }

        private void TryReparentWaterOnInactiveEntry()
        {
            if (!_waterLaunchPointCreated)
                return;

            Transform saunaThrow = _states.Length > 3 ? _states[3].target : null;
            if (saunaThrow == null)
                return;

            if (_waterLaunchPoint == null)
            {
                _waterLaunchPoint = saunaThrow.Find(WaterLaunchPointName);
                if (_waterLaunchPoint == null)
                    return;
            }

            Transform waterTransform = _waterTransform;
            if (waterTransform == null)
            {
                // Resolve from the already-tracked SaunaThrow transform so reparented hierarchies still work.
                waterTransform = saunaThrow.Find("Water");
            }

            if (waterTransform == null)
                return;

            _waterTransform = waterTransform;

            bool isInactive = !waterTransform.gameObject.activeInHierarchy;
            if (!isInactive)
            {
                _waterWasInactiveLastFrame = false;
                return;
            }

            if (_waterWasInactiveLastFrame)
                return;

            if (waterTransform.parent != _waterLaunchPoint)
                waterTransform.SetParent(_waterLaunchPoint, false);

            _waterWasInactiveLastFrame = true;
            PlayMakerHelpers.DebugLog("[VR-LADLE] Reparented inactive Water -> MVRC_WaterLaunch");
        }

        private Transform ResolveTransform(string path, bool playerRelative)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (string.Compare(path, "PLAYER", System.StringComparison.Ordinal) == 0)
            {
                GameObject player = GameObject.Find("PLAYER");
                return player != null ? player.transform : null;
            }

            if (path.StartsWith("PLAYER/", System.StringComparison.Ordinal))
                return VRObjectResolver.FindPlayerChild(path.Substring(7));

            if (path.StartsWith("Pivot/", System.StringComparison.Ordinal))
                return VRObjectResolver.FindPlayerChild(path);

            if (playerRelative)
            {
                return VRObjectResolver.FindPlayerChild(path);
            }

            // For hierarchical paths not under PLAYER, find the root first, then navigate down
            string[] pathParts = path.Split('/');
            if (pathParts.Length > 1)
            {
                GameObject root = GameObject.Find(pathParts[0]);
                if (root != null)
                {
                    Transform current = root.transform;
                    for (int i = 1; i < pathParts.Length && current != null; i++)
                        current = current.Find(pathParts[i]);
                    if (current != null)
                        return current;
                }
            }

            // Fallback: try as direct scene path
            GameObject go = GameObject.Find(path);
            if (go == null && !path.Contains("/"))
            {
                // GameObject.Find silently misses inactive objects.
                // For simple names (no path separator) scan all transforms as fallback.
                // Restrict to root objects (parent == null) to avoid ambiguous name matches.
                Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].parent == null && all[i].name == path)
                        return all[i];
                }
            }
            return go != null ? go.transform : null;
        }
        // ─────────────────────────────────────────────────────────────────────────────

        public void Cleanup(bool restoreOriginalParent = true)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                CameraReparentState state = _states[i];

                if (restoreOriginalParent
                    && state != null
                    && state.originalCaptured
                    && state.target != null)
                {
                    state.target.SetParent(state.originalParent, false);
                    state.target.localPosition = state.originalLocalPosition;
                    state.target.localRotation = state.originalLocalRotation;
                    state.target.localScale = state.originalLocalScale;
                }

                if (state != null)
                {
                    state.target = null;
                    state.parent = null;
                    state.originalParent = null;
                    state.originalCaptured = false;
                    state.nextParentResolveTime = 0f;
                    state.nextTargetResolveTime = 0f;
                }
            }

            _waterLaunchPoint = null;
            _waterTransform = null;
            _waterWasInactiveLastFrame = false;
        }

        private void ProcessState(CameraReparentState state, float now)
        {
            if (state == null)
                return;

            if (!EnsureParentResolved(state, now))
                return;

            if (!EnsureTargetResolved(state, now))
                return;

            if (state.target == null || state.parent == null)
                return;

            EnsureParenting(state);
            EnsureLocalTransform(state);
        }

        private bool EnsureParentResolved(CameraReparentState state, float now)
        {
            if (state.parent != null)
                return true;

            if (now < state.nextParentResolveTime)
                return false;

            Transform parentTransform = ResolveTransform(state.item.parentPath, state.item.playerRelative);
            if (parentTransform == null)
            {
                state.nextParentResolveTime = now + state.item.resolveRetrySeconds;
                return false;
            }

            state.parent = parentTransform;
            state.nextParentResolveTime = 0f;
            return true;
        }

        private bool EnsureTargetResolved(CameraReparentState state, float now)
        {
            if (state.target != null)
                return true;

            if (now < state.nextTargetResolveTime)
                return false;

            Transform targetTransform = ResolveTransform(state.item.targetPath, state.item.playerRelative);
            if (targetTransform == null)
            {
                state.nextTargetResolveTime = now + state.item.resolveRetrySeconds;
                return false;
            }

            state.target = targetTransform;
            state.nextTargetResolveTime = 0f;
            return true;
        }

        private static bool EnsureInitialized(PlayMakerFSM fsm)
        {
            if (fsm == null || fsm.gameObject == null)
                return false;

            if (fsm.Fsm != null)
                return true;

            System.Collections.Generic.List<Transform> toRestore = new System.Collections.Generic.List<Transform>();
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

        private void EnsureParenting(CameraReparentState state)
        {
            if (state.target.parent == state.parent)
                return;

            if (!state.originalCaptured)
            {
                state.originalParent = state.target.parent;
                state.originalLocalPosition = state.target.localPosition;
                state.originalLocalRotation = state.target.localRotation;
                state.originalLocalScale = state.target.localScale;
                state.originalCaptured = true;
            }

            state.target.SetParent(state.parent, false);
        }

        private void EnsureLocalTransform(CameraReparentState state)
        {
            bool positionNeedsUpdate = (state.target.localPosition - state.item.localPosition).sqrMagnitude > PositionToleranceSqr;
            bool scaleNeedsUpdate = (state.target.localScale - state.item.localScale).sqrMagnitude > ScaleToleranceSqr;
            bool rotationNeedsUpdate = Quaternion.Angle(state.target.localRotation, Quaternion.Euler(state.item.localEuler)) > EulerTolerance;

            if (positionNeedsUpdate)
                state.target.localPosition = state.item.localPosition;

            if (rotationNeedsUpdate)
                state.target.localEulerAngles = state.item.localEuler;

            if (scaleNeedsUpdate)
                state.target.localScale = state.item.localScale;
        }
    }
}