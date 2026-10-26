namespace Tafaqqad.Api.Services;

public interface IFileStorage
{
    /// Saves the file under Uploads/{folder} and returns a relative path like "office-requests/abc/xyz.pdf".
    Task<string> SaveAsync(IFormFile file, string folder);
    void DeleteFolder(string folder);
    string? ResolvePath(string relativePath);
    void DeleteFile(string relativePath);
}

public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    // Uploads live next to the app, NOT in wwwroot, so they are never served publicly.
    public LocalFileStorage(IWebHostEnvironment env) =>
        _root = Path.Combine(env.ContentRootPath, "Uploads");

    public async Task<string> SaveAsync(IFormFile file, string folder)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var name = $"{Guid.NewGuid():N}{ext}";           // we choose the name, never the user
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);

        await using var stream = File.Create(Path.Combine(dir, name));
        await file.CopyToAsync(stream);

        return $"{folder}/{name}";
    }

    public void DeleteFolder(string folder)
    {
        var dir = Path.Combine(_root, folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // Returns the full path only if the file exists AND is inside the Uploads folder
    // (blocks tricks like "../../appsettings.json").
    public string? ResolvePath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rootWithSeparator = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) && File.Exists(full)
            ? full
            : null;
    }

    public void DeleteFile(string relativePath)
    {
        var path = ResolvePath(relativePath);
        if (path is not null) File.Delete(path);
    }
}