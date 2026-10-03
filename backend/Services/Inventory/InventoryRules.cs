namespace LifeLink.Services.Inventory
{
    /// <summary>
    /// The single "below threshold" rule used everywhere (Phase 4): the backend low-stock list and DTO flag, the
    /// inventory check (agent and rule-based fallback) and the hospital dashboard (which reads the DTO flag).
    /// A group is low when its available units are strictly below its minimum threshold; at the threshold it is not low.
    /// A hospital "holds" a group for the help alert when its available units are strictly above its own threshold.
    /// </summary>
    public static class InventoryRules
    {
        public static bool IsBelowThreshold(int unitsAvailable, int minimumThreshold) => unitsAvailable < minimumThreshold;

        public static bool IsAboveThreshold(int unitsAvailable, int minimumThreshold) => unitsAvailable > minimumThreshold;
    }
}
