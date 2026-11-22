using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Forge.Art;

/// <summary>Textures embedded in the assembly (see the EmbeddedResource entries in sts2-forge.csproj).</summary>
public static class ForgeArt
{
	public const string RestSiteIconResource = "Sts2Forge.Art.forge_rest_site.png";

	public const string RestSiteIconBigResource = "Sts2Forge.Art.forge_rest_site_big.png";

	private static readonly object Gate = new object();

	private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>(StringComparer.Ordinal);

	private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);

	public static Texture2D? RestSiteIcon
	{
		get
		{
			return Get(RestSiteIconResource);
		}
	}

	public static Texture2D? RestSiteIconBig
	{
		get
		{
			return Get(RestSiteIconBigResource);
		}
	}

	public static Texture2D? Get(string resourceName)
	{
		lock (Gate)
		{
			if (Cache.TryGetValue(resourceName, out Texture2D? cached))
			{
				return cached;
			}
			Texture2D? texture = Decode(resourceName);
			Cache[resourceName] = texture;
			return texture;
		}
	}

	private static Texture2D? Decode(string resourceName)
	{
		try
		{
			Assembly assembly = typeof(ForgeArt).Assembly;
			string? resolved = Resolve(assembly, resourceName);
			if (resolved == null)
			{
				Report(resourceName, "embedded resource not found");
				return null;
			}
			using Stream? stream = assembly.GetManifestResourceStream(resolved);
			if (stream == null)
			{
				Report(resourceName, "embedded resource stream is null");
				return null;
			}
			using MemoryStream buffer = new MemoryStream();
			stream.CopyTo(buffer);
			byte[] png = buffer.ToArray();
			using Image image = new Image();
			Error error = image.LoadPngFromBuffer(png);
			if (error != Error.Ok || image.GetWidth() <= 0 || image.GetHeight() <= 0)
			{
				Report(resourceName, "png decode failed: " + error);
				return null;
			}
			return ImageTexture.CreateFromImage(image);
		}
		catch (Exception e)
		{
			Report(resourceName, e.GetType().Name + ": " + e.Message);
			return null;
		}
	}

	private static string? Resolve(Assembly assembly, string resourceName)
	{
		string[] names = assembly.GetManifestResourceNames();
		foreach (string name in names)
		{
			if (string.Equals(name, resourceName, StringComparison.Ordinal))
			{
				return name;
			}
		}
		foreach (string name in names)
		{
			if (name.EndsWith("." + resourceName, StringComparison.Ordinal))
			{
				return name;
			}
		}
		return null;
	}

	private static void Report(string resourceName, string reason)
	{
		lock (Gate)
		{
			if (!Reported.Add(resourceName))
			{
				return;
			}
		}
		Log.Error(Sts2ForgeMod.LogPrefix + "texture '" + resourceName + "' unavailable: " + reason);
	}
}
