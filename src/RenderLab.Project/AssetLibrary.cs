using System.Collections.Immutable;
using System.Text.Json;

namespace RenderLab.Project;

/// <summary>
/// Project-scoped inventory of every usable engine asset, keyed by stable
/// GUID. Built by <see cref="AssetLibraryScanner.Scan"/> from the on-disk
/// <c>.meta</c> sidecars, <c>.mat.json</c> material files, and
/// <c>.proc.meta</c> procedural-asset files. Pure data — no Vulkan, no
/// registry.
/// </summary>
public sealed record AssetLibrary(
    ImmutableDictionary<System.Guid, AssetEntry> ByGuid,
    DateTime ScannedAtUtc)
{
    public static AssetLibrary Empty { get; } = new(
        ImmutableDictionary<System.Guid, AssetEntry>.Empty,
        DateTime.MinValue);

    public IEnumerable<AssetEntry> EntriesOfKind(AssetKind kind) =>
        ByGuid.Values.Where(e => e.Kind == kind);

    public AssetEntry? Find(System.Guid guid) =>
        ByGuid.TryGetValue(guid, out var e) ? e : null;
}

/// <summary>
/// Discriminated union root for library entries. Identity is the
/// <see cref="Guid"/> + <see cref="Kind"/> pair; <see cref="Name"/> is for
/// display only and may change without breaking references.
/// </summary>
public abstract record AssetEntry(System.Guid Guid, AssetKind Kind, string Name);

/// <summary>
/// A file-backed asset: bytes live at <see cref="ProjectRelativePath"/> and
/// import settings come from the sidecar. Used for meshes and textures.
/// </summary>
public sealed record FileAssetEntry(
    System.Guid Guid,
    AssetKind Kind,
    string Name,
    string ProjectRelativePath,
    ImportSettings Import) : AssetEntry(Guid, Kind, Name);

/// <summary>
/// A procedural asset: no bytes on disk, just a generator id + params. Lives
/// in a self-describing <c>.proc.meta</c> file (<see cref="ProjectRelativePath"/>).
/// </summary>
public sealed record ProceduralAssetEntry(
    System.Guid Guid,
    AssetKind Kind,
    string Name,
    string ProjectRelativePath,
    string Generator,
    JsonElement? Params) : AssetEntry(Guid, Kind, Name);

/// <summary>
/// A material asset — a first-class, file-backed parameter bundle stored as
/// <c>.mat.json</c>. <see cref="AlbedoTex"/> points at a texture entry's
/// GUID (or <c>null</c> for an untextured material).
/// </summary>
public sealed record MaterialAssetEntry(
    System.Guid Guid,
    string Name,
    string ProjectRelativePath,
    MaterialParamsDoc Params,
    AssetRef? AlbedoTex) : AssetEntry(Guid, AssetKind.Material, Name);

/// <summary>
/// Material parameter values, serialised inside a <c>.mat.json</c> file.
/// Today only the Blinn-Phong shape is represented.
/// </summary>
public sealed record MaterialParamsDoc(
    float[] Albedo,
    float SpecularStrength,
    float Shininess);

/// <summary>On-disk shape of <c>*.mat.json</c>.</summary>
public sealed record MaterialFileDoc(
    int Version,
    string Name,
    MaterialParamsDoc Params,
    AssetRef? AlbedoTex);

/// <summary>On-disk shape of <c>*.proc.meta</c>: a self-describing procedural
/// asset (no sibling sidecar).</summary>
public sealed record ProceduralFileDoc(
    int Version,
    System.Guid Guid,
    AssetKind Kind,
    string Name,
    string Generator,
    JsonElement? Params);
