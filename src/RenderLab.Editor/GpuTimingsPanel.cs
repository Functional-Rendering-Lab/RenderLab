using Ptah;
using Ptah.Widgets;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// What the last frame cost: the GPU's time per pass, the CPU's time for the whole frame, and
/// what the interface itself came to. Read-only - the panel emits no messages, because there is
/// nothing here to edit.
/// </summary>
internal static class GpuTimingsPanel
{
    internal static void Draw(WidgetKit w, FrameStats stats, UiCost cost)
    {
        w.SectionLabel("PASSES");

        double total = 0;
        for (int i = 0; i < stats.TimestampMillis.Count; i++)
        {
            total += stats.TimestampMillis[i];

            // Two passes are allowed to be called the same thing; two rows are not allowed to
            // have the same key. `##` is the convention for exactly that: what follows it names
            // the row and is not shown.
            Readout(w, $"{stats.TimestampLabels[i]}##{i}", $"{stats.TimestampMillis[i]:F3} ms");
        }

        w.Separator();
        Readout(w, "Total GPU", $"{total:F3} ms");

        w.SectionLabel("FRAME");
        float dt = stats.DeltaSeconds;
        Readout(w, "CPU", $"{dt * 1000f:F1} ms");
        Readout(w, "Rate", $"{(dt > 0f ? 1f / dt : 0f):F0} fps");

        // The interface's own cost, in the panel that already exists to show what a frame cost.
        // A display list is planned before it is recorded, so the two numbers are the whole of
        // what the UI asks of the GPU: how much was described, and how few calls it took.
        w.SectionLabel("INTERFACE");
        Readout(w, "Commands", cost.Commands.ToString());
        Readout(w, "Draw calls", cost.DrawCalls.ToString());
    }

    /// <summary>
    /// A label and a number, laid out in the same two columns a form row uses, so a readout and
    /// an editable field line up when they end up in the same panel.
    /// </summary>
    private static void Readout(WidgetKit w, string label, string value)
    {
        using (w.FieldRow(label))
        using (w.Ui.Size(UISize.Text(), UISize.Text()))
            w.DataRow($"value_{label}", value);
    }
}
