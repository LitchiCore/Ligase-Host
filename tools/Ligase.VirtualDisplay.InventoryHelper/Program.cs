using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint DevpropTypeString = 0x00000012;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidData = 13;
    private const int ErrorNotFound = 1168;
    private const string TargetHardwareId = "ROOT\\SUDOMAKER\\SUDOVDA";
    private static readonly Guid DevpkeyDriverInfPath =
        new("A8B865DD-2E3D-4094-AD97-E593A70C75D6");
    private const uint DevpkeyDeviceDriverInfPathPid = 5;

    private sealed record Device(
        string InstanceId, bool Present, string Status, string DriverInf);
    private sealed record Result(int SchemaVersion, string State, Device[] Devices);
    private sealed record ValidationNode(
        string InstanceId, string[] HardwareIds, bool Present,
        string Status, string DriverInf);
    private sealed record ValidationFixture(int SchemaVersion, ValidationNode[] Nodes);

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--validate-fixture")
                return RunValidationFixture(args[1]);
            if (args.Length != 1 || args[0] != "--inventory")
                return Fail("inputInvalid", 10);
            if (IsValidationEnabled())
            {
                var behavior = Environment.GetEnvironmentVariable(
                    "LIGASE_VDISPLAY_INVENTORY_HELPER_BEHAVIOR");
                if (behavior == "hang")
                    Thread.Sleep(30000);
                if (behavior == "overflow")
                {
                    Console.Out.Write(new string('A', 70000));
                    return 0;
                }
                if (behavior == "stderr")
                {
                    Console.Error.Write("closed-validation-error");
                    return 0;
                }
                if (behavior is "stdoutPending" or "dualPending")
                {
                    Console.Out.Write("{");
                    Console.Out.Flush();
                    if (behavior == "stdoutPending")
                        Thread.Sleep(30000);
                }
                if (behavior is "stderrPending" or "dualPending")
                {
                    Console.Error.Write("x");
                    Console.Error.Flush();
                    Thread.Sleep(30000);
                }
                if (behavior == "overflowPending")
                {
                    Console.Out.Write(new string('A', 70000));
                    Console.Out.Flush();
                    Thread.Sleep(30000);
                }
            }
            var present = EnumerateInstanceIds(DigcfAllClasses | DigcfPresent);
            var devices = EnumerateMatches(present);
            if (devices.Count > 16)
                return Fail("resultInvalid", 13);
            Write(new Result(1, "available", devices.ToArray()));
            return 0;
        }
        catch (Win32Exception)
        {
            return Fail("inventoryUnavailable", 11);
        }
        catch
        {
            return Fail("inventoryInvalid", 12);
        }
    }

    private static int RunValidationFixture(string path)
    {
        if (!IsValidationEnabled())
            return Fail("validationUnavailable", 14);
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetPathRoot(fullPath), "D:\\",
                StringComparison.OrdinalIgnoreCase))
            return Fail("validationUnavailable", 14);
        var item = new FileInfo(fullPath);
        if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0 ||
            item.Length > 1024 * 1024)
            return Fail("validationUnavailable", 14);
        var fixture = JsonSerializer.Deserialize<ValidationFixture>(
            File.ReadAllText(fullPath, new UTF8Encoding(false, true)),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        if (fixture is null || fixture.SchemaVersion != 1 ||
            fixture.Nodes is null || fixture.Nodes.Length > 128)
            return Fail("validationInvalid", 15);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<Device>();
        foreach (var node in fixture.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.InstanceId) ||
                node.HardwareIds is null || node.Status is null ||
                node.DriverInf is null || !seen.Add(node.InstanceId) ||
                node.HardwareIds.Any(string.IsNullOrWhiteSpace))
                return Fail("validationInvalid", 15);
            if (!node.HardwareIds.Any(value =>
                    StringComparer.OrdinalIgnoreCase.Equals(value,
                        TargetHardwareId)))
                continue;
            devices.Add(new Device(node.InstanceId, node.Present,
                node.Status, node.DriverInf));
        }
        if (devices.Count > 16)
            return Fail("resultInvalid", 13);
        Write(new Result(1, "available", devices
            .OrderBy(itemValue => itemValue.InstanceId,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(itemValue => itemValue.InstanceId, StringComparer.Ordinal)
            .ToArray()));
        return 0;
    }

    private static bool IsValidationEnabled() =>
        Environment.GetEnvironmentVariable(
            "LIGASE_INSTALL_VALIDATION_HARNESS") == "1" &&
        Environment.GetEnvironmentVariable(
            "LIGASE_VDISPLAY_INVENTORY_HELPER_VALIDATION") == "1";

    private static List<Device> EnumerateMatches(HashSet<string> present)
    {
        var result = new List<Device>();
        using var set = SafeDeviceInfoSet.Open(DigcfAllClasses);
        foreach (var infoValue in set.Enumerate())
        {
            var info = infoValue;
            var hardwareIds = ReadMultiString(set.Handle, ref info, SpdrpHardwareId);
            if (!hardwareIds.Any(value =>
                    StringComparer.OrdinalIgnoreCase.Equals(value, TargetHardwareId)))
                continue;
            var instanceId = ReadInstanceId(set.Handle, ref info);
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new InvalidDataException();
            var isPresent = present.Contains(instanceId);
            var status = ReadStatus(info.DevInst, isPresent);
            var driverInf = ReadDriverInf(set.Handle, ref info);
            result.Add(new Device(instanceId, isPresent, status, driverInf));
        }
        return result
            .OrderBy(item => item.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> EnumerateInstanceIds(uint flags)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var set = SafeDeviceInfoSet.Open(flags);
        foreach (var infoValue in set.Enumerate())
        {
            var info = infoValue;
            if (!result.Add(ReadInstanceId(set.Handle, ref info)))
                throw new InvalidDataException();
        }
        return result;
    }

    private static string ReadInstanceId(IntPtr set, ref SpDevinfoData info)
    {
        SetupDiGetDeviceInstanceIdW(set, ref info, null, 0, out var required);
        if (required < 2 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var value = new StringBuilder(checked((int)required));
        if (!SetupDiGetDeviceInstanceIdW(set, ref info, value,
                required, out var actual) || actual != required)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return value.ToString();
    }

    private static string[] ReadMultiString(IntPtr set, ref SpDevinfoData info,
        uint property)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref info, property,
            out _, null, 0, out var required);
        var error = Marshal.GetLastWin32Error();
        if (error is ErrorInvalidData or ErrorNotFound)
            return Array.Empty<string>();
        if (error != ErrorInsufficientBuffer || required < 2 || required > 65536)
            throw new Win32Exception(error);
        var buffer = new byte[required];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property,
                out _, buffer, required, out var actual) || actual != required)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Encoding.Unicode.GetString(buffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ReadDriverInf(IntPtr set, ref SpDevinfoData info)
    {
        // devpkey.h: DEVPKEY_Device_DriverInfPath =
        // {A8B865DD-2E3D-4094-AD97-E593A70C75D6}, 5.
        var key = new Devpropkey
        {
            Fmtid = DevpkeyDriverInfPath,
            Pid = DevpkeyDeviceDriverInfPathPid
        };
        SetupDiGetDevicePropertyW(set, ref info, ref key, out var type,
            null, 0, out var required, 0);
        var error = Marshal.GetLastWin32Error();
        if (error is ErrorInvalidData or ErrorNotFound)
            return string.Empty;
        if (error != ErrorInsufficientBuffer || type != DevpropTypeString ||
            required < 2 || required > 32768)
            throw new Win32Exception(error);
        var buffer = new byte[required];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out type,
                buffer, required, out var actual, 0) || actual != required ||
            type != DevpropTypeString)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    private static string ReadStatus(uint devInst, bool present)
    {
        if (!present)
            return "Unknown";
        var result = CM_Get_DevNode_Status(out _, out var problem, devInst, 0);
        if (result != 0)
            throw new Win32Exception(checked((int)result));
        return problem == 0 ? "OK" : "Problem";
    }

    private static int Fail(string code, int exit)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            state = "failed",
            code,
            devices = Array.Empty<object>()
        }));
        return exit;
    }

    private static void Write(Result result) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Devpropkey
    {
        public Guid Fmtid;
        public uint Pid;
    }

    private sealed class SafeDeviceInfoSet : IDisposable
    {
        public IntPtr Handle { get; }
        private SafeDeviceInfoSet(IntPtr handle) => Handle = handle;
        public static SafeDeviceInfoSet Open(uint flags)
        {
            var handle = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, flags);
            if (handle == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new SafeDeviceInfoSet(handle);
        }
        public IEnumerable<SpDevinfoData> Enumerate()
        {
            for (uint index = 0; ; index++)
            {
                var info = new SpDevinfoData
                {
                    CbSize = checked((uint)Marshal.SizeOf<SpDevinfoData>())
                };
                if (SetupDiEnumDeviceInfo(Handle, index, ref info))
                {
                    yield return info;
                    continue;
                }
                const int noMoreItems = 259;
                var error = Marshal.GetLastWin32Error();
                if (error == noMoreItems)
                    yield break;
                throw new Win32Exception(error);
            }
        }
        public void Dispose() => SetupDiDestroyDeviceInfoList(Handle);
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid,
        string? enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index,
        ref SpDevinfoData info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set,
        ref SpDevinfoData info, StringBuilder? instanceId, uint size,
        out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set,
        ref SpDevinfoData info, uint property, out uint type, byte[]? buffer,
        uint size, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set,
        ref SpDevinfoData info, ref Devpropkey key, out uint type,
        byte[]? buffer, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status,
        out uint problemNumber, uint devInst, uint flags);
}
