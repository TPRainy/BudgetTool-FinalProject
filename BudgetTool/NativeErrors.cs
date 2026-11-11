using System.Globalization;

namespace BudgetTool;

public static class NativeErrors
{
    public static Error InvalidInput(string argument, decimal value) => new(
        "usage.invalid_input",
        argument + " " + value.ToString(CultureInfo.InvariantCulture) + " is outside the accepted range"
            + " (spent 0.00 to " + Max() + ", limit 0.01 to " + Max() + ")",
        ErrorKind.Domain);

    public static Error Rejected(long spentCents, long limitCents) => new(
        "usage.rejected",
        "the native usage calculation rejected spent=" + spentCents + " cents, limit=" + limitCents + " cents",
        ErrorKind.Domain);

    public static Error Unavailable(string detail) => new(
        "native.unavailable",
        "native library '" + NativeMethods.LibraryName + "' cannot be used: " + detail,
        ErrorKind.Operational);

    private static string Max() => NativeUsage.MaxAmount.ToString("F2", CultureInfo.InvariantCulture);
}
