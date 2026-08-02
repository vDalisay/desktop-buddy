using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Godot-free character-library filesystem boundary. The resolved absolute root is supplied
/// by the main thread; every operation thereafter is safe to execute on a worker thread.
/// </summary>
public interface ICharacterFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    IReadOnlyList<string> EnumerateDirectories(string path);
    string ReadAllText(string path);
    byte[] ReadPrefix(string path, int maximumBytes);
    void WriteAllTextDurable(string path, string content);
    void ReplaceFileWithBackup(string temporaryPath, string primaryPath, string backupPath);
    void MoveFile(string sourcePath, string destinationPath);
    void MoveDirectory(string sourcePath, string destinationPath);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    FileAttributes GetAttributes(string path);
    bool IsReparsePoint(string path);
}

public sealed class CharacterFileSystem : ICharacterFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IReadOnlyList<string> EnumerateDirectories(string path) =>
        Directory.Exists(path) ? [.. Directory.EnumerateDirectories(path)] : [];

    public string ReadAllText(string path) => File.ReadAllText(path);

    public byte[] ReadPrefix(string path, int maximumBytes)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: Math.Min(maximumBytes, 4096),
            FileOptions.SequentialScan);
        int length = (int)Math.Min(stream.Length, maximumBytes);
        var result = new byte[length];
        int offset = 0;
        while (offset < result.Length)
        {
            int read = stream.Read(result, offset, result.Length - offset);
            if (read == 0)
                break;
            offset += read;
        }

        if (offset == result.Length)
            return result;
        Array.Resize(ref result, offset);
        return result;
    }

    public void WriteAllTextDurable(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    public void ReplaceFileWithBackup(
        string temporaryPath,
        string primaryPath,
        string backupPath)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);
        File.Replace(temporaryPath, primaryPath, backupPath, ignoreMetadataErrors: true);
    }

    public void MoveFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: false);

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive);
    }

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public bool IsReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return false;

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return true;

        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        return info.LinkTarget is not null;
    }
}
