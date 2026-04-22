using RenderLab.Gpu;
using Silk.NET.Vulkan;

namespace RenderLab.Ui.ImGui;

/// <summary>
/// Records GPU-side timestamp queries to measure per-pass execution time.
/// Each pass gets a begin/end timestamp pair; results are read back the following frame.
/// </summary>
public sealed class GpuTimestamps : IDisposable
{
    private readonly GpuState _state;
    private readonly QueryPool _queryPool;
    private readonly uint _queryCount;
    private readonly float _timestampPeriod; // nanoseconds per tick
    private readonly ulong[] _results;
    private readonly string[] _labels;
    private readonly double[] _timingsMs;
    private uint _nextQuery;

    public ReadOnlySpan<double> TimingsMs => _timingsMs.AsSpan(0, (int)Math.Min(_nextQuery / 2, (uint)_timingsMs.Length));
    public ReadOnlySpan<string> Labels => _labels.AsSpan(0, (int)Math.Min(_nextQuery / 2, (uint)_labels.Length));

    private GpuTimestamps(GpuState state, QueryPool pool, uint queryCount, float timestampPeriod)
    {
        _state = state;
        _queryPool = pool;
        _queryCount = queryCount;
        _timestampPeriod = timestampPeriod;
        _results = new ulong[queryCount];
        _labels = new string[queryCount / 2];
        _timingsMs = new double[queryCount / 2];
    }

    /// <summary>
    /// Creates a timestamp query pool sized for <paramref name="passCount"/> passes
    /// (2 queries per pass: begin + end).
    /// </summary>
    public static unsafe GpuTimestamps Create(GpuState state, uint passCount)
    {
        var period = state.Capabilities.TimestampPeriod;

        // 2 queries per pass (begin + end)
        uint queryCount = passCount * 2;

        var poolInfo = new QueryPoolCreateInfo
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = queryCount,
        };

        if (state.Vk.CreateQueryPool(state.Device, &poolInfo, null, out var pool) != Result.Success)
            throw new InvalidOperationException("Failed to create timestamp query pool.");

        return new GpuTimestamps(state, pool, queryCount, period);
    }

    public unsafe void Reset(Vk vk, CommandBuffer cmd)
    {
        _nextQuery = 0;
        vk.CmdResetQueryPool(cmd, _queryPool, 0, _queryCount);
    }

    public unsafe void BeginPass(Vk vk, CommandBuffer cmd, string label)
    {
        if (_nextQuery >= _queryCount) return;
        uint pairIndex = _nextQuery / 2;
        if (pairIndex < (uint)_labels.Length)
            _labels[pairIndex] = label;
        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, _queryPool, _nextQuery++);
    }

    public unsafe void EndPass(Vk vk, CommandBuffer cmd)
    {
        if (_nextQuery >= _queryCount) return;
        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, _queryPool, _nextQuery++);
    }

    public unsafe void ReadResults()
    {
        uint count = _nextQuery;
        if (count < 2) return;

        fixed (ulong* pResults = _results)
        {
            var result = _state.Vk.GetQueryPoolResults(
                _state.Device, _queryPool, 0, count,
                (nuint)(count * sizeof(ulong)), pResults,
                (ulong)sizeof(ulong), QueryResultFlags.Result64Bit);

            if (result != Result.Success) return;
        }

        for (uint i = 0; i + 1 < count; i += 2)
        {
            uint pairIndex = i / 2;
            if (pairIndex >= (uint)_timingsMs.Length) break;
            ulong delta = _results[i + 1] - _results[i];
            _timingsMs[pairIndex] = delta * _timestampPeriod / 1_000_000.0; // ns -> ms
        }
    }

    public unsafe void Dispose()
    {
        _state.Vk.DestroyQueryPool(_state.Device, _queryPool, null);
    }
}
