using Lunet;

var sourceFilePath = "/home/romamihalich/projects/lunet/examples/hello_world.ln";

var sourceCode = File.ReadAllText(sourceFilePath);
var lexer = new Lexer(sourceCode);
var parser = new Parser(lexer);
var ast = parser.Parse();

var outFilePath = Path.ChangeExtension(sourceFilePath, ".dll");
CodeGen.Generate(ast, outFilePath);
Console.WriteLine($"Generated file: {outFilePath}");