namespace Lunet.Tests;

[TestFixture]
public class ParserTests
{
    private readonly VerifySettings _verifySettings = new();

    public ParserTests()
    {
        _verifySettings.UseDirectory("Snapshots");
        _verifySettings.AddExtraSettings(jsonOpts =>
        {
            jsonOpts.TypeNameHandling = Argon.TypeNameHandling.Objects;
            jsonOpts.DefaultValueHandling = Argon.DefaultValueHandling.Include;
        });
    }

    [Test]
    public Task HelloWorldTest()
    {
        var text = """
        import System

        function main()
            System::Console.WriteLine("Hello, world")
        end
        """;

        var diagnostics = new Diagnostics();
        var lexer = new Lexer(text, diagnostics);
        var parser = new Parser(lexer, diagnostics);
        var ast = parser.Parse();

        Assert.That(!diagnostics.Any(), "Diagnostics should be empty");

        return Verify(ast, _verifySettings);
    }

    [Test]
    public Task VariableTest()
    {
        var text = """
        import System

        function main()
            local greeting: string = "Hello, world"
            System::Console.WriteLine(greeting)
        end
        """;

        var diagnostics = new Diagnostics();
        var lexer = new Lexer(text, diagnostics);
        var parser = new Parser(lexer, diagnostics);
        var ast = parser.Parse();

        Assert.That(!diagnostics.Any(), "Diagnostics should be empty");

        return Verify(ast, _verifySettings);
    }

    [Test]
    [TestCase("01", "2 + 4 * 5")]
    [TestCase("02", "(2 + 4) * 5")]
    [TestCase("03", "x > 3 and x < 9")]
    [TestCase("04", "\"Hello, \" .. name")]
    [TestCase("05", "n == 5 or n ~= 2 and not (n >= 10)")]
    [TestCase("06", "10 / 2 - 3 <= f")]
    public Task Expressions(string n, string text)
    {
        var diagnostics = new Diagnostics();
        var lexer = new Lexer(text, diagnostics);
        var parser = new Parser(lexer, diagnostics);
        var expr = parser.ParseExpression();

        Assert.That(!diagnostics.Any(), "Diagnostics should be empty");

        return Verify(expr, _verifySettings)
            .UseFileName($"{nameof(ParserTests)}.{nameof(Expressions)}_{n}");
    }

    [Test]
    public Task Functions()
    {
        var text = """
        function sum(a: int, b: int) -> int
            return a + b
        end
        """;

        var diagnostics = new Diagnostics();
        var lexer = new Lexer(text, diagnostics);
        var parser = new Parser(lexer, diagnostics);
        var ast = parser.Parse();

        Assert.That(!diagnostics.Any(), "Diagnostics should be empty");

        return Verify(ast, _verifySettings);
    }
}