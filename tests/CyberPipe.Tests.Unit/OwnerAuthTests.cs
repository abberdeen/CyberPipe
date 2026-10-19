namespace CyberPipe.Tests.Unit;

using System.Security.Cryptography;
using System.Text;
using CyberPipe.Contracts;
using CyberPipe.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class OwnerAuthTests
{
    private sealed class MutableWhitelistProvider : IOwnerWhitelistProvider
    {
        public OwnerWhitelistSnapshot Snapshot { get; set; } = OwnerWhitelistSnapshot.Empty;

        public bool ShouldThrow { get; set; }

        public bool ReturnNull { get; set; }

        public event EventHandler? WhitelistChanged;

        public void RaiseChanged() => WhitelistChanged?.Invoke(this, EventArgs.Empty);

        public Task<OwnerWhitelistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Whitelist store unavailable.");
            }

            return Task.FromResult<OwnerWhitelistSnapshot>(ReturnNull ? null! : Snapshot);
        }
    }

    private sealed class CapturingLogger : ILogger<OwnerAuthorizer>
    {
        private readonly object _sync = new();

        public List<string> Messages { get; } = [];

        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_sync)
            {
                Messages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<CyberPipeOptions>
    {
        public CyberPipeOptions CurrentValue { get; set; } = new();

        public CyberPipeOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<CyberPipeOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private static OwnerWhitelistSnapshot FullSnapshot() =>
        new()
        {
            TelegramOwnerIds = ["111", "222"],
            DesktopOwnerId = "desktop-owner",
            CliOwnerId = "cli-owner",
        };

    [Theory]
    [InlineData(OwnerChannel.Telegram, "111")]
    [InlineData(OwnerChannel.Telegram, "222")]
    [InlineData(OwnerChannel.Desktop, "desktop-owner")]
    [InlineData(OwnerChannel.Cli, "cli-owner")]
    public async Task WhitelistedIdentity_IsAuthorized(OwnerChannel channel, string senderId)
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() });
        var principal = new ChannelPrincipal { Channel = channel, SenderId = senderId };

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(principal);

        Assert.Equal(OwnerAuthOutcome.Authorized, decision.Outcome);
        Assert.Equal(OwnerAuthReason.WhitelistMatch, decision.Reason);
        Assert.True(decision.IsAuthorized);
        Assert.True(await authorizer.IsOwnerAsync(principal));
    }

    [Theory]
    [InlineData(OwnerChannel.Telegram, "desktop-owner")]
    [InlineData(OwnerChannel.Telegram, "cli-owner")]
    [InlineData(OwnerChannel.Desktop, "111")]
    [InlineData(OwnerChannel.Desktop, "cli-owner")]
    [InlineData(OwnerChannel.Cli, "111")]
    [InlineData(OwnerChannel.Cli, "desktop-owner")]
    public async Task SameIdentityInForeignChannel_IsDenied(OwnerChannel channel, string senderId)
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() });

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(new ChannelPrincipal { Channel = channel, SenderId = senderId });

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.NotInWhitelist, decision.Reason);
    }

    [Fact]
    public async Task DisplayNameMatchWithoutIdentityMatch_IsDenied()
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() });
        var principal = new ChannelPrincipal
        {
            Channel = OwnerChannel.Telegram,
            SenderId = "999",
            DisplayName = "111",
        };

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(principal);

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.NotInWhitelist, decision.Reason);
    }

    [Theory]
    [InlineData(OwnerChannel.Telegram)]
    [InlineData(OwnerChannel.Desktop)]
    [InlineData(OwnerChannel.Cli)]
    public async Task EmptyChannelWhitelist_DeniesEveryone(OwnerChannel channel)
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = OwnerWhitelistSnapshot.Empty });

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(new ChannelPrincipal { Channel = channel, SenderId = "any-id" });

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.EmptyWhitelist, decision.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptySenderId_IsDeniedWithMissingIdentity(string? senderId)
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() });

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(
            new ChannelPrincipal { Channel = OwnerChannel.Telegram, SenderId = senderId! });

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.MissingIdentity, decision.Reason);
    }

    [Fact]
    public async Task NullPrincipal_IsDeniedWithMissingIdentity()
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() });

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(null!);

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.MissingIdentity, decision.Reason);
    }

    [Theory]
    [InlineData(OwnerChannel.Webhook)]
    [InlineData(OwnerChannel.Unknown)]
    public async Task WebhookAndUnknownChannels_AreNeverOwnerAuthorized(OwnerChannel channel)
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() });

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(
            new ChannelPrincipal { Channel = channel, SenderId = "111" });

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.UnknownChannel, decision.Reason);
        Assert.False(await authorizer.IsOwnerAsync(new ChannelPrincipal { Channel = channel, SenderId = "111" }));
    }

    [Fact]
    public async Task ThrowingProvider_DeniesEveryoneWithWhitelistUnavailable()
    {
        var provider = new MutableWhitelistProvider { Snapshot = FullSnapshot(), ShouldThrow = true };
        var authorizer = new OwnerAuthorizer(provider);

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(
            new ChannelPrincipal { Channel = OwnerChannel.Telegram, SenderId = "111" });

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.WhitelistUnavailable, decision.Reason);
    }

    [Fact]
    public async Task NullSnapshot_DeniesWithWhitelistUnavailable()
    {
        var provider = new MutableWhitelistProvider { Snapshot = FullSnapshot(), ReturnNull = true };
        var authorizer = new OwnerAuthorizer(provider);

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(
            new ChannelPrincipal { Channel = OwnerChannel.Cli, SenderId = "cli-owner" });

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.WhitelistUnavailable, decision.Reason);
    }

    [Fact]
    public async Task ProviderFailureAfterPriorSuccess_DoesNotReuseCachedAuthorization()
    {
        var provider = new MutableWhitelistProvider { Snapshot = FullSnapshot() };
        var authorizer = new OwnerAuthorizer(provider);
        var principal = new ChannelPrincipal { Channel = OwnerChannel.Telegram, SenderId = "111" };

        Assert.True((await authorizer.AuthorizeAsync(principal)).IsAuthorized);

        provider.ShouldThrow = true;
        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(principal);

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.WhitelistUnavailable, decision.Reason);
    }

    [Fact]
    public async Task RevokedIdentity_AppliesToNextCheckWithoutRestart()
    {
        var provider = new MutableWhitelistProvider { Snapshot = FullSnapshot() };
        var authorizer = new OwnerAuthorizer(provider);
        var principal = new ChannelPrincipal { Channel = OwnerChannel.Desktop, SenderId = "desktop-owner" };

        Assert.True((await authorizer.AuthorizeAsync(principal)).IsAuthorized);

        provider.Snapshot = new OwnerWhitelistSnapshot
        {
            TelegramOwnerIds = ["111", "222"],
            DesktopOwnerId = null,
            CliOwnerId = "cli-owner",
        };
        provider.RaiseChanged();

        OwnerAuthDecision decision = await authorizer.AuthorizeAsync(principal);

        Assert.Equal(OwnerAuthOutcome.Denied, decision.Outcome);
        Assert.Equal(OwnerAuthReason.EmptyWhitelist, decision.Reason);
    }

    [Fact]
    public async Task AddedIdentity_AppliesToNextCheckWithoutRestart()
    {
        var provider = new MutableWhitelistProvider { Snapshot = OwnerWhitelistSnapshot.Empty };
        var authorizer = new OwnerAuthorizer(provider);
        var principal = new ChannelPrincipal { Channel = OwnerChannel.Telegram, SenderId = "777" };

        Assert.Equal(OwnerAuthReason.EmptyWhitelist, (await authorizer.AuthorizeAsync(principal)).Reason);

        provider.Snapshot = new OwnerWhitelistSnapshot { TelegramOwnerIds = ["777"] };

        Assert.True((await authorizer.AuthorizeAsync(principal)).IsAuthorized);
    }

    [Fact]
    public async Task Summary_ExposesCountsWithoutFullIdentities()
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() });

        OwnerWhitelistSummary summary = await authorizer.GetWhitelistSummaryAsync();

        Assert.Equal(2, summary.TelegramCount);
        Assert.True(summary.HasDesktopOwner);
        Assert.True(summary.HasCliOwner);
        Assert.True(summary.HasAnyOwner);
        Assert.DoesNotContain("111", summary.ToString());
        Assert.DoesNotContain("desktop-owner", summary.ToString());
    }

    [Fact]
    public async Task Summary_WhenProviderFails_ReportsNoOwners()
    {
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { ShouldThrow = true });

        OwnerWhitelistSummary summary = await authorizer.GetWhitelistSummaryAsync();

        Assert.False(summary.HasAnyOwner);
        Assert.Equal(0, summary.TelegramCount);
    }

    [Fact]
    public async Task AuditLog_ContainsNoFullIdentities()
    {
        var logger = new CapturingLogger();
        var authorizer = new OwnerAuthorizer(new MutableWhitelistProvider { Snapshot = FullSnapshot() }, logger);

        await authorizer.AuthorizeAsync(new ChannelPrincipal { Channel = OwnerChannel.Telegram, SenderId = "111" });
        await authorizer.AuthorizeAsync(new ChannelPrincipal { Channel = OwnerChannel.Desktop, SenderId = "intruder" });

        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, m => Assert.DoesNotContain("111", m));
        Assert.All(logger.Messages, m => Assert.DoesNotContain("intruder", m));

        string expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("111")))[..8];
        Assert.Contains(logger.Messages, m => m.Contains(expectedHash, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ChannelKind.Telegram, OwnerChannel.Telegram)]
    [InlineData(ChannelKind.Desktop, OwnerChannel.Desktop)]
    [InlineData(ChannelKind.Cli, OwnerChannel.Cli)]
    public void FromChannel_MapsKnownChannels(ChannelKind channelKind, OwnerChannel expected)
    {
        ChannelPrincipal principal = ChannelPrincipal.FromChannel(channelKind, "owner-1", "Owner");

        Assert.Equal(expected, principal.Channel);
        Assert.Equal("owner-1", principal.SenderId);
        Assert.Equal("Owner", principal.DisplayName);
    }

    [Fact]
    public async Task OptionsProvider_ReadsOwnerSectionAndReflectsReload()
    {
        var monitor = new TestOptionsMonitor
        {
            CurrentValue = new CyberPipeOptions
            {
                Owner = new OwnerOptions
                {
                    TelegramOwnerIds = ["42", "  ", ""],
                    DesktopOwnerId = "desktop-1",
                    CliOwnerId = null,
                },
            },
        };
        using var provider = new OptionsOwnerWhitelistProvider(monitor);
        var authorizer = new OwnerAuthorizer(provider);

        Assert.True(await authorizer.IsOwnerAsync(new ChannelPrincipal { Channel = OwnerChannel.Telegram, SenderId = "42" }));
        Assert.False(await authorizer.IsOwnerAsync(new ChannelPrincipal { Channel = OwnerChannel.Cli, SenderId = "x" }));

        monitor.CurrentValue = new CyberPipeOptions
        {
            Owner = new OwnerOptions { TelegramOwnerIds = [], DesktopOwnerId = null, CliOwnerId = "cli-9" },
        };

        Assert.False(await authorizer.IsOwnerAsync(new ChannelPrincipal { Channel = OwnerChannel.Telegram, SenderId = "42" }));
        Assert.True(await authorizer.IsOwnerAsync(new ChannelPrincipal { Channel = OwnerChannel.Cli, SenderId = "cli-9" }));
    }
}
