/********************************************************
 * Module Name    : VAS
 * Purpose        : To use for common methods
 * Class Used     : VAS_CommonMethod
 * Chronological Development
 * VAI050    24-03-2025
 ******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace ModelLibrary.Classes
{

    public class VAS_CommonMethod
    {
        private static VLogger log = VLogger.GetVLogger("VAS_CommonMethod");

        /// <summary>
        /// VAI050-This method used to copy history record data
        /// </summary>
        /// <param name="FromTableID"></param>
        /// <param name="ToTableID"></param>
        /// <param name="ToRecordID"></param>
        /// <param name="FromRecordID"></param>
        /// <param name="trx"></param>
        /// <param name="ctx"></param>
        public static void CopyHistorRecordData(int FromTableID, int ToTableID, int ToRecordID, int FromRecordID, Trx trx, Ctx ctx)
        {
            // Copy Mail Attachments
            if (FromTableID > 0)
            {
                int[] mailAttachmentIDs = MMailAttachment1.GetAllIDs("MailAttachment1",
                    "AD_Table_ID=" + FromTableID + " AND Record_ID=" + FromRecordID, trx);
                if (mailAttachmentIDs.Length > 0)
                {
                    MMailAttachment1 newAttachment = null;
                    MMailAttachment1 oldAttachment = null;
                    for (int i = 0; i < mailAttachmentIDs.Length; i++)
                    {
                        oldAttachment = new MMailAttachment1(ctx, mailAttachmentIDs[i], trx);
                        newAttachment = new MMailAttachment1(ctx, 0, trx);
                        oldAttachment.CopyTo(newAttachment);
                        if (ToRecordID != 0)
                            newAttachment.SetRecord_ID(ToRecordID);
                        if (ToTableID > 0)
                            newAttachment.SetAD_Table_ID(ToTableID);
                        if (!newAttachment.Save())
                            log.SaveError("ERROR:", "Error in Copying Email");
                    }
                }


                // Copy History Records

                int[] historyRecordIDs = MAppointmentsInfo.GetAllIDs("AppointmentsInfo",
                    "AD_Table_ID=" + FromTableID + " AND Record_ID=" + FromRecordID, trx);
                if (historyRecordIDs.Length > 0)
                {
                    MAppointmentsInfo newAppointment = null;
                    MAppointmentsInfo oldAppointment = null;
                    for (int i = 0; i < historyRecordIDs.Length; i++)
                    {
                        oldAppointment = new MAppointmentsInfo(ctx, historyRecordIDs[i], trx);
                        newAppointment = new MAppointmentsInfo(ctx, 0, trx);
                        oldAppointment.CopyTo(newAppointment);
                        newAppointment.SetStartDate(oldAppointment.GetStartDate().Value.ToLocalTime());
                        newAppointment.SetEndDate(oldAppointment.GetEndDate().Value.ToLocalTime());
                        if (ToRecordID != 0)
                            newAppointment.SetRecord_ID(ToRecordID);
                        if (ToTableID > 0)
                            newAppointment.SetAD_Table_ID(ToTableID);
                        if (!newAppointment.Save())
                            log.SaveError("ERROR:", "Error in Copying History Records");
                    }
                }
                // Copy Chat Data

                int[] chatIDs = MChat.GetAllIDs("CM_Chat",
                    "AD_Table_ID=" + FromTableID + " AND Record_ID=" + FromRecordID, trx);
                if (chatIDs.Length > 0)
                {
                    MChatEntry newChatEntry = null;
                    MChatEntry oldChatEntry = null;
                    MChat oldChat = new MChat(ctx, chatIDs[0], trx);
                    MChat newChat = new MChat(ctx, 0, trx);
                    oldChat.CopyTo(newChat);
                    if (ToRecordID != 0)
                        newChat.SetRecord_ID(ToRecordID);
                    if (ToTableID > 0)
                        newChat.SetAD_Table_ID(ToTableID);

                    if (newChat.Save())
                    {
                        string sql = "SELECT CM_ChatEntry_ID FROM CM_ChatEntry WHERE IsActive='Y' AND CM_Chat_ID=" + chatIDs[0];
                        DataSet ds = DB.ExecuteDataset(sql, null, trx);

                        if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                        {
                            for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                            {
                                int chatEntryID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["CM_ChatEntry_ID"]);
                                oldChatEntry = new MChatEntry(ctx, chatEntryID, trx);
                                newChatEntry = new MChatEntry(ctx, 0, trx);
                                oldChatEntry.CopyTo(newChatEntry);
                                newChatEntry.SetCM_Chat_ID(newChat.GetCM_Chat_ID());
                                newChatEntry.SetCharacterData(oldChatEntry.GetCharacterData());
                                if (!newChatEntry.Save())
                                {
                                    log.Severe("VIS_ErrorCopyChatData");
                                }
                            }
                        }
                    }
                    else
                    {
                        log.Severe("VIS_ErrorCopyChatData");
                    }
                }
            }
        }
    }
}
