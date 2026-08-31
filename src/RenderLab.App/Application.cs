using System.Collections.Immutable;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Vulkan;
using RenderLab.Assets;
using RenderLab.Editor;
using RenderLab.Functional;
using RenderLab.Gpu;
using RenderLab.Gpu.Assets;
using RenderLab.Pipelines;
using RenderLab.Platform.Desktop;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.App;

using Scene = RenderLab.Scene.Scene;
using Result = RenderLab.Functional.Result;

/// <summary>
/// The single composition root that replaces the per-demo bootstraps. Owns
/// window, GPU, asset registry, editor shell and main loop. Loads a project
/// from disk, instantiates the project's pipeline, opens its default scene
/// (when the pipeline consumes scenes), and drives the frame loop. Handles
/// project / scene lifecycle requests from the menu (open, switch scene,
/// save) by tearing down GPU-side asset state on idle and reloading.
/// </summary>
public sealed class Application : IDisposable
{
    const int DefaultWidth = 1280;
    const int DefaultHeight = 720;
    const float RotateSensitivity = 0.005f;
    const float PanSensitivity = 0.01f;
    const float ZoomSensitivity = 0.3f;

    readonly PipelineRegistry _registry;
    readonly IProceduralAssetSource _procedural;

    DesktopWindow window = null!;
    Vk vk = null!;
    GpuState gpu = null!;
    AssetRegistry assets = null!;
    SceneAssetResolver resolver = null!;

    // The editor. The view holds the panel tree; the shell holds the atlas, the context, the
    // input translation and the draw target.
    PtahUi ptah = null!;
    readonly EditorView editor = new();

    RenderPass overlayRenderPass;
    Framebuffer[] overlayFramebuffers = [];
    IPipeline pipeline = null!;
    ShaderHotReload hotReload = null!;
    ProjectManifest manifest = null!;
    string projectRoot = "";
    string activeScenePath = "";
    SceneAssetSources sources = SceneAssetSources.Empty;
    ProjectAssetIndex projectIndex = ProjectAssetIndex.Empty("");
    AssetLibrary assetLibrary = AssetLibrary.Empty;

    AppUiModel app = AppUiModel.Default;
    UiModel ui = UiModel.Default;
    UiIntent lastIntent = UiIntent.None;

    public Application(PipelineRegistry registry, IProceduralAssetSource procedural)
    {
        _registry = registry;
        _procedural = procedural;
    }

    public int Run(string projectPath)
    {
        var settings = EditorSettingsIO.ReadOrDefault();

        var firstLoad = LoadProject(projectPath, restorePanels: settings);
        if (firstLoad.IsError)
        {
            Console.Error.WriteLine(firstLoad.Match<string>(_ => null!, e => e));
            return 1;
        }

        // Restore last-active scene when it's still listed under the project,
        // otherwise leave the default scene the project just loaded.
        if (!string.IsNullOrEmpty(settings.LastScenePath)
            && string.Equals(Path.GetFullPath(projectPath), settings.LastProjectPath, StringComparison.OrdinalIgnoreCase)
            && manifest.Scenes.Contains(settings.LastScenePath, StringComparer.OrdinalIgnoreCase)
            && !string.Equals(activeScenePath, settings.LastScenePath, StringComparison.OrdinalIgnoreCase))
        {
            var swap = LoadScene(settings.LastScenePath);
            if (swap.IsError) Console.Error.WriteLine(swap.Match<string>(_ => null!, e => e));
        }

        Loop();

        PersistEditorSettings();
        return 0;
    }

    /// <summary>
    /// Load a project from disk: tears down the previous pipeline (when
    /// switching), reads the manifest, instantiates the new pipeline, and —
    /// if the pipeline consumes scenes — opens the default scene. Used both
    /// by <see cref="Run"/> and by the <c>Open Project</c> menu item.
    /// </summary>
    Result<Unit, string> LoadProject(string projectPath, EditorSettings? restorePanels = null)
    {
        var newRoot = Path.GetFullPath(projectPath);

        var manifestRes = ProjectIO.ReadManifest(newRoot);
        if (manifestRes.IsError)
            return Result.Error<Unit, string>(
                $"failed to load project '{projectPath}': {manifestRes.Match<ProjectError>(_ => null!, e => e).Message}");
        var newManifest = manifestRes.Match(ok: m => m, error: _ => null!);

        var pipelineRes = _registry.Resolve(newManifest.Pipeline);
        if (pipelineRes.IsError)
            return Result.Error<Unit, string>(
                pipelineRes.Match<PipelineError>(_ => null!, e => e).Message);
        var newPipeline = pipelineRes.Match(ok: p => p, error: _ => null!);

        // First call: bring up window + GPU + editor + registry. Re-entrant
        // calls (project switch) reuse the existing infrastructure but
        // dispose the previous pipeline and clear the registry so the new
        // pipeline's Initialize can register its own resources.
        if (gpu is null)
        {
            InitPlatformAndGpu(newManifest.Name);
        }
        else
        {
            vk.DeviceWaitIdle(gpu.Device);
            hotReload.OnReload = null;
            pipeline.Dispose();
            // Project switch: wipe the registry and resolver caches so
            // the next project starts from built-ins. Scene-to-scene
            // swap inside a project no longer goes through this path.
            assets.ResetForSceneSwap();
            resolver.Clear();
            window.SetTitle($"RenderLab — {newManifest.Name}");
        }

        manifest = newManifest;
        projectRoot = newRoot;
        pipeline = newPipeline;
        sources = SceneAssetSources.Empty;
        ui = UiModel.Default;
        activeScenePath = "";
        pipeline.Initialize(gpu!, assets, overlayRenderPass);
        pipeline.RecreateTransient(gpu!);

        // The Visualization panel offers what this pipeline can resolve, so the model has to name
        // one of those or the panel would show nothing chosen while the screen showed something.
        // UiModel.Default asks for Final, which a pipeline with no lighting pass does not have.
        var modes = pipeline.SupportedVisualizations;
        if (!modes.IsEmpty && !modes.Contains(ui.Viz))
            ui = ui with { Viz = modes[0] };
        hotReload.OnReload = g => pipeline.ReloadShaders(g);

        var availableScenes = manifest.Scenes.Length > 0
            ? ImmutableArray.CreateRange(manifest.Scenes)
            : (string.IsNullOrEmpty(manifest.DefaultScene)
                ? ImmutableArray<string>.Empty
                : ImmutableArray.Create(manifest.DefaultScene));
        app = app.WithProject(manifest.Name, "", availableScenes);

        if (restorePanels is not null)
        {
            var hidden = ImmutableHashSet.CreateRange(
                restorePanels.HiddenPanels
                    .Where(name => Enum.TryParse<PanelId>(name, out _))
                    .Select(Enum.Parse<PanelId>));
            var visible = ImmutableHashSet.CreateRange(Enum.GetValues<PanelId>().Where(p => !hidden.Contains(p)));
            app = app with { VisiblePanels = visible };
        }

        projectIndex = ProjectAssetScanner.Scan(projectRoot);
        assetLibrary = AssetLibraryScanner.Scan(projectIndex);
        resolver.Bind(projectRoot, assetLibrary, _procedural);

        if (pipeline.ConsumesScenes)
        {
            if (string.IsNullOrEmpty(manifest.DefaultScene))
                return Result.Error<Unit, string>(
                    $"pipeline '{manifest.Pipeline}' consumes scenes but project '{manifest.Name}' has no defaultScene");
            return LoadScene(manifest.DefaultScene);
        }

        return Result.Ok<Unit, string>(Unit.Value);
    }

