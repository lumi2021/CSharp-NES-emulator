using Emulator.Components.Core;
using ImGuiNET;
using Silk.NET.OpenGL;
using System.Numerics;

namespace Emulator.Components;

public class Ppu : Component
{
    
    private readonly byte[] _vramNametable0 = new byte[960 + 64];
    private readonly byte[] _vramNametable1 = new byte[960 + 64];
    private readonly byte[] _vramPallete = new byte[32];
    private readonly byte[,,] _vramChr = new byte[16 * 32, 8, 8];
    private byte[] _vramOam = new byte[256];

    private int _cycleCounter = 0;
    private int _scanlineCounter = 0;
    private bool _nmiOccurred = false;
    
    public bool VBlankNmInterrupt = false;
    public bool IsMaster = false;
    public byte SpriteHeight = 8;
    public byte BackgroundPatternTable = 0;
    public byte SpritePatternTable = 0;
    public byte IncrementPerRead = 1;

    private byte _ppumask = 0;
    private byte _ppustat = 0;
    private byte _oamaddr = 0;
    private byte _oamdata = 0;

    private ushort _vramAddr = 0;

    private int _nmtbX = 0;
    private int _nmtbY = 0;
    private int _scrollX = 0;
    private int _scrollY = 0;

    private byte _oamDma = 0;

    private int _regx = 0;
    private ushort _regt = 0;
    private bool _regw = false;

    private uint _backgroundTexHandler = 0;
    private uint _foregroundTexHandler = 0;
    
    public bool OnVblank
    {
        get => (_ppustat & 0b_1000_0000) != 0;
        set => _ppustat = (byte)((_ppustat & ~0b_1000_0000) | (value ? 0b_1000_0000 : 0));
    }

    #region SilkNet shit
    private int _texWrapMode = (int)TextureWrapMode.Repeat;
    private int _texMinFilter = (int)TextureMinFilter.Nearest;
    private int _texMagFilter = (int)TextureMagFilter.Nearest;
    #endregion

    private (byte r, byte g, byte b)[] palletes = [
        (0x80, 0x80, 0x80), (0x00, 0x3D, 0xA6), (0x00, 0x12, 0xB0), (0x44, 0x00, 0x96), (0xA1, 0x00, 0x5E),
        (0xC7, 0x00, 0x28), (0xBA, 0x06, 0x00), (0x8C, 0x17, 0x00), (0x5C, 0x2F, 0x00), (0x10, 0x45, 0x00),
        (0x05, 0x4A, 0x00), (0x00, 0x47, 0x2E), (0x00, 0x41, 0x66), (0x00, 0x00, 0x00), (0x05, 0x05, 0x05),
        (0x05, 0x05, 0x05), (0xC7, 0xC7, 0xC7), (0x00, 0x77, 0xFF), (0x21, 0x55, 0xFF), (0x82, 0x37, 0xFA),
        (0xEB, 0x2F, 0xB5), (0xFF, 0x29, 0x50), (0xFF, 0x22, 0x00), (0xD6, 0x32, 0x00), (0xC4, 0x62, 0x00),
        (0x35, 0x80, 0x00), (0x05, 0x8F, 0x00), (0x00, 0x8A, 0x55), (0x00, 0x99, 0xCC), (0x21, 0x21, 0x21),
        (0x09, 0x09, 0x09), (0x09, 0x09, 0x09), (0xFF, 0xFF, 0xFF), (0x0F, 0xD7, 0xFF), (0x69, 0xA2, 0xFF),
        (0xD4, 0x80, 0xFF), (0xFF, 0x45, 0xF3), (0xFF, 0x61, 0x8B), (0xFF, 0x88, 0x33), (0xFF, 0x9C, 0x12),
        (0xFA, 0xBC, 0x20), (0x9F, 0xE3, 0x0E), (0x2B, 0xF0, 0x35), (0x0C, 0xF0, 0xA4), (0x05, 0xFB, 0xFF),
        (0x5E, 0x5E, 0x5E), (0x0D, 0x0D, 0x0D), (0x0D, 0x0D, 0x0D), (0xFF, 0xFF, 0xFF), (0xA6, 0xFC, 0xFF),
        (0xB3, 0xEC, 0xFF), (0xDA, 0xAB, 0xEB), (0xFF, 0xA8, 0xF9), (0xFF, 0xAB, 0xB3), (0xFF, 0xD2, 0xB0),
        (0xFF, 0xEF, 0xA6), (0xFF, 0xF7, 0x9C), (0xD7, 0xE8, 0x95), (0xA6, 0xED, 0xAF), (0xA2, 0xF2, 0xDA),
        (0x99, 0xFF, 0xFC), (0xDD, 0xDD, 0xDD), (0x11, 0x11, 0x11), (0x11, 0x11, 0x11)
    ];

