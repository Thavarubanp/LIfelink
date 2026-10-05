namespace LifeLink.DTOs.Attention
{
    /// <summary>Aggregate workflow items requiring action by the authenticated caller's role and scope.</summary>
    public class RoleAttentionDto
    {
        public int PendingHospitalVerifications { get; set; }
        public int PendingTransferResponses { get; set; }
        public int PendingScreeningReviews { get; set; }
    }
}
