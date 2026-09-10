using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Doctors;
using LifeLink.Services.Doctors;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DoctorsController : ControllerBase
    {
        private readonly IDoctorService _doctorService;

        public DoctorsController(IDoctorService doctorService)
        {
            _doctorService = doctorService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateDoctor([FromBody] CreateDoctorDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _doctorService.CreateDoctorAsync(dto);
                return CreatedAtAction(nameof(GetDoctorById), new { id = result.DoctorId }, result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDoctors([FromQuery] Guid? hospitalId)
        {
            var list = await _doctorService.GetDoctorsAsync(hospitalId);
            return Ok(list);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetDoctorById(Guid id)
        {
            var doctor = await _doctorService.GetDoctorByIdAsync(id);
            if (doctor == null) return NotFound(new { message = $"Doctor with ID {id} not found." });
            return Ok(doctor);
        }
    }
}
