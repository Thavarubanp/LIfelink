namespace LifeLink.Entities
{
    public enum BloodRequestStatus
    {
        Pending,
        Approved,
        Rejected,
        Completed,
        Cancelled,
        // Hospital verified the request and assigned a doctor; awaiting the doctor's approve/reject decision.
        Verified
    }
}
