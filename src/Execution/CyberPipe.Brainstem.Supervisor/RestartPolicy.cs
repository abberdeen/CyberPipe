namespace CyberPipe.Brainstem.Supervisor;

/// <summary>
/// Политика перезапуска воркера Brainstem с экспоненциальным бэкоффом и сбросом счётчика после стабильной работы.
/// </summary>
public sealed class RestartPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly TimeSpan _stableThreshold;
    private readonly TimeProvider _timeProvider;

    private int _consecutiveFailures;
    private long _lastSuccessTimestamp;

    public RestartPolicy(
        int maxAttempts,
        TimeSpan initialBackoff,
        TimeSpan maxBackoff,
        TimeSpan stableThreshold,
        TimeProvider? timeProvider = null)
    {
        _maxAttempts = Math.Max(0, maxAttempts);
        _initialBackoff = initialBackoff < TimeSpan.Zero ? TimeSpan.Zero : initialBackoff;
        _maxBackoff = maxBackoff < _initialBackoff ? _initialBackoff : maxBackoff;
        _stableThreshold = stableThreshold < TimeSpan.Zero ? TimeSpan.Zero : stableThreshold;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastSuccessTimestamp = _timeProvider.GetTimestamp();
    }

    public int ConsecutiveFailures => _consecutiveFailures;
    public int MaxAttempts => _maxAttempts;

    public void OnWorkerStarted()
    {
        _lastSuccessTimestamp = _timeProvider.GetTimestamp();
    }

    public void CheckStabilityReset()
    {
        if (_consecutiveFailures > 0)
        {
            TimeSpan elapsed = _timeProvider.GetElapsedTime(_lastSuccessTimestamp);
            if (elapsed >= _stableThreshold)
            {
                _consecutiveFailures = 0;
            }
        }
    }

    public void Reset()
    {
        _consecutiveFailures = 0;
        _lastSuccessTimestamp = _timeProvider.GetTimestamp();
    }

    public (bool CanRestart, TimeSpan BackoffDelay) RecordFailure()
    {
        CheckStabilityReset();

        _consecutiveFailures++;
        if (_consecutiveFailures > _maxAttempts)
        {
            return (false, TimeSpan.Zero);
        }

        // Exponential backoff: initial * 2^(failures - 1)
        double multiplier = Math.Pow(2, _consecutiveFailures - 1);
        double delayMs = _initialBackoff.TotalMilliseconds * multiplier;
        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(delayMs, _maxBackoff.TotalMilliseconds));

        return (true, delay);
    }
}
