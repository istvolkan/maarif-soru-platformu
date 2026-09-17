using System.Globalization;

namespace MaarifPlatform.Application.Visuals;

/// <summary>LLM'in ürettiği "2*x-3" gibi matematiksel ifadeleri GÜVENLİ şekilde değerlendirir —
/// üçüncü parti bir "eval" kütüphanesi KULLANILMAZ (LLM çıktısını değerlendirmek arbitrary-code-
/// execution riski taşır); elle yazılmış, yalnızca aşağıdaki whitelist'i tanıyan bir recursive-
/// descent parser bunu yapısal olarak imkansız kılar. Sözdizimi hatalarında (bilinmeyen
/// karakter/fonksiyon, eşleşmeyen parantez) <see cref="FormatException"/> fırlatır — bu,
/// GenerateBatchAsync'in regenerate mantığına düşer. Matematiksel tanımsızlıklar (sqrt(-1),
/// 1/0, tan(pi/2) vb.) hata SAYILMAZ — birçok gerçek fonksiyon bazı x değerlerinde
/// tanımsızdır; bu durumlarda NaN/Infinity döner, VisualSpecRenderer bu noktaları çizimden
/// atlar (sürekliliği bozmadan matematiksel olarak doğru kalır).</summary>
public static class MathExpressionEvaluator
{
    private static readonly HashSet<string> KnownFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sin", "cos", "tan", "sqrt", "abs", "log", "ln", "exp"
    };

    /// <summary>İfadeyi bir kez ayrıştırır (sözdizimi hatası burada fırlatılır), her x için
    /// hızlıca yeniden değerlendirilebilen bir delegate döner.</summary>
    public static Func<double, double> Compile(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormatException("Boş ifade.");
        }

        var parser = new Parser(expression);
        var node = parser.ParseExpression();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            throw new FormatException($"İfade beklenmeyen karakterle bitiyor (konum {parser.Position}): '{expression}'.");
        }

        return node;
    }

    public static double Evaluate(string expression, double x) => Compile(expression)(x);

    /// <summary>Grammar (standart matematik önceliği, üs sağdan-birleşimli, birli işaret
    /// üsten GEVŞEK bağlanır — "-x^2" = -(x^2), "x^-1" = 1/x):
    /// expr := term (('+'|'-') term)*
    /// term := signedFactor (('*'|'/') signedFactor)*
    /// signedFactor := ('-'|'+')? power
    /// power := primary ('^' signedFactor)?
    /// primary := NUMBER | 'x' | 'pi' | 'e' | FUNC '(' expr ')' | '(' expr ')'</summary>
    private sealed class Parser(string text)
    {
        public int Position { get; private set; }
        public bool AtEnd => Position >= text.Length;
        private char Current => AtEnd ? '\0' : text[Position];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(text[Position]))
            {
                Position++;
            }
        }

        private bool TryConsume(char c)
        {
            SkipWhitespace();
            if (Current == c)
            {
                Position++;
                return true;
            }
            return false;
        }

        private void Expect(char c)
        {
            if (!TryConsume(c))
            {
                throw new FormatException($"'{c}' bekleniyordu (konum {Position}): '{text}'.");
            }
        }

        public Func<double, double> ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                if (TryConsume('+'))
                {
                    var right = ParseTerm();
                    var prev = left;
                    left = x => prev(x) + right(x);
                }
                else if (TryConsume('-'))
                {
                    var right = ParseTerm();
                    var prev = left;
                    left = x => prev(x) - right(x);
                }
                else
                {
                    break;
                }
            }
            return left;
        }

        private Func<double, double> ParseTerm()
        {
            var left = ParseSignedFactor();
            while (true)
            {
                if (TryConsume('*'))
                {
                    var right = ParseSignedFactor();
                    var prev = left;
                    left = x => prev(x) * right(x);
                }
                else if (TryConsume('/'))
                {
                    var right = ParseSignedFactor();
                    var prev = left;
                    left = x => prev(x) / right(x); // 0'a bölme -> Infinity/NaN, hata değil
                }
                else
                {
                    break;
                }
            }
            return left;
        }

        private Func<double, double> ParseSignedFactor()
        {
            if (TryConsume('-'))
            {
                var inner = ParsePower();
                return x => -inner(x);
            }
            if (TryConsume('+'))
            {
                return ParsePower();
            }
            return ParsePower();
        }

        private Func<double, double> ParsePower()
        {
            var baseExpr = ParsePrimary();
            if (TryConsume('^'))
            {
                var exponent = ParseSignedFactor();
                var prevBase = baseExpr;
                return x => Math.Pow(prevBase(x), exponent(x));
            }
            return baseExpr;
        }

        private Func<double, double> ParsePrimary()
        {
            SkipWhitespace();
            if (TryConsume('('))
            {
                var inner = ParseExpression();
                Expect(')');
                return inner;
            }

            if (!AtEnd && (char.IsDigit(Current) || Current == '.'))
            {
                return ParseNumber();
            }

            if (!AtEnd && (char.IsLetter(Current) || Current == '_'))
            {
                return ParseIdentifierOrFunction();
            }

            throw new FormatException($"Beklenmeyen karakter '{Current}' (konum {Position}): '{text}'.");
        }

        private Func<double, double> ParseNumber()
        {
            var start = Position;
            while (!AtEnd && (char.IsDigit(Current) || Current == '.'))
            {
                Position++;
            }
            var token = text[start..Position];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"Geçersiz sayı '{token}' (konum {start}).");
            }
            return _ => value;
        }

        private Func<double, double> ParseIdentifierOrFunction()
        {
            var start = Position;
            while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
            {
                Position++;
            }
            var name = text[start..Position];

            SkipWhitespace();
            if (Current == '(')
            {
                if (!KnownFunctions.Contains(name))
                {
                    throw new FormatException($"Bilinmeyen fonksiyon '{name}' (konum {start}).");
                }
                Position++; // '('
                var arg = ParseExpression();
                Expect(')');
                return BuildFunctionCall(name, arg);
            }

            return name.ToLowerInvariant() switch
            {
                "x" => x => x,
                "pi" => _ => Math.PI,
                "e" => _ => Math.E,
                _ => throw new FormatException($"Bilinmeyen tanımlayıcı '{name}' (konum {start}).")
            };
        }

        private static Func<double, double> BuildFunctionCall(string name, Func<double, double> arg) =>
            name.ToLowerInvariant() switch
            {
                "sin" => x => Math.Sin(arg(x)),
                "cos" => x => Math.Cos(arg(x)),
                "tan" => x => Math.Tan(arg(x)),
                "sqrt" => x => Math.Sqrt(arg(x)),
                "abs" => x => Math.Abs(arg(x)),
                "log" => x => Math.Log10(arg(x)),
                "ln" => x => Math.Log(arg(x)),
                "exp" => x => Math.Exp(arg(x)),
                _ => throw new FormatException($"Bilinmeyen fonksiyon '{name}'.")
            };
    }
}
