# VentCalc v2.0

VentCalc v2.0 — новый C# Revit Add-in для профессиональных расчётов вентиляции. Проект создан с нуля: старый pyRevit-код не переносится и не копируется.

## Состав решения

- `VentCalc.Revit` — Revit add-in, точка входа `IExternalApplication`, команды и Ribbon.
- `VentCalc.Core` — расчётное ядро без зависимости от Autodesk Revit API.
- `VentCalc.UI` — WPF-интерфейс будущих окон плагина.
- `VentCalc.Reports` — будущая генерация Excel-файлов и отчётов.
- `VentCalc.Tests` — тесты расчётных формул и ядра.

## Целевая платформа

- Autodesk Revit 2025.
- .NET Framework 4.8.
- Visual Studio 2022.

## Что реализовано на первом этапе

После загрузки add-in в Revit создаётся вкладка **VentCalc** с двумя панелями:

### Панель «Вентиляция»

- **Расчёт**
- **Инспектор**
- **МС**
- **Трасса**
- **Скорости**
- **Настройки**

### Панель «Сервис»

- **Очистить**
- **О программе**

Каждая команда пока открывает `TaskDialog` с названием команды. Расчётная логика на этом этапе не реализована.

## Запуск в Revit 2025

1. Откройте `VentCalc.sln` в Visual Studio 2022.
2. Убедитесь, что установлен Autodesk Revit 2025 и доступны ссылки:
   - `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll`
   - `C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll`
3. Соберите решение в конфигурации `Debug` или `Release`.
4. Создайте папку для add-in, например:
   - `%APPDATA%\Autodesk\Revit\Addins\2025\VentCalc`
5. Скопируйте сборки из `VentCalc.Revit\bin\<Configuration>\net48\` в эту папку.
6. Скопируйте шаблон `deploy\VentCalc.addin` в:
   - `%APPDATA%\Autodesk\Revit\Addins\2025\VentCalc.addin`
7. Если путь к `VentCalc.Revit.dll` отличается, отредактируйте элемент `<Assembly>` в `VentCalc.addin`.
8. Запустите Revit 2025 и проверьте вкладку **VentCalc** на Ribbon.

## Примечания для разработки

- В репозиторий не добавляются бинарные файлы, папки `bin` и `obj` исключены через `.gitignore`.
- Revit-зависимый код должен оставаться в `VentCalc.Revit`.
- Расчётная логика должна добавляться в `VentCalc.Core` без ссылок на Revit API.
- WPF-окна должны добавляться в `VentCalc.UI`.
- Экспорт Excel и отчёты должны добавляться в `VentCalc.Reports`.
