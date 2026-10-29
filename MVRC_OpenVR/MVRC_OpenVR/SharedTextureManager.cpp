// MVRC (My Winter Car VR)
// Copyright (C) 2026 ot-apurit-awman
// Licensed under the GNU GPL v3 or later. See LICENSE.

#include "SharedTextureManager.h"
#include <stdio.h>

SharedTextureManager::SharedTextureManager() {}

SharedTextureManager::~SharedTextureManager()
{
    FullDestroy();
}

void SharedTextureManager::InitSeparateDevice()
{
    if (separateDevice) return; // already initialized

    D3D_FEATURE_LEVEL featureLevel = D3D_FEATURE_LEVEL_11_0;
    HRESULT hr = D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        0,
        &featureLevel, 1,
        D3D11_SDK_VERSION,
        &separateDevice,
        nullptr,
        &separateContext);

    if (FAILED(hr))
    {
        char buf[128];
        sprintf_s(buf, "MVRC: D3D11CreateDevice failed hr=0x%08X\n", (unsigned)hr);
        OutputDebugStringA(buf);
        return;
    }

    char buf[128];
    sprintf_s(buf, "MVRC: Separate D3D11 device created OK, dev=0x%p\n", (void*)separateDevice);
    OutputDebugStringA(buf);
}

bool SharedTextureManager::SetDeviceFromTexture(void* nativeTex)
{
    if (!nativeTex)
    {
        OutputDebugStringA("MVRC: SetDeviceFromTexture: null ptr\n");
        return false;
    }

    ID3D11Resource* res = static_cast<ID3D11Resource*>(nativeTex);
    ID3D11Device* dev = nullptr;
    res->GetDevice(&dev);

    if (!dev)
    {
        OutputDebugStringA("MVRC: GetDevice returned NULL\n");
        return false;
    }

    char buf[128];
    sprintf_s(buf, "MVRC: GetDevice OK, dev=0x%p\n", (void*)dev);
    OutputDebugStringA(buf);

    if (unityContext) { unityContext->Release(); unityContext = nullptr; }

    // Store Unity's device. Do NOT AddRef: GetDevice added one reference, which is released below.
    // to balance. Unity holds its own strong ref, so the device remains valid for our lifetime.
    unityDevice = dev;
    unityDevice->GetImmediateContext(&unityContext);

    InitSeparateDevice();

    // Balance the reference added by GetDevice. Unity still owns the device.
    dev->Release();

    return true;
}

