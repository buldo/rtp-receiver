﻿namespace RtpReceiver.Rtp;

/// <summary>
/// The ICE set up roles that a peer can be in. The role determines how the DTLS
/// handshake is performed, i.e. which peer is the client and which is the server.
/// </summary>
public enum IceRolesEnum
{
    actpass = 0,
    passive = 1,
    active = 2
}