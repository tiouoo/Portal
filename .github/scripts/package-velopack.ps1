$ErrorActionPreference = 'Stop'

$project = Join-Path $env:GITHUB_WORKSPACE 'src/Portal.Desktop/Portal.Desktop.csproj'
$publishDir = Join-Path $env:GITHUB_WORKSPACE "publish/$($env:RID)"
$releaseDir = Join-Path $env:GITHUB_WORKSPACE 'Releases'
$icon = switch ($env:PACKAGE_KIND) {
    'windows' { Join-Path $env:GITHUB_WORKSPACE 'assets/Icon-Pattern-Shadow-Border.ico' }
    'linux' { Join-Path $env:GITHUB_WORKSPACE 'assets/Icon-Pattern-Shadow-Border.png' }
    'macos' { Join-Path $env:GITHUB_WORKSPACE 'assets/Icon-Pattern-Shadow-Border.icns' }
}
$mainExe = if ($env:PACKAGE_KIND -eq 'windows') { 'Portal.Desktop.exe' } else { 'Portal.Desktop' }

New-Item -ItemType Directory -Force -Path $publishDir, $releaseDir | Out-Null
$previousFull = Get-ChildItem -LiteralPath $releaseDir -Filter "$($env:PACK_ID)-*-full.nupkg" |
    ForEach-Object {
        $prefix = "$($env:PACK_ID)-"
        $suffix = "-$($env:VELOPACK_CHANNEL)-full.nupkg"
        if ($_.Name.StartsWith($prefix) -and $_.Name.EndsWith($suffix)) {
            $versionText = $_.Name.Substring($prefix.Length, $_.Name.Length - $prefix.Length - $suffix.Length)
            [PSCustomObject]@{ File = $_; Version = [System.Management.Automation.SemanticVersion]$versionText }
        }
    } |
    Sort-Object Version -Descending |
    Select-Object -First 1
Get-ChildItem -LiteralPath $releaseDir -File | Where-Object {
    $null -eq $previousFull -or $_.FullName -ne $previousFull.File.FullName
} | Remove-Item -Force

