namespace Lunet.Tests;

[TestFixture]
public class ParserTests
{
    private const string HelloWorld =
    """
    import System

    function main()
        System::Console.WriteLine("Hello, world")
    end
    """;

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
        var diagnostics = new Diagnostics();
        var lexer = new Lexer(HelloWorld, diagnostics);
        var parser = new Parser(lexer, diagnostics);
        var ast = parser.Parse();

        return Verify(ast, _verifySettings);
    }
}