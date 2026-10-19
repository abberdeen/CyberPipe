using CyberPipe.Contracts;

namespace CyberPipe.Chains.Execution;

/// <summary>
/// Композитный вызов навыков цепочки (TASK-090): опрашивает зарегистрированные
/// реестры навыков (host, personal, knowledge, voice) по порядку и возвращает
/// первый успех; все отказы агрегируются в ошибку.
/// </summary>
public sealed class CompositeSkillInvoker : ISkillInvoker
{
    private readonly IReadOnlyList<ISkillInvoker> _invokers;

    /// <summary>Создаёт композит поверх реестров навыков.</summary>
    public CompositeSkillInvoker(IEnumerable<ISkillInvoker> invokers)
    {
        ArgumentNullException.ThrowIfNull(invokers);
        _invokers = invokers.ToList();
        if (_invokers.Count == 0)
        {
            throw new ArgumentException("At least one skill invoker is required.", nameof(invokers));
        }
    }

    /// <inheritdoc />
    public async Task<SkillInvocationResult> InvokeSkillAsync(SkillInvocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        foreach (ISkillInvoker invoker in _invokers)
        {
            SkillInvocationResult result;
            try
            {
                result = await invoker.InvokeSkillAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{invoker.GetType().Name}: {ex.Message}");
                continue;
            }

            if (result.IsSuccess)
            {
                return result;
            }

            errors.Add($"{invoker.GetType().Name}: {result.Error?.Message ?? "skill failed"}");
        }

        return new SkillInvocationResult
        {
            IsSuccess = false,
            Error = new ErrorDto
            {
                Code = "Skill.NotFound",
                Message = $"No skill '{request.SkillId}' handled the invocation.",
                Details = string.Join("; ", errors),
            },
        };
    }
}

/// <summary>
/// Внутрипроцессный планировщик отложенного возобновления запусков (TASK-090):
/// таймеры в памяти, возобновление через <see cref="IChainExecutor.ResumeAsync" />.
/// Для персистентных расписаний Cron используется Quartz (TASK-045).
/// </summary>
public sealed class TimerDelayedResumeScheduler : IDelayedResumeScheduler, IDisposable
{
    private readonly Func<RunId, CancellationToken, Task> _resumeHandler;
    private readonly List<Timer> _timers = [];
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>Создаёт планировщик с обработчиком возобновления.</summary>
    public TimerDelayedResumeScheduler(Func<RunId, CancellationToken, Task> resumeHandler)
    {
        _resumeHandler = resumeHandler ?? throw new ArgumentNullException(nameof(resumeHandler));
    }

    /// <inheritdoc />
    public Task ScheduleResumeAsync(RunId runId, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Timer? timer = null;
            timer = new Timer(
                _ =>
                {
                    try
                    {
                        _resumeHandler(runId, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        lock (_sync)
                        {
                            _timers.Remove(timer!);
                        }

                        timer?.Dispose();
                    }
                },
                null,
                delay,
                Timeout.InfiniteTimeSpan);
            _timers.Add(timer);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (Timer timer in _timers)
            {
                timer.Dispose();
            }

            _timers.Clear();
        }
    }
}
