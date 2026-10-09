param(
    [ValidateSet('Status','List','Select','SelectInstalled','Restore','Diagnose','UseAvailable')][string]$Mode='Status',
    [string]$Name='Apex Performance',
    [string]$Guid
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '..\Modules\Apex.Common.psm1') -Force

$apexPlans=@('Apex Maximum Performance','Apex Ultimate Performance','Apex Performance','Apex Balanced','Apex Power Saver','Apex Laptop Performance','Apex Custom')
$processorSubgroup='54533251-82be-4824-96c1-47b60b740d00'
$settings=@{
    Min='893dee8e-2bef-41e0-89c6-b55d0929964c'
    Max='bc5038f7-23e0-4960-96da-33abaf5935ec'
    Boost='be337238-0d82-4146-a960-4f3749d470c7'
}

function Invoke-PowerCfg {
    param([string[]]$Arguments)
    $output=& powercfg.exe @Arguments 2>&1
    $code=$LASTEXITCODE
    if($code -ne 0){throw "powercfg $($Arguments -join ' ') failed (exit $code): $($output -join ' ')"}
    return $output
}

function Get-AvailablePlans {
    $output=Invoke-PowerCfg @('/list')
    $plans=[Collections.Generic.List[object]]::new()
    foreach($line in $output){
        if($line -match '(?<guid>[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}).*?\((?<name>[^()]*)\)'){
            $plans.Add([pscustomobject]@{
                Guid=$matches.guid.ToLowerInvariant()
                Name=$matches.name.Trim()
                Active=($line -match '\*\s*$')
            })
        }
    }
    return @($plans)
}

function Get-ActivePlan {
    $plans=Get-AvailablePlans
    $active=$plans|Where-Object Active|Select-Object -First 1
    if($active){return $active}
    $output=Invoke-PowerCfg @('/getactivescheme')
    $line=$output|Select-Object -First 1
    if($line -match '(?<guid>[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})\s*\((?<name>.*?)\)'){
        return [pscustomobject]@{Guid=$matches.guid.ToLowerInvariant();Name=$matches.name;Active=$true}
    }
    throw "Windows did not return the active power-plan GUID. Output: $line"
}

function Get-PlanByGuid {
    param([string]$Guid)
    return (Get-AvailablePlans|Where-Object { $_.Guid -eq $Guid }|Select-Object -First 1)
}

function Test-PlanSetting {
    param([string]$PlanGuid,[string]$SettingGuid)
    $output=& powercfg.exe /query $PlanGuid $processorSubgroup $SettingGuid 2>&1
    $code=$LASTEXITCODE
    $text=$output -join "`n"
    if($code -ne 0 -or $text -notmatch '(?i)Current (AC|DC) Power Setting Index'){
        $reason=($output|Select-Object -First 1)
        if(-not $reason){$reason='Setting is not exposed by this Windows build or processor.'}
        return [pscustomobject]@{Supported=$false;Output=$text;Reason=[string]$reason}
    }
    return [pscustomobject]@{Supported=$true;Output=$text;Reason=''}
}

function Set-VerifiedProcessorValue {
    param([string]$PlanGuid,[string]$SettingGuid,[string]$PowerSource,[int]$Value,[string]$Label)
    $support=Test-PlanSetting -PlanGuid $PlanGuid -SettingGuid $SettingGuid
    if(-not $support.Supported){
        return [pscustomobject]@{Setting=$Label;PowerSource=$PowerSource;Requested=$Value;Applied=$false;Reason=$support.Reason}
    }
    $verb=if($PowerSource -eq 'AC'){'/setacvalueindex'}else{'/setdcvalueindex'}
    try {
        Invoke-PowerCfg @($verb,$PlanGuid,$processorSubgroup,$SettingGuid,[string]$Value)|Out-Null
    } catch {
        return [pscustomobject]@{Setting=$Label;PowerSource=$PowerSource;Requested=$Value;Applied=$false;Reason=$_.Exception.Message}
    }
    $verify=Test-PlanSetting -PlanGuid $PlanGuid -SettingGuid $SettingGuid
    $sourceLabel=if($PowerSource -eq 'AC'){'AC'}else{'DC'}
    $pattern="(?im)Current $sourceLabel Power Setting Index:\s*0x(?<value>[0-9a-f]+)"
    if(-not $verify.Supported -or $verify.Output -notmatch $pattern){
        throw "Could not verify $Label on $PowerSource power for plan $PlanGuid."
    }
    $actual=[Convert]::ToInt32($matches.value,16)
    if($actual -ne $Value){throw "$Label on $PowerSource power remained $actual; requested $Value."}
    return [pscustomobject]@{Setting=$Label;PowerSource=$PowerSource;Requested=$Value;Applied=$true;Reason='Verified'}
}

