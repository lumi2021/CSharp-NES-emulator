using System.Numerics;
using ImGuiNET;

public sealed class FileDialog
{

    private string _currentDirectory = "";
    private string _selectedFile = "";
    private string _fileName = "";

    private string[] _extensions = [];

    private Action<string>? _onOpen;
    private Action? _onCancel;

    public bool IsOpen { get; private set; }

    public void Open(
        string initialDirectory,
        string[]? extensions = null,
        Action<string>? onOpen = null,
        Action? onCancel = null)
    {
        if (!Directory.Exists(initialDirectory))
            initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        _currentDirectory = Path.GetFullPath(initialDirectory);
        _selectedFile = "";
        _fileName = "";

        _extensions = extensions ?? [];

        _onOpen = onOpen;
        _onCancel = onCancel;

        IsOpen = true;
    }

    public void Draw()
    {
        if (!IsOpen) return;

        ImGui.SetNextWindowSize(new Vector2(900, 600), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(500, 350), new Vector2(float.MaxValue, float.MaxValue));

        var visible = true;

        if (ImGui.Begin("Select File", ref visible, ImGuiWindowFlags.NoCollapse))
        {
            DrawNavigation();
            ImGui.Separator();

            DrawFileList();

            ImGui.Separator();

            DrawFooter();
        }

        ImGui.End();

        if (!visible) Cancel();
    }

    private void DrawNavigation()
    {
        if (ImGui.Button("<"))
        {
            var parent = Directory.GetParent(_currentDirectory)?.FullName;

            if (parent != null)
            {
                _currentDirectory = parent;
                ClearSelection();
            }
        }

        ImGui.SameLine();

        if (ImGui.Button(">"))
        {
            // Navegação futura:
            // mantenha um histórico de diretórios se quiser implementar isso.
        }

        ImGui.SameLine();

        if (ImGui.Button("^"))
        {
            var parent = Directory.GetParent(_currentDirectory)?.FullName;

            if (parent != null)
            {
                _currentDirectory = parent;
                ClearSelection();
            }
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1);

        var directory = _currentDirectory;

        if (ImGui.InputText(
                "##currentDirectory",
                ref directory,
                4096,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (Directory.Exists(directory))
            {
                _currentDirectory = Path.GetFullPath(directory);
                ClearSelection();
            }
        }
    }

    private void DrawFileList()
    {
        ImGui.BeginChild("FileList", new Vector2(0, -100), ImGuiChildFlags.Border);
        DrawTableHeader();
        ImGui.Separator();

        try
        {
            var directories = Directory
                .EnumerateDirectories(_currentDirectory)
                .OrderBy(Path.GetFileName);

            foreach (var directory in directories) DrawDirectory(directory);

            var files = Directory
                .EnumerateFiles(_currentDirectory)
                .Where(IsAllowedFile)
                .OrderBy(Path.GetFileName);

            foreach (var file in files) DrawFile(file);
        }
        catch (UnauthorizedAccessException)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), "Permission denied.");
        }
        catch (IOException exception)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), exception.Message);
        }

        ImGui.EndChild();
    }

    private void DrawTableHeader()
    {
        ImGui.Columns(4, "FileColumns", true);

        ImGui.Text("Name");
        ImGui.NextColumn();

        ImGui.Text("Size");
        ImGui.NextColumn();

        ImGui.Text("Type");
        ImGui.NextColumn();

        ImGui.Text("Modified");
        ImGui.NextColumn();

        ImGui.Columns(1);
    }

    private void DrawDirectory(string path)
    {
        var name = Path.GetFileName(path);

        if (string.IsNullOrEmpty(name)) name = path;

        ImGui.PushID(path);

        ImGui.Columns(4, "DirectoryColumns", false);

        var selected = false;

        if (ImGui.Selectable($"[DIR] {name}", selected, ImGuiSelectableFlags.SpanAllColumns))
            _selectedFile = "";
        
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _currentDirectory = path;
            ClearSelection();
        }

        ImGui.NextColumn();

        ImGui.Text("-");
        ImGui.NextColumn();

        ImGui.Text("Directory");
        ImGui.NextColumn();

        try
        {
            ImGui.Text(Directory.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"));
        }
        catch
        {
            ImGui.Text("-");
        }

        ImGui.NextColumn();

        ImGui.Columns(1);

        ImGui.PopID();
    }

    private void DrawFile(string path)
    {
        var name = Path.GetFileName(path);

        var selected = string.Equals(_selectedFile, path, StringComparison.OrdinalIgnoreCase);

        ImGui.PushID(path);
        ImGui.Columns(4, "FileColumns", false);

        if (ImGui.Selectable(name, selected, ImGuiSelectableFlags.SpanAllColumns))
        {
            _selectedFile = path;
            _fileName = name;

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenSelected();
        }

        ImGui.NextColumn();

        try
        {
            ImGui.Text(FormatSize(new FileInfo(path).Length));
        }
        catch
        {
            ImGui.Text("-");
        }

        ImGui.NextColumn();

        ImGui.Text(GetFileType(path));
        ImGui.NextColumn();

        try
        {
            ImGui.Text(File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"));
        }
        catch
        {
            ImGui.Text("-");
        }

        ImGui.NextColumn();
        ImGui.Columns(1);
        
        ImGui.PopID();
    }

    private void DrawFooter()
    {
        ImGui.Text("File name:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(350);
        ImGui.InputText("##FileName", ref _fileName, 1024);

        ImGui.SameLine();

        if (ImGui.Button("Open")) OpenSelected();
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) Cancel();
    }

    private bool IsAllowedFile(string path)
    {
        if (_extensions.Length == 0) return true;
        var extension = Path.GetExtension(path);
        return _extensions.Any(x => string.Equals(NormalizeExtension(x), extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeExtension(string extension)
    {
        if (extension.StartsWith("*.")) return extension[1..];
        if (!extension.StartsWith('.')) return "." + extension;
        return extension;
    }

    private void OpenSelected()
    {
        string path;

        if (!string.IsNullOrWhiteSpace(_fileName))
        {
            path = Path.Combine(_currentDirectory, _fileName);
        }
        else
        {
            path = _selectedFile;
        }

        if (!File.Exists(path)) return;
        IsOpen = false;
        _onOpen?.Invoke(Path.GetFullPath(path));
    }

    private void Cancel()
    {
        IsOpen = false;
        _onCancel?.Invoke();
    }

    private void ClearSelection()
    {
        _selectedFile = "";
        _fileName = "";
    }

    private static string GetFileType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".cs" => "C# Source",
            ".cpp" => "C++ Source",
            ".h" or ".hpp" => "C/C++ Header",
            ".png" => "PNG Image",
            ".jpg" or ".jpeg" => "JPEG Image",
            ".gif" => "GIF Image",
            ".txt" => "Text Document",
            ".json" => "JSON",
            ".xml" => "XML",
            ".pdf" => "PDF Document",
            ".dll" => "Assembly",
            ".exe" => "Executable",
            
            _ => string.IsNullOrEmpty(extension)
                ? "File"
                : extension[1..].ToUpperInvariant() + " File"
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }
}