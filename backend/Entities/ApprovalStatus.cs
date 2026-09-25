namespace LifeLink.Entities
{
    /// <summary>
    /// A hospital registration stays Pending until the admin decides. A rejected registration stays Rejected while the
    /// admin and the hospital exchange comments and corrections, until the admin approves it.
    /// </summary>
    public enum ApprovalStatus
    {
        Pending,
        Approved,
        Rejected
    }
}
