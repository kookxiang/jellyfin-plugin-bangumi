# [bgm.tv](https://bgm.tv) metadata provider for Jellyfin

[![Jellyfin Plugin](https://github.com/kookxiang/jellyfin-plugin-bangumi/actions/workflows/build.yml/badge.svg)](https://github.com/kookxiang/jellyfin-plugin-bangumi/actions/workflows/build.yml)

Jellyfin bgm.tv 数据源插件，用于拉取中文番剧信息及图片。

支持将播放进度同步至 bgm.tv

![后台配置](https://github.com/user-attachments/assets/1d1bdfd9-a932-4bf5-9b0a-ec15a1aed3a0)

# 下载

 - [CI 最新版](https://github.com/kookxiang/jellyfin-plugin-bangumi/releases/tag/ci)
 - [GitHub 稳定版](https://github.com/kookxiang/jellyfin-plugin-bangumi/releases/latest)

# 安装

## 通过插件库安装

1. 控制台中选择 插件 - 存储库 - 添加
2. 在插件目录中找到 Bangumi 插件安装

目前有三个插件库地址可供选择，可以视网络情况自行选择：
 - GitHub Pages\
   https://kookxiang.github.io/jellyfin-plugin-bangumi/repository.json
 - CloudFlare Pages\
   https://jellyfin-plugin-bangumi.kookxiang.dev/repository.json
 - CloudFlare Pages（不推荐）\
   https://jellyfin-plugin-bangumi.pages.dev/repository.json

安装后可在后台更新，推荐使用此方式安装

## 手动安装

1. 下载插件 DLL 文件至 `Jellyfin 数据目录/Plugins/Bangumi`
2. 重新启动 Jellyfin

# Emby 安装

Emby 版本的插件要求 4.9.0.33 及以上的版本，低于这个版本的会无法启动插件。

可以从 [linuxserver/emby](https://hub.docker.com/r/linuxserver/emby/tags) 和 [emby/embyserver](https://hub.docker.com/r/emby/embyserver/tags) 上找到比 4.9.0.33 更高的版本。

1. 下载插件 DLL 文件至 `Emby 数据目录/plugins/`
2. 重新启动 Emby

## 目录类型

在插件的媒体库目录配置中选择 **Auto / 正片 / 特典**，或在视频所在目录的 `bangumi.ini` 中设置：

```ini
[Bangumi]
Type=Normal
```

- `Auto`（默认，也可省略）：保留自动识别，支持同一目录混放正片与特典。
- `Normal`：此目录中的剧集强制作为正片，使用非零季号。
- `Special`：此目录中的剧集强制作为特典，使用第 0 季。

强制模式优先于文件名、目录名、旧季号及 Bangumi 返回的类型；重新按所属条目和集号匹配，不沿用已保存的单集 ID（包括开启“信任已有 Bangumi ID”时）。同集号有多种类型时优先匹配配置的类型；没有对应类型时仍可使用其他类型的元数据，最终分类保持配置值。保存后刷新该目录的剧集元数据生效。配置只作用于 `bangumi.ini` 所在目录，不递归应用到子目录。

## 按文件名设置剧集偏移量

两个字幕组的文件在同一目录、但集数编号不同时，可以在媒体库目录配置的「按文件名指定偏移量」中每行填写 `文件名通配符=偏移量`，或编辑该目录的 `bangumi.ini`：

```ini
[Bangumi]
Offset=0

[Section.1]
Selector=[某字幕组][**].mp4
Offset=26

[Section.2]
Selector=[另一字幕组]*.mp4
Offset=0
```

选择器只匹配文件名，不匹配路径。`*` 匹配任意长度的字符，`?` 匹配单个字符，方括号是普通字符；匹配不区分大小写。按文件中的顺序使用第一个命中的节；节中可以覆盖 `ID`、`Offset`、`Report`、`Skip`、`CorrectIndex` 和 `Type`，未填写的字段沿用 `[Bangumi]` 中的值。例如第一条规则会将 `[某字幕组][27].mp4` 的第 27 集对应到 Bangumi 第 1 集。目录级字段可以省略 `[Bangumi]` 节名，但须写在第一个 `[Section.n]` 之前。`bangumi.ini` 使用 INI 格式，并非 TOML 文件。修改后刷新该目录的剧集元数据生效。

## Jellyfin 12.0 剧集版本误合并的临时修复

在“剧集解析”中启用 **（实验性）修正 Jellyfin 12.0 多版本识别错误的问题**（仅 Jellyfin 12.0.x 默认启用，其他版本默认关闭；保留已保存的开关设置）。这是绕过 Jellyfin 12.0 文件名误分组的**临时修复方案**，未来将下线。与 Basic、AnitomySharp、混合解析模式均兼容，只作用于启用 Bangumi 单集元数据的电视剧媒体库。Jellyfin 12.1 已修复主要的误合并问题；从 12.0 升级后若不再需要此功能，可手动关闭并扫描媒体库。

1. 扫描媒体库：没有有效 Bangumi 单集 ID 的文件先分别入库。
2. 等待获取元数据，必要时手动刷新并纠正错误的单集 ID。
3. 再次扫描媒体库：同一目录中，相同有效单集 ID 且文件路径识别出的季号、集号和类型一致时才合并为版本；不同 ID、不同集数或无法识别集数的文件保持独立。

此开关仅控制版本分组，不覆盖集号；元数据仍遵循所选解析器及其配置。分组使用已保存的 ID，扫描阶段不会查询 Bangumi。仅刷新元数据不会立即重建分组；文件名校验可阻止被复制的相同 ID 把不同集数重新合并；已污染的单集 ID 和元数据仍需独立刷新纠正。已有的错误自动版本关联会在扫描时按当前 ID 拆分，保留可识别的单集记录；手动合并的版本不由此功能拆分。关闭后，下次扫描恢复 Jellyfin 的原生文件名分组规则。

## 缺失与待播剧集（Jellyfin 12）

在插件的「元数据」设置中开启「显示已播出的缺失剧集」或「显示待播剧集」，选择启用的媒体库并保存，再扫描媒体库。默认关闭；媒体库也需要启用 Bangumi 剧集元数据。客户端是否显示缺失/待播条目，还取决于客户端自身的显示设置。

只使用已下载的离线数据库，不请求在线接口。仅导入 Bangumi 标记为正片、集号为整数且具有完整有效播出日期的剧集；小数集号、特别篇和日期未知的剧集跳过。当日及以后播出的条目由待播开关控制。离线数据缺失时保留已有占位，等待数据恢复。

按已有季的 Bangumi ID 匹配；第一季可使用系列 ID，目录 `bangumi.ini` 的 `ID` 优先，`Offset`、`CorrectIndex`、`Skip` 和强制特典配置同样生效。不自动猜测或创建完全缺失的季。首次扫描尚未完成季映射和剧集刮削时，可能需要再次扫描。

占位只保存元数据，没有媒体文件。重复扫描会更新已有占位，文件补齐后扫描会清理对应占位。关闭功能、取消选中媒体库或设置目录跳过后，下次扫描仅删除本功能标记的占位，不删除媒体文件或其他来源的虚拟剧集。关闭媒体库的 Bangumi 系列元数据源会阻止本功能运行，请先关闭本功能并扫描完成清理。
