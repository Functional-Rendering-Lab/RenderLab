namespace RenderLab.Assets;

/// <summary>
/// Opaque, typed handle to a registered material asset. Pure value type — never
/// exposes GPU resources. Allocated by an asset registry; <see cref="None"/> is
/// the sentinel for "no material", which the registry maps to a built-in
/// default Blinn-Phong material so render code can resolve unconditionally.
/// </summary>
public readonly record struct MaterialId(int Value)
{
    public static readonly MaterialId None = new(0);
    public bool IsNone => Value == 0;
    public override string ToString() => IsNone ? "MaterialId.None" : $"MaterialId({Value})";
}
