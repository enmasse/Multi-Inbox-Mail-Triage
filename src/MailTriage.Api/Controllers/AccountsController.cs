using Microsoft.AspNetCore.Mvc;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly IEmailRepository _repository;

    public AccountsController(IEmailRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAccounts(CancellationToken cancellationToken)
    {
        var accounts = await _repository.GetMailAccountsAsync(cancellationToken);
        // Don't expose passwords in the response
        var result = accounts.Select(a => new
        {
            a.Id, a.Name, a.Host, a.Port, a.Username, a.UseSsl,
            a.IsEnabled, a.Mailbox, a.PollingIntervalSeconds, a.CreatedAt
        });
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAccount(int id, CancellationToken cancellationToken)
    {
        var account = await _repository.GetMailAccountAsync(id, cancellationToken);
        if (account == null) return NotFound();
        return Ok(new { account.Id, account.Name, account.Host, account.Port, account.Username, account.UseSsl, account.IsEnabled, account.Mailbox, account.PollingIntervalSeconds, account.CreatedAt });
    }

    [HttpPost]
    public async Task<IActionResult> AddAccount([FromBody] CreateAccountRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var account = new MailAccount
        {
            Name = request.Name,
            Host = request.Host,
            Port = request.Port,
            Username = request.Username,
            Password = request.Password,
            UseSsl = request.UseSsl,
            IsEnabled = request.IsEnabled,
            Mailbox = request.Mailbox,
            PollingIntervalSeconds = request.PollingIntervalSeconds
        };
        var created = await _repository.AddMailAccountAsync(account, cancellationToken);
        return CreatedAtAction(nameof(GetAccount), new { id = created.Id }, new { created.Id, created.Name });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAccount(int id, [FromBody] UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await _repository.GetMailAccountAsync(id, cancellationToken);
        if (account == null) return NotFound();
        account.Name = request.Name ?? account.Name;
        account.IsEnabled = request.IsEnabled ?? account.IsEnabled;
        account.PollingIntervalSeconds = request.PollingIntervalSeconds ?? account.PollingIntervalSeconds;
        if (!string.IsNullOrEmpty(request.Password)) account.Password = request.Password;
        await _repository.UpdateMailAccountAsync(account, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAccount(int id, CancellationToken cancellationToken)
    {
        await _repository.DeleteMailAccountAsync(id, cancellationToken);
        return NoContent();
    }
}

public record CreateAccountRequest(
    string Name,
    string Host,
    int Port,
    string Username,
    string Password,
    bool UseSsl = true,
    bool IsEnabled = true,
    string Mailbox = "INBOX",
    int PollingIntervalSeconds = 60
);

public record UpdateAccountRequest(
    string? Name,
    bool? IsEnabled,
    int? PollingIntervalSeconds,
    string? Password
);
