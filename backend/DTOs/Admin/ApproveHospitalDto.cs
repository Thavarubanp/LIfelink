using System;

namespace LifeLink.DTOs.Admin
{
    public class ApproveHospitalDto
    {
        /// <summary>The latest conversation entry the admin had seen; a newer hospital reply makes the approval fail with 409.</summary>
        public Guid? LastSeenEntryId { get; set; }
    }
}
