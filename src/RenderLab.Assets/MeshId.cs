namespace RenderLab.Assets;

/// <summary>
/// Opaque, typed handle to a registered mesh asset. Pure value type — never
/// exposes GPU resources. Allocated by an asset registry; <see cref="None"/> is
/// the sentinel for "no mesh".
/// </summary>
public readonly record struct MeshId(int Value)
{
    public static readonly MeshId None = new(0);
    public bool IsNone => Value == 0;
    public override string ToString() => IsNone ? "MeshId.None" : $"MeshId({Value})";
}
