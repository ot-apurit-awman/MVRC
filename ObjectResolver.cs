// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using UnityEngine;
using MSCLoader;

namespace MVRC
{
    public static class VRObjectResolver
    {
        public static Transform FindPlayerChild(string relativePath)
        {
            GameObject player = GameObject.Find("PLAYER");
            if (player == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(relativePath))
                return player.transform;

            Transform result = player.transform.Find(relativePath);

            return result;
        }

        public static Transform FindFromRoot(string rootName, string relativePath)
        {
            if (string.IsNullOrEmpty(rootName))
                return null;

            GameObject root = GameObject.Find(rootName);
            if (root == null)
                return null;

            if (string.IsNullOrEmpty(relativePath))
                return root.transform;

            return root.transform.Find(relativePath);
        }

        public static GameObject FindInactiveByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject candidate = all[i];
                if (candidate != null && candidate.name == name)
                    return candidate;
            }

            return null;
        }
    }
}