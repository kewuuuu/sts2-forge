# 火堆强化 · 设计规格（v0.2.0 依据）

> 本文件是实现与验收的唯一依据。任何与代码冲突的地方以本文件为准；本文件与游戏源码冲突的地方以已核实行号为准。

## 1. 一句话

火堆（Rest Site）新增一个选项「强化」：选一张牌，付出一批**同名牌**当材料，有概率把它的**强化等级 +1**；等级越高效果越强，也越容易掉回去。

## 2. 规则

- 等级 0–5，初始 0。可反复强化（成功时 +1），上限 +5；**已满 5 级的牌不再出现在选牌列表里**（成功只会白扣材料、失败还会掉级）。
- 在独立页面手选并锁定一张目标。每轮手选 `当前等级 + 1` 张同名牌（等级不限，排除目标和不可移除牌）。
- 成功率 `p` 由设置给（默认 50%，范围 0–100）：
  - 成功 → 等级 +1。
  - 失败 → 材料不退；等级 −1（+0 时不变）。
- 每轮立即扣材料、结算、记录结果并留在页面。满级或材料不足时可以结束。退出前一轮也没做则不登记。
- 可强化的牌：攻击 / 技能 / 能力 / 诅咒。状态牌、任务牌不可（无意义且会污染牌组）。

## 3. 等级效果

| 等级 | 效果 |
|---|---|
| +1 | 每次打出时获得 3 格挡 |
| +2 | 攻击 +3 伤害；技能 +3 格挡；**能力牌打出时抽 1 张**；**诅咒牌可打出** |
| +3 | 打出时获得 1 点能量 |
| +4 | 打出时额外再打一次（与重放次数相乘：重放 +1 时变 2 次 → ×2） |
| +5 | 打出时获得 1 层「奇迹」（免伤） |

奇迹层数大于 0 时，生命不会下降。降低生命上限时最多降至当前生命。每次出牌额外消耗 1 点能量，回合最大能量增加当前层数。回合结束、弃牌前，失去「当前手牌数 + 1」层，最低为 0。

手动打出 X 费牌时先预留 1 点，剩余能量决定 X。自动打出仍免基础费，但需支付奇迹的 1 点；能量不足时按原版无法自动打出的流程移至结果牌堆。已预付的自动出牌和同一次打出的重放均不重复收费。

等级效果**叠加**：+3 同时拥有 +1/+2 的效果。

## 4. 概率与成本（期望材料数）

递推：`F_n = ((n+1) + (1-p)·F_{n-1}) / p`，`F_0 = 1/p`。

| 成功率 | +1 | +2 | +3 | +4 | +5 |
|---|---|---|---|---|---|
| 100% | 1 | 3 | 6 | 10 | 15 |
| 80% | 1.3 | 4.1 | 8.5 | 14.6 | 22.4 |
| 70% | 1.4 | 4.9 | 10.7 | 18.9 | 29.5 |
| **50%（默认）** | **2** | **8** | **20** | **40** | **70** |
| 30% | 3.3 | 17.8 | 61.5 | 176.8 | 462.5 |
| 20% | 5 | 35 | 170 | 730 | 2995 |

- `p = 0`：永远失败 —— 材料照扣、等级往回掉。页面与设置页显示成功率。
- `p = 100`：无风险，+5 累计只要 15 张。

## 5. 设置项（JSON：`<游戏目录>/mods/config/sts2-forge.cfg`，内容 JSON）

| 键 | 类型 | 默认 | 范围 | 含义 |
|---|---|---|---|---|
| `success_percent` | int | 50 | 0–100 | 强化成功率 |
| `consume_rest_action` | bool | true | — | 至少完成一轮后，结束强化是否占掉火堆行动（false = 还可治疗或打铁） |
| `one_forge_per_rest` | bool | true | — | 同一火堆是否仅允许一个完成过强化轮次的会话 |
| `log_debug` | bool | false | — | 打印强化过程日志 |

- 非法值回落默认并 `Log.Warn`。
- 参数**开局冻结**（联机以房主为准），与另外两个模组同一套：快照在 `RunStarted`（单人/房主）或采纳房主配置后建立，本局内改设置只影响下一局。

## 6. 存档与联机

- 强化等级存进 `SerializableCard.Props`（int 键 `forge_level`）。补丁点：`CardModel.ToSerializable()`（写）与 `CardModel.FromSerializable(...)`（读）。
- 关键词不进存档；因为「诅咒可打出」靠 `RemoveKeyword(Unplayable)`，**读档时必须按 forge_level 重新应用**（在 FromSerializable 补丁里做）。
- 联机：`success_percent` 等参数由房主下发（INetMessage，同前两个模组）；强化本身发生在火堆（非战斗），且结果写进 run state 的牌数据，随存档/移交同步。**不要**新造"强化按钮"的自定义网络消息。

