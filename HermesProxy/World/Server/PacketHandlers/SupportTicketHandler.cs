using System;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server;

public partial class WorldSocket
{
    [PacketHandler(Opcode.CMSG_SUPPORT_TICKET_SUBMIT_COMPLAINT)]
    void HandleSupportTicketSubmitComplaint(SupportTicketSubmitComplaint complaint)
    {
        var targetPlayerName = Session.GameState.GetPlayerName(complaint.TargetCharacterGuid);
        if (string.IsNullOrWhiteSpace(targetPlayerName))
        {
            Session.SendHermesTextMessage("Unable to report player because CharacterName was not resolved (can be fixed by restarting the client)", isError: true);
            return;
        }

        var ticketText = $"[REPORTED VIA QUICKMENU]\r\nI would like to report player '{targetPlayerName}'";

        if (!WowGuid128.IsUnknownPlayerGuid(complaint.TargetCharacterGuid))
            ticketText += $"  (id: {complaint.TargetCharacterGuid.GetCounter()})";

        if (complaint.ComplaintType != GmTicketComplaintType.Unknown)
            ticketText += $" for {complaint.ComplaintType}";

        if (complaint.SelectedMailInfo != null)
            ticketText += "\r\n" + $"Mail in question (id: {complaint.SelectedMailInfo.MailId}) with subject '{complaint.SelectedMailInfo.MailSubject}'";

        if (!complaint.TextNote.IsEmpty())
        {
            ticketText += "\r\n" + "-------------";
            ticketText += "\r\n" + complaint.TextNote;
        }

        WorldPacket packet = new WorldPacket(Opcode.CMSG_GM_TICKET_CREATE);

        if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V2_0_1_6180))
        {
            packet.WriteUInt8(2); // GMTICKET_BEHAVIOR_HARASSMENT
            packet.WriteUInt32(complaint.Header.SelfPlayerMapId);
            packet.WriteVector3(complaint.Header.SelfPlayerPos);
            packet.WriteCString(ticketText);
            // MIRASU (Kronos ticket parser 2026-05-23): Kronos's CMSG_GM_TICKET_CREATE
            // expects a trailing uint32 after the ticket text (likely the
            // "need_response_from_gm" flag that the TBC+ branch already writes).
            // The proxy previously wrote an empty cstring here, which is 1 byte
            // where Kronos wants 4 — the server logged a ByteBufferException on
            // every ticket submit. vmangos's parser reads `reservedForFutureUse`
            // as a cstring-until-null; the first zero byte of uint32(0) satisfies
            // that terminator and the remaining 3 bytes are ignored.
            packet.WriteUInt32(0);
        }
        else
        {
            packet.WriteUInt32(complaint.Header.SelfPlayerMapId);
            packet.WriteVector3(complaint.Header.SelfPlayerPos);
            packet.WriteCString(ticketText);
            packet.WriteUInt32(0); // we dont need the gm to reach back

            packet.WriteUInt32(0); // chat lines count
            packet.WriteUInt32(0); // chat text inflated size
            packet.WriteBytes(Array.Empty<byte>()); // rest of the message are deflated chat lines
        }

        SendPacketToServer(packet);
    }
}