    private void WritePpuCtrl(byte value)
    {
        VBlankNmInterrupt = (value & 0b_1000_0000) != 0;
        IsMaster = (value & 0b_0100_0000) == 0;
        SpriteHeight = (byte)(((value & 0b_0010_0000) == 0) ? 8 : 16);
        BackgroundPatternTable = (byte)(((value & 0b_0001_0000) == 0) ? 0 : 1);
        SpritePatternTable = (byte)(((value & 0b_0000_1000) == 0) ? 0 : 1);
        IncrementPerRead = (byte)(((value & 0b_0000_0100) == 0) ? 1 : 32);

        _regt = (ushort)((_regt & 0b_111_00_11_111_11111) | ((value & 0b_0000_0011) << 10));
    }
    private void WritePpuScroll(byte value)
    {
        if (!_regw)
        {
            var coarseX = value >> 3;
            var fineX = value & 0b111;

            _regx = fineX;
            _regt = (ushort)((_regt & ~0b0000000000011111) | coarseX);
        }
        else
        {
            var coarseY = value >> 3;
            var fineY = value & 0b111;

            _regt &= 0b0000110000011111;
            _regt |= (ushort)((coarseY & 0b_11111) << 5);
            _regt |= (ushort)((fineY & 0b_111) << 12);

            UpdateScrollFromRegt();
        }
        
        _regw = !_regw;
    }
    private void WritePpuAddress(byte value)
    {
        if (!_regw)
            _regt = (ushort)((_regt & 0b_00_000000_11111111) | ((value & 0b_00_111111) << 8));
        
        else
        {
            _regt = (ushort)((_regt & 0b_00_111111_00000000) | value);
            _vramAddr = _regt;
        }

        _regw = !_regw;
    }
    private void WritePpuData(byte value)
    {
        var addr = _vramAddr;
        _vramAddr += IncrementPerRead;
        
        switch (addr)
        {
            case >= 0x2000 and < 0x3000: WriteWithNametableMirroring(addr, value); break;
            
            case >= 0x3F00 and <= 0x3F1F: _vramPallete[addr - 0x3F00] = value; break;
            case >= 0x3F20 and <= 0x3FFF: _vramPallete[(addr - 0x3F00) % 0x20] = value; break;
            
            //default: throw new ArgumentOutOfRangeException();
        }

        _regw = false;
        _updateNametablesSheet = true;
    }
    private byte ReadPpuData()
    {
        var addr = _vramAddr;
        _vramAddr += IncrementPerRead;
        
        return addr switch
        {
            < 0x2000 => system.Rom.RomData.ChrData[addr],
            < 0x3000 => ReadWithNametableMirroring(addr),
            
            >= 0x3F00 and < 0x3F20 => _vramPallete[addr - 0x3F00],
            >= 0x3F20 and <= 0x3FFF => _vramPallete[(addr - 0x3F00) % 0x20],
            
            _ => 0
        };
    }
    

