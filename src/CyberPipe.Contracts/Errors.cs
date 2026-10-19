namespace CyberPipe.Contracts;

/// <summary>
/// Транспортно-нейтральное описание ошибки для межпроцессных вызовов.
/// Потребители: Service, Desktop и CLI (TASK-020, TASK-021, TASK-061, TASK-062), TASK-090.
/// </summary>
public sealed record ErrorDto
{
    /// <summary>Машинный код ошибки, например <c>ChainDefinition.MissingStep</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Человекочитаемое сообщение об ошибке без секретов.</summary>
    public required string Message { get; init; }

    /// <summary>Дополнительные детали ошибки, безопасные для показа владельцу; может отсутствовать.</summary>
    public string? Details { get; init; }
}

/// <summary>
/// Результат межпроцессного вызова: значение при успехе или <see cref="ErrorDto" /> при отказе.
/// Потребители: Service, Desktop и CLI (TASK-021, TASK-061, TASK-062), TASK-090.
/// </summary>
/// <typeparam name="T">Тип значения при успехе.</typeparam>
public sealed record Result<T>
{
    /// <summary>Признак успешного результата.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Значение при успехе; отсутствует при отказе.</summary>
    public T? Value { get; init; }

    /// <summary>Ошибка при отказе; отсутствует при успехе.</summary>
    public ErrorDto? Error { get; init; }

    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="value">Значение результата.</param>
    /// <returns>Успешный результат.</returns>
    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };

    /// <summary>Создаёт результат-отказ.</summary>
    /// <param name="error">Описание ошибки.</param>
    /// <returns>Результат-отказ.</returns>
    public static Result<T> Failure(ErrorDto error) => new() { IsSuccess = false, Error = error };

    /// <summary>Создаёт результат-отказ по коду и сообщению.</summary>
    /// <param name="code">Машинный код ошибки.</param>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <returns>Результат-отказ.</returns>
    public static Result<T> Failure(string code, string message) =>
        Failure(new ErrorDto { Code = code, Message = message });
}

/// <summary>
/// Результат операции без возвращаемого значения. Потребители: TASK-021, TASK-090.
/// </summary>
public sealed record Result
{
    /// <summary>Признак успешного результата.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Ошибка при отказе; отсутствует при успехе.</summary>
    public ErrorDto? Error { get; init; }

    /// <summary>Создаёт успешный результат.</summary>
    /// <returns>Успешный результат.</returns>
    public static Result Success() => new() { IsSuccess = true };

    /// <summary>Создаёт результат-отказ.</summary>
    /// <param name="error">Описание ошибки.</param>
    /// <returns>Результат-отказ.</returns>
    public static Result Failure(ErrorDto error) => new() { IsSuccess = false, Error = error };

    /// <summary>Создаёт результат-отказ по коду и сообщению.</summary>
    /// <param name="code">Машинный код ошибки.</param>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <returns>Результат-отказ.</returns>
    public static Result Failure(string code, string message) =>
        Failure(new ErrorDto { Code = code, Message = message });
}
