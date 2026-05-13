using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class InvoicesController : Controller
    {
        /// <summary>
        /// Returns duplicate suspected pairs: same customer, same amount, ordered within 7 days.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDuplicates()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            var list = new List<object>();

            string duplicateInvoicesSql = @"
                SELECT a.DocumentNo AS InvoiceA,
                       b.DocumentNo AS InvoiceB,
                       bp.Name AS Customer,
                       a.GrandTotal AS Amount,
                       org.Name AS OrgName,
                       a.DateInvoiced AS DateA,
                       b.DateInvoiced AS DateB,
                       a.DocStatus AS DocStatusA,
                       b.DocStatus AS DocStatusB,
                       ABS(CAST(a.DateInvoiced AS DATE) - CAST(b.DateInvoiced AS DATE)) AS DaysApart
                FROM C_Invoice a
                INNER JOIN C_Invoice b ON (a.C_BPartner_ID=b.C_BPartner_ID
                AND a.GrandTotal=b.GrandTotal
                AND a.C_Invoice_ID < b.C_Invoice_ID
                AND CAST(b.DateInvoiced AS DATE) BETWEEN CAST(a.DateInvoiced AS DATE) - 7 AND CAST(a.DateInvoiced AS DATE) + 7)
                INNER JOIN C_BPartner bp ON (a.C_BPartner_ID=bp.C_BPartner_ID)
                INNER JOIN AD_Org org ON (a.AD_Org_ID=org.AD_Org_ID)
                WHERE a.DocStatus IS NOT NULL
                AND b.DocStatus IS NOT NULL
                AND a.IsSOTrx='Y'
                AND b.IsSOTrx='Y'
                AND a.IsActive='Y'
                AND b.IsActive='Y'";

            /*
             * MRole handling:
             * Apply MRole only on the main physical table selected by the user.
             *
             * Main physical table: C_Invoice
             * Primary alias: a
             *
             * Do not apply MRole on:
             * 1. Final combined WITH query
             * 2. CTE alias DuplicateInvoices
             * 3. Secondary self-join alias b
             * 4. Joined lookup tables C_BPartner or AD_Org
             */
            duplicateInvoicesSql = MRole.GetDefault(ctx).AddAccessSQL(
                duplicateInvoicesSql,
                "a",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH DuplicateInvoices AS (
                    " + duplicateInvoicesSql + @"
                )
                SELECT InvoiceA,
                       InvoiceB,
                       Customer,
                       Amount,
                       OrgName,
                       DateA,
                       DateB,
                       DocStatusA,
                       DocStatusB,
                       DaysApart
                FROM DuplicateInvoices
                ORDER BY DaysApart,
                         Amount DESC";

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                while (dr != null && dr.Read())
                {
                    list.Add(new
                    {
                        invoiceA = Util.GetValueOfString(dr["InvoiceA"]),
                        invoiceB = Util.GetValueOfString(dr["InvoiceB"]),
                        customer = Util.GetValueOfString(dr["Customer"]),
                        amount = Util.GetValueOfDecimal(dr["Amount"]),
                        orgName = Util.GetValueOfString(dr["OrgName"]),
                        dateA = Convert.ToDateTime(dr["DateA"]).ToString("MMM dd, yyyy"),
                        dateB = Convert.ToDateTime(dr["DateB"]).ToString("MMM dd, yyyy"),
                        docStatusA = Util.GetValueOfString(dr["DocStatusA"]),
                        docStatusB = Util.GetValueOfString(dr["DocStatusB"]),
                        daysApart = Util.GetValueOfInt(dr["DaysApart"])
                    });
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }

            return Json(JsonConvert.SerializeObject(list), JsonRequestBehavior.AllowGet);
        }
    }
}