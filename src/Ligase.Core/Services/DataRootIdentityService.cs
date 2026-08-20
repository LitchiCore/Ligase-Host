using System.Text.Json;
using Ligase.Host.Core.Domain.Installation;

namespace Ligase.Host.Core.Services;

public sealed record DataRootIdentityState(
    string DataRoot,
    string InstanceIdentity,
    string StorageStatus,
    string BootstrapStatus);

public sealed class DataRootIdentityService(
    LigasePaths paths,
    InstallationLayout installationLayout)
{
    public DataRootIdentityState Read()
    {
        var root = Path.GetFullPath(paths.RootDirectory);
        var programDataInstances = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Ligase Host",
            "Instances"));
        var localAppData = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ligase Host"));
        var relative = Path.GetRelativePath(programDataInstances, root);
        var instanceId = Guid.Empty;
        var isProgramDataInstance =
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative) &&
            Guid.TryParse(relative, out instanceId) &&
            string.Equals(relative, instanceId.ToString("D"), StringComparison.OrdinalIgnoreCase);

        var bootstrapMatches = TryReadBootstrapDataRoot(
            installationLayout.BootstrapPath,
            out var bootstrapRoot) &&
            string.Equals(
                Path.GetFullPath(bootstrapRoot!),
                root,
                StringComparison.OrdinalIgnoreCase);

        var storageStatus = isProgramDataInstance
            ? "当前使用 ProgramData 独立实例目录"
            : root.StartsWith(
                localAppData + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(root, localAppData, StringComparison.OrdinalIgnoreCase)
                ? "当前仍使用 LocalAppData；尚未确认迁移到 ProgramData"
                : "当前使用显式自定义数据目录";
        return new DataRootIdentityState(
            root,
            isProgramDataInstance ? instanceId.ToString("D") : "非标准实例目录",
            storageStatus,
            bootstrapMatches
                ? "安装 bootstrap 与当前数据目录一致"
                : "未能确认安装 bootstrap 与当前数据目录一致");
    }

    private static bool TryReadBootstrapDataRoot(string path, out string? dataRoot)
    {
        dataRoot = null;
        try
        {
            if (!File.Exists(path)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("dataRoot", out var value) ||
                value.ValueKind != JsonValueKind.String)
                return false;
            dataRoot = value.GetString();
            return !string.IsNullOrWhiteSpace(dataRoot) && Path.IsPathFullyQualified(dataRoot);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}
