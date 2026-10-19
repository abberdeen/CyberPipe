namespace CyberPipe.Contracts;

/// <summary>Снимок сквозного контекста операции для обогащения логов.</summary>
/// <param name="RunId">Идентификатор запуска цепочки.</param>
/// <param name="ChainId">Идентификатор цепочки; может отсутствовать вне цепочки.</param>
/// <param name="ChannelKind">Канал, из которого пришла операция; может отсутствовать.</param>
/// <param name="IntentTier">Ярус маршрутизации; может отсутствовать до маршрутизации.</param>
public sealed record OperationContextSnapshot(
    RunId RunId,
    ChainId? ChainId,
    ChannelKind? ChannelKind,
    IntentTier? IntentTier);

/// <summary>
/// Сквозной контекст операции на AsyncLocal: RunId генерируется один раз на запуск и
/// прокидывается через все await внутри области, чтобы события лога цепочки и каналов
/// собирались по одному идентификатору. Вложенные области восстанавливают внешнюю при завершении.
/// </summary>
public static class OperationContext
{
    private static readonly AsyncLocal<OperationContextSnapshot?> CurrentValue = new();

    /// <summary>Текущий контекст операции; null вне области.</summary>
    public static OperationContextSnapshot? Current => CurrentValue.Value;

    /// <summary>Генерирует новый уникальный идентификатор запуска.</summary>
    /// <returns>Новый RunId.</returns>
    public static RunId NewRunId() => RunId.New();

    /// <summary>Открывает область с новым сгенерированным RunId.</summary>
    /// <param name="chainId">Идентификатор цепочки; может отсутствовать.</param>
    /// <param name="channelKind">Канал операции; может отсутствовать.</param>
    /// <param name="intentTier">Ярус маршрутизации; может отсутствовать.</param>
    /// <returns>Объект, закрывающий область и восстанавливающий предыдущий контекст.</returns>
    public static IDisposable BeginScope(
        ChainId? chainId = null,
        ChannelKind? channelKind = null,
        IntentTier? intentTier = null) =>
        BeginScope(NewRunId(), chainId, channelKind, intentTier);

    /// <summary>Открывает область с заданным RunId, например из ChainRunContext запуска.</summary>
    /// <param name="runId">Идентификатор запуска.</param>
    /// <param name="chainId">Идентификатор цепочки; может отсутствовать.</param>
    /// <param name="channelKind">Канал операции; может отсутствовать.</param>
    /// <param name="intentTier">Ярус маршрутизации; может отсутствовать.</param>
    /// <returns>Объект, закрывающий область и восстанавливающий предыдущий контекст.</returns>
    public static IDisposable BeginScope(
        RunId runId,
        ChainId? chainId = null,
        ChannelKind? channelKind = null,
        IntentTier? intentTier = null)
    {
        OperationContextSnapshot? previous = CurrentValue.Value;
        CurrentValue.Value = new OperationContextSnapshot(runId, chainId, channelKind, intentTier);
        return new Scope(previous);
    }

    private sealed class Scope(OperationContextSnapshot? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CurrentValue.Value = previous;
        }
    }
}
