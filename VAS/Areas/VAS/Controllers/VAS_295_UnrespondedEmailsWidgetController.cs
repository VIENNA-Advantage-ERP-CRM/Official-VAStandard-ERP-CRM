/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Unresponded Emails triage list widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-23
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_295_UnrespondedEmailsWidget
    /// Purpose     : 3x1 compact list - customers whose most recent e-mail is
    ///               inbound and still unanswered, ranked by how long it has been
    ///               waiting (oldest first). Correspondence source follows the
    ///               "Mails" mapping every overview panel's Activity feed already
    ///               uses (see VAS_ActivitySourcesModel): MailAttachment1 filed
    ///               directly on C_BPartner (AD_Table_ID/Record_ID), excluding
    ///               inbound Letters (AttachmentType = 'I'). For each customer,
    ///               only the newest such e-mail decides the state: if it is
    ///               inbound (IsMailSent &lt;&gt; 'Y') the customer is awaiting a
    ///               reply; if the newest one is outbound (IsMailSent = 'Y') the
    ///               customer has already been answered, even if older inbound
    ///               mails exist. MRole (tenant + org + record access) is applied
    ///               to the main physical table C_BPartner only (CTE rule) -
    ///               MailAttachment1 is a supporting join, the same way VAS_135
    ///               never applies MRole to C_ProjectPhase.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_295_UnrespondedEmailsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_295_UnrespondedEmailsWidgetController).FullName);

        // Compact 3x1 widget: 3 rows per page. The client always sends an explicit
        // limit, so this is the fallback default only (matches VAS_135/294's
        // WidgetPageSize pattern).
        private const int WidgetPageSize = 3;
        private const int MaxListPageSize = 25;

        /// <summary>
        /// Tenant-local "today" as a DATE, dialect-specific. Returned as its own
        /// column so days-waiting is computed in C# (DateTime - DateTime); doing
        /// the day subtraction in SQL yields an interval that the data provider
        /// maps to a System.TimeSpan, which Util.GetValueOfInt cannot convert.
        /// Matches VAS_138.TodayDateExpr().
        /// </summary>
        private string TodayDateExpr()
        {
            return DB.IsPostgreSQL()
                ? "CAST(CURRENT_DATE AS DATE)"
                : "TRUNC(SYSDATE)";
        }

        /// <summary>
        /// Paged customer list: customers whose newest e-mail (MailAttachment1,
        /// excluding inbound Letters) is itself inbound and unanswered, ranked by
        /// the mail's age (oldest/longest-waiting first).
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (3 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, offset, limit } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Every customer in scope. MRole on the main physical table alias
                // "bp" only - a plain SELECT with no GROUP BY / ORDER BY, since
                // AddAccessSQL appends its predicate at the end of the statement.
                string customerBodySql = @"
                    SELECT bp.C_BPartner_ID AS C_BPartner_ID,
                           bp.Name AS Customer_Name,
                           bp.SalesRep_ID AS SalesRep_ID
                    FROM C_BPartner bp
                    WHERE bp.IsActive = 'Y'
                      AND bp.AD_Client_ID = @Client_ID
                      AND bp.IsCustomer = 'Y'";
                customerBodySql = MRole.GetDefault(ctx).AddAccessSQL(customerBodySql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // Each customer's own e-mails (not Letters), newest first per
                // customer. AD_Table_ID is resolved by name rather than hardcoded,
                // since the numeric id is not guaranteed the same across
                // installations. "Not 'I'" (rather than "= 'M'") follows
                // VAS_ActivitySourcesModel.LoadMailAttachments: AttachmentType is
                // sometimes left null, and demanding 'M' would hide mails that are
                // really there.
                string rankedMailSql = @"
                    SELECT ma.Record_ID AS C_BPartner_ID,
                           ma.Title AS Mail_Title,
                           ma.IsMailSent AS Is_Mail_Sent,
                           COALESCE(ma.DateMailReceived, ma.Created) AS Mail_Date,
                           ROW_NUMBER() OVER (
                               PARTITION BY ma.Record_ID
                               ORDER BY COALESCE(ma.DateMailReceived, ma.Created) DESC, ma.MailAttachment1_ID DESC
                           ) AS RN
                    FROM MailAttachment1 ma
                    WHERE ma.AD_Table_ID =(SELECT t.AD_Table_ID FROM AD_Table t WHERE t.TableName = N'C_BPartner')
                      AND COALESCE(ma.IsActive, 'Y') = 'Y'
                      AND (ma.AttachmentType IS NULL OR TRIM(ma.AttachmentType) <> 'I')";

                string sql = @"
                    WITH CustomerBody AS (
                        " + customerBodySql + @"
                    ),
                    RankedMail AS (
                        " + rankedMailSql + @"
                    )
                    SELECT cb.C_BPartner_ID AS Customer_Id,
                           cb.Customer_Name AS Customer_Name,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           COALESCE(rm.Mail_Title, N'') AS Mail_Title,
                           rm.Mail_Date AS Mail_Date,
                           " + TodayDateExpr() + @" AS As_Of_Date,
                           COUNT(1) OVER () AS Total_Customers
                    FROM CustomerBody cb
                    INNER JOIN RankedMail rm ON (rm.C_BPartner_ID=cb.C_BPartner_ID AND rm.RN=1 AND COALESCE(rm.Is_Mail_Sent, N'') <> 'Y')
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=cb.SalesRep_ID AND owner.AD_Client_ID = @Client_ID AND owner.IsActive = 'Y')
                    ORDER BY rm.Mail_Date ASC,
                             cb.Customer_Name ASC,
                             cb.C_BPartner_ID ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);

                        DateTime? mailDate = dr["Mail_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Mail_Date"]);
                        DateTime? asOfDate = dr["As_Of_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["As_Of_Date"]);
                        // Days the newest e-mail has been waiting. Computed in C#
                        // to avoid the SQL date subtraction returning an
                        // interval/TimeSpan (matches VAS_138's overdueDays).
                        int unrespDays = 0;
                        if (mailDate.HasValue && asOfDate.HasValue)
                        {
                            unrespDays = (int)(asOfDate.Value.Date - mailDate.Value.Date).TotalDays;
                            if (unrespDays < 0) { unrespDays = 0; }
                        }

                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            ownerName = Util.GetValueOfString(dr["Owner_Name"]),
                            mailTitle = Util.GetValueOfString(dr["Mail_Title"]),
                            unrespDays = unrespDays
                        });
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_295_UnrespondedEmailsWidget.GetRows", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
