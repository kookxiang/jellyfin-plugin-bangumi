using System;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Bangumi.Utils;

/// <summary>
/// fullPath 所表示的路径类型
/// </summary>
public enum ExcludeRegexPathType
{
    /// <summary>
    /// fullPath 表示 Season 目录路径
    /// </summary>
    SeasonFolder,

    /// <summary>
    /// fullPath 表示剧集文件路径
    /// </summary>
    EpisodeFile
}

/// <summary>
/// 正则配置所属的业务类型
/// </summary>
public enum ExcludeRegexType
{
    /// <summary>
    /// 排除白名单正则
    /// </summary>
    ExcludeWhitelist,

    /// <summary>
    /// 特典排除正则
    /// </summary>
    Special,

    /// <summary>
    /// 杂项排除正则
    /// </summary>
    Misc
}

/// <summary>
/// 当前参与匹配的输入粒度
/// </summary>
public enum ExcludeRegexInputType
{
    /// <summary>
    /// 完整路径
    /// </summary>
    FullPath,

    /// <summary>
    /// 目录名
    /// </summary>
    FolderName,

    /// <summary>
    /// 文件名
    /// </summary>
    FileName
}

public static class ExcludeRegexMatcher
{
    /// <summary>
    /// 使用给定正则配置匹配指定输入
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="patterns">按换行分隔的正则表达式配置</param>
    /// <param name="input">待匹配的输入文本</param>
    /// <param name="log">日志记录器</param>
    /// <param name="regexType">当前匹配的正则类型</param>
    /// <param name="inputType">当前匹配的输入粒度</param>
    /// <returns>是否命中任意正则</returns>
    private static bool MatchExcludeRegexes<T>(
        string patterns,
        string? input,
        Logger<T>? log,
        ExcludeRegexType regexType,
        ExcludeRegexInputType inputType)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        // 按行分割正则配置
        var patternFullPath = patterns?.Split("\n") ?? [];
        foreach (var item in patternFullPath)
        {
            if (string.IsNullOrEmpty(item))
            {
                continue;
            }

            try
            {
                Regex regex = new(item, RegexOptions.IgnoreCase);
                if (regex.IsMatch(input))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                LogMatchFailure(log, regexType, inputType, input, item, e);
            }
        }

