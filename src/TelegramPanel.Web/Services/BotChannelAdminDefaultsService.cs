using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

namespace TelegramPanel.Web.Services;

public sealed record BotChannelAdminDefaults(TelegramPanel.Core.Services.Telegram.BotTelegramService.BotAdminRights Rights);

/// <summary>
/// Bot 频道“设置管理员（机器人/ID）”默认权限（保存到 appsettings.local.json）
/// </summary>
public sealed class BotChannelAdminDefaultsService
{
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public BotChannelAdminDefaultsService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configFilePath = LocalConfigFile.ResolvePath(configuration, environment);
    }

    public async Task<BotChannelAdminDefaults?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_configFilePath))
                return null;

            var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null)
                return null;

            if (root["BotChannelAdminDefaults"] is not JsonObject section)
                return null;

            if (section["Rights"] is not JsonObject rightsObj)
                return null;

            var rights = new TelegramPanel.Core.Services.Telegram.BotTelegramService.BotAdminRights(
                ManageChat: ReadBool(rightsObj, "ManageChat"),
                ChangeInfo: ReadBool(rightsObj, "ChangeInfo"),
                PostMessages: ReadBool(rightsObj, "PostMessages"),
                EditMessages: ReadBool(rightsObj, "EditMessages"),
                DeleteMessages: ReadBool(rightsObj, "DeleteMessages"),
                InviteUsers: ReadBool(rightsObj, "InviteUsers"),
                RestrictMembers: ReadBool(rightsObj, "RestrictMembers"),
                PinMessages: ReadBool(rightsObj, "PinMessages"),
                PromoteMembers: ReadBool(rightsObj, "PromoteMembers")
            );

            return new BotChannelAdminDefaults(rights);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(BotChannelAdminDefaults defaults, CancellationToken cancellationToken = default)
    {
        if (defaults == null)
            throw new ArgumentNullException(nameof(defaults));

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await LocalConfigFile.EnsureExistsAsync(_configFilePath, cancellationToken);

            var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

            var section = root["BotChannelAdminDefaults"] as JsonObject ?? new JsonObject();
            section["Rights"] = new JsonObject
            {
                ["ManageChat"] = defaults.Rights.ManageChat,
                ["ChangeInfo"] = defaults.Rights.ChangeInfo,
                ["PostMessages"] = defaults.Rights.PostMessages,
                ["EditMessages"] = defaults.Rights.EditMessages,
                ["DeleteMessages"] = defaults.Rights.DeleteMessages,
                ["InviteUsers"] = defaults.Rights.InviteUsers,
                ["RestrictMembers"] = defaults.Rights.RestrictMembers,
                ["PinMessages"] = defaults.Rights.PinMessages,
                ["PromoteMembers"] = defaults.Rights.PromoteMembers
            };
            root["BotChannelAdminDefaults"] = section;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var updatedJson = JsonSerializer.Serialize(root, options);
            await LocalConfigFile.WriteJsonAtomicallyAsync(_configFilePath, updatedJson, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static bool ReadBool(JsonObject obj, string key)
    {
        if (obj == null || string.IsNullOrWhiteSpace(key))
            return false;

        if (obj[key] is not JsonValue v)
            return false;

        if (v.TryGetValue<bool>(out var b))
            return b;

        if (v.TryGetValue<int>(out var i))
            return i != 0;

        if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out var sb))
            return sb;

        return false;
    }
}
