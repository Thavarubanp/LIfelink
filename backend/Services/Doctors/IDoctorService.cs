using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Doctors;

namespace LifeLink.Services.Doctors
{
    public interface IDoctorService
    {
        Task<DoctorResponseDto> CreateDoctorAsync(CreateDoctorDto dto);
        Task<List<DoctorResponseDto>> GetDoctorsAsync(Guid? hospitalId = null);
        Task<DoctorResponseDto?> GetDoctorByIdAsync(Guid doctorId);
        Task DeleteDoctorAsync(Guid doctorId, Guid hospitalId);
    }
}
