namespace LifeLink.Entities
{
    /// <summary>
    /// Hospital registration state. The admin acts on Pending and AwaitingAdminReview; the hospital replies only while
    /// Rejected. A hospital reply moves Rejected to AwaitingAdminReview, and the hospital cannot reply again until the
    /// admin approves, rejects again, or asks for more information (which returns it to Rejected).
    /// </summary>
    public enum ApprovalStatus
    {
        Pending,
        Approved,
        Rejected,
        AwaitingAdminReview
    }
}
