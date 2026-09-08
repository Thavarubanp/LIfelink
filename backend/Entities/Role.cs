using System.Collections.Generic;

namespace LifeLink.Entities
{
    public class Role
    {
        public int RoleId { get; set; }
        public string Name { get; set; } = string.Empty;

        // Navigation property
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}
