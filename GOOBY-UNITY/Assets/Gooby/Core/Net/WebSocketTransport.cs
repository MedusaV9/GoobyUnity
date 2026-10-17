using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gooby.Core.Net
{
    /// <summary>
    /// <see cref="INetTransport"/> over <see cref="ClientWebSocket"/>: background connect (timeout),
    /// receive loop (fragmented frames, UTF-8), serialized send queue (ClientWebSocket forbids
    /// concurrent sends), events via <see cref="ConcurrentQueue{T}"/>. Never throws into the caller.
    /// One instance per connection attempt.
    /// </summary>
    public sealed class WebSocketTransport : INetTransport, INetTransportOpenFrame
    {
        /// <summary>Connect (TCP + TLS + upgrade) timeout.</summary>
        public int ConnectTimeoutMs { get; set; } = 8000;
        /// <summary>WebSocket-level keep-alive (pong frames) — independent of the protocol PING.</summary>
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
        /// <summary>Largest accepted incoming message; bigger ones close the socket (1009).</summary>
        public int MaxMessageBytes { get; set; } = 1024 * 1024;

        private readonly ConcurrentQueue<NetTransportEvent> _events = new ConcurrentQueue<NetTransportEvent>();
        private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();
        private readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private int _started;
        private int _closedEmitted;
        private volatile bool _open;
        private volatile bool _closeRequested;

        /// <summary>Sent from the I/O thread right after the handshake (HELLO), ahead of everything queued later.</summary>
        public string SendOnOpen { get; set; }

        /// <summary>True between Opened and Closed.</summary>
        public bool IsOpen => _open;

        public void Connect(string url)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri)
                || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                _events.Enqueue(NetTransportEvent.Error("invalid url: " + url));
                EmitClosed(NetTransportEvent.CodeConnectFailed, "BAD_URL");
                return;
            }
            try
            {
                Task.Run(() => RunAsync(uri));
            }
            catch (Exception e)
            {
                _events.Enqueue(NetTransportEvent.Error(e.Message));
                EmitClosed(NetTransportEvent.CodeConnectFailed, "CONNECT_FAILED");
            }
        }

        public void Send(string text)
        {
            if (!_open || _closeRequested || text == null) return;
            _outgoing.Enqueue(text);
            try { _sendSignal.Release(); } catch (Exception) { /* disposed/overflow — ignore */ }
        }

        public void Close()
        {
            if (_closeRequested) return;
            _closeRequested = true;
            try
            {
                if (_open)
                {
                    // Graceful: the send loop emits the close frame; hard abort after 2 s.
                    _sendSignal.Release();
                    _cts.CancelAfter(2000);
                }
                else
                {
                    _cts.Cancel(); // still connecting → abort
                }
            }
            catch (Exception)
            {
                // never throw into the caller
            }
        }

        public bool TryDequeue(out NetTransportEvent ev) => _events.TryDequeue(out ev);

        private void EmitClosed(int code, string reason)
        {
            _open = false;
            if (Interlocked.Exchange(ref _closedEmitted, 1) == 0)
                _events.Enqueue(NetTransportEvent.Closed(code, reason));
        }

        private async Task RunAsync(Uri uri)
        {
            ClientWebSocket ws;
            try
            {
                ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = KeepAliveInterval;
            }
            catch (Exception e)
            {
                _events.Enqueue(NetTransportEvent.Error(e.Message));
                EmitClosed(NetTransportEvent.CodeConnectFailed, "CONNECT_FAILED");
                return;
            }

            var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            try
            {
                connectCts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var timedOut = connectCts.IsCancellationRequested && !_cts.IsCancellationRequested;
                if (_closeRequested)
                {
                    EmitClosed(1000, "CLIENT_CLOSE");
                }
                else if (timedOut)
                {
                    _events.Enqueue(NetTransportEvent.Error("connect timeout"));
                    EmitClosed(NetTransportEvent.CodeConnectTimeout, "CONNECT_TIMEOUT");
                }
                else
                {
                    _events.Enqueue(NetTransportEvent.Error(Describe(e)));
                    EmitClosed(NetTransportEvent.CodeConnectFailed, "CONNECT_FAILED");
                }
                SafeDispose(ws);
                connectCts.Dispose();
                return;
            }
            connectCts.Dispose();

            if (_closeRequested)
            {
                try { ws.Abort(); } catch (Exception) { /* ignore */ }
                EmitClosed(1000, "CLIENT_CLOSE");
                SafeDispose(ws);
                return;
            }

            _open = true;
            var first = SendOnOpen;
            if (!string.IsNullOrEmpty(first))
            {
                _outgoing.Enqueue(first);
                try { _sendSignal.Release(); } catch (Exception) { /* ignore */ }
            }
            _events.Enqueue(NetTransportEvent.Opened());
            var sendTask = SendLoopAsync(ws);
            await ReceiveLoopAsync(ws).ConfigureAwait(false);
            _open = false;
            try { _cts.Cancel(); } catch (Exception) { /* ignore */ }
            try { await sendTask.ConfigureAwait(false); } catch (Exception) { /* ignore */ }
            SafeDispose(ws);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws)
        {
            var buffer = new byte[8192];
            var message = new MemoryStream();
            var code = NetTransportEvent.CodeAbnormal;
            var reason = "";
            try
            {
                while (true)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        code = result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : 1005;
                        reason = result.CloseStatusDescription ?? "";
                        await CompleteCloseHandshake(ws).ConfigureAwait(false);
                        break;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        code = 1009;
                        reason = "MESSAGE_TOO_BIG";
                        try { ws.Abort(); } catch (Exception) { /* ignore */ }
                        break;
                    }
                    if (result.EndOfMessage)
                    {
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                            _events.Enqueue(NetTransportEvent.Message(text));
                        }
                        message.SetLength(0);
                    }
                }
            }
            catch (Exception e)
            {
                if (_closeRequested)
                {
                    code = 1000;
                    reason = "CLIENT_CLOSE";
                }
                else
                {
                    _events.Enqueue(NetTransportEvent.Error(Describe(e)));
                    code = NetTransportEvent.CodeAbnormal;
                    reason = "CONNECTION_LOST";
                }
            }
            if (_closeRequested && code == NetTransportEvent.CodeAbnormal)
            {
                code = 1000;
                reason = "CLIENT_CLOSE";
            }
            EmitClosed(code, reason);
        }

        private async Task CompleteCloseHandshake(ClientWebSocket ws)
        {
            if (ws.State != WebSocketState.CloseReceived) return;
            var locked = false;
            try
            {
                using (var t = new CancellationTokenSource(1000))
                {
                    await _sendLock.WaitAsync(t.Token).ConfigureAwait(false);
                    locked = true;
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", t.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // best effort
            }
            finally
            {
                if (locked) _sendLock.Release();
            }
        }

        private async Task SendLoopAsync(ClientWebSocket ws)
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await _sendSignal.WaitAsync(_cts.Token).ConfigureAwait(false);
                    // Frames queued before Close() still go out (e.g. a last PAL_ACK), then the close frame.
                    while (_outgoing.TryDequeue(out var text))
                    {
                        var bytes = Encoding.UTF8.GetBytes(text);
                        await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
                        try
                        {
                            if (ws.State != WebSocketState.Open) return;
                            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _sendLock.Release();
                        }
                    }
                    if (_closeRequested)
                    {
                        await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
                        try
                        {
                            if (ws.State == WebSocketState.Open)
                                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", _cts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _sendLock.Release();
                        }
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception e)
            {
                if (!_closeRequested)
                {
                    _events.Enqueue(NetTransportEvent.Error("send failed: " + Describe(e)));
                    try { ws.Abort(); } catch (Exception) { /* receive loop reports the close */ }
                }
            }
        }

        private static void SafeDispose(ClientWebSocket ws)
        {
            try { ws.Dispose(); } catch (Exception) { /* ignore */ }
        }

        private static string Describe(Exception e)
        {
            var inner = e;
            while (inner.InnerException != null) inner = inner.InnerException;
            return ReferenceEquals(inner, e) ? e.Message : e.Message + " (" + inner.Message + ")";
        }
    }
}
