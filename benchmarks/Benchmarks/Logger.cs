using System;
using System.Collections.Generic;
using System.Threading;
using Benchmark.Serilog_Sinks_InsightOps_3_1_0;
using BenchmarkDotNet.Attributes;
using Serilog;
using Serilog.Sinks.InsightOps;
using WaffleGenerator;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class LoggerBenchmark
{
    public IEnumerable<object[]> Data()
    {
        for (int i = 0; i < 3; i++)
        {
            var text = WaffleEngine.Text(paragraphs: 1, includeHeading: false);
            yield return [text];
        }
    }

    readonly ILogger _classic;
    readonly ILogger _newAsyncLogger;

    public LoggerBenchmark()
    {
        _classic = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.InsightOps_3_1_0(new InsightOpsSinkSettings_3_1_0
            {
                DataHubAddress = "localhost",
                DataHubPort = Program.FakeLogPort,
                IsUsingDataHub = true
            })
            .CreateLogger();

        // Create our logger.
        _newAsyncLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.InsightOps(new InsightOpsSinkSettings
            {
                DataHubAddress = "localhost",
                DataHubPort = Program.FakeLogPort,
                IsUsingDataHub = true
            })
            .CreateLogger();

    }

    [Benchmark(Baseline = true)]
    [ArgumentsSource(nameof(Data))]
    public void TestLog(string log)
    {
        _classic.Information("msg={Log}", log);
    }

    [Benchmark]
    [ArgumentsSource(nameof(Data))]
    public void TestLogNew(string log)
    {
        _newAsyncLogger.Information("msg={Log}", log);
    }
}
