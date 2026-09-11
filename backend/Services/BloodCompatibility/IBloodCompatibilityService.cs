using System.Collections.Generic;

namespace LifeLink.Services.BloodCompatibility
{
    public interface IBloodCompatibilityService
    {
        bool IsCompatible(string donorBloodGroup, string recipientBloodGroup);
        List<string> GetCompatibleRecipients(string donorBloodGroup);
    }
}
