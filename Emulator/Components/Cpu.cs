using Emulator.Components.Core;
using ImGuiNET;
using System.Text;

namespace Emulator.Components;

public class Cpu : Component
{
    public ushort progCounter = 0;

    public byte stackPointer = 0;

    public byte accumulator = 0;
    public byte indexX = 0;
    public byte indexY = 0;

    public byte flags = 0;

    public bool Negative
    {
        get => (flags & (byte)SF.Negative) != 0;
        set => flags = (byte)((flags & ~(byte)SF.Negative) | (value ? (byte)SF.Negative : 0));
    }
    public bool Overflow
    {
        get => (flags & (byte)SF.Overflow) != 0;
        set => flags = (byte)((flags & ~(byte)SF.Overflow) | (value ? (byte)SF.Overflow : 0));
    }
    public bool Break
    {
        get => (flags & (byte)SF.Break) != 0;
        set => flags = (byte)((flags & ~(byte)SF.Break) | (value ? (byte)SF.Break : 0));
    }
    public bool Decimal
    {
        get => (flags & (byte)SF.Decimal) != 0;
        set => flags = (byte)((flags & ~(byte)SF.Decimal) | (value ? (byte)SF.Decimal : 0));
    }
    public bool Interrupt
    {
        get => (flags & (byte)SF.Interrupt) != 0;
        set => flags = (byte)((flags & ~(byte)SF.Interrupt) | (value ? (byte)SF.Interrupt : 0));
    }
    public bool Zero
    {
        get => (flags & (byte)SF.Zero) != 0;
        set => flags = (byte)((flags & ~(byte)SF.Zero) | (value ? (byte)SF.Zero : 0));
    }
    public bool Carry
    {
        get => (flags & (byte)SF.Carry) != 0;
        set => flags = (byte)((flags & ~(byte)SF.Carry) | (value ? (byte)SF.Carry : 0));
    }

    private ushort StackAddress => (ushort)(0x0100 + stackPointer);

    public bool paused = true;
    public bool doStep = false;
    
    public int clockCount = 0;

    public ushort ResetVector => MemReadWord(0xFFFC);
    public ushort NonMaskableInterrupt => MemReadWord(0xFFFA);
    public ushort InterruptRequest => MemReadWord(0xFFFE);

    public Cpu(VirtualSystem mb) : base(mb)
    {
        Program.DrawPopup += DebugCPU;
    }
    
    public void Reset()
    {
        SetProgramCounter(ResetVector);
        flags = 0;
    }
    public void RequestNmInterrupt()
    {
        Push(progCounter);
        Push(flags);
        Interrupt = true;
        SetProgramCounter(NonMaskableInterrupt);
    }
    public void RequestInterrupt()
    {
        if (Interrupt) return;
        
        Push(progCounter);
        Push(flags);
        Interrupt = true;
        SetProgramCounter(InterruptRequest);
    }
    public void RequestBreak()
    {
        Push((ushort)(progCounter + 1));
        Break = true;
        Push(flags);
        Interrupt = true;
        SetProgramCounter(InterruptRequest);
        
        Console.WriteLine("Break");
    }


    public void Tick()
    {
        clockCount = 0;
        
        if (paused)
        {
            if (!doStep) return;
            doStep = false;
        }

        var opCode = ReadCounter();
        var (operation, mode) = DecodeOpCode(opCode);
        
        //Console.WriteLine($"{opCode} {operation} {mode}");
        Execute(operation, mode);
    }

