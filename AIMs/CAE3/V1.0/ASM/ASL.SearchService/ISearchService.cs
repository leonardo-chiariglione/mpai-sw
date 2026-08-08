namespace ASL.SearchService;

public interface ISearchService
{
    IEnumerable<string> SearchFileNames(
        string rootPath,
        string pattern);

    IEnumerable<string> SearchDirectories(
        string rootPath,
        string pattern);

    IEnumerable<string> SearchContent(
        string rootPath,
        string text);
}