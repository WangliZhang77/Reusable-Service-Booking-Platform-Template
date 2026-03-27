using BookingTemplate.Application.DTOs.Bookings;
using BookingTemplate.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingTemplate.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(IBookingService bookingService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await bookingService.CreateBookingAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetBookings), new { id = created.Id }, created);
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
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBookings([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var bookings = await bookingService.GetBookingsAsync(date, cancellationToken);
        return Ok(bookings);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateBookingStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await bookingService.UpdateBookingStatusAsync(
                id,
                request.Status,
                request.AdminNotes,
                request.CancelReason,
                cancellationToken);

            return Ok(updated);
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
            return Conflict(new { message = ex.Message });
        }
    }
}
