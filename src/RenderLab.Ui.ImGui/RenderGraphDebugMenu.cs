using System.Collections.Immutable;
using System.Numerics;
using ImGuiNET;
using RenderLab.Graph;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

public static class RenderGraphDebugMenu
{
    public static void Draw(ImmutableArray<ResolvedPass> resolvedPasses)
    {
        ImGui.SetNextWindowPos(new Vector2(300, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 300), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Render Graph"))
        {
            ImGui.End();
            return;
        }

        for (int i = 0; i < resolvedPasses.Length; i++)
        {
            var pass = resolvedPasses[i];
            var decl = pass.Declaration;

            if (!ImGui.TreeNodeEx($"{i}: {decl.Name}", ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            if (!decl.Inputs.IsEmpty)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Inputs:");
                foreach (var input in decl.Inputs)
                    ImGui.BulletText($"{input.Resource.Name}  ({input.Usage})");
            }

            if (!decl.Outputs.IsEmpty)
            {
                ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "Outputs:");
                foreach (var output in decl.Outputs)
                    ImGui.BulletText($"{output.Resource.Name}  ({output.Usage})");
            }

            if (!pass.BarriersBefore.IsEmpty)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Barriers:");
                foreach (var b in pass.BarriersBefore)
                    ImGui.BulletText($"{b.Resource.Name}  {b.FromUsage} -> {b.ToUsage}");
            }

            ImGui.TreePop();
        }

        ImGui.End();
    }
}
