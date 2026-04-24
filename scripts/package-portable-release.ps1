[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$IncludeRuntimeBinaries,
    [switch]$IncludePackageCache,
    [switch]$IncludeUserData,
    [switch]$SkipPublish,
    [switch]$SkipInstaller,
    [string]$Channel = 'stable',
    [string]$ReleasePageUri = '',
    [string]$DotNetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    if ($env:GITHUB_REF_TYPE -eq 'tag' -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) {
        $Version = $env:GITHUB_REF_NAME.TrimStart('v')
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_NUMBER)) {
        $Version = "0.0.0-ci.$($env:GITHUB_RUN_NUMBER)"
    }
    else {
        $Version = '0.0.0-local'
    }
}

$safeVersion = ($Version -replace '[^\w\.-]', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($safeVersion)) {
    throw 'Version resolved to an empty package label.'
}

$publishRoot = Join-Path $root "artifacts\publish\Locora.App-$RuntimeIdentifier"
$distRoot = Join-Path $root 'artifacts\dist'
$stageRoot = Join-Path $distRoot "Locora-$safeVersion-$RuntimeIdentifier"
$zipPath = Join-Path $distRoot "Locora-$safeVersion-$RuntimeIdentifier-portable.zip"
$installerPath = Join-Path $distRoot "Locora-$safeVersion-$RuntimeIdentifier-setup.exe"
$checksumsPath = Join-Path $distRoot 'checksums.txt'
$releaseManifestPath = Join-Path $distRoot 'release-manifest.json'

function Resolve-DotNetExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Candidate
    )

    if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $programFilesDotNet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $programFilesDotNet -PathType Leaf) {
        return $programFilesDotNet
    }

    throw "dotnet executable was not found. Pass -DotNetPath with the full path to dotnet.exe."
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null
    Get-ChildItem -LiteralPath $SourcePath -Force | Copy-Item -Destination $DestinationPath -Recurse -Force
}

function Get-LowerSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotNetExecutable,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Write-Host "$Description..."
    $process = Start-Process -FilePath $DotNetExecutable -ArgumentList $Arguments -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$Description failed with exit code $($process.ExitCode)."
    }
}

function Write-InstallerSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallerSourceRoot,
        [Parameter(Mandatory = $true)]
        [string]$InstallerVersion
    )

    $projectPath = Join-Path $InstallerSourceRoot 'Locora.Setup.csproj'
    $programPath = Join-Path $InstallerSourceRoot 'Program.cs'

    Set-Content -Encoding UTF8 -Path $projectPath -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Locora.Setup</AssemblyName>
    <Version>$InstallerVersion</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <InformationalVersion>$InstallerVersion</InformationalVersion>
    <Authors>CodeLevel8 Team</Authors>
    <Company>CodeLevel8 Team</Company>
    <Product>Locora Setup</Product>
    <Copyright>Copyright (c) CodeLevel8 Team</Copyright>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include="payload.zip" LogicalName="payload.zip" />
  </ItemGroup>
