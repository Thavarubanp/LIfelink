using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<UserRole> UserRoles { get; set; } = null!;
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; } = null!;

        // Student 3 DbSets
        public DbSet<Hospital> Hospitals { get; set; } = null!;
        public DbSet<BloodInventory> BloodInventories { get; set; } = null!;
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; } = null!;
        public DbSet<EmergencyRequest> EmergencyRequests { get; set; } = null!;
        public DbSet<HospitalTransferRequest> HospitalTransferRequests { get; set; } = null!;

        // Student 2 DbSets
        public DbSet<Doctor> Doctors { get; set; } = null!;
        public DbSet<BloodRequestVerification> BloodRequestVerifications { get; set; } = null!;
        public DbSet<DonorVerification> DonorVerifications { get; set; } = null!;
        public DbSet<DonorPatientMatch> DonorPatientMatches { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;

        // Student 1 DbSets
        public DbSet<BloodRequest> BloodRequests { get; set; } = null!;
        public DbSet<Acceptance> Acceptances { get; set; } = null!;
        public DbSet<RequestFulfillmentHistory> RequestFulfillmentHistories { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.UserId);
                entity.HasIndex(u => u.Email).IsUnique();
                entity.Property(u => u.Email).IsRequired().HasMaxLength(256);
                entity.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
                entity.Property(u => u.LastName).IsRequired().HasMaxLength(100);
                entity.Property(u => u.PasswordHash).IsRequired();
                entity.Property(u => u.PhoneNumber).HasMaxLength(20);
                entity.Property(u => u.Gender).HasMaxLength(20);
                entity.Property(u => u.Address).HasMaxLength(500);
                entity.Property(u => u.AccountStatus)
                      .HasConversion<string>()
                      .IsRequired();
            });

            // Role configuration
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(r => r.RoleId);
                entity.HasIndex(r => r.Name).IsUnique();
                entity.Property(r => r.Name).IsRequired().HasMaxLength(50);
            });

            // UserRole configuration (Composite Key)
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasKey(ur => new { ur.UserId, ur.RoleId });

                entity.HasOne(ur => ur.User)
                      .WithMany(u => u.UserRoles)
                      .HasForeignKey(ur => ur.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ur => ur.Role)
                      .WithMany(r => r.UserRoles)
                      .HasForeignKey(ur => ur.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // PasswordResetToken configuration
            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasKey(prt => prt.PasswordResetTokenId);
                entity.HasIndex(prt => prt.TokenHash);
                entity.HasIndex(prt => prt.UserId);

                entity.HasOne(prt => prt.User)
                      .WithMany(u => u.PasswordResetTokens)
                      .HasForeignKey(prt => prt.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Hospital configuration
            modelBuilder.Entity<Hospital>(entity =>
            {
                entity.HasKey(h => h.HospitalId);
                entity.Property(h => h.Name).IsRequired().HasMaxLength(200);
                entity.Property(h => h.LicenseNumber).HasMaxLength(100);
                entity.Property(h => h.Email).HasMaxLength(200);
                entity.Property(h => h.ContactNumber).HasMaxLength(50);
                entity.Property(h => h.IsVerified).IsRequired().HasDefaultValue(false);
            });

            // BloodInventory configuration
            modelBuilder.Entity<BloodInventory>(entity =>
            {
                entity.HasKey(i => i.InventoryId);
                entity.HasIndex(i => i.HospitalId);
                entity.HasIndex(i => i.BloodGroup);
                entity.HasIndex(i => new { i.HospitalId, i.BloodGroup }).IsUnique();

                entity.Property(i => i.BloodGroup).IsRequired().HasMaxLength(10);

                entity.HasOne(i => i.Hospital)
                      .WithMany(h => h.BloodInventories)
                      .HasForeignKey(i => i.HospitalId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // InventoryTransaction configuration
            modelBuilder.Entity<InventoryTransaction>(entity =>
            {
                entity.HasKey(t => t.TransactionId);
                entity.HasIndex(t => t.InventoryId);
                entity.Property(t => t.TransactionType).IsRequired().HasMaxLength(50);

                entity.HasOne(t => t.Inventory)
                      .WithMany(i => i.Transactions)
                      .HasForeignKey(t => t.InventoryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // EmergencyRequest configuration
            modelBuilder.Entity<EmergencyRequest>(entity =>
            {
                entity.HasKey(e => e.EmergencyRequestId);
                entity.HasIndex(e => e.HospitalId);
                entity.HasIndex(e => e.BloodGroup);
                entity.HasIndex(e => e.Priority);
                entity.HasIndex(e => e.Status);

                entity.Property(e => e.BloodGroup).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Priority).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);

                entity.HasOne(e => e.Hospital)
                      .WithMany(h => h.EmergencyRequests)
                      .HasForeignKey(e => e.HospitalId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // HospitalTransferRequest configuration
            modelBuilder.Entity<HospitalTransferRequest>(entity =>
            {
                entity.HasKey(t => t.TransferRequestId);
                entity.HasIndex(t => t.SenderHospitalId);
                entity.HasIndex(t => t.ReceiverHospitalId);
                entity.HasIndex(t => t.BloodGroup);
                entity.HasIndex(t => t.Status);

                entity.Property(t => t.BloodGroup).IsRequired().HasMaxLength(10);
                entity.Property(t => t.Status).IsRequired().HasMaxLength(20);

                entity.HasOne(t => t.SenderHospital)
                      .WithMany(h => h.SentTransferRequests)
                      .HasForeignKey(t => t.SenderHospitalId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(t => t.ReceiverHospital)
                      .WithMany(h => h.ReceivedTransferRequests)
                      .HasForeignKey(t => t.ReceiverHospitalId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Doctor configuration
            modelBuilder.Entity<Doctor>(entity =>
            {
                entity.HasKey(d => d.DoctorId);
                entity.HasIndex(d => d.HospitalId);
                entity.HasIndex(d => d.Email);

                entity.Property(d => d.FirstName).IsRequired().HasMaxLength(100);
                entity.Property(d => d.LastName).IsRequired().HasMaxLength(100);
                entity.Property(d => d.Email).IsRequired().HasMaxLength(200);

                entity.HasOne(d => d.Hospital)
                      .WithMany(h => h.Doctors)
                      .HasForeignKey(d => d.HospitalId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.User)
                      .WithMany()
                      .HasForeignKey(d => d.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // BloodRequestVerification configuration
            modelBuilder.Entity<BloodRequestVerification>(entity =>
            {
                entity.HasKey(v => v.VerificationId);
                entity.HasIndex(v => v.BloodRequestId);
                entity.HasIndex(v => v.DoctorId);

                entity.Property(v => v.Status)
                      .HasConversion<string>()
                      .IsRequired();

                entity.HasOne(v => v.Doctor)
                      .WithMany()
                      .HasForeignKey(v => v.DoctorId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // DonorVerification configuration
            modelBuilder.Entity<DonorVerification>(entity =>
            {
                entity.HasKey(v => v.DonorVerificationId);
                entity.HasIndex(v => v.AcceptanceId);
                entity.HasIndex(v => v.DoctorId);

                entity.Property(v => v.Status)
                      .HasConversion<string>()
                      .IsRequired();

                entity.HasOne(v => v.Doctor)
                      .WithMany()
                      .HasForeignKey(v => v.DoctorId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // DonorPatientMatch configuration
            modelBuilder.Entity<DonorPatientMatch>(entity =>
            {
                entity.HasKey(m => m.MatchId);
                entity.HasIndex(m => m.BloodRequestId);
                entity.HasIndex(m => m.DonorUserId);
                entity.HasIndex(m => m.DoctorId);

                entity.Property(m => m.Status)
                      .HasConversion<string>()
                      .IsRequired();

                entity.HasOne(m => m.Doctor)
                      .WithMany()
                      .HasForeignKey(m => m.DoctorId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(m => m.DonorUser)
                      .WithMany()
                      .HasForeignKey(m => m.DonorUserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Notification configuration
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(n => n.NotificationId);
                entity.HasIndex(n => n.UserId);
                entity.HasIndex(n => n.HospitalId);
                entity.Property(n => n.Title).IsRequired().HasMaxLength(200);
                entity.Property(n => n.NotificationType).IsRequired().HasMaxLength(100);

                entity.HasOne(n => n.User)
                      .WithMany()
                      .HasForeignKey(n => n.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(n => n.Hospital)
                      .WithMany()
                      .HasForeignKey(n => n.HospitalId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Student 1: BloodRequest configuration
            modelBuilder.Entity<BloodRequest>(entity =>
            {
                entity.HasKey(b => b.BloodRequestId);
                entity.HasIndex(b => b.PatientUserId);
                entity.HasIndex(b => b.HospitalId);
                entity.HasIndex(b => b.BloodGroup);
                entity.HasIndex(b => b.Status);
                entity.HasIndex(b => b.CreatedAt);
                entity.HasIndex(b => b.ExpiryDate);

                entity.Property(b => b.BloodGroup).IsRequired().HasMaxLength(10);
                entity.Property(b => b.UnitsRequired).IsRequired();
                entity.Property(b => b.FulfilledUnits).IsRequired().HasDefaultValue(0);
                entity.Property(b => b.Reason).IsRequired().HasMaxLength(500);
                entity.Property(b => b.Priority).IsRequired().HasMaxLength(20);
                entity.Property(b => b.ConcurrencyToken).IsConcurrencyToken();
                entity.Property(b => b.Status)
                      .HasConversion<string>()
                      .IsRequired();

                entity.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_BloodRequests_UnitsRequired", "\"UnitsRequired\" >= 1 AND \"UnitsRequired\" <= 10");
                    t.HasCheckConstraint("CK_BloodRequests_FulfilledUnits", "\"FulfilledUnits\" >= 0 AND \"FulfilledUnits\" <= \"UnitsRequired\"");
                });
            });

            // Student 1: Acceptance configuration
            modelBuilder.Entity<Acceptance>(entity =>
            {
                entity.HasKey(a => a.AcceptanceId);
                entity.HasIndex(a => a.BloodRequestId);
                entity.HasIndex(a => a.DonorUserId);
                entity.HasIndex(a => a.Status);
                entity.HasIndex(a => a.AcceptedAt);

                entity.Property(a => a.RejectionReason).HasMaxLength(500);
                entity.Property(a => a.Status)
                      .HasConversion<string>()
                      .IsRequired();
            });

            // Student 1: RequestFulfillmentHistory configuration
            modelBuilder.Entity<RequestFulfillmentHistory>(entity =>
            {
                entity.HasKey(h => h.Id);
                entity.HasIndex(h => h.BloodRequestId);
                entity.HasIndex(h => h.AcceptanceId);
                entity.HasIndex(h => h.DonorUserId);
                entity.HasIndex(h => h.FulfilledAt);
            });

            // Deterministic Role Seeding
            modelBuilder.Entity<Role>().HasData(
                new Role { RoleId = 1, Name = "User" },
                new Role { RoleId = 2, Name = "HospitalStaff" },
                new Role { RoleId = 3, Name = "Doctor" },
                new Role { RoleId = 4, Name = "Admin" }
            );
        }
    }
}