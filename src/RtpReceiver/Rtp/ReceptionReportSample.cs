﻿namespace RtpReceiver.Rtp;

/// <summary>
/// Represents a point in time sample for a reception report.
/// </summary>
public class ReceptionReportSample
{
    public const int PAYLOAD_SIZE = 24;

    /// <summary>
    /// Data source being reported.
    /// </summary>
    public uint SSRC;

    /// <summary>
    /// Fraction lost since last SR/RR.
    /// </summary>
    public byte FractionLost;

    /// <summary>
    /// Cumulative number of packets lost (signed!).
    /// </summary>
    public int PacketsLost;

    /// <summary>
    /// Extended last sequence number received.
    /// </summary>
    public uint ExtendedHighestSequenceNumber;

    /// <summary>
    /// Interarrival jitter.
    /// </summary>
    public uint Jitter;

    /// <summary>
    /// Last SR packet from this source.
    /// </summary>
    public uint LastSenderReportTimestamp;

    /// <summary>
    /// Delay since last SR packet.
    /// </summary>
    public uint DelaySinceLastSenderReport;

    /// <summary>
    /// Creates a new Reception Report object.
    /// </summary>
    /// <param name="ssrc">The synchronisation source this reception report is for.</param>
    /// <param name="fractionLost">The fraction of RTP packets lost since the previous Sender or Receiver
    /// Report was sent.</param>
    /// <param name="packetsLost">The total number of RTP packets that have been lost since the
    /// beginning of reception.</param>
    /// <param name="highestSeqNum">Extended highest sequence number received from source.</param>
    /// <param name="jitter">Interarrival jitter of the RTP packets received within the last reporting period.</param>
    /// <param name="lastSRTimestamp">The timestamp from the most recent RTCP Sender Report packet
    /// received.</param>
    /// <param name="delaySinceLastSR">The delay between receiving the last Sender Report packet and the sending
    /// of this Reception Report.</param>
    public ReceptionReportSample(
        uint ssrc,
        byte fractionLost,
        int packetsLost,
        uint highestSeqNum,
        uint jitter,
        uint lastSRTimestamp,
        uint delaySinceLastSR)
    {
        SSRC = ssrc;
        FractionLost = fractionLost;
        PacketsLost = packetsLost;
        ExtendedHighestSequenceNumber = highestSeqNum;
        Jitter = jitter;
        LastSenderReportTimestamp = lastSRTimestamp;
        DelaySinceLastSenderReport = delaySinceLastSR;
    }

    public ReceptionReportSample(byte[] packet)
    {
        if (BitConverter.IsLittleEndian)
        {
            SSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 0));
            FractionLost = packet[4];
            PacketsLost = NetConvert.DoReverseEndian(BitConverter.ToInt32(new byte[] { 0x00, packet[5], packet[6], packet[7] }, 0));
            ExtendedHighestSequenceNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 8));
            Jitter = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 12));
            LastSenderReportTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 16));
            DelaySinceLastSenderReport = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 20));
        }
        else
        {
            SSRC = BitConverter.ToUInt32(packet, 4);
            FractionLost = packet[4];
            PacketsLost = BitConverter.ToInt32(new byte[] { 0x00, packet[5], packet[6], packet[7] }, 0);
            ExtendedHighestSequenceNumber = BitConverter.ToUInt32(packet, 8);
            Jitter = BitConverter.ToUInt32(packet, 12);
            LastSenderReportTimestamp = BitConverter.ToUInt32(packet, 16);
            LastSenderReportTimestamp = BitConverter.ToUInt32(packet, 20);
        }
    }

    /// <summary>
    /// Serialises the reception report block to a byte array.
    /// </summary>
    /// <returns>A byte array.</returns>
    public byte[] GetBytes()
    {
        byte[] payload = new byte[24];

        if (BitConverter.IsLittleEndian)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SSRC)), 0, payload, 0, 4);
            payload[4] = FractionLost;
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(PacketsLost)), 1, payload, 5, 3);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ExtendedHighestSequenceNumber)), 0, payload, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Jitter)), 0, payload, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(LastSenderReportTimestamp)), 0, payload, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(DelaySinceLastSenderReport)), 0, payload, 20, 4);
        }
        else
        {
            Buffer.BlockCopy(BitConverter.GetBytes(SSRC), 0, payload, 0, 4);
            payload[4] = FractionLost;
            Buffer.BlockCopy(BitConverter.GetBytes(PacketsLost), 1, payload, 5, 3);
            Buffer.BlockCopy(BitConverter.GetBytes(ExtendedHighestSequenceNumber), 0, payload, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(Jitter), 0, payload, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(LastSenderReportTimestamp), 0, payload, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(DelaySinceLastSenderReport), 0, payload, 20, 4);
        }

        return payload;
    }
}