function Get-ProfileMap {
    param([object[]]$Plans)
    $map=@{}
    foreach($profile in $apexPlans){
        $match=$Plans|Where-Object { $_.Name -eq $profile }|Select-Object -First 1
        if($match){$map[$profile]=$match.Guid}
    }
    return $map
}

function Ensure-ApexPlan {
    param([string]$ProfileName)
    $plans=Get-AvailablePlans
    $existing=$plans|Where-Object { $_.Name -eq $ProfileName }|Select-Object -First 1
    if($existing){return [pscustomobject]@{Plan=$existing;Created=$false;SourceName=$null}}
    $source=$null
    if($ProfileName -in @('Apex Maximum Performance','Apex Ultimate Performance')){
        $source=$plans|Where-Object { $_.Name -eq 'Ultimate Performance' }|Select-Object -First 1
    }
    if(-not $source){$source=$plans|Where-Object { $_.Name -eq 'Balanced' }|Select-Object -First 1}
    if(-not $source){$source=Get-ActivePlan}
    $duplicate=Invoke-PowerCfg @('/duplicatescheme',$source.Guid)
    $duplicateText=$duplicate -join ' '
    if($duplicateText -notmatch '(?<guid>[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})'){
        throw "Windows duplicated a plan but returned no parseable GUID: $duplicateText"
    }
    $newGuid=$matches.guid.ToLowerInvariant()
    Invoke-PowerCfg @('/changename',$newGuid,$ProfileName,"Apex OS $ProfileName")|Out-Null
    $created=Get-PlanByGuid -Guid $newGuid
    if(-not $created){throw "Created plan '$ProfileName' ($newGuid) is not listed by Windows after naming."}
    return [pscustomobject]@{Plan=$created;Created=$true;SourceName=$source.Name}
}

function Set-ProfilePolicy {
    param([string]$ProfileName,[string]$PlanGuid)
    $results=[Collections.Generic.List[object]]::new()
    switch($ProfileName){
        {$_ -in @('Apex Maximum Performance','Apex Ultimate Performance')} {
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Min 'AC' 100 'Processor minimum'))
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Max 'AC' 100 'Processor maximum'))
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Boost 'AC' 2 'Processor boost mode'))
        }
        'Apex Performance' {
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Max 'AC' 100 'Processor maximum'))
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Boost 'AC' 3 'Processor boost mode'))
        }
        'Apex Power Saver' {
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Max 'AC' 70 'Processor maximum'))
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Max 'DC' 60 'Processor maximum'))
        }
        'Apex Laptop Performance' {
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Max 'AC' 100 'Processor maximum'))
            $results.Add((Set-VerifiedProcessorValue $PlanGuid $settings.Max 'DC' 80 'Processor maximum'))
        }
    }
    return @($results)
}

function Save-PlanState {
    param([string]$Path,[string]$PreviousGuid,[hashtable]$Profiles)
    [pscustomobject]@{PreviousGuid=$PreviousGuid;Profiles=$Profiles}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $Path -Encoding UTF8
}

