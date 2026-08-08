namespace ASL.FileService;

public interface IFileService
{
    string ReadFile(string path);

    void WriteFile(string path, string content);

    void CreateFile(string path);

    void DeleteFile(string path);

    void CopyFile(string sourcePath, string destinationPath);

    void MoveFile(string sourcePath, string destinationPath);
}