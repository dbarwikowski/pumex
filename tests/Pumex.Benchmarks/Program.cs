using BenchmarkDotNet.Running;

namespace Pumex.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        // BenchmarkSwitcher exposes the standard CLI: --filter, --list, --job dry, etc.
        // Run a single class with: dotnet run -c Release --project tests/Pumex.Benchmarks --
        //                          --filter "*Search*"
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
