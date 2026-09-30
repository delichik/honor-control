using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using HonorControl.Contracts;

namespace HonorControl.Service;

internal sealed class ConfigurationStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HonorControl");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "service.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object sync = new();

    public ConfigurationStore() => EnsurePrivateDirectory();

    public bool AuthorizeOrEnroll(string? sid, int clientSession, int activeConsoleSession)
    {
        if (sid == null) return false;
        lock (sync)
        {
            ServiceDocument document = ReadDocument();
            if (document.OwnerSid == null)
            {
                if (clientSession < 0 || activeConsoleSession < 0 || clientSession != activeConsoleSession) return false;
                WriteDocument(document with { OwnerSid = sid });
                return true;
            }
            return string.Equals(document.OwnerSid, sid, StringComparison.OrdinalIgnoreCase);
        }
    }

    public DesiredConfiguration Load()
    {
        lock (sync) return ReadDocument().Desired;
    }

    public DesiredConfiguration Update(Func<DesiredConfiguration, DesiredConfiguration> update)
    {
        lock (sync)
        {
            ServiceDocument document = ReadDocument();
            DesiredConfiguration desired = update(document.Desired);
            Validate(desired);
            WriteDocument(document with { Desired = desired });
            return desired;
        }
    }

    private ServiceDocument ReadDocument()
    {
        if (!File.Exists(FilePath)) return new ServiceDocument();
        if ((File.GetAttributes(FilePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("服务配置不能是重解析点。");
        return JsonSerializer.Deserialize<ServiceDocument>(File.ReadAllText(FilePath, Encoding.UTF8))
            ?? throw new InvalidDataException("服务配置为空。");
    }

    private void WriteDocument(ServiceDocument document)
    {
        EnsurePrivateDirectory();
        string temporaryPath = FilePath + ".tmp";
        if (File.Exists(temporaryPath) && (File.GetAttributes(temporaryPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("服务配置临时文件不能是重解析点。");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, FilePath, true);
    }

    private static void Validate(DesiredConfiguration desired)
    {
        if ((desired.ChargeStart.HasValue || desired.ChargeEnd.HasValue)
            && (!desired.ChargeStart.HasValue || !desired.ChargeEnd.HasValue
                || desired.ChargeStart.Value < 0 || desired.ChargeStart.Value > 100
                || desired.ChargeEnd.Value < 1 || desired.ChargeEnd.Value > 100
                || desired.ChargeStart.Value >= desired.ChargeEnd.Value))
            throw new ArgumentOutOfRangeException(nameof(desired), "充电阈值必须满足 0 <= start < end <= 100。");
        if (desired.PerformanceMode is not (null or 1 or 2))
            throw new ArgumentOutOfRangeException(nameof(desired), "性能模式必须是智能或高能。");
    }

    private static void EnsurePrivateDirectory()
    {
        DirectoryInfo directory = Directory.CreateDirectory(DirectoryPath);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("服务配置目录不能是重解析点。");
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private sealed record ServiceDocument
    {
        public string? OwnerSid { get; init; }
        public DesiredConfiguration Desired { get; init; } = new();
    }
}
