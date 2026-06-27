using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CloudStorage.Net
{
    public class GooglePickerServer : IDisposable
    {
        private HttpListener _listener;
        private string _accessToken;
        private string _clientId;
        private string _appId;
        private string _developerKey;
        private TaskCompletionSource<List<GooglePickerFile>> _tcs;

        public List<string> LoopbackHosts { get; private set; }
        private int _portIndex = 0;

        public GooglePickerServer(string accessToken, string clientId, string appId, string developerKey = "")
        {
            _accessToken = accessToken;
            _clientId = clientId;
            _appId = appId;
            _developerKey = developerKey;

            int port = GetFreePort();
            string redirectUri = $"http://localhost:{port}/";

            LoopbackHosts = new List<string> { redirectUri };
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async Task<List<GooglePickerFile>> StartAndGetFiles()
        {
            _tcs = new TaskCompletionSource<List<GooglePickerFile>>();

            while (_portIndex < LoopbackHosts.Count)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(LoopbackHosts[_portIndex]);
                    _listener.Start();
                    break;
                }
                catch (HttpListenerException)
                {
                    _portIndex++;
                    if (_portIndex >= LoopbackHosts.Count)
                        throw new Exception("Could not find an available port for the local server.");
                }
            }

            string url = LoopbackHosts[_portIndex];
            Debug.WriteLine($"Google Picker Server started at {url}");

            // Open the system browser
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            // Start listening for requests
            _ = ListenAsync();

            return await _tcs.Task;
        }

        private async Task ListenAsync()
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    if (request.HttpMethod == "GET" && (request.Url.AbsolutePath == "/" || request.Url.AbsolutePath == "/index.html"))
                    {
                        // Serve the picker HTML
                        string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "googlepicker.html");
                        if (!File.Exists(htmlPath))
                        {
                            htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "googlepicker.html");
                        }

                        if (File.Exists(htmlPath))
                        {
                            string html = File.ReadAllText(htmlPath);
                            // Inject credentials
                            string script = $"\n<script>function initCalled() {{ initPicker('{_accessToken}', '{_clientId}', '{_appId}', '{_developerKey}'); }} \n window.addEventListener('load', initCalled); </script>\n";
                            html = html.Replace("</body>", script + "</body>");

                            byte[] buffer = Encoding.UTF8.GetBytes(html);
                            response.ContentLength64 = buffer.Length;
                            response.ContentType = "text/html";
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            string errorMsg = "Could not find googlepicker.html at: " + htmlPath;
                            byte[] buffer = Encoding.UTF8.GetBytes(errorMsg);
                            response.StatusCode = 404;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                    }
                    else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/picker-result")
                    {
                        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                        {
                            string json = await reader.ReadToEndAsync();
                            try
                            {
                                if (json.Contains("\"message\":\"CANCEL\""))
                                {
                                    _tcs.TrySetResult(null);
                                }
                                else if (json.Contains("\"message\":\"ERROR:"))
                                {
                                    _tcs.TrySetException(new Exception("Picker Error: " + json));
                                }
                                else
                                {
                                    var pickedFiles = JsonConvert.DeserializeObject<List<GooglePickerFile>>(json);
                                    _tcs.TrySetResult(pickedFiles);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("JSON Parsing Error: " + ex.Message + "\nJSON: " + json);
                                _tcs.TrySetException(ex);
                            }
                        }

                        response.StatusCode = 200;
                        response.ContentType = "text/plain";
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("OK"), 0, 2);

                        // Stop listening after receiving result
                        _listener.Stop();
                    }
                    else
                    {
                        response.StatusCode = 404;
                    }

                    response.OutputStream.Close();
                }
            }
            catch (Exception ex)
            {
                if (_listener.IsListening)
                {
                    Debug.WriteLine("GooglePickerServer Error: " + ex.Message);
                    _tcs.TrySetException(ex);
                }
            }
        }

        public void Dispose()
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
        }
    }
}
