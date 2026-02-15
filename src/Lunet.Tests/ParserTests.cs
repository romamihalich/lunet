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
}