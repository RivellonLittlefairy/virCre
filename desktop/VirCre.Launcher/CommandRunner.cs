using System.Diagnostics;
using System.Text;

namespace VirCre.Launcher;

public sealed record CommandResult(int ExitCode, string Output, string Error);
public interface ICommandRunner {
    Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token);
}

/// <summary>Runs metadata commands with an explicit deadline, cancellation and no console window.</summary>
public sealed class CommandRunner : ICommandRunner {
    public async Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        using var process = new Process { StartInfo = new ProcessStartInfo(file) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        }};
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) { throw new IOException($"找不到或无法启动 {file}，请检查 PATH 中的工具路径。"); }
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        } catch (OperationCanceledException) {
            if (!process.HasExited) {
                try { process.Kill(true); } catch (InvalidOperationException) { /* Already exited after the check. */ }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            try { await Task.WhenAll(output, error).ConfigureAwait(false); } catch (OperationCanceledException) { }
            token.ThrowIfCancellationRequested();
            throw new TimeoutException($"{file} 读取超时，请检查手机连接后重试。");
        }
    }
}
