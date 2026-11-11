using System.Runtime.InteropServices;

namespace BudgetTool;

public static class NativeMethods
{
    public const string LibraryName = "budgetcalc";

    [DllImport(LibraryName, EntryPoint = "bt_usage_percent", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    public static extern long UsagePercent(long spentCents, long limitCents);
}
