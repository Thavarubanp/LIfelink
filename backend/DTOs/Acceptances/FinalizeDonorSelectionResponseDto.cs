namespace LifeLink.DTOs.Acceptances
{
    public class FinalizeDonorSelectionResponseDto
    {
        public int MatchedDonors { get; set; }
        public int RejectedDonors { get; set; }
        public int TotalFulfilledUnits { get; set; }
        public int RemainingUnits { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
