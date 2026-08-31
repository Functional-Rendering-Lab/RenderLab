using System.Numerics;
using Ptah;
using Ptah.Widgets;
using RenderLab.Assets;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The one editing surface in the tool, showing whichever item <see cref="UiModel.Selection"/>
/// points at. The outliners - Scene, Asset Browser - emit <see cref="UiMsg.Select"/>; this
/// switches on what they selected and lays out the fields for it. There are no inline editors
/// anywhere else, which is the rule that keeps one value from having two places it can be typed.
/// <para>
/// Every field here takes a value and hands one back, so the panel reads as the form it is: the
/// widget owns the gesture and the clamping, this owns what the value means, and a message is
/// pushed on the frame something actually moved. <c>Edit&lt;T&gt;.Changed</c> is what says so,
/// and it is a better answer than the float comparisons the ImGui version needed - those asked
/// the same question of the value after the fact, which is the wrong question for an angle,
/// because an angle round-trips through a quaternion and does not come back bit-identical.
/// </para>
/// </summary>
internal static class InspectorPanel
{
    private const float DegPerRad = 180f / MathF.PI;
    private const float RadPerDeg = MathF.PI / 180f;

    /// <summary>The smallest scale a drawable is allowed, so a transform never carries a zero.</summary>
    private const float MinScale = 1e-4f;

    private static readonly string[] BackgroundModes =
    [
        "Solid color",
        "Ambient gradient",
    ];

    internal static void Draw(WidgetKit w, WidgetState state, UiModel model,
        IAssetCatalog catalog, AssetLibrary library,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        switch (model.Selection)
        {
            case Selection.None:
                Hint(w, "empty", "Nothing selected. Pick an item from Scene, Asset Browser, or Lighting.");
                break;
            case Selection.Drawable d:
                DrawDrawable(w, state, model, d.LocalId, catalog, dispatch);
                break;
            case Selection.Light l:
                DrawLight(w, state, model, l.Index, dispatch);
                break;
            case Selection.MaterialAsset m:
                DrawMaterial(w, state, m.Guid, library, dispatchApp);
                break;
            case Selection.MeshAsset me:
                DrawAsset(w, me.Guid, library, dispatchApp);
                break;
            case Selection.TextureAsset t:
                DrawAsset(w, t.Guid, library, dispatchApp);
                break;
            case Selection.Environment:
                DrawEnvironment(w, state, model, dispatch);
                break;
            case Selection.Camera:
                DrawCamera(w, state, model, dispatch);
                break;
        }
    }

    // ---- Drawable --------------------------------------------------------------

    private static void DrawDrawable(WidgetKit w, WidgetState state, UiModel model, Guid id,
        IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        EditableDrawable? drawable = model.Drawables.FirstOrDefault(d => d.LocalId == id);
        if (drawable is null)
        {
            Hint(w, "gone", "Selected drawable no longer exists.");
            return;
        }

        Header(w, "DRAWABLE", drawable.Name);

        Transform t = drawable.Transform;
        Edit<Vector3> position = w.InputVector3("Position", t.Position, speed: 0.05f);
        Edit<Vector3> rotation = w.InputVector3("Rotation (deg)", EulerDeg(t.Rotation), speed: 1f);
        Edit<float> scale = w.InputFloat("Scale", t.Scale, speed: 0.02f, min: 0.1f, max: 5f);

        if (position.Changed || rotation.Changed || scale.Changed)
        {
            // Only a rotation the user moved is written back through Euler angles. A quaternion
            // that made the trip out and back is not the one that set off: an orientation has
            // more than one triple of angles, so converting the panel's triple every frame would
            // rewrite the transform on frames where nobody touched it, and the scene would be
            // permanently dirty.
            dispatch(new UiMsg.SetDrawableTransform(id, t with
            {
                Position = position.Value,
                Rotation = rotation.Changed
                    ? UnitQuaternion.UnsafeFromUnit(Quat(rotation.Value))
                    : t.Rotation,
                Scale = PositiveScale.UnsafeFrom(MathF.Max(scale.Value, MinScale)),
            }));
        }

        MeshAsset[] meshes = [.. catalog.AllMeshes];
        Edit<int> mesh = w.Combo(state.Popups, "Mesh",
            Array.FindIndex(meshes, m => m.Id == drawable.Mesh), Names(meshes, m => m.Name));
        if (mesh.Changed)
            dispatch(new UiMsg.SetDrawableMesh(id, meshes[mesh.Value].Id));

        MaterialAsset[] materials = [.. catalog.AllMaterials];
        Edit<int> material = w.Combo(state.Popups, "Material",
            Array.FindIndex(materials, m => m.Id == drawable.Material), Names(materials, m => m.Name));
        if (material.Changed)
            dispatch(new UiMsg.SetDrawableMaterial(id, materials[material.Value].Id));

        Hint(w, "material_hint", "Material parameters are edited by selecting the material asset.");
    }

