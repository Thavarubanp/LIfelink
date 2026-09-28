namespace LifeLink.Entities
{
    /// <summary>
    /// An entity with an optimistic concurrency token. AppDbContext moves the token on whenever the entity changes
    /// (or a child row that depends on its current state is added), so two people changing it at the same moment
    /// never silently overwrite each other: the later save fails with 409.
    /// </summary>
    public interface IConcurrencyVersioned
    {
        int ConcurrencyToken { get; set; }
    }
}
