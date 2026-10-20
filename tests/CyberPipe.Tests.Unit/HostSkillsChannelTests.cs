using CyberPipe.Desktop.Services;
using CyberPipe.Desktop.ViewModels;
using CyberPipe.Skills.Host.Media;
using CyberPipe.Skills.Host.Registry;
using CyberPipe.Skills.Host.System;
using CyberPipe.Skills.Host.Timers;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class HostSkillsChannelTests
{
    [Fact]
    public async Task ExternalChannel_Message_RunsHostSkill()
    {
        HostSkillsTestViewModel form = CreateForm(new FakeMediaController());
        form.SelectedSkill = form.Skills.Single(skill => skill.SkillId == "volume.set");
        form.PayloadJson = "{\"volume\":40}";

        await form.SendAsync();

        Assert.Equal("External channel", form.Transcript[0].Source);
        Assert.Contains("\"skillId\":\"volume.set\"", form.Transcript[0].Text);
        Assert.DoesNotContain("Telegram", form.Transcript[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"volume\":40", form.ResultText);
        Assert.Contains("Volume 40%", form.HostState);
    }

    [Fact]
    public async Task ExternalChannel_InvalidPayload_ReturnsChannelError()
    {
        var media = new FakeMediaController { Volume = 50 };
        HostSkillsTestViewModel form = CreateForm(media);
        form.SelectedSkill = form.Skills.Single(skill => skill.SkillId == "volume.set");
        form.PayloadJson = "{ not json";

        await form.SendAsync();

        Assert.Contains("Host.ValidationError", form.Transcript[1].Text);
        Assert.Contains("Volume 50%", form.HostState);
    }

    private static HostSkillsTestViewModel CreateForm(FakeMediaController media)
    {
        var registry = new HostSkillRegistry(
            media,
            new FakeSystemStatusProvider(),
            new InMemoryTimerService(),
            new InMemoryAlarmService());
        var inbox = new ExternalChannelInbox();
        var handler = new ExternalChannelHostSkillHandler(registry);
        inbox.Subscribe(handler);
        return new HostSkillsTestViewModel(inbox, handler, media, registry.GetAllSkills());
    }
}