## 7. 展示：仿词条

- 卡面：在描述行里插入一行与「消耗/虚无」同款的词条文字（例：`强化 3`），走后置补丁改描述装配结果。
- 悬浮提示：`CardModel.HoverTips` 后置追加一条，列出当前等级的全部生效效果。
- **不是** `CardKeyword`：不 `AddKeyword` 自定义值（枚举不可扩展），也不复用任何已有词条值。

## 8. 已核实的实现锚点（D:/engine/DSH/Workspace/_cache/sts2-1131-src）

- 火堆选项：`MegaCrit.Sts2.Core.Events/RestSiteOption.cs:53-74`（Generate 里 Heal/Smith/(Mend) 之后调 `Hook.ModifyRestSiteOptions`）、`:23-29`（OptionId/Title/Description，loc 表 `rest_site_ui` 键 `OPTION_<ID>.name/.description/.descriptionDisabled`）、`:40` IsEnabled、`:83` `OnSelect`、`:31-35` Icon 路径写死 `ui/rest_site/option_<optionid>.png`。
- 注入点：`MegaCrit.Sts2.Core.Hooks/Hook.cs:2156` `ModifyRestSiteOptions`（静态，可直接 postfix 追加选项），模型侧等价物 `AbstractModel.cs:2183`。
- 选牌同步沿用 `CardSelectCmd.FromDeckGeneric` 使用的 `PlayerChoiceSynchronizer`。页面自行收集玩家选择，材料按牌组顺序建立快照并过滤目标、同名及 `IsRemovable`。
- 等级存档：`CardModel.cs:2429-2440` ToSerializable、`:2447-2471` FromSerializable；`SavedProperties` 支持 `List<SavedProperty<int>>`。
- 关键词：`CardModel.cs:1464-1486` AddKeyword/RemoveKeyword；`CanPlay` 只在 `:1896` 查 `Unplayable`；费用取值处 `PlayerCombatState.cs:218` 已 `Math.Max(0, …)` ⇒ 诅咒可打出花 0 费。
- 伤害/格挡加成要写**两处**（照抄附魔模式）：预览 `DamageVar.cs:28-40` / `BlockVar.cs:34` / `CalculatedDamageVar.cs:48,70` / `CalculatedBlockVar.cs:35,55`；结算 `Hook.cs:1685-1695`（伤害）、`Hook.cs:1512-1517`（格挡）。预览处调 Hook 必须传未加成的 BaseValue（`DamageVar.cs:42`），否则 +3 会算两次。
- 出牌入口：`CardModel.OnPlayWrapper:2020`（统一后置点：抽牌 / +1 费 / 再打一次 / 奇迹都挂这里）；抽牌用 `CardPileCmd.Draw(choiceContext, 1m, player)`（`CardPileCmd.cs:988`）。
- 卡面文字装配：`CardModel.cs:1551-1590`；悬浮提示 getter：`:1053`。
- 火堆完成语义：日志里原版会打 "Completing rest site for player X because there are no options left" ⇒ 房间靠"剩余选项"推进；`OnSelect` 的返回值语义**必须先读 `RestSiteOption.cs` 与 `NRestSiteRoom`/RestSiteSynchronizer 确认再写**，不要凭猜。

## 9. v0.2.0 验收标准

1. `dotnet build -c Release` 0 警告 0 错误，`dist/sts2-forge/{sts2-forge.dll, sts2-forge.json}` 产出。
2. 无头启动 exit=0，日志有 `[sts2-forge] initialized; success_percent=…, consume_rest_action=…, one_forge_per_rest=…`，且无指向本模组的 ERROR。
3. 火堆里出现「强化」选项，图标不是空白/占位。
4. 同一页能完成 0→1→0→1，逐轮消耗 1、2、1 张。材料不足、满级、零轮取消或最终结束均符合规则。用双端检查只给操作者显示页面，其他端准确复制消耗与结果。
5. 卡面出现词条行，悬浮有说明；读档后等级与词条都还在。
6. +2 伤害/格挡的**显示数字与实际结算一致**；诅咒在 +2 后可以打出且 0 费。
7. 设置页（RitsuLib，可选）能改 4 个键，概率滑块下方实时表格随拖动更新；不装 RitsuLib 时退回 JSON-only 且仍可编译。
