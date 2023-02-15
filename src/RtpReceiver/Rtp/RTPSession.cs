using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RtpReceiver.Rtp;

public class RTPSession : IDisposable
{
    public const int DEFAULT_DTMF_EVENT_PAYLOAD_ID = 101;
    protected const int SDP_SESSIONID_LENGTH = 10;             // The length of the pseudo-random string to use for the session ID.

    /// <summary>
    /// When there are no RTP packets being sent for an audio or video stream webrtc.lib
    /// still sends RTCP Receiver Reports with this hard coded SSRC. No doubt it's defined
    /// in an RFC somewhere but I wasn't able to find it from a quick search.
    /// </summary>
    private const uint RTCP_RR_NOSTREAM_SSRC = 4195875351U;

    private static ILogger logger = new NullLogger<RTPSession>();

    private RtpSessionConfig rtpSessionConfig;

    private Boolean m_acceptRtpFromAny = false;
    private string m_sdpSessionID = null;           // Need to maintain the same SDP session ID for all offers and answers.
    private int m_sdpAnnouncementVersion = 0;       // The SDP version needs to increase whenever the local SDP is modified (see https://tools.ietf.org/html/rfc6337#section-5.2.5).
    private int m_rtpChannelsCount = 0;            // Need to know the number of RTP Channels

    // The stream used for the underlying RTP session to create a single RTP channel that will
    // be used to multiplex all required media streams. (see addSingleTrack())
    private MediaStream m_primaryStream;

    private RTPChannel MultiplexRtpChannel = null;

    private List<List<SDPSsrcAttribute>> audioRemoteSDPSsrcAttributes = new List<List<SDPSsrcAttribute>>();
    private List<List<SDPSsrcAttribute>> videoRemoteSDPSsrcAttributes = new List<List<SDPSsrcAttribute>>();

    /// <summary>
    /// The primary stream for this session - can be an AudioStream or a VideoStream
    /// </summary>
    public MediaStream PrimaryStream => m_primaryStream;

    /// <summary>
    /// The primary Audio Stream for this session
    /// </summary>
    public AudioStream AudioStream
    {
        get
        {
            if (AudioStreamList.Count > 0)
            {
                return AudioStreamList[0];
            }
            return null;
        }
    }

    /// <summary>
    /// The primary Video Stream for this session
    /// </summary>
    public VideoStream VideoStream
    {
        get
        {
            if (VideoStreamList.Count > 0)
            {
                return VideoStreamList[0];
            }
            return null;
        }
    }

    /// <summary>
    /// The primary remote audio track for this session. Will be null if the remote party is not sending audio.
    /// </summary>
    public MediaStreamTrack AudioRemoteTrack => AudioStream?.RemoteTrack;

    /// <summary>
    /// The primary reporting session for the audio stream. Will be null if only video is being sent.
    /// </summary>
    public RTCPSession AudioRtcpSession => AudioStream?.RtcpSession;

    /// <summary>
    /// The primary Audio remote RTP end point this stream is sending media to.
    /// </summary>
    public IPEndPoint AudioDestinationEndPoint => AudioStream?.DestinationEndPoint;

    /// <summary>
    /// The primary Audio remote RTP control end point this stream is sending to RTCP reports for the media stream to.
    /// </summary>
    public IPEndPoint AudioControlDestinationEndPoint => AudioStream?.ControlDestinationEndPoint;

    /// <summary>
    /// The primary remote video track for this session. Will be null if the remote party is not sending video.
    /// </summary>
    public MediaStreamTrack VideoRemoteTrack => VideoStream?.RemoteTrack;

    /// <summary>
    /// The primary reporting session for the video stream. Will be null if only audio is being sent.
    /// </summary>
    public RTCPSession VideoRtcpSession => VideoStream?.RtcpSession;

    /// <summary>
    /// The primary Video remote RTP end point this stream is sending media to.
    /// </summary>
    public IPEndPoint VideoDestinationEndPoint => VideoStream?.DestinationEndPoint;

    /// <summary>
    /// The primary Video remote RTP control end point this stream is sending to RTCP reports for the media stream to.
    /// </summary>
    public IPEndPoint VideoControlDestinationEndPoint => VideoStream?.ControlDestinationEndPoint;

    /// <summary>
    /// List of all Audio Streams for this session
    /// </summary>
    public List<AudioStream> AudioStreamList { get; } = new();

    /// <summary>
    /// List of all Video Streams for this session
    /// </summary>
    public List<VideoStream> VideoStreamList { get; } = new();

    /// <summary>
    /// The SDP offered by the remote call party for this session.
    /// </summary>
    public SDP RemoteDescription { get; protected set; }

    /// <summary>
    /// Indicates the maximum frame size that can be reconstructed from RTP packets during the depacketisation
    /// process.
    /// </summary>
    public int MaxReconstructedVideoFrameSize { get => VideoStream.MaxReconstructedVideoFrameSize; set => VideoStream.MaxReconstructedVideoFrameSize = value; }

    /// <summary>
    /// Indicates whether the session has been closed. Once a session is closed it cannot
    /// be restarted.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Indicates whether the session has been started. Starting a session tells the RTP
    /// socket to start receiving,
    /// </summary>
    public bool IsStarted { get; private set; }

    /// <summary>
    /// Indicates whether this session is using audio.
    /// </summary>
    public bool HasAudio => AudioStream?.HasAudio == true;

    /// <summary>
    /// Indicates whether this session is using video.
    /// </summary>
    public bool HasVideo => VideoStream?.HasVideo == true;

    /// <summary>
    /// Set if the session has been bound to a specific IP address.
    /// Normally not required but some esoteric call or network set ups may need.
    /// </summary>
    public IPAddress RtpBindAddress => rtpSessionConfig.BindAddress;

