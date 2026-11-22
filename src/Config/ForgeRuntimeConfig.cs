using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Forge.Config;

public static class ForgeRuntimeConfig
{
	private static ForgeConfig? _hostOverride;

	private static ForgeConfig? _runSnapshot;

	private static bool _loggedClientWrite;

	public static ForgeConfig Effective => _hostOverride ?? ForgeConfigStore.Current;

	public static ForgeConfig ForThisRun => _runSnapshot ?? Effective;

	public static bool IsMultiplayerClient
	{
		get
		{
			INetGameService? service = RunManager.Instance.NetService;
			return service != null && service.Type == NetGameType.Client;
		}
	}

	public static void BeginRun(ForgeConfig config)
	{
		_runSnapshot = config.Normalized();
		Log.Info(Sts2ForgeMod.LogPrefix + "run settings frozen: " + _runSnapshot.Fingerprint());
	}

	public static void EndRun()
	{
		_runSnapshot = null;
	}

	public static void ClearOverride()
	{
		_hostOverride = null;
	}

	public static bool TryAdoptFromHost(ForgeConfig config, ulong senderId)
	{
		if (!IsMultiplayerClient)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "ignored host config from " + senderId + ": this peer is not a multiplayer client");
			return false;
		}
		_hostOverride = config.Normalized();
		Log.Info(Sts2ForgeMod.LogPrefix + "adopted host config from " + senderId + ": " + _hostOverride.Fingerprint());
		return true;
	}

	public static bool TryWriteLocal(ForgeConfig config)
	{
		if (IsMultiplayerClient)
		{
			if (!_loggedClientWrite)
			{
				_loggedClientWrite = true;
				Log.Warn(Sts2ForgeMod.LogPrefix + "settings are host-controlled in multiplayer; local writes ignored");
			}
			return false;
		}
		ForgeConfigStore.Save(config);
		return true;
	}
}