bool SharedTextureManager::CreateTextures(int width, int height)
{
    Destroy(); // releases textures, contexts; keeps devices alive

    if (!separateDevice)
    {
            OutputDebugStringA("MVRC: CreateTextures: separateDevice null\n");
        return false;
    }
    if (!unityDevice)
    {
            OutputDebugStringA("MVRC: CreateTextures: unityDevice null\n");
        return false;
    }

    // Destroy() released unityContext. Re-acquire it.
    unityDevice->GetImmediateContext(&unityContext);

    texWidth  = width;
    texHeight = height;

    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width              = (UINT)width;
    desc.Height             = (UINT)height;
    desc.MipLevels          = 1;
    desc.ArraySize          = 1;
    desc.Format             = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count   = 1;
    desc.SampleDesc.Quality = 0;
    desc.Usage              = D3D11_USAGE_DEFAULT;
    desc.BindFlags          = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    desc.MiscFlags          = D3D11_RESOURCE_MISC_SHARED; // legacy shared handle, cross-process
    desc.CPUAccessFlags     = 0;

    HRESULT hr = separateDevice->CreateTexture2D(&desc, nullptr, &leftSeparateTex);
    if (FAILED(hr))
    {
        char buf[128];
        sprintf_s(buf, "MVRC: CreateTexture2D left failed hr=0x%08X\n", (unsigned)hr);
        OutputDebugStringA(buf);
        lastCreateError = hr;
        return false;
    }

    hr = separateDevice->CreateTexture2D(&desc, nullptr, &rightSeparateTex);
    if (FAILED(hr))
    {
        char buf[128];
        sprintf_s(buf, "MVRC: CreateTexture2D right failed hr=0x%08X\n", (unsigned)hr);
        OutputDebugStringA(buf);
        lastCreateError = hr;
        return false;
    }

    // Get legacy shared handles via IDXGIResource::GetSharedHandle
    {
        IDXGIResource* dxgiRes = nullptr;
        if (SUCCEEDED(leftSeparateTex->QueryInterface(__uuidof(IDXGIResource), (void**)&dxgiRes)))
        {
            dxgiRes->GetSharedHandle(&leftSharedHandle);
            dxgiRes->Release();
        }
        if (!leftSharedHandle)
            OutputDebugStringA("MVRC: WARNING: leftSharedHandle is NULL\n");
    }
    {
        IDXGIResource* dxgiRes = nullptr;
        if (SUCCEEDED(rightSeparateTex->QueryInterface(__uuidof(IDXGIResource), (void**)&dxgiRes)))
        {
            dxgiRes->GetSharedHandle(&rightSharedHandle);
            dxgiRes->Release();
        }
        if (!rightSharedHandle)
            OutputDebugStringA("MVRC: WARNING: rightSharedHandle is NULL\n");
    }

    // Open left shared texture on unityDevice
    hr = unityDevice->OpenSharedResource(leftSharedHandle, __uuidof(ID3D11Texture2D), (void**)&leftUnityView);
    if (FAILED(hr))
    {
        char buf[128];
        sprintf_s(buf, "MVRC: OpenSharedResource left failed hr=0x%08X\n", (unsigned)hr);
        OutputDebugStringA(buf);
        return false;
    }

    // Open right shared texture on unityDevice
    hr = unityDevice->OpenSharedResource(rightSharedHandle, __uuidof(ID3D11Texture2D), (void**)&rightUnityView);
    if (FAILED(hr))
    {
        char buf[128];
        sprintf_s(buf, "MVRC: OpenSharedResource right failed hr=0x%08X\n", (unsigned)hr);
        OutputDebugStringA(buf);
        return false;
    }

    // Self-test: verify format of the opened view
    D3D11_TEXTURE2D_DESC leftViewDesc = {};
    leftUnityView->GetDesc(&leftViewDesc);
    {
        char buf[128];
        sprintf_s(buf, "MVRC: UnityView format=%d (expect 27=DXGI_FORMAT_R8G8B8A8_UNORM)\n", leftViewDesc.Format);
        OutputDebugStringA(buf);
    }

    {
        char buf[256];
        sprintf_s(buf,
            "MVRC: CreateTextures OK: leftSep=0x%p leftView=0x%p handle=0x%p\n",
            (void*)leftSeparateTex, (void*)leftUnityView, (void*)leftSharedHandle);
        OutputDebugStringA(buf);
    }

    return true;
}

void SharedTextureManager::CopyToShared(void* unityTexPtr, bool isLeft)
{
    if (!unityContext)
    {
        OutputDebugStringA("MVRC: CopyToShared: context null\n");
        return;
    }
    if (!unityTexPtr)
    {
        OutputDebugStringA("MVRC: CopyToShared: src null\n");
        return;
    }

    ID3D11Texture2D* src = static_cast<ID3D11Texture2D*>(unityTexPtr);
    ID3D11Texture2D* dst = isLeft ? leftUnityView : rightUnityView;

    if (!dst)
    {
        OutputDebugStringA("MVRC: CopyToShared: dst null\n");
        return;
    }

    // dst is leftUnityView (opened on unityDevice), and src is Unity's RT. Both use unityDevice.
    unityContext->CopyResource(dst, src);
}

void SharedTextureManager::FlushPendingCopies()
{
    if (!unityContext) return;
    // One flush per frame is enough after both eye copies have been queued.
    unityContext->Flush();
}

void SharedTextureManager::Destroy()
{
    if (leftUnityView)   { leftUnityView->Release();   leftUnityView   = nullptr; }
    if (rightUnityView)  { rightUnityView->Release();  rightUnityView  = nullptr; }
    if (leftSeparateTex) { leftSeparateTex->Release(); leftSeparateTex = nullptr; }
    if (rightSeparateTex){ rightSeparateTex->Release();rightSeparateTex= nullptr; }
    if (unityContext)    { unityContext->Release();     unityContext     = nullptr; }
    // Legacy GetSharedHandle handles are owned by the resource, not the application.
    // do NOT call CloseHandle() on them. Just null the pointers.
    leftSharedHandle  = nullptr;
    rightSharedHandle = nullptr;
    // Do NOT release unityDevice. Unity owns it.
    // Do NOT release separateDevice/separateContext. Keep them for reuse.
}

void SharedTextureManager::FullDestroy()
{
    Destroy();
    if (separateContext) { separateContext->Release(); separateContext = nullptr; }
    if (separateDevice)  { separateDevice->Release();  separateDevice  = nullptr; }
    // unityDevice is owned by Unity. Do not Release it.
    unityDevice = nullptr;
}