        return false;
    }

    /// <summary>
    /// 根据路径类型选择适用的完整路径、目录名、文件名正则进行匹配
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="fullPath">完整路径</param>
    /// <param name="pathType">fullPath 对应的路径类型</param>
    /// <param name="libraryManager">媒体库管理器，用于判断目录层级和根目录类型</param>
    /// <param name="log">日志记录器</param>
    /// <param name="regexType">当前匹配的正则类型</param>
    /// <param name="fullPathPatterns">完整路径正则配置</param>
    /// <param name="folderNamePatterns">目录名称正则配置</param>
    /// <param name="fileNamePatterns">文件名正则配置</param>
    /// <returns>是否命中任意层级的正则</returns>
    private static bool MatchByPath<T>(
        string fullPath,
        ExcludeRegexPathType pathType,
        ILibraryManager libraryManager,
        Logger<T>? log,
        ExcludeRegexType regexType,
        string fullPathPatterns,
        string folderNamePatterns,
        string fileNamePatterns)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return false;
        }

        // 匹配完整路径
        if (MatchExcludeRegexes(fullPathPatterns, fullPath, log, regexType, ExcludeRegexInputType.FullPath))
        {
            return true;
        }

        // 根据路径类型识别目录名称位置
        var folderPath = pathType switch
        {
            ExcludeRegexPathType.SeasonFolder => fullPath,
            ExcludeRegexPathType.EpisodeFile => Path.GetDirectoryName(fullPath) ?? string.Empty,
            _ => string.Empty
        };
        var folderName = Path.GetFileName(folderPath);
        // Series 目录可能包含多种类型关键字容易误报，如：xxx S1 + S2 + OVA，因此不参与目录名匹配
        var shouldCheckFolderName = !string.IsNullOrEmpty(folderName)
            && libraryManager.FindByPath(folderPath, true) is not Series;
        // 匹配目录名
        if (shouldCheckFolderName
            && MatchExcludeRegexes(folderNamePatterns, folderName, log, regexType, ExcludeRegexInputType.FolderName))
        {
            return true;
        }

        // 非剧集类型不需要匹配文件名
        if (pathType != ExcludeRegexPathType.EpisodeFile)
        {
            return false;
        }

        // 匹配文件名
        var fileName = Path.GetFileName(fullPath);
        return MatchExcludeRegexes(fileNamePatterns, fileName, log, regexType, ExcludeRegexInputType.FileName);
    }

    /// <summary>
    /// 记录正则匹配过程中出现的异常信息
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="log">日志记录器</param>
    /// <param name="regexType">当前匹配的正则类型</param>
    /// <param name="inputType">当前匹配的输入粒度</param>
    /// <param name="input">触发异常的输入内容</param>
    /// <param name="pattern">触发异常的正则表达式</param>
    /// <param name="exception">异常对象</param>
    private static void LogMatchFailure<T>(
        Logger<T>? log,
        ExcludeRegexType regexType,
        ExcludeRegexInputType inputType,
        string input,
        string pattern,
        Exception exception)
    {
        if (log == null) return;

        log.Error(
            "Exclude regex match failed. Type: {Type}, InputType: {InputType}, Input: {Input}, Pattern: {Pattern}, Error: {Error}",
            regexType,
            inputType,
            input,
            pattern,
            exception.Message);
    }

    /// <summary>
    /// 使用排除白名单配置执行路径匹配
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="configuration">插件配置</param>
    /// <param name="fullPath">完整路径</param>
    /// <param name="pathType">fullPath 对应的路径类型</param>
    /// <param name="libraryManager">媒体库管理器</param>
    /// <param name="log">日志记录器</param>
    /// <returns>是否命中排除白名单</returns>
    public static bool MatchExcludeWhitelist<T>(
        PluginConfiguration configuration,
        string fullPath,
        ExcludeRegexPathType pathType,
        ILibraryManager libraryManager,
        Logger<T>? log)
        => MatchByPath(
            fullPath,
            pathType,
            libraryManager,
            log,
            ExcludeRegexType.ExcludeWhitelist,
            configuration.ExcludeWhitelistRegexFullPath,
            configuration.ExcludeWhitelistRegexFolderName,
            configuration.ExcludeWhitelistRegexFileName);

    /// <summary>
    /// 使用特典排除配置执行路径匹配
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="configuration">插件配置</param>
    /// <param name="fullPath">完整路径</param>
    /// <param name="pathType">fullPath 对应的路径类型</param>
    /// <param name="libraryManager">媒体库管理器</param>
    /// <param name="log">日志记录器</param>
    /// <returns>是否命中特典排除规则</returns>
    public static bool MatchSpecialExclude<T>(
        PluginConfiguration configuration,
        string fullPath,
        ExcludeRegexPathType pathType,
        ILibraryManager libraryManager,
        Logger<T>? log)
        => MatchByPath(
            fullPath,
            pathType,
            libraryManager,
            log,
            ExcludeRegexType.Special,
            configuration.SpExcludeRegexFullPath,
            configuration.SpExcludeRegexFolderName,
            configuration.SpExcludeRegexFileName);

    /// <summary>
    /// 使用杂项排除配置执行路径匹配
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="configuration">插件配置</param>
    /// <param name="fullPath">完整路径</param>
    /// <param name="pathType">fullPath 对应的路径类型</param>
    /// <param name="libraryManager">媒体库管理器</param>
    /// <param name="log">日志记录器</param>
    /// <returns>是否命中杂项排除规则</returns>
    public static bool MatchMiscExclude<T>(
        PluginConfiguration configuration,
        string fullPath,
        ExcludeRegexPathType pathType,
        ILibraryManager libraryManager,
        Logger<T>? log)
        => MatchByPath(
            fullPath,
            pathType,
            libraryManager,
            log,
            ExcludeRegexType.Misc,
            configuration.MiscExcludeRegexFullPath,
            configuration.MiscExcludeRegexFolderName,
            configuration.MiscExcludeRegexFileName);

    /// <summary>
    /// 判断指定路径是否应被识别为特典
    /// 白名单命中时始终返回 false
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="configuration">插件配置</param>
    /// <param name="fullPath">完整路径</param>
    /// <param name="pathType">fullPath 对应的路径类型</param>
    /// <param name="libraryManager">媒体库管理器</param>
    /// <param name="log">日志记录器</param>
    /// <returns>是否应视为特典</returns>
    public static bool IsSpecial<T>(
        PluginConfiguration configuration,
        string fullPath,
        ExcludeRegexPathType pathType,
        ILibraryManager libraryManager,
        Logger<T>? log)
    {
        if (MatchExcludeWhitelist(
                configuration,
                fullPath,
                pathType,
                libraryManager,
                log))
        {
            return false;
        }

        return MatchSpecialExclude(
            configuration,
            fullPath,
            pathType,
            libraryManager,
            log);
    }

    /// <summary>
    /// 判断指定路径是否应被识别为杂项
    /// 白名单命中时始终返回 false
    /// </summary>
    /// <typeparam name="T">日志类型</typeparam>
    /// <param name="configuration">插件配置</param>
    /// <param name="fullPath">完整路径</param>
    /// <param name="pathType">fullPath 对应的路径类型</param>
    /// <param name="libraryManager">媒体库管理器</param>
    /// <param name="log">日志记录器</param>
    /// <returns>是否应视为杂项</returns>
    public static bool IsMisc<T>(
        PluginConfiguration configuration,
        string fullPath,
        ExcludeRegexPathType pathType,
        ILibraryManager libraryManager,
        Logger<T>? log)
    {
        if (MatchExcludeWhitelist(
                configuration,
                fullPath,
                pathType,
                libraryManager,
                log))
        {
            return false;
        }

        return MatchMiscExclude(
            configuration,
            fullPath,
            pathType,
            libraryManager,
            log);
    }
}