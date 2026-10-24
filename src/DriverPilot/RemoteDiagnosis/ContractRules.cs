using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace DriverPilot.RemoteDiagnosis;

/// <summary>Limits and text markers shared with the server: contracts/remote-diagnosis/v1/rules.json and errors.json.</summary>
public sealed class ContractRules
{
    public const string ReportSchemaVersion = "1.1";
    public int MaxUploadBytes { get; }
    public int MaxResultBytes { get; }
    public int MaxScanAgeSeconds { get; }
    public int MaxObservationWindowSeconds { get; }
    public int PollInitialSeconds { get; }
    public int PollMaxBackoffSeconds { get; }
    public IReadOnlySet<string> ErrorCodes { get; }
    readonly Regex[] forbiddenText;

    public static ContractRules Embedded { get; } = new(ReadResource("rules.json"), ReadResource("errors.json"));

    public ContractRules(string rulesJson, string errorsJson)
    {
        using var rules = JsonDocument.Parse(rulesJson);
        var limits = rules.RootElement.GetProperty("limits");
        MaxUploadBytes = limits.GetProperty("max_upload_bytes").GetInt32();
        MaxResultBytes = limits.GetProperty("max_result_bytes").GetInt32();
        MaxScanAgeSeconds = limits.GetProperty("max_scan_age_seconds").GetInt32();
        MaxObservationWindowSeconds = limits.GetProperty("max_observation_window_seconds").GetInt32();
        var polling = rules.RootElement.GetProperty("polling");
        PollInitialSeconds = polling.GetProperty("initial_seconds").GetInt32();
        PollMaxBackoffSeconds = polling.GetProperty("max_backoff_seconds").GetInt32();
        forbiddenText = rules.RootElement.GetProperty("forbidden_text").GetProperty("patterns").EnumerateArray()
            .Select(p => new Regex(p.GetProperty("pattern").GetString()!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .ToArray();
        using var errors = JsonDocument.Parse(errorsJson);
        ErrorCodes = errors.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Conservative: a pattern that cannot be evaluated in time counts as a match.</summary>
    public bool HasForbiddenText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        try { return forbiddenText.Any(pattern => pattern.IsMatch(value)); }
        catch (RegexMatchTimeoutException) { return true; }
    }

    static string ReadResource(string name)
    {
        using var stream = typeof(ContractRules).Assembly.GetManifestResourceStream("DriverPilot.Contracts." + name)
            ?? throw new InvalidOperationException("Missing contract resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Value formats of common.schema.json, used for outgoing data and strict response checks.</summary>
public static class ContractFormat
{
    static readonly RegexOptions Options = RegexOptions.CultureInvariant;
    static readonly Regex UuidPattern = new(@"\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z", Options);
    static readonly Regex TimestampPattern = new(@"\A[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{1,7})?Z\z", Options);
    static readonly Regex DatePattern = new(@"\A[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])\z", Options);
    static readonly Regex Sha256Pattern = new(@"\A[0-9a-f]{64}\z", Options);
    static readonly Regex LocalIdPattern = new(@"\A[a-z0-9][a-z0-9-]{0,31}\z", Options);
    static readonly Regex VersionLabelPattern = new(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,31}\z", Options);

    public static bool IsUuid(string? value) => value != null && UuidPattern.IsMatch(value);
    public static bool IsTimestamp(string? value) => value != null && TimestampPattern.IsMatch(value);
    public static bool IsDate(string? value) => value != null && DatePattern.IsMatch(value);
    public static bool IsSha256(string? value) => value != null && Sha256Pattern.IsMatch(value);
    public static bool IsLocalId(string? value) => value != null && LocalIdPattern.IsMatch(value);
    public static bool IsVersionLabel(string? value) => value != null && VersionLabelPattern.IsMatch(value);

    public static string Uuid(Guid value) => value.ToString("D");
    public static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    public static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

    /// <summary>Schema lengths count Unicode code points, not UTF-16 units.</summary>
    public static int Length(string value) => value.EnumerateRunes().Count();

    /// <summary>free_text: tab, line feed and carriage return are the only control characters allowed.</summary>
    public static bool IsFreeText(string? value, int maxLength, int minLength = 1)
        => value != null && Length(value) >= minLength && Length(value) <= maxLength
           && !value.Any(c => c == '\u007F' || (c < ' ' && c is not ('\t' or '\n' or '\r')));

    /// <summary>technical_text: one line without angle brackets, backtick or backslash.</summary>
    public static bool IsTechnicalText(string? value)
        => value != null && Length(value) is >= 1 and <= 160 && !value.Any(c => c < ' ' || c is '\u007F' or '<' or '>' or '`' or '\\');
}
