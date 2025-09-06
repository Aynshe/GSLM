using System;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace GameStoreLibraryManager.Common
{
    /// <summary>
    /// A simple local HTTP server to listen for an authentication callback.
    /// </summary>
    public class LocalAuthServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();

        /// <summary>
        /// Initializes the server to listen on a specific URI.
        /// </summary>
        /// <param name="uri">The URI to listen on (e.g., "http://localhost:8080/callback/"). Must end with a '/'.</param>
        public LocalAuthServer(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                throw new ArgumentNullException(nameof(uri));
            if (!uri.EndsWith("/"))
                throw new ArgumentException("URI must end with a '/'", nameof(uri));

            _listener.Prefixes.Add(uri);
        }

        /// <summary>
        /// Starts the listener, waits for a single request, processes it, and returns the query parameters.
        /// </summary>
        /// <returns>A NameValueCollection of the query parameters from the request.</returns>
        public async Task<NameValueCollection> ListenForCallbackAsync()
        {
            _listener.Start();
            try
            {
                var context = await _listener.GetContextAsync();
                var request = context.Request;

                var queryParams = HttpUtility.ParseQueryString(request.Url.Query);

                // Send a success response to the browser
                var response = context.Response;
                string responseString = "<html><head><meta charset='UTF-8'><title>Authentication Success</title></head><body><h1>Authentication successful!</h1><p>You can now close this browser window.</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentType = "text/html; charset=utf-8";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = buffer.Length;

                var output = response.OutputStream;
                await output.WriteAsync(buffer, 0, buffer.Length);
                output.Close();

                return queryParams;
            }
            finally
            {
                if (_listener.IsListening)
                {
                    _listener.Stop();
                }
            }
        }

        public void Dispose()
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
            ((IDisposable)_listener).Dispose();
        }
    }
}
