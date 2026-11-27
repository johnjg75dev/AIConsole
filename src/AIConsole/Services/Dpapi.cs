using System.Security.Cryptography;
using System.Text;

namespace AIConsole.Services;

/// <summary>Windows DPAPI (current-user) protection for secrets like API keys.</summary>
public static class Dpapi
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIConsole.v1");

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = Encoding.UTF8.GetBytes(plain);
        var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public static string? Unprotect(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) return null;
        try
        {
            var bytes = Convert.FromBase64String(enc);
            var dec = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch
        {
            return null; // wrong user/machine or corrupted
        }
    }
}
