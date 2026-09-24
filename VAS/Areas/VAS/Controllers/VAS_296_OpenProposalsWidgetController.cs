/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Open Proposals widget endpoints
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
    /// Module Name : VAS_296_OpenProposalsWidget
    /// Purpose     : 3x2 list - customers with open sales proposals, ranked by
    ///               proposal value desc. A proposal is a C_Order sales document
    ///               not yet converted into a completed order:
    ///               IsSOTrx='Y' AND (IsSalesQuotation='Y' OR (C_DocType.DocBaseType
    ///               ='SOO' AND DocSubTypeSO IN ('OB','ON','QT'))) AND DocStatus IN
    ///               ('DR','IN','CO') - exactly the Sales Proposal definition given
    ///               for this deployment. Pairs with the Sales Proposal module; this
    ///               is its per-customer rollup. Value is GrandTotal converted to
    ///               the tenant accounting currency via CurrencyConvert, so
    ///               multi-currency proposals share one correct base figure (same
    ///               pattern as VAS_138's overdue receivables). MRole (tenant + org
    ///               + record access) is applied to the main physical table
    ///               C_Order only.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_296_OpenProposalsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_296_OpenProposalsWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        /// <summary>Single-row tenant accounting currency (symbol, ISO, precision).</summary>
        private const string SchemaCurrencySql = @"
            SELECT ci.AD_Client_ID AS AD_Client_ID,
                   cs.C_Currency_ID AS Acct_Currency_ID,
                   cur.StdPrecision AS Std_Precision,
                   cur.ISO_Code AS ISO_Code,
                   CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
            FROM AD_ClientInfo ci
            INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
            INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
            WHERE ci.IsActive = 'Y'
              AND ci.AD_Client_ID = @Client_ID";

        /// <summary>Proposal order total converted to the tenant accounting currency.</summary>
        private const string BaseAmtExpr =
            "CurrencyConvert(o.GrandTotal, o.C_Currency_ID, sc.Acct_Currency_ID, o.DateOrdered, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID)";

        /// <summary>
        /// The shared FROM/WHERE for the open-proposal population. Every row is an
        /// active customer sales document that is a Sales Proposal by this
        /// deployment's definition and still open (not yet closed/voided). Alias
        /// "o" is the MRole physical table.
        /// </summary>
        private string ProposalFromWhere()
        {
            return @"
                FROM C_Order o
                INNER JOIN C_DocType dt ON (dt.C_DocType_ID=o.C_DocType_ID AND dt.IsActive = 'Y')
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.C_BPartner_ID AND bp.AD_Client_ID=o.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=o.AD_Client_ID)
                WHERE o.IsActive = 'Y'
                  AND o.AD_Client_ID = @Client_ID
                  AND COALESCE(o.IsSOTrx, 'N') = 'Y'
                  AND (COALESCE(o.IsSalesQuotation, 'N') = 'Y'
                       OR (dt.DocBaseType = 'SOO' AND COALESCE(dt.DocSubTypeSO, ' ') IN ('OB', 'ON', 'QT')))
                  AND o.DocStatus IN ('DR', 'IN', 'CO')";
        }

        /// <summary>
        /// One row per customer with at least one open proposal - proposal count,
        /// summed value (tenant accounting currency) - ranked by value desc, then
        /// name, then id (stable paging).
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; 7 for the widget, up to 25 for the full list.</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            try
            {
                // Order-level projection over the shared proposal population; rolled
                // up to one row per customer below. MRole on "o". No ORDER BY here -
                // AddAccessSQL splices its predicate ahead of the first ORDER BY it
                // finds, so the sort must live in the outer query.
                string proposalRowsSql = @"
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           bp.SalesRep_ID AS SalesRep_ID,
                           o.C_Order_ID AS Order_Id,
                           ROUND(" + BaseAmtExpr + @", sc.Std_Precision) AS Order_Amt
                    " + ProposalFromWhere();
                proposalRowsSql = MRole.GetDefault(ctx).AddAccessSQL(proposalRowsSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    proposal_rows AS (
                        " + proposalRowsSql + @"
                    ),
                    by_customer AS (
                        SELECT pr.Customer_Id AS Customer_Id,
                               pr.Customer_Name AS Customer_Name,
                               pr.SalesRep_ID AS SalesRep_ID,
                               COUNT(DISTINCT pr.Order_Id) AS Proposal_Count,
                               SUM(pr.Order_Amt) AS Proposal_Value
                        FROM proposal_rows pr
                        GROUP BY pr.Customer_Id, pr.Customer_Name, pr.SalesRep_ID
                    )
                    SELECT c.Customer_Id,
                           c.Customer_Name,
                           c.Proposal_Count,
                           c.Proposal_Value,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision,
                           COUNT(1) OVER () AS Total_Rows,
                           SUM(c.Proposal_Value) OVER () AS Total_Value
                    FROM by_customer c
                    CROSS JOIN schema_currency sc
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=c.SalesRep_ID AND owner.AD_Client_ID = @Client_ID AND owner.IsActive = 'Y')
                    ORDER BY c.Proposal_Value DESC,
                             c.Customer_Name ASC,
                             c.Customer_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;
                decimal totalValue = 0;
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        totalValue = Util.GetValueOfDecimal(dr["Total_Value"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            ownerName = Util.GetValueOfString(dr["Owner_Name"]),
                            proposalCount = Util.GetValueOfInt(dr["Proposal_Count"]),
                            proposalValue = Util.GetValueOfDecimal(dr["Proposal_Value"])
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
                    total_value = totalValue,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_296_OpenProposalsWidget.GetRows", ex);
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
