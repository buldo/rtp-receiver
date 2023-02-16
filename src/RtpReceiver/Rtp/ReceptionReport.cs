namespace RtpReceiver.Rtp;

/// <summary>
/// Maintains the reception statistics for a received RTP stream.
/// </summary>
public class ReceptionReport
{
    //private const int MAX_DROPOUT = 3000;
    //private const int MAX_MISORDER = 100;
    //private const int MIN_SEQUENTIAL = 2;
    private const int RTP_SEQ_MOD = 1 << 16;
    //private const int MAX_POSITIVE_LOSS = 0x7fffff;
    //private const int MAX_NEGATIVE_LOSS = 0x800000;
    private const int SEQ_NUM_WRAP_LOW = 256;
    private const int SEQ_NUM_WRAP_HIGH = 65280;

    /// <summary>
    /// Data source being reported.
    /// </summary>
    public uint SSRC;

    /// <summary>
    /// highest seq. number seen
    /// </summary>
    private ushort m_max_seq;

    /// <summary>
    /// Increments by UInt16.MaxValue each time the sequence number wraps around.
    /// </summary>
    private ulong m_cycles;

    /// <summary>
    /// The first sequence number received.
    /// </summary>
    private uint m_base_seq;

    /// <summary>
    /// last 'bad' seq number + 1.
    /// </summary>
    private uint m_bad_seq;

    /// <summary>
    /// sequ. packets till source is valid.
    /// </summary>
    //private uint m_probation;

    /// <summary>
    /// packets received.
    /// </summary>
    private uint m_received;

    /// <summary>
    /// packet expected at last interval.
    /// </summary>
    private ulong m_expected_prior;

    /// <summary>
    /// packet received at last interval.
    /// </summary>
    private uint m_received_prior;

    /// <summary>
    /// relative trans time for prev pkt.
    /// </summary>
    private uint m_transit;

    /// <summary>
    /// Estimated jitter.
    /// </summary>
    private uint m_jitter;

    /// <summary>
    /// Received last SR packet timestamp.
    /// </summary>
    private ReceivedSRTimestamp m_receivedLSRTimestamp = null;

    /// <summary>
    /// Creates a new Reception Report object.
    /// </summary>
    /// <param name="ssrc">The synchronisation source this reception report is for.</param>
    public ReceptionReport(uint ssrc)
    {
        SSRC = ssrc;
    }

    /// <summary>
    /// Updates the state when an RTCP sender report is received from the remote party.
    /// </summary>
    /// <param name="srNtpTimestamp">The sender report timestamp.</param>
    internal void RtcpSenderReportReceived(ulong srNtpTimestamp)
    {
        System.Threading.Interlocked.Exchange(ref m_receivedLSRTimestamp,
            new ReceivedSRTimestamp
            {
                NTP = (uint)((srNtpTimestamp >> 16) & 0xFFFFFFFF),
                ReceivedAt = DateTime.Now
            });
    }

    /// <summary>
    /// Carries out the calculations required to measure properties related to the reception of
    /// received RTP packets. The algorithms employed are:
    ///  - RFC3550 A.1 RTP Data Header Validity Checks (for sequence number calculations).
    ///  - RFC3550 A.3 Determining Number of Packets Expected and Lost.
    ///  - RFC3550 A.8 Estimating the Interarrival Jitter.
    /// </summary>
    /// <param name="seq">The sequence number in the RTP header.</param>
    /// <param name="rtpTimestamp">The timestamp in the RTP header.</param>
    /// <param name="arrivalTimestamp">The current timestamp in the SAME units as the RTP timestamp.
    /// For example for 8Khz audio the arrival timestamp needs 8000 ticks per second.</param>
    internal void RtpPacketReceived(ushort seq, uint rtpTimestamp, uint arrivalTimestamp)
    {
        // Sequence number calculations and cycles as per RFC3550 Appendix A.1.
        //if (m_received == 0)
        //{
        //    init_seq(seq);
        //    m_max_seq = (ushort)(seq - 1);
        //    m_probation = MIN_SEQUENTIAL;
        //}
        //bool ready = update_seq(seq);

        if (m_received == 0)
        {
            m_base_seq = seq;
        }

        m_received++;

        if (seq == m_max_seq + 1)
        {
            // Packet is in sequence.
            m_max_seq = seq;
        }
        else if (seq == 0 && m_max_seq == ushort.MaxValue)
        {
            // Packet is in sequence and a wrap around has occurred.
            m_max_seq = seq;
            m_cycles += RTP_SEQ_MOD;
        }
        else
        {
            // Out of order, duplicate or skipped sequence number.
            if (seq > m_max_seq)
            {
                // Seqnum is greater than expected. RTP packet is dropped or out of order.
                m_max_seq = seq;
            }
            else if (seq < SEQ_NUM_WRAP_LOW && m_max_seq > SEQ_NUM_WRAP_HIGH)
            {
                // Seqnum is out of order and has wrapped.
                m_max_seq = seq;
                m_cycles += RTP_SEQ_MOD;
            }
            else
            {
                // Remaining conditions are:
                // - seqnum == m_max_seq indicating a duplicate RTP packet, or
                // - is seqnum is more than 1 less than m_max_seqnum. Which most
                //   likely indicates an RTP packet was delivered out of order.
                m_bad_seq++;
            }
        }

        // Estimating the Interarrival Jitter as defined in RFC3550 Appendix A.8.
        uint transit = arrivalTimestamp - rtpTimestamp;
        int d = (int)(transit - m_transit);
        m_transit = transit;
        if (d < 0)
        {
            d = -d;
        }
        m_jitter += (uint)(d - ((m_jitter + 8) >> 4));

        //return ready;
    }

}