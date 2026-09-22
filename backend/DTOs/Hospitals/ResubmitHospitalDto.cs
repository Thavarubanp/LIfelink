using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Hospitals
{
    public class ResubmitHospitalDto
    {
        public string? Name { get; set; }
        public string? LicenseNumber { get; set; }
        public string? RegistrationNumber { get; set; }
        public string? Address { get; set; }
        public string? ContactNumber { get; set; }
        public string? City { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonPhone { get; set; }
        public string? ContactPersonEmail { get; set; }
        public string? LicenseDocumentUrl { get; set; }
        public string? LicenseDocumentName { get; set; }
        public string? AccreditationDocumentUrl { get; set; }
        public string? AccreditationDocumentName { get; set; }
        public string? Comments { get; set; }
    }
}