    private byte ReadWithNametableMirroring(ushort addr)
    {
        var (a, b) = ProcessNametableMirroring(addr);
        return a ? _vramNametable0[b] : _vramNametable1[b];
    }
    private void WriteWithNametableMirroring(ushort addr, byte value)
    {
        var (a, b) = ProcessNametableMirroring(addr);
        if(a) _vramNametable0[b] = value;
        else  _vramNametable1[b] = value;
    }
    private (byte tile, byte paletteIndex) GetTile(int table, int posx, int posy)
    {
        var tileLinearAddr = (ushort)(0x2000 + table * 0x400 + posy * 32 + posx);
        byte tile = ReadWithNametableMirroring(tileLinearAddr);
        
        var attrBase = (ushort)(0x2000 + table * 0x400 + 0x3C0);
        int attrX = posx >> 2; // 0..7
        int attrY = posy >> 2; // 0..7
        int attrOffset = attrY * 8 + attrX;
        var attrAddr = (ushort)(attrBase + attrOffset);

        byte attr = ReadWithNametableMirroring(attrAddr);
        
        int txInAttr = posx & 0b11; // 0..3
        int tyInAttr = posy & 0b11; // 0..3
        int quadrant = (txInAttr >= 2 ? 1 : 0) + (tyInAttr >= 2 ? 2 : 0); // 0..3

        byte paletteIndex = (byte)((attr >> (quadrant * 2)) & 0b11); // 0..3

        return (tile, paletteIndex);
    }
    private (bool nmtb, ushort addr) ProcessNametableMirroring(ushort addr)
    {
        int rAddr = addr;
        bool nmtb = false;

        // solve odd mirroring addresses
        //if (gAddr < 0x2000) gAddr += 0x2000;

        // +---+---+
        // | 0 | 1 | 0x2000 0x2400
        // +---+---+
        // | 2 | 3 | 0x2800 0x2C00
        // +---+---+
        // 0x3F00 - 0x3F1F | 0x3F20 .. 0x3FFF

        if (rAddr is < 0x2000 or >= 0x3000) return (nmtb, (ushort)rAddr);
        
        var nametableIndex = 0;
            
        switch (rAddr)
        {
            case < 0x2400:
                nametableIndex = 0;
                rAddr -= 0x2000;
                break;
                
            case < 0x2800:
                nametableIndex = 1;
                rAddr -= 0x2400;
                break;
                
            case < 0x2C00:
                nametableIndex = 2;
                rAddr -= 0x2800;
                break;
                
            case < 0x3000:
                nametableIndex = 3;
                rAddr -= 0x2C00;
                break;
        }

        var mirroring = system.Rom.RomData.NametableArrangement;
        nmtb = mirroring == NametableArrangement.Vertical
            ? nametableIndex % 2 == 0
            : nametableIndex > 1;

        return (nmtb, (ushort)rAddr);
    }
    
    private (byte r, byte g, byte b) GetColorFromBgPalette(int paletteIndex, int colorIndex)
    {
        int offset = (paletteIndex * 4) + colorIndex; // 0..15
        offset &= 0x1F;
        return GetPal(_vramPallete[offset]);
    }
    private (byte r, byte g, byte b) GetColorFromSpritePalette(int paletteIndex, int colorIndex)
    {
        int offset = 0x10 + paletteIndex * 4 + colorIndex; // 0x10..0x1F
        offset &= 0x1F;
        return GetPal(_vramPallete[offset]);
    }
    
    private void CopyOamData(byte hAddr) => _vramOam = system.Ram.CopyPage(hAddr);

    public Ppu(VirtualSystem mb) : base(mb)
    {
        Program.DrawPopup += RenderGame;
        Program.DrawPopup += DebugPpu;
        Program.DrawPopup += DebugVram;

        CreateSpriteSheets();
    }

    public void ResetRomData()
    {
        DecodeSpriteSheets();
        UpdateSpriteSheet();
    }

    public void Tick()
    {
        UpdateForeground();
        if (_updateNametablesSheet) UpdateBackground();
        if (VBlankNmInterrupt)
        {
            system.Cpu.RequestNmInterrupt();
            VBlankNmInterrupt = false;
        }
    }
    
