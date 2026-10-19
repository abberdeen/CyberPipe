namespace CyberPipe.ServiceControl;

/// <summary>
/// Адаптер для взаимодействия с Windows Service Control Manager (SCM) (TASK-021).
/// Выделен в интерфейс для чистого тестирования сценариев без обращения к реальной ОС.
/// </summary>
public interface IScmManager
{
    /// <summary>Проверяет, установлена ли служба в операционной системе.</summary>
    bool IsServiceInstalled(string serviceName);

    /// <summary>Возвращает текущее состояние службы из SCM (Running, Stopped, StartPending, StopPending и т.д.).</summary>
    string GetServiceStatus(string serviceName);

    /// <summary>Запрашивает старт службы через SCM.</summary>
    Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>Запрашивает остановку службы через SCM.</summary>
    Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default);
}
