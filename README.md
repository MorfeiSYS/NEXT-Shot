# NEXT-Shot

WPF-приложение для Windows: скриншоты области/окна/экрана, разметка на оверлее, запись видео (FFmpeg).

## Требования

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — для сборки
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — для запуска опубликованной сборки
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — только для сборки инсталлятора

## Сборка

```powershell
dotnet build NEXTShot\NEXTShot.csproj -c Release
```

При первой сборке скачается `ffmpeg.exe` (BtbN FFmpeg-Builds) в `NEXTShot/ffmpeg/`.

## Инсталлятор

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\_tools\BuildInstaller.ps1
```

Результат: `installer/output/NEXTShot-Setup-<версия>.exe`.

## Структура

| Путь | Назначение |
|------|------------|
| `NEXTShot/` | Основное приложение |
| `_tools/` | FetchFfmpeg, BuildInstaller, утилиты разработки |
| `installer/` | Inno Setup (`NEXTShot.iss`) |

## Утилиты разработки

- `_tools/FetchFfmpeg.ps1` — загрузка FFmpeg
- `_tools/ExtractUi/Program.cs` — извлечение иконок из **вашей локальной** копии `Skrinshoter.exe` (путь передаётся аргументом; бинарник в репозиторий не входит)
- `_tools/ListRes/` — просмотр embedded-ресурсов сборки

История изменений — [CHANGELOG.md](CHANGELOG.md).

> **Примечание:** иконки в `Resources/Toolbar/` получены из ресурсов «Скриншотера» для совместимости UI. Перед публичной публикацией убедитесь, что у вас есть право их распространять, или замените на свои ассеты.
