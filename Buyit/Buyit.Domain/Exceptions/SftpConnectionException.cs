namespace Buyit.Domain.Exceptions;

// Thrown when the SFTP server cannot be reached does a 502 Bad Gateway
public class SftpConnectionException : Exception
{
    public SftpConnectionException(string message) : base(message) { }
}