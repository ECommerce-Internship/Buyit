namespace Buyit.Application.DTOs;

/// <summary>The summary returned after a bulk product import.</summary>
public class ImportResultDto
{
    // How many products were successfully created.
    public int AddedCount { get; set; }

    // How many rows were rejected because they failed validation.
    public int FailedCount { get; set; }

    // One entry per rejected row: which row, and why it failed.
    // "= new()" gives it an empty list so it is never null.
    public List<ImportRowError> Errors { get; set; } = new();
}

/// <summary>A single rejected row: its row number in the spreadsheet and the reason.</summary>
public class ImportRowError
{
    // The Excel row number (1-based, exactly as the admin sees it in Excel).
    public int Row { get; set; }

    // A human-readable explanation, e.g. "Price must be a number greater than 0."
    public string Reason { get; set; } = string.Empty;
}