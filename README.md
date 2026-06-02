# OsuLocalServer

osu本地数据服务，供 web 端使用以增强功能

| 功能 | 路径 | 说明 |
| ------ | ------ | ------ |
| 状态 | `/api/status` | 各模块可用性 |
| Stable 文件 | `/api/stable/files/{**relativePath}` | 从 osu!stable 目录读取文件，支持 `*` 通配符 |
| Stable 收藏夹 | `POST /api/stable/collections` | 追加 beatmap md5hash 到 stable 收藏夹 |
| Lazer 查询 | `/api/lazer/{scores,beatmaps,beatmapsets,collections}?rql=...&depth=N` | RQL 查询 Realm 数据库（Score / Beatmap / BeatmapSet / Collection），`depth` 控制嵌套展开 |
| Lazer 文件 | `/api/lazer/files/{hash}` | 按 hash 获取文件 |
| Lazer 收藏夹 | `POST /api/lazer/collections` | 追加 beatmap md5hash 到 lazer 收藏夹 |
| osu! API v2 代理 | `/api/osuapi/v2/**` | 反向代理到 osu.ppy.sh |
| 设置页 | `/settings` | 图形化配置管理 |
