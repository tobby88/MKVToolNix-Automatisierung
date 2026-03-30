using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Puffert häufige Textupdates und übergibt sie gebündelt an die UI, damit Status-/Logausgaben flüssig bleiben.
/// </summary>
public sealed class BufferedTextStore
{
    private readonly StringBuilder _buffer = new();
    private readonly object _sync = new();
    private readonly Action<Action> _scheduleFlush;
    private readonly Action<string> _applyText;
    private bool _flushScheduled;

    public BufferedTextStore(Action<Action> scheduleFlush, Action<string> applyText)
    {
        _scheduleFlush = scheduleFlush;
        _applyText = applyText;
    }

    public void Reset(string initialText = "")
    {
        initialText = MojibakeRepair.NormalizeLikelyMojibake(initialText);
        lock (_sync)
        {
            _buffer.Clear();
            _buffer.Append(initialText);
            _flushScheduled = false;
        }

        _applyText(initialText);
    }

    public void AppendLine(string line)
    {
        line = MojibakeRepair.NormalizeLikelyMojibake(line);
        lock (_sync)
        {
            _buffer.AppendLine(line);
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
        }

        _scheduleFlush(Flush);
    }

    public string GetTextSnapshot()
    {
        lock (_sync)
        {
            return _buffer.ToString();
        }
    }

    private void Flush()
    {
        string text;
        lock (_sync)
        {
            text = _buffer.ToString();
            _flushScheduled = false;
        }

        _applyText(text);
    }
}
