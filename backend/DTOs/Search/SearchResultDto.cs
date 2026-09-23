using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Search
{
    public class SearchItemDto
    {
        public string ResultType { get; set; } = string.Empty; // "Hospital", "Doctor", "User"
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string SubText { get; set; } = string.Empty;
        public string? ExtraInfo { get; set; }
        public string? AvatarInitial { get; set; }
        public string Route { get; set; } = string.Empty;
    }

    public class SearchResponseDto
    {
        public List<SearchItemDto> Hospitals { get; set; } = new();
        public List<SearchItemDto> Doctors { get; set; } = new();
        public List<SearchItemDto> Users { get; set; } = new();
        public int TotalCount => Hospitals.Count + Doctors.Count + Users.Count;
    }
}
