using System.Windows.Forms; // Used to keep a low-cost main loop alive so we can wait for our hook. 

namespace SplunkActivityMonitor
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Diagnostics;
    using System.Configuration;
    using System.IO;
    using System.Net;

    public static class Program
    {
        public static string hostname = Dns.GetHostName();
        public static string SplunkURI, HECToken, TargetIndex, TargetSourcetype, DebugLogTarget;
        public static bool DebugMode;

        public class Monitor : IDisposable
        {
            WinEventDelegate dele = null;
            private IntPtr foregroundhook;

            // Constants from Windows.h
            private const uint WINEVENT_OUTOFCONTEXT = 0;
            private const uint EVENT_SYSTEM_FOREGROUND = 3;

            string res = "";

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
                [DllImport("user32.dll")]
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
                string sqlFormattedDate = myDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff").Replace(@"\", @"\\");
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
                } catch (Exception e)
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

                if (DebugMode)
                    WriteToFile(res);
                StartWebRequest(res);
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
            /// Build header for our web request and hand off async request stream
            /// </summary>
            /// <param name="res">valid JSON Splunk event to be POSTed</param>
            private void StartWebRequest(string res)
            {
                this.res = res;

                string baseurl = SplunkURI;
                string token = HECToken;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(baseurl);
                WebHeaderCollection headers = new WebHeaderCollection
                {
                    "Authorization: Splunk " + token
                };

                request.Method = "POST";
                request.Accept = "application/json";
                request.Headers = headers;

                request.BeginGetRequestStream(new AsyncCallback(GetRequestStreamCallback), request);
            }

            /// <summary>
            /// Sends off HttpWebRequest, including adding the JSON POST data to the request. Initialises responce callback. 
            /// </summary>
            private void GetRequestStreamCallback(IAsyncResult asynchronousResult)
            {
                HttpWebRequest request = (HttpWebRequest)asynchronousResult.AsyncState;

                string PostData = "{ \"host\": \"" + hostname + "\"," +
                                  "\"source\": \"SplunkActivityMonitor.exe\"," +
                                  "\"sourcetype\": \"" + TargetSourcetype + "\"," +
                                  "\"index\": \"" + TargetIndex + "\"," +
                                  "\"event\": { " +
                                    res +
                                  "}}";

                if (DebugMode)
                    WriteToFile(PostData);

                // For whatever reason POST data must be added to the HttpWebRequest as a Byte Stream during the request
                Stream postStream;
                try
                {
                    postStream = request.EndGetRequestStream(asynchronousResult);
                }
                catch (WebException we)
                {
                    Debug.WriteLine("Web exception occured -- is the web server down? " + we.StackTrace);
                    if (DebugMode)
                        WriteToFile("Web exception occured -- is the web server down? " + we.StackTrace);
                    return;
                }

                byte[] byteArray = Encoding.UTF8.GetBytes(PostData);

                postStream.Write(byteArray, 0, byteArray.Length);
                postStream.Close();

                //Start the web request
                request.BeginGetResponse(new AsyncCallback(GetResponceStreamCallback), request);
                
            }

            /// <summary>
            /// Outputs results of async HttpWebRequest
            /// </summary>
            private void GetResponceStreamCallback(IAsyncResult callbackResult)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)callbackResult.AsyncState;
                    HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(callbackResult);
                    using (StreamReader httpWebStreamReader = new StreamReader(response.GetResponseStream()))
                    {
                        string result = httpWebStreamReader.ReadToEnd();
                        Debug.WriteLine(result);
                    }
                } catch(Exception e)
                {
                    Debug.WriteLine("Exception during web request - " + e.StackTrace);
                    if (DebugMode)
                        WriteToFile("Exception during web request - " + e.ToString());
                }
            }

            ~Monitor()
            {
                Dispose(false);
            }

            public Monitor()
            {
                StartHook();
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
            bool AlowBadCerts = false;
            DebugMode = false;
            SplunkURI = ReadSetting("SplunkURI") ?? "http://localhost:8088/services/collector/event";
            HECToken = ReadSetting("HECToken") ?? "7b179726-daab-4122-8ce8-364a5cd724d3";
            TargetIndex = ReadSetting("TargetIndex") ?? "main";
            TargetSourcetype = ReadSetting("TargetSourcetype") ?? "WindowsActivityMonitor:Activity";
            DebugLogTarget = ReadSetting("DebugLogTarget") ?? @Environment.ExpandEnvironmentVariables("%ProgramW6432%") + @"\Splunk\etc\apps\TA-WindowsActivityMonitor\spool\Monitor - CurrentActivity.log";
            bool.TryParse(ReadSetting("DebugMode"), out DebugMode);
            bool.TryParse(ReadSetting("AlowBadCerts"), out AlowBadCerts);

            string cfg = "New Config: " +
                "SplunkURI=\"" + SplunkURI +
                "\" HECToken=\"" + HECToken +
                "\" Debug=\"" + DebugMode +
                "\" TargetIndex=\"" + TargetIndex +
                "\" TargetSourcetype=\"" + TargetSourcetype +
                "\" AllowBadCerts=\"" + AlowBadCerts +
                "\" DebugLogTarget=\"" + DebugLogTarget + "\"";

            if (DebugMode)
                WriteToFile(cfg);
            Debug.WriteLine(cfg);

            // Start form
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
