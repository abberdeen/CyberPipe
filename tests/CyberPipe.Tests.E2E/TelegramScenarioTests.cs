namespace CyberPipe.Tests.E2E;

using CyberPipe.Channels.Telegram.Adapter;
using CyberPipe.Channels.Telegram.Model;
using CyberPipe.Contracts;
using Xunit;

/// <summary>
/// Сквозной Telegram-сценарий на реальной композиции TASK-090 (TASK-091):
/// приём → Owner → Tier0–Tier3 → бюджет → HITL → навык → ответ в тот же чат.
/// Внешние границы — фейки (эмбеддинг, LLM, исполнитель цепочек, Telegram-адаптер);
/// роутеры, губернатор, HITL, канал и PostgreSQL — настоящие.
/// </summary>
public sealed class TelegramScenarioTests
{
    private const string OwnerId = "111";
    private const long OwnerNumericId = 111;
    private const string StrangerId = "999";
    private const long StrangerNumericId = 999;

    [E2EFact]
    public async Task HitlApprove_RunsSkillOnceAndRepliesToSameChat()
    {
        await using var host = await E2EHost.CreateAsync(OwnerId, 777101);

        await host.FeedTextAsync(OwnerNumericId, "громкость на 100");

        Guid approvalId = await WaitForApprovalAsync(host);
        Assert.Contains(
            host.Adapter.SentMessages,
            m => m.Text != null && m.Text.Contains("Запрос подтверждения", StringComparison.Ordinal));
        Assert.Equal(0, host.VolumeSkill.Invocations);

        await host.FeedCallbackAsync("cb-approve-1", OwnerNumericId, $"hitl:approve:{approvalId}")
            ;
        HitlApprovalResult decision =
            await host.Assistant.WaitDecisionAsync(approvalId, TimeSpan.FromSeconds(10));

        Assert.Equal(HitlDecision.Approved, decision.Decision);
        Assert.Equal(1, host.VolumeSkill.Invocations);
        Assert.Equal("100", host.VolumeSkill.LastLevel);
        Assert.Equal(1, host.ExecutorStub.ResumeCalls);

        SentTelegramMessageRecord reply = host.Adapter.SentMessages[^1];
        Assert.Equal(host.ChatId, reply.ChatId);
        Assert.Contains("100", reply.Text ?? string.Empty, StringComparison.Ordinal);

        HitlApprovalRecord? record = await host.HitlRepository.GetAsync(approvalId);
        Assert.NotNull(record);
        Assert.Equal(HitlStatus.Resolved, record.Status);
        Assert.NotNull(record.Resolution);
        Assert.Equal(HitlDecision.Approved, record.Resolution.Decision);

        BudgetStatusDto budget = await host.Governor.GetStatusAsync();
        Assert.True(budget.TokensUsedToday > 0);
    }

    [E2EFact]
    public async Task FastPath_Tier1MatchCacheRepeatAndTier2Reply()
    {
        await using var host = await E2EHost.CreateAsync(OwnerId, 777102);

        await host.FeedTextAsync(OwnerNumericId, "что ты умеешь");
        Assert.Equal(1, host.HelpSkill.Invocations);
        Assert.Equal(0, host.Llm.TotalCalls);
        SentTelegramMessageRecord first = host.Adapter.SentMessages[^1];
        Assert.Equal(host.ChatId, first.ChatId);
        Assert.Contains("Available", first.Text ?? string.Empty, StringComparison.Ordinal);

        await Task.Delay(1200);
        await host.FeedTextAsync(OwnerNumericId, "что ты умеешь");
        Assert.Equal(2, host.HelpSkill.Invocations);
        Assert.Equal(0, host.Llm.TotalCalls);
        SentTelegramMessageRecord second = host.Adapter.SentMessages[^1];
        Assert.Equal(first.Text, second.Text);

        CacheStatsDto stats = await host.SemanticCache.GetStatsAsync();
        Assert.True(stats.Hits >= 1);

        await host.FeedTextAsync(OwnerNumericId, "какая сегодня погода на марсе");
        Assert.Equal(1, host.Llm.GetCallCount(ChatModelClass.Light));
        Assert.Equal(3, host.HelpSkill.Invocations);
        SentTelegramMessageRecord third = host.Adapter.SentMessages[^1];
        Assert.Contains("Available", third.Text ?? string.Empty, StringComparison.Ordinal);

        host.Adapter.EnqueueTextMessage(host.ChatId, OwnerNumericId, "покажи команды");
        TelegramUpdate duplicate = Assert.Single(await host.Adapter.GetUpdatesAsync());
        int repliesBefore = host.Adapter.SentMessages.Count;
        int helpBefore = host.HelpSkill.Invocations;
        await host.FeedUpdatesAsync(duplicate, duplicate);
        Assert.Equal(helpBefore + 1, host.HelpSkill.Invocations);
        Assert.Equal(repliesBefore + 1, host.Adapter.SentMessages.Count);
    }

