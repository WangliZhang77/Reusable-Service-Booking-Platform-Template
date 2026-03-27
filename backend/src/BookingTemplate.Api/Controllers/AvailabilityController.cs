using BookingTemplate.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingTemplate.Api.Controllers;

[ApiController]
[Route("api/availability")]
public sealed class AvailabilityController(IBookingService bookingService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAvailability(
        [FromQuery] Guid serviceId,
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        try
        {
            var availability = await bookingService.GetAvailabilityAsync(serviceId, date, cancellationToken);
            return Ok(availability);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
