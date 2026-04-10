# Android Port — Architecture & Decision Log

This document records the Android porting journey: what we tried, what failed, why we landed on Mono, and the path forward for NativeAOT.

## Current State (Phase 2 — April 2026)

Phase 2 validated: Full deferred rendering pipeline running on Android via Mono + Silk.NET Vulkan 1.1 on android-arm64. GBuffer → Lighting → Tonemap with render graph compiler and `DeviceCapabilities`-driven format selection.

### Architecture

```
RenderLabActivity (Activity + SurfaceView)
  |-> SurfaceHolder.Callback: surfaceChanged → ANativeWindow_fromSurface (JNI)
  |-> Background render thread
        |-> AndroidWindow (IPlatformWindow impl)
        |-> VulkanDevice.Create(vk, extensions, surface, apiVersion: Vk.Version11)
        |     '-> DeviceCapabilities (depth format, limits, feature flags)
        |-> Shader loading from Android assets (Assets.Open)
        |-> RenderGraphCompiler.Compile() → ResolvedPass[]
        |-> VulkanGraphExecutor.Execute() per frame:
              |-> GBuffer pass (3 MRT + depth, push constants, cube mesh)
              |-> Lighting pass (GBuffer sampling, fullscreen triangle)
              '-> Tonemap pass (HDR → swapchain)
```

Runtime: **Mono JIT** (via `net9.0-android` workload). Not NativeAOT.

### DeviceCapabilities

Queried once during `VulkanDevice.Create` and stored on `GpuState.Capabilities`. Centralizes all device property/feature queries so passes never call Vulkan directly for capability checks. Key fields for Android:

| Field | Typical Desktop | Typical Android | Used By |
|-------|----------------|-----------------|---------|
| `DepthFormat` | D32_SFLOAT | D24_UNORM_S8_UINT | GBuffer render pass, depth image |
| `MaxColorAttachments` | 8 | 4-8 | GBuffer MRT validation (needs 3) |
| `MaxSamplersPerStage` | 16 | 4-16 | Lighting pass (binds 3) |
| `SupportsGeometryShader` | true | false | Paper feature gating |
| `SupportsTessellation` | true | false | Paper feature gating |
| `TimestampSupported` | true | varies | GPU timing (disabled if false) |

---

## What We Tried

### Attempt 1: NativeAOT + NativeActivity (`net9.0` / `linux-bionic-arm64`)

**Approach:** Pure NativeAOT shared library. No Java, no Mono. C entry point via `[UnmanagedCallersOnly(EntryPoint = "ANativeActivity_onCreate")]`. The `.so` would be loaded directly by Android's `NativeActivity`.

```xml
<TargetFramework>net9.0</TargetFramework>
<RuntimeIdentifier>linux-bionic-arm64</RuntimeIdentifier>
<NativeLib>shared</NativeLib>
<PublishAot>true</PublishAot>
```

**Result:** Build failed immediately:
```
error: Cross-OS native compilation is not supported.
```

**Why:** The .NET 9 ILCompiler (`Microsoft.DotNet.ILCompiler` 9.0.13) cannot cross-compile from Windows to `linux-bionic-arm64`. NativeAOT cross-compilation only works same-OS (e.g., Windows→Windows-arm64, Linux→Linux-arm64). Building from a Linux host or inside WSL could theoretically work but was not attempted.

**Files created (later removed):**
- `NativeActivityBridge.cs` — P/Invoke for `ANativeWindow_getWidth/getHeight`, `ALooper_pollAll`, `ANativeActivityCallbacks` struct layout
- `AndroidMain.cs` — `ANativeActivity_onCreate` entry point with lifecycle callback wiring and clear-screen render loop

### Attempt 2: NativeAOT + `net9.0-android` TFM

**Approach:** Use the Android workload's build pipeline with `PublishAot=true`, hoping it would handle cross-compilation internally.

```xml
<TargetFramework>net9.0-android</TargetFramework>
<RuntimeIdentifier>android-arm64</RuntimeIdentifier>
<NativeLib>shared</NativeLib>
<PublishAot>true</PublishAot>
```

