using System.Text.Json;
using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class EditorSettingsTests
{
    [Fact]
    public void Settings_round_trip_through_JSON_unchanged()
    {
        var original = new EditorSettings(
            Version: 1,
            LastProjectPath: @"C:\repos\my-project",
            LastScenePath: "scenes/main.scene.json",
            HiddenPanels: ["GpuTimings", "RenderGraph"]);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var roundTripped = JsonSerializer.Deserialize<EditorSettings>(bytes, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Version, roundTripped!.Version);
        Assert.Equal(original.LastProjectPath, roundTripped.LastProjectPath);
        Assert.Equal(original.LastScenePath, roundTripped.LastScenePath);
        Assert.Equal(original.HiddenPanels, roundTripped.HiddenPanels);
    }

    [Fact]
    public void ReadOrDefault_returns_default_when_file_absent()
    {
        // The real settings path may not exist in CI; ReadOrDefault must not
        // throw — defaults are the documented fallback.
        var settings = EditorSettingsIO.ReadOrDefault();
        Assert.NotNull(settings);
        Assert.Equal(1, settings.Version);
    }

    [Fact]
    public void Default_is_safe_seed_for_a_clean_install()
    {
        var d = EditorSettings.Default;
        Assert.Equal(1, d.Version);
        Assert.Equal("", d.LastProjectPath);
        Assert.Equal("", d.LastScenePath);
        Assert.Empty(d.HiddenPanels);
    }
}
