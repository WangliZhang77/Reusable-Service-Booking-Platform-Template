using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.AspNetCore.Mvc;

namespace BookingTemplate.Api.Controllers;

/// <summary>
/// 最小连通性测试：不经过 booking / function calling，仅验证 API Key 与 Google.GenAI SDK。
/// Key 来源：Gemini:ApiKey（含 User Secrets / appsettings）或环境变量 GEMINI_API_KEY。
/// </summary>
[ApiController]
[Route("api/test-gemini")]
public sealed class TestGeminiController(IConfiguration configuration) : ControllerBase
{
    private const string ModelId = "gemini-3-flash-preview";

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new
            {
                ok = false,
                error = "No API key. Set Gemini:ApiKey (User Secrets) or environment variable GEMINI_API_KEY."
            });
        }

        try
        {
            var client = new Client(apiKey: apiKey);
            var response = await client.Models.GenerateContentAsync(
                model: ModelId,
                contents: "Reply with only: backend works",
                cancellationToken: cancellationToken);

            var reply = ExtractFirstText(response);
            if (string.IsNullOrWhiteSpace(reply))
            {
                return StatusCode(502, new { ok = false, error = "Empty model response" });
            }

            return Ok(new { ok = true, reply });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { ok = false, error = ex.Message });
        }
    }

    private static string? ExtractFirstText(GenerateContentResponse response)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        var parts = candidate?.Content?.Parts;
        if (parts is null || parts.Count == 0)
        {
            return null;
        }

        return parts[0].Text;
    }

    private string? ResolveApiKey()
    {
        var fromConfig = configuration["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig.Trim();
        }

        return System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    }
}
