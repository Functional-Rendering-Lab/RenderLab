using System.Collections.Immutable;
using RenderLab.Graph;

namespace RenderLab.Ui;

/// <summary>
/// Per-frame information the view displays but does not own. Keeps <see cref="UiModel"/>
/// focused on editable state — transient measurements (frame time, GPU timings, compiled
/// render graph) flow through here so the view can render read-only panels.
/// </summary>
public readonly record struct FrameStats(
    float DeltaSeconds,
    IReadOnlyList<string> TimestampLabels,
    IReadOnlyList<double> TimestampMillis,
    ImmutableArray<ResolvedPass> ResolvedPasses);
