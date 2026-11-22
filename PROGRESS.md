# 进度

## 2026-11-22 立项

- 项目与骨架建立，规格写入 docs/design.md。

## 2026-11-22 v0.1.0-beta 落地

- 火堆选项、五档等级效果、四键配置与联机同步、图标、本地化（自建 forge_ui 表）。
- 无头验收：12/12 补丁、exit=0、零本模组 [ERROR]。修掉 `ModelDb.Inject(ForgeCombatListener)` 造成的启动崩溃（AbstractModel 构造函数自己会注册，ModelDb.Init 再构造一次就 DuplicateModelException），改为 `ModHelper.SubscribeForCombatStateHooks` 惰性解析。

## 2026-11-22 玩家报障后的修复（垫子跳过 + 结果可见）

玩家日志两次都是 `forge fail (pad consumed): card=STRIKE_IRONCLAD level 0 -> 0 p=50% ... pad=INJURY`：等级推进逻辑本身没错，是 50% 掷骰失败，但玩家看不到任何结果，所以读成“选了没锻造、只是删牌”。

- **垫子步真正可跳过**：`CardSelectorPrefs(prompt, 0, 1)`。原版 `NDeckCardSelectScreen.RefreshConfirmButtonVisibility` 只在 min != max 时启用确认按钮，而 (1,1) 让确认永远灰、只剩返回箭头 ⇒ 玩家不知道那就是跳过。
- **结果可见**：新增 `src/UI/ForgePopup.cs`（`NGenericPopup.WaitForConfirmation` 单按钮告知框），成功/失败/材料不足三种结果各弹一次；只在 `LocalContext.IsMe(owner)` 的那一端弹，联机对端不重复弹；弹不出（模态被占、TestMode、文案格式化失败）只记一行日志，绝不阻塞火堆。
- **0 级不再要求垫子**：0 级不会掉级，垫子纯属白烧一张牌 ⇒ 跳过该步。
- **满 5 级的牌不再可锻铸**：`ForgeLevels.IsForgeable` 排除 `GetLevel(card) >= MaxLevel`（成功白扣 (level+1) 张材料，失败还会掉级）。
- **选项不再变成死按钮**：`IsEnabled` 先判 `OneForgePerRest && HasForged`，本层已锻过就置灰；catch 分支改为 false。
- **install.ps1 路径 bug**：`Split-Path -Parent $PSScriptRoot` 指到上层目录，报 nothing staged in D:\engine\DSH\projects\dist\sts2-forge。
- 文案：主选牌提示带 `{percent}` 与“失败不退材料”；中文 `FORGE.TIP_LEVEL` 错字“缎铸”改成“锻铸”。
