using System;
using System.Threading;
using BenchmarkDotNet.Running;

namespace Benchmark;

internal static class Program
{
    public static int FakeLogPort { get; private set; }
    private static void Main(string[] args)
    {
        FakeLogPort = new Random().Next(5000, 5999);
        Thread listenerThread = new Thread(() => FakeRapid7.StartFakeLogEndpoint(FakeLogPort));
        listenerThread.IsBackground = true;
        listenerThread.Start();

#if !DEBUG
        BenchmarkRunner.Run<AsyncClientBenchmark>();
        BenchmarkRunner.Run<LoggerBenchmark>();
#else
        BenchmarkRunner.Run<AsyncClientBenchmark>(new BenchmarkDotNet.Configs.DebugInProcessConfig());
#endif
    }
}
