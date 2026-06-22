using System;

namespace Buyit.Domain.Exceptions
{
    // Thrown when an upstream/external service (e.g. the Gemini API) fails
    // or returns an unusable response. Maps to HTTP 502 Bad Gateway.
    public class ExternalServiceException : Exception
    {
        public ExternalServiceException(string message) : base(message)
        {
        }

        public ExternalServiceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}