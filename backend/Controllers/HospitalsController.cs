using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Hospitals;
using LifeLink.Services.Hospitals;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HospitalsController : ControllerBase
    {
        private readonly IHospitalService _hospitalService;

        public HospitalsController(IHospitalService hospitalService)
        {
            _hospitalService = hospitalService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateHospital([FromBody] CreateHospitalDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var result = await _hospitalService.CreateHospitalAsync(dto);
            return CreatedAtAction(nameof(GetHospitalById), new { id = result.HospitalId }, result);
        }

        [HttpGet]
        public async Task<IActionResult> GetHospitals([FromQuery] bool? isVerified)
        {
            var list = await _hospitalService.GetHospitalsAsync(isVerified);
            return Ok(list);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetHospitalById(Guid id)
        {
            var hospital = await _hospitalService.GetHospitalByIdAsync(id);
            if (hospital == null) return NotFound(new { message = $"Hospital with ID {id} not found." });
            return Ok(hospital);
        }

        [HttpPut("{id:guid}/verify")]
        [HttpPut("{id:guid}/approve")]
        public async Task<IActionResult> VerifyHospital(Guid id, [FromBody] VerifyHospitalDto? dto)
        {
            var isVerified = dto?.IsVerified ?? true;
            var updated = await _hospitalService.VerifyHospitalAsync(id, isVerified);
            if (updated == null) return NotFound(new { message = $"Hospital with ID {id} not found." });
            return Ok(updated);
        }

        [HttpPut("{id:guid}/resubmit")]
        public async Task<IActionResult> ResubmitHospital(Guid id, [FromBody] ResubmitHospitalDto dto)
        {
            try
            {
                var result = await _hospitalService.ResubmitHospitalAsync(id, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
