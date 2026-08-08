namespace ASL.DirectoryService;

public interface IDirectoryService
{
    IEnumerable<string> BrowseRepositories(string rootPath);

    IEnumerable<string> BrowseDirectory(string path);

    IEnumerable<string> BrowseTree(string path);

    bool Exists(string path);
}