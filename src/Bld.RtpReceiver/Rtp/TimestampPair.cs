﻿namespace Bld.RtpReceiver.Rtp;

internal class TimestampPair
{
    public uint RtpTimestamp { get; set; }
    public ulong NtpTimestamp { get; set; }
}