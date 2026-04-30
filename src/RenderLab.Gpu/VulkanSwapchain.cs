using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Swapchain lifecycle: surface-format / present-mode selection, image and
/// image-view creation, and recreation on window resize. Mutates
/// <see cref="GpuState"/> in place — the swapchain handles, images, and views
/// live on the kernel.
/// </summary>
/// <remarks>
/// Present mode and image count are chosen for a lab/measurement context, not
/// AAA smoothness. We prefer <c>FIFO_RELAXED</c> over <c>MAILBOX</c> and use
/// exactly <c>capabilities.MinImageCount</c> (no <c>+1</c>) so that frame-time
/// overruns are visible (tearing on the missed frame) instead of being hidden
/// by extra buffering. See <c>blogs/ideas/field-notes/swapchain-present-mode/</c>
/// for the decision record.
/// </remarks>
public static class VulkanSwapchain
{
    public static unsafe void Create(GpuState state, uint width = 0, uint height = 0)
    {
        var vk = state.Vk;

        state.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(
            state.PhysicalDevice, state.Surface, out var capabilities);

        var format = ChooseSurfaceFormat(state);
        var presentMode = ChoosePresentMode(state);
        var extent = ChooseExtent(capabilities, width, height);

        // Lab policy: use the minimum the surface supports, no +1. Extra
        // buffering would hide frame-time overruns; we want them visible.
        uint imageCount = capabilities.MinImageCount;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        Console.WriteLine($"  Swapchain: present mode = {presentMode}, image count = {imageCount} (min = {capabilities.MinImageCount}, max = {capabilities.MaxImageCount})");

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = state.Surface,
            MinImageCount = imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            PreTransform = SurfaceTransformFlagsKHR.IdentityBitKhr,
            CompositeAlpha = ChooseCompositeAlpha(capabilities),
            PresentMode = presentMode,
            Clipped = true,
        };

        if (state.GraphicsQueueFamily != state.PresentQueueFamily)
        {
            var queueFamilies = stackalloc uint[] { state.GraphicsQueueFamily, state.PresentQueueFamily };
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = 2;
            createInfo.PQueueFamilyIndices = queueFamilies;
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        if (state.KhrSwapchain.CreateSwapchain(state.Device, &createInfo, null, out var swapchain) != Result.Success)
            throw new InvalidOperationException("Failed to create swapchain.");

        state.Swapchain = swapchain;
        state.SwapchainFormat = format.Format;
        state.SwapchainExtent = extent;

        // Get swapchain images
        state.KhrSwapchain.GetSwapchainImages(state.Device, swapchain, &imageCount, null);
        state.SwapchainImages = new Image[imageCount];
        fixed (Image* ptr = state.SwapchainImages)
            state.KhrSwapchain.GetSwapchainImages(state.Device, swapchain, &imageCount, ptr);

        // Create image views
        state.SwapchainImageViews = new ImageView[state.SwapchainImages.Length];
        for (int i = 0; i < state.SwapchainImages.Length; i++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = state.SwapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = state.SwapchainFormat,
                Components = new ComponentMapping
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity,
                },
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };

            if (vk.CreateImageView(state.Device, &viewInfo, null, out state.SwapchainImageViews[i]) != Result.Success)
                throw new InvalidOperationException("Failed to create swapchain image view.");
        }
    }

    public static unsafe void Destroy(GpuState state)
    {
        foreach (var view in state.SwapchainImageViews)
            state.Vk.DestroyImageView(state.Device, view, null);

        state.KhrSwapchain.DestroySwapchain(state.Device, state.Swapchain, null);
        state.SwapchainImageViews = [];
        state.SwapchainImages = [];
    }

    public static void Recreate(GpuState state, uint width, uint height)
    {
        if (width == 0 || height == 0) return;

        state.Vk.DeviceWaitIdle(state.Device);
        Destroy(state);
        Create(state, width, height);
    }

    private static unsafe SurfaceFormatKHR ChooseSurfaceFormat(GpuState state)
    {
        uint formatCount = 0;
        state.KhrSurface.GetPhysicalDeviceSurfaceFormats(state.PhysicalDevice, state.Surface, &formatCount, null);
        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* ptr = formats)
            state.KhrSurface.GetPhysicalDeviceSurfaceFormats(state.PhysicalDevice, state.Surface, &formatCount, ptr);

        foreach (var fmt in formats)
        {
            if (fmt.Format == Format.B8G8R8A8Srgb && fmt.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return fmt;
        }

        return formats[0];
    }

    // Lab policy: prefer FIFO_RELAXED so a missed frame tears on that frame
    // (loud, visible feedback) instead of being smoothed over by Mailbox or
    // double-displayed under strict FIFO. FIFO is the guaranteed fallback —
    // every Vulkan implementation must support it.
    private static unsafe PresentModeKHR ChoosePresentMode(GpuState state)
    {
        uint modeCount = 0;
        state.KhrSurface.GetPhysicalDeviceSurfacePresentModes(state.PhysicalDevice, state.Surface, &modeCount, null);
        var modes = new PresentModeKHR[modeCount];
        fixed (PresentModeKHR* ptr = modes)
            state.KhrSurface.GetPhysicalDeviceSurfacePresentModes(state.PhysicalDevice, state.Surface, &modeCount, ptr);

        foreach (var mode in modes)
        {
            if (mode == PresentModeKHR.FifoRelaxedKhr)
                return mode;
        }

        return PresentModeKHR.FifoKhr;
    }

    private static CompositeAlphaFlagsKHR ChooseCompositeAlpha(SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.SupportedCompositeAlpha.HasFlag(CompositeAlphaFlagsKHR.OpaqueBitKhr))
            return CompositeAlphaFlagsKHR.OpaqueBitKhr;
        if (capabilities.SupportedCompositeAlpha.HasFlag(CompositeAlphaFlagsKHR.InheritBitKhr))
            return CompositeAlphaFlagsKHR.InheritBitKhr;
        return CompositeAlphaFlagsKHR.OpaqueBitKhr;
    }

    private static Extent2D ChooseExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
    {
        // When the caller provides explicit dimensions, prefer those over CurrentExtent.
        if (width > 0 && height > 0)
        {
            return new Extent2D
            {
                Width = Math.Clamp(width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Height = Math.Clamp(height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height),
            };
        }

        if (capabilities.CurrentExtent.Width != uint.MaxValue)
            return capabilities.CurrentExtent;

        return capabilities.MinImageExtent;
    }
}
