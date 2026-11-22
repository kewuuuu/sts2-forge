# 配置（sts2-forge.cfg）

文件位置：

`<游戏可执行文件目录>/mods/config/sts2-forge.cfg`，目录不存在会自动创建。内容是 JSON。

## 四个键

| 键 | 类型 | 默认 | 范围 | 作用 |
| --- | --- | --- | --- | --- |
| `success_percent` | int | 50 | 0..100 | 每次锻铸的成功率。0 = 必定失败（材料照扣、等级 −1 或被垫子挡住），100 = 必定成功 |
| `consume_rest_action` | bool | true | true/false | 锻铸是否占掉本次火堆行动。true = 选完即结束火堆；false = 还能继续治疗/打铁 |
| `one_forge_per_rest` | bool | true | true/false | 同一处火堆是否只能锻铸一次。仅在 `consume_rest_action=false` 时有意义 |
| `log_debug` | bool | false | true/false | 把每次锻铸的种子细节写进 godot.log，排障用 |

## 示例

```json
{
  "success_percent": 50,
  "consume_rest_action": true,
  "one_forge_per_rest": true,
  "log_debug": false
}
```

## 加载与容错

- 文件缺失 / 非 UTF-8 / JSON 非法 / 根节点不是对象 → 整份默认值。
- 单个键类型不对 → 该键用默认值，其余键照常生效。
- `success_percent` 越界 → 钳制到 0..100 并打一条 WARN。
- 读到的值与默认值不同时，启动日志会多一条 WARN：联机局需要每位玩家用同一份 cfg（房主的会覆盖客户端）。

## 生效时机

开一局时把当时的配置冻结成这一局的快照（日志 `run started; for_this_run=…`），局内改 cfg 或拖动设置页只影响**下一局**。
联机时以房主为准：房主把快照广播给客户端（日志 `broadcast config: …` / `host config adopted mid-run …`），客户端自己的 cfg 只在单机生效。
