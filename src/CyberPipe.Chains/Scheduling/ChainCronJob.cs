using System.Text.Json;
using CyberPipe.Contracts;
using Quartz;

namespace CyberPipe.Chains.Scheduling;

/// <summary>
/// Quartz-задание для выполнения cron-срабатывания цепочки (TASK-045).
/// Не исполняет саму цепочку, а вызывает порт триггера (ICronTriggerRegistrar / CronTriggerManager).
/// </summary>
[DisallowConcurrentExecution]
public sealed class ChainCronJob : IJob
{
    private readonly Func<ChainId, DateTimeOffset, Task>? _fireCronHandler;
    private readonly IDeliveryPort? _deliveryPort;

    /// <summary>Параметризованный конструктор для JobFactory.</summary>
    public ChainCronJob(Func<ChainId, DateTimeOffset, Task>? fireCronHandler = null, IDeliveryPort? deliveryPort = null)
    {
        _fireCronHandler = fireCronHandler;
        _deliveryPort = deliveryPort;
    }

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        JobDataMap data = context.MergedJobDataMap;
        string? chainIdString = data.GetString("ChainId");
        if (string.IsNullOrWhiteSpace(chainIdString))
        {
            throw new InvalidOperationException("ChainCronJob: ChainId is missing from JobDataMap.");
        }

        var chainId = new ChainId(Guid.Parse(chainIdString));

        string? policyString = data.GetString("MisfirePolicy");
        ChainMisfirePolicy policy = Enum.TryParse<ChainMisfirePolicy>(policyString, out var parsedPolicy)
            ? parsedPolicy
            : ChainMisfirePolicy.FireOnceNow;

        int lateMinutesThreshold = data.ContainsKey("LateMinutesThreshold")
            ? data.GetInt("LateMinutesThreshold")
            : 5;
        if (lateMinutesThreshold <= 0)
        {
            lateMinutesThreshold = 5;
        }

        DateTimeOffset scheduledFireTimeUtc = context.ScheduledFireTimeUtc?.UtcDateTime ?? DateTimeOffset.UtcNow;
        DateTimeOffset actualFireTimeUtc = context.FireTimeUtc.UtcDateTime;
        TimeSpan latency = actualFireTimeUtc - scheduledFireTimeUtc;

        IDeliveryPort? deliveryPort = _deliveryPort ?? context.Scheduler.Context.Get("DeliveryPort") as IDeliveryPort;

        // Обработка политики misfire при опоздании сверх порога
        if (latency > TimeSpan.FromMinutes(lateMinutesThreshold))
        {
            if (policy is ChainMisfirePolicy.SkipIfLate or ChainMisfirePolicy.DoNothing)
            {
                // Пропуск запуска по политике
                return;
            }

            if (policy == ChainMisfirePolicy.AskUser)
            {
                // Отправка запроса владельцу через порт доставки без автоматического запуска
                if (deliveryPort is not null)
                {
                    await deliveryPort.SendAsync(new DeliveryRequest
                    {
                        Channel = ChannelKind.Telegram,
                        Target = "owner",
                        Text = $"Cron-запуск цепочки {chainId} опоздал на {latency.TotalMinutes:F1} мин. Требуется решение владельца.",
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            chainId = chainId.ToString(),
                            scheduledAtUtc = scheduledFireTimeUtc.ToString("o"),
                            latencyMinutes = latency.TotalMinutes,
                            reason = "MisfireAskUser",
                        }),
                    }, context.CancellationToken).ConfigureAwait(false);
                }

                return;
            }
        }

        // Вызов обработчика срабатывания триггера
        Func<ChainId, DateTimeOffset, Task>? handler = _fireCronHandler
            ?? context.Scheduler.Context.Get("FireCronHandler") as Func<ChainId, DateTimeOffset, Task>;

        if (handler is null)
        {
            throw new InvalidOperationException("ChainCronJob: No fireCronHandler configured in Job or SchedulerContext.");
        }

        await handler(chainId, scheduledFireTimeUtc).ConfigureAwait(false);
    }
}
