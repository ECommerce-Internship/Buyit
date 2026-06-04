using System;

namespace Buyit.Domain.Exceptions
{
    // This exception is thrown when a resource (like a Product or User) 
    // is searched for but does not exist in the database.
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message)
        {
        }
    }
}