// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using UnityEngine;
using MSCLoader;

namespace MVRC
{
    public class HMDLook : MonoBehaviour
    {
        public enum Axis { Yaw, Pitch, Roll, Position }
        public Axis axis;

        private MouseLook mouseLook;

        // HMD rotation from LookManager
        public static float CurrentYaw   = 0f;
        public static float CurrentPitch = 0f;
        public static float CurrentRoll  = 0f;
        public static bool  PoseValid    = false;

        // Default pitch limits used when MouseLook component is unavailable
        private const float DefaultMinPitch = -80f;
        private const float DefaultMaxPitch =  80f;

        public static Vector3 CurrentPosition = Vector3.zero;

        void Start()
        {
            mouseLook = GetComponent<MouseLook>();
            if (mouseLook == null)
            {
                // Only pitch strictly depends on MouseLook limits; yaw/roll can run without it.
                if (axis == Axis.Pitch)
                    ModConsole.Error("HMDLook (" + axis + "): No MouseLook component found on " + gameObject.name);
                else
                    PlayMakerHelpers.DebugLog("HMDLook (" + axis + "): No MouseLook component found on " + gameObject.name);
            }
        }

        void Update()
        {
            // Mouse input is intentionally ignored; all camera rotation comes from
            // the HMD pose via LookManager.  Controller thumbstick turning is handled
            // in LookManager.Apply() by adjusting yawOffset directly.
            if      (axis == Axis.Yaw)   ApplyYaw();
            else if (axis == Axis.Pitch) ApplyPitch();
            else if (axis == Axis.Roll)  ApplyRoll();
        }

        private void ApplyYaw()
        {
            float yaw = PoseValid ? CurrentYaw : 0f;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private void ApplyPitch()
        {
            float pitch = 0f;
            if (PoseValid && !float.IsNaN(CurrentPitch) && !float.IsInfinity(CurrentPitch))
                pitch = CurrentPitch;

            // Still clamp to the game's pitch limits so the camera can't flip over
            float minP = (mouseLook != null) ? mouseLook.minimumY : DefaultMinPitch;
            float maxP = (mouseLook != null) ? mouseLook.maximumY : DefaultMaxPitch;
            pitch = Mathf.Clamp(pitch, minP, maxP);

            transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ApplyRoll()
        {
            float roll = 0f;
            if (PoseValid && !float.IsNaN(CurrentRoll) && !float.IsInfinity(CurrentRoll))
                roll = CurrentRoll;
            transform.localRotation = Quaternion.Euler(0f, 0f, roll);
        }

        /// <summary>
        /// Re-applies the current HMD rotation without reading new input.
        /// Called from LookManager.ApplyNow() immediately before Camera.Render()
        /// to eliminate the 1-frame tracking delay.
        /// </summary>
        public void ApplyCurrentRotation()
        {
            if      (axis == Axis.Yaw)   ApplyYaw();
            else if (axis == Axis.Pitch) ApplyPitch();
            else if (axis == Axis.Roll)  ApplyRoll();
        }

        // RecenterView no longer needs to reset mouse accumulators; kept as a no-op
        // so call sites in LookManager don't need to change.
        public static void RecenterView()
        {
            ModConsole.Print("View recentered.");
        }
    }
}
