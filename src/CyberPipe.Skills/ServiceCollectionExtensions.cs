using CyberPipe.Contracts;
using CyberPipe.Skills.Host.Media;
using CyberPipe.Skills.Host.Registry;
using CyberPipe.Skills.Host.System;
using CyberPipe.Skills.Host.Timers;
using CyberPipe.Skills.Knowledge.Registry;
using CyberPipe.Skills.Knowledge.Services;
using CyberPipe.Skills.Personal.Data;
using CyberPipe.Skills.Personal.Registry;
using CyberPipe.Skills.Voice.Converters;
using CyberPipe.Skills.Voice.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CyberPipe.Skills;

/// <summary>
/// Модуль регистрации навыков: host, personal, knowledge, voice (TASK-090).
/// Платформенные контроллеры host-навыков — только на Windows; на прочих ОС
/// реестр host-навыков пуст (без подмен тестовыми двойниками).
/// Голосовой движок отсутствует в поставке: реестр voice пуст до настройки движка.
/// </summary>
public static class SkillsServiceCollectionExtensions
{
    /// <summary>Регистрирует реестры и сервисы навыков.</summary>
    public static IServiceCollection AddCyberPipeSkills(this IServiceCollection services, CyberPipeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (OperatingSystem.IsWindows())
        {
            services.TryAddSingleton<IHostMediaController, WindowsMediaController>();
            services.TryAddSingleton<ISystemStatusProvider, WindowsSystemStatusProvider>();
        }

        services.TryAddSingleton<ITimerService, InMemoryTimerService>();
        services.TryAddSingleton<IAlarmService, InMemoryAlarmService>();
        services.TryAddSingleton<HostSkillRegistry>(sp => OperatingSystem.IsWindows()
            ? new HostSkillRegistry(
                sp.GetRequiredService<IHostMediaController>(),
                sp.GetRequiredService<ISystemStatusProvider>(),
                sp.GetRequiredService<ITimerService>(),
                sp.GetRequiredService<IAlarmService>())
            : new HostSkillRegistry());

        services.TryAddSingleton<INoteRepository, InMemoryNoteRepository>();
        services.TryAddSingleton<IFinanceRepository, InMemoryFinanceRepository>();
        services.TryAddSingleton<IReminderRepository, InMemoryReminderRepository>();
        services.TryAddSingleton<IContactRepository, InMemoryContactRepository>();
        services.TryAddSingleton<PersonalSkillRegistry>();

        services.TryAddSingleton<IKnowledgeService, KnowledgeService>();
        services.TryAddSingleton<IKnowledgeContextProvider, KnowledgeContextProvider>();
        services.TryAddSingleton<KnowledgeSkillRegistry>();
        services.TryAddSingleton<IHostSkillRegistry>(sp => sp.GetRequiredService<HostSkillRegistry>());
        services.TryAddSingleton<IPersonalSkillRegistry>(sp => sp.GetRequiredService<PersonalSkillRegistry>());
        services.TryAddSingleton<IKnowledgeSkillRegistry>(sp => sp.GetRequiredService<KnowledgeSkillRegistry>());
        services.TryAddSingleton<IVoiceSkillRegistry>(sp => sp.GetRequiredService<VoiceSkillRegistry>());

        services.TryAddSingleton<IAudioConverter, FfmpegAudioConverter>();
        services.TryAddSingleton<VoiceSkillRegistry>(sp => new VoiceSkillRegistry());

        return services;
    }
}
