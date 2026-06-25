using System.Collections.Generic;
using System.Globalization;

namespace BlackLaunch;

internal static class i18n
{
    private static string _currentLanguage = "en";
    private const string FallbackLanguage = "en";

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        {
            "en", new Dictionary<string, string> {
                { "LaunchSettings", "Launch Settings" },
                { "NicknamePlaceholder", "Your nickname" },
                { "LoadingVersions", "Loading versions list..." },
                { "Play", "Play" },
                { "Stop", "Exit" },
                { "OpenFolderTooltip", "Open instance folder" },
                { "ErrorFetchingVersions", "Error fetching versions: {0}" },
                { "ErrorOpeningFolder", "Error opening folder: {0}" },
                { "ErrorEmptyNicknameOrVersion", "Enter nickname and select game version" },
                { "ErrorLaunch", "Launch error: {0}" },
                { "ErrorLiteLoader", "LiteLoader does not support version {0}!" },
                { "StatusDownloadingFiles", "Downloading files.." },
                { "StatusDownloading", "Downloading: {0}" },
                { "StatusTimeLeft", "Time left: {0}" },
                { "StatusPreparingLoader", "Preparing {0}.." },
                { "StatusCheckingAssets", "Checking libraries and assets.." },
                { "StatusBuildingFiles", "Building game files.." },
                { "StatusReady", "Ready to launch" },
                { "StatusGameRunning", "Game is running" }
            }
        }, {
            "ru", new Dictionary<string, string> {
                { "LaunchSettings", "Настройки запуска" },
                { "NicknamePlaceholder", "Ваш никнейм" },
                { "LoadingVersions", "Загрузка списка версий" },
                { "Play", "Играть" },
                { "Stop", "Выйти" },
                { "OpenFolderTooltip", "Открыть папку сборки" },
                { "ErrorFetchingVersions", "Ошибка получения версий: {0}" },
                { "ErrorOpeningFolder", "Ошибка открытия папки: {0}" },
                { "ErrorEmptyNicknameOrVersion", "Введите ник и выберите версию игры" },
                { "ErrorLaunch", "Ошибка запуска: {0}" },
                { "ErrorLiteLoader", "LiteLoader не поддерживает версию {0}!" },
                { "StatusDownloadingFiles", "Скачивание файлов.." },
                { "StatusDownloading", "Загрузка: {0}" },
                { "StatusTimeLeft", "Осталось: {0}" },
                { "StatusPreparingLoader", "Подготовка {0}.." },
                { "StatusCheckingAssets", "Проверка библиотек и ассетов.." },
                { "StatusBuildingFiles", "Сборка файлов игры.." },
                { "StatusReady", "Готово к запуску" },
                { "StatusGameRunning", "Игра запущена" }
            }
        }
    };

    static i18n()
    {
        string systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
        SetLanguage(systemLanguage);
    }

    public static void SetLanguage(string langCode)
    {
        if (Translations.ContainsKey(langCode)) {
            _currentLanguage = langCode;
        } else _currentLanguage = FallbackLanguage;
    }

    public static string Get(string key, params object[] args) {
        if (!Translations[_currentLanguage].TryGetValue(key, out string? value)) {
            if (!Translations[FallbackLanguage].TryGetValue(key, out value)) value = $"[{key}]";
        }
        if (args.Length > 0) try { return string.Format(value, args); } catch { return value; }
        return value;
    }
}
