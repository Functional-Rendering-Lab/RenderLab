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
            HiddenPanels: ["GpuTimings", "RenderGraph"],
            Theme: "Neutral");

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
        Assert.Equal(original.Theme, roundTripped.Theme);
    }

    [Fact]
    public void Settings_written_before_the_theme_existed_still_load()
    {
        // The file on a user's disk today has no theme in it, and a launch that threw over that
        // would be a launch broken by a preference. The absent name reads as no preference.
        const string legacy = """
            {
              "version": 1,
              "lastProjectPath": "D:/repos/my-project",
              "lastScenePath": "scenes/main.scene.json",
              "hiddenPanels": ["GpuTimings"]
            }
            """;

        var settings = JsonSerializer.Deserialize<EditorSettings>(legacy, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.NotNull(settings);
        Assert.Equal(["GpuTimings"], settings!.HiddenPanels);
        Assert.True(string.IsNullOrEmpty(settings.Theme));
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
        Assert.Equal("", d.Theme);
    }
}
