using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VAdvantage.VOS;
using VIS.Filters;

namespace VIS.Controllers
{
    public class InvoicesController : Controller
    {
        /// <summary>
        /// Returns duplicate-suspected sales-invoice pairs: same customer, same currency, same
        /// amount, issued within 7 days of each other. Pairs are skipped once either invoice has
        /// been acknowledged as not-a-duplicate (C_Invoice.VAS_IsNonDuplicateAcknowledge = 'Y',
        /// set by the "Keep both" action), so the banner/Review no longer flags them. Each row also
        /// carries the base (accounting-schema) currency symbol. Uses only ANSI/date arithmetic that
        /// runs on both Oracle and PostgreSQL (DATE − DATE → whole days on both).
        /// </summary>
        /// <returns>JSON { symbol, pairs[] } where each pair has invoiceA/B, customer, amount,
        /// dateA/B, docStatusA/B and daysApart.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDuplicates()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            var list = new List<object>();

            /* Absolute day gap between the two invoice dates, built per database dialect.
               PostgreSQL: in this deployment DateInvoiced behaves as a timestamp, so
               CAST(... AS DATE) − 7 / DATE − DATE raise
               "operator does not exist: timestamp without time zone - integer" (or yield an
               INTERVAL that cannot be compared to an integer). Convert (timestamp − timestamp)
               into whole NUMERIC days via date_part('epoch', …) / 86400 instead — this never does
               timestamp ± integer. date_part(...) is used in place of EXTRACT(EPOCH FROM …) so the
               SELECT and ON clauses carry no stray FROM keyword, leaving MRole.AddAccessSQL able to
               locate the real FROM clause when it injects the access predicate on the main alias a.
               Oracle: DATE − DATE already yields whole days; TRUNC drops any time component. */
            string dayGap;
            if (DB.IsPostgreSQL())
            {
                dayGap = "ABS(CAST(date_part('epoch', (CAST(a.DateInvoiced AS TIMESTAMP) - CAST(b.DateInvoiced AS TIMESTAMP))) / 86400 AS NUMERIC))";
            }
            else
            {
                dayGap = "ABS(TRUNC(a.DateInvoiced) - TRUNC(b.DateInvoiced))";
            }

            string duplicateInvoicesSql = @"
                SELECT a.DocumentNo AS InvoiceA,
                       b.DocumentNo AS InvoiceB,
                       a.C_Invoice_ID AS InvoiceAId,
                       b.C_Invoice_ID AS InvoiceBId,
                       a.C_BPartner_ID AS Dup_C_BPartner_ID,
                       a.C_Currency_ID AS Dup_C_Currency_ID,
                       bp.Name AS Customer,
                       a.GrandTotal AS Amount,
                       org.Name AS OrgName,
                       a.DateInvoiced AS DateA,
                       b.DateInvoiced AS DateB,
                       a.DocStatus AS DocStatusA,
                       b.DocStatus AS DocStatusB,
                       CASE WHEN dc.CurSymbol IS NOT NULL THEN dc.CurSymbol ELSE dc.ISO_Code END AS Dup_Cur_Symbol,
                       " + dayGap + @" AS DaysApart
                FROM C_Invoice a
                INNER JOIN C_Invoice b ON (a.C_BPartner_ID=b.C_BPartner_ID
                AND a.GrandTotal=b.GrandTotal
                AND a.C_Currency_ID=b.C_Currency_ID
                AND a.C_DocTypeTarget_ID=b.C_DocTypeTarget_ID
                AND a.C_Invoice_ID < b.C_Invoice_ID
                AND " + dayGap + @" <= 7)
                INNER JOIN C_BPartner bp ON (a.C_BPartner_ID=bp.C_BPartner_ID)
                INNER JOIN AD_Org org ON (a.AD_Org_ID=org.AD_Org_ID)
                INNER JOIN C_Currency dc ON (a.C_Currency_ID=dc.C_Currency_ID)
                WHERE a.DocStatus NOT IN ('RE' , 'VO')
                AND b.DocStatus NOT IN ('RE' , 'VO')
                AND a.IsSOTrx='Y'
                AND b.IsSOTrx='Y'
                AND a.IsActive='Y'
                AND b.IsActive='Y'
                AND COALESCE(a.VAS_IsNonDuplicateAcknowledge, 'N') <> 'Y'
                AND COALESCE(b.VAS_IsNonDuplicateAcknowledge, 'N') <> 'Y'";

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

