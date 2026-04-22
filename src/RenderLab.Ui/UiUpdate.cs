namespace RenderLab.Ui;

/// <summary>
/// Pure reducer: folds <see cref="UiMsg"/> messages into a new <see cref="UiModel"/>.
/// No side effects, no I/O — unit-testable without a GPU.
/// </summary>
public static class UiUpdate
{
    public static UiModel Apply(UiModel model, UiMsg msg) => msg switch
    {
        UiMsg.UpdateCamera m          => model with { Camera = m.Camera },
        UiMsg.UpdateLight m           => model with { KeyLight = m.Light },
        UiMsg.UpdateMaterial m        => model with { Material = m.Material },
        UiMsg.UpdateMeshTransform m   => model with { MeshTransform = m.Transform },
        UiMsg.SetShading m            => model with { Shading = m.Mode },
        UiMsg.SetLightingOnly m       => model with { LightingOnly = m.On },
        UiMsg.SetViz m                => model with { Viz = m.Mode },
        _                             => model,
    };

    public static UiModel ApplyAll(UiModel model, IEnumerable<UiMsg> msgs)
    {
        foreach (var msg in msgs) model = Apply(model, msg);
        return model;
    }
}
