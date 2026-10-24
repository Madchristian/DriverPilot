using System.Globalization;
namespace DriverPilot.RemoteDiagnosis;

/// <summary>
/// hardware_details of report schema 1.1. Raw Windows values are validated and mapped onto the contract;
/// anything out of range, malformed or hit by a text marker becomes null, never a guessed value.
/// </summary>
public static class HardwareDetailsProjection
{
    public const int MaxMemoryModules = 32, MaxGpus = 4, MaxDisks = 16;
    // nvidia-smi --query-gpu field order; Inventory.ps1 requests exactly these columns.
    public const string NvidiaSmiFields = "name,temperature.gpu,fan.speed,clocks.gr,clocks.max.gr,clocks.mem,clocks.max.mem,power.draw,power.limit,power.default_limit,power.max_limit,pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max";
    static readonly int NvidiaSmiColumns = NvidiaSmiFields.Split(',').Length;

    public static ReportHardwareDetails? Build(HardwareDetails? details, ContractRules rules)
    {
        if (details == null) return null;
        string? Text(string? value) => RemoteReportBuilder.HardwareText(value, rules);
        return new(Cpu(details.Cpu), Memory(details.Memory, Text), Gpus(details.Gpus, details.NvidiaSmi, Text), Storage(details.Storage, Text), Platform(details.Platform, Text));
    }

    static ReportCpu? Cpu(CpuDetails? cpu)
        => cpu == null ? null : new(Range(cpu.Cores, 1, 1024), Range(cpu.Threads, 1, 4096), Range(cpu.MaxClockMhz, 100, 10000));

    static ReportMemory? Memory(MemoryDetails? memory, Func<string?, string?> text)
        => memory == null ? null : new(Range(memory.SlotsTotal, 1, 64), (memory.Modules ?? []).Where(m => m != null).Take(MaxMemoryModules)
            .Select(m => new ReportMemoryModule(text(m.Slot), Range(m.CapacityMb, 1, 1048576), MemoryType(m.SmbiosType), Range(m.RatedMts, 1, 20000), Range(m.ConfiguredMts, 1, 20000),
                text(m.Manufacturer), text(m.PartNumber)))
            .ToList());

    /// <summary>SMBIOS memory type codes (DSP0134, table 76).</summary>
    static string? MemoryType(int? code) => code switch
    {
        null or 0 or 2 => null, 24 => "ddr3", 26 => "ddr4", 34 => "ddr5", 30 => "lpddr4", 35 => "lpddr5", _ => "other"
    };

