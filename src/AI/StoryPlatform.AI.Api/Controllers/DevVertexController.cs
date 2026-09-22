using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.AI.Application.Abstractions.LLM;

namespace StoryPlatform.AI.Api.Controllers;

/// <summary>
/// Dev-only connectivity test for the Vertex AI provider path.
/// Returns 404 outside the Development environment so it can never leak
/// into staging or production.
/// </summary>
[ApiController]
[Route("api/dev/ai")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DevVertexController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public DevVertexController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpPost("vertex-test")]
    public async Task<IActionResult> VertexTest(
        [FromServices] ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        using var schemaDocument = JsonDocument.Parse("""{"type":"string"}""");

        var result = await llmClient.GenerateStructuredAsync(
            prompt: "Return exactly: VERTEX_AI_OK",
            schemaName: "vertex_connectivity_check",
            schema: schemaDocument.RootElement,
            cancellationToken);

        return Ok(new
        {
            status = "ok",
            provider = result.ModelProvider,
            model = result.Model,
            modelVersion = result.ModelVersion,
            response = result.Content,
            latencyMs = result.LatencyMs
        });
    }
}
