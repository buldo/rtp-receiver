using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RtpReceiver.Rtp;

public class RTCPFeedback
{
    private static ILogger logger = new NullLogger<RTCPFeedback>();

    public int SENDER_PAYLOAD_SIZE = 20;
    public int MIN_PACKET_SIZE = 0;

    public RTCPHeader Header;
    public uint SenderSSRC; // Packet Sender
    public uint MediaSSRC;
    public ushort PID; // Packet ID (PID): 16 bits to specify a lost packet, the RTP sequence number of the lost packet.
    public ushort BLP; // bitmask of following lost packets (BLP): 16 bits
    public uint FCI; // Feedback Control Information (FCI)

    public RTCPFeedback(uint senderSsrc, uint mediaSsrc, RTCPFeedbackTypesEnum feedbackMessageType, ushort sequenceNo, ushort bitMask)
    {
        Header = new RTCPHeader(feedbackMessageType);
        SENDER_PAYLOAD_SIZE = 12;
        MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + SENDER_PAYLOAD_SIZE;
        SenderSSRC = senderSsrc;
        MediaSSRC = mediaSsrc;
        PID = sequenceNo;
        BLP = bitMask;
    }

    /// <summary>
    /// Constructor for RTP feedback reports that do not require any additional feedback control
    /// indication parameters (e.g. RTCP Rapid Resynchronisation Request).
    /// </summary>
    /// <param name="feedbackMessageType">The payload specific feedback type.</param>
    public RTCPFeedback(uint senderSsrc, uint mediaSsrc, RTCPFeedbackTypesEnum feedbackMessageType)
    {
        Header = new RTCPHeader(feedbackMessageType);
        SenderSSRC = senderSsrc;
        MediaSSRC = mediaSsrc;
        SENDER_PAYLOAD_SIZE = 8;
    }

    /// <summary>
    /// Constructor for payload feedback reports that do not require any additional feedback control
    /// indication parameters (e.g. Picture Loss Indication reports).
    /// </summary>
    /// <param name="feedbackMessageType">The payload specific feedback type.</param>
    public RTCPFeedback(uint senderSsrc, uint mediaSsrc, PSFBFeedbackTypesEnum feedbackMessageType)
    {
        Header = new RTCPHeader(feedbackMessageType);
        SenderSSRC = senderSsrc;
        MediaSSRC = mediaSsrc;
        SENDER_PAYLOAD_SIZE = 8;
    }

    /// <summary>
    /// Create a new RTCP Report from a serialised byte array.
    /// </summary>
    /// <param name="packet">The byte array holding the serialised feedback report.</param>
    public RTCPFeedback(byte[] packet)
    {
        Header = new RTCPHeader(packet);

        int payloadIndex = RTCPHeader.HEADER_BYTES_LENGTH;
        if (BitConverter.IsLittleEndian)
        {
            SenderSSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, payloadIndex));
            MediaSSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, payloadIndex + 4));
        }
        else
        {
            SenderSSRC = BitConverter.ToUInt32(packet, payloadIndex);
            MediaSSRC = BitConverter.ToUInt32(packet, payloadIndex + 4);
        }

        switch (Header)
        {
            case var x when x.PacketType == RTCPReportTypesEnum.RTPFB && x.FeedbackMessageType == RTCPFeedbackTypesEnum.RTCP_SR_REQ:
                SENDER_PAYLOAD_SIZE = 8;
                // PLI feedback reports do no have any additional parameters.
                break;
            case var x when x.PacketType == RTCPReportTypesEnum.RTPFB:
                SENDER_PAYLOAD_SIZE = 12;
                if (BitConverter.IsLittleEndian)
                {
                    PID = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, payloadIndex + 8));
                    BLP = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, payloadIndex + 10));
                }
                else
                {
                    PID = BitConverter.ToUInt16(packet, payloadIndex + 8);
                    BLP = BitConverter.ToUInt16(packet, payloadIndex + 10);
                }
                break;

            case var x when x.PacketType == RTCPReportTypesEnum.PSFB && x.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI:
                SENDER_PAYLOAD_SIZE = 8;
                break;

            //default:
            //    throw new NotImplementedException($"Deserialisation for feedback report {Header.PacketType} not yet implemented.");
        }
    }

    //0                   1                   2                   3
    //0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    //+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    //|V=2|P|   FMT   |       PT      |          length               |
    //+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    //|                  SSRC of packet sender                        |
    //+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    //|                  SSRC of media source                         |
    //+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    //:            Feedback Control Information(FCI)                  :
    //:                                                               :
    public byte[] GetBytes()
    {
        byte[] buffer = new byte[RTCPHeader.HEADER_BYTES_LENGTH + SENDER_PAYLOAD_SIZE];
        Header.SetLength((ushort)(buffer.Length / 4 - 1));

        Buffer.BlockCopy(Header.GetBytes(), 0, buffer, 0, RTCPHeader.HEADER_BYTES_LENGTH);
        int payloadIndex = RTCPHeader.HEADER_BYTES_LENGTH;

        // All feedback packets require the Sender and Media SSRC's to be set.
        if (BitConverter.IsLittleEndian)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderSSRC)), 0, buffer, payloadIndex, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(MediaSSRC)), 0, buffer, payloadIndex + 4, 4);
        }
        else
        {
            Buffer.BlockCopy(BitConverter.GetBytes(SenderSSRC), 0, buffer, payloadIndex, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(MediaSSRC), 0, buffer, payloadIndex + 4, 4);
        }

        switch (Header)
        {
            case var x when x.PacketType == RTCPReportTypesEnum.RTPFB && x.FeedbackMessageType == RTCPFeedbackTypesEnum.RTCP_SR_REQ:
                // PLI feedback reports do no have any additional parameters.
                break;
            case var x when x.PacketType == RTCPReportTypesEnum.RTPFB:
                if (BitConverter.IsLittleEndian)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(PID)), 0, buffer, payloadIndex + 8, 2);
                    Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(BLP)), 0, buffer, payloadIndex + 10, 2);
                }
                else
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(PID), 0, buffer, payloadIndex + 8, 2);
                    Buffer.BlockCopy(BitConverter.GetBytes(BLP), 0, buffer, payloadIndex + 10, 2);
                }
                break;

            case var x when x.PacketType == RTCPReportTypesEnum.PSFB && x.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI:
                break;
            case var x when x.PacketType == RTCPReportTypesEnum.PSFB && x.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.AFB:
                // Application feedback reports do no have any additional parameters?
                break;
            default:
                logger?.LogDebug($"Serialization for feedback report {Header.PacketType} and message type "
                                 + $"{Header.FeedbackMessageType} not yet implemented.");
                break;
            //throw new NotImplementedException($"Serialisation for feedback report {Header.PacketType} and message type "
            //+ $"{Header.FeedbackMessageType} not yet implemented.");
        }
        return buffer;
    }
}