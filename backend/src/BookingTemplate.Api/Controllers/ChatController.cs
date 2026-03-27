using BookingTemplate.Application.DTOs.Chat;
using BookingTemplate.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingTemplate.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(IChatService chatService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await chatService.ReplyAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            // Chat widget expects JSON; return 200 with a safe message so the UI does not show "500".
            return Ok(new ChatResponseDto(
                "Sorry, something went wrong while processing your message. Please try again in a moment.",
                "error"));
        }
    }
}
