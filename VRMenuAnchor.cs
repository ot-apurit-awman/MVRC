// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using MSCLoader;
using UnityEngine;

namespace MVRC
{
    public class VRMenuAnchor : MonoBehaviour
    {
        public float PreferredDistance = 2.0f;
        public float MinDistance = 1.2f;
        public float MaxDistance = 3.5f;
        public float HeightOffset = 0.0f;

        private bool _placed = false;
        private bool _anchorActive = false;

        public static VRMenuAnchor Instance { get; private set; }

        public static void PlaceMenu()
        {
            if (Instance != null)
                Instance.RequestPlace();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void RequestPlace()
        {
            _placed = false;
            _anchorActive = true;
        }

        public void ReleaseAnchor()
        {
            _anchorActive = false;
        }

        private void Update()
        {
            if (!_anchorActive) return;
            if (_placed) return;

            DoPlace();
            _placed = true;
        }

        private void DoPlace()
        {
            Transform cam = VRCompositorManager.VRHeadTransform;
            if (cam == null && Camera.main != null)
                cam = Camera.main.transform;

            if (cam == null)
            {
                PlayMakerHelpers.DebugLog("[MENU] WARNING: No head/camera transform for menu placement.");
                return;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.01f)
                flatForward = Vector3.ProjectOnPlane(cam.forward, cam.right);
            if (flatForward.sqrMagnitude < 0.01f)
                flatForward = cam.forward;
            flatForward.Normalize();

            float placeDist = PreferredDistance;
            RaycastHit hit;
            if (Physics.Raycast(cam.position, flatForward, out hit, PreferredDistance + 0.5f))
                placeDist = Mathf.Clamp(hit.distance - 0.15f, MinDistance, MaxDistance);

            Vector3 targetPos = cam.position + flatForward * placeDist;
            targetPos.y = cam.position.y + HeightOffset;

            Vector3 lookDir = cam.position - targetPos;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude < 0.0001f)
                lookDir = -flatForward;
            lookDir.Normalize();
            Quaternion targetRot = Quaternion.LookRotation(-lookDir, Vector3.up);

            transform.position = targetPos;
            transform.rotation = targetRot;

            PlayMakerHelpers.DebugLog("[MENU] Anchor placed at distance=" + placeDist.ToString("F2") + " pos=" + targetPos);
        }
    }
}
