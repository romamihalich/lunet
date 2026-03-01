namespace Lunet;

public record Ast(IReadOnlyList<ITopLevelStatement> Statements);

public interface ITopLevelStatement;
public record ImportStatement(NamespacePath Path, Location Location) : ITopLevelStatement;
public record FunctionStatement(string Name, IReadOnlyList<IStatement> Body) : ITopLevelStatement;

public interface IStatement;
public record ExpressionStatement(FunctionCallExpression Expression) : IStatement;
public record VariableDefinitionStatement(string Name, string Type, IExpression Rvalue, Location TypeLocation) : IStatement;
public record IfStatement(IExpression Condition, IReadOnlyList<IStatement> Block, IReadOnlyList<IStatement>? ElseBlock) : IStatement;

public interface IExpression
{
    public Location Location { get; }
}
public record ParenthesisedExpression(IExpression Expression, Location Location) : IExpression;
public record IdentExpression(string Name, Location Location) : IExpression;
public record StringExpression(string Value, Location Location) : IExpression;
public record IntExpression(int Value, Location Location) : IExpression;
public record BoolExpression(bool Value, Location Location) : IExpression;

public enum UnaryKind
{
    Not,
}

public record UnaryExpression(UnaryKind Kind, IExpression Expression, Location Location) : IExpression;

public enum BinopKind
{
    Mul, Div, Add, Sub, Concat,
    Equal, NotEqual, And, Or,
    Greater, GreaterOrEqual, Less, LessOrEqual,
}

public static class BinopFacts
{
    public const int MaxPrecedence = 6;

    public static int GetPrecedence(BinopKind kind) => kind switch
    {
        BinopKind.Mul            => MaxPrecedence,
        BinopKind.Div            => MaxPrecedence,
        BinopKind.Add           => MaxPrecedence - 1,
        BinopKind.Sub          => MaxPrecedence - 1,
        BinopKind.Concat         => MaxPrecedence - 2,
        BinopKind.Equal          => MaxPrecedence - 3,
        BinopKind.NotEqual       => MaxPrecedence - 3,
        BinopKind.Greater        => MaxPrecedence - 3,
        BinopKind.GreaterOrEqual => MaxPrecedence - 3,
        BinopKind.Less           => MaxPrecedence - 3,
        BinopKind.LessOrEqual    => MaxPrecedence - 3,
        BinopKind.And            => MaxPrecedence - 4,
        BinopKind.Or             => MaxPrecedence - 5,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string GetText(this BinopKind kind) => kind switch
    {
        BinopKind.Mul            => "*",
        BinopKind.Div            => "/",
        BinopKind.Add            => "+",
        BinopKind.Sub            => "-",
        BinopKind.Concat         => "..",
        BinopKind.Equal          => "==",
        BinopKind.NotEqual       => "~=",
        BinopKind.Greater        => ">",
        BinopKind.GreaterOrEqual => ">=",
        BinopKind.Less           => "<",
        BinopKind.LessOrEqual    => "<=",
        BinopKind.And            => "and",
        BinopKind.Or             => "or",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryGetBinop(TokenKind tokenKind, out BinopKind kind)
    {
        switch (tokenKind)
        {
            case TokenKind.Asterisk:
                kind = BinopKind.Mul;
                return true;

            case TokenKind.Slash:
                kind = BinopKind.Div;
                return true;

            case TokenKind.Plus:
                kind = BinopKind.Add;
                return true;

            case TokenKind.Minus:
                kind = BinopKind.Sub;
                return true;

            case TokenKind.DoubleDot:
                kind = BinopKind.Concat;
                return true;

            case TokenKind.DoubleEquals:
                kind = BinopKind.Equal;
                return true;

            case TokenKind.NotEquals:
                kind = BinopKind.NotEqual;
                return true;

            case TokenKind.And:
                kind = BinopKind.And;
                return true;

            case TokenKind.Or:
                kind = BinopKind.Or;
                return true;

            case TokenKind.Greater:
                kind = BinopKind.Greater;
                return true;

            case TokenKind.GreaterOrEqual:
                kind = BinopKind.GreaterOrEqual;
                return true;

            case TokenKind.Less:
                kind = BinopKind.Less;
                return true;

            case TokenKind.LessOrEqual:
                kind = BinopKind.LessOrEqual;
                return true;

            default:
                kind = default;
                return false;
        }
    }
}

public record BinopExpression(BinopKind Kind, IExpression Left, IExpression Right, Location Location) : IExpression;

public record FunctionCallExpression(QualifiedIdentExpression Name, IReadOnlyList<IExpression> Args, Location Location) : IExpression;

public record QualifiedIdentExpression(NamespacePath Path, string? Ident, Location Location) : IExpression;

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

        var body = ParseBlock();

        if (!ExpectToken(TokenKind.End, out _))
        {
            return null;
        }

        return new FunctionStatement(name, body);
    }

