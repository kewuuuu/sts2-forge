#if RITSULIB
using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
using Sts2Forge.Config;
using Sts2Forge.Core;

namespace Sts2Forge.Integration;

/// <summary>
/// Optional RitsuLib settings page. The assembly check keeps the mod working when RitsuLib's UI module
/// is not loaded at runtime even though the reference assembly was present at build time.
/// </summary>
internal static class RitsuLibSettingsPage
{
	private const string SettingsAssemblyName = "STS2-RitsuLib.Settings";

	private const string PageId = "sts2-forge-settings";

	private const string SectionId = "forge";

	public static void TryRegister()
	{
		if (!IsSettingsAssemblyLoaded())
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "RitsuLib settings assembly not loaded; settings page unavailable");
			return;
		}
		try
		{
			Register();
			Log.Info(Sts2ForgeMod.LogPrefix + "RitsuLib settings page registered: mod_id=" + Sts2ForgeMod.ModId + ", page_id=" + PageId);
		}
		catch (Exception e)
		{
			Log.Warn(Sts2ForgeMod.LogPrefix + "RitsuLib settings page not registered: " + e.GetType().Name + ": " + e.Message);
		}
	}

	private static bool IsSettingsAssemblyLoaded()
	{
		foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (string.Equals(assembly.GetName().Name, SettingsAssemblyName, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static void Register()
	{
		ModSettingsPageBuilder builder = new ModSettingsPageBuilder(Sts2ForgeMod.ModId, PageId);
		builder.WithTitle(Text("强化", "Upgrade"));
		builder.WithDescription(Text("强化成功率与火堆行为。联机时以房主参数为准。", "Upgrade success chance and campfire behavior. Multiplayer uses the host’s settings."));
		builder.WithModDisplayName(Text("强化", "Upgrade"));
		builder.AddSection(SectionId, section =>
		{
			section.WithTitle(Text("强化设置", "Upgrade settings"));
			IModSettingsValueBinding<int> success = ModSettingsBindings.Callback<int>(
				Sts2ForgeMod.ModId,
				"success_percent",
				() => ForgeRuntimeConfig.Effective.SuccessPercent,
				value => WriteLocal(config => config.SuccessPercent = value),
				() => { },
				SaveScope.Global);
			section.AddIntSlider(
				"success_percent",
				Text("强化成功率（%）", "Upgrade success chance (%)"),
				success,
				ForgeConfig.SuccessPercentMin,
				ForgeConfig.SuccessPercentMax,
				1,
				value => value + "%",
				Text("0 表示必定失败，100 表示必定成功。", "0 always fails and 100 always succeeds."));
			section.AddParagraph(
				"expected_materials",
				ModSettingsText.Dynamic(BuildExpectedTable, new IModSettingsBinding[] { success }),
				Text("按当前成功率推算达到各等级平均需要消耗的材料张数（不含目标牌）。", "Average number of materials needed to reach each level at the current success chance (excluding the target card)."));
			section.AddToggle(
				"consume_rest_action",
				Text("强化消耗本次火堆行动", "Upgrade uses the rest site action"),
				ModSettingsBindings.Callback<bool>(
					Sts2ForgeMod.ModId,
					"consume_rest_action",
					() => ForgeRuntimeConfig.Effective.ConsumeRestAction,
					value => WriteLocal(config => config.ConsumeRestAction = value),
					() => { },
					SaveScope.Global),
				Text("开启后，强化至少一轮并结束时会用掉火堆行动。关闭后仍可休息或打铁。", "Finishing after at least one round uses the rest site action. When disabled, you can still rest or smith."));
			section.AddToggle(
				"one_forge_per_rest",
				Text("每处火堆只允许一次强化会话", "One upgrade session per rest site"),
				ModSettingsBindings.Callback<bool>(
					Sts2ForgeMod.ModId,
					"one_forge_per_rest",
					() => ForgeRuntimeConfig.Effective.OneForgePerRest,
					value => WriteLocal(config => config.OneForgePerRest = value),
					() => { },
					SaveScope.Global),
				Text("完成至少一轮才计入次数。同一会话内可以继续强化。", "Only sessions with at least one completed round count. You can upgrade multiple times in a session."));
			section.AddToggle(
				"log_debug",
				Text("输出调试日志", "Write debug logs"),
				ModSettingsBindings.Callback<bool>(
					Sts2ForgeMod.ModId,
					"log_debug",
					() => ForgeRuntimeConfig.Effective.LogDebug,
					value => WriteLocal(config => config.LogDebug = value),
					() => { },
					SaveScope.Global),
				Text("记录每轮强化的随机种子。", "Log random seed details for each upgrade attempt."));
		});
		ModSettingsPage page = builder.Build();
		ModSettingsRegistry.Register(page);
	}

	private static string Translate(string chinese, string english) =>
		LocManager.Instance?.CultureInfo.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ? chinese : english;

	private static ModSettingsText Text(string chinese, string english) =>
		ModSettingsText.DynamicFullRefreshOnly(() => Translate(chinese, english));

	private static string BuildExpectedTable()
	{
		ForgeConfig config = ForgeRuntimeConfig.Effective;
		double probability = config.SuccessPercent / 100.0;
		StringBuilder builder = new StringBuilder();
		builder.Append(Translate("当前成功率 ", "Current success chance ")).Append(config.SuccessPercent).Append('%');
		if (config.SuccessPercent <= 0)
		{
			builder.Append(Translate("（必定失败：期望材料为无穷大）", " (always fails: expected materials are infinite)"));
		}
		builder.Append('\n');
		builder.Append(Translate("期望材料：", "Expected materials: "));
		AppendLevels(builder, probability);
		if (ForgeRuntimeConfig.IsMultiplayerClient)
		{
			builder.Append('\n').Append(Translate("联机中：本页只读，数值以房主为准。", "Multiplayer: these settings are read-only and come from the host."));
		}
		return builder.ToString();
	}

	private static void AppendLevels(StringBuilder builder, double probability)
	{
		for (int level = 1; level <= ForgeLevels.MaxLevel; level++)
		{
			if (level > 1)
			{
				builder.Append(" / ");
			}
			builder.Append('+').Append(level).Append(' ').Append(ForgeLevels.FormatExpected(level, probability));
		}
	}

	private static void WriteLocal(Action<ForgeConfig> mutate)
	{
		ForgeConfig draft = ForgeRuntimeConfig.Effective.Clone();
		mutate(draft);
		ForgeRuntimeConfig.TryWriteLocal(draft);
	}
}
#endif
