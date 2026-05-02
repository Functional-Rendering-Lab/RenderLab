using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class PathSandboxTests
{
    [Fact]
    public void ResolveProjectPath_accepts_relative_inside_root()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            var ok = ProjectIO.ResolveProjectPath(temp, "scenes/main.scene.json");
            Assert.True(ok.IsOk);
            var resolved = ok.Match(ok: p => p, error: _ => "");
            Assert.StartsWith(temp, resolved, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ResolveProjectPath_rejects_dot_dot_escape()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            var bad = ProjectIO.ResolveProjectPath(temp, "../../etc/passwd");
            Assert.True(bad.IsError);
            Assert.IsType<ProjectError.PathEscape>(bad.Match<ProjectError?>(_ => null, e => e));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ResolveProjectPath_rejects_absolute_outside_root()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            // An absolute path like /tmp/foo or C:\Windows is treated as escaping
            // the project root (Path.Combine ignores the relative segment when
            // the second arg is absolute, but the resulting GetFullPath won't
            // start with the project root).
            var outside = OperatingSystem.IsWindows() ? "C:\\Windows\\System32" : "/etc/passwd";
            var bad = ProjectIO.ResolveProjectPath(temp, outside);
            Assert.True(bad.IsError);
            Assert.IsType<ProjectError.PathEscape>(bad.Match<ProjectError?>(_ => null, e => e));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ResolveProjectPath_accepts_nested_subdirs()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            var ok = ProjectIO.ResolveProjectPath(temp, "scenes/sub/inner.scene.json");
            Assert.True(ok.IsOk);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
