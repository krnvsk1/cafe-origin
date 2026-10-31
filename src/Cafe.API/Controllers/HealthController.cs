using Microsoft.AspNetCore.Mvc;

namespace Cafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new 
            { 
                Status = "Healthy",
                Service = "Cafe API",
                Timestamp = DateTime.UtcNow,
                Version = "1.0.0",
                Endpoints = new[]
                {
                    "GET /api/health",
                    "GET /api/direct",
                    "GET /api/dishes",
                    "GET /api/orders",
                    "GET /swagger"
                }
            });
        }
    }
}
