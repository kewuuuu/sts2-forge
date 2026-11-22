using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Sts2Forge.Config;

namespace Sts2Forge.Net;

public static class ForgeConfigSync
{
	private const int MaxConfigRequestsPerRun = 5;

	private static INetGameService? _attachedService;

	private static RunState? _runState;

	private static bool _loggedMessageIds;

	private static bool _adoptedHostConfig;

	private static bool _configRequestedThisFloor;

	private static int _configRequestFloor = -1;

	private static int _configRequestsThisRun;

	private static bool _loggedRequestCapReached;

	public static void AttachForRun(RunState state)
	{
		_runState = state;
		_adoptedHostConfig = false;
		_configRequestedThisFloor = false;
		_configRequestFloor = -1;
		_configRequestsThisRun = 0;
		_loggedRequestCapReached = false;
		try
		{
			INetGameService? service = RunManager.Instance.NetService;
			NetGameType type = service?.Type ?? NetGameType.None;
			LogMessageIdsOnce();
			if (service == null || (type != NetGameType.Host && type != NetGameType.Client))
			{
				Log.Info(Sts2ForgeMod.LogPrefix + "single-player run; local config applies");
				return;
			}
			if (!ReferenceEquals(_attachedService, service))
			{
				_attachedService = service;
				if (type == NetGameType.Host)
				{
					service.RegisterMessageHandler<ForgeConfigRequestMessage>(OnConfigRequested);
				}
				else
				{
					service.RegisterMessageHandler<ForgeConfigMessage>(OnConfigReceived);
				}
			}
			if (!service.IsConnected)
			{
				Log.Warn(Sts2ForgeMod.LogPrefix + "net service not connected; config sync deferred");
				return;
			}
			if (type == NetGameType.Host)
			{
				Broadcast(service);
			}
			else
			{
				service.SendMessage(new ForgeConfigRequestMessage());
			}
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "config sync attach failed: " + e.Message);
		}
	}

	public static void RequestConfigIfMissing()
	{
		try
		{
			if (_adoptedHostConfig || !ForgeRuntimeConfig.IsMultiplayerClient)
			{
				return;
			}
			INetGameService? service = RunManager.Instance.NetService;
			if (service == null || !service.IsConnected)
			{
				return;
			}
			int floor = _runState?.TotalFloor ?? -1;
			if (_configRequestedThisFloor && _configRequestFloor == floor)
			{
				return;
			}
			if (_configRequestsThisRun >= MaxConfigRequestsPerRun)
			{
				if (!_loggedRequestCapReached)
				{
					_loggedRequestCapReached = true;
					Log.Warn(Sts2ForgeMod.LogPrefix + "config request cap reached (" + MaxConfigRequestsPerRun + " per run); using local config");
				}
				return;
			}
			_configRequestedThisFloor = true;
			_configRequestFloor = floor;
			_configRequestsThisRun++;
			service.SendMessage(new ForgeConfigRequestMessage());
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "config request failed: " + e.Message);
		}
	}

	private static void OnConfigRequested(ForgeConfigRequestMessage message, ulong senderId)
	{
		try
		{
			INetGameService? service = RunManager.Instance.NetService;
			if (service == null)
			{
				return;
			}
			Broadcast(service);
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "config broadcast failed: " + e.Message);
		}
	}

	private static void OnConfigReceived(ForgeConfigMessage message, ulong senderId)
	{
		try
		{
			if (!ForgeRuntimeConfig.TryAdoptFromHost(message.ToConfig(), senderId))
			{
				return;
			}
			_adoptedHostConfig = true;
			ForgeConfig adopted = ForgeRuntimeConfig.Effective;
			ForgeRuntimeConfig.BeginRun(adopted);
			Log.Info(Sts2ForgeMod.LogPrefix + "host config adopted mid-run (sender=" + senderId + "): " + adopted.Fingerprint());
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "config adopt failed: " + e.Message);
		}
	}

	private static void Broadcast(INetGameService service)
	{
		ForgeConfigMessage message = ForgeConfigMessage.From(ForgeRuntimeConfig.ForThisRun);
		service.SendMessage(message);
		Log.Info(Sts2ForgeMod.LogPrefix + "broadcast config: " + ForgeRuntimeConfig.ForThisRun.Fingerprint());
	}

	private static void LogMessageIdsOnce()
	{
		if (_loggedMessageIds)
		{
			return;
		}
		_loggedMessageIds = true;
		try
		{
			int configId = MegaCrit.Sts2.Core.Multiplayer.Serialization.MessageTypes.TypeToId<ForgeConfigMessage>();
			int requestId = MegaCrit.Sts2.Core.Multiplayer.Serialization.MessageTypes.TypeToId<ForgeConfigRequestMessage>();
			Log.Info(Sts2ForgeMod.LogPrefix + "net message ids: config=" + configId + ", request=" + requestId);
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "net message type not registered: " + e.Message);
		}
	}
}
