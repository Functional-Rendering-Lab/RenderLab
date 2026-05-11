using RenderLab.App;
using RenderLab.Assets;
using RenderLab.Pipelines;
using RenderLab.Project;

var registry = new PipelineRegistry()
    .Register("triangle", () => new TrianglePipeline())
    .Register("gbuffer",  () => new GBufferPipeline())
    .Register("deferred", () => new DeferredPipeline());

var procedural = new DefaultProceduralAssets();

var projectPath = ResolveProject(args);

using var application = new Application(registry, procedural);
return application.Run(projectPath);

// Path resolution order:
//   1. explicit argv → use as-is (caller's responsibility).
//   2. else last-opened project from per-user editor settings (M6.3).
//   3. else default to projects/deferred under the runtime base directory
//      (the App csproj stages all starter projects there); fall back to the
//      source-tree path so `dotnet run` from the repo root keeps working.
static string ResolveProject(string[] argv)
{
    if (argv.Length > 0) return argv[0];

    var settings = EditorSettingsIO.ReadOrDefault();
    if (!string.IsNullOrEmpty(settings.LastProjectPath)
        && Directory.Exists(settings.LastProjectPath))
        return settings.LastProjectPath;

    var staged = Path.Combine(AppContext.BaseDirectory, "projects", "deferred");
    if (Directory.Exists(staged)) return staged;
    return "code/projects/deferred";
}
