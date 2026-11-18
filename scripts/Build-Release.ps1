[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $parts = $Version.Split('.') | ForEach-Object { [int]$_ }
    if ($parts[0] -gt 255 -or $parts[1] -gt 255 -or $parts[2] -gt 65535) {
        throw 'Version exceeds Windows Installer limits (255.255.65535).'
    }
    $commit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
    $branch = & git branch --show-current
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source branch.' }
    if ([string]::IsNullOrWhiteSpace($branch)) { $branch = 'detached' }
    $changes = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source cleanliness.' }
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw '.NET SDK is unavailable.' }

    $output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root "artifacts\release\$Version" }
    $payload = Join-Path $output 'payload'
    New-Item $output -ItemType Directory -Force | Out-Null
    if (Test-Path $payload) {
        throw "Payload directory already exists: $payload. Use a fresh checkout for a new build."
    }
    New-Item $payload -ItemType Directory | Out-Null

    & dotnet run --project tests\Quickminder.Tests -c Release *> (Join-Path $output 'tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Regression checks failed. See $output\tests.log" }
    & dotnet publish src\Quickminder -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None "-p:Version=$Version" -o $payload *> (Join-Path $output 'publish.log')
    if ($LASTEXITCODE -ne 0) { throw "Publish failed. See $output\publish.log" }
    $files = @(Get-ChildItem $payload -File -Recurse)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'Quickminder.exe') {
        throw 'Expected exactly one self-contained Quickminder.exe; refusing to omit publish files.'
    }

    $name = "Quickminder-$Version-win-x64"
    & dotnet build installer\Quickminder.Installer.wixproj -c Release `
        "-p:AppVersion=$Version" "-p:PayloadDirectory=$payload" "-p:OutputName=$name" `
        "-p:BaseIntermediateOutputPath=$output\installer-obj\" `
        "-p:IntermediateOutputPath=$output\installer-obj\Release\" `
        "-p:OutputPath=$output\" *> (Join-Path $output 'installer.log')
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed. See $output\installer.log" }
    $msi = Join-Path $output "$name.msi"
    if (!(Test-Path $msi)) { throw "Installer missing: $msi" }
    $installerLog = Get-Content (Join-Path $output 'installer.log') -Raw
    $iceValidation = if ($installerLog -match 'WIX1105') {
        'Unavailable: Windows system policy blocked WiX ICE validation (WIX1105). No suppression or elevation used.'
    } else { 'WiX ICE validation completed.' }
    $unexpectedWarnings = @(Select-String -Path (Join-Path $output 'installer.log') -Pattern 'warning (?!WIX1105)\w+\d+:')
    if ($unexpectedWarnings.Count -gt 0) { throw "Unexpected installer warnings. See $output\installer.log" }

    $windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
    $database = $windowsInstaller.OpenDatabase($msi, 0)
    function Read-MsiValue([string]$Query) {
        $view = $database.OpenView($Query)
        try {
            [void]$view.Execute()
            $row = $view.Fetch()
            if ($null -ne $row) {
                try { $row.StringData(1) }
                finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($row) }
            }
        }
        finally {
            [void]$view.Close()
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        }
    }
    try {
        if ((Read-MsiValue 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductVersion''') -ne $Version) {
            throw 'MSI ProductVersion does not match the requested release.'
        }
        if (Read-MsiValue 'SELECT `Value` FROM `Property` WHERE `Property` = ''ALLUSERS''') {
            throw 'MSI must install per-user, not per-machine.'
        }
        if ((Read-MsiValue 'SELECT `Directory_Parent` FROM `Directory` WHERE `Directory` = ''UserProgramsFolder''') -ne 'LocalAppDataFolder' -or
            (Read-MsiValue 'SELECT `Directory_Parent` FROM `Directory` WHERE `Directory` = ''INSTALLFOLDER''') -ne 'UserProgramsFolder') {
            throw 'MSI install directory is not under LocalAppData\Programs.'
        }
        if ((Read-MsiValue 'SELECT `Target` FROM `Shortcut` WHERE `Shortcut` = ''StartMenuShortcut''') -ne '[#QuickminderExe]' -or
            (Read-MsiValue 'SELECT `Directory_` FROM `Shortcut` WHERE `Shortcut` = ''StartMenuShortcut''') -ne 'QuickminderMenu') {
            throw 'MSI Start menu shortcut does not target the installed app.'
        }
        if (Read-MsiValue 'SELECT `Name` FROM `_Tables` WHERE `Name` = ''CustomAction''') {
            if (Read-MsiValue 'SELECT `Action` FROM `CustomAction`') {
                throw 'Unexpected installer custom action.'
            }
        }
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($windowsInstaller)
    }

    $extraction = Join-Path $output 'extracted'
    $extractLog = Join-Path $output 'extract.log'
    $process = Start-Process msiexec.exe -ArgumentList @('/a', "`"$msi`"", '/qn', "TARGETDIR=`"$extraction`"", '/L*v', "`"$extractLog`"") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "MSI extraction failed ($($process.ExitCode)). See $extractLog" }
    $extracted = @(Get-ChildItem $extraction -Filter Quickminder.exe -File -Recurse)
    if ($extracted.Count -ne 1 -or
        (Get-FileHash $extracted[0].FullName).Hash -ne (Get-FileHash (Join-Path $payload 'Quickminder.exe')).Hash) {
        throw 'MSI payload did not round-trip with the same SHA-256.'
    }

    $zip = Join-Path $output "$name-portable.zip"
    Compress-Archive -LiteralPath (Join-Path $payload 'Quickminder.exe') -DestinationPath $zip
    $artifacts = @($msi, $zip) | ForEach-Object {
        [ordered]@{
            name = Split-Path $_ -Leaf
            bytes = (Get-Item $_).Length
            sha256 = (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $record = Join-Path $output 'release-record.json'
    [ordered]@{
        version = $Version
        sourceCommit = $commit.Trim()
        sourceBranch = $branch.Trim()
        sourceDirty = [bool]$changes
        builtUtc = [DateTime]::UtcNow.ToString('o')
        dotnetSdk = $sdk.Trim()
        runtime = 'win-x64'
        configuration = 'Release; self-contained; single-file; native libraries self-extract'
        installer = 'WiX 5.0.2; per-user MSI; no auto-start; preserves notes and preferences on uninstall'
        verification = 'Regression executable passed; single-file payload verified; MSI version, per-user scope, install path, Start shortcut and absence of custom actions checked; administrative extraction matches published EXE SHA-256'
        iceValidation = $iceValidation
        signing = 'Unsigned; no signing certificate configured'
        artifacts = $artifacts
    } | ConvertTo-Json -Depth 5 | Set-Content $record -Encoding utf8
    @($msi, $zip, $record) | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf)
    } | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Release artifacts: $output"
    if ($installerLog -match 'WIX1105') { Write-Warning $iceValidation }
    if ($changes) { Write-Warning 'Built from uncommitted changes. Validation only; do not publish this build.' }
}
finally {
    Pop-Location
}
