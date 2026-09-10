using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Doctors;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Doctors
{
    public class DoctorService : IDoctorService
    {
        private readonly AppDbContext _context;

        public DoctorService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<DoctorResponseDto> CreateDoctorAsync(CreateDoctorDto dto)
        {
            var hospital = await _context.Hospitals.FindAsync(dto.HospitalId);
            if (hospital == null)
            {
                throw new InvalidOperationException($"Hospital with ID {dto.HospitalId} was not found.");
            }

            var doctor = new Doctor
            {
                DoctorId = Guid.NewGuid(),
                HospitalId = dto.HospitalId,
                UserId = dto.UserId,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber ?? string.Empty,
                LicenseNumber = dto.LicenseNumber ?? string.Empty,
                Specialization = dto.Specialization ?? string.Empty,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.Doctors.AddAsync(doctor);
            await _context.SaveChangesAsync();

            return MapToResponseDto(doctor, hospital.Name);
        }

        public async Task<List<DoctorResponseDto>> GetDoctorsAsync(Guid? hospitalId = null)
        {
            var query = _context.Doctors
                .Include(d => d.Hospital)
                .AsQueryable();

            if (hospitalId.HasValue)
            {
                query = query.Where(d => d.HospitalId == hospitalId.Value);
            }

            var list = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
            return list.Select(d => MapToResponseDto(d, d.Hospital?.Name ?? string.Empty)).ToList();
        }

        public async Task<DoctorResponseDto?> GetDoctorByIdAsync(Guid doctorId)
        {
            var doctor = await _context.Doctors
                .Include(d => d.Hospital)
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId);

            return doctor == null ? null : MapToResponseDto(doctor, doctor.Hospital?.Name ?? string.Empty);
        }

        private static DoctorResponseDto MapToResponseDto(Doctor doctor, string hospitalName)
        {
            return new DoctorResponseDto
            {
                DoctorId = doctor.DoctorId,
                HospitalId = doctor.HospitalId,
                HospitalName = hospitalName,
                UserId = doctor.UserId,
                FirstName = doctor.FirstName,
                LastName = doctor.LastName,
                Email = doctor.Email,
                PhoneNumber = doctor.PhoneNumber,
                LicenseNumber = doctor.LicenseNumber,
                Specialization = doctor.Specialization,
                IsActive = doctor.IsActive,
                CreatedAt = doctor.CreatedAt
            };
        }
    }
}