try {
    $active=Get-ActivePlan
    $plans=Get-AvailablePlans
    $profiles=Get-ProfileMap -Plans $plans
    $deviceType=Get-ApexDeviceType

    if($Mode -eq 'List'){
        ConvertTo-Json -InputObject @($plans) -Depth 4 -Compress
        $null=Write-ApexLog -Action 'Installed Power Plan Inventory' -Result 'Complete' -Message "Plans=$($plans.Count); Active=$($active.Name); ActiveGuid=$($active.Guid)"
        exit 0
    }

    if($Mode -eq 'Status'){
        if($Name -and $Name -in $apexPlans){
            $guid=$profiles[$Name]
            $report=[pscustomobject]@{Profile=$Name;Available=[bool]$guid;Guid=$guid;Active=($active.Name -eq $Name);CurrentPlan=$active.Name;DeviceType=$deviceType}
        }else{
            $report=[pscustomobject]@{CurrentPlan=$active.Name;ActiveGuid=$active.Guid;DeviceType=$deviceType;Profiles=@(foreach($profile in $apexPlans){[pscustomobject]@{Name=$profile;Available=[bool]$profiles[$profile];Guid=$profiles[$profile];Active=($active.Name -eq $profile)}})}
        }
        $report|ConvertTo-Json -Depth 5
        exit 0
    }

    if($Mode -eq 'Diagnose'){
        $checks=foreach($entry in $settings.GetEnumerator()){
            $query=Test-PlanSetting -PlanGuid $active.Guid -SettingGuid $entry.Value
            [pscustomobject]@{Setting=$entry.Key;Guid=$entry.Value;Supported=$query.Supported;Reason=$query.Reason;Query=$query.Output}
        }
        [pscustomobject]@{DeviceType=$deviceType;ActivePlan=$active;AvailablePlans=$plans;ApexProfiles=@(foreach($profile in $apexPlans){[pscustomobject]@{Name=$profile;Guid=$profiles[$profile];Available=[bool]$profiles[$profile]}});ProcessorControls=$checks}|ConvertTo-Json -Depth 7
        $log=Write-ApexLog -Action 'Power Plan Diagnostics' -Result 'Complete' -Message "Active=$($active.Name); Device=$deviceType"
        "`nLog: $log"
        exit 0
    }

    $statePath=Get-ApexSnapshotPath 'power-plans'
    $state=if(Test-Path -LiteralPath $statePath){Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json}else{$null}

    if($Mode -eq 'Restore'){
        if(-not $state -or -not $state.PreviousGuid){throw 'No saved active power plan is available to restore.'}
        $previous=Get-PlanByGuid -Guid ([string]$state.PreviousGuid)
        if(-not $previous){throw "Saved power plan $($state.PreviousGuid) is no longer available. No powercfg switch was attempted."}
        if(-not $previous){throw "Saved power plan $($state.PreviousGuid) is unavailable. Use an installed Windows power plan instead."}
        Invoke-PowerCfg @('/setactive',$previous.Guid)|Out-Null
        $verified=Get-ActivePlan
        if($verified.Guid -ne $previous.Guid){throw "Windows did not activate the saved plan '$($previous.Name)' ($($previous.Guid))."}
        $state.PreviousGuid=$null
        $state|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $statePath -Encoding UTF8
        $log=Write-ApexLog -Action 'Power Plan Restore' -Result 'Success' -Message "Guid=$($previous.Guid); Name=$($previous.Name)"
        [pscustomobject]@{Result='Success';ActivePlan=$verified.Name;ActiveGuid=$verified.Guid;Log=$log}|ConvertTo-Json -Depth 3
        exit 0
    }

    if($Mode -eq 'UseAvailable'){
        $compatible=$plans|Where-Object { $_.Name -eq 'Balanced' }|Select-Object -First 1
        if(-not $compatible){$compatible=$active}
        if($compatible.Guid -ne $active.Guid){
            Invoke-PowerCfg @('/setactive',$compatible.Guid)|Out-Null
            $verified=Get-ActivePlan
            if($verified.Guid -ne $compatible.Guid){throw "Windows did not activate available compatible plan '$($compatible.Name)'."}
        }else{$verified=$active}
        $log=Write-ApexLog -Action 'Use Available Power Plan' -Result 'Success' -Message "Guid=$($verified.Guid); Name=$($verified.Name)"
        [pscustomobject]@{Result='Success';Message='Using an already-installed Windows plan; no unsupported plan was created.';ActivePlan=$verified.Name;ActiveGuid=$verified.Guid;Log=$log}|ConvertTo-Json -Depth 3
        exit 0
    }

    if($Mode -eq 'SelectInstalled'){
        $selected=Get-PlanByGuid -Guid $Guid
        if(-not $selected){throw "Power plan GUID '$Guid' is not currently installed. Refresh the plan list and choose an available scheme."}
        $previousGuid=if($state -and $state.PreviousGuid){[string]$state.PreviousGuid}else{$active.Guid}
        Save-PlanState -Path $statePath -PreviousGuid $previousGuid -Profiles $profiles
        Invoke-PowerCfg @('/setactive',$selected.Guid)|Out-Null
        $verified=Get-ActivePlan
        if($verified.Guid -ne $selected.Guid){throw "Windows did not activate installed plan '$($selected.Name)' ($($selected.Guid))."}
        $log=Write-ApexLog -Action 'Select Installed Power Plan' -Result 'Success' -Message "Guid=$($verified.Guid); Name=$($verified.Name); PreviousGuid=$previousGuid"
        [pscustomobject]@{Result='Success';ActivePlan=$verified.Name;ActiveGuid=$verified.Guid;Log=$log}|ConvertTo-Json -Depth 4
        exit 0
    }

    if($Mode -ne 'Select' -or $Name -notin $apexPlans){throw "Unsupported Apex power plan request: '$Name'."}
    $statePath=Get-ApexSnapshotPath 'power-plans'
    $state=if(Test-Path -LiteralPath $statePath){Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json}else{$null}
    $previousGuid=if($state -and $state.PreviousGuid){[string]$state.PreviousGuid}else{$active.Guid}
    $ensured=Ensure-ApexPlan -ProfileName $Name
    $profile=$ensured.Plan
    $profiles[$Name]=$profile.Guid
    Save-PlanState -Path $statePath -PreviousGuid $previousGuid -Profiles $profiles

    $settingsResult=Set-ProfilePolicy -ProfileName $Name -PlanGuid $profile.Guid
    Invoke-PowerCfg @('/setactive',$profile.Guid)|Out-Null
    $verified=Get-ActivePlan
    if($verified.Guid -ne $profile.Guid){throw "Windows did not activate '$Name' ($($profile.Guid)); active GUID is $($verified.Guid)."}
    $skipped=@($settingsResult|Where-Object { -not $_.Applied })
    $result=[pscustomobject]@{
        Result='Success'
        Profile=$Name
        Guid=$profile.Guid
        ActivePlan=$verified.Name
        DeviceType=$deviceType
        Created=$ensured.Created
        ClonedFrom=$ensured.SourceName
        AppliedSettings=$settingsResult
        SkippedSettings=$skipped
        BatteryNote=if($Name -in @('Apex Maximum Performance','Apex Ultimate Performance') -and $deviceType -match 'Laptop'){ 'Maximum performance processor changes apply only on AC; battery values remain cloned from the selected source plan.' }else{$null}
    }
    $log=Write-ApexLog -Action 'Power Plan Select' -Result 'Success' -Message ($result|ConvertTo-Json -Depth 6 -Compress)
    $result|Add-Member -NotePropertyName Log -NotePropertyValue $log
    $result|ConvertTo-Json -Depth 7
}catch{
    $log=Write-ApexLog -Action "Power Plan $Mode" -Result 'Failed' -Message $_.Exception.Message
    [Console]::Error.WriteLine("Power plan unavailable or could not be applied. $($_.Exception.Message) Log: $log")
    exit 1
}