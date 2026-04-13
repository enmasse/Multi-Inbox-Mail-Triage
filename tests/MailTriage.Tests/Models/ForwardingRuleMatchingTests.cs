using FluentAssertions;
using MailTriage.Core.Models;

namespace MailTriage.Tests.Models;

/// <summary>
/// Tests the forwarding rule model properties and enum semantics.
/// </summary>
public class ForwardingRuleMatchingTests
{
    [Fact]
    public void ForwardingRule_DefaultsToEnabled()
    {
        var rule = new ForwardingRule();
        rule.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ForwardingRule_CanMatchByCategory()
    {
        var rule = new ForwardingRule
        {
            Name = "Forward Spam",
            ForwardToAddress = "spam@example.com",
            MatchCategory = TriageCategory.Spam
        };

        rule.MatchCategory.Should().Be(TriageCategory.Spam);
        rule.MinPriority.Should().BeNull();
    }

    [Fact]
    public void TriageCategory_EnumValues_AreCorrect()
    {
        TriageCategory.Unknown.Should().Be((TriageCategory)0);
        TriageCategory.ActionRequired.Should().Be((TriageCategory)1);
        TriageCategory.Spam.Should().Be((TriageCategory)4);
    }

    [Fact]
    public void TriagePriority_EnumValues_AreOrdered()
    {
        ((int)TriagePriority.Low).Should().BeLessThan((int)TriagePriority.Normal);
        ((int)TriagePriority.Normal).Should().BeLessThan((int)TriagePriority.High);
        ((int)TriagePriority.High).Should().BeLessThan((int)TriagePriority.Urgent);
    }

    [Theory]
    [InlineData(TriagePriority.Urgent, TriagePriority.High, true)]
    [InlineData(TriagePriority.Low, TriagePriority.High, false)]
    [InlineData(TriagePriority.High, TriagePriority.High, true)]
    public void Priority_Comparison_WorksAsExpected(TriagePriority emailPriority, TriagePriority minPriority, bool shouldMatch)
    {
        (emailPriority >= minPriority).Should().Be(shouldMatch);
    }

    [Fact]
    public void MailAccount_DefaultValues_AreReasonable()
    {
        var account = new MailAccount();
        account.Port.Should().Be(993);
        account.UseSsl.Should().BeTrue();
        account.IsEnabled.Should().BeTrue();
        account.Mailbox.Should().Be("INBOX");
        account.PollingIntervalSeconds.Should().Be(60);
    }

    [Fact]
    public void TriagedEmail_DefaultCategory_IsUnknown()
    {
        var email = new TriagedEmail();
        email.Category.Should().Be(TriageCategory.Unknown);
        email.Priority.Should().Be(TriagePriority.Normal);
    }
}
