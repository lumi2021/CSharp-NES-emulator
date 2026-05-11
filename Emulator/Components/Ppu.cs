using Emulator.Components.Core;
using ImGuiNET;
using Silk.NET.OpenGL;
using System.Numerics;

namespace Emulator.Components;

public class Ppu : Component
{
    private const string _vertShader =
        """
        #version 330 core
        void main() {
            vec2 verts[3] = vec2[](
                vec2(-1.0, -1.0),
                vec2( 3.0, -1.0),
                vec2(-1.0,  3.0)
            );
            gl_Position = vec4(verts[gl_VertexID], 0.0, 1.0);
        }
        """;

    private const string _bgFragShader =
        """
        #version 330 core
        out vec4 fragColor;

        uniform usampler2D uChr;
        uniform usampler2D uNmt;
        uniform usampler2D uPalette;
        uniform usampler2D uLineScroll;
        uniform vec3 uMasterPalette[64];

        uniform int uBgTable;
        uniform int uScrollX;
        uniform int uScrollY;
        uniform int uNmtX;
        uniform int uNmtY;

        vec4 nesBgColor(int palOffset) {
            palOffset &= 0x1F;
            uint nesIdx = texelFetch(uPalette, ivec2(palOffset, 0), 0).r;
            return vec4(uMasterPalette[nesIdx & 63u], 1.0);
        }

        void main() {
            ivec2 screen = ivec2(int(gl_FragCoord.x), 239 - int(gl_FragCoord.y));
        
            uvec3 ls    = texelFetch(uLineScroll, ivec2(screen.y, 0), 0).rgb;
            int scrollX = int(ls.r) | (((int(ls.b) >> 2) & 1) << 8);
            int scrollY = int(ls.g);
            int nmtX    =  int(ls.b)       & 1;
            int nmtY    = (int(ls.b) >> 1) & 1;
            
            int wx = screen.x + scrollX + nmtX * 256;
            int wy = screen.y + scrollY + nmtY * 240;
            
            int tx   = (wx >> 3) & 63;
            int ty   = ((wy >> 3) % 60 + 60) % 60;
            int pxT  = wx & 7;
            int pyT  = wy & 7;
            
            uvec2 nmData = texelFetch(uNmt, ivec2(tx, ty), 0).rg;
            int tileIdx  = int(nmData.r);
            int palIdx   = int(nmData.g);
            
            int chrX    = uBgTable * 128 + (tileIdx % 16) * 8 + pxT;
            int chrY    = (tileIdx / 16) * 8 + pyT;
            uint pixVal = texelFetch(uChr, ivec2(chrX, chrY), 0).r;
            
            if (pixVal == 0u)
                fragColor = nesBgColor(0);
            else
                fragColor = nesBgColor(palIdx * 4 + int(pixVal));
        }
        """;

    private const string _fgFragShader =
        """
        #version 330 core
        out vec4 fragColor;

        uniform usampler2D uChr;
        uniform usampler2D uOam;
        uniform usampler2D uPalette;
        uniform vec3 uMasterPalette[64];
        uniform int uSprTable;
        uniform int uSprHeight;

        vec4 nesSprColor(int palIdx, uint pixVal) {
            int offset = (0x10 + palIdx * 4 + int(pixVal)) & 0x1F;
            uint nesIdx = texelFetch(uPalette, ivec2(offset, 0), 0).r;
            return vec4(uMasterPalette[nesIdx & 63u], 1.0);
        }

        int oamByte(int base, int offset) {
            return int(texelFetch(uOam, ivec2(base + offset, 0), 0).r);
        }

        void main() {
            ivec2 screen = ivec2(int(gl_FragCoord.x), 239 - int(gl_FragCoord.y));
            fragColor = vec4(0.0);
            
            for (int i = 63; i >= 0; i--) {
                int base   = i * 4;
                int sprY   = oamByte(base, 0) + 1; // +1 conforme hardware NES
                int tileI  = oamByte(base, 1);
                int attrs  = oamByte(base, 2);
                int sprX   = oamByte(base, 3);

                if (sprY <= 0 || sprY >= 240) continue;

                int dx = screen.x - sprX;
                int dy = screen.y - sprY;
                if (dx < 0 || dx >= 8 || dy < 0 || dy >= uSprHeight) continue;

                bool flipX  = (attrs & 0x40) != 0;
                bool flipY  = (attrs & 0x80) != 0;
                int  palIdx = attrs & 3;

                int tx = flipX ? 7 - dx : dx;

                int tyLocal;
                if (uSprHeight == 8) {
                    tyLocal = flipY ? 7 - dy : dy;
                } else {
                    bool inBottom = (dy >= 8);
                    int  halfDy   = dy % 8;
                    tyLocal = flipY ? 7 - halfDy : halfDy;

                    if (flipY) inBottom = !inBottom;
                    dy = inBottom ? dy + 8 : dy;
                }

                int usedTile;
                if (uSprHeight == 8) {
                    usedTile = tileI + uSprTable * 256;
                } else {
                    int top       = tileI & 0xFE;
                    int tableBase = ((tileI & 1) == 1) ? 256 : 0;
                    usedTile = (dy < 8) ? (top + tableBase) : (top + 1 + tableBase);
                }

                int tableId   = usedTile / 256;
                int localTile = usedTile % 256;
                int chrX = tableId * 128 + (localTile % 16) * 8 + tx;
                int chrY = (localTile / 16) * 8 + tyLocal;
                uint pixVal = texelFetch(uChr, ivec2(chrX, chrY), 0).r;

                if (pixVal == 0u) continue;
                fragColor = nesSprColor(palIdx, pixVal);
            }

            if (fragColor.a < 0.5) discard;
        }
        """;

