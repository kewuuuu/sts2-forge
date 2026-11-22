# 进度

## 2026-11-22 立项

- 项目与骨架建立，规格写入 docs/design.md。

## 2026-11-22 v0.1.0-beta 落地

- 火堆选项、五档等级效果、四键配置与联机同步、图标、本地化（自建 forge_ui 表）。
- 无头验收：12/12 补丁、exit=0、零本模组 [ERROR]。修掉 `ModelDb.Inject(ForgeCombatListener)` 造成的启动崩溃（AbstractModel 构造函数自己会注册，ModelDb.Init 再构造一次就 DuplicateModelException），改为 `ModHelper.SubscribeForCombatStateHooks` 惰性解析。

## 2026-11-22 火堆锻铸会话

- 新增独立 Godot 页面，支持手选目标和每轮材料，保留每轮结果与消耗。
- 逐阶段复用原版选择同步，只有操作者看到页面。完成至少一轮的会话在退出时登记一次。
