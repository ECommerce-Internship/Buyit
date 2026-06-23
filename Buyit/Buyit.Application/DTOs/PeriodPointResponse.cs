namespace Buyit.Application.DTOs;

// One bucket on a time series, e.g. ("2026-06", 1234.50) or ("2026-06-23", 12).
public record PeriodPointResponse(string Period, decimal Value);