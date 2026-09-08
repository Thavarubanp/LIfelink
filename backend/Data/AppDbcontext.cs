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