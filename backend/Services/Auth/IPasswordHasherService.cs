using LifeLink.Entities;

namespace LifeLink.Services.Auth
{
    public interface IPasswordHasherService
    {
        string HashPassword(User user, string password);
        bool VerifyPassword(User user, string hashedPassword, string providedPassword);
    }
}
