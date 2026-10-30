namespace CardScout.Core.Images;

public class LocalDiskImageStorage : IImageStorage
{
    private readonly string _root;

    public LocalDiskImageStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, string folder)
    {
        var relativePath = $"{folder.Trim('/')}/{DateTime.UtcNow:yyyy-MM}/{Guid.NewGuid():N}{extension}";
        var fullPath = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file);
        return relativePath;
    }

    public Task<Stream?> OpenReadAsync(string path)
    {
        var fullPath = ResolvePath(path);
        Stream? stream = File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path)
    {
        var fullPath = ResolvePath(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ResolvePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image path escapes the storage root");
        return fullPath;
    }
}
