
using Xunit;

namespace StockPilot.IntegrationTests;

[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection
    : ICollectionFixture<CustomWebApplicationFactory>
{
}