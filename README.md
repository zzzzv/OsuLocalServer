# OsuLocalServer

osu本地数据服务

## API

| 功能 | 路径 | 说明 |
| ------ | ------ | ------ |
| 状态 | `/api/status` | 各模块可用性 |
| Stable 文件 | `/api/stable/files/{**relativePath}` | 从 osu!stable 目录读取文件，支持 `*` 通配符 |
| Stable 收藏夹 | `POST /api/stable/collections` | 追加 beatmap md5hash 到 stable 收藏夹 |
| Lazer 查询 | `/api/lazer/{scores,beatmaps,beatmapsets,collections}?rql=...&depth=N` | RQL 查询 Realm 数据库（Score / Beatmap / BeatmapSet / Collection），`depth` 控制嵌套展开 |
| Lazer 文件 | `/api/lazer/files/{hash}` | 按 hash 获取文件 |
| Lazer 收藏夹 | `POST /api/lazer/collections` | 追加 beatmap md5hash 到 lazer 收藏夹 |
| osu! API v2 代理 | `/api/osuapi/v2/**` | 反向代理到 osu.ppy.sh |
| Mania SR 数据 | `/api/management/mania-sr/msgpack` | 已提取的谱面 SR 信息 |

## 页面

| 页面 | 路径 | 说明 |
| --- | --- | --- |
| 状态 | `/` | 各模块可用性 |
| 设置 | `/settings` | 图形化配置管理 |
| 管理 | `/management` | 后台任务 |

## 后台任务

| 任务 | 说明 |
| --- | --- |
| 生成 Mania SR | 从 stable 的 `osu!.db` 提取官方 SR（PPY），并用 StarRatingRebirth 算法计算 XXY SR，结果保存到 msgpack |
| 写入 Mania SR | 将 msgpack 中的 SR 写回本地数据库：Stable 写入 `osu!.db` 的 `ManiaStarRating`，Lazer 写入 `client.realm` 的 `BeatmapInfo.StarRating`。可选择写入 PPY 或 XXY |
