using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using Sts2Forge.Core;

namespace Sts2Forge.UI;

/// <summary>
/// Modal feedback for the forge flow. A null answer means "no UI was available" and the caller must
/// carry on without waiting, so a missing or busy modal layer can never stall the rest site.
/// </summary>
internal static class ForgePopup
{
	internal static async Task<bool?> Ask(LocString body, LocString header, LocString? noButton, LocString yesButton)
	{
		try
		{
			// Format first: a placeholder that no variable satisfies throws inside the popup's own
			// SetText, which would leave an unclickable modal on screen.
			string headerText = header.GetFormattedText();
			string bodyText = body.GetFormattedText();
			string buttonText = yesButton.GetFormattedText();
			if (string.IsNullOrEmpty(bodyText) || string.IsNullOrEmpty(buttonText))
			{
				Log.Warn(Sts2ForgeMod.LogPrefix + "popup skipped: empty text (" + headerText + ")");
				return null;
			}
			NModalContainer? container = NModalContainer.Instance;
			if (container == null)
			{
				return null;
			}
			if (container.OpenModal != null)
			{
				Log.Info(Sts2ForgeMod.LogPrefix + "popup skipped: another modal is already open");
				return null;
			}
			NGenericPopup? popup = NGenericPopup.Create();
			if (popup == null)
			{
				return null;
			}
			container.Add(popup);
			return await popup.WaitForConfirmation(body, header, noButton, yesButton);
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "popup failed: " + e.Message);
			return null;
		}
	}

	/// <summary>Single-button notice: the player only acknowledges the result.</summary>
	internal static Task<bool?> Tell(LocString body, LocString header)
	{
		return Ask(body, header, null, new LocString(ForgeLevels.LocTable, "FORGE.RESULT_OK"));
	}
}
