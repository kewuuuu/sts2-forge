# 实现笔记（火堆强化 v0.2.0）

## 1. `RestSiteOption.OnSelect()` 的返回值语义（先读这一节）

`RestSiteOption.cs:79-83` 的注释与 `RestSiteSynchronizer.cs:175-196` 的实现共同定义了契约：

- **返回 true** = 这次行动成功：`RestSiteSynchronizer.cs:197` 把选项 id 记进 `RestSiteChoices`，随后按 `Hook.ShouldDisableRemainingRestSiteOptions`（`:202`）决定清空剩余选项还是保留；选项被消费。
- **返回 false** = 没成功：`RestSiteSynchronizer.cs:193-196` 直接 `return false`，选项**留在列表里**，可以再选一次；不写历史。

强化页面结束后，如果至少完成一轮，`ForgeRestSiteOption.OnSelect` 先登记一次 `ForgeAttempts`，再以 `ConsumeRestAction` 决定是否消费火堆行动。配置关闭时，仍可继续治疗或打铁。`OneForgePerRest` 限制同一玩家和楼层开启另一个会话。零轮退出不登记。

## 2. 哪些端执行

`RestSiteSynchronizer.ChooseOption` 在每一端都对选中强化的玩家调用 `OnSelect`。只有该玩家本地打开 Godot 覆盖层、读操作、掷骰并显示特效。回合结算复用原版的 `PlayerChoiceSynchronizer`：

- 每端为目标和每个材料阶段按相同顺序预留选择 ID。目标消息是一个索引，空列表是未选目标就结束。
- 每轮消息是当前材料候选列表的索引，加上操作者生成的成功标记。空列表结束会话。候选列表从双方已同步的牌组按顺序生成。
- 其余端等待同一 ID 对应的选择，校验并按相同顺序扣材料，再应用收到的成败。即使满级或不足，仍等待一次明确的结束选择。轮次选择 ID 加入种子，重新回到同等级时可产生不同结果。
- `NCardSmithVfx.Create(NCard)` 在固定卡面原位播放特效。按钮、返回热键和覆盖层关闭均交给同一结束入口，普通输入在逐轮结算及特效期间锁定。

配置仍由房主单独同步。`ForgeConfigMessage`（4 个字段，尾部追加）由房主广播、客户端采纳，`ForgeConfigRequestMessage` 用于中途加入时补拉。

## 3. 等级存哪

- 运行时：`ConditionalWeakTable<CardModel, ForgeLevelBox>`（`ForgeLevels.cs:40`），不污染 `CardModel`，也不会被存档序列化。
- 存档：`CardModel.ToSerializable` 的 postfix 往 `SerializableCard.Props.ints` 里塞 `forge_level`（`SavePatches.cs:40-59`）；`FromSerializable` 的 postfix 读回来并立即 `ApplyKeywordState`（`:61-84`）。`SavedProperty<T>` 是 `SavedProperties` 的**嵌套类型**，必须写 `SavedProperties.SavedProperty<int>`。
- 克隆：`AbstractModel.MutableClone` 的 postfix 复制等级（`RestSitePatches.cs:134-147`），所以 `RunState.CloneCard`、复制药水、事件复制都会带上等级。
- 关键词不进存档（`CardModel.cs:73-78` 注释）⇒ 「诅咒 +2 可打出」这条规则每次读档都按等级重新算：`ForgeLevels.ApplyKeywordState` 用 `AddKeyword/RemoveKeyword` 改实例级 `LocalKeywords`。

## 4. 补丁清单（`PatchApplier.cs:26-40`）

| 目标 | 作用 | 缺失时 |
| --- | --- | --- |
| `RestSiteOption.Generate` postfix | 把「强化」插在「打铁」后面 | 选项不出现 |
| `RestSiteOption.Icon` prefix | 返回内嵌 PNG（`AssetPaths` 同时被 prefix 成空数组，避免原版去 `res://ui/rest_site/option_forge.png` 找图） | 图标回落 missing_power |
| `AbstractModel.MutableClone` postfix | 克隆时复制等级 | 克隆牌丢等级 |
| `Hook.ModifyDamage` prefix | +2 起攻击牌伤害 +3 | 伤害加成失效（卡面数字仍显示） |
| `Hook.ModifyBlock` prefix | +2 起技能牌格挡 +3 | 格挡加成失效 |
| `Hook.ModifyCardPlayCount` postfix | +4 起打出次数 ×2（与重放相乘） | 额外打出失效 |
| `CardModel.GetDescriptionForPile` postfix | 卡面追加「强化 N」 | 卡面不显示等级 |
| `CardModel.HoverTips` postfix | 悬停加强化词条 | 悬停无说明 |
| `CardModel.ToSerializable` / `FromSerializable` postfix | 等级进出存档 | 等级不保存 |
| `LocManager.Initialize` postfix | 注入本地化（DLL 模组没有 pck） | 显示 key |

每个补丁单独 try/catch，缺一个只打一条 WARN，不影响其它补丁与游戏本身（`PatchApplier.cs:42-60`）。

+1 / +2 / +3 / +5 的打出效果不在补丁里，而是 `ForgeCombatListener`（`AbstractModel` 子类，`ShouldReceiveCombatHooks=true`，经 `ModHelper.SubscribeForCombatStateHooks` 每场战斗挂一次）：`AfterCardPlayed` 里按等级单独获得 3 格挡 / 抽牌 / 给能量 / 施加 ForgeMiraclePower（奇迹）。

## 5. 已知边界

- `ModifyDamage` / `ModifyBlock` 是**全局**钩子：只要出牌来源是这张牌就 +3，不区分「预览」与「结算」——两处各自加一次，所以卡面与实伤一致；但第三方 mod 若直接读 `DynamicVars` 而不是走钩子，会看不到这 +3。
- `ModifyCardPlayCount` 直接 ×2：对已经是 2 次的牌（重放）就是 4 次，符合设计「与重放相乘」；但它同时也乘到别的 mod 对同一张牌的加成上。
- 等级只按「卡牌实例」记：`player.RunState.CreateCard` 出来的新实例默认 0 级；要带等级得走克隆。
- 诅咒 +2 变可打出：靠删掉实例级 `Unplayable` 关键词实现，费用由原版钳制（负费用按 0 结算），所以「0 费可打出」不额外改费用。
- `ForgeAttempts` 只在内存：读档后 `(NetId, Floor)` 计数清零，若玩家在 `consume_rest_action=false` 的同一处火堆存读档，可以再强化一次。
