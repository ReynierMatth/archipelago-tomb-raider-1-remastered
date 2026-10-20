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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr hProcess, IntPtr lpThreadAttributes, UIntPtr dwStackSize,
        IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize,
        uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize,
        uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [StructLayout(LayoutKind.Sequential)]
    private struct THREADENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ThreadID;
        public uint th32OwnerProcessID;
        public int tpBasePri;
        public int tpDeltaPri;
        public uint dwFlags;
    }

    private const int PROCESS_VM_READ = 0x0010;
    private const int PROCESS_VM_WRITE = 0x0020;
    private const int PROCESS_VM_OPERATION = 0x0008;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_CREATE_THREAD = 0x0002;

    private const uint TH32CS_SNAPTHREAD = 0x00000004;
    private const uint THREAD_SUSPEND_RESUME = 0x0002;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    private const uint MEM_COMMIT = 0x00001000;
    private const uint MEM_RESERVE = 0x00002000;
    private const uint MEM_RELEASE = 0x00008000;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
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
            PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION | PROCESS_CREATE_THREAD,
            false,
            _gameProcess.Id);

        if (_processHandle == IntPtr.Zero)
            return false;

        _exeBaseAddress = _gameProcess.MainModule?.BaseAddress ?? IntPtr.Zero;
        _tomb1DllBase = FindModuleBase(TR1RMemoryMap.TR1ModuleName);

        // Offset layer keys its manifest on the on-disk build of the host exe.
        OffsetModel.CurrentExePath ??= _gameProcess.MainModule?.FileName;

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

    /// <summary>Writes a 16-bit integer to process memory.</summary>
    public bool WriteInt16(IntPtr address, short value)
        => Write(address, value);

    // =================================================================
    // REMOTE CALL (CreateRemoteThread)
    // =================================================================

    /// <summary>
    /// Calls a function inside the attached process via CreateRemoteThread.
    /// The target must have the signature DWORD fn(int arg) — arg arrives in RCX
    /// (x64 first parameter / thread parameter). All other threads of the target
    /// process are suspended while the call runs so it does not race the game's
    /// main loop; they are always resumed in finally.
    /// Returns the remote function's return value (EAX) or null on failure/timeout.
    /// On timeout the remote thread is left to finish once threads resume; the
    /// caller should treat null as "retry later". Exit codes are informational
    /// only — callers must verify effects (e.g. ring read-back), because
    /// Inv_AddItem returns 0 both for success and refusal in some paths.
    /// </summary>
    public uint? CallRemote(IntPtr func, int arg, int timeoutMs = 2000)
    {
        if (!IsAttached || _processHandle == IntPtr.Zero || func == IntPtr.Zero || _gameProcess == null)
            return null;

        var suspended = SuspendOtherThreads();
        IntPtr hRemote = IntPtr.Zero;
        try
        {
            hRemote = CreateRemoteThread(_processHandle, IntPtr.Zero, UIntPtr.Zero,
                func, new IntPtr(arg), 0, out _);
            if (hRemote == IntPtr.Zero)
                return null;

            if (WaitForSingleObject(hRemote, (uint)timeoutMs) != WAIT_OBJECT_0)
                return null;

            if (!GetExitCodeThread(hRemote, out uint exitCode))
                return null;

            return exitCode;
        }
        finally
        {
            if (hRemote != IntPtr.Zero)
                CloseHandle(hRemote);
            ResumeThreads(suspended);
        }
    }

    /// <summary>
    /// Calls a function inside the attached process with all three x64 register
    /// parameters set explicitly — <c>DWORD fn(long rcx, long rdx, long r8d)</c>.
    ///
    /// <see cref="CallRemote"/> can only hand its single argument to RCX (the
    /// thread parameter), which is enough for Inv_AddItem but not for
    /// three-argument entry points such as Sound_Effect(sfx, pos, flags).
    /// This variant allocates a 42-byte trampoline inside the target that loads
    /// RCX/RDX/R8 and jumps to <paramref name="func"/>; the target's own RET then
    /// returns straight to the thread start routine, so EAX becomes the thread
    /// exit code.
    ///
    /// Same suspension contract as CallRemote: every other thread of the target
    /// is suspended while the call runs, so it cannot race the game's main loop.
    /// Returns the target's return value or null on failure/timeout.
    ///
    /// The trampoline page is freed only after the remote thread has finished.
    /// On timeout it is deliberately leaked — the thread may still be executing
    /// it, and freeing it under a live thread would crash the game.
    /// </summary>
    public uint? CallRemote3(IntPtr func, long rcx, long rdx, long r8d, int timeoutMs = 2000)
    {
        if (!IsAttached || _processHandle == IntPtr.Zero || func == IntPtr.Zero || _gameProcess == null)
            return null;

        byte[] stub = BuildTrampoline(func, rcx, rdx, r8d);
        IntPtr remote = VirtualAllocEx(_processHandle, IntPtr.Zero, (UIntPtr)stub.Length,
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        if (remote == IntPtr.Zero)
            return null;

        if (!WriteProcessMemory(_processHandle, remote, stub, stub.Length, out int written) ||
            written != stub.Length)
        {
            VirtualFreeEx(_processHandle, remote, UIntPtr.Zero, MEM_RELEASE);
            return null;
        }

        var suspended = SuspendOtherThreads();
        IntPtr hRemote = IntPtr.Zero;
        bool finished = false;
        try
        {
            hRemote = CreateRemoteThread(_processHandle, IntPtr.Zero, UIntPtr.Zero,
                remote, IntPtr.Zero, 0, out _);
            if (hRemote == IntPtr.Zero)
                return null;

            if (WaitForSingleObject(hRemote, (uint)timeoutMs) != WAIT_OBJECT_0)
                return null;

            if (!GetExitCodeThread(hRemote, out uint exitCode))
                return null;

            finished = true;
            return exitCode;
        }
        finally
        {
            if (hRemote != IntPtr.Zero)
                CloseHandle(hRemote);
            ResumeThreads(suspended);
            if (finished)
                VirtualFreeEx(_processHandle, remote, UIntPtr.Zero, MEM_RELEASE);
        }
    }

    /// <summary>
    /// Builds the in-process trampoline:
    ///   mov rcx,imm64 / mov rdx,imm64 / mov r8,imm64 / mov rax,func / jmp rax
    /// 42 bytes. The jmp is a tail call, so no return address is pushed here.
    /// </summary>
    private static byte[] BuildTrampoline(IntPtr func, long rcx, long rdx, long r8d)
    {
        var stub = new byte[42];
        int i = 0;

        void Op(params byte[] bytes)
        {
            foreach (byte b in bytes) stub[i++] = b;
        }

        void Imm64(long value)
        {
            foreach (byte b in BitConverter.GetBytes(value)) stub[i++] = b;
        }

        Op(0x48, 0xB9); Imm64(rcx);                          // mov rcx, imm64
        Op(0x48, 0xBA); Imm64(rdx);                          // mov rdx, imm64
        Op(0x49, 0xB8); Imm64(r8d);                          // mov r8,  imm64
        Op(0x48, 0xB8); Imm64(func.ToInt64());               // mov rax, func
        Op(0xFF, 0xE0);                                      // jmp rax
        return stub;
    }

    /// <summary>
    /// Suspends every thread of the attached game except the caller's own, so a
    /// remote call cannot race the game's main loop. Handles are returned for
    /// <see cref="ResumeThreads"/>; they are always resumed by the caller's finally.
    /// </summary>
    private List<IntPtr> SuspendOtherThreads()
    {
        var suspended = new List<IntPtr>();
        if (_gameProcess == null)
            return suspended;

        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
        if (snapshot != new IntPtr(-1))
        {
            var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
            if (Thread32First(snapshot, ref te))
            {
                do
                {
                    if (te.th32OwnerProcessID != (uint)_gameProcess.Id)
                        continue;
                    IntPtr hThread = OpenThread(THREAD_SUSPEND_RESUME, false, te.th32ThreadID);
                    if (hThread == IntPtr.Zero)
                        continue;
                    if (SuspendThread(hThread) != 0xFFFFFFFF)
                        suspended.Add(hThread);
                    else
                        CloseHandle(hThread);
                } while (Thread32Next(snapshot, ref te));
            }
            CloseHandle(snapshot);
        }
        return suspended;
    }

    private void ResumeThreads(List<IntPtr> suspended)
    {
        foreach (IntPtr h in suspended)
        {
            ResumeThread(h);
            CloseHandle(h);
        }
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

    private const uint PAGE_READWRITE = 0x04;
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
