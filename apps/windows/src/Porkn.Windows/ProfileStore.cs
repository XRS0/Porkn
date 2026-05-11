using System.Text.Json;

namespace Porkn.Windows;

internal sealed class ProfileStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public List<Profile> Profiles { get; private set; } = [];

    public ProfileStore()
    {
        _filePath = Path.Combine(AppPaths.DataDirectory, "profiles.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            Profiles = [];
            return;
        }

        var json = File.ReadAllText(_filePath);
        Profiles = JsonSerializer.Deserialize<List<Profile>>(json, _jsonOptions) ?? [];
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(Profiles, _jsonOptions));
    }

    public void Upsert(IEnumerable<Profile> incoming)
    {
        foreach (var profile in incoming)
        {
            var existing = Profiles.FirstOrDefault(p => StableKey(p) == StableKey(profile));
            if (existing is null)
            {
                Profiles.Add(profile);
                continue;
            }

            profile.Id = existing.Id;
            profile.CreatedAt = existing.CreatedAt;
            var index = Profiles.IndexOf(existing);
            Profiles[index] = profile;
        }

        Save();
    }

    public void Delete(Profile profile)
    {
        Profiles.RemoveAll(p => p.Id == profile.Id);
        Save();
    }

    private static string StableKey(Profile profile)
    {
        return string.Join('|', profile.Protocol.ToLowerInvariant(), profile.Username?.ToLowerInvariant() ?? "", profile.Host.ToLowerInvariant(), profile.Port);
    }
}
