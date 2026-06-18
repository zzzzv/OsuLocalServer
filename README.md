# OsuLocalServer

osu本地数据服务

## API

### GET

| 路径 | 说明 |
| ------ | ------ |
| `/api/status` | 各模块可用性 |
| `/api/stable/files/{**relativePath}` | 从 osu!stable 目录读取文件，支持 `*` 通配符 |
| `/api/stable/folder/{**folderPath}` | 列出 stable 目录下指定文件夹内的所有文件名、大小和修改时间 |
| `/api/lazer/scores?rql=...&depth=N` | RQL 查询 Score |
| `/api/lazer/beatmaps?rql=...&depth=N` | RQL 查询 Beatmap |
| `/api/lazer/beatmapsets?rql=...&depth=N` | RQL 查询 BeatmapSet |
| `/api/lazer/collections?rql=...&depth=N` | RQL 查询 Collection |
| `/api/lazer/files/{hash}` | 按 hash 获取文件 |
| `/api/osuapi/v2/**` | 反向代理到 osu.ppy.sh |
| `/api/management/mania-sr/msgpack` | 已提取的谱面 SR 信息 |

### POST

#### `/api/stable/collection/update`

更新 stable 收藏夹，追加 beatmap md5hash。

```json
{ "name": "...", "beatmapMd5Hashes": ["...", "..."] }
```

#### `/api/stable/star-rating/update`

批量写入 Mania Star Rating（NM/HT/DT）到 stable 的 `osu!.db`。

```json
{ "starRatings": { "<md5>": { "nm": 6.53, "ht": 5.21, "dt": 7.82 } } }
```

#### `/api/lazer/collection/update`

更新 lazer 收藏夹，追加 beatmap md5hash。

```json
{ "name": "...", "beatmapMd5Hashes": ["...", "..."] }
```

#### `/api/lazer/star-rating/calculate?mods=...`

计算谱面 Star Rating，body 为原始 .osu 文件内容，mods 通过查询参数传递（JSON 数组，需 URL 编码）。

```http
POST /api/lazer/star-rating/calculate?mods=[{"acronym":"DT","settings":{"speed_change":1.5}},{"acronym":"HR"}]
Content-Type: text/plain

osu file format v14...
```

#### `/api/lazer/star-rating/update`

批量写入 Star Rating 到 lazer 的 `client.realm`。

```json
{ "starRatings": { "<md5>": 6.53, "<md5>": 4.21 } }
```

#### `/api/tools/xxy-calculate?speedRate=...`

使用 StarRatingRebirth 计算 XXY SR。body 为原始 .osu 文件内容，`speedRate` 通过查询参数传递。

```http
POST /api/tools/xxy-calculate?speedRate=1.5
Content-Type: text/plain

osu file format v14...
```

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
