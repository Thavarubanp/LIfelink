using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Verification;
using LifeLink.Services.Verification;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/donor-verification")]
    public class DonorVerificationController : ControllerBase
    {
        private readonly IVerificationService _verificationService;

        public DonorVerificationController(IVerificationService verificationService)
        {
            _verificationService = verificationService;
        }

        [HttpPut("{id:guid}/approve")]
        public async Task<IActionResult> ApproveDonorVerification(Guid id, [FromBody] ApproveRejectRequestDto dto)
        {
            try
            {
                var result = await _verificationService.ApproveDonorVerificationAsync(id, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id:guid}/reject")]
        public async Task<IActionResult> RejectDonorVerification(Guid id, [FromBody] ApproveRejectRequestDto dto)
        {
            try
            {
                var result = await _verificationService.RejectDonorVerificationAsync(id, dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDonorVerifications()
        {
            var list = await _verificationService.GetDonorVerificationsAsync();
            return Ok(list);
        }
    }
}
