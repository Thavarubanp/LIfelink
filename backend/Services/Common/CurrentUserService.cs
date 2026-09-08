using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace LifeLink.Services.Common
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? UserId
        {
            get
            {
                var subClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;

                return Guid.TryParse(subClaim, out var userId) ? userId : null;
            }
        }

        public string? Email =>
            _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("email")?.Value;

        public IEnumerable<string> Roles =>
            _httpContextAccessor.HttpContext?.User?.FindAll(ClaimTypes.Role).Select(c => c.Value)
            ?? Enumerable.Empty<string>();

        public bool IsAuthenticated =>
            _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    }
}
