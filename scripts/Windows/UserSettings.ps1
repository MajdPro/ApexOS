param(
    [ValidateSet('Status','StartupList','DisableStartup','RestoreStartup','OpenStartup','OpenSearchIndex','OpenIndexOptions','OpenBackgroundApps','OpenGraphics','OpenGaming','OpenGameBar','OpenStorage','OpenWindowsUpdate')][string]$Mode='Status',
    [string]$Name,
    [ValidateSet('Registry64','Registry32')][string]$RegistryView='Registry64'
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '..\Modules\Apex.Common.psm1') -Force

function Get-CurrentUserRunEntries {
    $entries=[Collections.Generic.List[object]]::new()
    foreach($viewName in @('Registry64','Registry32')){
        $view=[Enum]::Parse([Microsoft.Win32.RegistryView],$viewName)
        $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
        try {
            $run=$base.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$false)
            if(-not $run){continue}
            try {
                foreach($entryName in $run.GetValueNames()){
                    $kind=$run.GetValueKind($entryName)
                    if($kind -notin @([Microsoft.Win32.RegistryValueKind]::String,[Microsoft.Win32.RegistryValueKind]::ExpandString)){continue}
                    $entries.Add([pscustomobject]@{
                        Name=$entryName
                        RegistryView=$viewName
                        Command=[string]$run.GetValue($entryName,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                        Kind=[string]$kind
                    })
                }
            } finally {$run.Dispose()}
        } finally {$base.Dispose()}
    }
    return @($entries|Sort-Object RegistryView,Name)
}

function Write-StartupSnapshot {
    param([Parameter(Mandatory)][object]$Entry)
    $path=Get-ApexSnapshotPath -Name 'startup-run-entries'
    $snapshots=@()
    if(Test-Path -LiteralPath $path){$snapshots=@(Get-Content -LiteralPath $path -Raw|ConvertFrom-Json)}
    if(-not($snapshots|Where-Object { $_.Name -eq $Entry.Name -and $_.RegistryView -eq $Entry.RegistryView })){
        $snapshots+= [pscustomobject]@{Name=$Entry.Name;RegistryView=$Entry.RegistryView;Command=$Entry.Command;Kind=$Entry.Kind;SavedAt=(Get-Date).ToString('o')}
        $snapshots|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $path -Encoding UTF8
    }
}

