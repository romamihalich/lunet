using System.Text;

namespace Lunet;

public record struct Token(TokenKind Kind, object? Value);

public enum TokenKind
{
    Illegal,
    Eof,

    Ident,
    String,

    // keywords
    Import,
    Function,
    End,

    // symbols
    OParen,
    CParen,
    Colon,
    DoubleColon,
    Dot,
}

public class Lexer
{
    private readonly string _sourceCode;

    private int _position;

    public Lexer(string sourceCode)
    {
        _sourceCode = sourceCode;
    }

    public Token Lex()
    {
        SkipWhiteSpace();

        var ch = NextChar();

        return ch switch
        {
            '\0'                    => new(TokenKind.Eof, null),
            _ when IsIdentStart(ch) => ReadIdentOrKeyword(ch),
            '"'                     => ReadString(),

            '('                     => new(TokenKind.OParen, null),
            ')'                     => new(TokenKind.CParen, null),
            ':' when Peek() == ':'  => Eat(TokenKind.DoubleColon, 1),
            ':'                     => new(TokenKind.Colon, null),
            '.'                     => new(TokenKind.Dot, null),
            _                       => new(TokenKind.Illegal, null),
        };
    }

    private Token Eat(TokenKind kind, int skipCount)
    {
        for (int i = 0; i < skipCount; i++)
        {
            NextChar();
        }
        return new(kind, null);
    }

    private Token ReadIdentOrKeyword(char firstCh)
    {
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

        if (LookupKeyword(ident, out var keywordKind))
        {
            return new(keywordKind, null);
        }
        else
        {
            return new(TokenKind.Ident, ident);
        }
    }

    private Token ReadString()
    {
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

        if (ch == '"')
        {
            var text = sb.ToString();
            return new(TokenKind.String, text);
        }
        else
        {
            // TODO: add to diagnostics error message
            return new(TokenKind.Illegal, null);
        }
    }

    private static bool LookupKeyword(string ident, out TokenKind kind)
    {
        _ = ident switch
        {
            "import"   => kind = TokenKind.Import,
            "function" => kind = TokenKind.Function,
            "end"      => kind = TokenKind.End,
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

    private void SkipWhiteSpace()
    {
        while (_position < _sourceCode.Length
               && IsWhiteSpace(_sourceCode[_position]))
        {
            _position++;
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
        _position++;
        return ch;
    }
}
