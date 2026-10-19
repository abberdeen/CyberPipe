using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Timers;

/// <summary>
/// Сервис управления таймерами в памяти с поддержкой TimeProvider и событий срабатывания (TASK-050).
/// </summary>
public sealed class InMemoryTimerService : ITimerService, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly IMessageBus? _messageBus;
    private readonly ConcurrentDictionary<Guid, TimerHolder> _timers = new();
    private bool _disposed;

    /// <summary>
    /// Создаёт новый экземпляр InMemoryTimerService.
    /// </summary>
    public InMemoryTimerService(TimeProvider? timeProvider = null, IMessageBus? messageBus = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _messageBus = messageBus;
    }

    /// <inheritdoc />
    public event Action<TimerRecord>? TimerFired;

    /// <inheritdoc />
    public Task<TimerRecord> SetTimerAsync(TimeSpan duration, string title, string payloadJson = "{}", CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Timer duration must be positive.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Timer title must not be empty.", nameof(title));
        }

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        DateTimeOffset firesAtUtc = nowUtc.Add(duration);

        var record = new TimerRecord
        {
            TimerId = Guid.NewGuid(),
            Title = title,
            Duration = duration,
            CreatedAtUtc = nowUtc,
            FiresAtUtc = firesAtUtc,
            PayloadJson = payloadJson,
            IsActive = true,
            IsCancelled = false,
            IsFired = false,
        };

        var holder = new TimerHolder(record);
        ITimer timer = _timeProvider.CreateTimer(OnTimerFired, holder, duration, Timeout.InfiniteTimeSpan);
        holder.AttachTimer(timer);

        _timers[record.TimerId] = holder;
        return Task.FromResult(holder.CurrentRecord);
    }

    /// <inheritdoc />
    public Task<bool> CancelTimerAsync(Guid timerId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_timers.TryGetValue(timerId, out TimerHolder? holder))
        {
            return Task.FromResult(holder.TryCancel());
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TimerRecord>> ListActiveTimersAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IReadOnlyList<TimerRecord> active = _timers.Values
            .Select(h => h.CurrentRecord)
            .Where(r => r.IsActive)
            .OrderBy(r => r.FiresAtUtc)
            .ToList();

        return Task.FromResult(active);
    }

    /// <inheritdoc />
    public Task<TimerRecord?> GetTimerAsync(Guid timerId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_timers.TryGetValue(timerId, out TimerHolder? holder))
        {
            return Task.FromResult<TimerRecord?>(holder.CurrentRecord);
        }

        return Task.FromResult<TimerRecord?>(null);
    }

    private void OnTimerFired(object? state)
    {
        if (state is not TimerHolder holder)
        {
            return;
        }

        if (!holder.TryMarkFired(out TimerRecord? firedRecord) || firedRecord is null)
        {
            return;
        }

        try
        {
            TimerFired?.Invoke(firedRecord);
        }
        catch
        {
            // Не допускаем падения планировщика из-за ошибок подписчиков
        }

        if (_messageBus is not null)
        {
            try
            {
                var triggerEvent = new TriggerEvent
                {
                    EventId = Guid.NewGuid(),
                    ChainId = ChainId.New(),
                    Kind = ChainTriggerKind.FunctionCall,
                    OccurredAtUtc = _timeProvider.GetUtcNow(),
                    PayloadJson = firedRecord.PayloadJson,
                    IdempotencyKey = $"timer:{firedRecord.TimerId}:{firedRecord.FiresAtUtc.ToUnixTimeSeconds()}",
                    Source = $"timer:{firedRecord.TimerId}",
                };

                _ = _messageBus.PublishAsync(triggerEvent);
            }
            catch
            {
                // Игнорируем сбои шины при асинхронном событии таймера
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TimerHolder holder in _timers.Values)
        {
            holder.Dispose();
        }

        _timers.Clear();
    }

    private sealed class TimerHolder : IDisposable
    {
        private readonly object _lock = new();
        private ITimer? _timer;
        private TimerRecord _record;

        public TimerHolder(TimerRecord initialRecord)
        {
            _record = initialRecord;
        }

        public TimerRecord CurrentRecord
        {
            get
            {
                lock (_lock)
                {
                    return _record;
                }
            }
        }

        public void AttachTimer(ITimer timer)
        {
            lock (_lock)
            {
                _timer = timer;
            }
        }

        public bool TryCancel()
        {
            lock (_lock)
            {
                if (!_record.IsActive || _record.IsCancelled || _record.IsFired)
                {
                    return false;
                }

                _timer?.Dispose();
                _timer = null;
                _record = _record with
                {
                    IsActive = false,
                    IsCancelled = true,
                };

                return true;
            }
        }

        public bool TryMarkFired(out TimerRecord? firedRecord)
        {
            lock (_lock)
            {
                if (!_record.IsActive || _record.IsCancelled || _record.IsFired)
                {
                    firedRecord = null;
                    return false;
                }

                _timer?.Dispose();
                _timer = null;
                _record = _record with
                {
                    IsActive = false,
                    IsFired = true,
                };

                firedRecord = _record;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                if (_record.IsActive)
                {
                    _record = _record with
                    {
                        IsActive = false,
                        IsCancelled = true,
                    };
                }
            }
        }
    }
}
