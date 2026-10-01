using System.Security.Cryptography;
using System.Text;
namespace DriverPilot.RemoteDiagnosis;

/// <summary>Windows DPAPI bound to the current user: the stored token is useless for other accounts and other PCs.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    static readonly byte[] Purpose = Encoding.UTF8.GetBytes("DriverPilot.RemoteDiagnosis.v1");
    public string Protect(string secret)
        => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Purpose, DataProtectionScope.CurrentUser));
    public string? Unprotect(string protectedValue)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Purpose, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}

/// <summary>Access and case reference in the per-user settings folder. Never holds report content or a plain token.</summary>
public sealed class FileRemoteStateStore : IRemoteStateStore
{
    const string FileName = "remote-diagnosis.json";
    public RemoteState Load() => Store.Read<RemoteState>(FileName) ?? new();
    public void Save(RemoteState state) => Store.Save(FileName, state);
}
