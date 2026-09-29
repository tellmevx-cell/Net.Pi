using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class ExecuteCommandTool : ITool
{
    private readonly string _workingDirectory;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(2);
    private const int MaxOutputLines = 2000;
    private const int MaxOutputChars = 512 * 1024; // 512 KB

    public string Name => "execute_command";
    public string Description => "Executes a shell command on the host system (cmd on Windows with UTF-8, bash on Unix) with timeout, cancellation, and output buffer protection.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The shell command to execute" },
            timeoutSeconds = new { type = "integer", description = "Timeout in seconds (default 120)" }
        },
        required = new[] { "command" }
    };

    public ExecuteCommandTool(string? workingDirectory = null)
    {
        _workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var command = node?["command"]?.GetValue<string>();
            var timeoutSeconds = node?["timeoutSeconds"]?.GetValue<int>() ?? (int)_defaultTimeout.TotalSeconds;

            if (string.IsNullOrWhiteSpace(command))
            {
                return ToolResult.Error("Missing required parameter: 'command'.");
            }

            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var fileName = isWindows ? "cmd.exe" : "/bin/bash";
            // Prepend chcp 65001 on Windows cmd to guarantee UTF-8 console output
            var args = isWindows ? $"/c \"chcp 65001 >nul && {command}\"" : $"-c \"{command.Replace("\"", "\\\"")}\"";

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            int stdoutLineCount = 0;
            int stderrLineCount = 0;
            bool stdoutTruncated = false;
            bool stderrTruncated = false;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (stdoutLineCount < MaxOutputLines && stdout.Length < MaxOutputChars)
                {
                    stdout.AppendLine(e.Data);
                    stdoutLineCount++;
                }
                else if (!stdoutTruncated)
                {
                    stdout.AppendLine($"... [Output truncated after {MaxOutputLines} lines / {MaxOutputChars / 1024} KB limit]");
                    stdoutTruncated = true;
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (stderrLineCount < MaxOutputLines && stderr.Length < MaxOutputChars)
                {
                    stderr.AppendLine(e.Data);
                    stderrLineCount++;
                }
                else if (!stderrTruncated)
                {
                    stderr.AppendLine($"... [Error output truncated after {MaxOutputLines} lines]");
                    stderrTruncated = true;
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (timeoutCts.IsCancellationRequested)
                {
                    return ToolResult.Error($"Command timed out after {timeoutSeconds} seconds.");
                }
                return ToolResult.Error("Command execution was cancelled.");
            }

            var output = stdout.ToString();
            var error = stderr.ToString();

            var combined = new StringBuilder();
            if (!string.IsNullOrEmpty(output)) combined.AppendLine(output);
            if (!string.IsNullOrEmpty(error)) combined.AppendLine($"[STDERR]\n{error}");

            var resultText = combined.ToString().Trim();
            if (process.ExitCode != 0)
            {
                return ToolResult.Error($"Command exited with code {process.ExitCode}.\n{resultText}");
            }

            return ToolResult.Ok(string.IsNullOrEmpty(resultText) ? "(command succeeded with no output)" : resultText);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to execute command: {ex.Message}");
        }
    }
}
