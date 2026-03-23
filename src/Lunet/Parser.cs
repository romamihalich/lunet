namespace Lunet;

public record Ast(IReadOnlyList<ITopLevelStatement> Statements);

public interface ITopLevelStatement;
public record ImportStatement(IReadOnlyList<string> Path, Location Location) : ITopLevelStatement;
public record FunctionStatement(string Name, List<FunctionParameter> Parameters, IReadOnlyList<IStatement> Body, TypeNameExpression? ReturnType) : ITopLevelStatement;

public record struct FunctionParameter(string Name, TypeNameExpression Type);

public interface IStatement;
public record ExpressionStatement(IExpression Expression) : IStatement;
public record VariableDefinitionStatement(string Name, TypeNameExpression Type, IExpression Rvalue) : IStatement;
public record AssignmentStatement(QualifiedNameExpression Name, IExpression Rvalue) : IStatement;
public record IndexAssignmentStatement(IndexAccessExpression IndexAccessExpression, IExpression Rvalue) : IStatement;
public record IfStatement(IExpression Condition, IReadOnlyList<IStatement> Block, IReadOnlyList<IStatement>? ElseBlock) : IStatement;
public record WhileStatement(IExpression Condition, IReadOnlyList<IStatement> Block) : IStatement;
public record ReturnStatement(IExpression Expression) : IStatement;

public interface IExpression
{
    public Location Location { get; }
}
public record ParenthesisedExpression(IExpression Expression, Location Location) : IExpression;
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
    Mul, Div, Mod, Add, Sub, Concat,
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
        BinopKind.Mod            => MaxPrecedence,
        BinopKind.Add            => MaxPrecedence - 1,
        BinopKind.Sub            => MaxPrecedence - 1,
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
        BinopKind.Mod            => "%",
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

            case TokenKind.Percent:
                kind = BinopKind.Mod;
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

public record FunctionCallExpression(IExpression Expression, IReadOnlyList<IExpression> Args, Location Location) : IExpression;

public record QualifiedNameExpression(IReadOnlyList<string> Path, string? Ident, Location Location) : IExpression
{
    public override string ToString()
    {
        var s = string.Join("::", Path);
        if (!string.IsNullOrEmpty(s) && Ident != null)
        {
            s += ".";
        }
        s += Ident;
        return  s;
    }
}

public record CastExpression(IExpression Expression, TypeNameExpression Type, Location Location) : IExpression;

public record ArrayExpression(IReadOnlyList<IExpression> Elements, Location Location) : IExpression;

public record IndexAccessExpression(IExpression Array, IExpression IndexExpression, Location Location) : IExpression;

public record TypeNameExpression(QualifiedNameExpression Name, bool IsArray, Location Location) : IExpression
{
    public override string? ToString()
    {
        var qname = base.ToString();
        if (IsArray)
        {
            qname += "[]";
        }
        return qname;
    }
}

public class Parser
{
    private const int LookheadSize = 3;

    private readonly Lexer _lexer;
    private readonly Diagnostics _diagnostics;

    private readonly Token[] _lookahead;
    private int _lookheadIndex;

    public Parser(Lexer lexer, Diagnostics diagnostics)
    {
        _lexer = lexer;
        _diagnostics = diagnostics;
        _lookahead = new Token[LookheadSize];
        for (int i = 0; i < LookheadSize; i++)
        {
            _lookahead[i] = lexer.Lex();
        }
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

        var parameters = ParseFunctionParameters();

        if (parameters == null)
        {
            return null;
        }

        TypeNameExpression? returnType = null;
        if (Peek().Kind == TokenKind.Arrow)
        {
            NextToken();
            returnType = ParseTypeNameExpression();
            if (returnType == null)
            {
                return null;
            }
        }

        var body = ParseBlock();

        if (!ExpectToken(TokenKind.End, out _))
        {
            return null;
        }

        return new FunctionStatement(name, parameters, body, returnType);
    }

    private List<FunctionParameter>? ParseFunctionParameters()
    {
        if (!ExpectToken(TokenKind.OpenRoundBracket, out _))
        {
            return null;
        }

        var parameters = new List<FunctionParameter>();
        while (Peek().Kind != TokenKind.CloseRoundBracket)
        {
            if (!ExpectToken(TokenKind.Ident, out var nameToken))
            {
                return null;
            }

            var name = (string)nameToken.Value!;

            if (!ExpectToken(TokenKind.Colon))
            {
                return null;
            }

            var type = ParseTypeNameExpression();

            if (type == null)
            {
                return null;
            }

            parameters.Add(new(name, type));

            if (Peek().Kind != TokenKind.Comma)
            {
                break;
            }

            // skip ','
            NextToken();
        }

        if (!ExpectToken(TokenKind.CloseRoundBracket, out _))
        {
            return null;
        }

        return parameters;
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

        return new([(string)ident.Value!], location);
    }

