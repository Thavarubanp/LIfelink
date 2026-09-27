namespace LifeLink.Entities
{
    public static class TransactionType
    {
        public const string InitialStock = "INITIAL_STOCK";
        public const string StockAddition = "ADD";
        public const string StockDeduction = "DEDUCT";
        public const string TransferIn = "TRANSFER_IN";
        public const string TransferOut = "TRANSFER_OUT";
        public const string EmergencyDispatch = "EMERGENCY_DISPATCH";
        public const string Adjustment = "ADJUSTMENT";
        public const string DonationCollected = "DONATION_COLLECTED";
        public const string Issued = "ISSUED";
        public const string Expired = "EXPIRED";
        public const string LegacyMigrated = "LEGACY_MIGRATED";
        public const string Seeded = "SEEDED";
        public const string Reserved = "RESERVED";                 // held for a transfer offer or hospital donation
        public const string Released = "RELEASED";                 // hold ended, packet available again
        public const string Donated = "DONATED";                   // given by this hospital to a blood request
        public const string DonationReceived = "DONATION_RECEIVED"; // received from another hospital for own request
    }
}
