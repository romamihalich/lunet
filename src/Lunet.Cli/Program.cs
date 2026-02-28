using Lunet;

if (args.Length == 0)
{
    Console.Error.WriteLine("ERROR: No input provided");
    return 1;
}

var sourceFilePath = args[0];

string sourceCode;
try
{
    sourceCode = File.ReadAllText(sourceFilePath);
}
catch (IOException e)
{
    Console.Error.WriteLine($"ERROR: Could not open file: {e.Message}");
    return 1;
}

var diagnostics = new Diagnostics();

var lexer = new Lexer(sourceCode, diagnostics);
var parser = new Parser(lexer, diagnostics);
var ast = parser.Parse();

if (diagnostics.HasError)
{
    PrintDiagnostics(diagnostics);
    return 1;
}

var outFilePath = Path.ChangeExtension(sourceFilePath, ".dll");

CodeGen.Generate(ast, outFilePath, diagnostics);

if (diagnostics.HasError)
{
    PrintDiagnostics(diagnostics);
    return 1;
}

Console.WriteLine($"Generated file: {outFilePath}");

return 0;

static void PrintDiagnostics(Diagnostics diagnostics)
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
        Console.Error.WriteLine($"{severity}:{row}:{col}: {message}");
    }
    Console.ForegroundColor = foreground;
}