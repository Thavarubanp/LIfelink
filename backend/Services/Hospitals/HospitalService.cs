using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Hospitals;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Hospitals
{
    public class HospitalService : IHospitalService
    {
        private readonly AppDbContext _context;

        public HospitalService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<HospitalResponseDto> CreateHospitalAsync(CreateHospitalDto dto)
        {
            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = dto.Name,
                LicenseNumber = dto.LicenseNumber ?? string.Empty,
                Address = dto.Address ?? string.Empty,
                ContactNumber = dto.ContactNumber ?? string.Empty,
                Email = dto.Email ?? string.Empty,
                IsVerified = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.Hospitals.AddAsync(hospital);
            await _context.SaveChangesAsync();

            return MapToResponseDto(hospital);
        }

        public async Task<List<HospitalResponseDto>> GetHospitalsAsync(bool? isVerified = null)
        {
            var query = _context.Hospitals.AsQueryable();

            if (isVerified.HasValue)
            {
                query = query.Where(h => h.IsVerified == isVerified.Value);
            }

            var list = await query.OrderByDescending(h => h.CreatedAt).ToListAsync();
            return list.Select(MapToResponseDto).ToList();
        }

        public async Task<HospitalResponseDto?> GetHospitalByIdAsync(Guid hospitalId)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            return hospital == null ? null : MapToResponseDto(hospital);
        }

        public async Task<HospitalResponseDto?> VerifyHospitalAsync(Guid hospitalId, bool isVerified)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            if (hospital == null) return null;

            hospital.IsVerified = isVerified;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return MapToResponseDto(hospital);
        }

        private static HospitalResponseDto MapToResponseDto(Hospital hospital)
        {
            return new HospitalResponseDto
            {
                HospitalId = hospital.HospitalId,
                Name = hospital.Name,
                LicenseNumber = hospital.LicenseNumber,
                Address = hospital.Address,
                ContactNumber = hospital.ContactNumber,
                Email = hospital.Email,
                IsVerified = hospital.IsVerified,
                CreatedAt = hospital.CreatedAt,
                UpdatedAt = hospital.UpdatedAt
            };
        }
    }
}
