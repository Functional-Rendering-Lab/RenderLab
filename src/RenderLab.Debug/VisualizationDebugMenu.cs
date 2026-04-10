using ImGuiNET;

namespace RenderLab.Debug;

public static class VisualizationDebugMenu
{
    private static readonly string[] ModeNames =
        Enum.GetNames<VisualizationMode>();

    public static VisualizationMode Draw(VisualizationMode current)
    {
        int index = (int)current;
        ImGui.Combo("Buffer", ref index, ModeNames, ModeNames.Length);
        return (VisualizationMode)index;
    }
}
