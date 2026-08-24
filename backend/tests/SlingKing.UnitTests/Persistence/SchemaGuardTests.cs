using Shouldly;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.UnitTests.Persistence;

/// <summary>
/// These exercise the guard's decision logic against connection <em>strings</em>. Nothing here
/// opens a socket: the guard has to be trustworthy on a machine with no database in reach,
/// because that is exactly the machine on which someone will paste the wrong connection string.
/// </summary>
public class SchemaGuardTests
{
    [Theory]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=test")]
    [InlineData("Host=db.supabase.co;Database=postgres;SearchPath=test")]
    [InlineData("Host=db.supabase.co;Database=postgres;search path=TEST")]
    public void Accepts_a_connection_string_pinned_to_the_test_schema(string connection)
    {
        Should.NotThrow(() => TestSchemaFixture.AssertTargetsTestSchema(connection));
    }

    [Theory]
    [InlineData("Host=db.supabase.co;Database=postgres")]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=public")]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=public,test")]
    [InlineData("")]
    public void Refuses_anything_that_could_reach_real_data(string connection)
    {
        Should.Throw<InvalidOperationException>(
            () => TestSchemaFixture.AssertTargetsTestSchema(connection));
    }

    /// <summary>
    /// A search path is a list, and an unqualified write lands in its <em>first</em> entry.
    /// "test" appearing somewhere in the list is therefore not safety: any list mentioning
    /// public leaves a stray statement one typo away from the real stock.
    /// </summary>
    [Theory]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=test,public")]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=public,test")]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=test,other")]
    public void Refuses_a_multi_entry_search_path_even_when_it_contains_test(string connection)
    {
        Should.Throw<InvalidOperationException>(
            () => TestSchemaFixture.AssertTargetsTestSchema(connection));
    }

    [Fact]
    public void Refuses_a_null_connection_string()
    {
        Should.Throw<InvalidOperationException>(
            () => TestSchemaFixture.AssertTargetsTestSchema(null!));
    }

    [Fact]
    public void Refuses_a_whitespace_only_connection_string()
    {
        Should.Throw<InvalidOperationException>(
            () => TestSchemaFixture.AssertTargetsTestSchema("   "));
    }

    [Fact]
    public void Says_which_variable_is_missing_when_nothing_is_configured()
    {
        var error = Should.Throw<InvalidOperationException>(
            () => TestSchemaFixture.AssertTargetsTestSchema(""));

        error.Message.ShouldContain("SLINGKING_DB_TEST");
    }

    [Fact]
    public void Names_the_offending_search_path_so_the_fix_is_obvious()
    {
        var error = Should.Throw<InvalidOperationException>(
            () => TestSchemaFixture.AssertTargetsTestSchema(
                "Host=db.supabase.co;Database=postgres;Search Path=public"));

        error.Message.ShouldContain("public");
        error.Message.ShouldContain(TestSchemaFixture.SchemaName);
    }
}
