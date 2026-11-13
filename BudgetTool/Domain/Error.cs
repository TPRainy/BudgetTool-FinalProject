namespace BudgetTool;

public enum ErrorKind { Domain, Operational }

public sealed record Error
{
    public Error(string code, string message, ErrorKind kind)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("an error needs a code", nameof(code));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("an error needs a message", nameof(message));

        Code = code;
        Message = message;
        Kind = kind;
    }

    public string Code { get; }
    public string Message { get; }
    public ErrorKind Kind { get; }
}
