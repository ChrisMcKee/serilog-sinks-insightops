using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Serilog.Sinks.InsightOps.Rapid7;

namespace Benchmark;

#if NET8_0
[MemoryDiagnoser]
public class StringBuilderBenchmark
{

    [Benchmark(Baseline = true)]
    public string UsingStringBuilder()
    {
        var sb = new StringBuilder();

        sb.Append("0000000602000000240000525341310004000");
        sb.Append("x8i9eahae89-aeh8fehiafhipeafhiaehipaefhipaehipfipehafhipeipaeiphfipheh");
        sb.Append("0000000602000000240000525341310004000");
        sb.Append(DateTime.UtcNow.ToLongDateString());
        sb.Append("\r\n");

        return sb.ToString();
    }

    [Benchmark]
    public string UsingStringBuilderCache()
    {
        var sb = StringBuilderCache.Acquire();

        sb.Append("0000000602000000240000525341310004000");
        sb.Append("x8i9eahae89-aeh8fehiafhipeafhiaehipaefhipaehipfipehafhipeipaeiphfipheh");
        sb.Append("0000000602000000240000525341310004000");
        sb.Append(DateTime.UtcNow.ToLongDateString());
        sb.Append("\r\n");

        return StringBuilderCache.GetStringAndRelease(sb);
    }
}
#endif
