using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Forge.Core;

/// <summary>Per-rest-site attempt bookkeeping. In-memory only: it is never written to the save file.</summary>
public static class ForgeAttempts
{
	private static readonly Dictionary<(ulong NetId, int Floor), int> Attempts = new Dictionary<(ulong NetId, int Floor), int>();

	public static int Count(ulong netId, int floor)
	{
		return Attempts.TryGetValue((netId, floor), out int value) ? value : 0;
	}

	public static bool HasForged(ulong netId, int floor)
	{
		return Count(netId, floor) > 0;
	}

	public static int Register(ulong netId, int floor)
	{
		int next = Count(netId, floor) + 1;
		Attempts[(netId, floor)] = next;
		return next;
	}

	public static void Reset()
	{
		if (Attempts.Count > 0)
		{
			Log.Info(Sts2ForgeMod.LogPrefix + "forget " + Attempts.Count + " rest site forge counter(s)");
		}
		Attempts.Clear();
	}
}
