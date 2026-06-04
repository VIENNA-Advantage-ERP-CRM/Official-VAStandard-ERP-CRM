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
    /// Module Name : Unreconciled receipts (Widget 05)
    /// Purpose     : KPI = COUNT of customer receipts (CO/CL) not yet reconciled
    ///               against the bank statement (IsReconciled <> 'Y'). Drill-down
    ///               lists each receipt (Date, Receipt No., Customer, Bank
    ///               account, Payment Currency, Amount). Query per PROMPT.md
    ///               "Widget 05 — Unreconciled receipts".
    /// Chronological development:
    ///   VAS         2026-06-01 Created
    /// </summary>
    public class VAS_011_UnreconciledReceiptsController : Controller
    {
        /// <summary>
        /// KPI: count of unreconciled customer receipts. MRole is applied
        /// directly on the physical C_Payment table.
        /// </summary>
        /// <returns>JSON { count }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnreconciledReceipts()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT COUNT(1) AS Unreconciled_Count
                FROM C_Payment Payment
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND COALESCE(Payment.IsReconciled, 'N') = 'N'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            int count = 0;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    count = Util.GetValueOfInt(dr["Unreconciled_Count"]);
                }

                var result = new { count = count };
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
        /// One page of the drill-down list: unreconciled receipts with Date,
        /// Receipt No., Customer, Bank account, Payment Currency and Amount. The
        /// per-row amount stays in the receipt's OWN (document) currency, with
        /// its symbol. Server-side paged via OFFSET/FETCH. MRole on C_Payment
        /// inside the ReceiptsData CTE only.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 10).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnreconciledRows(int pageNo = 1, int pageSize = 10)
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
                  AND COALESCE(Payment.IsReconciled, 'N') = 'N'";

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