    // ---- Light -----------------------------------------------------------------

    private static void DrawLight(WidgetKit w, WidgetState state, UiModel model, int index,
        Action<UiMsg> dispatch)
    {
        if (index < 0 || index >= model.Lights.Length)
        {
            Hint(w, "gone", "Selected light no longer exists.");
            return;
        }

        Light light = model.Lights[index];
        Header(w, "LIGHT", light switch
        {
            PointLight => $"{index} - point",
            DirectionalLight => $"{index} - directional",
            _ => index.ToString(),
        });

        switch (light)
        {
            case PointLight p:
            {
                Edit<Vector3> position = w.InputVector3("Position", p.Position, speed: 0.05f);
                Edit<Color> color = w.ColorEdit(state.Colors, "Color", Rgb(p.Color));
                Edit<float> intensity = w.InputFloat("Intensity", p.Intensity.Value,
                    speed: 0.05f, min: 0f, max: 100f);

                if (position.Changed || color.Changed || intensity.Changed)
                {
                    dispatch(new UiMsg.UpdateLight(index, new PointLight(
                        position.Value,
                        Color01.UnsafeFrom(Rgb(color.Value)),
                        Intensity.UnsafeFrom(intensity.Value))));
                }

                break;
            }

            case DirectionalLight d:
            {
                Edit<Vector3> direction = w.InputVector3("Direction", d.Direction.Value, speed: 0.02f);
                Edit<Color> color = w.ColorEdit(state.Colors, "Color", Rgb(d.Color));
                Edit<float> intensity = w.InputFloat("Intensity", d.Intensity.Value,
                    speed: 0.05f, min: 0f, max: 100f);

                if (direction.Changed || color.Changed || intensity.Changed)
                {
                    // A direction has to be a unit vector and the field has no idea: scrubbing one
                    // component through the origin produces a triple that cannot be normalised, so
                    // a rejected one leaves the light pointing where it was.
                    Direction next = Direction.Create(direction.Value)
                        .Match(ok: x => x, error: _ => d.Direction);

                    dispatch(new UiMsg.UpdateLight(index, new DirectionalLight(
                        next,
                        Color01.UnsafeFrom(Rgb(color.Value)),
                        Intensity.UnsafeFrom(intensity.Value))));
                }

                break;
            }
        }

        w.Separator();
        if (w.ToolButton("Remove").Clicked)
            dispatch(new UiMsg.RemoveLight(index));
    }

    // ---- Material asset --------------------------------------------------------

    private static void DrawMaterial(WidgetKit w, WidgetState state, Guid guid,
        AssetLibrary library, Action<AppUiMsg> dispatchApp)
    {
        if (library.Find(guid) is not MaterialAssetEntry m)
        {
            Hint(w, "gone", "Selected material asset no longer exists.");
            return;
        }

        Header(w, "MATERIAL", m.Name);
        Path(w, m.ProjectRelativePath);

        MaterialParamsDoc p = m.Params;
        Edit<Color> albedo = w.ColorEdit(state.Colors, "Albedo", Rgb(p.Albedo));
        Edit<float> specular = w.InputFloat("Specular", p.SpecularStrength,
            speed: 0.005f, min: 0f, max: 1f);
        Edit<float> shininess = w.InputFloat("Shininess", p.Shininess,
            speed: 1f, min: 1f, max: MaterialParams.ShininessRange);

        // "(none)" is an item rather than an empty combo, because clearing the texture is
        // something the user does, and an untextured material is something they should be able
        // to see that they have.
        AssetEntry[] textures =
        [
            .. library.EntriesOfKind(AssetKind.Texture)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        ];
        string[] items = ["(none)", .. textures.Select(e => e.Name)];

        Edit<int> texture = w.Combo(state.Popups, "Albedo Tex", CurrentTexture(m, textures), items);

        if (albedo.Changed || specular.Changed || shininess.Changed || texture.Changed)
        {
            RenderLab.Functional.Optional<AssetRef> next = texture.Changed
                ? Chosen(textures, texture.Value)
                : m.AlbedoTex;

            Vector3 rgb = Rgb(albedo.Value);
            dispatchApp(new AppUiMsg.RequestUpdateMaterial(
                Guid: m.Guid,
                Albedo: [rgb.X, rgb.Y, rgb.Z],
                SpecularStrength: specular.Value,
                Shininess: shininess.Value,
                AlbedoTexGuid: next.Match<Guid?>(some: x => x.Guid, none: () => null),
                AlbedoTexSub: next.Match<string?>(some: x => x.Sub, none: () => null)));
        }
    }

