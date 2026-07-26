using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;

namespace Direct2dCad.Benchmarks;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class CadBenchmarkAttribute : Attribute, IConfigSource
{
    public IConfig Config { get; } = ManualConfig
        .CreateEmpty()
        .AddDiagnoser(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default)
        .AddColumn(BenchmarkDotNet.Columns.CategoriesColumn.Default)
        .AddColumn(BenchmarkDotNet.Columns.RankColumn.Arabic)
        .AddColumn(BenchmarkDotNet.Columns.StatisticColumn.P95)
        .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest))
        .WithOptions(ConfigOptions.JoinSummary);
}
