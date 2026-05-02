using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Pure reducer: folds <see cref="UiMsg"/> messages into a new <see cref="UiModel"/>.
/// No side effects, no I/O — unit-testable without a GPU.
/// </summary>
public static class UiUpdate
{
    public static UiModel Apply(UiModel model, UiMsg msg) => msg switch
    {
        UiMsg.UpdateCamera m            => model with { Camera = m.Camera },
        UiMsg.UpdateLight m             => UpdateLight(model, m.Index, m.Light),
        UiMsg.AddPointLight             => model with { Lights = model.Lights.Add(UiModel.DefaultNewPointLight) },
        UiMsg.AddDirectionalLight       => model with { Lights = model.Lights.Add(UiModel.DefaultNewDirectionalLight) },
        UiMsg.RemoveLight m             => RemoveLight(model, m.Index),
        UiMsg.UpdateAmbient m           => model with { Ambient = m.Ambient },
        UiMsg.AddDrawable m             => AddDrawable(model, m.Name, m.Mesh, m.Transform, m.Material),
        UiMsg.RemoveDrawable m          => RemoveDrawable(model, m.LocalId),
        UiMsg.SelectDrawable m          => model with { SelectedDrawable = m.LocalId },
        UiMsg.SetDrawableTransform m    => UpdateDrawable(model, m.LocalId, d => d with { Transform = m.Transform }),
        UiMsg.SetDrawableMaterial m     => UpdateDrawable(model, m.LocalId, d => d with { Material = m.Material }),
        UiMsg.SetShading m              => model with { Shading = m.Mode },
        UiMsg.SetLightingOnly m         => model with { LightingOnly = m.On },
        UiMsg.SetViz m                  => model with { Viz = m.Mode },
        UiMsg.SetClearColor m           => model with { ClearColor = m.Color },
        _                               => model,
    };

    private static UiModel UpdateLight(UiModel model, int index, Light light) =>
        index >= 0 && index < model.Lights.Length
            ? model with { Lights = model.Lights.SetItem(index, light) }
            : model;

    private static UiModel RemoveLight(UiModel model, int index) =>
        index >= 0 && index < model.Lights.Length
            ? model with { Lights = model.Lights.RemoveAt(index) }
            : model;

    private static UiModel AddDrawable(UiModel model, string name, RenderLab.Assets.MeshId mesh, Transform transform, MaterialParams material)
    {
        var drawable = new EditableDrawable(Guid.NewGuid(), name, mesh, transform, material);
        return model with
        {
            Drawables = model.Drawables.Add(drawable),
            SelectedDrawable = drawable.LocalId,
        };
    }

    private static UiModel RemoveDrawable(UiModel model, Guid id)
    {
        var idx = IndexOf(model.Drawables, id);
        if (idx < 0) return model;
        var next = model.Drawables.RemoveAt(idx);
        var selection = model.SelectedDrawable == id ? null : model.SelectedDrawable;
        return model with { Drawables = next, SelectedDrawable = selection };
    }

    private static UiModel UpdateDrawable(UiModel model, Guid id, Func<EditableDrawable, EditableDrawable> edit)
    {
        var idx = IndexOf(model.Drawables, id);
        return idx < 0
            ? model
            : model with { Drawables = model.Drawables.SetItem(idx, edit(model.Drawables[idx])) };
    }

    private static int IndexOf(System.Collections.Immutable.ImmutableArray<EditableDrawable> drawables, Guid id)
    {
        for (int i = 0; i < drawables.Length; i++)
            if (drawables[i].LocalId == id) return i;
        return -1;
    }

    public static UiModel ApplyAll(UiModel model, IEnumerable<UiMsg> msgs)
    {
        foreach (var msg in msgs) model = Apply(model, msg);
        return model;
    }
}
