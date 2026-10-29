// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

using HutongGames.PlayMaker;
using MSCLoader;
using System.Reflection;
using UnityEngine;

namespace MVRC
{
    public static class PlayMakerHelpers
    {
        [System.Diagnostics.Conditional("MVRC_DEBUG")]
        public static void DebugLog(string msg)
        {
            ModConsole.Print(msg);
        }

        public static FsmState GetPlayMakerState(this GameObject obj, string stateName, string fsmName = null)
        {
            if (obj == null)
                return null;

            PlayMakerFSM pm = null;
            if (!string.IsNullOrEmpty(fsmName))
                pm = ActionHelpers.GetGameObjectFsm(obj, fsmName);

            if (pm == null)
                pm = obj.GetComponent<PlayMakerFSM>();

            return pm != null ? pm.GetState(stateName) : null;
        }

        public static bool ForceState(this PlayMakerFSM pm, string targetStateName)
        {
            if (pm == null || string.IsNullOrEmpty(targetStateName))
                return false;

            try
            {
                pm.SendEvent(targetStateName);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static class PhysicsCompat
    {
        private static bool _loggedOverlapSphereAllocFallback = false;

        private static readonly System.Type QueryTriggerInteractionType =
            typeof(Physics).Assembly.GetType("UnityEngine.QueryTriggerInteraction");

        private static readonly object QueryTriggerUseGlobal = GetQueryTriggerValue("UseGlobal");
        public static readonly object QueryTriggerCollide = GetQueryTriggerValue("Collide");
        private static readonly object QueryTriggerIgnore = GetQueryTriggerValue("Ignore");

        private static readonly MethodInfo OverlapSphereNonAllocWithMaskAndTrigger =
            QueryTriggerInteractionType != null
                ? typeof(Physics).GetMethod(
                    "OverlapSphereNonAlloc",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new System.Type[] { typeof(Vector3), typeof(float), typeof(Collider[]), typeof(int), QueryTriggerInteractionType },
                    null)
                : null;

        private static readonly MethodInfo OverlapSphereNonAllocWithMask =
            typeof(Physics).GetMethod(
                "OverlapSphereNonAlloc",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new System.Type[] { typeof(Vector3), typeof(float), typeof(Collider[]), typeof(int) },
                null);

        private static readonly MethodInfo OverlapSphereNonAllocNoMaskAndTrigger =
            QueryTriggerInteractionType != null
                ? typeof(Physics).GetMethod(
                    "OverlapSphereNonAlloc",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new System.Type[] { typeof(Vector3), typeof(float), typeof(Collider[]), QueryTriggerInteractionType },
                    null)
                : null;

        private static readonly MethodInfo OverlapSphereNonAllocNoMask =
            typeof(Physics).GetMethod(
                "OverlapSphereNonAlloc",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new System.Type[] { typeof(Vector3), typeof(float), typeof(Collider[]) },
                null);

        private static readonly MethodInfo OverlapSphereWithMaskAndTrigger =
            QueryTriggerInteractionType != null
                ? typeof(Physics).GetMethod(
                    "OverlapSphere",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new System.Type[] { typeof(Vector3), typeof(float), typeof(int), QueryTriggerInteractionType },
                    null)
                : null;

        private static readonly MethodInfo OverlapSphereNoMaskAndTrigger =
            QueryTriggerInteractionType != null
                ? typeof(Physics).GetMethod(
                    "OverlapSphere",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new System.Type[] { typeof(Vector3), typeof(float), QueryTriggerInteractionType },
                    null)
                : null;

        private static int InvokeNonAlloc(MethodInfo method, params object[] args)
        {
            if (method == null)
                return -1;

            object val = method.Invoke(null, args);
            return val is int ? (int)val : 0;
        }

        private static object GetQueryTriggerValue(string name)
        {
            if (QueryTriggerInteractionType == null)
                return null;

            try
            {
                return System.Enum.Parse(QueryTriggerInteractionType, name);
            }
            catch
            {
                return null;
            }
        }

        private static Collider[] InvokeOverlapSphereAlloc(
            Vector3 position,
            float radius,
            int layerMask,
            object queryTriggerInteraction,
            bool hasLayerMask)
        {
            MethodInfo withMask = OverlapSphereWithMaskAndTrigger;
            MethodInfo noMask = OverlapSphereNoMaskAndTrigger;

            if (hasLayerMask && withMask != null && queryTriggerInteraction != null)
                return withMask.Invoke(null, new object[] { position, radius, layerMask, queryTriggerInteraction }) as Collider[];

            if (!hasLayerMask && noMask != null && queryTriggerInteraction != null)
                return noMask.Invoke(null, new object[] { position, radius, queryTriggerInteraction }) as Collider[];

            if (hasLayerMask)
                return Physics.OverlapSphere(position, radius, layerMask);

            return Physics.OverlapSphere(position, radius);
        }

        public static int OverlapSphereNonAlloc(
            Vector3 position,
            float radius,
            Collider[] results,
            int layerMask,
            object queryTriggerInteraction)
        {
            if (results == null || results.Length == 0)
                return 0;

            if (queryTriggerInteraction != null)
            {
                int withTriggerCount = InvokeNonAlloc(
                    OverlapSphereNonAllocWithMaskAndTrigger,
                    position,
                    radius,
                    results,
                    layerMask,
                    queryTriggerInteraction);
                if (withTriggerCount >= 0)
                    return withTriggerCount;
            }

            int count = InvokeNonAlloc(OverlapSphereNonAllocWithMask, position, radius, results, layerMask);
            if (count >= 0)
                return count;

            if (queryTriggerInteraction != null)
            {
                int noMaskTriggerCount = InvokeNonAlloc(
                    OverlapSphereNonAllocNoMaskAndTrigger,
                    position,
                    radius,
                    results,
                    queryTriggerInteraction);
                if (noMaskTriggerCount >= 0)
                    return noMaskTriggerCount;
            }

            count = InvokeNonAlloc(OverlapSphereNonAllocNoMask, position, radius, results);
            if (count >= 0)
                return count;

            Collider[] hits = InvokeOverlapSphereAlloc(position, radius, layerMask, queryTriggerInteraction, true);
            if (!_loggedOverlapSphereAllocFallback)
            {
                _loggedOverlapSphereAllocFallback = true;
                PlayMakerHelpers.DebugLog("[PhysicsCompat] OverlapSphereNonAlloc fell back to an allocating path.");
            }
            return CopyResults(hits, results);
        }

        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results, int layerMask)
        {
            return OverlapSphereNonAlloc(position, radius, results, layerMask, QueryTriggerUseGlobal);
        }

        public static int OverlapSphereNonAllocIgnoreTriggers(Vector3 position, float radius, Collider[] results, int layerMask)
        {
            return OverlapSphereNonAlloc(position, radius, results, layerMask, QueryTriggerIgnore);
        }

        public static int OverlapSphereNonAlloc(
            Vector3 position,
            float radius,
            Collider[] results,
            object queryTriggerInteraction)
        {
            if (results == null || results.Length == 0)
                return 0;

            if (queryTriggerInteraction != null)
            {
                int withTriggerCount = InvokeNonAlloc(
                    OverlapSphereNonAllocNoMaskAndTrigger,
                    position,
                    radius,
                    results,
                    queryTriggerInteraction);
                if (withTriggerCount >= 0)
                    return withTriggerCount;
            }

            int count = InvokeNonAlloc(OverlapSphereNonAllocNoMask, position, radius, results);
            if (count >= 0)
                return count;

            Collider[] hits = InvokeOverlapSphereAlloc(position, radius, Physics.AllLayers, queryTriggerInteraction, false);
            return CopyResults(hits, results);
        }

        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results)
        {
            return OverlapSphereNonAlloc(position, radius, results, QueryTriggerUseGlobal);
        }

        public static int OverlapSphereNonAllocIgnoreTriggers(Vector3 position, float radius, Collider[] results)
        {
            return OverlapSphereNonAlloc(position, radius, results, QueryTriggerIgnore);
        }

        private static int CopyResults(Collider[] source, Collider[] destination)
        {
            if (source == null || source.Length == 0)
                return 0;

            int count = source.Length;
            if (count > destination.Length)
                count = destination.Length;

            for (int i = 0; i < count; i++)
                destination[i] = source[i];

            return count;
        }
    }
}
