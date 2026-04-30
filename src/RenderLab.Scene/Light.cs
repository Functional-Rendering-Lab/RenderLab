namespace RenderLab.Scene;

/// <summary>
/// Discriminated union of light variants the deferred lighting pass understands.
/// Concrete cases: <see cref="PointLight"/> (positional with attenuation) and
/// <see cref="DirectionalLight"/> (constant direction, no falloff). The pure
/// scene domain holds these as a single <c>ImmutableArray&lt;Light&gt;</c>;
/// <see cref="LightPacking"/> partitions and packs them into the GPU SSBO at
/// the engine boundary.
/// </summary>
public abstract record Light;
