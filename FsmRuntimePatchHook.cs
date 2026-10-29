// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using Harmony;
using HutongGames.PlayMaker;

namespace MVRC
{
    /// <summary>
    /// Hooks PlayMakerFSM lifecycle callbacks so FSMs spawned after startup can
    /// be patched immediately without polling.
    /// </summary>
    public static class VRFsmRuntimePatchHook
    {
        private const string HarmonyId = "msc_vr.runtimefsmhook";

        private static HarmonyInstance _harmony = null;
        private static bool _installed = false;

        public static void Install()
        {
            if (_installed) return;

            _harmony = HarmonyInstance.Create(HarmonyId);

            int patchCount = 0;
            PatchLifecycle("OnEnable", "PlayMakerFsmOnEnablePostfix", ref patchCount);
            PatchLifecycle("Awake", "PlayMakerFsmAwakePostfix", ref patchCount);

            _installed = patchCount > 0;
            if (_installed)
            {
                PlayMakerHelpers.DebugLog("[VRFsmRuntimePatchHook] Installed runtime lifecycle hooks.");
            }
            else
            {
                PlayMakerHelpers.DebugLog("[VRFsmRuntimePatchHook] No lifecycle methods patched.");
            }
        }

        public static void Uninstall()
        {
            if (!_installed || _harmony == null) return;

            try
            {
                _harmony.UnpatchAll();
            }
            catch
            {
            }

            _harmony = null;
            _installed = false;
            PlayMakerHelpers.DebugLog("[VRFsmRuntimePatchHook] Uninstalled runtime lifecycle hooks.");
        }

        private static void PatchLifecycle(string originalMethodName, string postfixMethodName, ref int patchCount)
        {
            var original = AccessTools.Method(typeof(PlayMakerFSM), originalMethodName);
            if (original == null)
            {
                PlayMakerHelpers.DebugLog("[VRFsmRuntimePatchHook] Missing PlayMakerFSM." + originalMethodName + "() in this build.");
                return;
            }

            var postfix = new HarmonyMethod(typeof(VRFsmRuntimePatchHook), postfixMethodName);
            _harmony.Patch(original, postfix: postfix);
            patchCount++;
        }

        private static void PlayMakerFsmOnEnablePostfix(PlayMakerFSM __instance)
        {
            VRFsmPatcher.TryPatchRuntimeFsm(__instance);
        }

        private static void PlayMakerFsmAwakePostfix(PlayMakerFSM __instance)
        {
            VRFsmPatcher.TryPatchRuntimeFsm(__instance);
        }
    }
}
