using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Forge.Config;

public static class ForgeConfigStore
{
	public const string FileName = "sts2-forge.cfg";

	private static ForgeConfig _current = ForgeConfig.Default;

	public static ForgeConfig Current => _current;

	public static ForgeConfig Effective => _current.Normalized();

	public static string ResolveConfigPath()
	{
		string executableDirectory = Path.GetDirectoryName(OS.GetExecutablePath()) ?? string.Empty;
		return Path.Combine(executableDirectory, "mods", "config", FileName);
	}

	public static void Load()
	{
		try
		{
			string path = ResolveConfigPath();
			if (!File.Exists(path))
			{
				_current = ForgeConfig.Default;
				Log.Info(Sts2ForgeMod.LogPrefix + "no config file found, using defaults (" + path + ")");
				return;
			}
			_current = ForgeConfig.Parse(File.ReadAllText(path));
			Log.Info(Sts2ForgeMod.LogPrefix + "config loaded from " + path + ": " + _current.Fingerprint());
		}
		catch (Exception e)
		{
			_current = ForgeConfig.Default;
			Log.Warn(Sts2ForgeMod.LogPrefix + "config load failed, using defaults: " + e.Message);
		}
	}

	public static void Save(ForgeConfig config)
	{
		try
		{
			ForgeConfig normalized = config.Normalized();
			string path = ResolveConfigPath();
			string? directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}
			using MemoryStream stream = new MemoryStream();
			using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
			{
				writer.WriteStartObject();
				writer.WriteNumber("success_percent", normalized.SuccessPercent);
				writer.WriteBoolean("consume_rest_action", normalized.ConsumeRestAction);
				writer.WriteBoolean("one_forge_per_rest", normalized.OneForgePerRest);
				writer.WriteBoolean("log_debug", normalized.LogDebug);
				writer.WriteEndObject();
			}
			File.WriteAllText(path, Encoding.UTF8.GetString(stream.ToArray()));
			_current = normalized;
			Log.Info(Sts2ForgeMod.LogPrefix + "config saved to " + path + ": " + normalized.Fingerprint());
		}
		catch (Exception e)
		{
			Log.Error(Sts2ForgeMod.LogPrefix + "config save failed: " + e);
		}
	}
}
