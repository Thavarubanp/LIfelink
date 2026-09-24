namespace LifeLink.Entities
{
    public enum TransferRequestStatus
    {
        Pending,
        Approved,
        Rejected,
        Completed,
        Cancelled // deleted by its creator while pending
    }
}
