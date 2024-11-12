using BenchmarkDotNet.Running;

namespace Benchmark;

internal static class Program
{
    private static void Main(string[] args)
    {
#if !DEBUG
        BenchmarkRunner.Run<AsyncClientBenchmark>();
#else
        BenchmarkRunner.Run<AsyncClientBenchmark>(new BenchmarkDotNet.Configs.DebugInProcessConfig());
#endif
    }
}