    /// <summary>
    /// Which item in the texture combo the material is pointing at: zero for "(none)", and zero
    /// also for a reference this control cannot express - a sub-texture inside an atlas has a
    /// name the list does not carry, and a combo that showed one of its siblings instead would be
    /// saying the material holds something it does not.
    /// </summary>
    private static int CurrentTexture(MaterialAssetEntry m, AssetEntry[] textures) =>
        m.AlbedoTex.Match(
            some: reference => string.IsNullOrEmpty(reference.Sub)
                ? Array.FindIndex(textures, e => e.Guid == reference.Guid) + 1
                : 0,
            none: () => 0);

    private static RenderLab.Functional.Optional<AssetRef> Chosen(AssetEntry[] textures, int item) =>
        item <= 0 || item > textures.Length
            ? RenderLab.Functional.Optional<AssetRef>.None
            : RenderLab.Functional.Optional<AssetRef>.Some(new AssetRef(textures[item - 1].Guid));

    // ---- Mesh and texture entries ----------------------------------------------

    private static void DrawAsset(WidgetKit w, Guid guid, AssetLibrary library,
        Action<AppUiMsg> dispatchApp)
    {
        AssetEntry? entry = library.Find(guid);
        if (entry is null)
        {
            Hint(w, "gone", "Selected asset no longer exists.");
            return;
        }

        Header(w, entry.Kind.ToString().ToUpperInvariant(), entry.Name);

        switch (entry)
        {
            case FileAssetEntry f:
                Path(w, f.ProjectRelativePath);
                DrawImportSettings(w, f, dispatchApp);
                break;
            case ProceduralAssetEntry p:
                Readout(w, "Generator", p.Generator);
                Path(w, p.ProjectRelativePath);
                break;
        }
    }

    private static void DrawImportSettings(WidgetKit w, FileAssetEntry f,
        Action<AppUiMsg> dispatchApp)
    {
        w.SectionLabel("IMPORT");

        switch (f.Import)
        {
            case MeshImportSettings ms:
            {
                Edit<float> scale = w.InputFloat("Scale", ms.Scale,
                    speed: 0.01f, min: 0.001f, max: 100f);
                if (scale.Changed)
                    dispatchApp(new AppUiMsg.RequestUpdateMeshImport(f.Guid, scale.Value));

                Hint(w, "import_where", Sidecar);
                break;
            }

            case TextureImportSettings ts:
            {
                Edit<bool> srgb = w.Checkbox("sRGB", ts.SRgb);
                Edit<bool> mips = w.Checkbox("Generate mips", ts.Mips);
                if (srgb.Changed || mips.Changed)
                    dispatchApp(new AppUiMsg.RequestUpdateTextureImport(f.Guid, srgb.Value, mips.Value));

                Hint(w, "import_where", Sidecar);
                break;
            }

            default:
                Hint(w, "import_none", "No import settings for this kind.");
                break;
        }
    }

    private const string Sidecar = "Stored in the .meta sidecar, applied on the next scene reload.";

    // ---- Camera ----------------------------------------------------------------

    private static void DrawCamera(WidgetKit w, WidgetState state, UiModel model,
        Action<UiMsg> dispatch)
    {
        Header(w, "CAMERA");

        FreeCameraState camera = model.Camera;
        Edit<Vector3> position = w.InputVector3("Position", camera.Position, speed: 0.05f);
        Edit<float> yaw = w.InputFloat("Yaw (deg)", camera.Yaw * DegPerRad,
            speed: 0.5f, format: "0.0");
        Edit<float> pitch = w.InputFloat("Pitch (deg)", camera.Pitch * DegPerRad,
            speed: 0.5f, min: -89.9f, max: 89.9f, format: "0.0");

        if (position.Changed || yaw.Changed || pitch.Changed)
        {
            dispatch(new UiMsg.UpdateCamera(camera with
            {
                Position = position.Value,
                Yaw = yaw.Value * RadPerDeg,
                Pitch = pitch.Value * RadPerDeg,
            }));
        }

        Edit<int> background = w.Combo(state.Popups, "Background",
            (int)model.Background, BackgroundModes);
        if (background.Changed)
            dispatch(new UiMsg.SetBackground((BackgroundMode)background.Value));

        w.Separator();
        if (w.ToolButton("Reset view").Clicked)
            dispatch(new UiMsg.UpdateCamera(FreeCameraController.CreateDefault()));
    }

    // ---- Environment -----------------------------------------------------------

