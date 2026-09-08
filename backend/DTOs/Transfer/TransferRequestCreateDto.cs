using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Transfer
{
    public class TransferRequestCreateDto
    {
        [Required]
        public Guid SenderHospitalId { get; set; }

        [Required]
        public Guid ReceiverHospitalId { get; set; }

        [Required]
        public string BloodGroup { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "UnitsRequested must be greater than zero.")]
        public int UnitsRequested { get; set; }

        public string Notes { get; set; } = string.Empty;
    }
}
