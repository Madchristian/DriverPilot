using DriverPilot;
using System.Text.Json;

static class Program
{
    static int failures;
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static void Test(string name,Action test) { try{test();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex.Message);} }
    static int Main(string[] args)
    {
        Test("Hardware JSON roundtrip retains profile; old snapshot remains compatible",()=>{
            const string legacy="{\"Windows\":\"Windows 11\",\"Cpu\":\"CPU\",\"Board\":\"Board\",\"Bios\":\"1\",\"RamGB\":32,\"Devices\":[],\"Findings\":[],\"Warnings\":[]}";
            var old=JsonSerializer.Deserialize<Snapshot>(legacy)!;
            Check(old.Board=="Board","legacy snapshot not readable");
            var json=legacy[..^1]+",\"Hardware\":{\"SystemManufacturer\":\"ASUSTeK COMPUTER INC.\",\"SystemModel\":\"Synthetic notebook\",\"BoardManufacturer\":\"ASUSTeK COMPUTER INC.\",\"BoardModel\":\"Synthetic board\",\"BoardRevision\":\"1.0\",\"BiosVersion\":\"123\",\"BiosDate\":\"2020-01-01\",\"Architecture\":\"X64\",\"WindowsBuild\":\"26100\",\"IsPortable\":true}}";
            using var result=JsonDocument.Parse(JsonSerializer.Serialize(JsonSerializer.Deserialize<Snapshot>(json)));
            Check(result.RootElement.TryGetProperty("Hardware",out var hardware)&&hardware.GetProperty("BoardRevision").GetString()=="1.0","Hardware profile silently discarded");
        });
        Test("Vendor routing uses OEM identity and exact board matching",()=>{
            var routing=typeof(Snapshot).Assembly.GetType("DriverPilot.ManufacturerRouting");
            Check(routing!=null,"Manufacturer routing missing: foreign PCs have no explicit support result");
            object Route(HardwareProfile p)=>routing!.GetMethod("Detect")!.Invoke(null,[p])!;
            string Value(object route,string key)=>route.GetType().GetProperty(key)!.GetValue(route)!.ToString()!;
            foreach(var (raw,vendor,host) in new[]{("ASUSTeK COMPUTER INC.","ASUS","www.asus.com"),("Micro-Star International Co., Ltd.","MSI","www.msi.com"),("Gigabyte Technology Co., Ltd.","Gigabyte","www.gigabyte.com"),("ASRock Inc.","ASRock","www.asrock.com"),("Dell Inc.","Dell","www.dell.com"),("Hewlett-Packard","HP","support.hp.com"),("LENOVO","Lenovo","pcsupport.lenovo.com"),("Acer Incorporated","Acer","www.acer.com")}) {
                var r=Route(new(BoardManufacturer:raw,BoardModel:"Synthetic unverified model"));
                Check(Value(r,"Vendor")==vendor,"normalization: "+raw);
                Check(new Uri(Value(r,"SourceUrl")).Host==host,"official portal: "+vendor);
                Check(Value(r,"ExactModelVerified")=="False","manual portal claimed model verification");
            }
            var exact=new HardwareProfile(BoardManufacturer:"Micro-Star International Co., Ltd.",BoardModel:"MPG X670E CARBON WIFI (MS-7D70)");
            Check(Value(Route(exact),"ExactModelVerified")=="True","verified MSI board lost");
            foreach(var profile in new[]{exact with{BoardManufacturer="ASUS"},exact with{BoardModel="NOT MPG X670E CARBON WIFI"},exact with{SystemManufacturer="Dell Inc.",SystemModel="Synthetic OEM"},exact with{IsPortable=true},exact with{SystemManufacturer="Unrecognized OEM",SystemModel="Custom notebook",IsPortable=true}})
                Check(Value(Route(profile),"ExactModelVerified")=="False","misleading/OEM identity selected retail MSI");
            Check(Value(Route(exact with{SystemManufacturer="Dell Inc.",SystemModel="Synthetic OEM"}),"Vendor")=="Dell","OEM did not override board");
            foreach(var vendor in new[]{"", "To be filled by O.E.M.","Default string","Not ASUS", "ASUS.evil", "Unknown"}) {
                var r=Route(new(BoardManufacturer:vendor,BoardModel:"MPG X670E CARBON WIFI"));
                Check(Value(r,"SourceUrl")=="","unrecognized identity got guessed URL: "+vendor);
            }
        });
        Test("Read-only inventory emits shared hardware profile and tolerates absent CIM fields",()=>{
            using var stream=typeof(Snapshot).Assembly.GetManifestResourceStream("DriverPilot.Scripts.Inventory.ps1")!;
            var script=new System.IO.StreamReader(stream).ReadToEnd();
            var mocks="""
                function Get-CimInstance($ClassName) {
                    switch($ClassName) {
                        Win32_ComputerSystem { [pscustomobject]@{Manufacturer='ASUSTeK COMPUTER INC.';Model='Synthetic notebook';PCSystemType=2} }
                        Win32_BaseBoard { [pscustomobject]@{Manufacturer='ASUS';Product='Synthetic board';Version='1.0';SerialNumber='DO-NOT-EXPORT'} }
                        Win32_BIOS { [pscustomobject]@{SMBIOSBIOSVersion='123';ReleaseDate=[datetime]'2020-01-02';SerialNumber='DO-NOT-EXPORT'} }
                        Win32_OperatingSystem { [pscustomobject]@{Caption='Windows 11';BuildNumber='26100';OSArchitecture='64-bit'} }
                        Win32_Processor { [pscustomobject]@{Name='Synthetic CPU'} }
                        Win32_PhysicalMemory { [pscustomobject]@{Capacity=34359738368} }
                        Win32_SystemEnclosure { [pscustomobject]@{ChassisTypes=@(10)} }
                    }
                }
                function Get-WinEvent { @() }
                """;
            var json=PowerShell.Script(mocks+"\n"+script).GetAwaiter().GetResult();
            var s=JsonSerializer.Deserialize<Snapshot>(json)!;
            Check(s.Hardware is {SystemManufacturer:"ASUSTeK COMPUTER INC.",SystemModel:"Synthetic notebook",BoardManufacturer:"ASUS",BoardModel:"Synthetic board",BoardRevision:"1.0",BiosVersion:"123",BiosDate:"2020-01-02",WindowsBuild:"26100",IsPortable:true},"inventory hardware identity missing");
            Check(!json.Contains("DO-NOT-EXPORT"),"serial exported");
            var empty=PowerShell.Script("function Get-CimInstance { $null }; function Get-WinEvent { @() };\n"+script).GetAwaiter().GetResult();
            Check(JsonSerializer.Deserialize<Snapshot>(empty)!.Hardware!=null,"missing CIM broke profile");
        });
        Test("Official URL validation allows only exact reviewed HTTPS hosts",()=>{
            foreach(var host in new[]{"www.asus.com","www.msi.com","www.gigabyte.com","www.asrock.com","www.dell.com","support.hp.com","pcsupport.lenovo.com","www.acer.com","www.nvidia.com","gfwsl.geforce.com"})
                Check(ManufacturerScanner.OfficialUrl("https://"+host+"/"),"official host rejected: "+host);
            foreach(var url in new[]{"https://unreviewed.msi.com/","https://www.asus.com.evil.test/","https://evil.test/?www.asus.com","https://www.asus.com@evil.test/","http://www.asus.com/","https://www.asus.com:444/","file:///C:/x"})
                Check(!ManufacturerScanner.OfficialUrl(url),"unsafe URL accepted: "+url);
        });
        Test("Report pipeline shares snapshot identity and exposes unsupported devices without invented chipset",()=>{
            var build=typeof(ManufacturerScanner).GetMethod("BuildReport");
            Check(build!=null,"Testable read-only manufacturer pipeline missing");
            var profile=new HardwareProfile(SystemManufacturer:"ASUSTeK COMPUTER INC.",SystemModel:"Synthetic notebook",BoardManufacturer:"ASUS",BoardModel:"Synthetic board",BiosVersion:"123",IsPortable:true);
            var snapshot=new Snapshot("Windows 11","AMD Ryzen X670E misleading CPU","MPG X670E CARBON WIFI","wrong",32,[new("Mystery device","Net","1","Unknown","",0,"synthetic")],[],[],profile);
            var inventory=new ManufacturerInventory("MPG X670E CARBON WIFI","wrong","wrong",[new("Mystery SSD","1",100)],"1.0");
            int calls=0;Func<string,Task<string>> fetch=_=>{calls++;throw new Exception("No network for unsupported synthetic hardware");};
            var report=((Task<ManufacturerReport>)build!.Invoke(null,[snapshot,inventory,fetch])!).GetAwaiter().GetResult();
            Check(calls==0,"Unrelated MSI inventory or CPU triggered exact-model fetch");
            Check(report.Inventory.Board==profile.BoardModel&&report.Inventory.BiosVersion=="123","independent inventory overwrote snapshot identity");
            var bios=report.Findings.Single(f=>f.Category.Contains("BIOS"));
            Check(bios.SourceUrl.Contains("asus.com")&&!bios.Newer&&bios.Details.Contains("nicht verifiziert"),"ASUS portal claimed exact-model support");
            Check(report.Findings.Any(f=>f.Device=="Mystery SSD"&&f.Status.Contains("offen")&&f.SourceUrl==""),"unknown disk silently skipped");
            Check(report.Findings.Any(f=>f.Device=="Mystery device"&&f.Status.Contains("offen")),"unknown device silently skipped");
            Check(report.Findings.All(f=>!f.Device.Contains("X670E")),"invented X670E chipset");
        });
        Test("OEM notebooks and unsupported Windows architecture never query desktop packages",()=>{
            var gpu=new Device("NVIDIA GeForce RTX 4090","Display","32.0.16.1714","NVIDIA","",0,"synthetic");
            var exact=new HardwareProfile(BoardManufacturer:"MSI",BoardModel:"MPG X670E CARBON WIFI",Architecture:"X64",WindowsBuild:"26100");
            foreach(var profile in new[]{exact with{SystemManufacturer="Dell Inc.",SystemModel="Synthetic laptop",IsPortable=true},exact with{Architecture="Arm64"},exact with{WindowsBuild="19045"},exact with{WindowsBuild=""}}) {
                var calls=new List<string>();Func<string,Task<string>> fetch=url=>{calls.Add(url);return Task.FromResult("MPG X670E CARBON WIFI");};
                var s=new Snapshot("Windows 11","CPU",exact.BoardModel,"1",32,[gpu],[],[],profile);
                var r=ManufacturerScanner.BuildReport(s,new("","","",[],"1.0"),fetch).GetAwaiter().GetResult();
                Check(!calls.Any(url=>url.Contains("amd.com")||url.Contains("geforce.com")),"desktop package catalog used despite OEM/OS mismatch");
                Check(r.Findings.All(f=>!f.Newer),"unverified OS generated newer update");
                if(profile.IsPortable)Check(r.Findings.Single(f=>f.Device==gpu.Name).SourceUrl.Contains("dell.com"),"notebook GPU not routed through OEM");
            }
        });
        Test("Manufacturer inventory does not recollect conflicting identity or export disk serial",()=>{
            using var stream=typeof(Snapshot).Assembly.GetManifestResourceStream("DriverPilot.Scripts.ManufacturerInventory.ps1")!;
            var script=new System.IO.StreamReader(stream).ReadToEnd();
            var mocks="""
                $identityQueries=0
                function Get-CimInstance($ClassName) {
                    if($ClassName -eq 'Win32_DiskDrive') { [pscustomobject]@{Model=$null;FirmwareRevision=$null;Size=1073741824;SerialNumber='DO-NOT-EXPORT'} }
                    else { $script:identityQueries++; [pscustomobject]@{Product='STALE';ReleaseDate=[datetime]'2020-01-01'} }
                }
                function Get-ItemProperty { @() }
                """;
            // Return queries separately so the assertion, not a PowerShell exception, detects duplicate identity collection.
            var wrapped=mocks+"\n$result=& {\n"+script+"\n}; [pscustomobject]@{Queries=$identityQueries;Result=$result} | ConvertTo-Json -Compress";
            // Supply a non-null model on the first probe; null handling is independently checked after the identity gate.
            var json=PowerShell.Script(wrapped.Replace("Model=$null","Model='Synthetic SSD'")).GetAwaiter().GetResult();
            using var doc=JsonDocument.Parse(json);
            Check(doc.RootElement.GetProperty("Queries").GetInt32()==0,"manufacturer inventory re-queries system identity");
            var nullJson=PowerShell.Script(wrapped).GetAwaiter().GetResult();
            Check(!nullJson.Contains("DO-NOT-EXPORT"),"disk serial exported");
        });
        Test("BIOS guide follows the finding vendor, not universally MSI",()=>{
            var guide=typeof(ManufacturerScanner).GetMethod("BiosGuideUrl");
            Check(guide!=null,"Vendor-bound BIOS guide is missing");
            var f=new ManufacturerFinding("Synthetic","BIOS / UEFI","1","?","","Manual","https://www.asus.com/support/download-center/","");
            Check((string)guide!.Invoke(null,[f])! == f.SourceUrl,"ASUS guide not ASUS");
            Check(((string)guide.Invoke(null,[f with{SourceUrl=ManufacturerScanner.MsiUrl}])!).EndsWith("MB_BIOS_Update"),"known MSI guide lost");
            Check((string)guide.Invoke(null,[f with{SourceUrl="https://www.asus.com.evil.test/"}])! == "","unsafe guide accepted");
            Check((string)guide.Invoke(null,[f with{SourceUrl=""}])! == "","unknown vendor got guide");
            using var source=typeof(Snapshot).Assembly.GetManifestResourceStream("Features.source")!;
            var text=new System.IO.StreamReader(source).ReadToEnd();
            Check(text.Contains("ManufacturerScanner.BiosGuideUrl(f)")&&!text.Contains("MSI-Anleitung für BIOS-Updates"),"UI still has universal MSI guide");
        });
        Test("Verified desktop MSI AMD NVIDIA and SSD routes remain intact with synthetic catalog fixtures",()=>{
            var profile=new HardwareProfile(BoardManufacturer:"Micro-Star International Co., Ltd.",BoardModel:"MPG X670E CARBON WIFI (MS-7D70)",Architecture:"X64",WindowsBuild:"26100");
            var gpu=new Device("NVIDIA GeForce RTX 4090","Display","32.0.16.1714","NVIDIA","",0,"synthetic");
            var snapshot=new Snapshot("Microsoft Windows 11 Pro","CPU",profile.BoardModel,"1",32,[gpu],[],[],profile);
            var inventory=new ManufacturerInventory("","","",[new("WD_BLACK SN850X 2000GB","1",2000),new("Corsair MP600 PRO","1",1000),new("Samsung SSD 980 PRO","1",1000),new("CT1000P3SSD8","1",1000)],"1.0.0.0");
            var calls=new List<string>();Func<string,Task<string>> fetch=url=>{
                calls.Add(url);
                if(url==ManufacturerScanner.MsiUrl)return Task.FromResult("MPG X670E CARBON WIFI");
                if(url==ManufacturerScanner.AmdUrl)return Task.FromResult("<article><h4>AMD Chipset Drivers</h4><p>Revision Number 2.0.0.0 Release Date 2020-01-01</p></article>");
                if(url.Contains("controller.php"))return Task.FromResult("{\"ProductNames\":{\"one\":{\"ProductNameMeta\":\"NVIDIA GeForce RTX 4090\",\"OperatingSystemID\":\"135\",\"ProductSeriesID\":\"1\",\"ProductFamilyID\":\"2\"}}}");
                return Task.FromResult("{\"Success\":\"1\",\"IDS\":[{\"downloadInfo\":{\"IsBeta\":\"0\",\"IsWHQL\":\"1\",\"IsFeaturePreview\":\"0\",\"Version\":\"617.14\",\"DetailsURL\":\"https://www.nvidia.com/en-us/drivers/\",\"ReleaseDateTime\":\"Tue Sep 22, 2020\"}}]}");
            };
            var report=ManufacturerScanner.BuildReport(snapshot,inventory,fetch).GetAwaiter().GetResult();
            Check(calls.Count==4,"known routes not exercised");
            Check(report.Findings.Single(f=>f.Device=="AMD X670E Chipsatz").Newer,"AMD version comparison lost");
            Check(report.Findings.Single(f=>f.Device==gpu.Name).Status=="Angebotene Version installiert","NVIDIA version mapping lost");
            Check(report.Findings.Count(f=>f.Category=="SSD-Firmware"&&ManufacturerScanner.OfficialUrl(f.SourceUrl)&&!f.Newer)==4,"SSD manual support lost");
            var legacy=ManufacturerScanner.BuildReport(snapshot with{Hardware=null},inventory,fetch).GetAwaiter().GetResult();
            Check(!legacy.Findings.Any(f=>f.Device=="AMD X670E Chipsatz"),"legacy board-only data guessed chipset");
            Check(legacy.Inventory.Hardware==null,"legacy inventory gained invented hardware profile");
        });
        Test("Explicit JSON nulls fail closed without crashing manufacturer report",()=>{
            var s=JsonSerializer.Deserialize<Snapshot>("{\"Devices\":null,\"Hardware\":{\"SystemManufacturer\":null,\"BoardManufacturer\":null,\"BoardModel\":null}}")!;
            bool crashed=false;ManufacturerReport? report=null;
            try{report=ManufacturerScanner.BuildReport(s,new("","","",null!,""),_=>throw new Exception("Unexpected network")).GetAwaiter().GetResult();}catch{crashed=true;}
            Check(!crashed,"null JSON fields crash report instead of unsupported fallback");
            Check(report!.Findings.Any(f=>f.Status.Contains("Nicht unterstützt")),"null identity not explicitly unsupported");
        });
        if(args.Contains("--live-read-only"))Test("Live local inventory smoke (read only, no report persistence or network)",()=>{
            var s=JsonSerializer.Deserialize<Snapshot>(PowerShell.Run("Inventory").GetAwaiter().GetResult())!;
            Check(s.Hardware!=null,"live hardware profile missing");
            var h=s.Hardware!;
            Check(!string.IsNullOrWhiteSpace(h.BoardModel)&&!string.IsNullOrWhiteSpace(h.Architecture)&&!string.IsNullOrWhiteSpace(h.WindowsBuild),"live mandatory identity absent");
            var inv=JsonSerializer.Deserialize<ManufacturerInventory>(PowerShell.Run("ManufacturerInventory").GetAwaiter().GetResult())!;
            Check(inv.Disks.Count>0,"live disks missing");
            Console.WriteLine($"LIVE {h.SystemManufacturer} / {h.SystemModel}; {h.BoardManufacturer} / {h.BoardModel}; architecture={h.Architecture}; build={h.WindowsBuild}; portable={h.IsPortable}; disks={inv.Disks.Count}; route={ManufacturerRouting.Detect(h).Vendor}; exact={ManufacturerRouting.Detect(h).ExactModelVerified}");
        });
        return failures==0?0:1;
    }
}