    /// <summary>
    /// Gets fired when the remote SDP is received and the set of common audio formats is set. (on the primary one)
    /// </summary>
    public event Action<List<AudioFormat>> OnAudioFormatsNegotiated;

    /// <summary>
    /// Gets fired when the remote SDP is received and the set of common audio formats is set. (using its index)
    /// </summary>
    public event Action<int, List<AudioFormat>> OnAudioFormatsNegotiatedByIndex;

    /// <summary>
    /// Gets fired when the remote SDP is received and the set of common video formats is set. (on the primary one)
    /// </summary>
    public event Action<List<VideoFormat>> OnVideoFormatsNegotiated;

    /// <summary>
    /// Gets fired when the remote SDP is received and the set of common video formats is set. (using its index)
    /// </summary>
    public event Action<int, List<VideoFormat>> OnVideoFormatsNegotiatedByIndex;

    /// <summary>
    /// Gets fired when a full video frame is reconstructed from one or more RTP packets
    /// received from the remote party. (on the primary one)
    /// </summary>
    /// <remarks>
    ///  - Received from end point,
    ///  - The frame timestamp,
    ///  - The encoded video frame payload.
    ///  - The video format of the encoded frame.
    /// </remarks>
    public event Action<IPEndPoint, uint, byte[], VideoFormat> OnVideoFrameReceived;

    /// <summary>
    /// Gets fired when a full video frame is reconstructed from one or more RTP packets
    /// received from the remote party. (using its index)
    /// </summary>
    /// <remarks>
    ///  - Index of the VideoStream
    ///  - Received from end point,
    ///  - The frame timestamp,
    ///  - The encoded video frame payload.
    ///  - The video format of the encoded frame.
    /// </remarks>
    public event Action<int, IPEndPoint, uint, byte[], VideoFormat> OnVideoFrameReceivedByIndex;

    /// <summary>
    /// Gets fired when an RTP packet is received from a remote party. (on the primary one)
    /// Parameters are:
    ///  - Remote endpoint packet was received from,
    ///  - The media type the packet contains, will be audio or video,
    ///  - The full RTP packet.
    /// </summary>
    public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;

    /// <summary>
    /// Gets fired when an RTP packet is received from a remote party (using its index).
    /// Parameters are:
    ///  - index of the AudioStream or VideoStream
    ///  - Remote endpoint packet was received from,
    ///  - The media type the packet contains, will be audio or video,
    ///  - The full RTP packet.
    /// </summary>
    public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceivedByIndex;

    /// <summary>
    /// Gets fired when an RTP event is detected on the remote call party's RTP stream (on the primary one).
    /// </summary>
    public event Action<IPEndPoint, RTPEvent, RTPHeader> OnRtpEvent;

    /// <summary>
    /// Gets fired when an RTP event is detected on the remote call party's RTP stream (using its index).
    /// </summary>
    public event Action<int, IPEndPoint, RTPEvent, RTPHeader> OnRtpEventByIndex;

    /// <summary>
    /// Gets fired when the RTP session and underlying channel are closed.
    /// </summary>
    public event Action<string> OnRtpClosed;

    /// <summary>
    /// Gets fired when an RTCP BYE packet is received from the remote party.
    /// The string parameter contains the BYE reason. Normally a BYE
    /// report means the RTP session is finished. But... cases have been observed where
    /// an RTCP BYE is received when a remote party is put on hold and then the session
    /// resumes when take off hold. It's up to the application to decide what action to
    /// take when n RTCP BYE is received.
    /// </summary>
    public event Action<string> OnRtcpBye;

    /// <summary>
    /// Fires when the connection for a media type (the primary one) is classified as timed out due to not
    /// receiving any RTP or RTCP packets within the given period.
    /// </summary>
    public event Action<SDPMediaTypesEnum> OnTimeout;

    /// <summary>
    /// Fires when the connection for a media type (using its index) is classified as timed out due to not
    /// receiving any RTP or RTCP packets within the given period.
    /// </summary>
    public event Action<int, SDPMediaTypesEnum> OnTimeoutByIndex;

    /// <summary>
    /// Gets fired when an RTCP report is received (the primary one). This event is for diagnostics only.
    /// </summary>
    public event Action<IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReport;

    /// <summary>
    /// Gets fired when an RTCP report is received (using its index). This event is for diagnostics only.
    /// </summary>
    public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReportByIndex;

    /// <summary>
    /// Gets fired when an RTCP report is sent (the primary one). This event is for diagnostics only.
    /// </summary>
    public event Action<SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReport;

    /// <summary>
    /// Gets fired when an RTCP report is sent (using its nidex). This event is for diagnostics only.
    /// </summary>
    public event Action<int, SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReportByIndex;

    /// <summary>
    /// Gets fired when the start method is called on the session. This is the point
    /// audio and video sources should commence generating samples.
    /// </summary>
    public event Action OnStarted;

    /// <summary>
    /// Gets fired when the session is closed. This is the point audio and video
    /// source should stop generating samples.
    /// </summary>
    public event Action OnClosed;

    /// <summary>
    /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
    /// pseudo random values.
    /// </summary>
    /// <param name="isRtcpMultiplexed">If true RTCP reports will be multiplexed with RTP on a single channel.
    /// If false (standard mode) then a separate socket is used to send and receive RTCP reports.</param>
    /// <param name="isSecure">If true indicated this session is using SRTP to encrypt and authorise
    /// RTP and RTCP packets. No communications or reporting will commence until the
    /// is explicitly set as complete.</param>
    /// <param name="isMediaMultiplexed">If true only a single RTP socket will be used for both audio
    /// and video (standard case for WebRTC). If false two separate RTP sockets will be used for
    /// audio and video (standard case for VoIP).</param>
    /// <param name="bindAddress">Optional. If specified this address will be used as the bind address for any RTP
    /// and control sockets created. Generally this address does not need to be set. The default behaviour
    /// is to bind to [::] or 0.0.0.0,d depending on system support, which minimises network routing
    /// causing connection issues.</param>
    /// <param name="bindPort">Optional. If specified a single attempt will be made to bind the RTP socket
    /// on this port. It's recommended to leave this parameter as the default of 0 to let the Operating
    /// System select the port number.</param>
    public RTPSession(bool isMediaMultiplexed, bool isRtcpMultiplexed, IPAddress bindAddress = null, int bindPort = 0)
        : this(new RtpSessionConfig
        {
            IsMediaMultiplexed = isMediaMultiplexed,
            IsRtcpMultiplexed = isRtcpMultiplexed,
            BindAddress = bindAddress,
            BindPort = bindPort,
        })
    {
    }