**Result:** The C# compilation succeeded but the NativeAOT linking step silently failed — no `.so` was produced. The build then errored trying to copy the non-existent file:
```
error MSB3030: Could not copy the file "bin\...\native\RenderLab.Platform.Android.so" 
because it was not found.
```

**Why:** The `net9.0-android` TFM activates the Xamarin/Android SDK build targets, which conflict with the standard ILCompiler NativeAOT pipeline. The Android workload's `PublishAot` goes through Mono AOT (ahead-of-time compilation for Mono), not NativeAOT (the ILCompiler). The `NativeLib=shared` flag is a NativeAOT-only concept — Mono AOT doesn't understand it. The two systems clashed silently.

### Attempt 3: Cross-compilation ILCompiler packages

**Approach:** Manually reference the bionic cross-compilation runtime pack to enable Windows→Android NativeAOT.

Tried:
```xml
<!-- Experimental LLVM packages — not on nuget.org -->
<PackageReference Include="Microsoft.DotNet.ILCompiler.LLVM" Version="9.0.0-*" />
<PackageReference Include="runtime.linux-bionic-arm64.Microsoft.DotNet.ILCompiler.LLVM" Version="9.0.0-*" />
```

Then:
```xml
<!-- Standard cross-compilation pack -->
<PackageReference Include="runtime.linux-bionic-arm64.Microsoft.DotNet.ILCompiler" Version="9.0.13" />
```

**Result:**
- LLVM packages: `NU1101: Unable to find package` — these are experimental packages from `dotnet/runtimelab` and not published to nuget.org.
- Standard bionic pack: `NU1102: Unable to find package with version >= 9.0.13` — only an 8.0 preview exists (`8.0.0-preview.6.23329.7`). Microsoft has not shipped cross-OS NativeAOT runtime packs for .NET 9.

### Attempt 4: Mono runtime via `net9.0-android` (current)

**Approach:** Standard .NET Android app using Mono JIT runtime. Activity + SurfaceView instead of NativeActivity. No NativeAOT.

```xml
<TargetFramework>net9.0-android</TargetFramework>
<OutputType>Exe</OutputType>
<!-- No NativeLib, no PublishAot -->
```

**Result:** Builds and runs. First attempt crashed with SIGABRT:
```
F monodroid: No assemblies found in '...__override__/arm64-v8a'.
F monodroid: ALL entries in APK named `lib/arm64-v8a/` MUST be STORED.
```

**Fix:** Disable assembly compression so Mono can memory-map them:
```xml
<AndroidEnableAssemblyCompression>false</AndroidEnableAssemblyCompression>
<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>
```

After fix: cornflower blue clear screen rendering successfully on device.

---

## Why Mono (For Now)

| Factor | NativeAOT | Mono |
|--------|-----------|------|
| Cross-OS compilation (Windows→Android) | Not supported in .NET 9 | N/A — Mono runs as JIT |
| Build from Linux host | Theoretically possible, untested | N/A |
| APK size (Debug) | Estimated ~5-10 MB | 72 MB (uncompressed assemblies) |
| Startup time | Fast (native code) | Slower (JIT warmup) |
| P/Invoke overhead | Minimal | Minimal (marshaling layer) |
| Silk.NET Vulkan compatibility | Unknown — untested path | Confirmed working |
| Android lifecycle integration | Manual (NativeActivity callbacks) | Automatic (Activity/SurfaceView) |
| Debugging | Difficult (native debugger only) | Standard .NET debugging via IDE |

Mono was chosen because it's the only path that works today from a Windows development machine. The goal of Phase 1 was to validate Silk.NET Vulkan on Android hardware — and Mono achieved that.

---

## Future: NativeAOT Path

The long-term goal is a NativeAOT `.so` with no managed runtime — a true native C-level Android app. This eliminates the 72 MB Mono overhead, removes JIT warmup, and aligns with the PRD's "NativeActivity + ANativeWindow + NativeAOT `.so`" vision.

### What needs to happen

