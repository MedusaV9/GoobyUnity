using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>One queued letter: the MAIL_SEND payload (with its clientId) plus bookkeeping.</summary>
    public sealed class NetOutboxEntry
    {
        /// <summary>Entry id = the letter's clientId (server dedupes retries by it).</summary>
        public string Id { get; internal set; }
        public string Kind { get; internal set; } = NetOutbox.KindMail;
        /// <summary>Unix ms when queued.</summary>
        public long At { get; internal set; }
        /// <summary>MAIL_SEND payload {to, text, clientId, item?}.</summary>
        public JObject Payload { get; internal set; }

        public string To => NetJson.Str(Payload?["to"]);
        public JObject Item => Payload?["item"] as JObject;
    }

    /// <summary>
    /// Persistent offline queue of letters (port of outbox.gd + net_mail.gd flush): letters written
    /// offline are stored as MAIL_SEND payloads and sent one by one when online. Success (incl.
    /// server dupe) removes the entry; transient codes (OFFLINE, TIMEOUT, RATE_LIMIT, DAILY_LIMIT,
    /// MAILBOX_FULL) keep it and stop the flush; every other code is definitive → removed and
    /// reported as bounced (UI refunds postage/gift). Atomic JSON save on every change.
    /// </summary>
    public sealed class NetOutbox
    {
        public const string KindMail = "mail";
        public const int MaxEntries = 200;

        /// <summary>Codes worth a later retry — the entry stays queued.</summary>
        public static readonly HashSet<string> RetryableCodes = new HashSet<string>
        {
            NetErrorCodes.Offline,
            NetErrorCodes.Timeout,
            NetErrorCodes.RateLimit,
            "DAILY_LIMIT",
            "MAILBOX_FULL",
        };

        private readonly List<NetOutboxEntry> _entries = new List<NetOutboxEntry>();
        private readonly Func<long> _clock;

        public NetOutbox(string path, Func<long> unixMs = null)
        {
            Path = path;
            _clock = unixMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Load();
        }

        public string Path { get; }
        public int Count => _entries.Count;
        public IReadOnlyList<NetOutboxEntry> Entries => _entries;
        /// <summary>True while a <see cref="Flush"/> is waiting for a reply.</summary>
        public bool Flushing { get; private set; }

        /// <summary>Queues a MAIL_SEND payload (must contain a clientId; one is generated otherwise). Returns the entry id.</summary>
        public string Enqueue(JObject payload)
        {
            var p = payload != null ? (JObject)payload.DeepClone() : new JObject();
            var id = NetJson.Str(p["clientId"]);
            if (id.Length == 0)
            {
                id = NewClientId();
                p["clientId"] = id;
            }
            var existing = _entries.FindIndex(e => e.Id == id);
            var entry = new NetOutboxEntry { Id = id, Kind = KindMail, At = _clock(), Payload = p };
            if (existing >= 0) _entries[existing] = entry;
            else _entries.Add(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            Save();
            return id;
        }

        public bool Contains(string id) => _entries.Exists(e => e.Id == id);

        public bool Remove(string id)
        {
            var idx = _entries.FindIndex(e => e.Id == id);
            if (idx < 0) return false;
            _entries.RemoveAt(idx);
            Save();
            return true;
        }

        public void Clear()
        {
            _entries.Clear();
            Save();
        }

        /// <summary>
        /// Sends queued letters one by one via <c>session.Request("MAIL_SEND")</c>. Re-entrant calls while a
        /// flush is running are ignored. <paramref name="onDelivered"/>(entry, reply) after success;
        /// <paramref name="onBounced"/>(entry, code) after a definitive failure; <paramref name="onDone"/>
        /// when the flush stops (queue empty or transient failure).
        /// </summary>
        public void Flush(NetSession session, Action<NetOutboxEntry, NetReply> onDelivered,
            Action<NetOutboxEntry, string> onBounced, Action onDone = null)
        {
            if (Flushing || session == null) return;
            if (_entries.Count == 0 || !session.IsOnline)
            {
                NetLog.Guard(onDone, "outbox onDone");
                return;
            }
            Flushing = true;
            FlushNext(session, onDelivered, onBounced, onDone);
        }

        private void FlushNext(NetSession session, Action<NetOutboxEntry, NetReply> onDelivered,
            Action<NetOutboxEntry, string> onBounced, Action onDone)
        {
            if (_entries.Count == 0 || !session.IsOnline)
            {
                Flushing = false;
                NetLog.Guard(onDone, "outbox onDone");
                return;
            }
            var entry = _entries[0];
            session.Request("MAIL_SEND", (JObject)entry.Payload.DeepClone(), reply =>
            {
                if (reply.Ok)
                {
                    Remove(entry.Id);
                    NetLog.Guard(() => onDelivered?.Invoke(entry, reply), "outbox onDelivered");
                }
                else if (RetryableCodes.Contains(reply.Code))
                {
                    Flushing = false;
                    NetLog.Guard(onDone, "outbox onDone");
                    return;
                }
                else
                {
                    Remove(entry.Id);
                    NetLog.Guard(() => onBounced?.Invoke(entry, reply.Code), "outbox onBounced");
                }
                FlushNext(session, onDelivered, onBounced, onDone);
            });
        }

        /// <summary>Letter client id accepted by the server (^[A-Za-z0-9._-]{8,64}$).</summary>
        public static string NewClientId() => "m-" + NetRandom.Hex(16);

        private void Load()
        {
            _entries.Clear();
            if (string.IsNullOrEmpty(Path)) return;
            try
            {
                if (!File.Exists(Path)) return;
                var o = NetJson.ParseObject(File.ReadAllText(Path, Encoding.UTF8));
                if (!(o?["entries"] is JArray arr)) return;
                foreach (var t in arr)
                {
                    if (!(t is JObject e)) continue;
                    var id = NetJson.Str(e["id"]);
                    var payload = e["payload"] as JObject;
                    if (id.Length == 0 || payload == null) continue;
                    var kind = NetJson.Str(e["kind"], KindMail);
                    if (kind != KindMail) continue;
                    _entries.Add(new NetOutboxEntry { Id = id, Kind = kind, At = NetJson.Long(e["at"]), Payload = payload });
                }
            }
            catch (Exception e)
            {
                NetLog.Warn("[outbox] " + Path + " unreadable — starting empty: " + e.Message);
                _entries.Clear();
            }
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(Path)) return;
            var arr = new JArray();
            foreach (var e in _entries)
            {
                arr.Add(new JObject
                {
                    ["id"] = e.Id,
                    ["kind"] = e.Kind,
                    ["at"] = e.At,
                    ["payload"] = e.Payload.DeepClone(),
                });
            }
            var root = new JObject { ["v"] = 1, ["entries"] = arr };
            if (!NetFiles.WriteAtomic(Path, root.ToString(Formatting.None)))
                NetLog.Warn("[outbox] cannot write " + Path);
        }
    }
}
