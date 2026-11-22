using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using Sts2Forge.Config;

namespace Sts2Forge.Net;

/// <summary>
/// Host to clients. Message types are discovered by name and assigned ids at startup,
/// so the class name must never change once released.
/// </summary>
public class ForgeConfigMessage : INetMessage
{
	public int SuccessPercent { get; set; } = ForgeConfig.SuccessPercentDefault;

	public bool ConsumeRestAction { get; set; } = true;

	public bool OneForgePerRest { get; set; } = true;

	public bool LogDebug { get; set; }

	public bool ShouldBroadcast => true;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Info;

	public bool ShouldBuffer => true;

	public static ForgeConfigMessage From(ForgeConfig config)
	{
		ForgeConfig normalized = config.Normalized();
		return new ForgeConfigMessage
		{
			SuccessPercent = normalized.SuccessPercent,
			ConsumeRestAction = normalized.ConsumeRestAction,
			OneForgePerRest = normalized.OneForgePerRest,
			LogDebug = normalized.LogDebug
		};
	}

	public ForgeConfig ToConfig()
	{
		return new ForgeConfig
		{
			SuccessPercent = SuccessPercent,
			ConsumeRestAction = ConsumeRestAction,
			OneForgePerRest = OneForgePerRest,
			LogDebug = LogDebug
		};
	}

	public void Serialize(PacketWriter writer)
	{
		writer.WriteInt(SuccessPercent);
		writer.WriteBool(ConsumeRestAction);
		writer.WriteBool(OneForgePerRest);
		writer.WriteBool(LogDebug);
	}

	public void Deserialize(PacketReader reader)
	{
		SuccessPercent = reader.ReadInt();
		ConsumeRestAction = reader.ReadBool();
		OneForgePerRest = reader.ReadBool();
		LogDebug = reader.ReadBool();
	}
}

/// <summary>Client to host: ask for the authoritative config.</summary>
public class ForgeConfigRequestMessage : INetMessage
{
	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.VeryDebug;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
	}

	public void Deserialize(PacketReader reader)
	{
	}
}
