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
        var l = new Lexer(HelloWorld);
        var p = new Parser(l);
        var ast = p.Parse();

        return Verify(ast, _verifySettings);
    }
}