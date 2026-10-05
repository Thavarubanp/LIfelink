using System.Threading.Tasks;
using LifeLink.DTOs.Attention;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/attention")]
    [Authorize]
    public class AttentionController : ControllerBase
    {
        private readonly RoleAttentionService _attention;

        public AttentionController(RoleAttentionService attention)
        {
            _attention = attention;
        }

        [HttpGet]
        [ProducesResponseType(typeof(RoleAttentionDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<RoleAttentionDto>> Get() => Ok(await _attention.GetAsync());
    }
}
