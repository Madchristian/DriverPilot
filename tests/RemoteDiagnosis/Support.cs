using System.Net;
using System.Text;
using System.Text.Json;
using DriverPilot.RemoteDiagnosis;
using Json.Schema;
namespace DriverPilot.Tests;

static class Assert
{
    public static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); }
    public static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("FAIL: expected " + typeof(T).Name + ": " + name);
    }
}

/// <summary>The contract as checked in: schemas and fixtures are read from contracts/remote-diagnosis/v1.</summary>
static class Contract
{
    public static readonly string Root = Locate();
    static readonly Dictionary<string, JsonSchema> Schemas = Load();

    static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "contracts", "remote-diagnosis", "v1");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("contracts/remote-diagnosis/v1 not found above " + AppContext.BaseDirectory);
    }

    static Dictionary<string, JsonSchema> Load()
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        var names = Directory.GetFiles(Path.Combine(Root, "schemas"), "*.schema.json").Select(Path.GetFileName).Select(n => n!)
            // References are resolved while building: shared definitions first, then the embedded result.
            .OrderBy(n => n == "common.schema.json" ? 0 : n == "result.schema.json" ? 1 : 2).ThenBy(n => n, StringComparer.Ordinal);
        var schemas = new Dictionary<string, JsonSchema>();
        foreach (var name in names)
        {
            var uri = new Uri("https://contract.invalid/schemas/" + name);
            var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(Root, "schemas", name)), options, uri);
            options.SchemaRegistry.Register(uri, schema);
            schemas[name.Replace(".schema.json", "")] = schema;
        }
        return schemas;
    }

    public static bool Conforms(string schema, ReadOnlySpan<byte> json)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        return Schemas[schema].Evaluate(document.RootElement).IsValid;
    }

    public static byte[] Fixture(string relativePath) => File.ReadAllBytes(Path.Combine(Root, "fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar)));
    public static IEnumerable<string> Fixtures(string kind, string schema)
        => Directory.GetFiles(Path.Combine(Root, "fixtures", kind, schema), "*.json").OrderBy(p => p, StringComparer.Ordinal);
}

sealed record Exchange(HttpMethod Method, string Path, string? Authorization, string? IdempotencyKey, string? IfNoneMatch, string? ContentType, bool HasContentEncoding, byte[] Body);

/// <summary>Scripted server: each request is recorded and answered by the next queued reply.</summary>
sealed class FakeServer : HttpMessageHandler
{
    public List<Exchange> Requests { get; } = [];
    readonly Queue<Func<HttpResponseMessage>> replies = new();

    public void Reply(HttpStatusCode status, byte[]? json = null, string? etag = null, int? retryAfter = null, string contentType = "application/json")
        => replies.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status);
            if (json != null) { response.Content = new ByteArrayContent(json); response.Content.Headers.ContentType = new(contentType); }
            if (etag != null) response.Headers.TryAddWithoutValidation("ETag", etag);
            if (retryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter.Value.ToString());
            return response;
        });
    public void Reply(HttpStatusCode status, string json, string? etag = null) => Reply(status, Encoding.UTF8.GetBytes(json), etag);
    public void ReplyError(HttpStatusCode status, string code, int? retryAfter = null)
        => Reply(status, JsonSerializer.SerializeToUtf8Bytes(new { error = new { code, message = "synthetic" }, request_id = "3e2d1c0b-9a8f-4e7d-8c6b-5a4f3e2d1c0b", retry_after_seconds = retryAfter }), retryAfter: retryAfter);
    public void Fail() => replies.Enqueue(() => throw new HttpRequestException("synthetic network failure"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
        Requests.Add(new(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), Header("Idempotency-Key"), Header("If-None-Match"),
            request.Content?.Headers.ContentType?.ToString(), request.Content?.Headers.ContentEncoding.Count > 0,
            request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancel)));
        if (replies.Count == 0) throw new InvalidOperationException("Unexpected request: " + request.Method + " " + request.RequestUri);
        return replies.Dequeue()();
    }
}

sealed class MemoryStore : IRemoteStateStore
{
    public RemoteState State = new();
    public RemoteState Load() => State;
    public void Save(RemoteState state) => State = state;
}

