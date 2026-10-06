using System;

namespace LifeLink.DTOs.Transfer
{
    public class TransferCounterpartAvailabilityDto
    {
        public Guid HospitalId { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public string BloodGroup { get; set; } = string.Empty;
        public int TransferableUnits { get; set; }
    }
}