    /// <summary>
    /// Replace the active scene with <paramref name="projectRelative"/>'s
    /// content. The registry and resolver caches persist across scene
    /// swaps inside a project — meshes, textures, and materials already
    /// uploaded by previous scenes are reused by GUID, not re-uploaded.
    /// Loading a scene therefore reduces to (1) refresh the library so
    /// any sidecars written by <c>ReadScene</c> are visible, (2) walk
    /// the doc through the resolver to bind <see cref="AssetRef"/>s to
    /// runtime ids.
    /// </summary>
    Result<Unit, string> LoadScene(string projectRelative)
    {
        var sceneRes = ProjectIO.ReadScene(projectRoot, projectRelative);
        if (sceneRes.IsError)
            return Result.Error<Unit, string>(
                $"failed to read scene '{projectRelative}': {sceneRes.Match<ProjectError>(_ => null!, e => e).Message}");
        var doc = sceneRes.Match(ok: d => d, error: _ => null!);

        // ReadScene may have upgraded a legacy scene and written new asset
        // files (.proc.meta, .mat.json) — refresh the library so the loader
        // can resolve their AssetRefs.
        projectIndex = ProjectAssetScanner.Scan(projectRoot);
        assetLibrary = AssetLibraryScanner.Scan(projectIndex);
        resolver.Bind(projectRoot, assetLibrary, _procedural);

        var loaded = SceneLoader.Load(doc, resolver);
        if (loaded.IsError)
            return Result.Error<Unit, string>(
                $"failed to load scene: {loaded.Match<SceneLoadError>(_ => null!, e => e).Message}");
        var ls = loaded.Match(ok: m => m, error: _ => null!);
        ui = ls.Ui;
        sources = ls.Sources;
        activeScenePath = projectRelative;
        app = app.WithActiveScene(projectRelative);
        projectIndex = ProjectAssetScanner.Scan(projectRoot);
        assetLibrary = AssetLibraryScanner.Scan(projectIndex);
        resolver.Bind(projectRoot, assetLibrary, _procedural);
        return Result.Ok<Unit, string>(Unit.Value);
    }