    [E2EFact]
    public async Task NonOwnerMessage_RejectedBeforeRouter()
    {
        await using var host = await E2EHost.CreateAsync(OwnerId, 777103);

        await host.FeedTextAsync(StrangerNumericId, "громкость на 100");

        Assert.Equal(0, host.VolumeSkill.Invocations);
        Assert.Equal(0, host.Assistant.RouterCalls);
        Assert.DoesNotContain(
            host.Adapter.SentMessages,
            m => m.Text != null && m.Text.Contains("Volume set", StringComparison.Ordinal));
    }

    [E2EFact]
    public async Task HitlReject_SkillNotRunAndOwnerNotified()
    {
        await using var host = await E2EHost.CreateAsync(OwnerId, 777104);

        await host.FeedTextAsync(OwnerNumericId, "громкость на 50");
        Guid approvalId = await WaitForApprovalAsync(host);

        await host.FeedCallbackAsync("cb-reject-1", OwnerNumericId, $"hitl:reject:{approvalId}")
            ;
        HitlApprovalResult decision =
            await host.Assistant.WaitDecisionAsync(approvalId, TimeSpan.FromSeconds(10));

        Assert.Equal(HitlDecision.Rejected, decision.Decision);
        Assert.Equal(0, host.VolumeSkill.Invocations);
        Assert.Equal(1, host.ExecutorStub.CancelCalls);

        SentTelegramMessageRecord reply = host.Adapter.SentMessages[^1];
        Assert.Equal(host.ChatId, reply.ChatId);
        Assert.Contains("Отклонено", reply.Text ?? string.Empty, StringComparison.Ordinal);

        HitlApprovalRecord? record = await host.HitlRepository.GetAsync(approvalId);
        Assert.NotNull(record);
        Assert.Equal(HitlStatus.Resolved, record.Status);
        Assert.NotNull(record.Resolution);
        Assert.Equal(HitlDecision.Rejected, record.Resolution.Decision);
    }

    [E2EFact]
    public async Task BudgetExhausted_RefusesWithLimitsAndSkipsSkillAndLlm()
    {
        await using var host = await E2EHost.CreateAsync(
            OwnerId, 777105, dailyCapUsd: 0.000001m, skillHosting: ModelHostingType.RemoteCloud)
            ;

        await host.FeedTextAsync(OwnerNumericId, "громкость на 100");

        Assert.Equal(0, host.VolumeSkill.Invocations);
        Assert.Equal(0, host.Llm.TotalCalls);
        Assert.DoesNotContain(
            host.Adapter.SentMessages,
            m => m.Text != null && m.Text.Contains("Запрос подтверждения", StringComparison.Ordinal));
        Assert.NotEmpty(host.Adapter.SentMessages);
        SentTelegramMessageRecord reply = host.Adapter.SentMessages[^1];
        Assert.Contains("бюджет", reply.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Лимиты:", reply.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("/день", reply.Text ?? string.Empty, StringComparison.Ordinal);
    }

    [E2EFact]
    public async Task HitlTimeout_ActionNotRunRecordExpiredAndOwnerNotified()
    {
        await using var host = await E2EHost.CreateAsync(OwnerId, 777106);
        host.Assistant.OverrideHitlLifetime(TimeSpan.FromSeconds(1.5));

        await host.FeedTextAsync(OwnerNumericId, "громкость на 70");
        Guid approvalId = await WaitForApprovalAsync(host);

        await Task.Delay(2000);
        int expired = await host.Hitl.ExpireOverdueAsync(DateTimeOffset.UtcNow);
        Assert.True(expired >= 1);

        HitlApprovalResult decision =
            await host.Assistant.WaitDecisionAsync(approvalId, TimeSpan.FromSeconds(10));
        Assert.Equal(HitlDecision.Expired, decision.Decision);
        Assert.Equal(0, host.VolumeSkill.Invocations);
        Assert.Equal(1, host.ExecutorStub.CancelCalls);

        SentTelegramMessageRecord reply = host.Adapter.SentMessages[^1];
        Assert.Equal(host.ChatId, reply.ChatId);
        Assert.Contains("Время подтверждения вышло", reply.Text ?? string.Empty, StringComparison.Ordinal);

        HitlApprovalRecord? record = await host.HitlRepository.GetAsync(approvalId);
        Assert.NotNull(record);
        Assert.Equal(HitlStatus.Expired, record.Status);
        Assert.NotNull(record.Resolution);
        Assert.Equal(HitlDecision.Expired, record.Resolution.Decision);
    }

    private static async Task<Guid> WaitForApprovalAsync(E2EHost host)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (host.Assistant.LastApprovalId is Guid approvalId)
            {
                return approvalId;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("HITL approval card never appeared in the fake transport.");
    }
}
