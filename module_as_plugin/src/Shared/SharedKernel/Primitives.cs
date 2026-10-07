using System.Security.Cryptography;

namespace SharedKernel;

// CUID-style ids (§SR-9): cuid prefix + 24 lowercase alphanumerics.
public static class Cuid
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string New(string prefix = "c")
    {
        Span<byte> bytes = stackalloc byte[18];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[24];
        for (var i = 0; i < 24; i++)
            chars[i] = Alphabet[bytes[i % bytes.Length] % Alphabet.Length];
        return $"{prefix}_{new string(chars)}";
    }
}

// Minimal Result type to keep Application layer free of exceptions for expected failures.
public sealed class Result<T>
{
    public bool Ok { get; }
    public T? Value { get; }
    public ErrorEnvelope? Error { get; }

    private Result(T value) { Ok = true; Value = value; }
    private Result(ErrorEnvelope error) { Ok = false; Error = error; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Fail(string code, string message, object? details = null, bool retryable = false)
        => new(new ErrorEnvelope(code, message, details, retryable));
}
