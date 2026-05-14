using RenderLab.Assets;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Ui.Tests;

public class UiUpdateSelectionTests
{
    private static UiModel Fresh() => UiModel.Default;

    [Fact]
    public void Select_storesSelection()
    {
        var sel = new Selection.Environment();
        var next = UiUpdate.Apply(Fresh(), new UiMsg.Select(sel));
        Assert.Equal(sel, next.Selection);
    }

    [Fact]
    public void AddDrawable_selectsTheNewDrawable()
    {
        var next = UiUpdate.Apply(Fresh(), new UiMsg.AddDrawable("test", MeshId.None, Transform.Default, MaterialId.None));
        var d = Assert.IsType<Selection.Drawable>(next.Selection);
        Assert.Equal(next.Drawables[^1].LocalId, d.LocalId);
    }

    [Fact]
    public void RemoveDrawable_clearsSelectionWhenSelected()
    {
        var added = UiUpdate.Apply(Fresh(), new UiMsg.AddDrawable("t", MeshId.None, Transform.Default, MaterialId.None));
        var id = ((Selection.Drawable)added.Selection).LocalId;
        var removed = UiUpdate.Apply(added, new UiMsg.RemoveDrawable(id));
        Assert.IsType<Selection.None>(removed.Selection);
    }

    [Fact]
    public void RemoveLight_clearsSelectionWhenTargeted()
    {
        var start = Fresh();
        var picked = UiUpdate.Apply(start, new UiMsg.Select(new Selection.Light(0)));
        var removed = UiUpdate.Apply(picked, new UiMsg.RemoveLight(0));
        Assert.IsType<Selection.None>(removed.Selection);
    }

    [Fact]
    public void RemoveLight_shiftsIndexWhenLowerRemoved()
    {
        var start = Fresh();
        var picked = UiUpdate.Apply(start, new UiMsg.Select(new Selection.Light(2)));
        var removed = UiUpdate.Apply(picked, new UiMsg.RemoveLight(0));
        var s = Assert.IsType<Selection.Light>(removed.Selection);
        Assert.Equal(1, s.Index);
    }
}
