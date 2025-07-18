/********************************************************
 * Module Name    : VAS
 * Purpose        : To use for common methods
 * Class Used     : VAS_CommonMethod
 * Chronological Development
 * VAI050    24-03-2025
 ******************************************************/
using CoreLibrary.Classes;
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
        private static CCache<int, List<string>> s_cache_AmtDim = new CCache<int, List<string>>("AmtDim", 40);

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

        #region Reversal Document creation for Amount Dimension Control

        /// <summary>
        /// This function is used to get the Amount Dimension columns from the specified tables
        /// </summary>
        /// <param name="AD_Table_ID">table ID</param>
        /// <returns>List of Column Names</returns>
        /// <author>VIS_045, 17-July-2025</author>
        public static List<string> GetAmountDimensionColumns(int AD_Table_ID)
        {
            List<string> lstAmtDimCols = new List<string>();
            if (!s_cache_AmtDim.TryGetValue(AD_Table_ID, out lstAmtDimCols) || lstAmtDimCols.Count == 0)
            {
                if (lstAmtDimCols == null)
                {
                    lstAmtDimCols = new List<string>();
                }
                string sql = $@"SELECT ColumnName, AD_Column_ID FROM AD_Column WHERE AD_Table_ID = {AD_Table_ID} AND IsActive = 'Y' AND AD_Reference_ID = 47 ";
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {
                        lstAmtDimCols.Add(Util.GetValueOfString(ds.Tables[0].Rows[i]["ColumnName"]));
                    }
                    s_cache_AmtDim.Add(AD_Table_ID, lstAmtDimCols);
                }
            }
            return lstAmtDimCols;
        }

        /// <summary>
        /// This function is used to create Amount Dimension Copy record during Reversal
        /// </summary>
        /// <param name="from">From Object</param>
        /// <param name="To">To Object</param>
        /// <param name="AD_Table_ID">Table ID</param>
        /// <returns>Amount Dimesnion ID (when created Successfully)</returns>
        /// <author>VIS_045, 18-July-2025</author>
        public static string SetAmountDimControlValue(PO from, PO To, int AD_Table_ID)
        {
            string errormessage = string.Empty;
            try
            {
                // Get Column Name of Amount Dimension reference 
                List<string> lstAmtDimCol = VAS_CommonMethod.GetAmountDimensionColumns(AD_Table_ID);
                if (lstAmtDimCol != null && lstAmtDimCol.Count > 0)
                {
                    int C_DimAmt_ID = 0;
                    foreach (string columnName in lstAmtDimCol)
                    {
                        // Check Amount Dimension reference is linked or not
                        C_DimAmt_ID = from.Get_ValueAsInt(columnName);
                        if (C_DimAmt_ID > 0)
                        {
                            // Create reversal document of Amount Dimension Control Reference
                            C_DimAmt_ID = VAS_CommonMethod.CreateAmountDimensionEntry(from.GetCtx(), C_DimAmt_ID, To.Get_Trx(), out errormessage);
                            if (!string.IsNullOrEmpty(errormessage))
                            {
                                return errormessage;
                            }

                            // Set Value on reversal/copied document
                            To.Set_Value(columnName, C_DimAmt_ID);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Log(Level.SEVERE, "Amount Dimesnion Control value not saved - " + ex.Message.ToString());
            }
            return errormessage;
        }

        /// <summary>
        /// This function is used to create counter entry for Reversal / Copieed Document of Amount Dimesnion Control
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="C_DimAmt_ID">Original Amount Dimension Control Reference ID</param>
        /// <param name="trxName">TrxName</param>
        /// <param name="errorMessage"> OUT Error Message</param>
        /// <returns>Reversal / Copied Amount Dimension Control Reference ID</returns>
        /// <author>VIS_045, 18-July-2025</author>
        public static int CreateAmountDimensionEntry(Ctx ctx, int C_DimAmt_ID, Trx trxName, out string errorMessage)
        {
            int reversalDimAmt_ID = 0;
            errorMessage = string.Empty;

            // Get Detail of Original Amount Dimension Control reference for copying
            DataSet dsData = GetDimAmtRecord(C_DimAmt_ID);

            if (dsData != null && dsData.Tables.Count == 3)
            {
                // CReate object of Amt Dimension Header
                PO _amtDim = MTable.GetPO(ctx, "C_DimAmt", 0, trxName);

                // Get All Columns
                MColumn[] DimAmtCols = MTable.Get(ctx, "C_DimAmt").GetColumns(false);

                foreach (DataColumn colName in dsData.Tables[0].Columns)
                {
                    // Get Column
                    MColumn columnName = DimAmtCols.FirstOrDefault(col => col.GetColumnName().Equals(colName.ColumnName, StringComparison.OrdinalIgnoreCase));

                    // Set Values
                    if (columnName.GetColumnName().ToLower().Equals("amount"))
                    {
                        _amtDim.Set_ValueNoCheck(columnName.GetColumnName(), decimal.Negate(Util.GetValueOfDecimal(dsData.Tables[0].Rows[0][colName.ColumnName])));
                    }
                    else
                    {
                        _amtDim.Set_ValueNoCheck(columnName.GetColumnName(), dsData.Tables[0].Rows[0][colName.ColumnName]);
                    }
                }
                _amtDim.Set_ValueNoCheck("C_DimAmt_ID", 0);
                if (!_amtDim.Save())
                {
                    ValueNamePair vp = VLogger.RetrieveError();
                    if (vp != null)
                    {
                        string val = vp.GetName();
                        if (String.IsNullOrEmpty(val))
                        {
                            val = vp.GetValue();
                        }
                        errorMessage = val;
                    }
                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        errorMessage = Msg.GetMsg(ctx, "VAS_AmtDimNotCreated");
                    }
                    return 0;
                }
                else
                {
                    // copied Header Amount Dimension ID
                    reversalDimAmt_ID = _amtDim.Get_ValueAsInt("C_DimAmt_ID");

                    int reversalDimTypeID = 0;
                    int reversalDimLineID = 0;

                    // Iteration on Amount DImension Type Records
                    for (int dimType = 0; dimType < dsData.Tables[1].Rows.Count; dimType++)
                    {
                        // Create Amount Dimession type
                        reversalDimTypeID = CreateAmtDimtypeRecord(ctx, reversalDimAmt_ID, dsData.Tables[1].Columns, dsData.Tables[1].Rows[dimType], trxName, out errorMessage);
                        if (reversalDimTypeID > 0)
                        {
                            // Get Amount Dimension Lines against Amount Dimension type
                            DataRow[] drAmtDimLine = dsData.Tables[2].Select("C_DimAmt_ID =" + Util.GetValueOfInt(dsData.Tables[0].Rows[0]["C_DimAmt_ID"]) +
                                " AND C_DimAmtAcctType_ID = " + Util.GetValueOfInt(dsData.Tables[1].Rows[dimType]["C_DimAmtAcctType_ID"]));
                            if (drAmtDimLine != null)
                            {
                                for (int line = 0; line < drAmtDimLine.Length; line++)
                                {
                                    // Create Amount Dimession type
                                    reversalDimLineID = CreateAmtDimLineRecord(ctx, reversalDimAmt_ID, reversalDimTypeID, dsData.Tables[2].Columns, drAmtDimLine[line], trxName, out errorMessage);
                                    if (reversalDimLineID < 0)
                                    {
                                        return 0;
                                    }
                                }
                            }
                        }
                        else
                        {
                            return 0;
                        }
                    }
                }
            }
            return reversalDimAmt_ID;
        }

        /// <summary>
        /// This function is used to create counter entry for Reversal / Copieed Document of Amount Dimesnion type
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="DimAmt_ID">Header Amt Dimension ID against whcih Type to be created</param>
        /// <param name="Columns">Data Columns</param>
        /// <param name="dr">DataRow</param>
        /// <param name="trxName">TrxName</param>
        /// <param name="errorMessage">OUT Error Message</param>
        /// <returns>Reversal / Copied Amount Dimension Type Reference ID</returns>
        /// <author>VIS_045, 18-July-2025</author>
        public static int CreateAmtDimtypeRecord(Ctx ctx, int DimAmt_ID, DataColumnCollection Columns, DataRow dr, Trx trxName, out string errorMessage)
        {
            errorMessage = string.Empty;

            // Create Object
            PO _amtDimType = MTable.GetPO(ctx, "C_DimAmtAcctType", 0, trxName);

            // Get All Columns
            MColumn[] DimAmtCols = MTable.Get(ctx, "C_DimAmtAcctType").GetColumns(false);

            foreach (DataColumn colName in Columns)
            {
                // Get Column
                MColumn columnName = DimAmtCols.FirstOrDefault(col => col.GetColumnName().Equals(colName.ColumnName, StringComparison.OrdinalIgnoreCase));

                // Set Values
                if (columnName.GetColumnName().ToLower().Equals("totaldimlineamout"))
                {
                    _amtDimType.Set_ValueNoCheck(columnName.GetColumnName(), decimal.Negate(Util.GetValueOfDecimal(dr[colName.ColumnName])));
                }
                else
                {
                    _amtDimType.Set_ValueNoCheck(columnName.GetColumnName(), dr[colName.ColumnName]);
                }
            }

            // Set header ID
            _amtDimType.Set_ValueNoCheck("C_DimAmt_ID", DimAmt_ID);

            // Clear own ID
            _amtDimType.Set_ValueNoCheck("C_DimAmtAcctType_ID", 0);
            if (!_amtDimType.Save())
            {
                ValueNamePair vp = VLogger.RetrieveError();
                if (vp != null)
                {
                    string val = vp.GetName();
                    if (String.IsNullOrEmpty(val))
                    {
                        val = vp.GetValue();
                    }
                    errorMessage = val;
                }
                if (string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = Msg.GetMsg(ctx, "VAS_AmtDimTypeNotCreated");
                }
                return 0;
            }
            return _amtDimType.Get_ValueAsInt("C_DimAmtAcctType_ID");
        }

        /// <summary>
        /// This function is used to create counter entry for Reversal / Copieed Document of Amount Dimesnion Lines
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="DimAmt_ID">Header Amt Dimension ID against whcih Type to be created</param>
        /// <param name="C_DimAmtAcctType_ID">Amt Dimension Type ID against whcih Line to be created</param>
        /// <param name="Columns">Data Columns</param>
        /// <param name="dr">DataRow</param>
        /// <param name="trxName">TrxName</param>
        /// <param name="errorMessage">OUT Error Message</param>
        /// <returns>Reversal / Copied Amount Dimension Line Reference ID</returns>
        /// <author>VIS_045, 18-July-2025</author>
        public static int CreateAmtDimLineRecord(Ctx ctx, int DimAmt_ID, int C_DimAmtAcctType_ID, DataColumnCollection Columns, DataRow dr, Trx trxName, out string errorMessage)
        {
            errorMessage = string.Empty;

            // Create Object
            PO _amtDimLine = MTable.GetPO(ctx, "C_DimAmtLine", 0, trxName);

            // Get All Columns
            MColumn[] DimAmtCols = MTable.Get(ctx, "C_DimAmtLine").GetColumns(false);

            foreach (DataColumn colName in Columns)
            {
                // Get Column
                MColumn columnName = DimAmtCols.FirstOrDefault(col => col.GetColumnName().Equals(colName.ColumnName, StringComparison.OrdinalIgnoreCase));

                // Set Values
                if (columnName.GetColumnName().ToLower().Equals("amount"))
                {
                    _amtDimLine.Set_ValueNoCheck(columnName.GetColumnName(), decimal.Negate(Util.GetValueOfDecimal(dr[colName.ColumnName])));
                }
                else
                {
                    _amtDimLine.Set_ValueNoCheck(columnName.GetColumnName(), dr[colName.ColumnName]);
                }
            }

            // Set Parent ID's
            _amtDimLine.Set_ValueNoCheck("C_DimAmt_ID", DimAmt_ID);
            _amtDimLine.Set_ValueNoCheck("C_DimAmtAcctType_ID", C_DimAmtAcctType_ID);

            // Clear own reference 
            _amtDimLine.Set_ValueNoCheck("C_DimAmtLine_ID", 0);
            if (!_amtDimLine.Save())
            {
                ValueNamePair vp = VLogger.RetrieveError();
                if (vp != null)
                {
                    string val = vp.GetName();
                    if (String.IsNullOrEmpty(val))
                    {
                        val = vp.GetValue();
                    }
                    errorMessage = val;
                }
                if (string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = Msg.GetMsg(ctx, "VAS_AmtDimTypeNotCreated");
                }
                return 0;
            }
            return _amtDimLine.Get_ValueAsInt("C_DimAmtLine_ID");
        }

        /// <summary>
        /// This function is used to get the Amount Dimesnion Control Record Details
        /// </summary>
        /// <param name="C_DimAmt_ID">Amount Dimension Control ID</param>
        /// <returns>Dataset</returns>
        /// <author>VIS_045, 18-July-2025</author>
        public static DataSet GetDimAmtRecord(int C_DimAmt_ID)
        {
            string sql = string.Empty;
            DataSet ds = new DataSet();

            // 1️ Load C_DimAmt table
            sql = $@"SELECT * FROM C_DimAmt WHERE C_DimAmt_ID = {C_DimAmt_ID}";
            DataTable dtDimAmt = DB.ExecuteDataset(sql).Tables[0].Copy(); ;
            if (dtDimAmt.Rows.Count > 0)
            {
                dtDimAmt.TableName = "C_DimAmt";
                ds.Tables.Add(dtDimAmt);
            }

            // 2️ Load C_DimAmtAcctType table
            sql = $@"SELECT * FROM C_DimAmtAcctType WHERE C_DimAmt_ID = {C_DimAmt_ID}";
            DataTable dtAcctType = DB.ExecuteDataset(sql).Tables[0].Copy(); ;
            if (dtAcctType.Rows.Count > 0)
            {
                dtAcctType.TableName = "C_DimAmtAcctType";
                ds.Tables.Add(dtAcctType);
            }

            // 3️ Load C_DimAmtLine table
            sql = $@"SELECT * FROM C_DimAmtLine WHERE C_DimAmt_ID = {C_DimAmt_ID}";
            DataTable dtAmtLine = DB.ExecuteDataset(sql).Tables[0].Copy(); ;
            if (dtAmtLine.Rows.Count > 0)
            {
                dtAmtLine.TableName = "C_DimAmtLine";
                ds.Tables.Add(dtAmtLine);
            }

            return ds;
        }

        #endregion

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
