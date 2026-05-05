param(
    [string]$PluginProjectDir = (Join-Path $PSScriptRoot "..\GroupClashes"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "artifacts"),
    [string]$Version = "",
    [string]$Manufacturer = "Kanukhin_A",
    [string]$ProductName = "SmartGroupClashes",
    [string]$UpgradeCode = "B9C47E21-6F83-4D1A-A7E2-5C8F0E1D2A3B"
)

$ErrorActionPreference = "Stop"

function Get-SafeId([string]$value) {
    $id = ($value -replace "[^A-Za-z0-9_]", "_")
    if ([string]::IsNullOrWhiteSpace($id)) { $id = "ROOT" }
    if ($id[0] -match "[0-9]") { $id = "_" + $id }
    return $id
}

function Get-NormalizedMsiProductVersion([string]$raw) {
    $parts = ($raw.Trim() -split '\.') + @("0", "0", "0", "0")
    $nums = @(0, 0, 0, 0)
    for ($i = 0; $i -lt 4; $i++) {
        $n = 0
        [void][int]::TryParse($parts[$i], [ref]$n)
        if ($n -gt 255) { $n = 255 }
        if ($n -lt 0) { $n = 0 }
        $nums[$i] = $n
    }
    return "$($nums[0]).$($nums[1]).$($nums[2]).$($nums[3])"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$pluginDir = Resolve-Path $PluginProjectDir
$binDir = Join-Path $pluginDir "bin"
$packageContentsPath = Join-Path $pluginDir "PackageContents.xml"

if (-not (Test-Path $packageContentsPath)) {
    throw "Не найден файл PackageContents.xml. Ожидаемый путь: $packageContentsPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $raw = Get-Content -LiteralPath $packageContentsPath -Raw -Encoding UTF8
    $m = [regex]::Match($raw, 'AppVersion\s*=\s*"([^"]+)"')
    if ($m.Success) {
        $Version = $m.Groups[1].Value.Trim()
    }
    else {
        $Version = "1.0.0.0"
    }
}

$Version = Get-NormalizedMsiProductVersion $Version

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
Write-Host "Каталог вывода MSI: $OutputDir"
Write-Host "Версия пакета (WiX / имя файла, нормализовано под MSI): $Version"

$releaseDirs = Get-ChildItem -Path $binDir -Directory |
    Where-Object { $_.Name -match "^\d{4}Release$" -and (Test-Path (Join-Path $_.FullName "SmartGroupClashes.dll")) } |
    Sort-Object Name

if ($releaseDirs.Count -eq 0) {
    throw "В каталоге «$binDir» нет ни одной папки вида «2024Release», «2026Release» и т.п. с файлом SmartGroupClashes.dll. Сначала соберите решение в Visual Studio в конфигурации Release для нужных версий Navisworks."
}

$stagingRoot = Join-Path $PSScriptRoot "staging"
$bundleName = "SmartGroupClashes.bundle"
$bundleRoot = Join-Path $stagingRoot $bundleName
$bundleContentsRoot = Join-Path $bundleRoot "Contents"
$wxsPath = Join-Path $PSScriptRoot "Product.generated.wxs"

if (Test-Path $stagingRoot) { Remove-Item $stagingRoot -Recurse -Force }
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $bundleContentsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Copy-Item $packageContentsPath (Join-Path $bundleRoot "PackageContents.xml") -Force

foreach ($releaseDir in $releaseDirs) {
    $year = $releaseDir.Name.Substring(0, 4)
    $dest = Join-Path $bundleContentsRoot $year
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item (Join-Path $releaseDir.FullName "*") $dest -Recurse -Force
}

# Убрать из поставки устаревшие сборки с прежним именем (иначе в MSI попадут лишние DLL).
Get-ChildItem -Path $bundleContentsRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -ieq ".dll" -and $_.Name -like "GroupClashes*" } |
    Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $bundleContentsRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "GroupClashesIcon*.ico" } |
    Remove-Item -Force -ErrorAction SilentlyContinue

foreach ($releaseDir in $releaseDirs) {
    $year = $releaseDir.Name.Substring(0, 4)
    $dllPath = Join-Path (Join-Path $bundleContentsRoot $year) "SmartGroupClashes.dll"
    if (-not (Test-Path -LiteralPath $dllPath)) {
        throw "В составе bundle для года $year нет SmartGroupClashes.dll по пути: $dllPath. Соберите проект в конфигурации ${year}Release."
    }
    $dllLen = (Get-Item -LiteralPath $dllPath).Length
    if ($dllLen -lt 1024) {
        throw "Файл SmartGroupClashes.dll слишком мал ($dllLen байт) для года $year — похоже на ошибку сборки."
    }
}

