using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;

namespace RenderLab.Gpu;

/// <summary>
/// What a pipeline renders into: an image, its view, and how big it is.
/// <para>
/// It used to be the swapchain image, chosen by index, and every pipeline said so - a framebuffer
/// per swapchain image, a render pass ending in <c>PresentSrcKhr</c>, an extent read straight off
/// <see cref="GpuState.SwapchainExtent"/>. That is the arrangement where the editor is something
/// drawn over the top of the frame afterwards, and the price of it is that the scene is the size
/// and shape of the <em>window</em> rather than of the panel it is looked at through.
/// </para>
/// <para>
/// A pipeline is handed one of these now and never asks which image is being presented. What it
/// draws into is somebody else's decision, which is what makes rendering the scene into a
/// viewport, a thumbnail, or a file the same code.
/// </para>
/// </summary>
public readonly record struct RenderTarget(Image Image, ImageView View, Extent2D Extent);

/// <summary>
/// The image the scene is rendered into, sized to the viewport panel rather than to the window,
/// and handed to the editor as a picture to draw.
/// <para>
/// The swapchain is the editor's now. Nothing draws into it but the interface, and the scene
/// arrives inside that interface the way any other picture would - which is what makes the
/// viewport's aspect ratio the viewport's, stops the renderer shading two million pixels that
/// panels are about to cover, and means the shell can put something in front of the scene without
/// having to be transparent to it.
/// </para>
/// <para>
/// Its format is the swapchain's, so the tonemapper writes exactly the bytes it wrote before and
/// the sampler decodes them again on the way into the interface. Anything else would put a second
/// colour-space decision in a program that has already made one.
/// </para>
/// </summary>
public sealed class ViewportTarget : IDisposable
{
    private readonly GpuState _gpu;
    private readonly Image _image;
    private readonly Allocation _allocation;
    private readonly ImageView _view;
    private readonly Sampler _sampler;

    private ViewportTarget(GpuState gpu, Image image, Allocation allocation, ImageView view,
        Sampler sampler, Extent2D extent)
    {
        _gpu = gpu;
        _image = image;
        _allocation = allocation;
        _view = view;
        _sampler = sampler;
        Extent = extent;
    }

    /// <summary>How big it is, in physical pixels - the size the scene is actually rendered at.</summary>
    public Extent2D Extent { get; }

    /// <summary>What the interface samples it through.</summary>
    public ImageView View => _view;

    public Sampler Sampler => _sampler;

    /// <summary>What a pipeline is given: the image, to barrier, and the view, to hang a framebuffer on.</summary>
    public RenderTarget AsRenderTarget => new(_image, _view, Extent);

    /// <summary>
    /// Makes one. Never smaller than a pixel each way: a panel dragged shut is a viewport of zero
    /// area, and a zero-sized image is not a thing Vulkan will make.
    /// </summary>
    public static ViewportTarget Create(GpuState gpu, uint width, uint height)
    {
        uint w = Math.Max(1, width);
        uint h = Math.Max(1, height);

        var (image, allocation, view) = VulkanImage.CreateOffscreen(gpu, gpu.SwapchainFormat, w, h);
        return new ViewportTarget(gpu, image, allocation, view, VulkanImage.CreateSampler(gpu),
            new Extent2D(w, h));
    }

    /// <summary>
    /// Whether this is still the right size for a viewport of <paramref name="width"/> x
    /// <paramref name="height"/> physical pixels. A frame that says yes costs nothing; a frame
    /// that says no costs the device going idle, which is why it is asked rather than assumed.
    /// </summary>
    public bool Fits(uint width, uint height) =>
        Extent.Width == Math.Max(1, width) && Extent.Height == Math.Max(1, height);

    public unsafe void Dispose()
    {
        _gpu.Vk.DestroySampler(_gpu.Device, _sampler, null);
        VulkanImage.DestroyOffscreen(_gpu, _image, _allocation, _view);
    }
}
