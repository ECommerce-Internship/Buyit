using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface ISftpImportService
{
    // Downloads the Excel file from SFTP and feeds it into the existing import service
    Task<ImportResultDto> ImportFromSftpAsync();
}