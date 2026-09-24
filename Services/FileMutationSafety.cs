using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Konservative Schreibgrenze für Medien und Begleitdateien. Lexikalische Pfadprüfungen
/// allein können Links nicht erkennen; deshalb werden vorhandene Vorfahren kontrolliert.
/// Die Prüfung ist kein Schutz gegen einen lokalen Angreifer, der danach Pfade austauscht.
/// </summary>
internal static class FileMutationSafety
{
    /// <summary>
    /// Weist Reparse-Points und case-sensitive Unterbäume ab. Nicht vorhandene Zielordner
    /// sind erlaubt; ihre vorhandenen Vorfahren müssen dieselben Bedingungen erfüllen.
    /// </summary>
    public static void EnsureOrdinaryPath(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { current = Path.GetDirectoryName(current); continue; }
            catch (DirectoryNotFoundException) { current = Path.GetDirectoryName(current); continue; }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Schreiben über einen Datei-/Verzeichnislink ist nicht erlaubt: {current}");

            if ((attributes & FileAttributes.Directory) != 0)
            {
                using var handle = CreateFile(current, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                if (handle.IsInvalid)
                    throw new IOException($"Pfadprüfung fehlgeschlagen: {current}", new Win32Exception(Marshal.GetLastWin32Error()));
                // Ältere Dateisysteme/SMB-Server unterstützen FileCaseSensitiveInfo nicht.
                // Dort gelten weiterhin die vorhandenen exakten Ziel-Kollisionsprüfungen.
                if (GetFileInformationByHandleEx(handle, 23, out var flags, sizeof(uint)) && (flags & 1) != 0)
                    throw new IOException($"Case-sensitive Verzeichnisse werden beim Schreiben nicht unterstützt: {current}");
            }
            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>
    /// Verhindert zusätzlich in-place Änderungen an einem Hardlink, die unbemerkt auch
    /// einen anderen Archivpfad verändern würden. Nicht vorhandene Ziele bleiben erlaubt.
    /// </summary>
    public static void EnsureOrdinaryFile(string path)
    {
        EnsureOrdinaryPath(path);
        if (!File.Exists(path)) return;
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var information))
            throw new IOException($"Dateiidentität konnte nicht geprüft werden: {path}", new Win32Exception(Marshal.GetLastWin32Error()));
        if (information.NumberOfLinks > 1)
            throw new IOException($"Dateien mit mehreren Hardlinks werden nicht verändert: {path}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, SizeHigh, SizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out uint information, uint size);
}
