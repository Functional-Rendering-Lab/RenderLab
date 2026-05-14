namespace RenderLab.Ui;

/// <summary>
/// Currently selected item in the editor. The Inspector window renders whichever
/// branch the Selection holds; outliner / browser panels emit <see cref="UiMsg.Select"/>
/// to change it. <see cref="None"/> is the empty selection (Inspector shows a hint).
/// Light is indexed because lights are an ordered SSBO; everything else is keyed
/// by stable id.
/// </summary>
public abstract record Selection
{
    public sealed record None : Selection;
    public sealed record Drawable(Guid LocalId) : Selection;
    public sealed record Light(int Index) : Selection;
    public sealed record MaterialAsset(Guid Guid) : Selection;
    public sealed record MeshAsset(Guid Guid) : Selection;
    public sealed record TextureAsset(Guid Guid) : Selection;
    public sealed record Environment : Selection;
    public sealed record Camera : Selection;

    public static readonly Selection Empty = new None();
}
