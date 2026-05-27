using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpessaSharp.Utils;

public abstract class BasePrinter(StringBuilder? sb = null)
{
    [InterpolatedStringHandler]
    public ref struct SHWrapper
    {
        private StringBuilder.AppendInterpolatedStringHandler _handler;

        public SHWrapper(int literalLength, int formattedCount, BasePrinter printer)
        {
            printer.AppendIndent();
            _handler = new StringBuilder.AppendInterpolatedStringHandler(
                literalLength, formattedCount, printer._sb);
        }

        public void AppendLiteral(string s) => _handler.AppendLiteral(s);
        public void AppendFormatted<T>(T t) => _handler.AppendFormatted(t);
        public void AppendFormatted<T>(T t, string format) =>
            _handler.AppendFormatted(t, format);
    }
    
    private readonly StringBuilder _sb = sb ?? new StringBuilder();
    private int _indent;
    private bool _indentedLine;
    
    private void AppendIndent()
    {
        if (_indentedLine) return;
        _indentedLine = true;
        _sb.Append(' ', _indent);
    }

    protected static string ToStr(bool b) =>
        b ? "true" : "false";

    protected static string ToStr<T>(T t) where T : unmanaged =>
        t.ToString()!.ToLower();

    public BasePrinter IncIndent(int v = 4) => SetIndent(_indent + v);
    
    public BasePrinter SetIndent(int v)
    {
        _indent = v;
        return this;
    }

    public BasePrinter Append(
        [InterpolatedStringHandlerArgument("")]
        ref SHWrapper handler) => this;

    public BasePrinter AppendLine(
        [InterpolatedStringHandlerArgument("")]
        ref SHWrapper handler) => AppendLine();

    public BasePrinter Append(string s = "")
    {
        AppendIndent();
        _sb.Append(s);
        return this;
    }

    public BasePrinter Append(char c, int total)
    {
        AppendIndent();
        _sb.Append(c, total);
        return this;
    }

    public BasePrinter AppendLine(string s = "")
    {
        if (!string.IsNullOrWhiteSpace(s))
            AppendIndent();
        _sb.AppendLine(s);
        _indentedLine = false;
        return this;
    }
    
    public BasePrinter AppendLine(char c, int total)
    {
        AppendIndent();
        _sb.Append(c, total);
        _sb.AppendLine();
        _indentedLine = false;
        return this;
    }

    protected BasePrinter Clear()
    {
        _sb.Clear();
        _indent = 0;
        _indentedLine = false;
        return this;
    }
    
    public string GetString() => _sb.ToString();
    
    public abstract string Print();
    
    // From Util.ToISOString
    protected static string ToISOString(DateTime dt) =>
        dt.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'", 
            CultureInfo.InvariantCulture);
}