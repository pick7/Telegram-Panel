using System.IO.Compression;
using System.Text.Json;
using TelegramPanel.Modules;

namespace TelegramPanel.Web.Modules;

public sealed class ModuleInstallerService
{
    private readonly ModuleLayout _layout;
    private readonly ModuleStateStore _stateStore;
    private readonly BuiltIn.BuiltInModuleCatalog _builtInCatalog;
    private readonly string _hostVersion;

    public ModuleInstallerService(ModuleLayout layout, ModuleStateStore stateStore, BuiltIn.BuiltInModuleCatalog builtInCatalog, string hostVersion)
    {
        _layout = layout;
        _stateStore = stateStore;
        _builtInCatalog = builtInCatalog;
        _hostVersion = hostVersion;
    }

    public async Task<IReadOnlyList<ModuleOverview>> GetOverviewAsync()
    {
        var state = await _stateStore.LoadAsync();
        var list = new List<ModuleOverview>();

        foreach (var item in state.Modules.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            var active = (item.ActiveVersion ?? "").Trim();
            ModuleManifest? manifest = null;
            string? manifestError = null;

            if (!string.IsNullOrWhiteSpace(active))
            {
                try
                {
                    if (item.BuiltIn)
                    {
                        if (_builtInCatalog.TryGetManifest(item.Id, out var m))
                            manifest = m;
                        else
                            manifestError = "内置模块未注册";
                    }
                    else
                    {
                        var manifestPath = Path.Combine(_layout.InstalledDir, item.Id, active, "manifest.json");
                        if (File.Exists(manifestPath))
                        {
                            var json = await File.ReadAllTextAsync(manifestPath);
                            manifest = JsonSerializer.Deserialize<ModuleManifest>(json);
                        }
                    }
                }
                catch (Exception ex)
                {
                    manifestError = ex.Message;
                }
            }

            list.Add(new ModuleOverview(
                Id: item.Id,
                Enabled: item.Enabled,
                ActiveVersion: item.ActiveVersion,
                LastGoodVersion: item.LastGoodVersion,
                InstalledVersions: item.InstalledVersions.OrderByDescending(x => x, StringComparer.Ordinal).ToList(),
                Manifest: manifest,
                ManifestError: manifestError,
                BuiltIn: item.BuiltIn));
        }

        return list;
    }

