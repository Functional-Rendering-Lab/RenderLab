using System.Collections.Immutable;
using Ptah.Widgets;
using RenderLab.Graph;

namespace RenderLab.Editor;

/// <summary>
/// What the graph compiler decided this frame: the passes in the order they will run, what each
/// one reads and writes, and the barriers inserted in front of it. Read-only, like GPU Timings -
/// the graph is compiled from the pipeline's declarations, so there is nothing here to edit.
/// <para>
/// The ImGui version painted its three headings blue, green and orange. The words under them said
/// the same thing, and a colour spent on decoration is a colour that no longer means anything
/// when something actually needs to be found: this theme spends its accent on selection. So the
/// headings are headings, which is what <see cref="WidgetKit.SectionLabel"/> is for.
/// </para>
/// </summary>
internal static class RenderGraphPanel
{
    internal static void Draw(WidgetKit w, WidgetState state, ImmutableArray<ResolvedPass> passes)
    {
        if (passes.IsEmpty)
        {
            w.DataRow("empty", "No graph. This pipeline records its passes by hand.");
            return;
        }

        for (int i = 0; i < passes.Length; i++)
        {
            ResolvedPass pass = passes[i];
            RenderPassDeclaration decl = pass.Declaration;

            // The index is in the label because the order is the point - a render graph's whole
            // output is a sequence - and out of the id, because a pass keeps its expanded state
            // when a pipeline change renumbers it.
            if (!w.TreeNode(state.Trees, $"pass_{decl.Name}", $"{i}: {decl.Name}", defaultOpen: true).Open)
                continue;

            using (w.Indent())
            {
                if (!decl.Inputs.IsEmpty)
                {
                    w.SectionLabel("INPUTS");
                    foreach (PassInput input in decl.Inputs)
                        w.DataRow($"in_{decl.Name}_{input.Resource.Name}",
                            $"{input.Resource.Name} ({input.Usage})");
                }

                if (!decl.Outputs.IsEmpty)
                {
                    w.SectionLabel("OUTPUTS");
                    foreach (PassOutput output in decl.Outputs)
                        w.DataRow($"out_{decl.Name}_{output.Resource.Name}",
                            $"{output.Resource.Name} ({output.Usage})");
                }

                if (!pass.BarriersBefore.IsEmpty)
                {
                    w.SectionLabel("BARRIERS");
                    foreach (BarrierDesc barrier in pass.BarriersBefore)
                        w.DataRow($"bar_{decl.Name}_{barrier.Resource.Name}",
                            $"{barrier.Resource.Name} {barrier.FromUsage} → {barrier.ToUsage}");
                }
            }
        }
    }
}
