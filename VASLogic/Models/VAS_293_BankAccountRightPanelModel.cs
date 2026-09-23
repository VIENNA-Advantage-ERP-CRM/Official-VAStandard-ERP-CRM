/// <summary>
/// Module Name : VASLogic
/// Purpose     : Bank Account right panel data (read side). Returns a contextual,
///               read-only overview of the selected C_BankAccount record for the
///               VAS.VAS_293_BankAccountRightPanel tab panel:
///
///                 - the account identity with its bank, organisation, currency
///                   (symbol / ISO / precision) and the bank's postal address
///                   components, in one read,
///                 - the account financial position (current balance, opening
///                   balance, unmatched balance, credit limit),
///                 - the latest C_BankAccountLine (highest StatementDate, then
///                   highest C_BankAccountLine_ID) plus the active line count,
///                   from which the panel reports the reconciliation state,
///                 - one Linked Configuration row per ACTIVE child tab of the
///                   hosting window, each carrying its active record count and a
///                   short configuration detail.
///
///               C_BankAccount is the primary record table. Every other table is
///               read only to describe it.
///
///               MRole access filtering is applied to the MAIN physical table of
///               each query - the alias the user actually fetches from - and never
///               to a lookup join (C_Bank, C_Currency, AD_Org, C_Location,
///               C_Region, C_Country, C_AcctSchema). ORDER BY is appended AFTER
///               AddAccessSQL, because the rewriter appends its predicate to the
///               end of the WHERE clause it is given. The last JOIN of every
///               statement keeps a plain ON (no function call), which the access
///               parser needs.
///
///               Dictionary reads (AD_Tab / AD_Table / AD_Column / AD_Ref_List)
///               deliberately run WITHOUT AddAccessSQL - the same convention the
///               shipped VAS_291 / VAS_292 panels use for GetListReferenceName.
///               These rows live at AD_Client_ID = 0 and describe the window the
///               user already has open; they are metadata about the caller's own
///               screen, not business data, and routing them through the document
///               access rewriter would filter the System rows the dictionary is
///               made of.
///
///               Resilience: the Linked Configuration queries are independent of
///               each other AND of the panel. Each one is gated on its AD_Tab being
///               active and wrapped on its own, so a module table that is not
///               installed in a given environment (VA012_BankStatementClass,
///               FRPT_BankAccount_Acct) degrades that single row to "not
///               configured" instead of emptying the panel.
///
///               Portability: COALESCE / CASE / ANSI joins only - no NVL, DECODE,
///               ROWNUM, LIMIT or FETCH FIRST - so one statement serves both Oracle
///               and PostgreSQL; the "latest line" is the first row of a sorted
///               result, read in C#. Free-text literals carry the national-character
///               prefix; stored code comparisons (IsActive='Y') deliberately do not.
///               The account number and the IBAN leave the server whole - the panel
///               shows them as entered, by explicit request.
///
///               List-reference columns (BankAccountType) are resolved to their
///               translated names through AD_Ref_List / AD_Ref_List_Trl; the panel
///               never renders a raw code. IsActive / IsDefault are Yes-No flags and
///               are labelled on the client through AD_Message. The reconciliation
///               state leaves as a CODE, never as text - the client owns its wording.
/// Chronological development:
///   VAI145   2026-09-22  Created.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_293_BankAccountRightPanelModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_293_BankAccountRightPanelModel).FullName);

        /// <summary>Reconciliation state codes. The panel maps each to an AD_Message,
        /// so the wording is translated on the client and never travels as text.</summary>
        public const string RECON_RECONCILED = "RECONCILED";
        public const string RECON_REVIEW = "REVIEW";
        public const string RECON_NO_STATEMENT = "NOSTMT";

        /// <summary>Physical tables behind the linked child tabs, in the order the
        /// panel lists them. A tab whose table is not one of these is not a
        /// configuration row, and a row is only ever drawn for a table that has an
        /// ACTIVE tab in the hosting window.</summary>
        public const string TBL_ACCOUNT_LINE = "C_BankAccountLine";
        public const string TBL_ACCOUNT_DOC = "C_BankAccountDoc";
        public const string TBL_PAYMENT_PROCESSOR = "C_PaymentProcessor";
        public const string TBL_STATEMENT_LOADER = "C_BankStatementLoader";
        public const string TBL_STATEMENT_CLASS = "VA012_BankStatementClass";
        public const string TBL_DEFAULT_ACCOUNTING = "FRPT_BankAccount_Acct";

        /// <summary>How many configuration names the Bank Account Document row spells
        /// out before it falls back to a "+n" remainder.</summary>
        private const int DOC_NAMES_SHOWN = 2;

        /// <summary>AD dictionary answers for "does this column exist", keyed
        /// TableName.ColumnName. The dictionary does not change between requests, and
        /// the check guards a column that is present in some environments only, so the
        /// answer is read once per application lifetime rather than once per panel load.</summary>
        private static readonly Dictionary<string, bool> _columnExists =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _columnExistsLock = new object();

        // ----------------------------------------------------------------- //
        //  Entry point                                                       //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Returns the full panel payload for the selected bank account. The account
        /// row itself is read under MRole, so an id the browser sent for a record the
        /// role cannot see comes back as an empty payload - the client-supplied id is
        /// never trusted on its own.
        /// </summary>
        /// <param name="ctx">User context (client / org / role / language).</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="AD_Window_ID">Hosting window, taken from the panel's own tab on
        /// the client. Decides which child tabs exist; 0 leaves the Linked
        /// Configuration section out rather than guessing a window.</param>
        /// <returns>Populated <see cref="BankAccountPanelData"/>; an empty instance
        /// (C_BankAccount_ID = 0) when the id is invalid or no accessible row exists.</returns>
        public BankAccountPanelData GetAccountOverview(Ctx ctx, int C_BankAccount_ID, int AD_Window_ID)
        {
            BankAccountPanelData result = new BankAccountPanelData();
            result.LinkedConfigs = new List<LinkedConfigRow>();

            if (ctx == null || C_BankAccount_ID <= 0)
            {
                return result;
            }

            if (!LoadAccount(ctx, C_BankAccount_ID, result))
            {
                return result;   // not accessible to this role, or does not exist
            }

            /* The account lines carry both facts the Statement & Reconciliation
               section needs - the latest statement and how many lines exist - so
               they are read once here and reused by the linked-configuration row. */
            LoadAccountLines(ctx, C_BankAccount_ID, result);
            ApplyReconciliationState(result);

            /* Configuration rows are driven by the window's OWN tabs: a table with no
               active tab in this window is not a configuration the user can reach, so
               neither the row nor its count query exists. */
            if (AD_Window_ID > 0)
            {
                result.LinkedConfigs = LoadLinkedConfigs(ctx, C_BankAccount_ID, AD_Window_ID, result);
            }

            return result;
        }

        // ----------------------------------------------------------------- //
        //  1. Account identity, position, bank and address                   //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reads the account identity, its financial position, the organisation, the
        /// bank with its routing facts and postal address, and the account currency.
        /// This is the query that decides whether the caller may see the record at
        /// all, so MRole is applied here, on C_BankAccount. The location chain is
        /// joined LEFT OUTER so a bank with no address still comes back.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="result">Payload filled in place.</param>
        /// <returns>True when an accessible row was found.</returns>
        private bool LoadAccount(Ctx ctx, int C_BankAccount_ID, BankAccountPanelData result)
        {
            /* OpenBalance is present on C_BankAccount in some environments only, so the
               dictionary decides whether it may be named. Asking AD_Column beats a
               failed statement: naming a column that does not exist fails the WHOLE
               query, which would blank the panel over one optional metric. */
            bool hasOpenBalance = HasColumn("C_BankAccount", "OpenBalance");

            StringBuilder sql = new StringBuilder();
            sql.Append(@"SELECT ba.C_BankAccount_ID,
                                ba.AD_Org_ID,
                                org.Name AS OrganizationName,
                                COALESCE(ba.Name, N'') AS BankAccountName,
                                COALESCE(ba.Description, N'') AS AccountDescription,
                                COALESCE(ba.AccountNo, N'') AS AccountNo,
                                COALESCE(ba.IBAN, N'') AS IBAN,
                                ba.BankAccountType,
                                ba.CurrentBalance,
                                ba.UnMatchedBalance,
                                ba.CreditLimit,");
            if (hasOpenBalance)
            {
                sql.Append(@"
                                ba.OpenBalance,");
            }
            sql.Append(@"
                                COALESCE(ba.IsDefault, 'N') AS IsDefault,
                                COALESCE(ba.IsActive, 'N') AS IsActive,
                                b.C_Bank_ID,
                                COALESCE(b.Name, N'') AS BankName,
                                COALESCE(b.RoutingNo, N'') AS RoutingNo,
                                COALESCE(b.SwiftCode, N'') AS SwiftCode,
                                cur.C_Currency_ID,
                                cur.ISO_Code AS CurISO,
                                CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS CurSymbol,
                                cur.StdPrecision,
                                COALESCE(loc.Address1, N'') AS Address1,
                                COALESCE(loc.Address2, N'') AS Address2,
                                COALESCE(loc.Address3, N'') AS Address3,
                                COALESCE(loc.Address4, N'') AS Address4,
                                COALESCE(loc.City, N'') AS City,
                                COALESCE(loc.Postal, N'') AS Postal,
                                COALESCE(loc.Postal_Add, N'') AS Postal_Add,
                                COALESCE(reg.Name, N'') AS RegionName,
                                COALESCE(country.Name, N'') AS CountryName
                         FROM C_BankAccount ba
                         INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                         INNER JOIN C_Currency cur ON (cur.C_Currency_ID=ba.C_Currency_ID)
                         INNER JOIN AD_Org org ON (org.AD_Org_ID=ba.AD_Org_ID)
                         LEFT OUTER JOIN C_Location loc ON (loc.C_Location_ID=b.C_Location_ID)
                         LEFT OUTER JOIN C_Region reg ON (reg.C_Region_ID=loc.C_Region_ID)
                         LEFT OUTER JOIN C_Country country ON (country.C_Country_ID=loc.C_Country_ID)
                         WHERE ba.C_BankAccount_ID=@C_BankAccount_ID
                           AND ba.AD_Client_ID=@AD_Client_ID");

            /* MRole on the MAIN physical table only - every join above merely
               describes the account the user already asked for. */
            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankAccount_ID", C_BankAccount_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_293 LoadAccount(" + C_BankAccount_ID + "): " + ex.Message);
                return false;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return false;
            }

            DataRow r = ds.Tables[0].Rows[0];

            result.C_BankAccount_ID = Util.GetValueOfInt(r["C_BankAccount_ID"]);
            result.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            result.OrganizationName = Util.GetValueOfString(r["OrganizationName"]);
            result.BankAccountName = Util.GetValueOfString(r["BankAccountName"]);
            result.AccountDescription = Util.GetValueOfString(r["AccountDescription"]);
            result.AccountNo = Util.GetValueOfString(r["AccountNo"]);
            result.IBAN = Util.GetValueOfString(r["IBAN"]);

            result.BankAccountType = Util.GetValueOfString(r["BankAccountType"]);
            /* BankAccountType is a LIST column stored as a short code; the panel must
               never render the raw code, so the dictionary resolves the display name. */
            result.BankAccountTypeName = GetListReferenceName(ctx, "C_BankAccount", "BankAccountType", result.BankAccountType);

            result.IsDefault = Util.GetValueOfString(r["IsDefault"]) == "Y";
            result.IsActive = Util.GetValueOfString(r["IsActive"]) == "Y";

            /* Account currency drives every amount the panel shows. */
            result.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.CurISO = Util.GetValueOfString(r["CurISO"]);
            result.CurSymbol = Util.GetValueOfString(r["CurSymbol"]);
            result.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);

            result.CurrentBalance = Round(Util.GetValueOfDecimal(r["CurrentBalance"]), result.StdPrecision);
            result.UnMatchedBalance = Round(Util.GetValueOfDecimal(r["UnMatchedBalance"]), result.StdPrecision);
            result.CreditLimit = Round(Util.GetValueOfDecimal(r["CreditLimit"]), result.StdPrecision);
            result.HasOpenBalance = hasOpenBalance;
            if (hasOpenBalance)
            {
                result.OpenBalance = Round(Util.GetValueOfDecimal(r["OpenBalance"]), result.StdPrecision);
            }

            result.C_Bank_ID = Util.GetValueOfInt(r["C_Bank_ID"]);
            result.BankName = Util.GetValueOfString(r["BankName"]);
            result.RoutingNo = Util.GetValueOfString(r["RoutingNo"]);
            result.SwiftCode = Util.GetValueOfString(r["SwiftCode"]);

            /* Composed here, not in SQL: string concatenation of a variable number of
               optional parts differs between Oracle and PostgreSQL, and the blank
               components have to be dropped rather than rendered as empty separators. */
            result.BankAddress = ComposeAddress(r);

            return true;
        }

        /// <summary>
        /// Joins the populated address components of the bank's location into one
        /// display line, in postal order, skipping every blank part.
        /// </summary>
        /// <param name="r">Row of the main account query.</param>
        /// <returns>Comma-separated address, or "" when the bank has no location.</returns>
        private static string ComposeAddress(DataRow r)
        {
            string[] parts = new string[]
            {
                Util.GetValueOfString(r["Address1"]),
                Util.GetValueOfString(r["Address2"]),
                Util.GetValueOfString(r["Address3"]),
                Util.GetValueOfString(r["Address4"]),
                Util.GetValueOfString(r["City"]),
                Util.GetValueOfString(r["RegionName"]),
                JoinPostal(Util.GetValueOfString(r["Postal"]), Util.GetValueOfString(r["Postal_Add"])),
                Util.GetValueOfString(r["CountryName"])
            };

            StringBuilder address = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = (parts[i] == null) ? "" : parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }
                if (address.Length > 0)
                {
                    address.Append(", ");
                }
                address.Append(part);
            }
            return address.ToString();
        }

        /// <summary>Joins a postal code with its additional part ("8001-2200"); either
        /// side may be blank.</summary>
        /// <param name="postal">Postal code.</param>
        /// <param name="postalAdd">Additional postal code.</param>
        /// <returns>Combined postal code, or "" when both are blank.</returns>
        private static string JoinPostal(string postal, string postalAdd)
        {
            string a = (postal == null) ? "" : postal.Trim();
            string b = (postalAdd == null) ? "" : postalAdd.Trim();
            if (a.Length > 0 && b.Length > 0)
            {
                return a + "-" + b;
            }
            return (a.Length > 0) ? a : b;
        }

        // ----------------------------------------------------------------- //
        //  2. Account lines - latest statement + active count                //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reads the account's active lines newest-first and keeps the first row as
        /// the latest statement, plus the row count. Sorting and taking the first row
        /// in C# is deliberate: a row-limit clause is the one piece of SQL that cannot
        /// be written once for Oracle and PostgreSQL, and the same result set answers
        /// both the Statement section and the Account Line configuration count.
        /// An absent or unreadable line set is not an error - the panel reports "no
        /// statement" and keeps every other section.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="result">Payload filled in place.</param>
        private void LoadAccountLines(Ctx ctx, int C_BankAccount_ID, BankAccountPanelData result)
        {
            string sql = @"SELECT bal.C_BankAccountLine_ID,
                                  bal.StatementDate,
                                  bal.EndingBalance,
                                  bal.StatementDifference
                           FROM C_BankAccountLine bal
                           WHERE bal.C_BankAccount_ID=@C_BankAccount_ID
                             AND bal.IsActive='Y'
                             AND bal.AD_Client_ID=@AD_Client_ID";

            /* ORDER BY goes on AFTER AddAccessSQL - the rewriter appends its predicate
               to the end of the WHERE clause it is handed. */
            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "bal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO)
                + " ORDER BY bal.StatementDate DESC,bal.C_BankAccountLine_ID DESC";

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankAccount_ID", C_BankAccount_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_293 LoadAccountLines(" + C_BankAccount_ID + "): " + ex.Message);
                return;
            }

            if (ds == null || ds.Tables.Count == 0)
            {
                return;
            }

            result.AccountLineCount = ds.Tables[0].Rows.Count;
            if (result.AccountLineCount == 0)
            {
                return;
            }

            /* First row of the sorted set = highest StatementDate, then highest id. */
            DataRow r = ds.Tables[0].Rows[0];
            result.HasLatestLine = true;
            result.LatestStatementDate = FormatDate(Util.GetValueOfDateTime(r["StatementDate"]));
            result.LatestEndingBalance = Round(Util.GetValueOfDecimal(r["EndingBalance"]), result.StdPrecision);
            result.LatestStatementDifference = Round(Util.GetValueOfDecimal(r["StatementDifference"]), result.StdPrecision);
        }

        /// <summary>
        /// Decides the reconciliation state from the latest line and the account's
        /// unmatched balance. Both tests run on the decimal values at the currency's
        /// precision, never on formatted strings, and the answer leaves as a CODE so
        /// the client owns the wording.
        /// </summary>
        /// <param name="result">Payload filled in place.</param>
        private static void ApplyReconciliationState(BankAccountPanelData result)
        {
            if (!result.HasLatestLine)
            {
                result.ReconciliationState = RECON_NO_STATEMENT;
                return;
            }

            bool settled = IsWithinTolerance(result.LatestStatementDifference, result.StdPrecision)
                           && IsWithinTolerance(result.UnMatchedBalance, result.StdPrecision);

            result.ReconciliationState = settled ? RECON_RECONCILED : RECON_REVIEW;
        }

        // ----------------------------------------------------------------- //
        //  3. Linked configuration                                           //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Builds one configuration row per ACTIVE child tab of the hosting window,
        /// in the window's own tab order. The tab metadata is what decides a row
        /// exists at all, so a tab the environment has switched off costs nothing -
        /// neither the row nor its count query is produced.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="AD_Window_ID">Hosting window.</param>
        /// <param name="result">Payload read for currency / account-line facts.</param>
        /// <returns>Configuration rows, possibly empty; never null.</returns>
        private List<LinkedConfigRow> LoadLinkedConfigs(Ctx ctx, int C_BankAccount_ID, int AD_Window_ID,
                                                        BankAccountPanelData result)
        {
            List<LinkedConfigRow> rows = LoadLinkedTabs(ctx, AD_Window_ID);

            for (int i = 0; i < rows.Count; i++)
            {
                LinkedConfigRow row = rows[i];
                try
                {
                    FillLinkedConfig(ctx, C_BankAccount_ID, row, result);
                }
                catch (Exception ex)
                {
                    /* One configuration table missing from this environment must not
                       cost the user the other five rows, nor the rest of the panel. */
                    _log.Severe("VAS_293 FillLinkedConfig(" + row.TableName + ", "
                                + C_BankAccount_ID + "): " + ex.Message);
                    row.RecordCount = 0;
                    row.Detail = "";
                }
                row.IsConfigured = row.RecordCount > 0;
            }

            return rows;
        }

        /// <summary>
        /// Reads the ACTIVE child tabs of the hosting window whose table is one of the
        /// six configuration tables, in tab sequence. Runs without AddAccessSQL: these
        /// are AD dictionary rows at AD_Client_ID = 0 describing the window the caller
        /// already has open - the same convention GetListReferenceName uses below.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="AD_Window_ID">Hosting window.</param>
        /// <returns>One empty row per active configuration tab, in SeqNo order.</returns>
        private List<LinkedConfigRow> LoadLinkedTabs(Ctx ctx, int AD_Window_ID)
        {
            List<LinkedConfigRow> rows = new List<LinkedConfigRow>();

            /* The tab's own translated name is what the user sees on the tab header,
               so the row is labelled with it rather than with a string of ours. */
            string sql = @"SELECT t.AD_Tab_ID,
                                  t.SeqNo,
                                  COALESCE(trl.Name, t.Name) AS TabName,
                                  tbl.AD_Table_ID,
                                  tbl.TableName
                           FROM AD_Tab t
                           INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID=t.AD_Table_ID)
                           LEFT OUTER JOIN AD_Tab_Trl trl ON (trl.AD_Tab_ID=t.AD_Tab_ID
                                                              AND trl.AD_Language=@Language
                                                              AND trl.IsActive='Y')
                           WHERE t.AD_Window_ID=@AD_Window_ID
                             AND t.IsActive='Y'
                             AND tbl.IsActive='Y'
                             AND tbl.TableName IN (@TableName1,@TableName2,@TableName3,@TableName4,@TableName5,@TableName6)
                           ORDER BY t.SeqNo,t.AD_Tab_ID";

            /* SqlParameter binds by POSITION, so every occurrence gets its own name and
               the array is built in the order the names appear in the text above. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@Language", ctx.GetAD_Language()),
                new SqlParameter("@AD_Window_ID", AD_Window_ID),
                new SqlParameter("@TableName1", TBL_ACCOUNT_LINE),
                new SqlParameter("@TableName2", TBL_ACCOUNT_DOC),
                new SqlParameter("@TableName3", TBL_PAYMENT_PROCESSOR),
                new SqlParameter("@TableName4", TBL_STATEMENT_LOADER),
                new SqlParameter("@TableName5", TBL_STATEMENT_CLASS),
                new SqlParameter("@TableName6", TBL_DEFAULT_ACCOUNTING)
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(sql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_293 LoadLinkedTabs(" + AD_Window_ID + "): " + ex.Message);
                return rows;
            }

            if (ds == null || ds.Tables.Count == 0)
            {
                return rows;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                LinkedConfigRow row = new LinkedConfigRow();
                row.AD_Tab_ID = Util.GetValueOfInt(r["AD_Tab_ID"]);
                row.AD_Table_ID = Util.GetValueOfInt(r["AD_Table_ID"]);
                row.TableName = Util.GetValueOfString(r["TableName"]);
                row.TabName = Util.GetValueOfString(r["TabName"]);
                row.SeqNo = Util.GetValueOfInt(r["SeqNo"]);
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// Fills one configuration row's count and detail from its own table. Every
        /// branch reads only the columns the row displays, filtered by the current
        /// account, with MRole on that table's own alias. Credentials (password, PIN,
        /// user id, host and proxy settings) are never selected.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="row">Configuration row filled in place.</param>
        /// <param name="result">Payload, read for the account-line facts already loaded.</param>
        private void FillLinkedConfig(Ctx ctx, int C_BankAccount_ID, LinkedConfigRow row,
                                      BankAccountPanelData result)
        {
            switch (row.TableName)
            {
                case TBL_ACCOUNT_LINE:
                    /* Already read for the Statement section - no second query. The
                       detail is composed on the client, which owns date and amount
                       formatting, from the latest-line facts on the payload. */
                    row.RecordCount = result.AccountLineCount;
                    row.Detail = "";
                    break;

                case TBL_ACCOUNT_DOC:
                    FillAccountDoc(ctx, C_BankAccount_ID, row);
                    break;

                case TBL_PAYMENT_PROCESSOR:
                    FillPaymentProcessor(ctx, C_BankAccount_ID, row);
                    break;

                case TBL_STATEMENT_LOADER:
                    FillStatementLoader(ctx, C_BankAccount_ID, row);
                    break;

                case TBL_STATEMENT_CLASS:
                    FillStatementClass(ctx, C_BankAccount_ID, row);
                    break;

                case TBL_DEFAULT_ACCOUNTING:
                    FillDefaultAccounting(ctx, C_BankAccount_ID, row);
                    break;
            }
        }

        /// <summary>
        /// Bank Account Document: active count, and the first two document names as
        /// the detail ("SEPA Transfer · Cheque"), with a "+n" remainder beyond that.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="row">Configuration row filled in place.</param>
        private void FillAccountDoc(Ctx ctx, int C_BankAccount_ID, LinkedConfigRow row)
        {
            string sql = @"SELECT COALESCE(bad.Name, N'') AS Name
                           FROM C_BankAccountDoc bad
                           WHERE bad.C_BankAccount_ID=@C_BankAccount_ID
                             AND bad.IsActive='Y'
                             AND bad.AD_Client_ID=@AD_Client_ID";

            DataSet ds = ReadConfig(ctx, sql, "bad", " ORDER BY bad.Name", C_BankAccount_ID, row.TableName);
            if (ds == null || ds.Tables.Count == 0)
            {
                return;
            }

            DataRowCollection all = ds.Tables[0].Rows;
            row.RecordCount = all.Count;

            List<string> names = new List<string>();
            for (int i = 0; i < all.Count && i < DOC_NAMES_SHOWN; i++)
            {
                string name = Util.GetValueOfString(all[i]["Name"]);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
            row.Detail = string.Join(" · ", names.ToArray());

            /* "+2" tells the user the list is longer without spelling every name out. */
            int remaining = all.Count - DOC_NAMES_SHOWN;
            if (remaining > 0 && row.Detail.Length > 0)
            {
                row.Detail += " +" + remaining;
            }
        }

        /// <summary>
        /// Payment Processor: active count, and "Processor name · Currency" for the
        /// first one. Host, port, user id, password and proxy settings are never read.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="row">Configuration row filled in place.</param>
        private void FillPaymentProcessor(Ctx ctx, int C_BankAccount_ID, LinkedConfigRow row)
        {
            string sql = @"SELECT COALESCE(pp.Name, N'') AS Name,
                                  COALESCE(cur.ISO_Code, N'') AS CurrencyCode
                           FROM C_PaymentProcessor pp
                           LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=pp.C_Currency_ID)
                           WHERE pp.C_BankAccount_ID=@C_BankAccount_ID
                             AND pp.IsActive='Y'
                             AND pp.AD_Client_ID=@AD_Client_ID";

            DataSet ds = ReadConfig(ctx, sql, "pp", " ORDER BY pp.Name", C_BankAccount_ID, row.TableName);
            if (ds == null || ds.Tables.Count == 0)
            {
                return;
            }

            row.RecordCount = ds.Tables[0].Rows.Count;
            if (row.RecordCount == 0)
            {
                return;
            }

            DataRow r = ds.Tables[0].Rows[0];
            row.Detail = JoinBits(Util.GetValueOfString(r["Name"]), Util.GetValueOfString(r["CurrencyCode"]));
        }

        /// <summary>
        /// Statement Loader: active count, the loader name and its last run. The date
        /// leaves as ISO and the "never run" wording is the client's, so the detail is
        /// handed over in parts rather than as a finished sentence. Password, PIN,
        /// user id, host and proxy settings are never read.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="row">Configuration row filled in place.</param>
        private void FillStatementLoader(Ctx ctx, int C_BankAccount_ID, LinkedConfigRow row)
        {
            string sql = @"SELECT COALESCE(bsl.Name, N'') AS Name,
                                  COALESCE(bsl.StmtLoaderClass, N'') AS StmtLoaderClass,
                                  bsl.DateLastRun
                           FROM C_BankStatementLoader bsl
                           WHERE bsl.C_BankAccount_ID=@C_BankAccount_ID
                             AND bsl.IsActive='Y'
                             AND bsl.AD_Client_ID=@AD_Client_ID";

            DataSet ds = ReadConfig(ctx, sql, "bsl", " ORDER BY bsl.Name", C_BankAccount_ID, row.TableName);
            if (ds == null || ds.Tables.Count == 0)
            {
                return;
            }

            row.RecordCount = ds.Tables[0].Rows.Count;
            if (row.RecordCount == 0)
            {
                return;
            }

            DataRow r = ds.Tables[0].Rows[0];
            /* The loader class stands in when the row was saved without a name. */
            string name = Util.GetValueOfString(r["Name"]);
            row.Detail = (name.Length > 0) ? name : Util.GetValueOfString(r["StmtLoaderClass"]);
            row.DetailDate = FormatDate(Util.GetValueOfDateTime(r["DateLastRun"]));
        }

        /// <summary>
        /// Statement Class (VA012): active count and the first class name. The table
        /// belongs to an optional module, so a missing table surfaces as the caller's
        /// logged exception and the row degrades to "not configured".
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="row">Configuration row filled in place.</param>
        private void FillStatementClass(Ctx ctx, int C_BankAccount_ID, LinkedConfigRow row)
        {
            string sql = @"SELECT COALESCE(bsc.VA012_BankStatementClassName, N'') AS ClassName
                           FROM VA012_BankStatementClass bsc
                           WHERE bsc.C_BankAccount_ID=@C_BankAccount_ID
                             AND bsc.IsActive='Y'
                             AND bsc.AD_Client_ID=@AD_Client_ID";

            DataSet ds = ReadConfig(ctx, sql, "bsc", " ORDER BY bsc.VA012_BankStatementClassName",
                                    C_BankAccount_ID, row.TableName);
            if (ds == null || ds.Tables.Count == 0)
            {
                return;
            }

            row.RecordCount = ds.Tables[0].Rows.Count;
            if (row.RecordCount == 0)
            {
                return;
            }

            row.Detail = Util.GetValueOfString(ds.Tables[0].Rows[0]["ClassName"]);
        }

        /// <summary>
        /// Default Accounting (FRPT): active mapping count and the accounting book of
        /// the first mapping. Only the book name is read - the valid-combination and
        /// account-default joins add nothing the row displays.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="row">Configuration row filled in place.</param>
        private void FillDefaultAccounting(Ctx ctx, int C_BankAccount_ID, LinkedConfigRow row)
        {
            string sql = @"SELECT COALESCE(acs.Name, N'') AS AccountingBook
                           FROM FRPT_BankAccount_Acct baa
                           LEFT OUTER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID=baa.C_AcctSchema_ID)
                           WHERE baa.C_BankAccount_ID=@C_BankAccount_ID
                             AND baa.IsActive='Y'
                             AND baa.AD_Client_ID=@AD_Client_ID";

            DataSet ds = ReadConfig(ctx, sql, "baa", " ORDER BY baa.SeqNo,baa.FRPT_BankAccount_Acct_ID",
                                    C_BankAccount_ID, row.TableName);
            if (ds == null || ds.Tables.Count == 0)
            {
                return;
            }

            row.RecordCount = ds.Tables[0].Rows.Count;
            if (row.RecordCount == 0)
            {
                return;
            }

            row.Detail = Util.GetValueOfString(ds.Tables[0].Rows[0]["AccountingBook"]);
        }

        /// <summary>
        /// Runs one configuration query: MRole on its own main alias, the sort appended
        /// after the rewriter, and the two standard binds. Exceptions are left to the
        /// caller's per-row handler so one absent module table degrades a single row.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="sql">Statement up to and including its WHERE clause.</param>
        /// <param name="alias">Main physical table alias to secure.</param>
        /// <param name="orderBy">Sort clause, appended AFTER AddAccessSQL.</param>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="tableName">Table name, for the log line only.</param>
        /// <returns>Result set, or null when nothing came back.</returns>
        private DataSet ReadConfig(Ctx ctx, string sql, string alias, string orderBy,
                                   int C_BankAccount_ID, string tableName)
        {
            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + orderBy;

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankAccount_ID", C_BankAccount_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            return DB.ExecuteDataset(accessSql, param, null);
        }

        // ----------------------------------------------------------------- //
        //  Helpers                                                           //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// True when the AD dictionary knows an active column of that name on that
        /// table. Used for a column that exists in some environments only, so naming
        /// it never fails the query that would otherwise carry the whole panel. The
        /// answer is cached for the lifetime of the application - the dictionary does
        /// not change between requests.
        /// </summary>
        /// <param name="tableName">Physical table.</param>
        /// <param name="columnName">Column to look for.</param>
        /// <returns>True when the column is defined and active.</returns>
        private static bool HasColumn(string tableName, string columnName)
        {
            string key = tableName + "." + columnName;

            lock (_columnExistsLock)
            {
                bool cached;
                if (_columnExists.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            string sql = @"SELECT COUNT(1)
                           FROM AD_Column col
                           INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID=col.AD_Table_ID)
                           WHERE tbl.TableName=@TableName
                             AND col.ColumnName=@ColumnName
                             AND col.IsActive='Y'
                             AND tbl.IsActive='Y'";

            bool exists = false;
            try
            {
                /* Two binds, each occurring once, in the order they appear. */
                exists = Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null)) > 0;
            }
            catch (Exception ex)
            {
                /* Unknown means "do not name it" - the optional metric is worth less
                   than the query that carries the rest of the panel. */
                _log.Severe("VAS_293 HasColumn(" + key + "): " + ex.Message);
            }

            lock (_columnExistsLock)
            {
                _columnExists[key] = exists;
            }
            return exists;
        }

        /// <summary>
        /// Resolves the translated display name of one List-reference value from
        /// AD_Ref_List / AD_Ref_List_Trl for the session language. The AD_Column is
        /// located by TableName + ColumnName - stable across environments - rather
        /// than by a hard-coded AD_Column_ID.
        /// </summary>
        /// <param name="ctx">User context (supplies the UI language).</param>
        /// <param name="tableName">Physical table owning the column.</param>
        /// <param name="columnName">List-reference column.</param>
        /// <param name="code">Stored short code.</param>
        /// <returns>Translated display name, or the code itself when unmapped.</returns>
        private string GetListReferenceName(Ctx ctx, string tableName, string columnName, string code)
        {
            if (ctx == null || string.IsNullOrEmpty(code))
            {
                return code;
            }

            string sql = @"SELECT COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                           FROM AD_Column col
                           INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID=col.AD_Table_ID)
                           INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID=col.AD_Reference_Value_ID)
                           LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID
                                                                   AND rlt.AD_Language=@Language
                                                                   AND rlt.IsActive='Y')
                           WHERE tbl.TableName=@TableName
                             AND col.ColumnName=@ColumnName
                             AND col.IsActive='Y'
                             AND rl.IsActive='Y'
                             AND rl.Value=@Code";

            try
            {
                /* Four binds, each occurring once, in the order they appear. */
                string name = Util.GetValueOfString(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@Language", ctx.GetAD_Language()),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@Code", code)
                }, null));

                return string.IsNullOrEmpty(name) ? code : name;
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_293 GetListReferenceName(" + tableName + "." + columnName + "): " + ex.Message);
                return code;
            }
        }

        /// <summary>Joins the populated parts with the middle dot the panel uses
        /// between compound facts.</summary>
        /// <param name="first">First part; skipped when blank.</param>
        /// <param name="second">Second part; skipped when blank.</param>
        /// <returns>Joined text, possibly "".</returns>
        private static string JoinBits(string first, string second)
        {
            string a = (first == null) ? "" : first.Trim();
            string b = (second == null) ? "" : second.Trim();
            if (a.Length > 0 && b.Length > 0)
            {
                return a + " · " + b;
            }
            return (a.Length > 0) ? a : b;
        }

        /// <summary>
        /// True when the amount is zero within the currency's precision - half of the
        /// smallest representable unit (0.005 at two decimals). Decided on the decimal
        /// value, never on a formatted string.
        /// </summary>
        /// <param name="value">Amount to test.</param>
        /// <param name="precision">Currency standard precision.</param>
        /// <returns>True when |value| is within the tolerance.</returns>
        private static bool IsWithinTolerance(decimal value, int precision)
        {
            int p = (precision >= 0 && precision <= 10) ? precision : 2;
            decimal tolerance = 0.5m / (decimal)Math.Pow(10, p);
            return Math.Abs(value) <= tolerance;
        }

        /// <summary>Rounds an amount to the currency precision, away from zero.</summary>
        /// <param name="value">Raw amount.</param>
        /// <param name="precision">Currency standard precision.</param>
        /// <returns>Rounded amount.</returns>
        private static decimal Round(decimal value, int precision)
        {
            int p = (precision >= 0 && precision <= 28) ? precision : 2;
            return Math.Round(value, p, MidpointRounding.AwayFromZero);
        }

        /// <summary>Formats a nullable date as ISO yyyy-MM-dd (empty when null); the
        /// client owns the display format so the user's locale decides it.</summary>
        /// <param name="value">Date to format.</param>
        /// <returns>ISO date string, or "".</returns>
        private static string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "";
        }

        // ----------------------------------------------------------------- //
        //  Payload                                                           //
        // ----------------------------------------------------------------- //

        /// <summary>Everything the panel renders for one bank account.</summary>
        public class BankAccountPanelData
        {
            public int C_BankAccount_ID { get; set; }
            public int AD_Org_ID { get; set; }
            public string OrganizationName { get; set; }

            /// <summary>C_BankAccount.Name - the account's own display name; may be blank,
            /// in which case the panel falls back to the account number.</summary>
            public string BankAccountName { get; set; }
            public string AccountDescription { get; set; }
            /// <summary>Full number, unmasked by explicit request.</summary>
            public string AccountNo { get; set; }
            public string IBAN { get; set; }

            /// <summary>Stored BankAccountType code and its translated name.</summary>
            public string BankAccountType { get; set; }
            public string BankAccountTypeName { get; set; }

            public bool IsDefault { get; set; }
            public bool IsActive { get; set; }

            /// <summary>Account currency - drives every amount the panel shows.</summary>
            public int C_Currency_ID { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }

            public decimal CurrentBalance { get; set; }
            public decimal UnMatchedBalance { get; set; }
            public decimal CreditLimit { get; set; }
            /// <summary>Opening balance, when the environment defines the column.
            /// <see cref="HasOpenBalance"/> says whether it was read at all - the panel
            /// shows an em dash rather than a misleading zero when it was not.</summary>
            public decimal OpenBalance { get; set; }
            public bool HasOpenBalance { get; set; }

            public int C_Bank_ID { get; set; }
            public string BankName { get; set; }
            public string RoutingNo { get; set; }
            public string SwiftCode { get; set; }
            /// <summary>Bank postal address, already composed in postal order.</summary>
            public string BankAddress { get; set; }

            /// <summary>Active C_BankAccountLine rows on this account.</summary>
            public int AccountLineCount { get; set; }
            /// <summary>False when the account has no account line at all - the panel
            /// then reports "no statement" instead of a zero balance.</summary>
            public bool HasLatestLine { get; set; }
            /// <summary>ISO yyyy-MM-dd of the latest line's StatementDate.</summary>
            public string LatestStatementDate { get; set; }
            public decimal LatestEndingBalance { get; set; }
            public decimal LatestStatementDifference { get; set; }

            /// <summary>RECONCILED / REVIEW / NOSTMT - a code, never display text.</summary>
            public string ReconciliationState { get; set; }

            /// <summary>One row per ACTIVE configuration tab of the hosting window.</summary>
            public List<LinkedConfigRow> LinkedConfigs { get; set; }
        }

        /// <summary>One Linked Configuration row: an active child tab of the hosting
        /// window and what is configured behind it for this account.</summary>
        public class LinkedConfigRow
        {
            /// <summary>Target of the row's click - the tab the hosting window switches
            /// to. Resolved from the window's own metadata, never hard-coded.</summary>
            public int AD_Tab_ID { get; set; }
            public int AD_Table_ID { get; set; }
            /// <summary>Physical table; the panel keys its icon and its Account Line
            /// special case off this.</summary>
            public string TableName { get; set; }
            /// <summary>Translated tab name, exactly as the tab header reads.</summary>
            public string TabName { get; set; }
            public int SeqNo { get; set; }

            /// <summary>Active rows behind the tab for this account. Zero is a real
            /// answer - it means "not configured", not "unknown".</summary>
            public int RecordCount { get; set; }
            /// <summary>RecordCount &gt; 0.</summary>
            public bool IsConfigured { get; set; }

            /// <summary>Short configuration detail, already composed where it is pure
            /// text. Blank for Account Line, whose detail is a date and an amount the
            /// client formats from the payload's latest-line facts.</summary>
            public string Detail { get; set; }
            /// <summary>ISO yyyy-MM-dd qualifier for the detail (statement loader's last
            /// run); blank when there is none.</summary>
            public string DetailDate { get; set; }
        }
    }
}
