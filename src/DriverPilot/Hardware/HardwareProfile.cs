namespace DriverPilot;

public record HardwareProfile(string SystemManufacturer="", string SystemModel="", string BoardManufacturer="", string BoardModel="", string BoardRevision="", string BiosVersion="", string BiosDate="", string Architecture="", string WindowsBuild="", bool IsPortable=false);

/// <summary>Model part of a Windows hardware ID. The instance suffix (often a serial number) is never returned.</summary>
public sealed record HardwareId(string Bus,string VendorId,string ProductId,string? SubsystemId)
{
    public static HardwareId? Parse(string? instanceId)
    {
        if(instanceId==null||instanceId.Length>512)return null;
        const System.Text.RegularExpressions.RegexOptions options=System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        var pci=System.Text.RegularExpressions.Regex.Match(instanceId,@"\APCI\\VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(?:&SUBSYS_([0-9A-F]{8}))?(?=&|\\|\z)",options);
        if(pci.Success)
        {
            // Reject a malformed SUBSYS instead of silently accepting just VEN/DEV.
            if(instanceId.Contains("&SUBSYS_",StringComparison.OrdinalIgnoreCase)&&!pci.Groups[3].Success)return null;
            return new("pci",pci.Groups[1].Value.ToUpperInvariant(),pci.Groups[2].Value.ToUpperInvariant(),pci.Groups[3].Success?pci.Groups[3].Value.ToUpperInvariant():null);
        }
        var usb=System.Text.RegularExpressions.Regex.Match(instanceId,@"\AUSB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?=&|\\|\z)",options);
        return usb.Success?new("usb",usb.Groups[1].Value.ToUpperInvariant(),usb.Groups[2].Value.ToUpperInvariant(),null):null;
    }
}

public record ManufacturerRoute(string Vendor,string Model,string SourceUrl,string BiosGuideUrl,bool ExactModelVerified,bool IsOem,string Reason);

// Every module returns detection, official source and support level through the same contract.
public interface IManufacturerModule
{
    string Vendor { get; }
    ManufacturerRoute Detect(HardwareProfile profile,bool systemIdentity);
}

public static class ManufacturerRouting
{
    sealed record PortalModule(string Vendor,string Portal) : IManufacturerModule
    {
        public ManufacturerRoute Detect(HardwareProfile profile,bool systemIdentity)
        {
            var model=Clean(systemIdentity?profile.SystemModel:profile.BoardModel);
            var exact=!systemIdentity&&Vendor=="MSI"&&IsCarbon(model);
            var source=exact?ManufacturerScanner.MsiUrl:Portal;
            return new(Vendor,model,source,exact?"https://www.msi.com/support/technical_details/MB_BIOS_Update":source,exact,systemIdentity,
                exact?"Bekannte MSI-Modellzuordnung; BIOS-Version weiterhin manuell prüfen.":"Manuelles offizielles Herstellerportal. Exaktes Modell und Revision nicht verifiziert; kein getesteter modellgenauer Support und kein Versionsvergleich.");
        }
    }
    static readonly IManufacturerModule[] Modules=[
        new PortalModule("ASUS","https://www.asus.com/support/download-center/"),
        new PortalModule("MSI","https://www.msi.com/support"),
        new PortalModule("Gigabyte","https://www.gigabyte.com/Support"),
        new PortalModule("ASRock","https://www.asrock.com/support/"),
        new PortalModule("Dell","https://www.dell.com/support/home/"),
        new PortalModule("HP","https://support.hp.com/drivers"),
        new PortalModule("Lenovo","https://pcsupport.lenovo.com/"),
        new PortalModule("Acer","https://www.acer.com/support/drivers-and-manuals")];

    static string Clean(string? value)=>System.Text.RegularExpressions.Regex.Replace(value??"",@"\s+"," ").Trim();
    internal static bool Placeholder(string? value)=>Clean(value).TrimEnd('.').ToUpperInvariant() is "" or "UNKNOWN" or "NONE" or "N/A" or "DEFAULT STRING" or "SYSTEM PRODUCT NAME" or "SYSTEM MANUFACTURER" or "SYSTEM MODEL" or "TO BE FILLED BY O.E.M" or "NOT APPLICABLE";
    public static string NormalizeVendor(string? value)=>Clean(value).TrimEnd('.').ToUpperInvariant() switch
    {
        "ASUS" or "ASUSTEK COMPUTER INC" or "ASUSTEK COMPUTER INCORPORATED"=>"ASUS",
        "MSI" or "MICRO-STAR INTERNATIONAL CO., LTD" or "MICRO-STAR INTERNATIONAL CO.,LTD"=>"MSI",
        "GIGABYTE" or "GIGABYTE TECHNOLOGY CO., LTD"=>"Gigabyte",
        "ASROCK" or "ASROCK INC"=>"ASRock",
        "DELL" or "DELL INC" or "DELL COMPUTER CORPORATION"=>"Dell",
        "HP" or "HP INC" or "HEWLETT-PACKARD" or "HEWLETT PACKARD"=>"HP",
        "LENOVO"=>"Lenovo", "ACER" or "ACER INCORPORATED"=>"Acer", _=>""
    };
    static bool IsCarbon(string model)=>model.Equals("MPG X670E CARBON WIFI",StringComparison.OrdinalIgnoreCase)||model.Equals("MPG X670E CARBON WIFI (MS-7D70)",StringComparison.OrdinalIgnoreCase);
    public static ManufacturerRoute Detect(HardwareProfile? profile)
    {
        profile??=new();
        var systemVendor=NormalizeVendor(profile.SystemManufacturer);
        var boardVendor=NormalizeVendor(profile.BoardManufacturer);
        // Laptop/OEM firmware belongs to the system vendor, never its retail board supplier.
        var sameBoard=Clean(profile.SystemModel).Equals(Clean(profile.BoardModel),StringComparison.OrdinalIgnoreCase)
            ||(boardVendor=="MSI"&&IsCarbon(Clean(profile.BoardModel))&&Clean(profile.SystemModel).Equals("MS-7D70",StringComparison.OrdinalIgnoreCase));
        var systemIdentity=profile.IsPortable||systemVendor is "Dell" or "HP" or "Lenovo" or "Acer"
            ||(!Placeholder(profile.SystemManufacturer)&&(systemVendor!=boardVendor||(!Placeholder(profile.SystemModel)&&!sameBoard)));
        var vendor=systemIdentity?systemVendor:boardVendor;
        var module=Modules.FirstOrDefault(m=>m.Vendor==vendor);
        return module?.Detect(profile,systemIdentity)??new("Unbekannt",Clean(systemIdentity?profile.SystemModel:profile.BoardModel),"","",false,systemIdentity,
            "Nicht unterstützt / Zuordnung offen: Hersteller unbekannt oder Platzhalter. Exaktes System-/Mainboardmodell und Revision manuell identifizieren; keine Quelle oder Aktualität geraten.");
    }
}
