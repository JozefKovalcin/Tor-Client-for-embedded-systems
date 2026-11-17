using System.Globalization;
using System.Text.Json;

namespace TorManager.Desktop.Presentation;

// Internal display projections, not replacement W1 contracts or validators.
// Production feeds must validate full W1 v1 schemas before crossing this seam.
internal sealed record TaskCard(string TaskId, long Sequence, string Phase, string State,
    double? Completed, double? Total, string? Unit, string Message, long? RawExitCode,
    string? ReasonCode, string? LogRef)
{
    public string StateLabel => State == "succeeded" ? "Completed (succeeded)" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(State);
    public string StateColor => State switch
    {
        "succeeded" => "#7DDCB0", "failed" => "#FF9F9F", "blocked" => "#E8C36B",
        "interrupted" => "#D4ADF9", "running" => "#8ABEFF", _ => "#BEC8D4"
    };
    public bool IsIndeterminate => Completed is null;
    public bool IsActiveIndeterminate => IsIndeterminate && State is "running" or "cancelling";
    public bool ShowProgress => !IsIndeterminate || IsActiveIndeterminate;
    public double ProgressValue => Total is > 0 ? 100 * (Completed!.Value / Total.Value) : 0;
    public string ProgressText => IsIndeterminate ? "Progress unavailable / indeterminate" :
        FormattableString.Invariant($"{Completed:0.##} / {Total:0.##} {Unit}");
    public string Detail => string.Join(" · ", new[]
    {
        ReasonCode is null ? null : "Reason: " + ReasonCode,
        RawExitCode is null ? null : "Exit code: " + RawExitCode.Value.ToString(CultureInfo.InvariantCulture),
        LogRef is null ? null : "Log reference: " + LogRef
    }.Where(value => value is not null));

    public static TaskCard FromJson(JsonElement value)
    {
        JsonFields.Version(value);
        var state = JsonFields.Text(value, "state");
        if (!new[] { "queued", "running", "cancelling", "cancelled", "succeeded", "failed", "blocked", "interrupted" }.Contains(state))
            throw new JsonException("Unknown task state");
        double? completed = JsonFields.Number(value, "completed"), total = JsonFields.Number(value, "total");
        var unit = JsonFields.NullableText(value, "unit");
        if ((completed is null || total is null || unit is null) && (completed is not null || total is not null || unit is not null))
            throw new JsonException("Partial progress");
        if (completed < 0 || total <= 0 || completed > total ||
            (unit is not null && !new[] { "B", "steps", "%" }.Contains(unit)) || unit == "%" && total != 100)
            throw new JsonException("Invalid progress");
        var reason = JsonFields.NullableText(value, "reasonCode");
        if (state == "interrupted" && !new[] { "app-crash", "runner-crash", "runner-lost" }.Contains(reason))
            throw new JsonException("Missing interruption reason");
        var exit = JsonFields.Integer(value, "rawExitCode");
        if (exit is < 0 or > uint.MaxValue || state == "succeeded" && exit is not null and not 0)
            throw new JsonException("Invalid exit result");
        var sequence = JsonFields.Integer(value, "sequence");
        if (sequence is null or < 0) throw new JsonException("Invalid task sequence");
        return new(JsonFields.Text(value, "taskId"), sequence.Value,
            JsonFields.Text(value, "phase"), state, completed, total, unit,
            JsonFields.Text(value, "message"), exit, reason, JsonFields.NullableText(value, "logRef"));
    }
}

internal sealed record DependencyCard(string Id, string Status, string Requirement, string Version, string Reason)
{
    public string StateColor => Status switch
    {
        "AVAILABLE" => "#7DDCB0", "INCOMPATIBLE" => "#FF9F9F", "BLOCKED" => "#E8C36B", _ => "#BEC8D4"
    };
    public static DependencyCard FromJson(JsonElement value)
    {
        JsonFields.Version(value);
        var status = JsonFields.Text(value, "status");
        if (!new[] { "AVAILABLE", "MISSING", "INCOMPATIBLE", "UNKNOWN", "BLOCKED" }.Contains(status))
            throw new JsonException("Unknown dependency classification");
        return new(JsonFields.Text(value, "id"), status, JsonFields.Text(value, "requirement"),
            JsonFields.NullableText(value, "detectedVersion") ?? "Not observed", JsonFields.Text(value, "reason"));
    }
}

internal static class JsonFields
{
    public static void Version(JsonElement value)
    {
        if (Integer(value, "schemaVersion") != 1)
            throw new JsonException("Unsupported schemaVersion");
    }
    public static string Text(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.String &&
        value.GetProperty(name).GetString() is { } text && !string.IsNullOrWhiteSpace(text)
        ? text : throw new JsonException("Expected text field");
    public static string? NullableText(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Text(value, name);
    public static double? Number(JsonElement value, string name)
    {
        var item = value.GetProperty(name);
        if (item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new JsonException("Invalid/nonfinite number");
        return number;
    }
    public static long? Integer(JsonElement value, string name)
    {
        var item = value.GetProperty(name);
        if (item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind != JsonValueKind.Number) throw new JsonException("Non-numeric integer");
        if (item.TryGetInt64(out var exact)) return exact;
        if (!item.TryGetDecimal(out var number) || decimal.Truncate(number) != number ||
            number < long.MinValue || number > long.MaxValue ||
            !item.TryGetDouble(out var finite) || !double.IsFinite(finite) || (double)number != finite)
            throw new JsonException("Nonintegral number");
        return checked((long)number);
    }
}
