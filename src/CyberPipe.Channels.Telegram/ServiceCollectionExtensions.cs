using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CyberPipe.Channels.Telegram;

/// <summary>
/// Модуль регистрации Telegram-канала (TASK-090): адаптер Bot API, сервис канала
/// (он же порт доставки и отправитель HITL-карточек), верификатор для мастера.
/// Без токена в хранилище секретов канал регистрируется на отключённом адаптере:
/// граф собирается, отправка невозможна до настройки.
/// </summary>
public static class ChannelsServiceCollectionExtensions
{
    /// <summary>Регистрирует Telegram-канал.</summary>
    public static IServiceCollection AddCyberPipeChannels(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(new TelegramOptions());
        services.TryAddSingleton<ITelegramBotClientAdapter>(sp =>
        {
            ISecretStore store = sp.GetRequiredService<ISecretStore>();
            string? token = null;
            try
            {
                token = store
                    .GetAsync(options.Channels.TelegramBotTokenSecretKey)
                    .GetAwaiter().GetResult()?.Reveal();
            }
            catch (Exception)
            {
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return new FakeTelegramBotClientAdapter();
            }

            return new HttpTelegramBotClientAdapter(new HttpClient(), token);
        });

        services.TryAddSingleton<TelegramChannelService>(sp =>
            new TelegramChannelService(
                sp.GetRequiredService<ITelegramBotClientAdapter>(),
                sp.GetRequiredKeyedService<IOwnerGuard>("telegram"),
                sp.GetService<TelegramOptions>(),
                sp.GetService<IMessageBus>(),
                new DeferredHitlApprovalService(sp)));
        services.TryAddSingleton<IDeliveryPort>(sp => sp.GetRequiredService<TelegramChannelService>());
        services.TryAddSingleton<IHitlCardSender>(sp => sp.GetRequiredService<TelegramChannelService>());
        services.AddHostedService(sp => sp.GetRequiredService<TelegramChannelService>());
        services.TryAddSingleton<IChannelVerifier, SetupChannelVerifier>();

        return services;
    }
}
