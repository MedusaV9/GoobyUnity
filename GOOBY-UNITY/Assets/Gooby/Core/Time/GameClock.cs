using System;

namespace Gooby.Core.Time
{
    /// <summary>The ONLY time source for state/logic code. Tests pin it.</summary>
    public interface IClock
    {
        long NowMs();
        string LocalDay(long ms = -1);
    }

    /// <summary>
    /// Pinnable game clock — port of GOOBY/src/core/clock.js / scripts/logic/clock.gd.
    /// <see cref="LocalDay"/> yields a YYYY-MM-DD string in LOCAL time; the UTC offset comes
    /// from the OS unless overridden (tests pin offset 0 to match TZ=UTC golden values).
    /// </summary>
    public sealed class GameClock : IClock
    {
        private bool _pinned;
        private long _pinnedMs;
        private int _offsetMinutes;
        private bool _offsetOverridden;

        public long NowMs()
        {
            if (_pinned) return _pinnedMs;
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void Pin(long ms)
        {
            _pinned = true;
            _pinnedMs = ms;
        }

        public void Advance(long ms)
        {
            if (_pinned) _pinnedMs += ms;
        }

        public void Unpin() => _pinned = false;

        public bool IsPinned => _pinned;

        public void SetUtcOffsetMinutes(int minutes)
        {
            _offsetMinutes = minutes;
            _offsetOverridden = true;
        }

        public int UtcOffsetMinutes(long atMs)
        {
            if (_offsetOverridden) return _offsetMinutes;
            var local = TimeZoneInfo.Local;
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(atMs).UtcDateTime;
            return (int)local.GetUtcOffset(utc).TotalMinutes;
        }

        public string LocalDay(long ms = -1)
        {
            var at = ms >= 0 ? ms : NowMs();
            var localSecs = (long)Math.Floor(at / 1000.0) + UtcOffsetMinutes(at) * 60L;
            var d = DateTimeOffset.FromUnixTimeSeconds(localSecs).UtcDateTime;
            return $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}";
        }

        /// <summary>Local calendar parts (year, month, day, hour) for an epoch-ms with a given bias in minutes.</summary>
        public static (int year, int month, int day, int hour) LocalDateTime(long nowMs, int biasMinutes)
        {
            var secs = (long)(nowMs / 1000.0) + biasMinutes * 60L;
            var d = DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
            return (d.Year, d.Month, d.Day, d.Hour);
        }
    }
}
