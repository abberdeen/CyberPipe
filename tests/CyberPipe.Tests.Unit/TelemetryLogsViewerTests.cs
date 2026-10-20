using System.Text.Json;
using CyberPipe.Desktop.Services;
using CyberPipe.Desktop.ViewModels;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class TelemetryLogsViewerTests
{
    private sealed class FakeClipboardService : IClipboardService
    {
        public string? CopiedText { get; private set; }

        public Task SetTextAsync(string? text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void ParseLogLine_WithOpenTelemetryTrace_ParsesAllFields()
    {
        // Arrange
        string jsonLine = """
        {
            "Timestamp": "2026-10-20T04:13:59.6146153+05:00",
            "Level": "Information",
            "MessageTemplate": "Chain step {Step} completed with status {Status}",
            "TraceId": "c4b3df50a63a4ff32acc0d2306e06fd7",
            "SpanId": "7cf8e8c9be0f207d",
            "Properties": {
                "Step": "WhisperTranscription",
                "Status": "OK",
                "SourceContext": "CyberPipe.Brainstem.Supervisor",
                "ProcessName": "Brainstem"
            }
        }
        """;

        // Act
        OpenTelemetryLogEntry? entry = TelemetryLogReaderService.ParseLogLine(jsonLine, "Service");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("Information", entry.Level);
        Assert.Equal("Brainstem", entry.ProcessName);
        Assert.Equal("c4b3df50a63a4ff32acc0d2306e06fd7", entry.TraceId);
        Assert.Equal("7cf8e8c9be0f207d", entry.SpanId);
        Assert.True(entry.HasTrace);
        Assert.Equal("CyberPipe.Brainstem.Supervisor", entry.SourceContext);
        Assert.Contains("Chain step WhisperTranscription completed with status OK", entry.Message);
        Assert.Equal("WhisperTranscription", entry.Properties["Step"]);
    }

    [Fact]
    public async Task ReadLogsAsync_AggregatesLogsFromMultipleProcesses()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"cyberpipe-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string serviceLog = Path.Combine(tempDir, "Service-20261020.log");
            string brainstemLog = Path.Combine(tempDir, "Brainstem-20261020.log");
            string desktopLog = Path.Combine(tempDir, "Desktop-20261020.log");

            await File.WriteAllLinesAsync(serviceLog, [
                """{"Timestamp":"2026-10-20T10:00:00.0000000+00:00","Level":"Information","MessageTemplate":"Service started","Properties":{"ProcessName":"Service"}}""",
                """{"Timestamp":"2026-10-20T10:00:01.0000000+00:00","Level":"Information","MessageTemplate":"Dispatching job to Brainstem","TraceId":"trace-1234567890ab","SpanId":"span-1","Properties":{"ProcessName":"Service"}}"""
            ]);

            await File.WriteAllLinesAsync(brainstemLog, [
                """{"Timestamp":"2026-10-20T10:00:02.0000000+00:00","Level":"Information","MessageTemplate":"Brainstem executing Whisper","TraceId":"trace-1234567890ab","SpanId":"span-2","Properties":{"ProcessName":"Brainstem"}}""",
                """{"Timestamp":"2026-10-20T10:00:03.0000000+00:00","Level":"Warning","MessageTemplate":"Whisper CPU throttling","Properties":{"ProcessName":"Brainstem"}}"""
            ]);

            await File.WriteAllLinesAsync(desktopLog, [
                """{"Timestamp":"2026-10-20T10:00:04.0000000+00:00","Level":"Information","MessageTemplate":"Desktop UI connected","Properties":{"ProcessName":"Desktop"}}"""
            ]);

            var reader = new TelemetryLogReaderService();

            // Act
            IReadOnlyList<OpenTelemetryLogEntry> logs = await reader.ReadLogsAsync(tempDir);

            // Assert: all 5 logs from Service, Brainstem, Desktop are loaded
            Assert.Equal(5, logs.Count);

            // Verify order by timestamp
            Assert.Equal("Service started", logs[0].Message);
            Assert.Equal("Desktop UI connected", logs[4].Message);

            // Check OpenTelemetry traces
            int traceCount = logs.Count(l => l.HasTrace);
            Assert.Equal(2, traceCount);

            var traceLogs = logs.Where(l => l.TraceId == "trace-1234567890ab").ToList();
            Assert.Equal(2, traceLogs.Count);
            Assert.Contains(traceLogs, l => l.ProcessName == "Service");
            Assert.Contains(traceLogs, l => l.ProcessName == "Brainstem");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TelemetryLogsViewModel_FilteringAndTraceCorrelation()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"cyberpipe-logs-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllLinesAsync(Path.Combine(tempDir, "Service-20261020.log"), [
                """{"Timestamp":"2026-10-20T10:00:00.0000000+00:00","Level":"Information","MessageTemplate":"Normal service log","Properties":{"ProcessName":"Service"}}""",
                """{"Timestamp":"2026-10-20T10:00:01.0000000+00:00","Level":"Information","MessageTemplate":"Service job start","TraceId":"shared-trace-999","SpanId":"s1","Properties":{"ProcessName":"Service"}}"""
            ]);

            await File.WriteAllLinesAsync(Path.Combine(tempDir, "Brainstem-20261020.log"), [
                """{"Timestamp":"2026-10-20T10:00:02.0000000+00:00","Level":"Error","MessageTemplate":"Brainstem job error","TraceId":"shared-trace-999","SpanId":"s2","Properties":{"ProcessName":"Brainstem"}}""",
                """{"Timestamp":"2026-10-20T10:00:03.0000000+00:00","Level":"Information","MessageTemplate":"Brainstem idle","Properties":{"ProcessName":"Brainstem"}}"""
            ]);

            var reader = new TelemetryLogReaderService();
            var clipboard = new FakeClipboardService();
            var vm = new TelemetryLogsViewModel(reader, clipboard, logDirectory: tempDir, enableTimer: false);

            // Wait for initial load
            await vm.LoadLogsAsync(isAutoRefresh: false);
            Assert.Equal(4, vm.FilteredEntries.Count);

            // 1. Filter by Process: Brainstem only
            vm.SelectedProcessFilter = "Brainstem";
            Assert.Equal(2, vm.FilteredEntries.Count);
            Assert.All(vm.FilteredEntries, e => Assert.Equal("Brainstem", e.ProcessName));

            // Reset process filter
            vm.SelectedProcessFilter = "All Processes";
            Assert.Equal(4, vm.FilteredEntries.Count);

            // 2. Filter by OpenTelemetry Only (HasTrace)
            vm.OpenTelemetryOnly = true;
            Assert.Equal(2, vm.FilteredEntries.Count);
            Assert.All(vm.FilteredEntries, e => Assert.True(e.HasTrace));

            // 3. Filter by specific TraceId across processes
            vm.OpenTelemetryOnly = false;
            vm.SelectedEntry = vm.FilteredEntries.First(e => e.TraceId == "shared-trace-999");
            vm.FilterByTraceCommand.Execute(null);

            Assert.True(vm.HasActiveTraceFilter);
            Assert.Equal("shared-trace-999", vm.ActiveTraceIdFilter);
            Assert.Equal(2, vm.FilteredEntries.Count);
            // Must contain entries from BOTH Service and Brainstem
            Assert.Contains(vm.FilteredEntries, e => e.ProcessName == "Service");
            Assert.Contains(vm.FilteredEntries, e => e.ProcessName == "Brainstem");

            // Copy TraceId command
            await vm.CopyTraceIdCommand.ExecuteAsync(null);
            Assert.Equal("shared-trace-999", clipboard.CopiedText);

            // Copy raw JSON command
            await vm.CopySelectedJsonCommand.ExecuteAsync(null);
            Assert.Contains("shared-trace-999", clipboard.CopiedText);

            // Clear trace filter
            vm.ClearTraceFilterCommand.Execute(null);
            Assert.False(vm.HasActiveTraceFilter);
            Assert.Equal(4, vm.FilteredEntries.Count);

            // 4. Filter by Level: Error only
            vm.SelectedLevelFilter = "Error";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("Brainstem job error", vm.FilteredEntries[0].Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void MainWindowViewModel_OpenLogsWindowCommand_InvokesCallback()
    {
        // Arrange
        bool windowRequested = false;
        MainWindowViewModel mainVm = MainWindowViewModelFactory.CreateFallback();
        mainVm.OpenLogsWindowRequested = () => windowRequested = true;

        // Act
        mainVm.OpenLogsWindowCommand.Execute(null);

        // Assert
        Assert.True(windowRequested);
        Assert.NotNull(mainVm.Logs);
        Assert.NotNull(mainVm.Logs.ProcessFilters);
        Assert.Contains("Brainstem", mainVm.Logs.ProcessFilters);
        Assert.Contains("Service", mainVm.Logs.ProcessFilters);
    }
}
