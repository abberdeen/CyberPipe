namespace CyberPipe.Cli.Commands;

public static class LogsCommand
{
    public static async Task<int> ExecuteAsync(CliContext ctx, string[] args, CancellationToken ct = default)
    {
        string logDir = ctx.Options?.Logging.LogDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(logDir))
        {
            await ctx.Out.WriteLineAsync($"No logs found in {logDir}.").ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        string[] logFiles = Directory.GetFiles(logDir, "*.log", SearchOption.TopDirectoryOnly);
        if (logFiles.Length == 0)
        {
            await ctx.Out.WriteLineAsync($"No log files available in {logDir}.").ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        // Get newest log file
        string newestFile = logFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
        await ctx.Out.WriteLineAsync($"Reading log file: {Path.GetFileName(newestFile)}").ConfigureAwait(false);

        int lineCount = 50;
        foreach (string arg in args)
        {
            if (int.TryParse(arg, out int parsedLines))
            {
                lineCount = parsedLines;
            }
        }

        try
        {
            using var fileStream = new FileStream(newestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            var lines = new List<string>();
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            {
                lines.Add(line);
                if (lines.Count > lineCount)
                {
                    lines.RemoveAt(0);
                }
            }

            foreach (string l in lines)
            {
                await ctx.Out.WriteLineAsync(l).ConfigureAwait(false);
            }

            return CliExitCodes.Success;
        }
        catch (Exception ex)
        {
            await ctx.Error.WriteLineAsync($"Failed to read logs: {ex.Message}").ConfigureAwait(false);
            return CliExitCodes.GeneralError;
        }
    }
}
