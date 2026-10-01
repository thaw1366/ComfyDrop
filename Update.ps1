param([string]$Package, [string]$Destination, [int]$WaitForProcess = 0, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    if (!$Destination) {
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Select your existing ComfyDrop folder. Saved settings and credentials will be kept.'
        $picker.SelectedPath = Join-Path $env:USERPROFILE 'Downloads\ComfyDrop'
        if ($picker.ShowDialog() -ne 'OK') { exit }
        $Destination = $picker.SelectedPath
        $picker.Dispose()
    }
    $Destination = [IO.Path]::GetFullPath($Destination)
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    $targetExe = Join-Path $Destination 'ComfyDrop.exe'
    if (!$Package -and [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -eq $Destination.TrimEnd('\')) {
        $picker = New-Object System.Windows.Forms.OpenFileDialog
        $picker.Title = 'Choose the new ComfyDrop ZIP'
        $picker.Filter = 'ComfyDrop update ZIP|*.zip'
        if ($picker.ShowDialog() -ne 'OK') { exit }
        $Package = $picker.FileName
        $picker.Dispose()
    }
    if ($WaitForProcess) {
        $process = Get-Process -Id $WaitForProcess -ErrorAction SilentlyContinue
        if ($process -and !$process.WaitForExit(120000)) { throw 'ComfyDrop did not close. Close it and run the updater again.' }
    }
    $running = Get-Process ComfyDrop -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe }
    if ($running) {
        [System.Windows.Forms.MessageBox]::Show('Please close the existing ComfyDrop window, then click OK to continue.','Update ComfyDrop') | Out-Null
        if (Get-Process ComfyDrop -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe }) { throw 'ComfyDrop is still open. No program files were replaced.' }
    }
    $stage = Join-Path $Destination ('.update-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $files = @('ComfyDrop.exe','runpodctl.exe','Update.ps1','Update.cmd','README.md','runpodctl-LICENSE.txt','ComfyDrop.cs','NetworkDrive.cs','AppSettings.cs','Build.ps1')
    if ($Package) {
        $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Package))
        try {
            foreach ($name in $files) {
                $entry = $archive.GetEntry("ComfyDrop/$name")
                if (!$entry) { $entry = $archive.GetEntry($name) }
                if ($entry) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $stage $name),$false) }
            }
        } finally { $archive.Dispose() }
    } else {
        foreach ($name in $files) {
            $source = Join-Path $PSScriptRoot $name
            if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name) }
        }
    }
    $candidate = Join-Path $stage 'ComfyDrop.exe'
    if (!(Test-Path -LiteralPath $candidate)) { throw 'This package does not contain ComfyDrop.exe.' }
    $identity = [Reflection.AssemblyName]::GetAssemblyName($candidate)
    if ($identity.Name -ne 'ComfyDrop' -or $identity.Version -lt [Version]'1.1.0.0') { throw 'This is not a compatible ComfyDrop update.' }
    # Copy legacy profiles only when no central profile exists. Never overwrite credentials.
    $profileRoot = if ($env:COMFYDROP_PROFILE_DIR) { $env:COMFYDROP_PROFILE_DIR } else { Join-Path $env:LOCALAPPDATA 'ComfyDrop' }
    [IO.Directory]::CreateDirectory($profileRoot) | Out-Null
    foreach ($name in @('settings.json','network-profile.json')) {
        $legacy = Join-Path $Destination $name
        $saved = Join-Path $profileRoot $name
        if ((Test-Path -LiteralPath $legacy) -and !(Test-Path -LiteralPath $saved)) { Copy-Item -LiteralPath $legacy -Destination $saved }
    }
    foreach ($name in $files | Where-Object { $_ -ne 'ComfyDrop.exe' }) {
        $source = Join-Path $stage $name
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $Destination $name) -Force }
    }
    if (Test-Path -LiteralPath $targetExe) {
        [IO.File]::Replace($candidate,$targetExe,(Join-Path $Destination 'ComfyDrop.previous.exe'))
    } else { [IO.File]::Move($candidate,$targetExe) }
    if (!$NoLaunch) { Start-Process -FilePath $targetExe }
    Write-Output "Updated ComfyDrop to $($identity.Version). Saved settings preserved."
} catch {
    if ($NoLaunch) { throw }
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'ComfyDrop update failed') | Out-Null
    exit 1
} finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        $resolvedStage = [IO.Path]::GetFullPath($stage)
        $resolvedTarget = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
        if ($resolvedStage.StartsWith($resolvedTarget,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolvedStage).StartsWith('.update-')) {
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
}
