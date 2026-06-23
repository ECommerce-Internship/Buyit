namespace Buyit.Domain.Exceptions;

// Thrown when the file path does not exist on the SFTP server, does 404
public class SftpPathNotFoundException : Exception
{
    public SftpPathNotFoundException(string message) : base(message) { }
}