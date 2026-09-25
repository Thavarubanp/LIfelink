namespace LifeLink.Entities
{
    /// <summary>
    /// One entry in a hospital's registration conversation (stored as text in HospitalApprovalHistories.Status).
    /// The hospital's own status is <see cref="ApprovalStatus"/>; these values record what happened.
    /// </summary>
    public enum RegistrationEntryType
    {
        Submitted,      // the hospital registered
        Rejected,       // the admin rejected the pending registration
        AdminComment,   // admin message while the registration is rejected
        HospitalReply,  // hospital reply, optionally with corrected details or documents
        Approved        // the admin approved; the conversation is closed
    }
}
