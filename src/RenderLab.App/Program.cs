using RenderLab.App.Demos;

var name = args.FirstOrDefault() ?? "deferred";

IDemo demo = name.ToLowerInvariant() switch
{
    "triangle" => new TriangleDemo(),
    "gbuffer"  => new GBufferDemo(),
    "deferred" => new DeferredDemo(),
    _ => throw new ArgumentException(
        $"Unknown demo '{name}'. Available: triangle, gbuffer, deferred"),
};

using (demo) demo.Run();
