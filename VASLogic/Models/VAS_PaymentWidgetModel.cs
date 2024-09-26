using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_PaymentWidgetModel
    {
        /// <summary>
        /// Get Bank Balance
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public List<BankDetails> GetBankBalance(Ctx ctx)
        {
            List<BankDetails> getBankDetails = new List<BankDetails>();
            string qry = @"SELECT C_BANKACCOUNT.AD_CLIENT_ID,
                                    C_BANKACCOUNT.AD_ORG_ID,
                                    C_BANKACCOUNT.C_BANK_ID,
                                    C_BANKACCOUNT.C_BANKACCOUNT_ID,
                                    C_BANKACCOUNT.C_CURRENCY_ID,
                                    C_BANKACCOUNT.ACCOUNTNO,
                                    C_CURRENCY.ISO_CODE,C_BANK.NAME,
                                    C_CURRENCY.STDPRECISION,
                                    C_BANKACCOUNTLINE.ENDINGBALANCE,
                                    C_BANKACCOUNTLINE.STATEMENTDATE,BAL.BA
                                FROM C_BANKACCOUNT
                                    INNER JOIN BAL ON (BAL.C_BANKACCOUNT_ID = C_BANKACCOUNT.C_BANKACCOUNT_ID AND BAL.BA=1 )            
                                    INNER JOIN C_BANKACCOUNTLINE ON (C_BANKACCOUNTLINE.C_BANKACCOUNTLINE_ID = BAL.C_BANKACCOUNTLINE_ID AND BAL.BA=1 
                                    AND C_BANKACCOUNTLINE.C_BANKACCOUNT_ID=C_BANKACCOUNT.C_BANKACCOUNT_ID)
                                    INNER JOIN C_BANK ON (C_BANKACCOUNT.C_BANK_ID = C_BANK.C_BANK_ID )
                                    INNER JOIN C_CURRENCY ON (C_BANKACCOUNT.C_CURRENCY_ID = C_CURRENCY.C_CURRENCY_ID )
                                WHERE C_BANK.ISACTIVE = 'Y'
                                    AND C_BANKACCOUNT.ISACTIVE = 'Y'
                                    AND C_BANKACCOUNTLINE.ISACTIVE = 'Y'
                                    AND C_CURRENCY.ISACTIVE = 'Y'";

            qry = MRole.GetDefault(ctx).AddAccessSQL(qry, "C_BankAccount", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
            //string abc = @")AS BankBalance)";
            string _sql = @"WITH BAL AS(SELECT ROW_NUMBER() OVER (PARTITION BY C_BANKACCOUNTLINE.C_BANKACCOUNT_ID
                ORDER BY C_BANKACCOUNTLINE.STATEMENTDATE DESC, C_BANKACCOUNTLINE.C_BANKACCOUNTLINE_ID DESC) AS BA,
                C_BANKACCOUNTLINE.C_BANKACCOUNT_ID,C_BANKACCOUNTLINE.C_BANKACCOUNTLINE_ID  FROM C_BANKACCOUNTLINE)(SELECT
                            AD_CLIENT_ID,
                            AD_ORG_ID,
                            C_BANK_ID,
                            C_BANKACCOUNT_ID,
                            C_CURRENCY_ID,
                            ACCOUNTNO,
                            ISO_CODE,NAME,
                            STDPRECISION,
                            ENDINGBALANCE,
                            STATEMENTDATE
                        FROM
                            (" + qry + @" ORDER BY C_BANK.NAME))";
            DataSet _ds = DB.ExecuteDataset(_sql, null, null);
            if (_ds != null && _ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < _ds.Tables[0].Rows.Count; i++)
                {
                    BankDetails bankDetails = new BankDetails();
                    bankDetails.C_Bank_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["C_Bank_ID"]);
                    bankDetails.C_BankAccount_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["C_BankAccount_ID"]);
                    bankDetails.C_Currency_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["C_Currency_ID"]);
                    bankDetails.Name = Util.GetValueOfString(_ds.Tables[0].Rows[i]["Name"]);
                    bankDetails.AccountNo = Util.GetValueOfString(_ds.Tables[0].Rows[i]["AccountNo"]);
                    bankDetails.ISO_Code = Util.GetValueOfString(_ds.Tables[0].Rows[i]["ISO_Code"]);
                    bankDetails.StdPrecision = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["StdPrecision"]);
                    bankDetails.EndingBalance = Decimal.Round(Util.GetValueOfDecimal(_ds.Tables[0].Rows[i]["EndingBalance"]),
                        Util.GetValueOfInt(_ds.Tables[0].Rows[i]["StdPrecision"]), MidpointRounding.AwayFromZero);
                    getBankDetails.Add(bankDetails);
                }

            }
            return getBankDetails;
        }

        /// <summary>
        /// Get Cashbook Balance
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public List<CashbookDetails> GetCashBookBalance(Ctx ctx)
        {
            List<CashbookDetails> getCashbookDetails = new List<CashbookDetails>();
            string _sql = @"SELECT C_CashBook.AD_Client_ID,C_CashBook.AD_Org_ID, C_CashBook.C_CashBook_ID,C_Currency.ISO_Code,
                            C_Currency.StdPrecision,C_CashBook.CompletedBalance,C_CashBook.Name FROM  C_CashBook
                            INNER JOIN C_Currency ON (C_CashBook.C_Currency_ID=C_Currency.C_Currency_ID)
                            WHERE C_CashBook.IsActive='Y' AND C_Currency.IsActive='Y' ORDER BY C_CashBook.Name ASC";
            _sql = MRole.GetDefault(ctx).AddAccessSQL(_sql, "C_CashBook", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
            DataSet _ds = DB.ExecuteDataset(_sql, null, null);
            if (_ds != null && _ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < _ds.Tables[0].Rows.Count; i++)
                {
                    CashbookDetails cashbookDetails = new CashbookDetails();
                    cashbookDetails.Name = Util.GetValueOfString(_ds.Tables[0].Rows[i]["Name"]);
                    cashbookDetails.ISO_Code = Util.GetValueOfString(_ds.Tables[0].Rows[i]["ISO_Code"]);
                    cashbookDetails.StdPrecision = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["StdPrecision"]);
                    cashbookDetails.CompletedBalance = Decimal.Round(Util.GetValueOfDecimal(_ds.Tables[0].Rows[i]["CompletedBalance"]),
                        Util.GetValueOfInt(_ds.Tables[0].Rows[i]["StdPrecision"]), MidpointRounding.AwayFromZero);
                    getCashbookDetails.Add(cashbookDetails);
                }

            }
            return getCashbookDetails;
        }

        public class BankDetails
        {
            public int C_Bank_ID { get; set; }
            public int C_BankAccount_ID { get; set; }
            public string Name { get; set; }
            public int C_Currency_ID { get; set; }
            public string AccountNo { get; set; }
            public string ISO_Code { get; set; }
            public decimal EndingBalance { get; set; }
            public int StdPrecision { get; set; }
            public DateTime? StatementDate { get; set; }
        }
        public class CashbookDetails
        {
            public int C_Cashbook_ID { get; set; }
            public int C_Currency_ID { get; set; }
            public string Name { get; set; }
            public string ISO_Code { get; set; }
            public int StdPrecision { get; set; }
            public decimal CompletedBalance { get; set; }
        }
    }
}