    /// <summary>
    /// Creates a new RTP session. The synchronisation source and sequence number are initialised to
    /// pseudo random values.
    /// </summary>
    /// <param name="config">Contains required settings.</param>
    public RTPSession(RtpSessionConfig config)
    {
        rtpSessionConfig = config;
        m_sdpSessionID = Crypto.GetRandomInt(SDP_SESSIONID_LENGTH).ToString();
    }


    protected void ResetRemoteSDPSsrcAttributes()
    {
        audioRemoteSDPSsrcAttributes.Clear();
        videoRemoteSDPSsrcAttributes.Clear();
    }

    protected void AddRemoteSDPSsrcAttributes(SDPMediaTypesEnum mediaType, List<SDPSsrcAttribute> sdpSsrcAttributes)
    {
        if (mediaType == SDPMediaTypesEnum.audio)
        {
            audioRemoteSDPSsrcAttributes.Add(sdpSsrcAttributes);
        }
        else if (mediaType == SDPMediaTypesEnum.video)
        {
            videoRemoteSDPSsrcAttributes.Add(sdpSsrcAttributes);
        }
    }

    protected void LogRemoteSDPSsrcAttributes()
    {
        string str = "Audio:[ ";
        foreach (var audioRemoteSDPSsrcAttribute in audioRemoteSDPSsrcAttributes)
        {
            foreach (var attr in audioRemoteSDPSsrcAttribute)
            {
                str += attr.SSRC + " - ";
            }
        }
        str += "] \r\n Video: [ ";
        foreach (var videoRemoteSDPSsrcAttribute in videoRemoteSDPSsrcAttributes)
        {
            str += " [";
            foreach (var attr in videoRemoteSDPSsrcAttribute)
            {
                str += attr.SSRC + " - ";
            }
            str += "] ";
        }
        str += " ]";
        logger.LogDebug($"LogRemoteSDPSsrcAttributes: {str}");
    }

    private void CreateRtcpSession(MediaStream mediaStream)
    {
        if (mediaStream.CreateRtcpSession())
        {
            mediaStream.OnTimeoutByIndex += RaiseOnTimeOut;
            mediaStream.OnSendReportByIndex += RaiseOnSendReport;
            mediaStream.OnRtpEventByIndex += RaisedOnRtpEvent;
            mediaStream.OnRtpPacketReceivedByIndex += RaisedOnRtpPacketReceived;
            mediaStream.OnReceiveReportByIndex += RaisedOnOnReceiveReport;

            if (mediaStream.MediaType == SDPMediaTypesEnum.audio)
            {
                if (mediaStream is AudioStream audioStream)
                {
                    audioStream.OnAudioFormatsNegotiatedByIndex += RaisedOnAudioFormatsNegotiated;
                }
            }
            else
            {
                if (mediaStream is VideoStream videoStream)
                {
                    videoStream.OnVideoFormatsNegotiatedByIndex += RaisedOnVideoFormatsNegotiated;
                    videoStream.OnVideoFrameReceivedByIndex += RaisedOnOnVideoFrameReceived;
                }
            }
        }
    }

    private void CloseRtcpSession(MediaStream mediaStream, string reason)
    {
        if (mediaStream.RtcpSession != null)
        {
            mediaStream.OnTimeoutByIndex -= RaiseOnTimeOut;
            mediaStream.OnSendReportByIndex -= RaiseOnSendReport;
            mediaStream.OnRtpEventByIndex -= RaisedOnRtpEvent;
            mediaStream.OnRtpPacketReceivedByIndex -= RaisedOnRtpPacketReceived;
            mediaStream.OnReceiveReportByIndex -= RaisedOnOnReceiveReport;

            if (mediaStream.MediaType == SDPMediaTypesEnum.audio)
            {
                if (mediaStream is AudioStream audioStream)
                {
                    audioStream.OnAudioFormatsNegotiatedByIndex -= RaisedOnAudioFormatsNegotiated;
                }
            }
            else
            {
                if (mediaStream is VideoStream videoStream)
                {
                    videoStream.OnVideoFormatsNegotiatedByIndex -= RaisedOnVideoFormatsNegotiated;
                    videoStream.OnVideoFrameReceivedByIndex -= RaisedOnOnVideoFrameReceived;
                }
            }

            mediaStream.RtcpSession.Close(reason);
            mediaStream.RtcpSession = null;
        }
    }

    private void RaiseOnTimeOut(int index, SDPMediaTypesEnum media)
    {
        if (index == 0)
        {
            OnTimeout?.Invoke(media);
        }
        OnTimeoutByIndex?.Invoke(index, media);
    }

    private void RaiseOnSendReport(int index, SDPMediaTypesEnum media, RTCPCompoundPacket report)
    {
        if (index == 0)
        {
            OnSendReport?.Invoke(media, report);
        }
        OnSendReportByIndex?.Invoke(index, media, report);
    }

