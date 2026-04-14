using Microsoft.AspNetCore.Mvc;
using MailTriage.Core.Interfaces;

namespace MailTriage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TriageController : ControllerBase
{
    private readonly ITriageService _triageService;
    private readonly IMailTriageMetrics _metrics;

    public TriageController(ITriageService triageService, IMailTriageMetrics metrics)
    {
        _triageService = triageService;
        _metrics = metrics;
    }

    [HttpPost]
    public async Task<IActionResult> TriageEmail([FromBody] ManualTriageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _triageService.TriageEmailAsync(request.Subject, request.FromAddress, request.BodyText, cancellationToken);
            _metrics.RecordTriageRequest(true);
            return Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordTriageRequest(false);
            throw;
        }
    }
}

public record ManualTriageRequest(string Subject, string FromAddress, string BodyText);
