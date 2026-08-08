namespace ASL.FileService;

public class FileService : IFileService
{
    public string ReadFile(string path)
    {
        return File.ReadAllText(path);
    }

    public void WriteFile(string path, string content)
    {
        File.WriteAllText(path, content);
    }

    public void CreateFile(string path)
    {
        using var stream = File.Create(path);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void CopyFile(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, true);
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath, true);
    }
}