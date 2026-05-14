using System.Numerics;
using ImGuiNET;
using RenderLab.Assets;
using RenderLab.Project;
using RenderLab.Scene;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Single editor surface for whichever item <see cref="UiModel.Selection"/> points at.
/// Outliner / browser panels (Scene, Asset Browser, Lighting) emit
/// <see cref="UiMsg.Select"/>; the Inspector switches on the selection and renders
/// the appropriate fields. There are no duplicate inline editors anywhere else.
/// </summary>
public static class InspectorPanel
{
    public static void Draw(UiModel model, IAssetCatalog catalog, AssetLibrary library,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        ImGui.SetNextWindowPos(new Vector2(1010, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360, 520), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Inspector"))
        {
            ImGui.End();
            return;
        }

        switch (model.Selection)
        {
            case Selection.None:
                ImGui.TextDisabled("Nothing selected.");
                ImGui.TextDisabled("Pick an item from Scene, Asset Browser, or Lighting.");
                break;
            case Selection.Drawable d:
                DrawDrawable(model, d.LocalId, catalog, dispatch);
                break;
            case Selection.Light l:
                DrawLight(model, l.Index, dispatch);
                break;
            case Selection.MaterialAsset m:
                DrawMaterialAsset(m.Guid, library, dispatchApp);
                break;
            case Selection.MeshAsset me:
                DrawAssetEntry(me.Guid, library, dispatchApp);
                break;
            case Selection.TextureAsset t:
                DrawAssetEntry(t.Guid, library, dispatchApp);
                break;
            case Selection.Environment:
                DrawEnvironment(model, dispatch);
                break;
            case Selection.Camera:
                DrawCamera(model, dispatch);
                break;
        }

        ImGui.End();
    }

    // ─── Drawable ──────────────────────────────────────────────────────

    private static void DrawDrawable(UiModel model, Guid id, IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        var drawable = model.Drawables.FirstOrDefault(d => d.LocalId == id);
        if (drawable is null)
        {
            ImGui.TextDisabled("Selected drawable no longer exists.");
            return;
        }

        ImGui.SeparatorText($"Drawable — {drawable.Name}");

        var t = drawable.Transform;
        var position = DebugFields.DragVector3("Position", t.Position, 0.05f);
        var eulerCurrent = QuatToEulerDeg(t.Rotation);
        var eulerNext = DebugFields.DragVector3("Rotation (deg)", eulerCurrent, 1f);
        var scale = DebugFields.DragFloat("Scale", t.Scale, 0.02f, 0.1f, 5f);

        var rotation = eulerNext == eulerCurrent ? t.Rotation : EulerDegToQuat(eulerNext);
        var nextTransform = t with { Position = position, Rotation = rotation, Scale = scale };
        if (!nextTransform.Equals(t))
            dispatch(new UiMsg.SetDrawableTransform(id, nextTransform));

        var meshes = catalog.AllMeshes.ToArray();
        var meshSel = drawable.Mesh;
        MeshCombo("Mesh", meshes, ref meshSel);
        if (meshSel != drawable.Mesh)
            dispatch(new UiMsg.SetDrawableMesh(id, meshSel));

        var materials = catalog.AllMaterials.ToArray();
        var matSel = drawable.Material;
        MaterialCombo("Material", materials, ref matSel);
        if (matSel != drawable.Material)
            dispatch(new UiMsg.SetDrawableMaterial(id, matSel));

        ImGui.TextDisabled("Edit material params by selecting the material asset.");
    }

    // ─── Light ─────────────────────────────────────────────────────────

    private static void DrawLight(UiModel model, int index, Action<UiMsg> dispatch)
    {
        if (index < 0 || index >= model.Lights.Length)
        {
            ImGui.TextDisabled("Selected light no longer exists.");
            return;
        }

        var light = model.Lights[index];
        var kind = light switch
        {
            PointLight       => "point",
            DirectionalLight => "directional",
            _                => "?",
        };
        ImGui.SeparatorText($"Light {index} — {kind}");

        switch (light)
        {
            case PointLight p:
            {
                var position  = DebugFields.DragVector3("Position", p.Position, 0.05f);
                var color     = DebugFields.ColorEdit("Color", p.Color);
                var intensity = DebugFields.DragFloat("Intensity", p.Intensity.Value, 0.05f, 0f, 100f);
                var next = new PointLight(position, color, Intensity.Of(MathF.Max(intensity, 0f)));
                if (!next.Equals(p)) dispatch(new UiMsg.UpdateLight(index, next));
                break;
            }
            case DirectionalLight d:
            {
                var rawDir    = DebugFields.DragVector3("Direction", d.Direction.Value, 0.02f);
                var color     = DebugFields.ColorEdit("Color", d.Color);
                var intensity = DebugFields.DragFloat("Intensity", d.Intensity.Value, 0.05f, 0f, 100f);
                Direction nextDir;
                try { nextDir = Direction.Create(rawDir); }
                catch (ArgumentException) { nextDir = d.Direction; }
                var next = new DirectionalLight(nextDir, color, Intensity.Of(MathF.Max(intensity, 0f)));
                if (!next.Equals(d)) dispatch(new UiMsg.UpdateLight(index, next));
                break;
            }
        }

        if (ImGui.Button("Remove"))
            dispatch(new UiMsg.RemoveLight(index));
    }

    // ─── Material asset ────────────────────────────────────────────────

    private static void DrawMaterialAsset(Guid guid, AssetLibrary library, Action<AppUiMsg> dispatchApp)
    {
        if (library.Find(guid) is not MaterialAssetEntry m)
        {
            ImGui.TextDisabled("Selected material asset no longer exists.");
            return;
        }
        ImGui.SeparatorText($"Material — {m.Name}");
        ImGui.TextDisabled(m.ProjectRelativePath);
        ImGui.Separator();

        var p = m.Params;
        var albedo = new Vector3(
            p.Albedo.Length > 0 ? p.Albedo[0] : 0f,
            p.Albedo.Length > 1 ? p.Albedo[1] : 0f,
            p.Albedo.Length > 2 ? p.Albedo[2] : 0f);

        var nextAlbedo = DebugFields.ColorEdit("Albedo", albedo);
        bool albedoChanged = nextAlbedo != albedo;

        var nextSpec = DebugFields.DragFloat("Spec Strength", p.SpecularStrength, 0.005f, 0f, 1f);
        bool specChanged = !FloatsEqual(nextSpec, p.SpecularStrength);

        var nextShininess = DebugFields.DragFloat("Shininess", p.Shininess, 1f, 1f, MaterialParams.ShininessRange);
        bool shinChanged = !FloatsEqual(nextShininess, p.Shininess);

        var textures = library.EntriesOfKind(AssetKind.Texture)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var items = new string[textures.Length + 1];
        items[0] = "(none)";
        for (int i = 0; i < textures.Length; i++) items[i + 1] = textures[i].Name;

        int selected = 0;
        if (m.AlbedoTex is AssetRef cur && string.IsNullOrEmpty(cur.Sub))
        {
            for (int i = 0; i < textures.Length; i++)
                if (textures[i].Guid == cur.Guid) { selected = i + 1; break; }
        }
        int nextSelected = DebugFields.ComboEdit("Albedo Tex", selected, items);
        bool texChanged = nextSelected != selected;

        AssetRef? nextTex = m.AlbedoTex;
        if (texChanged)
            nextTex = nextSelected == 0 ? null : new AssetRef(textures[nextSelected - 1].Guid);

        if (albedoChanged || specChanged || shinChanged || texChanged)
        {
            dispatchApp(new AppUiMsg.RequestUpdateMaterial(
                Guid: m.Guid,
                Albedo: new[] { nextAlbedo.X, nextAlbedo.Y, nextAlbedo.Z },
                SpecularStrength: nextSpec,
                Shininess: nextShininess,
                AlbedoTexGuid: nextTex?.Guid,
                AlbedoTexSub: nextTex?.Sub));
        }
    }

    // ─── Read-only asset entries (mesh / texture) ──────────────────────

    private static void DrawAssetEntry(Guid guid, AssetLibrary library, Action<AppUiMsg> dispatchApp)
    {
        var e = library.Find(guid);
        if (e is null)
        {
            ImGui.TextDisabled("Selected asset no longer exists.");
            return;
        }
        ImGui.SeparatorText($"{e.Kind} — {e.Name}");
        switch (e)
        {
            case FileAssetEntry f:
                ImGui.TextWrapped(f.ProjectRelativePath);
                ImGui.Separator();
                DrawImportSettings(f, dispatchApp);
                break;
            case ProceduralAssetEntry p:
                ImGui.Text($"Generator: {p.Generator}");
                ImGui.TextWrapped(p.ProjectRelativePath);
                break;
        }
    }

    private static void DrawImportSettings(FileAssetEntry f, Action<AppUiMsg> dispatchApp)
    {
        ImGui.SeparatorText("Import settings");
        ImGui.TextDisabled("Stored in the .meta sidecar.");
        switch (f.Import)
        {
            case MeshImportSettings ms:
            {
                var scale = DebugFields.DragFloat("Scale", ms.Scale, 0.01f, 0.001f, 100f);
                if (!FloatsEqual(scale, ms.Scale))
                    dispatchApp(new AppUiMsg.RequestUpdateMeshImport(f.Guid, scale));
                ImGui.TextDisabled("(Applied on next scene reload.)");
                break;
            }
            case TextureImportSettings ts:
            {
                bool sRgb = ts.SRgb;
                bool mips = ts.Mips;
                bool changed = false;
                if (ImGui.Checkbox("sRGB", ref sRgb)) changed = true;
                if (ImGui.Checkbox("Generate mips", ref mips)) changed = true;
                if (changed)
                    dispatchApp(new AppUiMsg.RequestUpdateTextureImport(f.Guid, sRgb, mips));
                ImGui.TextDisabled("(Applied on next scene reload.)");
                break;
            }
            default:
                ImGui.TextDisabled("No import settings for this kind.");
                break;
        }
    }

    // ─── Camera ────────────────────────────────────────────────────────

    private const float DegPerRad = 180f / MathF.PI;
    private const float RadPerDeg = MathF.PI / 180f;

    private static void DrawCamera(UiModel model, Action<UiMsg> dispatch)
    {
        var state = model.Camera;
        ImGui.SeparatorText("Camera");

        var position = DebugFields.DragVector3("Position", state.Position, 0.05f);
        var yawDeg   = DebugFields.DragFloat("Yaw",   state.Yaw * DegPerRad, 0.5f, format: "%.1f deg");
        var pitchDeg = DebugFields.DragFloat("Pitch", state.Pitch * DegPerRad, 0.5f, -89.9f, 89.9f, "%.1f deg");

        ImGui.Separator();
        if (ImGui.Button("Reset"))
        {
            dispatch(new UiMsg.UpdateCamera(FreeCameraController.CreateDefault()));
            return;
        }

        var next = state with
        {
            Position = position,
            Yaw = yawDeg * RadPerDeg,
            Pitch = pitchDeg * RadPerDeg,
        };
        if (!next.Equals(state))
            dispatch(new UiMsg.UpdateCamera(next));

        var bg = (BackgroundMode)DebugFields.ComboEdit("Background", (int)model.Background, BackgroundModeNames);
        if (bg != model.Background) dispatch(new UiMsg.SetBackground(bg));
    }

    // ─── Environment (global lighting) ─────────────────────────────────

    private static readonly string[] BackgroundModeNames =
    {
        "Solid color",
        "Ambient gradient",
    };

    private static void DrawEnvironment(UiModel model, Action<UiMsg> dispatch)
    {
        ImGui.SeparatorText("Environment");

        var sky    = DebugFields.ColorEdit("Sky",    model.Ambient.Sky);
        var ground = DebugFields.ColorEdit("Ground", model.Ambient.Ground);
        if (sky != model.Ambient.Sky || ground != model.Ambient.Ground)
            dispatch(new UiMsg.UpdateAmbient(new HemisphericAmbient(sky, ground)));

        var clear = DebugFields.ColorEdit("Clear color", model.ClearColor);
        if (clear != model.ClearColor) dispatch(new UiMsg.SetClearColor(clear));
    }

    // ─── Shared combos / helpers ───────────────────────────────────────

    internal static void MeshCombo(string label, MeshAsset[] meshes, ref MeshId selected)
    {
        var current = selected;
        var currentName = meshes.FirstOrDefault(m => m.Id == current)?.Name ?? "(none)";
        if (!ImGui.BeginCombo(label, currentName)) return;
        foreach (var m in meshes)
        {
            var isSelected = m.Id == current;
            if (ImGui.Selectable($"{m.Name}##mesh{m.Id.Value}", isSelected))
                selected = m.Id;
            if (isSelected) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    internal static void MaterialCombo(string label, MaterialAsset[] materials, ref MaterialId selected)
    {
        var current = selected;
        var currentName = materials.FirstOrDefault(m => m.Id == current)?.Name ?? "(none)";
        if (!ImGui.BeginCombo(label, currentName)) return;
        foreach (var m in materials)
        {
            var isSelected = m.Id == current;
            if (ImGui.Selectable($"{m.Name}##mat{m.Id.Value}", isSelected))
                selected = m.Id;
            if (isSelected) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private static bool FloatsEqual(float a, float b) => MathF.Abs(a - b) <= 1e-6f;

    private static Vector3 QuatToEulerDeg(Quaternion q)
    {
        float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
        float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);

        float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
        float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(sinyCosp, cosyCosp);

        return new Vector3(pitch, yaw, roll) * (180f / MathF.PI);
    }

    private static Quaternion EulerDegToQuat(Vector3 eulerDeg)
    {
        var r = eulerDeg * (MathF.PI / 180f);
        return Quaternion.CreateFromYawPitchRoll(r.Y, r.X, r.Z);
    }
}
