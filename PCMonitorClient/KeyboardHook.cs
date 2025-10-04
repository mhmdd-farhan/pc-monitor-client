using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

public class KeyboardHook : IDisposable
{
    // CRITICAL: Keep strong reference to prevent GC
    private static LowLevelKeyboardProc _proc;
    private IntPtr _hookID = IntPtr.Zero;
    private bool _isDisposed = false;

    // Delegate for the hook callback
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Constants
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    // Constructor
    public KeyboardHook()
    {
        // Store as static to prevent GC
        _proc = HookCallback;
    }

    // Set the hook
    public void SetHook()
    {
        if (_hookID != IntPtr.Zero)
        {
            Debug.WriteLine("Hook already set, skipping...");
            return;
        }

        try
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                // FIX: Use underscore, not asterisk!
                _hookID = SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    _proc,
                    GetModuleHandle(curModule.ModuleName),
                    0);

                if (_hookID == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"Failed to set hook. Error code: {errorCode}");
                    throw new System.ComponentModel.Win32Exception(errorCode);
                }

                Debug.WriteLine("Keyboard hook set successfully");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error setting keyboard hook: {ex.Message}");
            throw;
        }
    }

    // Remove the hook
    public void Dispose()
    {
        if (_isDisposed)
            return;

        try
        {
            if (_hookID != IntPtr.Zero)
            {
                bool success = UnhookWindowsHookEx(_hookID);
                if (success)
                {
                    Debug.WriteLine("Keyboard hook removed successfully");
                }
                else
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"Failed to unhook. Error code: {errorCode}");
                }
                _hookID = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error disposing keyboard hook: {ex.Message}");
        }
        finally
        {
            _isDisposed = true;
        }
    }

    // The callback method
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                // Marshal the data from lParam
                KBDLLHOOKSTRUCT kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Block Ctrl+Alt+Del combinations
                if (kbStruct.vkCode == 0xA2 || kbStruct.vkCode == 0xA3 ||
                    kbStruct.vkCode == 0xA4 || kbStruct.vkCode == 0xA5)
                {
                    return (IntPtr)1;
                }

                // Block Windows key
                if (kbStruct.vkCode == 0x5B || kbStruct.vkCode == 0x5C)
                {
                    return (IntPtr)1; // Block key
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in HookCallback: {ex.Message}");
            // Don't block on error, pass through
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    // Struct for keyboard data
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // PInvoke declarations
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}