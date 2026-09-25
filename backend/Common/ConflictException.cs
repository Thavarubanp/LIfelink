using System;

namespace LifeLink.Common
{
    /// <summary>
    /// The request is valid but conflicts with the record's current state (for example rejecting a registration that is
    /// no longer pending). Returned as HTTP 409 by the controllers and GlobalExceptionMiddleware.
    /// </summary>
    public class ConflictException : InvalidOperationException
    {
        public ConflictException(string message) : base(message) { }
    }
}
