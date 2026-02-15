namespace Lunet;

public record Ast(IReadOnlyList<ITopLevelStatement> Statements);

public interface ITopLevelStatement;
public record ImportStatement(NamespacePath Path) : ITopLevelStatement;
public record FunctionStatement(string Name, IReadOnlyList<IStatement> Body) : ITopLevelStatement;

public interface IStatement;
public record ExpressionStatement(FunctionCallExpression Expression) : IStatement;
public record VariableDefinitionStatement(string Name, string Type, IExpression Rvalue) : IStatement;

public interface IExpression;
public record IdentExpression(string Name) : IExpression;
public record StringExpression(string Value) : IExpression;
public record IntExpression(int Value) : IExpression;
public record BoolExpression(bool Value) : IExpression;
public record FunctionCallExpression(QualifiedIdentExpression Name, IReadOnlyList<IExpression> Args) : IExpression;

public record QualifiedIdentExpression(NamespacePath Path, string? Ident) : IExpression;

public record NamespacePath(IReadOnlyList<string> Path);

public class Parser
{
    private readonly Lexer _lexer;
    private readonly Diagnostics _diagnostics;

    private Token _lookahead;

    public Parser(Lexer lexer, Diagnostics diagnostics)
    {
        _lexer = lexer;
        _diagnostics = diagnostics;
        _lookahead = lexer.Lex();
    }

    public Ast Parse()
    {
        var statements = new List<ITopLevelStatement>();

        while (ParseTopLevelStatement() is { } statement)
        {
            statements.Add(statement);
        }

        return new Ast(statements);
    }

    private ITopLevelStatement? ParseTopLevelStatement()
    {
        switch (Peek().Kind)
        {
            case TokenKind.Import:
                return ParseImportStatement();

            case TokenKind.Function:
                return ParseFunctionStatement();

            default:
                return null;
        }
    }

    private FunctionStatement? ParseFunctionStatement()
    {
        if (!ExpectToken(TokenKind.Function, out _))
        {
            return null;
        }

        if (!ExpectToken(TokenKind.Ident, out var nameToken))
        {
            return null;
        }

        var name = (string)nameToken.Value!;

        if (!ExpectToken(TokenKind.OParen, out _)) return null;
        if (!ExpectToken(TokenKind.CParen, out _)) return null;

        var body = new List<IStatement>();

        while (ParseStatement() is { } statement)
        {
            body.Add(statement);
        }

        if (!ExpectToken(TokenKind.End, out _)) return null;

        return new FunctionStatement(name, body);
    }

    private ImportStatement? ParseImportStatement()
    {
        if (!ExpectToken(TokenKind.Import, out _))
        {
            return null;
        }

        if (!ExpectToken(TokenKind.Ident, out var ident))
        {
            return null;
        }

        return new(new([(string)ident.Value!]));
    }

    private IStatement? ParseStatement()
    {
        switch (Peek().Kind)
        {
            case TokenKind.Ident:
            {
                var identExpr = ParseQualifiedIdentExpression();
                if (identExpr == null)
                {
                    return null;
                }
                if (Peek().Kind == TokenKind.OParen)
                {
                    var args = ParseArgs();
                    if (args == null) return null;
                    return new ExpressionStatement(
                        new FunctionCallExpression(identExpr, args)
                    );
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            case TokenKind.Local:
            {
                return ParseVariableDefinitionStatement();
            }
            default:
                return null;
        }
    }

    private VariableDefinitionStatement? ParseVariableDefinitionStatement()
    {
        if (!ExpectToken(TokenKind.Local))
        {
            return null;
        }

        if (!ExpectToken(TokenKind.Ident, out var nameTok))
        {
            return null;
        }

        var name = (string)nameTok.Value!;

        if (!ExpectToken(TokenKind.Colon))
        {
            return null;
        }

        // TODO: qualified name
        if (!ExpectToken(TokenKind.Ident, out var typeTok))
        {
            return null;
        }

        var type = (string)typeTok.Value!;

        if (!ExpectToken(TokenKind.Equals))
        {
            return null;
        }

        var rvalue = ParseExpression();

        if (rvalue == null)
        {
            return null;
        }

        return new VariableDefinitionStatement(name, type, rvalue);
    }

    private IReadOnlyList<IExpression>? ParseArgs()
    {
        if (!ExpectToken(TokenKind.OParen, out _)) return null;

        var expr = ParseExpression();

        if (expr == null)
        {
            return null;
        }

        // TODO: support for multiple args

        if (!ExpectToken(TokenKind.CParen, out _)) return null;

        return [expr];
    }

    private QualifiedIdentExpression? ParseQualifiedIdentExpression()
    {
        if (!ExpectToken(TokenKind.Ident, out var firstIdent))
        {
            return null;
        }

        var peek = Peek();
        if (peek.Kind == TokenKind.DoubleColon)
        {
            var path = new List<string>() { (string)firstIdent.Value! };
            while (Peek().Kind == TokenKind.DoubleColon)
            {
                NextToken();
                if (!ExpectToken(TokenKind.Ident, out var ident)) return null;
                path.Add((string)ident.Value!);
            }
            if (Peek().Kind == TokenKind.Dot)
            {
                NextToken();
                if (!ExpectToken(TokenKind.Ident, out var ident)) return null;
                return new QualifiedIdentExpression(new(path), (string)ident.Value!);
            }
            return new QualifiedIdentExpression(new(path), null);
        }
        else if (peek.Kind == TokenKind.Dot)
        {
            NextToken();
            if (!ExpectToken(TokenKind.Ident, out var ident)) return null;
            return new QualifiedIdentExpression(new([(string)firstIdent.Value!]), (string)ident.Value!);
        }
        else
        {
            return new QualifiedIdentExpression(new([]), (string)firstIdent.Value!);
        }
    }

    private IExpression? ParseExpression()
    {
        var t = NextToken();
        switch (t.Kind)
        {
            case TokenKind.Ident:
                return new IdentExpression((string)t.Value!);

            case TokenKind.String:
                return new StringExpression((string)t.Value!);

            case TokenKind.Int:
                return new IntExpression((int)t.Value!);

            case TokenKind.True:
                return new BoolExpression(true);

            case TokenKind.False:
                return new BoolExpression(false);

            default:
                return null;
        }
    }

    private bool ExpectToken(TokenKind kind, out Token t)
    {
        t = NextToken();
        if (t.Kind != kind)
        {
            _diagnostics.AddError(t.Location, $"Unexpected token \"{t.Kind}\"");
            return false;
        }

        return true;
    }

    private bool ExpectToken(TokenKind kind)
    {
        return ExpectToken(kind, out var _);
    }

    private Token Peek()
    {
        return _lookahead;
    }

    private Token NextToken()
    {
        var t = _lookahead;
        _lookahead = _lexer.Lex();
        return t;
    }
}