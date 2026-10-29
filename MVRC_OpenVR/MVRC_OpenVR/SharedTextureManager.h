// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

#pragma once
#include <d3d11.h>
#include <dxgi.h>
#include <windows.h>

class SharedTextureManager
{
public:
    SharedTextureManager();
    ~SharedTextureManager();

    // Called with Unity's native texture pointer (from RenderTexture.GetNativeTexturePtr())
    // Extracts and stores Unity's ID3D11Device* and ID3D11DeviceContext*
    // Also initializes the separate D3D11 device if not already done
    // Returns true on success
    bool SetDeviceFromTexture(void* nativeTex);

    // Returns true if unityDevice is non-null
    bool HasDevice() const { return unityDevice != nullptr; }

    // Returns true if unityContext is non-null
    bool HasContext() const { return unityContext != nullptr; }

    // Creates left and right shared textures
    // Returns true if both textures were created successfully
    bool CreateTextures(int width, int height);

    // Returns true if both separate textures and both unity views are non-null
    bool IsReady() const
    {
        return leftSeparateTex != nullptr && rightSeparateTex != nullptr
            && leftUnityView   != nullptr && rightUnityView   != nullptr;
    }

    // Copy from a Unity RenderTexture (identified by its native pointer) into the shared texture
    // Uses unityContext->CopyResource(unityView, src)
    // isLeft=true for left eye, false for right eye
    void CopyToShared(void* unityTexPtr, bool isLeft);

    // Flush pending copy commands once after all per-eye CopyResource calls for a frame.
    void FlushPendingCopies();

    // Returns the ID3D11Texture2D* on separateDevice. This is what gets submitted to OpenVR.
    void* GetLeftTexture()  const { return (void*)leftSeparateTex;  }
    void* GetRightTexture() const { return (void*)rightSeparateTex; }

    // Returns the DXGI shared HANDLE for the left/right texture (from GetSharedHandle)
    void* GetLeftSharedHandle()  const { return (void*)leftSharedHandle;  }
    void* GetRightSharedHandle() const { return (void*)rightSharedHandle; }

    // Returns the last HRESULT from CreateTexture2D
    HRESULT GetLastCreateError() const { return lastCreateError; }

    // Releases all textures and contexts, but does NOT release unityDevice (Unity owns it)
    // Does NOT release separateDevice. Keep it alive for reuse.
    void Destroy();

    // Full teardown including separateDevice
    void FullDestroy();

private:
    // Unity's device, extracted from Unity's RenderTexture via GetDevice().
    // Used ONLY for CopyResource (writing into the Unity-side shared views)
    ID3D11Device*        unityDevice   = nullptr;
    ID3D11DeviceContext* unityContext  = nullptr;

    // Our own separate D3D11 device. Textures created here are submitted to the compositor.
    // The compositor can import these cross-process via OpenSharedResource
    ID3D11Device*        separateDevice  = nullptr;
    ID3D11DeviceContext* separateContext = nullptr;

    // Textures on separateDevice. Their NativePointer is submitted to IVRCompositor::Submit.
    ID3D11Texture2D* leftSeparateTex  = nullptr;
    ID3D11Texture2D* rightSeparateTex = nullptr;

    // The same textures opened on unityDevice via OpenSharedResource
    // CopyResource writes from Unity RT into these
    ID3D11Texture2D* leftUnityView  = nullptr;
    ID3D11Texture2D* rightUnityView = nullptr;

    // DXGI shared handles (legacy HANDLE from GetSharedHandle)
    HANDLE leftSharedHandle  = nullptr;
    HANDLE rightSharedHandle = nullptr;

    // Texture dimensions
    int texWidth  = 0;
    int texHeight = 0;

    // Last HRESULT from CreateTexture2D for diagnostics
    HRESULT lastCreateError = S_OK;

    // Initializes separateDevice if not already done
    void InitSeparateDevice();
};
