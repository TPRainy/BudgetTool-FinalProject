namespace BudgetTool;

public static class FileErrors
{
    public static Error UnsupportedType(string path) => new(
        "file.unsupported_type",
        "data file '" + path + "' is not a .json file",
        ErrorKind.Operational);

    public static Error NotFound(string path) => new(
        "file.not_found",
        "data file '" + path + "' was not found",
        ErrorKind.Operational);

    public static Error Unreadable(string path, string detail) => new(
        "file.unreadable",
        "data file '" + path + "' cannot be read: " + detail,
        ErrorKind.Operational);

    public static Error Malformed(string path, string detail) => new(
        "file.malformed",
        "data file '" + path + "' is malformed: " + detail,
        ErrorKind.Operational);
}
