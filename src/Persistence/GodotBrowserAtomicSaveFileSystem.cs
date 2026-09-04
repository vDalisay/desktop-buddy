using System.IO;
using Godot;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Web-only filesystem adapter for <see cref="JsonProgressStore"/>. The browser build's
/// persistent user filesystem belongs to Godot/Emscripten; using System.IO directly in the
/// experimental single-threaded .NET runtime can stall during the first write.
/// </summary>
internal sealed class GodotBrowserAtomicSaveFileSystem : IAtomicSaveFileSystem
{
    public bool Exists(string path) => Godot.FileAccess.FileExists(path);

    public string ReadAllText(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
            throw new IOException($"Could not open browser save for reading: {path} ({Godot.FileAccess.GetOpenError()}).");
        return file.GetAsText();
    }

    public void CreateDirectory(string path)
    {
        GD.Print($"[INFO] [WebPersistence] Ensuring save directory {path}.");
        Error error = DirAccess.MakeDirRecursiveAbsolute(path);
        if (error != Error.Ok && DirAccess.Open(path) is null)
            throw new IOException($"Could not create browser save directory: {path} ({error}).");
    }

    public void WriteDurable(string path, string contents)
    {
        GD.Print($"[INFO] [WebPersistence] Writing {path} chars={contents.Length}.");
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file is null)
            throw new IOException($"Could not open browser save for writing: {path} ({Godot.FileAccess.GetOpenError()}).");
        file.StoreString(contents);
        Error error = file.GetError();
        if (error != Error.Ok)
            throw new IOException($"Could not write browser save: {path} ({error}).");
        GD.Print($"[INFO] [WebPersistence] Wrote {path}; closing FileAccess.");
    }

    public void Replace(string temporary, string primary, string backup)
    {
        if (Exists(backup))
            Remove(backup);
        if (Exists(primary))
        {
            WriteDurable(backup, ReadAllText(primary));
            Remove(primary);
        }
        Move(temporary, primary);
    }

    public void Move(string source, string destination)
    {
        GD.Print($"[INFO] [WebPersistence] Moving {source} -> {destination}.");
        Error error = DirAccess.RenameAbsolute(source, destination);
        if (error != Error.Ok)
            throw new IOException($"Could not move browser save {source} -> {destination} ({error}).");
        GD.Print($"[INFO] [WebPersistence] Move completed {destination}.");
    }

    private static void Remove(string path)
    {
        Error error = DirAccess.RemoveAbsolute(path);
        if (error != Error.Ok)
            throw new IOException($"Could not remove browser save: {path} ({error}).");
    }
}
