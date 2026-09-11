using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Acceptances
{
    public class FinalizeDonorSelectionDto
    {
        public List<Guid> SelectedAcceptanceIds { get; set; } = new();
    }
}
