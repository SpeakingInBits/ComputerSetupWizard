using System.Diagnostics;
using System.Text;

namespace ComputerSetupWizard.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>Output and error text together, for showing to the user.</summary>
    public string AllText => string.Join(Environment.NewLine, new[] { Output, Error }.Where(s => s.Length > 0));
}

public static class ProcessRunner
{
    /// <summary>Runs a command-line tool without a window and collects its output.</summary>
    /// <param name="environment">Variables to set for the child; a null value removes the variable.</param>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? onOutput = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? input = null,
        CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo(ToolPaths.Resolve(fileName))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null)
            startInfo.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        ApplyEnvironment(startInfo, environment);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => Collect(output, e.Data, onOutput);
        process.ErrorDataReceived += (_, e) => Collect(error, e.Data, onOutput);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        lock (output)
        lock (error)
            return new ProcessResult(process.ExitCode, output.ToString().TrimEnd(), error.ToString().TrimEnd());
    }

    /// <summary>
    /// Starts a console tool in its own window for steps that need the user, such as <c>gh auth login</c>,
    /// and waits for it to close.
    /// </summary>
    public static async Task<int> RunInConsoleWindowAsync(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        // Without redirection or CreateNoWindow, a console child of a GUI app gets a console of its own
        var startInfo = new ProcessStartInfo(ToolPaths.Resolve(fileName)) { UseShellExecute = false };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        ApplyEnvironment(startInfo, environment);

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>Runs a program that needs administrator rights, which shows the UAC prompt, and waits for it.</summary>
    public static async Task<int> RunElevatedAsync(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments) { UseShellExecute = true, Verb = "runas" };
        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    static void ApplyEnvironment(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string?>? environment)
    {
        if (environment is null)
            return;
        foreach (var (name, value) in environment)
        {
            if (value is null)
                startInfo.Environment.Remove(name);
            else
                startInfo.Environment[name] = value;
        }
    }

    static void Collect(StringBuilder buffer, string? line, Action<string>? onOutput)
    {
        if (line is null)
            return;
        lock (buffer)
            buffer.AppendLine(line);
        onOutput?.Invoke(line);
    }
}
