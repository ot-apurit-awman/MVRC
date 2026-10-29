// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace MVRC
{
    public static class VRMenuManager
    {
        private static PlayMakerFSM _openMenuFsm;
        private static PlayMakerFSM _resumeButtonFsm;   
        private static bool _warnedMissingFsm = false;

        public static bool IsOpen { get; private set; }

        public static void Initialize()
        {
            if (_openMenuFsm != null)
            {
                if (_openMenuFsm.gameObject != null)
                    return;
                _openMenuFsm = null;
                IsOpen = false;
                VRFsmActionBridge.MenuIsOpen = false;
            }

            GameObject optionsDb = GameObject.Find("Systems/OptionsDB");
            if (optionsDb == null)
            {
                WarnMissingFsm();
                return;
            }

            PlayMakerFSM[] fsms = optionsDb.GetComponents<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
            {
                PlayMakerFSM fsm = fsms[i];
                if (fsm != null && fsm.FsmName == "Open Menu")
                {
                    _openMenuFsm = fsm;
                    break;
                }
            }

            if (_openMenuFsm == null)
            {
                WarnMissingFsm();
                return;
            }

            _warnedMissingFsm = false;

            EnsureMenuAnchor(optionsDb);
        }
        private static PlayMakerFSM FindResumeFsm()
        {
            GameObject player = GameObject.Find("PLAYER");
            if (player == null) return null;

            Transform t = player.transform.Find("OptionsMenu/Menu/Btn_Resume/Button");
            if (t == null)
            {
                PlayMakerHelpers.DebugLog("[VRMenu] WARNING: Btn_Resume/Button not found");
                return null;
            }

            PlayMakerFSM[] fsms = t.GetComponents<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
                if (fsms[i] != null && fsms[i].FsmName == "Button")
                    return fsms[i];

            return null;
        }
        public static void ToggleMenu()
        {
            PlayMakerHelpers.DebugLog("[VRMenu] ToggleMenu called, IsOpen=" + IsOpen);
            if (IsOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        public static void OpenMenu()
        {
            if (IsOpen)
                return;

            Initialize();
            if (_openMenuFsm == null)
                return;

            VRMenuAnchor.PlaceMenu();
            SendMenuEvent();

            IsOpen = true;
            VRFsmActionBridge.MenuIsOpen = true;
        }

        public static void CloseMenu()
        {
            if (!IsOpen)
                return;

            if (_resumeButtonFsm == null || _resumeButtonFsm.gameObject == null)
                _resumeButtonFsm = FindResumeFsm();

            if (_resumeButtonFsm != null && _resumeButtonFsm.Fsm != null)
                _resumeButtonFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("CLICK"));
            else
                PlayMakerHelpers.DebugLog("[VRMenu] WARNING: Could not send CLICK to resume button");

            if (VRMenuAnchor.Instance != null)
                VRMenuAnchor.Instance.ReleaseAnchor();

            IsOpen = false;
            VRFsmActionBridge.MenuIsOpen = false;
        }

        private static void SendMenuEvent()
        {
            if (_openMenuFsm == null || _openMenuFsm.Fsm == null)
            {
                WarnMissingFsm();
                return;
            }

            _openMenuFsm.Fsm.ProcessEvent(FsmEvent.GetFsmEvent("MENU"));
        }

        private static void EnsureMenuAnchor(GameObject optionsDb)
        {
            GameObject anchorRoot = FindMenuVisualRoot(optionsDb);
            if (anchorRoot == null)
                anchorRoot = optionsDb;

            if (anchorRoot.GetComponent<VRMenuAnchor>() == null)
                anchorRoot.AddComponent<VRMenuAnchor>();
        }

        private static GameObject FindMenuVisualRoot(GameObject optionsDb)
        {
            if (optionsDb == null)
                return null;

            Transform optionsTransform = optionsDb.transform;
            Transform firstRendererChild = null;

            for (int i = 0; i < optionsTransform.childCount; i++)
            {
                Transform child = optionsTransform.GetChild(i);
                if (child == null)
                    continue;

                string lowerName = child.name != null ? child.name.ToLowerInvariant() : string.Empty;
                if (lowerName.Contains("menu"))
                    return child.gameObject;

                if (child.GetComponent<Canvas>() != null)
                    return child.gameObject;

                if (firstRendererChild == null && child.GetComponentInChildren<Renderer>() != null)
                    firstRendererChild = child;
            }

            if (firstRendererChild != null)
                return firstRendererChild.gameObject;

            return optionsDb;
        }

        private static void WarnMissingFsm()
        {
            if (_warnedMissingFsm)
                return;

            _warnedMissingFsm = true;
        }
        public static void SyncFromGame()
        {
            FsmBool playerInMenu = FsmVariables.GlobalVariables.GetFsmBool("PlayerInMenu");
            if (playerInMenu == null) return;

            bool gameMenuOpen = playerInMenu.Value;
            if (gameMenuOpen == IsOpen) return;

            // Only sync when the game closed the menu behind us (IsOpen=true, FSM=false).
            // Never write IsOpen=true here. That fights CloseMenu() while the FSM
            // is still catching up to the MENU event we just sent (takes ≥1 frame).
            if (IsOpen && !gameMenuOpen)
            {
                PlayMakerHelpers.DebugLog("[VRMenu] PlayerInMenu desync; game closed externally, syncing to: false");
                IsOpen = false;
                VRFsmActionBridge.MenuIsOpen = false;
                if (VRMenuAnchor.Instance != null)
                    VRMenuAnchor.Instance.ReleaseAnchor();
            }
            // !IsOpen && gameMenuOpen: the FSM is still catching up to our close. Ignore it.
        }
    }
}