    private ImportStatement? ParseImportStatement()
    {
        if (!ExpectToken(TokenKind.Import, out var importToken))
        {
            return null;
        }

        if (!ExpectToken(TokenKind.Ident, out var ident))
        {
            return null;
        }

        var location = Location.Combine(importToken.Location, ident.Location);

        return new(new([(string)ident.Value!]), location);
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
                    var (args, argsLocation) = ParseArgs();
                    if (args == null)
                    {
                        return null;
                    }
                    var location = Location.Combine(identExpr.Location, argsLocation);
                    return new ExpressionStatement(
                        new FunctionCallExpression(identExpr, args, location)
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
            case TokenKind.If:
            {
                return ParseIfStatement();
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

        if (!ExpectToken(TokenKind.Ident, out var nameToken))
        {
            return null;
        }

        var name = (string)nameToken.Value!;

        if (!ExpectToken(TokenKind.Colon))
        {
            return null;
        }

        // TODO: qualified name
        if (!ExpectToken(TokenKind.Ident, out var typeToken))
        {
            return null;
        }

        var type = (string)typeToken.Value!;

        if (!ExpectToken(TokenKind.Equals))
        {
            return null;
        }

        var rvalue = ParseExpression();

        if (rvalue == null)
        {
            return null;
        }

        return new VariableDefinitionStatement(name, type, rvalue, typeToken.Location);
    }

    private IfStatement? ParseIfStatement()
    {
        if (!ExpectToken(TokenKind.If))
        {
            return null;
        }

        var cond = ParseExpression();
        if (cond == null)
        {
            return null;
        }

        if (!ExpectToken(TokenKind.Then))
        {
            return null;
        }

        var block = ParseBlock();

        IReadOnlyList<IStatement>? elseBlock = null;
        if (Peek().Kind == TokenKind.Else)
        {
            NextToken();
            elseBlock = ParseBlock();
        }

        if (!ExpectToken(TokenKind.End, out _))
        {
            return null;
        }

        return new IfStatement(cond, block, elseBlock);
    }

    private IReadOnlyList<IStatement> ParseBlock()
    {
        var block = new List<IStatement>();

        while (ParseStatement() is { } statement)
        {
            block.Add(statement);
        }

        return block;
    }

    private (IReadOnlyList<IExpression>?, Location) ParseArgs()
    {
        if (!ExpectToken(TokenKind.OParen, out var oparenToken)) return (null, default);

        var expr = ParseExpression();

        if (expr == null)
        {
            return (null, default);
        }

        // TODO: support for multiple args

        if (!ExpectToken(TokenKind.CParen, out var cparenToken)) return (null, default);

        return ([expr], Location.Combine(oparenToken.Location, cparenToken.Location));
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
            Location lastLocation = default;
            while (Peek().Kind == TokenKind.DoubleColon)
            {
                NextToken();
                if (!ExpectToken(TokenKind.Ident, out var ident)) return null;
                lastLocation = ident.Location;
                path.Add((string)ident.Value!);
            }
            if (Peek().Kind == TokenKind.Dot)
            {
                NextToken();
                if (!ExpectToken(TokenKind.Ident, out var ident)) return null;
                var location = Location.Combine(firstIdent.Location, ident.Location);
                return new QualifiedIdentExpression(new(path), (string)ident.Value!, location);
            }
            else
            {
                var location = lastLocation != default
                    ? Location.Combine(firstIdent.Location, lastLocation)
                    : firstIdent.Location;
                
                return new QualifiedIdentExpression(new(path), null, location);
            }
        }
        else if (peek.Kind == TokenKind.Dot)
        {
            NextToken();
            if (!ExpectToken(TokenKind.Ident, out var ident)) return null;
            var location = Location.Combine(firstIdent.Location, ident.Location);
            return new QualifiedIdentExpression(new([(string)firstIdent.Value!]), (string)ident.Value!, location);
        }
        else
        {
            return new QualifiedIdentExpression(new([]), (string)firstIdent.Value!, firstIdent.Location);
        }
    }

    public IExpression? ParseExpression()
    {
        return ParseBinopExpression(0);
    }

    private IExpression? ParseBinopExpression(int precedence)
    {
        if (precedence > BinopFacts.MaxPrecedence)
        {
            return ParseUnaryExpression();
        }

        var left = ParseBinopExpression(precedence + 1);
        if (left == null)
        {
            return null;
        }
        while (BinopFacts.TryGetBinop(Peek().Kind, out var kind))
        {
            if (BinopFacts.GetPrecedence(kind) != precedence)
            {
                break;
            }
            NextToken();
            var right = ParseBinopExpression(precedence + 1);
            if (right == null)
            {
                return null;
            }
            var location = Location.Combine(left.Location, right.Location);
            left = new BinopExpression(kind, left, right, location);
        }
        return left;
    }

    private IExpression? ParseUnaryExpression()
    {
        if (Peek().Kind == TokenKind.Not)
        {
            var notToken = NextToken();
            var expr = ParseUnaryExpression();
            if (expr == null)
            {
                return null;
            }
            var location = Location.Combine(notToken.Location, expr.Location);
            return new UnaryExpression(UnaryKind.Not, expr, location);
        }
        return ParsePrimaryExpression();
    }

    private IExpression? ParsePrimaryExpression()
    {
        var t = NextToken();
        switch (t.Kind)
        {
            case TokenKind.Ident:
                return new IdentExpression((string)t.Value!, t.Location);

            case TokenKind.String:
                return new StringExpression((string)t.Value!, t.Location);

            case TokenKind.Int:
                return new IntExpression((int)t.Value!, t.Location);

            case TokenKind.True:
                return new BoolExpression(true, t.Location);

            case TokenKind.False:
                return new BoolExpression(false, t.Location);

            case TokenKind.OParen:
            {
                var expr = ParseExpression();
                if (expr == null)
                {
                    return null;
                }
                if (!ExpectToken(TokenKind.CParen, out var cparenToken))
                {
                    return null;
                }
                var location = Location.Combine(t.Location, cparenToken.Location);
                return new ParenthesisedExpression(expr, location);
            }

            default:
                _diagnostics.AddError("Expected expression", t.Location);
                return null;
        }
    }

    private bool ExpectToken(TokenKind kind, out Token t)
    {
        t = NextToken();
        if (t.Kind != kind)
        {
            _diagnostics.AddError($"Unexpected token '{t.Kind}'", t.Location);
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