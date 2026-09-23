using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Doctors;
using LifeLink.Entities;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Doctors
{
    public class DoctorService : IDoctorService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;

        public DoctorService(AppDbContext context, IPasswordHasherService passwordHasher)
        {
            _context = context;
            _passwordHasher = passwordHasher;
        }

        public async Task<DoctorResponseDto> CreateDoctorAsync(CreateDoctorDto dto)
        {
            // Validate hospital exists
            var hospital = await _context.Hospitals.FindAsync(dto.HospitalId);
            if (hospital == null)
            {
                throw new InvalidOperationException($"Hospital with ID {dto.HospitalId} was not found.");
            }

            // Enforce global email uniqueness across Users, Hospitals, and Doctors tables
            var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
            if (await EmailUniquenessHelper.IsEmailTakenAsync(_context, normalizedEmail))
            {
                throw new InvalidOperationException("An account with this email address already exists.");
            }

            // Enforce system-wide SLMC uniqueness (stored trimmed + upper-cased; unique index is the final guard)
            var normalizedSlmc = SlmcUniquenessHelper.Normalize(dto.LicenseNumber);
            if (normalizedSlmc.Length == 0)
            {
                throw new InvalidOperationException("SLMC number is required.");
            }
            if (await SlmcUniquenessHelper.IsSlmcTakenAsync(_context, normalizedSlmc))
            {
                throw new InvalidOperationException(SlmcUniquenessHelper.DuplicateMessage);
            }

            // Get or ensure the Doctor role exists
            var doctorRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Doctor");
            if (doctorRole == null)
            {
                throw new InvalidOperationException("Doctor role is not configured in the system. Please contact an administrator.");
            }

            // Create the User account with a hashed password
            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = dto.FirstName.Trim(),
                LastName = dto.LastName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = dto.PhoneNumber?.Trim() ?? string.Empty,
                AccountStatus = AccountStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            newUser.PasswordHash = _passwordHasher.HashPassword(newUser, dto.Password);

            await _context.Users.AddAsync(newUser);

            // Assign Doctor role
            await _context.UserRoles.AddAsync(new UserRole
            {
                UserId = newUser.UserId,
                RoleId = doctorRole.RoleId
            });

            // Create the Doctor entity linked to this User and Hospital
            var doctor = new Doctor
            {
                DoctorId = Guid.NewGuid(),
                HospitalId = dto.HospitalId,
                UserId = newUser.UserId,
                FirstName = dto.FirstName.Trim(),
                LastName = dto.LastName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = dto.PhoneNumber?.Trim() ?? string.Empty,
                LicenseNumber = normalizedSlmc,
                Specialization = dto.Specialization?.Trim() ?? string.Empty,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.Doctors.AddAsync(doctor);

            // Save atomically
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (SlmcUniquenessHelper.IsSlmcUniqueViolation(ex))
            {
                throw new InvalidOperationException(SlmcUniquenessHelper.DuplicateMessage);
            }

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

        /// <summary>
        /// Hospital deletes one of its doctors, whatever the doctor's state (first login, history, decisions, matches).
        /// - Undecided assignments are removed and their requests return to Pending for reassignment.
        /// - Decided verifications, screenings, matches, acceptances and request statuses are kept
        ///   (DoctorId is set to null by the FK; nothing else changes).
        /// - The doctor's login is removed. If that same account also holds personal donor/requester history,
        ///   the account row is kept (so the history stays intact) but loses the Doctor role and is deactivated.
        /// </summary>
        public async Task DeleteDoctorAsync(Guid doctorId, Guid hospitalId)
        {
            var doctor = await _context.Doctors.FindAsync(doctorId);
            if (doctor == null)
            {
                throw new KeyNotFoundException($"Doctor with ID {doctorId} was not found.");
            }

            if (doctor.HospitalId != hospitalId)
            {
                throw new UnauthorizedAccessException("You can only delete doctors created by your hospital.");
            }

            var now = DateTime.UtcNow;

            // Undecided assignments carry no decision history; drop them and send the requests back to the hospital
            var pendingAssignments = await _context.BloodRequestVerifications
                .Where(v => v.DoctorId == doctorId && v.Status == VerificationStatus.Pending)
                .ToListAsync();
            var pendingRequestIds = pendingAssignments.Select(v => v.BloodRequestId).ToList();
            var awaitingRequests = await _context.BloodRequests
                .Where(r => pendingRequestIds.Contains(r.BloodRequestId) && r.Status == BloodRequestStatus.Verified)
                .ToListAsync();
            foreach (var request in awaitingRequests)
            {
                request.Status = BloodRequestStatus.Pending;
                request.UpdatedAt = now;
            }
            _context.BloodRequestVerifications.RemoveRange(pendingAssignments);

            if (doctor.UserId.HasValue)
            {
                var userId = doctor.UserId.Value;
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    // Donor/requester history references this account by id (DonorPatientMatches with a
                    // Restrict FK; Acceptances, fulfilment history and requests with no FK at all).
                    var hasPersonalHistory =
                        await _context.DonorPatientMatches.AnyAsync(m => m.DonorUserId == userId) ||
                        await _context.Acceptances.AnyAsync(a => a.DonorUserId == userId) ||
                        await _context.RequestFulfillmentHistories.AnyAsync(h => h.DonorUserId == userId) ||
                        await _context.BloodRequests.AnyAsync(r => r.PatientUserId == userId);

                    if (hasPersonalHistory)
                    {
                        // Keep the row so that history stays intact; remove all doctor access and disable sign-in
                        _context.UserRoles.RemoveRange(_context.UserRoles.Where(ur => ur.UserId == userId));
                        _context.PasswordResetTokens.RemoveRange(_context.PasswordResetTokens.Where(t => t.UserId == userId));
                        user.AccountStatus = AccountStatus.Inactive;
                        user.UpdatedAt = now;
                    }
                    else
                    {
                        // Removing the User removes login access; its roles and reset tokens cascade
                        _context.Users.Remove(user);
                    }
                }
            }

            _context.Doctors.Remove(doctor);
            await _context.SaveChangesAsync();
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
                MustChangePassword = doctor.MustChangePassword,
                CreatedAt = doctor.CreatedAt
            };
        }
    }
}
