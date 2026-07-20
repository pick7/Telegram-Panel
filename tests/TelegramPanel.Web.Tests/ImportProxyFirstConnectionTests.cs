using System.Reflection;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;
using WTelegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ImportProxyFirstConnectionTests
{
    [Theory]
    [InlineData(OutboundProxyProtocols.Http)]
    [InlineData(OutboundProxyProtocols.Socks5)]
    [InlineData(OutboundProxyProtocols.MtProto)]
    public async Task 已有代理会在首次导入验证前传给SessionImporter(string protocol)
    {
        await using var fixture = await ImportFixture.CreateAsync(protocol);
        fixture.Importer.BeforeImport = () => Assert.Empty(fixture.Db.Accounts);

        var result = await fixture.Service.ImportFromStringSessionAsync(
            "session-data",
            12345,
            "0123456789abcdef0123456789abcdef",
            proxyBinding: new AccountProxyBindingInput("existing", fixture.Proxy.Id));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(fixture.Importer.SeenProxy);
        Assert.Equal(fixture.Proxy.Id, fixture.Importer.SeenProxy!.ProxyId);
        Assert.Equal(protocol, fixture.Importer.SeenProxy.Protocol);
        Assert.Equal(
            fixture.Proxy.Id,
            await fixture.Db.Accounts.AsNoTracking().Select(x => x.ProxyId).SingleAsync());
    }

    [Fact]
    public async Task 显式直连首次验证不携带代理并在入库后关闭全局代理()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);
        fixture.Importer.BeforeImport = () => Assert.Empty(fixture.Db.Accounts);

        var result = await fixture.Service.ImportFromStringSessionAsync(
            "session-data",
            12345,
            "0123456789abcdef0123456789abcdef",
            proxyBinding: new AccountProxyBindingInput("direct"));

        Assert.True(result.Success, result.Error);
        Assert.Null(fixture.Importer.SeenProxy);
        var account = await fixture.Db.Accounts.AsNoTracking().SingleAsync();
        Assert.Null(account.ProxyId);
        Assert.False(account.UseGlobalProxy);
    }

    [Fact]
    public async Task 全局策略首次验证即使用Telegram全局代理并保留继承模式()
    {
        await using var fixture = await ImportFixture.CreateAsync(
            OutboundProxyProtocols.Http,
            new Dictionary<string, string?>
            {
                ["Telegram:Proxy:Server"] = "127.0.0.2",
                ["Telegram:Proxy:Port"] = "1088",
                ["Telegram:Proxy:Username"] = "global-user",
                ["Telegram:Proxy:Password"] = "global-pass"
            });

        var result = await fixture.Service.ImportFromStringSessionAsync(
            "session-data",
            12345,
            "0123456789abcdef0123456789abcdef",
            proxyBinding: new AccountProxyBindingInput("global"));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(fixture.Importer.SeenProxy);
        Assert.Equal(OutboundProxyProtocols.Socks5, fixture.Importer.SeenProxy!.Protocol);
        Assert.Equal("127.0.0.2", fixture.Importer.SeenProxy.Host);
        Assert.Equal(1088, fixture.Importer.SeenProxy.Port);
        var account = await fixture.Db.Accounts.AsNoTracking().SingleAsync();
        Assert.Null(account.ProxyId);
        Assert.True(account.UseGlobalProxy);
    }

    [Fact]
    public async Task WARP环境不可用时不会开始首次Telegram验证()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);

        var result = await fixture.Service.ImportFromStringSessionAsync(
            "session-data",
            12345,
            "0123456789abcdef0123456789abcdef",
            proxyBinding: new AccountProxyBindingInput("warp_per_account"));

        Assert.False(result.Success);
        Assert.Equal(0, fixture.Importer.ImportCount);
        Assert.Empty(await fixture.Db.Accounts.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.OutboundProxies.AsNoTracking()
            .Where(x => x.Kind == OutboundProxyKinds.Warp)
            .ToListAsync());
    }

    [Fact]
    public async Task 缺省导入策略会在首次验证使用全局代理并保存继承模式()
    {
        await using var fixture = await ImportFixture.CreateAsync(
            OutboundProxyProtocols.Http,
            new Dictionary<string, string?>
            {
                ["Telegram:Proxy:Server"] = "127.0.0.3",
                ["Telegram:Proxy:Port"] = "1090"
            });

        var result = await fixture.Service.ImportFromStringSessionAsync(
            "session-data",
            12345,
            "0123456789abcdef0123456789abcdef");

        Assert.True(result.Success, result.Error);
        Assert.Equal("127.0.0.3", fixture.Importer.SeenProxy?.Host);
        var account = await fixture.Db.Accounts.AsNoTracking().SingleAsync();
        Assert.Null(account.ProxyId);
        Assert.True(account.UseGlobalProxy);
    }

    [Fact]
    public async Task 相同来源的两次Resin导入使用不同临时Lease身份()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);
        fixture.Proxy.Kind = OutboundProxyKinds.Resin;
        fixture.Proxy.ResinPlatform = "Default";
        await fixture.Db.SaveChangesAsync();

        var first = await fixture.Service.ImportFromStringSessionAsync(
            "session-data-1",
            12345,
            "0123456789abcdef0123456789abcdef",
            proxyBinding: new AccountProxyBindingInput("existing", fixture.Proxy.Id));
        var second = await fixture.Service.ImportFromStringSessionAsync(
            "session-data-2",
            12345,
            "0123456789abcdef0123456789abcdef",
            proxyBinding: new AccountProxyBindingInput("existing", fixture.Proxy.Id));

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        var usernames = fixture.Importer.SeenProxies
            .Select(x => x?.Username)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        Assert.Equal(2, usernames.Length);
        Assert.All(usernames, username => Assert.StartsWith("Default.tg_import_", username));
        Assert.NotEqual(usernames[0], usernames[1]);
    }

    [Fact]
    public async Task Session批量导入在创建WARP前拒绝超过十个账号()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);
        var files = Enumerable.Range(1, AccountImportService.MaxPerAccountWarpBatchSize + 1)
            .Select(index => new AccountImportFile(
                $"{index}.session",
                new MemoryStream(new byte[] { 1, 2, 3 })))
            .ToList();

        try
        {
            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                fixture.Service.ImportFromSessionFileStreamsAsync(
                    files,
                    12345,
                    "0123456789abcdef0123456789abcdef",
                    proxyBinding: new AccountProxyBindingInput("warp_per_account")));

            Assert.Contains("最多处理 10 个账号", error.Message);
            Assert.Equal(0, fixture.Importer.ImportCount);
        }
        finally
        {
            foreach (var file in files)
                await file.Content.DisposeAsync();
        }
    }

    [Fact]
    public async Task Zip批量导入在创建WARP前拒绝超过十个账号()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);
        await using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 1; index <= AccountImportService.MaxPerAccountWarpBatchSize + 1; index++)
            {
                var entry = archive.CreateEntry($"{index}/{index}.json");
                await using var content = entry.Open();
                await content.WriteAsync(Encoding.UTF8.GetBytes("{}"));
            }
        }
        zipStream.Position = 0;

        var results = await fixture.Service.ImportFromZipStreamAsync(
            "many-accounts.zip",
            zipStream,
            proxyBinding: new AccountProxyBindingInput("warp_per_account"));

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("最多处理 10 个账号", result.Error);
        Assert.Equal(0, fixture.Importer.ImportCount);
    }

    [Fact]
    public async Task Zip条目数超限会在解压前整体拒绝()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);
        await using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < 5_001; index++)
                archive.CreateEntry($"empty/{index}.txt");
        }
        zipStream.Position = 0;

        var results = await fixture.Service.ImportFromZipStreamAsync("too-many.zip", zipStream);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("条目数超过上限", result.Error);
        Assert.Equal(0, fixture.Importer.ImportCount);
    }

    [Fact]
    public async Task 批量导入重复手机号不会再次覆盖已保存账号()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);
        var directory = Path.Combine(Path.GetTempPath(), $"telegram-panel-duplicate-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "8613800000200.session");
        fixture.Importer.ResultFactory = count =>
        {
            var replacement = AtomicSessionFileReplacement.Create(target);
            File.WriteAllText(replacement.StagingPath, $"session-{count}");
            replacement.Apply();
            return new ImportResult(
                true,
                "8613800000200",
                10200,
                $"imported-{count}",
                target)
            {
                PendingSessionReplacement = replacement
            };
        };
        var files = new[]
        {
            new AccountImportFile("first.session", new MemoryStream(new byte[] { 1 })),
            new AccountImportFile("second.session", new MemoryStream(new byte[] { 2 }))
        };

        try
        {
            var results = await fixture.Service.ImportFromSessionFileStreamsAsync(
                files,
                12345,
                "0123456789abcdef0123456789abcdef",
                proxyBinding: new AccountProxyBindingInput("direct"));

            Assert.Equal(2, results.Count);
            Assert.True(results[0].Success, results[0].Error);
            Assert.False(results[1].Success);
            Assert.Equal("重复账号已跳过", results[1].Error);
            var account = await fixture.Db.Accounts.AsNoTracking().SingleAsync();
            Assert.Equal("imported-1", account.Username);
            Assert.Equal(target, account.SessionPath);
            Assert.Equal("session-1", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.rollback-*.session"));
        }
        finally
        {
            foreach (var file in files)
                await file.Content.DisposeAsync();
        }
    }

    [Fact]
    public async Task 数据库保存失败时会恢复被替换的旧Session()
    {
        await using var fixture = await ImportFixture.CreateAsync(OutboundProxyProtocols.Http);
        var directory = Path.Combine(Path.GetTempPath(), $"telegram-panel-atomic-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "8613800000201.session");
        await File.WriteAllTextAsync(target, "old-session");
        fixture.Importer.ResultFactory = _ =>
        {
            var replacement = AtomicSessionFileReplacement.Create(target);
            File.WriteAllText(replacement.StagingPath, "new-session");
            replacement.Apply();
            return new ImportResult(
                true,
                "8613800000201",
                10201,
                "atomic-import",
                target)
            {
                PendingSessionReplacement = replacement
            };
        };

        var result = await fixture.Service.ImportFromStringSessionAsync(
            "session-data",
            12345,
            "0123456789abcdef0123456789abcdef",
            categoryId: int.MaxValue,
            proxyBinding: new AccountProxyBindingInput("direct"));

        Assert.False(result.Success);
        Assert.Contains("文件已回滚", result.Error);
        Assert.Equal("old-session", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.rollback-*.session"));
        Assert.Empty(await fixture.Db.Accounts.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task 压缩包导入数据库失败时也会恢复旧Session()
    {
        var sessionsPath = Path.Combine(Path.GetTempPath(), $"telegram-panel-package-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionsPath);
        await using var fixture = await ImportFixture.CreateAsync(
            OutboundProxyProtocols.Http,
            new Dictionary<string, string?>
            {
                ["Telegram:SessionsPath"] = sessionsPath
            });
        var target = Path.Combine(sessionsPath, "8613800000202.session");
        await File.WriteAllTextAsync(target, "old-package-session");
        await using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var jsonEntry = archive.CreateEntry("account/account.json");
            await using (var json = jsonEntry.Open())
            {
                await json.WriteAsync(Encoding.UTF8.GetBytes(
                    "{\"api_id\":12345,\"api_hash\":\"0123456789abcdef0123456789abcdef\",\"phone\":\"+8613800000202\",\"user_id\":10202}"));
            }

            var sessionEntry = archive.CreateEntry("account/account.session");
            await using var session = sessionEntry.Open();
            await session.WriteAsync(Encoding.UTF8.GetBytes("new-package-session"));
        }
        zipStream.Position = 0;

        await fixture.Db.DisposeAsync();
        var results = await fixture.Service.ImportFromZipStreamAsync(
            "atomic-package.zip",
            zipStream,
            proxyBinding: new AccountProxyBindingInput("direct"));

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Equal("old-package-session", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.EnumerateFiles(sessionsPath, "*.rollback-*.session"));
    }

    [Theory]
    [InlineData(OutboundProxyProtocols.Http)]
    [InlineData(OutboundProxyProtocols.Socks5)]
    public void Http与Socks5导入验证使用统一Tcp连接器(string protocol)
    {
        using var client = CreateClient();
        ApplyImportProxy(client, NewOptions(protocol));

        Assert.NotNull(client.TcpHandler);
    }

    [Fact]
    public void MTProto导入验证配置MTProxyUrl()
    {
        using var client = CreateClient();
        ApplyImportProxy(client, NewOptions(OutboundProxyProtocols.MtProto));

        Assert.Equal(
            "https://t.me/proxy?server=127.0.0.1&port=1080&secret=abcdef",
            client.MTProxyUrl);
    }

    private static Client CreateClient()
    {
        var sessionPath = Path.Combine(
            Path.GetTempPath(),
            $"telegram-panel-import-proxy-test-{Guid.NewGuid():N}.session");
        string Config(string what) => what switch
        {
            "api_id" => "12345",
            "api_hash" => "0123456789abcdef0123456789abcdef",
            "session_pathname" => sessionPath,
            "session_key" => "0123456789abcdef0123456789abcdef",
            _ => null!
        };
        return new Client(Config);
    }

    private static void ApplyImportProxy(Client client, ProxyConnectionOptions options)
    {
        var configurator = typeof(SessionImporter).Assembly.GetType(
            "TelegramPanel.Core.Services.Telegram.TelegramImportProxyConfigurator",
            throwOnError: true)!;
        var apply = configurator.GetMethod(
            "Apply",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("未找到导入代理配置方法");
        apply.Invoke(null, new object?[] { client, options, CancellationToken.None });
    }

    private static ProxyConnectionOptions NewOptions(string protocol) => new(
        7,
        "import-proxy",
        OutboundProxyKinds.Manual,
        protocol,
        "127.0.0.1",
        1080,
        "user",
        "password",
        protocol == OutboundProxyProtocols.MtProto ? "abcdef" : null);

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ImportFixture(
            SqliteConnection connection,
            AppDbContext db,
            RecordingSessionImporter importer,
            AccountImportService service,
            OutboundProxy proxy)
        {
            _connection = connection;
            Db = db;
            Importer = importer;
            Service = service;
            Proxy = proxy;
        }

        public AppDbContext Db { get; }
        public RecordingSessionImporter Importer { get; }
        public AccountImportService Service { get; }
        public OutboundProxy Proxy { get; }

        public static async Task<ImportFixture> CreateAsync(
            string protocol,
            IEnumerable<KeyValuePair<string, string?>>? configurationValues = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var proxy = new OutboundProxy
            {
                Name = $"import-{protocol}",
                Kind = OutboundProxyKinds.Manual,
                Protocol = protocol,
                Host = "127.0.0.1",
                Port = 1080,
                Secret = protocol == OutboundProxyProtocols.MtProto ? "abcdef" : null,
                IsEnabled = true
            };
            db.OutboundProxies.Add(proxy);
            await db.SaveChangesAsync();

            var configurationBuilder = new ConfigurationBuilder();
            if (configurationValues != null)
                configurationBuilder.AddInMemoryCollection(configurationValues);
            var configuration = configurationBuilder.Build();
            var pool = new StubClientPool();
            var probe = new ProxyEgressProbeService();
            var warp = new WarpContainerManager(
                db,
                configuration,
                probe,
                NullLogger<WarpContainerManager>.Instance);
            var proxyManagement = new ProxyManagementService(
                db,
                pool,
                probe,
                warp,
                NullLogger<ProxyManagementService>.Instance);
            var accountManagement = new AccountManagementService(
                new AccountRepository(db),
                new ChannelRepository(db),
                new GroupRepository(db),
                pool,
                configuration,
                NullLogger<AccountManagementService>.Instance,
                proxyManagement,
                new SessionPathResolver(configuration));
            var importer = new RecordingSessionImporter();
            var service = new AccountImportService(
                importer,
                db,
                accountManagement,
                NullLogger<AccountImportService>.Instance,
                configuration,
                proxyManagement);

            return new ImportFixture(connection, db, importer, service, proxy);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingSessionImporter : ISessionImporter
    {
        public Action? BeforeImport { get; set; }
        public Func<int, ImportResult>? ResultFactory { get; set; }
        public ProxyConnectionOptions? SeenProxy { get; private set; }
        public List<ProxyConnectionOptions?> SeenProxies { get; } = new();
        public int ImportCount { get; private set; }

        public Task<ImportResult> ImportFromSessionFileAsync(
            string filePath,
            int apiId,
            string apiHash,
            long? userId = null,
            string? phoneHint = null,
            string? sessionKey = null,
            ProxyConnectionOptions? proxy = null,
            CancellationToken cancellationToken = default) =>
            ImportAsync(proxy);

        public async Task<List<ImportResult>> BatchImportSessionFilesAsync(
            string[] filePaths,
            int apiId,
            string apiHash,
            ProxyConnectionOptions? proxy = null,
            CancellationToken cancellationToken = default) =>
            new() { await ImportAsync(proxy) };

        public Task<ImportResult> ImportFromStringSessionAsync(
            string sessionString,
            int apiId,
            string apiHash,
            ProxyConnectionOptions? proxy = null,
            CancellationToken cancellationToken = default) =>
            ImportAsync(proxy);

        public Task<bool> ValidateSessionAsync(string sessionPath) => Task.FromResult(true);

        private Task<ImportResult> ImportAsync(ProxyConnectionOptions? proxy)
        {
            ImportCount++;
            SeenProxy = proxy;
            SeenProxies.Add(proxy);
            BeforeImport?.Invoke();
            return Task.FromResult(ResultFactory?.Invoke(ImportCount) ?? new ImportResult(
                true,
                "8613800000000",
                10001,
                "imported",
                "sessions/8613800000000.session"));
        }
    }

    private sealed class StubClientPool : ITelegramClientPool
    {
        public int ActiveClientCount => 0;

        public Task<Client> GetOrCreateClientAsync(
            int accountId,
            int apiId,
            string apiHash,
            string sessionPath,
            string? sessionKey = null,
            string? phoneNumber = null,
            long? userId = null) =>
            throw new NotSupportedException();

        public Client? GetClient(int accountId) => null;
        public Task RemoveClientAsync(int accountId) => Task.CompletedTask;
        public Task RemoveAllClientsAsync() => Task.CompletedTask;
        public bool IsClientConnected(int accountId) => false;
    }
}
