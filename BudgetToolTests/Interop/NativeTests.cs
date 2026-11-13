using System.Runtime.InteropServices;
using Xunit;

namespace BudgetTool.Tests;

public class NativeTests
{
    [DllImport("budgetcalc_missing_library", EntryPoint = "bt_usage_percent", CallingConvention = CallingConvention.Cdecl)]
    private static extern long MissingLibrary(long spentCents, long limitCents);

    [DllImport(NativeMethods.LibraryName, EntryPoint = "bt_function_that_does_not_exist", CallingConvention = CallingConvention.Cdecl)]
    private static extern long MissingFunction(long spentCents, long limitCents);

    private static Error FailureOf<T>(Result<T> result) => Assert.IsType<Failure<T>>(result).Error;

    [Fact]
    public void RealNativeCall_ReturnsTheManuallyCalculatedValue()
    {
        Assert.Equal(133L, NativeMethods.UsagePercent(40_000, 30_000));
        Assert.Equal(13L, NativeMethods.UsagePercent(1, 8));
        Assert.Equal(100L, NativeMethods.UsagePercent(30_000, 30_000));
        Assert.Equal(0L, NativeMethods.UsagePercent(0, 30_000));
        Assert.Equal(-1L, NativeMethods.UsagePercent(-1, 30_000));
        Assert.Equal(-1L, NativeMethods.UsagePercent(100, 0));
    }

    [Fact]
    public void Adapter_RealCall_ConvertsMoneyToCents()
    {
        var usage = new NativeUsage();

        Assert.Equal(133L, Assert.IsType<Success<long>>(usage.Percent(400m, 300m)).Value);
        Assert.Equal(89L, Assert.IsType<Success<long>>(usage.Percent(400m, 450m)).Value);
        Assert.Equal(0L, Assert.IsType<Success<long>>(usage.Percent(0m, 300m)).Value);
    }

    [Fact]
    public void Adapter_InvalidInput_IsRejectedBeforeTheNativeCall()
    {
        var calls = 0;
        var usage = new NativeUsage((spent, limit) =>
        {
            calls++;
            return 0;
        });

        Assert.Equal("usage.invalid_input", FailureOf(usage.Percent(-1m, 300m)).Code);
        Assert.Equal("usage.invalid_input", FailureOf(usage.Percent(10m, 0m)).Code);
        Assert.Equal("usage.invalid_input", FailureOf(usage.Percent(10m, 0.004m)).Code);
        Assert.Equal("usage.invalid_input", FailureOf(usage.Percent(NativeUsage.MaxAmount + 1m, 300m)).Code);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Adapter_NegativeNativeAnswer_BecomesRejectedFailure()
    {
        var usage = new NativeUsage((spent, limit) => -1);

        Assert.Equal("usage.rejected", FailureOf(usage.Percent(1m, 2m)).Code);
    }

    [Fact]
    public void MissingLibrary_RealLoadFailure_BecomesResultFailure()
    {
        var error = FailureOf(new NativeUsage(MissingLibrary).Percent(400m, 300m));

        Assert.Equal("native.unavailable", error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
    }

    [Fact]
    public void MissingExport_BindingFailure_BecomesResultFailure()
    {
        var error = FailureOf(new NativeUsage(MissingFunction).Percent(400m, 300m));

        Assert.Equal("native.unavailable", error.Code);
    }
}
