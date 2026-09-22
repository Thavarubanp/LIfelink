using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Hospitals;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

using LifeLink.Services.Auth;
using LifeLink.Services.Common;

namespace LifeLink.Services.Hospitals
{
    public class HospitalService : IHospitalService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;

        public HospitalService(AppDbContext context, IPasswordHasherService? passwordHasher = null)
        {
            _context = context;
            _passwordHasher = passwordHasher ?? new PasswordHasherService();
        }

        public async Task<HospitalResponseDto> CreateHospitalAsync(CreateHospitalDto dto)
        {
            var normalizedEmail = dto.Email?.Trim().ToLowerInvariant() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(normalizedEmail))
            {
                if (await EmailUniquenessHelper.IsEmailTakenAsync(_context, normalizedEmail))
                {
                    throw new InvalidOperationException("An account with this email address already exists.");
                }
            }

            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = dto.Name?.Trim() ?? string.Empty,
                LicenseNumber = dto.LicenseNumber?.Trim() ?? string.Empty,
                Address = dto.Address?.Trim() ?? string.Empty,
                ContactNumber = dto.ContactNumber?.Trim() ?? string.Empty,
                Email = normalizedEmail,
                RegistrationNumber = dto.RegistrationNumber?.Trim() ?? dto.LicenseNumber?.Trim(),
                City = dto.City?.Trim(),
                ContactPersonName = dto.ContactPersonName?.Trim(),
                ContactPersonPhone = dto.ContactPersonPhone?.Trim(),
                ContactPersonEmail = dto.ContactPersonEmail?.Trim(),
                LicenseDocumentUrl = dto.LicenseDocumentUrl,
                LicenseDocumentName = dto.LicenseDocumentName,
                AccreditationDocumentUrl = dto.AccreditationDocumentUrl,
                AccreditationDocumentName = dto.AccreditationDocumentName,
                ApprovalStatus = ApprovalStatus.Pending,
                IsVerified = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.Hospitals.AddAsync(hospital);