            duplicateInvoicesSql = MRole.GetDefault(ctx).AddAccessSQL(
                duplicateInvoicesSql,
                "b",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* Base (accounting-schema) currency symbol for the session client; ISO code is the
               fallback when no display symbol is configured. Filtered to a single client row so
               the CROSS JOIN below cannot multiply the duplicate result set. The duplicate pairs
               are matched on the same C_Currency_ID, so this one symbol applies to every amount. */
            string schemaCurrencySql = @"
                SELECT CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema acc ON (ci.C_AcctSchema1_ID=acc.C_AcctSchema_ID)
                INNER JOIN C_Currency cur ON (acc.C_Currency_ID=cur.C_Currency_ID)
                WHERE ci.AD_Client_ID=" + ctx.GetAD_Client_ID();

            string sql = @"
                WITH DuplicateInvoices AS (
                    " + duplicateInvoicesSql + @"
                ),
                Schema_Currency AS (
                    " + schemaCurrencySql + @"
                )
                SELECT di.InvoiceA,
                       di.InvoiceB,
                       di.InvoiceAId,
                       di.InvoiceBId,
                       di.Dup_C_BPartner_ID,
                       di.Dup_C_Currency_ID,
                       di.Customer,
                       di.Amount,
                       di.OrgName,
                       di.DateA,
                       di.DateB,
                       di.DocStatusA,
                       di.DocStatusB,
                       di.DaysApart,
                       di.Dup_Cur_Symbol,
                       sc.Cur_Symbol
                FROM DuplicateInvoices di
                CROSS JOIN Schema_Currency sc
                ORDER BY di.DaysApart,
                         di.Amount DESC";

            string currencySymbol = "";
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                while (dr != null && dr.Read())
                {
                    /* Symbol is identical on every row (CROSS JOIN); capture it once. */
                    if (string.IsNullOrEmpty(currencySymbol))
                    {
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    }

                    list.Add(new
                    {
                        invoiceA = Util.GetValueOfString(dr["InvoiceA"]),
                        invoiceB = Util.GetValueOfString(dr["InvoiceB"]),
                        /* Invoice IDs + duplicate key let the Review dialog request the line-level
                           detail (GetDuplicateReview) for exactly this set. */
                        invoiceAId = Util.GetValueOfInt(dr["InvoiceAId"]),
                        invoiceBId = Util.GetValueOfInt(dr["InvoiceBId"]),
                        cBPartnerId = Util.GetValueOfInt(dr["Dup_C_BPartner_ID"]),
                        cCurrencyId = Util.GetValueOfInt(dr["Dup_C_Currency_ID"]),
                        customer = Util.GetValueOfString(dr["Customer"]),
                        amount = Util.GetValueOfDecimal(dr["Amount"]),
                        /* Symbol of the invoice's own currency (the set shares one currency); the
                           Review dialog shows set amounts in this currency, not the base currency. */
                        curSymbol = Util.GetValueOfString(dr["Dup_Cur_Symbol"]),
                        orgName = Util.GetValueOfString(dr["OrgName"]),
                        dateA = dr["DateA"] != DBNull.Value ? Convert.ToDateTime(dr["DateA"]).ToString("MMM dd, yyyy") : "",
                        dateB = dr["DateB"] != DBNull.Value ? Convert.ToDateTime(dr["DateB"]).ToString("MMM dd, yyyy") : "",
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

            /* Return the base-currency symbol alongside the pairs so the widget can render it
               before each amount regardless of the user's browser locale. */
            return Json(JsonConvert.SerializeObject(new { symbol = currencySymbol, pairs = list }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the line-level Duplicate Invoice Review detail for one duplicate set — the invoice
        /// IDs passed from the Review dialog (originally produced by GetDuplicates). For each invoice it
        /// returns header fields (organization, customer, sales order, delivery order, payment term,
        /// delivery location, grand total) plus one row per invoice line. Amounts are NOT converted: a
        /// duplicate set is matched on a single shared currency, so every amount is shown in the
        /// invoice's own currency (symbol/ISO/precision are that currency's). Header match flags
        /// (Is_Same_*) and a per-line
        /// Line_Match_Status drive the green/red dots in the modal. The duplicate set is identified by
        /// explicit invoice IDs (rather than a re-derived DENSE_RANK set id) so the detail always shows
        /// exactly the invoices the banner flagged. MRole is applied only on the main physical table
        /// (C_Invoice). Compatible with Oracle and PostgreSQL.
        /// </summary>
        /// <param name="invoiceIds">Comma-separated C_Invoice_ID values for the duplicate set.</param>
        /// <returns>JSON { symbol, iso, stdPrecision, rows[] } of flattened header + line rows.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDuplicateReview(string invoiceIds)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            /* Parse and validate the id list: only positive integers become bound parameters, so the
               client-supplied list cannot inject SQL. Capped to a sane maximum. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            List<string> idPlaceholders = new List<string>();
            if (!string.IsNullOrEmpty(invoiceIds))
            {
                string[] tokens = invoiceIds.Split(',');
                for (int t = 0; t < tokens.Length && idPlaceholders.Count < 50; t++)
                {
                    int id;
                    if (int.TryParse(tokens[t].Trim(), out id) && id > 0)
                    {
                        string pName = "@Inv" + idPlaceholders.Count;
                        idPlaceholders.Add(pName);
                        parameters.Add(new SqlParameter(pName, id));
                    }
                }
            }

            /* No valid ids -> nothing to compare. */
            if (idPlaceholders.Count == 0)
            {
                return Json(JsonConvert.SerializeObject(new { symbol = "", iso = "", stdPrecision = 0, rows = new List<object>() }), JsonRequestBehavior.AllowGet);
            }

            string inList = string.Join(", ", idPlaceholders);

            /* Main physical table for the review = C_Invoice (alias i). Rank by date so the newest
               invoice becomes 'Newer'/'Newest' and the oldest 'Original'. */
            string selectedInvoicesSql = @"
                SELECT i.C_Invoice_ID AS C_Invoice_ID,
                       i.DocumentNo AS Invoice_Document_No,
                       i.DocStatus AS DocStatus,
                       i.IsPaid AS IsPaid,
                       i.DateInvoiced AS Invoice_Date,
                       i.C_BPartner_ID AS C_BPartner_ID,
                       i.GrandTotal AS Grand_Total,
                       i.C_Currency_ID AS C_Currency_ID,
                       i.C_PaymentTerm_ID AS C_PaymentTerm_ID,
                       i.C_BPartner_Location_ID AS C_BPartner_Location_ID,
                       i.C_Order_ID AS C_Order_ID,
                       i.AD_Org_ID AS AD_Org_ID,
                       i.AD_Client_ID AS AD_Client_ID,
                       i.DateAcct AS DateAcct,
                       i.C_ConversionType_ID AS C_ConversionType_ID,
                       bp.Name AS Customer_Name,
                       cur.ISO_Code AS Currency_ISO,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Currency_Symbol,
                       cur.StdPrecision AS Currency_Precision,
                       dt.Name AS Document_Type_Name,
                       dt.DocBaseType AS Doc_Base_Type,
                       ROW_NUMBER() OVER (ORDER BY i.DateInvoiced DESC, i.C_Invoice_ID DESC) AS Newest_Rank,
                       ROW_NUMBER() OVER (ORDER BY i.DateInvoiced ASC, i.C_Invoice_ID ASC) AS Oldest_Rank
                FROM C_Invoice i
                INNER JOIN C_BPartner bp ON (i.C_BPartner_ID=bp.C_BPartner_ID)
                INNER JOIN C_Currency cur ON (i.C_Currency_ID=cur.C_Currency_ID)
                INNER JOIN C_DocType dt ON (dt.C_DocType_ID=i.C_DocTypeTarget_ID)
                WHERE i.C_Invoice_ID IN (" + inList + @")
                  AND i.IsActive='Y'
                  AND i.AD_Client_ID=" + clientId;

            /* MRole only on the main physical table (C_Invoice); the Line_Detail CTE consumes this
               already-secured result and the lookup/child joins inherit that filtering. */
            selectedInvoicesSql = MRole.GetDefault(ctx).AddAccessSQL(
                selectedInvoicesSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* Match flags use MIN(x) OVER () = MAX(x) OVER () rather than COUNT(DISTINCT x) OVER (),
               because neither Oracle nor PostgreSQL support DISTINCT inside a window function.
               Delivery order: shipment line -> M_InOut; sales order: invoice/order line -> C_Order.
               Delivery location prefers the shipment's partner location, else the invoice's. */
            string sql = @"
                WITH Selected_Invoices AS (
                    " + selectedInvoicesSql + @"
                ),
                Line_Detail AS (
                    SELECT si.C_Invoice_ID AS C_Invoice_ID,
                           si.Newest_Rank AS Newest_Rank,
                           CASE
                               WHEN si.Newest_Rank=1 THEN 'Newer'
                               WHEN si.Oldest_Rank=1 THEN 'Original'
                               ELSE 'Other'
                           END AS Panel_Role,
                           si.Invoice_Document_No AS Invoice_Document_No,
                           CASE
                               WHEN si.DocStatus IN ('CO', 'CL') AND si.IsPaid='Y' THEN 'Paid'
                               WHEN si.DocStatus IN ('CO', 'CL') THEN 'Sent'
                               WHEN si.DocStatus='DR' THEN 'Draft'
                               ELSE si.DocStatus
                           END AS Invoice_Status,
                           si.DocStatus AS DocStatus,
                           si.Invoice_Date AS Invoice_Date,
                           org.Name AS Organization_Name,
                           si.Document_Type_Name AS Document_Type_Name,
                           si.Doc_Base_Type AS Doc_Base_Type,
                           si.Customer_Name AS Customer_Name,
                           so.DocumentNo AS Sales_Order_No,
                           dio.DocumentNo AS Delivery_Order_No,
                           pt.Name AS Payment_Term_Name,
                           COALESCE(dbpl.Name, bpl.Name) AS Delivery_Location,
                           si.Grand_Total AS Grand_Total,
                           si.Currency_ISO AS Currency_ISO,
                           /* No currency conversion: a duplicate set shares one currency, so amounts
                              are shown in the invoice's own currency. */
                           si.Grand_Total AS Grand_Total_Base,
                           si.Currency_ISO AS Base_Currency_ISO,
                           si.Currency_Symbol AS Base_Currency_Symbol,
                           si.Currency_Precision AS Std_Precision,
                           il.C_InvoiceLine_ID AS C_InvoiceLine_ID,
                           il.Line AS Line_No,
                           p.Name AS Product_Name,
                           asi.Description AS Attribute_Set_Instance,
                           ch.Name AS Charge_Name,
                           il.QtyInvoiced AS Qty_Invoiced,
                           il.QtyEntered AS Qty_Entered,
                           il.PriceActual AS Unit_Price,
                           il.LineNetAmt AS Line_Net_Amount,
                           il.LineNetAmt AS Line_Net_Amount_Base,
                           CASE WHEN COUNT(1) OVER (PARTITION BY COALESCE(il.M_Product_ID, -1), COALESCE(il.C_Charge_ID, -1), COALESCE(il.M_AttributeSetInstance_ID, -1), il.QtyInvoiced, il.PriceActual, il.LineNetAmt) > 1 THEN 'matching' ELSE 'differing' END AS Line_Match_Status,
                           CASE WHEN MIN(si.C_BPartner_ID) OVER () = MAX(si.C_BPartner_ID) OVER () THEN 'Y' ELSE 'N' END AS Is_Same_Customer,
                           CASE WHEN MIN(si.Grand_Total) OVER () = MAX(si.Grand_Total) OVER () THEN 'Y' ELSE 'N' END AS Is_Same_Grand_Total,
                           CASE WHEN MIN(si.C_Currency_ID) OVER () = MAX(si.C_Currency_ID) OVER () THEN 'Y' ELSE 'N' END AS Is_Same_Currency,
                           CASE WHEN MIN(COALESCE(si.C_PaymentTerm_ID, -1)) OVER () = MAX(COALESCE(si.C_PaymentTerm_ID, -1)) OVER () THEN 'Y' ELSE 'N' END AS Is_Same_Payment_Term,
                           CASE WHEN MIN(si.Invoice_Date) OVER () = MAX(si.Invoice_Date) OVER () THEN 'Y' ELSE 'N' END AS Is_Same_Invoice_Date
                    FROM Selected_Invoices si
                    INNER JOIN AD_Org org ON (org.AD_Org_ID=si.AD_Org_ID)
                    LEFT OUTER JOIN C_PaymentTerm pt ON (pt.C_PaymentTerm_ID=si.C_PaymentTerm_ID)
                    LEFT OUTER JOIN C_BPartner_Location bpl ON (bpl.C_BPartner_Location_ID=si.C_BPartner_Location_ID)
                    LEFT OUTER JOIN C_InvoiceLine il ON (il.C_Invoice_ID=si.C_Invoice_ID AND il.IsActive='Y')
                    LEFT OUTER JOIN M_Product p ON (p.M_Product_ID=il.M_Product_ID)
                    LEFT OUTER JOIN C_Charge ch ON (ch.C_Charge_ID=il.C_Charge_ID)
                    LEFT OUTER JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID=il.M_AttributeSetInstance_ID)
                    LEFT OUTER JOIN C_OrderLine sol ON (sol.C_OrderLine_ID=il.C_OrderLine_ID)
                    LEFT OUTER JOIN C_Order so ON (so.C_Order_ID=COALESCE(si.C_Order_ID, sol.C_Order_ID))
                    LEFT OUTER JOIN M_InOutLine diol ON (diol.M_InOutLine_ID=il.M_InOutLine_ID)
                    LEFT OUTER JOIN M_InOut dio ON (dio.M_InOut_ID=diol.M_InOut_ID)
                    LEFT OUTER JOIN C_BPartner_Location dbpl ON (dbpl.C_BPartner_Location_ID=dio.C_BPartner_Location_ID)
                )
                SELECT ld.C_Invoice_ID,
                       ld.Panel_Role,
                       ld.Invoice_Document_No,
                       ld.Invoice_Status,
                       ld.DocStatus,
                       ld.Invoice_Date,
                       ld.Organization_Name,
                       ld.Document_Type_Name,
                       ld.Doc_Base_Type,
                       ld.Customer_Name,
                       ld.Sales_Order_No,
                       ld.Delivery_Order_No,
                       ld.Payment_Term_Name,
                       ld.Delivery_Location,
                       ld.Grand_Total,
                       ld.Currency_ISO,
                       ld.Grand_Total_Base,
                       ld.Base_Currency_ISO,
                       ld.Base_Currency_Symbol,
                       ld.Std_Precision,
                       ld.C_InvoiceLine_ID,
                       ld.Line_No,
                       ld.Product_Name,
                       ld.Attribute_Set_Instance,
                       ld.Charge_Name,
                       ld.Qty_Invoiced,
                       ld.Qty_Entered,
                       ld.Unit_Price,
                       ld.Line_Net_Amount,
                       ld.Line_Net_Amount_Base,
                       ld.Line_Match_Status,
                       ld.Is_Same_Customer,
                       ld.Is_Same_Grand_Total,
                       ld.Is_Same_Currency,
                       ld.Is_Same_Payment_Term,
                       ld.Is_Same_Invoice_Date
                FROM Line_Detail ld
                ORDER BY ld.Newest_Rank, ld.Line_No";

            List<object> rows = new List<object>();
            string symbol = "";
            string iso = "";
            int stdPrecision = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    if (string.IsNullOrEmpty(symbol))
                    {
                        symbol = Util.GetValueOfString(dr["Base_Currency_Symbol"]);
                    }
                    if (string.IsNullOrEmpty(iso))
                    {
                        iso = Util.GetValueOfString(dr["Base_Currency_ISO"]);
                    }
                    stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);

                    rows.Add(new
                    {
                        cInvoiceId = Util.GetValueOfInt(dr["C_Invoice_ID"]),
                        panelRole = Util.GetValueOfString(dr["Panel_Role"]),
                        invoiceDocumentNo = Util.GetValueOfString(dr["Invoice_Document_No"]),
                        invoiceStatus = Util.GetValueOfString(dr["Invoice_Status"]),
                        docStatus = Util.GetValueOfString(dr["DocStatus"]),
                        invoiceDate = dr["Invoice_Date"] != DBNull.Value ? Convert.ToDateTime(dr["Invoice_Date"]).ToString("MMM dd, yyyy") : "",
                        organizationName = Util.GetValueOfString(dr["Organization_Name"]),
                        documentTypeName = Util.GetValueOfString(dr["Document_Type_Name"]),
                        docBaseType = Util.GetValueOfString(dr["Doc_Base_Type"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        salesOrderNo = Util.GetValueOfString(dr["Sales_Order_No"]),
                        deliveryOrderNo = Util.GetValueOfString(dr["Delivery_Order_No"]),
                        paymentTermName = Util.GetValueOfString(dr["Payment_Term_Name"]),
                        deliveryLocation = Util.GetValueOfString(dr["Delivery_Location"]),
                        grandTotal = Util.GetValueOfDecimal(dr["Grand_Total"]),
                        currencyIso = Util.GetValueOfString(dr["Currency_ISO"]),
                        grandTotalBase = Util.GetValueOfDecimal(dr["Grand_Total_Base"]),
                        baseCurrencyIso = Util.GetValueOfString(dr["Base_Currency_ISO"]),
                        cInvoiceLineId = Util.GetValueOfInt(dr["C_InvoiceLine_ID"]),
                        lineNo = Util.GetValueOfInt(dr["Line_No"]),
                        productName = Util.GetValueOfString(dr["Product_Name"]),
                        attributeSetInstance = Util.GetValueOfString(dr["Attribute_Set_Instance"]),
                        chargeName = Util.GetValueOfString(dr["Charge_Name"]),
                        qtyInvoiced = Util.GetValueOfDecimal(dr["Qty_Invoiced"]),
                        qtyEntered = Util.GetValueOfDecimal(dr["Qty_Entered"]),
                        unitPrice = Util.GetValueOfDecimal(dr["Unit_Price"]),
                        lineNetAmount = Util.GetValueOfDecimal(dr["Line_Net_Amount"]),
                        lineNetAmountBase = Util.GetValueOfDecimal(dr["Line_Net_Amount_Base"]),
                        lineMatchStatus = Util.GetValueOfString(dr["Line_Match_Status"]),
                        isSameCustomer = Util.GetValueOfString(dr["Is_Same_Customer"]),
                        isSameGrandTotal = Util.GetValueOfString(dr["Is_Same_Grand_Total"]),
                        isSameCurrency = Util.GetValueOfString(dr["Is_Same_Currency"]),
                        isSamePaymentTerm = Util.GetValueOfString(dr["Is_Same_Payment_Term"]),
                        isSameInvoiceDate = Util.GetValueOfString(dr["Is_Same_Invoice_Date"])
                    });
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(JsonConvert.SerializeObject(new { symbol = symbol, iso = iso, stdPrecision = stdPrecision, rows = rows }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the paged "Invoices needing your attention" list for the dashboard widget.
        /// Only three actionable kinds of sales invoice are returned, every amount converted to the
        /// client's accounting-schema (base) currency:
        ///   Overdue   - unpaid pay schedule due in the last 7 days through today  (sort priority 1)
        ///   Due soon  - unpaid pay schedule due tomorrow .. today + 3 days        (sort priority 2)
        ///   Sent      - completed/closed in the recent window, whether or not the invoice e-mail has
        ///               been sent; the row carries Email_Sent (C_Invoice.VAS_IsEmailSent) so the widget
        ///               can show a "sent" vs "not sent" chip                       (sort priority 3)
        /// The Overdue and Due soon windows do not overlap (today belongs to Overdue), so an invoice
        /// only matches both via separate pay schedules; the dedup then keeps the highest-priority
        /// (lowest Sort_Priority) row per invoice. Compatible with Oracle and PostgreSQL.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (widget default 5).</param>
        /// <returns>JSON { symbol, iso, stdPrecision, rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAttentionInvoices(int pageNo = 1, int pageSize = 5)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0)
            {
                pageNo = 1;
            }
            if (pageSize <= 0)
            {
                pageSize = 5;
            }

            int offset = (pageNo - 1) * pageSize;
            int clientId = ctx.GetAD_Client_ID();

            /* Whole-day gap between an invoice's due date and today.
               PostgreSQL: in this deployment the date columns behave as timestamps, so DATE - DATE
               yields an INTERVAL that cannot be compared to / UNIONed with an integer (error 42804).
               Compute the gap via date_part('epoch', TIMESTAMP - TIMESTAMP) / 86400 instead, with both
               sides truncated to the day so the result is whole days. date_part is used (never
               EXTRACT(EPOCH FROM ...)) so no stray FROM keyword confuses MRole.AddAccessSQL's FROM
               parsing. Oracle: TRUNC date subtraction already yields whole days. */
            string dayDiff = DB.IsPostgreSQL()
                ? "CAST(date_part('epoch', (date_trunc('day', CAST(ips.DueDate AS TIMESTAMP)) - CAST(CURRENT_DATE AS TIMESTAMP))) / 86400 AS NUMERIC)"
                : "(TRUNC(ips.DueDate) - TRUNC(SYSDATE))";

            /* "Completed in the last 1 days": timestamp >= (today - 1). Subtracting an integer from a
               date is well-defined on both dialects and never yields an INTERVAL. */
            string updatedRecent = DB.IsPostgreSQL()
                ? "i.DateInvoiced >= CURRENT_DATE - 1"
                : "i.DateInvoiced >= TRUNC(SYSDATE) - 1";

            /* Typed NULLs for the Sent branch. A bare NULL routed through a CTE resolves to TEXT in
               PostgreSQL and then fails to UNION with the timestamp/numeric columns of the schedule
               branch, so the NULLs are cast to the matching type per dialect. */
            string nullDate = DB.IsPostgreSQL() ? "CAST(i.DateInvoiced AS TIMESTAMP)" : "CAST(i.DateInvoiced AS DATE)";
            string nullDelta = DB.IsPostgreSQL() ? "CAST(NULL AS NUMERIC)" : "CAST(NULL AS NUMBER)";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            /* Base (accounting-schema) currency for the session client. Filtered to a single client
               row so the CROSS JOIN in the outer query cannot multiply the result set. */
            string schemaCurrencySql = @"
                SELECT ci.AD_Client_ID AS AD_Client_ID,
                       acc.C_Currency_ID AS Base_Currency_ID,
                       cur.StdPrecision AS Std_Precision,
                       cur.ISO_Code AS Base_Currency_ISO,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Base_Currency_Symbol
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema acc ON (ci.C_AcctSchema1_ID=acc.C_AcctSchema_ID)
                INNER JOIN C_Currency cur ON (acc.C_Currency_ID=cur.C_Currency_ID)
                WHERE ci.AD_Client_ID=" + clientId;

            /* Overdue + Due soon: main physical table C_InvoicePaySchedule (alias ips). One row per
               unpaid, valid schedule with an open amount whose due date falls in the last 7 days
               through the next 3 days (dayDiff -7 .. 3). The amount is the schedule's own DueAmt (the
               instalment due), converted to base currency only when the invoice currency differs. */
            string scheduleSql = @"
                SELECT i.C_Invoice_ID AS C_Invoice_ID,
                       i.C_BPartner_ID AS C_BPartner_ID,
                       i.DocumentNo AS Invoice_Document_No,
                       bp.Name AS Customer_Name,
                       i.DateInvoiced AS Invoice_Date,
                       ips.DueDate AS Due_Date,
                       CASE
                           WHEN " + dayDiff + @" BETWEEN -7 AND 0 THEN 'Overdue'
                           WHEN " + dayDiff + @" BETWEEN 1 AND 3 THEN 'Due soon'
                       END AS Attention_Status,
                       CASE
                           WHEN " + dayDiff + @" BETWEEN -7 AND 0 THEN 1
                           WHEN " + dayDiff + @" BETWEEN 1 AND 3 THEN 2
                       END AS Sort_Priority,
                       " + dayDiff + @" AS Days_Delta,
                       (CASE WHEN dt.DocBaseType='ARC' THEN -1 ELSE 1 END) *
                       CASE
                           WHEN i.C_Currency_ID=sc.Base_Currency_ID THEN ips.DueAmt
                           ELSE CurrencyConvert(ips.DueAmt, i.C_Currency_ID, sc.Base_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                       END AS Amount_Base,
                       i.VAS_IsEmailSent AS Email_Sent
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                INNER JOIN C_BPartner bp ON (i.C_BPartner_ID=bp.C_BPartner_ID)
                INNER JOIN Schema_Currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                INNER JOIN C_DocType dt ON (dt.C_DocType_ID=i.C_DocTypeTarget_ID)
                WHERE i.IsSOTrx='Y'
                  AND i.IsActive='Y'
                  AND i.IsPaid='N'
                  AND ips.IsActive='Y'
                  AND ips.IsValid='Y'
                  AND ips.VA009_IsPaid='N'
                  AND COALESCE(ips.DueAmt, 0) > 0
                  AND " + dayDiff + @" BETWEEN -7 AND 3
                  AND i.AD_Client_ID=" + clientId;

            /* MRole only on the main physical table of this CTE body (C_InvoicePaySchedule); never on
               the joined lookup tables, the CTE aliases, or the final combined query. */
            scheduleSql = MRole.GetDefault(ctx).AddAccessSQL(
                scheduleSql,
                "C_InvoicePaySchedule",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* Sent: main physical table C_Invoice (alias i). Recently completed/closed sales invoices,
               shown regardless of whether the invoice e-mail has been sent — both states appear, and
               Email_Sent (C_Invoice.VAS_IsEmailSent) is returned so the row can show "sent" vs
               "not sent". No completion-timestamp column exists on C_Invoice in this schema, so Updated
               is used as the completion date (per the spec fallback). Has no dependency on
               C_InvoicePaySchedule. */
            string sentSql = @"
                SELECT i.C_Invoice_ID AS C_Invoice_ID,
                       i.C_BPartner_ID AS C_BPartner_ID,
                       i.DocumentNo AS Invoice_Document_No,
                       bp.Name AS Customer_Name,
                       i.DateInvoiced AS Invoice_Date,
                       " + nullDate + @" AS Due_Date,
                       'Sent' AS Attention_Status,
                       3 AS Sort_Priority,
                       " + nullDelta + @" AS Days_Delta,
                       (CASE WHEN dt.DocBaseType='ARC' THEN -1 ELSE 1 END) *
                       CASE
                           WHEN i.C_Currency_ID=sc.Base_Currency_ID THEN i.GrandTotal
                           ELSE CurrencyConvert(i.GrandTotal, i.C_Currency_ID, sc.Base_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                       END AS Amount_Base,
                       i.VAS_IsEmailSent AS Email_Sent
                FROM C_Invoice i
                INNER JOIN C_BPartner bp ON (i.C_BPartner_ID=bp.C_BPartner_ID)
                INNER JOIN Schema_Currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                INNER JOIN C_DocType dt ON (dt.C_DocType_ID=i.C_DocTypeTarget_ID)
                WHERE i.IsSOTrx='Y'
                  AND i.IsActive='Y'
                  AND i.DocStatus IN ('CO', 'CL')
                  AND " + updatedRecent + @"
                  AND i.AD_Client_ID=" + clientId;

            /* MRole only on the main physical table of this CTE body (C_Invoice / alias i). */
            sentSql = MRole.GetDefault(ctx).AddAccessSQL(
                sentSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* Combine both kinds, count once, then page. ROUND uses the base-currency precision.
               Sort_Priority orders the groups; Sent rows carry a NULL Due_Date and sort last.
               Dedup is per (invoice, attention status) on the schedule rows: an invoice that has BOTH
               an overdue and a due-soon pay schedule shows as TWO rows — one Overdue and one Due soon
               (each keeping the most urgent / earliest-due schedule of that status). Multiple schedules
               of the same status collapse to a single row for that status. The Sent row is shown only
               for invoices with no current overdue/due-soon schedule, so a schedule row always wins
               over Sent (preserves the previous priority behaviour without hiding the due-soon row). */
            string sql = @"
                WITH Schema_Currency AS (
                    " + schemaCurrencySql + @"
                ),
                Schedule_Attention AS (
                    " + scheduleSql + @"
                ),
                Sent_Attention AS (
                    " + sentSql + @"
                ),
                Ranked_Schedule AS (
                    SELECT sa.C_Invoice_ID, sa.C_BPartner_ID, sa.Invoice_Document_No, sa.Customer_Name, sa.Invoice_Date, sa.Due_Date, sa.Attention_Status, sa.Sort_Priority, sa.Days_Delta, sa.Amount_Base, sa.Email_Sent,
                           ROW_NUMBER() OVER (PARTITION BY sa.C_Invoice_ID, sa.Attention_Status ORDER BY sa.Due_Date, sa.Days_Delta) AS Rn
                    FROM Schedule_Attention sa
                    WHERE sa.Attention_Status IS NOT NULL
                ),
                Deduped_Schedule AS (
                    SELECT rs.C_Invoice_ID, rs.C_BPartner_ID, rs.Invoice_Document_No, rs.Customer_Name, rs.Invoice_Date, rs.Due_Date, rs.Attention_Status, rs.Sort_Priority, rs.Days_Delta, rs.Amount_Base, rs.Email_Sent
                    FROM Ranked_Schedule rs
                    WHERE rs.Rn=1
                ),
                All_Attention AS (
                    SELECT ds.C_Invoice_ID, ds.C_BPartner_ID, ds.Invoice_Document_No, ds.Customer_Name, ds.Invoice_Date, ds.Due_Date, ds.Attention_Status, ds.Sort_Priority, ds.Days_Delta, ds.Amount_Base, ds.Email_Sent
                    FROM Deduped_Schedule ds
                    UNION ALL
                    SELECT se.C_Invoice_ID, se.C_BPartner_ID, se.Invoice_Document_No, se.Customer_Name, se.Invoice_Date, se.Due_Date, se.Attention_Status, se.Sort_Priority, se.Days_Delta, se.Amount_Base, se.Email_Sent
                    FROM Sent_Attention se
                    WHERE se.C_Invoice_ID NOT IN (SELECT ds2.C_Invoice_ID FROM Deduped_Schedule ds2)
                ),
                Count_Data AS (
                    SELECT COUNT(1) AS Total_Records
                    FROM All_Attention
                )
                SELECT da.C_Invoice_ID,
                       da.C_BPartner_ID,
                       da.Invoice_Document_No,
                       da.Customer_Name,
                       da.Invoice_Date,
                       da.Due_Date,
                       da.Attention_Status,
                       da.Sort_Priority,
                       da.Days_Delta,
                       da.Email_Sent,
                       ROUND(COALESCE(da.Amount_Base, 0), sp.Std_Precision) AS Amount_Base,
                       sp.Base_Currency_ISO,
                       sp.Base_Currency_Symbol,
                       sp.Std_Precision,
                       cd.Total_Records
                FROM All_Attention da
                CROSS JOIN Count_Data cd
                CROSS JOIN Schema_Currency sp
                ORDER BY da.Sort_Priority, da.Due_Date, da.Amount_Base DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<object> rows = new List<object>();
            int totalRecords = 0;
            string currencySymbol = "";
            string currencyIso = "";
            int stdPrecision = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["Total_Records"]);
                    stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                    currencyIso = Util.GetValueOfString(dr["Base_Currency_ISO"]);

                    /* Symbol is identical on every row (single-client CROSS JOIN); capture it once. */
                    if (string.IsNullOrEmpty(currencySymbol))
                    {
                        currencySymbol = Util.GetValueOfString(dr["Base_Currency_Symbol"]);
                    }

                    /* Days_Delta and Due_Date are NULL on Sent rows; keep them null so the client can
                       render an em dash rather than a fabricated date/age. */
                    object daysDeltaValue = dr["Days_Delta"] == DBNull.Value ? null : (object)Util.GetValueOfInt(dr["Days_Delta"]);

                    rows.Add(new
                    {
                        cInvoiceId = Util.GetValueOfInt(dr["C_Invoice_ID"]),
                        cBPartnerId = Util.GetValueOfInt(dr["C_BPartner_ID"]),
                        documentNo = Util.GetValueOfString(dr["Invoice_Document_No"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        invoiceDate = dr["Invoice_Date"] != DBNull.Value ? Convert.ToDateTime(dr["Invoice_Date"]).ToString("yyyy-MM-dd") : "",
                        dueDate = dr["Due_Date"] != DBNull.Value ? Convert.ToDateTime(dr["Due_Date"]).ToString("yyyy-MM-dd") : "",
                        daysDelta = daysDeltaValue,
                        attentionStatus = Util.GetValueOfString(dr["Attention_Status"]),
                        /* Y/N invoice e-mail state; only meaningful for the 'Sent' rows, where the
                           widget shows a "sent" vs "not sent" chip. */
                        emailSent = Util.GetValueOfString(dr["Email_Sent"]),
                        amountBase = Util.GetValueOfDecimal(dr["Amount_Base"])
                    });
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            var result = new
            {
                symbol = currencySymbol,
                iso = currencyIso,
                stdPrecision = stdPrecision,
                rows = rows,
                pageNo = pageNo,
                pageSize = pageSize,
                totalRecords = totalRecords,
                totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Parses an ISO (yyyy-MM-dd) date sent by the search widget's date-range inputs. Returns null
        /// for an empty / malformed value, so a bad filter simply does not narrow the search rather
        /// than failing it. Parsed with the invariant culture (the HTML date input always posts ISO),
        /// never the server's locale.
        /// </summary>
        private static DateTime? ParseIsoDate(string value)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(value) &&
                DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }
            return null;
        }

        /// <summary>
        /// Parses an amount bound sent by the search widget's amount-range inputs. The client reads
        /// those through the framework's VAmountTextBox, whose getValue() already resolves the user's
        /// decimal separator and hands back a plain JS number — so the wire format is always invariant
        /// ("1234.5"), never locale-formatted. Returns null for an empty / malformed value.
        /// </summary>
        private static decimal? ParseAmount(string value)
        {
            decimal parsed;
            if (!string.IsNullOrEmpty(value) &&
                decimal.TryParse(value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return null;
        }

        /// <summary>
        /// Type-ahead search for the Sales Invoice search widget. Matches active sales invoices of the
        /// session client by document number, customer name, target document type (C_DocTypeTarget_ID —
        /// by C_DocType.Name or DocBaseType, e.g. "Credit Memo" / "ARC"), SALES ORDER document number
        /// (typing an order's DocumentNo returns every invoice raised against it — matched on the
        /// header link C_Invoice.C_Order_ID and on the line link C_InvoiceLine.C_OrderLine_ID →
        /// C_OrderLine → C_Order, so a "create lines from" invoice with no header order is found too),
        /// document status (keyword → DocStatus code or IsPaid flag), or grand-total amount (when the
        /// query is numeric). Those are
        /// OR'ed; the optional filters then NARROW (AND) the result:
        ///   invFrom/invTo - Invoice Date  (C_Invoice.DateInvoiced)
        ///   dueFrom/dueTo - Due Date      (C_InvoicePaySchedule.DueDate, matched via EXISTS so an
        ///                                  invoice with several schedules is still one row)
        ///   amtFrom/amtTo - grand-total range, compared on ABS(GrandTotal) exactly like the free-text
        ///                   amount match, so a credit memo's negative total falls in the same band
        ///   currencyId    - C_Invoice.C_Currency_ID (the widget picks it with the framework's
        ///                   C_Currency_ID lookup control)
        /// Either bound of a range may be omitted (open-ended). A filter alone is a valid search — the
        /// free-text term may be empty when at least one filter is set. Date ranges are inclusive of
        /// both days: the upper bound is bound as the exclusive start of the NEXT day, so a
        /// DateInvoiced / DueDate carrying a time component still matches (no TRUNC — Oracle-only). The
        /// Due Date range only decides WHICH invoices match; the schedule's own due date is not
        /// returned. Returns the top N matches (newest first), each carrying its own currency
        /// symbol/ISO and its target document type, so the widget can list them and zoom the host
        /// window's invoice tab to the chosen record. Every user value is bound as a parameter (no SQL
        /// injection); MRole is applied on the main physical table (C_Invoice). Oracle/PostgreSQL
        /// compatible.
        /// </summary>
        /// <param name="q">Free-text search term. May be empty when a filter is supplied.</param>
        /// <param name="max">Maximum rows to return (1..25, default 10).</param>
        /// <param name="page">1-based page for the dropdown's scroll paging.</param>
        /// <param name="invFrom">Invoice Date range start (yyyy-MM-dd), inclusive; optional.</param>
        /// <param name="invTo">Invoice Date range end (yyyy-MM-dd), inclusive; optional.</param>
        /// <param name="dueFrom">Due Date range start (yyyy-MM-dd), inclusive; optional.</param>
        /// <param name="dueTo">Due Date range end (yyyy-MM-dd), inclusive; optional.</param>
        /// <param name="amtFrom">Grand-total range start (invariant decimal), inclusive; optional.</param>
        /// <param name="amtTo">Grand-total range end (invariant decimal), inclusive; optional.</param>
        /// <param name="currencyId">C_Currency_ID to restrict to; 0 / absent = any currency.</param>
        /// <returns>JSON { rows[] } where each row has cInvoiceId, documentNo, customerName, grandTotal,
        /// docStatus, isPaid, dateInvoiced, docTypeName, docBaseType, orderDocumentNo, curSymbol,
        /// currencyIso.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchInvoices(string q, int max = 10, int page = 1,
            string invFrom = null, string invTo = null, string dueFrom = null, string dueTo = null,
            string amtFrom = null, string amtTo = null, int currencyId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            /* Date-range filters. The upper bounds become the exclusive start of the following day so
               the range stays inclusive of that whole day even when the column carries a time. */
            DateTime? invFromDate = ParseIsoDate(invFrom);
            DateTime? invToDate = ParseIsoDate(invTo);
            DateTime? dueFromDate = ParseIsoDate(dueFrom);
            DateTime? dueToDate = ParseIsoDate(dueTo);
            /* Amount range + currency restriction — same AND semantics as the date ranges. */
            decimal? amtFromValue = ParseAmount(amtFrom);
            decimal? amtToValue = ParseAmount(amtTo);
            /* 0 in BOTH bounds means "no amount filter", not "invoices totalling exactly zero" —
               otherwise a zeroed-out pair would silently return nothing. A single 0 bound is kept:
               0 → 5000 is a real band. */
            if (amtFromValue.HasValue && amtToValue.HasValue && amtFromValue.Value == 0m && amtToValue.Value == 0m)
            {
                amtFromValue = null;
                amtToValue = null;
            }
            if (currencyId < 0)
            {
                currencyId = 0;
            }
            bool hasFilter = invFromDate.HasValue || invToDate.HasValue || dueFromDate.HasValue || dueToDate.HasValue
                             || amtFromValue.HasValue || amtToValue.HasValue || currencyId > 0;

            q = (q ?? "").Trim();
            /* Nothing to search on: no term AND no filter. (A filter on its own IS a search.) */
            if (q.Length == 0 && !hasFilter)
            {
                return Json(JsonConvert.SerializeObject(new { rows = new List<object>() }), JsonRequestBehavior.AllowGet);
            }
            if (max <= 0 || max > 25)
            {
                max = 25;
            }
            /* 1-based page for scroll paging (25 rows per page). */
            if (page < 1)
            {
                page = 1;
            }

            /* All user input is bound; only fixed, code-controlled literals (status codes) are inlined.
               Oracle binds parameters by POSITION (not by name), so:
                 (a) a value used in two places needs two distinct placeholders, each supplied once, and
                 (b) the parameter list must be built in the exact order the placeholders appear in the
                     final SQL.
               Hence @Like1/@Like2 (same value) for the two LIKE clauses, and @Max (the FETCH bind) is
               added last, after the SQL is assembled. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            string likeVal = "%" + q.ToUpper() + "%";

            List<string> ors = new List<string>();
            if (q.Length > 0)
            {
                parameters.Add(new SqlParameter("@Like1", likeVal));
                ors.Add("UPPER(i.DocumentNo) LIKE @Like1");
                parameters.Add(new SqlParameter("@Like2", likeVal));
                ors.Add("UPPER(bp.Name) LIKE @Like2");
                /* Target document type (C_DocTypeTarget_ID): by its name ("Credit Memo", "AR
                   Invoice") or by its DocBaseType code ("ARC", "ARI"). */
                parameters.Add(new SqlParameter("@Like3", likeVal));
                ors.Add("UPPER(dt.Name) LIKE @Like3");
                parameters.Add(new SqlParameter("@Like4", likeVal));
                ors.Add("UPPER(dt.DocBaseType) LIKE @Like4");
                /* Sales order document number: typing an order's DocumentNo returns EVERY invoice
                   raised against that order. Two ways an invoice hangs off an order, both counted:
                     - header link, C_Invoice.C_Order_ID (the LEFT-joined so alias), and
                     - line link, C_InvoiceLine.C_OrderLine_ID -> C_OrderLine -> C_Order, which is
                       how a "create lines from" invoice references its order when the header
                       C_Order_ID was never set.
                   The line-level test is an EXISTS so an invoice with many matching lines is still
                   ONE row. (This whole OR block is appended after AddAccessSQL - see below - so the
                   access parser never sees these subqueries.) */
                parameters.Add(new SqlParameter("@Like5", likeVal));
                ors.Add("UPPER(so.DocumentNo) LIKE @Like5");
                parameters.Add(new SqlParameter("@Like6", likeVal));
                ors.Add(@"EXISTS (SELECT 1 FROM C_InvoiceLine ilo
                                  INNER JOIN C_OrderLine olo ON (olo.C_OrderLine_ID=ilo.C_OrderLine_ID)
                                  INNER JOIN C_Order oho ON (oho.C_Order_ID=olo.C_Order_ID)
                                  WHERE ilo.C_Invoice_ID=i.C_Invoice_ID
                                    AND ilo.IsActive='Y'
                                    AND UPPER(oho.DocumentNo) LIKE @Like6)");
            }

            /* Status keyword -> document-status code(s). Codes are constants, safe to inline. */
            string ql = q.ToLower();
            List<string> codes = new List<string>();
            /* Completed ('CO') and voided ('VO') invoices are excluded from search, so those
               keywords are intentionally not mapped here. */
            if (ql.Contains("draft")) { codes.Add("'DR'"); }
            if (ql.Contains("progress")) { codes.Add("'IP'"); }
            if (ql.Contains("close")) { codes.Add("'CL'"); }
            if (ql.Contains("approv")) { codes.Add("'AP'"); }
            if (ql.Contains("complete")) { codes.Add("'CO'"); }
            if (ql.Contains("reverse")) { codes.Add("'RE'"); codes.Add("'VO'"); }
            if (ql.Contains("invalid")) { codes.Add("'IN'"); }
            if (ql.Contains("waiting")) { codes.Add("'WP'"); codes.Add("'WC'"); }
            if (codes.Count > 0)
            {
                ors.Add("i.DocStatus IN (" + string.Join(", ", codes) + ")");
            }
            /* Paid / unpaid keyword (check 'unpaid' first since it contains 'paid'). */
            if (ql.Contains("unpaid"))
            {
                ors.Add("i.IsPaid='N'");
            }
            else if (ql.Contains("paid"))
            {
                ors.Add("i.IsPaid='Y'");
            }

            /* Amount match when the term parses as a number (commas/symbols stripped). */
            decimal amt;
            if (decimal.TryParse(q.Replace(",", "").Replace("$", "").Trim(), out amt))
            {
                parameters.Add(new SqlParameter("@Amt", Math.Abs(amt)));
                ors.Add("ABS(i.GrandTotal) = @Amt");
            }

            /* The sales order is LEFT joined: an invoice raised without an order (C_Order_ID unset)
               must still be found by its number / customer / amount. */
            string selectSql = @"
                SELECT i.C_Invoice_ID AS C_Invoice_ID,
                       i.DocumentNo AS Invoice_Document_No,
                       bp.Name AS Customer_Name,
                       i.GrandTotal AS Grand_Total,
                       i.DocStatus AS DocStatus,
                       i.IsPaid AS IsPaid,
                       i.DateInvoiced AS Invoice_Date,
                       dt.Name AS Doc_Type_Name,
                       dt.DocBaseType AS Doc_Base_Type,
                       so.DocumentNo AS Order_Document_No,
                       cur.ISO_Code AS Currency_ISO,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                FROM C_Invoice i
                INNER JOIN C_BPartner bp ON (i.C_BPartner_ID=bp.C_BPartner_ID)
                INNER JOIN C_Currency cur ON (i.C_Currency_ID=cur.C_Currency_ID)
                INNER JOIN C_DocType dt ON (dt.C_DocType_ID=i.C_DocTypeTarget_ID)
                LEFT OUTER JOIN C_Order so ON (so.C_Order_ID=i.C_Order_ID)
                WHERE i.IsSOTrx='Y'
                  AND i.IsActive='Y'
                  AND i.AD_Client_ID=" + clientId;

            /* MRole only on the main physical table (C_Invoice / alias i). Applied while the WHERE
               still carries nothing but plain predicates — everything with a subquery goes on after. */
            selectSql = MRole.GetDefault(ctx).AddAccessSQL(
                selectSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* The free-text OR block is appended AFTER AddAccessSQL: the sales-order LINE match is an
               EXISTS carrying its own FROM / WHERE, which would otherwise confuse the access-SQL
               parser when it looks for the statement's real FROM clause. Appending keeps the role
               predicate intact and the subquery out of its way. No term (a filter-only search) means
               no block at all — an empty "AND ()" would not parse. */
            if (ors.Count > 0)
            {
                selectSql += @"
                  AND (" + string.Join(" OR ", ors) + ")";
            }

            /* The date ranges follow, for the same reason (the Due Date filter is an EXISTS too).
               Both bounds are half-open
               (>= from, < to+1day) so no TRUNC / date_trunc is needed on either dialect.
               Parameters are added in the exact order their placeholders appear in the final SQL —
               Oracle binds by POSITION, not by name. */
            if (invFromDate.HasValue)
            {
                selectSql += @"
                  AND i.DateInvoiced >= @InvFrom";
                parameters.Add(new SqlParameter("@InvFrom", SqlDbType.DateTime) { Value = invFromDate.Value });
            }
            if (invToDate.HasValue)
            {
                selectSql += @"
                  AND i.DateInvoiced < @InvToExcl";
                parameters.Add(new SqlParameter("@InvToExcl", SqlDbType.DateTime) { Value = invToDate.Value.AddDays(1) });
            }
            if (dueFromDate.HasValue || dueToDate.HasValue)
            {
                /* EXISTS (not a join): an invoice with several pay schedules in the range stays a
                   SINGLE row in the dropdown. */
                selectSql += @"
                  AND EXISTS (SELECT 1 FROM C_InvoicePaySchedule ips
                              WHERE ips.C_Invoice_ID=i.C_Invoice_ID
                                AND ips.IsActive='Y'";
                if (dueFromDate.HasValue)
                {
                    selectSql += @"
                                AND ips.DueDate >= @DueFrom";
                    parameters.Add(new SqlParameter("@DueFrom", SqlDbType.DateTime) { Value = dueFromDate.Value });
                }
                if (dueToDate.HasValue)
                {
                    selectSql += @"
                                AND ips.DueDate < @DueToExcl";
                    parameters.Add(new SqlParameter("@DueToExcl", SqlDbType.DateTime) { Value = dueToDate.Value.AddDays(1) });
                }
                selectSql += ")";
            }
            /* Amount band on ABS(GrandTotal): the same comparison the free-text amount match uses, so
               a credit memo (negative total) lands in the band its magnitude belongs to. */
            if (amtFromValue.HasValue)
            {
                selectSql += @"
                  AND (i.GrandTotal) >= @AmtFrom";
                parameters.Add(new SqlParameter("@AmtFrom", Math.Abs(amtFromValue.Value)));
            }
            if (amtToValue.HasValue)
            {
                selectSql += @"
                  AND (i.GrandTotal) <= @AmtTo";
                parameters.Add(new SqlParameter("@AmtTo", Math.Abs(amtToValue.Value)));
            }
            if (currencyId > 0)
            {
                selectSql += @"
                  AND i.C_Currency_ID=@CurrencyId";
                parameters.Add(new SqlParameter("@CurrencyId", currencyId));
            }

            /* Ordering / paging is appended last, after both the access predicate and the filters -
               the Due Date range only decides WHICH invoices match; the schedule's own due date is
               not part of the result. */
            string sql = selectSql + @"
                ORDER BY i.DateInvoiced DESC, i.C_Invoice_ID DESC
                OFFSET @Offset ROWS FETCH NEXT @Max ROWS ONLY";

            /* Scroll paging: skip the pages already loaded, then fetch ONE row more than the page
               size so we can tell the client whether another page exists (hasMore) without a
               separate COUNT query. @Offset then @Max appear last in the SQL — for Oracle's
               positional binding they must be added last, in that order (after the @Like*, the
               optional @Amt, the optional date bounds and the optional amount / currency filters). */
            parameters.Add(new SqlParameter("@Offset", (page - 1) * max));
            parameters.Add(new SqlParameter("@Max", max + 1));

            List<object> rows = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        cInvoiceId = Util.GetValueOfInt(dr["C_Invoice_ID"]),
                        documentNo = Util.GetValueOfString(dr["Invoice_Document_No"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        grandTotal = Util.GetValueOfDecimal(dr["Grand_Total"]),
                        docStatus = Util.GetValueOfString(dr["DocStatus"]),
                        isPaid = Util.GetValueOfString(dr["IsPaid"]),
                        dateInvoiced = dr["Invoice_Date"] != DBNull.Value ? Convert.ToDateTime(dr["Invoice_Date"]).ToString("MMM dd, yyyy") : "",
                        docTypeName = Util.GetValueOfString(dr["Doc_Type_Name"]),
                        docBaseType = Util.GetValueOfString(dr["Doc_Base_Type"]),
                        /* Sales order the invoice hangs off (header link); empty when there is none
                           or when the invoice only references the order line-by-line. */
                        orderDocumentNo = Util.GetValueOfString(dr["Order_Document_No"]),
                        curSymbol = Util.GetValueOfString(dr["Cur_Symbol"]),
                        currencyIso = Util.GetValueOfString(dr["Currency_ISO"])
                    });
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            /* The extra (max+1)th row only signals another page exists; trim it so the client
               always receives at most a full page of `max` rows. */
            bool hasMore = rows.Count > max;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return Json(JsonConvert.SerializeObject(new { rows = rows, page = page, pageSize = max, hasMore = hasMore }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// "Keep both" action of the Duplicate Invoice Review dialog: flags every invoice in the
        /// reviewed duplicate set as acknowledged-not-a-duplicate
        /// (C_Invoice.VAS_IsNonDuplicateAcknowledge = 'Y'). Once flagged, GetDuplicates skips the set
        /// so the banner/Review no longer raises it. The id list is validated to positive integers
        /// (bound parameters) and the update is scoped to the session client, so neither SQL injection
        /// nor cross-tenant writes are possible. No DocStatus change — the invoices are left as-is.
        /// </summary>
        /// <param name="invoiceIds">Comma-separated C_Invoice_ID values for the duplicate set.</param>
        /// <returns>JSON { success, updated } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult KeepBothInvoices(string invoiceIds)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" });
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            /* Parse and validate the id list: only positive integers become bound parameters, so the
               client-supplied list cannot inject SQL. Capped to a sane maximum. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            List<string> idPlaceholders = new List<string>();
            if (!string.IsNullOrEmpty(invoiceIds))
            {
                string[] tokens = invoiceIds.Split(',');
                for (int t = 0; t < tokens.Length && idPlaceholders.Count < 50; t++)
                {
                    int id;
                    if (int.TryParse(tokens[t].Trim(), out id) && id > 0)
                    {
                        string pName = "@Inv" + idPlaceholders.Count;
                        idPlaceholders.Add(pName);
                        parameters.Add(new SqlParameter(pName, id));
                    }
                }
            }

            if (idPlaceholders.Count == 0)
            {
                return Json(new { error = "No valid invoice ids" });
            }

            string inList = string.Join(", ", idPlaceholders);
            /* Scoped to the session client so the acknowledge flag can never be flipped on another
               tenant's invoice even if a foreign id is passed. */
            string sql = "UPDATE C_Invoice SET VAS_IsNonDuplicateAcknowledge = 'Y' " +
                         "WHERE C_Invoice_ID IN (" + inList + ") AND AD_Client_ID = " + clientId;

            int updated = DB.ExecuteQuery(sql, parameters.ToArray());

            return Json(JsonConvert.SerializeObject(new { success = true, updated = updated }));
        }

        /// <summary>
        /// "Reverse newer invoice" action of the Duplicate Invoice Review dialog: runs the document's
        /// Reverse-Correct action on the newer invoice of a duplicate set — the same path the DocAction
        /// button drives. SetDocAction("RC") + ProcessIt("RC") dispatches through DocumentEngine to
        /// MInvoice.ReverseCorrectIt() (a workflow document action), which posts the reversing invoice
        /// and leaves the original at DocStatus = 'RE'. Reversed/voided invoices already drop out of
        /// GetDuplicates, so the set clears on the next refresh. Only Completed/Closed sales invoices of
        /// the session client can be reversed here; everything runs inside one transaction.
        /// </summary>
        /// <param name="cInvoiceId">C_Invoice_ID of the newer invoice to reverse.</param>
        /// <returns>JSON { success, message, docStatus, documentNo } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult ReverseNewerInvoice(int cInvoiceId)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" });
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (cInvoiceId <= 0)
            {
                return Json(new { error = "Invalid invoice" });
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("ReverseDupInvoice"));
            try
            {
                MInvoice invoice = new MInvoice(ctx, cInvoiceId, trx);

                /* Guard: must exist, belong to this client, be a sales invoice, and be in a state the
                   Reverse-Correct action accepts (Completed or Closed). */
                if (invoice.GetC_Invoice_ID() <= 0)
                {
                    trx.Rollback();
                    return Json(new { error = "Invoice not found" });
                }
                if (invoice.GetAD_Client_ID() != ctx.GetAD_Client_ID() || !invoice.IsSOTrx())
                {
                    trx.Rollback();
                    return Json(new { error = "Invoice not found" });
                }

                string docStatus = invoice.GetDocStatus();
                if (docStatus != "CO" && docStatus != "CL")
                {
                    trx.Rollback();
                    return Json(new { error = "Only a completed or closed invoice can be reversed" });
                }

                /* Run the workflow document action. ProcessIt("RC") -> DocumentEngine -> MInvoice
                   .ReverseCorrectIt(). DocAction is set first to mirror the DocAction-button flow. */
                invoice.SetDocAction(DocActionConstants.ACTION_Reverse_Correct);
                bool processed = invoice.ProcessIt(DocActionConstants.ACTION_Reverse_Correct);
                if (!processed)
                {
                    string pm = invoice.GetProcessMsg();
                    trx.Rollback();
                    return Json(new { error = "Reversal failed" + (string.IsNullOrEmpty(pm) ? "" : ": " + pm) });
                }

                if (!invoice.Save(trx))
                {
                    ValueNamePair vp = VLogger.RetrieveError();
                    trx.Rollback();
                    return Json(new { error = "Reversed but could not be saved" + (vp != null ? ": " + vp.GetName() : "") });
                }

                trx.Commit();

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    message = invoice.GetProcessMsg(),
                    docStatus = invoice.GetDocStatus(),
                    documentNo = invoice.GetDocumentNo()
                }));
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { /* ignore */ }
                return Json(new { error = ex.Message });
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
            }
        }
    }
}