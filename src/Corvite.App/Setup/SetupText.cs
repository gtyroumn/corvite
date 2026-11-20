namespace Corvite.App.Setup;

/// <summary>Installer texts in the three program languages. Keys are short ids for XAML; Tr() translates the English progress messages of Installer.</summary>
public static class SetupText
{
    private static readonly Dictionary<string, string[]> T = new()
    {
        // XAML ids                   ru / uk / en
        ["loc"]      = new[] { "МЕСТО УСТАНОВКИ", "МІСЦЕ ВСТАНОВЛЕННЯ", "INSTALL LOCATION" },
        ["browse"]   = new[] { "Обзор…", "Огляд…", "Browse…" },
        ["existing"] = new[] { "Существующая установка обновится на месте; настройки и резервная копия сохранятся.", "Наявне встановлення оновиться на місці; налаштування та резервна копія збережуться.", "An existing installation is updated in place; your settings and backup are kept." },
        ["lang"]     = new[] { "ЯЗЫК ПРОГРАММЫ", "МОВА ПРОГРАМИ", "PROGRAM LANGUAGE" },
        ["desktop"]  = new[] { "Создать ярлык на рабочем столе", "Створити ярлик на робочому столі", "Create a desktop shortcut" },
        ["rest"]     = new[] { "Вернуть настройки Windows к исходным значениям", "Повернути налаштування Windows до початкових значень", "Restore Windows settings to the original values" },
        ["rest2"]    = new[] { "Используется резервная копия, сохранённая при первом запуске.", "Використовується резервна копія, збережена під час першого запуску.", "Uses the backup Corvite saved on first launch." },
        ["del"]      = new[] { "Также удалить мои настройки, журнал и резервную копию", "Також видалити мої налаштування, журнал і резервну копію", "Also delete my settings, log and backup" },
        ["del2"]     = new[] { "Не рекомендуется: именно резервная копия возвращает ваши настройки.", "Не рекомендується: саме резервна копія повертає ваші налаштування.", "Not recommended: the backup is what restores your settings." },
        ["cancel"]   = new[] { "Отмена", "Скасувати", "Cancel" },
        ["launch"]   = new[] { "Запустить Corvite", "Запустити Corvite", "Launch Corvite" },
        ["finish"]   = new[] { "Готово", "Готово", "Finish" },
        ["wrong"]    = new[] { "Что-то пошло не так", "Щось пішло не так", "Something went wrong" },
        ["details"]  = new[] { "Подробности в %ProgramData%\\Corvite\\Logs\\corvite.log", "Подробиці в %ProgramData%\\Corvite\\Logs\\corvite.log", "Details are in %ProgramData%\\Corvite\\Logs\\corvite.log" },
        ["close"]    = new[] { "Закрыть", "Закрити", "Close" },
        ["t.inst"]   = new[] { "Установка Corvite", "Встановлення Corvite", "Install Corvite" },
        ["t.uninst"] = new[] { "Удаление Corvite", "Видалення Corvite", "Uninstall Corvite" },
        ["s.inst"]   = new[] { "Версия {0}. Несколько секунд, и можно играть.", "Версія {0}. Кілька секунд, і можна грати.", "Version {0}. A few seconds and you are ready to play." },
        ["s.uninst"] = new[] { "Corvite будет удалён с этого компьютера.", "Corvite буде видалено з цього комп'ютера.", "Corvite will be removed from this computer." },
        ["b.inst"]   = new[] { "Установить", "Встановити", "Install" },
        ["unb"]      = new[] { "Удалить установленный Corvite", "Видалити встановлений Corvite", "Uninstall the installed Corvite" },
        ["b.uninst"] = new[] { "Удалить", "Видалити", "Uninstall" },
        ["d.inst"]   = new[] { "Corvite установлен", "Corvite встановлено", "Corvite is installed" },
        ["d.uninst"] = new[] { "Corvite удалён", "Corvite видалено", "Corvite was removed" },
        ["h.inst"]   = new[] { "Всё готово. Нажмите «Готово», чтобы запустить.", "Усе готове. Натисніть «Готово», щоб запустити.", "Everything is in place. Press Finish to start." },
        ["h.uninst"] = new[] { "Ваши настройки и резервная копия сохранены, если вы не выбрали их удаление.", "Ваші налаштування та резервна копія збережені, якщо ви не вибрали їх видалення.", "Your settings and backup are kept unless you chose to delete them." },
        ["dlg"]      = new[] { "Выберите папку для установки Corvite", "Оберіть папку для встановлення Corvite", "Choose where to install Corvite" },
    };

    // English progress messages from Installer -> ru / uk
    private static readonly Dictionary<string, string[]> P = new()
    {
        ["Preparing…"] = new[] { "Подготовка…", "Підготовка…" },
        ["Copying files…"] = new[] { "Копирование файлов…", "Копіювання файлів…" },
        ["Creating shortcuts…"] = new[] { "Создание ярлыков…", "Створення ярликів…" },
        ["Registering the app…"] = new[] { "Регистрация приложения…", "Реєстрація застосунку…" },
        ["Done"] = new[] { "Готово", "Готово" },
        ["Restoring Windows settings…"] = new[] { "Возврат настроек Windows…", "Повернення налаштувань Windows…" },
        ["Removing shortcuts…"] = new[] { "Удаление ярлыков…", "Видалення ярликів…" },
        ["Unregistering the app…"] = new[] { "Отмена регистрации приложения…", "Скасування реєстрації застосунку…" },
        ["Deleting settings and backups…"] = new[] { "Удаление настроек и резервных копий…", "Видалення налаштувань і резервних копій…" },
        ["Done (some settings could not be restored)"] = new[] { "Готово (часть настроек вернуть не удалось)", "Готово (частину налаштувань повернути не вдалося)" },
    };

    private static int Idx(string lang) => lang == "ru" ? 0 : lang == "uk" ? 1 : 2;

    public static string Get(string key, string lang) => T.TryGetValue(key, out var v) ? v[Idx(lang)] : key;

    public static string Tr(string english, string lang)
    {
        var i = Idx(lang);
        return i < 2 && P.TryGetValue(english, out var v) ? v[i] : english;
    }
}
