using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : On-account receipts (Widget 06)
    /// Purpose     : Customer advances / on-account receipts not yet matched to
    ///               invoices. KPI = SUM(PaymentAmount) of unallocated receipts rolled
    ///               up in the client's accounting-schema (base) currency, plus
    ///               the count of unapplied advances. Drill-down lists each
    ///               receipt (Date, Receipt No., Customer, Bank account, Amount).
    ///               Query per PROMPT.md "Widget 06 — On-account receipts".
    /// Chronological development:
    ///   VAS         2026-06-01 Created
    /// </summary>
    public class VAS_003_OnAccountReceiptsController : Controller
    {
        /// <summary>
        /// KPI: total on-account (unallocated) receipt amount converted to the
        /// accounting-schema (base) currency, its symbol, and the unapplied
        /// advance count. The schema-currency row is CROSS JOINed to the
        /// aggregate so the base-currency symbol is always returned (even when
        /// there are zero advances). MRole is applied only on the physical
        /// C_Payment table inside the OnAccountAgg CTE — never on the CTE alias
        /// or the outer combined query, per the project CTE rule.
        /// </summary>
        /// <returns>JSON { cCurrencyId, symbol, onAccountAmount, advanceCount }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOnAccountReceipts()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            /* Base (accounting-schema) currency for this client, with its
               symbol — single row. */
            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID AS AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision AS StdPrecision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=" + clientId;

            /* On-account = customer receipts (CO/CL) still unallocated — covers
               both prepayments and plain on-account receipts (both PROMPT.md
               branches reduce to IsAllocated='N'). Amount converted to schema
               currency. MRole on the physical C_Payment table. */
            string onAccountAggSql = @"
                SELECT SUM(CASE WHEN Payment.C_Currency_ID=SchemaCurrency.C_Currency_ID
                                THEN COALESCE(Payment.PaymentAmount, 0)
                                ELSE CurrencyConvert(COALESCE(Payment.PaymentAmount, 0), Payment.C_Currency_ID, SchemaCurrency.C_Currency_ID, COALESCE(Payment.DateAcct, Payment.DateTrx), Payment.C_ConversionType_ID, Payment.AD_Client_ID, Payment.AD_Org_ID)
                           END) AS OnAccountAmount,
                       COUNT(1) AS Advance_Count
                FROM C_Payment Payment
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Payment.AD_Client_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND COALESCE(Payment.IsAllocated, 'N') = 'N'";

            onAccountAggSql = MRole.GetDefault(ctx).AddAccessSQL(
                onAccountAggSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                OnAccountAgg AS (
                    " + onAccountAggSql + @"
                )
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.Cur_Symbol,
                       COALESCE(OnAccountAgg.Advance_Count, 0) AS Advance_Count,
                       ROUND(COALESCE(OnAccountAgg.OnAccountAmount, 0), SchemaCurrency.StdPrecision) AS OnAccountAmount
                FROM SchemaCurrency
                CROSS JOIN OnAccountAgg";

            int currencyId = 0;
            string currencySymbol = "";
            decimal onAccountAmount = 0;
            int advanceCount = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    advanceCount = Util.GetValueOfInt(dr["Advance_Count"]);
                    onAccountAmount = Util.GetValueOfDecimal(dr["OnAccountAmount"]);
                }

                var result = new
                {
                    cCurrencyId = currencyId,
                    symbol = currencySymbol,
                    onAccountAmount = onAccountAmount,
                    advanceCount = advanceCount
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// One page of the drill-down list: on-account (unallocated) receipts
        /// with Date, Receipt No., Customer, Bank account and Amount. The
        /// per-row amount stays in the receipt's OWN (document) currency — the
        /// symbol is returned alongside so the UI can prefix it. Server-side
        /// paged via OFFSET/FETCH. MRole on the physical C_Payment table inside
        /// the ReceiptsData CTE only.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 10).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOnAccountRows(int pageNo = 1, int pageSize = 10)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 10; }
            int offset = (pageNo - 1) * pageSize;

            /* Bank name falls back to the bank-account name; account number is
               returned raw and masked to the last 4 digits in the UI. */
            string receiptsBaseSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DateTrx AS Trx_Date,
                       Payment.DocumentNo AS Document_No,
                       BPartner.Name AS Customer_Name,
                       COALESCE(Bank.Name, BankAccount.Name) AS Bank_Name,
                       COALESCE(BankAccount.AccountNo, '') AS Account_No,
                       Payment.PaymentAmount AS Pay_Amount,
                       Currency.ISO_Code AS Payment_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Payment_Currency_Symbol,
                       Currency.StdPrecision AS Std_Precision
                FROM C_Payment Payment
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=Payment.C_BPartner_ID)
                LEFT OUTER JOIN C_BankAccount BankAccount ON (BankAccount.C_BankAccount_ID=Payment.C_BankAccount_ID)
                LEFT OUTER JOIN C_Bank Bank ON (Bank.C_Bank_ID=BankAccount.C_Bank_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=Payment.C_Currency_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND COALESCE(Payment.IsAllocated, 'N') = 'N'";

            receiptsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                receiptsBaseSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH ReceiptsData AS (
                    " + receiptsBaseSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM ReceiptsData
                )
                SELECT ReceiptsData.Payment_ID,
                       ReceiptsData.Trx_Date,
                       ReceiptsData.Document_No,
                       ReceiptsData.Customer_Name,
                       ReceiptsData.Bank_Name,
                       ReceiptsData.Account_No,
                       ReceiptsData.Pay_Amount,
                       ReceiptsData.Payment_Currency,
                       ReceiptsData.Payment_Currency_Symbol,
                       ReceiptsData.Std_Precision,
                       CountData.TotalRecords
                FROM ReceiptsData
                CROSS JOIN CountData
                ORDER BY ReceiptsData.Trx_Date DESC, ReceiptsData.Document_No DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime? trxDate = Util.GetValueOfDateTime(dr["Trx_Date"]);
                    int rowPrecision = Util.GetValueOfInt(dr["Std_Precision"]);

                    decimal amount = Math.Round(
                        Util.GetValueOfDecimal(dr["Pay_Amount"]),
                        rowPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    rows.Add(new
                    {
                        paymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        date = trxDate.HasValue
                            ? trxDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        bankName = Util.GetValueOfString(dr["Bank_Name"]),
                        accountNo = Util.GetValueOfString(dr["Account_No"]),
                        amount = amount,
                        currencyIso = Util.GetValueOfString(dr["Payment_Currency"]),
                        curSymbol = Util.GetValueOfString(dr["Payment_Currency_Symbol"]),
                        stdPrecision = rowPrecision
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }
    }
}
