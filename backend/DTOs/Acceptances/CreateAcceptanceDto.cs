using System;

namespace LifeLink.DTOs.Acceptances
{
    public class CreateAcceptanceDto
    {
        public Guid BloodRequestId { get; set; }
        public string DonorBloodGroup { get; set; } = string.Empty;
    }
}
