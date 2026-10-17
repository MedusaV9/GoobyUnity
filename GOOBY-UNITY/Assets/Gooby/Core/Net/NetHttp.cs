using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Gooby.Core.Net
{
    /// <summary>
    /// Minimal REST abstraction for the GOOBY-SERVER /api/* calls. <c>done(status, body)</c> must be
    /// invoked on the main thread: implementations queue completions and deliver them in <see cref="Pump"/>.
    /// status 0 = transport failure (no connection, timeout); body then holds an error text.
    /// </summary>
    public interface INetHttp
    {
        /// <param name="method">GET, PUT, POST, DELETE…</param>
        /// <param name="url">Absolute URL.</param>
        /// <param name="bearer">Authorization value: "Bearer id:secret" (or just "id:secret"); null/empty = none.</param>
        /// <param name="jsonBody">JSON request body or null.</param>
        /// <param name="done">Completion (status, body) — delivered from <see cref="Pump"/>.</param>
        void Send(string method, string url, string bearer, string jsonBody, Action<int, string> done);

        /// <summary>Delivers queued completions (call on the main thread, e.g. from SocialClient.Pump).</summary>
        void Pump();
    }

    /// <summary><see cref="INetHttp"/> over <see cref="HttpClient"/> (10 s timeout, completions via ConcurrentQueue).</summary>
    public sealed class HttpClientNetHttp : INetHttp, IDisposable
    {
        private sealed class Completion
        {
            public Action<int, string> Done;
            public int Status;
            public string Body;
        }

        private readonly HttpClient _client;
        private readonly ConcurrentQueue<Completion> _done = new ConcurrentQueue<Completion>();
        private int _inFlight;

        public HttpClientNetHttp(int timeoutMs = 10000)
        {
            _client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs > 0 ? timeoutMs : 10000) };
        }

        /// <summary>Requests started but not yet delivered through <see cref="Pump"/>.</summary>
        public int Pending => System.Threading.Volatile.Read(ref _inFlight);

        public void Send(string method, string url, string bearer, string jsonBody, Action<int, string> done)
        {
            System.Threading.Interlocked.Increment(ref _inFlight);
            try
            {
                Task.Run(() => RunAsync(method, url, bearer, jsonBody, done));
            }
            catch (Exception e)
            {
                _done.Enqueue(new Completion { Done = done, Status = 0, Body = e.Message });
            }
        }

        private async Task RunAsync(string method, string url, string bearer, string jsonBody, Action<int, string> done)
        {
            var status = 0;
            string body;
            try
            {
                using (var req = new HttpRequestMessage(new HttpMethod((method ?? "GET").ToUpperInvariant()), url))
                {
                    if (!string.IsNullOrEmpty(bearer))
                    {
                        var header = bearer.StartsWith("Bearer ", StringComparison.Ordinal) ? bearer : "Bearer " + bearer;
                        req.Headers.TryAddWithoutValidation("Authorization", header);
                    }
                    if (jsonBody != null)
                        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    using (var resp = await _client.SendAsync(req).ConfigureAwait(false))
                    {
                        status = (int)resp.StatusCode;
                        body = resp.Content != null ? await resp.Content.ReadAsStringAsync().ConfigureAwait(false) : "";
                    }
                }
            }
            catch (TaskCanceledException)
            {
                status = 0;
                body = "timeout";
            }
            catch (Exception e)
            {
                status = 0;
                body = e.Message;
            }
            _done.Enqueue(new Completion { Done = done, Status = status, Body = body ?? "" });
        }

        public void Pump()
        {
            while (_done.TryDequeue(out var c))
            {
                System.Threading.Interlocked.Decrement(ref _inFlight);
                if (c.Done == null) continue;
                try
                {
                    c.Done(c.Status, c.Body);
                }
                catch (Exception e)
                {
                    NetLog.Error("http callback threw: " + e);
                }
            }
        }

        public void Dispose()
        {
            try { _client.Dispose(); } catch (Exception) { /* ignore */ }
        }
    }
}
