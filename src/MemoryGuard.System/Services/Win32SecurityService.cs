using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using MemoryGuard.Platform.Native;

namespace MemoryGuard.Platform.Services;

public sealed class Win32SecurityService
{
    public (bool IsValidSignature, string Publisher) VerifyFileSignature(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return (false, string.Empty);
        }

        string publisher = string.Empty;

        // Try getting publisher from X509 certificate
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            publisher = cert.GetNameInfo(X509NameType.SimpleName, false) ?? string.Empty;
        }
        catch
        {
            // Certificate not extractable, fallback to FileVersionInfo
        }

        if (string.IsNullOrWhiteSpace(publisher))
        {
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(filePath);
                publisher = fvi.CompanyName ?? string.Empty;
            }
            catch
            {
                // Ignore FileVersionInfo errors
            }
        }

        // Verify cryptographic trust using WinVerifyTrust
        var fileInfo = NativeMethods.WINTRUST_FILE_INFO.Create(filePath);
        var trustData = NativeMethods.WINTRUST_DATA.Create(ref fileInfo);

        try
        {
            int result = NativeMethods.WinVerifyTrust(
                IntPtr.Zero,
                NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2,
                ref trustData
            );

            // 0 (ERROR_SUCCESS) means signature is valid and trusted
            bool isTrusted = (result == 0);
            return (isTrusted, publisher);
        }
        catch
        {
            return (false, publisher);
        }
        finally
        {
            trustData.Free();
        }
    }
}