    private void UpdateBackground()
    {
        byte[] buf = new byte[512 * 480 * 3];

        for (var nametable = 0; nametable < 4; nametable++)
        {
            for (var tx = 0; tx < 32; tx++)
            {
                for (var ty = 0; ty < 30; ty++)
                {
                    var (tile, palIdx) = GetTile(nametable, tx, ty);
                    
                    for (var py = 0; py < 8; py++) for (var px = 0; px < 8; px++)
                    {
                        int pixelValue = _vramChr[BackgroundPatternTable * 256 + tile, px, py]; // 0..3
                            
                        var tbx = (nametable % 2) != 0 ? 32 : 0;
                        var tby = nametable >= 2 ? 30 : 0;

                        var x = (tbx + tx) * 8 + px;
                        var y = (tby + ty) * 8 + py;
                        var pixelIndex = (y * (64 * 8) + x) * 3;
                        
                        if (pixelValue == 0)
                        {
                            var c = GetPal(_vramPallete[0]);
                            buf[pixelIndex + 0] = c.r;
                            buf[pixelIndex + 1] = c.g;
                            buf[pixelIndex + 2] = c.b;
                        }
                        else
                        {
                            var c = GetColorFromBgPalette(palIdx, pixelValue);
                            buf[pixelIndex + 0] = c.r;
                            buf[pixelIndex + 1] = c.g;
                            buf[pixelIndex + 2] = c.b;
                        }
                    }
                }
            }
        }

        var gl = Program.gl;
        gl.BindTexture(TextureTarget.Texture2D, _backgroundTexHandler);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgb, 64 * 8, 60 * 8, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, buf);
    }
    private void UpdateForeground()
    {
        var buf = new byte[256 * 240 * 4]; // RGBA

        for (var sprite = 0; sprite < 64; sprite++)
        {
            byte y = _vramOam[sprite * 4 + 0];
            byte tileIndex = _vramOam[sprite * 4 + 1];
            byte attributes = _vramOam[sprite * 4 + 2];
            byte x = _vramOam[sprite * 4 + 3];

            int spriteY = y + 1;
            int spriteX = x;
            
            if (y == 0 || spriteY >= 240) continue;

            bool flipX = (attributes & 0b0100_0000) != 0;
            bool flipY = (attributes & 0b1000_0000) != 0;
            int palette = attributes & 0b11;

            int height = (SpriteHeight == 16) ? 16 : 8;


            for (int py = 0; py < height; py++)
            {
                int sy = spriteY + py;
                if (sy < 0 || sy >= 240) continue;

                for (int px = 0; px < 8; px++)
                {
                    int sx = spriteX + px;
                    if (sx < 0 || sx >= 256) continue;

                    int tx = flipX ? 7 - px : px;
                    int ty = flipY ? ((height == 16) ? (py < 8 ? 7 - py : 7 - (py - 8)) : 7 - py) : (py % 8);

                    int usedTile;
                    if (height == 8)
                    {
                        usedTile = tileIndex + (SpritePatternTable == 0 ? 0 : 256);
                    }
                    else
                    {
                        int top = tileIndex & 0xFE;
                        if (py < 8)
                            usedTile = top + ( (tileIndex & 1) == 1 ? 256 : 0 );
                        else
                            usedTile = top + 1 + ( (tileIndex & 1) == 1 ? 256 : 0 );
                    }

                    int pixelValue = _vramChr[usedTile, tx, ty];
                    if (pixelValue == 0) continue;
                    
                    var color = GetColorFromSpritePalette(palette, pixelValue);

                    int idx = (sx + sy * 256) * 4;
                    buf[idx + 0] = color.r;
                    buf[idx + 1] = color.g;
                    buf[idx + 2] = color.b;
                    buf[idx + 3] = 255;
                }
            }
        }

        var gl = Program.gl;
        gl.BindTexture(TextureTarget.Texture2D, _foregroundTexHandler);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 256, 240, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, buf);
    }
    private void UpdateScrollFromRegt()
    {
        // v register layout:
        // bits 0-4   = coarse X (0..31)
        // bits 5-9   = coarse Y (0..29)
        // bit 10     = nametable X (0 or 1)
        // bit 11     = nametable Y (0 or 1)
        // bits 12-14 = fine Y (0..7)
        // fine X is stored separately in _regx

        var coarseX = _regt & 0x1F;                 // bits 0..4
        var coarseY = (_regt >> 5) & 0x1F;          // bits 5..9
        var nametableX = (_regt >> 10) & 0x1;       // bit 10
        var nametableY = (_regt >> 11) & 0x1;       // bit 11
        var fineY = (_regt >> 12) & 0x7;            // bits 12..14
        var fineX = _regx & 0x7;
        
        
        // each nametable is 32x30 tiles (256x240 pixels)
        _scrollX = coarseX * 8 + fineX;   // 0..511
        _scrollY = coarseY * 8 + fineY;   // 0..479
        _nmtbX = nametableX;
        _nmtbY = nametableY;

        if (_scrollY >= 200)
        {
            
        }
        
        //Console.WriteLine($"Scroll update: {_scrollX:D3} {_nmtbX} {_scrollY:D3} {_nmtbY}");
    }
    
    public byte ReadRegister(ushort addr)
    {
        int regIndex = (addr - 0x2000) % 8;
        switch (regIndex) {
        
            case 2:
                _regw = false;
                
                var temps = _ppustat;
                if (_nmiOccurred) temps |= 0x80;
                _nmiOccurred = false;
                return temps;
            
            
            case 4:
                return _oamdata;
            
            case 7:
                return ReadPpuData();

            default:
                Console.WriteLine($"PPU Register {regIndex} is not readable!");
                break;

        };

        return 0;
    }
    public void WriteRegister(ushort addr, byte data)
    {
        if (addr == 0x4014)
        {
            CopyOamData(data);
            return;
        }

        var regIndex = (addr - 0x2000) % 8;
        switch (regIndex)
        {
            case 0: WritePpuCtrl(data); break;
            case 1: _ppumask = data; break;
            case 3: _oamaddr = data; break;
            case 4: _oamdata = data; break;
            case 5: WritePpuScroll(data); break;
            case 6: WritePpuAddress(data); break;
            case 7: WritePpuData(data); break;

            default:
                Console.WriteLine($"PPU Register {regIndex} is not writeable!");
                break;

        };
    }

    // debug shit
    private uint _spriteSheetHandlerLeft = 0;
    private uint _spriteSheetHandlerRight = 0;
    private byte _viewingSheet = 0;
    private int _palleteIndexA = 1;
    private int _palleteIndexB = 17;
    private int _palleteIndexC = 2;
    private bool _showNametablesAttributeTable = false;
    private bool _updateNametablesSheet = false;

    private void DebugPpu()
    {
        ImGui.Begin("PPU Debug");
        {
            ImGui.SeparatorText("Control:");

            if (VBlankNmInterrupt) ImGui.Text("NMI enabled"); else ImGui.TextDisabled("NMI Disabled"); ImGui.SameLine();
            if (OnVblank) ImGui.Text("VBlank"); else ImGui.TextDisabled("VBlank"); ImGui.SameLine();
            if (_nmiOccurred) ImGui.Text("Occurred"); else ImGui.TextDisabled("Occurred");
            if (IsMaster) ImGui.Text("PPU Master"); else ImGui.TextDisabled("PPU Slave");

            ImGui.TextDisabled("Sprite size:"); ImGui.SameLine();
            ImGui.Text(SpriteHeight == 16 ? "8x16" : "8x8");

            ImGui.TextDisabled("FG sheet:"); ImGui.SameLine();
            ImGui.Text(SpritePatternTable == 1 ? "Right" : "Left");
            ImGui.TextDisabled("BG sheet:"); ImGui.SameLine();
            ImGui.Text(BackgroundPatternTable == 1 ? "Right" : "Left");


            ImGui.TextDisabled("VRAM inc:"); ImGui.SameLine();
            ImGui.Text($"x{IncrementPerRead}");

            ImGui.SeparatorText("Flags:");
            ImGui.Text($"{_ppustat:b8}");

            ImGui.SeparatorText("Data:");
            ImGui.TextDisabled("Temp Addr:"); ImGui.SameLine();
            ImGui.Text($"${_regt:X4} ({_regt:b16})");
            ImGui.TextDisabled("Data Addr:"); ImGui.SameLine();
            ImGui.Text($"${_vramAddr:X4} ({_vramAddr:b16})");

            ImGui.SeparatorText("Scroll:");

            if (ImGui.BeginTable("CPU controls", 2))
            {

                ImGui.TableNextColumn();
                ImGui.TextDisabled("X scroll:"); ImGui.SameLine();
                ImGui.Text($"{_scrollX}");
                
                ImGui.TableNextColumn();
                ImGui.TextDisabled("X table:"); ImGui.SameLine();
                ImGui.Text($"{_nmtbX}");
                
                ImGui.TableNextColumn();
                ImGui.TextDisabled("Y scroll:"); ImGui.SameLine();
                ImGui.Text($"{_scrollY}");
                
                ImGui.TableNextColumn();
                ImGui.TextDisabled("Y table:"); ImGui.SameLine();
                ImGui.Text($"{_nmtbY}");
                
                ImGui.EndTable();
            }

            
            ImGui.SeparatorText("Internal:");
            
            ImGui.TextDisabled("Cycle Counter:"); ImGui.SameLine();
            ImGui.Text($"{_cycleCounter}");
            
            ImGui.TextDisabled("Scanline:"); ImGui.SameLine();
            ImGui.Text($"{_scanlineCounter}");
        }
        ImGui.End();

        ImGui.Begin("Sprite Sheet View", ImGuiWindowFlags.AlwaysAutoResize);
        {
            ImGui.Image((nint)(_viewingSheet == 0 ? _spriteSheetHandlerLeft : _spriteSheetHandlerRight), new(700, 700));

            if (ImGui.Button("Left Sheet")) _viewingSheet = 0;
            ImGui.SameLine();
            if (ImGui.Button("Right Sheet")) _viewingSheet = 1;

            ImGui.Text("Pallete:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("A", ref _palleteIndexA, 1, 1, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (_palleteIndexA > 64) _palleteIndexA -= 64;
                if (_palleteIndexA < 1) _palleteIndexA += 64;

                UpdateSpriteSheet();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("B", ref _palleteIndexB, 1, 1, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (_palleteIndexB > 64) _palleteIndexB -= 64;
                if (_palleteIndexB < 1) _palleteIndexB += 64;

                UpdateSpriteSheet();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("C", ref _palleteIndexC, 1, 1, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (_palleteIndexC > 64) _palleteIndexC -= 64;
                if (_palleteIndexC < 1) _palleteIndexC += 64;

                UpdateSpriteSheet();
            }

        }
        ImGui.End();
    }
    private void DebugVram()
    {
        ImGui.Begin("VRAM View", ImGuiWindowFlags.AlwaysAutoResize);
        {
            ImDrawListPtr drawList = ImGui.GetWindowDrawList(); ;

            ImGui.SeparatorText("Pattern Tables:");

            ImGui.Image((nint)_spriteSheetHandlerLeft, new(300, 300)); ImGui.SameLine();
            ImGui.Image((nint)_spriteSheetHandlerRight, new(300, 300));

            ImGui.SeparatorText("Nametables:");

            float maxW = ImGui.GetContentRegionAvail().X;
            Vector2 renderRes = new(256, 240);

            float imageAspectRatio = renderRes.X / renderRes.Y;
            Vector2 finalSize = new(maxW, maxW / imageAspectRatio);
            float scale = finalSize.X / renderRes.X / 2;

            var cp = ImGui.GetCursorScreenPos();

            ImGui.Image((nint)_backgroundTexHandler, finalSize);

            int camX = _scrollX + _nmtbX * 256;
            int camY = _scrollY + _nmtbY * 240;
            
            drawList.AddRect(
                cp + (new Vector2(camX * scale, camY * scale)),
                cp + (new Vector2((camX + 256) * scale, (camY + 240) * scale)),
                ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 1f))
            );

            if (ImGui.Checkbox("Show attributes", ref _showNametablesAttributeTable)) _updateNametablesSheet = true;
            
            for (var i = 0; i < 30; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:"); ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable0[(i * 32) .. ((i+1) * 32)].Select(e => $"{e:X2}")));
            }
            ImGui.NewLine();
            for (var i = 30; i < 32; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:"); ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable0[(i * 32)..((i + 1) * 32)].Select(e => $"{e:X2}")));
            }
            ImGui.NewLine();
            for (var i = 0; i < 30; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:"); ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable1[(i * 32)..((i + 1) * 32)].Select(e => $"{e:X2}")));
            }
            ImGui.NewLine();
            for (var i = 30; i < 32; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:"); ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable1[(i * 32)..((i + 1) * 32)].Select(e => $"{e:X2}")));
            }
            
            ImGui.SeparatorText("Sprites");
            
            ImGui.Image((nint)_foregroundTexHandler, finalSize);
            
            for (var i = 0; i < 16; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 4:X3}:"); ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramOam[(4*i) .. (4*i+4)].Select(e => $"{e:X2}")));
            }

            ImGui.SeparatorText("Palletes:");

            ImGui.TextDisabled("Background:");

            var cursorPos = ImGui.GetCursorScreenPos();
            var cpx = cursorPos.X;
            var cpy = cursorPos.Y;

            for (var i = 0; i < 16; i++)
            {
                var c = GetPal(_vramPallete[i]);
                var color = ImGui.GetColorU32(new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f));

                var posA = new Vector2(cpx + (33 * i), cpy);
                var posB = new Vector2(cpx + (33 * (i + 1) - 1), cpy + 32);
                drawList.AddRectFilled(posA, posB, color);
            }
            ImGui.Dummy(new(32 * 16, 32));

            ImGui.TextDisabled($"{0x00:X2}:"); ImGui.SameLine();
            ImGui.Text(string.Join(" ", _vramPallete[..0x10].Select(e => $"{e:X2}")));

            ImGui.TextDisabled("Sprites:");

            cursorPos = ImGui.GetCursorScreenPos();
            cpx = cursorPos.X;
            cpy = cursorPos.Y;

            for (var i = 0; i < 16; i++)
            {
                var c = GetPal(_vramPallete[0x10 + i]);
                var color = ImGui.GetColorU32(new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f));

                var posA = new Vector2(cpx + (33 * i), cpy);
                var posB = new Vector2(cpx + (33 * (i+1) - 1), cpy + 32);

                drawList.AddRectFilled(posA, posB, color, 0);
            }
            ImGui.Dummy(new(32 * 16, 32));

            ImGui.TextDisabled($"{0x10:X2}:"); ImGui.SameLine();
            ImGui.Text(string.Join(" ", _vramPallete[0x10..0x20].Select(e => $"{e:X2}")));
        }
        ImGui.End();
    }
    private void RenderGame()
    {
        ImGui.Begin("Video Out");
        var drawList = ImGui.GetWindowDrawList();

        Vector2 viewSize = ImGui.GetContentRegionAvail();
        Vector2 cursorPos = ImGui.GetCursorScreenPos();
        Vector2 renderRes = new(256, 240);

        float canvasAspectRatio = viewSize.X / viewSize.Y;
        float imageAspectRatio = renderRes.X / renderRes.Y;

        Vector2 finalSize = (canvasAspectRatio < imageAspectRatio)
            ? new Vector2(viewSize.X, viewSize.X / imageAspectRatio)
            : new Vector2(viewSize.Y * imageAspectRatio, viewSize.Y);

        int camX = _scrollX + _nmtbX * 256;
        int camY = _scrollY + _nmtbY * 240;
        
        Vector2 bgSize = new(512, 480); // 2x nametables
        Vector2 scrollUV = new Vector2(camX % 512, camY % 480) / bgSize;
        
        Vector2 uv0 = scrollUV;
        Vector2 uv1 = scrollUV + renderRes / bgSize;
        
        drawList.AddImage(
            (nint)_backgroundTexHandler,
            cursorPos,
            cursorPos + finalSize,
            uv0, uv1);

        drawList.AddImage(
            (nint)_foregroundTexHandler,
            cursorPos,
            cursorPos + finalSize,
            new Vector2(0, 0),
            new Vector2(1, 1));
        
        ImGui.End();
    }

    private void CreateSpriteSheets()
    {
        var gl = Program.gl;
        _spriteSheetHandlerLeft = gl.GenTexture();
        _spriteSheetHandlerRight = gl.GenTexture();
        _backgroundTexHandler = gl.GenTexture();
        _foregroundTexHandler = gl.GenTexture();

        gl.BindTexture(TextureTarget.Texture2D, _spriteSheetHandlerLeft);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapS, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapT, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, in _texMinFilter);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, in _texMagFilter);

        gl.BindTexture(TextureTarget.Texture2D, _spriteSheetHandlerRight);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapS, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapT, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, in _texMinFilter);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, in _texMagFilter);

        gl.BindTexture(TextureTarget.Texture2D, _backgroundTexHandler);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapS, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapT, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, in _texMinFilter);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, in _texMagFilter);
        
        gl.BindTexture(TextureTarget.Texture2D, _foregroundTexHandler);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapS, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapT, in _texWrapMode);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, in _texMinFilter);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, in _texMagFilter);
    }
    private void UpdateSpriteSheet()
    {
        var imageData = GetSpritesRgbData();
        var imageDataLeft = imageData.AsSpan(0, imageData.Length / 2);
        var imageDataRIght = imageData.AsSpan(imageData.Length / 2, imageData.Length / 2);

        var gl = Program.gl;
        gl.BindTexture(TextureTarget.Texture2D, _spriteSheetHandlerLeft);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 128, 128, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, imageDataLeft);

        gl.BindTexture(TextureTarget.Texture2D, _spriteSheetHandlerRight);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 128, 128, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, imageDataRIght);
    }
    
    private void DecodeSpriteSheets()
    {
        var chr = system.Rom.RomData.ChrData;
        var tiles = chr.Length / 16;
        
        for (var t = 0; t < tiles && t < (16 * 32); t++)
        {
            for (var row = 0; row < 8; row++)
            {
                var b0 = chr[t * 16 + row];       // bitplane 0
                var b1 = chr[t * 16 + 8 + row];   // bitplane 1
                
                for (var col = 0; col < 8; col++)
                {
                    var bit = 7 - col;
                    var pv = ((b0 >> bit) & 1) | (((b1 >> bit) & 1) << 1);
                    _vramChr[t, col, row] = (byte)pv;
                }
            }
        }
    }
    private byte[] GetSpritesRgbData()
    {
        var imageData = new byte[16 * 32 * 8 * 8 * 3];

        for (var tx = 0; tx < 16; tx++)
        {
            for (var ty = 0; ty < 32; ty++)
            {
                for (var px = 0; px < 8; px++)
                {
                    for (var py = 0; py < 8; py++)
                    {

                        var pixelValue = _vramChr[tx + ty * 16, px, py];

                        (int r, int g, int b) = pixelValue switch
                        {
                            1 => GetPal(_palleteIndexA - 1),
                            2 => GetPal(_palleteIndexB - 1),
                            3 => GetPal(_palleteIndexC - 1),

                            _ => (0, 0, 0)
                        };

                        var pixelIndex = ((tx * 8) + px + (ty * 16 * 8 * 8) + (py * 16 * 8)) * 3;

                        imageData[pixelIndex + 0] = (byte)r;
                        imageData[pixelIndex + 1] = (byte)g;
                        imageData[pixelIndex + 2] = (byte)b;

                    }
                }

            }
        }

        return imageData;
    }
    
    private (byte r, byte g, byte b) GetPal(int index) => palletes[index & 0b00_111111];
}
