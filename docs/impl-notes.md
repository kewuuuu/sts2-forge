# 实现笔记（火堆锻铸 v0.1.0-beta）

## 1. `RestSiteOption.OnSelect()` 的返回值语义（先读这一节）

`RestSiteOption.cs:79-83` 的注释与 `RestSiteSynchronizer.cs:175-196` 的实现共同定义了契约：

- **返回 true** = 这次行动成功：`RestSiteSynchronizer.cs:197` 把选项 id 记进 `RestSiteChoices`，随后按 `Hook.ShouldDisableRemainingRestSiteOptions`（`:202`）决定清空剩余选项还是保留；选项被消费。
- **返回 false** = 没成功：`RestSiteSynchronizer.cs:193-196` 直接 `return false`，选项**留在列表里**，可以再选一次；不写历史。

所以 `ForgeRestSiteOption.ForgeAsync` 的结尾是 `return config.ConsumeRestAction;`：

- `consume_rest_action=true` → 返回 true，锻铸吃掉本次火堆。
- `consume_rest_action=false` → 返回 false，选项仍在，可以继续锻铸或去治疗/打铁；此时 `one_forge_per_rest` 用 `ForgeAttempts`（内存里按 `(NetId, Floor)` 计数）拦住第二次。

选择被取消（没选主卡）、材料不够、内部异常 → 一律返回 false，不消耗任何东西。

## 2. 哪些端执行

`RestSiteSynchronizer.ChooseOption` 在**每一端**都会对**选中该选项的玩家**跑一遍 `option.OnSelect()`（`RestSiteSynchronizer.cs:105-117` 收消息后 `TaskHelper.RunSafely(ChooseOption(...))`，`:189` 调 `OnSelect`）。所以锻铸的每一步都必须确定性地只依赖 run state：

- 掷骰用 `ForgeLevels.CreateRng`：种子由 `RunState.TotalFloor`、当前等级、材料张数混出来，再交给 `new Rng(owner, main.Id, mixin)`（`ForgeLevels.cs:167-173`），**不消费** `player.PlayerRng`。
- 材料挑选是纯函数（按牌组顺序取前 N 张同名牌），不依赖字典顺序或本地配置。
- 因此锻铸本身**没有新增任何网络消息**；两端各自算出相同结果。

联机只同步「参数」：`ForgeConfigMessage`（4 个字段，尾部追加）由房主广播、客户端采纳，`ForgeConfigRequestMessage` 用于中途加入时补拉。

## 3. 等级存哪

- 运行时：`ConditionalWeakTable<CardModel, ForgeLevelBox>`（`ForgeLevels.cs:40`），不污染 `CardModel`，也不会被存档序列化。
- 存档：`CardModel.ToSerializable` 的 postfix 往 `SerializableCard.Props.ints` 里塞 `forge_level`（`SavePatches.cs:40-59`）；`FromSerializable` 的 postfix 读回来并立即 `ApplyKeywordState`（`:61-84`）。`SavedProperty<T>` 是 `SavedProperties` 的**嵌套类型**，必须写 `SavedProperties.SavedProperty<int>`。
- 克隆：`AbstractModel.MutableClone` 的 postfix 复制等级（`RestSitePatches.cs:134-147`），所以 `RunState.CloneCard`、复制药水、事件复制都会带上等级。
- 关键词不进存档（`CardModel.cs:73-78` 注释）⇒ 「诅咒 +2 可打出」这条规则每次读档都按等级重新算：`ForgeLevels.ApplyKeywordState` 用 `AddKeyword/RemoveKeyword` 改实例级 `LocalKeywords`。

## 4. 补丁清单（`PatchApplier.cs:26-40`）

| 目标 | 作用 | 缺失时 |
| --- | --- | --- |
| `RestSiteOption.Generate` postfix | 把「锻铸」插在「打铁」后面 | 选项不出现 |
| `RestSiteOption.Icon` prefix | 返回内嵌 PNG（`AssetPaths` 同时被 prefix 成空数组，避免原版去 `res://ui/rest_site/option_forge.png` 找图） | 图标回落 missing_power |
| `AbstractModel.MutableClone` postfix | 克隆时复制等级 | 克隆牌丢等级 |
| `Hook.ModifyDamage` prefix | +1 起攻击牌伤害 +3 | 伤害加成失效（卡面数字仍显示） |
| `Hook.ModifyBlock` prefix | +1 起技能牌格挡 +3 | 格挡加成失效 |
| `Hook.ModifyCardPlayCount` postfix | +4 起打出次数 ×2（与重放相乘） | 额外打出失效 |
| `CardModel.GetDescriptionForPile` postfix | 卡面追加「锻铸 N」 | 卡面不显示等级 |
| `CardModel.HoverTips` postfix | 悬停加锻铸词条 | 悬停无说明 |
| `CardModel.ToSerializable` / `FromSerializable` postfix | 等级进出存档 | 等级不保存 |
| `LocManager.Initialize` postfix | 注入本地化（DLL 模组没有 pck） | 显示 key |

每个补丁单独 try/catch，缺一个只打一条 WARN，不影响其它补丁与游戏本身（`PatchApplier.cs:42-60`）。

+2 / +3 / +5 的打出效果不在补丁里，而是 `ForgeCombatListener`（`AbstractModel` 子类，`ShouldReceiveCombatHooks=true`，经 `ModHelper.SubscribeForCombatStateHooks` 每场战斗挂一次）：`AfterCardPlayed` 里按等级补抽牌 / 给能量 / 给 BufferPower（奇迹）。

## 5. 已知边界

- `ModifyDamage` / `ModifyBlock` 是**全局**钩子：只要出牌来源是这张牌就 +3，不区分「预览」与「结算」——两处各自加一次，所以卡面与实伤一致；但第三方 mod 若直接读 `DynamicVars` 而不是走钩子，会看不到这 +3。
- `ModifyCardPlayCount` 直接 ×2：对已经是 2 次的牌（重放）就是 4 次，符合设计「与重放相乘」；但它同时也乘到别的 mod 对同一张牌的加成上。
- 等级只按「卡牌实例」记：`player.RunState.CreateCard` 出来的新实例默认 0 级；要带等级得走克隆。
- 诅咒 +2 变可打出：靠删掉实例级 `Unplayable` 关键词实现，费用由原版钳制（负费用按 0 结算），所以「0 费可打出」不额外改费用。
- `ForgeAttempts` 只在内存：读档后 `(NetId, Floor)` 计数清零，若玩家在 `consume_rest_action=false` 的同一处火堆存读档，可以再锻一次。
