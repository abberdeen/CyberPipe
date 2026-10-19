using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CyberPipe.Desktop.Localization;

/// <summary>
/// Менеджер локализации (RU/EN) для динамического переключения языка оболочки в runtime.
/// Все строковые ресурсы привязываются через индексер Strings["Key"] или свойства.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string> RuStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        // General / Header
        ["App.Title"] = "CyberPipe — Персональный ассистент",
        ["Header.Status"] = "Служба:",
        ["Header.Budget"] = "Бюджет сегодня:",
        ["Header.Language"] = "Язык",
        ["Header.Theme"] = "Тема",
        ["Header.Theme.Light"] = "Светлая",
        ["Header.Theme.Dark"] = "Тёмная",
        ["Header.SetupWizard"] = "Мастер первого запуска",
        ["Header.SetupRequired"] = "Требуется первоначальная настройка системы!",

        // Navigation Tabs
        ["Tab.Chat"] = "Чат",
        ["Tab.Chains"] = "Цепочки",
        ["Tab.Hitl"] = "HITL-подтверждения",
        ["Tab.Service"] = "Служба",
        ["Tab.Schedules"] = "Расписания",
        ["Tab.Models"] = "Модели",

        // Chat
        ["Chat.InputPlaceholder"] = "Введите сообщение ассистенту...",
        ["Chat.Send"] = "Отправить",
        ["Chat.Abort"] = "Прервать",
        ["Chat.Attach"] = "Прикрепить файл",
        ["Chat.Attachment"] = "Вложение:",
        ["Chat.ClearAttachment"] = "Очистить",
        ["Chat.Budget.Limits"] = "Дневной лимит:",
        ["Chat.Budget.Spent"] = "Потрачено:",
        ["Chat.Budget.Remaining"] = "Остаток:",
        ["Chat.Budget.Tokens"] = "Токенов:",
        ["Chat.Tier.Prefix"] = "Ярус:",
        ["Chat.Processing"] = "Обработка запроса...",
        ["Chat.TaskAborted"] = "Задача прервана пользователем.",

        // Chains
        ["Chains.Title"] = "Определения и запуски цепочек",
        ["Chains.ReadOnlyNotice"] = "Определения цепочек загружаются из конфигурации (только чтение). Визуальный редактор отключён.",
        ["Chains.List"] = "Список цепочек",
        ["Chains.SelectPrompt"] = "Выберите цепочку для просмотра деталей и запусков",
        ["Chains.Details"] = "Параметры цепочки",
        ["Chains.Trigger"] = "Триггер:",
        ["Chains.Skill"] = "Навык:",
        ["Chains.Script"] = "Скрипт:",
        ["Chains.Delivery"] = "Доставка:",
        ["Chains.RequiresHitl"] = "HITL обязателен:",
        ["Chains.ManualInvoke"] = "Ручной запуск (FunctionCall)",
        ["Chains.PayloadJson"] = "Входная полезная нагрузка (JSON):",
        ["Chains.TriggerNow"] = "Запустить цепочку",
        ["Chains.Runs"] = "История запусков и чекпоинты",
        ["Chains.Checkpoints"] = "Чекпоинты шагов:",

        // HITL
        ["Hitl.Title"] = "Очередь подтверждений (Human-In-The-Loop)",
        ["Hitl.PendingQueue"] = "Ожидающие подтверждения",
        ["Hitl.NoPending"] = "Нет ожидающих подтверждений",
        ["Hitl.Details"] = "Детали запроса подтверждения",
        ["Hitl.ActionName"] = "Действие:",
        ["Hitl.Summary"] = "Описание:",
        ["Hitl.ExpiresAt"] = "Истекает:",
        ["Hitl.PayloadJson"] = "Полезная нагрузка:",
        ["Hitl.Approve"] = "Одобрить",
        ["Hitl.Reject"] = "Отклонить",
        ["Hitl.Edit"] = "Изменить и одобрить",
        ["Hitl.ReasonPlaceholder"] = "Причина отклонения (опционально)...",

        // Service
        ["Service.Title"] = "Управление службой CyberPipe",
        ["Service.Status"] = "Текущий статус:",
        ["Service.Version"] = "Версия службы:",
        ["Service.DesktopVersion"] = "Версия Desktop:",
        ["Service.StartedAt"] = "Время старта:",
        ["Service.LastError"] = "Последняя ошибка:",
        ["Service.Start"] = "Запустить службу",
        ["Service.Stop"] = "Остановить службу",
        ["Service.Restart"] = "Перезапустить службу",
        ["Service.Refresh"] = "Обновить статус",
        ["Service.Running"] = "Служба работает штатно.",
        ["Service.Stopped"] = "Служба остановлена. Нажмите «Запустить».",
        ["Service.Faulted"] = "Служба завершилась с ошибкой.",
        ["Service.Unavailable"] = "Служба недоступна (канал связи отключён).",
        ["Service.NotInstalled"] = "Служба не установлена в системе. Запустите scripts/install-service.ps1 от имени Администратора.",

        // Schedules
        ["Schedules.Title"] = "Расписания цепочек (Quartz.NET)",
        ["Schedules.List"] = "Активные cron-расписания",
        ["Schedules.ChainId"] = "Идентификатор цепочки:",
        ["Schedules.Cron"] = "Cron-выражение:",
        ["Schedules.NextFire"] = "Следующий запуск:",
        ["Schedules.IsPaused"] = "Приостановлено:",
        ["Schedules.Pause"] = "Приостановить",
        ["Schedules.Resume"] = "Возобновить",
        ["Schedules.Unschedule"] = "Удалить расписание",
        ["Schedules.TriggerNow"] = "Запустить сейчас",
        ["Schedules.AddUpdate"] = "Регистрация cron-расписания",
        ["Schedules.Save"] = "Сохранить расписание",
        ["Schedules.NoDesignerNotice"] = "Редактирование расписания осуществляется только текстовым cron-выражением с валидацией контракта.",

        // Models
        ["Models.Title"] = "Каталоги моделей и назначение ярусов",
        ["Models.TierAssignments"] = "Текущие назначения моделей по ярусам",
        ["Models.Catalog"] = "Каталог моделей (локальные и облачные)",
        ["Models.InstallerNotice"] = "Установка локальных моделей через Ollama и советник по совместимости будут доступны в TASK-081 и TASK-082.",
        ["Models.LaunchInstaller"] = "Установка локальных моделей (TASK-081)",
    };

    private static readonly Dictionary<string, string> EnStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        // General / Header
        ["App.Title"] = "CyberPipe — Personal Assistant",
        ["Header.Status"] = "Service:",
        ["Header.Budget"] = "Budget today:",
        ["Header.Language"] = "Language",
        ["Header.Theme"] = "Theme",
        ["Header.Theme.Light"] = "Light",
        ["Header.Theme.Dark"] = "Dark",
        ["Header.SetupWizard"] = "Setup Wizard",
        ["Header.SetupRequired"] = "Initial system setup required!",

        // Navigation Tabs
        ["Tab.Chat"] = "Chat",
        ["Tab.Chains"] = "Chains",
        ["Tab.Hitl"] = "HITL Approvals",
        ["Tab.Service"] = "Service",
        ["Tab.Schedules"] = "Schedules",
        ["Tab.Models"] = "Models",

        // Chat
        ["Chat.InputPlaceholder"] = "Type a message to the assistant...",
        ["Chat.Send"] = "Send",
        ["Chat.Abort"] = "Abort",
        ["Chat.Attach"] = "Attach file",
        ["Chat.Attachment"] = "Attachment:",
        ["Chat.ClearAttachment"] = "Clear",
        ["Chat.Budget.Limits"] = "Daily limit:",
        ["Chat.Budget.Spent"] = "Spent:",
        ["Chat.Budget.Remaining"] = "Remaining:",
        ["Chat.Budget.Tokens"] = "Tokens:",
        ["Chat.Tier.Prefix"] = "Tier:",
        ["Chat.Processing"] = "Processing request...",
        ["Chat.TaskAborted"] = "Task was aborted by user.",

        // Chains
        ["Chains.Title"] = "Chain Definitions & Runs",
        ["Chains.ReadOnlyNotice"] = "Chain definitions are loaded from configuration (read-only). Visual designer is disabled.",
        ["Chains.List"] = "Chains List",
        ["Chains.SelectPrompt"] = "Select a chain to inspect details and runs",
        ["Chains.Details"] = "Chain Parameters",
        ["Chains.Trigger"] = "Trigger:",
        ["Chains.Skill"] = "Skill:",
        ["Chains.Script"] = "Script:",
        ["Chains.Delivery"] = "Delivery:",
        ["Chains.RequiresHitl"] = "HITL required:",
        ["Chains.ManualInvoke"] = "Manual Trigger (FunctionCall)",
        ["Chains.PayloadJson"] = "Input Payload (JSON):",
        ["Chains.TriggerNow"] = "Trigger Chain",
        ["Chains.Runs"] = "Run History & Checkpoints",
        ["Chains.Checkpoints"] = "Step Checkpoints:",

        // HITL
        ["Hitl.Title"] = "Human-In-The-Loop Approvals Queue",
        ["Hitl.PendingQueue"] = "Pending Approvals",
        ["Hitl.NoPending"] = "No pending approvals",
        ["Hitl.Details"] = "Approval Request Details",
        ["Hitl.ActionName"] = "Action:",
        ["Hitl.Summary"] = "Summary:",
        ["Hitl.ExpiresAt"] = "Expires:",
        ["Hitl.PayloadJson"] = "Payload:",
        ["Hitl.Approve"] = "Approve",
        ["Hitl.Reject"] = "Reject",
        ["Hitl.Edit"] = "Edit & Approve",
        ["Hitl.ReasonPlaceholder"] = "Rejection reason (optional)...",

        // Service
        ["Service.Title"] = "CyberPipe Service Management",
        ["Service.Status"] = "Current Status:",
        ["Service.Version"] = "Service Version:",
        ["Service.DesktopVersion"] = "Desktop Version:",
        ["Service.StartedAt"] = "Started At:",
        ["Service.LastError"] = "Last Error:",
        ["Service.Start"] = "Start Service",
        ["Service.Stop"] = "Stop Service",
        ["Service.Restart"] = "Restart Service",
        ["Service.Refresh"] = "Refresh Status",
        ["Service.Running"] = "Service is running normally.",
        ["Service.Stopped"] = "Service is stopped. Click Start to run.",
        ["Service.Faulted"] = "Service encountered a fault.",
        ["Service.Unavailable"] = "Service is unavailable (communication channel disconnected).",
        ["Service.NotInstalled"] = "Service is not installed on this system. Run scripts/install-service.ps1 as Administrator.",

        // Schedules
        ["Schedules.Title"] = "Chain Schedules (Quartz.NET)",
        ["Schedules.List"] = "Active Cron Schedules",
        ["Schedules.ChainId"] = "Chain ID:",
        ["Schedules.Cron"] = "Cron Expression:",
        ["Schedules.NextFire"] = "Next Fire:",
        ["Schedules.IsPaused"] = "Paused:",
        ["Schedules.Pause"] = "Pause",
        ["Schedules.Resume"] = "Resume",
        ["Schedules.Unschedule"] = "Unschedule",
        ["Schedules.TriggerNow"] = "Trigger Now",
        ["Schedules.AddUpdate"] = "Register Cron Schedule",
        ["Schedules.Save"] = "Save Schedule",
        ["Schedules.NoDesignerNotice"] = "Schedule editing is supported via raw text cron expression with contract validation.",

        // Models
        ["Models.Title"] = "Model Catalogs & Tier Assignments",
        ["Models.TierAssignments"] = "Active Tier Model Assignments",
        ["Models.Catalog"] = "Model Catalog (Local & Cloud)",
        ["Models.InstallerNotice"] = "Local model installation via Ollama and hardware advisor will be available in TASK-081 and TASK-082.",
        ["Models.LaunchInstaller"] = "Install Local Models (TASK-081)",
    };

    private string _currentCulture = "ru";

    public static LocalizationManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value.ToLowerInvariant() == "en" ? "en" : "ru";
                OnPropertyChanged();
                OnPropertyChanged("Item[]");
            }
        }
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        Dictionary<string, string> dict = _currentCulture == "en" ? EnStrings : RuStrings;
        if (dict.TryGetValue(key, out string? value))
        {
            return value;
        }

        // Fallback to Ru or key itself
        if (RuStrings.TryGetValue(key, out string? ruValue))
        {
            return ruValue;
        }

        return key;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