    private void Execute(Operation op, AddressMode mode)
    {
        switch (op)
        {
            case Operation.Brk: {
                    RequestBreak();
                } break;
            case Operation.Nop: break;

            case Operation.And: {
                    accumulator &= GetValue(mode);

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;
            case Operation.Ora: {
                    accumulator |= GetValue(mode);

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;
            case Operation.Eor: {
                    accumulator ^= GetValue(mode);

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;

            case Operation.Adc: {
                    byte a = accumulator;
                    byte b = GetValue(mode);

                    int res = a + b + (Carry ? 1 : 0);
                    accumulator = (byte)res;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                    Carry = res > 255;

                    Overflow = ((a ^ b) & 0x80) == 0 && ((a ^ res) & 0x80) != 0;
                } break;
            case Operation.Sbc: {
                    byte a = accumulator;
                    byte b = GetValue(mode);

                    int res = a - b - (1 - (Carry ? 1 : 0));
                    accumulator = (byte)res;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                    Carry = res >= 0;

                    Overflow = ((a ^ b) & 0x80) != 0 && ((a ^ res) & 0x80) != 0;
                } break;

            case Operation.Asl: {
                    byte val;
                    ushort addr = 0;
                    if (mode == AddressMode.Accumulator)
                    {
                        val = accumulator;
                    }
                    else
                    {
                        addr = GetAddress(mode);
                        val = system.Bus.CpuRead(addr);
                    }

                    Carry = (val & 0b_1000_0000) != 0;

                    val = (byte)(val << 1);

                    Negative = (val & (byte)SF.Negative) != 0;
                    Zero = val == 0;

                    if (mode == AddressMode.Accumulator) accumulator = val;
                    else system.Bus.CpuWrite(addr, val);

                } break;
            case Operation.Lsr: {
                    byte val;
                    ushort addr = 0;
                    if (mode == AddressMode.Accumulator)
                    {
                        val = accumulator;
                    }
                    else
                    {
                        addr = GetAddress(mode);
                        val = system.Bus.CpuRead(addr);
                    }

                    Carry = (val & 0b_0000_0001) != 0;

                    val = (byte)(val >> 1);

                    Negative = (val & (byte)SF.Negative) != 0;
                    Zero = val == 0;

                    if (mode == AddressMode.Accumulator) accumulator = val;
                    else system.Bus.CpuWrite(addr, val);
                } break;
            case Operation.Rol: {
                    ushort addr = 0;
                    byte value = 0;

                    if (mode == AddressMode.Accumulator)
                    {
                        value = accumulator;
                    }
                    else
                    {
                        addr = GetAddress(mode);
                        value = system.Bus.CpuRead(addr);
                    }

                    bool oldbit7 = (value & 0b_1000_0000) != 0;

                    value = (byte)(value << 1);
                    value |= (byte)(Carry ? 0b_0000_0001 : 0);
                    Carry = oldbit7;

                    if (mode == AddressMode.Accumulator) accumulator = value;
                    else system.Bus.CpuWrite(addr, value);

                    Negative = (value & (byte)SF.Negative) != 0;
                    Zero = value == 0;

                } break;
            case Operation.Ror: {
                    ushort addr = 0;
                    byte value = 0;

                    if (mode == AddressMode.Accumulator)
                    {
                        value = accumulator;
                    }
                    else
                    {
                        addr = GetAddress(mode);
                        value = system.Bus.CpuRead(addr);
                    }

                    bool oldbit0 = (value & 0b_0000_0001) != 0;

                    value = (byte)(value >> 1);
                    value |= (byte)(Carry ? 0b_1000_0000 : 0);
                    Carry = oldbit0;

                    if (mode == AddressMode.Accumulator) accumulator = value;
                    else system.Bus.CpuWrite(addr, value);

                    Negative = (value & (byte)SF.Negative) != 0;
                    Zero = value == 0;

                } break;

            case Operation.Bcc: {
                    if (!Carry) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;
            case Operation.Bcs: {
                    if (Carry) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;
            case Operation.Beq: {
                    if (Zero) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;
            case Operation.Bmi: {
                    if (Negative) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;
            case Operation.Bne: {
                    if (!Zero) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;
            case Operation.Bpl: {
                    if (!Negative) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;
            case Operation.Bvc: {
                    if (!Overflow) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;
            case Operation.Bvs: {
                    if (Overflow) progCounter = GetAddress(mode);
                    else progCounter++;
                } break;

            case Operation.Jmp: {
                    progCounter = GetAddress(mode);
                } break;
            case Operation.Jsr: {
                    Push((ushort)(progCounter + 1));
                    ushort addr = GetAddress(mode);
                    progCounter = addr;
                } break;

            case Operation.Rts: {
                    progCounter = (ushort)(PopAddress() + 1);
                } break;
            case Operation.Rti: {
                    flags = Pop();
                    progCounter = PopAddress();
                } break;

            case Operation.Bit: {
                    byte val = GetValue(mode);
                    Zero = (accumulator & val) == 0;
                    Negative = (val & (byte)SF.Negative) != 0;
                    Overflow = (val & (byte)SF.Overflow) != 0;
                } break;
            case Operation.Cmp: {
                    byte a = accumulator;
                    byte b = GetValue(mode);

                    int res = a - b;

                    Negative = (res & (byte)SF.Negative) != 0;
                    Zero = res == 0;
                    Carry = a >= b;
                } break;
            case Operation.Cpx: {
                    byte a = indexX;
                    byte b = GetValue(mode);

                    int res = a - b;

                    Negative = (res & (byte)SF.Negative) != 0;
                    Zero = res == 0;
                    Carry = a >= b;
                } break;
            case Operation.Cpy: {
                    byte a = indexY;
                    byte b = GetValue(mode);

                    int res = a - b;

                    Negative = (res & (byte)SF.Negative) != 0;
                    Zero = res == 0;
                    Carry = a >= b;
                } break;

            case Operation.Dec: {
                    var addr = GetAddress(mode);
                    int value = system.Bus.CpuRead(addr) - 1;
                    system.Bus.CpuWrite(addr, (byte)value);

                    flags = (byte)((flags & ~(byte)SF.Negative) | (byte)(value & (byte)SF.Negative));
                    flags = (byte)((flags & ~(byte)SF.Zero) | (value == 0 ? (byte)SF.Zero : 0));
                } break;
            case Operation.Dex: {
                    indexX--;
                    flags = (byte)((flags & ~(byte)SF.Negative) | (byte)(indexX & (byte)SF.Negative));
                    flags = (byte)((flags & ~(byte)SF.Zero) | (indexX == 0 ? (byte)SF.Zero : 0));
                } break;
            case Operation.Dey: {
                    indexY--;
                    flags = (byte)((flags & ~(byte)SF.Negative) | (byte)(indexY & (byte)SF.Negative));
                    flags = (byte)((flags & ~(byte)SF.Zero) | (indexY == 0 ? (byte)SF.Zero : 0));
                } break;
            
            case Operation.Inc: {
                    byte value = GetValue(mode, out var addr);
                    byte nvalue = (byte)(value + 1);

                    system.Bus.CpuWrite(addr, nvalue);

                    Negative = (nvalue & (byte)SF.Negative) != 0;
                    Zero = nvalue == 0;
                } break;
            case Operation.Inx: {
                    indexX++;

                    Negative = (indexX & (byte)SF.Negative) != 0;
                    Zero = indexX == 0;
                } break;
            case Operation.Iny: {
                    indexY++;

                    Negative = (indexY & (byte)SF.Negative) != 0;
                    Zero = indexY == 0;
                } break;
                     
            case Operation.Lda: {
                    byte value = GetValue(mode);
                    accumulator = value;
                    flags = (byte)((flags & ~(byte)SF.Negative) | (byte)(accumulator & (byte)SF.Negative));
                    flags = (byte)((flags & ~(byte)SF.Zero) | (accumulator == 0 ? (byte)SF.Zero : 0));
                } break;
            case Operation.Ldx: {
                    ushort addr = GetValue(mode);
                    indexX = (byte)addr;
                    flags = (byte)((flags & ~(byte)SF.Negative) | (byte)(indexX & (byte)SF.Negative));
                    flags = (byte)((flags & ~(byte)SF.Zero) | (indexX == 0 ? (byte)SF.Zero : 0));
                } break;
            case Operation.Ldy: {
                    ushort addr = GetValue(mode);
                    indexY = (byte)addr;
                    flags = (byte)((flags & ~(byte)SF.Negative) | (byte)(indexY & (byte)SF.Negative));
                    flags = (byte)((flags & ~(byte)SF.Zero) | (indexY == 0 ? (byte)SF.Zero : 0));
                } break;
            
            case Operation.Pha: {
                    Push(accumulator);
                } break;
            case Operation.Php: {
                    byte status = flags;
                    status |= (byte)SF.Break;
                    status |= 0b_00100000;
                    Push(status);
                } break;
            case Operation.Pla: {
                    accumulator = Pop();

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;
            case Operation.Plp: flags = (byte)(Pop() & 0b_11_00_1111); break;
                     
            case Operation.Sec: Carry = true; break;
            case Operation.Sed: Decimal = true; break;
            case Operation.Sei: Interrupt = true; break;

            case Operation.Clc: Carry = false; break;
            case Operation.Cld: Decimal = false; break;
            case Operation.Cli: Interrupt = false; break;
            case Operation.Clv: Overflow = false; break;

            case Operation.Sta: {
                    ushort addr = GetAddress(mode);
                    MemWrite(addr, accumulator);
                } break;
            case Operation.Stx: {
                    ushort addr = GetAddress(mode);
                    MemWrite(addr, indexX);
                } break;
            case Operation.Sty: {
                    ushort addr = GetAddress(mode);
                    MemWrite(addr, indexY);
                } break;

            case Operation.Tax: {
                    indexX = accumulator;
                    Negative = (indexX & (byte)SF.Negative) != 0;
                    Zero = indexX == 0;
                } break;
            case Operation.Tay: {
                    indexY = accumulator;
                    Negative = (indexY & (byte)SF.Negative) != 0;
                    Zero = indexY == 0;
                } break;
            case Operation.Tsx: {
                    indexX = stackPointer;
                    Negative = (indexX & (byte)SF.Negative) != 0;
                    Zero = indexX == 0;
                } break;

            case Operation.Txa: {
                    accumulator = indexX;
                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;
            case Operation.Tya: {
                    accumulator = indexY;
                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;
            case Operation.Txs: {
                    stackPointer = indexX;
                } break;

            // Ilegal shit here

            case Operation.Kil: {
                    progCounter--;
                    paused = true;
                } break;
            case Operation.Nops: {
                    GetValue(mode);
                } break;
            
            case Operation.Anc2:
            case Operation.Anc: {
                    accumulator &= GetValue(mode);

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Carry = Negative;
                    Zero = accumulator == 0;
                } break;
            
            case Operation.Slo: {
                    // ASL
                    byte val = GetValue(mode, out ushort addr);

                    Carry = (val & 0b_1000_0000) != 0;
                    val = (byte)(val << 1);

                    system.Bus.CpuWrite(addr, val);

                    // ORA
                    accumulator |= val;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;
            case Operation.Rla: {
                    // ROL
                    byte val = GetValue(mode, out ushort addr);

                    bool oldbit7 = (val & 0b_1000_0000) != 0;

                    val = (byte)(val << 1);
                    val |= (byte)(Carry ? 0b_0000_0001 : 0);
                    Carry = oldbit7;

                    system.Bus.CpuWrite(addr, val);

                    // AND
                    accumulator &= val;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;
            case Operation.Sre: {
                    // LSR
                    byte val = GetValue(mode, out ushort addr);

                    Carry = (val & 0b_0000_0001) != 0;

                    val = (byte)(val >> 1);

                    Negative = (val & (byte)SF.Negative) != 0;
                    Zero = val == 0;

                    if (mode == AddressMode.Accumulator) accumulator = val;
                    else system.Bus.CpuWrite(addr, val);

                    // EOR
                    accumulator ^= val;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;

            case Operation.Alr: {
                    // AND
                    accumulator &= GetValue(mode);
                    var val = accumulator;

                    // LSR
                    Carry = (val & 0b_0000_0001) != 0;

                    val = (byte)(val >> 1);

                    accumulator = val;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;

            case Operation.Rra: {
                    // ROR
                    byte value = GetValue(mode, out ushort addr);
                    bool oldbit0 = (value & 0b_0000_0001) != 0;

                    value = (byte)(value >> 1);
                    value |= (byte)(Carry ? 0b_1000_0000 : 0);
                    Carry = oldbit0;

                    system.Bus.CpuWrite(addr, value);

                    // ADC
                    byte a = accumulator;
                    byte b = value;

                    int res = (a + b + (Carry ? 1 : 0));
                    accumulator = (byte)res;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                    Carry = res > 255;
                    Overflow = (((a >> 7) & 1) ^ ((accumulator >> 7) & 1)) != 0;
                } break;

            case Operation.Arr: {
                    // AND
                    accumulator &= GetValue(mode);

                    // ROR
                    bool oldbit0 = (accumulator & 0b_0000_0001) != 0;

                    accumulator = (byte)(accumulator >> 1);
                    accumulator |= (byte)(Carry ? 0b_1000_0000 : 0);

                    // Weird flag update
                    Carry = (accumulator & (1 << 6)) != 0;
                    Overflow = (((accumulator >> 6) & 1) ^ ((accumulator >> 5) & 1)) != 0;

                    Negative = (accumulator & (byte)SF.Negative) != 0;
                    Zero = accumulator == 0;
                } break;

            default: throw new NotImplementedException($"{op}");
        }
    }

    private byte GetValue(AddressMode mode) => GetValue(mode, out _);
    private byte GetValue(AddressMode mode, out ushort addr)
    {
        addr = 0;
        switch (mode)
        {
            // Do not use addresses
            case AddressMode.Implied: return 0;
            case AddressMode.Immediate: return ReadCounter();
            case AddressMode.Accumulator: return accumulator;

            default:
                addr = GetAddress(mode);
                return MemRead(addr);
        }
    }
    private ushort GetAddress(AddressMode mode)
    {
        ushort addr;
        switch (mode)
        {
            case AddressMode.Immediate: return ReadCounter();

            case AddressMode.Absolute: return ReadCounterWord();
            case AddressMode.AbsoluteX: return (ushort)(ReadCounterWord() + indexX);
            case AddressMode.AbsoluteY: return (ushort)(ReadCounterWord() + indexY);

            case AddressMode.Indirect: 
                addr = ReadCounterWord();
                return MemReadWord(addr);
            case AddressMode.XIndirect:
                addr = (ushort)(ReadCounter() + indexX);
                if (addr > 0xFF) addr &= 0x00FF;
                return MemReadWord(addr);
            case AddressMode.IndirectY:
                addr = (ushort)(ReadCounter());
                if (addr > 0xFF) addr &= 0x00FF;
                return (ushort)(MemReadWord(addr) + indexY);

            case AddressMode.ZeroPage: return ReadCounter();
            case AddressMode.ZeroPageX: return (ushort)((ReadCounter() + indexX) & 0x00FF);
            case AddressMode.ZeroPageY: return (ushort)((ReadCounter() + indexY) & 0x00FF);

            case AddressMode.Relative: return (ushort)((sbyte)ReadCounter() + progCounter);

            default: throw new NotImplementedException();
        }
    }

    private static (Operation operation, AddressMode mode) DecodeOpCode(byte opCode)
    {
      
        return opCode switch
        {
            0x00 => (Operation.Brk, AddressMode.Implied),
            0x01 => (Operation.Ora, AddressMode.XIndirect),
            0x02 => (Operation.Kil, AddressMode.Implied),
            0x03 => (Operation.Slo, AddressMode.XIndirect),
            0x04 => (Operation.Nops, AddressMode.ZeroPage),
            0x05 => (Operation.Ora, AddressMode.ZeroPage),
            0x06 => (Operation.Asl, AddressMode.ZeroPage),
            0x07 => (Operation.Slo, AddressMode.ZeroPage),
            0x08 => (Operation.Php, AddressMode.Implied),
            0x09 => (Operation.Ora, AddressMode.Immediate),
            0x0A => (Operation.Asl, AddressMode.Accumulator),
            0x0B => (Operation.Anc, AddressMode.Immediate),
            0x0C => (Operation.Nops, AddressMode.Absolute),
            0x0D => (Operation.Ora, AddressMode.Absolute),
            0x0E => (Operation.Asl, AddressMode.Absolute),
            0x0F => (Operation.Slo, AddressMode.Absolute),

            // 0x10 - 0x1F
            0x10 => (Operation.Bpl, AddressMode.Relative),
            0x11 => (Operation.Ora, AddressMode.IndirectY),
            0x12 => (Operation.Kil, AddressMode.Implied),
            0x13 => (Operation.Slo, AddressMode.IndirectY),
            0x14 => (Operation.Nops, AddressMode.ZeroPageX),
            0x15 => (Operation.Ora, AddressMode.ZeroPageX),
            0x16 => (Operation.Asl, AddressMode.ZeroPageX),
            0x17 => (Operation.Slo, AddressMode.ZeroPageX),
            0x18 => (Operation.Clc, AddressMode.Implied),
            0x19 => (Operation.Ora, AddressMode.AbsoluteY),
            0x1A => (Operation.Nops, AddressMode.Implied),
            0x1B => (Operation.Slo, AddressMode.AbsoluteY),
            0x1C => (Operation.Nops, AddressMode.AbsoluteX),
            0x1D => (Operation.Ora, AddressMode.AbsoluteX),
            0x1E => (Operation.Asl, AddressMode.AbsoluteX),
            0x1F => (Operation.Slo, AddressMode.AbsoluteX),

            // 0x20 - 0x2F
            0x20 => (Operation.Jsr, AddressMode.Absolute),
            0x21 => (Operation.And, AddressMode.XIndirect),
            0x22 => (Operation.Kil, AddressMode.Implied),
            0x23 => (Operation.Rla, AddressMode.XIndirect),
            0x24 => (Operation.Bit, AddressMode.ZeroPage),
            0x25 => (Operation.And, AddressMode.ZeroPage),
            0x26 => (Operation.Rol, AddressMode.ZeroPage),
            0x27 => (Operation.Rla, AddressMode.ZeroPage),
            0x28 => (Operation.Plp, AddressMode.Implied),
            0x29 => (Operation.And, AddressMode.Immediate),
            0x2A => (Operation.Rol, AddressMode.Accumulator),
            0x2B => (Operation.Anc2, AddressMode.Immediate),
            0x2C => (Operation.Bit, AddressMode.Absolute),
            0x2D => (Operation.And, AddressMode.Absolute),
            0x2E => (Operation.Rol, AddressMode.Absolute),
            0x2F => (Operation.Rla, AddressMode.Absolute),

            // 0x30 - 0x3F
            0x30 => (Operation.Bmi, AddressMode.Relative),
            0x31 => (Operation.And, AddressMode.IndirectY),
            0x32 => (Operation.Kil, AddressMode.Implied),
            0x33 => (Operation.Rla, AddressMode.IndirectY),
            0x34 => (Operation.Nops, AddressMode.ZeroPageX),
            0x35 => (Operation.And, AddressMode.ZeroPageX),
            0x36 => (Operation.Rol, AddressMode.ZeroPageX),
            0x37 => (Operation.Rla, AddressMode.ZeroPageX),
            0x38 => (Operation.Sec, AddressMode.Implied),
            0x39 => (Operation.And, AddressMode.AbsoluteY),
            0x3A => (Operation.Nops, AddressMode.Implied),
            0x3B => (Operation.Rla, AddressMode.AbsoluteY),
            0x3C => (Operation.Nops, AddressMode.AbsoluteX),
            0x3D => (Operation.And, AddressMode.AbsoluteX),
            0x3E => (Operation.Rol, AddressMode.AbsoluteX),
            0x3F => (Operation.Rla, AddressMode.AbsoluteX),

            // 0x40 - 0x4F
            0x40 => (Operation.Rti, AddressMode.Implied),
            0x41 => (Operation.Eor, AddressMode.XIndirect),
            0x42 => (Operation.Kil, AddressMode.Implied),
            0x43 => (Operation.Sre, AddressMode.XIndirect),
            0x44 => (Operation.Nops, AddressMode.ZeroPage),
            0x45 => (Operation.Eor, AddressMode.ZeroPage),
            0x46 => (Operation.Lsr, AddressMode.ZeroPage),
            0x47 => (Operation.Sre, AddressMode.ZeroPage),
            0x48 => (Operation.Pha, AddressMode.Implied),
            0x49 => (Operation.Eor, AddressMode.Immediate),
            0x4A => (Operation.Lsr, AddressMode.Accumulator),
            0x4B => (Operation.Alr, AddressMode.Immediate),
            0x4C => (Operation.Jmp, AddressMode.Absolute),
            0x4D => (Operation.Eor, AddressMode.Absolute),
            0x4E => (Operation.Lsr, AddressMode.Absolute),
            0x4F => (Operation.Sre, AddressMode.Absolute),

            // 0x50 - 0x5F
            0x50 => (Operation.Bvc, AddressMode.Relative),
            0x51 => (Operation.Eor, AddressMode.IndirectY),
            0x52 => (Operation.Kil, AddressMode.Implied),
            0x53 => (Operation.Sre, AddressMode.IndirectY),
            0x54 => (Operation.Nops, AddressMode.ZeroPageX),
            0x55 => (Operation.Eor, AddressMode.ZeroPageX),
            0x56 => (Operation.Lsr, AddressMode.ZeroPageX),
            0x57 => (Operation.Sre, AddressMode.ZeroPageX),
            0x58 => (Operation.Cli, AddressMode.Implied),
            0x59 => (Operation.Eor, AddressMode.AbsoluteY),
            0x5A => (Operation.Nops, AddressMode.Implied),
            0x5B => (Operation.Sre, AddressMode.AbsoluteY),
            0x5C => (Operation.Nops, AddressMode.AbsoluteX),
            0x5D => (Operation.Eor, AddressMode.AbsoluteX),
            0x5E => (Operation.Lsr, AddressMode.AbsoluteX),
            0x5F => (Operation.Sre, AddressMode.AbsoluteX),

            // 0x60 - 0x6F
            0x60 => (Operation.Rts, AddressMode.Implied),
            0x61 => (Operation.Adc, AddressMode.XIndirect),
            0x62 => (Operation.Kil, AddressMode.Implied),
            0x63 => (Operation.Rra, AddressMode.XIndirect),
            0x64 => (Operation.Nops, AddressMode.ZeroPage),
            0x65 => (Operation.Adc, AddressMode.ZeroPage),
            0x66 => (Operation.Ror, AddressMode.ZeroPage),
            0x67 => (Operation.Rra, AddressMode.ZeroPage),
            0x68 => (Operation.Pla, AddressMode.Implied),
            0x69 => (Operation.Adc, AddressMode.Immediate),
            0x6A => (Operation.Ror, AddressMode.Accumulator),
            0x6B => (Operation.Arr, AddressMode.Immediate),
            0x6C => (Operation.Jmp, AddressMode.Indirect),
            0x6D => (Operation.Adc, AddressMode.Absolute),
            0x6E => (Operation.Ror, AddressMode.Absolute),
            0x6F => (Operation.Rra, AddressMode.Absolute),

            // 0x70 - 0x7F
            0x70 => (Operation.Bvs, AddressMode.Relative),
            0x71 => (Operation.Adc, AddressMode.IndirectY),
            0x72 => (Operation.Kil, AddressMode.Implied),
            0x73 => (Operation.Rra, AddressMode.IndirectY),
            0x74 => (Operation.Nops, AddressMode.ZeroPageX),
            0x75 => (Operation.Adc, AddressMode.ZeroPageX),
            0x76 => (Operation.Ror, AddressMode.ZeroPageX),
            0x77 => (Operation.Rra, AddressMode.ZeroPageX),
            0x78 => (Operation.Sei, AddressMode.Implied),
            0x79 => (Operation.Adc, AddressMode.AbsoluteY),
            0x7A => (Operation.Nops, AddressMode.Implied),
            0x7B => (Operation.Rra, AddressMode.AbsoluteY),
            0x7C => (Operation.Nops, AddressMode.AbsoluteX),
            0x7D => (Operation.Adc, AddressMode.AbsoluteX),
            0x7E => (Operation.Ror, AddressMode.AbsoluteX),
            0x7F => (Operation.Rra, AddressMode.AbsoluteX),

            // 0x80 - 0x8F
            0x80 => (Operation.Nops, AddressMode.Immediate),
            0x81 => (Operation.Sta, AddressMode.XIndirect),
            0x82 => (Operation.Nops, AddressMode.Immediate),
            0x83 => (Operation.Sax, AddressMode.XIndirect),
            0x84 => (Operation.Sty, AddressMode.ZeroPage),
            0x85 => (Operation.Sta, AddressMode.ZeroPage),
            0x86 => (Operation.Stx, AddressMode.ZeroPage),
            0x87 => (Operation.Sax, AddressMode.ZeroPage),
            0x88 => (Operation.Dey, AddressMode.Implied),
            0x89 => (Operation.Nops, AddressMode.Immediate),
            0x8A => (Operation.Txa, AddressMode.Implied),
            0x8B => (Operation.Ane, AddressMode.Immediate),
            0x8C => (Operation.Sty, AddressMode.Absolute),
            0x8D => (Operation.Sta, AddressMode.Absolute),
            0x8E => (Operation.Stx, AddressMode.Absolute),
            0x8F => (Operation.Sax, AddressMode.Absolute),

            // 0x90 - 0x9F
            0x90 => (Operation.Bcc, AddressMode.Relative),
            0x91 => (Operation.Sta, AddressMode.IndirectY),
            0x92 => (Operation.Kil, AddressMode.Implied),
            0x93 => (Operation.Sha, AddressMode.IndirectY),
            0x94 => (Operation.Sty, AddressMode.ZeroPageX),
            0x95 => (Operation.Sta, AddressMode.ZeroPageX),
            0x96 => (Operation.Stx, AddressMode.ZeroPageY),
            0x97 => (Operation.Sax, AddressMode.ZeroPageY),
            0x98 => (Operation.Tya, AddressMode.Implied),
            0x99 => (Operation.Sta, AddressMode.AbsoluteY),
            0x9A => (Operation.Txs, AddressMode.Implied),
            0x9B => (Operation.Tas, AddressMode.AbsoluteY),
            0x9C => (Operation.Shy, AddressMode.AbsoluteX),
            0x9D => (Operation.Sta, AddressMode.AbsoluteX),
            0x9E => (Operation.Shx, AddressMode.AbsoluteY),
            0x9F => (Operation.Sha, AddressMode.AbsoluteY),

            // 0xA0 - 0xAF
            0xA0 => (Operation.Ldy, AddressMode.Immediate),
            0xA1 => (Operation.Lda, AddressMode.XIndirect),
            0xA2 => (Operation.Ldx, AddressMode.Immediate),
            0xA3 => (Operation.Lax, AddressMode.XIndirect),
            0xA4 => (Operation.Ldy, AddressMode.ZeroPage),
            0xA5 => (Operation.Lda, AddressMode.ZeroPage),
            0xA6 => (Operation.Ldx, AddressMode.ZeroPage),
            0xA7 => (Operation.Lax, AddressMode.ZeroPage),
            0xA8 => (Operation.Tay, AddressMode.Implied),
            0xA9 => (Operation.Lda, AddressMode.Immediate),
            0xAA => (Operation.Tax, AddressMode.Implied),
            0xAB => (Operation.Lxa, AddressMode.Immediate),
            0xAC => (Operation.Ldy, AddressMode.Absolute),
            0xAD => (Operation.Lda, AddressMode.Absolute),
            0xAE => (Operation.Ldx, AddressMode.Absolute),
            0xAF => (Operation.Lax, AddressMode.Absolute),

            // 0xB0 - 0xBF
            0xB0 => (Operation.Bcs, AddressMode.Relative),
            0xB1 => (Operation.Lda, AddressMode.IndirectY),
            0xB2 => (Operation.Kil, AddressMode.Implied),
            0xB3 => (Operation.Lax, AddressMode.IndirectY),
            0xB4 => (Operation.Ldy, AddressMode.ZeroPageX),
            0xB5 => (Operation.Lda, AddressMode.ZeroPageX),
            0xB6 => (Operation.Ldx, AddressMode.ZeroPageY),
            0xB7 => (Operation.Lax, AddressMode.ZeroPageY),
            0xB8 => (Operation.Clv, AddressMode.Implied),
            0xB9 => (Operation.Lda, AddressMode.AbsoluteY),
            0xBA => (Operation.Tsx, AddressMode.Implied),
            0xBB => (Operation.Las, AddressMode.AbsoluteY),
            0xBC => (Operation.Ldy, AddressMode.AbsoluteX),
            0xBD => (Operation.Lda, AddressMode.AbsoluteX),
            0xBE => (Operation.Ldx, AddressMode.AbsoluteY),
            0xBF => (Operation.Lax, AddressMode.AbsoluteY),

            // 0xC0 - 0xCF
            0xC0 => (Operation.Cpy, AddressMode.Immediate),
            0xC1 => (Operation.Cmp, AddressMode.XIndirect),
            0xC2 => (Operation.Nops, AddressMode.Immediate),
            0xC3 => (Operation.Dcp, AddressMode.XIndirect),
            0xC4 => (Operation.Cpy, AddressMode.ZeroPage),
            0xC5 => (Operation.Cmp, AddressMode.ZeroPage),
            0xC6 => (Operation.Dec, AddressMode.ZeroPage),
            0xC7 => (Operation.Dcp, AddressMode.ZeroPage),
            0xC8 => (Operation.Iny, AddressMode.Implied),
            0xC9 => (Operation.Cmp, AddressMode.Immediate),
            0xCA => (Operation.Dex, AddressMode.Implied),
            0xCB => (Operation.Ane, AddressMode.Immediate),
            0xCC => (Operation.Cpy, AddressMode.Absolute),
            0xCD => (Operation.Cmp, AddressMode.Absolute),
            0xCE => (Operation.Dec, AddressMode.Absolute),
            0xCF => (Operation.Dcp, AddressMode.Absolute),

            // 0xD0 - 0xDF
            0xD0 => (Operation.Bne, AddressMode.Relative),
            0xD1 => (Operation.Cmp, AddressMode.IndirectY),
            0xD2 => (Operation.Kil, AddressMode.Implied),
            0xD3 => (Operation.Dcp, AddressMode.IndirectY),
            0xD4 => (Operation.Nops, AddressMode.ZeroPageX),
            0xD5 => (Operation.Cmp, AddressMode.ZeroPageX),
            0xD6 => (Operation.Dec, AddressMode.ZeroPageX),
            0xD7 => (Operation.Dcp, AddressMode.ZeroPageX),
            0xD8 => (Operation.Cld, AddressMode.Implied),
            0xD9 => (Operation.Cmp, AddressMode.AbsoluteY),
            0xDA => (Operation.Nops, AddressMode.Implied),
            0xDB => (Operation.Dcp, AddressMode.AbsoluteY),
            0xDC => (Operation.Nops, AddressMode.AbsoluteX),
            0xDD => (Operation.Cmp, AddressMode.AbsoluteX),
            0xDE => (Operation.Dec, AddressMode.AbsoluteX),
            0xDF => (Operation.Dcp, AddressMode.AbsoluteX),

            // 0xE0 - 0xEF
            0xE0 => (Operation.Cpx, AddressMode.Immediate),
            0xE1 => (Operation.Sbc, AddressMode.XIndirect),
            0xE2 => (Operation.Nops, AddressMode.Immediate),
            0xE3 => (Operation.Isc, AddressMode.XIndirect),
            0xE4 => (Operation.Cpx, AddressMode.ZeroPage),
            0xE5 => (Operation.Sbc, AddressMode.ZeroPage),
            0xE6 => (Operation.Inc, AddressMode.ZeroPage),
            0xE7 => (Operation.Isc, AddressMode.ZeroPage),
            0xE8 => (Operation.Inx, AddressMode.Implied),
            0xE9 => (Operation.Sbc, AddressMode.Immediate),
            0xEA => (Operation.Nop, AddressMode.Implied),
            0xEB => (Operation.Usbc, AddressMode.Immediate),
            0xEC => (Operation.Cpx, AddressMode.Absolute),
            0xED => (Operation.Sbc, AddressMode.Absolute),
            0xEE => (Operation.Inc, AddressMode.Absolute),
            0xEF => (Operation.Isc, AddressMode.Absolute),

            // 0xF0 - 0xFF
            0xF0 => (Operation.Beq, AddressMode.Relative),
            0xF1 => (Operation.Sbc, AddressMode.IndirectY),
            0xF2 => (Operation.Kil, AddressMode.Implied),
            0xF3 => (Operation.Isc, AddressMode.IndirectY),
            0xF4 => (Operation.Nops, AddressMode.ZeroPageX),
            0xF5 => (Operation.Sbc, AddressMode.ZeroPageX),
            0xF6 => (Operation.Inc, AddressMode.ZeroPageX),
            0xF7 => (Operation.Isc, AddressMode.ZeroPageX),
            0xF8 => (Operation.Sed, AddressMode.Implied),
            0xF9 => (Operation.Sbc, AddressMode.AbsoluteY),
            0xFA => (Operation.Nops, AddressMode.Implied),
            0xFB => (Operation.Isc, AddressMode.AbsoluteY),
            0xFC => (Operation.Nops, AddressMode.AbsoluteX),
            0xFD => (Operation.Sbc, AddressMode.AbsoluteX),
            0xFE => (Operation.Inc, AddressMode.AbsoluteX),
            0xFF => (Operation.Isc, AddressMode.AbsoluteX),
        };
    }

    private void DebugCPU()
    {
        ImGui.SetNextWindowSize(new(150, 270));
        ImGui.Begin("CPU Debug", ImGuiWindowFlags.NoResize);
        {
            ImGui.SeparatorText("Registers");

            ImGui.TextDisabled($"A: "); ImGui.SameLine();
            ImGui.Text($"{accumulator:X2} {accumulator}");
            ImGui.TextDisabled($"X: "); ImGui.SameLine();
            ImGui.Text($"{indexX:X2} {indexX}");
            ImGui.TextDisabled($"Y: "); ImGui.SameLine();
            ImGui.Text($"{indexY:X2} {indexY}");

            ImGui.NewLine();

            ImGui.TextDisabled($"SP: "); ImGui.SameLine();
            ImGui.Text($"{stackPointer:X4}");
            ImGui.TextDisabled($"PC: "); ImGui.SameLine();
            ImGui.Text($"{progCounter:X4}");

            ImGui.SeparatorText("Flags");

            if ((flags & (byte)SF.Carry) == (byte)SF.Carry) ImGui.Text("C"); else ImGui.TextDisabled("C"); ImGui.SameLine();
            if ((flags & (byte)SF.Zero) == (byte)SF.Zero) ImGui.Text("Z"); else ImGui.TextDisabled("Z"); ImGui.SameLine();
            if ((flags & (byte)SF.Interrupt) == (byte)SF.Interrupt) ImGui.Text("I"); else ImGui.TextDisabled("I"); ImGui.SameLine();
            if ((flags & (byte)SF.Decimal) == (byte)SF.Decimal) ImGui.Text("D"); else ImGui.TextDisabled("D"); ImGui.SameLine();
            if ((flags & (byte)SF.Break) == (byte)SF.Break) ImGui.Text("B"); else ImGui.TextDisabled("B"); ImGui.SameLine();
            ImGui.TextDisabled("-"); ImGui.SameLine();
            if ((flags & (byte)SF.Overflow) == (byte)SF.Overflow) ImGui.Text("O"); else ImGui.TextDisabled("O"); ImGui.SameLine();
            if ((flags & (byte)SF.Negative) == (byte)SF.Negative) ImGui.Text("N"); else ImGui.TextDisabled("N");

            ImGui.SeparatorText("Vectors:");

            ImGui.TextDisabled("NMI:"); ImGui.SameLine(); ImGui.Text($"${NonMaskableInterrupt:X4}");
            ImGui.TextDisabled("RES:"); ImGui.SameLine(); ImGui.Text($"${ResetVector:X4}");
            ImGui.TextDisabled("IRQ:"); ImGui.SameLine(); ImGui.Text($"${InterruptRequest:X4}");

            ImGui.Separator();

            ImGui.BeginTable("CPU controls", 3);

            ImGui.TableNextColumn();
            if (ImGui.Button(paused ? "Continue" : "Pause", new(-1, 20))) paused = !paused;

            ImGui.TableNextColumn();
            if (!paused) ImGui.BeginDisabled();
            if (ImGui.Button("Step", new(-1, 20))) doStep = true;
            if (!paused) ImGui.EndDisabled();

            ImGui.TableNextColumn();
            if (ImGui.Button("Reset", new(-1, 20))) Reset();

            ImGui.EndTable();
            
            ImGui.SeparatorText("Internal:");
            ImGui.TextDisabled("Instruction Clock Count:"); ImGui.SameLine();
            ImGui.Text($"{clockCount}");
            
        }
        ImGui.End();
    }
    

    public void SetProgramCounter(ushort value)
    {
        progCounter = value;
    }

    public void MemWrite(ushort addr, byte val)
    {
        clockCount++;
        system.Bus.CpuWrite(addr, val);
    }
    public byte MemRead(ushort addr)
    {
        clockCount++;
        return system.Bus.CpuRead(addr);
    }
    public ushort MemReadWord(ushort addr)
    {
        clockCount += 2;

        ushort addr1 = addr;
        ushort addr2 = (ushort)((addr & 0xFF00) | ((addr + 1) & 0x00FF));

        var a = system.Bus.CpuRead(addr1);
        var b = system.Bus.CpuRead(addr2);

        return (ushort)((b << 8) | a);
    }

    public void Push(byte val)
    {
        MemWrite(StackAddress, val);
        stackPointer--;
    }
    public void Push(ushort val)
    {
        var b = BitConverter.GetBytes(val);
        MemWrite(StackAddress, b[1]);
        stackPointer--;
        MemWrite(StackAddress, b[0]);
        stackPointer--;
    }

    public byte Pop()
    {
        stackPointer++;
        byte a = MemRead(StackAddress);
        return a;
    }
    public ushort PopAddress()
    {
        stackPointer++;
        byte a = MemRead(StackAddress);
        stackPointer++;
        byte b = MemRead(StackAddress);
        return (ushort)(b << 8 | a);
    }

    public byte ReadCounter()
    {
        clockCount++;
        return system.Bus.CpuRead(progCounter++);
    }
    public ushort ReadCounterWord()
    {
        return (ushort)(ReadCounter() | ReadCounter() << 8);
    }

    private enum Operation : byte
    {
        Adc, And, Asl, Bcc, Bcs, Beq, Bit, Bmi, Bne,
        Bpl, Brk, Bvc, Bvs, Clc, Cld, Cli, Clv, Cmp,
        Cpx, Cpy, Dec, Dex, Dey, Eor, Inc, Inx, Iny,
        Jmp, Jsr, Lda, Ldx, Ldy, Lsr, Nop, Ora, Pha,
        Php, Pla, Plp, Rol, Ror, Rti, Rts, Sbc, Sec,
        Sed, Sei, Sta, Stx, Sty, Tax, Tay, Tsx, Txa,
        Txs, Tya,

        // Illegal shit here

        Alr, Anc, Anc2, Ane, Arr, Dcp, Isc, Las, Lax,
        Lxa, Rla, Rra, Sax, Sbx, Sha, Shx, Shy, Slo,
        Sre, Tas, Usbc, Nops, Kil
    }

    private enum AddressMode : byte
    {
        Implied = 0,
        Accumulator,
        Absolute,
        AbsoluteX,
        AbsoluteY,
        Immediate,
        Indirect,
        XIndirect,
        IndirectY,
        Relative,
        ZeroPage,
        ZeroPageX,
        ZeroPageY
    }

    [Flags]
    private enum SF : byte
    {
        Carry     = 0b_00000001,
        Zero      = 0b_00000010,
        Interrupt = 0b_00000100,
        Decimal   = 0b_00001000,
        Break     = 0b_00010000,
        Overflow  = 0b_01000000,
        Negative  = 0b_10000000,
    }
    
}
