using RenderLab.App.Demos;
using RenderLab.Ui;

DemoId? next = ParseInitialDemo(args);
AppUiModel app = AppUiModel.Default(next!.Value);

while (next is { } id)
{
    IDemo demo = Construct(id);
    using (demo)
        next = demo.Run(app.HandOffTo(id));
    if (next is not null)
        app = app.HandOffTo(next.Value);
}

static DemoId ParseInitialDemo(string[] args)
{
    string name = (args.FirstOrDefault() ?? "deferred").ToLowerInvariant();
    return name switch
    {
        "triangle" => DemoId.Triangle,
        "gbuffer"  => DemoId.GBuffer,
        "deferred" => DemoId.Deferred,
        _ => throw new ArgumentException(
            $"Unknown demo '{name}'. Available: triangle, gbuffer, deferred"),
    };
}

static IDemo Construct(DemoId id) => id switch
{
    DemoId.Triangle => new TriangleDemo(),
    DemoId.GBuffer  => new GBufferDemo(),
    DemoId.Deferred => new DeferredDemo(),
    _ => throw new ArgumentOutOfRangeException(nameof(id)),
};