    private static void DrawEnvironment(WidgetKit w, WidgetState state, UiModel model,
        Action<UiMsg> dispatch)
    {
        Header(w, "ENVIRONMENT");

        Edit<Color> sky = w.ColorEdit(state.Colors, "Sky", Rgb(model.Ambient.Sky));
        Edit<Color> ground = w.ColorEdit(state.Colors, "Ground", Rgb(model.Ambient.Ground));
        if (sky.Changed || ground.Changed)
        {
            dispatch(new UiMsg.UpdateAmbient(new HemisphericAmbient(
                Color01.UnsafeFrom(Rgb(sky.Value)),
                Color01.UnsafeFrom(Rgb(ground.Value)))));
        }

        Edit<Color> clear = w.ColorEdit(state.Colors, "Clear color", Rgb(model.ClearColor));
        if (clear.Changed)
            dispatch(new UiMsg.SetClearColor(Rgb(clear.Value)));
    }

    // ---- The shapes every section is built out of ------------------------------

    /// <summary>A section, and the name of the thing being inspected under it.</summary>
    private static void Header(WidgetKit w, string kind, string name)
    {
        w.SectionLabel(kind);
        using (w.Ui.TextColor(w.Theme.Bright))
            w.DataRow($"name_{kind}", name);

        w.Separator();
    }

    /// <summary>A section with nothing to name: the camera and the environment are one of each.</summary>
    private static void Header(WidgetKit w, string kind)
    {
        w.SectionLabel(kind);
        w.Separator();
    }

    /// <summary>
    /// A line of guidance, wrapped to the panel. Muted, because none of it is a value, and
    /// wrapped rather than clipped, because the inspector is the narrowest column in the tool
    /// and a sentence that runs off the edge of it says nothing at all.
    /// </summary>
    private static void Hint(WidgetKit w, string key, string text)
    {
        w.Ui.Spacer(UISize.Pixels(w.Theme.Gap));
        using (w.Ui.TextColor(w.Theme.Muted))
            w.TextWrapped($"hint_{key}", text);
    }

    /// <summary>
    /// A project-relative path. It is one word as far as wrapping is concerned, which is exactly
    /// the case <c>TextWrapped</c> breaks inside a word for.
    /// </summary>
    private static void Path(WidgetKit w, string path)
    {
        using (w.Ui.TextColor(w.Theme.Muted))
            w.TextWrapped("path", path);
    }

    /// <summary>A label and a value that is not editable, in the columns a field row uses.</summary>
    private static void Readout(WidgetKit w, string label, string value)
    {
        using (w.FieldRow(label))
        using (w.Ui.Size(UISize.Text(), UISize.Text()))
            w.DataRow($"value_{label}", value);
    }

    private static string[] Names<T>(T[] assets, Func<T, string> name) => [.. assets.Select(name)];

    // ---- Between the model's colours and the interface's -----------------------

    /// <summary>
    /// Ptah's colour from a linear RGB triple, opaque. The alpha belongs to the framework rather
    /// than to the scene - a light has no transparency - so it is set here and never read back.
    /// </summary>
    private static Color Rgb(Vector3 rgb) => new(rgb.X, rgb.Y, rgb.Z);

    /// <summary>The same, from the loose float array a material document stores.</summary>
    private static Color Rgb(float[] rgb) => new(
        rgb.Length > 0 ? rgb[0] : 0f,
        rgb.Length > 1 ? rgb[1] : 0f,
        rgb.Length > 2 ? rgb[2] : 0f);

    private static Vector3 Rgb(Color color) => new(color.R, color.G, color.B);

    // ---- Between a quaternion and the angles a person can type -----------------

    /// <summary>
    /// A rotation as pitch, yaw and roll in degrees. The editor presents Euler angles because
    /// nobody types a quaternion, and <see cref="Transform"/> persists the quaternion because
    /// Euler angles gimbal-lock and do not interpolate.
    /// </summary>
    private static Vector3 EulerDeg(Quaternion q)
    {
        float sinrCosp = 2f * ((q.W * q.X) + (q.Y * q.Z));
        float cosrCosp = 1f - (2f * ((q.X * q.X) + (q.Y * q.Y)));
        float roll = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2f * ((q.W * q.Y) - (q.Z * q.X));
        float pitch = MathF.Abs(sinp) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinp)
            : MathF.Asin(sinp);

        float sinyCosp = 2f * ((q.W * q.Z) + (q.X * q.Y));
        float cosyCosp = 1f - (2f * ((q.Y * q.Y) + (q.Z * q.Z)));
        float yaw = MathF.Atan2(sinyCosp, cosyCosp);

        return new Vector3(pitch, yaw, roll) * DegPerRad;
    }

    private static Quaternion Quat(Vector3 eulerDeg)
    {
        Vector3 r = eulerDeg * RadPerDeg;
        return Quaternion.CreateFromYawPitchRoll(r.Y, r.X, r.Z);
    }
}
