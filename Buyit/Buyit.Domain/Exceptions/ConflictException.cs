using System;

namespace Buyit.Domain.Exceptions
{
    public class ConflictException : Exception
    {   // Thrown when a business rule conflict occurs like duplicate emails 
        public ConflictException(string message) : base(message)
        {
        }
    }
}
