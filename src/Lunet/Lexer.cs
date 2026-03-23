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
    As,

    OpenRoundBracket,
    CloseRoundBracket,
    OpenCurlyBracket,
    CloseCurlyBracket,
    OpenSquareBracket,
    CloseSquareBracket,

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

            '('                     => Read(TokenKind.OpenRoundBracket, 0),
            ')'                     => Read(TokenKind.CloseRoundBracket, 0),
            '{'                     => Read(TokenKind.OpenCurlyBracket, 0),
            '}'                     => Read(TokenKind.CloseCurlyBracket, 0),
            '['                     => Read(TokenKind.OpenSquareBracket, 0),
            ']'                     => Read(TokenKind.CloseSquareBracket, 0),
            ':' when Peek() == ':'  => Read(TokenKind.DoubleColon, 1),
            ':'                     => Read(TokenKind.Colon, 0),
            '-' when Peek() == '>'  => Read(TokenKind.Arrow, 1),
            ','                     => Read(TokenKind.Comma, 0),
            '.' when Peek() == '.'  => Read(TokenKind.DoubleDot, 1),
            '.'                     => Read(TokenKind.Dot, 0),
            '~' when Peek() == '='  => Read(TokenKind.NotEquals, 1),
            '=' when Peek() == '='  => Read(TokenKind.DoubleEquals, 1),
            '='                     => Read(TokenKind.Equals, 0),
            '*'                     => Read(TokenKind.Asterisk, 0),
            '/'                     => Read(TokenKind.Slash, 0),
            '%'                     => Read(TokenKind.Percent, 0),
            '+'                     => Read(TokenKind.Plus, 0),
            '-'                     => Read(TokenKind.Minus, 0),
            '>' when Peek() == '='  => Read(TokenKind.GreaterOrEqual, 1),
            '>'                     => Read(TokenKind.Greater, 0),
            '<' when Peek() == '='  => Read(TokenKind.LessOrEqual, 1),
            '<'                     => Read(TokenKind.Less, 0),
            '\0'                    => Read(TokenKind.Eof, 0),
            _                       => Read(TokenKind.Illegal, 0),
        };
    }

    private Token Read(TokenKind kind, int skipCount)
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
            "as"       => kind = TokenKind.As,
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