    public async Task<InstallResult> InstallAsync(Stream packageStream, string fileName, bool enableAfterInstall = false)
    {
        if (packageStream == null)
            throw new ArgumentNullException(nameof(packageStream));

        fileName = (fileName ?? "").Trim();
        if (fileName.Length == 0)
            fileName = "module.tpm";

        var installId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(_layout.StagingDir, installId);
        Directory.CreateDirectory(staging);

        try
        {
            using var buffer = new MemoryStream();
            await packageStream.CopyToAsync(buffer);
            buffer.Position = 0;

            // 1) 解压到 staging
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
            {
                var extractError = ExtractZipToDirectorySafe(zip, staging);
                if (!string.IsNullOrWhiteSpace(extractError))
                    return InstallResult.Fail(extractError);
            }

            // 2) 读取 manifest
            var manifestPath = Path.Combine(staging, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                // 兼容用户把“文件夹整体压缩”的情况：<root>/<folder>/manifest.json
                TryPromoteSingleRootFolder(staging);
            }

            if (!File.Exists(manifestPath))
                return InstallResult.Fail("缺少 manifest.json");

            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<ModuleManifest>(manifestJson);
            if (manifest == null)
                return InstallResult.Fail("manifest.json 格式无效");

            NormalizeManifest(manifest);
            var validateError = ValidateManifest(manifest);
            if (!string.IsNullOrWhiteSpace(validateError))
                return InstallResult.Fail(validateError);

            var hostCompatError = CheckHostCompatibility(manifest);
            if (!string.IsNullOrWhiteSpace(hostCompatError))
                return InstallResult.Fail(hostCompatError);

            // 3) 基础结构校验（entry assembly）
            var entryAssembly = (manifest.Entry?.Assembly ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(entryAssembly))
            {
                var entryPath = Path.Combine(staging, "lib", entryAssembly);
                if (!File.Exists(entryPath))
                    return InstallResult.Fail($"入口程序集不存在：lib/{entryAssembly}");
            }

            // 4) 写入 packages/<id>/<version>.tpm（便于回滚/留档）
            var packageDir = Path.Combine(_layout.PackagesDir, manifest.Id);
            Directory.CreateDirectory(packageDir);
            var packagePath = Path.Combine(packageDir, $"{manifest.Version}.tpm");
            if (!File.Exists(packagePath))
                await File.WriteAllBytesAsync(packagePath, buffer.ToArray());

            // 5) 安装目录：installed/<id>/<version>
            var targetDir = Path.Combine(_layout.InstalledDir, manifest.Id, manifest.Version);
            if (Directory.Exists(targetDir))
                return InstallResult.Fail("该版本已安装");

            Directory.CreateDirectory(Path.Combine(_layout.InstalledDir, manifest.Id));
            Directory.Move(staging, targetDir);

            // 6) 更新 state
            var state = await _stateStore.LoadAsync();
            var item = state.Modules.FirstOrDefault(m => string.Equals(m.Id, manifest.Id, StringComparison.Ordinal));
            if (item == null)
            {
                item = new ModuleStateItem { Id = manifest.Id, Enabled = false, BuiltIn = false };
                state.Modules.Add(item);
            }

            item.InstalledVersions ??= new List<string>();
            if (!item.InstalledVersions.Contains(manifest.Version, StringComparer.Ordinal))
                item.InstalledVersions.Add(manifest.Version);

            item.ActiveVersion ??= manifest.Version;
            if (enableAfterInstall)
                item.Enabled = true;

            await _stateStore.SaveAsync(state);

            return InstallResult.Ok(manifest.Id, manifest.Version);
        }
        catch (Exception ex)
        {
            return InstallResult.Fail(ex.Message);
        }
        finally
        {
            // 如果 staging 还在（未 move），清理
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    public async Task<OperationResult> EnableAsync(string id, string? version = null)
    {
        id = (id ?? "").Trim();
        version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        if (id.Length == 0)
            return OperationResult.Fail("id 不能为空");

        var state = await _stateStore.LoadAsync();
        var item = state.Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (item == null)
            return OperationResult.Fail("模块未安装");

        if (item.BuiltIn)
        {
            // 内置模块版本跟随宿主，不从 installed/<id>/<ver>/manifest.json 读取
            item.ActiveVersion = _hostVersion;
            item.InstalledVersions ??= new List<string>();
            if (!item.InstalledVersions.Contains(_hostVersion, StringComparer.Ordinal))
                item.InstalledVersions.Add(_hostVersion);

            if (!_builtInCatalog.TryGetManifest(id, out var builtInManifest))
                return OperationResult.Fail("内置模块未注册");

            var builtInHostCompatError = CheckHostCompatibility(builtInManifest);
            if (!string.IsNullOrWhiteSpace(builtInHostCompatError))
                return OperationResult.Fail(builtInHostCompatError);

            var builtInDepError = await CheckDependenciesAsync(state, builtInManifest);
            if (!string.IsNullOrWhiteSpace(builtInDepError))
                return OperationResult.Fail(builtInDepError);

            item.Enabled = true;
            await _stateStore.SaveAsync(state);
            return OperationResult.Ok();
        }

        if (!string.IsNullOrWhiteSpace(version))
            item.ActiveVersion = version;

        if (string.IsNullOrWhiteSpace(item.ActiveVersion))
            return OperationResult.Fail("未选择启用的版本");

        var manifest = await TryLoadManifestAsync(id, item.ActiveVersion);
        if (manifest == null)
            return OperationResult.Fail("无法读取 manifest.json");

        var hostCompatError = CheckHostCompatibility(manifest);
        if (!string.IsNullOrWhiteSpace(hostCompatError))
            return OperationResult.Fail(hostCompatError);

        var depError = await CheckDependenciesAsync(state, manifest);
        if (!string.IsNullOrWhiteSpace(depError))
            return OperationResult.Fail(depError);

        item.Enabled = true;
        await _stateStore.SaveAsync(state);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DisableAsync(string id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0)
            return OperationResult.Fail("id 不能为空");

        var state = await _stateStore.LoadAsync();
        var item = state.Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (item == null)
            return OperationResult.Fail("模块不存在");

        item.Enabled = false;
        await _stateStore.SaveAsync(state);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetActiveVersionAsync(string id, string version)
    {
        id = (id ?? "").Trim();
        version = (version ?? "").Trim();
        if (id.Length == 0 || version.Length == 0)
            return OperationResult.Fail("参数无效");

        var state = await _stateStore.LoadAsync();
        var item = state.Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (item == null)
            return OperationResult.Fail("模块不存在");

        if (item.BuiltIn)
            return OperationResult.Fail("内置模块版本随宿主，不支持切换");

        var dir = Path.Combine(_layout.InstalledDir, id, version);
        if (!Directory.Exists(dir))
            return OperationResult.Fail("该版本未安装");

        item.ActiveVersion = version;
        await _stateStore.SaveAsync(state);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> RemoveModuleAsync(string id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0)
            return OperationResult.Fail("id 不能为空");

        var state = await _stateStore.LoadAsync();
        var item = state.Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (item == null)
            return OperationResult.Fail("模块不存在");

        if (item.BuiltIn)
            return OperationResult.Fail("内置模块不允许删除");

        // 先禁用，避免重启后继续加载（即使后续物理删除失败）
        item.Enabled = false;
        await _stateStore.SaveAsync(state);

        try
        {
            // 直接删除，不保留到 trash
            var moduleDir = Path.Combine(_layout.InstalledDir, id);
            if (Directory.Exists(moduleDir))
                Directory.Delete(moduleDir, recursive: true);

            var packageDir = Path.Combine(_layout.PackagesDir, id);
            if (Directory.Exists(packageDir))
                Directory.Delete(packageDir, recursive: true);

            state.Modules.RemoveAll(m => string.Equals(m.Id, id, StringComparison.Ordinal));
            await _stateStore.SaveAsync(state);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"删除失败：{ex.Message}（建议先停用并重启后再删除）");
        }
    }

    public async Task<OperationResult> RemoveModuleVersionAsync(string id, string version)
    {
        id = (id ?? "").Trim();
        version = (version ?? "").Trim();
        if (id.Length == 0 || version.Length == 0)
            return OperationResult.Fail("参数无效");

        var state = await _stateStore.LoadAsync();
        var item = state.Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (item == null)
            return OperationResult.Fail("模块不存在");

        if (item.BuiltIn)
            return OperationResult.Fail("内置模块不支持删除版本");

        if (string.Equals((item.ActiveVersion ?? "").Trim(), version, StringComparison.Ordinal))
            return OperationResult.Fail("不能删除当前启用版本，请先切换 ActiveVersion");

        try
        {
            // 直接删除，不保留到 trash
            var versionDir = Path.Combine(_layout.InstalledDir, id, version);
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, recursive: true);

            var packageFile = Path.Combine(_layout.PackagesDir, id, $"{version}.tpm");
            if (File.Exists(packageFile))
                File.Delete(packageFile);

            item.InstalledVersions ??= new List<string>();
            item.InstalledVersions.RemoveAll(v => string.Equals(v, version, StringComparison.Ordinal));
            if (string.Equals((item.LastGoodVersion ?? "").Trim(), version, StringComparison.Ordinal))
                item.LastGoodVersion = null;

            // 如果删到一个版本都不剩，则等价于删除模块
            if (item.InstalledVersions.Count == 0)
                state.Modules.RemoveAll(m => string.Equals(m.Id, id, StringComparison.Ordinal));

            await _stateStore.SaveAsync(state);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"删除版本失败：{ex.Message}（建议先停用并重启后再删除）");
        }
    }