    private IStatement? ParseStatement()
    {
        switch (Peek().Kind)
        {
            case TokenKind.Local:
            {
                return ParseVariableDefinitionStatement();
            }
            case TokenKind.If:
            {
                return ParseIfStatement();
            }
            case TokenKind.While:
            {
                return ParseWhileStatement();
            }
            case TokenKind.Return:
            {
                return ParseReturnStatement();
            }
            default:
            {
                var expr = ParseExpression();
                if (expr == null)
                {
                    return null;
                }
                if (expr is QualifiedNameExpression qnameExpr && Peek().Kind == TokenKind.Equals)
                {
                    NextToken();
                    var rvalue = ParseExpression();
                    if (rvalue == null)
                    {
                        return null;
                    }
                    return new AssignmentStatement(qnameExpr, rvalue);
                }
                else if (expr is IndexAccessExpression indexAccessExpression && Peek().Kind == TokenKind.Equals)
                {
                    NextToken();
                    var rvalue = ParseExpression();
                    if (rvalue == null)
                    {
                        return null;
                    }
                    return new IndexAssignmentStatement(indexAccessExpression, rvalue);
                }
                return new ExpressionStatement(expr);
            }
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

        var type = ParseTypeNameExpression();

        if (type == null)
        {
            return null;
        }

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

    private IfStatement? ParseIfStatement()
    {
        var elseIf = false;
        if (Peek().Kind == TokenKind.ElseIf)
        {
            elseIf = true;
            NextToken();
        }
        else if (!ExpectToken(TokenKind.If))
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
        if (Peek().Kind == TokenKind.ElseIf)
        {
            var elseIfStatement = ParseIfStatement();
            if (elseIfStatement == null)
            {
                return null;
            }
            elseBlock = [elseIfStatement];
        }
        else if (Peek().Kind == TokenKind.Else)
        {
            NextToken();
            elseBlock = ParseBlock();
        }

        if (!elseIf && !ExpectToken(TokenKind.End, out _))
        {
            return null;
        }

        return new IfStatement(cond, block, elseBlock);
    }

    private WhileStatement? ParseWhileStatement()
    {
        if (!ExpectToken(TokenKind.While))
        {
            return null;
        }

        var cond = ParseExpression();
        if (cond == null)
        {
            return null;
        }

        if (!ExpectToken(TokenKind.Do))
        {
            return null;
        }

        var block = ParseBlock();

        if (!ExpectToken(TokenKind.End))
        {
            return null;
        }

        return new WhileStatement(cond, block);
    }

    private ReturnStatement? ParseReturnStatement()
    {
        if (!ExpectToken(TokenKind.Return))
        {
            return null;
        }

        var expr = ParseExpression();

        if (expr == null)
        {
            return null;
        }

        return new ReturnStatement(expr);
    }

    private IReadOnlyList<IStatement> ParseBlock()
    {
        var block = new List<IStatement>();

        while (Peek().Kind != TokenKind.End
               && Peek().Kind != TokenKind.Else
               && Peek().Kind != TokenKind.ElseIf
               && ParseStatement() is { } statement)
        {
            block.Add(statement);
        }

        return block;
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
        return ParseSuffixExpression();
    }

    private IExpression? ParseSuffixExpression()
    {
        var expr = ParsePrimaryExpression();
        if (expr == null)
        {
            return null;
        }
        if (Peek().Kind == TokenKind.OpenRoundBracket)
        {
            var (args, argsLocation) = ParseArgs();
            if (args == null)
            {
                return null;
            }
            var location = Location.Combine(expr.Location, argsLocation);
            return new FunctionCallExpression(expr, args, location);
        }
        else if (Peek().Kind == TokenKind.As)
        {
            NextToken();
            var type = ParseTypeNameExpression();
            if (type == null)
            {
                return null;
            }
            var location = Location.Combine(expr.Location, type.Location);
            return new CastExpression(expr, type, location);
        }
        else if (Peek().Kind == TokenKind.OpenSquareBracket)
        {
            NextToken();
            var indexExpr = ParseExpression();
            if (indexExpr == null)
            {
                return null;
            }
            if (!ExpectToken(TokenKind.CloseSquareBracket, out var closeBracketToken))
            {
                return null;
            }
            var location = Location.Combine(expr.Location, closeBracketToken.Location);
            return new IndexAccessExpression(expr, indexExpr, location);
        }
        return expr;
    }

    private IExpression? ParsePrimaryExpression()
    {
        if (Peek().Kind == TokenKind.Ident)
        {
            return ParseQualifiedNameExpression();
        }
        else if (Peek().Kind == TokenKind.OpenCurlyBracket)
        {
            return ParseArrayExpression();
        }
        var t = NextToken();
        switch (t.Kind)
        {
            case TokenKind.String:
                return new StringExpression((string)t.Value!, t.Location);

            case TokenKind.Int:
                return new IntExpression((int)t.Value!, t.Location);

            case TokenKind.True:
                return new BoolExpression(true, t.Location);

            case TokenKind.False:
                return new BoolExpression(false, t.Location);

            case TokenKind.OpenRoundBracket:
            {
                var expr = ParseExpression();
                if (expr == null)
                {
                    return null;
                }
                if (!ExpectToken(TokenKind.CloseRoundBracket, out var cparenToken))
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

    private (IReadOnlyList<IExpression>?, Location) ParseArgs()
    {
        if (!ExpectToken(TokenKind.OpenRoundBracket, out var oparenToken))
        {
            return (null, default);
        }

        var args = new List<IExpression>();
        while (Peek().Kind != TokenKind.CloseRoundBracket)
        {
            var expr = ParseExpression();

            if (expr == null)
            {
                return (null, default);
            }

            args.Add(expr);

            if (Peek().Kind != TokenKind.Comma)
            {
                break;
            }

            // skip ','
            NextToken();
        }

        if (!ExpectToken(TokenKind.CloseRoundBracket, out var cparenToken))
        {
            return (null, default);
        }

        var location = Location.Combine(oparenToken.Location, cparenToken.Location);

        return (args, location);
    }

    private ArrayExpression? ParseArrayExpression()
    {
        if (!ExpectToken(TokenKind.OpenCurlyBracket, out var openBracketToken))
        {
            return null;
        }

        var elements = new List<IExpression>();
        while (Peek().Kind != TokenKind.CloseCurlyBracket)
        {
            var expr = ParseExpression();

            if (expr == null)
            {
                return null;
            }

            elements.Add(expr);

            if (Peek().Kind != TokenKind.Comma)
            {
                break;
            }

            // skip ','
            NextToken();
        }

        if (!ExpectToken(TokenKind.CloseCurlyBracket, out var closeBracketToken))
        {
            return null;
        }

        var location = Location.Combine(openBracketToken.Location, closeBracketToken.Location);

        return new ArrayExpression(elements, location);
    }

    private TypeNameExpression? ParseTypeNameExpression()
    {
        var qname = ParseQualifiedNameExpression();
        if (qname == null)
        {
            return null;
        }
        var location = qname.Location;
        var isArray = false;
        if (Peek().Kind == TokenKind.OpenSquareBracket)
        {
            NextToken();
            if (!ExpectToken(TokenKind.CloseSquareBracket, out var closeBracketToken))
            {
                return null;
            }
            isArray = true;
            location = Location.Combine(location, closeBracketToken.Location);
        }
        return new TypeNameExpression(qname, isArray, location);
    }

    private QualifiedNameExpression? ParseQualifiedNameExpression()
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
                return new QualifiedNameExpression(path, (string)ident.Value!, location);
            }
            else
            {
                var location = lastLocation != default
                    ? Location.Combine(firstIdent.Location, lastLocation)
                    : firstIdent.Location;
                
                return new QualifiedNameExpression(path, null, location);
            }
        }
        else if (peek.Kind == TokenKind.Dot)
        {
            NextToken();
            if (!ExpectToken(TokenKind.Ident, out var ident)) return null;
            var location = Location.Combine(firstIdent.Location, ident.Location);
            return new QualifiedNameExpression([(string)firstIdent.Value!], (string)ident.Value!, location);
        }
        else
        {
            return new QualifiedNameExpression([], (string)firstIdent.Value!, firstIdent.Location);
        }
    }

    private bool ExpectToken(TokenKind kind, out Token t)
    {
        t = NextToken();
        if (t.Kind != kind)
        {
            _diagnostics.AddError($"Unexpected token '{t.Kind}', expected {kind}", t.Location);
            return false;
        }

        return true;
    }

    private bool ExpectToken(TokenKind kind)
    {
        return ExpectToken(kind, out var _);
    }

    private Token Peek(int offset = 0)
    {
        if (offset < 0 || offset >= LookheadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset was outside the statically defined limit");
        }
        return _lookahead[(_lookheadIndex + offset) % LookheadSize];
    }

    private Token NextToken()
    {
        var t = _lookahead[_lookheadIndex];
        _lookahead[_lookheadIndex] = _lexer.Lex();
        _lookheadIndex = (_lookheadIndex + 1) % LookheadSize;
        return t;
    }
}