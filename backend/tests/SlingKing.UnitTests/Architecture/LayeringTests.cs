using System.Reflection;
using Shouldly;
using Xunit;

namespace SlingKing.UnitTests.Architecture;

public class LayeringTests
{
    private static readonly string[] FrameworkPrefixes =
        { "System", "netstandard", "mscorlib", "Microsoft.CSharp" };

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_class_library()
    {
        var domain = typeof(SlingKing.Domain.DomainAssembly).Assembly;

        var offenders = domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !FrameworkPrefixes.Any(p =>
                name.Equals(p, StringComparison.Ordinal) ||
                name.StartsWith(p + ".", StringComparison.Ordinal)))
            .ToArray();

        offenders.ShouldBeEmpty(
            "Domain must stay a leaf. Remove these references: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Application_does_not_reference_Infrastructure()
    {
        var application = typeof(SlingKing.Application.ApplicationAssembly).Assembly;

        application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ShouldNotContain("SlingKing.Infrastructure");
    }
}
