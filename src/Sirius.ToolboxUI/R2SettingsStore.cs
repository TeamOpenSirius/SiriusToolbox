using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sirius.ToolboxUI;

internal sealed record R2SavedSettings(
    string RootDirectory,
    string Endpoint,
    string Bucket,
    string KeyPrefix,
    string CustomMappings,
    string? AccessKeyId,
    string? SecretAccessKey,
    string? SessionToken,
    bool OnlyUploadChanged)
{
    public static R2SavedSettings Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        null,
        null,
        null,
        false);
}

internal static class R2SettingsStore
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const uint CryptProtectLocalMachine = 0x4;
    private const string UserScopePrefix = "u:";
    private const string MachineScopePrefix = "m:";
    private const int MaxProtectedValueBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiriusToolbox",
        "r2-settings.json");

    public static R2SavedSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return R2SavedSettings.Empty;

        try
        {
            var stored = JsonSerializer.Deserialize<StoredR2Settings>(
                File.ReadAllText(SettingsPath),
                JsonOptions);
            if (stored is null)
                return R2SavedSettings.Empty;

            return new R2SavedSettings(
                stored.RootDirectory ?? string.Empty,
                stored.Endpoint ?? string.Empty,
                stored.Bucket ?? string.Empty,
                stored.KeyPrefix ?? string.Empty,
                stored.CustomMappings ?? string.Empty,
                UnprotectOptional(stored.AccessKeyId),
                UnprotectOptional(stored.SecretAccessKey),
                UnprotectOptional(stored.SessionToken),
                stored.OnlyUploadChanged);
        }
        catch
        {
            // A damaged or copied settings file must not prevent the toolbox from starting.
            return R2SavedSettings.Empty;
        }
    }

    public static void Save(R2SavedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var path = SettingsPath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var stored = new StoredR2Settings
        {
            RootDirectory = settings.RootDirectory,
            Endpoint = settings.Endpoint,
            Bucket = settings.Bucket,
            KeyPrefix = settings.KeyPrefix,
            CustomMappings = settings.CustomMappings,
            AccessKeyId = ProtectOptional(settings.AccessKeyId),
            SecretAccessKey = ProtectOptional(settings.SecretAccessKey),
            SessionToken = ProtectOptional(settings.SessionToken),
            OnlyUploadChanged = settings.OnlyUploadChanged
        };

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(stored, JsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
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
                // The successfully written settings are more important than cleanup of a stale temp file.
            }
        }
    }

    internal static void VerifyProtectionRoundTrip()
    {
        const string original = "Sirius Toolbox R2 凭据自测";
        var protectedValue = Protect(original);
        if (string.Equals(protectedValue, original, StringComparison.Ordinal) ||
            !string.Equals(Unprotect(protectedValue), original, StringComparison.Ordinal))
        {
            throw new CryptographicException("R2 凭据 DPAPI 加密往返自测失败。");
        }
    }

    private static string? ProtectOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Protect(value.Trim());

    private static string? UnprotectOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Unprotect(value);

    private static string Protect(string value)
    {
        var input = Encoding.UTF8.GetBytes(value);
        if (TryProtect(input, CryptProtectUiForbidden, out var protectedValue, out var userError))
            return UserScopePrefix + Convert.ToBase64String(protectedValue);

        // Some non-interactive Windows environments do not have a usable user
        // profile/master key. Keep the settings usable there with a machine-scoped
        // DPAPI fallback; the settings file itself still stays in the user's local
        // application-data directory and never contains the secret in plaintext.
        if (TryProtect(
                input,
                CryptProtectUiForbidden | CryptProtectLocalMachine,
                out protectedValue,
                out var machineError))
        {
            return MachineScopePrefix + Convert.ToBase64String(protectedValue);
        }

        throw new CryptographicException(
            $"Windows DPAPI 加密失败（用户范围 {userError}；计算机范围 {machineError}）。" +
            $" 用户范围：{new Win32Exception(userError).Message}；计算机范围：{new Win32Exception(machineError).Message}");
    }

    private static bool TryProtect(byte[] input, uint flags, out byte[] protectedValue, out int error)
    {
        var inputPointer = Marshal.AllocHGlobal(input.Length);
        var output = default(DataBlob);
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var inputBlob = new DataBlob { Length = input.Length, Data = inputPointer };
            if (!CryptProtectData(
                    ref inputBlob,
                    "Sirius Toolbox R2 credentials",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    flags,
                    ref output))
            {
                error = Marshal.GetLastWin32Error();
                protectedValue = Array.Empty<byte>();
                return false;
            }

            error = 0;
            protectedValue = CopyBlob(output);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(inputPointer);
            FreeBlob(output);
        }
    }

    private static string Unprotect(string protectedValue)
    {
        byte[] input;
        var flags = CryptProtectUiForbidden;
        var hasScopePrefix = false;
        try
        {
            if (protectedValue.StartsWith(UserScopePrefix, StringComparison.Ordinal))
            {
                hasScopePrefix = true;
                protectedValue = protectedValue[UserScopePrefix.Length..];
            }
            else if (protectedValue.StartsWith(MachineScopePrefix, StringComparison.Ordinal))
            {
                hasScopePrefix = true;
                protectedValue = protectedValue[MachineScopePrefix.Length..];
                flags |= CryptProtectLocalMachine;
            }

            input = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("R2 凭据缓存格式无效。", exception);
        }

        var inputPointer = Marshal.AllocHGlobal(input.Length);
        var output = default(DataBlob);
        var description = IntPtr.Zero;
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var inputBlob = new DataBlob { Length = input.Length, Data = inputPointer };
            if (!CryptUnprotectData(
                    ref inputBlob,
                    out description,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    flags,
                    ref output))
            {
                var error = Marshal.GetLastWin32Error();
                // Values written by the original implementation had no scope
                // prefix. Try the machine scope for those legacy values.
                if (!hasScopePrefix && flags == CryptProtectUiForbidden)
                {
                    FreeBlob(output);
                    output = default;
                    if (CryptUnprotectData(
                            ref inputBlob,
                            out description,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            CryptProtectUiForbidden | CryptProtectLocalMachine,
                            ref output))
                    {
                        return Encoding.UTF8.GetString(CopyBlob(output));
                    }

                    error = Marshal.GetLastWin32Error();
                }

                throw new CryptographicException($"Windows DPAPI 解密失败（{error}）：{new Win32Exception(error).Message}");
            }

            return Encoding.UTF8.GetString(CopyBlob(output));
        }
        finally
        {
            Marshal.FreeHGlobal(inputPointer);
            FreeBlob(output);
            if (description != IntPtr.Zero)
                LocalFree(description);
        }
    }

    private static byte[] CopyBlob(DataBlob blob)
    {
        if (blob.Length < 0 || blob.Length > MaxProtectedValueBytes || blob.Data == IntPtr.Zero)
            throw new CryptographicException("R2 凭据缓存大小无效。");

        var result = new byte[blob.Length];
        if (result.Length > 0)
            Marshal.Copy(blob.Data, result, 0, result.Length);
        return result;
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
            LocalFree(blob.Data);
    }

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        [MarshalAs(UnmanagedType.LPWStr)] string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        ref DataBlob dataOut);

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        ref DataBlob dataOut);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    private sealed class StoredR2Settings
    {
        public string? RootDirectory { get; set; }
        public string? Endpoint { get; set; }
        public string? Bucket { get; set; }
        public string? KeyPrefix { get; set; }
        public string? CustomMappings { get; set; }
        public string? AccessKeyId { get; set; }
        public string? SecretAccessKey { get; set; }
        public string? SessionToken { get; set; }
        public bool OnlyUploadChanged { get; set; }
    }
}