if ($env:PACKAGE_KIND -eq 'macos') {
    dotnet restore $project -r $env:RID
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet msbuild $project -t:BundleApp -p:RuntimeIdentifier=$env:RID -p:Configuration=Release `
        -p:SelfContained=true -p:Version=$env:APP_VERSION -p:CFBundleVersion=$env:BUNDLE_VERSION `
        -p:CFBundleShortVersionString=$env:PACKAGE_BASE_VERSION
    if ($LASTEXITCODE -ne 0) { throw 'macOS app bundle build failed.' }

    $appBundle = Join-Path $env:GITHUB_WORKSPACE "src/Portal.Desktop/bin/Release/net10.0/$($env:RID)/publish/Portal.app"
    $plist = Join-Path $appBundle 'Contents/Info.plist'
    Copy-Item -LiteralPath $icon -Destination (Join-Path $appBundle 'Contents/Resources/Icon-Pattern-Shadow-Border.icns') -Force
    & /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes array' $plist
    & /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0 dict' $plist
    & /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0:CFBundleURLName string xyz.tiouoo.portal' $plist
    & /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0:CFBundleURLRole string Viewer' $plist
    & /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0:CFBundleURLSchemes array' $plist
    & /usr/libexec/PlistBuddy -c 'Add :CFBundleURLTypes:0:CFBundleURLSchemes:0 string portal' $plist
    $publishDir = $appBundle
} else {
    dotnet publish $project -c Release -r $env:RID --self-contained true `
        /p:Version=$env:APP_VERSION /p:DebugType=none /p:DebugSymbols=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

$arguments = @(
    'pack', '--yes',
    '--packId', $env:PACK_ID,
    '--packVersion', $env:APP_VERSION,
    '--packDir', $publishDir,
    '--outputDir', $releaseDir,
    '--runtime', $env:RID,
    '--channel', $env:VELOPACK_CHANNEL,
    '--mainExe', $mainExe,
    '--packTitle', 'Portal',
    '--packAuthors', 'tiouoo',
    '--icon', $icon
)
if ($env:PACKAGE_KIND -eq 'macos') {
    $arguments += @('--bundleId', 'xyz.tiouoo.portal')
}

vpk @arguments
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }

switch ($env:PACKAGE_KIND) {
    'windows' {
        $setup = Get-ChildItem -LiteralPath $releaseDir -Filter '*-Setup.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $portable = Get-ChildItem -LiteralPath $releaseDir -Filter '*-Portable.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -eq $setup -or $null -eq $portable) { throw 'Velopack did not create the Windows setup and portable packages.' }
        Copy-Item -LiteralPath $portable.FullName -Destination (Join-Path $releaseDir 'Portal.win.x64.portable.zip') -Force
        Compress-Archive -LiteralPath $setup.FullName -DestinationPath (Join-Path $releaseDir 'Portal.win.x64.installer.zip') -Force
    }
    'linux' {
        $appImage = Get-ChildItem -LiteralPath $releaseDir -Filter '*.AppImage' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -eq $appImage) { throw 'Velopack did not create the Linux AppImage.' }
        Copy-Item -LiteralPath $appImage.FullName -Destination (Join-Path $releaseDir "Portal.linux.$($env:PUBLIC_ARCH).AppImage") -Force
    }
    'macos' {
        $portable = Get-ChildItem -LiteralPath $releaseDir -Filter '*-Portable.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -eq $portable) { throw 'Velopack did not create the macOS portable app package.' }

        $dmgRoot = Join-Path $env:RUNNER_TEMP "portal-dmg-$($env:PUBLIC_ARCH)"
        if (Test-Path -LiteralPath $dmgRoot) { Remove-Item -LiteralPath $dmgRoot -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $dmgRoot | Out-Null
        & /usr/bin/ditto -x -k $portable.FullName $dmgRoot
        if ($LASTEXITCODE -ne 0) { throw 'Unable to extract the macOS portable package.' }
        $app = Get-ChildItem -LiteralPath $dmgRoot -Filter '*.app' -Directory | Select-Object -First 1
        if ($null -eq $app) { throw 'The macOS portable package does not contain an app bundle.' }
        & /usr/bin/hdiutil create -volname Portal -srcfolder $dmgRoot -ov -format UDZO (Join-Path $releaseDir "Portal.osx.mac.$($env:PUBLIC_ARCH).dmg")
        if ($LASTEXITCODE -ne 0) { throw 'Unable to create the macOS DMG.' }
    }
}

$publicAssets = switch ($env:PACKAGE_KIND) {
    'windows' {
        'Portal.win.x64.installer.zip'
        'Portal.win.x64.portable.zip'
    }
    'linux' {
        "Portal.linux.$($env:PUBLIC_ARCH).AppImage"
    }
    'macos' {
        "Portal.osx.mac.$($env:PUBLIC_ARCH).dmg"
    }
}
$feedName = "releases.$($env:VELOPACK_CHANNEL).json"
$feedPath = Join-Path $releaseDir $feedName
$feed = Get-Content -LiteralPath $feedPath -Raw | ConvertFrom-Json
$feed.Assets = @($feed.Assets | Where-Object { $_.Version -eq $env:APP_VERSION })
$feed | ConvertTo-Json -Depth 10 -Compress | Set-Content -LiteralPath $feedPath -NoNewline

Get-ChildItem -LiteralPath $releaseDir -File | Where-Object {
    ($_.Extension -eq '.nupkg' -and $_.Name -notlike "$($env:PACK_ID)-$($env:APP_VERSION)-$($env:VELOPACK_CHANNEL)-*.nupkg") -or
    ($_.Extension -ne '.nupkg' -and $_.Name -ne $feedName -and $_.Name -notin $publicAssets)
} | Remove-Item -Force
