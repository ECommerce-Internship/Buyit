namespace Buyit.Application.DTOs;

// TB-156: result of one (bounded, re-runnable) embedding-backfill call.
//   Embedded  — products embedded in THIS call.
//   Failed    — products whose embedding call failed this run (they stay pending; safe to re-run).
//   Remaining — products that still have no embedding after this call. Re-run until this is 0.
public record BackfillEmbeddingsResponse(int Embedded, int Failed, int Remaining);
