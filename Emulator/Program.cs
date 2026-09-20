using Emulator.Components;
using ImGuiNET;
using Newtonsoft.Json;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System.Numerics;
using System.Text;

namespace Emulator;

public static class Program
{
    private static IWindow _window = null!;
    public static GL gl = null!;
    public static IInputContext input = null!;
    private static ImGuiController _imgui = null!;
    private static VirtualSystem _system = null!;

    private static readonly string IMGUI_SAVE_DEFAULT = Path.Combine(AppContext.BaseDirectory, "imgui_config_default.ini");
    private static readonly string IMGUI_SAVE = Path.Combine(AppContext.BaseDirectory, "imgui_config.ini");

    public delegate void PopupDrawingDelegate();
    public static List<(string name, bool enabled, PopupDrawingDelegate callee)> WindowViews = [];

    private static FileDialog _fileDialog = new();
    
    public static void Main() {

        //_system = new VirtualSystem();

        //RunTests();
        //return;

        WindowOptions options = WindowOptions.Default with
        {
            Size = new(1000, 500),
            Title = "SharpNES",
        };

        _window = Window.Create(options);

        _window.Load += OnLoad;
        _window.Closing += OnClose;
        _window.Update += OnUpdate;
        _window.Render += OnRender;

        _window.Resize += (s) => gl.Viewport(_window.FramebufferSize);
        _window.FramebufferResize += (s) => gl.Viewport(s);
        _window.StateChanged += (s) => gl.Viewport(_window.FramebufferSize);

        _window.Run();
    }

    private static void OnLoad()
    {
        gl = _window.CreateOpenGL();
        input = _window.CreateInput();

        _window.Center();
        _window.WindowState = WindowState.Maximized;

        _imgui = new ImGuiController(gl, _window, input);
        
        var io = ImGui.GetIO();
        unsafe
        {
            io.ConfigFlags            |= ImGuiConfigFlags.DockingEnable;
            io.ConfigFlags            |= ImGuiConfigFlags.ViewportsEnable;
            io.NativePtr->IniFilename =  null;
        }
        
        ImGui.LoadIniSettingsFromDisk(!File.Exists(IMGUI_SAVE) ? IMGUI_SAVE_DEFAULT : IMGUI_SAVE);
        _system = new VirtualSystem();
    }
    private static void OnClose()
    {
        ImGui.SaveIniSettingsToDisk(IMGUI_SAVE);
    }

    private static void OnUpdate(double delta)
    {
        _system.Process(delta);
        if (ImGui.GetIO().WantSaveIniSettings) ImGui.SaveIniSettingsToDisk(IMGUI_SAVE);
    }
    private static void OnRender(double delta)
    {
        _system.Draw();
        
        if (_window.WindowState == WindowState.Minimized) return;

        _imgui.Update((float)delta);

        gl.ClearColor(1f, 1f, 1f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit);

        var imGuiViewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(imGuiViewport.WorkPos);
        ImGui.SetNextWindowSize(imGuiViewport.WorkSize);
        ImGui.SetNextWindowViewport(imGuiViewport.ID);

        const ImGuiWindowFlags dockSpaceFlags =
            ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus;

        ImGui.Begin("SharpNES", dockSpaceFlags);

        if (_fileDialog.IsOpen) _fileDialog.Draw();
        
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Open..."))
                    _fileDialog.Open("./", [".nes"], path => _system.InsertCartriadge(RomReader.LoadFromPath(path)));

                if (ImGui.MenuItem("Save", "Ctrl+S"))
                {
                    // Salvar
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Exit"))
                {
                    // Sair
                }

                ImGui.EndMenu();
            }
            
            // if (ImGui.BeginMenu("View"))
            // {
            //     ImGui.MenuItem("Scene");
            //     ImGui.MenuItem("Inspector");
            //     ImGui.MenuItem("Console");
            //
            //     ImGui.EndMenu();
            // }
            ImGui.EndMainMenuBar();
        }
        
        ImGui.DockSpace(ImGui.GetID("MainDockSpace"), Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

        foreach (var (_, enabled, callee) in WindowViews) if (enabled) callee.Invoke();

        ImGui.End();

        _imgui.Render();
    }
    
}