function Restore-StartupEntries {
    $path=Get-ApexSnapshotPath -Name 'startup-run-entries'
    if(-not(Test-Path -LiteralPath $path)){throw 'No disabled startup-entry backup is available.'}
    $snapshots=@(Get-Content -LiteralPath $path -Raw|ConvertFrom-Json)
    $pending=[Collections.Generic.List[object]]::new()
    $restored=0
    foreach($entry in $snapshots){
        $view=[Enum]::Parse([Microsoft.Win32.RegistryView],[string]$entry.RegistryView)
        $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
        try {
            $run=$base.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
            try {
                if($run.GetValueNames() -contains [string]$entry.Name){$pending.Add($entry);continue}
                $kind=[Enum]::Parse([Microsoft.Win32.RegistryValueKind],[string]$entry.Kind)
                $run.SetValue([string]$entry.Name,[string]$entry.Command,$kind)
                $restored++
            } finally {$run.Dispose()}
        } finally {$base.Dispose()}
    }
    if($pending.Count -eq 0){Remove-Item -LiteralPath $path -Force}
    else {$pending|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $path -Encoding UTF8}
    return [pscustomobject]@{Restored=$restored;Conflicts=$pending.Count}
}
try {
    if($Mode -eq 'Status'){'Ready';exit 0}
    switch($Mode){
        'StartupList' {
            $entries=@(Get-CurrentUserRunEntries)
            ConvertTo-Json -InputObject $entries -Depth 5 -Compress
            $null=Write-ApexLog -Action 'Startup Inventory' -Result 'Complete' -Message "CurrentUserRunEntries=$($entries.Count)"
        }
        'DisableStartup' {
            if([string]::IsNullOrWhiteSpace($Name)){throw 'Choose a startup entry before disabling it.'}
            $entry=Get-CurrentUserRunEntries|Where-Object { $_.Name -ceq $Name -and $_.RegistryView -eq $RegistryView }|Select-Object -First 1
            if(-not $entry){throw "The selected startup entry is no longer present: $Name ($RegistryView). Refresh the inventory and try again."}
            Write-StartupSnapshot -Entry $entry
            $view=[Enum]::Parse([Microsoft.Win32.RegistryView],$RegistryView)
            $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
            try {
                $run=$base.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
                if(-not $run){throw 'The current-user startup registry key is unavailable.'}
                try {$run.DeleteValue($Name,$false)}finally{$run.Dispose()}
            } finally {$base.Dispose()}
            if(Get-CurrentUserRunEntries|Where-Object { $_.Name -ceq $Name -and $_.RegistryView -eq $RegistryView }){throw 'Windows retained the startup entry after the disable request.'}
            $log=Write-ApexLog -Action 'Disable Startup Entry' -Result 'Success' -Message "Name=$Name; View=$RegistryView; Backup=Backups/Settings/startup-run-entries.json"
            "Disabled current-user startup entry '$Name'. The original value is backed up for restore. Log: $log"
        }
        'RestoreStartup' {
            $result=Restore-StartupEntries
            $outcome=if($result.Conflicts){'Partial'}else{'Success'}
            $log=Write-ApexLog -Action 'Restore Startup Entries' -Result $outcome -Message "Restored=$($result.Restored); Conflicts=$($result.Conflicts); ExistingEntriesWereNotOverwritten=True"
            "Restored $($result.Restored) disabled startup entries. Existing values were not overwritten; $($result.Conflicts) conflicting backup entries remain saved. Log: $log"
        }
        'OpenStartup' {$uri='ms-settings:startupapps';$message='Opened Windows Startup Apps settings. Use Windows controls to review and change individual entries.'}
        'OpenSearchIndex' {$uri='ms-settings:search';$message='Opened Windows Search settings. Indexing options remain under user control.'}
        'OpenIndexOptions' {Start-Process -FilePath 'control.exe' -ArgumentList @('/name','Microsoft.IndexingOptions');$log=Write-ApexLog -Action 'Open Indexing Options' -Result 'Success';'Opened Windows Indexing Options. No service settings were changed.';exit 0}
        'OpenBackgroundApps' {$uri='ms-settings:appsfeatures';$message='Opened Windows Installed apps settings. Review app-specific background permissions there.'}
        'OpenGraphics' {$uri='ms-settings:display-advancedgraphics';$message='Opened Windows Graphics settings. Choose a per-app GPU preference in Windows.'}
        'OpenGaming' {$uri='ms-settings:gaming-gamemode';$message='Opened Windows Game Mode settings. Apex does not remove Xbox or Gaming Services components.'}
        'OpenGameBar' {$uri='ms-settings:gaming-gamebar';$message='Opened Windows Game Bar settings. Game DVR capture preferences are managed separately by Apex Gaming Mode.'}
        'OpenStorage' {$uri='ms-settings:storagesense';$message='Opened Windows Storage settings. Review proposed cleanup categories before deleting anything.'}
        'OpenWindowsUpdate' {$uri='ms-settings:windowsupdate';$message='Opened Windows Update settings. Apex did not change update policy.'}
    }
    if($uri){Start-Process $uri;$log=Write-ApexLog -Action "Open $Mode" -Result 'Success' -Message $uri;"$message`nLog: $log"}
}catch{$log=Write-ApexLog -Action "Windows User Settings $Mode" -Result 'Failed' -Message $_.Exception.Message;[Console]::Error.WriteLine("$($_.Exception.Message) Log: $log");exit 1}