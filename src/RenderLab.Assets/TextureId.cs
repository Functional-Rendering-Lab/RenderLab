namespace RenderLab.Assets;

/// <summary>
/// Opaque, typed handle to a registered texture asset. Pure value type — never
/// exposes GPU resources. Allocated by an asset registry; <see cref="None"/> is
/// the sentinel for "no texture", which the resolver maps to a built-in 1×1
/// white fallback so shaders can sample unconditionally.
/// </summary>
public readonly record struct TextureId(int Value)
{
    public static readonly TextureId None = new(0);
    public bool IsNone => Value == 0;
    public override string ToString() => IsNone ? "TextureId.None" : $"TextureId({Value})";
}
