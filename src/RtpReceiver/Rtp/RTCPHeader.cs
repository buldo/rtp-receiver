namespace RtpReceiver.Rtp;

/// <summary>
/// RTCP Header as defined in RFC3550.
/// </summary>
public class RTCPHeader
{
    public const int HEADER_BYTES_LENGTH = 4;
    public const int MAX_RECEPTIONREPORT_COUNT = 32;
    public const int RTCP_VERSION = 2;

    public int Version { get; private set; } = RTCP_VERSION;         // 2 bits.
    public int PaddingFlag { get; private set; } = 0;                 // 1 bit.
    public int ReceptionReportCount { get; private set; } = 0;        // 5 bits.
    public RTCPReportTypesEnum PacketType { get; private set; }       // 8 bits.
    public UInt16 Length { get; private set; }                        // 16 bits.

    /// <summary>
    /// The Feedback Message Type is used for RFC4585 transport layer feedback reports.
    /// When used this field gets set in place of the Reception Report Counter field.
    /// </summary>
    public RTCPFeedbackTypesEnum FeedbackMessageType { get; private set; } = RTCPFeedbackTypesEnum.unassigned;

    /// <summary>
    /// The Payload Feedback Message Type is used for RFC4585 payload layer feedback reports.
    /// When used this field gets set in place of the Reception Report Counter field.
    /// </summary>
    public PSFBFeedbackTypesEnum PayloadFeedbackMessageType { get; private set; } = PSFBFeedbackTypesEnum.unassigned;

    public RTCPHeader(RTCPFeedbackTypesEnum feedbackType)
    {
        PacketType = RTCPReportTypesEnum.RTPFB;
        FeedbackMessageType = feedbackType;
    }

    public RTCPHeader(PSFBFeedbackTypesEnum feedbackType)
    {
        PacketType = RTCPReportTypesEnum.PSFB;
        PayloadFeedbackMessageType = feedbackType;
    }

    public RTCPHeader(RTCPReportTypesEnum packetType, int reportCount)
    {
        PacketType = packetType;
        ReceptionReportCount = reportCount;
    }

    /// <summary>
    /// Identifies whether an RTCP header is for a standard RTCP packet or for an
    /// RTCP feedback report.
    /// </summary>
    /// <returns>True if the header is for an RTCP feedback report or false if not.</returns>
    public bool IsFeedbackReport()
    {
        if (PacketType == RTCPReportTypesEnum.RTPFB ||
            PacketType == RTCPReportTypesEnum.PSFB)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Extract and load the RTCP header from an RTCP packet.
    /// </summary>
    /// <param name="packet"></param>
    public RTCPHeader(byte[] packet)
    {
        if (packet.Length < HEADER_BYTES_LENGTH)
        {
            throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP header packet.");
        }

        UInt16 firstWord = BitConverter.ToUInt16(packet, 0);

        if (BitConverter.IsLittleEndian)
        {
            firstWord = NetConvert.DoReverseEndian(firstWord);
            Length = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
        }
        else
        {
            Length = BitConverter.ToUInt16(packet, 2);
        }

        Version = Convert.ToInt32(firstWord >> 14);
        PaddingFlag = Convert.ToInt32((firstWord >> 13) & 0x1);
        PacketType = (RTCPReportTypesEnum)(firstWord & 0x00ff);

        if (IsFeedbackReport())
        {
            if (PacketType == RTCPReportTypesEnum.RTPFB)
            {
                FeedbackMessageType = (RTCPFeedbackTypesEnum)((firstWord >> 8) & 0x1f);
            }
            else
            {
                PayloadFeedbackMessageType = (PSFBFeedbackTypesEnum)((firstWord >> 8) & 0x1f);
            }
        }
        else
        {
            ReceptionReportCount = Convert.ToInt32((firstWord >> 8) & 0x1f);
        }
    }

    public byte[] GetHeader(int receptionReportCount, UInt16 length)
    {
        if (receptionReportCount > MAX_RECEPTIONREPORT_COUNT)
        {
            throw new ApplicationException("The Reception Report Count value cannot be larger than " + MAX_RECEPTIONREPORT_COUNT + ".");
        }

        ReceptionReportCount = receptionReportCount;
        Length = length;

        return GetBytes();
    }

    /// <summary>
    /// The length of this RTCP packet in 32-bit words minus one,
    /// including the header and any padding.
    /// </summary>
    public void SetLength(ushort length)
    {
        Length = length;
    }

    public byte[] GetBytes()
    {
        byte[] header = new byte[4];

        UInt32 firstWord = ((uint)Version << 30) + ((uint)PaddingFlag << 29) + ((uint)PacketType << 16) + Length;

        if (IsFeedbackReport())
        {
            if (PacketType == RTCPReportTypesEnum.RTPFB)
            {
                firstWord += (uint)FeedbackMessageType << 24;
            }
            else
            {
                firstWord += (uint)PayloadFeedbackMessageType << 24;
            }
        }
        else
        {
            firstWord += (uint)ReceptionReportCount << 24;
        }

        if (BitConverter.IsLittleEndian)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(firstWord)), 0, header, 0, 4);
        }
        else
        {
            Buffer.BlockCopy(BitConverter.GetBytes(firstWord), 0, header, 0, 4);
        }

        return header;
    }
}