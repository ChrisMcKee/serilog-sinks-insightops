using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;

namespace Benchmark;

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        var baseConfig = Job.ShortRun.WithIterationCount(1000).WithWarmupCount(1);

        this.AddJob(baseConfig.WithRuntime(CoreRuntime.Core80).WithJit(Jit.RyuJit).WithPlatform(Platform.X64));
        this.AddJob(baseConfig.WithRuntime(ClrRuntime.Net48).WithJit(Jit.RyuJit).WithPlatform(Platform.X64));

        this.AddExporter(MarkdownExporter.GitHub);
        this.AddExporter(CsvExporter.Default);
        this.AddExporter(RPlotExporter.Default);

        this.AddDiagnoser(MemoryDiagnoser.Default);
    }

}
