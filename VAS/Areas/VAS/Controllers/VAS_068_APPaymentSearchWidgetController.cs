/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for the AP Payment Search widget.
 *                  Full-width dashboard search bar that searches
 *                  outgoing payments (C_Payment, IsReceipt = 'N')
 *                  and returns the most relevant matches. Clicking a
 *                  result zooms to the payment record.
 * chronological  : Development
 * Created Date   : 13 June 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * Notes:
 *   - Read-only. Only SELECT queries are executed.
 *   - The free-text term is a bind parameter (@Q1). Client id, schema
 *     currency id and row cap are validated integers, then inlined.
 *   - MRole.AddAccessSQL applied to the main physical table only
 *     (the join-free base sub-select aliased p). IsActive = 'Y';
 *     amounts stay in the payment's OWN currency (ABS so outgoing
 *     payments show positive) and carry that currency's symbol,
 *     ISO code and StdPrecision - nothing is converted.
 *   - Besides free text, the term also matches document-status
 *     keywords ("completed", "draft"), paid / allocated / reconciled
 *     keywords, and an amount when it parses as a number - the same
 *     shorthand InvoicesController.SearchInvoices supports.
 *   - The widget's funnel filters (Account Date range, bank account,
 *     payment-amount band, currency) are ANDed onto the outer query, so
 *     the MRole access SQL inside the base sub-select is untouched. Any
 *     filter on its own is a valid search - the term may then be empty.
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

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_068_APPaymentSearchWidgetController : Controller
    {
        string strQuery = "";
        private static int _windowId = -1;

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
        /// Searches outgoing payments for the free-text term and returns the most relevant hits.
        /// The optional filters NARROW (AND) the term match:
        ///   acctFrom/acctTo - Account Date  (C_Payment.DateAcct)
        ///   bankAccountId   - Bank Account  (C_Payment.C_BankAccount_ID)
        ///   amtFrom/amtTo   - payment-amount band on ABS(PayAmt), as the free-text amount match uses
        ///   currencyId      - C_Payment.C_Currency_ID
        /// A filter on its own is a valid search: the term may be shorter than the 2-character
        /// minimum (or empty) when at least one filter is set.
        /// </summary>
        public JsonResult Search(string query, int maxRows = 25, int offset = 0,
            string acctFrom = null, string acctTo = null, int bankAccountId = 0,
            string amtFrom = null, string amtTo = null, int currencyId = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DocSearchResult result = BuildResult(ctx, query, maxRows, offset,
                    acctFrom, acctTo, bankAccountId, amtFrom, amtTo, currencyId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private DocSearchResult BuildResult(Ctx ctx, string query, int maxRows, int offset,
            string acctFrom, string acctTo, int bankAccountId,
            string amtFrom, string amtTo, int currencyId)
        {
            DocSearchResult result = new DocSearchResult();
            result.Items = new List<SearchHit>();
            result.WindowId = ResolveWindowId();

            // Filters. The upper date bound becomes the exclusive start of the following day, so the
            // range stays inclusive of that whole day even when the column carries a time.
            DateTime? acctFromDate = ParseIsoDate(acctFrom);
            DateTime? acctToDate   = ParseIsoDate(acctTo);
            decimal? amtFromValue  = ParseAmount(amtFrom);
            decimal? amtToValue    = ParseAmount(amtTo);
            if (currencyId < 0) { currencyId = 0; }
            if (bankAccountId < 0) { bankAccountId = 0; }
            // 0 in BOTH amount bounds means "no amount filter", not "payments of exactly zero" -
            // otherwise a zeroed-out pair would silently return nothing.
            if (amtFromValue.HasValue && amtToValue.HasValue && amtFromValue.Value == 0m && amtToValue.Value == 0m)
            {
                amtFromValue = null;
                amtToValue   = null;
            }
            bool hasFilter = acctFromDate.HasValue || acctToDate.HasValue || bankAccountId > 0
                             || amtFromValue.HasValue || amtToValue.HasValue || currencyId > 0;

            string term = (query ?? "").Trim().ToLower();
            // A filter on its own IS a search, so the 2-character minimum only applies when there
            // is nothing else to narrow by.
            bool hasTerm = term.Length >= 2;
            if (!hasTerm && !hasFilter) { return result; }
            if (maxRows < 1) { maxRows = 1; }
            if (maxRows > 50) { maxRows = 50; }
            if (offset < 0) { offset = 0; }
            if (offset > 100000) { offset = 100000; }

            int clientId = ctx.GetAD_Client_ID();

            /* Schema currency is only a display fallback now - each hit carries its
               own currency symbol and precision, so a client without an accounting
               schema no longer suppresses the whole result set. */
            strQuery = @"SELECT c.CurSymbol, c.StdPrecision
                    FROM C_AcctSchema cs
                    INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                    INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                   WHERE ci.AD_Client_ID = " + clientId + @"
                     AND ci.IsActive = 'Y'
                     AND cs.IsActive = 'Y'
                     AND c.IsActive = 'Y'";
            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            result.StdPrecision = 2;

            DataSet cDs = DB.ExecuteDataset(strQuery, null, null);
            if (cDs != null && cDs.Tables.Count > 0 && cDs.Tables[0].Rows.Count > 0)
            {
                result.CurSymbol    = Util.GetValueOfString(cDs.Tables[0].Rows[0]["CurSymbol"]);
                result.StdPrecision = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            bool isPg    = DB.IsPostgreSQL();
            string sep   = isPg ? "' '" : "N' '";
            string empty = isPg ? "''"  : "N''";

            string baseSql = @"SELECT p.C_Payment_ID, p.DocumentNo, p.CheckNo, p.TrxNo, p.PONum, p.Description,
                       p.AccountNo, p.C_BPartner_ID, p.C_BankAccount_ID, p.PayAmt, p.C_Currency_ID, p.DateAcct,
                       p.C_ConversionType_ID, p.C_DocType_ID, p.DocStatus, p.C_Invoice_ID, p.AD_Client_ID, p.AD_Org_ID,
                       p.IsAllocated, p.IsReconciled
                  FROM C_Payment p
                 WHERE p.IsReceipt = 'N' AND p.IsActive = 'Y' AND p.AD_Client_ID = " + clientId;
            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Keyword shorthand, mirroring InvoicesController.SearchInvoices: a
               status word, a paid/allocated/reconciled word, or a number widens the
               match beyond the free-text LIKE. All of these are OR'd onto the text
               search, so they add hits rather than narrowing them. Status codes and
               Y/N flags are code-controlled literals - safe to inline; the amount is
               bound. */
            string ql = term;
            List<string> extraOrs = new List<string>();

            List<string> codes = new List<string>();
            if (ql.Contains("draft"))    { codes.Add("'DR'"); }
            if (ql.Contains("progress")) { codes.Add("'IP'"); }
            if (ql.Contains("complete")) { codes.Add("'CO'"); }
            if (ql.Contains("close"))    { codes.Add("'CL'"); }
            if (ql.Contains("approv"))   { codes.Add("'AP'"); }
            if (ql.Contains("reverse"))  { codes.Add("'RE'"); codes.Add("'VO'"); }
            if (ql.Contains("void"))     { codes.Add("'VO'"); }
            if (ql.Contains("invalid"))  { codes.Add("'IN'"); }
            if (ql.Contains("waiting"))  { codes.Add("'WP'"); codes.Add("'WC'"); }
            if (codes.Count > 0)
            {
                extraOrs.Add("b.DocStatus IN (" + string.Join(", ", codes) + ")");
            }

            /* A payment has no IsPaid column - being applied to an invoice is what
               "paid" means here, so paid/unpaid read IsAllocated, same as the
               allocated/unallocated pair. Negative forms are tested first because
               "unpaid" contains "paid" and "unallocated" contains "allocated". */
            if (ql.Contains("unpaid") || ql.Contains("not paid") ||
                ql.Contains("unallocated") || ql.Contains("not allocated"))
            {
                extraOrs.Add("COALESCE(b.IsAllocated, 'N') = 'N'");
            }
            else if (ql.Contains("paid") || ql.Contains("allocated"))
            {
                extraOrs.Add("b.IsAllocated = 'Y'");
            }

            if (ql.Contains("unreconciled") || ql.Contains("not reconciled"))
            {
                extraOrs.Add("COALESCE(b.IsReconciled, 'N') = 'N'");
            }
            else if (ql.Contains("reconciled"))
            {
                extraOrs.Add("b.IsReconciled = 'Y'");
            }

            /* Sign-agnostic amount match: outgoing payments are stored negative in
               some setups, so "1500" finds either sign.
               Gated on hasTerm: without a term the whole OR block is dropped, so binding
               @Amt would leave a parameter with no placeholder - fatal under Oracle's
               positional binding. */
            decimal amt = 0;
            bool hasAmt = hasTerm && decimal.TryParse(
                term.Replace(",", "").Replace("$", "").Trim(),
                out amt
            );
            if (hasAmt)
            {
                extraOrs.Add("ABS(b.PayAmt) = @Amt");
            }

            string extraOrSql = extraOrs.Count > 0
                ? "\n                    OR " + string.Join("\n                    OR ", extraOrs)
                : "";

            /* The whole free-text match (text LIKE, linked-invoice EXISTS, keyword ORs) is ONE
               parenthesised clause, so the filters appended below AND onto it as a unit instead of
               binding to the last OR. No term (a filter-only search) -> nothing to match on and the
               filters do all the narrowing; 1=1 keeps the WHERE well-formed without binding
               @Q1 / @Q2. */
            string textMatch = @"LOWER(b.DocumentNo || " + sep +
                 " || COALESCE(b.CheckNo, " + empty + ") || " + sep +
                 " || COALESCE(b.TrxNo, " + empty + ") || " + sep +
                 " || COALESCE(b.PONum, " + empty + ") || " + sep +
                 " || COALESCE(b.Description, " + empty + ") || " + sep +
                 " || COALESCE(b.AccountNo, " + empty + ") || " + sep +
                 " || COALESCE(bp.Name, " + empty + ") || " + sep +
                 " || COALESCE(bp.Value, " + empty + ") || " + sep +
                 " || COALESCE(ba.AccountNo, " + empty + ") || " + sep +
                 " || COALESCE(bk.Name, " + empty + ") || " + sep +
                 " || COALESCE(dt.Name, " + empty + ") || " + sep +
                 " || COALESCE(dt.PrintName, " + empty + @")) LIKE @Q1
                    OR EXISTS (SELECT 1 FROM C_Invoice inv
                                WHERE inv.IsActive = 'Y'
                                  AND (inv.C_Invoice_ID = b.C_Invoice_ID
                                       OR EXISTS (SELECT 1 FROM C_AllocationLine al
                                                   WHERE al.C_Payment_ID = b.C_Payment_ID
                                                     AND al.C_Invoice_ID = inv.C_Invoice_ID
                                                     AND al.IsActive = 'Y'))
                                  AND LOWER(COALESCE(inv.DocumentNo, " + empty + ") || " + sep +
                                  " || COALESCE(inv.InvoiceReference, " + empty + ") || " + sep +
                                  " || COALESCE(inv.POReference, " + empty + @")) LIKE @Q2)"
                 + extraOrSql;
            string matchClause = hasTerm ? "(" + textMatch + ")" : "1=1";

            // Lookup tables (C_DocType / C_Bank) are joined outside MRole scope and
            // their names added to the searchable text so the user can search by
            // document-type name or bank name - not by id.
            string core = @"SELECT b.C_Payment_ID AS RecordId, b.DocumentNo AS DocumentNo, bp.Name AS Title,
                       COALESCE(ABS(b.PayAmt), 0) AS Amount,
                       cur.ISO_Code AS CurrencyIso,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS CurSymbol,
                       COALESCE(cur.StdPrecision, 2) AS StdPrecision,
                       dt.Name AS DocTypeName,
                       bk.Name AS BankName, ba.AccountNo AS BankAccountNo,
                       b.IsAllocated AS IsAllocated, b.IsReconciled AS IsReconciled,
                       b.DateAcct AS DocDate, b.DocStatus AS DocStatus,
                       CASE WHEN LOWER(b.DocumentNo) = @QExact THEN 4
                            WHEN LOWER(b.DocumentNo) LIKE @QStart THEN 3
                            WHEN LOWER(b.DocumentNo) LIKE @QDoc THEN 2
                            ELSE 1 END AS Relevance,
                       /* The linked invoice is a PROPERTY of the payment, so it is
                          resolved unconditionally. It used to be filtered by the
                          search term as well, which made the same payment show its
                          invoice when found by invoice number but hide it when found
                          by its own document number - the field appeared to come and
                          go depending on what was typed. Matching on the invoice is
                          still handled by the EXISTS in the WHERE below. */
                       (SELECT MIN(inv.DocumentNo) FROM C_Invoice inv
                          WHERE inv.IsActive = 'Y'
                            AND (inv.C_Invoice_ID = b.C_Invoice_ID
                                 OR EXISTS (SELECT 1 FROM C_AllocationLine al
                                             WHERE al.C_Payment_ID = b.C_Payment_ID
                                               AND al.C_Invoice_ID = inv.C_Invoice_ID
                                               AND al.IsActive = 'Y'))) AS MatchedInvoiceNo
                  FROM (" + baseSql + @") b
                 LEFT JOIN C_BPartner bp ON (bp.C_BPartner_ID = b.C_BPartner_ID)
                 INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID = b.C_BankAccount_ID)
                 INNER JOIN C_Bank bk ON (bk.C_Bank_ID = ba.C_Bank_ID)
                 INNER JOIN C_DocType dt ON (dt.C_DocType_ID = b.C_DocType_ID)
                 INNER JOIN C_Currency cur ON (cur.C_Currency_ID = b.C_Currency_ID)
                 WHERE " + matchClause;

            /* Filters are ANDed onto the OUTER query (alias b = the already-MRole'd base select),
               so the access SQL inside baseSql is untouched. Date bounds are half-open
               (>= from, < to+1day): no TRUNC / date_trunc, so the same SQL runs on Oracle and
               PostgreSQL. Parameters are added further down in the exact order their placeholders
               appear here - Oracle binds by POSITION, not by name. */
            List<SqlParameter> filterParams = new List<SqlParameter>();
            if (acctFromDate.HasValue)
            {
                core += " AND b.DateAcct >= @AcctFrom";
                filterParams.Add(new SqlParameter("@AcctFrom", SqlDbType.DateTime) { Value = acctFromDate.Value });
            }
            if (acctToDate.HasValue)
            {
                core += " AND b.DateAcct < @AcctToExcl";
                filterParams.Add(new SqlParameter("@AcctToExcl", SqlDbType.DateTime) { Value = acctToDate.Value.AddDays(1) });
            }
            if (bankAccountId > 0)
            {
                core += " AND b.C_BankAccount_ID = @BankAccountId";
                filterParams.Add(new SqlParameter("@BankAccountId", bankAccountId));
            }
            // Band on ABS(PayAmt) - the payment's own currency, exactly like the free-text amount
            // match above and the Amount shown in the row.
            if (amtFromValue.HasValue)
            {
                core += " AND (b.PayAmt) >= @AmtFrom";
                filterParams.Add(new SqlParameter("@AmtFrom", Math.Abs(amtFromValue.Value)));
            }
            if (amtToValue.HasValue)
            {
                core += " AND (b.PayAmt) <= @AmtTo";
                filterParams.Add(new SqlParameter("@AmtTo", Math.Abs(amtToValue.Value)));
            }
            if (currencyId > 0)
            {
                core += " AND b.C_Currency_ID = @CurrencyId";
                filterParams.Add(new SqlParameter("@CurrencyId", currencyId));
            }

            if (isPg)
            {
                strQuery = core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC LIMIT " + maxRows + " OFFSET " + offset;
            }
            else
            {
                strQuery = "SELECT * FROM (SELECT t.*, ROWNUM AS rn FROM (" + core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC) t WHERE ROWNUM <= " + (offset + maxRows) + ") WHERE rn > " + offset;
            }

            // Oracle binds by POSITION, so this list must follow the order the
            // placeholders appear in the SQL: the three relevance params in the
            // SELECT CASE, then @Q1 and @Q2 in the WHERE, then @Amt at the tail of
            // the keyword OR block, then the filter bounds. (The MatchedInvoiceNo
            // subquery no longer filters by the term, so it binds nothing.)
            // @Q1 / @Q2 are bound ONLY when a term is present - a filter-only search
            // drops them from the SQL.
            string like = "%" + term + "%";
            List<SqlParameter> p2 = new List<SqlParameter>
            {
                new SqlParameter("@QExact", term),
                new SqlParameter("@QStart", term + "%"),
                new SqlParameter("@QDoc", like)
            };
            if (hasTerm)
            {
                p2.Add(new SqlParameter("@Q1", like));
                p2.Add(new SqlParameter("@Q2", like));
            }
            if (hasAmt)
            {
                p2.Add(new SqlParameter("@Amt", Math.Abs(amt)));
            }
            p2.AddRange(filterParams);
            DataSet ds = DB.ExecuteDataset(strQuery, p2.ToArray(), null);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string docNo = Util.GetValueOfString(row["DocumentNo"]);
                    string title = Util.GetValueOfString(row["Title"]);
                    if (string.IsNullOrEmpty(title)) { title = docNo; }
                    DateTime? d = Util.GetValueOfDateTime(row["DocDate"]);

                    result.Items.Add(new SearchHit
                    {
                        RecordId   = Util.GetValueOfInt(row["RecordId"]),
                        DocumentNo = docNo,
                        Title      = title,
                        Amount     = Util.GetValueOfDecimal(row["Amount"]),
                        CurSymbol    = Util.GetValueOfString(row["CurSymbol"]),
                        CurrencyIso  = Util.GetValueOfString(row["CurrencyIso"]),
                        StdPrecision = Util.GetValueOfInt(row["StdPrecision"]),
                        DocTypeName  = Util.GetValueOfString(row["DocTypeName"]),
                        BankName     = Util.GetValueOfString(row["BankName"]),
                        BankAccountNo = Util.GetValueOfString(row["BankAccountNo"]),
                        IsAllocated  = Util.GetValueOfString(row["IsAllocated"]) == "Y",
                        IsReconciled = Util.GetValueOfString(row["IsReconciled"]) == "Y",
                        DocDate    = d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "",
                        DocStatus  = Util.GetValueOfString(row["DocStatus"]),
                        MatchedInvoiceNo = Util.GetValueOfString(row["MatchedInvoiceNo"])
                    });
                }
            }
            return result;
        }

        /// <summary>Header window for zoom: VAS_APPayment, else standard "Payment".</summary>
        private int ResolveWindowId()
        {
            if (_windowId >= 0) { return _windowId; }
            int id = GetWindowIdByName("VAS_APPayment");
            if (id == 0) { id = GetWindowIdByName("Payment"); }
            _windowId = id;
            return _windowId;
        }

        private int GetWindowIdByName(string name)
        {
            string q = "SELECT MIN(AD_Window_ID) AS WID FROM AD_Window WHERE Name = @Name AND IsActive = 'Y'";
            SqlParameter[] p = { new SqlParameter("@Name", name) };
            return Util.GetValueOfInt(DB.ExecuteScalar(q, p, null));
        }

        public class DocSearchResult
        {
            public string         CurSymbol    { get; set; }
            public int            StdPrecision { get; set; }
            public int            WindowId     { get; set; }
            public List<SearchHit> Items       { get; set; }
        }

        public class SearchHit
        {
            public int     RecordId   { get; set; }
            public string  DocumentNo { get; set; }
            public string  Title      { get; set; }

            /* Amount is in the payment's own currency - CurSymbol / CurrencyIso /
               StdPrecision describe THAT currency, not the accounting schema's. */
            public decimal Amount       { get; set; }
            public string  CurSymbol    { get; set; }
            public string  CurrencyIso  { get; set; }
            public int     StdPrecision { get; set; }

            public string  DocTypeName   { get; set; }
            public string  BankName      { get; set; }
            public string  BankAccountNo { get; set; }
            public bool    IsAllocated   { get; set; }
            public bool    IsReconciled  { get; set; }

            public string  DocDate    { get; set; }
            public string  DocStatus  { get; set; }
            public string  MatchedInvoiceNo { get; set; }
        }
    }
}
