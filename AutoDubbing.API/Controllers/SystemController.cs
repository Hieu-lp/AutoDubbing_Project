using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        [HttpGet("health")]
        public IActionResult CheckHealth()
        {
            return Ok(new
            {
                Status = "Online",
                Message = "AutoDubbing API is running perfectly on Docker!",
                Time = DateTime.UtcNow
            });
        }
    }
}