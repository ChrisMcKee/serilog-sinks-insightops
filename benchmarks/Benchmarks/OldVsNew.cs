using System;
using System.Collections.Generic;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Serilog.Sinks.InsightOps.Rapid7;
using WaffleGenerator;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class AsyncClientBenchmark
{
    public IEnumerable<object[]> Data()
    {
        for (int i = 0; i < 3; i++)
        {
            var text = WaffleEngine.Text(paragraphs: 1, includeHeading: false);
            yield return [text];
        }
    }

    readonly InsightCore.Net.AsyncLogger _classic;
    readonly AsyncLogger _newAsyncLogger;

    public AsyncClientBenchmark()
    {
        _classic = new InsightCore.Net.AsyncLogger();
        _classic.setDataHubAddr("localhost");
        _classic.setDataHubPort(Program.FakeLogPort);
        _classic.setIsUsingDataHub(true);

        _newAsyncLogger = new AsyncLogger();
        _newAsyncLogger.SetDataHubAddr("localhost");
        _newAsyncLogger.SetDataHubPort(Program.FakeLogPort);
        _newAsyncLogger.SetIsUsingDataHub(true);
    }

    [Benchmark(Baseline = true)]
    [ArgumentsSource(nameof(Data))]
    public void TestLog(string log)
    {
        _classic.AddLine(log);
    }

    [Benchmark]
    [ArgumentsSource(nameof(Data))]
    public void TestLogNew(string log)
    {
        _newAsyncLogger.QueueLogEvent(log);
    }
}
