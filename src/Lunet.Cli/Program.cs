using Lunet;

var sourceFilePath = "/home/romamihalich/projects/lunet/examples/hello_world.ln";
var sourceCode = File.ReadAllText(sourceFilePath);

var diagnostics = new Diagnostics();

var lexer = new Lexer(sourceCode, diagnostics);
var parser = new Parser(lexer, diagnostics);
var ast = parser.Parse();

if (diagnostics.HasError)
{
    var foreground = Console.ForegroundColor;
    foreach (var diagnostic in diagnostics)
    {
        Console.ForegroundColor = foreground;

        string severity;
        switch (diagnostic.Severity)
        {
            case DiagnosticSeverity.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                severity = "ERROR";
                break;
            case DiagnosticSeverity.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                severity = "WARNING";
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var row = diagnostic.Location.StartRow;
        var col = diagnostic.Location.StartCol;
        var message = diagnostic.Message;
        Console.WriteLine($"{severity}:{row}:{col}: {message}");
    }
    Console.ForegroundColor = foreground;
    return 1;
}

var outFilePath = Path.ChangeExtension(sourceFilePath, ".dll");
CodeGen.Generate(ast, outFilePath);
Console.WriteLine($"Generated file: {outFilePath}");

return 0;