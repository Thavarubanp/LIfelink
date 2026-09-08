using System;
using System.Collections.Generic;
using LifeLink.Entities;

namespace LifeLink.Services.Auth
{
    public interface IJwtService
    {
        (string Token, DateTime ExpiresAt) GenerateToken(User user, IEnumerable<string> roles);
    }
}
