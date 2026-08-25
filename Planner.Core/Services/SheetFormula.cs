using System.Globalization;
using System.Text.RegularExpressions;

namespace Planner.Core.Services;

public static class SheetFormula
{
    private static readonly Regex CellRx = new(@"^\$?([A-Za-z]+)\$?(\d+)$", RegexOptions.CultureInvariant);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static List<List<string>> EvaluateGrid(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var cols = headers.Count;
        var height = rows.Count;
        var cache = new Dictionary<(int r, int c), string>();
        var visiting = new HashSet<(int r, int c)>();
        var result = new List<List<string>>(height);
        for (var r = 0; r < height; r++)
        {
            var line = new List<string>(cols);
            for (var c = 0; c < cols; c++)
            {
                line.Add(EvalCell(r, c, headers, rows, cache, visiting));
            }

            result.Add(line);
        }

        return result;
    }

    public static string EvaluateCell(
        string raw,
        int row,
        int col,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
        => EvalCell(row, col, headers, rows, new Dictionary<(int, int), string>(), []);

    private static string EvalCell(
        int row,
        int col,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        Dictionary<(int r, int c), string> cache,
        HashSet<(int r, int c)> visiting)
    {
        var key = (row, col);
        if (cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var raw = GetRaw(row, col, rows);
        if (string.IsNullOrEmpty(raw) || raw[0] != '=')
        {
            cache[key] = raw;
            return raw;
        }

        if (!visiting.Add(key))
        {
            return "#DÖNGÜ";
        }

        string text;
        try
        {
            var val = new Parser(raw[1..], headers, rows, cache, visiting).Parse();
            text = val.Display();
        }
        catch
        {
            text = "#HATA";
        }

        visiting.Remove(key);
        cache[key] = text;
        return text;
    }

    private static string GetRaw(int row, int col, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (row < 0 || row >= rows.Count || col < 0)
        {
            return "";
        }

        var line = rows[row];
        return col < line.Count ? line[col] ?? "" : "";
    }

    public static bool TryParseCell(string token, out int col, out int row)
    {
        col = 0;
        row = 0;
        var m = CellRx.Match(token.Trim());
        if (!m.Success)
        {
            return false;
        }

        col = ColumnIndex(m.Groups[1].Value);
        return col >= 0 && int.TryParse(m.Groups[2].Value, out row) && row >= 1;
    }

    public static int ColumnIndex(string name)
    {
        var n = 0;
        foreach (var ch in name.ToUpperInvariant())
        {
            if (ch is < 'A' or > 'Z')
            {
                return -1;
            }

            n = n * 26 + (ch - 'A' + 1);
        }

        return n - 1;
    }

    private sealed class Val
    {
        public double? Number;
        public string? Text;
        public List<double>? List;
        public string? Error;
        public int? CountAll;

        public static Val Num(double n) => new() { Number = n };
        public static Val Str(string t) => new() { Text = t };
        public static Val Err(string e) => new() { Error = e };
        public static Val Arr(List<double> a, int? countAll = null) => new() { List = a, CountAll = countAll };

        public string Display()
        {
            if (Error is not null) return Error;
            if (List is not null) return List.Count == 0 ? "0" : List.Sum().ToString(Inv);
            if (Number is { } n)
            {
                return Math.Abs(n - Math.Round(n)) < 1e-9 ? ((long)Math.Round(n)).ToString(Inv) : n.ToString("0.########", Inv);
            }

            return Text ?? "";
        }

        public double AsNumber()
        {
            if (Error is not null) throw new InvalidOperationException(Error);
            if (Number is { } n) return n;
            if (List is { Count: > 0 } l) return l.Sum();
            if (double.TryParse(Text, NumberStyles.Float, Inv, out var p)) return p;
            return 0;
        }

        public bool Truthy()
        {
            if (Error is not null) return false;
            if (Number is { } n) return Math.Abs(n) > 1e-12;
            return !string.IsNullOrWhiteSpace(Text);
        }
    }

    private sealed class Parser
    {
        private readonly string _src;
        private readonly IReadOnlyList<string> _headers;
        private readonly IReadOnlyList<IReadOnlyList<string>> _rows;
        private readonly Dictionary<(int, int), string> _cache;
        private readonly HashSet<(int, int)> _visiting;
        private int _i;

        public Parser(
            string src,
            IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<string>> rows,
            Dictionary<(int, int), string> cache,
            HashSet<(int, int)> visiting)
        {
            _src = src;
            _headers = headers;
            _rows = rows;
            _cache = cache;
            _visiting = visiting;
        }

        public Val Parse()
        {
            var v = ParseCmp();
            Skip();
            return _i < _src.Length ? Val.Err("#HATA") : v;
        }

        private Val ParseCmp()
        {
            var left = ParseAdd();
            Skip();
            if (_i >= _src.Length)
            {
                return left;
            }

            var op = PeekOp();
            if (op is not ("=" or "<>" or "<=" or ">=" or "<" or ">"))
            {
                return left;
            }

            _i += op.Length;
            var right = ParseAdd();
            if (left.Error is not null) return left;
            if (right.Error is not null) return right;
            var ln = left.Number ?? (double.TryParse(left.Text, NumberStyles.Float, Inv, out var a) ? a : double.NaN);
            var rn = right.Number ?? (double.TryParse(right.Text, NumberStyles.Float, Inv, out var b) ? b : double.NaN);
            bool ok;
            if (!double.IsNaN(ln) && !double.IsNaN(rn))
            {
                ok = op switch
                {
                    "=" => Math.Abs(ln - rn) < 1e-9,
                    "<>" => Math.Abs(ln - rn) >= 1e-9,
                    "<" => ln < rn,
                    ">" => ln > rn,
                    "<=" => ln <= rn,
                    ">=" => ln >= rn,
                    _ => false
                };
            }
            else
            {
                var cmp = string.Compare(left.Display(), right.Display(), StringComparison.CurrentCultureIgnoreCase);
                ok = op switch
                {
                    "=" => cmp == 0,
                    "<>" => cmp != 0,
                    "<" => cmp < 0,
                    ">" => cmp > 0,
                    "<=" => cmp <= 0,
                    ">=" => cmp >= 0,
                    _ => false
                };
            }

            return Val.Num(ok ? 1 : 0);
        }

        private Val ParseAdd()
        {
            var v = ParseMul();
            while (true)
            {
                Skip();
                if (Match('+'))
                {
                    v = Val.Num(v.AsNumber() + ParseMul().AsNumber());
                }
                else if (Match('-'))
                {
                    v = Val.Num(v.AsNumber() - ParseMul().AsNumber());
                }
                else
                {
                    return v;
                }
            }
        }

        private Val ParseMul()
        {
            var v = ParseUnary();
            while (true)
            {
                Skip();
                if (Match('*'))
                {
                    v = Val.Num(v.AsNumber() * ParseUnary().AsNumber());
                }
                else if (Match('/'))
                {
                    var d = ParseUnary().AsNumber();
                    v = Math.Abs(d) < 1e-12 ? Val.Err("#SAYI/0!") : Val.Num(v.AsNumber() / d);
                }
                else
                {
                    return v;
                }
            }
        }

        private Val ParseUnary()
        {
            Skip();
            if (Match('-'))
            {
                return Val.Num(-ParseUnary().AsNumber());
            }

            if (Match('+'))
            {
                return ParseUnary();
            }

            return ParsePrimary();
        }

        private Val ParsePrimary()
        {
            Skip();
            if (Match('('))
            {
                var inner = ParseCmp();
                Skip();
                Match(')');
                return inner;
            }

            if (_i < _src.Length && (char.IsDigit(_src[_i]) || _src[_i] == '.'))
            {
                var start = _i;
                while (_i < _src.Length && (char.IsDigit(_src[_i]) || _src[_i] == '.'))
                {
                    _i++;
                }

                return Val.Num(double.Parse(_src[start.._i], Inv));
            }

            if (_i < _src.Length && (_src[_i] == '"' || _src[_i] == '\''))
            {
                var q = _src[_i++];
                var start = _i;
                while (_i < _src.Length && _src[_i] != q)
                {
                    _i++;
                }

                var s = _src[start.._i];
                if (_i < _src.Length)
                {
                    _i++;
                }

                return Val.Str(s);
            }

            var ident = ReadIdent();
            if (ident.Length == 0)
            {
                return Val.Err("#HATA");
            }

            Skip();
            if (Match('('))
            {
                var args = new List<Val>();
                Skip();
                if (!Peek(')'))
                {
                    args.Add(ParseArg());
                    Skip();
                    while (Match(',') || Match(';'))
                    {
                        args.Add(ParseArg());
                        Skip();
                    }
                }

                Match(')');
                return Call(ident, args);
            }

            if (TryParseCell(ident, out var c, out var r1))
            {
                Skip();
                if (Match(':'))
                {
                    var endTok = ReadIdent();
                    if (!TryParseCell(endTok, out var c2, out var r2))
                    {
                        return Val.Err("#YOK");
                    }

                    return RangeVal(c, r1, c2, r2);
                }

                var text = EvalCell(r1 - 1, c, _headers, _rows, _cache, _visiting);
                if (double.TryParse(text, NumberStyles.Float, Inv, out var n))
                {
                    return Val.Num(n);
                }

                return Val.Str(text);
            }

            return Val.Err("#YOK");
        }

        private Val ParseArg()
        {
            Skip();
            var start = _i;
            var ident = ReadIdent();
            Skip();
            if (TryParseCell(ident, out var c, out var r1) && Match(':'))
            {
                var endTok = ReadIdent();
                if (TryParseCell(endTok, out var c2, out var r2))
                {
                    return RangeVal(c, r1, c2, r2);
                }
            }

            _i = start;
            return ParseCmp();
        }

        private Val Call(string name, List<Val> args)
        {
            var nums = Flatten(args);
            switch (NormFn(name))
            {
                case "SUM":
                case "TOPLA":
                    return Val.Num(nums.Sum());
                case "AVERAGE":
                case "ORTALAMA":
                    return nums.Count == 0 ? Val.Err("#SAYI/0!") : Val.Num(nums.Average());
                case "MIN":
                    return nums.Count == 0 ? Val.Err("#YOK") : Val.Num(nums.Min());
                case "MAX":
                    return nums.Count == 0 ? Val.Err("#YOK") : Val.Num(nums.Max());
                case "COUNT":
                case "SAYI":
                    return Val.Num(nums.Count);
                case "COUNTA":
                case "SAYDOLU":
                    return Val.Num(args.Sum(CountA));
                case "IF":
                case "EGER":
                    if (args.Count < 2) return Val.Err("#HATA");
                    return args[0].Truthy() ? args[1] : (args.Count > 2 ? args[2] : Val.Str(""));
                case "ROUND":
                case "YUVARLA":
                    var digits = args.Count > 1 ? (int)args[1].AsNumber() : 0;
                    return Val.Num(Math.Round(args[0].AsNumber(), digits, MidpointRounding.AwayFromZero));
                case "ABS":
                    return Val.Num(Math.Abs(args[0].AsNumber()));
                case "CONCAT":
                case "BIRLESTIR":
                    return Val.Str(string.Concat(args.Select(a => a.Display())));
                case "LEN":
                case "UZUNLUK":
                    return Val.Num((args.Count > 0 ? args[0].Display() : "").Length);
                case "SQRT":
                case "KAREKOK":
                    var s = args.Count > 0 ? args[0].AsNumber() : 0;
                    return s < 0 ? Val.Err("#SAYI") : Val.Num(Math.Sqrt(s));
                default:
                    return Val.Err("#AD?");
            }
        }

        private List<double> Flatten(List<Val> args)
        {
            var list = new List<double>();
            foreach (var a in args)
            {
                if (a.Error is not null) continue;
                if (a.List is not null) list.AddRange(a.List);
                else if (a.Number is { } n) list.Add(n);
                else if (double.TryParse(a.Text, NumberStyles.Float, Inv, out var p)) list.Add(p);
            }

            return list;
        }

        private Val RangeVal(int c1, int r1, int c2, int r2)
            => Val.Arr(NumbersInRange(c1, r1, c2, r2), CountNonEmpty(c1, r1, c2, r2));

        private static string NormFn(string name)
        {
            var s = name.Trim().ToUpperInvariant();
            return s.Replace('\u0130', 'I').Replace('\u0131', 'I')
                .Replace('Ğ', 'G').Replace('Ü', 'U').Replace('Ş', 'S').Replace('Ö', 'O').Replace('Ç', 'C');
        }

        private static int CountA(Val a)
        {
            if (a.CountAll is { } n) return n;
            if (a.List is not null) return a.List.Count;
            if (a.Error is not null) return 0;
            return string.IsNullOrWhiteSpace(a.Display()) ? 0 : 1;
        }

        private int CountNonEmpty(int c1, int r1, int c2, int r2)
        {
            var n = 0;
            var minC = Math.Min(c1, c2);
            var maxC = Math.Max(c1, c2);
            var minR = Math.Min(r1, r2);
            var maxR = Math.Max(r1, r2);
            for (var r = minR; r <= maxR; r++)
            {
                for (var c = minC; c <= maxC; c++)
                {
                    if (!string.IsNullOrWhiteSpace(EvalCell(r - 1, c, _headers, _rows, _cache, _visiting)))
                    {
                        n++;
                    }
                }
            }

            return n;
        }

        private List<double> NumbersInRange(int c1, int r1, int c2, int r2)
        {
            var list = new List<double>();
            var minC = Math.Min(c1, c2);
            var maxC = Math.Max(c1, c2);
            var minR = Math.Min(r1, r2);
            var maxR = Math.Max(r1, r2);
            for (var r = minR; r <= maxR; r++)
            {
                for (var c = minC; c <= maxC; c++)
                {
                    var text = EvalCell(r - 1, c, _headers, _rows, _cache, _visiting);
                    if (double.TryParse(text, NumberStyles.Float, Inv, out var n))
                    {
                        list.Add(n);
                    }
                }
            }

            return list;
        }

        private string ReadIdent()
        {
            Skip();
            var start = _i;
            while (_i < _src.Length && (char.IsLetterOrDigit(_src[_i]) || _src[_i] is '_' or '$'))
            {
                _i++;
            }

            return _src[start.._i];
        }

        private string PeekOp()
        {
            if (_i + 1 < _src.Length)
            {
                var two = _src[_i..(_i + 2)];
                if (two is "<>" or "<=" or ">=")
                {
                    return two;
                }
            }

            if (_i < _src.Length && _src[_i] is '<' or '>' or '=')
            {
                return _src[_i].ToString();
            }

            return "";
        }

        private void Skip()
        {
            while (_i < _src.Length && char.IsWhiteSpace(_src[_i]))
            {
                _i++;
            }
        }

        private bool Peek(char c) => _i < _src.Length && _src[_i] == c;

        private bool Match(char c)
        {
            if (!Peek(c))
            {
                return false;
            }

            _i++;
            return true;
        }
    }
}
