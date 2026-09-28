using System;

namespace LifeLink.Common
{
    /// <summary>
    /// The request is valid but conflicts with the record's current state (for example rejecting a registration that is
    /// no longer pending, or someone else changing the same record at the same moment). Returned as HTTP 409 by
    /// GlobalExceptionMiddleware; controllers let it pass (their InvalidOperationException handlers exclude it).
    /// </summary>
    public class ConflictException : InvalidOperationException
    {
        /// <summary>Shown to the loser of a race: the record changed between reading it and saving.</summary>
        public const string DefaultMessage = "This was just changed by someone else. Please refresh and try again.";

        public ConflictException(string message) : base(message) { }

        public ConflictException() : base(DefaultMessage) { }
    }
}