    private uint _bgShader;
    private uint _sprShader;
    private uint _vao;

    private uint _chrTex;
    private uint _nmtTex;
    private uint _paletteTex;
    private uint _oamTex;

    private uint _fbo;
    private uint _fboTex;
    
    private uint _lineScrollTex;
    private readonly byte[] _lineScrollBuf = new byte[240 * 3];

    private readonly byte[] _vramNametable0 = new byte[960 + 64];
    private readonly byte[] _vramNametable1 = new byte[960 + 64];
    private readonly byte[] _vramPallete = new byte[32];
    private readonly byte[,,] _vramChr = new byte[16 * 32, 8, 8];
    private byte[] _vramOam = new byte[256];

    private int _scanlineCounter = 0;
    private bool _nmiOccurred;

    public bool VBlankNmInterrupt;
    public bool IsMaster ;
    public byte SpriteHeight = 8;
    public byte BackgroundPatternTable;
    public byte SpritePatternTable;
    public byte IncrementPerRead = 1;

    private byte _ppumask;
    private byte _ppustat;
    private byte _oamaddr;
    private byte _oamdata;

    private ushort _vramAddr;

    private int _nmtbX;
    private int _nmtbY;
    private int _scrollX;
    private int _scrollY;

    private byte _oamDma = 0;

    private int _regx;
    private ushort _regt;
    private bool _regw;

    private uint _backgroundTexHandler;
    private uint _foregroundTexHandler;

    public bool OnVblank
    {
        get => (_ppustat & 0b_1000_0000) != 0;
        set => _ppustat = (byte)((_ppustat & ~0b_1000_0000) | (value ? 0b_1000_0000 : 0));
    }

    public bool Sprite0Hit
    {
        get => (_ppustat & 0b_0100_0000) != 0;
        set => _ppustat = (byte)((_ppustat & ~0b_0100_0000) | (value ? 0b_0100_0000 : 0));
    }

    public bool IsHorizontalMirroring => system.Rom.RomData.NametableArrangement == NametableArrangement.Horizontal;

    #region SilkNet shit

    private int _texWrapMode = (int)TextureWrapMode.Repeat;
    private int _texMinFilter = (int)TextureMinFilter.Nearest;
    private int _texMagFilter = (int)TextureMagFilter.Nearest;

    #endregion

    static readonly (byte r, byte g, byte b)[] palletes =
    [
        (0x62, 0x62, 0x62), (0x00, 0x1C, 0x95), (0x19, 0x04, 0xAC), (0x42, 0x00, 0x9D),
        (0x61, 0x00, 0x6B), (0x6E, 0x00, 0x25), (0x65, 0x05, 0x00), (0x49, 0x1E, 0x00),
        (0x22, 0x37, 0x00), (0x00, 0x49, 0x00), (0x00, 0x4F, 0x00), (0x00, 0x48, 0x16),
        (0x00, 0x35, 0x5E), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),

