using System.Numerics;
using ImGuiNET;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

internal static class ImGuiTheme
{
    internal static unsafe void LoadFont(ImGuiIOPtr io, float size = 16f)
    {
        var assembly = typeof(ImGuiTheme).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "RenderLab.Ui.ImGui.Fonts.Inter-Regular.ttf")!;

        var buf = new byte[stream.Length];
        stream.ReadExactly(buf);

        fixed (byte* ptr = buf)
            io.Fonts.AddFontFromMemoryTTF((nint)ptr, buf.Length, size);

        io.FontGlobalScale = 1f;
    }

    internal static void Apply()
    {
        ImGui.StyleColorsDark();

        var style = ImGui.GetStyle();
        style.WindowRounding = 4f;
        style.FrameRounding  = 3f;
        style.GrabRounding   = 3f;
        style.TabRounding    = 3f;
        style.ItemSpacing    = new Vector2(8, 5);
        style.FramePadding   = new Vector2(6, 3);

        var colors = style.Colors;
        colors[(int)ImGuiCol.WindowBg]                  = Hex(0x111111FF);
        colors[(int)ImGuiCol.ChildBg]                   = Hex(0x161616FF);
        colors[(int)ImGuiCol.PopupBg]                   = Hex(0x1C1C1CFF);
        colors[(int)ImGuiCol.Border]                    = Hex(0x2C2C2CFF);
        colors[(int)ImGuiCol.FrameBg]                   = Hex(0x1C1C1CFF);
        colors[(int)ImGuiCol.FrameBgHovered]            = Hex(0x262626FF);
        colors[(int)ImGuiCol.FrameBgActive]             = Hex(0x303030FF);
        colors[(int)ImGuiCol.TitleBg]                   = Hex(0x0A0A0AFF);
        colors[(int)ImGuiCol.TitleBgActive]             = Hex(0x111111FF);
        colors[(int)ImGuiCol.MenuBarBg]                 = Hex(0x0A0A0AFF);
        colors[(int)ImGuiCol.ScrollbarBg]               = Hex(0x111111FF);
        colors[(int)ImGuiCol.ScrollbarGrab]             = Hex(0x3A3A3AFF);
        colors[(int)ImGuiCol.Header]                    = Hex(0xD4904AFF);
        colors[(int)ImGuiCol.HeaderHovered]             = Hex(0xDFA060FF);
        colors[(int)ImGuiCol.HeaderActive]              = Hex(0xEAB070FF);
        colors[(int)ImGuiCol.Button]                    = Hex(0xC97B3088);
        colors[(int)ImGuiCol.ButtonHovered]             = Hex(0xC97B30BB);
        colors[(int)ImGuiCol.ButtonActive]              = Hex(0xD4904AFF);
        colors[(int)ImGuiCol.Tab]                       = Hex(0x16161666);
        colors[(int)ImGuiCol.TabHovered]                = Hex(0xC97B30AA);
        colors[(int)ImGuiCol.TabSelected]               = Hex(0xC97B30CC);
        colors[(int)ImGuiCol.TabDimmed]                 = Hex(0x16161666);
        colors[(int)ImGuiCol.TabDimmedSelected]         = Hex(0xC97B3099);
        colors[(int)ImGuiCol.TabSelectedOverline]       = Hex(0xA0602AFF);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = Hex(0xC94C30FF);
        colors[(int)ImGuiCol.DockingPreview]            = Hex(0xC97B3066);
        colors[(int)ImGuiCol.SeparatorHovered]          = Hex(0xC97B30AA);
        colors[(int)ImGuiCol.SeparatorActive]           = Hex(0xD4904AFF);
        colors[(int)ImGuiCol.SliderGrab]                = Hex(0xC97B30FF);
        colors[(int)ImGuiCol.SliderGrabActive]          = Hex(0xD4904AFF);
        colors[(int)ImGuiCol.CheckMark]                 = Hex(0xD4904AFF);
        colors[(int)ImGuiCol.TextSelectedBg]            = Hex(0xC97B3044);
        colors[(int)ImGuiCol.NavCursor]                 = Hex(0xD4904AFF);
    }

    private static Vector4 Hex(uint rgba) => new(
        ((rgba >> 24) & 0xFF) / 255f,
        ((rgba >> 16) & 0xFF) / 255f,
        ((rgba >>  8) & 0xFF) / 255f,
        ( rgba        & 0xFF) / 255f);
}
