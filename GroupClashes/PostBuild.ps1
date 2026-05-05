param ($Configuration, $TargetName, $ProjectDir, $TargetPath, $TargetDir)

Write-Host "Конфигурация: $Configuration"
Write-Host "Целевой файл: $TargetPath"
Write-Host "Папка вывода: $TargetDir"

function CopyToFolder {
    param (
        [string]$SourceDir,
        [string]$DestinationDir
    )

    try {
        if (-not (Test-Path $DestinationDir)) {
            New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
        } else {
            Get-ChildItem -Path $DestinationDir -Force | Remove-Item -Recurse -Force
        }

        Copy-Item -Path (Join-Path $SourceDir "*") -Destination $DestinationDir -Recurse -Force
        Write-Host "Скопировано в: $DestinationDir"
    }
    catch {
        Write-Host "Ошибка копирования в: $DestinationDir"
        Write-Host $_
        throw
    }
}

function RemoveFileIfExists {
    param (
        [string]$FilePath
    )

    if (Test-Path $FilePath) {
        Remove-Item $FilePath -Force
        Write-Host "Удален файл: $FilePath"
    }
}

$revitVersion = $Configuration.Replace("Debug", "").Replace("Release", "")

# Копирование в локальный каталог плагинов Navisworks (%AppData%) — основная цель после сборки.
$addinMainFolder = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\SmartGroupClashes.bundle"
New-Item -ItemType Directory -Path $addinMainFolder -Force | Out-Null
Copy-Item -Path (Join-Path $ProjectDir "PackageContents.xml") -Destination (Join-Path $addinMainFolder "PackageContents.xml") -Force

$addinFolder = Join-Path $addinMainFolder ("Contents\" + $revitVersion)
CopyToFolder -SourceDir $TargetDir -DestinationDir $addinFolder

# Дополнительно: упаковка релиза на внешний путь (если каталог существует на машине разработчика).
$releasePath = "G:\My Drive\05 - Travail\Revit Dev\GroupClashes\Releases\Current Release\SmartGroupClashes.bundle"
if (Test-Path $releasePath) {
    Copy-Item -Path (Join-Path $ProjectDir "PackageContents.xml") -Destination (Join-Path $releasePath "PackageContents.xml") -Force
    $releaseFolder = Join-Path $releasePath ("Contents\" + $revitVersion)
    CopyToFolder -SourceDir $TargetDir -DestinationDir $releaseFolder

    $bundleFolder = (Get-Item $releasePath).Parent.FullName
    $releaseZip = Join-Path $bundleFolder ($TargetName + ".zip")
    RemoveFileIfExists -FilePath $releaseZip

    # Удаляем старую DLL в корне сборки релиза (если осталась от предыдущей установки/упаковки).
    $oldPluginDll = Join-Path $bundleFolder "SmartGroupClashes.dll"
    RemoveFileIfExists -FilePath $oldPluginDll

    if (Get-Command 7z -ErrorAction SilentlyContinue) {
        7z a -tzip $releaseZip (Join-Path $bundleFolder "SmartGroupClashes.bundle\") | Out-Null
        Write-Host "Создан архив релиза: $releaseZip"
    }
    else {
        Write-Host "7z не найден. Пропуск создания архива."
    }
}
else {
    Write-Host "Внешний путь релиза не найден. Пропуск упаковки релиза."
}