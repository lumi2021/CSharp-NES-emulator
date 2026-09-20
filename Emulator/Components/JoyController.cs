using Emulator.Components.Core;
using ImGuiNET;
using Silk.NET.GLFW;
using Silk.NET.Input;
using System.Numerics;

namespace Emulator.Components;

public class JoyController : Component
{

    public readonly int joyId;
    private readonly IInputContext inputContext;

    private Dictionary<string, bool> _inputMap = [];
    private readonly string[] _bitOrder = ["A", "B", "Select", "Start", "Up", "Down", "Left", "Right"];

    public int readingIndex = 0;
    private JoyControllerMode _mode = JoyControllerMode.Read;

    public byte InputBitRegister
    {
        get
        {
            if (readingIndex > 7) readingIndex = 0;
            return (byte)(_inputMap[_bitOrder[readingIndex++]] ? 1 : 0);
        }
    }
    public byte ControlRegister => 0b_0000_0001;
    public JoyControllerMode Mode
    {
        get => _mode;
        set
        {
            readingIndex = 0;
            _mode = value;
        }
    }

    public JoyController(int joyId, VirtualSystem sys, IInputContext inputCtx) : base(sys)
    {
        this.joyId = joyId;
        inputContext = inputCtx;
        SetupInputMap();
        
        Program.WindowViews.Add(("Joy1 View", true, DebugController));
    }

    private void SetupInputMap()
    {
        _inputMap.Add("Left", false);
        _inputMap.Add("Right", false);
        _inputMap.Add("Up", false);
        _inputMap.Add("Down", false);
        _inputMap.Add("Start", false);
        _inputMap.Add("Select", false);
        _inputMap.Add("A", false);
        _inputMap.Add("B", false);

        foreach (var i in inputContext.Keyboards)
        {
            i.KeyDown += OnKeyDown;
            i.KeyUp += OnKeyUp;
        }
    }

    private void DebugController()
    {
        ImGui.Begin($"joy {joyId}");

        var drawList = ImGui.GetWindowDrawList();
        var cp = ImGui.GetCursorScreenPos();

        ImGui.TextDisabled("Out:"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["A"] ? new(1f, 1f, 1f, 1f) : new(.5f,.5f,.5f,1f), "A"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["B"] ? new(1f, 1f, 1f, 1f) : new(.5f,.5f,.5f,1f), "B"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["Select"] ? new(1f, 1f, 1f, 1f) : new(.5f,.5f,.5f,1f), "S"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["Start"] ? new(1f, 1f, 1f, 1f) : new(.5f,.5f,.5f,1f), "T"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["Up"] ? new(1f, 1f, 1f, 1f) : new(.5f,.5f,.5f,1f), "U"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["Down"] ? new(1f, 1f, 1f, 1f) : new(.5f,.5f,.5f,1f), "D"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["Left"] ? new(1f, 1f, 1f, 1f) : new(.5f,.5f,.5f,1f), "L"); ImGui.SameLine();
        ImGui.TextColored(_inputMap["Right"] ? new(1f, 1f, 1f, 1f) : new(.5f, .5f, .5f, 1f), "R");

        var view = ImGui.GetContentRegionAvail();
        var drawSize = new Vector2(320, 100);

        var mg = (view - drawSize) / 2;

        var pressed = ImGui.GetColorU32(new Vector4(.8f, .8f, 1f, 1f));
        var released = ImGui.GetColorU32(new Vector4(.3f, .3f, .5f, .5f));

        drawList.AddRectFilled(cp + mg + new Vector2( 0, 30), cp + mg + new Vector2( 30, 60), _inputMap["Left"] ? pressed : released, 10);
        drawList.AddRectFilled(cp + mg + new Vector2(30,  0), cp + mg + new Vector2( 60, 30), _inputMap["Up"] ? pressed : released, 10);
        drawList.AddRectFilled(cp + mg + new Vector2(60, 30), cp + mg + new Vector2( 90, 60), _inputMap["Right"] ? pressed : released, 10);
        drawList.AddRectFilled(cp + mg + new Vector2(30, 60), cp + mg + new Vector2( 60, 90), _inputMap["Down"] ? pressed : released, 10);

        drawList.AddRectFilled(cp + mg + new Vector2(120, 90), cp + mg + new Vector2(150, 100), _inputMap["Select"] ? pressed : released, 10);
        drawList.AddRectFilled(cp + mg + new Vector2(170, 90), cp + mg + new Vector2(200, 100), _inputMap["Start"] ? pressed : released, 10);

        drawList.AddCircleFilled(cp + mg + new Vector2(250, 80), 20, _inputMap["A"] ? pressed : released);
        drawList.AddCircleFilled(cp + mg + new Vector2(300, 80), 20, _inputMap["B"] ? pressed : released);

        ImGui.End();
    }


    private void OnKeyUp(IKeyboard keyboard, Key key, int arg3) => SetKey(key, false);
    private void OnKeyDown(IKeyboard keyboard, Key key, int arg3) => SetKey(key, true);

    private void SetKey(Key k, bool value)
    {
        if (ImGui.IsAnyItemActive()) return;

        switch (k)
        {
            case Key.W: _inputMap["Up"] = value; break;
            case Key.A: _inputMap["Left"] = value; break;
            case Key.S: _inputMap["Down"] = value; break;
            case Key.D: _inputMap["Right"] = value; break;

            case Key.Q: _inputMap["Start"] = value; break;
            case Key.E: _inputMap["Select"] = value; break;

            case Key.Left or Key.Up: _inputMap["A"] = value; break;
            case Key.Right or Key.Down: _inputMap["B"] = value; break;

            default: return;
        }
    }
}

public enum JoyControllerMode : byte
{
    Read,
    Write
}
