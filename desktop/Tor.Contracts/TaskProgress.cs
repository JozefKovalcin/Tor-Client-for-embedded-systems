using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Numerics;

namespace Tor.Contracts;

/// <summary>Binding for the unchanged W1 TaskProgress v1 wire contract.</summary>
public sealed record TaskProgress(
    int SchemaVersion, string TaskId, string? ParentTaskId, long Sequence,
    DateTimeOffset TimestampUtc, string Phase, string State,
    double? Completed, double? Total, string? Unit, string Message,
    bool CancellationSupported, string? RetryOfTaskId, uint? RawExitCode,
    int? TerminationSignal, string? ReasonCode, string? LogRef)
{
    public void Validate()
    {
        if (SchemaVersion != 1 || Sequence < 0 || TimestampUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Unsupported progress version/sequence/time");
        ContractReference.Require(TaskId);
        foreach (var value in new[] { ParentTaskId, RetryOfTaskId, LogRef })
            if (value is not null) ContractReference.Require(value);
        if (State is not ("queued" or "running" or "cancelling" or "cancelled" or
            "succeeded" or "failed" or "blocked" or "interrupted"))
            throw new InvalidDataException("Unknown task state");
        if (string.IsNullOrWhiteSpace(Phase) || string.IsNullOrWhiteSpace(Message))
            throw new InvalidDataException("Progress requires phase/message");
        if (Completed.HasValue != Total.HasValue || (Completed is null) != (Unit is null))
            throw new InvalidDataException("Progress must be entirely determinate or indeterminate");
        if (Completed is { } done && (!double.IsFinite(done) || !double.IsFinite(Total!.Value) ||
            done < 0 || Total.Value <= 0 || done > Total.Value || Unit is not ("B" or "steps" or "%")))
            throw new InvalidDataException("Invalid progress bounds");
        if (Unit == "%" && Total != 100 || ParentTaskId == TaskId || RetryOfTaskId == TaskId)
            throw new InvalidDataException("Invalid percentage or self reference");
        if (TerminationSignal is < 1 or > 127 || (TerminationSignal is not null && RawExitCode is not null))
            throw new InvalidDataException("Signal and exit must remain distinct");
        if (State == "interrupted" && ReasonCode is not ("app-crash" or "runner-crash" or "runner-lost"))
            throw new InvalidDataException("Interrupted tasks require a crash/loss reason");
        if (State == "succeeded" && (RawExitCode is not (null or 0) || TerminationSignal is not null))
            throw new InvalidDataException("Success cannot carry a failed process result");
    }
}

public static class ContractJson
{
    public static JsonSerializerOptions Options => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new IntegralJsonNumberConverterFactory() },
        WriteIndented = true
    };
}

/// <summary>JSON Schema integers are numeric values, not a restriction to integer lexical spelling.</summary>
internal sealed class IntegralJsonNumberConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(uint);
    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) => type == typeof(int)
        ? new IntegralJsonNumberConverter<int>() : type == typeof(long)
        ? new IntegralJsonNumberConverter<long>() : new IntegralJsonNumberConverter<uint>();
    private sealed class IntegralJsonNumberConverter<T> : JsonConverter<T> where T : struct, IBinaryInteger<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException("Finite integral JSON number required");
            using var parsed = JsonDocument.ParseValue(ref reader);
            var literal = parsed.RootElement.GetRawText();
            if (literal.Length > 128) throw new JsonException("Integral number spelling exceeds binding limit");
            var pieces = literal.Split(['e', 'E']);
            var point = pieces[0].IndexOf('.');
            var digits = pieces[0].Replace(".", "", StringComparison.Ordinal);
            var value = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            if (value.IsZero) return T.Zero;
            if (pieces.Length == 2 && !int.TryParse(pieces[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                throw new JsonException("Integral number exponent exceeds binding range");
            long exponent = pieces.Length == 2 ? int.Parse(pieces[1], CultureInfo.InvariantCulture) : 0;
            long scale = (point < 0 ? 0 : pieces[0].Length - point - 1) - exponent;
            if (scale > 0)
            {
                if (scale > digits.Length) throw new JsonException("Fractional JSON number is not an integer");
                var denominator = BigInteger.Pow(10, (int)scale);
                if (value % denominator != 0) throw new JsonException("Fractional JSON number is not an integer");
                value /= denominator;
            }
            else
            {
                if (-scale > 20) throw new JsonException("Integral number exceeds binding range");
                value *= BigInteger.Pow(10, (int)-scale);
            }
            try { return T.CreateChecked(value); }
            catch (OverflowException) { throw new JsonException("Integral number exceeds binding range"); }
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.ToString(null, CultureInfo.InvariantCulture));
    }
}

public static class ContractReference
{
    private static readonly Regex Shape = new(@"\A[A-Za-z0-9][A-Za-z0-9_.-]*(?:/[A-Za-z0-9][A-Za-z0-9_.-]*)*\z", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> PrivateSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "control_auth_cookie", "hs_ed25519_secret_key", "private_key", "DataDirectory",
        "hidden_service", "data-onion", "onion-service"
    };
    public static void Require(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512 || !Shape.IsMatch(value) ||
            value.Split('/').Any(PrivateSegments.Contains) ||
            new[] { ".pem", ".key", ".cookie", ".pfx", ".p12" }.Any(x => value.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unsafe contract reference");
    }
}
