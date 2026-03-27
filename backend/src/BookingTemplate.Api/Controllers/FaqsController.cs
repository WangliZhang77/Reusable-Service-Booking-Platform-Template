using BookingTemplate.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingTemplate.Api.Controllers;

[ApiController]
[Route("api/faqs")]
public sealed class FaqsController(IFaqService faqService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var faqs = await faqService.GetAllAsync(cancellationToken);
        return Ok(faqs);
    }

    [HttpGet("published")]
    public async Task<IActionResult> GetPublished(CancellationToken cancellationToken)
    {
        var faqs = await faqService.GetPublishedAsync(cancellationToken);
        return Ok(faqs);
    }

    [HttpGet("by-category/{category}")]
    public async Task<IActionResult> GetByCategory([FromRoute] string category, CancellationToken cancellationToken)
    {
        try
        {
            var faqs = await faqService.GetByCategoryAsync(category, cancellationToken);
            return Ok(faqs);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
