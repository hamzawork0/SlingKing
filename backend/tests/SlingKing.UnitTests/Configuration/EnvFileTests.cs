using Shouldly;
using SlingKing.Infrastructure.Configuration;
using Xunit;

namespace SlingKing.UnitTests.Configuration;

public class EnvFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"env-{Guid.NewGuid():N}");

    private void WriteFile(string contents) => File.WriteAllText(_path, contents);

    [Fact]
    public void Reads_simple_key_value_pairs()
    {
        WriteFile("SK_TEST_ONE=hello\nSK_TEST_TWO=world\n");

        EnvFile.Load(_path).ShouldBe(2);

        Environment.GetEnvironmentVariable("SK_TEST_ONE").ShouldBe("hello");
        Environment.GetEnvironmentVariable("SK_TEST_TWO").ShouldBe("world");
    }

    [Fact]
    public void Ignores_comments_and_blank_lines()
    {
        WriteFile("# a comment\n\n   \nSK_TEST_THREE=value\n");

        EnvFile.Load(_path).ShouldBe(1);

        Environment.GetEnvironmentVariable("SK_TEST_THREE").ShouldBe("value");
    }

    [Fact]
    public void Keeps_equals_signs_inside_the_value()
    {
        WriteFile("SK_TEST_CONN=Host=db;Password=a=b=c\n");

        EnvFile.Load(_path);

        Environment.GetEnvironmentVariable("SK_TEST_CONN").ShouldBe("Host=db;Password=a=b=c");
    }

    [Fact]
    public void Strips_surrounding_quotes()
    {
        WriteFile("SK_TEST_QUOTED=\"spaced value\"\n");

        EnvFile.Load(_path);

        Environment.GetEnvironmentVariable("SK_TEST_QUOTED").ShouldBe("spaced value");
    }

    [Fact]
    public void Never_overwrites_a_variable_already_set_in_the_process()
    {
        Environment.SetEnvironmentVariable("SK_TEST_EXISTING", "from-process");
        WriteFile("SK_TEST_EXISTING=from-file\n");

        EnvFile.Load(_path).ShouldBe(0);

        Environment.GetEnvironmentVariable("SK_TEST_EXISTING").ShouldBe("from-process");
    }

    [Fact]
    public void A_missing_file_is_not_an_error()
    {
        EnvFile.Load(Path.Combine(Path.GetTempPath(), "definitely-not-here")).ShouldBe(0);
    }

    public void Dispose()
    {
        foreach (var key in new[]
                 {
                     "SK_TEST_ONE", "SK_TEST_TWO", "SK_TEST_THREE",
                     "SK_TEST_CONN", "SK_TEST_QUOTED", "SK_TEST_EXISTING"
                 })
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        if (File.Exists(_path)) File.Delete(_path);
    }
}
