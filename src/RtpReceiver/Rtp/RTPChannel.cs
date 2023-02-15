using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RtpReceiver.Rtp;

/// <summary>
/// A communications channel for transmitting and receiving Real-time Protocol (RTP) and
/// Real-time Control Protocol (RTCP) packets. This class performs the socket management
/// functions.
/// </summary>
public class RTPChannel : IDisposable
{
    private static ILogger logger = new NullLogger<RTPChannel>();
    protected UdpReceiver m_rtpReceiver;
    private Socket m_controlSocket;
    protected UdpReceiver m_controlReceiver;
    private bool m_rtpReceiverStarted = false;
    private bool m_controlReceiverStarted = false;
    private bool m_isClosed;

    public Socket RtpSocket { get; private set; }

    /// <summary>
    /// The last remote end point an RTP packet was sent to or received from. Used for
    /// reporting purposes only.
    /// </summary>
    protected IPEndPoint LastRtpDestination { get; set; }

    /// <summary>
    /// The last remote end point an RTCP packet was sent to or received from. Used for
    /// reporting purposes only.
    /// </summary>
    internal IPEndPoint LastControlDestination { get; private set; }

    /// <summary>
    /// The local port we are listening for RTP (and whatever else is multiplexed) packets on.
    /// </summary>
    public int RTPPort { get; private set; }

    /// <summary>
    /// The local end point the RTP socket is listening on.
    /// </summary>
    public IPEndPoint RTPLocalEndPoint { get; private set; }

    /// <summary>
    /// The local port we are listening for RTCP packets on.
    /// </summary>
    public int ControlPort { get; private set; }

    /// <summary>
    /// The local end point the control socket is listening on.
    /// </summary>
    public IPEndPoint ControlLocalEndPoint { get; private set; }

    /// <summary>
    /// Returns true if the RTP socket supports dual mode IPv4 and IPv6. If the control
    /// socket exists it will be the same.
    /// </summary>
    public bool IsDualMode
    {
        get
        {
            if (RtpSocket != null && RtpSocket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return RtpSocket.DualMode;
            }
            else
            {
                return false;
            }
        }
    }

    public bool IsClosed
    {
        get { return m_isClosed; }
    }

    public event Action<int, IPEndPoint, byte[]> OnRTPDataReceived;
    public event Action<int, IPEndPoint, byte[]> OnControlDataReceived;
    public event Action<string> OnClosed;

    /// <summary>
    /// Creates a new RTP channel. The RTP and optionally RTCP sockets will be bound in the constructor.
    /// They do not start receiving until the Start method is called.
    /// </summary>
    /// <param name="createControlSocket">Set to true if a separate RTCP control socket should be created. If RTP and
    /// RTCP are being multiplexed (as they are for WebRTC) there's no need to a separate control socket.</param>
    /// <param name="bindAddress">Optional. An IP address belonging to a local interface that will be used to bind
    /// the RTP and control sockets to. If left empty then the IPv6 any address will be used if IPv6 is supported
    /// and fallback to the IPv4 any address.</param>
    /// <param name="bindPort">Optional. The specific port to attempt to bind the RTP port on.</param>
    public RTPChannel(bool createControlSocket, IPAddress bindAddress, int bindPort = 0)
    {
        NetServices.CreateRtpSocket(createControlSocket, bindAddress, bindPort, out var rtpSocket, out m_controlSocket);

        if (rtpSocket == null)
        {
            throw new ApplicationException("The RTP channel was not able to create an RTP socket.");
        }
        else if (createControlSocket && m_controlSocket == null)
        {
            throw new ApplicationException("The RTP channel was not able to create a Control socket.");
        }

        RtpSocket = rtpSocket;
        RTPLocalEndPoint = RtpSocket.LocalEndPoint as IPEndPoint;
        RTPPort = RTPLocalEndPoint.Port;
        ControlLocalEndPoint = (m_controlSocket != null) ? m_controlSocket.LocalEndPoint as IPEndPoint : null;
        ControlPort = (m_controlSocket != null) ? ControlLocalEndPoint.Port : 0;
    }

    /// <summary>
    /// Starts listening on the RTP and control ports.
    /// </summary>
    public void Start()
    {
        StartRtpReceiver();
        StartControlReceiver();
    }

    /// <summary>
    /// Starts the UDP receiver that listens for RTP packets.
    /// </summary>
    private void StartRtpReceiver()
    {
        if (!m_rtpReceiverStarted)
        {
            m_rtpReceiverStarted = true;

            logger.LogDebug($"RTPChannel for {RtpSocket.LocalEndPoint} started.");

            m_rtpReceiver = new UdpReceiver(RtpSocket);
            m_rtpReceiver.OnPacketReceived += OnRTPPacketReceived;
            m_rtpReceiver.OnClosed += Close;
            m_rtpReceiver.BeginReceiveFrom();
        }
    }


    /// <summary>
    /// Starts the UDP receiver that listens for RTCP (control) packets.
    /// </summary>
    private void StartControlReceiver()
    {
        if (!m_controlReceiverStarted && m_controlSocket != null)
        {
            m_controlReceiverStarted = true;

            m_controlReceiver = new UdpReceiver(m_controlSocket);
            m_controlReceiver.OnPacketReceived += OnControlPacketReceived;
            m_controlReceiver.OnClosed += Close;
            m_controlReceiver.BeginReceiveFrom();
        }
    }

    /// <summary>
    /// Closes the session's RTP and control ports.
    /// </summary>
    public void Close(string reason)
    {
        if (!m_isClosed)
        {
            try
            {
                string closeReason = reason ?? "normal";

                if (m_controlReceiver == null)
                {
                    logger.LogDebug($"RTPChannel closing, RTP receiver on port {RTPPort}. Reason: {closeReason}.");
                }
                else
                {
                    logger.LogDebug($"RTPChannel closing, RTP receiver on port {RTPPort}, Control receiver on port {ControlPort}. Reason: {closeReason}.");
                }

                m_isClosed = true;
                m_rtpReceiver?.Close(null);
                m_controlReceiver?.Close(null);

                OnClosed?.Invoke(closeReason);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTPChannel.Close. " + excp);
            }
        }
    }

    /// <summary>
    /// Event handler for packets received on the RTP UDP socket.
    /// </summary>
    /// <param name="receiver">The UDP receiver the packet was received on.</param>
    /// <param name="localPort">The local port it was received on.</param>
    /// <param name="remoteEndPoint">The remote end point of the sender.</param>
    /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
    protected virtual void OnRTPPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
    {
        if (packet?.Length > 0)
        {
            LastRtpDestination = remoteEndPoint;
            OnRTPDataReceived?.Invoke(localPort, remoteEndPoint, packet);
        }
    }

    /// <summary>
    /// Event handler for packets received on the control UDP socket.
    /// </summary>
    /// <param name="receiver">The UDP receiver the packet was received on.</param>
    /// <param name="localPort">The local port it was received on.</param>
    /// <param name="remoteEndPoint">The remote end point of the sender.</param>
    /// <param name="packet">The raw packet received which should always be an RTCP packet.</param>
    private void OnControlPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
    {
        LastControlDestination = remoteEndPoint;
        OnControlDataReceived?.Invoke(localPort, remoteEndPoint, packet);
    }

    protected virtual void Dispose(bool disposing)
    {
        Close(null);
    }

    public void Dispose()
    {
        Close(null);
    }
}