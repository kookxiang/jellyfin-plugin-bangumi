using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Bangumi.Web;

public sealed class UserSettingsInjectionService(Logger<UserSettingsInjectionService> logger) : IHostedService
{
    internal static readonly Guid TransformationId = Guid.Parse("5476427d-26e2-49ed-a3be-d8c39535f219");
    private const string InterfaceTypeName = "Jellyfin.Plugin.FileTransformation.PluginInterface";
    private readonly object _sync = new();
    private bool _registered;
    private string? _reason;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
            Reconcile(true);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
            Unregister();
        return Task.CompletedTask;
    }

    public UserSettingsInjectionStatus GetStatus()
    {
        lock (_sync)
        {
            Reconcile(true);
            return BuildStatus();
        }
    }

    public UserSettingsInjectionStatus SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (!enabled)
            {
                Unregister();
                PersistEnabled(false);
                _reason = null;
                return BuildStatus();
            }

            _reason = null;
            if (!TryRegister(out var reason))
            {
                PersistEnabled(false);
                _reason = reason;
                return BuildStatus();
            }

            PersistEnabled(true);
            return BuildStatus();
        }
    }

    private void Reconcile(bool persistDisabled)
    {
        if (Plugin.Instance?.Configuration.EnableUserSettingsInjection != true)
        {
            if (_registered)
                Unregister();
            return;
        }

        if (!TryGetInterface(out _, out var unavailableReason))
        {
            _registered = false;
            _reason = unavailableReason;
            if (persistDisabled)
                PersistEnabled(false);
            logger.Warn("普通用户 Bangumi 设置注入已自动关闭：{Reason}", unavailableReason);
            return;
        }

        if (TryRegister(out var reason, true))
            return;

        _reason = reason;
        if (persistDisabled)
            PersistEnabled(false);
        logger.Warn("普通用户 Bangumi 设置注入已自动关闭：{Reason}", reason);
    }

    private bool TryRegister(out string? reason, bool verifyRuntime = false)
    {
        if (_registered && !verifyRuntime)
        {
            reason = null;
            return true;
        }

        if (!TryGetInterface(out var interfaceType, out reason))
        {
            return false;
        }
        var registerMethod = interfaceType!.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static)!;

        try
        {
            var wasRegistered = _registered;
            var payload = new JObject
            {
                ["id"] = TransformationId,
                ["fileNamePattern"] = "(?:^|[/\\\\])index\\.html$",
                ["callbackAssembly"] = typeof(UserSettingsIndexTransformer).Assembly.FullName,
                ["callbackClass"] = typeof(UserSettingsIndexTransformer).FullName,
                ["callbackMethod"] = nameof(UserSettingsIndexTransformer.Transform)
            };
            registerMethod.Invoke(null, [payload]);
            _registered = true;
            reason = null;
            if (!wasRegistered)
                logger.Info("已注册普通用户 Bangumi 设置注入");
            return true;
        }
        catch (Exception exception)
        {
            _registered = false;
            reason = exception.GetBaseException().Message;
            logger.Error("注册普通用户 Bangumi 设置注入失败", exception);
            return false;
        }
    }

    private void Unregister()
    {
        if (!_registered)
            return;
        try
        {
            FindInterface()?.GetMethod("RemoveTransformation", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, [TransformationId]);
        }
        catch (Exception exception)
        {
            logger.Error("注销普通用户 Bangumi 设置注入失败", exception);
        }
        finally
        {
            _registered = false;
        }
    }

    private static Type? FindInterface() => AssemblyLoadContext.All
        .SelectMany(context => context.Assemblies)
        .FirstOrDefault(assembly => assembly.GetName().Name?.Contains("FileTransformation", StringComparison.Ordinal) == true)
        ?.GetType(InterfaceTypeName);

    private static bool TryGetInterface(out Type? interfaceType, out string? reason)
    {
        interfaceType = FindInterface();
        var register = interfaceType?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
        var remove = interfaceType?.GetMethod("RemoveTransformation", BindingFlags.Public | BindingFlags.Static);
        if (register != null && remove != null)
        {
            reason = null;
            return true;
        }

        reason = interfaceType == null
            ? "未安装 File Transformation 或插件尚未加载"
            : "File Transformation 版本不兼容（缺少注册或注销接口）";
        return false;
    }

    private static void PersistEnabled(bool enabled)
    {
        var plugin = Plugin.Instance;
        if (plugin == null || plugin.Configuration.EnableUserSettingsInjection == enabled)
            return;
        plugin.Configuration.EnableUserSettingsInjection = enabled;
        plugin.UpdateConfiguration(plugin.Configuration);
    }

    private UserSettingsInjectionStatus BuildStatus()
    {
        var available = TryGetInterface(out _, out _);
        return new UserSettingsInjectionStatus(
            Plugin.Instance?.Configuration.EnableUserSettingsInjection == true,
            available,
            _registered,
            _reason);
    }
}

public sealed record UserSettingsInjectionStatus(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("dependencyAvailable")] bool DependencyAvailable,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("reason")] string? Reason);

public static class UserSettingsIndexTransformer
{
    internal const string Marker = "<!-- bangumi-user-settings -->";
    internal const string Script = "<script type=\"module\" src=\"configurationpage?name=Plugin.Bangumi.UserSettings.Script\"></script>";

    public static string Transform(JObject payload) => Transform(payload.Value<string>("contents") ?? string.Empty);

    internal static string Transform(string contents)
    {
        if (contents.Contains(Marker, StringComparison.Ordinal))
            return contents;
        var body = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return body < 0 ? contents : contents.Insert(body, $"{Marker}{Script}\n");
    }
}
