namespace ASL.DirectoryService;

public class DirectoryService : IDirectoryService
{
    public IEnumerable<string> BrowseRepositories(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException(rootPath);
        }

        return Directory
            .GetDirectories(rootPath)
            .Select(Path.GetFileName)!
            .Where(name => !string.IsNullOrWhiteSpace(name));
    }

    public IEnumerable<string> BrowseDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        return Directory
            .EnumerateFileSystemEntries(path)
            .Select(Path.GetFileName)!
            .Where(name => !string.IsNullOrWhiteSpace(name));
    }

    public IEnumerable<string> BrowseTree(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        return Directory.EnumerateFileSystemEntries(
            path,
            "*",
            SearchOption.AllDirectories);
    }

    public bool Exists(string path)
    {
        return Directory.Exists(path)
            || File.Exists(path);
    }
}