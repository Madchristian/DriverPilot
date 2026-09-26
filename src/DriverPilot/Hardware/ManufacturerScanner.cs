using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace DriverPilot;

public record FirmwareDisk(string Model,string Firmware,double CapacityGB);
public record ManufacturerInventory(string Board,string BiosVersion,string BiosDate,List<FirmwareDisk> Disks,string ChipsetVersion,HardwareProfile? Hardware=null);
public record ManufacturerFinding(string Device,string Category,string Installed,string Available,string ReleaseDate,string Status,string SourceUrl,string Details,bool Newer=false);
public record ManufacturerReport(DateTime CheckedAt,ManufacturerInventory Inventory,List<ManufacturerFinding> Findings);

public static class ManufacturerScanner
{
    public const string MsiUrl="https://www.msi.com/Motherboard/MPG-X670E-CARBON-WIFI/support";
    public const string AmdUrl="https://www.amd.com/en/support/downloads/drivers.html/chipsets/am5/x670e.html";
    public const string NvidiaUrl="https://www.nvidia.com/en-us/geforce/drivers/";
    public const string CorsairUrl="https://help.corsair.com/hc/en-us/articles/15292042915213-SSD-How-to-Update-your-SSD-Firmware";
    public const string SandiskUrl="https://support-en.sandisk.com/app/answers/detailweb/a_id/50745";
    public const string CreativeUrl="https://support.creative.com/Downloads/searchdownloads.aspx?filename=SB";
    static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
    static readonly SemaphoreSlim Gate=new(1,1);
    public static bool OfficialUrl(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0)return false;
        return new[]{"msi.com","www.msi.com","amd.com","www.amd.com","nvidia.com","www.nvidia.com","gfwsl.geforce.com","www.corsair.com","help.corsair.com","support-en.sandisk.com","support.creative.com","www.intel.com","semiconductor.samsung.com","www.crucial.com","www.asus.com","www.gigabyte.com","www.asrock.com","www.dell.com","support.hp.com","pcsupport.lenovo.com","www.acer.com"}.Contains(uri.Host,StringComparer.OrdinalIgnoreCase);
    }
    public static string BiosGuideUrl(ManufacturerFinding finding)=>!finding.Category.Contains("BIOS")||!OfficialUrl(finding.SourceUrl)?"":finding.SourceUrl==MsiUrl?"https://www.msi.com/support/technical_details/MB_BIOS_Update":finding.SourceUrl;
    static async Task<string> Fetch(string url)
    {
        for(int hop=0;hop<5;hop++)
        {
            if(!OfficialUrl(url))throw new Exception("Verweis außerhalb der zugelassenen Herstellerdomains.");
            using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("DriverPilot/0.2 (+Windows; manufacturer-update-check)");
            using var response=await Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);
            if((int)response.StatusCode is >=300 and <400)
            {
                var location=response.Headers.Location??throw new Exception("Weiterleitung ohne Ziel.");url=(location.IsAbsoluteUri?location:new Uri(new Uri(url),location)).AbsoluteUri;continue;
            }
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(8*1024*1024);return await response.Content.ReadAsStringAsync();
        }
        throw new Exception("Zu viele Weiterleitungen.");
    }
    static string Plain(string html)=>Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," ")),"\\s+"," ").Trim();
    static string Get(JsonElement e,string key)=>e.TryGetProperty(key,out var p)?p.ToString():"";
    public static async Task<ManufacturerReport> Scan(Snapshot snapshot, bool persist = true)
    {
        await Gate.WaitAsync();
        try
        {
            var json=await PowerShell.Run("ManufacturerInventory");var inventory=JsonSerializer.Deserialize<ManufacturerInventory>(json,Store.Json)??throw new Exception("Firmware-Inventar fehlt.");
            var report=await BuildReport(snapshot,inventory,Fetch);if(persist)Store.Save("manufacturers.json",report);return report;
        }
        finally{Gate.Release();}
    }
    // No persistence or process launch: the production scan and offline regression tests share this path.
    public static async Task<ManufacturerReport> BuildReport(Snapshot snapshot,ManufacturerInventory inventory,Func<string,Task<string>> fetch)
    {
            snapshot=snapshot with{Devices=snapshot.Devices??[]};
            var profile=snapshot.Hardware;
            inventory=inventory with{Board=profile?.BoardModel??snapshot.Board??"",BiosVersion=profile?.BiosVersion??snapshot.Bios??"",BiosDate=profile?.BiosDate??"",Hardware=profile,Disks=inventory.Disks??[]};
            var route=ManufacturerRouting.Detect(profile);
            var windows11X64=string.Equals(profile?.Architecture,"X64",StringComparison.OrdinalIgnoreCase)&&int.TryParse(profile?.WindowsBuild,out var build)&&build>=22000&&(snapshot.Windows??"").Contains("Windows 11",StringComparison.OrdinalIgnoreCase);
            var checks=new List<Task<List<ManufacturerFinding>>>();
            if(route.ExactModelVerified){checks.Add(CheckMsi(inventory,snapshot,fetch));if(windows11X64)checks.Add(CheckAmd(inventory,fetch));}
            if(!route.IsOem&&windows11X64)foreach(var gpu in snapshot.Devices.Where(d=>string.Equals(d.Class,"Display",StringComparison.OrdinalIgnoreCase)&&(d.Name??"").StartsWith("NVIDIA GeForce",StringComparison.OrdinalIgnoreCase)))checks.Add(CheckNvidia(gpu,fetch));
            if(snapshot.Devices.Any(d=>d.Name=="Sound Blaster ZxR"))checks.Add(CheckCreative(snapshot,fetch));
            var rows=(await Task.WhenAll(checks)).SelectMany(x=>x).ToList();
            if(!route.ExactModelVerified)rows.Add(new(string.IsNullOrWhiteSpace(route.Model)?"System / Mainboard unbekannt":route.Model,"BIOS / UEFI",inventory.BiosVersion,"Nicht ermittelt","—",route.SourceUrl.Length==0?"Nicht unterstützt · Zuordnung offen":"Manuelle Modellprüfung",route.SourceUrl,route.Reason));
            foreach(var device in snapshot.Devices.Where(d=>d.Class is "Display" or "Net" or "Bluetooth"))
            {
                var name=device.Name??"Unbekanntes Gerät";
                if(route.IsOem){rows.Add(new(name,"OEM-Treiber",device.Version,"Nicht ermittelt","—",route.SourceUrl.Length==0?"Nicht unterstützt · Zuordnung offen":"Manuelle Modellprüfung · Zuordnung offen",route.SourceUrl,"OEM-/Notebook-Support hat Vorrang vor generischen Komponentenpaketen. "+route.Reason));continue;}
                if(device.Class=="Display"&&name.StartsWith("NVIDIA GeForce",StringComparison.OrdinalIgnoreCase)&&!windows11X64)rows.Add(new(name,"Grafiktreiber",device.Version,"Nicht ermittelt","—","Versionsvergleich offen",NvidiaUrl,"Windows-Version oder Architektur nicht für den vorhandenen Windows-11-x64-Katalog bestätigt. Modell und Betriebssystem manuell auswählen."));
                if((device.Provider??"").Contains("Intel",StringComparison.OrdinalIgnoreCase))rows.Add(new(name,"Herstellerportal",device.Version,"Nicht ermittelt","—","Manuelle Modellprüfung","https://www.intel.com/content/www/us/en/download-center/home.htm","Offizieller Intel Download Center. Das Portal ist ein Einstiegspunkt; ein automatischer modellgenauer Versionsvergleich wurde nicht durchgeführt."));
                if(device.Class=="Display"&&(device.Provider??"").Contains("Advanced Micro",StringComparison.OrdinalIgnoreCase))rows.Add(new(name,"Grafiktreiber",device.Version,"Nicht ermittelt","—","Manuelle Modellprüfung","https://www.amd.com/en/support/download/drivers.html","Integrierte AMD-Grafik: Prozessor/Grafikmodell und Windows-Version im AMD-Portal auswählen. Windows-Komponentenversion und Paketversion sind nicht direkt gleichzusetzen."));
            }
            foreach(var disk in inventory.Disks)
            {
                if((disk.Model??"").Contains("SN850X",StringComparison.OrdinalIgnoreCase))rows.Add(new(disk.Model??"","SSD-Firmware",disk.Firmware,"Nicht ermittelt","—","Herstellertool nötig",SandiskUrl,"Die passende Firmware muss SANDISK Dashboard anhand des konkreten Laufwerks bestimmen. Unterschiedliche Firmwarezweige desselben Modells sind möglich. Kein öffentlicher Versionsvergleich durchgeführt."));
                else if((disk.Model??"").Contains("MP600",StringComparison.OrdinalIgnoreCase))rows.Add(new(disk.Model??"","SSD-Firmware",disk.Firmware,"Nicht ermittelt","—","Herstellertool nötig",CorsairUrl,"CORSAIR verweist zur Firmwareprüfung auf SSD Toolbox. Dort genau dieses Laufwerk auswählen und „Check For Update“ verwenden. Die installierte Firmware wurde lokal gelesen; eine neue Firmware ist damit nicht bestätigt."));
                else if((disk.Model??"").Contains("Samsung",StringComparison.OrdinalIgnoreCase))rows.Add(new(disk.Model??"","SSD-Firmware",disk.Firmware,"Nicht ermittelt","—","Herstellertool nötig","https://semiconductor.samsung.com/consumer-storage/support/tools/","Mit Samsung Magician die Unterstützung des konkreten Laufwerks prüfen. OEM-Laufwerke können abweichende Supportwege haben. Kein automatischer Versionsvergleich durchgeführt."));
                else if((disk.Model??"").Contains("Crucial",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(disk.Model??"",@"^CT\d+",RegexOptions.IgnoreCase))rows.Add(new(disk.Model??"","SSD-Firmware",disk.Firmware,"Nicht ermittelt","—","Manuelle Modellprüfung","https://www.crucial.com/support/ssd-support","Exaktes Crucial-Modell im SSD-Support prüfen und die Firmwareanleitung beachten. Kein automatischer Versionsvergleich durchgeführt."));
                else rows.Add(new(disk.Model??"Unbekanntes Laufwerk","SSD / Laufwerk-Firmware",disk.Firmware,"Nicht ermittelt","—","Nicht unterstützt · Zuordnung offen","","Kein geprüfter Herstellerweg für dieses Laufwerk. Modell und OEM-Zugehörigkeit manuell prüfen; keine Aktualität bestätigt."));
            }
            foreach(var device in snapshot.Devices.Where(d=>!rows.Any(r=>r.Device==d.Name)))rows.Add(new(device.Name,"Herstellerprüfung",device.Version,"Nicht ermittelt","—","Nicht unterstützt · Zuordnung offen","","Kein modellgenauer Herstellervergleich verfügbar. Windows-Gerätestatus ist keine Aussage zur Treiberaktualität."));
            return new(DateTime.Now,inventory,rows);
    }
    public static ManufacturerFinding ParseAmd(string html,string installed)
    {
        var article=Regex.Match(html,@"<article\b[^>]*>(?:(?!</article>).)*<h4>\s*AMD Chipset Drivers\s*</h4>(?:(?!</article>).)*</article>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
        if(!article.Success)throw new Exception("AMD-Chipsatzabschnitt im Seitenformat nicht erkannt.");
        var text=Plain(article.Value);
        var version=Regex.Match(text,@"Revision Number\s+(\d+(?:\.\d+){2,4})");
        var date=Regex.Match(text,@"Release Date\s+(\d{4}-\d{2}-\d{2})");
        if(!version.Success||!date.Success)throw new Exception("AMD liefert keine eindeutig lesbare Version mit Datum.");
        return Compare("AMD X670E Chipsatz","Treiberpaket",installed,version.Groups[1].Value,date.Groups[1].Value,AmdUrl,"Vergleich der installierten AMD-Chipsatzpaketversion mit dem offiziellen X670E-Paket für Windows 11. Kein automatischer Hersteller-Installer und keine Installationsfreigabe.");
    }
    static async Task<List<ManufacturerFinding>> CheckAmd(ManufacturerInventory inventory,Func<string,Task<string>> fetch)
    {
        try{return [ParseAmd(await fetch(AmdUrl),inventory.ChipsetVersion)];}
        catch(Exception ex){return [Failure("AMD X670E Chipsatz","Treiberpaket",inventory.ChipsetVersion,AmdUrl,ex)];}
    }
    static async Task<List<ManufacturerFinding>> CheckMsi(ManufacturerInventory inventory,Snapshot snapshot,Func<string,Task<string>> fetch)
    {
        string status,detail;
        try
        {
            var html=await fetch(MsiUrl);
            if(!html.Contains("MPG X670E CARBON WIFI",StringComparison.OrdinalIgnoreCase)&&!html.Contains("MPG-X670E-CARBON-WIFI",StringComparison.OrdinalIgnoreCase))throw new Exception("Die Produktseite wurde nicht eindeutig erkannt.");
            status="Im Browser prüfen";detail="Die Supportseite ist erreichbar, stellt aber keinen hier verlässlich auslesbaren BIOS-/Treiberkatalog bereit. Version und Datum im MSI-Support vergleichen. Keine Aussage, ob ein Update verfügbar ist.";
        }
        catch(Exception ex){status="Quelle nicht abrufbar";detail="Die MSI-Supportseite konnte nicht automatisch geprüft werden. "+Friendly(ex)+" Bitte die Quelle im Browser öffnen. Eine aktuelle Version ist nicht bestätigt.";}
        var rows=new List<ManufacturerFinding>{new(inventory.Board,"BIOS / UEFI",inventory.BiosVersion+" · "+inventory.BiosDate,"Nicht ermittelt","—",status,MsiUrl,detail+" BIOS-Updates bleiben manuell; Modell MS-7D70 und MSI-Anleitung beachten.")};
        foreach(var d in snapshot.Devices.Where(d=>d.Name is "Realtek Gaming 2.5GbE Family Controller" or "RZ616 Wi-Fi 6E 160MHz" or "RZ616 Bluetooth(R) Adapter"))rows.Add(new(d.Name,"Mainboard-Treiber",d.Version,"Nicht ermittelt","—",status,MsiUrl,detail+(d.Code==22?" Das Gerät ist aktuell deaktiviert.":"")));
        return rows;
    }
    public static string NvidiaVersion(string windowsVersion)
    {
        if(!Version.TryParse(windowsVersion,out var v)||v.Build<0||v.Revision<0)return "";
        var digits=(v.Build%10).ToString(CultureInfo.InvariantCulture)+v.Revision.ToString("D4",CultureInfo.InvariantCulture);
        return digits[..3]+"."+digits[3..];
    }
    static async Task<List<ManufacturerFinding>> CheckNvidia(Device gpu,Func<string,Task<string>> fetch)
    {
        var installed=NvidiaVersion(gpu.Version);
        try
        {
            const string route="https://gfwsl.geforce.com/nvidia_web_services/controller.php?com.nvidia.services.Drivers.getMenuArrayProductOperatingSystemMetaData/";
            var parameters=Uri.EscapeDataString("{\"pt\":\"0\",\"pst\":\"0\",\"d2\":\"0\",\"d4\":\"0\",\"d5\":\"0\",\"driverType\":\"all\",\"sa\":\"0\"}");
            using var catalog=JsonDocument.Parse(await fetch(route+parameters));
            var matches=catalog.RootElement.GetProperty("ProductNames").EnumerateObject().Select(p=>p.Value).Where(v=>v.ValueKind==JsonValueKind.Object&&Get(v,"ProductNameMeta").Split('|').Select(s=>s.Trim()).Contains(gpu.Name)&&Get(v,"OperatingSystemID")=="135").ToList();
            if(matches.Count!=1)throw new Exception("Grafikkarte und Windows 11 nicht eindeutig im NVIDIA-Katalog zugeordnet.");
            var match=matches[0];
            var url="https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid="+Get(match,"ProductSeriesID")+"&pfid="+Get(match,"ProductFamilyID")+"&osID=135&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=0";
            using var drivers=JsonDocument.Parse(await fetch(url));
            return [ParseNvidia(drivers.RootElement,gpu.Name,installed)];
        }
        catch(Exception ex){return [Failure(gpu.Name,"Grafiktreiber",installed,NvidiaUrl,ex)];}
    }
    public static ManufacturerFinding ParseNvidia(JsonElement root,string name,string installed)
    {
        if(Get(root,"Success")!="1")throw new Exception("NVIDIA meldet kein auswertbares Suchergebnis.");
        var entries=root.GetProperty("IDS").EnumerateArray().Select(v=>v.GetProperty("downloadInfo")).Where(v=>Get(v,"IsBeta")=="0"&&Get(v,"IsWHQL")=="1"&&Get(v,"IsFeaturePreview")=="0").ToList();
        if(entries.Count==0)throw new Exception("Kein passendes stabiles WHQL-Paket ermittelt.");
        var info=entries.OrderByDescending(v=>Version.TryParse(Get(v,"Version"),out var version)?version:new Version(0,0)).First();
        var source=Get(info,"DetailsURL");if(!OfficialUrl(source))throw new Exception("Ungültiger NVIDIA-Quellenlink.");
        if(!DateTime.TryParseExact(Get(info,"ReleaseDateTime"),"ddd MMM dd, yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new Exception("NVIDIA-Veröffentlichungsdatum nicht erkannt.");
        return Compare(name,"Grafiktreiber",installed,Get(info,"Version"),date.ToString("yyyy-MM-dd"),source,"Offizieller NVIDIA-Katalog: exaktes GPU-Modell, Windows 11, Game Ready, WHQL, keine Beta. Windows-Treiberversion wurde in die NVIDIA-Versionsnummer umgerechnet. Keine Installation durchgeführt.");
    }
    static async Task<List<ManufacturerFinding>> CheckCreative(Snapshot snapshot,Func<string,Task<string>> fetch)
    {
        var installed=snapshot.Devices.First(d=>d.Name=="Sound Blaster ZxR").Version;
        try
        {
            var html=await fetch(CreativeUrl);
            if(!html.Contains("SBZxRDriverInstaller.exe",StringComparison.OrdinalIgnoreCase))throw new Exception("ZxR-Treiberpaket im Katalog nicht erkannt.");
            return [new("Sound Blaster ZxR","Audiotreiber",installed,"Versionsvergleich offen","—","Im Browser prüfen",CreativeUrl,"Der offizielle Katalog enthält einen ZxR-Treiberinstaller. Die Paketversion ist nicht zuverlässig mit der installierten Windows-Treiberversion vergleichbar. Ein neueres Update ist damit nicht bestätigt.")];
        }
        catch(Exception ex){return [Failure("Sound Blaster ZxR","Audiotreiber",installed,CreativeUrl,ex)];}
    }
    public static ManufacturerFinding Compare(string device,string category,string installed,string available,string date,string source,string detail)
    {
        if(!DateTime.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var released)||released.Date>DateTime.Today)throw new Exception("Unplausibles Veröffentlichungsdatum.");
        bool newer=false;string status;
        if(!Version.TryParse(installed,out var current)||!Version.TryParse(available,out var latest))status="Versionsvergleich offen";
        else if(latest>current){newer=true;status="Neuere Version gefunden";if(released>DateTime.Today.AddDays(-14))status="Neue Version · zunächst abwarten";}
        else if(latest==current)status="Angebotene Version installiert";
        else status="Installiert ist eine neuere Version";
        return new(device,category,string.IsNullOrWhiteSpace(installed)?"Nicht ermittelt":installed,available,date,status,source,detail,newer);
    }
    static ManufacturerFinding Failure(string device,string category,string installed,string source,Exception ex)=>new(device,category,string.IsNullOrWhiteSpace(installed)?"Nicht ermittelt":installed,"Nicht ermittelt","—","Quelle nicht abrufbar",source,Friendly(ex)+" Kein belastbarer Versionsvergleich möglich.");
    static string Friendly(Exception ex)=>ex is HttpRequestException {StatusCode:HttpStatusCode.Forbidden}?"Hersteller blockiert den automatischen Abruf (HTTP 403).":ex is TaskCanceledException?"Zeitlimit beim Herstellerabruf überschritten.":ex.Message;
}
