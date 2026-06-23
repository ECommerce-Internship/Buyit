namespace Buyit.Application.Common;

// config class for SFTP connection settings.
// Bound from the "Sftp" section in appsettings.json via IOptions<SftpSettings>
public class SftpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FilePath { get; set; } = "/upload/products.xlsx";
}