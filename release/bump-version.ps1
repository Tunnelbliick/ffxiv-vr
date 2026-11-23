param(
    [switch]$Revision
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$projectPath = Join-Path $PWD "FfxivVr/FfxivVr.csproj"
$xml = [xml]::new()
$xml.PreserveWhitespace = $true
$xml.Load($projectPath)
$versionNode = $xml.SelectSingleNode("/Project/PropertyGroup/Version")

$projectVersion = [version]$versionNode.InnerText
$currentVersion = $projectVersion.ToString()
if ($Revision) {
    $nextVersion = "{0}.{1}.{2}.{3}" -f $projectVersion.Major, $projectVersion.Minor, $projectVersion.Build, ([Math]::Max($projectVersion.Revision, 0) + 1)
}
else {
    $nextVersion = "{0}.{1}.{2}" -f $projectVersion.Major, $projectVersion.Minor, ($projectVersion.Build + 1)
}
$versionString = "v$nextVersion"

$previousTag = git describe --tags --abbrev=0 --match "v[0-9]*"
$changeLog = git log --pretty=format:"# %s%n%b" "$previousTag..HEAD" --invert-grep --grep="Publish Version"
if (!$changeLog) {
    Write-Host "Changelog was empty, exiting"
    Exit 1
}

Write-Host "Bumping version from $currentVersion to $nextVersion"
Write-Host "=== Change Log ==="
Write-Host ($changeLog -join "`n")

$versionNode.InnerText = $nextVersion
$xml.Save($projectPath)

Set-Content -Path "release/changelog.txt" -Value $changeLog

if ($env:GITHUB_OUTPUT) {
    "VERSION_STRING=$versionString" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}
