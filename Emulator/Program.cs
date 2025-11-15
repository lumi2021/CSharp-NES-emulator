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

    public delegate void PopupDrawingDelegate();
    public static event PopupDrawingDelegate DrawPopup = null!;

    public static void Main() {

        //_system = new VirtualSystem();

        //RunTests();
        //return;

        WindowOptions options = WindowOptions.Default with
        {
            Size = new(1000, 500),
            Title = "Emulator"
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

        _imgui = new(gl, _window, input);
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

        ImGui.LoadIniSettingsFromDisk("imgui.ini");

        _system = new VirtualSystem();

        _system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Super Mario Bros.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Super Mario Bros 3.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Tetris.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Donkey Kong.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Donkey Kong Classics.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/snow.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Thwaite.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Pac-Man.nes"));
        //_system.InsertCartriadge(RomReader.LoadFromPath("ROMs/Ice Climber.nes"));
    }
    private static void OnClose()
    {
        ImGui.SaveIniSettingsToDisk("imgui.ini");
    }

    private static void OnUpdate(double delta) => _system.Process(delta);
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

        ImGuiWindowFlags dockSpaceFlags =
        ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;

        ImGui.Begin("Game", dockSpaceFlags);

        ImGui.DockSpace(ImGui.GetID("MainDockSpace"), Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

        DrawPopup?.Invoke();

        ImGui.End();

        _imgui.Render();
    }


    /*
    private static void RunTests()
    {
        Console.WriteLine("Initializing tests routine...");

        using HttpClient client = new();
        string baseUrl = "https://raw.githubusercontent.com/SingleStepTests/ProcessorTests/refs/heads/main/nes6502/v1/";

        bool testFailed = false;
        dynamic currentTest = null!;

        int[] toSkip = [
            0x63, 0x67,
            0x6f, 0x73, 0x77, 0x7b, 0x7f, 0x83, 0x87, 0x8b, 0x8f, 0x93, 0x97, 0x9b, 0x9f,
            0x9c, 0x9e, 0x9f, 0xa3, 0xa7, 0xab, 0xaf, 0xb3, 0xb7, 0xbb, 0xbf, 0xc3, 0xc7,
            0xcb, 0xcf, 0xd3, 0xd7, 0xdb, 0xdf, 0xe3, 0xe7, 0xeb, 0xef, 0xf3, 0xf7, 0xfb,
            0xff
        ];

        for (int opCode = 68; opCode < 256; opCode++)
        {
            if (toSkip.Contains(opCode))
            {
                Console.WriteLine($"Skipping OpCode {opCode:X2}");
                continue;
            }
            Console.WriteLine($"Testing OpCode {opCode:X2}");

            Console.WriteLine($"Requesting test...");
            var url = $"{baseUrl}{opCode:x2}.json";
            var tr = client.GetAsync(url);
            tr.Wait();
            var tr2 = tr.Result.Content.ReadAsStringAsync();
            tr2.Wait();

            var resString = tr2.Result;

            List<dynamic> tests = JsonConvert.DeserializeObject<List<object>>(resString)!;

            Console.WriteLine($"Starting {opCode:X2} tests...");

            for (int tidx = 0; tidx < tests.Count; tidx++)
            {
                var t = tests[tidx];
                currentTest = t;

                Console.Write($"{tidx + 1:d5}: {t.name} ... ");

                dynamic i = t.initial;
                dynamic f = t.final;

                _system.Cpu.progCounter = (ushort)i.pc;
                _system.Cpu.stackPointer = (byte)i.s;

                _system.Cpu.accumulator = (byte)i.a;
                _system.Cpu.indexX = (byte)i.x;
                _system.Cpu.indexY = (byte)i.y;

                _system.Cpu.flags = (byte)i.p;

                foreach (var r in i.ram)
                {
                    _system.debugMemory[r[0]] = (byte)r[1];
                }

                // run test
                _system.Cpu.doStep = true;
                _system.Cpu.Tick();

                // Verify if everything is right
                if
                (
                    _system.Cpu.progCounter == (ushort)f.pc &&
                    _system.Cpu.stackPointer == (byte)f.s &&
                    _system.Cpu.accumulator == (byte)f.a &&
                    _system.Cpu.indexX == (byte)f.x &&
                    _system.Cpu.indexY == (byte)f.y &&
                    CompareFlags(_system.Cpu.flags, (byte)f.p)
                )
                {
                    foreach (var r in f.ram)
                    {
                        if (_system.debugMemory[(ushort)r[0]] != (byte)r[1])
                            goto testFailedFailed;
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK");
                    Console.ResetColor();
                    continue;

                }
                else
                {
                    goto testFailedFailed;
                }
            }

            continue;
            testFailedFailed:
            testFailed = true;
            break;
        }

        if (testFailed)
        {

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Fail");
            Console.ResetColor();

            Console.WriteLine("\n## Initial:");
            Console.WriteLine($"PC: {(ushort)currentTest.initial.pc:X4}");
            Console.WriteLine($"SP: {(ushort)currentTest.initial.s:X2}");
            Console.WriteLine($"A:  {(ushort)currentTest.initial.a:X2}");
            Console.WriteLine($"X:  {(ushort)currentTest.initial.x:X2}");
            Console.WriteLine($"Y:  {(ushort)currentTest.initial.y:X2}");
            Console.WriteLine($"F:  {GetFlagsAsString((byte)currentTest.initial.p)}");
            Console.WriteLine("Ram:");
            foreach (var i in currentTest.initial.ram)
                Console.WriteLine($"${(ushort)i[0]:X4}: {(byte)i[1]:X2}");

            Console.WriteLine("\n## Final:");
            Console.WriteLine($"PC: {(ushort)currentTest.final.pc:X4}");
            Console.WriteLine($"SP: {(ushort)currentTest.final.s:X2}");
            Console.WriteLine($"A:  {(ushort)currentTest.final.a:X2}");
            Console.WriteLine($"X:  {(ushort)currentTest.final.x:X2}");
            Console.WriteLine($"Y:  {(ushort)currentTest.final.y:X2}");
            Console.WriteLine($"F:  {GetFlagsAsString((byte)currentTest.final.p)}");
            Console.WriteLine("Ram:");
            foreach (var i in currentTest.final.ram)
                Console.WriteLine($"${(ushort)i[0]:X4}: {(byte)i[1]:X2}");

            Console.WriteLine("\n##Results:");
            if (_system.Cpu.progCounter != (ushort)currentTest.final.pc) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"PC: {_system.Cpu.progCounter:X4} ({(ushort)currentTest.final.pc:X4})");

            if (_system.Cpu.stackPointer != (ushort)currentTest.final.s) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"SP: {_system.Cpu.stackPointer:X2} ({(ushort)currentTest.final.s:X2})");

            if (_system.Cpu.accumulator != (ushort)currentTest.final.a) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"A:  {_system.Cpu.accumulator:X2} ({(ushort)currentTest.final.a:X2})");

            if (_system.Cpu.indexX != (ushort)currentTest.final.x) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"X:  {_system.Cpu.indexX:X2} ({(ushort)currentTest.final.x:X2})");

            if (_system.Cpu.indexY != (ushort)currentTest.final.y) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Y:  {_system.Cpu.indexY:X2} ({(ushort)currentTest.final.y:X2})");

            if (!CompareFlags(_system.Cpu.flags, (byte)currentTest.final.p)) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"F:  {GetFlagsAsString(_system.Cpu.flags)} ({GetFlagsAsString((byte)currentTest.final.p)})");

            Console.WriteLine("Ram:");
            foreach (var i in currentTest.final.ram)
            {
                if (_system.debugMemory[(ushort)i[0]] != (byte)i[1]) Console.ForegroundColor = ConsoleColor.Red;
                else Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"${(ushort)i[0]:X4}: {_system.debugMemory[(ushort)i[0]]:X2} ({(byte)i[1]:X2})");
            }

            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("All tests passed! yupiiii!");
            Console.ResetColor();
        }
    }
    */

    private static bool CompareFlags(byte a, byte b) => (a & 0b_11001111) == (b & 0b_11001111);
    private static string GetFlagsAsString(byte v)
    {
        v = (byte)(v & 0b_11001111);

        string baseFlags = "NV--DIZC";
        var sb = new StringBuilder();

        for (var i = 0; i < 8; i++)
            sb.Append((((v & (1 << (7 - i))) != 0) ? "\x1b[97m" : "\x1b[90m") + baseFlags[i]);

        sb.Append("\x1b[0m");
        sb.Append($" ({v:b8})");
        return sb.ToString();
    }
}
