using System;
using System.Collections.Generic;

namespace Buyit.Domain.Exceptions
{
    public class ValidationException : Exception
    {   // Thrown when input data fails validation 
        public IDictionary<string, string[]> Errors { get; }

        public ValidationException()
            : base("One or more validation failures have occurred.")
        {
            Errors = new Dictionary<string, string[]>();
        }

        public ValidationException(IDictionary<string, string[]> errors)
            : base("One or more validation failures have occurred.")
        {
            Errors = errors;
        }
    }
}