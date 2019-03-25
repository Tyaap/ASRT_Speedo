﻿using System;
using System.Runtime.InteropServices;
using System.Threading;
using static Speedo.NativeMethods;

namespace Speedo.Hook
{
    public sealed class DebugMonitor
    {
        private static IntPtr m_AckEvent = IntPtr.Zero;
        private static IntPtr m_ReadyEvent = IntPtr.Zero;
        private static IntPtr m_SharedFile = IntPtr.Zero;
        private static IntPtr m_SharedMem = IntPtr.Zero;
        private static Thread m_Capturer = null;
        private static readonly object m_SyncRoot = new object();
        private static Mutex m_Mutex = null;

        private DebugMonitor()
        {
        }

        public static event EventHandler<OnOutputDebugStringEventArgs> OnOutputDebugStringHandler = delegate { };

        public static void Start()
        {
            lock (m_SyncRoot)
            {
                if (m_Capturer != null)
                {
                    throw new ApplicationException("This DebugMonitor is already started.");
                }

                if (Environment.OSVersion.ToString().IndexOf("Microsoft") == -1)
                {
                    throw new NotSupportedException("This DebugMonitor is only supported on Microsoft operating systems.");
                }

                m_Mutex = new Mutex(false, typeof(DebugMonitor).Namespace, out bool createdNew);
                if (!createdNew)
                {
                    throw new ApplicationException("There is already an instance of 'DbMon.NET' running.");
                }

                SECURITY_DESCRIPTOR sd = new SECURITY_DESCRIPTOR();
                if (!InitializeSecurityDescriptor(ref sd, 1U))
                {
                    throw CreateApplicationException("Failed to initializes the security descriptor.");
                }

                if (!SetSecurityDescriptorDacl(ref sd, true, IntPtr.Zero, false))
                {
                    throw CreateApplicationException("Failed to initializes the security descriptor");
                }

                SECURITY_ATTRIBUTES securityAttributes = new SECURITY_ATTRIBUTES();
                m_AckEvent = CreateEvent(ref securityAttributes, false, false, "DBWIN_BUFFER_READY");
                if (m_AckEvent == IntPtr.Zero)
                {
                    throw CreateApplicationException("Failed to create event 'DBWIN_BUFFER_READY'");
                }

                m_ReadyEvent = CreateEvent(ref securityAttributes, false, false, "DBWIN_DATA_READY");
                if (m_ReadyEvent == IntPtr.Zero)
                {
                    throw CreateApplicationException("Failed to create event 'DBWIN_DATA_READY'");
                }

                m_SharedFile = CreateFileMapping(new IntPtr(-1), ref securityAttributes, PageProtection.ReadWrite, 0U, 4096U, "DBWIN_BUFFER");
                if (m_SharedFile == IntPtr.Zero)
                {
                    throw CreateApplicationException("Failed to create a file mapping to slot 'DBWIN_BUFFER'");
                }

                m_SharedMem = MapViewOfFile(m_SharedFile, 4, 0U, 0U, (IntPtr)512);
                if (m_SharedMem == IntPtr.Zero)
                {
                    throw CreateApplicationException("Failed to create a mapping view for slot 'DBWIN_BUFFER'");
                }

                m_Capturer = new Thread(new ThreadStart(Capture));
                m_Capturer.Start();
            }
        }

        private static void Capture()
        {
            IntPtr ptr = new IntPtr(m_SharedMem.ToInt32() + Marshal.SizeOf(typeof(int)));
            while (true)
            {
                SetEvent(m_AckEvent);
                int num = WaitForSingleObject(m_ReadyEvent, uint.MaxValue);
                if (m_Capturer != null)
                {
                    if (num == 0)
                    {
                        FireOnOutputDebugString(Marshal.ReadInt32(m_SharedMem), Marshal.PtrToStringAnsi(ptr));
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private static void FireOnOutputDebugString(int pid, string text)
        {
            OnOutputDebugStringHandler(null, new OnOutputDebugStringEventArgs(pid, text));
        }

        public static void Dispose()
        {
            if (m_AckEvent != IntPtr.Zero)
            {
                if (!CloseHandle(m_AckEvent))
                {
                    throw CreateApplicationException("Failed to close handle for 'AckEvent'");
                }

                m_AckEvent = IntPtr.Zero;
            }
            if (m_ReadyEvent != IntPtr.Zero)
            {
                if (!CloseHandle(m_ReadyEvent))
                {
                    throw CreateApplicationException("Failed to close handle for 'ReadyEvent'");
                }

                m_ReadyEvent = IntPtr.Zero;
            }
            if (m_SharedFile != IntPtr.Zero)
            {
                if (!CloseHandle(m_SharedFile))
                {
                    throw CreateApplicationException("Failed to close handle for 'SharedFile'");
                }

                m_SharedFile = IntPtr.Zero;
            }
            if (m_SharedMem != IntPtr.Zero)
            {
                if (!UnmapViewOfFile(m_SharedMem))
                {
                    throw CreateApplicationException("Failed to unmap view for slot 'DBWIN_BUFFER'");
                }

                m_SharedMem = IntPtr.Zero;
            }
            if (m_Mutex == null)
            {
                return;
            }

            m_Mutex.Close();
            m_Mutex = null;
        }

        public static void Stop()
        {
            lock (m_SyncRoot)
            {
                if (m_Capturer == null)
                {
                    throw new ObjectDisposedException(nameof(DebugMonitor), "This DebugMonitor is not running.");
                }

                m_Capturer = null;
                PulseEvent(m_ReadyEvent);
                while (m_AckEvent != IntPtr.Zero)
                {
                    ;
                }
            }
        }

        private static ApplicationException CreateApplicationException(string text)
        {
            int error = Marshal.GetLastWin32Error();

            if (text == null || text.Length < 1)
            {
                throw new ArgumentNullException(nameof(text), "'text' may not be empty or null.");
            }

            return new ApplicationException(string.Format("{0}. Last Win32 Error was {1}", text, error));
        }
    }
}
