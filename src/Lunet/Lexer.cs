using System.Text;

namespace Lunet;

public record struct Token(TokenKind Kind, object? Value, Location Location);

public enum TokenKind
{
    Illegal,
    Eof,

    Ident,
    String,
    Int,
    True,
    False,

    // keywords
    Import,
    Function,
    End,
    Local,
    And,
    Or,
    Not,
    If,
    Then,
    Else,
    ElseIf,
    While,
    Do,
    Return,

    // symbols
    OParen,
    CParen,
    Colon,
    DoubleColon,
    Arrow,
    Comma,
    Dot,
    DoubleDot,
    Equals,
    DoubleEquals,
    NotEquals,
    Asterisk,
    Slash,
    Percent,
    Plus,
    Minus,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}

public class Lexer
{
    private readonly string _sourceCode;
    private readonly Diagnostics _diagnostics;

    private int _position;
    private int _row = 1;
    private int _col = 0;

    public Lexer(string sourceCode, Diagnostics diagnostics)
    {
        _sourceCode = sourceCode;
        _diagnostics = diagnostics;
    }

    public Token Lex()
    {
        SkipWhiteSpace();

        var ch = NextChar();

        while (ch == '-' && Peek() == '-')
        {
            // skip comment
            while (true)
            {
                var curr = NextChar();
                if (curr == '\n' || curr == '\0')
                {
                    break;
                }
            }
            SkipWhiteSpace();
            ch = NextChar();
        }

        return ch switch
        {
            >= '0' and <= '9'       => ReadNumber(ch),
            _ when IsIdentStart(ch) => ReadIdentOrKeyword(ch),
            '"'                     => ReadString(),

            '('                     => Eat(TokenKind.OParen, 0),
            ')'                     => Eat(TokenKind.CParen, 0),
            ':' when Peek() == ':'  => Eat(TokenKind.DoubleColon, 1),
            ':'                     => Eat(TokenKind.Colon, 0),
            '-' when Peek() == '>'  => Eat(TokenKind.Arrow, 1),
            ','                     => Eat(TokenKind.Comma, 0),
            '.' when Peek() == '.'  => Eat(TokenKind.DoubleDot, 1),
            '.'                     => Eat(TokenKind.Dot, 0),
            '~' when Peek() == '='  => Eat(TokenKind.NotEquals, 1),
            '=' when Peek() == '='  => Eat(TokenKind.DoubleEquals, 1),
            '='                     => Eat(TokenKind.Equals, 0),
            '*'                     => Eat(TokenKind.Asterisk, 0),
            '/'                     => Eat(TokenKind.Slash, 0),
            '%'                     => Eat(TokenKind.Percent, 0),
            '+'                     => Eat(TokenKind.Plus, 0),
            '-'                     => Eat(TokenKind.Minus, 0),
            '>' when Peek() == '='  => Eat(TokenKind.GreaterOrEqual, 1),
            '>'                     => Eat(TokenKind.Greater, 0),
            '<' when Peek() == '='  => Eat(TokenKind.LessOrEqual, 1),
            '<'                     => Eat(TokenKind.Less, 0),
            '\0'                    => Eat(TokenKind.Eof, 0),
            _                       => Eat(TokenKind.Illegal, 0),
        };
    }

    private Token Eat(TokenKind kind, int skipCount)
    {
        var startLoc = GetCurrentLocation();
        for (int i = 0; i < skipCount; i++)
        {
            NextChar();
        }
        var endLoc = GetCurrentLocation();
        var loc = Location.Combine(startLoc, endLoc);
        if (kind == TokenKind.Illegal)
        {
            _diagnostics.AddError("Illegal token", loc);
        }
        return new(kind, null, loc);
    }

    private Token ReadIdentOrKeyword(char firstCh)
    {
        var startLoc = GetCurrentLocation();
        var sb = new StringBuilder();
        sb.Append(firstCh);
        while (true)
        {
            var ch = Peek();
            if (ch == '\0' || !IsIdentRest(ch))
            {
                break;
            }
            sb.Append(ch);
            NextChar();
        }
        var ident = sb.ToString();
        var endLoc = GetCurrentLocation();
        var loc = Location.Combine(startLoc, endLoc);
        if (LookupKeyword(ident, out var keywordKind))
        {
            return new(keywordKind, null, loc);
        }
        else
        {
            return new(TokenKind.Ident, ident, loc);
        }
    }

    private Token ReadString()
    {
        var startLoc = GetCurrentLocation();
        var sb = new StringBuilder();
        char ch;
        while (true)
        {
            ch = NextChar();
            if (ch == '\0' || ch == '"')
            {
                break;
            }
            // TODO: think about escaping (\r,\n,\t and so on)
            sb.Append(ch);
        }
        var endLoc = GetCurrentLocation();
        var loc = Location.Combine(startLoc, endLoc);
        if (ch == '"')
        {
            var text = sb.ToString();
            return new(TokenKind.String, text, loc);
        }
        else
        {
            _diagnostics.AddError("Unclosed string literal", loc);
            return new(TokenKind.Illegal, null, loc);
        }
    }

    private Token ReadNumber(char firstCh)
    {
        var startLoc = GetCurrentLocation();
        var sb = new StringBuilder();
        sb.Append(firstCh);
        while (char.IsAsciiDigit(Peek()))
        {
            sb.Append(NextChar());
        }
        var endLoc = GetCurrentLocation();
        var loc = Location.Combine(startLoc, endLoc);
        // TODO: what if number is too big?
        var n = int.Parse(sb.ToString());
        return new(TokenKind.Int, n, loc);
    }

    private static bool LookupKeyword(string ident, out TokenKind kind)
    {
        _ = ident switch
        {
            "import"   => kind = TokenKind.Import,
            "function" => kind = TokenKind.Function,
            "end"      => kind = TokenKind.End,
            "local"    => kind = TokenKind.Local,
            "true"     => kind = TokenKind.True,
            "false"    => kind = TokenKind.False,
            "and"      => kind = TokenKind.And,
            "or"       => kind = TokenKind.Or,
            "not"      => kind = TokenKind.Not,
            "if"       => kind = TokenKind.If,
            "then"     => kind = TokenKind.Then,
            "else"     => kind = TokenKind.Else,
            "elseif"   => kind = TokenKind.ElseIf,
            "while"    => kind = TokenKind.While,
            "do"       => kind = TokenKind.Do,
            "return"   => kind = TokenKind.Return,
            _          => kind = default,
        };

        return kind != default;
    }

    private static bool IsWhiteSpace(char ch)
    {
        // NOTE: maybe explicitly state what is white space in this lang,
        // for quick and easy solution builtin dotnet method for now
        return char.IsWhiteSpace(ch);
    }

    private static bool IsIdentStart(char ch)
    {
        return char.IsLetter(ch) || ch == '_';
    }

    private static bool IsIdentRest(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }

    private Location GetCurrentLocation()
    {
        return new Location(
            row: _row,
            col: _col
        );
    }

    private void SkipWhiteSpace()
    {
        while (_position < _sourceCode.Length
               && IsWhiteSpace(_sourceCode[_position]))
        {
            NextChar();
        }
    }

    private char Peek()
    {
        if (_position >= _sourceCode.Length)
        {
            return '\0';
        }

        return _sourceCode[_position];
    }

    private char NextChar()
    {
        if (_position >= _sourceCode.Length)
        {
            return '\0';
        }

        var ch = _sourceCode[_position];
        if (ch == '\n')
        {
            _row++;
            _col = 0;
        }
        else
        {
            _col++;
        }
        _position++;
        return ch;
    }
}