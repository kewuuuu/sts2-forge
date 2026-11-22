using System;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Forge.Config;

public sealed class ForgeConfig
{
	public const int SuccessPercentMin = 0;
	public const int SuccessPercentMax = 100;
	public const int SuccessPercentDefault = 50;

	public int SuccessPercent { get; set; } = SuccessPercentDefault;

	public bool ConsumeRestAction { get; set; } = true;

	public bool OneForgePerRest { get; set; } = true;

	public bool LogDebug { get; set; }

	public static ForgeConfig Default => new ForgeConfig();

	public ForgeConfig Clone()
	{
		return new ForgeConfig
		{
			SuccessPercent = SuccessPercent,
			ConsumeRestAction = ConsumeRestAction,
			OneForgePerRest = OneForgePerRest,
			LogDebug = LogDebug
		};
	}

	public ForgeConfig Normalized()
	{
		int percent = Math.Clamp(SuccessPercent, SuccessPercentMin, SuccessPercentMax);
		if (percent != SuccessPercent)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "success_percent " + SuccessPercent + " out of range [" + SuccessPercentMin + ", " + SuccessPercentMax + "], clamped to " + percent);
		}
		return new ForgeConfig
		{
			SuccessPercent = percent,
			ConsumeRestAction = ConsumeRestAction,
			OneForgePerRest = OneForgePerRest,
			LogDebug = LogDebug
		};
	}

	public string Fingerprint()
	{
		string body = "success_percent=" + SuccessPercent
			+ " consume_rest_action=" + Sts2ForgeMod.BoolText(ConsumeRestAction)
			+ " one_forge_per_rest=" + Sts2ForgeMod.BoolText(OneForgePerRest)
			+ " log_debug=" + Sts2ForgeMod.BoolText(LogDebug);
		uint hash = 2166136261u;
		foreach (char c in body)
		{
			hash ^= c;
			hash *= 16777619u;
		}
		return body + " h=" + hash.ToString("x8");
	}

	public static ForgeConfig Parse(string? json)
	{
		ForgeConfig config = Default;
		if (string.IsNullOrWhiteSpace(json))
		{
			return config;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip
			});
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				Log.Warn(Sts2ForgeMod.LogPrefix + "config root is not a JSON object, using defaults");
				return config;
			}
			foreach (JsonProperty property in root.EnumerateObject())
			{
				switch (property.Name.ToLowerInvariant())
				{
					case "success_percent":
						if (TryInt(property.Value, out int percent))
						{
							config.SuccessPercent = percent;
						}
						break;
					case "consume_rest_action":
						if (TryBool(property.Value, out bool consume))
						{
							config.ConsumeRestAction = consume;
						}
						break;
					case "one_forge_per_rest":
						if (TryBool(property.Value, out bool once))
						{
							config.OneForgePerRest = once;
						}
						break;
					case "log_debug":
						if (TryBool(property.Value, out bool debug))
						{
							config.LogDebug = debug;
						}
						break;
					default:
						Log.Warn(Sts2ForgeMod.LogPrefix + "config key '" + property.Name + "' is unknown, ignored");
						break;
				}
			}
		}
		catch (JsonException e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "config JSON is invalid, using defaults: " + e.Message);
			return Default;
		}
		return config.Normalized();
	}

	private static bool TryInt(JsonElement element, out int value)
	{
		if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
		{
			return true;
		}
		Log.Warn(Sts2ForgeMod.LogPrefix + "config value is not an integer, using default");
		value = 0;
		return false;
	}

	private static bool TryBool(JsonElement element, out bool value)
	{
		if (element.ValueKind == JsonValueKind.True)
		{
			value = true;
			return true;
		}
		if (element.ValueKind == JsonValueKind.False)
		{
			value = false;
			return true;
		}
		Log.Warn(Sts2ForgeMod.LogPrefix + "config value is not a boolean, using default");
		value = false;
		return false;
	}
}
