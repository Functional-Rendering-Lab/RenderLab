using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;
using Scene = RenderLab.Scene.Scene;

/// <summary>
/// Read-only inspector for the per-frame <see cref="Scene"/> snapshot.
/// Lists camera, meshes, and lights. No edits — interactivity is intentionally
/// deferred until after M4.
/// </summary>
public static class ScenePanel
{
    public static void Draw(Scene scene)
    {
        ImGui.SetNextWindowPos(new Vector2(640, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 300), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Scene"))
        {
            ImGui.End();
            return;
        }

        if (ImGui.TreeNodeEx("Camera", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var c = scene.Camera;
            ImGui.BulletText($"pos    ({c.Position.X:F2}, {c.Position.Y:F2}, {c.Position.Z:F2})");
            ImGui.BulletText($"target ({c.Target.X:F2}, {c.Target.Y:F2}, {c.Target.Z:F2})");
            ImGui.BulletText($"fov    {c.FovRadians * 180f / MathF.PI:F1}°");
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx($"Meshes ({scene.Meshes.Length})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "Drawables:");
            foreach (var m in scene.Meshes)
            {
                var p = m.Transform.Position;
                ImGui.BulletText($"{m.Name}  verts={m.Data.Vertices.Length}  pos=({p.X:F2},{p.Y:F2},{p.Z:F2})  scale={m.Transform.Scale:F2}");
            }
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx($"Lights ({scene.Lights.Length})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.5f, 1.0f), "Point lights:");
            for (int i = 0; i < scene.Lights.Length; i++)
            {
                var l = scene.Lights[i];
                ImGui.BulletText($"[{i}] pos=({l.Position.X:F2},{l.Position.Y:F2},{l.Position.Z:F2})  intensity={l.Intensity:F2}");
            }
            ImGui.TreePop();
        }

        ImGui.End();
    }
}