    void InitPlatformAndGpu(string projectName)
    {
        window = DesktopWindow.Create($"RenderLab — {projectName}", DefaultWidth, DefaultHeight);
        vk = Vk.GetApi();
        gpu = VulkanDevice.Create(vk, window.GetRequiredVulkanExtensions(),
            instance => window.CreateVulkanSurface(instance));
        assets = new AssetRegistry(gpu);
        resolver = new SceneAssetResolver(assets);
        overlayRenderPass = VulkanPipeline.CreateOverlayRenderPass(gpu);
        overlayFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, overlayRenderPass);
        ptah = PtahUi.Create(gpu, overlayRenderPass, window.Input).Match(
            ok: shell => shell,
            error: failure => throw new InvalidOperationException(
                $"Failed to create the Ptah UI shell: {failure}"));
        hotReload = new ShaderHotReload(msg => Console.WriteLine($"  shader: {msg}"));
    }

    void Loop()
    {
        var frameTimer = System.Diagnostics.Stopwatch.StartNew();
        double lastFrameTime = 0;

        while (!window.IsClosing)
        {
            if (app.RequestedExit) break;

            window.DoEvents();

            if (window.Width == 0 || window.Height == 0) continue;

            if (window.WasResized || gpu.FramebufferResized)
            {
                window.ClearResizeFlag();
                gpu.FramebufferResized = false;
                RecreateSwapchainResources();
                continue;
            }

            double currentTime = frameTimer.Elapsed.TotalSeconds;
            float dt = (float)(currentTime - lastFrameTime);
            lastFrameTime = currentTime;

            assets.Tick();
            hotReload.Pump(gpu);

            var input = window.PollInput();
            var keyboard = window.PollKeyboard();

            foreach (var (key, down) in keyboard.KeyEvents)
                if (down && key == Key.F5) hotReload.RequestForceReload();

            var cameraInput = new CameraInput(
                YawDelta:  input.LeftButtonDown ? -input.MouseDelta.X * RotateSensitivity : 0,
                PitchDelta: input.LeftButtonDown ? -input.MouseDelta.Y * RotateSensitivity : 0,
                MoveDelta: new Vector3(
                    input.MiddleButtonDown ? -input.MouseDelta.X * PanSensitivity : 0,
                    input.MiddleButtonDown ?  input.MouseDelta.Y * PanSensitivity : 0,
                    input.ScrollDelta * ZoomSensitivity));

            var prevUi = ui;

            // One camera, whether or not the pipeline consumes a scene. A scene-less pipeline
            // used to keep its own and be fed through IPipeline.HandleInput, which is also why it
            // had to draw its own window to show the numbers.
            if (!lastIntent.WantCaptureMouse)
                ui = ui with { Camera = FreeCameraController.Update(ui.Camera, cameraInput) };

            float aspect = (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height;
            Scene? scene = pipeline.ConsumesScenes ? SceneBuilder.BuildScene(ui, aspect) : null;

            // Nothing to feed an interface here any more. The shell reads the window's own
            // Silk.NET input context through Ptah's InputTracker, so this frame's mouse and
            // keyboard reach it without the loop copying them anywhere.
            pipeline.TickStats();

            if (!VulkanFrame.BeginFrame(gpu, out var imageIndex))
            {
                RecreateSwapchainResources();
                continue;
            }

            var cmd = gpu.CommandBuffers[gpu.CurrentFrame];

            // The interface is built before the pipeline records, because building it is what
            // produces the model changes the pipeline then reads.
            var stats = pipeline.GetFrameStats(dt);
            var view = ptah.Frame(window.Width, window.Height, dt,
                context => editor.Draw(context, app, ui, assets, assetLibrary, projectIndex,
                    pipeline.SupportedVisualizations, stats, ptah.Cost));

            ApplyViewMessages(view, prevUi);

            // ApplyViewMessages may have reloaded the scene (registry wiped, ui replaced with
            // fresh ids). Rebuild the snapshot so drawables reference live assets, not removed
            // ones.
            if (pipeline.ConsumesScenes)
                scene = SceneBuilder.BuildScene(ui, aspect);

            // Pipeline records its passes (must leave swapchain in PresentSrcKhr).
            pipeline.RecordFrame(gpu, cmd, scene, ui, dt, imageIndex);

            // And the interface goes over the top of it, in an overlay pass of its own.
            ptah.Record(cmd, overlayFramebuffers[imageIndex], gpu.SwapchainExtent);

            if (!VulkanFrame.EndFrame(gpu, imageIndex))
                RecreateSwapchainResources();
        }
    }

    void ApplyViewMessages(UiViewResult view, UiModel uiBeforeFrameInputs)
    {
        // Material asset edits are registry-side effects the pure reducer
        // can't see; apply before folding so the next frame resolves the
        // new value.
        foreach (var msg in view.Messages)
            if (msg is UiMsg.UpdateMaterialAsset edit)
                assets.UpdateMaterial(edit.Id, edit.Asset);

        var extra = new List<UiMsg>();
        foreach (var amsg in view.AppMessages)
            ApplyAppMessage(amsg, extra);

        ui = UiUpdate.ApplyAll(ui, view.Messages);
        ui = UiUpdate.ApplyAll(ui, extra);
        app = AppUiUpdate.ApplyAll(app, view.AppMessages);
        lastIntent = view.Intent;

        // Mark dirty whenever the on-disk doc would differ from the runtime
        // state. Camera mouse drags, gizmo edits, panel edits — every
        // mutation flows through ui in the end. Material asset edits don't
        // appear in ui (they live in the registry); the dispatch above
        // marks dirty explicitly.
        bool changed = !ReferenceEquals(uiBeforeFrameInputs, ui) && uiBeforeFrameInputs != ui;
        bool materialEdit = view.Messages.Any(m => m is UiMsg.UpdateMaterialAsset);
        if ((changed || materialEdit) && !app.SceneDirty)
            app = app with { SceneDirty = true };
    }

    /// <summary>
    /// Single dispatch point for app-shell messages. Side-effecting messages
    /// run here; pure ones flow through to the reducer.
    /// </summary>
    void ApplyAppMessage(AppUiMsg msg, List<UiMsg>? followUp = null)
    {
        switch (msg)
        {
            case AppUiMsg.RequestImportGltf import:
                if (followUp is not null) followUp.AddRange(HandleImport(import.Path));
                else                       HandleImport(import.Path); // discard drawables when no follow-up sink
                break;
            case AppUiMsg.RequestImportGltfDialog:
                var picked = PlatformDialogs.OpenGltfFile();
                if (picked is not null && followUp is not null) followUp.AddRange(HandleImport(picked));
                break;
            case AppUiMsg.RequestRemoveMesh rm:
                HandleRemoveMesh(rm.Id);
                break;
            case AppUiMsg.RequestRemoveTexture rt:
                HandleRemoveTexture(rt.Id);
                break;
            case AppUiMsg.RequestRemoveMaterial rmat:
                HandleRemoveMaterial(rmat.Id);
                break;
            case AppUiMsg.RequestUpdateMaterial umat:
                HandleUpdateMaterial(umat);
                break;
            case AppUiMsg.RequestAddDrawableFromAsset add:
                if (followUp is not null) followUp.AddRange(HandleAddDrawableFromAsset(add.MeshGuid));
                break;
            case AppUiMsg.RequestUpdateMeshImport umi:
                HandleUpdateMeshImport(umi.Guid, umi.Scale);
                break;
            case AppUiMsg.RequestUpdateTextureImport uti:
                HandleUpdateTextureImport(uti.Guid, uti.SRgb, uti.Mips);
                break;
            case AppUiMsg.RequestDeleteAsset del:
                HandleDeleteAsset(del.Guid);
                break;
            case AppUiMsg.RequestRenameAsset ren:
                HandleRenameAsset(ren.Guid, ren.NewName);
                break;
            case AppUiMsg.RequestSaveScene:
                HandleSaveScene(activeScenePath);
                break;
            case AppUiMsg.RequestSaveSceneAs:
                HandleSaveSceneAs();
                break;
            case AppUiMsg.RequestOpenProjectDialog:
                HandleOpenProjectDialog();
                break;
            case AppUiMsg.RequestOpenProject op:
                HandleOpenProject(op.Path);
                break;
            case AppUiMsg.RequestOpenScene os:
                HandleOpenScene(os.ProjectRelative);
                break;
            case AppUiMsg.RequestReloadScene:
                HandleOpenScene(activeScenePath);
                break;
            case AppUiMsg.RequestNewProjectDialog:
                HandleNewProjectDialog();
                break;
            case AppUiMsg.RequestRescanProject:
                projectIndex = ProjectAssetScanner.Scan(projectRoot);
                assetLibrary = AssetLibraryScanner.Scan(projectIndex);
                resolver.Bind(projectRoot, assetLibrary, _procedural);
                break;
            case AppUiMsg.RequestRevealInExplorer rv:
                PlatformDialogs.RevealInExplorer(rv.AbsolutePath);
                break;
        }
    }

    IEnumerable<UiMsg> HandleImport(string path)
    {
        // Project-relative paths resolve under the project root; absolute
        // paths import in place (file picker yields absolutes).
        var resolved = Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectRoot, path);
        var result = assets.ImportGltf(resolved);
        return result.Match<IEnumerable<UiMsg>>(
            ok: r =>
            {
                Console.WriteLine($"  glTF: imported {r.Meshes.Length} mesh(es), {r.Textures.Length} texture(s), {r.Materials.Length} material(s), {r.Drawables.Length} drawable(s) from {path}");
                RecordImportSources(resolved, r);
                if (r.Meshes.Length > 0 || r.Textures.Length > 0 || r.Materials.Length > 0)
                    app = app with { SceneDirty = true };
                projectIndex = ProjectAssetScanner.Scan(projectRoot);
                assetLibrary = AssetLibraryScanner.Scan(projectIndex);
                resolver.Bind(projectRoot, assetLibrary, _procedural);
                return r.Drawables.Select(d => new UiMsg.AddDrawable(
                    d.Name, d.Mesh,
                    new Transform(
                        d.Position,
                        UnitQuaternion.UnsafeFromUnit(d.Rotation),
                        PositiveScale.UnsafeFrom(d.Scale > 0f ? d.Scale : 1f)),
                    d.Material));
            },
            error: e =>
            {
                Console.WriteLine($"  glTF import failed for {resolved}: {e.Message}");
                return Array.Empty<UiMsg>();
            });
    }

    /// <summary>
    /// Drag-to-scene: spawn a drawable referencing a mesh asset and pair
    /// it with a material asset (first available in the library, so save
    /// round-trips). For glTF-backed meshes we address the first
    /// sub-mesh; procedurals carry no sub.
    /// </summary>
    IEnumerable<UiMsg> HandleAddDrawableFromAsset(Guid meshGuid)
    {
        var entry = assetLibrary.Find(meshGuid);
        if (entry is null || entry.Kind != AssetKind.Mesh)
        {
            Console.WriteLine($"  add drawable: guid {meshGuid:D} is not a mesh in this project");
            return Array.Empty<UiMsg>();
        }
        var meshRef = entry is FileAssetEntry ? new AssetRef(meshGuid, "mesh:0") : new AssetRef(meshGuid);
        var meshRes = resolver.ResolveMesh(meshRef);
        if (meshRes.IsError)
        {
            Console.WriteLine($"  add drawable: mesh resolve failed: {meshRes.Match<SceneLoadError>(_ => null!, e => e).Message}");
            return Array.Empty<UiMsg>();
        }
        var meshId = meshRes.Match(ok: x => x, error: _ => default);
        sources = sources.WithMesh(meshId, meshRef);

        MaterialId materialId = assets.BuiltinDefaultMaterial;
        AssetRef? materialRef = null;
        foreach (var libEntry in assetLibrary.ByGuid.Values)
        {
            if (libEntry is not MaterialAssetEntry m) continue;
            var matRes = resolver.ResolveMaterial(new AssetRef(m.Guid));
            if (matRes.IsError) continue;
            materialId = matRes.Match(ok: x => x, error: _ => default);
            materialRef = new AssetRef(m.Guid);
            break;
        }
        if (materialRef is AssetRef mr)
            sources = sources.WithMaterial(materialId, mr);
        else
            Console.WriteLine("  add drawable: no .mat.json in project — drawable bound to built-in default; save will refuse until a material is assigned");

        app = app with { SceneDirty = true };
        return new UiMsg[] { new UiMsg.AddDrawable(entry.Name, meshId, Transform.Default, materialId) };
    }

    /// <summary>
    /// Track each freshly-registered import as an <see cref="AssetRef"/> so
    /// the next save can round-trip its drawables. Imports outside the
    /// project root have no stable id and are skipped — save will refuse
    /// drawables that reference them.
    /// </summary>
    void RecordImportSources(string absolutePath, GltfImportResult r)
    {
        if (!IsUnderProjectRoot(absolutePath)) return;
        var meta = AssetMetaIO.ReadOrCreate(absolutePath, AssetKind.Mesh);
        var guid = meta.Guid;
        for (int i = 0; i < r.Meshes.Length; i++)
            sources = sources.WithMesh(r.Meshes[i], new AssetRef(guid, $"mesh:{i}"));
        for (int i = 0; i < r.Textures.Length; i++)
            sources = sources.WithTexture(r.Textures[i], new AssetRef(guid, $"image:{i}"));
    }

    bool IsUnderProjectRoot(string absolute)
    {
        var fullRoot = Path.GetFullPath(projectRoot);
        var withSep = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var fullAbs = Path.GetFullPath(absolute);
        return fullAbs.StartsWith(withSep, StringComparison.OrdinalIgnoreCase);
    }

    string ToProjectRelativeOrAbsolute(string absolute)
    {
        var fullRoot = Path.GetFullPath(projectRoot);
        var withSep = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var fullAbs = Path.GetFullPath(absolute);
        if (fullAbs.StartsWith(withSep, StringComparison.OrdinalIgnoreCase))
        {
            // Use forward slashes in the on-disk doc — the scene format is
            // project-relative across platforms.
            return fullAbs[withSep.Length..].Replace(Path.DirectorySeparatorChar, '/');
        }
        return fullAbs;
    }

    void HandleRemoveMesh(MeshId id)
    {
        int refs = ui.Drawables.Count(d => d.Mesh == id);
        if (refs > 0)
        {
            Console.WriteLine($"  remove mesh #{id.Value}: refused, {refs} drawable(s) still reference it");
            return;
        }
        try
        {
            assets.RemoveMesh(id);
            sources = sources.WithoutMesh(id);
            app = app with { SceneDirty = true };
        }
        catch (Exception ex) { Console.WriteLine($"  remove mesh #{id.Value} failed: {ex.Message}"); }
    }

    void HandleRemoveTexture(TextureId id)
    {
        if (id == assets.BuiltinWhiteTexture)
        {
            Console.WriteLine("  remove texture: built-in white fallback is protected");
            return;
        }
        int refs = assets.AllMaterials.OfType<BlinnPhongMaterial>().Count(m => m.AlbedoMap.ValueOr(TextureId.None) == id);
        if (refs > 0)
        {
            Console.WriteLine($"  remove texture #{id.Value}: refused, {refs} material(s) still reference it");
            return;
        }
        try
        {
            assets.RemoveTexture(id);
            (pipeline as DeferredPipeline)?.InvalidateMaterialTexture(id);
            sources = sources.WithoutTexture(id);
            app = app with { SceneDirty = true };
        }
        catch (Exception ex) { Console.WriteLine($"  remove texture #{id.Value} failed: {ex.Message}"); }
    }

    /// <summary>
    /// Persist a material edit from the Asset Browser to its <c>.mat.json</c>
    /// sidecar and — when the material is currently bound to a registered
    /// <see cref="MaterialId"/> — mirror the change in the runtime registry so
    /// the next frame renders with the new parameters.
    /// </summary>
    void HandleUpdateMaterial(AppUiMsg.RequestUpdateMaterial msg)
    {
        var entry = assetLibrary.Find(msg.Guid) as MaterialAssetEntry;
        if (entry is null)
        {
            Console.WriteLine($"  update material {msg.Guid:D}: no library entry");
            return;
        }

        Optional<AssetRef> albedoTexRef = msg.AlbedoTexGuid is Guid g
            ? Optional<AssetRef>.Some(new AssetRef(g, msg.AlbedoTexSub))
            : Optional<AssetRef>.None;

        var resolved = ProjectIO.ResolveProjectPath(projectRoot, entry.ProjectRelativePath);
        if (resolved.IsError)
        {
            Console.WriteLine($"  update material {entry.Name}: {resolved.Match<ProjectError>(_ => null!, e => e).Message}");
            return;
        }
        var absolute = resolved.Match(ok: p => p, error: _ => "");

        var doc = new MaterialFileDoc(
            Version: 1,
            Name: entry.Name,
            Params: new MaterialParamsDoc(msg.Albedo, msg.SpecularStrength, msg.Shininess),
            AlbedoTex: albedoTexRef);
        try
        {
            AssetLibraryScanner.WriteMaterial(absolute, doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  update material {entry.Name}: write failed: {ex.Message}");
            return;
        }

        projectIndex = ProjectAssetScanner.Scan(projectRoot);
        assetLibrary = AssetLibraryScanner.Scan(projectIndex);
        resolver.Bind(projectRoot, assetLibrary, _procedural);

        // Reverse-lookup the runtime MaterialId. A material may be edited even
        // when no scene drawable references it — disk persistence is the only
        // effect in that case.
        MaterialId? liveId = null;
        foreach (var kv in sources.MaterialSources)
            if (kv.Value.Guid == msg.Guid) { liveId = kv.Key; break; }
        if (liveId is not MaterialId id) return;

        if (!assets.TryGetMaterial(id, out var existing) || existing is not BlinnPhongMaterial bp) return;

        var albedoMap = bp.AlbedoMap;
        if (albedoTexRef.IsSome)
        {
            var texRef = albedoTexRef.Match(some: x => x, none: () => default!);
            albedoMap = Optional<TextureId>.None;
            foreach (var kv in sources.TextureSources)
                if (kv.Value == texRef) { albedoMap = Optional<TextureId>.Some(kv.Key); break; }
        }
        else
        {
            albedoMap = Optional<TextureId>.None;
        }

        var albedo = msg.Albedo;
        assets.UpdateMaterial(id, new BlinnPhongMaterial(
            id, bp.Name,
            new Vector3(albedo[0], albedo[1], albedo[2]),
            msg.SpecularStrength,
            msg.Shininess,
            albedoMap));
        // The scene doc is unchanged (the material lives in its own .mat.json
        // which was just written), so SceneDirty stays as-is.
    }

    void HandleRemoveMaterial(MaterialId id)
    {
        if (id == assets.BuiltinDefaultMaterial)
        {
            Console.WriteLine("  remove material: built-in default is protected");
            return;
        }
        int refs = ui.Drawables.Count(d => d.Material == id);
        if (refs > 0)
        {
            Console.WriteLine($"  remove material #{id.Value}: refused, {refs} drawable(s) still reference it");
            return;
        }
        try
        {
            assets.RemoveMaterial(id);
            app = app with { SceneDirty = true };
        }
        catch (Exception ex) { Console.WriteLine($"  remove material #{id.Value} failed: {ex.Message}"); }
    }

    /// <summary>
    /// Delete an Asset Browser entry. The Asset Browser surfaces stable
    /// library entries keyed by guid; runtime ids may or may not exist
    /// for any given entry depending on whether the active scene
    /// touched it. Refuses when the live scene (or, for textures, any
    /// material file in the project) still references the entry. On
    /// success, removes the live runtime registration (if any), deletes
    /// the source + sidecar from disk, invalidates the resolver cache,
    /// and rescans the library.
    /// </summary>
    void HandleDeleteAsset(Guid guid)
    {
        var entry = assetLibrary.Find(guid);
        if (entry is null)
        {
            Console.WriteLine($"  delete asset {guid:D}: no library entry");
            return;
        }

        // Ref-check at the project level so we don't orphan a scene
        // drawable or a material file that still cites this guid.
        var refs = CountRefsTo(guid, entry.Kind);
        if (refs > 0)
        {
            Console.WriteLine($"  delete asset '{entry.Name}': refused, {refs} reference(s) still point at it");
            return;
        }

        // Resolve absolute paths to delete BEFORE we touch the registry,
        // so a missing/locked file aborts the operation with no GPU side
        // effects.
        string? primaryAbsolute = null;
        string? sidecarAbsolute = null;
        switch (entry)
        {
            case MaterialAssetEntry m:
                primaryAbsolute = ResolveAbsolute(m.ProjectRelativePath);
                sidecarAbsolute = primaryAbsolute is null ? null : AssetMetaIO.MetaPathFor(primaryAbsolute);
                break;
            case FileAssetEntry f:
                primaryAbsolute = ResolveAbsolute(f.ProjectRelativePath);
                sidecarAbsolute = primaryAbsolute is null ? null : AssetMetaIO.MetaPathFor(primaryAbsolute);
                break;
            case ProceduralAssetEntry p:
                // .proc.meta IS the source — no separate sidecar.
                primaryAbsolute = ResolveAbsolute(p.ProjectRelativePath);
                break;
        }
        if (primaryAbsolute is null)
        {
            Console.WriteLine($"  delete asset '{entry.Name}': could not resolve project-relative path");
            return;
        }

        // Idle the GPU before tearing down any live registration — the
        // texture/mesh removal queues a deferred destroy, but we'd
        // rather not race the in-flight frame.
        bool touchedGpu = false;
        switch (entry.Kind)
        {
            case AssetKind.Mesh:
                foreach (var kv in sources.MeshSources)
                    if (kv.Value.Guid == guid)
                    {
                        if (!touchedGpu) { vk.DeviceWaitIdle(gpu.Device); touchedGpu = true; }
                        try { assets.RemoveMesh(kv.Key); } catch (Exception ex) { Console.WriteLine($"  delete asset '{entry.Name}': RemoveMesh failed: {ex.Message}"); }
                        sources = sources.WithoutMesh(kv.Key);
                    }
                break;
            case AssetKind.Texture:
                foreach (var kv in sources.TextureSources)
                    if (kv.Value.Guid == guid)
                    {
                        if (!touchedGpu) { vk.DeviceWaitIdle(gpu.Device); touchedGpu = true; }
                        try
                        {
                            assets.RemoveTexture(kv.Key);
                            (pipeline as DeferredPipeline)?.InvalidateMaterialTexture(kv.Key);
                        }
                        catch (Exception ex) { Console.WriteLine($"  delete asset '{entry.Name}': RemoveTexture failed: {ex.Message}"); }
                        sources = sources.WithoutTexture(kv.Key);
                    }
                break;
            case AssetKind.Material:
                foreach (var kv in sources.MaterialSources)
                    if (kv.Value.Guid == guid)
                    {
                        try { assets.RemoveMaterial(kv.Key); } catch (Exception ex) { Console.WriteLine($"  delete asset '{entry.Name}': RemoveMaterial failed: {ex.Message}"); }
                        sources = sources.WithoutMaterial(kv.Key);
                    }
                break;
        }

        // Disk: source + sidecar. Missing files are tolerated — the goal
        // state is "neither exists." A locked file surfaces as a console
        // error; the runtime state is already consistent.
        TryDelete(primaryAbsolute);
        if (sidecarAbsolute is not null) TryDelete(sidecarAbsolute);

        resolver.InvalidateGuid(guid);

        projectIndex = ProjectAssetScanner.Scan(projectRoot);
        assetLibrary = AssetLibraryScanner.Scan(projectIndex);
        resolver.Bind(projectRoot, assetLibrary, _procedural);

        Console.WriteLine($"  delete asset '{entry.Name}': removed {primaryAbsolute}");
    }

    /// <summary>
    /// Rename a project-level asset. Moves the source file + meta
    /// sidecar (file assets) or the .mat.json + sidecar (materials)
    /// or just the .proc.meta (procedurals) to a sibling path with the
    /// new basename, preserving the original extension. For material
    /// and procedural docs the embedded <c>name</c> field is updated to
    /// match. The GUID is unchanged, so scene-level AssetRefs keep
    /// pointing at the renamed entry without any scene rewrites.
    /// </summary>
    void HandleRenameAsset(Guid guid, string newName)
    {
        var entry = assetLibrary.Find(guid);
        if (entry is null)
        {
            Console.WriteLine($"  rename asset {guid:D}: no library entry");
            return;
        }

        var sanitized = (newName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(sanitized))
        {
            Console.WriteLine($"  rename asset '{entry.Name}': empty name");
            return;
        }
        if (sanitized.IndexOfAny(['/', '\\']) >= 0
            || sanitized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || sanitized.StartsWith('.'))
        {
            Console.WriteLine($"  rename asset '{entry.Name}': invalid name '{sanitized}'");
            return;
        }
        if (string.Equals(sanitized, entry.Name, StringComparison.Ordinal))
            return; // no-op

        string projectRelative;
        switch (entry)
        {
            case MaterialAssetEntry m: projectRelative = m.ProjectRelativePath; break;
            case FileAssetEntry f:    projectRelative = f.ProjectRelativePath; break;
            case ProceduralAssetEntry p: projectRelative = p.ProjectRelativePath; break;
            default:
                Console.WriteLine($"  rename asset '{entry.Name}': unknown entry kind");
                return;
        }

        var primaryAbsolute = ResolveAbsolute(projectRelative);
        if (primaryAbsolute is null || !File.Exists(primaryAbsolute))
        {
            Console.WriteLine($"  rename asset '{entry.Name}': source missing at {projectRelative}");
            return;
        }

        // Preserve the original file's compound extension. .mat.json and
        // .proc.meta both have two dots; Path.GetExtension only returns
        // the last segment, so we detect those explicitly.
        string fileName = Path.GetFileName(primaryAbsolute);
        string ext = fileName.EndsWith(".mat.json", StringComparison.OrdinalIgnoreCase)
            ? ".mat.json"
            : fileName.EndsWith(".proc.meta", StringComparison.OrdinalIgnoreCase)
                ? ".proc.meta"
                : Path.GetExtension(fileName);

        string dir = Path.GetDirectoryName(primaryAbsolute)!;
        string newPrimary = Path.Combine(dir, sanitized + ext);
        if (File.Exists(newPrimary)
            && !string.Equals(newPrimary, primaryAbsolute, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  rename asset '{entry.Name}': target already exists at {newPrimary}");
            return;
        }

        try
        {
            // Move source first, then sidecar. If only the source moves
            // successfully and the sidecar fails, the next scan still
            // finds the file via its old .meta (mismatched basename is
            // fine — the .meta carries the guid).
            File.Move(primaryAbsolute, newPrimary);

            // File assets and materials have a separate .meta sidecar;
            // procedural assets do not (the .proc.meta IS the source).
            if (entry is not ProceduralAssetEntry)
            {
                string oldMeta = AssetMetaIO.MetaPathFor(primaryAbsolute);
                string newMeta = AssetMetaIO.MetaPathFor(newPrimary);
                if (File.Exists(oldMeta)) File.Move(oldMeta, newMeta);
            }

            // Rewrite the embedded name for self-describing assets so
            // future scans report the new name (and any tool that reads
            // the file directly stays consistent).
            switch (entry)
            {
                case MaterialAssetEntry m:
                    AssetLibraryScanner.WriteMaterial(newPrimary, new MaterialFileDoc(
                        Version: 1, Name: sanitized, Params: m.Params, AlbedoTex: m.AlbedoTex));
                    break;
                case ProceduralAssetEntry p:
                {
                    var doc = ProceduralAssetIO.TryRead(newPrimary);
                    if (doc is not null)
                        ProceduralAssetIO.Write(newPrimary, doc with { Name = sanitized });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  rename asset '{entry.Name}': {ex.Message}");
            return;
        }

        projectIndex = ProjectAssetScanner.Scan(projectRoot);
        assetLibrary = AssetLibraryScanner.Scan(projectIndex);
        resolver.Bind(projectRoot, assetLibrary, _procedural);

        Console.WriteLine($"  rename asset: '{entry.Name}' → '{sanitized}' at {newPrimary}");
    }

    /// <summary>
    /// Persist edited <c>MeshImportSettings</c> (currently just
    /// <c>Scale</c>) to the asset's <c>.meta</c> sidecar. The importer
    /// does not yet consume these fields — the write is forward-looking
    /// storage so the editor round-trips.
    /// </summary>
    void HandleUpdateMeshImport(Guid guid, float scale)
    {
        if (assetLibrary.Find(guid) is not FileAssetEntry f || f.Kind != AssetKind.Mesh)
        {
            Console.WriteLine($"  update mesh import {guid:D}: not a file-backed mesh");
            return;
        }
        WriteMetaImport(f, new MeshImportSettings(Scale: scale));
    }

    /// <summary>
    /// Persist edited <c>TextureImportSettings</c> to the texture's
    /// <c>.meta</c> sidecar. Like <see cref="HandleUpdateMeshImport"/>,
    /// the fields are not yet honoured by the texture uploader.
    /// </summary>
    void HandleUpdateTextureImport(Guid guid, bool sRgb, bool mips)
    {
        if (assetLibrary.Find(guid) is not FileAssetEntry f || f.Kind != AssetKind.Texture)
        {
            Console.WriteLine($"  update texture import {guid:D}: not a file-backed texture");
            return;
        }
        WriteMetaImport(f, new TextureImportSettings(SRgb: sRgb, Mips: mips));
    }

    void WriteMetaImport(FileAssetEntry f, ImportSettings settings)
    {
        var absolute = ResolveAbsolute(f.ProjectRelativePath);
        if (absolute is null)
        {
            Console.WriteLine($"  update import {f.Name}: could not resolve project-relative path");
            return;
        }
        var metaPath = AssetMetaIO.MetaPathFor(absolute);
        try
        {
            AssetMetaIO.Write(metaPath, new AssetMetaDoc(f.Guid, f.Kind, settings));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  update import {f.Name}: write failed: {ex.Message}");
            return;
        }
        projectIndex = ProjectAssetScanner.Scan(projectRoot);
        assetLibrary = AssetLibraryScanner.Scan(projectIndex);
        resolver.Bind(projectRoot, assetLibrary, _procedural);
    }

    int CountRefsTo(Guid guid, AssetKind kind)
    {
        int count = 0;
        switch (kind)
        {
            case AssetKind.Mesh:
                foreach (var d in ui.Drawables)
                    if (sources.MeshSources.TryGetValue(d.Mesh, out var r) && r.Guid == guid) count++;
                break;
            case AssetKind.Material:
                foreach (var d in ui.Drawables)
                    if (sources.MaterialSources.TryGetValue(d.Material, out var r) && r.Guid == guid) count++;
                break;
            case AssetKind.Texture:
                // File-level check: every material on disk that points at
                // this texture, regardless of whether the active scene has
                // it loaded.
                foreach (var e in assetLibrary.ByGuid.Values)
                    if (e is MaterialAssetEntry m && m.AlbedoTex.IsSome && m.AlbedoTex.Match(some: x => x.Guid, none: () => Guid.Empty) == guid) count++;
                break;
        }
        return count;
    }

    string? ResolveAbsolute(string projectRelative)
    {
        var res = ProjectIO.ResolveProjectPath(projectRoot, projectRelative);
        return res.IsOk ? res.Match(ok: p => p, error: _ => null!) : null;
    }

    static void TryDelete(string absolutePath)
    {
        try
        {
            if (File.Exists(absolutePath)) File.Delete(absolutePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  delete: could not remove {absolutePath}: {ex.Message}");
        }
    }

    // ─── M6.2 / M6.3 handlers ──────────────────────────────────────────

    void HandleSaveScene(string projectRelative)
    {
        if (string.IsNullOrEmpty(projectRelative))
        {
            HandleSaveSceneAs();
            return;
        }
        var built = SceneDocumentBuilder.From(ui, assets, sources);
        if (built.IsError)
        {
            Console.WriteLine($"  save: {built.Match<SceneSaveError>(_ => null!, e => e).Message}");
            return;
        }
        var doc = built.Match(ok: d => d, error: _ => null!);
        var write = ProjectIO.WriteScene(projectRoot, projectRelative, doc);
        if (write.IsError)
        {
            Console.WriteLine($"  save failed: {write.Match<ProjectError>(_ => null!, e => e).Message}");
            return;
        }
        if (!string.Equals(activeScenePath, projectRelative, StringComparison.OrdinalIgnoreCase))
            activeScenePath = projectRelative;
        app = app.WithActiveScene(activeScenePath); // clears SceneDirty
        Console.WriteLine($"  save: wrote {projectRelative}");
    }

    void HandleSaveSceneAs()
    {
        // Default to the project's scenes/ folder so saved files land where
        // the manifest expects to find them.
        var sceneDir = Path.Combine(projectRoot, "scenes");
        var defaultName = string.IsNullOrEmpty(activeScenePath)
            ? "untitled"
            : Path.GetFileNameWithoutExtension(activeScenePath).Replace(".scene", "", StringComparison.OrdinalIgnoreCase);
        var picked = PlatformDialogs.SaveSceneFile(sceneDir, defaultName);
        if (picked is null) return;

        var rel = ToProjectRelativeOrAbsolute(picked);
        if (Path.IsPathRooted(rel))
        {
            Console.WriteLine($"  save-as refused: {picked} is outside the project root");
            return;
        }
        // Add the freshly-saved scene to the project's scene list so it
        // shows up in the Open Scene submenu without a manifest reload.
        if (!manifest.Scenes.Contains(rel, StringComparer.OrdinalIgnoreCase))
        {
            var grown = manifest.Scenes.Append(rel).ToArray();
            manifest = manifest with { Scenes = grown };
            // Default-scene stays put; the user explicitly saved a sibling.
            ProjectIO.WriteManifest(projectRoot, manifest);
            app = app with { AvailableScenes = ImmutableArray.CreateRange(grown) };
        }
        HandleSaveScene(rel);
    }

    void HandleOpenProjectDialog()
    {
        var picked = PlatformDialogs.OpenFolder();
        if (picked is null) return;
        HandleOpenProject(picked);
    }

    void HandleOpenProject(string path)
    {
        // Confirmation-on-dirty is not in M6.3 scope; the dirty flag is
        // surfaced in the title bar / menu so the user can decide.
        var load = LoadProject(path);
        if (load.IsError)
        {
            Console.WriteLine($"  open project: {load.Match<string>(_ => null!, e => e)}");
            return;
        }
        PersistEditorSettings();
    }

    void HandleOpenScene(string projectRelative)
    {
        if (string.IsNullOrEmpty(projectRelative)) return;
        var swap = LoadScene(projectRelative);
        if (swap.IsError)
        {
            Console.WriteLine($"  open scene: {swap.Match<string>(_ => null!, e => e)}");
            return;
        }
        PersistEditorSettings();
    }

    void HandleNewProjectDialog()
    {
        var folder = PlatformDialogs.OpenFolder();
        if (folder is null) return;
        try
        {
            CreateSkeletonProject(folder);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  new project: failed to create skeleton at {folder}: {ex.Message}");
            return;
        }
        HandleOpenProject(folder);
    }

    /// <summary>
    /// Writes a minimal <c>project.json</c>, a procedural sphere mesh, a
    /// default Blinn-Phong material, and a scene that references both via
    /// stable <see cref="AssetRef"/>s. Refuses to overwrite if the folder
    /// already contains a manifest.
    /// </summary>
    static void CreateSkeletonProject(string folder)
    {
        Directory.CreateDirectory(folder);
        var manifestPath = Path.Combine(folder, ProjectIO.ManifestFileName);
        if (File.Exists(manifestPath))
            throw new InvalidOperationException($"{manifestPath} already exists");

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        if (string.IsNullOrEmpty(name)) name = "Untitled";
        var defaultScene = "scenes/main.scene.json";
        var manifest = new ProjectManifest(
            Version: 1,
            Name: name,
            Pipeline: "deferred",
            DefaultScene: defaultScene,
            Scenes: [defaultScene]);
        ProjectIO.WriteManifest(folder, manifest);

        // Procedural sphere mesh, written as a .proc.meta self-describing
        // asset so the library scanner picks it up.
        var meshGuid = Guid.NewGuid();
        var procPath = Path.Combine(folder, "assets", "proc", "sphere_Sphere.proc.meta");
        ProceduralAssetIO.Write(procPath, new ProceduralFileDoc(
            Version: 1,
            Guid: meshGuid,
            Kind: AssetKind.Mesh,
            Name: "Sphere",
            Generator: "sphere",
            Params: null));

        // File-backed default material with its .meta sidecar.
        var matPath = Path.Combine(folder, "assets", "materials", "Sphere.mat.json");
        AssetLibraryScanner.WriteMaterial(matPath, new MaterialFileDoc(
            Version: 1,
            Name: "Sphere",
            Params: new MaterialParamsDoc([0.6f, 0.6f, 0.6f], 0.5f, 32f),
            AlbedoTex: Optional<AssetRef>.None));
        var matMeta = AssetMetaIO.ReadOrCreate(matPath, AssetKind.Material);

        var doc = new SceneDocument(
            Version: 2,
            Camera: new CameraDoc([2.1f, 1.85f, 2.1f], 45f, -31.5f, 45f),
            Ambient: new AmbientDoc([0.4f, 0.5f, 0.7f], [0.18f, 0.16f, 0.14f]),
            Lights: [new PointLightDoc([2f, 3f, 2f], [1f, 0.95f, 0.9f], 5f)],
            RenderConfig: new RenderConfigDoc(ShadingMode.BlinnPhong, false, VisualizationMode.Final, [0f, 0f, 0f]),
            Drawables: [new DrawableDoc("Sphere",
                Mesh: new AssetRef(meshGuid),
                Material: new AssetRef(matMeta.Guid),
                Transform: new TransformDoc([0f, 0f, 0f], [0f, 0f, 0f, 1f], 1f))]);
        ProjectIO.WriteScene(folder, defaultScene, doc);
    }

    void PersistEditorSettings()
    {
        var hidden = Enum.GetValues<PanelId>()
            .Where(p => !app.IsPanelVisible(p))
            .Select(p => p.ToString())
            .ToArray();
        EditorSettingsIO.Write(new EditorSettings(
            Version: 1,
            LastProjectPath: projectRoot,
            LastScenePath: activeScenePath,
            HiddenPanels: hidden));
    }

    void RecreateSwapchainResources()
    {
        vk.DeviceWaitIdle(gpu.Device);
        if (overlayFramebuffers.Length > 0)
            VulkanPipeline.DestroyFramebuffers(gpu, overlayFramebuffers);
        VulkanDevice.DestroyRenderFinishedSemaphores(gpu);
        VulkanSwapchain.Recreate(gpu, (uint)window.Width, (uint)window.Height);
        VulkanDevice.CreateRenderFinishedSemaphores(gpu);
        overlayFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, overlayRenderPass);
        pipeline.RecreateTransient(gpu);
    }

    public unsafe void Dispose()
    {
        if (gpu is null) { window?.Dispose(); return; }
        vk.DeviceWaitIdle(gpu.Device);
        hotReload?.Dispose();
        pipeline?.Dispose();
        ptah?.Dispose();
        if (overlayFramebuffers.Length > 0)
            VulkanPipeline.DestroyFramebuffers(gpu, overlayFramebuffers);
        if (overlayRenderPass.Handle != 0)
            vk.DestroyRenderPass(gpu.Device, overlayRenderPass, null);
        assets?.Dispose();
        gpu.Dispose();
        window.Dispose();
    }
}
