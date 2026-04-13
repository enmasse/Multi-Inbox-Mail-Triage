using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;
using MailTriage.Infrastructure.Imap;

namespace MailTriage.Tests.Imap;

public class MailMonitorServiceTests
{
    private readonly Mock<IImapClientFactory> _mockClientFactory;
    private readonly Mock<IEmailRepository> _mockRepository;
    private readonly Mock<ITriageService> _mockTriageService;
    private readonly Mock<IEmailForwarder> _mockForwarder;
    private readonly ImapMailMonitorService _service;

    public MailMonitorServiceTests()
    {
        _mockClientFactory = new Mock<IImapClientFactory>();
        _mockRepository = new Mock<IEmailRepository>();
        _mockTriageService = new Mock<ITriageService>();
        _mockForwarder = new Mock<IEmailForwarder>();

        _service = new ImapMailMonitorService(
            _mockClientFactory.Object,
            _mockRepository.Object,
            _mockTriageService.Object,
            _mockForwarder.Object,
            NullLogger<ImapMailMonitorService>.Instance);
    }

    [Fact]
    public async Task PollAccountAsync_WhenClientFactoryThrows_ReturnsEmptyList()
    {
        var account = new MailAccount
        {
            Id = 1,
            Name = "Test",
            Host = "imap.example.com",
            Port = 993,
            Username = "user@example.com",
            Password = "pass",
            UseSsl = true
        };

        _mockClientFactory
            .Setup(f => f.CreateAndConnectAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        var results = await _service.PollAccountAsync(account);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_CompletesWithoutError()
    {
        await _service.StartAsync();
    }

    [Fact]
    public async Task StopAsync_CompletesWithoutError()
    {
        await _service.StopAsync();
    }
}
