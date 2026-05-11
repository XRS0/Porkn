using System.Diagnostics;

namespace Porkn.Windows;

internal sealed class SingBoxProcessManager
{
    private Process? _process;

    public bool IsRunning => _process?.HasExited == false;

    public void Start(Profile profile, int localProxyPort, Action<string> onLog)
    {
        if (IsRunning) throw new InvalidOperationException("sing-box is already running");

        Directory.CreateDirectory(AppPaths.RuntimeDirectory);
        var configPath = Path.Combine(AppPaths.RuntimeDirectory, "active-sing-box.json");
        File.WriteAllText(configPath, SingBoxConfigGenerator.Generate(profile, localProxyPort));

        var binary = ResolveSingBoxBinary();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = $"run -c \"{configPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) onLog(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) onLog(args.Data); };
        process.Exited += (_, _) => onLog($"sing-box exited with code {process.ExitCode}");

        if (!process.Start()) throw new InvalidOperationException("Unable to start sing-box");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
    }

    public void Stop()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2500);
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static string ResolveSingBoxBinary()
    {
        var bundled = Path.Combine(AppPaths.AppDirectory, "Resources", "bin", "sing-box.exe");
        if (File.Exists(bundled)) return bundled;

        var local = Path.Combine(AppPaths.AppDirectory, "sing-box.exe");
        if (File.Exists(local)) return local;

        return "sing-box.exe";
    }
}
