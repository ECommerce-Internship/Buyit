using System;

namespace Buyit.Domain.Exceptions
{
    public class ForbiddenException : Exception
    {   // Thrown when a user is authenticated but lacks permission or autherization
        public ForbiddenException(string message) : base(message)
        {
        }
    }
}
