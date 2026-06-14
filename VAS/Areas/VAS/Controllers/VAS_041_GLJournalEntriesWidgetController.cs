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
    public class VAS_041_GLJournalEntriesWidgetController : Controller
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
        /// Returns the count of actual GL Journal entries (PostingType='A')
        /// posted in the current calendar month, together with month labels.
        /// </summary>
        public JsonResult GetMonthlyEntryCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            SchemaInfo schema = GetPrimarySchema(ctx);
            DateTime today = DateTime.Now.Date;
            int currentMonth = today.Month;
            int currentYear = today.Year;
            int entryCount = 0;

            string sql = "SELECT COUNT(GL_Journal.GL_Journal_ID) AS EntryCount FROM GL_Journal"
                       + " WHERE GL_Journal.PostingType = 'A'"
                       + " AND GL_Journal.IsActive = 'Y'"
                       + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID"
                       + " AND EXTRACT(MONTH FROM GL_Journal.DateAcct) = @CurrentMonth"
                       + " AND EXTRACT(YEAR FROM GL_Journal.DateAcct) = @CurrentYear";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] sqlParams =
            {
                new SqlParameter("@AcctSchemaID", schema.AcctSchemaId),
                new SqlParameter("@CurrentMonth", currentMonth),
                new SqlParameter("@CurrentYear",  currentYear)
            };
            DataSet ds = DB.ExecuteDataset(sql, sqlParams, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                entryCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["EntryCount"]);
            }

            var result = new
            {
                EntryCount = entryCount,
                MonthName  = today.ToString("MMMM"),
                MonthAbbr  = today.ToString("MMM"),
                Year       = today.Year
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns GL Journal entries for the current month for the drill-down popup.
        /// </summary>
        public JsonResult GetMonthlyEntries()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            SchemaInfo schema = GetPrimarySchema(ctx);
            DateTime today = DateTime.Now.Date;
            int currentMonth = today.Month;
            int currentYear = today.Year;

            string sqlBase = "SELECT GL_Journal.GL_Journal_ID,"
                           + " GL_Journal.DocumentNo,"
                           + " GL_Journal.DateAcct,"
                           + " GL_Journal.Description,"
                           + " GL_Journal.DocStatus,"
                           + " AD_Ref_List.Name AS DocStatusName,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctDr),0) AS TotalDebit,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctCr),0) AS TotalCredit"
                           + " FROM GL_Journal"
                           + " LEFT OUTER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID=GL_JournalLine.GL_Journal_ID"
                           + " AND GL_JournalLine.IsActive='Y')"
                           + " LEFT OUTER JOIN AD_Ref_List ON (AD_Ref_List.AD_Reference_ID=131"
                           + " AND AD_Ref_List.Value=GL_Journal.DocStatus"
                           + " AND AD_Ref_List.IsActive='Y')"
                           + " WHERE GL_Journal.PostingType='A'"
                           + " AND GL_Journal.IsActive='Y'"
                           + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID"
                           + " AND EXTRACT(MONTH FROM GL_Journal.DateAcct)=@CurrentMonth"
                           + " AND EXTRACT(YEAR FROM GL_Journal.DateAcct)=@CurrentYear";

            sqlBase = MRole.GetDefault(ctx).AddAccessSQL(
                sqlBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = sqlBase
                       + " GROUP BY GL_Journal.GL_Journal_ID, GL_Journal.DocumentNo,"
                       + " GL_Journal.DateAcct, GL_Journal.Description,"
                       + " GL_Journal.DocStatus, AD_Ref_List.Name"
                       + " ORDER BY GL_Journal.DateAcct DESC, GL_Journal.GL_Journal_ID DESC"
                       + " FETCH FIRST 100 ROWS ONLY";

            SqlParameter[] mainParams =
            {
                new SqlParameter("@AcctSchemaID", schema.AcctSchemaId),
                new SqlParameter("@CurrentMonth", currentMonth),
                new SqlParameter("@CurrentYear", currentYear)
            };

            DataSet ds = DB.ExecuteDataset(sql, mainParams, null);
            List<object> entries = new List<object>();
            decimal totalDebit = 0m;
            decimal totalCredit = 0m;

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    decimal debit = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalDebit"]), schema.StdPrecision, MidpointRounding.AwayFromZero);
                    decimal credit = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalCredit"]), schema.StdPrecision, MidpointRounding.AwayFromZero);

                    totalDebit += debit;
                    totalCredit += credit;

                    string statusName = Util.GetValueOfString(row["DocStatusName"]);
                    string docStatus = Util.GetValueOfString(row["DocStatus"]);
                    if (string.IsNullOrEmpty(statusName)) { statusName = docStatus; }

                    string dateAcctStr = "";
                    if (row["DateAcct"] != DBNull.Value)
                    {
                        dateAcctStr = Convert.ToDateTime(row["DateAcct"]).ToString("dd MMM yyyy");
                    }

                    entries.Add(new
                    {
                        GL_Journal_ID = Util.GetValueOfInt(row["GL_Journal_ID"]),
                        DocumentNo = Util.GetValueOfString(row["DocumentNo"]),
                        DateAcct = dateAcctStr,
                        Description = Util.GetValueOfString(row["Description"]),
                        DocStatus = docStatus,
                        StatusName = statusName,
                        TotalDebit = debit,
                        TotalCredit = credit
                    });
                }
            }

            return Json(JsonConvert.SerializeObject(new
            {
                Entries = entries,
                TotalCount = entries.Count,
                TotalDebit = Decimal.Round(totalDebit, schema.StdPrecision, MidpointRounding.AwayFromZero),
                TotalCredit = Decimal.Round(totalCredit, schema.StdPrecision, MidpointRounding.AwayFromZero),
                MonthName = today.ToString("MMMM"),
                MonthAbbr = today.ToString("MMM"),
                Year = today.Year,
                CurSymbol = schema.CurSymbol,
                ISOCode = schema.ISOCode,
                StdPrecision = schema.StdPrecision
            }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns a single GL Journal with its accounting lines for the second drill-down popup.
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
                             + " GL_Journal.DocStatus, GL_Journal.Created,"
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

        /// <summary>
        /// Returns the count of GL Journal documents that are not yet posted
        /// (DocStatus IN Draft/Complete/Closed AND Posted = 'N').
        /// </summary>
        public JsonResult GetUnpostedCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx          = Session["ctx"] as Ctx;
            int unpostedCount = 0;

            string sql = "SELECT COUNT(GL_Journal_ID) AS UnpostedCount FROM GL_Journal"
                       + " WHERE Posted = 'N'"
                       + " AND DocStatus NOT IN ('VO')"
                       + " AND IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                unpostedCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["UnpostedCount"]);
            }

            return Json(JsonConvert.SerializeObject(new { UnpostedCount = unpostedCount }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns unposted GL Journal documents for the VAS_036 drill-down popup.
        /// </summary>
        public JsonResult GetUnpostedEntries()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            SchemaInfo schema = GetPrimarySchema(ctx);

            string sqlBase = "SELECT GL_Journal.GL_Journal_ID,"
                           + " GL_Journal.DocumentNo,"
                           + " GL_Journal.DateAcct,"
                           + " GL_Journal.Description,"
                           + " GL_Journal.DocStatus,"
                           + " AD_Ref_List.Name AS DocStatusName,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctDr),0) AS TotalDebit,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctCr),0) AS TotalCredit"
                           + " FROM GL_Journal"
                           + " LEFT OUTER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID=GL_JournalLine.GL_Journal_ID"
                           + " AND GL_JournalLine.IsActive='Y')"
                           + " LEFT OUTER JOIN AD_Ref_List ON (AD_Ref_List.AD_Reference_ID=131"
                           + " AND AD_Ref_List.Value=GL_Journal.DocStatus"
                           + " AND AD_Ref_List.IsActive='Y')"
                           + " WHERE GL_Journal.Posted='N'"
                           + " AND GL_Journal.DocStatus NOT IN ('VO')"
                           + " AND GL_Journal.IsActive='Y'"
                           + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID";

            sqlBase = MRole.GetDefault(ctx).AddAccessSQL(
                sqlBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = sqlBase
                       + " GROUP BY GL_Journal.GL_Journal_ID, GL_Journal.DocumentNo,"
                       + " GL_Journal.DateAcct, GL_Journal.Description,"
                       + " GL_Journal.DocStatus, AD_Ref_List.Name"
                       + " ORDER BY GL_Journal.DateAcct DESC, GL_Journal.GL_Journal_ID DESC"
                       + " FETCH FIRST 100 ROWS ONLY";

            SqlParameter[] sqlParams = { new SqlParameter("@AcctSchemaID", schema.AcctSchemaId) };
            DataSet ds = DB.ExecuteDataset(sql, sqlParams, null);
            List<object> entries = new List<object>();
            decimal totalDebit = 0m;
            decimal totalCredit = 0m;

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    decimal debit = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalDebit"]), schema.StdPrecision, MidpointRounding.AwayFromZero);
                    decimal credit = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalCredit"]), schema.StdPrecision, MidpointRounding.AwayFromZero);

                    totalDebit += debit;
                    totalCredit += credit;

                    string statusName = Util.GetValueOfString(row["DocStatusName"]);
                    string docStatus = Util.GetValueOfString(row["DocStatus"]);
                    if (string.IsNullOrEmpty(statusName)) { statusName = docStatus; }

                    string dateAcctStr = "";
                    if (row["DateAcct"] != DBNull.Value)
                    {
                        dateAcctStr = Convert.ToDateTime(row["DateAcct"]).ToString("dd MMM yyyy");
                    }

                    entries.Add(new
                    {
                        GL_Journal_ID = Util.GetValueOfInt(row["GL_Journal_ID"]),
                        DocumentNo = Util.GetValueOfString(row["DocumentNo"]),
                        DateAcct = dateAcctStr,
                        Description = Util.GetValueOfString(row["Description"]),
                        DocStatus = docStatus,
                        StatusName = statusName,
                        TotalDebit = debit,
                        TotalCredit = credit
                    });
                }
            }

            return Json(JsonConvert.SerializeObject(new
            {
                Entries = entries,
                TotalCount = entries.Count,
                TotalDebit = Decimal.Round(totalDebit, schema.StdPrecision, MidpointRounding.AwayFromZero),
                TotalCredit = Decimal.Round(totalCredit, schema.StdPrecision, MidpointRounding.AwayFromZero),
                CurSymbol = schema.CurSymbol,
                ISOCode = schema.ISOCode,
                StdPrecision = schema.StdPrecision
            }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the percentage of posted documents across GL_Journal and GL_JournalBatch.
        /// PostedCount / TotalCount * 100, rounded to the nearest integer.
        /// </summary>
        public JsonResult GetPostedPercentage()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = "SELECT SUM(CASE WHEN GL_Journal.Posted = 'Y' THEN 1 ELSE 0 END) AS PostedCount,"
                       + " COUNT(1) AS TotalCount"
                       + " FROM GL_Journal"
                       + " WHERE GL_Journal.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int postedCount = 0;
            int totalCount  = 0;

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                postedCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["PostedCount"]);
                totalCount  = Util.GetValueOfInt(ds.Tables[0].Rows[0]["TotalCount"]);
            }

            int percentage = totalCount > 0
                ? (int)Math.Round((double)postedCount / totalCount * 100)
                : 0;

            var result = new
            {
                PostedCount = postedCount,
                TotalCount  = totalCount,
                Percentage  = percentage
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
