# SmartGroupClashes

Плагин для **Autodesk Navisworks Manage**, который ускоряет разбор коллизий: группирует результаты Clash Detective по выбранным правилам и позволяет работать сразу с несколькими тестами.

Проект основан на [GroupClashes](https://github.com/simonmoreau/GroupClashes) и развивается как отдельная ветка с собственным именованием, сборкой и установкой.

## Возможности

- Группировка коллизий по разным режимам (оси, уровень, статус, имя элемента, имя семейства, имя типа, пользовательское).
- Мультивыбор тестов с пакетной группировкой и разгруппировкой.
- Русскоязычный интерфейс панели и локализованные подписи режимов.
- Поддержка нескольких версий Navisworks в одном MSI-пакете.
- Установка в `%AppData%\Autodesk\ApplicationPlugins\SmartGroupClashes.bundle`.

## Требования

- Windows 10/11
- Autodesk Navisworks Manage (поддерживаемые годы определяются собранными конфигурациями)
- Visual Studio 2022 (для сборки исходников)
- .NET SDK (для `dotnet` и сборки MSI)
- PowerShell 5.1+

## Инструкция для пользователей

Раздел для тех, кто устанавливает и использует готовый MSI, без сборки из исходников. В настоящий момент 

### Установка

1. Закройте Autodesk Navisworks Manage.
2. Запустите файл `SmartGroupClashes-<версия>.msi`.
3. Завершите установку в мастере Windows.
4. Откройте Navisworks и дождитесь загрузки плагинов.

Плагин устанавливается в профиль текущего пользователя:

- `%AppData%\Autodesk\ApplicationPlugins\SmartGroupClashes.bundle`

### Обновление

- Установите новый MSI поверх старого.
- Предыдущая версия плагина удаляется автоматически.
- Дополнительно очищается старая папка `%AppData%\Autodesk\ApplicationPlugins\GroupClashes.bundle`, если она осталась от старых сборок.

### Удаление

1. Откройте `Параметры Windows -> Приложения`.
2. Найдите `SmartGroupClashes`.
3. Нажмите `Удалить`.

### Если плагин не появился в Navisworks

- Проверьте, что установлен `Navisworks Manage` (не Simulate/Freedom).
- Перезапустите Navisworks после установки.
- Убедитесь, что в `%AppData%\Autodesk\ApplicationPlugins\` есть папка `SmartGroupClashes.bundle`.

## Быстрый старт

1. Откройте `GroupClashes.sln` в Visual Studio.
2. Выберите нужную конфигурацию, например `2026Release`.
3. Соберите проект `SmartGroupClashes`.
4. Запустите Navisworks и проверьте работу панели плагина.

После сборки скрипт `GroupClashes/PostBuild.ps1` копирует файлы плагина в каталог профиля пользователя.

## Сборка плагина

Сборки находятся в `GroupClashes/bin/<Год>Release`. Для включения версии Navisworks в MSI должна существовать папка формата `####Release` с файлом `SmartGroupClashes.dll`.

Пример сборки из консоли:

```powershell
dotnet msbuild "GroupClashes.sln" /t:Build /p:Configuration=2024Release /p:Platform="Any CPU"
dotnet msbuild "GroupClashes.sln" /t:Build /p:Configuration=2026Release /p:Platform="Any CPU"
```

## Сборка MSI

Используйте `Build-Msi.bat` в корне репозитория:

```bat
Build-Msi.bat
```

Сценарий:

- собирает staging-каталог в `Installer/staging/SmartGroupClashes.bundle`;
- включает в MSI все найденные `####Release`-сборки;
- удаляет из staging лишние артефакты старого имени;
- генерирует `Installer/Product.generated.wxs`;
- формирует MSI в `Installer/artifacts`.

По умолчанию версия берётся из `AppVersion` в `GroupClashes/PackageContents.xml` и приводится к формату `a.b.c.d`.

Пример с параметрами:

```bat
Build-Msi.bat -Version "1.2.0" -OutputDir "C:\Builds\MSI"
```

### Параметры сборки MSI

- `-PluginProjectDir` — путь к проекту плагина с `PackageContents.xml`.
- `-OutputDir` — каталог для итогового MSI.
- `-Version` — версия продукта и имя файла MSI.
- `-Manufacturer` — производитель в свойствах установщика.
- `-ProductName` — имя продукта в мастере установки.
- `-UpgradeCode` — постоянный GUID семейства обновлений.

## Обновление и удаление старых версий

MSI использует стандартный механизм major upgrade и удаляет ранее установленную версию пакета.

Дополнительно при установке выполняется очистка legacy-папки:

- `%AppData%\Autodesk\ApplicationPlugins\GroupClashes.bundle`

Это предотвращает ситуацию, когда в профиле пользователя остаются одновременно старая и новая версии плагина.

## Структура репозитория

- `GroupClashes/` — исходный код плагина, ресурсы, `PackageContents.xml`.
- `Installer/` — сценарии и файлы для сборки MSI.
- `Build-Msi.bat` — точка входа для сборки установщика.

## Источник и лицензия

Базовый проект: [simonmoreau/GroupClashes](https://github.com/simonmoreau/GroupClashes).

Лицензионные условия и авторские права определяются лицензией исходного проекта (MIT).
