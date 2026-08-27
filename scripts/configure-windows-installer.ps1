param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$manifest = Join-Path $env:TEMP "KuiperZone.PupNet/cc.tiouo.Portal-$Runtime-$Configuration-Setup/Portal.Desktop.iss"
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "PupNet Inno Setup manifest was not found: $manifest"
}

$content = [System.IO.File]::ReadAllText($manifest)
$anchor = "AllowNoIcons=yes"
if (-not $content.Contains($anchor)) {
    throw "PupNet Inno Setup manifest does not contain the expected anchor: $anchor"
}
if ($content -match "(?m)^DisableDirPage=") {
    throw "PupNet Inno Setup manifest already configures DisableDirPage; review this script."
}

$content = $content.Replace($anchor, "$anchor`r`nDisableDirPage=yes")
[System.IO.File]::WriteAllText($manifest, $content, [System.Text.UTF8Encoding]::new($false))

& iscc $manifest
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}
