param(
    [Parameter(Mandatory)]
    [string]$VersionString,
    [Parameter(Mandatory)]
    [string]$Repository,
    [switch]$KeepListing
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$nextVersion = $VersionString -replace '^v', ''
$assemblyVersion = (($nextVersion -split '\.') + @('0') * 4)[0..3] -join '.'

$manifest = Get-Content "FfxivVr/bin/x64/Release/FfxivVR/FfxivVR.json" -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.AssemblyVersion -ne $assemblyVersion) {
    throw "Built manifest version $($manifest.AssemblyVersion) does not match $nextVersion"
}

if ($KeepListing) {
    $published = @(Get-Content "PluginRepo/pluginmaster.json" -Raw | ConvertFrom-Json -AsHashtable)[0]
    foreach ($key in "Name", "Description", "Punchline") {
        $manifest[$key] = $published[$key]
    }
}

$downloadLink = "https://github.com/$Repository/releases/download/$VersionString/FfxivVR.zip"
$manifest.RepoUrl = "https://github.com/$Repository"
$manifest.Changelog = (Get-Content "release/changelog.txt" -Raw).TrimEnd()
$manifest.DownloadLinkInstall = $downloadLink
$manifest.DownloadLinkTesting = $downloadLink
$manifest.DownloadLinkUpdate = $downloadLink
$manifest.LastUpdated = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

ConvertTo-Json -InputObject @($manifest) -Depth 32 | Set-Content "PluginRepo/pluginmaster.json"

git config user.name "github-actions[bot]"
git config user.email "41898283+github-actions[bot]@users.noreply.github.com"
git add FfxivVr/FfxivVr.csproj PluginRepo/pluginmaster.json release/changelog.txt
git commit -m "Publish Version $nextVersion"
git tag -a $VersionString -m "FFXIV VR $VersionString"
