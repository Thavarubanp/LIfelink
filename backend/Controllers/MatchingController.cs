using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Matching;
using LifeLink.Services.Matching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/matching")]
    [Authorize(Roles = "Admin")]
    public class MatchingController : ControllerBase
    {
        private readonly IMatchingService _matchingService;

        public MatchingController(IMatchingService matchingService)
        {
            _matchingService = matchingService;
        }

        [HttpGet]
        public async Task<IActionResult> GetMatches()
        {
            var list = await _matchingService.GetMatchesAsync();
            return Ok(list);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetMatchById(Guid id)
        {
            var match = await _matchingService.GetMatchByIdAsync(id);
            if (match == null) return NotFound(new { message = $"Match with ID {id} not found." });
            return Ok(match);
        }
    }
}
