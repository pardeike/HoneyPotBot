using System.Text.Json;

namespace HoneyPotBot;

public static class Config
{
	private static Dictionary<string, string>? _config;

	private static Dictionary<string, string> LoadConfig()
	{
		var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var configPath = Path.Combine(homeDir, ".api-keys");

		if (!File.Exists(configPath))
			throw new FileNotFoundException($"Configuration file not found at {configPath}");

		var json = File.ReadAllText(configPath);
		var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

		return config ?? throw new InvalidOperationException($"Failed to parse configuration file at {configPath}");
	}

	public static string? Get(string key)
	{
		_config ??= LoadConfig();
		return _config.TryGetValue(key, out var value) ? value : null;
	}
}
