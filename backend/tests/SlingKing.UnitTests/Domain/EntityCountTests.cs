using Shouldly;
using Xunit;

namespace SlingKing.UnitTests.Domain;

public class EntityCountTests
{
    private static readonly string[] EntityNamespaces =
    {
        "SlingKing.Domain.Platform",
        "SlingKing.Domain.Catalog",
        "SlingKing.Domain.Inventory",
        "SlingKing.Domain.Purchasing",
        "SlingKing.Domain.Sales",
        "SlingKing.Domain.Payments",
        "SlingKing.Domain.Operations"
    };

    [Fact]
    public void The_domain_declares_exactly_twenty_seven_entities()
    {
        var entities = typeof(SlingKing.Domain.DomainAssembly).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && EntityNamespaces.Contains(t.Namespace))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToArray();

        entities.Length.ShouldBe(27,
            "expected 27 entities, found " + entities.Length + ": " + string.Join(", ", entities));
    }

    [Fact]
    public void Every_entity_namespace_is_populated()
    {
        var assembly = typeof(SlingKing.Domain.DomainAssembly).Assembly;

        foreach (var ns in EntityNamespaces)
        {
            assembly.GetTypes()
                .Count(t => t.IsClass && !t.IsAbstract && t.Namespace == ns)
                .ShouldBeGreaterThan(0, $"{ns} has no entities");
        }
    }
}
