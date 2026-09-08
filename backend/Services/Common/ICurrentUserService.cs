using System;
using System.Collections.Generic;

namespace LifeLink.Services.Common
{
    public interface ICurrentUserService
    {
        Guid? UserId { get; }
        string? Email { get; }
        IEnumerable<string> Roles { get; }
        bool IsAuthenticated { get; }
    }
}
