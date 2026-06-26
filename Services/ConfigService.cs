using System.IO;
using System.Text.Json;
using BlackLaunch.Models;

namespace BlackLaunch.Services;

public class ConfigService(string configFile)
{
    private readonly string _configFile = configFile;

    public Config Load()
    {
        try {
            if (File.Exists(_configFile)) {
                var json = File.ReadAllText(_configFile);
                return JsonSerializer.Deserialize(json, ConfigContext.Default.Config) ?? new Config();
            }
        } catch {}
        return new Config();
    }

    public void Save(Config config)
    {
        try {
            var json = JsonSerializer.Serialize(config, ConfigContext.Default.Config);
            File.WriteAllText(_configFile, json);
        } catch {}
    }
}
