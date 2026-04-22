using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RenderLab.Gpu;

public static class VulkanDevice
{
    private static readonly string[] ValidationLayers = ["VK_LAYER_KHRONOS_validation"];
    private static readonly string[] DeviceExtensions = [KhrSwapchain.ExtensionName];

#if DEBUG
    private const bool EnableValidation = true;
#else
    private const bool EnableValidation = false;
#endif

    /// <summary>
    /// Creates the full Vulkan device stack: instance, surface, physical/logical device,
    /// queues, swapchain, command pool, command buffers, and synchronization primitives.
    /// </summary>
    /// <param name="vk">Silk.NET Vulkan API entry point.</param>
    /// <param name="requiredExtensions">Instance extensions required by the windowing system
    /// (obtained from <c>DesktopWindow.GetRequiredVulkanExtensions()</c>).</param>
    /// <param name="createSurface">Callback that creates a <see cref="SurfaceKHR"/> from the Vulkan instance.
    /// Called exactly once during initialization.</param>
    /// <param name="vulkanApiVersion">Vulkan API version to request. Defaults to Vulkan 1.3 when 0.</param>
    public static unsafe GpuState Create(Vk vk, string[] requiredExtensions, Func<Instance, SurfaceKHR> createSurface,
        uint vulkanApiVersion = 0, uint initialWidth = 0, uint initialHeight = 0)
    {
        var instance = CreateInstance(vk, requiredExtensions, vulkanApiVersion);
        var surface = createSurface(instance);

        if (!vk.TryGetInstanceExtension(instance, out KhrSurface khrSurface))
            throw new InvalidOperationException("Failed to get KHR_surface extension.");

        var physicalDevice = PickPhysicalDevice(vk, instance, surface, khrSurface);
        var (graphicsFamily, presentFamily) = FindQueueFamilies(vk, physicalDevice, surface, khrSurface);
        var capabilities = QueryDeviceCapabilities(vk, physicalDevice, vulkanApiVersion);

        var device = CreateLogicalDevice(vk, physicalDevice, graphicsFamily, presentFamily);

        vk.GetDeviceQueue(device, graphicsFamily, 0, out var graphicsQueue);
        vk.GetDeviceQueue(device, presentFamily, 0, out var presentQueue);

        if (!vk.TryGetDeviceExtension(instance, device, out KhrSwapchain khrSwapchain))
            throw new InvalidOperationException("Failed to get KHR_swapchain extension.");

        var state = new GpuState
        {
            Capabilities = capabilities,
            Vk = vk,
            Instance = instance,
            Surface = surface,
            PhysicalDevice = physicalDevice,
            Device = device,
            GraphicsQueue = graphicsQueue,
            PresentQueue = presentQueue,
            GraphicsQueueFamily = graphicsFamily,
            PresentQueueFamily = presentFamily,
            KhrSurface = khrSurface,
            KhrSwapchain = khrSwapchain,
            Allocator = new Allocator(vk, physicalDevice),
        };

        VulkanSwapchain.Create(state, initialWidth, initialHeight);
        CreateCommandPool(state);
        CreateCommandBuffers(state);
        CreateSyncObjects(state);

        return state;
    }

