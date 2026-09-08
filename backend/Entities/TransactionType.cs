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
    }
}
