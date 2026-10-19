namespace CyberPipe.Contracts;

/// <summary>
/// Типизированный идентификатор цепочки. Потребители: TASK-040, TASK-041, TASK-044.
/// </summary>
/// <param name="Value">Значение идентификатора.</param>
public readonly record struct ChainId(Guid Value)
{
    /// <summary>Пустой идентификатор.</summary>
    public static ChainId Empty => new(Guid.Empty);

    /// <summary>Создаёт новый уникальный идентификатор цепочки.</summary>
    /// <returns>Новый идентификатор.</returns>
    public static ChainId New() => new(Guid.NewGuid());

    /// <summary>Разбирает идентификатор из строки.</summary>
    /// <param name="value">Строковое представление.</param>
    /// <returns>Идентификатор цепочки.</returns>
    public static ChainId Parse(string value) => new(Guid.Parse(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Типизированный идентификатор запуска цепочки. Потребители: TASK-044, TASK-046.
/// </summary>
/// <param name="Value">Значение идентификатора.</param>
public readonly record struct RunId(Guid Value)
{
    /// <summary>Пустой идентификатор.</summary>
    public static RunId Empty => new(Guid.Empty);

    /// <summary>Создаёт новый уникальный идентификатор запуска.</summary>
    /// <returns>Новый идентификатор.</returns>
    public static RunId New() => new(Guid.NewGuid());

    /// <summary>Разбирает идентификатор из строки.</summary>
    /// <param name="value">Строковое представление.</param>
    /// <returns>Идентификатор запуска.</returns>
    public static RunId Parse(string value) => new(Guid.Parse(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Типизированный идентификатор сообщения канала или шины. Потребители: TASK-060..TASK-062, TASK-090.
/// </summary>
/// <param name="Value">Строковое значение идентификатора.</param>
public readonly record struct MessageId(string Value)
{
    /// <summary>Пустой идентификатор.</summary>
    public static MessageId Empty => new(string.Empty);

    /// <summary>Создаёт новый уникальный идентификатор сообщения.</summary>
    /// <returns>Новый идентификатор.</returns>
    public static MessageId New() => new(Guid.NewGuid().ToString());

    /// <inheritdoc />
    public override string ToString() => Value;
}