    private static unsafe Instance CreateInstance(Vk vk, string[] requiredExtensions, uint vulkanApiVersion = 0)
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = vulkanApiVersion != 0 ? vulkanApiVersion : Vk.Version13,
        };

        // Marshal extension strings to native
        var extPtrs = new nint[requiredExtensions.Length];
        for (int i = 0; i < requiredExtensions.Length; i++)
            extPtrs[i] = Marshal.StringToHGlobalAnsi(requiredExtensions[i]);

        try
        {
            fixed (nint* pExts = extPtrs)
            {
                var createInfo = new InstanceCreateInfo
                {
                    SType = StructureType.InstanceCreateInfo,
                    PApplicationInfo = &appInfo,
                    EnabledExtensionCount = (uint)requiredExtensions.Length,
                    PpEnabledExtensionNames = (byte**)pExts,
                };

                // Try with validation layers first (debug only)
                if (EnableValidation && ValidationLayersAvailable(vk))
                {
                    var layerPtrs = new nint[ValidationLayers.Length];
                    for (int i = 0; i < ValidationLayers.Length; i++)
                        layerPtrs[i] = Marshal.StringToHGlobalAnsi(ValidationLayers[i]);

                    try
                    {
                        fixed (nint* pLayers = layerPtrs)
                        {
                            createInfo.EnabledLayerCount = (uint)ValidationLayers.Length;
                            createInfo.PpEnabledLayerNames = (byte**)pLayers;

                            var layerResult = vk.CreateInstance(&createInfo, null, out var inst);
                            if (layerResult == Result.Success)
                            {
                                Console.WriteLine("  Validation layers: enabled");
                                return inst;
                            }
                            Console.WriteLine($"  Validation layers failed ({layerResult}), continuing without.");
                        }
                    }
                    finally
                    {
                        foreach (var ptr in layerPtrs) Marshal.FreeHGlobal(ptr);
                    }

                    // Reset for retry without layers
                    createInfo.EnabledLayerCount = 0;
                    createInfo.PpEnabledLayerNames = null;
                }

                var result = vk.CreateInstance(&createInfo, null, out var instance);
                if (result != Result.Success)
                    throw new InvalidOperationException($"Failed to create Vulkan instance: {result}");

                if (EnableValidation)
                    Console.WriteLine("  Validation layers: not available (install Vulkan SDK to enable)");

                return instance;
            }
        }
        finally
        {
            foreach (var ptr in extPtrs) Marshal.FreeHGlobal(ptr);
        }
    }

    private static unsafe bool ValidationLayersAvailable(Vk vk)
    {
        uint layerCount = 0;
        vk.EnumerateInstanceLayerProperties(&layerCount, null);
        if (layerCount == 0) return false;

        var available = new LayerProperties[layerCount];
        fixed (LayerProperties* ptr = available)
            vk.EnumerateInstanceLayerProperties(&layerCount, ptr);

        foreach (var required in ValidationLayers)
        {
            bool found = false;
            for (int i = 0; i < available.Length; i++)
            {
                fixed (LayerProperties* pLayer = &available[i])
                {
                    var name = Marshal.PtrToStringAnsi((nint)pLayer->LayerName);
                    if (name == required) { found = true; break; }
                }
            }
            if (!found) return false;
        }
        return true;
    }

    private static unsafe PhysicalDevice PickPhysicalDevice(Vk vk, Instance instance, SurfaceKHR surface, KhrSurface khrSurface)
    {
        uint deviceCount = 0;
        vk.EnumeratePhysicalDevices(instance, &deviceCount, null);
        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan-capable GPU found.");

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* ptr = devices)
            vk.EnumeratePhysicalDevices(instance, &deviceCount, ptr);

        PhysicalDevice? chosen = null;
        foreach (var dev in devices)
        {
            if (!IsDeviceSuitable(vk, dev, surface, khrSurface)) continue;

            vk.GetPhysicalDeviceProperties(dev, out var props);
            if (props.DeviceType == PhysicalDeviceType.DiscreteGpu)
                return dev;

            chosen ??= dev;
        }

        return chosen ?? throw new InvalidOperationException("No suitable Vulkan GPU found.");
    }

    private static unsafe bool IsDeviceSuitable(Vk vk, PhysicalDevice device, SurfaceKHR surface, KhrSurface khrSurface)
    {
        var (gf, pf) = FindQueueFamiliesOrDefault(vk, device, surface, khrSurface);
        if (gf == uint.MaxValue || pf == uint.MaxValue)
            return false;

        uint extCount = 0;
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extCount, null);
        var available = new ExtensionProperties[extCount];
        fixed (ExtensionProperties* ptr = available)
            vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extCount, ptr);

        var requiredSet = new HashSet<string>(DeviceExtensions);
        for (int i = 0; i < available.Length; i++)
        {
            fixed (ExtensionProperties* pExt = &available[i])
                requiredSet.Remove(Marshal.PtrToStringAnsi((nint)pExt->ExtensionName)!);
        }
        if (requiredSet.Count > 0) return false;

        uint formatCount = 0;
        khrSurface.GetPhysicalDeviceSurfaceFormats(device, surface, &formatCount, null);
        if (formatCount == 0) return false;

        uint modeCount = 0;
        khrSurface.GetPhysicalDeviceSurfacePresentModes(device, surface, &modeCount, null);
        return modeCount > 0;
    }

    public static (uint graphicsFamily, uint presentFamily) FindQueueFamilies(
        Vk vk, PhysicalDevice device, SurfaceKHR surface, KhrSurface khrSurface)
    {
        var (gf, pf) = FindQueueFamiliesOrDefault(vk, device, surface, khrSurface);
        if (gf == uint.MaxValue || pf == uint.MaxValue)
            throw new InvalidOperationException("Required queue families not found.");
        return (gf, pf);
    }

    private static unsafe (uint graphicsFamily, uint presentFamily) FindQueueFamiliesOrDefault(
        Vk vk, PhysicalDevice device, SurfaceKHR surface, KhrSurface khrSurface)
    {
        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);
        var families = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* ptr = families)
            vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, ptr);

        uint graphicsFamily = uint.MaxValue;
        uint presentFamily = uint.MaxValue;

        for (uint i = 0; i < families.Length; i++)
        {
            if (families[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                graphicsFamily = i;

            khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, surface, out var presentSupport);
            if (presentSupport)
                presentFamily = i;

            if (graphicsFamily != uint.MaxValue && presentFamily != uint.MaxValue)
                break;
        }

        return (graphicsFamily, presentFamily);
    }

    private static unsafe Device CreateLogicalDevice(Vk vk, PhysicalDevice physicalDevice, uint graphicsFamily, uint presentFamily)
    {
        var uniqueFamilies = graphicsFamily == presentFamily
            ? new[] { graphicsFamily }
            : new[] { graphicsFamily, presentFamily };

        var queueCreateInfos = new DeviceQueueCreateInfo[uniqueFamilies.Length];
        float priority = 1.0f;

        for (int i = 0; i < uniqueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &priority,
            };
        }

        var features = new PhysicalDeviceFeatures();

        var extPtrs = new nint[DeviceExtensions.Length];
        for (int i = 0; i < DeviceExtensions.Length; i++)
            extPtrs[i] = Marshal.StringToHGlobalAnsi(DeviceExtensions[i]);

        try
        {
            fixed (DeviceQueueCreateInfo* pQueueInfos = queueCreateInfos)
            fixed (nint* pExts = extPtrs)
            {
                var createInfo = new DeviceCreateInfo
                {
                    SType = StructureType.DeviceCreateInfo,
                    QueueCreateInfoCount = (uint)uniqueFamilies.Length,
                    PQueueCreateInfos = pQueueInfos,
                    PEnabledFeatures = &features,
                    EnabledExtensionCount = (uint)DeviceExtensions.Length,
                    PpEnabledExtensionNames = (byte**)pExts,
                };

                if (vk.CreateDevice(physicalDevice, &createInfo, null, out var device) != Result.Success)
                    throw new InvalidOperationException("Failed to create logical device.");

                return device;
            }
        }
        finally
        {
            foreach (var ptr in extPtrs) Marshal.FreeHGlobal(ptr);
        }
    }

    private static unsafe void CreateCommandPool(GpuState state)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = state.GraphicsQueueFamily,
        };

        if (state.Vk.CreateCommandPool(state.Device, &poolInfo, null, out var pool) != Result.Success)
            throw new InvalidOperationException("Failed to create command pool.");

        state.CommandPool = pool;
    }

    private static unsafe void CreateCommandBuffers(GpuState state)
    {
        state.CommandBuffers = new CommandBuffer[GpuState.MaxFramesInFlight];

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = state.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint)GpuState.MaxFramesInFlight,
        };

        fixed (CommandBuffer* ptr = state.CommandBuffers)
        {
            if (state.Vk.AllocateCommandBuffers(state.Device, &allocInfo, ptr) != Result.Success)
                throw new InvalidOperationException("Failed to allocate command buffers.");
        }
    }

    private static unsafe void CreateSyncObjects(GpuState state)
    {
        state.ImageAvailableSemaphores = new Semaphore[GpuState.MaxFramesInFlight];
        state.InFlightFences = new Fence[GpuState.MaxFramesInFlight];

        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };

        for (int i = 0; i < GpuState.MaxFramesInFlight; i++)
        {
            if (state.Vk.CreateSemaphore(state.Device, &semaphoreInfo, null, out state.ImageAvailableSemaphores[i]) != Result.Success ||
                state.Vk.CreateFence(state.Device, &fenceInfo, null, out state.InFlightFences[i]) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create sync objects.");
            }
        }

        CreateRenderFinishedSemaphores(state);
    }

    /// <summary>
    /// Creates one render-finished semaphore per swapchain image so the presentation
    /// engine can hold a semaphore until the image is re-acquired without blocking reuse.
    /// </summary>
    public static unsafe void CreateRenderFinishedSemaphores(GpuState state)
    {
        state.RenderFinishedSemaphores = new Semaphore[state.SwapchainImages.Length];
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };

        for (int i = 0; i < state.SwapchainImages.Length; i++)
        {
            if (state.Vk.CreateSemaphore(state.Device, &semaphoreInfo, null, out state.RenderFinishedSemaphores[i]) != Result.Success)
                throw new InvalidOperationException("Failed to create render-finished semaphore.");
        }
    }

    public static unsafe void DestroyRenderFinishedSemaphores(GpuState state)
    {
        foreach (var sem in state.RenderFinishedSemaphores)
            state.Vk.DestroySemaphore(state.Device, sem, null);
        state.RenderFinishedSemaphores = [];
    }

    /// <summary>
    /// Queries physical device properties, features, and format support to build
    /// an immutable <see cref="DeviceCapabilities"/> snapshot. Called once during
    /// <see cref="Create"/> before the logical device is created.
    /// </summary>
    private static unsafe DeviceCapabilities QueryDeviceCapabilities(
        Vk vk, PhysicalDevice physicalDevice, uint apiVersion)
    {
        vk.GetPhysicalDeviceProperties(physicalDevice, out var props);
        vk.GetPhysicalDeviceFeatures(physicalDevice, out var features);

        var depthFormat = VulkanImage.FindDepthFormat(vk, physicalDevice);
        var deviceName = Marshal.PtrToStringAnsi((nint)props.DeviceName) ?? "Unknown";

        return new DeviceCapabilities(
            DeviceName: deviceName,
            ApiVersion: apiVersion != 0 ? apiVersion : Vk.Version13,
            DepthFormat: depthFormat,
            TimestampPeriod: props.Limits.TimestampPeriod,
            TimestampSupported: props.Limits.TimestampComputeAndGraphics,
            MaxColorAttachments: props.Limits.MaxColorAttachments,
            MaxBoundDescriptorSets: props.Limits.MaxBoundDescriptorSets,
            MaxSamplersPerStage: props.Limits.MaxPerStageDescriptorSamplers,
            MaxPushConstantSize: props.Limits.MaxPushConstantsSize,
            MaxComputeWorkGroupSize: props.Limits.MaxComputeWorkGroupInvocations,
            SupportsGeometryShader: features.GeometryShader,
            SupportsTessellation: features.TessellationShader);
    }

    public static unsafe void Destroy(GpuState state)
    {
        state.Vk.DeviceWaitIdle(state.Device);

        DestroyRenderFinishedSemaphores(state);

        for (int i = 0; i < GpuState.MaxFramesInFlight; i++)
        {
            state.Vk.DestroySemaphore(state.Device, state.ImageAvailableSemaphores[i], null);
            state.Vk.DestroyFence(state.Device, state.InFlightFences[i], null);
        }

        state.Vk.DestroyCommandPool(state.Device, state.CommandPool, null);

        VulkanSwapchain.Destroy(state);

        state.Vk.DestroyDevice(state.Device, null);
        state.KhrSurface.DestroySurface(state.Instance, state.Surface, null);
        state.Vk.DestroyInstance(state.Instance, null);
        state.Vk.Dispose();
    }
}
