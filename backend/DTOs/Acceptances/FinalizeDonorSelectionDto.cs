using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Acceptances
{
    /// <summary>Record donations: the approved donors who donated, with the tested blood group when it differs from the declared one.</summary>
    public class FinalizeDonorSelectionDto
    {
        public List<Guid> SelectedAcceptanceIds { get; set; } = new();
        public Dictionary<Guid, string>? TestedBloodGroups { get; set; }
    }
}
