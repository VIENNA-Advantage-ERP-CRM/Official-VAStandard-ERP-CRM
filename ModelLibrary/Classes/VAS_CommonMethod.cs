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
            if (Env.IsModuleInstalled("VAI01_"))
            {
                string windowName = "VAS_Opportunity";
                if (ToTableID == 291)
                {
                    windowName = "VAS_Prospects";
                }
                CreateAITabPanel(FromTableID, ToTableID, ToRecordID, FromRecordID, windowName, trx, ctx);
            }
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
                        newAttachment.Set_ValueNoCheck("Created", oldAttachment.GetCreated());
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
                        newAppointment.Set_ValueNoCheck("Created", oldAppointment.GetCreated());

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
                    newChat.Set_ValueNoCheck("Created", oldChat.GetCreated());
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
                                newChatEntry.Set_ValueNoCheck("Created", oldChatEntry.GetCreated());

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
                //copy call data
                if (Env.IsModuleInstalled("VA048_"))
                {
                    string query = @"SELECT cd.VA048_CALLDETAILS_ID FROM VA048_CALLDETAILS cd
                                     WHERE cd.VA048_TO IS NOT NULL AND cd.ISACTIVE = 'Y'
                                     AND cd.AD_TABLE_ID = " + FromTableID + "   AND cd.RECORD_ID = " + FromRecordID;
                    DataSet dsChat = DB.ExecuteDataset(query);
                    if (dsChat != null && dsChat.Tables.Count > 0 && dsChat.Tables[0].Rows.Count > 0)
                    {
                        MTable tbl = new MTable(ctx, MTable.Get_Table_ID("VA048_CallDetails"), trx);

                        for (int i = 0; i < dsChat.Tables[0].Rows.Count; i++)
                        {
                            PO FromRecord = tbl.GetPO(ctx, Util.GetValueOfInt(dsChat.Tables[0].Rows[i]["VA048_CallDetails_ID"]), trx);
                            PO ToRecord = tbl.GetPO(ctx, 0, trx);
                            FromRecord.CopyTo(ToRecord);
                            ToRecord.Set_ValueNoCheck("Created", FromRecord.GetCreated());
                            if (ToRecordID != 0)
                                ToRecord.Set_Value("Record_ID", ToRecordID);
                            if (ToTableID > 0)
                                ToRecord.Set_Value("AD_Table_ID", ToTableID);
                            if (!ToRecord.Save())
                            {
                                log.Severe("VIS_ErrorCopyCallData");
                            }
                        }
                    }
                }

            }
        }


        /// <summary>
        /// VAI050-This method used to create entry on AI Assistant window
        /// </summary>
        /// <param name="ToTableID"></param>
        /// <param name="ToRecordID"></param>
        /// <param name="FromRecordID"></param>
        /// <param name="WindowName"></param>
        /// <param name="trx"></param>
        /// <param name="ctx"></param>
        public static void CreateAITabPanel(int FromTableID, int ToTableID, int ToRecordID, int FromRecordID, string WindowName, Trx trx, Ctx ctx)
        {
            string query = @"SELECT a.AD_Client_ID, a.AD_Org_ID, a.VAI01_AIAssistant_ID, b.VAI01_ThreadID 
                             FROM VAI01_AssistantScreen a
                             INNER JOIN VAI01_AssistantThread b ON a.VAI01_AssistantScreen_ID = b.VAI01_AssistantScreen_ID
                            WHERE a.AD_Table_ID=" + FromTableID + " AND b.VAI01_RecordID = '" + FromRecordID + "'";

            DataSet ds = DB.ExecuteDataset(query, null, trx);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                int adClientId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Client_ID"]);
                int adOrgId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                int aiAssistantId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAI01_AIAssistant_ID"]);
                string threadId = Util.GetValueOfString(ds.Tables[0].Rows[0]["VAI01_ThreadID"]);
                query = @"SELECT w.AD_Window_ID, t.AD_Tab_ID 
                          FROM AD_Window w
                          INNER JOIN AD_Tab t ON w.AD_Window_ID = t.AD_Window_ID
                          WHERE w.Name = '" + WindowName + "' AND t.AD_Column_ID IS NULL AND t.AD_Table_ID=" + ToTableID;

                DataSet ds2 = DB.ExecuteDataset(query, null, trx);
                if (ds2 != null && ds2.Tables.Count == 0 || ds2.Tables[0].Rows.Count == 0)
                    return;

                int windowId = Util.GetValueOfInt(ds2.Tables[0].Rows[0]["AD_Window_ID"]);
                int tabId = Util.GetValueOfInt(ds2.Tables[0].Rows[0]["AD_Tab_ID"]);

                // Check if AssistantScreen already exists
                query = @"SELECT VAI01_AssistantScreen_ID 
                          FROM VAI01_AssistantScreen 
                           WHERE VAI01_AIAssistant_ID=" + aiAssistantId + " AND AD_Tab_ID = " + tabId + " AND AD_Window_ID = " + windowId;
                int assistantScreenId = Util.GetValueOfInt(DB.ExecuteScalar(query, null, trx));
                if (assistantScreenId == 0)
                {
                    MTable screenTable = new MTable(ctx, MTable.Get_Table_ID("VAI01_AssistantScreen"), null);
                    PO screen = screenTable.GetPO(ctx, 0, trx);
                    screen.SetAD_Client_ID(adClientId);
                    screen.SetAD_Org_ID(adOrgId);
                    screen.Set_ValueNoCheck("VAI01_AIAssistant_ID", aiAssistantId);
                    screen.Set_Value("AD_Window_ID", windowId);
                    screen.Set_Value("AD_Tab_ID", tabId);
                    screen.Set_Value("AD_Table_ID", ToTableID);
                    if (!screen.Save())
                    {
                        return;
                    }

                    assistantScreenId = screen.Get_ValueAsInt("VAI01_AssistantScreen_ID");
                }

                // Create or link AssistantThread
                MTable threadTable = new MTable(ctx, MTable.Get_Table_ID("VAI01_AssistantThread"), null);
                PO thread = threadTable.GetPO(ctx, 0, trx);
                thread.SetAD_Client_ID(adClientId);
                thread.SetAD_Org_ID(adOrgId);
                thread.Set_ValueNoCheck("VAI01_AssistantScreen_ID", assistantScreenId);
                thread.Set_Value("VAI01_ThreadID", threadId);
                thread.Set_Value("VAI01_RecordID", ToRecordID);
                if (!thread.Save())
                {
                    return;
                }
            }
        }

    }
}
