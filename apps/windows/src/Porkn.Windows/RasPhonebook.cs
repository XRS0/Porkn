using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Porkn.Windows;

internal static class ProfileKinds
{
    public const string Ras = "ras";

    public static bool IsRasProfile(this Profile profile) =>
        string.Equals(profile.Protocol, Ras, StringComparison.OrdinalIgnoreCase);
}

internal static class RasPhonebookImporter
{
    public static List<Profile> Import(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("PBK path is empty", nameof(sourcePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("rasphone.pbk file not found", sourcePath);

        var managedPath = CopyToManagedPhonebook(sourcePath);
        return ParseProfiles(managedPath, sourcePath);
    }

    private static string CopyToManagedPhonebook(string sourcePath)
    {
        var directory = Path.Combine(AppPaths.DataDirectory, "RasPhonebooks");
        Directory.CreateDirectory(directory);

        var bytes = File.ReadAllBytes(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
        var safeName = string.Join("_", Path.GetFileNameWithoutExtension(sourcePath).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "rasphone";
        var destination = Path.Combine(directory, $"{safeName}-{hash}.pbk");
        File.WriteAllBytes(destination, bytes);
        return destination;
    }

    private static List<Profile> ParseProfiles(string managedPath, string sourcePath)
    {
        var text = ReadPhonebookText(managedPath);
        var sections = ParseSections(text);
        return sections
            .Where(section => IsLikelyVpnEntry(section.Values))
            .Select(section => ToProfile(section, managedPath, sourcePath))
            .ToList();
    }

    private static string ReadPhonebookText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(bytes);
        }
    }

    private static bool IsLikelyVpnEntry(Dictionary<string, string> values)
    {
        if (values.ContainsKey("VpnStrategy")) return true;
        if (values.TryGetValue("MEDIA", out var media) && media.Contains("vpn", StringComparison.OrdinalIgnoreCase)) return true;
        if (values.TryGetValue("Device", out var device) && device.Contains("vpn", StringComparison.OrdinalIgnoreCase)) return true;
        if (values.TryGetValue("Type", out var type) && type is "2" or "5") return true;
        if (values.ContainsKey("CustomDialDll")) return true;
        return false;
    }

    private static Profile ToProfile(PbkSection section, string managedPath, string sourcePath)
    {
        var values = section.Values;
        values.TryGetValue("PhoneNumber", out var phoneNumber);
        values.TryGetValue("Device", out var device);
        values.TryGetValue("VpnStrategy", out var vpnStrategy);
        values.TryGetValue("MEDIA", out var media);
        values.TryGetValue("Type", out var type);
        values.TryGetValue("UserName", out var userName);

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["entry_name"] = section.Name,
            ["phonebook_path"] = managedPath,
            ["source_phonebook_path"] = sourcePath
        };
        if (!string.IsNullOrWhiteSpace(phoneNumber)) query["phone_number"] = phoneNumber;
        if (!string.IsNullOrWhiteSpace(device)) query["device"] = device;
        if (!string.IsNullOrWhiteSpace(vpnStrategy)) query["vpn_strategy"] = vpnStrategy;
        if (!string.IsNullOrWhiteSpace(media)) query["media"] = media;
        if (!string.IsNullOrWhiteSpace(type)) query["type"] = type;

        return new Profile
        {
            Name = section.Name,
            Protocol = ProfileKinds.Ras,
            Host = section.Name,
            Port = 0,
            RawConfig = section.RawText,
            Username = string.IsNullOrWhiteSpace(userName) ? null : userName,
            Query = query
        };
    }

    private static List<PbkSection> ParseSections(string text)
    {
        var result = new List<PbkSection>();
        string? currentName = null;
        var currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentRaw = new StringBuilder();

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(currentName)) return;
            result.Add(new PbkSection(currentName, new Dictionary<string, string>(currentValues, StringComparer.OrdinalIgnoreCase), currentRaw.ToString().TrimEnd()));
        }

        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
            {
                Flush();
                currentName = trimmed[1..^1].Trim();
                currentValues.Clear();
                currentRaw.Clear();
                currentRaw.AppendLine(line);
                continue;
            }

            if (currentName is null) continue;
            currentRaw.AppendLine(line);
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key)) currentValues[key] = value;
        }

        Flush();
        return result;
    }

    private sealed record PbkSection(string Name, Dictionary<string, string> Values, string RawText);
}

internal sealed class RasDialManager
{
    public async Task ConnectAsync(Profile profile, Action<string> onLog, CancellationToken cancellationToken = default)
    {
        if (!profile.IsRasProfile()) throw new InvalidOperationException("Profile is not a Windows RAS profile");
        var entryName = EntryName(profile);
        var phonebookPath = PhonebookPath(profile);
        var arguments = new StringBuilder();
        arguments.Append(Quote(entryName));
        if (!string.IsNullOrWhiteSpace(profile.Username))
        {
            arguments.Append(' ').Append(Quote(profile.Username));
            if (!string.IsNullOrWhiteSpace(profile.Password)) arguments.Append(' ').Append(Quote(profile.Password));
        }
        arguments.Append(" /phonebook:").Append(Quote(phonebookPath));

        onLog($"Connecting Windows RAS VPN: {entryName}");
        var result = await RunRasDialAsync(arguments.ToString(), TimeSpan.FromSeconds(70), cancellationToken);
        LogOutput(result, onLog);
        if (result.ExitCode != 0) throw new InvalidOperationException($"rasdial failed with code {result.ExitCode}: {LastLine(result)}");
    }

    public async Task DisconnectAsync(Profile profile, Action<string> onLog, CancellationToken cancellationToken = default)
    {
        if (!profile.IsRasProfile()) return;
        var entryName = EntryName(profile);
        var phonebookPath = PhonebookPath(profile);
        var arguments = $"{Quote(entryName)} /disconnect /phonebook:{Quote(phonebookPath)}";
        onLog($"Disconnecting Windows RAS VPN: {entryName}");
        var result = await RunRasDialAsync(arguments, TimeSpan.FromSeconds(30), cancellationToken);
        LogOutput(result, onLog);
        if (result.ExitCode != 0)
        {
            onLog($"rasdial disconnect returned code {result.ExitCode}: {LastLine(result)}");
        }
    }

    private static string EntryName(Profile profile) => profile.Query.GetValueOrDefault("entry_name", profile.Host);

    private static string PhonebookPath(Profile profile)
    {
        var path = profile.Query.GetValueOrDefault("phonebook_path", "");
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("PBK phonebook path is missing");
        if (!File.Exists(path)) throw new FileNotFoundException("Managed PBK phonebook file is missing", path);
        return path;
    }

    private static async Task<RasDialResult> RunRasDialAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "rasdial.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        if (!process.Start()) throw new InvalidOperationException("Unable to start rasdial.exe");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"rasdial.exe timed out after {timeout.TotalSeconds:0} seconds");
        }
        return new RasDialResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static void LogOutput(RasDialResult result, Action<string> onLog)
    {
        foreach (var line in (result.StandardOutput + Environment.NewLine + result.StandardError)
                 .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            onLog(line);
        }
    }

    private static string LastLine(RasDialResult result)
    {
        return (result.StandardError + Environment.NewLine + result.StandardOutput)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? "unknown error";
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private sealed record RasDialResult(int ExitCode, string StandardOutput, string StandardError);
}
