using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Notification
{
    public class CreateRecommendationNotificationDto
    {
        [Required(ErrorMessage = "TargetFacilityId is required.")]
        public Guid TargetFacilityId { get; set; }

        public string TargetFacilityName { get; set; } = string.Empty;

        public string TargetFacilityType { get; set; } = "Hospital";

        public string RecommendationType { get; set; } = string.Empty;

        public string? NotificationType { get; set; }

        public Guid? RelatedFacilityId { get; set; }

        public string? RelatedFacilityName { get; set; }

        public string? RelatedFacilityType { get; set; }

        public string? BloodGroup { get; set; }

        public int RequiredUnits { get; set; }

        public int? AvailableSurplus { get; set; }

        public int? RecommendedTransferUnits { get; set; }

        [Required(ErrorMessage = "Title is required.")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Message is required.")]
        public string Message { get; set; } = string.Empty;
    }
}