    private void RaisedOnRtpEvent(int index, IPEndPoint ipEndPoint, RTPEvent rtpEvent, RTPHeader rtpHeader)
    {
        if (index == 0)
        {
            OnRtpEvent?.Invoke(ipEndPoint, rtpEvent, rtpHeader);
        }
        OnRtpEventByIndex?.Invoke(index, ipEndPoint, rtpEvent, rtpHeader);
    }

    private void RaisedOnRtpPacketReceived(int index, IPEndPoint ipEndPoint, SDPMediaTypesEnum media, RTPPacket rtpPacket)
    {
        if (index == 0)
        {
            OnRtpPacketReceived?.Invoke(ipEndPoint, media, rtpPacket);
        }
        OnRtpPacketReceivedByIndex?.Invoke(index, ipEndPoint, media, rtpPacket);
    }

    private void RaisedOnOnReceiveReport(int index, IPEndPoint ipEndPoint, SDPMediaTypesEnum media, RTCPCompoundPacket report)
    {
        if (index == 0)
        {
            OnReceiveReport?.Invoke(ipEndPoint, media, report);
        }
        OnReceiveReportByIndex?.Invoke(index, ipEndPoint, media, report);
    }

    private void RaisedOnAudioFormatsNegotiated(int index, List<AudioFormat> audioFormats)
    {
        if (index == 0)
        {
            OnAudioFormatsNegotiated?.Invoke(audioFormats);
        }
        OnAudioFormatsNegotiatedByIndex?.Invoke(index, audioFormats);
    }

    private void RaisedOnVideoFormatsNegotiated(int index, List<VideoFormat> videoFormats)
    {
        if (index == 0)
        {
            OnVideoFormatsNegotiated?.Invoke(videoFormats);
        }
        OnVideoFormatsNegotiatedByIndex?.Invoke(index, videoFormats);
    }

    private void RaisedOnOnVideoFrameReceived(int index, IPEndPoint ipEndPoint, uint timestamp, byte[] frame, VideoFormat videoFormat)
    {
        if (index == 0)
        {
            OnVideoFrameReceived?.Invoke(ipEndPoint, timestamp, frame, videoFormat);
        }
        OnVideoFrameReceivedByIndex?.Invoke(index, ipEndPoint, timestamp, frame, videoFormat);
    }

    protected virtual AudioStream GetOrCreateAudioStream(int index)
    {
        if (index < AudioStreamList.Count)
        {
            // We ask too fast a new AudioStram ...
            return AudioStreamList[index];
        }
        else if (index == AudioStreamList.Count)
        {
            AudioStream audioStream = new AudioStream(rtpSessionConfig, index);
            AudioStreamList.Add(audioStream);
            return audioStream;
        }
        return null;
    }

    protected virtual VideoStream GetOrCreateVideoStream(int index)
    {
        if (index < VideoStreamList.Count)
        {
            // We ask too fast a new AudioStram ...
            return VideoStreamList[index];
        }
        else if (index == VideoStreamList.Count)
        {
            VideoStream videoStream = new VideoStream(rtpSessionConfig, index);
            VideoStreamList.Add(videoStream);
            return videoStream;
        }
        return null;
    }

