using System.Collections.Concurrent;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Timers;

/// <summary>
/// Сервис управления будильниками в памяти с поддержкой однократных и повторяющихся расписаний (TASK-050).
/// </summary>
public sealed class InMemoryAlarmService : IAlarmService, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, AlarmHolder> _alarms = new();
    private bool _disposed;

    /// <summary>
    /// Создаёт новый экземпляр InMemoryAlarmService.
    /// </summary>
    public InMemoryAlarmService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public event Action<AlarmRecord>? AlarmFired;

    /// <inheritdoc />
    public Task<AlarmRecord> SetAlarmAsync(
        TimeOnly timeOfDay,
        string title,
        IReadOnlyList<DayOfWeek>? daysOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Alarm title must not be empty.", nameof(title));
        }

        IReadOnlyList<DayOfWeek> days = daysOfWeek ?? [];
        DateTimeOffset now = _timeProvider.GetLocalNow();
        DateTimeOffset nextFireUtc = CalculateNextFireUtc(timeOfDay, days, now);

        var record = new AlarmRecord
        {
            AlarmId = Guid.NewGuid(),
            Title = title,
            TimeOfDay = timeOfDay,
            DaysOfWeek = days,
            IsEnabled = true,
            NextFireTimeUtc = nextFireUtc,
        };

        var holder = new AlarmHolder(record, this);
        holder.ScheduleNext();

        _alarms[record.AlarmId] = holder;
        return Task.FromResult(holder.CurrentRecord);
    }

    /// <inheritdoc />
    public Task<bool> CancelAlarmAsync(Guid alarmId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_alarms.TryGetValue(alarmId, out AlarmHolder? holder))
        {
            return Task.FromResult(holder.Cancel());
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AlarmRecord>> ListAlarmsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IReadOnlyList<AlarmRecord> alarms = _alarms.Values
            .Select(h => h.CurrentRecord)
            .OrderBy(a => a.NextFireTimeUtc ?? DateTimeOffset.MaxValue)
            .ToList();

        return Task.FromResult(alarms);
    }

    internal DateTimeOffset CalculateNextFireUtc(TimeOnly timeOfDay, IReadOnlyList<DayOfWeek> daysOfWeek, DateTimeOffset nowLocal)
    {
        if (daysOfWeek.Count == 0)
        {
            // Однократный будильник на ближайшее время
            DateTime todayDate = nowLocal.Date;
            DateTime targetDateTime = todayDate.Add(timeOfDay.ToTimeSpan());

            if (targetDateTime <= nowLocal.DateTime)
            {
                targetDateTime = targetDateTime.AddDays(1);
            }

            var localTarget = new DateTimeOffset(targetDateTime, nowLocal.Offset);
            return localTarget.ToUniversalTime();
        }

        // Повторяющийся по дням недели
        for (int dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            DateTime candidateDate = nowLocal.Date.AddDays(dayOffset);
            if (daysOfWeek.Contains(candidateDate.DayOfWeek))
            {
                DateTime candidateDateTime = candidateDate.Add(timeOfDay.ToTimeSpan());
                if (dayOffset > 0 || candidateDateTime > nowLocal.DateTime)
                {
                    var localCandidate = new DateTimeOffset(candidateDateTime, nowLocal.Offset);
                    return localCandidate.ToUniversalTime();
                }
            }
        }

        // Защитный fallback
        return nowLocal.AddDays(1).ToUniversalTime();
    }

    private void OnHolderFired(AlarmHolder holder)
    {
        if (_disposed)
        {
            return;
        }

        AlarmRecord firedRecord = holder.CurrentRecord;
        try
        {
            AlarmFired?.Invoke(firedRecord);
        }
        catch
        {
            // Не допускаем падения планировщика
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
        foreach (AlarmHolder holder in _alarms.Values)
        {
            holder.Dispose();
        }

        _alarms.Clear();
    }

    private sealed class AlarmHolder : IDisposable
    {
        private readonly object _lock = new();
        private readonly InMemoryAlarmService _service;
        private ITimer? _timer;
        private AlarmRecord _record;

        public AlarmHolder(AlarmRecord record, InMemoryAlarmService service)
        {
            _record = record;
            _service = service;
        }

        public AlarmRecord CurrentRecord
        {
            get
            {
                lock (_lock)
                {
                    return _record;
                }
            }
        }

        public void ScheduleNext()
        {
            lock (_lock)
            {
                if (!_record.IsEnabled || _record.NextFireTimeUtc is null)
                {
                    return;
                }

                DateTimeOffset nowUtc = _service._timeProvider.GetUtcNow();
                TimeSpan delay = _record.NextFireTimeUtc.Value - nowUtc;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                _timer?.Dispose();
                _timer = _service._timeProvider.CreateTimer(
                    _ => TriggerFire(),
                    null,
                    delay,
                    Timeout.InfiniteTimeSpan);
            }
        }

        public bool Cancel()
        {
            lock (_lock)
            {
                if (!_record.IsEnabled)
                {
                    return false;
                }

                _timer?.Dispose();
                _timer = null;
                _record = _record with
                {
                    IsEnabled = false,
                    NextFireTimeUtc = null,
                };
                return true;
            }
        }

        private void TriggerFire()
        {
            AlarmRecord snapshot;
            lock (_lock)
            {
                if (!_record.IsEnabled)
                {
                    return;
                }

                snapshot = _record;

                if (_record.DaysOfWeek.Count > 0)
                {
                    // Перепланируем на следующий день недели
                    DateTimeOffset now = _service._timeProvider.GetLocalNow();
                    DateTimeOffset nextFire = _service.CalculateNextFireUtc(_record.TimeOfDay, _record.DaysOfWeek, now);
                    _record = _record with { NextFireTimeUtc = nextFire };
                    ScheduleNext();
                }
                else
                {
                    // Однократный завершается
                    _record = _record with
                    {
                        IsEnabled = false,
                        NextFireTimeUtc = null,
                    };
                    _timer?.Dispose();
                    _timer = null;
                }
            }

            _service.OnHolderFired(this);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                _record = _record with
                {
                    IsEnabled = false,
                    NextFireTimeUtc = null,
                };
            }
        }
    }
}
