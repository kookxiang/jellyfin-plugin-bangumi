# [bgm.tv](https://bgm.tv) metadata provider for Jellyfin

[![Jellyfin Plugin](https://github.com/kookxiang/jellyfin-plugin-bangumi/actions/workflows/build.yml/badge.svg)](https://github.com/kookxiang/jellyfin-plugin-bangumi/actions/workflows/build.yml)

Jellyfin bgm.tv 数据源插件，用于拉取中文番剧信息及图片。

支持将播放进度同步至 bgm.tv

![后台配置](https://user-images.githubusercontent.com/2725379/158064318-98a82a79-a783-4552-abaa-af18724ad9bf.png)

# 下载

 - [CI 最新版](https://github.com/kookxiang/jellyfin-plugin-bangumi/releases/tag/ci)
 - [GitHub 稳定版](https://github.com/kookxiang/jellyfin-plugin-bangumi/releases/latest)

# 安装

## 通过插件库安装

1. 控制台中选择 插件 - 存储库 - 添加：
`https://jellyfin-plugin-bangumi.pages.dev/repository.json`
2. 在插件目录中找到 Bangumi 插件安装

安装后可在后台更新，推荐使用此方式安装

## 手动安装

1. 下载插件 DLL 文件至 `Jellyfin 数据目录/Plugins/Bangumi`
2. 重新启动 Jellyfin

# Emby 安装

Emby 版本的插件要求 4.9.0.12 及以上的版本，低于这个版本的会无法启动插件。

可以从 [linuxserver/emby](https://hub.docker.com/r/linuxserver/emby/tags) 和 [emby/embyserver](https://hub.docker.com/r/emby/embyserver/tags) 上找到比 4.9.0.12 更高的版本。

1. 下载插件 DLL 文件至 `Emby 数据目录/plugins/`
2. 重新启动 Emby
