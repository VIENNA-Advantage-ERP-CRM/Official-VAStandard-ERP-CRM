/*******************************************************
 * Module Name    : VAS_Standard
 * Purpose        : Resolve recipient (Name + EMail) for the
 *                  VA112 PrintViewer share/email panel from
 *                  any record (AD_Table_ID + RecordID) via
 *                  the linked Business Partner's AD_User.
 * Class          : VAS_SentEmailDocModel
 * Chronological Development
 * Created Date   : 08-May-2026
 ******************************************************/
using CoreLibrary.DataBase;
using System;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Backing model for the VAS_SentEmailDoc reusable form. Given
    /// AD_Table_ID + RecordID it walks the record's C_BPartner_ID
    /// to AD_User and returns the contact Name + EMail. If the caller
    /// already passes Name and EMailID the lookup is short-circuited.
    /// </summary>
    public class VAS_SentEmailDocModel
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_SentEmailDocModel).FullName);

        /// <summary>
        /// Resolve recipient details for the VA112 share/email panel.
        /// If both Name and EMailID are supplied, returns them unchanged.
        /// Otherwise looks up C_BPartner_ID from the source table/record
        /// and pulls the first active AD_User with an EMail.
        /// </summary>
        public RecipientResult GetRecipient(Ctx ctx, int AD_Table_ID, int RecordID, string Name, string EMailID)
        {
            RecipientResult result = new RecipientResult();
            result.Name    = Name    == null ? "" : Name.Trim();
            result.EMailID = EMailID == null ? "" : EMailID.Trim();

            // Caller already passed both → nothing to resolve.
            if (!string.IsNullOrEmpty(result.Name) && !string.IsNullOrEmpty(result.EMailID))
            {
                result.Success = true;
                result.Message = "OK";
                return result;
            }

            if (AD_Table_ID <= 0 || RecordID <= 0)
            {
                result.Success = false;
                result.Message = "Invalid AD_Table_ID or RecordID";
                return result;
            }

            try
            {
                // Table name comes straight from system metadata, so it
                // is safe to embed in the lookup SQL below.
                string tableName = MTable.GetTableName(ctx, AD_Table_ID);
                if (string.IsNullOrEmpty(tableName))
                {
                    result.Success = false;
                    result.Message = "Table not found for AD_Table_ID = " + AD_Table_ID;
                    return result;
                }

                // Find the record's primary-key column and confirm it
                // actually carries a C_BPartner_ID FK. Both column names
                // come from AD_Column metadata (parameterized lookup),
                // never from caller input.
                string keyColumn = ResolveKeyColumn(AD_Table_ID);
                if (string.IsNullOrEmpty(keyColumn))
                {
                    result.Success = false;
                    result.Message = "Primary key column not found for table " + tableName;
                    return result;
                }
                if (!HasColumn(AD_Table_ID, "C_BPartner_ID"))
                {
                    result.Success = false;
                    result.Message = "Table " + tableName + " has no C_BPartner_ID column";
                    return result;
                }

                int bpId = LookupBPartnerId(tableName, keyColumn, RecordID);
                if (bpId <= 0)
                {
                    result.Success = false;
                    result.Message = "No Business Partner is linked to this record";
                    return result;
                }

                ResolveBPartnerContact(ctx, bpId, ref result);

                if (string.IsNullOrEmpty(result.EMailID))
                {
                    result.Success = false;
                    result.Message = "No email address is configured for the customer's contact";
                    return result;
                }

                result.Success = true;
                result.Message = "OK";
                return result;
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_SentEmailDocModel.GetRecipient: " + ex.Message);
                result.Success = false;
                result.Message = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Returns the IsKey='Y' column for AD_Table_ID. Uses a
        /// parameterized query so the AD_Table_ID value is safe.
        /// </summary>
        private static string ResolveKeyColumn(int AD_Table_ID)
        {
            const string sql = @"SELECT ColumnName FROM AD_Column
                                 WHERE AD_Table_ID = @tableId
                                   AND IsKey = 'Y'
                                   AND IsActive = 'Y'";
            SqlParameter[] param = new SqlParameter[1];
            param[0] = new SqlParameter("@tableId", AD_Table_ID);
            return Util.GetValueOfString(DB.ExecuteScalar(sql, param, null));
        }

        /// <summary>
        /// Confirms that AD_Table_ID exposes the given column. Used to
        /// verify a C_BPartner_ID FK exists before building the lookup.
        /// </summary>
        private static bool HasColumn(int AD_Table_ID, string columnName)
        {
            const string sql = @"SELECT COUNT(AD_Column_ID) FROM AD_Column
                                 WHERE AD_Table_ID = @tableId
                                   AND ColumnName = @colName
                                   AND IsActive = 'Y'";
            SqlParameter[] param = new SqlParameter[2];
            param[0] = new SqlParameter("@tableId", AD_Table_ID);
            param[1] = new SqlParameter("@colName", columnName);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
        }

        /// <summary>
        /// Read C_BPartner_ID from the source record. tableName and
        /// keyColumn are pulled from AD_Table/AD_Column metadata above
        /// and never from caller input; recordId is parameterized.
        /// </summary>
        private static int LookupBPartnerId(string tableName, string keyColumn, int recordId)
        {
            string sql = "SELECT C_BPartner_ID FROM " + tableName +
                         " WHERE " + keyColumn + " = @recordId";
            SqlParameter[] param = new SqlParameter[1];
            param[0] = new SqlParameter("@recordId", recordId);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null));
        }

        /// <summary>
        /// Pull the BPartner display name and the first active AD_User
        /// with a non-empty EMail. Caller-supplied values already on
        /// the result object are preserved (not overwritten with blank).
        /// </summary>
        private static void ResolveBPartnerContact(Ctx ctx, int C_BPartner_ID, ref RecipientResult result)
        {
            string sql = string.Empty;
            if (string.IsNullOrEmpty(result.Name))
            {
                sql = $@"SELECT Name FROM C_BPartner WHERE C_BPartner_ID = {C_BPartner_ID}";
                result.Name = Util.GetValueOfString(DB.ExecuteScalar(sql));
            }

             sql = @"SELECT EMail, Name FROM AD_User
                                 WHERE C_BPartner_ID = @bpId
                                   AND IsActive = 'Y'
                                   AND EMail IS NOT NULL
                                 ORDER BY AD_User_ID";
            SqlParameter[] param = new SqlParameter[1];
            param[0] = new SqlParameter("@bpId", C_BPartner_ID);

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                if (string.IsNullOrEmpty(result.EMailID))
                {
                    result.EMailID = Util.GetValueOfString(ds.Tables[0].Rows[0]["EMail"]);
                }
                string userName = Util.GetValueOfString(ds.Tables[0].Rows[0]["Name"]);
                if (string.IsNullOrEmpty(result.Name) && !string.IsNullOrEmpty(userName))
                {
                    result.Name = userName;
                }
            }
        }

        // -------------------- DTO --------------------
        public class RecipientResult
        {
            public bool   Success { get; set; }
            public string Message { get; set; }
            public string Name    { get; set; }
            public string EMailID { get; set; }
        }
    }
}
