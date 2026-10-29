// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <stdio.h>
#include "SharedTextureManager.h"

static SharedTextureManager s_Manager;

// -----------------------------------------------------------------------
// Unity 5.0 legacy device callback
// Unity calls this automatically. No headers are needed because the export is found by name.
// deviceType 2 = D3D11, eventType 0 = Initialize, 1 = Shutdown
// -----------------------------------------------------------------------
extern "C" __declspec(dllexport) void __cdecl
UnitySetGraphicsDevice(void* device, int deviceType, int eventType)
{
    if (deviceType == 2) // D3D11
    {
        if (eventType == 1)
            s_Manager.Destroy();
    }
}

// -----------------------------------------------------------------------
// C# callable exports
// -----------------------------------------------------------------------
extern "C" __declspec(dllexport) int __cdecl
MVRC_CreateEyeTextures(int width, int height)
{
    char buf[128];
    sprintf_s(buf, "MVRC: CreateEyeTextures called w=%d h=%d\n", width, height);
    OutputDebugStringA(buf);
    return s_Manager.CreateTextures(width, height) ? 1 : 0;
}
extern "C" __declspec(dllexport) int __cdecl
MVRC_GetDiagnostics()
{
    // Returns a bitmask:
    // bit 0 = device is non-null
    // bit 1 = context is non-null  
    // bit 2 = leftSharedTex is non-null
    // bit 3 = rightSharedTex is non-null
    int flags = 0;
    if (s_Manager.HasDevice())   flags |= 1;
    if (s_Manager.HasContext())  flags |= 2;
    if (s_Manager.GetLeftTexture()  != nullptr) flags |= 4;
    if (s_Manager.GetRightTexture() != nullptr) flags |= 8;
    return flags;
}

static void* s_PendingTex = nullptr;
static void* s_PendingLeftCopyTex  = nullptr;
static void* s_PendingRightCopyTex = nullptr;

extern "C" __declspec(dllexport) void __cdecl
MVRC_SetPendingTexture(void* tex)
{
    s_PendingTex = tex;
}

// Called by Unity on the render thread via GL.IssuePluginEvent
extern "C" __declspec(dllexport) void __cdecl
UnityRenderEvent(int eventID)
{
    if (eventID == 1 && s_PendingTex != nullptr)
    {
        s_Manager.SetDeviceFromTexture(s_PendingTex);
        s_PendingTex = nullptr;
    }
    else if (eventID == 2)
    {
        // Copy both eye textures on the render thread (safe D3D11 context access)
        bool copiedAny = false;
        if (s_PendingLeftCopyTex != nullptr)
        {
            s_Manager.CopyToShared(s_PendingLeftCopyTex, true);
            s_PendingLeftCopyTex = nullptr;
            copiedAny = true;
        }
        if (s_PendingRightCopyTex != nullptr)
        {
            s_Manager.CopyToShared(s_PendingRightCopyTex, false);
            s_PendingRightCopyTex = nullptr;
            copiedAny = true;
        }

        if (copiedAny)
        {
            s_Manager.FlushPendingCopies();
        }
    }
}

// Returns the UnityRenderEvent function pointer for use with GL.IssuePluginEvent
extern "C" __declspec(dllexport) void* __cdecl
MVRC_GetRenderEventFunc()
{
    return (void*)UnityRenderEvent;
}

extern "C" __declspec(dllexport) void __cdecl
MVRC_SetPendingCopyLeft(void* tex)
{
    s_PendingLeftCopyTex = tex;
}

extern "C" __declspec(dllexport) void __cdecl
MVRC_SetPendingCopyRight(void* tex)
{
    s_PendingRightCopyTex = tex;
}


extern "C" __declspec(dllexport) int __cdecl
MVRC_SetDeviceFromTexture(void* texPtr)
{
    return s_Manager.SetDeviceFromTexture(texPtr) ? 1 : 0;
}

extern "C" __declspec(dllexport) void __cdecl
MVRC_CopyToShared(void* unityTexPtr, int isLeft)
{
    s_Manager.CopyToShared(unityTexPtr, isLeft != 0);
    s_Manager.FlushPendingCopies();
}

extern "C" __declspec(dllexport) int __cdecl
MVRC_GetLastCreateError()
{
    return (int)s_Manager.GetLastCreateError();
}

extern "C" __declspec(dllexport) void* __cdecl
MVRC_GetLeftTexturePtr()
{
    return s_Manager.GetLeftTexture();
}

extern "C" __declspec(dllexport) void* __cdecl
MVRC_GetRightTexturePtr()
{
    return s_Manager.GetRightTexture();
}

extern "C" __declspec(dllexport) void* __cdecl
MVRC_GetLeftSharedHandle()
{
    return s_Manager.GetLeftSharedHandle();
}

extern "C" __declspec(dllexport) void* __cdecl
MVRC_GetRightSharedHandle()
{
    return s_Manager.GetRightSharedHandle();
}

extern "C" __declspec(dllexport) int __cdecl
MVRC_IsReady()
{
    return s_Manager.IsReady() ? 1 : 0;
}

extern "C" __declspec(dllexport) void __cdecl
MVRC_Destroy()
{
    s_Manager.Destroy();
}

// -----------------------------------------------------------------------
// DllMain
// -----------------------------------------------------------------------
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason, LPVOID lpReserved)
{
    return TRUE;
}

