namespace ASL.SearchService;

public class SearchService : ISearchService
{
    public IEnumerable<string> SearchFileNames(
        string rootPath,
        string pattern)
    {
        return Directory
            .EnumerateFiles(
                rootPath,
                "*",
                SearchOption.AllDirectories)
            .Where(f =>
                Path.GetFileName(f)
                    .Contains(
                        pattern,
                        StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> SearchDirectories(
        string rootPath,
        string pattern)
    {
        return Directory
            .EnumerateDirectories(
                rootPath,
                "*",
                SearchOption.AllDirectories)
            .Where(d =>
                Path.GetFileName(d)
                    .Contains(
                        pattern,
                        StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> SearchContent(
        string rootPath,
        string text)
    {
        foreach (var file in Directory.EnumerateFiles(
                     rootPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            string content;

            try
            {
                content = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            if (content.Contains(
                    text,
                    StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }
}