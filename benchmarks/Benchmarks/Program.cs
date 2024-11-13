using BenchmarkDotNet.Running;

namespace Benchmark;

internal static class Program
{
    private static void Main(string[] args)
    {
#if !DEBUG
        BenchmarkRunner.Run<AsyncClientBenchmark>();
        BenchmarkRunner.Run<LoggerBenchmark>();
#else
        BenchmarkRunner.Run<AsyncClientBenchmark>(new BenchmarkDotNet.Configs.DebugInProcessConfig());
#endif
    }
}
