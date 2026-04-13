using Microsoft.AspNetCore.Mvc;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RulesController : ControllerBase
{
    private readonly IEmailRepository _repository;

    public RulesController(IEmailRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetForwardingRulesAsync(cancellationToken);
        return Ok(rules);
    }

    [HttpPost]
    public async Task<IActionResult> AddRule([FromBody] ForwardingRule rule, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        rule.Id = 0;
        rule.CreatedAt = DateTime.UtcNow;
        var created = await _repository.AddForwardingRuleAsync(rule, cancellationToken);
        return CreatedAtAction(nameof(GetRules), new { id = created.Id }, created);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRule(int id, CancellationToken cancellationToken)
    {
        await _repository.DeleteForwardingRuleAsync(id, cancellationToken);
        return NoContent();
    }
}
