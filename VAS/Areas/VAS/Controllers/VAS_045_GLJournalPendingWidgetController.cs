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
    /// Controller for the Pending Action Queue widget — GL_Journal.
    /// Returns GL journals that require user action: Draft, In-Progress (awaiting approval),
    /// Approved (awaiting posting), or Not-Approved (returned for correction).
    /// Linked widgets:
    ///   1. GLJournalPendingWidget — action queue list with urgency markers and zoom-to-record.
    /// Amounts are displayed in the primary accounting schema base currency.
    /// MRole is applied on GL_Journal before ORDER BY.
    /// Age and urgency marker are computed in C# from GL_Journal.Created timestamp.
    /// </summary>
    public class VAS_045_GLJournalPendingWidgetController : Controller
    {
        /// <summary>
        /// Returns pending GL journals (DocStatus IN DR, IP, AP, NA) ordered oldest-first,
        /// capped at 15 display rows. TotalCount reflects all pending records.
        /// </summary>
        public JsonResult GetPendingQueue()
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

            // ── Step 3: Count all pending journals for header badge ───────────
            // Uses same WHERE conditions + MRole so the count respects row-level security.
            string countBase = "SELECT COUNT(1) FROM GL_Journal"
                             + " WHERE GL_Journal.DocStatus IN ('DR','IP','AP','NA')"
                             + " AND GL_Journal.IsActive='Y'"
                             + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID";

            countBase = MRole.GetDefault(ctx).AddAccessSQL(
                countBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] countParams = { new SqlParameter("@AcctSchemaID", acctSchemaId) };
            int totalCount = 0;
            DataSet countDs = DB.ExecuteDataset(countBase, countParams, null);
            if (countDs != null && countDs.Tables[0].Rows.Count > 0)
            {
                totalCount = Util.GetValueOfInt(countDs.Tables[0].Rows[0][0]);
            }

            // ── Step 4: Query pending journals — no GROUP BY needed ───────────
            // Correlated subquery fetches TotalDebit per journal without GROUP BY,
            // allowing MRole to be applied cleanly before ORDER BY.
            // FETCH FIRST 25 ROWS ONLY caps at the database level — works on Oracle 12c+ and PostgreSQL 8.4+.
            string sqlBase = "SELECT GL_Journal.GL_Journal_ID,"
                           + " GL_Journal.DocumentNo,"
                           + " GL_Journal.Description,"
                           + " GL_Journal.DocStatus,"
                           + " GL_Journal.Created,"
                           + " (SELECT COALESCE(SUM(jl.AmtAcctDr),0) FROM GL_JournalLine jl"
                           + " WHERE jl.GL_Journal_ID=GL_Journal.GL_Journal_ID AND jl.IsActive='Y') AS TotalDebit,"
                           + " AD_User.Name AS UserName"
                           + " FROM GL_Journal"
                           + " INNER JOIN AD_User ON (GL_Journal.CreatedBy=AD_User.AD_User_ID)"
                           + " WHERE GL_Journal.DocStatus IN ('DR','IP','AP','NA')"
                           + " AND GL_Journal.IsActive='Y'"
                           + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID";

            // Apply MRole before ORDER BY
            sqlBase = MRole.GetDefault(ctx).AddAccessSQL(
                sqlBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = sqlBase + " ORDER BY GL_Journal.Created ASC FETCH FIRST 25 ROWS ONLY";

            SqlParameter[] mainParams = { new SqlParameter("@AcctSchemaID", acctSchemaId) };
            DataSet ds = DB.ExecuteDataset(sql, mainParams, null);

            // ── Step 5: Build result — SQL already capped at 25 rows ─────────
            var queue    = new List<object>();
            DateTime now = DateTime.Now;

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    DataRow row = ds.Tables[0].Rows[i];

                    int    journalId   = Util.GetValueOfInt(row["GL_Journal_ID"]);
                    string docNo       = Util.GetValueOfString(row["DocumentNo"]);
                    string description = Util.GetValueOfString(row["Description"]);
                    string docStatus   = Util.GetValueOfString(row["DocStatus"]);
                    string userName    = Util.GetValueOfString(row["UserName"]);
                    decimal totalDebit = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalDebit"]), stdPrecision, MidpointRounding.AwayFromZero);

                    // Age — calculated from Created timestamp
                    DateTime created   = row["Created"] != DBNull.Value
                        ? Convert.ToDateTime(row["Created"])
                        : now;
                    double totalHours  = (now - created).TotalHours;

                    string ageStr;
                    if (totalHours < 1)
                        ageStr = "< 1h";
                    else if (totalHours < 24)
                        ageStr = ((int)totalHours) + "h";
                    else if (totalHours < 48)
                        ageStr = "1d";
                    else
                        ageStr = ((int)(totalHours / 24)) + "d";

                    // Urgency marker
                    string markerType;
                    if (totalHours >= 48)
                        markerType = "danger";
                    else if (totalHours >= 24)
                        markerType = "warn";
                    else
                        markerType = "info";

                    // Action label based on DocStatus
                    string actionLabel;
                    switch (docStatus)
                    {
                        case "IP": actionLabel = "Approval"; break;
                        case "AP": actionLabel = "Post";     break;
                        case "NA": actionLabel = "Resubmit"; break;
                        default:   actionLabel = "Draft";    break;
                    }

                    queue.Add(new
                    {
                        GL_Journal_ID = journalId,
                        DocumentNo    = docNo,
                        Description   = description,
                        DocStatus     = docStatus,
                        ActionLabel   = actionLabel,
                        MarkerType    = markerType,
                        AgeStr        = ageStr,
                        IsOverdue     = totalHours >= 48,
                        TotalDebit    = totalDebit,
                        UserName      = userName
                    });
                }
            }

            return Json(JsonConvert.SerializeObject(new
            {
                Queue        = queue,
                TotalCount   = totalCount,
                CurSymbol    = curSymbol,
                ISOCode      = isoCode,
                StdPrecision = stdPrecision
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
