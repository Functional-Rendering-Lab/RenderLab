using System.Numerics;
using Ptah.Widgets;
using RenderLab.Assets;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The scene outliner: what is in the scene, and what is selected. Clicking a row emits
/// <see cref="UiMsg.Select"/> and the Inspector draws the fields for it. Add, remove and clone
/// stay here because they are operations on the list rather than edits to an item.
/// <para>
/// The rows carry a name and nothing else. The ImGui version put each drawable's position in its
/// row and each light's position and intensity in its, which was a readout of three floats in a
/// column three hundred pixels wide, clipped halfway through and duplicating the panel next door.
/// One place a value is shown is the same rule as one place a value is typed, and the Inspector
/// is that place. What a row does have to say is which kind of thing it is, because a point light
/// and a directional light are not distinguishable from their names - so a light's row says so.
/// </para>
/// <para>
/// Every row in every outliner is a leaf node of a tree rather than a plain selectable row, which
/// is what makes their left margins agree. A selectable's label sits one padding step inside its
/// row and a node's sits where the arrow it does not have would have been, so the two are half a
/// step apart - not enough to read as nesting, and enough to read as ragged.
/// </para>
/// <para>
/// The mesh drop target went with the drag it received. Adding a project mesh to the scene is now
/// "Add to Scene" on the Asset Browser's own context menu, which dispatches the message this panel
/// used to unpack from a payload. The gesture changed; the model never knew there was one.
/// </para>
/// </summary>
internal static class ScenePanel
{
    /// <summary>How far a clone is moved off the original, so the copy is visible as a copy.</summary>
    private static readonly Vector3 CloneOffset = new(1f, 0f, 0f);

    internal static void Draw(WidgetKit w, WidgetState state, UiModel model,
        IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        // The camera and the environment are global state reached through the same
        // Select-to-Inspector pipe as anything in the scene. They are leaf nodes rather than
        // plain rows so their labels line up with the two headings under them instead of sitting
        // where those headings' arrows are.
        Entry(w, state, "camera", "Camera", model.Selection is Selection.Camera,
            () => dispatch(new UiMsg.Select(new Selection.Camera())));

        Entry(w, state, "environment", "Environment", model.Selection is Selection.Environment,
            () => dispatch(new UiMsg.Select(new Selection.Environment())));

        Drawables(w, state, model, catalog, dispatch);
        Lights(w, state, model, dispatch);
    }

    private static void Entry(WidgetKit w, WidgetState state, string id, string label,
        bool selected, Action onClick)
    {
        if (w.TreeNode(state.Trees, id, label, selected: selected, leaf: true).Comm.Clicked)
            onClick();
    }

    // ---- Drawables ---------------------------------------------------------------

    private static void Drawables(WidgetKit w, WidgetState state, UiModel model,
        IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        TreeComm node = w.TreeNode(state.Trees, "drawables",
            $"Drawables ({model.Drawables.Length})", defaultOpen: true);

        if (!node.Open)
            return;

        using (w.Indent())
        {
            Guid? selectedId = model.Selection is Selection.Drawable d ? d.LocalId : null;

            foreach (EditableDrawable drawable in model.Drawables)
            {
                TreeComm row = w.TreeNode(state.Trees, $"drawable_{drawable.LocalId:N}",
                    drawable.Name, selected: selectedId == drawable.LocalId, leaf: true);

                if (row.Comm.Clicked)
                    dispatch(new UiMsg.Select(new Selection.Drawable(drawable.LocalId)));
            }

            // The selection may name a drawable that is no longer in the list - for one frame
            // after a scene reload, or after something else removed it - so what the two buttons
            // act on is looked up rather than assumed, and they are unavailable when it is gone.
            EditableDrawable? selected = selectedId is Guid id
                ? model.Drawables.FirstOrDefault(d => d.LocalId == id)
                : null;

            using (w.ButtonRow("drawable_actions"))
            {
                WidgetKit kit = w.EnabledIf(selected is not null);

                if (kit.ToolButton("+ clone").Clicked && selected is EditableDrawable source)
                {
                    Transform nudged = source.Transform with
                    {
                        Position = source.Transform.Position + CloneOffset,
                    };

                    dispatch(new UiMsg.AddDrawable(
                        $"{source.Name} copy", source.Mesh, nudged, source.Material));
                }

                if (kit.ToolButton("- remove").Clicked && selected is EditableDrawable removed)
                    dispatch(new UiMsg.RemoveDrawable(removed.LocalId));
            }

            AddFromRegistry(w, state, catalog, dispatch);
        }
    }

