using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Low-level process memory access using P/Invoke on Windows.
/// Attaches to tomb123.exe and resolves tomb1.dll module base address.
/// Provides read/write methods for polling game state at runtime.
/// </summary>
public class ProcessMemory : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int PROCESS_VM_READ = 0x0010;
    private const int PROCESS_VM_WRITE = 0x0020;
    private const int PROCESS_VM_OPERATION = 0x0008;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;

    private IntPtr _processHandle;
    private Process? _gameProcess;
    private IntPtr _exeBaseAddress;
    private IntPtr _tomb1DllBase;

    public bool IsAttached => _processHandle != IntPtr.Zero && _gameProcess is { HasExited: false };

    /// <summary>Base address of tomb123.exe module.</summary>
    public IntPtr ExeBase => _exeBaseAddress;

    /// <summary>Base address of tomb1.dll module. Zero if not loaded.</summary>
    public IntPtr Tomb1Base => _tomb1DllBase;

    /// <summary>
    /// Tries to find and attach to the tomb123.exe process.
    /// Also resolves tomb1.dll module base address.
    /// </summary>
    public bool TryAttach()
    {
        var processes = Process.GetProcessesByName(TR1RMemoryMap.HostProcessName);
        if (processes.Length == 0)
            return false;

        _gameProcess = processes[0];

        _processHandle = OpenProcess(
            PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION,
            false,
            _gameProcess.Id);

        if (_processHandle == IntPtr.Zero)
            return false;

        _exeBaseAddress = _gameProcess.MainModule?.BaseAddress ?? IntPtr.Zero;
        _tomb1DllBase = FindModuleBase(TR1RMemoryMap.TR1ModuleName);

        return true;
    }

    /// <summary>
    /// Refreshes the tomb1.dll base address (call if DLL was loaded after attach).
    /// </summary>
    public bool RefreshTomb1Base()
    {
        if (!IsAttached) return false;

        try
        {
            _gameProcess!.Refresh();
            _tomb1DllBase = FindModuleBase(TR1RMemoryMap.TR1ModuleName);
            return _tomb1DllBase != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the base address of a named module within the attached process.
    /// </summary>
    private IntPtr FindModuleBase(string moduleName)
    {
        if (_gameProcess == null) return IntPtr.Zero;

        try
        {
            foreach (ProcessModule module in _gameProcess.Modules)
            {
                if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    return module.BaseAddress;
                }
            }
        }
        catch
        {
            // Access denied or process exited
        }

        return IntPtr.Zero;
    }

    // =================================================================
    // READ METHODS
    // =================================================================

    /// <summary>Reads raw bytes from process memory.</summary>
    public byte[] ReadBytes(IntPtr address, int size)
    {
        byte[] buffer = new byte[size];
        ReadProcessMemory(_processHandle, address, buffer, size, out _);
        return buffer;
    }

    /// <summary>Reads a struct from process memory.</summary>
    public T Read<T>(IntPtr address) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = ReadBytes(address, size);
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Reads a byte at the given address.</summary>
    public byte ReadByte(IntPtr address)
    {
        byte[] buffer = ReadBytes(address, 1);
        return buffer[0];
    }

    /// <summary>Reads an Int16 at the given address.</summary>
    public short ReadInt16(IntPtr address)
        => Read<short>(address);

    /// <summary>Reads a UInt16 at the given address.</summary>
    public ushort ReadUInt16(IntPtr address)
        => Read<ushort>(address);

    /// <summary>Reads an Int32 at the given address.</summary>
    public int ReadInt32(IntPtr address)
        => Read<int>(address);

    /// <summary>Reads a UInt32 at the given address.</summary>
    public uint ReadUInt32(IntPtr address)
        => Read<uint>(address);

    /// <summary>Reads an Int64 at the given address.</summary>
    public long ReadInt64(IntPtr address)
        => Read<long>(address);

    /// <summary>
    /// Reads a 64-bit pointer from process memory and returns it as IntPtr.
    /// Used for dereferencing pointer chains (e.g., LaraBase -> ITEM struct).
    /// </summary>
    public IntPtr ReadPointer(IntPtr address)
    {
        long value = ReadInt64(address);
        return new IntPtr(value);
    }

    // Convenience overloads with base + offset

    /// <summary>Reads a byte at base + offset.</summary>
    public byte ReadByte(IntPtr baseAddr, int offset)
        => ReadByte(baseAddr + offset);

    /// <summary>Reads an Int16 at base + offset.</summary>
    public short ReadInt16(IntPtr baseAddr, int offset)
        => ReadInt16(baseAddr + offset);

    /// <summary>Reads a UInt16 at base + offset.</summary>
    public ushort ReadUInt16(IntPtr baseAddr, int offset)
        => ReadUInt16(baseAddr + offset);

    /// <summary>Reads an Int32 at base + offset.</summary>
    public int ReadInt32(IntPtr baseAddr, int offset)
        => ReadInt32(baseAddr + offset);

    /// <summary>Reads a pointer at base + offset.</summary>
    public IntPtr ReadPointer(IntPtr baseAddr, int offset)
        => ReadPointer(baseAddr + offset);

    // =================================================================
    // WRITE METHODS
    // =================================================================

    /// <summary>Writes raw bytes to process memory.</summary>
    public bool WriteBytes(IntPtr address, byte[] data)
    {
        return WriteProcessMemory(_processHandle, address, data, data.Length, out _);
    }

    /// <summary>Writes a struct value to process memory.</summary>
    public bool Write<T>(IntPtr address, T value) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
        }
        finally
        {
            handle.Free();
        }
        return WriteBytes(address, buffer);
    }

    // =================================================================
    // AOB SCANNING
    // =================================================================

    /// <summary>
    /// Scans for an Array of Bytes pattern in process memory.
    /// Used to find addresses via byte patterns (ASLR-resistant).
    /// Wildcard bytes are represented as null.
    /// </summary>
    // =================================================================
    // VIRTUAL MEMORY ENUMERATION (for heap scanning)
    // =================================================================

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint PAGE_WRITECOPY = 0x08;

    /// <summary>
    /// Enumerates all readable/writable committed memory regions of the process.
    /// Returns list of (baseAddress, size) tuples.
    /// </summary>
    public List<(IntPtr Address, int Size)> GetReadableRegions(long maxAddress = 0x7FFFFFFFFFFF)
    {
        var regions = new List<(IntPtr, int)>();
        IntPtr address = IntPtr.Zero;

        while ((long)address < maxAddress)
        {
            int result = VirtualQueryEx(_processHandle, address, out var info, Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
            if (result == 0) break;

            if (info.State == MEM_COMMIT &&
                (info.Protect == PAGE_READWRITE || info.Protect == PAGE_EXECUTE_READWRITE || info.Protect == PAGE_WRITECOPY))
            {
                long size = (long)info.RegionSize;
                if (size > 0 && size <= 100_000_000) // skip absurdly large regions
                    regions.Add((info.BaseAddress, (int)size));
            }

            long next = (long)info.BaseAddress + (long)info.RegionSize;
            if (next <= (long)address) break; // overflow guard
            address = new IntPtr(next);
        }

        return regions;
    }

    public IntPtr AOBScan(byte?[] pattern, IntPtr startAddress, int scanSize)
    {
        byte[] memory = ReadBytes(startAddress, scanSize);

        for (int i = 0; i <= memory.Length - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (pattern[j].HasValue && memory[i + j] != pattern[j].Value)
                {
                    found = false;
                    break;
                }
            }
            if (found)
                return startAddress + i;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_processHandle != IntPtr.Zero)
        {
            CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
        _gameProcess?.Dispose();
    }
}
