namespace Tests;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Xunit;

public class BenchmarkTests(Xunit.Abstractions.ITestOutputHelper output)
{
    //[Fact]
    public void BenchmarkDotNet_Perf()
    {
        var logger = new AccumulationLogger();
        BenchmarkRunner.Run<FloatingBenchmarks>(DefaultConfig.Instance.AddLogger(logger));
        output.WriteLine(logger.GetLog());
    }
 //   IterationCount=400

 //| Method     | Mean       | Error   | StdDev   | Median     | Ratio | RatioSD |
 //|----------- |-----------:|--------:|---------:|-----------:|------:|--------:|
 //| DoubleAdd  |   249.3 ns | 0.09 ns |  0.50 ns |   249.2 ns |  1.00 |    0.00 |
 //| DecimalAdd | 5,501.9 ns | 4.94 ns | 29.79 ns | 5,504.4 ns | 22.06 |    0.15 |
}

[IterationCount(400)]
public class FloatingBenchmarks
{
    decimal m1;
    double d1;
    decimal m2;
    double d2;

    [GlobalSetup]
    public void Setup()
    {
        d1 = 12345.6789;
        d2 = 1234.56778;
        m1 = 12345.6789M;
        m2 = 1234.56778M;
    }

    [Benchmark(Baseline = true)]
    public double DoubleAdd()
    {
        var l1 = d1;
        var l2 = d2;
        return l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2;
    }

    [Benchmark]
    public decimal DecimalAdd()
    {
        var l1 = m1;
        var l2 = m2;
        return l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2;
    }
}