        (0xAB, 0xAB, 0xAB), (0x0C, 0x4E, 0xDB), (0x3D, 0x2E, 0xFF), (0x71, 0x15, 0xF3),
        (0x9B, 0x0B, 0xB9), (0xB0, 0x12, 0x62), (0xA9, 0x27, 0x04), (0x89, 0x46, 0x00),
        (0x57, 0x66, 0x00), (0x23, 0x7F, 0x00), (0x00, 0x89, 0x00), (0x00, 0x83, 0x32),
        (0x00, 0x6D, 0x90), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),

        (0xFF, 0xFF, 0xFF), (0x57, 0xA5, 0xFF), (0x82, 0x87, 0xFF), (0xB4, 0x6D, 0xFF),
        (0xDF, 0x60, 0xFF), (0xF8, 0x63, 0xC6), (0xF8, 0x74, 0x6D), (0xDE, 0x90, 0x20),
        (0xB3, 0xAE, 0x00), (0x81, 0xC8, 0x00), (0x56, 0xD5, 0x22), (0x3D, 0xD3, 0x6F),
        (0x3E, 0xC1, 0xC8), (0x4E, 0x4E, 0x4E), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),

        (0xFF, 0xFF, 0xFF), (0xBE, 0xE0, 0xFF), (0xCD, 0xD4, 0xFF), (0xE0, 0xCA, 0xFF),
        (0xF1, 0xC4, 0xFF), (0xFC, 0xC4, 0xEF), (0xFD, 0xCA, 0xCE), (0xF5, 0xD4, 0xAF),
        (0xE6, 0xDF, 0x9C), (0xD3, 0xE9, 0x9A), (0xC2, 0xEF, 0xA8), (0xB7, 0xEF, 0xC4),
        (0xB6, 0xEA, 0xE5), (0xB8, 0xB8, 0xB8), (0x00, 0x00, 0x00), (0x00, 0x00, 0x00),
    ];

    private void WritePpuCtrl(byte value)
    {
        VBlankNmInterrupt      = (value & 0b_1000_0000) != 0;
        IsMaster               = (value & 0b_0100_0000) == 0;
        SpriteHeight           = (byte)(((value & 0b_0010_0000) == 0) ? 8 : 16);
        BackgroundPatternTable = (byte)(((value & 0b_0001_0000) == 0) ? 0 : 1);
        SpritePatternTable     = (byte)(((value & 0b_0000_1000) == 0) ? 0 : 1);
        IncrementPerRead       = (byte)(((value & 0b_0000_0100) == 0) ? 1 : 32);

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

            _regt = (ushort)(
                (_regt & ~0x73E0) |
                ((coarseY & 0x1F) << 5) |
                ((fineY & 0x07) << 12)
            );
        }

        _regw = !_regw;
    }

    private void WritePpuAddress(byte value)
    {
        if (!_regw)
            _regt = (ushort)((_regt & 0b_00_000000_11111111) | ((value & 0b_00_111111) << 8));

        else
        {
            _regt     = (ushort)((_regt & 0b_00_111111_00000000) | value);
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
            case >= 0x2000 and < 0x3000:
            {
                WriteWithNametableMirroring(addr, value);
            } break;

            case >= 0x3F00 and <= 0x3F1F:
            {
                var pa = addr - 0x3F00;
                _vramPallete[pa] = value;
                if ((pa & 0x03) == 0) _vramPallete[pa ^ 0x10] = value;
            } break;
            case >= 0x3F20 and <= 0x3FFF:
            {
                
                _vramPallete[(addr - 0x3F00) % 0x20] = value;
            } break;

            //default: throw new ArgumentOutOfRangeException();
        }

        _regw                  = false;
        _updateNametablesSheet = true;
    }

    private byte ReadPpuData()
    {
        var addr = _vramAddr;
        _vramAddr += IncrementPerRead;

        switch (addr)
        {
            case < 0x2000: return system.Rom.RomData.ChrData[addr];
            case < 0x3000: return ReadWithNametableMirroring(addr);
            
            case >= 0x3F00 and < 0x3F20:
            {
                var pa = addr - 0x3F00;
                if ((pa & 0x03) == 0) pa &= 0x0F;
                return _vramPallete[pa];
            }
            case >= 0x3F20 and <= 0x3FFF:
            {
                var pa = (addr - 0x3F00) % 0x20;
                if ((pa & 0x03) == 0) pa &= 0x0F;
                return _vramPallete[pa];
            }
            
            default: return 0;
        }
    }


    private byte ReadWithNametableMirroring(ushort addr)
    {
        var (a, b) = ProcessNametableMirroring(addr);
        return a ? _vramNametable0[b] : _vramNametable1[b];
    }

    private void WriteWithNametableMirroring(ushort addr, byte value)
    {
        var (a, b) = ProcessNametableMirroring(addr);
        if (a) _vramNametable0[b] = value;
        else _vramNametable1[b]   = value;
    }

    private (byte tile, byte paletteIndex) GetTile(int table, int posX, int posY)
    {
        var tileLinearAddr = (ushort)(0x2000 + table * 0x400 + posY * 32 + posX);
        var tile = ReadWithNametableMirroring(tileLinearAddr);

        var attrBase = (ushort)(0x2000 + table * 0x400 + 0x3C0);
        var attrX = posX >> 2; // 0..7
        var attrY = posY >> 2; // 0..7
        var attrOffset = attrY * 8 + attrX;
        var attrAddr = (ushort)(attrBase + attrOffset);

        var attr = ReadWithNametableMirroring(attrAddr);

        var txInAttr = posX & 0b11;                                       // 0..3
        var tyInAttr = posY & 0b11;                                       // 0..3
        var quadrant = (txInAttr >= 2 ? 1 : 0) + (tyInAttr >= 2 ? 2 : 0); // 0..3

        var paletteIndex = (byte)((attr >> (quadrant * 2)) & 0b11); // 0..3

        return (tile, paletteIndex);
    }

    private (bool useNmtb0, ushort addr) ProcessNametableMirroring(ushort addr)
    {
        addr = (ushort)(0x2000 + (addr - 0x2000) % 0x1000);
        if (IsHorizontalMirroring)
        {
            //  0 | 0
            //  1 | 1
            return addr switch
            {
                < 0x2400 => (true, (ushort)(addr - 0x2000)),
                < 0x2800 => (true, (ushort)(addr - 0x2400)),
                < 0x2C00 => (false, (ushort)(addr - 0x2800)),
                _        => (false, (ushort)(addr - 0x2C00))
            };
        }

        {
            //  0 | 1
            //  0 | 1
            return addr switch
            {
                < 0x2400 => (true, (ushort)(addr - 0x2000)),
                < 0x2800 => (false, (ushort)(addr - 0x2400)),
                < 0x2C00 => (true, (ushort)(addr - 0x2800)),
                _        => (false, (ushort)(addr - 0x2C00))
            };
        }
    }
    
    private void CopyOamData(byte hAddr) => _vramOam = system.Ram.CopyPage(hAddr);

    public Ppu(VirtualSystem mb) : base(mb)
    {
        Program.DrawPopup += RenderGame;
        Program.DrawPopup += DebugPpu;
        Program.DrawPopup += DebugVram;

        var gl = Program.gl;

        _bgShader  = CompileShader(_vertShader, _bgFragShader);
        _sprShader = CompileShader(_vertShader, _fgFragShader);

        _vao = gl.GenVertexArray();

        _chrTex        = MakeTexture(gl, 256, 128, InternalFormat.R8ui, PixelFormat.RedInteger, PixelType.UnsignedByte);
        _nmtTex        = MakeTexture(gl, 64, 60, InternalFormat.RG8ui, PixelFormat.RGInteger, PixelType.UnsignedByte);
        _paletteTex    = MakeTexture(gl, 32, 1, InternalFormat.R8ui, PixelFormat.RedInteger, PixelType.UnsignedByte);
        _oamTex        = MakeTexture(gl, 256, 1, InternalFormat.R8ui, PixelFormat.RedInteger, PixelType.UnsignedByte);
        _lineScrollTex = MakeTexture(gl, 240, 1, InternalFormat.Rgb8ui, PixelFormat.RgbInteger, PixelType.UnsignedByte);
        
        _fboTex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _fboTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 256, 240, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _fboTex, 0);

        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete) throw new Exception();

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        
        CreateSpriteSheets();
    }

    public void Reset()
    {
        UploadChrTexture();
    }

    public void ProcessScanline()
    {
        switch (++_scanlineCounter)
        {
            case >= 0 and < 240:
            {
                var coarseX    = _regt & 0x1F;
                var coarseY    = (_regt >> 5) & 0x1F;
                var nametableX = (_regt >> 10) & 0x1;
                var nametableY = (_regt >> 11) & 0x1;
                var fineY      = (_regt >> 12) & 0x7;
                var fineX      = _regx & 0x7;

                if (coarseY >= 30) { coarseY -= 30; nametableY ^= 1; }

                var sx = (coarseX * 8 + fineX) & 0x1FF;
                var sy = coarseY * 8 + fineY;

                var i = _scanlineCounter * 3;
                _lineScrollBuf[i + 0] = (byte)(sx & 0xFF);
                _lineScrollBuf[i + 1] = (byte)(sy & 0xFF);
                _lineScrollBuf[i + 2] = (byte)(nametableX | (nametableY << 1) | (((sx >> 8) & 1) << 2));
                
                if (!Sprite0Hit) DetectSprite0Hit(_scanlineCounter);
                break;
            }
            case 241 when !OnVblank:
            {
                OnVblank = true;
                if (VBlankNmInterrupt) system.Cpu.RequestNmInterrupt();

                UpdateForeground();
                if (_updateNametablesSheet) UpdateBackground();
                UploadLineScroll();
                break;
            }
            case >= 262:
                _scanlineCounter = 0;
                OnVblank         = false;
                Sprite0Hit       = false;
            break;
        }
    }

    private void UploadLineScroll()
    {
        var gl = Program.gl;
        gl.BindTexture(TextureTarget.Texture2D, _lineScrollTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb8ui, 240, 1, 0,
            PixelFormat.RgbInteger, PixelType.UnsignedByte, _lineScrollBuf);
    }

    private void UpdateBackground()
    {
        var buf = new byte[64 * 60 * 2];
        for (var nmt = 0; nmt < 4; nmt++)
        {
            int offX = (nmt % 2) * 32, offY = (nmt / 2) * 30;
            for (var ty = 0; ty < 30; ty++)
            for (var tx = 0; tx < 32; tx++)
            {
                var (tile, palIdx) = GetTile(nmt, tx, ty);
                var i = ((offY + ty) * 64 + (offX + tx)) * 2;
                buf[i + 0] = tile;
                buf[i + 1] = palIdx;
            }
        }
        var gl = Program.gl;
        
        gl.BindTexture(TextureTarget.Texture2D, _nmtTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.RG8ui, 64, 60, 0,
            PixelFormat.RGInteger, PixelType.UnsignedByte, buf);

        gl.BindTexture(TextureTarget.Texture2D, _paletteTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8ui, 32, 1, 0,
            PixelFormat.RedInteger, PixelType.UnsignedByte, _vramPallete);

        var bgDbg = new byte[512 * 480 * 4];
        for (var nmt = 0; nmt < 4; nmt++)
        {
            int offX = (nmt % 2) * 256, offY = (nmt / 2) * 240;
            for (var ty = 0; ty < 30; ty++)
            for (var tx = 0; tx < 32; tx++)
            {
                var (tile, palIdx) = GetTile(nmt, tx, ty);
                var chrIdx = BackgroundPatternTable * 256 + tile;
                for (var py = 0; py < 8; py++)
                for (var px = 0; px < 8; px++)
                {
                    var pv = _vramChr[chrIdx, px, py];
                    var nesIdx = pv == 0
                        ? _vramPallete[0]
                        : _vramPallete[palIdx * 4 + pv];
                    var c = palletes[nesIdx & 63];
                    var di = ((offY + ty * 8 + py) * 512 + (offX + tx * 8 + px)) * 4;
                    bgDbg[di] = c.r; bgDbg[di+1] = c.g; bgDbg[di+2] = c.b; bgDbg[di+3] = 255;
                }
            }
        }
        
        gl.BindTexture(TextureTarget.Texture2D, _backgroundTexHandler);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 512, 480, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, bgDbg);
        
        _updateNametablesSheet = false;
    }
    private void UpdateForeground()
    {
        var fgDbg = new byte[256 * 240 * 4];
        for (var sprite = 0; sprite < 64; sprite++)
        {
            var sy0    = _vramOam[sprite * 4 + 0] + 1;
            var tileI  = _vramOam[sprite * 4 + 1];
            var attrs  = _vramOam[sprite * 4 + 2];
            var sx0    = _vramOam[sprite * 4 + 3];
            var flipX  = (attrs & 0x40) != 0;
            var flipY  = (attrs & 0x80) != 0;
            var palIdx = attrs & 3;
            var height = SpriteHeight == 16 ? 16 : 8;

            if (sy0 <= 0 || sy0 >= 240) continue;

            for (var py = 0; py < height; py++)
            for (var px = 0; px < 8; px++)
            {
                var sx = sx0 + px;
                var sy = sy0 + py;
                if (sx < 0 || sx >= 256 || sy < 0 || sy >= 240) continue;

                var tx = flipX ? 7 - px : px;
                var ty = flipY ? (height == 16 ? (py < 8 ? 7 - py : 7 - (py - 8)) : 7 - py) : py % 8;

                int usedTile;
                if (height == 8)
                    usedTile = tileI + SpritePatternTable * 256;
                else
                {
                    var top = tileI & 0xFE;
                    usedTile = (py < 8 ? top : top + 1) + ((tileI & 1) == 1 ? 256 : 0);
                }

                var pv = _vramChr[usedTile, tx, ty];
                if (pv == 0) continue;

                var nesIdx = _vramPallete[(0x10 + palIdx * 4 + pv) & 0x1F];
                var col = palletes[nesIdx & 63];
                var di = (sy * 256 + sx) * 4;
                fgDbg[di] = col.r; fgDbg[di+1] = col.g; fgDbg[di+2] = col.b; fgDbg[di+3] = 255;
            }
        }
        
        var gl = Program.gl;
        gl.BindTexture(TextureTarget.Texture2D, _oamTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8ui, 256, 1, 0,
            PixelFormat.RedInteger, PixelType.UnsignedByte, _vramOam);
        
        gl.BindTexture(TextureTarget.Texture2D, _foregroundTexHandler);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 256, 240, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, fgDbg);
    }
    private byte ReadBackgroundPixel(int screenX, int screenY)
    {
        var wx = screenX + _scrollX + _nmtbX * 256;
        var wy = screenY + _scrollY + _nmtbY * 240;

        var tx = (wx >> 3) & 63;
        var ty = ((wy >> 3) % 60 + 60) % 60;

        var pxT = wx & 7;
        var pyT = wy & 7;

        var table = (tx / 32) + ((ty / 30) * 2);

        tx %= 32;
        ty %= 30;

        var (tile, _) = GetTile(table, tx, ty);

        var chrIndex = BackgroundPatternTable * 256 + tile;

        return _vramChr[chrIndex, pxT, pyT];
    }
    private void DetectSprite0Hit(int scanline)
    {
        var sprY   = _vramOam[0] + 1;
        var sprX   = _vramOam[3];
        var tileI  = _vramOam[1];
        var attrs  = _vramOam[2];
        var flipX  = (attrs & 0x40) != 0;
        var flipY  = (attrs & 0x80) != 0;
        var height = SpriteHeight == 16 ? 16 : 8;

        var dy = scanline - sprY;
        if (dy < 0 || dy >= height) return;

        for (var px = 0; px < 8; px++)
        {
            var sx = sprX + px;
            if (sx >= 255) continue;

            var tx     = flipX ? 7 - px : px;
            var halfDy = dy % 8;
            var tyLocal = flipY ? 7 - halfDy : halfDy;
            
            var inBottom = dy >= 8;
            if (flipY && height == 16) inBottom = !inBottom;

            int usedTile;
            if (height == 8) usedTile = tileI + SpritePatternTable * 256;
            else
            {
                var top = tileI & 0xFE;
                usedTile = (inBottom ? top + 1 : top) + ((tileI & 1) == 1 ? 256 : 0);
            }

            var spPix = _vramChr[usedTile, tx, tyLocal];
            if (spPix == 0) continue;

            if (ReadBackgroundPixel(sx, scanline) != 0)
            {
                Sprite0Hit = true;
                return;
            }
        }
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

        var coarseX = _regt & 0x1F;
        var coarseY = (_regt >> 5) & 0x1F;
        var nametableX = (_regt >> 10) & 0x1;
        var nametableY = (_regt >> 11) & 0x1;
        var fineY = (_regt >> 12) & 0x7;
        var fineX = _regx & 0x7;

        if (coarseY >= 30)
        {
            coarseY    -= 30;
            nametableY ^= 1;
        }

        _scrollX = (coarseX * 8 + fineX) & 0x1FF;
        _scrollY = coarseY * 8 + fineY;

        _nmtbX = nametableX;
        _nmtbY = nametableY;

        //Console.WriteLine($"Scroll update: {_scrollX:D3} {_nmtbX} {_scrollY:D3} {_nmtbY}");
    }

    public byte ReadRegister(ushort addr)
    {
        var regIndex = (addr - 0x2000) & 7;
        switch (regIndex)
        {
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
        }

        return 0;
    }

    public void WriteRegister(ushort addr, byte data)
    {
        if (addr == 0x4014)
        {
            CopyOamData(data);
            return;
        }

        var regIndex = (addr - 0x2000) & 7;
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
        }
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

            if (VBlankNmInterrupt) ImGui.Text("NMI enabled");
            else ImGui.TextDisabled("NMI Disabled");
            ImGui.SameLine();
            if (OnVblank) ImGui.Text("VBlank");
            else ImGui.TextDisabled("VBlank");
            ImGui.SameLine();
            if (_nmiOccurred) ImGui.Text("Occurred");
            else ImGui.TextDisabled("Occurred");
            if (IsMaster) ImGui.Text("PPU Master");
            else ImGui.TextDisabled("PPU Slave");

            ImGui.TextDisabled("Sprite size:");
            ImGui.SameLine();
            ImGui.Text(SpriteHeight == 16 ? "8x16" : "8x8");

            ImGui.TextDisabled("FG sheet:");
            ImGui.SameLine();
            ImGui.Text(SpritePatternTable == 1 ? "Right" : "Left");
            ImGui.TextDisabled("BG sheet:");
            ImGui.SameLine();
            ImGui.Text(BackgroundPatternTable == 1 ? "Right" : "Left");


            ImGui.TextDisabled("VRAM inc:");
            ImGui.SameLine();
            ImGui.Text($"x{IncrementPerRead}");

            ImGui.SeparatorText("Flags:");
            ImGui.Text($"{_ppustat:b8}");

            ImGui.SeparatorText("Data:");
            ImGui.TextDisabled("Temp Addr:");
            ImGui.SameLine();
            ImGui.Text($"${_regt:X4} ({_regt:b16})");
            ImGui.TextDisabled("Data Addr:");
            ImGui.SameLine();
            ImGui.Text($"${_vramAddr:X4} ({_vramAddr:b16})");

            ImGui.SeparatorText("Scroll:");

            if (ImGui.BeginTable("CPU controls", 2))
            {
                ImGui.TableNextColumn();
                ImGui.TextDisabled("X scroll:");
                ImGui.SameLine();
                ImGui.Text($"{_scrollX}");

                ImGui.TableNextColumn();
                ImGui.TextDisabled("X table:");
                ImGui.SameLine();
                ImGui.Text($"{_nmtbX}");

                ImGui.TableNextColumn();
                ImGui.TextDisabled("Y scroll:");
                ImGui.SameLine();
                ImGui.Text($"{_scrollY}");

                ImGui.TableNextColumn();
                ImGui.TextDisabled("Y table:");
                ImGui.SameLine();
                ImGui.Text($"{_nmtbY}");

                ImGui.EndTable();
            }


            ImGui.SeparatorText("Internal:");

            ImGui.TextDisabled("Scanline:");
            ImGui.SameLine();
            ImGui.Text($"{_scanlineCounter}");
        }
        ImGui.End();

        ImGui.Begin("Sprite Sheet View", ImGuiWindowFlags.AlwaysAutoResize);
        {
            ImGui.Image((nint)_chrTex, new Vector2(100, 100), new Vector2(0, 1), new Vector2(1, 0));

            if (ImGui.Button("Left Sheet")) _viewingSheet = 0;
            ImGui.SameLine();
            if (ImGui.Button("Right Sheet")) _viewingSheet = 1;

            ImGui.Text("Pallete:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("A", ref _palleteIndexA, 1, 1, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (_palleteIndexA > 64) _palleteIndexA -= 64;
                if (_palleteIndexA < 1) _palleteIndexA  += 64;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("B", ref _palleteIndexB, 1, 1, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (_palleteIndexB > 64) _palleteIndexB -= 64;
                if (_palleteIndexB < 1) _palleteIndexB  += 64;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("C", ref _palleteIndexC, 1, 1, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (_palleteIndexC > 64) _palleteIndexC -= 64;
                if (_palleteIndexC < 1) _palleteIndexC  += 64;
            }
        }
        ImGui.End();
    }

    private void DebugVram()
    {
        ImGui.Begin("VRAM View", ImGuiWindowFlags.AlwaysAutoResize);
        {
            var drawList = ImGui.GetWindowDrawList();
            ;

            ImGui.SeparatorText("Pattern Tables:");

            ImGui.Image((nint)_spriteSheetHandlerLeft, new(300, 300));
            ImGui.SameLine();
            ImGui.Image((nint)_spriteSheetHandlerRight, new(300, 300));

            ImGui.SeparatorText("Nametables:");

            var maxW = ImGui.GetContentRegionAvail().X;
            Vector2 renderRes = new(256, 240);

            var imageAspectRatio = renderRes.X / renderRes.Y;
            Vector2 finalSize = new(maxW, maxW / imageAspectRatio);
            var scale = finalSize.X / renderRes.X / 2;

            var cp = ImGui.GetCursorScreenPos();

            ImGui.Image((nint)_backgroundTexHandler, finalSize);

            var camX = _scrollX + _nmtbX * 256;
            var camY = _scrollY + _nmtbY * 240;

            drawList.AddRect(
                cp + (new Vector2(camX * scale, camY * scale)),
                cp + (new Vector2((camX + 256) * scale, (camY + 240) * scale)),
                ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 1f))
            );

            if (ImGui.Checkbox("Show attributes", ref _showNametablesAttributeTable)) _updateNametablesSheet = true;

            for (var i = 0; i < 30; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:");
                ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable0[(i * 32) .. ((i + 1) * 32)].Select(e => $"{e:X2}")));
            }

            ImGui.NewLine();
            for (var i = 30; i < 32; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:");
                ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable0[(i * 32)..((i + 1) * 32)].Select(e => $"{e:X2}")));
            }

            ImGui.NewLine();
            for (var i = 0; i < 30; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:");
                ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable1[(i * 32)..((i + 1) * 32)].Select(e => $"{e:X2}")));
            }

            ImGui.NewLine();
            for (var i = 30; i < 32; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 32:X3}:");
                ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramNametable1[(i * 32)..((i + 1) * 32)].Select(e => $"{e:X2}")));
            }

            ImGui.SeparatorText("Sprites");

            ImGui.Image((nint)_foregroundTexHandler, finalSize);

            for (var i = 0; i < 16; i++)
            {
                ImGui.TextDisabled($"{0x00 + i * 4:X3}:");
                ImGui.SameLine();
                ImGui.Text(string.Join(" ", _vramOam[(4 * i) .. (4 * i + 4)].Select(e => $"{e:X2}")));
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

            ImGui.TextDisabled($"{0x00:X2}:");
            ImGui.SameLine();
            ImGui.Text(string.Join(" ", _vramPallete[..0x10].Select(e => $"{e:X2}")));

            ImGui.TextDisabled("Sprites:");

            cursorPos = ImGui.GetCursorScreenPos();
            cpx       = cursorPos.X;
            cpy       = cursorPos.Y;

            for (var i = 0; i < 16; i++)
            {
                var c = GetPal(_vramPallete[0x10 + i]);
                var color = ImGui.GetColorU32(new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, 1f));

                var posA = new Vector2(cpx + (33 * i), cpy);
                var posB = new Vector2(cpx + (33 * (i + 1) - 1), cpy + 32);

                drawList.AddRectFilled(posA, posB, color, 0);
            }

            ImGui.Dummy(new(32 * 16, 32));

            ImGui.TextDisabled($"{0x10:X2}:");
            ImGui.SameLine();
            ImGui.Text(string.Join(" ", _vramPallete[0x10..0x20].Select(e => $"{e:X2}")));
        }
        ImGui.End();
    }

    private void RenderGame()
    {
        var gl = Program.gl;

        Span<float> floats = stackalloc float[64 * 3];
        for (var i = 0; i < 64; i++)
        {
            floats[i * 3 + 0] = palletes[i].r / 255f;
            floats[i * 3 + 1] = palletes[i].g / 255f;
            floats[i * 3 + 2] = palletes[i].b / 255f;
        }

        // Configure FBO render
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.Viewport(0, 0, 256, 240);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.BindVertexArray(_vao);

        // Bind textures
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, _chrTex);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, _nmtTex);
        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(TextureTarget.Texture2D, _oamTex);
        gl.ActiveTexture(TextureUnit.Texture3);
        gl.BindTexture(TextureTarget.Texture2D, _paletteTex);
        gl.ActiveTexture(TextureUnit.Texture4);
        gl.BindTexture(TextureTarget.Texture2D, _lineScrollTex);

        gl.UseProgram(_bgShader);

        // Background
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uChr"), 0);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uNmt"), 1);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uPalette"), 3);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uBgTable"), BackgroundPatternTable);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uScrollX"), _scrollX);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uScrollY"), _scrollY);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uNmtX"), _nmtbX);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uNmtY"), _nmtbY);
        gl.Uniform3(gl.GetUniformLocation(_bgShader, "uMasterPalette"), floats);
        gl.Uniform1(gl.GetUniformLocation(_bgShader, "uLineScroll"), 4);
        
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // foreground
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.UseProgram(_sprShader);

        gl.Uniform1(gl.GetUniformLocation(_sprShader, "uChr"), 0);
        gl.Uniform1(gl.GetUniformLocation(_sprShader, "uOam"), 2);
        gl.Uniform1(gl.GetUniformLocation(_sprShader, "uPalette"), 3);
        gl.Uniform1(gl.GetUniformLocation(_sprShader, "uSprTable"), SpritePatternTable);
        gl.Uniform1(gl.GetUniformLocation(_sprShader, "uSprHeight"), SpriteHeight);
        gl.Uniform3(gl.GetUniformLocation(_sprShader, "uMasterPalette"), floats);
        
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.Disable(EnableCap.Blend);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.BindVertexArray(0);

        var viewport = ImGui.GetWindowViewport().Size;
        gl.Viewport(0, 0, (uint)viewport.X, (uint)viewport.Y);

        ImGui.Begin("Video Out");

        var viewSize = ImGui.GetContentRegionAvail();
        Vector2 renderRes = new(256, 240);
        var ar = renderRes.X / renderRes.Y;
        var canvasAr = viewSize.X / viewSize.Y;

        var finalSize = (canvasAr < ar)
            ? viewSize with { Y = viewSize.X / ar }
            : viewSize with { X = viewSize.Y * ar };

        ImGui.Image((nint)_fboTex, finalSize, new Vector2(0, 1), new Vector2(1, 0));

        ImGui.End();
    }

    private void CreateSpriteSheets()
    {
        var gl = Program.gl;
        _spriteSheetHandlerLeft  = gl.GenTexture();
        _spriteSheetHandlerRight = gl.GenTexture();
        _backgroundTexHandler    = gl.GenTexture();
        _foregroundTexHandler    = gl.GenTexture();

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
    
    private void UploadChrTexture()
    {
        var chr = system.Rom.RomData.ChrData;
        var tiles = chr.Length / 16;
        
        for (var t = 0; t < tiles && t < (16 * 32); t++)
        {
            for (var row = 0; row < 8; row++)
            {
                var b0 = chr[t * 16 + row];     // bitplane 0
                var b1 = chr[t * 16 + 8 + row]; // bitplane 1
                
                for (var col = 0; col < 8; col++)
                {
                    var bit = 7 - col;
                    var pv = ((b0 >> bit) & 1) | (((b1 >> bit) & 1) << 1);
                    _vramChr[t, col, row] = (byte)pv;
                }
            }
        }
        
        var buf = new byte[256 * 128];
        for (var table = 0; table < 2; table++)
        for (var tile = 0; tile < 256; tile++)
        for (var py = 0; py < 8; py++)
        for (var px = 0; px < 8; px++)
        {
            var texX = table * 128 + (tile % 16) * 8 + px;
            var texY = (tile / 16) * 8 + py;
            buf[texY * 256 + texX] = _vramChr[table * 256 + tile, px, py];
        }

        var gl = Program.gl;
        gl.BindTexture(TextureTarget.Texture2D, _chrTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8ui, 256, 128, 0,
            PixelFormat.RedInteger, PixelType.UnsignedByte, buf);
        
        // Pattern table viewers — RGBA8 com paleta de debug (preto→branco)
        ReadOnlySpan<(byte r, byte g, byte b)> dbgPal =
            [(0,0,0), (85,85,85), (170,170,170), (255,255,255)];

        var leftBuf  = new byte[128 * 128 * 4];
        var rightBuf = new byte[128 * 128 * 4];

        for (var y = 0; y < 128; y++)
        for (var x = 0; x < 128; x++)
        {
            var pv = buf[y * 256 + x];
            var dst = (y * 128 + x) * 4;
            leftBuf[dst]     = dbgPal[pv].r;
            leftBuf[dst + 1] = dbgPal[pv].g;
            leftBuf[dst + 2] = dbgPal[pv].b;
            leftBuf[dst + 3] = 255;

            pv                = buf[y * 256 + 128 + x];
            rightBuf[dst]     = dbgPal[pv].r;
            rightBuf[dst + 1] = dbgPal[pv].g;
            rightBuf[dst + 2] = dbgPal[pv].b;
            rightBuf[dst + 3] = 255;
        }

        gl.BindTexture(TextureTarget.Texture2D, _spriteSheetHandlerLeft);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 128, 128, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, leftBuf);

        gl.BindTexture(TextureTarget.Texture2D, _spriteSheetHandlerRight);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 128, 128, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, rightBuf);
    }

    private (byte r, byte g, byte b) GetPal(int index) => palletes[index & 0b00_111111];

    private static uint MakeTexture(GL gl, uint w, uint h,
        InternalFormat iFmt, PixelFormat pFmt, PixelType pType)
    {
        var t = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, t);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.TexImage2D(TextureTarget.Texture2D, 0, iFmt, w, h, 0, pFmt, pType, ReadOnlySpan<byte>.Empty);
        return t;
    }

    private static uint CompileShader(string vertSrc, string fragSrc)
    {
        var gl = Program.gl;

        var vs = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vs, vertSrc);
        gl.CompileShader(vs);
        gl.GetShader(vs, ShaderParameterName.CompileStatus, out var ok);
        if (ok == 0) throw new Exception("Vertex shader error:\n" + gl.GetShaderInfoLog(vs));

        var fs = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fs, fragSrc);
        gl.CompileShader(fs);
        gl.GetShader(fs, ShaderParameterName.CompileStatus, out ok);
        if (ok == 0) throw new Exception("Fragment shader error:\n" + gl.GetShaderInfoLog(fs));

        var prog = gl.CreateProgram();
        gl.AttachShader(prog, vs);
        gl.AttachShader(prog, fs);
        gl.LinkProgram(prog);
        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out ok);
        if (ok == 0) throw new Exception("Shader link error:\n" + gl.GetProgramInfoLog(prog));

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return prog;
    }
}
