using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RtpReceiver.Rtp;

public class MediaStreamTrack
{
    private static ILogger logger = new NullLogger<MediaStreamTrack>();

    /// <summary>
    /// The type of media stream represented by this track. Must be audio or video.
    /// </summary>
    public SDPMediaTypesEnum Kind { get; private set; }

    /// <summary>
    /// The value used in the RTP Synchronisation Source header field for media packets
    /// sent using this media stream.
    /// Be careful that the RTP Synchronisation Source header field should not be changed
    /// unless specific implementations require it. By default this value is chosen randomly,
    /// with the intent that no two synchronization sources within the same RTP session
    /// will have the same SSRC.
    /// </summary>
    public uint Ssrc { get; set; }

    /// <summary>
    /// The last seqnum received from the remote peer for this stream.
    /// </summary>
    public ushort LastRemoteSeqNum { get; internal set; }

    // The value used in the RTP Sequence Number header field for media packets.
    public ushort SeqNum { get { return (ushort)m_seqNum; } internal set { m_seqNum = value; } }

    /// <summary>
    /// The last abs-capture-time received from the remote peer for this stream.
    /// </summary>
    public TimestampPair LastAbsoluteCaptureTimestamp { get; internal set; }

    /// <summary>
    /// The media capabilities supported by this track.
    /// </summary>
    public List<SDPAudioVideoMediaFormat> Capabilities { get; internal set; }

    // <summary>
    ///  a=extmap - Mapping for RTP header extensions
    /// </summary>
    public Dictionary<int, RTPHeaderExtension> HeaderExtensions { get; }

    /// <summary>
    /// Represents the original and default stream status for the track. This is set
    /// when the track is created and does not change. It allows tracks to be set back to
    /// their original state after being put on hold etc. For example if a track is
    /// added as receive only video source then when after on and off hold it needs to
    /// be known that the track reverts receive only rather than sendrecv.
    /// </summary>
    public MediaStreamStatusEnum DefaultStreamStatus { get; private set; }

    /// <summary>
    /// Holds the stream state of the track.
    /// </summary>
    public MediaStreamStatusEnum StreamStatus { get; internal set; }

    /// <summary>
    /// If the SDP remote the remote party provides "a=ssrc" attributes, as specified
    /// in RFC5576, this property will hold the values. The list can be used when
    /// an RTP/RTCP packet is received and needs to be matched against a media type or
    /// RTCP report.
    /// </summary>
    public Dictionary<uint, SDPSsrcAttribute> SdpSsrc { get; set; } = new Dictionary<uint, SDPSsrcAttribute>();

    // The value used in the RTP Sequence Number header field for media packets.
    // Although valid values are all in the range of ushort, the underlying field is of type int, because Interlocked.CompareExchange is used to increment in a fast and thread-safe manner and there is no overload for ushort.
    private int m_seqNum;

    /// <summary>
    /// Creates a lightweight class to track a media stream track within an RTP session
    /// When supporting RFC3550 (the standard RTP specification) the relationship between
    /// an RTP stream and session is 1:1. For WebRTC and RFC8101 there can be multiple
    /// streams per session.
    /// </summary>
    /// <param name="kind">The type of media for this stream. There can only be one
    /// stream per media type.</param>
    /// <param name="isRemote">True if this track corresponds to a media announcement from the
    /// remote party.</param>
    /// <param name="capabilities">The capabilities for the track being added. Where the same media
    /// type is supported locally and remotely only the mutual capabilities can be used. This will
    /// occur if we receive an SDP offer (add track initiated by the remote party) and we need
    /// to remove capabilities we don't support.</param>
    /// <param name="streamStatus">The initial stream status for the media track. Defaults to
    /// send receive.</param>
    /// <param name="ssrcAttributes">Optional. If the track is being created from an SDP announcement this
    /// parameter contains a list of the SSRC attributes that should then match the RTP header SSRC value
    /// for this track.</param>
    public MediaStreamTrack(
        SDPMediaTypesEnum kind,
        List<SDPAudioVideoMediaFormat> capabilities,
        MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv,
        List<SDPSsrcAttribute> ssrcAttributes = null, Dictionary<int, RTPHeaderExtension> headerExtensions = null)
    {
        Kind = kind;
        Capabilities = capabilities;
        StreamStatus = streamStatus;
        DefaultStreamStatus = streamStatus;
        HeaderExtensions = headerExtensions ?? new Dictionary<int, RTPHeaderExtension>();

        // Add the source attributes from the remote SDP to help match RTP SSRC and RTCP CNAME values against
        // RTP and RTCP packets received from the remote party.
        if (ssrcAttributes?.Count > 0)
        {
            foreach (var ssrcAttr in ssrcAttributes)
            {
                if (!SdpSsrc.ContainsKey(ssrcAttr.SSRC))
                {
                    SdpSsrc.Add(ssrcAttr.SSRC, ssrcAttr);
                }
            }
        }
    }

    /// <summary>
    /// Checks whether the payload ID in an RTP packet received from the remote call party
    /// is in this track's list.
    /// </summary>
    /// <param name="payloadID">The payload ID to check against.</param>
    /// <returns>True if the payload ID matches one of the codecs for this stream. False if not.</returns>
    public bool IsPayloadIDMatch(int payloadID)
    {
        return Capabilities?.Any(x => x.ID == payloadID) == true;
    }

    /// <summary>
    /// Checks whether a SSRC value from an RTP header or RTCP report matches
    /// a value expected for this track.
    /// </summary>
    /// <param name="ssrc">The SSRC value to check.</param>
    /// <returns>True if the SSRC value is expected for this track. False if not.</returns>
    public bool IsSsrcMatch(uint ssrc)
    {
        return ssrc == Ssrc || SdpSsrc.ContainsKey(ssrc);
    }

    /// <summary>
    /// Gets the matching audio or video format for a payload ID.
    /// </summary>
    /// <param name="payloadID">The payload ID to get the format for.</param>
    /// <returns>An audio or video format or null if no payload ID matched.</returns>
    public SDPAudioVideoMediaFormat? GetFormatForPayloadID(int payloadID)
    {
        return Capabilities?.FirstOrDefault(x => x.ID == payloadID);
    }

    /// <summary>
    /// To restrict MediaStream Capabilties to one Audio/Video format. This Audio/Video format must already be present in the previous list or if the list is empty/null
    ///
    /// Usefull once you have successfully created a connection with a Peer to use the same format even even others negocitions are performed
    /// </summary>
    /// <param name="sdpAudioVideoMediaFormat">The Audio/Video Format to restrict</param>
    /// <returns>True if the operation has been performed</returns>
    public Boolean RestrictCapabilities(SDPAudioVideoMediaFormat sdpAudioVideoMediaFormat)
    {
        Boolean result = true;
        if (Capabilities?.Count > 0)
        {
            result = (Capabilities.Exists(x => x.ID == sdpAudioVideoMediaFormat.ID));
        }

        if (result)
        {
            Capabilities = new List<SDPAudioVideoMediaFormat> { sdpAudioVideoMediaFormat };
        }
        return true;
    }

    /// <summary>
    /// To restrict MediaStream Capabilties to one Video format. This Video format must already be present in the previous list or if the list is empty/null
    ///
    /// Usefull once you have successfully created a connection with a Peer to use the same format even even others negocitions are performed
    /// </summary>
    /// <param name="videoFormat">The Video Format to restrict</param>
    /// <returns>True if the operation has been performed</returns>
    public Boolean RestrictCapabilities(VideoFormat videoFormat)
    {
        return RestrictCapabilities(new SDPAudioVideoMediaFormat(videoFormat));
    }

}