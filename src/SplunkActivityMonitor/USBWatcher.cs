using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

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
            string smd5 = "unable to compute";
            using (var sha = SHA256.Create())
            using (var sh = SHA1.Create())
            {
                using (var stream = File.OpenRead(input))
                {
                    ssha = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    smd5 = BitConverter.ToString(sh.ComputeHash(stream)).Replace("-", "");
                }
            }
            return new string[] { ssha, smd5 };
        }

        private static void OnAction(object sender, FileSystemEventArgs e)
        {
            // Sleep for a little bit
            // 1. Wait for a file operation to complete, or we get "file in use" error
            // 2. Side-benefit of not being able to get hashes of files that are huge (this would use too much compute)
            string[] m = new string[] { "unable to compute", "unable to compute" };
            DateTime myDateTime = DateTime.Now;
            string sqlFormattedDate = myDateTime.ToString(Format).Replace(@"\", @"\\");
            Thread.Sleep(3000);
            try { m = GetHashes(e.FullPath); }
            catch (IOException) { }
            string res = "\"action\": \"" + e.ChangeType.ToString() + "\""
                + ", \"fullpath\": \"" + e.FullPath + "\""
                + ", \"name\": \"" + e.Name + "\""
                + ", \"md5\": \"" + m[1] + "\""
                + ", \"sha256\": \"" + m[0] + "\""
                + ", \"time\": \"" + sqlFormattedDate + "\"";
            res = res.Replace(@"\", @"\\");
            Debug.WriteLine(res);
            Monitor.w.StartWebRequest(res, false, true);
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            string[] m = new string[] { "unable to compute", "unable to compute" };
            DateTime myDateTime = DateTime.Now;
            string sqlFormattedDate = myDateTime.ToString(Format).Replace(@"\", @"\\");
            Thread.Sleep(3000);
            try { m = GetHashes(e.FullPath); }
            catch (IOException) { }
            string res = "\"action\": \"" + e.ChangeType.ToString() + "\""
                + ", \"oldpath\": \"" + e.OldFullPath + "\""
                + ", \"fullpath\": \"" + e.FullPath + "\""
                + ", \"name\": \"" + e.Name + "\""
                + ", \"md5\": \"" + m[1] + "\""
                + ", \"sha256\": \"" + m[0] + "\""
                + ", \"time\": \"" + sqlFormattedDate + "\"";
            res = res.Replace(@"\", @"\\");
            Debug.WriteLine(res);
            Monitor.w.StartWebRequest(res, false, true);
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