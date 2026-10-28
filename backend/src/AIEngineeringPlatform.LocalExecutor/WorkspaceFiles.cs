using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
namespace AIEngineeringPlatform.LocalExecutor;

public static class WorkspaceFiles
{
    private static readonly string[] IgnoredDirectories = [".git", "node_modules", "bin", "obj", "dist", "coverage", "test-results", "playwright-report"];
    public static bool IsSecret(string path) => path.Split('/').Any(part => part.StartsWith(".env", StringComparison.OrdinalIgnoreCase) && part != ".env.example") || new[] { ".pem", ".key", ".pfx", ".p12" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || Path.GetFileName(path) is "id_rsa" or "id_ed25519";
    public static bool IsSource(string path) => !IsSecret(path) && !path.Split('/').Any(IgnoredDirectories.Contains);
    public static string Resolve(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Any(character => char.IsControl(character)) || path.Split('/').Any(x => x is ".." or ".git" or "") || IsSecret(path)) throw new ArgumentException("Unsafe or secret repository path.");
        var current = root;
        foreach (var part in path.Split('/'))
        {
            current = Path.Combine(current, part);
            if (new FileInfo(current).LinkTarget != null || new DirectoryInfo(current).LinkTarget != null) throw new ArgumentException("Symbolic links are not permitted.");
        }
        if (!Path.GetFullPath(current).StartsWith(Path.GetFullPath(root).TrimEnd('/') + "/", StringComparison.Ordinal)) throw new ArgumentException("Path escaped workspace.");
        return current;
    }
    public static string[] Enumerate(string root, int limitMb)
    {
        var files = new List<string>(); long bytes = 0;
        void Walk(string directory)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, item);
                if (new FileInfo(item).LinkTarget != null || new DirectoryInfo(item).LinkTarget != null) throw new ArgumentException("Repository symlinks are not supported.");
                var attributes = File.GetAttributes(item);
                if (attributes.HasFlag(FileAttributes.Directory)) { if (!IgnoredDirectories.Contains(Path.GetFileName(item))) Walk(item); continue; }
                if (IsSecret(relative)) continue;
                var info = new FileInfo(item);
                bytes += info.Length;
                if (files.Count >= 10000 || bytes > limitMb * 1024L * 1024 || info.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Repository snapshot exceeds file or byte limits.");
                files.Add(relative);
            }
        }
        Walk(root); return files.Order(StringComparer.Ordinal).ToArray();
    }
    public static string Revision(string root, int limitMb)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Enumerate(root, limitMb))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path + "\0"));
            hash.AppendData(File.ReadAllBytes(Resolve(root, path)));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public static byte[] Archive(string root, int limitMb)
    {
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, leaveOpen: true))
            foreach (var path in Enumerate(root, limitMb))
            {
                using var data = File.OpenRead(Resolve(root, path));
                var entry = new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = data, Uid = 65532, Gid = 65532, Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite };
                writer.WriteEntry(entry);
            }
        return stream.ToArray();
    }
    public static void Write(string root, string path, string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > 100000) throw new ArgumentException("File content is too large.");
        var target = Resolve(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".aep-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, content, new UTF8Encoding(false)); File.Move(temporary, target, overwrite: true);
    }
    // A deliberately limited structured patch avoids feeding attacker-controlled patch
    // paths/options to a host program. Every replacement must match exactly once.
    public static void Patch(string root, string path, string search, string replacement)
    {
        if (string.IsNullOrEmpty(search)) throw new ArgumentException("Patch search text is required.");
        var content = File.ReadAllText(Resolve(root, path));
        var start = content.IndexOf(search, StringComparison.Ordinal);
        if (start < 0 || content.IndexOf(search, start + search.Length, StringComparison.Ordinal) >= 0) throw new ArgumentException("Patch must match exactly one source region.");
        Write(root, path, content[..start] + replacement + content[(start + search.Length)..]);
    }
}
