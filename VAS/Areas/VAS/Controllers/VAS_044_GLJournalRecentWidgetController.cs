using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Controllers
{
    /// <summary>
    /// Controller for GL Journal Recent Entries widget.
    /// Linked widgets:
    ///   1. GLJournalRecentWidget — last 6 GL Journal documents with document
    ///      number, account date, description, status name, total debit / total credit.
    /// Amounts are displayed in the primary accounting schema base currency.
    /// MRole is applied on GL_Journal (the primary access-controlled table).
    /// GROUP BY / ORDER BY are appended after MRole so the access filters
    /// remain inside the WHERE clause and do not corrupt the query structure.
    /// </summary>
    public class VAS_044_GLJournalRecentWidgetController : Controller
    {
        private class SchemaInfo
        {
            public int AcctSchemaId { get; set; }
            public string AcctSchemaName { get; set; }
            public string CurSymbol { get; set; }
            public string ISOCode { get; set; }
            public int StdPrecision { get; set; }
        }

        private SchemaInfo GetPrimarySchema(Ctx ctx)
        {
            SchemaInfo info = new SchemaInfo
            {
                AcctSchemaId = 0,
                AcctSchemaName = "",
                CurSymbol = "",
                ISOCode = "",
                StdPrecision = 2
            };

            string schemaSql = "SELECT C_AcctSchema.C_AcctSchema_ID, C_AcctSchema.Name,"
                             + " C_Currency.CurSymbol, C_Currency.ISO_Code, C_Currency.StdPrecision"
                             + " FROM C_AcctSchema"
                             + " INNER JOIN C_Currency ON (C_AcctSchema.C_Currency_ID=C_Currency.C_Currency_ID)"
                             + " WHERE C_AcctSchema.IsActive='Y'"
                             + " AND C_AcctSchema.AD_Client_ID=@ClientID";

            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", ctx.GetAD_Client_ID()) };
            DataSet schemaDs = DB.ExecuteDataset(schemaSql, schemaParams, null);
            if (schemaDs != null && schemaDs.Tables[0].Rows.Count > 0)
            {
                info.AcctSchemaId = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["C_AcctSchema_ID"]);
                info.AcctSchemaName = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["Name"]);
                info.CurSymbol = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["CurSymbol"]);
                info.ISOCode = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["ISO_Code"]);
                info.StdPrecision = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            return info;
        }

        /// <summary>
        /// Returns the 6 most recent GL Journal documents for the current client
        /// together with the window ID used to zoom to a specific journal.
        /// </summary>
        public JsonResult GetRecentEntries()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;

            // ── Step 1: Resolve primary accounting schema ─────────────────────
            int    acctSchemaId = 0;
            string curSymbol    = "";
            string isoCode      = "";
            int    stdPrecision = 2;

            string schemaSql = "SELECT C_AcctSchema.C_AcctSchema_ID,"
                             + " C_Currency.CurSymbol, C_Currency.ISO_Code, C_Currency.StdPrecision"
                             + " FROM C_AcctSchema"
                             + " INNER JOIN C_Currency ON (C_AcctSchema.C_Currency_ID=C_Currency.C_Currency_ID)"
                             + " WHERE C_AcctSchema.IsActive='Y'"
                             + " AND C_AcctSchema.AD_Client_ID=@ClientID";

            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", ctx.GetAD_Client_ID()) };
            DataSet schemaDs = DB.ExecuteDataset(schemaSql, schemaParams, null);
            if (schemaDs != null && schemaDs.Tables[0].Rows.Count > 0)
            {
                acctSchemaId = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["C_AcctSchema_ID"]);
                curSymbol    = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["CurSymbol"]);
                isoCode      = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["ISO_Code"]);
                stdPrecision = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            // ── Step 2: Build base query and apply MRole before GROUP BY ──────
            // MRole must be applied while the SQL ends with the WHERE clause.
            // GROUP BY and ORDER BY are appended afterwards so the access
            // predicates stay inside WHERE and do not break query syntax.
            string sqlBase = "SELECT GL_Journal.GL_Journal_ID,"
                           + " GL_Journal.DocumentNo,"
                           + " GL_Journal.DateAcct,"
                           + " GL_Journal.Description,"
                           + " GL_Journal.DocStatus,"
                           + " AD_Ref_List.Name AS DocStatusName,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctDr),0) AS TotalDebit,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctCr),0) AS TotalCredit"
                           + " FROM GL_Journal"
                           + " INNER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID=GL_JournalLine.GL_Journal_ID"
                           + " AND GL_JournalLine.IsActive='Y')"
                           + " LEFT OUTER JOIN AD_Ref_List ON (AD_Ref_List.AD_Reference_ID=131"
                           + " AND AD_Ref_List.Value=GL_Journal.DocStatus"
                           + " AND AD_Ref_List.IsActive='Y')"
                           + " WHERE GL_Journal.IsActive='Y'"
                           + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID";

            // Apply MRole now — before GROUP BY / ORDER BY
            sqlBase = MRole.GetDefault(ctx).AddAccessSQL(
                sqlBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Append GROUP BY, ORDER BY, then cap at 25 rows at the database level.
            // FETCH FIRST N ROWS ONLY is ANSI SQL — works on Oracle 12c+ and PostgreSQL 8.4+.
            string sql = sqlBase
                       + " GROUP BY GL_Journal.GL_Journal_ID, GL_Journal.DocumentNo,"
                       + " GL_Journal.DateAcct, GL_Journal.Description,"
                       + " GL_Journal.DocStatus, AD_Ref_List.Name"
                       + " ORDER BY GL_Journal.DateAcct DESC, GL_Journal.GL_Journal_ID DESC"
                       + " FETCH FIRST 25 ROWS ONLY";

            SqlParameter[] mainParams = { new SqlParameter("@AcctSchemaID", acctSchemaId) };
            DataSet ds = DB.ExecuteDataset(sql, mainParams, null);

            // ── Step 3: Resolve GL_Journal window ID for zoom ─────────────────
            // Query system-level (AD_Client_ID=0) primary tab to get the
            // standard GL Journal window — avoids client-specific overrides.
            //int windowId = 0;
            //string winSql = "SELECT MIN(AD_Tab.AD_Window_ID) FROM AD_Tab"
            //              + " WHERE AD_Tab.TableName='GL_Journal'"
            //              + " AND AD_Tab.TabLevel=0"
            //              + " AND AD_Tab.IsActive='Y'"
            //              + " AND AD_Tab.AD_Client_ID=0";

            //DataSet winDs = DB.ExecuteDataset(winSql, null, null);
            //if (winDs != null && winDs.Tables[0].Rows.Count > 0)
            //{
            //    windowId = Util.GetValueOfInt(winDs.Tables[0].Rows[0][0]);
            //}

            // ── Step 4: Build result — SQL already capped at 25 rows ─────────
            var    entries          = new List<object>();
            object unbalancedEntry  = null;

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                int count = ds.Tables[0].Rows.Count;
                for (int i = 0; i < count; i++)
                {
                    DataRow row = ds.Tables[0].Rows[i];

                    int    journalId   = Util.GetValueOfInt(row["GL_Journal_ID"]);
                    string docNo       = Util.GetValueOfString(row["DocumentNo"]);
                    string description = Util.GetValueOfString(row["Description"]);
                    string docStatus   = Util.GetValueOfString(row["DocStatus"]);
                    string statusName  = Util.GetValueOfString(row["DocStatusName"]);
                    if (string.IsNullOrEmpty(statusName)) { statusName = docStatus; }

                    string dateAcctStr = "";
                    if (row["DateAcct"] != DBNull.Value)
                    {
                        dateAcctStr = Convert.ToDateTime(row["DateAcct"]).ToString("MMM dd");
                    }

                    decimal totalDebit  = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalDebit"]),  stdPrecision, MidpointRounding.AwayFromZero);
                    decimal totalCredit = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalCredit"]), stdPrecision, MidpointRounding.AwayFromZero);

                    bool    isUnbalanced = (Math.Abs(totalDebit - totalCredit) > 0m);

                    entries.Add(new
                    {
                        GL_Journal_ID = journalId,
                        DocumentNo    = docNo,
                        DateAcct      = dateAcctStr,
                        Description   = description,
                        DocStatus     = docStatus,
                        StatusName    = statusName,
                        TotalDebit    = totalDebit,
                        TotalCredit   = totalCredit,
                        IsUnbalanced  = isUnbalanced
                    });

                    // Keep only the first unbalanced entry for the alert strip
                    if (isUnbalanced && unbalancedEntry == null)
                    {
                        decimal diff = Decimal.Round(
                            Math.Abs(totalDebit - totalCredit), stdPrecision, MidpointRounding.AwayFromZero);
                        unbalancedEntry = new
                        {
                            GL_Journal_ID = journalId,
                            DocumentNo    = docNo,
                            Difference    = diff
                        };
                    }
                }
            }

            return Json(JsonConvert.SerializeObject(new
            {
                Entries         = entries,
                UnbalancedEntry = unbalancedEntry, 
                CurSymbol       = curSymbol,
                ISOCode         = isoCode,
                StdPrecision    = stdPrecision
            }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns a single recent GL Journal with accounting lines for the row-click popup.
        /// </summary>
        public JsonResult GetJournalEntryDetail(int journalId)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            SchemaInfo schema = GetPrimarySchema(ctx);

            string headerBase = "SELECT GL_Journal.GL_Journal_ID,"
                              + " GL_Journal.DocumentNo,"
                              + " GL_Journal.DateAcct,"
                              + " GL_Journal.Description,"
                              + " GL_Journal.DocStatus,"
                              + " GL_Journal.Posted,"
                              + " GL_Journal.Created,"
                              + " AD_Ref_List.Name AS DocStatusName,"
                              + " AD_User.Name AS CreatedByName,"
                              + " COALESCE(SUM(GL_JournalLine.AmtAcctDr),0) AS TotalDebit,"
                              + " COALESCE(SUM(GL_JournalLine.AmtAcctCr),0) AS TotalCredit"
                              + " FROM GL_Journal"
                              + " LEFT OUTER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID=GL_JournalLine.GL_Journal_ID"
                              + " AND GL_JournalLine.IsActive='Y')"
                              + " LEFT OUTER JOIN AD_Ref_List ON (AD_Ref_List.AD_Reference_ID=131"
                              + " AND AD_Ref_List.Value=GL_Journal.DocStatus"
                              + " AND AD_Ref_List.IsActive='Y')"
                              + " LEFT OUTER JOIN AD_User ON (GL_Journal.CreatedBy=AD_User.AD_User_ID)"
                              + " WHERE GL_Journal.GL_Journal_ID=@JournalID"
                              + " AND GL_Journal.IsActive='Y'"
                              + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID";

            headerBase = MRole.GetDefault(ctx).AddAccessSQL(
                headerBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string headerSql = headerBase
                             + " GROUP BY GL_Journal.GL_Journal_ID, GL_Journal.DocumentNo,"
                             + " GL_Journal.DateAcct, GL_Journal.Description,"
                             + " GL_Journal.DocStatus, GL_Journal.Posted, GL_Journal.Created,"
                             + " AD_Ref_List.Name, AD_User.Name";

            SqlParameter[] headerParams =
            {
                new SqlParameter("@JournalID", journalId),
                new SqlParameter("@AcctSchemaID", schema.AcctSchemaId)
            };

            DataSet headerDs = DB.ExecuteDataset(headerSql, headerParams, null);
            if (headerDs == null || headerDs.Tables[0].Rows.Count == 0)
            {
                return Json(JsonConvert.SerializeObject(new { error = true }), JsonRequestBehavior.AllowGet);
            }

            DataRow headerRow = headerDs.Tables[0].Rows[0];
            string docStatus = Util.GetValueOfString(headerRow["DocStatus"]);
            string statusName = Util.GetValueOfString(headerRow["DocStatusName"]);
            if (string.IsNullOrEmpty(statusName)) { statusName = docStatus; }

            string dateAcctStr = "";
            if (headerRow["DateAcct"] != DBNull.Value)
            {
                dateAcctStr = Convert.ToDateTime(headerRow["DateAcct"]).ToString("dd MMM yyyy");
            }

            string createdStr = "";
            if (headerRow["Created"] != DBNull.Value)
            {
                createdStr = Convert.ToDateTime(headerRow["Created"]).ToString("dd MMM yyyy");
            }

            decimal totalDebit = Decimal.Round(
                Util.GetValueOfDecimal(headerRow["TotalDebit"]), schema.StdPrecision, MidpointRounding.AwayFromZero);
            decimal totalCredit = Decimal.Round(
                Util.GetValueOfDecimal(headerRow["TotalCredit"]), schema.StdPrecision, MidpointRounding.AwayFromZero);

            string linesSql = "SELECT GL_JournalLine.GL_JournalLine_ID,"
                            + " C_ElementValue.Value AS AccountCode,"
                            + " C_ElementValue.Name AS AccountName,"
                            + " GL_JournalLine.AmtAcctDr,"
                            + " GL_JournalLine.AmtAcctCr"
                            + " FROM GL_JournalLine"
                            + " INNER JOIN C_ElementValue ON (GL_JournalLine.Account_ID=C_ElementValue.C_ElementValue_ID)"
                            + " WHERE GL_JournalLine.GL_Journal_ID=@JournalID"
                            + " AND GL_JournalLine.IsActive='Y'"
                            + " ORDER BY GL_JournalLine.Line, GL_JournalLine.GL_JournalLine_ID";

            SqlParameter[] lineParams = { new SqlParameter("@JournalID", journalId) };
            DataSet lineDs = DB.ExecuteDataset(linesSql, lineParams, null);
            List<object> lines = new List<object>();

            if (lineDs != null && lineDs.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in lineDs.Tables[0].Rows)
                {
                    lines.Add(new
                    {
                        AccountCode = Util.GetValueOfString(row["AccountCode"]),
                        AccountName = Util.GetValueOfString(row["AccountName"]),
                        Debit = Decimal.Round(
                            Util.GetValueOfDecimal(row["AmtAcctDr"]), schema.StdPrecision, MidpointRounding.AwayFromZero),
                        Credit = Decimal.Round(
                            Util.GetValueOfDecimal(row["AmtAcctCr"]), schema.StdPrecision, MidpointRounding.AwayFromZero),
                        CostCenter = "-",
                        BPartner = "-",
                        Product = "-",
                        Project = "-"
                    });
                }
            }

            return Json(JsonConvert.SerializeObject(new
            {
                Journal = new
                {
                    GL_Journal_ID = Util.GetValueOfInt(headerRow["GL_Journal_ID"]),
                    DocumentNo = Util.GetValueOfString(headerRow["DocumentNo"]),
                    DateAcct = dateAcctStr,
                    Description = Util.GetValueOfString(headerRow["Description"]),
                    DocStatus = docStatus,
                    Posted = Util.GetValueOfString(headerRow["Posted"]),
                    StatusName = statusName,
                    TotalDebit = totalDebit,
                    TotalCredit = totalCredit,
                    AccountingBook = (string.IsNullOrEmpty(schema.AcctSchemaName) ? "Primary" : schema.AcctSchemaName),
                    CreatedByName = Util.GetValueOfString(headerRow["CreatedByName"]),
                    CreatedDate = createdStr
                },
                Lines = lines,
                CurSymbol = schema.CurSymbol,
                ISOCode = schema.ISOCode,
                StdPrecision = schema.StdPrecision
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
