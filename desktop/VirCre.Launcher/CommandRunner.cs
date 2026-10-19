using System.Diagnostics;
using System.Text;

namespace VirCre.Launcher;

public sealed record CommandResult(int ExitCode, string Output, string Error);
public interface ICommandRunner {
    Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token);
}

/// <summary>Runs metadata commands with an explicit deadline, cancellation and no console window.</summary>
public sealed class CommandRunner(BundledTools? tools=null) : ICommandRunner {
    public async Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var start=(tools??new BundledTools()).CreateStartInfo(file);
        start.UseShellExecute=false;start.CreateNoWindow=true;
        start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;
        using var process = new Process { StartInfo = start };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) { throw new IOException($"无法启动 {file}，请检查完整发布目录中的手机连接组件。"); }
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