    /// <summary>
    /// Sets the remote SDP description for this session.
    /// </summary>
    /// <param name="sessionDescription">The SDP that will be set as the remote description.</param>
    /// <returns>If successful an OK enum result. If not an enum result indicating the failure cause.</returns>
    public virtual SetDescriptionResultEnum SetRemoteDescription(SDP sessionDescription)
    {
        if (sessionDescription == null)
        {
            throw new ArgumentNullException("sessionDescription", "The session description cannot be null for SetRemoteDescription.");
        }

        try
        {
            if (sessionDescription.Media?.Count == 0)
            {
                return SetDescriptionResultEnum.NoRemoteMedia;
            }
            else if (sessionDescription.Media?.Count == 1)
            {
                var remoteMediaType = sessionDescription.Media.First().Media;
                if (remoteMediaType == SDPMediaTypesEnum.audio)
                {
                    return SetDescriptionResultEnum.NoMatchingMediaType;
                }
                else if (remoteMediaType == SDPMediaTypesEnum.video)
                {
                    return SetDescriptionResultEnum.NoMatchingMediaType;
                }
            }

            //Remove Remote Tracks before add new one (this was added to implement renegotiation logic)
            foreach (var audioStream in AudioStreamList)
            {
                audioStream.RemoteTrack = null;
            }

            foreach (var videoStream in VideoStreamList)
            {
                videoStream.RemoteTrack = null;
            }

            int currentAudioStreamCount = 0;
            int currentVideoStreamCount = 0;
            MediaStream currentMediaStream;

            foreach (var announcement in sessionDescription.Media.Where(x => x.Media == SDPMediaTypesEnum.audio || x.Media == SDPMediaTypesEnum.video))
            {
                if (announcement.Media == SDPMediaTypesEnum.audio)
                {
                    currentMediaStream = GetOrCreateAudioStream(currentAudioStreamCount++);
                    if (currentMediaStream == null)
                    {
                        return SetDescriptionResultEnum.Error;
                    }
                }
                else
                {
                    currentMediaStream = GetOrCreateVideoStream(currentVideoStreamCount++);
                    if (currentMediaStream == null)
                    {
                        return SetDescriptionResultEnum.Error;
                    }
                }

                MediaStreamStatusEnum mediaStreamStatus = announcement.MediaStreamStatus.HasValue ? announcement.MediaStreamStatus.Value : MediaStreamStatusEnum.SendRecv;
                var remoteTrack = new MediaStreamTrack(announcement.Media, announcement.MediaFormats.Values.ToList(), mediaStreamStatus, announcement.SsrcAttributes, announcement.HeaderExtensions);

                currentMediaStream.RemoteTrack = remoteTrack;

                List<SDPAudioVideoMediaFormat> capabilities = null;
                capabilities = remoteTrack.Capabilities;

                if (currentMediaStream.MediaType == SDPMediaTypesEnum.audio)
                {
                    if (capabilities?.Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE).Count() == 0)
                    {
                        return SetDescriptionResultEnum.AudioIncompatible;
                    }
                }
                else if (capabilities?.Count == 0)
                {
                    return SetDescriptionResultEnum.VideoIncompatible;

                }
            }

            //Close old RTCPSessions opened
            foreach (var audioStream in AudioStreamList)
            {
                audioStream.RtcpSession.Close(null);
            }

            //Close old RTCPSessions opened
            foreach (var videoStream in VideoStreamList)
            {
                videoStream.RtcpSession.Close(null);
            }

            RemoteDescription = sessionDescription;

            return SetDescriptionResultEnum.OK;
        }
        catch (Exception excp)
        {
            logger.LogError($"Exception in RTPSession SetRemoteDescription. {excp.Message}.");
            return SetDescriptionResultEnum.Error;
        }
    }

    /// <summary>
    /// Gets the RTP end point for an SDP media announcement from the remote peer.
    /// </summary>
    /// <param name="announcement">The media announcement to get the connection address for.</param>
    /// <param name="connectionAddress">The remote SDP session level connection address. Will be null if not available.</param>
    /// <returns>An IP end point for an SDP media announcement from the remote peer.</returns>
    private IPEndPoint GetAnnouncementRTPDestination(SDPMediaAnnouncement announcement, IPAddress connectionAddress)
    {
        SDPMediaTypesEnum kind = announcement.Media;
        IPEndPoint rtpEndPoint = null;

        var remoteAddr = (announcement.Connection != null) ? IPAddress.Parse(announcement.Connection.ConnectionAddress) : connectionAddress;

        if (remoteAddr != null)
        {
            if (announcement.Port < IPEndPoint.MinPort || announcement.Port > IPEndPoint.MaxPort)
            {
                logger.LogWarning($"Remote {kind} announcement contained an invalid port number {announcement.Port}.");

                // Set the remote port number to "9" which means ignore and wait for it be set some other way
                // such as when a remote RTP packet or arrives or ICE negotiation completes.
                rtpEndPoint = new IPEndPoint(remoteAddr, SDP.IGNORE_RTP_PORT_NUMBER);
            }
            else
            {
                rtpEndPoint = new IPEndPoint(remoteAddr, announcement.Port);
            }
        }

        return rtpEndPoint;
    }

    /// <summary>
    /// Used for child classes that require a single RTP channel for all RTP (audio and video)
    /// and RTCP communications.
    /// </summary>
    public void addSingleTrack(Boolean videoAsPrimary)
    {
        if (videoAsPrimary)
        {
            m_primaryStream = GetNextVideoStreamByLocalTrack();
        }
        else
        {
            m_primaryStream = GetNextAudioStreamByLocalTrack();
        }

        InitMediaStream(m_primaryStream);
    }

    private void InitMediaStream(MediaStream currentMediaStream)
    {
        var rtpChannel = CreateRtpChannel();
        currentMediaStream.AddRtpChannel(rtpChannel);
        CreateRtcpSession(currentMediaStream);
    }

    /// <summary>
    /// Removes a remote media stream to this session.
    /// </summary>
    /// <param name="track">The remote track to remove.</param>
    public bool RemoveRemoteTrack(MediaStreamTrack track)
    {
        // TODO - CI - Do we need to do something else ? How to remove an Audio/Video Stream ?
        if (track == null)
        {
            return false;
        }

        if (track.Kind == SDPMediaTypesEnum.audio)
        {
            AudioStream audioStream = null;

            foreach (var checkAudioStream in AudioStreamList)
            {
                if (checkAudioStream.RemoteTrack == track)
                {
                    checkAudioStream.RemoteTrack = null;
                    audioStream = checkAudioStream;
                    break;
                }
            }

            if (audioStream != null)
            {
                //if ( (audioStream.LocalTrack == null) && (audioStream.RemoteTrack == null) )
                //{
                //    AudioStreamList.Remove(audioStream);
                //}
                return true;
            }

        }
        else if (track.Kind == SDPMediaTypesEnum.video)
        {
            VideoStream videoStream = null;
            foreach (var checkVideoStream in VideoStreamList)
            {
                if (checkVideoStream.RemoteTrack == track)
                {
                    checkVideoStream.RemoteTrack = null;
                    videoStream = checkVideoStream;
                    break;
                }
            }

            if (videoStream != null)
            {
                //if ( (videoStream.LocalTrack == null) && (videoStream.RemoteTrack == null) )
                //{
                //    VideoStreamList.Remove(videoStream);
                //}
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a remote media stream to this session. Typically the only way remote tracks
    /// should get added is from setting the remote session description. Adding a remote
    /// track does not cause the creation of any local resources.
    /// </summary>
    /// <param name="track">The remote track to add.</param>
    public void AddRemoteTrack(MediaStreamTrack track)
    {
        MediaStream currentMediaStream;
        if (track.Kind == SDPMediaTypesEnum.audio)
        {
            currentMediaStream = GetNextAudioStreamByRemoteTrack();
        }
        else if (track.Kind == SDPMediaTypesEnum.video)
        {
            currentMediaStream = GetNextVideoStreamByRemoteTrack();
        }
        else
        {
            return;
        }

        currentMediaStream.RemoteTrack = track;

        // Even if there's no local audio/video track an RTCP session can still be required
        // in case the remote party send reports (presumably in case we decide we do want
        // to send or receive audio on this session at some later stage).
        CreateRtcpSession(currentMediaStream);
    }

    protected void SetGlobalDestination(IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
    {
        foreach (var audioStream in AudioStreamList)
        {
            audioStream.SetDestination(rtpEndPoint, rtcpEndPoint);
        }

        foreach (var videoStream in VideoStreamList)
        {
            videoStream.SetDestination(rtpEndPoint, rtcpEndPoint);
        }
    }

    private void InitIPEndPointAndSecurityContext(MediaStream mediaStream)
    {
        // Get primary AudioStream
        if ((m_primaryStream != null) && (mediaStream != null))
        {
            mediaStream.SetDestination(m_primaryStream.DestinationEndPoint, m_primaryStream.ControlDestinationEndPoint);
        }
    }

    protected virtual AudioStream GetNextAudioStreamByLocalTrack()
    {
        int index = AudioStreamList.Count;
        if (index > 0)
        {
            foreach (var audioStream in AudioStreamList)
            {
                return audioStream;
            }
        }

        // We need to create new AudioStream
        var newAudioStream = GetOrCreateAudioStream(index);

        // If it's not the first one we need to init it
        if (index != 0)
        {
            InitIPEndPointAndSecurityContext(newAudioStream);
        }

        return newAudioStream;
    }

    private AudioStream GetNextAudioStreamByRemoteTrack()
    {
        int index = AudioStreamList.Count;
        if (index > 0)
        {
            foreach (var audioStream in AudioStreamList)
            {
                if (audioStream.RemoteTrack == null)
                {
                    return audioStream;
                }
            }
        }

        // We need to create new AudioStream
        var newAudioStream = GetOrCreateAudioStream(index);

        // If it's not the first one we need to init it
        if (index != 0)
        {
            InitIPEndPointAndSecurityContext(newAudioStream);
        }

        return newAudioStream;
    }

    protected virtual VideoStream GetNextVideoStreamByLocalTrack()
    {
        int index = VideoStreamList.Count;
        if (index > 0)
        {
            foreach (var videoStream in VideoStreamList)
            {
                return videoStream;
            }
        }

        // We need to create new VideoStream and Init it
        var newVideoStream = GetOrCreateVideoStream(index);

        InitIPEndPointAndSecurityContext(newVideoStream);
        return newVideoStream;
    }

    private VideoStream GetNextVideoStreamByRemoteTrack()
    {
        int index = VideoStreamList.Count;
        if (index > 0)
        {
            foreach (var videoStream in VideoStreamList)
            {
                if (videoStream.RemoteTrack == null)
                {
                    return videoStream;
                }
            }
        }

        // We need to create new VideoStream and Init it
        var newVideoStream = GetOrCreateVideoStream(index);

        InitIPEndPointAndSecurityContext(newVideoStream);
        return newVideoStream;
    }

    /// <summary>
    /// Creates a new RTP channel (which manages the UDP socket sending and receiving RTP
    /// packets) for use with this session.
    /// </summary>
    /// <param name="mediaType">The type of media the RTP channel is for. Must be audio or video.</param>
    /// <returns>A new RTPChannel instance.</returns>
    protected virtual RTPChannel CreateRtpChannel()
    {
        if (rtpSessionConfig.IsMediaMultiplexed)
        {
            if (MultiplexRtpChannel != null)
            {
                return MultiplexRtpChannel;
            }
        }

        // If RTCP is multiplexed we don't need a control socket.
        int bindPort = (rtpSessionConfig.BindPort == 0) ? 0 : rtpSessionConfig.BindPort + m_rtpChannelsCount * 2;
        var rtpChannel = new RTPChannel(!rtpSessionConfig.IsRtcpMultiplexed, rtpSessionConfig.BindAddress, bindPort);


        if (rtpSessionConfig.IsMediaMultiplexed)
        {
            MultiplexRtpChannel = rtpChannel;
        }

        rtpChannel.OnRTPDataReceived += OnReceive;
        rtpChannel.OnControlDataReceived += OnReceive; // RTCP packets could come on RTP or control socket.
        rtpChannel.OnClosed += OnRTPChannelClosed;

        // Start the RTP, and if required the Control, socket receivers and the RTCP session.
        rtpChannel.Start();


        m_rtpChannelsCount++;

        return rtpChannel;
    }

    /// <summary>
    /// Starts the RTCP session(s) that monitor this RTP session.
    /// </summary>
    public virtual Task Start()
    {
        if (!IsStarted)
        {
            IsStarted = true;

            foreach (var audioStream in AudioStreamList)
            {
                if (audioStream.HasAudio && audioStream.RtcpSession != null)
                {
                    // The local audio track may have been disabled if there were no matching capabilities with
                    // the remote party.
                    audioStream.RtcpSession.Start();
                }
            }

            foreach (var videoStream in VideoStreamList)
            {
                if (videoStream.HasVideo && videoStream.RtcpSession != null)
                {
                    // The local video track may have been disabled if there were no matching capabilities with
                    // the remote party.
                    videoStream.RtcpSession.Start();
                }
            }

            OnStarted?.Invoke();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Close the session and RTP channel.
    /// </summary>
    public virtual void Close(string reason)
    {
        if (!IsClosed)
        {
            IsClosed = true;


            foreach (var audioStream in AudioStreamList)
            {
                if (audioStream != null)
                {
                    audioStream.IsClosed = true;
                    CloseRtcpSession(audioStream, reason);

                    if (audioStream.HasRtpChannel())
                    {
                        var rtpChannel = audioStream.GetRTPChannel();
                        rtpChannel.OnRTPDataReceived -= OnReceive;
                        rtpChannel.OnControlDataReceived -= OnReceive;
                        rtpChannel.OnClosed -= OnRTPChannelClosed;
                        rtpChannel.Close(reason);
                    }
                }
            }

            foreach (var videoStream in VideoStreamList)
            {
                if (videoStream != null)
                {
                    videoStream.IsClosed = true;
                    CloseRtcpSession(videoStream, reason);

                    if (videoStream.HasRtpChannel())
                    {
                        var rtpChannel = videoStream.GetRTPChannel();
                        rtpChannel.OnRTPDataReceived -= OnReceive;
                        rtpChannel.OnControlDataReceived -= OnReceive;
                        rtpChannel.OnClosed -= OnRTPChannelClosed;
                        rtpChannel.Close(reason);
                    }
                }
            }

            OnRtpClosed?.Invoke(reason);
            OnClosed?.Invoke();
        }
    }

    protected void OnReceive(int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
    {
        if (remoteEndPoint.Address.IsIPv4MappedToIPv6)
        {
            // Required for matching existing RTP end points (typically set from SDP) and
            // whether or not the destination end point should be switched.
            remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv4();
        }

        // Quick sanity check on whether this is not an RTP or RTCP packet.
        if (buffer?.Length > RTPHeader.MIN_HEADER_LEN && buffer[0] >= 128 && buffer[0] <= 191)
        {
            if (Enum.IsDefined(typeof(RTCPReportTypesEnum), buffer[1]))
            {
                // Only call OnReceiveRTCPPacket for supported RTCPCompoundPacket types
                if (buffer[1] == (byte)RTCPReportTypesEnum.SR ||
                    buffer[1] == (byte)RTCPReportTypesEnum.RR ||
                    buffer[1] == (byte)RTCPReportTypesEnum.SDES ||
                    buffer[1] == (byte)RTCPReportTypesEnum.BYE ||
                    buffer[1] == (byte)RTCPReportTypesEnum.PSFB ||
                    buffer[1] == (byte)RTCPReportTypesEnum.RTPFB)
                {
                    OnReceiveRTCPPacket(localPort, remoteEndPoint, buffer);
                }
            }
            else
            {
                OnReceiveRTPPacket(localPort, remoteEndPoint, buffer);
            }
        }
    }

    private void OnReceiveRTCPPacket(int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
    {
        //logger.LogDebug($"RTCP packet received from {remoteEndPoint} {buffer.HexStr()}");

        #region RTCP packet.

        // Get the SSRC in order to be able to figure out which media type
        // This will let us choose the apropriate unprotect methods
        uint ssrc;
        if (BitConverter.IsLittleEndian)
        {
            ssrc = NetConvert.DoReverseEndian(BitConverter.ToUInt32(buffer, 4));
        }
        else
        {
            ssrc = BitConverter.ToUInt32(buffer, 4);
        }

        MediaStream mediaStream = GetMediaStream(ssrc);
        if (mediaStream == null)
        {
            logger.LogWarning($"Could not find appropriate remote track for SSRC for RTCP packet - Ssrc:{ssrc}");
        }

        var rtcpPkt = new RTCPCompoundPacket(buffer);
        if (rtcpPkt != null)
        {
            mediaStream = GetMediaStream(rtcpPkt);
            if (rtcpPkt.Bye != null)
            {
                logger.LogDebug($"RTCP BYE received for SSRC {rtcpPkt.Bye.SSRC}, reason {rtcpPkt.Bye.Reason}.");

                // In some cases, such as a SIP re-INVITE, it's possible the RTP session
                // will keep going with a new remote SSRC.
                if (mediaStream?.RemoteTrack != null && rtcpPkt.Bye.SSRC == mediaStream.RemoteTrack.Ssrc)
                {
                    mediaStream.RtcpSession?.RemoveReceptionReport(rtcpPkt.Bye.SSRC);
                    //AudioDestinationEndPoint = null;
                    //AudioControlDestinationEndPoint = null;
                    mediaStream.RemoteTrack.Ssrc = 0;
                }
                else
                {
                    // We close peer connection only if there is no more local/remote tracks on the primary stream
                    if (m_primaryStream.RemoteTrack == null)
                    {
                        OnRtcpBye?.Invoke(rtcpPkt.Bye.Reason);
                    }
                }
            }
            else if (!IsClosed)
            {
                if (mediaStream?.RtcpSession != null)
                {
                    if (mediaStream.RtcpSession.LastActivityAt == DateTime.MinValue)
                    {
                        // On the first received RTCP report for a session check whether the remote end point matches the
                        // expected remote end point. If not it's "likely" that a private IP address was specified in the SDP.
                        // Take the risk and switch the remote control end point to the one we are receiving from.
                        if ((mediaStream.ControlDestinationEndPoint == null ||
                             !mediaStream.ControlDestinationEndPoint.Address.Equals(remoteEndPoint.Address) ||
                             mediaStream.ControlDestinationEndPoint.Port != remoteEndPoint.Port))
                        {
                            logger.LogDebug($"{mediaStream.MediaType} control end point switched from {mediaStream.ControlDestinationEndPoint} to {remoteEndPoint}.");
                            mediaStream.ControlDestinationEndPoint = remoteEndPoint;
                        }
                    }

                    mediaStream.RtcpSession.ReportReceived(remoteEndPoint, rtcpPkt);
                    mediaStream.RaiseOnReceiveReportByIndex(remoteEndPoint, rtcpPkt);
                }
                else if (rtcpPkt.ReceiverReport?.SSRC == RTCP_RR_NOSTREAM_SSRC)
                {
                    // Ignore for the time being. Not sure what use an empty RTCP Receiver Report can provide.
                }
                else if (AudioStream?.RtcpSession?.PacketsReceivedCount > 0 || VideoStream?.RtcpSession?.PacketsReceivedCount > 0)
                {
                    // Only give this warning if we've received at least one RTP packet.
                    //logger.LogWarning("Could not match an RTCP packet against any SSRC's in the session.");
                    //logger.LogTrace(rtcpPkt.GetDebugSummary());
                }
            }
        }
        else
        {
            logger.LogWarning("Failed to parse RTCP compound report.");
        }

        #endregion
    }

    private void OnReceiveRTPPacket(int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
    {
        if (!IsClosed)
        {
            var hdr = new RTPHeader(buffer);

            MediaStream mediaStream = GetMediaStream(hdr.SyncSource);

            if ((mediaStream == null) && (AudioStreamList.Count < 2) && (VideoStreamList.Count < 2))
            {
                mediaStream = GetMediaStreamFromPayloadType(hdr.PayloadType);
            }

            if (mediaStream == null)
            {
                logger.LogWarning($"An RTP packet with SSRC {hdr.SyncSource} and payload ID {hdr.PayloadType} was received that could not be matched to an audio or video stream.");
                return;
            }

            hdr.ReceivedTime = DateTime.Now;
            if (mediaStream.MediaType == SDPMediaTypesEnum.audio)
            {
                mediaStream.OnReceiveRTPPacket(hdr, localPort, remoteEndPoint, buffer, null);
            }
            else if (mediaStream.MediaType == SDPMediaTypesEnum.video)
            {
                mediaStream.OnReceiveRTPPacket(hdr, localPort, remoteEndPoint, buffer, mediaStream as VideoStream);
            }
        }
    }

    private MediaStream GetMediaStreamFromPayloadType(int payloadId)
    {
        foreach (var audioStream in AudioStreamList)
        {
            if (audioStream.RemoteTrack != null && audioStream.RemoteTrack.IsPayloadIDMatch(payloadId))
            {
                return audioStream;
            }
        }

        foreach (var videoStream in VideoStreamList)
        {
            if (videoStream.RemoteTrack != null && videoStream.RemoteTrack.IsPayloadIDMatch(payloadId))
            {
                return videoStream;
            }
        }

        return null;
    }

    private MediaStream GetMediaStream(uint ssrc)
    {
        if (HasAudio)
        {
            if (!HasVideo)
            {
                return AudioStream;
            }
        }
        else
        {
            if (HasVideo)
            {
                return VideoStream;
            }
        }

        foreach (var audioStream in AudioStreamList)
        {
            if (audioStream?.RemoteTrack?.IsSsrcMatch(ssrc) == true)
            {
                return audioStream;
            }
        }

        foreach (var videoStream in VideoStreamList)
        {
            if (videoStream?.RemoteTrack?.IsSsrcMatch(ssrc) == true)
            {
                return videoStream;
            }
        }

        return GetMediaStreamRemoteSDPSsrcAttributes(ssrc);
    }

    private MediaStream GetMediaStreamRemoteSDPSsrcAttributes(uint ssrc)
    {
        if (ssrc < 200)
        {
            return null;
        }

        bool found = false;
        int index;

        // Loop au audioRemoteSDPSsrcAttributes
        for (index = 0; index < audioRemoteSDPSsrcAttributes.Count; index++)
        {
            foreach (var ssrcAttributes in audioRemoteSDPSsrcAttributes[index])
            {
                if (ssrcAttributes.SSRC == ssrc)
                {
                    found = true;
                    break;
                }
            }
            if (found)
            {
                break;
            }
        }

        // Get related AudioStream if found
        if (found && (AudioStreamList.Count > index))
        {
            var audioStream = AudioStreamList[index];
            //if (audioStream?.RemoteTrack != null)
            //{
            //    audioStream.RemoteTrack.Ssrc = ssrc;
            //}
            return audioStream;
        }

        // Loop au videoRemoteSDPSsrcAttributes
        found = false;
        for (index = 0; index < videoRemoteSDPSsrcAttributes.Count; index++)
        {
            foreach (var ssrcAttributes in videoRemoteSDPSsrcAttributes[index])
            {
                if (ssrcAttributes.SSRC == ssrc)
                {
                    found = true;
                    break;
                }
            }
            if (found)
            {
                break;
            }
        }

        // Get related VideoStreamList if found
        if (found && (VideoStreamList.Count > index))
        {
            var videoStream = VideoStreamList[index];
            //if (videoStream?.RemoteTrack != null)
            //{
            //    videoStream.RemoteTrack.Ssrc = ssrc;
            //}
            return videoStream;
        }

        return null;
    }

    /// <summary>
    /// Attempts to get MediaStream that matches a received RTCP report.
    /// </summary>
    /// <param name="rtcpPkt">The RTCP compound packet received from the remote party.</param>
    /// <returns>If a match could be found an SSRC the MediaStream otherwise null.</returns>
    private MediaStream GetMediaStream(RTCPCompoundPacket rtcpPkt)
    {
        if (rtcpPkt.SenderReport != null)
        {
            return GetMediaStream(rtcpPkt.SenderReport.SSRC);
        }
        else if (rtcpPkt.ReceiverReport != null)
        {
            return GetMediaStream(rtcpPkt.ReceiverReport.SSRC);
        }
        else if (rtcpPkt.Feedback != null)
        {
            return GetMediaStream(rtcpPkt.Feedback.SenderSSRC);
        }

        // No match on SR/RR SSRC. Check the individual reception reports for a known SSRC.
        List<ReceptionReportSample> receptionReports = null;

        if (rtcpPkt.SenderReport != null)
        {
            receptionReports = rtcpPkt.SenderReport.ReceptionReports;
        }
        else if (rtcpPkt.ReceiverReport != null)
        {
            receptionReports = rtcpPkt.ReceiverReport.ReceptionReports;
        }

        if (receptionReports != null && receptionReports.Count > 0)
        {
            foreach (var recRep in receptionReports)
            {
                var mediaStream = GetMediaStream(recRep.SSRC);
                if (mediaStream != null)
                {
                    return mediaStream;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Event handler for the RTP channel closure.
    /// </summary>
    private void OnRTPChannelClosed(string reason)
    {
        Close(reason);
    }

    /// <summary>
    /// Close the session if the instance is out of scope.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        Close("disposed");
    }

    /// <summary>
    /// Close the session if the instance is out of scope.
    /// </summary>
    public virtual void Dispose()
    {
        Close("disposed");
    }
}