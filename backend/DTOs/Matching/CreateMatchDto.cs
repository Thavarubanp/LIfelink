using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Matching
{
    public class CreateMatchDto
    {
        [Required]
        public Guid BloodRequestId { get; set; }

        [Required]
        public Guid DonorUserId { get; set; }

        [Required]
        public Guid DoctorId { get; set; }

        public string? Notes { get; set; }
    }
}
