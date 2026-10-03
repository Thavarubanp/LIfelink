using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LifeLink.Common
{
    /// <summary>
    /// Save failures caused by two actions at the same moment: a concurrency token that moved on, or a unique index /
    /// foreign key the other action satisfied first. They become HTTP 409 with a clear message instead of a 500.
    /// </summary>
    public static class DatabaseConflicts
    {
        public const string DuplicateEmailMessage = "An account with this email address already exists.";
        public const string DuplicateActiveRequestMessage = "An active request already exists for this hospital and blood group.";
        public const string DuplicateOpenAppealMessage = "You already have an open appeal. Continue the conversation in that thread.";
        public const string AlreadySubmittedMessage = "This was already submitted. Please refresh to see the result.";

        /// <summary>The 409 message for a race-related save failure, or null when the failure is something else.</summary>
        public static string? ConflictMessage(DbUpdateException exception)
        {
            if (exception is DbUpdateConcurrencyException)
            {
                return ConflictException.DefaultMessage;
            }

            if (exception.InnerException is not PostgresException pg)
            {
                return null;
            }

            if (pg.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return pg.ConstraintName switch
                {
                    "IX_Users_Email" or "IX_Doctors_Email" => DuplicateEmailMessage,
                    "IX_BloodRequests_OneActivePerCreatorHospitalGroup" => DuplicateActiveRequestMessage,
                    "IX_Appeals_OneOpenPerUser" or "IX_Appeals_OneOpenPerHospital" => DuplicateOpenAppealMessage,
                    "PK_IdempotencyKeys" => AlreadySubmittedMessage,
                    _ => ConflictException.DefaultMessage
                };
            }

            return pg.SqlState == PostgresErrorCodes.ForeignKeyViolation ? ConflictException.DefaultMessage : null;
        }
    }
}
