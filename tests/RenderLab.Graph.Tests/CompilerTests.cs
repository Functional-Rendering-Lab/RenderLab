using System.Collections.Immutable;
using RenderLab.Graph;

namespace RenderLab.Graph.Tests;

public class CompilerTests
{
    [Fact]
    public void SinglePass_NoBarriers()
    {
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("Only",
                Inputs: [],
                Outputs: [new PassOutput(ResourceName.Of("Color"), ResourceUsage.ColorAttachmentWrite)]));

        var resolved = RenderGraphCompiler.Compile(passes);

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

        var resolved = RenderGraphCompiler.Compile(passes);

        Assert.Equal(2, resolved.Length);
        Assert.Equal("Geometry", resolved[0].Declaration.Name);
        Assert.Equal("PostProcess", resolved[1].Declaration.Name);

        // Geometry has no barriers
        Assert.Empty(resolved[0].BarriersBefore);

        // PostProcess has one barrier: OffscreenColor transitions from write to read
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
        // Declare PostProcess first, Geometry second — compiler should sort correctly
        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("PostProcess",
                Inputs: [new PassInput(offscreen, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(ResourceName.Of("Backbuffer"), ResourceUsage.Present)]),
            new RenderPassDeclaration("Geometry",
                Inputs: [],
                Outputs: [new PassOutput(offscreen, ResourceUsage.ColorAttachmentWrite)]));

        var resolved = RenderGraphCompiler.Compile(passes);

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

        var resolved = RenderGraphCompiler.Compile(passes);

        Assert.Equal(2, resolved.Length);
        var names = resolved.Select(r => r.Declaration.Name).ToHashSet();
        Assert.Contains("A", names);
        Assert.Contains("B", names);
    }

    [Fact]
    public void CycleDetection_Throws()
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

        Assert.Throws<InvalidOperationException>(() => RenderGraphCompiler.Compile(passes));
    }

    [Fact]
    public void DiamondDependency_CorrectToposortAndBarriers()
    {
        // A writes X, B reads X writes Y, C reads X writes Z, D reads Y and Z
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

        var resolved = RenderGraphCompiler.Compile(passes);

        Assert.Equal(4, resolved.Length);

        // A must come first
        Assert.Equal("A", resolved[0].Declaration.Name);

        // B and C must come before D
        var names = resolved.Select(r => r.Declaration.Name).ToList();
        Assert.True(names.IndexOf("B") < names.IndexOf("D"));
        Assert.True(names.IndexOf("C") < names.IndexOf("D"));

        // D should have barriers for Y and Z (both transition from write to read)
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
