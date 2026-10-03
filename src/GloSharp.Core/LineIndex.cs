namespace GloSharp.Core;

/// <summary>
/// O(log n) offset → line lookups over text split on <c>'\n'</c> only — the same line
/// definition <see cref="MarkerParser"/> uses (a trailing <c>'\r'</c> stays part of its line,
/// and Unicode line separators do not start new lines).
/// </summary>
internal sealed class LineIndex
{
    private readonly string _text;
    private readonly int[] _lineStarts;

    public LineIndex(string text)
    {
        _text = text;
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }
        _lineStarts = starts.ToArray();
    }

    public int LineCount => _lineStarts.Length;

    public int GetLineStart(int line) => _lineStarts[line];

    /// <summary>Length of the line's content, excluding the <c>'\n'</c> and any trailing <c>'\r'</c>.</summary>
    public int GetLineContentLength(int line)
    {
        var start = _lineStarts[line];
        var end = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] - 1 : _text.Length;
        if (end > start && _text[end - 1] == '\r')
            end--;
        return end - start;
    }

    public string GetLineText(int line) => _text.Substring(_lineStarts[line], GetLineContentLength(line));

    public int GetLine(int position)
    {
        var index = Array.BinarySearch(_lineStarts, position);
        return index >= 0 ? index : ~index - 1;
    }

    public (int Line, int Character) GetPosition(int position)
    {
        var line = GetLine(position);
        return (line, position - _lineStarts[line]);
    }
}
