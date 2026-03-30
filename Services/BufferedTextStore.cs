using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Puffert häufige Textupdates und übergibt sie gebündelt an die UI, damit Status-/Logausgaben flüssig bleiben.
/// </summary>
public sealed class BufferedTextStore
{
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _pendingAppendBuffer = new();
    private readonly object _sync = new();
    private readonly Action<Action> _scheduleFlush;
    private readonly Action<string> _replaceText;
    private readonly Action<string>? _appendText;
    private bool _flushScheduled;

    /// <summary>
    /// Erstellt einen Textpuffer fuer haeufige UI-Updates.
    /// </summary>
    /// <param name="scheduleFlush">Plant das spaetere Flushen auf dem gewuenschten Thread ein.</param>
    /// <param name="replaceText">Setzt den gesamten Textbestand, z. B. nach Reset oder initialem Laden.</param>
    /// <param name="appendText">
    /// Optionaler inkrementeller UI-Callback. Wenn gesetzt, werden Flushes nur mit den seit dem letzten Flush
    /// neu hinzugekommenen Zeilen gemeldet, statt jedes Mal den kompletten Text neu aufzubauen.
    /// </param>
    public BufferedTextStore(Action<Action> scheduleFlush, Action<string> replaceText, Action<string>? appendText = null)
    {
        _scheduleFlush = scheduleFlush;
        _replaceText = replaceText;
        _appendText = appendText;
    }

    public void Reset(string initialText = "")
    {
        initialText = MojibakeRepair.NormalizeLikelyMojibake(initialText);
        lock (_sync)
        {
            _buffer.Clear();
            _pendingAppendBuffer.Clear();
            _buffer.Append(initialText);
            _flushScheduled = false;
        }

        _replaceText(initialText);
    }

    public void AppendLine(string line)
    {
        line = MojibakeRepair.NormalizeLikelyMojibake(line);
        lock (_sync)
        {
            _buffer.AppendLine(line);
            _pendingAppendBuffer.AppendLine(line);
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
        string? appendedText = null;
        string? fullText = null;
        lock (_sync)
        {
            _flushScheduled = false;
            if (_pendingAppendBuffer.Length == 0)
            {
                return;
            }

            if (_appendText is null)
            {
                fullText = _buffer.ToString();
            }
            else
            {
                appendedText = _pendingAppendBuffer.ToString();
            }

            _pendingAppendBuffer.Clear();
        }

        if (_appendText is null)
        {
            _replaceText(fullText!);
            return;
        }

        _appendText(appendedText!);
    }
}