    public async Task<OperationResult> PruneOldVersionsAsync(string id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0)
            return OperationResult.Fail("id 不能为空");

        var state = await _stateStore.LoadAsync();
        var item = state.Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        if (item == null)
            return OperationResult.Fail("模块不存在");

        if (item.BuiltIn)
            return OperationResult.Fail("内置模块不支持清理版本");

        item.InstalledVersions ??= new List<string>();
        var active = (item.ActiveVersion ?? "").Trim();
        var lastGood = (item.LastGoodVersion ?? "").Trim();
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(active)) keep.Add(active);
        if (!string.IsNullOrWhiteSpace(lastGood)) keep.Add(lastGood);

        var toRemove = item.InstalledVersions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .Where(v => !keep.Contains(v))
            .ToList();

        if (toRemove.Count == 0)
            return OperationResult.Ok();

        foreach (var v in toRemove)
        {
            var r = await RemoveModuleVersionAsync(id, v);
            if (!r.Success)
                return r;
        }

        return OperationResult.Ok();
    }

    private async Task<ModuleManifest?> TryLoadManifestAsync(string id, string version)
    {
        try
        {
            var path = Path.Combine(_layout.InstalledDir, id, version, "manifest.json");
            if (!File.Exists(path))
                return null;
            var json = await File.ReadAllTextAsync(path);
            var manifest = JsonSerializer.Deserialize<ModuleManifest>(json);
            if (manifest == null)
                return null;
            NormalizeManifest(manifest);
            return manifest;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> CheckDependenciesAsync(ModuleState state, ModuleManifest manifest)
    {
        foreach (var dep in manifest.Dependencies ?? new List<ModuleDependency>())
        {
            var depId = (dep.Id ?? "").Trim();
            if (depId.Length == 0)
                return "依赖项缺少 id";

            var found = state.Modules.FirstOrDefault(m => string.Equals(m.Id, depId, StringComparison.Ordinal));
            if (found == null || string.IsNullOrWhiteSpace(found.ActiveVersion))
                return $"缺少依赖：{depId}";

            if (!found.Enabled)
                return $"依赖未启用：{depId}";

            if (!SemVer.TryParse(found.ActiveVersion, out var installed))
                return $"依赖版本无效：{depId} {found.ActiveVersion}";

            var rangeExpr = (dep.Range ?? "").Trim();
            if (!VersionRange.TryParse(rangeExpr, out var range, out var err))
                return $"依赖 range 无效：{depId} ({err})";

            if (!range.Contains(installed))
                return $"依赖不满足：{depId} 需要 {rangeExpr}，当前 {found.ActiveVersion}";
        }

        return null;
    }

    private static void TryPromoteSingleRootFolder(string stagingDir)
    {
        if (string.IsNullOrWhiteSpace(stagingDir))
            return;

        try
        {
            var manifest = Path.Combine(stagingDir, "manifest.json");
            if (File.Exists(manifest))
                return;

            var files = Directory.GetFiles(stagingDir)
                .Where(f => !string.Equals(Path.GetFileName(f), ".DS_Store", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var dirs = Directory.GetDirectories(stagingDir)
                .Where(d => !string.Equals(Path.GetFileName(d), "__MACOSX", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count != 0 || dirs.Count != 1)
                return;

            var root = dirs[0];
            var innerManifest = Path.Combine(root, "manifest.json");
            if (!File.Exists(innerManifest))
                return;

            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var target = Path.Combine(stagingDir, name);
                if (Directory.Exists(entry))
                {
                    if (Directory.Exists(target))
                        Directory.Delete(target, recursive: true);
                    Directory.Move(entry, target);
                }
                else if (File.Exists(entry))
                {
                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(entry, target);
                }
            }

            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // ignore：保持原行为（最终仍会报缺少 manifest.json）
        }
    }

    private string? CheckHostCompatibility(ModuleManifest manifest)
    {
        if (!SemVer.TryParse(_hostVersion, out var host))
            return null;

        var minOk = true;
        var maxOk = true;

        if (SemVer.TryParse(manifest.Host?.Min, out var min))
            minOk = host.CompareTo(min) >= 0;
        if (SemVer.TryParse(manifest.Host?.Max, out var max))
            maxOk = host.CompareTo(max) <= 0;

        if (!minOk || !maxOk)
            return $"宿主版本不兼容：当前 {_hostVersion}，要求 {manifest.Host?.Min ?? "-"} ~ {manifest.Host?.Max ?? "-"}";

        return null;
    }

    private static void NormalizeManifest(ModuleManifest manifest)
    {
        manifest.Id = (manifest.Id ?? "").Trim();
        manifest.Name = (manifest.Name ?? "").Trim();
        manifest.Version = (manifest.Version ?? "").Trim();
        manifest.Entry ??= new ModuleEntryPoint();
        manifest.Entry.Assembly = (manifest.Entry.Assembly ?? "").Trim();
        manifest.Entry.Type = (manifest.Entry.Type ?? "").Trim();
        manifest.Dependencies ??= new List<ModuleDependency>();
        manifest.Host ??= new HostCompatibility();
        manifest.Host.Min = (manifest.Host.Min ?? "").Trim();
        manifest.Host.Max = (manifest.Host.Max ?? "").Trim();
        foreach (var d in manifest.Dependencies)
        {
            d.Id = (d.Id ?? "").Trim();
            d.Range = (d.Range ?? "").Trim();
        }
    }

    private static string? ValidateManifest(ModuleManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            return "模块 id 不能为空";
        if (manifest.Id.Length > 100)
            return "模块 id 过长";
        if (!IsSafeId(manifest.Id))
            return "模块 id 仅允许字母/数字/.-_";

        if (string.IsNullOrWhiteSpace(manifest.Name))
            return "模块名称不能为空";
        if (manifest.Name.Length > 100)
            return "模块名称过长";

        if (!SemVer.TryParse(manifest.Version, out _))
            return "模块版本必须是 x.y.z";

        if (string.IsNullOrWhiteSpace(manifest.Entry.Assembly) || string.IsNullOrWhiteSpace(manifest.Entry.Type))
            return "入口点缺失（entry.assembly / entry.type）";

        return null;
    }

    private static bool IsSafeId(string id)
    {
        foreach (var ch in id)
        {
            if (char.IsLetterOrDigit(ch))
                continue;
            if (ch is '.' or '-' or '_' )
                continue;
            return false;
        }

        return true;
    }

    private static string? ExtractZipToDirectorySafe(ZipArchive zip, string destinationDir)
    {
        if (zip == null)
            throw new ArgumentNullException(nameof(zip));
        if (string.IsNullOrWhiteSpace(destinationDir))
            throw new ArgumentException("destinationDir 不能为空", nameof(destinationDir));

        var destFull = Path.GetFullPath(destinationDir);
        if (!destFull.EndsWith(Path.DirectorySeparatorChar))
            destFull += Path.DirectorySeparatorChar;

        foreach (var entry in zip.Entries)
        {
            var entryPath = (entry.FullName ?? string.Empty).Replace('\\', '/');
            if (entryPath.Length == 0)
                continue;

            // 禁止绝对路径
            if (entryPath.StartsWith("/", StringComparison.Ordinal) || entryPath.StartsWith("\\", StringComparison.Ordinal))
                return "压缩包包含非法路径（绝对路径）";

            var relative = entryPath.Replace('/', Path.DirectorySeparatorChar);
            var targetFull = Path.GetFullPath(Path.Combine(destFull, relative));
            if (!targetFull.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                return "压缩包包含非法路径（路径穿越）";

            // 目录条目（通常以 / 结尾，或 Name 为空）
            if (entryPath.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(targetFull);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFull) ?? destFull);
            entry.ExtractToFile(targetFull, overwrite: true);
        }

        return null;
    }
}

public sealed record ModuleOverview(
    string Id,
    bool Enabled,
    string? ActiveVersion,
    string? LastGoodVersion,
    IReadOnlyList<string> InstalledVersions,
    ModuleManifest? Manifest,
    string? ManifestError,
    bool BuiltIn);

public sealed record InstallResult(bool Success, string Message, string? ModuleId = null, string? Version = null)
{
    public static InstallResult Ok(string id, string version) => new(true, "ok", id, version);
    public static InstallResult Fail(string msg) => new(false, msg);
}

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok() => new(true, "ok");
    public static OperationResult Fail(string msg) => new(false, msg);
}
