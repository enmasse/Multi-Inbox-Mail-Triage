namespace MailTriage.IntegrationTests.Infrastructure;

[CollectionDefinition("GreenMail")]
public class GreenMailCollection : ICollectionFixture<GreenMailContainerFixture>
{
    // Marker class — xUnit uses [CollectionDefinition] to wire the shared fixture.
}
