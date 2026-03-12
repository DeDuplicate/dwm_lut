using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LutCreator
{
    internal static class Injector
    {
        public static readonly bool NoDebug;

        private static readonly string DllName = "dwm_lut.dll";
        private static readonly string BasePath;
        private static readonly string DllPath;
        private static readonly string LutsPath;
        private static readonly IntPtr LoadlibraryA;
        private static readonly IntPtr FreeLibrary;

        static Injector()
        {
            BasePath = Environment.ExpandEnvironmentVariables("%SYSTEMROOT%\\Temp\\");
            DllPath = BasePath + DllName;
            LutsPath = BasePath + "luts\\";

            var kernel32 = GetModuleHandle("kernel32.dll");
            LoadlibraryA = GetProcAddress(kernel32, "LoadLibraryA");
            FreeLibrary = GetProcAddress(kernel32, "FreeLibrary");

            try
            {
                Process.EnterDebugMode();
            }
            catch
            {
                NoDebug = true;
            }
        }

        public static bool IsInjected()
        {
            if (NoDebug) return false;

            var dwmInstances = Process.GetProcessesByName("dwm");
            foreach (var dwm in dwmInstances)
            {
                try
                {
                    foreach (ProcessModule module in dwm.Modules)
                    {
                        if (module.ModuleName == DllName)
                        {
                            module.Dispose();
                            dwm.Dispose();
                            return true;
                        }
                        module.Dispose();
                    }
                }
                catch { }
                dwm.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Inject the DWM LUT DLL with a generated .cube file.
        /// The cubeContent is written to the luts folder before injection.
        /// </summary>
        public static void Inject(string cubeContent, string monitorPosition)
        {
            // Find the DLL - check app directory first, then DwmLutGUI output
            var localDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllName);
            if (!File.Exists(localDll))
            {
                throw new Exception(
                    "dwm_lut.dll not found.\n\n" +
                    "Copy dwm_lut.dll from DwmLutGUI build output to:\n" + localDll);
            }

            File.Copy(localDll, DllPath, true);
            ClearPermissions(DllPath);

            // Create luts folder
            if (Directory.Exists(LutsPath))
                Directory.Delete(LutsPath, true);

            Directory.CreateDirectory(LutsPath);
            ClearPermissions(LutsPath);

            // Write .cube file - filename encodes monitor position (e.g., "0_0.cube")
            var cubeFileName = monitorPosition.Replace(",", "_") + ".cube";
            var cubeFilePath = Path.Combine(LutsPath, cubeFileName);
            // Use explicit ASCII encoding (no BOM) to match what the DLL C parser expects
            File.WriteAllText(cubeFilePath, cubeContent, System.Text.Encoding.ASCII);
            ClearPermissions(cubeFilePath);

            // Inject DLL into dwm.exe
            bool failed = false;
            var bytes = Encoding.ASCII.GetBytes(DllPath);
            var dwmInstances = Process.GetProcessesByName("dwm");
            var currentSessionId = Process.GetCurrentProcess().SessionId;

            foreach (var dwm in dwmInstances)
            {
                if (dwm.SessionId != currentSessionId)
                {
                    dwm.Dispose();
                    continue;
                }

                var address = VirtualAllocEx(dwm.Handle, IntPtr.Zero, (UIntPtr)bytes.Length,
                    AllocationType.Reserve | AllocationType.Commit, MemoryProtection.ReadWrite);
                WriteProcessMemory(dwm.Handle, address, bytes, (UIntPtr)bytes.Length, out _);
                var thread = CreateRemoteThread(dwm.Handle, IntPtr.Zero, 0, LoadlibraryA, address, 0, out _);
                WaitForSingleObject(thread, uint.MaxValue);

                GetExitCodeThread(thread, out var exitCode);
                if (exitCode == 0) failed = true;

                CloseHandle(thread);
                VirtualFreeEx(dwm.Handle, address, 0, FreeType.Release);
                dwm.Dispose();
            }

            // Clean up luts folder (DLL already read the files)
            try { Directory.Delete(LutsPath, true); } catch { }

            if (failed)
            {
                try { File.Delete(DllPath); } catch { }
                throw new Exception("Failed to load DLL into DWM. Check that dwm_lut.dll is the correct version.");
            }
        }

        public static void Uninject()
        {
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            var dwmInstances = Process.GetProcessesByName("dwm");

            foreach (var dwm in dwmInstances)
            {
                if (dwm.SessionId != currentSessionId)
                {
                    dwm.Dispose();
                    continue;
                }

                try
                {
                    foreach (ProcessModule module in dwm.Modules)
                    {
                        if (module.ModuleName == DllName)
                        {
                            var thread = CreateRemoteThread(dwm.Handle, IntPtr.Zero, 0,
                                FreeLibrary, module.BaseAddress, 0, out _);
                            WaitForSingleObject(thread, 3000);
                            CloseHandle(thread);
                        }
                        module.Dispose();
                    }
                }
                catch { }

                dwm.Dispose();
            }
        }

        /// <summary>
        /// Check if DwmLutGUI.exe is running (which would conflict with us).
        /// </summary>
        public static bool IsDwmLutGuiRunning()
        {
            return Process.GetProcessesByName("DwmLutGUI").Length > 0;
        }

        /// <summary>
        /// Uninject then immediately reinject with new LUT data.
        /// Matches DwmLutGUI's approach: no delay between uninject and inject.
        /// The DLL's DLL_PROCESS_DETACH handles cleanup internally,
        /// and WaitForSingleObject in Uninject ensures FreeLibrary completes.
        /// </summary>
        public static void ReInject(string cubeContent, string monitorPosition)
        {
            Uninject();
            Inject(cubeContent, monitorPosition);
        }

        private static void ClearPermissions(string path)
        {
            var hFile = CreateFile(path, DesiredAccess.ReadControl | DesiredAccess.WriteDac, 0, IntPtr.Zero,
                CreationDisposition.OpenExisting,
                FlagsAndAttributes.FileAttributeNormal | FlagsAndAttributes.FileFlagBackupSemantics,
                IntPtr.Zero);
            SetSecurityInfo(hFile, SeObjectType.SeFileObject, SecurityInformation.DaclSecurityInformation,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            CloseHandle(hFile);
        }

        #region P/Invoke

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpFileName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize,
            AllocationType flAllocationType, MemoryProtection flProtect);

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
            UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, FreeType dwFreeType);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateFile(string lpFileName, DesiredAccess dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, CreationDisposition dwCreationDisposition,
            FlagsAndAttributes dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("advapi32.dll")]
        private static extern uint SetSecurityInfo(IntPtr handle, SeObjectType ObjectType,
            SecurityInformation SecurityInfo, IntPtr psidOwner, IntPtr psidGroup, IntPtr pDacl, IntPtr pSacl);

        [Flags]
        private enum FreeType { Release = 0x8000 }

        [Flags]
        private enum AllocationType { Commit = 0x1000, Reserve = 0x2000 }

        [Flags]
        private enum MemoryProtection { ReadWrite = 0x04 }

        [Flags]
        private enum DesiredAccess { ReadControl = 0x20000, WriteDac = 0x40000 }

        private enum CreationDisposition { OpenExisting = 3 }

        [Flags]
        private enum FlagsAndAttributes { FileAttributeNormal = 0x80, FileFlagBackupSemantics = 0x2000000 }

        private enum SeObjectType { SeFileObject = 1 }

        private enum SecurityInformation { DaclSecurityInformation = 0x4 }

        #endregion
    }
}
