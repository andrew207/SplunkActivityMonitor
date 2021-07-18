namespace SplunkActivityMonitor
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Management;
    using System.Runtime.InteropServices;
    using System.Text;

    public class Monitor : IDisposable
    {
        WinEventDelegate dele;
        private IntPtr foregroundhook;

        // Constants from Windows.h
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const string Format = "yyyy-MM-dd HH:mm:ss.fff";

        // Web requester for sending data to Splunk
        public static WebReq w;
        private USBWatcher u;
        private BackgroundWorker bgwDriveDetector;

        // Required to map hook to function
        internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        // per https://msdn.microsoft.com/library/ms182161.aspx, 
        internal static class NativeMethods
        {
            // Required to set hook on foreground context switch
            [DllImport("user32.dll")]
            internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

            // Required to kill hook
            [DllImport("user32.dll")]
            internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

            // Required to free up resources / threads once hook is closed
            [DllImport("Ole32.dll")]
            internal static extern void CoUninitialize();

            // Required to get current foreground window hwnd
            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();

            // Required to get current foreground window title
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

            // Required to get PID related to current window hwnd
            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        }

        /// <summary>
        /// Returns string containing JSON Splunk Event with active window and process details
        /// </summary>
        public string GetActiveWindowTitle()
        {
            const int nChars = 256;
            IntPtr handle = IntPtr.Zero;
            StringBuilder buff = new StringBuilder(nChars);
            handle = NativeMethods.GetForegroundWindow();

            uint processId = 0;
            string directory = "unknown";

            // \ replace to \\ is so we still have a valid JSON string
            DateTime myDateTime = DateTime.Now;
            string sqlFormattedDate = myDateTime.ToString(Format).Replace(@"\", @"\\");
            string userName = System.Security.Principal.WindowsIdentity.GetCurrent().Name.Replace(@"\", @"\\");
            string version = "0";
            string name = "unknown";

            // May throw on special processes like Task Manager / windows lock screen
            try
            {
                if (NativeMethods.GetWindowThreadProcessId(handle, out processId) > 0)
                {
                    directory = (Process.GetProcessById((int)processId).MainModule.FileName);
                }
            }
            catch (Exception e)
            {
                directory = "unknown - " + e.Message;
            }
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(directory);
                name = info.ProductName.Replace(@"\", @"\\");
                version = info.ProductVersion.Replace(@"\", @"\\");
            }
            catch (Exception e)
            {
                name = "unknown - " + e.Message;
                version = "unknown - " + e.Message;
            }

            // If we are able to get the Title from the window send it, otherwise send [blank].
            if (NativeMethods.GetWindowText(handle, buff, nChars) > 0)
                return "\"time\":\"" + sqlFormattedDate + "\", \"user\":\"" + userName + "\", \"title\":\"" + buff.ToString() + "\", \"directory\":\"" + directory.Replace(@"\", @"\\") + "\", \"pid\":\"" + processId + "\", \"name\":\"" + name + "\", \"version\":\"" + version + "\"";
            else
                return "\"time\":\"" + sqlFormattedDate + "\", \"user\":\"" + userName + "\", \"title\":\"[blank]\", \"directory\":\"" + directory.Replace(@"\", @"\\") + "\", \"pid\":\"" + processId + "\", \"name\":\"" + name + "\", \"version\":\"" + version + "\"";
        }

        /// <summary>
        /// Gets currently active window details then hands of details to a web request
        /// </summary>
        public void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            string res = GetActiveWindowTitle();
            Debug.WriteLine("Hook triggered: " + hwnd);

            if (Program.DebugMode)
                Program.WriteToFile(res);
            w.StartWebRequest(res, true, false);
        }

        /// <summary>
        /// Activates hook to EVENT_SYSTEM_FOREGROUND, parsing our delegate as WinEventProc. 
        /// </summary>
        public void StartHook()
        {
            dele = new WinEventDelegate(WinEventProc);
            foregroundhook = NativeMethods.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, dele, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        /// <summary>
        /// Device Inserted callback -- sends off web request with new device details
        /// </summary>
        private void DeviceInsertedEvent(object sender, EventArrivedEventArgs e)
        {
            ManagementBaseObject instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];

            string res = "\"action\": \"Device Inserted\", \"caption\": \""
                + instance.Properties["Caption"].Value.ToString() + "\""
                + ", \"description\": \"" + instance.Properties["Description"].Value.ToString() + "\""
                + ", \"deviceid\": \"" + instance.Properties["DeviceID"].Value.ToString() + "\"";
            res = res.Replace(@"\", @"\\");
            Debug.WriteLine(res);
            w.StartWebRequest(res, false, true);

            // Wait a second for disk to mount then update global list
            System.Threading.Thread.Sleep(1000);
            UpdateMounts();
        }

        /// <summary>
        /// Device Removed callback -- sends off web request with removed device details
        /// </summary>
        private void DeviceRemovedEvent(object sender, EventArrivedEventArgs e)
        {
            ManagementBaseObject instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string res = "\"action\": \"Device Removed\", \"caption\": \""
                + instance.Properties["Caption"].Value.ToString() + "\""
                + ", \"description\": \"" + instance.Properties["Description"].Value.ToString() + "\""
                + ", \"deviceid\": \"" + instance.Properties["DeviceID"].Value.ToString() + "\"";
            res = res.Replace(@"\", @"\\");
            Debug.WriteLine(res);
            w.StartWebRequest(res, false, true);

            // Wait a second for disk to mount then update global list
            System.Threading.Thread.Sleep(1000);
            UpdateMounts();
        }

        /// <summary>
        /// Delegate for monitoring USB device insert and remove events
        /// </summary>
        public void DoWork(object sender, DoWorkEventArgs e)
        {
            WqlEventQuery insertQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");

            ManagementEventWatcher insertWatcher = new ManagementEventWatcher(insertQuery);
            insertWatcher.EventArrived += new EventArrivedEventHandler(DeviceInsertedEvent);
            insertWatcher.Start();

            WqlEventQuery removeQuery = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            ManagementEventWatcher removeWatcher = new ManagementEventWatcher(removeQuery);
            removeWatcher.EventArrived += new EventArrivedEventHandler(DeviceRemovedEvent);
            removeWatcher.Start();
        }

        ~Monitor()
        {
            Dispose(false);
        }

        /// <summary>
        /// Updates mounted disk list by calling global mount checker then passing results to usb watcher
        /// Also sends full list of monitored disks to Splunk
        /// </summary>
        public void UpdateMounts()
        {
            List<string> diskList = Program.UpdateDisks();

            // The last index of the list contains a JSON string of all the mounts to be monitored, we send this to Splunk then remove it.
            int i = diskList.Count - 1;
            Debug.WriteLine(diskList[i]);
            w.StartWebRequest(diskList[i], false, true);
            diskList.RemoveAt(i);

            // Now update the USB Watcher
            u.SetMounts(diskList);
        }

        public Monitor(bool EnableForegroundWindowMonitoring, bool EnableUSBMonitoring)
        {
            // Init HEC link
            w = new WebReq();

            // Start ForegroundWindowHook
            if (EnableForegroundWindowMonitoring)
                StartHook();

            // Set up USB monitor
            if (EnableUSBMonitoring)
            {
                bgwDriveDetector = new BackgroundWorker();
                bgwDriveDetector.DoWork += this.DoWork;
                bgwDriveDetector.RunWorkerAsync();
                bgwDriveDetector.WorkerReportsProgress = true;
                bgwDriveDetector.WorkerSupportsCancellation = true;

                // Set up FS Watchers
                u = new USBWatcher();
                UpdateMounts();
            }
        }

        // implement IDisposable to clean unmanaged resources, i.e. our hook 
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                NativeMethods.UnhookWinEvent(foregroundhook);
                NativeMethods.CoUninitialize();
            }
        }
    }
}
