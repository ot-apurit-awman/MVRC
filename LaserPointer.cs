// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using System;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;

namespace MVRC
{
    public class VRLaserPointer : MonoBehaviour
    {
        private static VRLaserPointer _instance;
        public static VRLaserPointer RightInstance;
        public static VRLaserPointer LeftInstance;
        public VRHand.HandSide Hand;
        private static readonly HashSet<GameObject> RegisteredTargets = new HashSet<GameObject>();

        public static VRLaserPointer Instance
        {
            get { return _instance; }
            private set { _instance = value; }
        }

        public static void RegisterTarget(GameObject target)
        {
            if (target == null) return;
            RegisteredTargets.Add(target);
        }

        public static void ResetRegisteredTargets()
        {
            RegisteredTargets.Clear();
        }

        public float MaxDistance = 10f;
        public LayerMask RaycastLayers = Physics.DefaultRaycastLayers;
        public Color ActiveColor = Color.cyan;
        public Color PassiveColor = Color.white;

        public GameObject CurrentTarget { get; private set; }
        public bool IsPointing { get; private set; }
        public bool LaserEnabled
        {
            get { return _laserEnabled; }
            set { SetLaserEnabled(value); }
        }

        public LineRenderer LaserLineRenderer { get; private set; }

        private bool _laserEnabled = true;
        private Transform _handTransform;

        private void Awake()
        {
            if (_instance == null)
                _instance = this;

            _handTransform = transform;
            EnsureLineRenderer();
        }

        private void OnEnable()
        {
            if (_instance == null)
                _instance = this;
            EnsureLineRenderer();
        }

        private void OnDestroy()
        {
            if (RightInstance == this) RightInstance = null;  
            if (LeftInstance == this) LeftInstance = null;    
            if (_instance == this)
                _instance = null;
        }

        private void EnsureLineRenderer()
        {
            if (LaserLineRenderer != null)
                return;

            GameObject lineGO = new GameObject("MVRC_LaserLine");
            lineGO.transform.SetParent(transform, false);
            lineGO.transform.localPosition = Vector3.zero;
            lineGO.transform.localRotation = Quaternion.identity;

            LaserLineRenderer = lineGO.AddComponent<LineRenderer>();
            LaserLineRenderer.useWorldSpace = true;
            LaserLineRenderer.SetVertexCount(2);
            LaserLineRenderer.SetWidth(0.01f, 0.01f);
            LaserLineRenderer.SetColors(PassiveColor, PassiveColor);
            LaserLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            LaserLineRenderer.receiveShadows = false;
            LaserLineRenderer.material = CreateLineMaterial();
            LaserLineRenderer.enabled = false;
        }

        private Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Diffuse");

            Material material = new Material(shader);
            material.color = PassiveColor;
            return material;
        }

        private void SetLaserEnabled(bool enabled)
        {
            _laserEnabled = enabled;
            if (!enabled)
            {
                CurrentTarget = null;
                IsPointing = false;
                if (LaserLineRenderer != null)
                    LaserLineRenderer.enabled = false;
            }
        }

        private static bool IsRegisteredTarget(GameObject go)
        {
            return go != null && RegisteredTargets.Contains(go);
        }

        private static GameObject ResolveRegisteredTarget(GameObject hitObject)
        {
            if (hitObject == null)
                return null;

            Transform t = hitObject.transform;
            while (t != null)
            {
                if (RegisteredTargets.Contains(t.gameObject))
                    return t.gameObject;
                t = t.parent;
            }

            return hitObject;
        }

        private void Update()
        {
            if (!_laserEnabled)
                return;

            if (_handTransform == null)
                _handTransform = transform;

            EnsureLineRenderer();

            Vector3 origin = _handTransform.position;
            Vector3 direction = _handTransform.forward;

            if (DebugVrControllerEmulation.IsActive && Camera.main != null)
                direction = Camera.main.transform.forward;

            RaycastHit[] hits = Physics.RaycastAll(origin, direction, MaxDistance, RaycastLayers);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            Vector3 endPoint = origin + direction * MaxDistance;
            GameObject hitObject = null;
            CurrentTarget = null;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                GameObject candidateObject = collider != null ? collider.gameObject : null;
                GameObject resolvedTarget = candidateObject != null ? ResolveRegisteredTarget(candidateObject) : null;

                if (resolvedTarget == null || !IsRegisteredTarget(resolvedTarget))
                    continue;

                hitObject = candidateObject;
                CurrentTarget = resolvedTarget;
                endPoint = hits[i].point;
                break;
            }

            IsPointing = CurrentTarget != null && IsRegisteredTarget(CurrentTarget);

            if (LaserLineRenderer != null)
            {
                LaserLineRenderer.enabled = true;
                LaserLineRenderer.SetPosition(0, origin);
                LaserLineRenderer.SetPosition(1, endPoint);
                LaserLineRenderer.SetColors(IsPointing ? ActiveColor : PassiveColor, IsPointing ? ActiveColor : PassiveColor);
                if (LaserLineRenderer.material != null)
                    LaserLineRenderer.material.color = IsPointing ? ActiveColor : PassiveColor;
            }
        }
    }
    public class VRLaserPointerPressAction : VRInjectedAction
    {
        public FsmEvent eventToFire;

        private bool _hasFired;

        public override void OnEnter()
        {
            _hasFired = false;
        }

        public override void OnUpdate()
        {
            if (_hasFired) return;

            GameObject owner = targetFsm != null ? targetFsm.gameObject : null;
            if (owner == null) return;

            VRLaserPointer right = VRLaserPointer.RightInstance;
            bool rightClicked = right != null && right.enabled && right.LaserEnabled
                && right.IsPointing && right.CurrentTarget == owner
                && VRFsmActionBridge.RightClickJustPressed_R;

            VRLaserPointer left = VRLaserPointer.LeftInstance;
            bool leftClicked = left != null && left.enabled && left.LaserEnabled
                && left.IsPointing && left.CurrentTarget == owner
                && VRFsmActionBridge.RightClickJustPressed_L;

            if (!rightClicked && !leftClicked) return;

            if (Fsm != null && eventToFire != null)
                Fsm.ProcessEvent(eventToFire);

            _hasFired = true;
        }

        public override void OnExit()
        {
            _hasFired = false;
        }
    }

    public class VRLaserPointerHoverAction : VRInjectedAction
    {
        public FsmEvent eventToFire;

        private bool _hasFired;

        public override void OnEnter()
        {
            _hasFired = false;
        }

        public override void OnUpdate()
        {
            if (_hasFired) return;

            GameObject owner = targetFsm != null ? targetFsm.gameObject : null;
            if (owner == null) return;

            VRLaserPointer right = VRLaserPointer.RightInstance;
            bool rightHit = right != null && right.LaserEnabled && right.IsPointing && right.CurrentTarget == owner;

            VRLaserPointer left = VRLaserPointer.LeftInstance;
            bool leftHit = left != null && left.LaserEnabled && left.IsPointing && left.CurrentTarget == owner;

            if (!rightHit && !leftHit) return;

            if (Fsm != null && eventToFire != null)
                Fsm.ProcessEvent(eventToFire);

            _hasFired = true;
        }

        public override void OnExit()
        {
            _hasFired = false;
        }
    }
}