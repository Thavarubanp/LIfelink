using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Verification;
using LifeLink.Services.Verification;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/requests")]
    public class RequestsVerificationController : ControllerBase
    {
        private readonly IVerificationService _verificationService;

        public RequestsVerificationController(IVerificationService verificationService)
        {
            _verificationService = verificationService;
        }

        [HttpPut("{id:guid}/approve")]
        public async Task<IActionResult> ApproveRequest(Guid id, [FromBody] ApproveRejectRequestDto dto)
        {
            try
            {
                var result = await _verificationService.ApproveBloodRequestAsync(id, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id:guid}/reject")]
        public async Task<IActionResult> RejectRequest(Guid id, [FromBody] ApproveRejectRequestDto dto)
        {
            try
            {
                var result = await _verificationService.RejectBloodRequestAsync(id, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("verifications")]
        public async Task<IActionResult> GetVerifications()
        {
            var list = await _verificationService.GetBloodRequestVerificationsAsync();
            return Ok(list);
        }
    }
}
