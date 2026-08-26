using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Browser-WASM implementation of the character persistence boundary. The experimental itch.io
/// runtime is single-threaded and System.IO writes against Emscripten's mounted user filesystem can
/// stall a managed callback indefinitely. Godot already owns that filesystem, so browser character
/// documents, paint PNGs and directory transactions go through FileAccess/DirAccess instead.
/// </summary>
internal sealed class GodotBrowserCharacterFileSystem : ICharacterFileSystem
{
    public bool FileExists(string path) => Godot.FileAccess.FileExists(path);

    public bool DirectoryExists(string path) => DirAccess.DirExistsAbsolute(path);

    public void CreateDirectory(string path)
    {
        Error error = DirAccess.MakeDirRecursiveAbsolute(path);
        if (error != Error.Ok && !DirectoryExists(path))
            throw new IOException($"Could not create browser character directory: {path} ({error}).");
    }

    public IReadOnlyList<string> EnumerateDirectories(string path)
    {
        if (!DirectoryExists(path))
            return Array.Empty<string>();

        string[] names = DirAccess.GetDirectoriesAt(path);
        var result = new string[names.Length];
        for (int index = 0; index < names.Length; index++)
            result[index] = Path.Combine(path, names[index]);
        return result;
    }

    public string ReadAllText(string path)
    {
        using var file = Open(path, Godot.FileAccess.ModeFlags.Read, "reading text");
        return file.GetAsText();
    }

    public byte[] ReadPrefix(string path, int maximumBytes)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        using var file = Open(path, Godot.FileAccess.ModeFlags.Read, "reading metadata");
        ulong length = Math.Min(file.GetLength(), (ulong)maximumBytes);
        return file.GetBuffer((long)length);
    }

    public byte[] ReadAllBytes(string path, int maximumBytes)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        using var file = Open(path, Godot.FileAccess.ModeFlags.Read, "reading bytes");
        ulong length = file.GetLength();
        if (length > (ulong)maximumBytes)
            throw new InvalidDataException($"File exceeds the {maximumBytes}-byte limit.");
        return file.GetBuffer((long)length);
    }

    public void WriteAllTextDurable(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var file = Open(path, Godot.FileAccess.ModeFlags.Write, "writing text");
        file.StoreString(content);
        EnsureWriteSucceeded(file, path);
    }

    public void WriteAllBytesDurable(string path, ReadOnlySpan<byte> content)
    {
        using var file = Open(path, Godot.FileAccess.ModeFlags.Write, "writing bytes");
        file.StoreBuffer(content.ToArray());
        EnsureWriteSucceeded(file, path);
    }

    public void ReplaceFileWithBackup(string temporaryPath, string primaryPath, string backupPath)
    {
        DeleteFile(backupPath);
        if (FileExists(primaryPath))
        {
            using var source = Open(primaryPath, Godot.FileAccess.ModeFlags.Read, "reading backup source");
            byte[] existing = source.GetBuffer((long)source.GetLength());
            WriteAllBytesDurable(backupPath, existing);
            DeleteFile(primaryPath);
        }
        MoveFile(temporaryPath, primaryPath);
    }

    public void MoveFile(string sourcePath, string destinationPath) => Move(sourcePath, destinationPath);

    public void MoveDirectory(string sourcePath, string destinationPath) => Move(sourcePath, destinationPath);

    public void DeleteFile(string path)
    {
        if (!FileExists(path))
            return;
        Error error = DirAccess.RemoveAbsolute(path);
        if (error != Error.Ok)
            throw new IOException($"Could not remove browser character file: {path} ({error}).");
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        if (!DirectoryExists(path))
            return;

        if (recursive)
        {
            foreach (string fileName in DirAccess.GetFilesAt(path))
                DeleteFile(Path.Combine(path, fileName));
            foreach (string directoryName in DirAccess.GetDirectoriesAt(path))
                DeleteDirectory(Path.Combine(path, directoryName), recursive: true);
        }

        Error error = DirAccess.RemoveAbsolute(path);
        if (error != Error.Ok)
            throw new IOException($"Could not remove browser character directory: {path} ({error}).");
    }

    public FileAttributes GetAttributes(string path) =>
        DirectoryExists(path) ? FileAttributes.Directory : FileAttributes.Normal;

    public bool IsReparsePoint(string path) => false;

    private static Godot.FileAccess Open(
        string path,
        Godot.FileAccess.ModeFlags mode,
        string operation)
    {
        Godot.FileAccess? file = Godot.FileAccess.Open(path, mode);
        if (file is null)
            throw new IOException($"Could not open browser character file for {operation}: {path} ({Godot.FileAccess.GetOpenError()}).");
        return file;
    }

    private static void EnsureWriteSucceeded(Godot.FileAccess file, string path)
    {
        Error error = file.GetError();
        if (error != Error.Ok)
            throw new IOException($"Could not write browser character file: {path} ({error}).");
    }

    private static void Move(string sourcePath, string destinationPath)
    {
        Error error = DirAccess.RenameAbsolute(sourcePath, destinationPath);
        if (error != Error.Ok)
            throw new IOException($"Could not move browser character path {sourcePath} -> {destinationPath} ({error}).");
    }
}
