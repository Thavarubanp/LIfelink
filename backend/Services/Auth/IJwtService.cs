using System;
using System.Collections.Generic;
using LifeLink.Entities;

namespace LifeLink.Services.Auth
{
    public interface IJwtService
    {
        /// <summary>Access token for a signed-in session; the session id goes in the "sid" claim (idle timeout, sign-out).</summary>
        (string Token, DateTime ExpiresAt) GenerateToken(User user, IEnumerable<string> roles, Guid? sessionId = null);
    }
}
