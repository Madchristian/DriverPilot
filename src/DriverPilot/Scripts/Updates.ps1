$session=New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID='DriverPilot'
$search=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
if($search.ResultCode -ne 2){throw "Windows Update meldet eine unvollständige Suche (Code $($search.ResultCode))."}
$rows=@(foreach($u in $search.Updates){[pscustomobject]@{Id=$u.Identity.UpdateID;Title=$u.Title;Manufacturer=[string]$u.DriverManufacturer;Class=[string]$u.DriverClass;Date=$u.LastDeploymentChangeTime.ToString('yyyy-MM-dd');Status='Angeboten · Stabilitätsprüfung im Wartungsauftrag'}})
ConvertTo-Json -InputObject $rows -Depth 4 -Compress
