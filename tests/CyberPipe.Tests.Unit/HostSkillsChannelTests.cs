using CyberPipe.Desktop.ViewModels;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class HostSkillsChannelTests
{
    [Fact]
    public async Task ExternalChannel_Message_RunsHostSkill()
    {
        HostSkillsTestViewModel form = HostSkillsTestViewModel.CreateProbe();
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
        HostSkillsTestViewModel form = HostSkillsTestViewModel.CreateProbe();
        form.SelectedSkill = form.Skills.Single(skill => skill.SkillId == "volume.set");
        form.PayloadJson = "{ not json";

        await form.SendAsync();

        Assert.Contains("Host.ValidationError", form.Transcript[1].Text);
        Assert.Contains("last command: none", form.HostState);
    }
}
