#if RITSULIB
using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Logging;
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
		builder.WithTitle(ModSettingsText.Literal("火堆锻铸 / Forge"));
		builder.WithDescription(ModSettingsText.Literal("锻铸成功率与火堆行为；联机时以房主参数为准。"));
		builder.WithModDisplayName(ModSettingsText.Literal("火堆锻铸 / Forge"));
		builder.AddSection(SectionId, section =>
		{
			section.WithTitle(ModSettingsText.Literal("锻铸参数"));
			IModSettingsValueBinding<int> success = ModSettingsBindings.Callback<int>(
				Sts2ForgeMod.ModId,
				"success_percent",
				() => ForgeRuntimeConfig.Effective.SuccessPercent,
				value => WriteLocal(config => config.SuccessPercent = value),
				() => { },
				SaveScope.Global);
			section.AddIntSlider(
				"success_percent",
				ModSettingsText.Literal("锻铸成功率（%）"),
				success,
				ForgeConfig.SuccessPercentMin,
				ForgeConfig.SuccessPercentMax,
				1,
				value => value + "%",
				ModSettingsText.Literal("0 表示必定失败，100 表示必定成功。"));
			section.AddParagraph(
				"expected_materials",
				ModSettingsText.Dynamic(BuildExpectedTable, new IModSettingsBinding[] { success }),
				ModSettingsText.Literal("按当前成功率推算的期望材料张数（向上累积到目标等级，不含主卡）。"));
			section.AddToggle(
				"consume_rest_action",
				ModSettingsText.Literal("锻铸消耗本次休息点"),
				ModSettingsBindings.Callback<bool>(
					Sts2ForgeMod.ModId,
					"consume_rest_action",
					() => ForgeRuntimeConfig.Effective.ConsumeRestAction,
					value => WriteLocal(config => config.ConsumeRestAction = value),
					() => { },
					SaveScope.Global),
				ModSettingsText.Literal("开启时，至少锻铸一轮后结束会话会用掉火堆行动。关闭时可继续治疗或打铁。"));
			section.AddToggle(
				"one_forge_per_rest",
				ModSettingsText.Literal("每处火堆只允许一次锻铸会话"),
				ModSettingsBindings.Callback<bool>(
					Sts2ForgeMod.ModId,
					"one_forge_per_rest",
					() => ForgeRuntimeConfig.Effective.OneForgePerRest,
					value => WriteLocal(config => config.OneForgePerRest = value),
					() => { },
					SaveScope.Global),
				ModSettingsText.Literal("完成至少一轮才计入次数。同一会话内可以继续锻铸。"));
			section.AddToggle(
				"log_debug",
				ModSettingsText.Literal("输出调试日志"),
				ModSettingsBindings.Callback<bool>(
					Sts2ForgeMod.ModId,
					"log_debug",
					() => ForgeRuntimeConfig.Effective.LogDebug,
					value => WriteLocal(config => config.LogDebug = value),
					() => { },
					SaveScope.Global),
				ModSettingsText.Literal("把每次锻铸的随机种子细节写进日志。"));
		});
		ModSettingsPage page = builder.Build();
		ModSettingsRegistry.Register(page);
	}

	private static string BuildExpectedTable()
	{
		ForgeConfig config = ForgeRuntimeConfig.Effective;
		double probability = config.SuccessPercent / 100.0;
		StringBuilder builder = new StringBuilder();
		builder.Append("当前成功率 ").Append(config.SuccessPercent).Append('%');
		if (config.SuccessPercent <= 0)
		{
			builder.Append("（必定失败：期望材料为无穷大）");
		}
		builder.Append('\n');
		builder.Append("期望材料：");
		AppendLevels(builder, probability);
		if (ForgeRuntimeConfig.IsMultiplayerClient)
		{
			builder.Append('\n').Append("联机中：本页只读，数值以房主为准。");
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
