using Lunet;

var sourceCode = File.ReadAllText("/home/romamihalich/projects/lunet/examples/hello_world.ln");
var l = new Lexer(sourceCode);
Token t;
while ((t = l.Lex()).Kind != TokenKind.Eof)
{
    Console.WriteLine($"{t.Kind}{(t.Value == null ? "" : $"({t.Value})")}");
}


l = new Lexer(sourceCode);
var p = new Parser(l);
var ast = p.Parse();
foreach (var statement in ast.Statements)
{
    Console.WriteLine(statement);
}