    /// <summary>
    /// Composes a new drawable out of any registered mesh and material. The registry is what the
    /// renderer has loaded, which is not the same list as the project's assets: a builtin cube
    /// is in one and not the other.
    /// </summary>
    private static void AddFromRegistry(WidgetKit w, WidgetState state, IAssetCatalog catalog,
        Action<UiMsg> dispatch)
    {
        MeshAsset[] meshes = [.. catalog.AllMeshes];
        MaterialAsset[] materials = [.. catalog.AllMaterials];

        if (meshes.Length == 0 || materials.Length == 0)
            return;

        w.SectionLabel("ADD FROM REGISTRY");

        // The two slots hold ids and the combos work in positions, so the position is derived
        // each frame rather than stored. That is what makes an id the registry no longer has -
        // a mesh removed since the picker was last touched - fall back to the first entry
        // instead of showing an empty control that adds nothing.
        //
        // "##add" is what tells these two combos from the Inspector's Mesh and Material rows,
        // which read the same and are on screen at the same time. Box keys are seeded by their
        // parent so the boxes could not collide, but the open-popup key is one string for the
        // whole application, and without this, opening one would drop the other's list open too.
        Edit<int> mesh = w.Combo(state.Popups, "Mesh##add",
            Math.Max(0, Array.FindIndex(meshes, m => m.Id == state.AddMesh)),
            [.. meshes.Select(m => m.Name)]);

        if (mesh.Changed)
            state.AddMesh = meshes[mesh.Value].Id;

        Edit<int> material = w.Combo(state.Popups, "Material##add",
            Math.Max(0, Array.FindIndex(materials, m => m.Id == state.AddMaterial)),
            [.. materials.Select(m => m.Name)]);

        if (material.Changed)
            state.AddMaterial = materials[material.Value].Id;

        using (w.ButtonRow("drawable_add"))
        {
            if (w.ToolButton("+ Add").Clicked)
            {
                dispatch(new UiMsg.AddDrawable(
                    meshes[mesh.Value].Name,
                    meshes[mesh.Value].Id,
                    Transform.Default,
                    materials[material.Value].Id));
            }
        }
    }

    // ---- Lights ------------------------------------------------------------------

    private static void Lights(WidgetKit w, WidgetState state, UiModel model,
        Action<UiMsg> dispatch)
    {
        TreeComm node = w.TreeNode(state.Trees, "lights",
            $"Lights ({model.Lights.Length})", defaultOpen: true);

        if (!node.Open)
            return;

        using (w.Indent())
        {
            for (int i = 0; i < model.Lights.Length; i++)
            {
                bool selected = model.Selection is Selection.Light light && light.Index == i;

                TreeComm row = w.TreeNode(state.Trees, $"light_{i}", Label(i, model.Lights[i]),
                    selected: selected, leaf: true);

                if (row.Comm.Clicked)
                    dispatch(new UiMsg.Select(new Selection.Light(i)));
            }

            using (w.ButtonRow("light_actions"))
            {
                if (w.ToolButton("+ Point").Clicked)
                    dispatch(new UiMsg.AddPointLight());

                if (w.ToolButton("+ Directional").Clicked)
                    dispatch(new UiMsg.AddDirectionalLight());
            }
        }
    }

    /// <summary>
    /// A light's row: its index, because lights are an ordered buffer and the index is what the
    /// selection holds, and its kind, because nothing else about the row would say.
    /// </summary>
    private static string Label(int index, Light light) => light switch
    {
        PointLight => $"[{index}] point",
        DirectionalLight => $"[{index}] directional",
        _ => $"[{index}] light",
    };
}
