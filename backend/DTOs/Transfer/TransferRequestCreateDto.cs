using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Transfer
{
    /// <summary>
    /// Created by the signed-in hospital. "Request": ask the counterpart for blood (by units). "Offer": send the
    /// packets listed in PacketIds to the counterpart (they are held until the offer is accepted, rejected or withdrawn).
    /// </summary>
    public class TransferRequestCreateDto
    {
        [Required]
        public string TransferType { get; set; } = "Request";

        [Required]
        public Guid CounterpartHospitalId { get; set; }

        [Required]
        public string BloodGroup { get; set; } = string.Empty;

        // Requests only; an offer's units are the number of selected packets
        public int UnitsRequested { get; set; }

        // Offers only: the sender's Available packets to send
        public List<Guid>? PacketIds { get; set; }

        public string Notes { get; set; } = string.Empty;
    }

    public class RejectTransferDto
    {
        public string Reason { get; set; } = string.Empty;
    }
}
