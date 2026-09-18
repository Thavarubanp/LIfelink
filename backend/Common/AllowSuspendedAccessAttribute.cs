using System;

namespace LifeLink.Common
{
    /// <summary>
    /// Decorates controllers or action methods that permit access by suspended accounts in Restricted Governance Mode.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class AllowSuspendedAccessAttribute : Attribute
    {
    }
}
