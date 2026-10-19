using Quartz;
using Quartz.Spi;

namespace CyberPipe.Chains.Scheduling;

/// <summary>
/// Фабрика создания экземпляров Quartz-заданий с внедрением зависимостей (TASK-045).
/// </summary>
public sealed class QuartzJobFactory : IJobFactory
{
    private readonly Func<Type, IJob> _resolver;

    /// <summary>Создаёт фабрику с делегатом разрешения типов заданий.</summary>
    public QuartzJobFactory(Func<Type, IJob> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <inheritdoc />
    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return _resolver(bundle.JobDetail.JobType);
    }

    /// <inheritdoc />
    public void ReturnJob(IJob job)
    {
        (job as IDisposable)?.Dispose();
    }
}