/// <summary>Reversible stand-in for DPAPI that still proves the token is never stored as typed.</summary>
sealed class TestProtector : ISecretProtector
{
    public string Protect(string secret) => "protected:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(secret).Reverse().ToArray());
    public string? Unprotect(string value)
        => value.StartsWith("protected:") ? Encoding.UTF8.GetString(Convert.FromBase64String(value["protected:".Length..]).Reverse().ToArray()) : null;
}

static class Synthetic
{
    public static readonly DateTimeOffset ScannedAt = new(2026, 10, 3, 14, 2, 11, 482, TimeSpan.Zero);
    public static readonly Guid ScanId = Guid.Parse("c1a9d3e4-52b7-4f60-8a1c-0d9e8f7a6b54");
    public const string GpuId = @"PCI\VEN_10DE&DEV_2684&SUBSYS_88891043&REV_A1\4&ALICE-SERIAL&0&0019";

    public static Finding Event(string source, int id, int level, DateTimeOffset first, DateTimeOffset last, string deviceRef = "", int count = 1)
        => new($"event:{source}:{id}:{deviceRef}:{first:yyyyMMddHH}", $"{source} · Ereignis {id}", "private raw event message C:\\Users\\Alice", "Warnung", "03.10.2026 15:41",
            deviceRef, count, first.LocalDateTime, last.LocalDateTime, source, id, level);

    public static Snapshot Snapshot() => new("Microsoft Windows 11 Pro · Build 26100", "AMD Ryzen 7 7800X3D 8-Core Processor", "legacy-board", "legacy-bios", 31.9,
        [
            new("NVIDIA GeForce RTX 4090 von Alice", "Display", "32.0.15.6094", "NVIDIA", "2026-08-14", 0, GpuId),
            new("Intel Ethernet", "Net", "2.1.4.3", "Intel", "2025-11-02", 0, @"PCI\VEN_8086&DEV_125C&REV_04\ALICE-MAC-001122"),
            new("WAN Miniport (IP)", "Net", "10.0.26100.1", "Microsoft", "2006-06-21", 0, @"SWD\MSRRAS\MS_NDISWANIP"),
            new("Alices USB-Empfänger", "USB", "not a version", "Vendor", "kaputt", 22, @"USB\VID_0E8D&PID_0616\ALICE-SERIAL-9"),
            new("Privater Drucker", "Printer", "1.0", "Vendor", "", 28, @"SWD\PRINTENUM\ALICE-PRINTER"),
            new("USB-Root-Hub", "USB", "10.0.26100.1", "Microsoft", "2024-01-01", 0, @"USB\VID_1234&PID_ABCD\HUB"),
            new("PCI-Bus", "System", "10.0.26100.1", "Microsoft", "2006-06-21", 0, @"ACPI\PNP0A08\0")
        ],
        [
            new("device:" + GpuId + ":0", "Gerätehinweis", "private", "Info", "03.10.2026 15:00"),
            Event("nvlddmkm", 153, 2, ScannedAt.AddMinutes(-24), ScannedAt.AddMinutes(-21), GpuId, 4),
            Event("Microsoft-Windows-WHEA-Logger", 17, 3, ScannedAt.AddHours(-19), ScannedAt.AddMinutes(-22), "", 12),
            Event("Application Error", 1000, 2, ScannedAt.AddHours(-1), ScannedAt.AddHours(-1)),
            // Starts a few seconds before the 24 hour window because the scan itself took time.
            Event("disk", 158, 4, ScannedAt.AddHours(-24).AddSeconds(-20), ScannedAt.AddHours(-23), "Disk 1", 2)
        ],
        ["private warning with ALICE-PC"],
        new("System manufacturer", "System Product Name", "Micro-Star International Co., Ltd.", "MPG X670E CARBON WIFI (MS-7D70)", "1.0", "1.I0", "2026-03-18", "X64", "26100", false),
        new("complete", "complete", "complete", "unavailable"));

    public static SymptomInput Symptom() => new("wow_crash", "WoW stürzt nach etwa 20 Minuten ab, Fehler #132 (0x85100084). Treiber 32.0.15.6094.", "1. WoW starten\r\n2. Schlachtzug betreten",
        new WowInput("11.2.5.63506", "wow_only", "no"));
}
