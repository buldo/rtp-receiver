namespace RtpReceiver.Rtp;

internal class ReceivedSRTimestamp
{
    /// <summary>
    /// NTP timestamp in sender report packet, in 32bit.
    /// </summary>
    public uint NTP = 0;

    /// <summary>
    /// Datetime the sender report was received at.
    /// </summary>
    public DateTime ReceivedAt = DateTime.MinValue;
}