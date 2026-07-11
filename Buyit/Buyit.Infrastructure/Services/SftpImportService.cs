using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Buyit.Domain.Exceptions;
using Renci.SshNet;

namespace Buyit.Infrastructure.Services;

public class SftpImportService : ISftpImportService
{
    private readonly SftpSettings _settings;
    private readonly IProductService _productService;
    private readonly ILogger<SftpImportService> _logger;

    public SftpImportService(
        IOptions<SftpSettings> settings,
        IProductService productService,
        ILogger<SftpImportService> logger)
    {
        _settings = settings.Value;
        _productService = productService;
        _logger = logger;
    }

    public async Task<ImportResultDto> ImportFromSftpAsync()
    {
        SftpClient? client = null;

        try
        {
            // Construction can itself throw (e.g. empty/missing Host or Username when the
            // Sftp:* config isn't set) — this must be inside the try too, otherwise a missing-
            // config problem surfaces as a raw 500 instead of a clean 502.
            client = new SftpClient(_settings.Host, _settings.Port, _settings.Username, _settings.Password);
            client.Connect();
            _logger.LogInformation("Connected to SFTP server at {Host}:{Port}", _settings.Host, _settings.Port);
        }
        catch (Exception ex)
        {
            // Connection failed (or client construction failed) — throw so controller can return 502
            _logger.LogError(ex, "Failed to connect to SFTP server at {Host}:{Port}", _settings.Host, _settings.Port);
            client?.Dispose();
            throw new SftpConnectionException($"Could not connect to SFTP server at {_settings.Host}:{_settings.Port}. {ex.Message}");
        }

        try
        {
            // Check file exists on the server before attempting download
            if (!client.Exists(_settings.FilePath))
            {
                _logger.LogWarning("File not found on SFTP server at path {FilePath}", _settings.FilePath);
                throw new Buyit.Domain.Exceptions.SftpFileNotFoundException($"File '{_settings.FilePath}' was not found on the SFTP server.");
            }

            // Download the file into a memory stream
            using var memoryStream = new MemoryStream();
            await client.DownloadFileAsync(_settings.FilePath, memoryStream);
            memoryStream.Position = 0;

            const long maxBytes = 10L * 1024 * 1024;
            if (memoryStream.Length == 0)
                throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
                { ["file"] = ["The file on the SFTP server is empty."] });
            if (memoryStream.Length > maxBytes)
                throw new Buyit.Domain.Exceptions.ValidationException(new Dictionary<string, string[]>
                { ["file"] = ["The file on the SFTP server exceeds the 10 MB limit."] });

            _logger.LogInformation("File downloaded from SFTP path {FilePath}, size {Size} bytes",
                _settings.FilePath, memoryStream.Length);

            // Feed the stream into the existing import service — no duplicate logic
            var result = await _productService.ImportAsync(memoryStream);

            _logger.LogInformation("Import completed. Added: {Added}, Failed: {Failed}",
                result.AddedCount, result.FailedCount);

            return result;
        }
        finally
        {
            // Always disconnect and dispose cleanly
            if (client.IsConnected)
                client.Disconnect();
            client.Dispose();
        }
    }
}