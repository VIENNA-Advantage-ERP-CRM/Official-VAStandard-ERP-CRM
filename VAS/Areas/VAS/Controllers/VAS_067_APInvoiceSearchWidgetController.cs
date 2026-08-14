/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for the AP Invoice Search widget.
 *                  Full-width dashboard search bar that searches
 *                  vendor (AP) invoices (C_Invoice, IsSOTrx = 'N')
 *                  and returns the most relevant matches. Clicking a
 *                  result zooms to the invoice record.
 * chronological  : Development
 * Created Date   : 13 June 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * Notes:
 *   - Read-only. Only SELECT queries are executed.
 *   - The free-text term is bound (@Q1 + relevance params @QExact /
 *     @QStart / @QDoc). Client id, schema currency id and row cap are
 *     validated integers, then inlined (no user text in SQL - rule 2).
 *   - MRole.AddAccessSQL is applied to the main physical table only
 *     (the join-free base sub-select aliased i) - rule 1.
 *   - IsActive = 'Y' on the query (rule 6). Amounts are shown in the
 *     INVOICE's own currency (no currencyConvert) - each row carries the
 *     C_Currency symbol / ISO code and StdPrecision of that currency, so
 *     the widget formats every hit in the currency it was raised in.
 *     Results are ordered by relevance, then most recent.
 *   - Runs on Oracle and PostgreSQL.
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_067_APInvoiceSearchWidgetController : Controller
    {
        string strQuery = "";
        private static int _windowId = -1; // resolved once per process (-1 = not resolved)

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Parses an ISO (yyyy-MM-dd) date sent by the widget's date-range inputs. Returns null for
        /// an empty / malformed value, so a bad filter simply does not narrow the search rather than
        /// failing it. Invariant culture - the HTML date input always posts ISO.
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
        /// Parses an amount bound from the widget's amount range. The client reads those through the
        /// framework VAmountTextBox, whose getValue() already resolves the user's decimal separator
        /// and returns a plain number - so the wire format is always invariant ("1234.5").
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
        /// Searches vendor (AP) invoices for the given free-text term and returns
        /// the most relevant hits (each with its own currency symbol/precision) and the
        /// AD_Window_ID to zoom to. The optional filters NARROW (AND) the term match:
        ///   invFrom/invTo - Invoice Date (C_Invoice.DateInvoiced)
        ///   dueFrom/dueTo - Due Date     (C_InvoicePaySchedule.DueDate, matched via EXISTS so an
        ///                                 invoice with several schedules is still one row)
        ///   amtFrom/amtTo - grand-total band on ABS(GrandTotal), as the free-text amount match uses
        ///   currencyId    - C_Invoice.C_Currency_ID
        /// A filter on its own is a valid search: the term may be shorter than the 2-character
        /// minimum (or empty) when at least one filter is set.
        /// </summary>
        public JsonResult Search(string query, int maxRows = 25, int offset = 0,
            string invFrom = null, string invTo = null, string dueFrom = null, string dueTo = null,
            string amtFrom = null, string amtTo = null, int currencyId = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DocSearchResult result = BuildResult(ctx, query, maxRows, offset,
                    invFrom, invTo, dueFrom, dueTo, amtFrom, amtTo, currencyId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private DocSearchResult BuildResult(Ctx ctx, string query, int maxRows, int offset,
            string invFrom, string invTo, string dueFrom, string dueTo,
            string amtFrom, string amtTo, int currencyId)
        {
            DocSearchResult result = new DocSearchResult();
            result.Items = new List<SearchHit>();
            result.WindowId = ResolveWindowId();

            // Keep the term as typed for the LIKE params - the SQL wraps BOTH the column and
            // the parameter in LOWER() so the match is case-insensitive using the DB's own
            // collation (not C#'s ToLower, which can disagree on some locales). `ql` is only
            // for the C#-side status/paid keyword checks below.
            // Filters. The upper date bounds become the exclusive start of the following day, so the
            // range stays inclusive of that whole day even when the column carries a time.
            DateTime? invFromDate = ParseIsoDate(invFrom);
            DateTime? invToDate   = ParseIsoDate(invTo);
            DateTime? dueFromDate = ParseIsoDate(dueFrom);
            DateTime? dueToDate   = ParseIsoDate(dueTo);
            decimal? amtFromValue = ParseAmount(amtFrom);
            decimal? amtToValue   = ParseAmount(amtTo);
            if (currencyId < 0) { currencyId = 0; }
            // 0 in BOTH amount bounds means "no amount filter", not "invoices totalling exactly
            // zero" - otherwise a zeroed-out pair would silently return nothing.
            if (amtFromValue.HasValue && amtToValue.HasValue && amtFromValue.Value == 0m && amtToValue.Value == 0m)
            {
                amtFromValue = null;
                amtToValue   = null;
            }
            bool hasFilter = invFromDate.HasValue || invToDate.HasValue || dueFromDate.HasValue || dueToDate.HasValue
                             || amtFromValue.HasValue || amtToValue.HasValue || currencyId > 0;

            string term = (query ?? "").Trim();
            // A filter on its own IS a search, so the 2-character minimum only applies when there
            // is nothing else to narrow by.
            bool hasTerm = term.Length >= 2;
            if (!hasTerm && !hasFilter) { return result; }
            string ql = term.ToLowerInvariant();
            if (maxRows < 1) { maxRows = 1; }
            if (maxRows > 50) { maxRows = 50; }
            if (offset < 0) { offset = 0; }
            if (offset > 100000) { offset = 100000; }

            int clientId = ctx.GetAD_Client_ID();

            bool isPg    = DB.IsPostgreSQL();
            string sep   = isPg ? "' '" : "N' '";
            string empty = isPg ? "''"  : "N''";

            // MRole on the join-free single-table base (vendor invoices).
            string baseSql = @"SELECT i.C_Invoice_ID, i.DocumentNo, i.Description, i.POReference, i.InvoiceReference,
                       i.C_BPartner_ID, i.GrandTotal, i.C_Currency_ID,
                       i.C_DocTypeTarget_ID, i.SalesRep_ID, i.DocStatus, i.IsPaid, i.AD_Client_ID, i.AD_Org_ID, i.DateInvoiced
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N' AND i.IsActive = 'Y' AND i.AD_Client_ID = " + clientId;
            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Beyond the free-text match, the term can also find invoices by document STATUS
            // (keyword -> DocStatus code), PAID flag (paid / unpaid), or exact AMOUNT (when the
            // term is numeric). These are OR-ed with the text match. Status codes and the paid
            // flag are fixed literals (safe to inline); the amount is bound (@Amt). Mirrors
            // InvoicesController.SearchInvoices.
            var extraOrs = new List<string>();
            var codes = new List<string>();
            if (ql.Contains("draft"))    { codes.Add("'DR'"); }
            if (ql.Contains("progress")) { codes.Add("'IP'"); }
            if (ql.Contains("close"))    { codes.Add("'CL'"); }
            if (ql.Contains("approv"))   { codes.Add("'AP'"); }
            if (ql.Contains("complete")) { codes.Add("'CO'"); }
            if (ql.Contains("reverse"))  { codes.Add("'RE'"); codes.Add("'VO'"); }
            if (ql.Contains("void"))     { codes.Add("'VO'"); }
            if (ql.Contains("invalid"))  { codes.Add("'IN'"); }
            if (ql.Contains("waiting"))  { codes.Add("'WP'"); codes.Add("'WC'"); }
            if (codes.Count > 0) { extraOrs.Add("b.DocStatus IN (" + string.Join(", ", codes) + ")"); }
            // "unpaid" checked first (it contains "paid").
            if (ql.Contains("unpaid"))   { extraOrs.Add("b.IsPaid = 'N'"); }
            else if (ql.Contains("paid")) { extraOrs.Add("b.IsPaid = 'Y'"); }
            // Initialised: with the && short-circuit below the compiler cannot prove the out
            // parameter was assigned, and it is read again when the parameter is bound.
            decimal amt = 0;
            // Gated on hasTerm: without a term the OR block is dropped entirely (matchClause = 1=1),
            // so binding @Amt would leave a parameter with no placeholder - fatal under Oracle's
            // positional binding.
            bool hasAmt = hasTerm && decimal.TryParse(ql.Replace(",", "").Replace("$", "").Trim(), out amt);
            if (hasAmt) { extraOrs.Add("ABS(b.GrandTotal) = @Amt"); }

            // Lookup tables (C_DocType / AD_User salesrep) are joined outside MRole
            // scope and their names added to the searchable text so the user can
            // search by document-type name or buyer - not by id.
            string textMatch = "LOWER(b.DocumentNo || " + sep +
                 " || COALESCE(b.POReference, " + empty + ") || " + sep +
                 " || COALESCE(b.InvoiceReference, " + empty + ") || " + sep +
                 " || COALESCE(b.Description, " + empty + ") || " + sep +
                 " || COALESCE(bp.Name, " + empty + ") || " + sep +
                 " || COALESCE(bp.Value, " + empty + ") || " + sep +
                 " || COALESCE(dt.Name, " + empty + ") || " + sep +
                 " || COALESCE(dt.PrintName, " + empty + ") || " + sep +
                 " || COALESCE(u.Name, " + empty + ")) LIKE LOWER(@Q1)";
            // No term (a filter-only search) -> nothing to match on; the filters appended below do
            // all the narrowing. 1=1 keeps the WHERE well-formed without binding @Q1.
            string matchClause;
            if (!hasTerm)
            {
                matchClause = "1=1";
            }
            else
            {
                matchClause = extraOrs.Count > 0
                    ? "(" + textMatch + " OR " + string.Join(" OR ", extraOrs) + ")"
                    : textMatch;
            }

            // Amount stays in the invoice's OWN currency (no currencyConvert) and the row carries
            // that currency's symbol / ISO code / precision, so each hit is formatted in the
            // currency it was raised in.
            string core = @"SELECT b.C_Invoice_ID AS RecordId, b.DocumentNo AS DocumentNo, bp.Name AS Title,
                       COALESCE(b.GrandTotal, 0) AS Amount,
                       cur.CurSymbol AS CurSymbol, cur.ISO_Code AS IsoCode, cur.StdPrecision AS StdPrecision,
                       b.DateInvoiced AS DocDate, b.DocStatus AS DocStatus, b.IsPaid AS IsPaid,
                       b.InvoiceReference AS InvoiceRef, dt.Name AS DocTypeName, u.Name AS SalesRepName,
                       CASE WHEN LOWER(b.DocumentNo) = LOWER(@QExact) THEN 4
                            WHEN LOWER(b.DocumentNo) LIKE LOWER(@QStart) THEN 3
                            WHEN LOWER(b.DocumentNo) LIKE LOWER(@QDoc) THEN 2
                            ELSE 1 END AS Relevance
                  FROM (" + baseSql + @") b
                 INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = b.C_BPartner_ID)
                 INNER JOIN C_Currency cur ON (cur.C_Currency_ID = b.C_Currency_ID)
                 INNER JOIN C_DocType dt ON (dt.C_DocType_ID = b.C_DocTypeTarget_ID)
                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = b.SalesRep_ID)
                 WHERE " + matchClause;

            // Filters are ANDed onto the OUTER query (alias b = the already-MRole'd base select), so
            // the access SQL inside baseSql is untouched and its parser never sees the Due Date
            // EXISTS. Date bounds are half-open (>= from, < to+1day): no TRUNC / date_trunc, so the
            // same SQL runs on Oracle and PostgreSQL. Parameters are added further down in the exact
            // order their placeholders appear here - Oracle binds by POSITION, not by name.
            var filterParams = new List<SqlParameter>();
            if (invFromDate.HasValue)
            {
                core += " AND b.DateInvoiced >= @InvFrom";
                filterParams.Add(new SqlParameter("@InvFrom", SqlDbType.DateTime) { Value = invFromDate.Value });
            }
            if (invToDate.HasValue)
            {
                core += " AND b.DateInvoiced < @InvToExcl";
                filterParams.Add(new SqlParameter("@InvToExcl", SqlDbType.DateTime) { Value = invToDate.Value.AddDays(1) });
            }
            if (dueFromDate.HasValue || dueToDate.HasValue)
            {
                // EXISTS (not a join): an invoice with several pay schedules in the range stays a
                // SINGLE row in the dropdown.
                core += @" AND EXISTS (SELECT 1 FROM C_InvoicePaySchedule ips
                                        WHERE ips.C_Invoice_ID = b.C_Invoice_ID
                                          AND ips.IsActive = 'Y'";
                if (dueFromDate.HasValue)
                {
                    core += " AND ips.DueDate >= @DueFrom";
                    filterParams.Add(new SqlParameter("@DueFrom", SqlDbType.DateTime) { Value = dueFromDate.Value });
                }
                if (dueToDate.HasValue)
                {
                    core += " AND ips.DueDate < @DueToExcl";
                    filterParams.Add(new SqlParameter("@DueToExcl", SqlDbType.DateTime) { Value = dueToDate.Value.AddDays(1) });
                }
                core += ")";
            }
            // Band on ABS(GrandTotal) - the invoice's own currency, exactly like the free-text
            // amount match above and the Amount now shown in the row.
            if (amtFromValue.HasValue)
            {
                core += " AND (b.GrandTotal) >= @AmtFrom";
                filterParams.Add(new SqlParameter("@AmtFrom", Math.Abs(amtFromValue.Value)));
            }
            if (amtToValue.HasValue)
            {
                core += " AND (b.GrandTotal) <= @AmtTo";
                filterParams.Add(new SqlParameter("@AmtTo", Math.Abs(amtToValue.Value)));
            }
            if (currencyId > 0)
            {
                core += " AND b.C_Currency_ID = @CurrencyId";
                filterParams.Add(new SqlParameter("@CurrencyId", currencyId));
            }

            // Fetch ONE row more than the page size so we can tell the client whether another
            // page exists (HasMore) without a separate COUNT - the extra row is trimmed below.
            // (Same scroll-paging trick as InvoicesController.SearchInvoices.)
            int fetch = maxRows + 1;
            if (isPg)
            {
                strQuery = core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC LIMIT " + fetch + " OFFSET " + offset;
            }
            else
            {
                strQuery = "SELECT * FROM (SELECT t.*, ROWNUM AS rn FROM (" + core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC) t WHERE ROWNUM <= " + (offset + fetch) + ") WHERE rn > " + offset;
            }

            // Relevance params (doc-no exact / prefix / contains) come first textually (in the
            // SELECT CASE), then @Q1 in the WHERE, then the optional @Amt, then the filter bounds
            // (Oracle binds by POSITION, so the list order must match the SQL placeholder order).
            // @Q1 is bound ONLY when a term is present - a filter-only search drops it from the SQL.
            string like = "%" + term + "%";
            var p = new List<SqlParameter>
            {
                new SqlParameter("@QExact", term),
                new SqlParameter("@QStart", term + "%"),
                new SqlParameter("@QDoc", like)
            };
            if (hasTerm) { p.Add(new SqlParameter("@Q1", like)); }
            if (hasAmt) { p.Add(new SqlParameter("@Amt", Math.Abs(amt))); }
            p.AddRange(filterParams);
            DataSet ds = DB.ExecuteDataset(strQuery, p.ToArray(), null);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string docNo = Util.GetValueOfString(row["DocumentNo"]);
                    string title = Util.GetValueOfString(row["Title"]);
                    if (string.IsNullOrEmpty(title)) { title = docNo; }
                    DateTime? d = Util.GetValueOfDateTime(row["DocDate"]);
                    // Symbol is optional on C_Currency - fall back to the ISO code so the amount is
                    // never shown bare (a bare number would read as the login currency).
                    string curSymbol = Util.GetValueOfString(row["CurSymbol"]);
                    string isoCode   = Util.GetValueOfString(row["IsoCode"]);
                    if (string.IsNullOrEmpty(curSymbol)) { curSymbol = isoCode; }

                    result.Items.Add(new SearchHit
                    {
                        RecordId    = Util.GetValueOfInt(row["RecordId"]),
                        DocumentNo  = docNo,
                        Title       = title,
                        Amount      = Util.GetValueOfDecimal(row["Amount"]),
                        CurSymbol   = curSymbol,
                        IsoCode     = isoCode,
                        StdPrecision = Util.GetValueOfInt(row["StdPrecision"]),
                        DocDate     = d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "",
                        DocStatus   = Util.GetValueOfString(row["DocStatus"]),
                        IsPaid      = Util.GetValueOfString(row["IsPaid"]) == "Y",
                        InvoiceRef  = Util.GetValueOfString(row["InvoiceRef"]),
                        DocType     = Util.GetValueOfString(row["DocTypeName"]),
                        SalesRep    = Util.GetValueOfString(row["SalesRepName"])
                    });
                }
            }
            // Trim the sentinel extra row; its presence signals another page exists.
            result.HasMore = result.Items.Count > maxRows;
            if (result.HasMore) { result.Items.RemoveAt(result.Items.Count - 1); }
            return result;
        }

        /// <summary>Header window for zoom: VAS_APInvoice, else standard "Invoice (Vendor)".</summary>
        private int ResolveWindowId()
        {
            if (_windowId >= 0) { return _windowId; }
            int id = new PoReceiptTabPanelModel().GetWindowId("VAS_APInvoice", "Invoice (Vendor)");
            _windowId = id;
            return _windowId;
        }

        public class DocSearchResult
        {
            public int            WindowId     { get; set; }
            public bool           HasMore      { get; set; }   // another page exists (scroll paging)
            public List<SearchHit> Items       { get; set; }
        }

        public class SearchHit
        {
            public int     RecordId   { get; set; }
            public string  DocumentNo { get; set; }
            public string  Title      { get; set; }   // vendor name
            public decimal Amount     { get; set; }   // GrandTotal in the INVOICE's own currency
            public string  CurSymbol  { get; set; }   // that currency's symbol (ISO code if none)
            public string  IsoCode    { get; set; }   // C_Currency.ISO_Code
            public int     StdPrecision { get; set; } // that currency's standard precision
            public string  DocDate    { get; set; }
            public string  DocStatus  { get; set; }
            public bool    IsPaid     { get; set; }
            public string  InvoiceRef { get; set; }   // InvoiceReference
            public string  DocType    { get; set; }   // C_DocType.Name
            public string  SalesRep   { get; set; }   // salesrep (AD_User.Name)
        }
    }
}