# Keep installer smaller; symbols are not required at runtime.
Get-ChildItem -Path $bundleRoot -Filter "*.pdb" -Recurse | Remove-Item -Force

$allFiles = Get-ChildItem -Path $bundleRoot -File -Recurse
if ($allFiles.Count -eq 0) {
    throw "В промежуточной папке установщика нет ни одного файла: $bundleRoot. Проверьте, что в bin\«год»Release лежат скомпилированные файлы плагина."
}

$dllInCab = @($allFiles | Where-Object { $_.Extension -ieq ".dll" })
Write-Host "В MSI войдёт файлов: $($allFiles.Count), из них сборок .dll: $($dllInCab.Count)"
foreach ($d in $dllInCab | Sort-Object FullName) {
    Write-Host "  DLL: $($d.FullName) ($($d.Length) байт)"
}

$iconForArp = Get-ChildItem -Path $bundleRoot -Recurse -File -Filter "SmartGroupClashesIcon_Small.ico" -ErrorAction SilentlyContinue |
    Select-Object -First 1

$relativeDirSet = New-Object System.Collections.Generic.HashSet[string]
$null = $relativeDirSet.Add("")
foreach ($file in $allFiles) {
    $relativeDir = [System.IO.Path]::GetDirectoryName($file.FullName.Substring($bundleRoot.Length).TrimStart('\'))
    if ($null -eq $relativeDir) { $relativeDir = "" }
    $relativeDir = $relativeDir -replace "/", "\"
    $segments = if ($relativeDir -eq "") { @() } else { $relativeDir.Split('\') }
    $current = ""
    foreach ($segment in $segments) {
        if ($current -eq "") { $current = $segment } else { $current = "$current\$segment" }
        $null = $relativeDirSet.Add($current)
    }
}

$relativeDirs = @($relativeDirSet) | Sort-Object { $_.Split('\').Count }, { $_ }

$dirIdMap = @{}
$dirIdMap[""] = "INSTALLFOLDER"
foreach ($relativeDir in $relativeDirs) {
    if ($relativeDir -eq "") { continue }
    $dirId = "DIR_" + (Get-SafeId $relativeDir)
    while ($dirIdMap.Values -contains $dirId) {
        $dirId = $dirId + "_X"
    }
    $dirIdMap[$relativeDir] = $dirId
}

$directoryChildren = @{}
foreach ($relativeDir in $relativeDirs) {
    $directoryChildren[$relativeDir] = New-Object System.Collections.Generic.List[string]
}
foreach ($relativeDir in $relativeDirs) {
    if ($relativeDir -eq "") { continue }
    $parent = [System.IO.Path]::GetDirectoryName($relativeDir)
    if ($null -eq $parent) { $parent = "" }
    $parent = $parent -replace "/", "\"
    $directoryChildren[$parent].Add($relativeDir)
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine("  <Package Name=`"$ProductName`" Manufacturer=`"$Manufacturer`" Version=`"$Version`" UpgradeCode=`"$UpgradeCode`" Language=`"1049`" Scope=`"perUser`" InstallerVersion=`"500`" Compressed=`"yes`">")
[void]$sb.AppendLine("    <SummaryInformation Description=`"Установка плагина «$ProductName» для Autodesk Navisworks Manage`" />")
if ($null -ne $iconForArp) {
    $iconEscaped = $iconForArp.FullName.Replace("&", "&amp;")
    [void]$sb.AppendLine("    <Icon Id=`"ARPICO`" SourceFile=`"$iconEscaped`" />")
    [void]$sb.AppendLine("    <Property Id=`"ARPPRODUCTICON`" Value=`"ARPICO`" />")
}
[void]$sb.AppendLine("    <MajorUpgrade Schedule=`"afterInstallValidate`" DowngradeErrorMessage=`"Уже установлена более новая версия «[ProductName]». Удалите её в списке установленных приложений и повторите установку.`" />")
[void]$sb.AppendLine("    <MediaTemplate EmbedCab=`"yes`" CompressionLevel=`"high`" />")
[void]$sb.AppendLine("    <StandardDirectory Id=`"AppDataFolder`">")
[void]$sb.AppendLine("      <Directory Id=`"DIR_AUTODESK`" Name=`"Autodesk`">")
[void]$sb.AppendLine("        <Directory Id=`"DIR_APPLICATIONPLUGINS`" Name=`"ApplicationPlugins`">")
[void]$sb.AppendLine("          <Directory Id=`"DIR_LEGACY_GROUPCLASHES_BUNDLE`" Name=`"GroupClashes.bundle`">")
[void]$sb.AppendLine("          </Directory>")
[void]$sb.AppendLine("          <Directory Id=`"INSTALLFOLDER`" Name=`"$bundleName`">")

function Append-Directories([string]$parentRelative, [int]$indentLevel) {
    $indent = (" " * $indentLevel)
    foreach ($childRelative in ($directoryChildren[$parentRelative] | Sort-Object)) {
        $childId = $dirIdMap[$childRelative]
        $childName = Split-Path $childRelative -Leaf
        [void]$sb.AppendLine("$indent<Directory Id=`"$childId`" Name=`"$childName`">")
        Append-Directories -parentRelative $childRelative -indentLevel ($indentLevel + 2)
        [void]$sb.AppendLine("$indent</Directory>")
    }
}

Append-Directories -parentRelative "" -indentLevel 12

[void]$sb.AppendLine("          </Directory>")
[void]$sb.AppendLine("        </Directory>")
[void]$sb.AppendLine("      </Directory>")
[void]$sb.AppendLine("    </StandardDirectory>")

[void]$sb.AppendLine("    <Feature Id=`"MainFeature`" Title=`"Файлы плагина «$ProductName»`" Level=`"1`">")
[void]$sb.AppendLine("      <ComponentGroupRef Id=`"CG_MainFiles`" />")
[void]$sb.AppendLine("      <ComponentRef Id=`"CMP_RemoveLegacyGroupClashes`" />")
[void]$sb.AppendLine("    </Feature>")

[void]$sb.AppendLine("    <Component Id=`"CMP_RemoveLegacyGroupClashes`" Directory=`"DIR_LEGACY_GROUPCLASHES_BUNDLE`" Guid=`"*`">")
[void]$sb.AppendLine("      <RemoveFile Id=`"RMF_LegacyGroupClashesBundleFiles`" Name=`"*.*`" On=`"install`" />")
[void]$sb.AppendLine("      <RemoveFolder Id=`"RMF_LegacyGroupClashesBundleFolder`" On=`"install`" />")
[void]$sb.AppendLine("      <RegistryValue Root=`"HKCU`" Key=`"Software\\Kanukhin_A\\SmartGroupClashes`" Name=`"LegacyCleanup`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />")
[void]$sb.AppendLine("    </Component>")

[void]$sb.AppendLine("    <ComponentGroup Id=`"CG_MainFiles`">")
$componentIndex = 1
foreach ($file in $allFiles | Sort-Object FullName) {
    $relativePath = $file.FullName.Substring($bundleRoot.Length).TrimStart('\')
    $relativeDir = [System.IO.Path]::GetDirectoryName($relativePath)
    if ($null -eq $relativeDir) { $relativeDir = "" }
    $relativeDir = $relativeDir -replace "/", "\"
    $directoryId = $dirIdMap[$relativeDir]
    $componentId = "CMP_" + $componentIndex
    $fileId = "FIL_" + $componentIndex
    $sourceEscaped = $file.FullName.Replace("&", "&amp;")
    [void]$sb.AppendLine("      <Component Id=`"$componentId`" Directory=`"$directoryId`" Guid=`"*`">")
    [void]$sb.AppendLine("        <File Id=`"$fileId`" Source=`"$sourceEscaped`" KeyPath=`"yes`" />")
    [void]$sb.AppendLine("      </Component>")
    $componentIndex++
}
[void]$sb.AppendLine("    </ComponentGroup>")
[void]$sb.AppendLine("  </Package>")
[void]$sb.AppendLine("</Wix>")

[System.IO.File]::WriteAllText($wxsPath, $sb.ToString(), [System.Text.Encoding]::UTF8)

Push-Location $repoRoot
try {
    if (-not (Test-Path (Join-Path $repoRoot ".config\dotnet-tools.json"))) {
        dotnet new tool-manifest | Out-Null
    }

    $toolList = dotnet tool list --local
    if ($toolList -notmatch "wix") {
        dotnet tool install wix --version 4.* | Out-Null
    }

    $msiPath = Join-Path $OutputDir "SmartGroupClashes-$Version.msi"
    Write-Host "Запуск WiX: dotnet tool run wix build ..."
    & dotnet tool run wix build $wxsPath -o $msiPath
    if ($LASTEXITCODE -ne 0) {
        throw "Сборка MSI завершилась с кодом $LASTEXITCODE. См. вывод WiX выше."
    }
    if (-not (Test-Path -LiteralPath $msiPath)) {
        throw "После сборки MSI не найден по пути: $msiPath"
    }
    Write-Host ""
    Write-Host "Сборка MSI завершена успешно."
    Write-Host "  Файл установки: $msiPath"
    Get-Item -LiteralPath $msiPath | ForEach-Object {
        Write-Host "  Размер: $($_.Length) байт"
        Write-Host "  Время записи: $($_.LastWriteTime)"
    }
    Write-Host "  В пакет вошли конфигурации: $($releaseDirs.Name -join ', ')"
    Write-Host ""
}
finally {
    Pop-Location
}
