using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// Which backend instance runs a background job (the sweep). The holder renews the lease every round; another
    /// instance takes over only after the lease has expired (the holder stopped). One atomic statement takes or renews
    /// it, so it also works through a transaction-mode connection pooler.
    /// </summary>
    public class BackgroundJobLease
    {
        public string Name { get; set; } = string.Empty;
        public string Holder { get; set; } = string.Empty;
        public DateTime LeasedUntil { get; set; }
    }
}
