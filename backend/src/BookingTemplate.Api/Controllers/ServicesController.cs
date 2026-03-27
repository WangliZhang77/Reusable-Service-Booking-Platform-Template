using BookingTemplate.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingTemplate.Api.Controllers;

[ApiController]
[Route("api/services")]
public sealed class ServicesController(IBookingService bookingService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetServices(CancellationToken cancellationToken)
    {
        var services = await bookingService.GetServicesAsync(cancellationToken);
        return Ok(services);
    }
}
