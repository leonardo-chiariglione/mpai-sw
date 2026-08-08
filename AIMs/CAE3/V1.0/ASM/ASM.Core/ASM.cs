using ASL.FileService;
using ASL.RepositoryService;
using ASL.SearchService;

namespace ASM.Core;

public class ASM
{
    private const string RootPath = @"D:\AI";

    private readonly IRepositoryService _repositoryService;
    private readonly IFileService _fileService;
    private readonly ISearchService _searchService;

    public ASM()
    {
        _repositoryService = new RepositoryService();
        _fileService = new FileService();
        _searchService = new SearchService();
    }

    public string Execute(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return "Empty instruction.";
        }

        var parts = instruction.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        var command = parts[0].ToLowerInvariant();

        try
        {
            switch (command)
            {
                case "browse":
                    return Browse(parts);

                case "read":
                    return Read(parts);

                case "searchdir":
                    return SearchDirectory(parts);

                case "searchfile":
                    return SearchFile(parts);

                case "find":
                    return Find(parts);

                case "open":
                    return Open(parts);

                case "help":
                    return Help();

                default:
                    return $"Unknown command: {command}";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private string Browse(string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: browse <path>";
        }

        return string.Join(
            Environment.NewLine,
            _repositoryService.BrowseDirectory(parts[1]));
    }

    private string Read(string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: read <path>";
        }

        return _fileService.ReadFile(parts[1]);
    }

    private string SearchDirectory(string[] parts)
    {
        if (parts.Length < 3)
        {
            return "Usage: searchdir <rootPath> <pattern>";
        }

        return string.Join(
            Environment.NewLine,
            _searchService.SearchDirectories(
                parts[1],
                parts[2]));
    }

    private string SearchFile(string[] parts)
    {
        if (parts.Length < 3)
        {
            return "Usage: searchfile <rootPath> <pattern>";
        }

        return string.Join(
            Environment.NewLine,
            _searchService.SearchFileNames(
                parts[1],
                parts[2]));
    }

    private string Find(string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: find <text>";
        }

        var matches =
            _searchService
                .SearchFileNames(
                    RootPath,
                    parts[1])
                .Where(path =>
                    !path.Contains(@"\bin\") &&
                    !path.Contains(@"\obj\"))
                .Take(20);

        return string.Join(
            Environment.NewLine,
            matches);
    }

    private string Open(string[] parts)
    {
        if (parts.Length < 2)
        {
            return "Usage: open <filename>";
        }

        var matches =
            _searchService
                .SearchFileNames(
                    RootPath,
                    parts[1])
                .Where(path =>
                    !path.Contains(@"\bin\") &&
                    !path.Contains(@"\obj\"))
                .Take(20)
                .ToList();

        if (matches.Count == 0)
        {
            return $"No file found matching '{parts[1]}'.";
        }

        if (matches.Count > 1)
        {
            return
                "Multiple matches found:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    matches);
        }

        return _fileService.ReadFile(matches[0]);
    }

    private string Help()
    {
        return """
Available commands:

browse <path>
read <path>
searchdir <rootPath> <pattern>
searchfile <rootPath> <pattern>
find <text>
open <filename>
help
exit
""";
    }
}