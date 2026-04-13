using Microsoft.AspNetCore.Mvc;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmailsController : ControllerBase
{
    private readonly IEmailRepository _repository;

    public EmailsController(IEmailRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmails(
        [FromQuery] int? accountId,
        [FromQuery] TriageCategory? category,
        [FromQuery] TriagePriority? minPriority,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (take > 200) take = 200;
        var emails = await _repository.GetTriagedEmailsAsync(accountId, category, minPriority, skip, take, cancellationToken);
        return Ok(emails);
    }
}