            // Log initial approval history
            await _context.HospitalApprovalHistories.AddAsync(new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = ApprovalStatus.Pending,
                Timestamp = DateTime.UtcNow,
                Comments = "Hospital registration submitted."
            });

            // Create staff User account for authentication if password provided and user does not exist
            if (!string.IsNullOrWhiteSpace(normalizedEmail) && !string.IsNullOrWhiteSpace(dto.Password))
            {
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
                if (existingUser == null)
                {
                    var staffUser = new User
                    {
                        UserId = Guid.NewGuid(),
                        FirstName = dto.Name?.Trim() ?? "Hospital",
                        LastName = "Staff",
                        Email = normalizedEmail,
                        PhoneNumber = dto.ContactNumber?.Trim() ?? string.Empty,
                        Address = dto.Address?.Trim() ?? string.Empty,
                        AccountStatus = AccountStatus.Active,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    staffUser.PasswordHash = _passwordHasher.HashPassword(staffUser, dto.Password);

                    await _context.Users.AddAsync(staffUser);

                    // Assign HospitalStaff role (RoleId = 2)
                    var staffRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "HospitalStaff");
                    int roleId = staffRole?.RoleId ?? 2;

                    await _context.UserRoles.AddAsync(new UserRole
                    {
                        UserId = staffUser.UserId,
                        RoleId = roleId
                    });
                }
            }

            await _context.SaveChangesAsync();

            return MapToResponseDto(hospital);
        }

        public async Task<List<HospitalResponseDto>> GetHospitalsAsync(bool? isVerified = null)
        {
            var query = _context.Hospitals
                .Include(h => h.ApprovalHistories)
                .AsQueryable();

            if (isVerified.HasValue)
            {
                query = query.Where(h => h.IsVerified == isVerified.Value);
            }

            var list = await query.OrderByDescending(h => h.UpdatedAt).ToListAsync();
            return list.Select(MapToResponseDto).ToList();
        }

        public async Task<HospitalResponseDto?> GetHospitalByIdAsync(Guid hospitalId)
        {
            var hospital = await _context.Hospitals
                .Include(h => h.ApprovalHistories)
                .FirstOrDefaultAsync(h => h.HospitalId == hospitalId);
            return hospital == null ? null : MapToResponseDto(hospital);
        }

        public async Task<HospitalResponseDto?> VerifyHospitalAsync(Guid hospitalId, bool isVerified)
        {
            var hospital = await _context.Hospitals
                .Include(h => h.ApprovalHistories)
                .FirstOrDefaultAsync(h => h.HospitalId == hospitalId);
            if (hospital == null) return null;

            hospital.IsVerified = isVerified;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return MapToResponseDto(hospital);
        }

        public async Task<HospitalResponseDto> ResubmitHospitalAsync(Guid hospitalId, ResubmitHospitalDto dto)
        {
            var hospital = await _context.Hospitals
                .Include(h => h.ApprovalHistories)
                .FirstOrDefaultAsync(h => h.HospitalId == hospitalId);

            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {hospitalId} not found.");
            }

            var changedFields = new List<string>();
            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name.Trim() != hospital.Name)
            {
                changedFields.Add("Hospital Name");
                hospital.Name = dto.Name.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.LicenseNumber) && dto.LicenseNumber.Trim() != hospital.LicenseNumber)
            {
                changedFields.Add("License Number");
                hospital.LicenseNumber = dto.LicenseNumber.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.RegistrationNumber) && dto.RegistrationNumber.Trim() != hospital.RegistrationNumber)
            {
                changedFields.Add("Registration Number");
                hospital.RegistrationNumber = dto.RegistrationNumber.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.Address) && dto.Address.Trim() != hospital.Address)
            {
                changedFields.Add("Address");
                hospital.Address = dto.Address.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.ContactNumber) && dto.ContactNumber.Trim() != hospital.ContactNumber)
            {
                changedFields.Add("Contact Phone");
                hospital.ContactNumber = dto.ContactNumber.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.City) && dto.City.Trim() != hospital.City)
            {
                changedFields.Add("City / Region");
                hospital.City = dto.City.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.ContactPersonName) && dto.ContactPersonName.Trim() != hospital.ContactPersonName)
            {
                changedFields.Add("Contact Person Name");
                hospital.ContactPersonName = dto.ContactPersonName.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.ContactPersonPhone) && dto.ContactPersonPhone.Trim() != hospital.ContactPersonPhone)
            {
                changedFields.Add("Contact Person Phone");
                hospital.ContactPersonPhone = dto.ContactPersonPhone.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.ContactPersonEmail) && dto.ContactPersonEmail.Trim() != hospital.ContactPersonEmail)
            {
                changedFields.Add("Contact Person Email");
                hospital.ContactPersonEmail = dto.ContactPersonEmail.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.LicenseDocumentUrl) && dto.LicenseDocumentUrl != hospital.LicenseDocumentUrl)
            {
                changedFields.Add("License Document");
                hospital.LicenseDocumentUrl = dto.LicenseDocumentUrl;
                hospital.LicenseDocumentName = dto.LicenseDocumentName ?? "License_Document";
            }
            if (!string.IsNullOrWhiteSpace(dto.AccreditationDocumentUrl) && dto.AccreditationDocumentUrl != hospital.AccreditationDocumentUrl)
            {
                changedFields.Add("Accreditation Document");
                hospital.AccreditationDocumentUrl = dto.AccreditationDocumentUrl;
                hospital.AccreditationDocumentName = dto.AccreditationDocumentName ?? "Accreditation_Document";
            }

            hospital.ApprovalStatus = ApprovalStatus.Resubmitted;
            hospital.ResubmittedAt = DateTime.UtcNow;
            hospital.UpdatedAt = DateTime.UtcNow;
            hospital.UpdatedFields = changedFields.Count > 0 ? string.Join(", ", changedFields) : "Registration Details Updated";

            var history = new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = ApprovalStatus.Resubmitted,
                Timestamp = DateTime.UtcNow,
                Comments = !string.IsNullOrWhiteSpace(dto.Comments) ? dto.Comments : "Application updated and resubmitted for admin review.",
                ChangedFields = hospital.UpdatedFields
            };
            await _context.HospitalApprovalHistories.AddAsync(history);

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
                ApprovalStatus = hospital.ApprovalStatus.ToString(),
                RejectionReason = hospital.RejectionReason,
                RejectionReportUrl = hospital.RejectionReportUrl,
                RejectionReportName = hospital.RejectionReportName,
                RegistrationNumber = hospital.RegistrationNumber,
                City = hospital.City,
                ContactPersonName = hospital.ContactPersonName,
                ContactPersonPhone = hospital.ContactPersonPhone,
                ContactPersonEmail = hospital.ContactPersonEmail,
                LicenseDocumentUrl = hospital.LicenseDocumentUrl,
                LicenseDocumentName = hospital.LicenseDocumentName,
                AccreditationDocumentUrl = hospital.AccreditationDocumentUrl,
                AccreditationDocumentName = hospital.AccreditationDocumentName,
                ResubmittedAt = hospital.ResubmittedAt,
                UpdatedFields = hospital.UpdatedFields,
                CreatedAt = hospital.CreatedAt,
                UpdatedAt = hospital.UpdatedAt,
                ApprovalHistory = hospital.ApprovalHistories?
                    .OrderBy(h => h.Timestamp)
                    .Select(h => new HospitalApprovalHistoryDto
                    {
                        Id = h.Id,
                        Status = h.Status.ToString(),
                        Timestamp = h.Timestamp,
                        AdminId = h.AdminId,
                        AdminName = h.AdminName ?? h.Admin?.FirstName,
                        Comments = h.Comments,
                        ReportDocumentName = h.ReportDocumentName,
                        ReportDocumentUrl = h.ReportDocumentUrl,
                        ChangedFields = h.ChangedFields
                    }).ToList() ?? new List<HospitalApprovalHistoryDto>()
            };
        }
    }
}
