using System.Runtime.InteropServices;
using System.Text;

namespace AthenaCompanion.Security;

internal sealed class OpenAiKeyProvider
{
    private const string CredentialTarget = "AthenaCompanion.OpenAI.ApiKey";
    private const string CredentialUserName = "OpenAI API Key";

    public OpenAiKeyLookupResult TryGetApiKey()
    {
        var savedKey = WindowsCredentialManager.TryRead(CredentialTarget);
        if (!string.IsNullOrWhiteSpace(savedKey))
        {
            return new OpenAiKeyLookupResult(savedKey, OpenAiKeySource.WindowsCredentialManager);
        }

        var environmentKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(environmentKey))
        {
            return new OpenAiKeyLookupResult(environmentKey, OpenAiKeySource.EnvironmentVariable);
        }

        return new OpenAiKeyLookupResult(null, OpenAiKeySource.None);
    }

    public bool HasSavedCredential() => !string.IsNullOrWhiteSpace(WindowsCredentialManager.TryRead(CredentialTarget));

    public void SaveApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }

        WindowsCredentialManager.Write(CredentialTarget, CredentialUserName, apiKey.Trim());
    }

    public void DeleteSavedApiKey() => WindowsCredentialManager.Delete(CredentialTarget);
}

internal sealed record OpenAiKeyLookupResult(string? ApiKey, OpenAiKeySource Source);

internal static class WindowsCredentialManager
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public static string? TryRead(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var credentialPointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static void Write(string target, string userName, string secret)
    {
        var secretBytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(secretBytes.Length);

        try
        {
            Marshal.Copy(secretBytes, 0, blob, secretBytes.Length);

            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = userName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Unable to save OpenAI API key. Win32 error: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static void Delete(string target)
    {
        if (!CredDelete(target, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            const int ErrorNotFound = 1168;
            if (error != ErrorNotFound)
            {
                throw new InvalidOperationException($"Unable to delete OpenAI API key. Win32 error: {error}");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
