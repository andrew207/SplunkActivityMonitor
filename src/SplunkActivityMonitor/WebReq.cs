using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace SplunkActivityMonitor
{
    public class WebReq
    {
        string res;

        /// <summary>
        /// Build header for our web request and hand off async request stream
        /// </summary>
        /// <param name="res">valid JSON Splunk event to be POSTed</param>
        public void StartWebRequest(string res, bool doFG, bool doUSB)
        {
            this.res = res;

            string baseurl = Program.SplunkURI;
            string token = Program.HECToken;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(baseurl));
            WebHeaderCollection headers = new WebHeaderCollection { "Authorization: Splunk " + token };

            request.Method = "POST";
            request.Accept = "application/json";
            request.Headers = headers;

            Debug.WriteLine(res);

            if (doFG)
                request.BeginGetRequestStream(new AsyncCallback(GetRequestStreamCallbackFG), request);
            if (doUSB)
                request.BeginGetRequestStream(new AsyncCallback(GetRequestStreamCallbackUSB), request);
        }

        /// <summary>
        /// Builds JSON format data for ForegroundWindowChange events then sends off the request. 
        /// </summary>
        private void GetRequestStreamCallbackFG(IAsyncResult asynchronousResult)
        {
            string PostData = "{ \"host\": \"" + Program.hostname + "\"," +
                              "\"source\": \"SplunkActivityMonitor.exe\"," +
                              "\"sourcetype\": \"" + Program.TargetSourcetypeForegroundWindowChange + "\"," +
                              "\"index\": \"" + Program.TargetIndex + "\"," +
                              "\"event\": { " +
                                res +
                              "}}";

            doReq(asynchronousResult, (HttpWebRequest)asynchronousResult.AsyncState, PostData);
        }

        /// <summary>
        /// Builds JSON format data for USB events then sends off the request. 
        /// </summary>
        private void GetRequestStreamCallbackUSB(IAsyncResult asynchronousResult)
        {
            string PostData = "{ \"host\": \"" + Program.hostname + "\"," +
                              "\"source\": \"SplunkActivityMonitor.exe\"," +
                              "\"sourcetype\": \"" + Program.TargetSourcetypeUSBChange + "\"," +
                              "\"index\": \"" + Program.TargetIndex + "\"," +
                              "\"event\": { " +
                                res +
                              "}}";

            doReq(asynchronousResult, (HttpWebRequest)asynchronousResult.AsyncState, PostData);
        }

        /// <summary>
        /// Does the actual web request, initiates response callbacks
        /// </summary>
        private void doReq(IAsyncResult asynchronousResult, HttpWebRequest request, string PostData)
        {
            if (Program.DebugMode)
                Program.WriteToFile(PostData);

            // For whatever reason POST data must be added to the HttpWebRequest as a Byte Stream during the request
            Stream postStream;
            try
            {
                postStream = request.EndGetRequestStream(asynchronousResult);
            }
            catch (WebException we)
            {
                Debug.WriteLine("Web exception occured -- is the web server down? " + we.StackTrace);
                if (Program.DebugMode)
                    Program.WriteToFile("Web exception occured -- is the web server down? " + we.StackTrace);
                return;
            }

            byte[] byteArray = Encoding.UTF8.GetBytes(PostData);

            postStream.Write(byteArray, 0, byteArray.Length);
            postStream.Close();

            //Start the web request
            request.BeginGetResponse(new AsyncCallback(GetResponseStreamCallback), request);
        }

        /// <summary>
        /// Outputs results of async HttpWebRequest
        /// </summary>
        private void GetResponseStreamCallback(IAsyncResult callbackResult)
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
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception during web request callback - " + e.StackTrace);
                if (Program.DebugMode)
                    Program.WriteToFile("Exception during web request callback - " + e.ToString());
            }
        }
    }
}
