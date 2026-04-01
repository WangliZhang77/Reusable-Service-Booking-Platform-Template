using BookingTemplate.Application.Interfaces.Services;
using BookingTemplate.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingTemplate.Api.Controllers;

[ApiController]
[Route("api/debug")]
public sealed class DebugController(
    IFaqService faqService,
    BookingChatToolExecutor toolExecutor) : ControllerBase
{
    [HttpGet("faq-status")]
    public async Task<IActionResult> FaqStatus(CancellationToken cancellationToken)
    {
        var all = await faqService.GetAllAsync(cancellationToken);
        var published = await faqService.GetPublishedAsync(cancellationToken);

        return Ok(new
        {
            allCount = all.Count,
            publishedCount = published.Count,
            sampleQuestions = published.Take(5).Select(x => x.Question).ToList()
        });
    }

    [HttpGet("search-faq")]
    public async Task<IActionResult> SearchFaq([FromQuery] string question, CancellationToken cancellationToken)
    {
        var result = await toolExecutor.SearchFaqAsync(question, cancellationToken);
        return Ok(new { question, result });
    }
}
