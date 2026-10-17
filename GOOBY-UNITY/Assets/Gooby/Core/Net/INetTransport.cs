namespace Gooby.Core.Net
{
    /// <summary>Kind of a <see cref="NetTransportEvent"/>.</summary>
    public enum NetTransportEventKind
    {
        Opened,
        Message,
        Closed,
        Error,
    }

    /// <summary>
    /// Event produced by a transport (any thread) and consumed on the main thread via
    /// <see cref="INetTransport.TryDequeue"/>. <see cref="NetTransportEventKind.Closed"/> is terminal.
    /// </summary>
    public sealed class NetTransportEvent
    {
        /// <summary>Close code: the connection could not be established (DNS, refused, TLS…).</summary>
        public const int CodeConnectFailed = -1;
        /// <summary>Close code: the connect attempt timed out.</summary>
        public const int CodeConnectTimeout = -2;
        /// <summary>Close code: the socket died without a close frame (RFC 6455 1006).</summary>
        public const int CodeAbnormal = 1006;

        public NetTransportEventKind Kind { get; }
        /// <summary>Message text (Message) or error description (Error).</summary>
        public string Text { get; }
        /// <summary>Close code (Closed): WebSocket close status or one of the negative Code* constants.</summary>
        public int Code { get; }
        /// <summary>Close reason (Closed), e.g. "AUTH_FAIL".</summary>
        public string Reason { get; }

        private NetTransportEvent(NetTransportEventKind kind, string text, int code, string reason)
        {
            Kind = kind;
            Text = text ?? "";
            Code = code;
            Reason = reason ?? "";
        }

        public static NetTransportEvent Opened() => new NetTransportEvent(NetTransportEventKind.Opened, "", 0, "");
        public static NetTransportEvent Message(string text) => new NetTransportEvent(NetTransportEventKind.Message, text, 0, "");
        public static NetTransportEvent Closed(int code, string reason) => new NetTransportEvent(NetTransportEventKind.Closed, "", code, reason);
        public static NetTransportEvent Error(string message) => new NetTransportEvent(NetTransportEventKind.Error, message, 0, "");

        public override string ToString() => Kind + (Kind == NetTransportEventKind.Closed ? "(" + Code + "," + Reason + ")" : Text.Length > 0 ? "(" + Text + ")" : "");
    }

    /// <summary>
    /// Text-frame transport abstraction used by <see cref="NetSession"/>. Implementations must never
    /// throw into the caller; results arrive as queued events. One instance = one connection attempt.
    /// </summary>
    public interface INetTransport
    {
        /// <summary>Starts connecting (non-blocking). Emits Opened, or Error + Closed on failure.</summary>
        void Connect(string url);

        /// <summary>Queues a text frame (dropped silently when not open).</summary>
        void Send(string text);

        /// <summary>Closes the connection (non-blocking, idempotent). The caller may drop the instance afterwards.</summary>
        void Close();

        /// <summary>Main-thread drain of pending events.</summary>
        bool TryDequeue(out NetTransportEvent ev);
    }

    /// <summary>
    /// Optional transport capability: a frame sent by the transport's own I/O thread the moment the socket opens,
    /// before <see cref="NetTransportEventKind.Opened"/> reaches the main thread. NetSession puts HELLO here — the
    /// server drops a connection that stays silent for 5 s, and a client rendering at a few frames per second (or
    /// stalled by a loading hitch) would otherwise only pump the Opened event after that deadline.
    /// </summary>
    public interface INetTransportOpenFrame
    {
        string SendOnOpen { get; set; }
    }
}
