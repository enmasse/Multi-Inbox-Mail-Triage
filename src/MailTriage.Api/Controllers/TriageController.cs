using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MailTriage.Core.Interfaces;

namespace MailTriage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TriageController : ControllerBase
{
    private readonly ITriageService _triageService;

    public TriageController(ITriageService triageService)
    {
        _triageService = triageService;
    }

    [HttpPost]
    public async Task<IActionResult> TriageEmail([FromBody] ManualTriageRequest request, CancellationToken cancellationToken)
    {
        var result = await _triageService.TriageEmailAsync(request.Subject, request.FromAddress, request.BodyText, cancellationToken);
        return Ok(result);
    }
}

public record ManualTriageRequest(string Subject, string FromAddress, string BodyText);
