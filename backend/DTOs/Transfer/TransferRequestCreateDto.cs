using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Transfer
{
    /// <summary>
    /// Created by the signed-in hospital. "Request": ask the counterpart for blood. "Offer": send blood to the counterpart.
    /// </summary>
    public class TransferRequestCreateDto
    {
        [Required]
        public string TransferType { get; set; } = "Request";

        [Required]
        public Guid CounterpartHospitalId { get; set; }

        [Required]
        public string BloodGroup { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "UnitsRequested must be greater than zero.")]
        public int UnitsRequested { get; set; }

        public string Notes { get; set; } = string.Empty;
    }

    public class RejectTransferDto
    {
        public string Reason { get; set; } = string.Empty;
    }
}