</Project>
"@

    $programSource = @'
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    private const string Version = "{{VERSION}}";
    private const string AppName = "Locora";
    private const string PayloadResourceName = "payload.zip";

    [STAThread]
    public static int Main(string[] args)
    {
        var quiet = HasSwitch(args, "/quiet", "--quiet");
        var noLaunch = HasSwitch(args, "/no-launch", "--no-launch");

        try
        {
            var installRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                AppName);

            if (IsRunningFromInstallRoot(installRoot))
            {
                throw new InvalidOperationException("Close Locora before installing this update, then run setup again.");
            }

            Directory.CreateDirectory(installRoot);
            ExtractPayload(installRoot);

            var appPath = Path.Combine(installRoot, "Locora.App.exe");
            if (!File.Exists(appPath))
            {
                throw new FileNotFoundException("The installer did not find Locora.App.exe after extraction.", appPath);
            }

            CreateShortcuts(appPath, installRoot);
            WriteUninstallCommand(installRoot);

            if (!quiet)
            {
                MessageBox.Show(
                    $"Locora {Version} was installed for this Windows user.\n\nInstall folder:\n{installRoot}",
                    "Locora Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            if (!noLaunch)
            {
                Process.Start(new ProcessStartInfo(appPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = installRoot
                });
            }

            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Locora Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return 1;
        }
    }

    private static bool HasSwitch(string[] args, params string[] names)
    {
        return args.Any(arg => names.Any(name => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static void ExtractPayload(string installRoot)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("Installer payload is missing.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var rootPrefix = archive.Entries
            .Select(entry => NormalizeEntryPath(entry.FullName).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var fullInstallRoot = Path.GetFullPath(installRoot);
        foreach (var entry in archive.Entries)
        {
            var relativePath = StripRootPrefix(NormalizeEntryPath(entry.FullName), rootPrefix);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(fullInstallRoot, relativePath));
            if (!destinationPath.StartsWith(fullInstallRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destinationPath, fullInstallRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Installer payload contains an unsafe path: {entry.FullName}");
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static string NormalizeEntryPath(string value)
    {
        return value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string StripRootPrefix(string value, string? rootPrefix)
    {
        if (string.IsNullOrWhiteSpace(rootPrefix))
        {
            return value.TrimStart(Path.DirectorySeparatorChar);
        }

        var prefix = rootPrefix + Path.DirectorySeparatorChar;
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }

    private static bool IsRunningFromInstallRoot(string installRoot)
    {
        var fullInstallRoot = Path.GetFullPath(installRoot);
        foreach (var process in Process.GetProcessesByName("Locora.App"))
        {
            try
            {
                var fileName = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    Path.GetFullPath(fileName).StartsWith(fullInstallRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore processes we cannot inspect.
            }
        }

        return false;
    }

    private static void CreateShortcuts(string appPath, string installRoot)
    {
        var desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Locora.lnk");
        var startMenuShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Locora.lnk");

        CreateShortcut(desktopShortcut, appPath, installRoot);
        CreateShortcut(startMenuShortcut, appPath, installRoot);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            var shortcutType = shortcut!.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Locora local development manager" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void WriteUninstallCommand(string installRoot)
    {
        var uninstallPath = Path.Combine(installRoot, "Uninstall Locora.cmd");
        var desktopShortcut = Path.Combine("%USERPROFILE%", "Desktop", "Locora.lnk");
        var startMenuShortcut = Path.Combine("%APPDATA%", "Microsoft", "Windows", "Start Menu", "Programs", "Locora.lnk");

        File.WriteAllText(uninstallPath, $$"""
@echo off
echo This will remove Locora shortcuts and installed files for the current user.
echo.
pause
del "{{desktopShortcut}}" 2>nul
del "{{startMenuShortcut}}" 2>nul
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Sleep -Seconds 1; Remove-Item -LiteralPath '{{installRoot.Replace("'", "''")}}' -Recurse -Force"
""");
    }
}
'@

    $programSource = $programSource.Replace('{{VERSION}}', $InstallerVersion)
    Set-Content -Encoding UTF8 -Path $programPath -Value $programSource

    return $projectPath
}

function New-LocoraInstaller {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PayloadZipPath,
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        [Parameter(Mandatory = $true)]
        [string]$InstallerVersion,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [string]$DotNetExecutable
    )

    $installerWorkRoot = Join-Path $root "artifacts\installer\Locora.Setup-$safeVersion-$RuntimeIdentifier"
    $installerOutRoot = Join-Path $installerWorkRoot 'publish'
    if (Test-Path -LiteralPath $installerWorkRoot) {
        Remove-Item -LiteralPath $installerWorkRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $installerWorkRoot, $installerOutRoot | Out-Null
    Copy-Item -LiteralPath $PayloadZipPath -Destination (Join-Path $installerWorkRoot 'payload.zip') -Force
    $installerProjectPath = Write-InstallerSource -InstallerSourceRoot $installerWorkRoot -InstallerVersion $InstallerVersion

    Invoke-DotNetCommand `
        -DotNetExecutable $DotNetExecutable `
        -Description 'Publishing Locora setup installer' `
        -Arguments @(
            'publish',
            $installerProjectPath,
            '-c',
            'Release',
            '-r',
            $RuntimeIdentifier,
            '--self-contained',
            'true',
            '-p:PublishSingleFile=true',
            '-p:EnableCompressionInSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-o',
            $installerOutRoot)

    $publishedInstaller = Join-Path $installerOutRoot 'Locora.Setup.exe'
    if (-not (Test-Path -LiteralPath $publishedInstaller -PathType Leaf)) {
        throw "Installer output was not found: $publishedInstaller"
    }

    Copy-Item -LiteralPath $publishedInstaller -Destination $OutputPath -Force
}

$dotnet = Resolve-DotNetExecutable -Candidate $DotNetPath
if (-not $SkipPublish -and (Test-Path -LiteralPath $publishRoot)) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishRoot, $distRoot | Out-Null

if (-not $SkipPublish) {
    $projectPath = Join-Path $root 'src\Locora.App\Locora.App.csproj'
    Invoke-DotNetCommand `
        -DotNetExecutable $dotnet `
        -Description 'Publishing Locora app' `
        -Arguments @(
            'publish',
            $projectPath,
            '-c',
            $Configuration,
            '-r',
            $RuntimeIdentifier,
            '--self-contained',
            'true',
            '-p:SelfContained=true',
            '-p:WindowsPackageType=None',
            '-p:WindowsAppSDKSelfContained=true',
            '-p:AllowUnsafeBlocks=true',
            '-p:PublishSingleFile=false',
            '-p:Platform=x64',
            '-o',
            $publishRoot)
}

if (-not (Test-Path -LiteralPath $publishRoot)) {
    throw "Publish folder does not exist: $publishRoot"
}

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

Copy-DirectoryContents $publishRoot $stageRoot

$portableDirs = @(
    'usr',
    'usr\config',
    'usr\logs',
    'usr\profiles',
    'usr\aliases',
    'usr\shell',
    'usr\templates',
    'usr\cache',
    'usr\updates',
    'usr\distribution',
    'www',
    'bin',
    'data',
    'temp',
    'archives',
    'package-manifests'
)

foreach ($relativePath in $portableDirs) {
    New-Item -ItemType Directory -Force -Path (Join-Path $stageRoot $relativePath) | Out-Null
}

$packageManifestsRoot = Join-Path $root 'package-manifests'
Copy-DirectoryContents $packageManifestsRoot (Join-Path $stageRoot 'package-manifests')

if ($IncludeUserData) {
    foreach ($relativePath in @('usr\config', 'usr\profiles', 'www', 'data')) {
        $sourcePath = Join-Path $root $relativePath
        Copy-DirectoryContents $sourcePath (Join-Path $stageRoot $relativePath)
    }
}

if ($IncludeRuntimeBinaries) {
    $binRoot = Join-Path $root 'bin'
    Copy-DirectoryContents $binRoot (Join-Path $stageRoot 'bin')
}

if ($IncludePackageCache) {
    $packageCacheRoot = Join-Path $root 'usr\cache\packages'
    Copy-DirectoryContents $packageCacheRoot (Join-Path $stageRoot 'usr\cache\packages')
}

Set-Content -Encoding UTF8 -Path (Join-Path $stageRoot 'README.txt') -Value @"
Locora portable distribution

Extract this folder anywhere writable, then run Locora.App.exe.
This build is self-contained for .NET and Windows App SDK dependencies.
The first run creates or refreshes usr/config, usr/logs, package manifests, and portable workspace folders.
"@

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -LiteralPath $stageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = Get-LowerSha256 -Path $zipPath

$installerHash = ''
if (-not $SkipInstaller) {
    if (Test-Path -LiteralPath $installerPath) {
        Remove-Item -LiteralPath $installerPath -Force
    }

    New-LocoraInstaller `
        -PayloadZipPath $zipPath `
        -OutputPath $installerPath `
        -InstallerVersion $Version `
        -RuntimeIdentifier $RuntimeIdentifier `
        -DotNetExecutable $dotnet
    $installerHash = Get-LowerSha256 -Path $installerPath
}

$checksumLines = @("$zipHash  $(Split-Path $zipPath -Leaf)")
if (-not [string]::IsNullOrWhiteSpace($installerHash)) {
    $checksumLines = @("$installerHash  $(Split-Path $installerPath -Leaf)") + $checksumLines
}
Set-Content -Encoding UTF8 -Path $checksumsPath -Value $checksumLines

$downloadFileName = if ([string]::IsNullOrWhiteSpace($installerHash)) {
    Split-Path $zipPath -Leaf
}
else {
    Split-Path $installerPath -Leaf
}

$downloadHash = if ([string]::IsNullOrWhiteSpace($installerHash)) {
    $zipHash
}
else {
    $installerHash
}

$assets = @()
if (-not [string]::IsNullOrWhiteSpace($installerHash)) {
    $assets += [ordered]@{
        platform = 'windows'
        architecture = 'x64'
        kind = 'installer'
        downloadUri = (Split-Path $installerPath -Leaf)
        sha256 = $installerHash
    }
}

$assets += [ordered]@{
    platform = 'windows'
    architecture = 'x64'
    kind = 'portable'
    downloadUri = (Split-Path $zipPath -Leaf)
    sha256 = $zipHash
}

$releaseManifest = [ordered]@{
    schema = 'locora.app-update.v1'
    appId = 'locora'
    channel = $Channel
    version = $Version
    releaseDate = (Get-Date).ToString('yyyy-MM-dd')
    releaseNotes = 'Self-contained Windows installer and portable distribution generated by release automation.'
    releasePageUri = $ReleasePageUri
    downloadUri = $downloadFileName
    sha256 = $downloadHash
    minimumSupportedVersion = '0.1.0'
    requiresManualInstall = $true
    assets = $assets
}
$releaseManifest | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -Path $releaseManifestPath

Write-Host "Portable distribution created: $zipPath"
Write-Host "Portable SHA-256: $zipHash"
if (-not [string]::IsNullOrWhiteSpace($installerHash)) {
    Write-Host "Installer created: $installerPath"
    Write-Host "Installer SHA-256: $installerHash"
}
Write-Host "Release manifest: $releaseManifestPath"
