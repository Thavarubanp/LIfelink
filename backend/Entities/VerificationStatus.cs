namespace LifeLink.Entities
{
    public enum VerificationStatus
    {
        Pending,
        Approved,
        Rejected,
        Superseded, // donor resubmitted answers; a newer report version replaced this one
        Closed      // no decision needed any more (donor withdrew, request fulfilled, deleted or expired)
    }
}
