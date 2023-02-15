namespace RtpReceiver.Rtp;

public class RTCPReceiverReport
{
    public const int MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + 4;

    public RTCPHeader Header;
    public uint SSRC;
    public List<ReceptionReportSample> ReceptionReports;

    /// <summary>
    /// Creates a new RTCP Reception Report payload.
    /// </summary>
    /// <param name="ssrc">The synchronisation source of the RTP packet being sent. Can be zero
    /// if there are none being sent.</param>
    /// <param name="receptionReports">A list of the reception reports to include. Can be empty.</param>
    public RTCPReceiverReport(uint ssrc, List<ReceptionReportSample> receptionReports)
    {
        Header = new RTCPHeader(RTCPReportTypesEnum.RR, receptionReports != null ? receptionReports.Count : 0);
        SSRC = ssrc;
        ReceptionReports = receptionReports;
    }

    /// <summary>
    /// Create a new RTCP Receiver Report from a serialised byte array.
    /// </summary>
    /// <param name="packet">The byte array holding the serialised receiver report.</param>
    public RTCPReceiverReport(byte[] packet)
    {
        if (packet.Length < MIN_PACKET_SIZE)
        {
            throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCPReceiverReport packet.");
        }

        Header = new RTCPHeader(packet);
        ReceptionReports = new List<ReceptionReportSample>();

        if (BitConverter.IsLittleEndian)
        {
            SSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
        }
        else
        {
            SSRC = BitConverter.ToUInt32(packet, 4);
        }

        int rrIndex = 8;
        for (int i = 0; i < Header.ReceptionReportCount; i++)
        {
            var rr = new ReceptionReportSample(packet.Skip(rrIndex + i * ReceptionReportSample.PAYLOAD_SIZE).ToArray());
            ReceptionReports.Add(rr);
        }
    }

    /// <summary>
    /// Gets the serialised bytes for this Receiver Report.
    /// </summary>
    /// <returns>A byte array.</returns>
    public byte[] GetBytes()
    {
        int rrCount = (ReceptionReports != null) ? ReceptionReports.Count : 0;
        byte[] buffer = new byte[RTCPHeader.HEADER_BYTES_LENGTH + 4 + rrCount * ReceptionReportSample.PAYLOAD_SIZE];
        Header.SetLength((ushort)(buffer.Length / 4 - 1));

        Buffer.BlockCopy(Header.GetBytes(), 0, buffer, 0, RTCPHeader.HEADER_BYTES_LENGTH);
        int payloadIndex = RTCPHeader.HEADER_BYTES_LENGTH;

        if (BitConverter.IsLittleEndian)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SSRC)), 0, buffer, payloadIndex, 4);
        }
        else
        {
            Buffer.BlockCopy(BitConverter.GetBytes(SSRC), 0, buffer, payloadIndex, 4);
        }

        int bufferIndex = payloadIndex + 4;
        for (int i = 0; i < rrCount; i++)
        {
            var receptionReportBytes = ReceptionReports[i].GetBytes();
            Buffer.BlockCopy(receptionReportBytes, 0, buffer, bufferIndex, ReceptionReportSample.PAYLOAD_SIZE);
            bufferIndex += ReceptionReportSample.PAYLOAD_SIZE;
        }

        return buffer;
    }
}