    static IReadOnlyList<ReportGpu>? Gpus(List<GpuDetails>? gpus, List<string>? nvidiaSmi, Func<string?, string?> text)
    {
        if (gpus == null) return null;
        // Only physical PCI adapters; remote and basic display adapters carry no diagnostic value.
        var physical = gpus.Where(g => g != null && HardwareId.Parse(g.PnpId) is { Bus: "pci" }).Take(MaxGpus).ToList();
        var rows = (nvidiaSmi ?? []).Select(ParseNvidiaSmi).Where(r => r != null).Select(r => r!.Value).ToList();
        var nvidia = physical.Count(g => Vendor(g) == "nvidia");
        var result = new List<ReportGpu>();
        foreach (var gpu in physical)
        {
            var vendor = Vendor(gpu);
            ReportGpuTelemetry? telemetry = null;
            if (vendor == "nvidia")
            {
                // Match by name; with a single NVIDIA adapter and a single row the order is unambiguous.
                var index = rows.FindIndex(r => string.Equals(r.Name, gpu.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (index < 0 && nvidia == 1 && rows.Count == 1) index = 0;
                if (index >= 0) { telemetry = rows[index].Telemetry; rows.RemoveAt(index); }
            }
            result.Add(new(vendor, text(gpu.Name), Range(gpu.VramBytes / (1024 * 1024), 1, 1048576), RemoteReportBuilder.DriverVersion(gpu.DriverVersion?.Trim()), telemetry));
        }
        return result;
    }

    static string Vendor(GpuDetails gpu) => HardwareId.Parse(gpu.PnpId)?.VendorId switch
    {
        "10DE" => "nvidia", "1002" => "amd", "8086" => "intel", _ => "other"
    };

    /// <summary>One CSV row in NvidiaSmiFields order; "[N/A]" and "[Not Supported]" become null.</summary>
    public static (string Name, ReportGpuTelemetry Telemetry)? ParseNvidiaSmi(string? row)
    {
        var cells = (row ?? "").Split(',').Select(c => c.Trim()).ToArray();
        if (cells.Length != NvidiaSmiColumns || cells[0].Length == 0) return null;
        int? Value(int column, long min, long max)
            => double.TryParse(cells[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Range((long)Math.Round(value), min, max) : null;
        return (cells[0], new(Value(1, 0, 150), Value(2, 0, 100), Value(3, 0, 10000), Value(4, 0, 10000), Value(5, 0, 50000), Value(6, 0, 50000),
            Value(7, 0, 2000), Value(8, 0, 2000), Value(9, 0, 2000), Value(10, 0, 2000), Value(11, 1, 8), Value(12, 1, 8), Value(13, 1, 32), Value(14, 1, 32)));
    }

    static ReportStorage? Storage(StorageDetails? storage, Func<string?, string?> text)
        => storage == null ? null : new(Range(storage.SystemSizeGb, 0, 1048576), Range(storage.SystemFreeGb, 0, 1048576), Range(storage.PagefileMb, 0, 1048576),
            (storage.Disks ?? []).Where(d => d != null).Take(MaxDisks)
                .Select(d => new ReportDisk(text(d.Model), Media(d.MediaType), Bus(d.BusType), Range(d.SizeGb, 0, 1048576), Health(d.Health), text(d.Firmware), d.SystemDisk))
                .ToList());

    // Get-PhysicalDisk reports names in Windows PowerShell; the numeric MSFT_PhysicalDisk codes are accepted as well.
    static string Media(string? value) => Code(value) switch { "SSD" or "4" => "ssd", "HDD" or "3" => "hdd", _ => "unspecified" };
    static string Bus(string? value) => Code(value) switch
    {
        "NVME" or "17" => "nvme", "SATA" or "11" => "sata", "USB" or "7" => "usb", "SAS" or "10" => "sas", "RAID" or "8" => "raid", _ => "other"
    };
    static string Health(string? value) => Code(value) switch { "HEALTHY" or "0" => "healthy", "WARNING" or "1" => "warning", "UNHEALTHY" or "2" => "unhealthy", _ => "unknown" };
    static string Code(string? value) => (value ?? "").Trim().ToUpperInvariant();

    static ReportPlatform? Platform(PlatformDetails? platform, Func<string?, string?> text)
        => platform == null ? null : new(
            Code(platform.FirmwareType) switch { "UEFI" => "uefi", "LEGACY" => "legacy", _ => null },
            platform.SecureBoot switch { 1 => true, 0 => false, _ => null },
            PowerPlan(platform.PowerPlanId),
            platform.HwSchMode switch { 2 => true, 1 => false, _ => null },
            platform.UptimeHours is { } hours && double.IsFinite(hours) ? Range((long)Math.Floor(hours), 0, 100000) : null);

    /// <summary>Win32_PowerPlan.InstanceID ("Microsoft:PowerPlan\{guid}") of the Windows default schemes.</summary>
    static string? PowerPlan(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;
        var guid = instanceId.Trim().TrimEnd('}').Split('{').Last().ToLowerInvariant();
        return guid switch
        {
            "381b4222-f694-41f0-9685-ff5bb260df2e" => "balanced", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "high_performance",
            "a1841308-3541-4fab-bc81-f71556f20b4a" => "power_saver", "e9a42b02-d5df-448d-aa00-03f14749eb61" => "ultimate_performance", _ => "custom"
        };
    }

    static int? Range(long? value, long min, long max) => value is { } v && v >= min && v <= max ? (int)v : null;
}