1. **Build from Linux (or WSL).** The ILCompiler can only target the host OS family. Building from an `x86_64` Linux host with the `linux-bionic-arm64` runtime pack may work. This is the most likely short-term path.

2. **Wait for .NET 10+ cross-OS NativeAOT.** Microsoft may ship `runtime.linux-bionic-arm64.Microsoft.DotNet.ILCompiler` for .NET 10. Track: [dotnet/runtime#80901](https://github.com/dotnet/runtime/issues/80901).

3. **Use `dotnet/runtimelab` experimental builds.** The NativeAOT LLVM backend (`Microsoft.DotNet.ILCompiler.LLVM`) targets WebAssembly and bionic but is not production-ready. The packages exist in the `dotnet/runtimelab` CI feeds, not on nuget.org.

4. **Docker-based CI build.** Set up a Linux container with .NET 9 SDK + Android NDK that runs `dotnet publish -r linux-bionic-arm64 -p:PublishAot=true`. This avoids the cross-OS limitation entirely by building on Linux.

### Code already written for NativeAOT

The NativeActivity entry point and P/Invoke bridge code was written and then removed during Phase 1. It can be recovered from this document or rewritten. The key components:

- **`NativeActivityBridge.cs`**: P/Invoke bindings for `ANativeWindow_getWidth/getHeight`, `ALooper_pollAll`, `ANativeWindow_acquire/release`, `__android_log_write`. Also contains `ANativeActivity` and `ANativeActivityCallbacks` struct layouts matching the NDK headers.

- **`AndroidMain.cs`**: `[UnmanagedCallersOnly(EntryPoint = "ANativeActivity_onCreate")]` entry point. Registers lifecycle callbacks via function pointers into the `ANativeActivityCallbacks` struct. On `onNativeWindowCreated`, creates `AndroidWindow` + `VulkanDevice` and enters a render loop. On `onNativeWindowDestroyed`, tears down Vulkan.

- **`AndroidWindow.cs`**: Same `IPlatformWindow` implementation works for both Mono and NativeAOT — it only uses `ANativeWindow_fromSurface` (Mono path) or receives the `ANativeWindow*` directly (NativeAOT path).

### Architecture comparison

```
Current (Mono):                          Future (NativeAOT):
                                         
Android Runtime (ART)                    Android Loader
  |-> Mono JIT                             |-> libRenderLab.so (native)
    |-> RenderLabActivity (C#)               |-> ANativeActivity_onCreate
      |-> SurfaceView                          |-> ANativeWindow callbacks
        |-> ANativeWindow (JNI)                  |-> VulkanDevice.Create
          |-> VulkanDevice.Create                  |-> render loop
            |-> render loop
```

The NativeAOT path removes ART, Mono, JNI, and SurfaceView from the stack. The Vulkan rendering code (everything in `RenderLab.Gpu`, `RenderLab.Graph`, `RenderLab.Scene`) is identical in both paths — only the platform shell differs.

---

## Key Vulkan Differences: Desktop vs Android

| Aspect | Desktop | Android |
|--------|---------|---------|
| Vulkan API version | 1.3 | 1.1 (configurable via `vulkanApiVersion` param) |
| Surface extension | `VK_KHR_win32_surface` | `VK_KHR_android_surface` |
| CompositeAlpha | `OpaqueBitKhr` (guaranteed) | Capability-queried (may need `InheritBitKhr`) |
| Surface format | `B8G8R8A8_SRGB` preferred | `R8G8B8A8_SRGB` typical, fallback to first available |
| Present mode | `Mailbox` → `FIFO` fallback | `FIFO` guaranteed, `Mailbox` varies |
| Pre-transform | Identity (desktop never rotates) | May be rotated (handled by `currentTransform`) |
| Depth format | `D32_SFLOAT` | May need `D24_UNORM_S8_UINT` fallback |

Depth format is handled via `DeviceCapabilities.DepthFormat` (queried at device creation). Surface format, present mode, composite alpha, and pre-transform are handled in `VulkanSwapchain` (re-queried on each swapchain recreation since they are surface-specific, not device-specific).
