using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Hospitals;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

using LifeLink.Services.Admin;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;

namespace LifeLink.Services.Hospitals
{
    public class HospitalService : IHospitalService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly IAdminNotificationService? _notificationService;

        public HospitalService(AppDbContext context, IPasswordHasherService? passwordHasher = null, IAdminNotificationService? notificationService = null)
        {
            _context = context;
            _passwordHasher = passwordHasher ?? new PasswordHasherService();
            _notificationService = notificationService;
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

            // Registration number is unique system-wide (stored trimmed + upper-cased; unique index is the final guard)
            var registrationNumber = NormalizeRegistrationNumber(dto.RegistrationNumber ?? dto.LicenseNumber);
            if (registrationNumber != null && await IsRegistrationNumberTakenAsync(registrationNumber))
            {
                throw new InvalidOperationException(DuplicateRegistrationMessage);
            }

            AttachmentRules.EnsureValidIfPresent(dto.LicenseDocumentUrl);
            AttachmentRules.EnsureValidIfPresent(dto.AccreditationDocumentUrl);

            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = dto.Name?.Trim() ?? string.Empty,
                LicenseNumber = dto.LicenseNumber?.Trim() ?? string.Empty,
                Address = dto.Address?.Trim() ?? string.Empty,
                ContactNumber = dto.ContactNumber?.Trim() ?? string.Empty,
                Email = normalizedEmail,
                RegistrationNumber = registrationNumber,
                City = dto.City?.Trim(),
                ContactPersonName = dto.ContactPersonName?.Trim(),
                ContactPersonPhone = dto.ContactPersonPhone?.Trim(),
                ContactPersonEmail = dto.ContactPersonEmail?.Trim(),
                LicenseDocumentUrl = AttachmentRules.HasContent(dto.LicenseDocumentUrl) ? dto.LicenseDocumentUrl : null,
                LicenseDocumentName = AttachmentRules.HasContent(dto.LicenseDocumentUrl) ? dto.LicenseDocumentName : null,
                AccreditationDocumentUrl = AttachmentRules.HasContent(dto.AccreditationDocumentUrl) ? dto.AccreditationDocumentUrl : null,
                AccreditationDocumentName = AttachmentRules.HasContent(dto.AccreditationDocumentUrl) ? dto.AccreditationDocumentName : null,
                ApprovalStatus = ApprovalStatus.Pending,
                IsVerified = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.Hospitals.AddAsync(hospital);

            // First entry of the registration conversation
            await _context.HospitalApprovalHistories.AddAsync(new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = RegistrationEntryType.Submitted,
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

            await SaveWithRegistrationNumberGuardAsync();

            return MapToResponseDto(hospital, includeAdminIdentity: false);
        }

        public async Task<List<HospitalSummaryDto>> GetHospitalsAsync(bool? isVerified = null)
        {
            var query = _context.Hospitals.AsNoTracking().AsQueryable();

            if (isVerified.HasValue)
            {
                query = query.Where(h => h.IsVerified == isVerified.Value);
            }

            return await query
                .OrderByDescending(h => h.UpdatedAt)
                .Select(h => new HospitalSummaryDto
                {
                    HospitalId = h.HospitalId,
                    Name = h.Name,
                    City = h.City,
                    Address = h.Address,
                    Email = h.Email,
                    ContactNumber = h.ContactNumber,
                    LicenseNumber = h.LicenseNumber,
                    RegistrationNumber = h.RegistrationNumber,
                    IsVerified = h.IsVerified,
                    IsSuspended = h.IsSuspended,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<Guid?> GetHospitalIdByEmailAsync(string? email)
        {
            // Hospital staff accounts share the hospital's registered email address
            if (string.IsNullOrWhiteSpace(email)) return null;

            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await _context.Hospitals
                .Where(h => h.Email != null && h.Email.ToLower() == normalizedEmail)
                .Select(h => (Guid?)h.HospitalId)
                .FirstOrDefaultAsync();
        }

        public async Task<HospitalResponseDto?> GetHospitalByIdAsync(Guid hospitalId, bool includeAdminIdentity = false)
        {
            var hospital = await LoadWithConversationAsync(hospitalId);
            return hospital == null ? null : MapToResponseDto(hospital, includeAdminIdentity);
        }

        public async Task<HospitalResponseDto?> VerifyHospitalAsync(Guid hospitalId, bool isVerified)
        {
            var hospital = await LoadWithConversationAsync(hospitalId);
            if (hospital == null) return null;

            hospital.IsVerified = isVerified;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return MapToResponseDto(hospital, includeAdminIdentity: true);
        }

        /// <summary>
        /// The hospital's reply in its rejected registration's conversation. Corrected details and documents apply to the
        /// registration immediately and the registration moves to AwaitingAdminReview: the hospital cannot reply again
        /// until the admin approves, rejects again or asks for more information.
        /// </summary>
        public async Task<HospitalResponseDto> ReplyToRegistrationAsync(Guid hospitalId, HospitalRegistrationReplyDto dto)
        {
            var hospital = await LoadWithConversationAsync(hospitalId)
                ?? throw new KeyNotFoundException($"Hospital with ID {hospitalId} not found.");

            if (hospital.ApprovalStatus != ApprovalStatus.Rejected)
            {
                throw new ConflictException(hospital.ApprovalStatus switch
                {
                    ApprovalStatus.Approved => "This registration is approved and read-only.",
                    ApprovalStatus.AwaitingAdminReview => "Your reply is with the administrator. You can reply again after the administrator responds.",
                    _ => "You can reply once the administrator has reviewed your registration."
                });
            }

            var message = dto.Message?.Trim() ?? string.Empty;
            if (message.Length < 3)
            {
                throw new InvalidOperationException("A reply message of at least 3 characters is required.");
            }
            AttachmentRules.EnsureValidIfPresent(dto.AttachmentUrl);

            var changes = new List<string>();

            void Correct(string label, string? incoming, string? current, Action<string> apply)
            {
                if (string.IsNullOrWhiteSpace(incoming)) return; // blank keeps the current value
                var value = incoming.Trim();
                if (value == current) return;
                changes.Add($"{label}: {Shown(current)} -> {value}");
                apply(value);
            }

            Correct("Hospital Name", dto.Name, hospital.Name, v => hospital.Name = v);
            Correct("License Number", dto.LicenseNumber, hospital.LicenseNumber, v => hospital.LicenseNumber = v);

            var registrationNumber = NormalizeRegistrationNumber(dto.RegistrationNumber);
            if (registrationNumber != null && registrationNumber != hospital.RegistrationNumber)
            {
                if (await IsRegistrationNumberTakenAsync(registrationNumber, hospital.HospitalId))
                {
                    throw new InvalidOperationException(DuplicateRegistrationMessage);
                }
                changes.Add($"Registration Number: {Shown(hospital.RegistrationNumber)} -> {registrationNumber}");
                hospital.RegistrationNumber = registrationNumber;
            }

            Correct("Address", dto.Address, hospital.Address, v => hospital.Address = v);
            Correct("City / Region", dto.City, hospital.City, v => hospital.City = v);
            Correct("Hospital Contact Number", dto.ContactNumber, hospital.ContactNumber, v => hospital.ContactNumber = v);
            Correct("Authorized Person Name", dto.ContactPersonName, hospital.ContactPersonName, v => hospital.ContactPersonName = v);
            Correct("Authorized Person Phone", dto.ContactPersonPhone, hospital.ContactPersonPhone, v => hospital.ContactPersonPhone = v);
            Correct("Authorized Person Email", dto.ContactPersonEmail, hospital.ContactPersonEmail, v => hospital.ContactPersonEmail = v);

            // Documents: only a new file is checked and recorded; an unchanged document is not re-validated
            if (!string.IsNullOrWhiteSpace(dto.LicenseDocumentUrl) && dto.LicenseDocumentUrl != hospital.LicenseDocumentUrl)
            {
                AttachmentRules.EnsureValidIfPresent(dto.LicenseDocumentUrl);
                var fileName = string.IsNullOrWhiteSpace(dto.LicenseDocumentName) ? "License_Document" : dto.LicenseDocumentName.Trim();
                changes.Add($"License Document: {ShownFile(hospital.LicenseDocumentUrl, hospital.LicenseDocumentName)} -> {fileName}");
                hospital.LicenseDocumentUrl = dto.LicenseDocumentUrl;
                hospital.LicenseDocumentName = fileName;
            }
            if (!string.IsNullOrWhiteSpace(dto.AccreditationDocumentUrl) && dto.AccreditationDocumentUrl != hospital.AccreditationDocumentUrl)
            {
                AttachmentRules.EnsureValidIfPresent(dto.AccreditationDocumentUrl);
                var fileName = string.IsNullOrWhiteSpace(dto.AccreditationDocumentName) ? "Accreditation_Document" : dto.AccreditationDocumentName.Trim();
                changes.Add($"Accreditation Document: {ShownFile(hospital.AccreditationDocumentUrl, hospital.AccreditationDocumentName)} -> {fileName}");
                hospital.AccreditationDocumentUrl = dto.AccreditationDocumentUrl;
                hospital.AccreditationDocumentName = fileName;
            }

            // The corrected registration must still meet the registration rules
            if (string.IsNullOrWhiteSpace(hospital.ContactPersonName))
            {
                throw new InvalidOperationException("Authorized person name is required.");
            }
            if (!IsTenDigits(hospital.ContactPersonPhone))
            {
                throw new InvalidOperationException("Authorized person phone number must be exactly 10 digits.");
            }
            if (!IsTenDigits(hospital.ContactNumber))
            {
                throw new InvalidOperationException("Hospital contact number must be exactly 10 digits.");
            }

            var hasAttachment = AttachmentRules.HasContent(dto.AttachmentUrl);
            var now = DateTime.UtcNow;
            await _context.HospitalApprovalHistories.AddAsync(new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = RegistrationEntryType.HospitalReply,
                Timestamp = now,
                Comments = message,
                ChangedFields = changes.Count > 0 ? string.Join("\n", changes) : null,
                ReportDocumentUrl = hasAttachment ? dto.AttachmentUrl : null,
                ReportDocumentName = hasAttachment ? (string.IsNullOrWhiteSpace(dto.AttachmentName) ? "attachment" : dto.AttachmentName.Trim()) : null
            });
            hospital.ApprovalStatus = ApprovalStatus.AwaitingAdminReview; // the admin's turn
            hospital.UpdatedAt = now;

            await SaveWithRegistrationNumberGuardAsync();

            if (_notificationService != null)
            {
                await _notificationService.NotifyAdminAsync(
                    "Hospital Registration Reply",
                    $"{hospital.Name} replied to its registration review{(changes.Count > 0 ? " with corrections" : string.Empty)}.");
            }

            return MapToResponseDto(hospital, includeAdminIdentity: false);
        }

        private const string DuplicateRegistrationMessage = "A hospital with this registration number already exists.";

        private Task<Hospital?> LoadWithConversationAsync(Guid hospitalId) =>
            _context.Hospitals
                .Include(h => h.ApprovalHistories).ThenInclude(e => e.Admin)
                .FirstOrDefaultAsync(h => h.HospitalId == hospitalId);

        private static bool IsTenDigits(string? value) => value != null && Regex.IsMatch(value, @"^\d{10}$");

        private static string Shown(string? value) => string.IsNullOrWhiteSpace(value) ? "(empty)" : value;

        private static string ShownFile(string? url, string? name) =>
            AttachmentRules.HasContent(url) ? (string.IsNullOrWhiteSpace(name) ? "previous file" : name) : "no file";

        /// <summary>Registration numbers are stored trimmed + upper-cased; blank becomes null (no NOT NULL constraint).</summary>
        private static string? NormalizeRegistrationNumber(string? raw)
        {
            var normalized = raw?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }

        private Task<bool> IsRegistrationNumberTakenAsync(string normalized, Guid? excludeHospitalId = null) =>
            _context.Hospitals.AnyAsync(h =>
                h.RegistrationNumber != null &&
                h.RegistrationNumber.Trim().ToUpper() == normalized &&
                (excludeHospitalId == null || h.HospitalId != excludeHospitalId));

        // A concurrent duplicate is caught by IX_Hospitals_RegistrationNumber; report it with the same message
        private async Task SaveWithRegistrationNumberGuardAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
                                               && pg.ConstraintName == "IX_Hospitals_RegistrationNumber")
            {
                throw new InvalidOperationException(DuplicateRegistrationMessage);
            }
        }

        private static HospitalResponseDto MapToResponseDto(Hospital hospital, bool includeAdminIdentity)
        {
            var hasLicense = AttachmentRules.HasContent(hospital.LicenseDocumentUrl);
            var hasAccreditation = AttachmentRules.HasContent(hospital.AccreditationDocumentUrl);
            var hasRejectionReport = AttachmentRules.HasContent(hospital.RejectionReportUrl);
            return new HospitalResponseDto
            {
                HospitalId = hospital.HospitalId,
                Name = hospital.Name,
                LicenseNumber = hospital.LicenseNumber,
                Address = hospital.Address,
                ContactNumber = hospital.ContactNumber,
                Email = hospital.Email,
                IsVerified = hospital.IsVerified,
                IsSuspended = hospital.IsSuspended,
                ApprovalStatus = hospital.ApprovalStatus.ToString(),
                AwaitingAdminReview = RegistrationThread.IsAwaitingAdminReview(hospital),
                RejectionReason = hospital.RejectionReason,
                RejectionReportUrl = hasRejectionReport ? hospital.RejectionReportUrl : null,
                RejectionReportName = hasRejectionReport ? hospital.RejectionReportName : null,
                RegistrationNumber = hospital.RegistrationNumber,
                City = hospital.City,
                ContactPersonName = hospital.ContactPersonName,
                ContactPersonPhone = hospital.ContactPersonPhone,
                ContactPersonEmail = hospital.ContactPersonEmail,
                LicenseDocumentUrl = hasLicense ? hospital.LicenseDocumentUrl : null,
                LicenseDocumentName = hasLicense ? hospital.LicenseDocumentName : null,
                AccreditationDocumentUrl = hasAccreditation ? hospital.AccreditationDocumentUrl : null,
                AccreditationDocumentName = hasAccreditation ? hospital.AccreditationDocumentName : null,
                CreatedAt = hospital.CreatedAt,
                UpdatedAt = hospital.UpdatedAt,
                ApprovalHistory = RegistrationThread.ToDtos(hospital, includeAdminIdentity)
            };
        }
    }
}
