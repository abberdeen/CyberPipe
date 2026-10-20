using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CyberPipe.Contracts;
using CyberPipe.Desktop.Common;
using CyberPipe.Desktop.Services;
using CyberPipe.Skills.Host.Media;
using CyberPipe.Skills.Host.Registry;
using CyberPipe.Skills.Host.System;
using CyberPipe.Skills.Host.Timers;

namespace CyberPipe.Desktop.ViewModels;

/// <summary>
/// Форма проверки хост-навыков. Команда уходит сообщением внешнего канала, а не прямым вызовом навыка.
/// </summary>
public sealed class HostSkillsTestViewModel : ViewModelBase
{
    private readonly ExternalChannelInbox _inbox;
    private readonly ExternalChannelHostSkillHandler _handler;
    private readonly FakeMediaController _media;
    private HostSkillChoice? _selectedSkill;
    private string _payloadJson = "{}";
    private string _resultText = string.Empty;
    private bool _isBusy;

    public HostSkillsTestViewModel(
        ExternalChannelInbox inbox,
        ExternalChannelHostSkillHandler handler,
        FakeMediaController media,
        IReadOnlyList<IHostSkill> skills)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        ArgumentNullException.ThrowIfNull(skills);

        foreach (IHostSkill skill in skills)
        {
            Skills.Add(new HostSkillChoice(skill.SkillId, skill.Name, skill.Description, SamplePayload(skill.SkillId)));
        }

        _selectedSkill = Skills.FirstOrDefault();
        if (_selectedSkill is not null)
        {
            _payloadJson = _selectedSkill.SamplePayload;
        }

        SendCommand = new AsyncRelayCommand(SendAsync, () => !IsBusy && SelectedSkill is not null);
    }

    public static HostSkillsTestViewModel CreateProbe()
    {
        var media = new FakeMediaController();
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

    public ObservableCollection<HostSkillChoice> Skills { get; } = [];

    public ObservableCollection<ChannelTranscriptLine> Transcript { get; } = [];

    public string ChannelName => "External channel";

    public HostSkillChoice? SelectedSkill
    {
        get => _selectedSkill;
        set
        {
            if (SetProperty(ref _selectedSkill, value) && value is not null)
            {
                PayloadJson = value.SamplePayload;
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PayloadJson
    {
        get => _payloadJson;
        set => SetProperty(ref _payloadJson, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string HostState =>
        $"Volume {_media.Volume}%, muted {_media.IsMuted}, playing {_media.IsPlaying}, last command: {_media.LastCommand ?? "none"}";

    public AsyncRelayCommand SendCommand { get; }

    public async Task SendAsync()
    {
        if (SelectedSkill is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            string envelope = BuildEnvelope(SelectedSkill.SkillId, PayloadJson);
            Transcript.Add(new ChannelTranscriptLine(ChannelName, envelope));
            await _inbox.PublishAsync(new IncomingMessage
            {
                Id = new MessageId(Guid.NewGuid().ToString("N")),
                Channel = ExternalChannelInbox.OutsideChannel,
                SenderId = "external-user",
                ChatId = "external-chat",
                Text = envelope,
                SentAtUtc = DateTimeOffset.UtcNow,
            }).ConfigureAwait(true);

            ExternalChannelReply reply = _handler.LastReply
                ?? new ExternalChannelReply(false, "External channel did not reply.", null, "ExternalChannel.NoReply");
            string replyText = reply.Accepted
                ? reply.Text
                : $"{reply.ErrorCode}: {reply.Text}";
            Transcript.Add(new ChannelTranscriptLine("Reply", replyText));
            ResultText = reply.OutputJson ?? reply.Text;
            OnPropertyChanged(nameof(HostState));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildEnvelope(string skillId, string payloadJson)
    {
        JsonNode? payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(payloadJson)
                ? new JsonObject()
                : JsonNode.Parse(payloadJson);
        }
        catch (System.Text.Json.JsonException)
        {
            payload = payloadJson;
        }

        return new JsonObject
        {
            ["skillId"] = skillId,
            ["payload"] = payload,
        }.ToJsonString();
    }

    private static string SamplePayload(string skillId) => skillId switch
    {
        "volume.set" => """{"volume":40}""",
        "volume.up" or "volume.down" => """{"step":10}""",
        "timer.set" => """{"durationSeconds":30,"title":"Test"}""",
        "timer.cancel" => """{"timerId":"00000000-0000-0000-0000-000000000000"}""",
        "alarm.set" => """{"time":"08:30","title":"Test","daysOfWeek":["Monday"]}""",
        "alarm.cancel" => """{"alarmId":"00000000-0000-0000-0000-000000000000"}""",
        _ => "{}",
    };
}

public sealed record HostSkillChoice(string SkillId, string Name, string Description, string SamplePayload);

public sealed record ChannelTranscriptLine(string Source, string Text);
