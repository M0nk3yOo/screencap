namespace Screencap.Core;

internal static class AtomicFile
{
    /// <summary>Writes to a temp file first so a crash never leaves a half-written file behind.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
