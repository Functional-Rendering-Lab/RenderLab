using System.Collections.Immutable;
using RenderLab.Functional;
using RenderLab.Graph;

namespace RenderLab.Graph.Tests;

public class CompilerTests
{
    private static ImmutableArray<ResolvedPass> Ok(Result<ImmutableArray<ResolvedPass>, GraphError> r) =>
        r.Match(
            ok: x => x,
            error: e => throw new Xunit.Sdk.XunitException($"Expected Ok, got {e}"));

    private static GraphError Err(Result<ImmutableArray<ResolvedPass>, GraphError> r) =>
        r.Match(
            ok: _ => throw new Xunit.Sdk.XunitException("Expected Error, got Ok"),
            error: e => e);

    [Fact]
    public void SinglePass_NoBarriers()
    {
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("Only",
                Inputs: [],
                Outputs: [new PassOutput(ResourceName.Of("Color"), ResourceUsage.ColorAttachmentWrite)]));

        var resolved = Ok(RenderGraphCompiler.Compile(passes));

        Assert.Single(resolved);
        Assert.Equal("Only", resolved[0].Declaration.Name);
        Assert.Empty(resolved[0].BarriersBefore);
    }

    [Fact]
    public void TwoPassDependency_CorrectOrderAndBarrier()
    {
        var offscreen = ResourceName.Of("OffscreenColor");
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("Geometry",
                Inputs: [],
                Outputs: [new PassOutput(offscreen, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("PostProcess",
                Inputs: [new PassInput(offscreen, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(ResourceName.Of("Backbuffer"), ResourceUsage.Present)]));

        var resolved = Ok(RenderGraphCompiler.Compile(passes));

        Assert.Equal(2, resolved.Length);
        Assert.Equal("Geometry", resolved[0].Declaration.Name);
        Assert.Equal("PostProcess", resolved[1].Declaration.Name);

        Assert.Empty(resolved[0].BarriersBefore);

        Assert.Single(resolved[1].BarriersBefore);
        var barrier = resolved[1].BarriersBefore[0];
        Assert.Equal(offscreen, barrier.Resource);
        Assert.Equal(ResourceUsage.ColorAttachmentWrite, barrier.FromUsage);
        Assert.Equal(ResourceUsage.ShaderRead, barrier.ToUsage);
    }

    [Fact]
    public void ReversedDeclaration_StillCorrectOrder()
    {
        var offscreen = ResourceName.Of("OffscreenColor");
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("PostProcess",
                Inputs: [new PassInput(offscreen, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(ResourceName.Of("Backbuffer"), ResourceUsage.Present)]),
            new RenderPassDeclaration("Geometry",
                Inputs: [],
                Outputs: [new PassOutput(offscreen, ResourceUsage.ColorAttachmentWrite)]));

        var resolved = Ok(RenderGraphCompiler.Compile(passes));

        Assert.Equal(2, resolved.Length);
        Assert.Equal("Geometry", resolved[0].Declaration.Name);
        Assert.Equal("PostProcess", resolved[1].Declaration.Name);
    }

    [Fact]
    public void IndependentPasses_BothPresent()
    {
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("A",
                Inputs: [],
                Outputs: [new PassOutput(ResourceName.Of("X"), ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("B",
                Inputs: [],
                Outputs: [new PassOutput(ResourceName.Of("Y"), ResourceUsage.ColorAttachmentWrite)]));

        var resolved = Ok(RenderGraphCompiler.Compile(passes));

        Assert.Equal(2, resolved.Length);
        var names = resolved.Select(r => r.Declaration.Name).ToHashSet();
        Assert.Contains("A", names);
        Assert.Contains("B", names);
    }

    [Fact]
    public void Cycle_ReturnsCycleError()
    {
        var x = ResourceName.Of("X");
        var y = ResourceName.Of("Y");
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("A",
                Inputs: [new PassInput(y, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(x, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("B",
                Inputs: [new PassInput(x, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(y, ResourceUsage.ColorAttachmentWrite)]));

        var error = Err(RenderGraphCompiler.Compile(passes));
        var cycle = Assert.IsType<GraphError.Cycle>(error);
        Assert.Equal(2, cycle.RemainingPasses.Length);
        Assert.Contains("A", cycle.RemainingPasses);
        Assert.Contains("B", cycle.RemainingPasses);
    }

    [Fact]
    public void DuplicateWriter_ReturnsDuplicateWriterError()
    {
        var x = ResourceName.Of("X");
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("A",
                Inputs: [],
                Outputs: [new PassOutput(x, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("B",
                Inputs: [],
                Outputs: [new PassOutput(x, ResourceUsage.ColorAttachmentWrite)]));

        var error = Err(RenderGraphCompiler.Compile(passes));
        var dup = Assert.IsType<GraphError.DuplicateWriter>(error);
        Assert.Equal(x, dup.Resource);
        Assert.Equal("A", dup.FirstPass);
        Assert.Equal("B", dup.SecondPass);
    }

    [Fact]
    public void InputWithoutWriter_ReturnsUnknownResourceError()
    {
        var ghost = ResourceName.Of("Ghost");
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("Reader",
                Inputs: [new PassInput(ghost, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(ResourceName.Of("Out"), ResourceUsage.Present)]));

        var error = Err(RenderGraphCompiler.Compile(passes));
        var unknown = Assert.IsType<GraphError.UnknownResource>(error);
        Assert.Equal(ghost, unknown.Resource);
        Assert.Equal("Reader", unknown.ConsumerPass);
    }

    [Fact]
    public void DiamondDependency_CorrectToposortAndBarriers()
    {
        var x = ResourceName.Of("X");
        var y = ResourceName.Of("Y");
        var z = ResourceName.Of("Z");
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("A",
                Inputs: [],
                Outputs: [new PassOutput(x, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("B",
                Inputs: [new PassInput(x, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(y, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("C",
                Inputs: [new PassInput(x, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(z, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("D",
                Inputs: [new PassInput(y, ResourceUsage.ShaderRead), new PassInput(z, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(ResourceName.Of("Final"), ResourceUsage.Present)]));

        var resolved = Ok(RenderGraphCompiler.Compile(passes));

        Assert.Equal(4, resolved.Length);
        Assert.Equal("A", resolved[0].Declaration.Name);

        var names = resolved.Select(r => r.Declaration.Name).ToList();
        Assert.True(names.IndexOf("B") < names.IndexOf("D"));
        Assert.True(names.IndexOf("C") < names.IndexOf("D"));

        var dPass = resolved.First(r => r.Declaration.Name == "D");
        Assert.Equal(2, dPass.BarriersBefore.Length);
    }

    [Fact]
    public void ResourceName_Create_RejectsNullEmptyAndWhitespace()
    {
        Assert.True(ResourceName.Create(null!).IsError);
        Assert.True(ResourceName.Create("").IsError);
        Assert.True(ResourceName.Create("   ").IsError);
    }

    [Fact]
    public void ResourceName_Create_AcceptsValidName()
    {
        var result = ResourceName.Create("GBuffer.Albedo");
        Assert.True(result.IsOk);
        result.Match(
            ok: r => { Assert.Equal("GBuffer.Albedo", r.Name); return 0; },
            error: _ => throw new Xunit.Sdk.XunitException("Expected Ok"));
    }

    [Fact]
    public void ResourceName_Of_ThrowsOnInvalid()
    {
        Assert.Throws<ArgumentException>(() => ResourceName.Of(""));
    }
}
