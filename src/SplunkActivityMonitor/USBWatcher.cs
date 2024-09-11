using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
namespace SplunkActivityMonitor
{
    public class USBWatcher
    {
        private List<string> mounts;
        private List<FileSystemWatcher> watchers;
        private const string Format = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>
        /// Creates individual FileSystemWatchers for each mount delivered. 
        /// </summary>
        /// <param name="value">List of mount points to watch</param>
        public void SetMounts(List<string> value)
        {
            mounts = value;
            watchers = new List<FileSystemWatcher>();
            foreach (string mount in mounts)
            {
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(mount);
                    w.NotifyFilter = NotifyFilters.Attributes
                                         | NotifyFilters.DirectoryName
                                         | NotifyFilters.FileName
                                         | NotifyFilters.LastWrite
                                         | NotifyFilters.Size;
                    w.Changed += OnAction;
                    w.Created += OnAction;
                    w.Deleted += OnAction;
                    w.Renamed += OnRenamed;
                    w.Error += OnError;
                    w.IncludeSubdirectories = true;
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (ArgumentNullException e)
                {
                    Debug.WriteLine("ArgumentNullException thrown when creating watch for " + mount + ". " + e.StackTrace);
                }
                catch (ArgumentException e)
                {
                    Debug.WriteLine("ArgumentException thrown when creating watch for " + mount + ". " + e.StackTrace);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Exception thrown when creating watch for " + mount + ". " + e.StackTrace);
                }
            }
        }

        /// <summary>
        /// Return SHA256 hash of a given file
        /// </summary>
        /// <param name="input">any file FullPath as a string</param>
        /// <returns></returns>
        private static string[] GetHashes(string input)
        {
            string ssha = "unable to compute";
            string ssh1 = "unable to compute";
            try
            {
                using (var sha = SHA256.Create())
                using (var sh = SHA1.Create())
                {
                    using (var stream = File.OpenRead(input))
                    {
                        ssha = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        ssh1 = BitConverter.ToString(sh.ComputeHash(stream)).Replace("-", "");
                    }
                }
            }
            catch (Exception) { }
            return new string[] { ssha, ssh1 };
        }

        /// <summary>
        /// Returns SpecificFileProperties, i.e. the user who modified a file.
        /// Note: this won't work on file deletions, so make sure we can handle exceptions where the file doesn't exist.
        /// Requires STAThread because Shell32
        /// </summary>
        /// <param name="file"></param>
        private static string[] GetFileDetails(string file)
        {
            List<string> res = new List<string>();

            try
            {
                string fileName = Path.GetFileName(file);
                string folderName = Path.GetDirectoryName(file);
                Shell32.Shell shell = new Shell32.Shell();
                Shell32.Folder objFolder;
                objFolder = shell.NameSpace(folderName);

                foreach (Shell32.FolderItem2 item in objFolder.Items())
                {
                    if (fileName == item.Name)
                    {
                        res.Add(objFolder.GetDetailsOf(item, 10).ToString());
                        res.Add(objFolder.GetDetailsOf(item, 1).ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
                Debug.WriteLine(e.Message);
                Debug.WriteLine(e.ToString());
                res.Add(e.Message);
                res.Add(e.Message);
            }

            return res.ToArray();
        }

        /// <summary>
        /// Callback when (pretty much) anything other than rename happens
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnAction(object sender, FileSystemEventArgs e)
        {
            // Sleep for a little bit
            // 1. Wait for a file operation to complete, or we get "file in use" error
            // 2. Side-benefit of not being able to get hashes of files that are huge (this would use too much compute)
            new Thread(() =>
            {
                string[] m = new string[] { "unable to compute", "unable to compute" };
                DateTime myDateTime = DateTime.Now;
                string sqlFormattedDate = myDateTime.ToString(Format).Replace(@"\", @"\\");
                Thread.Sleep(3000);

                try { m = GetHashes(e.FullPath); }
                catch (IOException) { }

                // Must run in STAThread
                string[] FileDetails = new string[2];
                Thread thread = new Thread(() =>
                {
                    FileDetails = GetFileDetails(e.FullPath);
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                string res = "\"action\": \"" + e.ChangeType.ToString() + "\""
                    + ", \"fullpath\": \"" + e.FullPath + "\""
                    + ", \"name\": \"" + e.Name + "\""
                    + ", \"sha1\": \"" + m[1] + "\""
                    + ", \"sha256\": \"" + m[0] + "\""
                    + ", \"lastuser\": \"" + FileDetails[0] + "\""
                    + ", \"size\": \"" + FileDetails[1] + "\""
                    + ", \"time\": \"" + sqlFormattedDate + "\"";
                res = res.Replace(@"\", @"\\");
                Debug.WriteLine(res);
                Monitor.w.StartWebRequest(res, false, true);
            }).Start();
        }

        /// <summary>
        /// Callback just for rename, because it provides us with an extra field for oldName
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            new Thread(() =>
            {
                string[] m = new string[] { "unable to compute", "unable to compute" };
                DateTime myDateTime = DateTime.Now;
                string sqlFormattedDate = myDateTime.ToString(Format).Replace(@"\", @"\\");
                Thread.Sleep(3000);

                try { m = GetHashes(e.FullPath); }
                catch (IOException) { }

                // Must run in STAThread
                string[] FileDetails = new string[2];
                Thread thread = new Thread(() =>
                {
                    FileDetails = GetFileDetails(e.FullPath);
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                string res = "\"action\": \"" + e.ChangeType.ToString() + "\""
                    + ", \"oldpath\": \"" + e.OldFullPath + "\""
                    + ", \"fullpath\": \"" + e.FullPath + "\""
                    + ", \"name\": \"" + e.Name + "\""
                    + ", \"md5\": \"" + m[1] + "\""
                    + ", \"sha256\": \"" + m[0] + "\""
                    + ", \"lastuser\": \"" + FileDetails[0] + "\""
                    + ", \"size\": \"" + FileDetails[1] + "\""
                    + ", \"time\": \"" + sqlFormattedDate + "\"";
                res = res.Replace(@"\", @"\\");
                Debug.WriteLine(res);
                Monitor.w.StartWebRequest(res, false, true);
            }).Start();
        }

        private static void OnError(object sender, ErrorEventArgs e) =>
            PrintException(e.GetException());

        private static void PrintException(Exception? ex)
        {
            if (ex != null)
            {
                Debug.WriteLine($"Message: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                PrintException(ex.InnerException);
            }
        }

        public USBWatcher()
        {

        }
    }
}
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.