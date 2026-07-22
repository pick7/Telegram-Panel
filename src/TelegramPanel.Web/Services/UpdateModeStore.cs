using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 保存入口脚本在容器重启前需要读取的更新策略。
/// </summary>
public sealed class UpdateModeStore
{
    public const string FileName = "update-mode.txt";

    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public UpdateModeStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public string GetMode(string configuredMode)
    {
        try
        {
            var path = ResolvePath();
            if (File.Exists(path))
            {
                var persisted = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(persisted))
                    return SelfUpdateOptions.NormalizeMode(persisted);
            }
        }
        catch
        {
            // 策略文件读取失败时回退到应用配置，不能阻塞服务启动。
        }

        return SelfUpdateOptions.NormalizeMode(configuredMode);
    }

    public async Task<string> SetModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        var normalized = SelfUpdateOptions.NormalizeMode(mode);
        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{path}.tmp-{Environment.ProcessId}-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                normalized + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
            return normalized;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // 临时文件清理失败不应覆盖策略保存结果。
            }
        }
    }

    private string ResolvePath() => Path.Combine(
        StoragePathResolver.ResolveWritableRoot(_configuration, _environment),
        FileName);
}
