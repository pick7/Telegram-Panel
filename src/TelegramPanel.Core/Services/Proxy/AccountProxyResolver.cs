using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Proxy;

/// <summary>
/// 解析账号当前代理，并为 Resin 生成稳定的账号身份。
/// </summary>
public sealed class AccountProxyResolver : IAccountProxyResolver
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AccountProxyResolver(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<AccountProxyResolution> ResolveAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0)
            return new AccountProxyResolution(null, true);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts
            .AsNoTracking()
            .Include(x => x.Proxy)
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);

        if (account == null)
            return new AccountProxyResolution(null, true);
        if (!account.ProxyId.HasValue)
            return new AccountProxyResolution(null, account.UseGlobalProxy);
        if (account.Proxy is not { IsEnabled: true } proxy)
            throw new InvalidOperationException($"账号 {accountId} 绑定的代理不可用，已阻止降级为直连");

        return new AccountProxyResolution(
            BuildConnectionOptions(proxy, $"tg_account_{accountId}"),
            false);
    }

    public static ProxyConnectionOptions BuildConnectionOptions(
        OutboundProxy proxy,
        string stableAccountKey)
    {
        var username = proxy.Username;
        var password = proxy.Password;

        if (proxy.Kind == OutboundProxyKinds.Resin)
        {
            var platform = string.IsNullOrWhiteSpace(proxy.ResinPlatform)
                ? "Default"
                : proxy.ResinPlatform.Trim();
            username = $"{platform}.{NormalizeResinAccount(stableAccountKey)}";
        }

        return new ProxyConnectionOptions(
            proxy.Id,
            proxy.Name,
            proxy.Kind,
            proxy.Protocol,
            proxy.Host,
            proxy.Port,
            username,
            password,
            proxy.Secret);
    }

    private static string NormalizeResinAccount(string value)
    {
        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')
            .ToArray();
        var normalized = new string(chars);
        return string.IsNullOrWhiteSpace(normalized) ? "telegram_panel" : normalized;
    }
}
