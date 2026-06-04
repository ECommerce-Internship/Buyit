using System;

namespace Buyit.Domain.Exceptions
{
    public class UnauthorizedException : Exception
    {   // Thrown when a user is not authenticated or their session has expired.
        public UnauthorizedException(string message) : base(message)
        {
        }
    }
}