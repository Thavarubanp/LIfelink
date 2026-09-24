namespace LifeLink.Entities
{
    public enum AccountStatus
    {
        Active = 1,
        Pending = 2,
        Suspended = 3,
        Inactive = 4,
        Blocked = 5,   // permanently blocked by an admin (email kept to stop re-registration)
        Deleted = 6    // self-deleted and anonymized (email released for re-registration)
    }
}
