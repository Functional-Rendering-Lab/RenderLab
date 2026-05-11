using System.Collections.Immutable;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Vulkan;
using RenderLab.Assets;
using RenderLab.Functional;
using RenderLab.Gpu;
using RenderLab.Gpu.Assets;
using RenderLab.Pipelines;
using RenderLab.Platform.Desktop;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;
using RenderLab.Ui.ImGui;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.App;

using Scene = RenderLab.Scene.Scene;
using ImGuiApi = ImGuiNET.ImGui;
using Result = RenderLab.Functional.Result;

/// <summary>
/// The single composition root that replaces the per-demo bootstraps. Owns
/// window, GPU, asset registry, ImGui shell and main loop. Loads a project
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
    VulkanImGui imgui = null!;
    RenderPass overlayRenderPass;
    Framebuffer[] overlayFramebuffers = [];
    IPipeline pipeline = null!;
    ProjectManifest manifest = null!;
    string projectRoot = "";
    string activeScenePath = "";
    SceneAssetSources sources = SceneAssetSources.Empty;

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

        // First call: bring up window + GPU + ImGui + registry. Re-entrant
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
            pipeline.Dispose();
            assets.ResetForSceneSwap();
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
    /// content. Idle the GPU, reset the registry to built-ins, ask the
    /// pipeline to drop scene-keyed caches, then reload — so re-running
    /// procedural generators against fresh ids gives the new scene a clean
    /// slate.
    /// </summary>
    Result<Unit, string> LoadScene(string projectRelative)
    {
        var sceneRes = ProjectIO.ReadScene(projectRoot, projectRelative);
        if (sceneRes.IsError)
            return Result.Error<Unit, string>(
                $"failed to read scene '{projectRelative}': {sceneRes.Match<ProjectError>(_ => null!, e => e).Message}");
        var doc = sceneRes.Match(ok: d => d, error: _ => null!);

        // Wipe registry + pipeline asset caches so the loader registers
        // fresh ids. Skip on first call (assets registry is fresh).
        if (!string.IsNullOrEmpty(activeScenePath))
        {
            vk.DeviceWaitIdle(gpu.Device);
            pipeline.ResetSceneState();
            assets.ResetForSceneSwap();
        }

        var loaded = SceneLoader.Load(projectRoot, doc, assets, _procedural);
        if (loaded.IsError)
            return Result.Error<Unit, string>(
                $"failed to load scene: {loaded.Match<SceneLoadError>(_ => null!, e => e).Message}");
        var ls = loaded.Match(ok: m => m, error: _ => null!);
        ui = ls.Ui;
        sources = ls.Sources;
        activeScenePath = projectRelative;
        app = app.WithActiveScene(projectRelative);
        return Result.Ok<Unit, string>(Unit.Value);
    }

    void InitPlatformAndGpu(string projectName)
    {
        window = DesktopWindow.Create($"RenderLab — {projectName}", DefaultWidth, DefaultHeight);
        vk = Vk.GetApi();
        gpu = VulkanDevice.Create(vk, window.GetRequiredVulkanExtensions(),
            instance => window.CreateVulkanSurface(instance));
        assets = new AssetRegistry(gpu);
        overlayRenderPass = VulkanPipeline.CreateOverlayRenderPass(gpu);
        overlayFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, overlayRenderPass);
        imgui = VulkanImGui.Create(gpu, overlayRenderPass);
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

            var input = window.PollInput();
            var keyboard = window.PollKeyboard();

            var cameraInput = new CameraInput(
                YawDelta:  input.LeftButtonDown ? -input.MouseDelta.X * RotateSensitivity : 0,
                PitchDelta: input.LeftButtonDown ? -input.MouseDelta.Y * RotateSensitivity : 0,
                MoveDelta: new Vector3(
                    input.MiddleButtonDown ? -input.MouseDelta.X * PanSensitivity : 0,
                    input.MiddleButtonDown ?  input.MouseDelta.Y * PanSensitivity : 0,
                    input.ScrollDelta * ZoomSensitivity));

            var prevUi = ui;

            if (!lastIntent.WantCaptureMouse)
            {
                if (pipeline.ConsumesScenes)
                    ui = ui with { Camera = FreeCameraController.Update(ui.Camera, cameraInput) };
                else
                    pipeline.HandleInput(cameraInput);
            }

            float aspect = (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height;
            Scene? scene = pipeline.ConsumesScenes ? SceneBuilder.BuildScene(ui, aspect) : null;

            // Feed input + keyboard to ImGui IO before NewFrame so widgets
            // see this frame's interactions (not the previous one's).
            var io = ImGuiApi.GetIO();
            io.MousePos       = input.MousePosition;
            io.MouseDown[0]   = input.LeftButtonDown;
            io.MouseDown[1]   = input.RightButtonDown;
            io.MouseDown[2]   = input.MiddleButtonDown;
            io.MouseWheel     = input.ScrollDelta;
            foreach (var c in keyboard.TypedChars) io.AddInputCharacter(c);
            foreach (var (key, down) in keyboard.KeyEvents)
            {
                var imKey = SilkKeyToImGui(key);
                if (imKey != ImGuiKey.None) io.AddKeyEvent(imKey, down);
            }

            pipeline.TickStats();

            if (!VulkanFrame.BeginFrame(gpu, out var imageIndex))
            {
                RecreateSwapchainResources();
                continue;
            }

            var cmd = gpu.CommandBuffers[gpu.CurrentFrame];

            // Build ImGui content (NewFrame + panels + DrawDebugUi) before
            // RecordFrame so the message-driven UiModel changes can land
            // before the pipeline reads ui in RecordFrame.
            imgui.NewFrame(window.Width, window.Height, dt);

            if (pipeline.ConsumesScenes)
            {
                var stats = pipeline.GetFrameStats(dt);
                var view = UiView.Draw(app, ui, scene!, assets, stats);
                ApplyViewMessages(view, prevUi);
                pipeline.DrawDebugUi();
                // ApplyViewMessages may have reloaded the scene (registry
                // wiped, ui replaced with fresh ids). Rebuild the snapshot
                // so drawables reference live assets, not removed ones.
                scene = SceneBuilder.BuildScene(ui, aspect);
            }
            else
            {
                AppMenuBar.Draw(app, msg => ApplyAppMessage(msg), includeViewMenu: false);
                pipeline.DrawDebugUi();
            }

            // Pipeline records its passes (must leave swapchain in PresentSrcKhr).
            pipeline.RecordFrame(gpu, cmd, scene, pipeline.ConsumesScenes ? ui : null, dt, imageIndex);

            // Application overlays the ImGui pass on top of whatever the
            // pipeline left in the swapchain.
            imgui.RecordCommands(vk, cmd, overlayRenderPass,
                overlayFramebuffers[imageIndex], gpu.SwapchainExtent);

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
    /// Single dispatch point for app-shell messages. Used by both the
    /// scene-consuming branch (folded out of <see cref="UiView"/>) and the
    /// scene-less branch (the bare menu bar). Side-effecting messages run
    /// here; pure ones flow through to the reducer.
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
                return r.Drawables.Select(d => new UiMsg.AddDrawable(
                    d.Name, d.Mesh,
                    new Transform(d.Position, d.Rotation, d.Scale),
                    d.Material));
            },
            error: e =>
            {
                Console.WriteLine($"  glTF import failed for {resolved}: {e.Message}");
                return Array.Empty<UiMsg>();
            });
    }

    /// <summary>
    /// Track each freshly-registered import as a <see cref="FileSourceDoc"/>
    /// rooted at the project — required so the next save can round-trip the
    /// asset back into the scene file. Files outside the project root are
    /// recorded by absolute path; the save will refuse them with a clear
    /// error rather than silently dropping the reference.
    /// </summary>
    void RecordImportSources(string absolutePath, GltfImportResult r)
    {
        var rel = ToProjectRelativeOrAbsolute(absolutePath);
        // The current SceneLoader file-source convention is "path resolves to
        // first mesh / first texture in the file"; record that convention.
        for (int i = 0; i < r.Meshes.Length; i++)
            sources = sources.WithMesh(r.Meshes[i], new FileSourceDoc(i == 0 ? rel : $"{rel}#mesh{i}"));
        for (int i = 0; i < r.Textures.Length; i++)
            sources = sources.WithTexture(r.Textures[i], new FileSourceDoc(i == 0 ? rel : $"{rel}#image{i}"));
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
        int refs = assets.AllMaterials.OfType<BlinnPhongMaterial>().Count(m => m.AlbedoMap == id);
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
    /// Writes a minimal <c>project.json</c> + empty <c>assets/</c> +
    /// <c>scenes/main.scene.json</c> with one procedural sphere so the
    /// new project opens to something visible. Refuses to overwrite if
    /// the folder already contains a manifest.
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

        // A starter scene the loader can open: one procedural sphere, a
        // single point light, default ambient.
        var doc = new SceneDocument(
            Version: 1,
            Camera: new CameraDoc([2.1f, 1.85f, 2.1f], 45f, -31.5f, 45f),
            Ambient: new AmbientDoc([0.4f, 0.5f, 0.7f], [0.18f, 0.16f, 0.14f]),
            Lights: [new PointLightDoc([2f, 3f, 2f], [1f, 0.95f, 0.9f], 5f)],
            RenderConfig: new RenderConfigDoc("blinnPhong", false, "final", [0f, 0f, 0f]),
            Assets: new SceneAssetsDoc(
                Meshes: [new MeshEntryDoc("Sphere", new ProceduralSourceDoc("sphere", null))],
                Textures: [],
                Materials: [new BlinnPhongMaterialDoc("Sphere", [0.6f, 0.6f, 0.6f], 0.5f, 32f, AlbedoMap: null)]),
            Drawables: [new DrawableDoc("Sphere", Mesh: 0, Material: 0,
                new TransformDoc([0f, 0f, 0f], [0f, 0f, 0f, 1f], 1f))]);
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

    static ImGuiKey SilkKeyToImGui(Key key) => key switch
    {
        Key.Tab           => ImGuiKey.Tab,
        Key.Left          => ImGuiKey.LeftArrow,
        Key.Right         => ImGuiKey.RightArrow,
        Key.Up            => ImGuiKey.UpArrow,
        Key.Down          => ImGuiKey.DownArrow,
        Key.PageUp        => ImGuiKey.PageUp,
        Key.PageDown      => ImGuiKey.PageDown,
        Key.Home          => ImGuiKey.Home,
        Key.End           => ImGuiKey.End,
        Key.Insert        => ImGuiKey.Insert,
        Key.Delete        => ImGuiKey.Delete,
        Key.Backspace     => ImGuiKey.Backspace,
        Key.Space         => ImGuiKey.Space,
        Key.Enter         => ImGuiKey.Enter,
        Key.Escape        => ImGuiKey.Escape,
        Key.ControlLeft   => ImGuiKey.LeftCtrl,
        Key.ControlRight  => ImGuiKey.RightCtrl,
        Key.ShiftLeft     => ImGuiKey.LeftShift,
        Key.ShiftRight    => ImGuiKey.RightShift,
        Key.AltLeft       => ImGuiKey.LeftAlt,
        Key.AltRight      => ImGuiKey.RightAlt,
        Key.SuperLeft     => ImGuiKey.LeftSuper,
        Key.SuperRight    => ImGuiKey.RightSuper,
        Key.A => ImGuiKey.A, Key.B => ImGuiKey.B, Key.C => ImGuiKey.C, Key.D => ImGuiKey.D,
        Key.E => ImGuiKey.E, Key.F => ImGuiKey.F, Key.G => ImGuiKey.G, Key.H => ImGuiKey.H,
        Key.I => ImGuiKey.I, Key.J => ImGuiKey.J, Key.K => ImGuiKey.K, Key.L => ImGuiKey.L,
        Key.M => ImGuiKey.M, Key.N => ImGuiKey.N, Key.O => ImGuiKey.O, Key.P => ImGuiKey.P,
        Key.Q => ImGuiKey.Q, Key.R => ImGuiKey.R, Key.S => ImGuiKey.S, Key.T => ImGuiKey.T,
        Key.U => ImGuiKey.U, Key.V => ImGuiKey.V, Key.W => ImGuiKey.W, Key.X => ImGuiKey.X,
        Key.Y => ImGuiKey.Y, Key.Z => ImGuiKey.Z,
        _ => ImGuiKey.None,
    };

    public unsafe void Dispose()
    {
        if (gpu is null) { window?.Dispose(); return; }
        vk.DeviceWaitIdle(gpu.Device);
        pipeline?.Dispose();
        imgui?.Dispose();
        if (overlayFramebuffers.Length > 0)
            VulkanPipeline.DestroyFramebuffers(gpu, overlayFramebuffers);
        if (overlayRenderPass.Handle != 0)
            vk.DestroyRenderPass(gpu.Device, overlayRenderPass, null);
        assets?.Dispose();
        gpu.Dispose();
        window.Dispose();
    }
}
