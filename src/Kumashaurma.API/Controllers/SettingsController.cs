using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Kumashaurma.API.Options;
using Kumashaurma.API.Services;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Публичные настройки заведения, нужные клиенту (часы работы и т.п.)
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : ControllerBase
    {
        private readonly RestaurantOptions _restaurant;
        private readonly IPaymentService _payments;

        public SettingsController(IOptions<RestaurantOptions> restaurant, IPaymentService payments)
        {
            _restaurant = restaurant.Value;
            _payments = payments;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var nowUtc = DateTime.UtcNow;
            return Ok(new
            {
                _restaurant.TimeZone,
                _restaurant.OpenTime,
                _restaurant.CloseTime,
                _restaurant.MinLeadMinutes,
                _restaurant.MaxDaysAhead,
                _restaurant.Latitude,
                _restaurant.Longitude,
                IsOpenNow = _restaurant.IsWithinWorkingHours(nowUtc),
                OnlinePaymentEnabled = _payments.OnlineEnabled,
                ServerTimeUtc = nowUtc
            });
        }
    }
}
