﻿using System.Windows.Forms; // Used to keep a low-cost main loop alive so we can wait for our hook. 

namespace SplunkActivityMonitor
{
    using System;
    using System.Diagnostics;
    using System.Configuration;
    using System.IO;
    using System.Net;
    using System.Collections.Generic;
    using System.Text;

    public static partial class Program
    {
        private static string Hostname = Dns.GetHostName();
        public static string SplunkURI, HECToken, TargetIndex, TargetSourcetypeForegroundWindowChange, TargetSourcetypeUSBChange, DebugLogTarget;
        public static bool DebugMode, AllowBadCerts;

        public static string hostname => Hostname;

        /// <summary>
        /// Returns true if given process name is running
        /// </summary>
        /// <param name="name">Name of the process you want to check.</param>
        public static bool IsProcessOpen(string name)
        {
            foreach (Process clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Appends given string to file
        /// </summary>
        /// <param name="res">String to append.</param>
        public static void WriteToFile(string res)
        {
            try
            {
                using (StreamWriter sw = File.AppendText(DebugLogTarget))
                {
                    sw.WriteLine(res);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Reads setting from app config
        /// </summary>
        /// <param name="key">Name of the app.config setting you want to read.</param>
        public static string ReadSetting(string key)
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                return appSettings[key] ?? "Not Found";
            }
            catch (ConfigurationErrorsException)
            {
                return "Error reading app settings";
            }
        }

        /// <summary>
        /// Places all removable devices into an array
        /// </summary>
        public static string UpdateDisks()
        {
            StringBuilder sb = new StringBuilder("\"name\": \"Removable Devices Reloaded\", \"drives\": [");
            int i = 0;
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Removable) {
                    double free = drive.TotalFreeSpace;
                    double capacity = drive.TotalSize;
                    string label = drive.VolumeLabel;
                    string root = drive.RootDirectory.FullName;

                    Debug.WriteLine("Monitoring {0} Drive: {1}", drive.DriveType, drive.Name);
                    string pre = "";
                    if (i != 0) pre = ","; 
                    sb.Append((pre += "{\"mount\": \"" + drive.Name.ToString() + "\", \"label\": \"" + label + "\", \"root\": \"" + root + "\", \"capacity\": \"" + capacity + "\", \"free\": \"" + free + "\" }").Replace(@"\", @"\\"));
                    i++;
                }
            }

            sb.Append(']');
            return sb.ToString();
        }

        [STAThread]
        static void Main(String[] args)
        {
            // Die if already running
            Process thisProc = Process.GetCurrentProcess();

            if (!IsProcessOpen("SplunkActivityMonitor") == false)
            {
                if (Process.GetProcessesByName(thisProc.ProcessName).Length > 1)
                {
                    Application.Exit();
                    return;
                }
            }

            // Read app.config
            AllowBadCerts = false;
            DebugMode = false;
            SplunkURI = ReadSetting("SplunkURI") ?? "https://localhost:8088/services/collector/event";
            HECToken = ReadSetting("HECToken") ?? "7b179726-daab-4122-8ce8-364a5cd724d3";
            TargetIndex = ReadSetting("TargetIndex") ?? "main";
            TargetSourcetypeUSBChange = ReadSetting("TargetSourcetypeUSBChange") ?? "WindowsActivityMonitor:USBChange";
            TargetSourcetypeForegroundWindowChange = ReadSetting("TargetSourcetypeForegroundWindowChange") ?? "WindowsActivityMonitor:ForegroundWindowChange";
            DebugLogTarget = ReadSetting("DebugLogTarget") ?? @Environment.ExpandEnvironmentVariables("%ProgramW6432%") + @"\Splunk\etc\apps\TA-WindowsActivityMonitor\spool\Monitor - CurrentActivity.log";
            _ = bool.TryParse(ReadSetting("DebugMode"), out DebugMode);
            _ = bool.TryParse(ReadSetting("AlowBadCerts"), out AllowBadCerts);

            string cfg = "New Config: " +
                "SplunkURI=\"" + SplunkURI +
                "\" HECToken=\"" + HECToken +
                "\" Debug=\"" + DebugMode +
                "\" TargetIndex=\"" + TargetIndex +
                "\" TargetSourcetypeUSB=\"" + TargetSourcetypeUSBChange +
                "\" TargetSourcetypeForeground=\"" + TargetSourcetypeForegroundWindowChange +
                "\" AllowBadCerts=\"" + AllowBadCerts +
                "\" DebugLogTarget=\"" + DebugLogTarget + "\"";

            if (AllowBadCerts)
            {
                Debug.WriteLine("Allowing all SSL certs...");
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            }

            // Allow more modern SSL that Windows supports by default (and explicitely disallow sslv3)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            if (DebugMode)
                WriteToFile(cfg);
            Debug.WriteLine(cfg);

            // Check for non-system disks
            UpdateDisks();

            // Start form
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(mainForm: new Form1());
        }
    }
}
