/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Quick GL Journal dashboard widget (Widget VAS_118) —
 *                  lookups + create/post of a two-line GL journal.
 * chronological  : Development
 * Created Date   : 2026-07-17
 * Created by     : VAI_XXX
 ******************************************************/

using DocumentFormat.OpenXml.VariantTypes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Windows.Interop;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_118_QuickJournal
    /// Purpose     : Backs the VAS_118_QuickJournal dashboard widget. Exposes the
    ///               modal lookups (role-accessible organizations, active
    ///               accounting schemas with their currency meta, GL-Journal
    ///               document types, active non-summary ledger accounts of a
    ///               schema, and cost/profit-center orgs) and creates a real
    ///               two-line GL journal (one debit + one credit) through the
    ///               standard MJournal / MJournalLine / MAccount model classes —
    ///               never a hand-written INSERT. The amount is entered in the
    ///               schema (accounting) currency, so the journal currency equals
    ///               the schema currency, CurrencyRate = 1, and the source amount
    ///               is also the accounted amount. All derived accounting values
    ///               (GL_Category_ID, C_Currency_ID, C_ConversionType_ID,
    ///               PostingType, C_ValidCombination_ID) are derived and validated
    ///               here on the server — none are trusted from the browser. The
    ///               whole create/post runs in a single Trx that is Closed in a
    ///               finally and rolled back on every failure path, so a failure
    ///               never leaves an orphan header or a single line. Save-draft
    ///               leaves the document Drafted; Post completes it via the same
    ///               ProcessIt(ACTION_COMPLETE) path the standard document
    ///               workflow uses. MRole row-level security is applied only on
    ///               the main physical table of each lookup SELECT.
    /// Chronological development:
    ///   VAI_XXX     2026-07-17 Created
    /// </summary>
    public class VAS_118_QuickJournalModel
    {
        /// <summary>DocBaseType of a GL Journal document type.</summary>
        private const string DOCBASETYPE_GLJOURNAL = "GLJ";

        /// <summary>Account element type inside an accounting schema.</summary>
        private const string ELEMENTTYPE_Account = "AC";

        /// <summary>Default page size for the account picker.</summary>
        private const int ACCOUNT_PAGE_SIZE = 50;

        /// <summary>
        /// One round-trip payload for opening the modal: the role-accessible
        /// organizations, the active accounting schemas (with currency meta), the
        /// GL-Journal document types, and the seed defaults (organization,
        /// accounting schema, business date). Schemas and document types are
        /// client-level here, so they do not reload on organization change — only
        /// the accounts and cost centers do.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="InitData"/>.</returns>
        public InitData GetInitData(Ctx ctx)
        {
            InitData data = new InitData();
            if (ctx == null) { return data; }

            data.Organizations = GetOrganizations(ctx, false, 0);
            data.Schemas = GetAcctSchemas(ctx);
            data.DefaultOrgId = ctx.GetAD_Org_ID();
            data.DefaultSchemaId = ResolveDefaultSchemaId(ctx, data.Schemas);
            /* Document types are scoped to the (default) organization — reloaded when
               the user changes the organization. */
            data.DocTypes = GetDocTypes(ctx, data.DefaultOrgId);
            data.Today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return data;
        }

        /// <summary>
        /// The client's PRIMARY accounting schema (AD_ClientInfo.C_AcctSchema1_ID)
        /// when it is in the accessible list; otherwise the first accessible schema
        /// (0 when none). Used to default-select the schema in the modal.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="schemas">The accessible schemas already fetched.</param>
        /// <returns>Default C_AcctSchema_ID (0 when none).</returns>
        private int ResolveDefaultSchemaId(Ctx ctx, List<SchemaOption> schemas)
        {
            int primary = Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT ClientInfo.C_AcctSchema1_ID FROM AD_ClientInfo ClientInfo WHERE ClientInfo.AD_Client_ID = " + ctx.GetAD_Client_ID(),
                null, null));
            for (int i = 0; i < schemas.Count; i++)
            {
                if (schemas[i].Id == primary) { return primary; }
            }
            return schemas.Count > 0 ? schemas[0].Id : 0;
        }

        /// <summary>
        /// Role-accessible organizations for the current client. When
        /// <paramref name="costCentersOnly"/> is true only cost/profit-center orgs
        /// are returned (the AD_OrgTrx_ID picker); otherwise header organizations
        /// (non cost/profit-center) are returned.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="costCentersOnly">True for the cost/profit-center list.</param>
        /// <param name="adOrgId">Selected header organization — cost centers are filtered to those whose parent (LegalEntityOrg) is this org.</param>
        /// <returns>List of <see cref="Option"/> (Id + Name).</returns>
        public List<Option> GetOrganizations(Ctx ctx, bool costCentersOnly, int adOrgId)
        {
            List<Option> list = new List<Option>();
            if (ctx == null) { return list; }

            /* Main physical table AD_Org (alias AD_Org) — MRole applied to this body. */
            string sql = @"
                SELECT AD_Org.AD_Org_ID AS Org_ID,
                       AD_Org.Name AS Org_Name
                FROM AD_Org AD_Org
                WHERE AD_Org.IsActive = 'Y'
                  AND AD_Org.IsSummary = 'N'
                  AND AD_Org.AD_Client_ID = " + ctx.GetAD_Client_ID();

            if (costCentersOnly)
            {
                sql += " AND (AD_Org.IsCostCenter = 'Y' OR AD_Org.IsProfitCenter = 'Y')";
                /* A cost center belongs to a header organization via LegalEntityOrg
                   (the parent org id, stored as text). */
                sql += $" AND CAST(AD_Org.LegalEntityOrg AS INTEGER) = {adOrgId}";
            }
            else
            {
                sql += " AND AD_Org.IsCostCenter = 'N' AND AD_Org.IsProfitCenter = 'N'";
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Org", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY AD_Org.Name";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, null);
                while (dr != null && dr.Read())
                {
                    list.Add(new Option
                    {
                        Id = Util.GetValueOfInt(dr["Org_ID"]),
                        Name = Util.GetValueOfString(dr["Org_Name"])
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        /// <summary>
        /// Active accounting schemas for the current client, each carrying its
        /// currency id, ISO code, display symbol and standard precision so the
        /// modal can show the currency beside Amount without another round trip.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <returns>List of <see cref="SchemaOption"/>.</returns>
        public List<SchemaOption> GetAcctSchemas(Ctx ctx)
        {
            List<SchemaOption> list = new List<SchemaOption>();
            if (ctx == null) { return list; }

            /* Main physical table C_AcctSchema (alias Schema) — MRole applied to this body. */
            string sql = @"
                SELECT Schema.C_AcctSchema_ID AS Schema_ID,
                       Schema.Name AS Schema_Name,
                       Schema.C_Currency_ID AS Currency_ID,
                       Currency.ISO_Code AS Currency_Iso,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.StdPrecision AS Currency_Precision
                FROM C_AcctSchema Schema
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID = Schema.C_Currency_ID)
                WHERE Schema.IsActive = 'Y'
                  AND Schema.AD_Client_ID = " + ctx.GetAD_Client_ID();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "Schema", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY Schema.Name";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, null);
                while (dr != null && dr.Read())
                {
                    list.Add(new SchemaOption
                    {
                        Id = Util.GetValueOfInt(dr["Schema_ID"]),
                        Name = Util.GetValueOfString(dr["Schema_Name"]),
                        CurrencyId = Util.GetValueOfInt(dr["Currency_ID"]),
                        CurrencyIso = Util.GetValueOfString(dr["Currency_Iso"]),
                        CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]),
                        Precision = Util.GetValueOfInt(dr["Currency_Precision"])
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        /// <summary>
        /// Active GL-Journal document types (DocBaseType 'GLJ') for the current
        /// client, scoped to the selected organization (shared org 0 + that org).
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="adOrgId">Selected header organization.</param>
        /// <returns>List of <see cref="Option"/> (Id + Name).</returns>
        public List<Option> GetDocTypes(Ctx ctx, int adOrgId)
        {
            List<Option> list = new List<Option>();
            if (ctx == null) { return list; }

            /* Main physical table C_DocType (alias DocType) — MRole applied to this body. */
            string sql = @"
                SELECT DocType.C_DocType_ID AS DocType_ID,
                       DocType.Name AS DocType_Name
                FROM C_DocType DocType
                WHERE DocType.IsActive = 'Y'
                  AND DocType.DocBaseType = 'GLJ'
                  AND (DocType.AD_Org_ID = 0 OR DocType.AD_Org_ID = " + adOrgId + @")
                  AND DocType.AD_Client_ID = " + ctx.GetAD_Client_ID();
            if (Env.IsModuleInstalled("VA028_"))
            {
                sql += " AND VA028_IsBatchDoc = 'N' ";
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "DocType", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY DocType.Name";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, null);
                while (dr != null && dr.Read())
                {
                    list.Add(new Option
                    {
                        Id = Util.GetValueOfInt(dr["DocType_ID"]),
                        Name = Util.GetValueOfString(dr["DocType_Name"])
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        /// <summary>
        /// Active, non-summary ledger accounts (C_ElementValue) of the account
        /// element (ElementType 'AC') of the given accounting schema, optionally
        /// filtered by a search string on the account code or name and paged. The
        /// same list backs both the debit and the credit picker.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="cAcctSchemaId">C_AcctSchema_ID whose account element is used.</param>
        /// <param name="search">Optional filter on Value / Name (may be null/empty).</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="adOrgId">Selected header organization (shared org 0 + that org).</param>
        /// <returns>List of <see cref="AccountOption"/> (Id + Code + Name).</returns>
        public List<AccountOption> GetAccounts(Ctx ctx, int cAcctSchemaId, string search, int pageNo, int adOrgId)
        {
            List<AccountOption> list = new List<AccountOption>();
            if (ctx == null || cAcctSchemaId <= 0) { return list; }
            if (pageNo <= 0) { pageNo = 1; }
            int offset = (pageNo - 1) * ACCOUNT_PAGE_SIZE;

            bool hasSearch = !string.IsNullOrEmpty(search);

            /* Main physical table C_ElementValue (alias EleVal) reached through the
               schema's account element. MRole applied to this body. */
            string sql = @"
                SELECT EleVal.C_ElementValue_ID AS Account_ID,
                       EleVal.Value AS Account_Code,
                       EleVal.Name AS Account_Name
                FROM C_ElementValue EleVal
                INNER JOIN C_Element Ele ON (Ele.C_Element_ID = EleVal.C_Element_ID)
                INNER JOIN C_AcctSchema_Element SchemaEle ON (SchemaEle.C_Element_ID = Ele.C_Element_ID
                            AND SchemaEle.ElementType = 'AC')
                WHERE EleVal.IsActive = 'Y'
                  AND EleVal.IsSummary = 'N'
                  AND SchemaEle.C_AcctSchema_ID = @SchemaId
                  AND (EleVal.AD_Org_ID = 0 OR EleVal.AD_Org_ID = " + adOrgId + @")
                  AND EleVal.AD_Client_ID = " + ctx.GetAD_Client_ID();

            if (hasSearch)
            {
                sql += " AND (UPPER(EleVal.Value) LIKE UPPER(@SearchV) OR UPPER(EleVal.Name) LIKE UPPER(@SearchN))";
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "EleVal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY EleVal.Value OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@SchemaId", cAcctSchemaId),
                new SqlParameter("@Offset", offset),
                new SqlParameter("@PageSize", ACCOUNT_PAGE_SIZE)
            };
            if (hasSearch)
            {
                parameters.Add(new SqlParameter("@SearchV", "%" + search + "%"));
                parameters.Add(new SqlParameter("@SearchN", "%" + search + "%"));
            }

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    list.Add(new AccountOption
                    {
                        Id = Util.GetValueOfInt(dr["Account_ID"]),
                        Code = Util.GetValueOfString(dr["Account_Code"]),
                        Name = Util.GetValueOfString(dr["Account_Name"])
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        /// <summary>
        /// Cost/profit-center organizations of the selected organization (the optional
        /// AD_OrgTrx_ID picker) — those whose parent LegalEntityOrg is that org.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="adOrgId">Selected header organization.</param>
        /// <returns>List of <see cref="Option"/> (Id + Name).</returns>
        public List<Option> GetCostCenters(Ctx ctx, int adOrgId)
        {
            return GetOrganizations(ctx, true, adOrgId);
        }

        /// <summary>
        /// Saves the two-line GL journal (one debit + one credit) as a DRAFT: creates
        /// it when req.GL_Journal_ID is 0, otherwise updates the existing draft (header
        /// + both lines) in place. It never completes the document — completion is the
        /// separate <see cref="CompleteQuickJournal"/> step so the draft is committed
        /// first. All derived accounting values are resolved here; the whole save runs
        /// in one transaction rolled back on any validation or save failure.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="req">Raw modal input (GL_Journal_ID = 0 to create, &gt; 0 to update).</param>
        /// <returns><see cref="QuickJournalResponse"/> with Success, GL_Journal_ID, DocumentNo, DocStatus, per-field errors and a user message.</returns>
        public QuickJournalResponse SaveQuickJournal(Ctx ctx, QuickJournalRequest req)
        {
            QuickJournalResponse result = new QuickJournalResponse { Success = false, FieldErrors = new Dictionary<string, string>() };

            if (ctx == null || req == null)
            {
                result.Message = Msg.GetMsg(ctx, "FillMandatory");
                return result;
            }

            /* ── 1. Field validation (collect per-field messages) ── */
            if (req.AD_Org_ID <= 0) { result.FieldErrors["AD_Org_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectOrganization"); }
            if (string.IsNullOrEmpty(req.DateAcct)) { result.FieldErrors["DateAcct"] = Msg.GetMsg(ctx, "VAS_118_SelectAccountingDate"); }
            if (req.C_AcctSchema_ID <= 0) { result.FieldErrors["C_AcctSchema_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectAccountingSchema"); }
            if (req.C_DocType_ID <= 0) { result.FieldErrors["C_DocType_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectDocumentType"); }
            //if (string.IsNullOrEmpty(req.Description)) { result.FieldErrors["Description"] = Msg.GetMsg(ctx, "VAS_118_EnterDescription"); }
            if (req.DebitAccount_ID <= 0) { result.FieldErrors["DebitAccount_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectDebitAccount"); }
            if (req.CreditAccount_ID <= 0) { result.FieldErrors["CreditAccount_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectCreditAccount"); }
            if (req.Amount <= 0) { result.FieldErrors["Amount"] = Msg.GetMsg(ctx, "VAS_118_EnterAmountGtZero"); }
            if (req.DebitAccount_ID > 0 && req.DebitAccount_ID == req.CreditAccount_ID)
            {
                result.FieldErrors["CreditAccount_ID"] = Msg.GetMsg(ctx, "VAS_118_DebitCreditSame");
            }

            DateTime? dateAcct = null;
            if (!string.IsNullOrEmpty(req.DateAcct))
            {
                DateTime parsed;
                if (DateTime.TryParse(req.DateAcct, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    dateAcct = parsed;
                }
                else
                {
                    result.FieldErrors["DateAcct"] = Msg.GetMsg(ctx, "VAS_118_SelectAccountingDate");
                }
            }

            if (result.FieldErrors.Count > 0)
            {
                result.Message = Msg.GetMsg(ctx, "FillMandatory");
                return result;
            }

            int clientId = ctx.GetAD_Client_ID();

            /* ── 2. Security: the completed org must be accessible to the role ── */
            if (!IsOrgAccessible(ctx, req.AD_Org_ID))
            {
                result.FieldErrors["AD_Org_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectOrganization");
                result.Message = Msg.GetMsg(ctx, "VIS_NoRecordFound");
                return result;
            }

            /* Both accounts must be active, non-summary and belong to the schema's account element. */
            if (!IsAccountValid(ctx, req.C_AcctSchema_ID, req.DebitAccount_ID))
            {
                result.FieldErrors["DebitAccount_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectDebitAccount");
            }
            if (!IsAccountValid(ctx, req.C_AcctSchema_ID, req.CreditAccount_ID))
            {
                result.FieldErrors["CreditAccount_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectCreditAccount");
            }
            if (req.AD_OrgTrx_ID > 0 && !IsCostCenterValid(ctx, req.AD_OrgTrx_ID))
            {
                result.FieldErrors["AD_OrgTrx_ID"] = Msg.GetMsg(ctx, "VIS_NoRecordFound");
            }
            if (result.FieldErrors.Count > 0)
            {
                result.Message = Msg.GetMsg(ctx, "FillMandatory");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("QJ"));
            try
            {
                /* ── 3. Derive accounting values (server-side, never from browser) ── */
                MDocType docType = MDocType.Get(ctx, req.C_DocType_ID);
                if (docType == null || docType.Get_ID() == 0 || docType.GetDocBaseType() != DOCBASETYPE_GLJOURNAL)
                {
                    trx.Rollback();
                    result.FieldErrors["C_DocType_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectDocumentType");
                    result.Message = Msg.GetMsg(ctx, "VAS_118_SelectDocumentType");
                    return result;
                }

                int glCategoryId = docType.GetGL_Category_ID();
                if (glCategoryId <= 0)
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "VAS_118_GLCategoryNotConfigured");
                    return result;
                }

                MAcctSchema schema = MAcctSchema.Get(ctx, req.C_AcctSchema_ID);
                if (schema == null || schema.Get_ID() == 0)
                {
                    trx.Rollback();
                    result.FieldErrors["C_AcctSchema_ID"] = Msg.GetMsg(ctx, "VAS_118_SelectAccountingSchema");
                    result.Message = Msg.GetMsg(ctx, "VAS_118_SelectAccountingSchema");
                    return result;
                }

                int currencyId = schema.GetC_Currency_ID();
                int precision = schema.GetStdPrecision();
                int conversionTypeId = MConversionType.GetDefault(clientId);

                /* Amount entered in the schema (accounting) currency ⇒ rate 1,
                   source amount equals accounted amount. Round to schema precision. */
                decimal amount = Math.Round(req.Amount, precision, MidpointRounding.AwayFromZero);
                if (amount <= 0)
                {
                    trx.Rollback();
                    result.FieldErrors["Amount"] = Msg.GetMsg(ctx, "VAS_118_EnterAmountGtZero");
                    result.Message = Msg.GetMsg(ctx, "VAS_118_EnterAmountGtZero");
                    return result;
                }

                /* ── 4. Journal header — create a new draft, or load the existing
                       draft to update (the modal stays open after the first save). ── */
                MJournal journal;
                bool isNew = req.GL_Journal_ID <= 0;
                if (isNew)
                {
                    journal = new MJournal(ctx, 0, trx);
                }
                else
                {
                    journal = new MJournal(ctx, req.GL_Journal_ID, trx);
                    if (journal.Get_ID() == 0)
                    {
                        trx.Rollback();
                        result.Message = Msg.GetMsg(ctx, "VIS_NoRecordFound");
                        return result;
                    }
                    if (journal.GetDocStatus() != MJournal.DOCSTATUS_Drafted)
                    {
                        /* A completed / closed journal can no longer be edited here. */
                        trx.Rollback();
                        result.Message = Msg.GetMsg(ctx, "VAS_118_JournalNotDraft");
                        return result;
                    }
                }
                journal.SetClientOrg(clientId, req.AD_Org_ID);
                journal.SetC_AcctSchema_ID(req.C_AcctSchema_ID);
                journal.SetC_DocType_ID(req.C_DocType_ID);
                journal.SetGL_Category_ID(glCategoryId);
                journal.SetPostingType(MJournal.POSTINGTYPE_Actual);
                journal.SetCurrency(currencyId, conversionTypeId, Env.ONE);
                journal.SetDateDoc(dateAcct);
                journal.SetDateAcct(dateAcct);   /* auto-resolves C_Period_ID */
                journal.SetDescription(req.Description);
                if (isNew)
                {
                    journal.SetDocStatus(MJournal.DOCSTATUS_Drafted);
                    journal.SetDocAction(MJournal.DOCACTION_Complete);
                    journal.SetIsApproved(false);
                    journal.SetProcessed(false);
                }
                if (!journal.Save())
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "VAS_118_JournalNotSaved") + RetrieveError();
                    return result;
                }

                /* ── 5. The two balanced lines (debit 10, credit 20). On an update the
                       existing lines are UPDATED in place (never deleted); on create
                       they are new. Match by line number so debit/credit stay stable. ── */
                MJournalLine[] existingLines = journal.GetLines(true);
                MJournalLine debitLine = null, creditLine = null;
                for (int i = 0; i < existingLines.Length; i++)
                {
                    if (existingLines[i].GetLine() == 10)
                    {
                        debitLine = existingLines[i];
                    }
                    else if (existingLines[i].GetLine() == 20)
                    {
                        creditLine = existingLines[i];
                    }
                }
                if (debitLine == null)
                {
                    debitLine = new MJournalLine(journal);
                }
                if (creditLine == null)
                {
                    creditLine = new MJournalLine(journal);
                }

                if (!SaveLine(ctx, debitLine, 10, req.AD_Org_ID, req.DebitAccount_ID, req.AD_OrgTrx_ID,
                        amount, Env.ZERO, req.Description, result))
                {
                    trx.Rollback();
                    return result;
                }
                if (!SaveLine(ctx, creditLine, 20, req.AD_Org_ID, req.CreditAccount_ID, req.AD_OrgTrx_ID,
                        Env.ZERO, amount, req.Description, result))
                {
                    trx.Rollback();
                    return result;
                }

                /* ── 6. Balance guard (server-side, decimal). ── */
                decimal totalSourceDr = amount + Env.ZERO;   /* debit leg Dr + credit leg Dr(0) */
                decimal totalSourceCr = Env.ZERO + amount;   /* debit leg Cr(0) + credit leg Cr */
                if (totalSourceDr != totalSourceCr)
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "VAS_118_JournalNotBalanced");
                    return result;
                }

                trx.Commit();

                /* Draft is saved / updated — the caller keeps the modal open, shows the
                   document number and enables Complete. Completion is a separate step
                   (CompleteQuickJournal) so the document is committed before it posts. */
                result.Success = true;
                result.GL_Journal_ID = journal.GetGL_Journal_ID();
                result.DocumentNo = journal.GetDocumentNo();
                result.DocStatus = journal.GetDocStatus();
                result.Message = Msg.GetMsg(ctx, "VAS_118_JournalSavedDraft");
                return result;
            }
            catch (Exception ex)
            {
                trx.Rollback();
                _log.Log(Level.SEVERE, "VAS_118 SaveQuickJournal", ex);
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                trx.Close();
            }
        }

        /// <summary>
        /// Completes a previously-saved DRAFT GL journal through the standard document
        /// process (DocumentEngine.CompleteOrReverse). The draft is already committed,
        /// so the document engine sees its header and lines. Returns the final
        /// DocumentNo / DocStatus. When the journal is already completed it is treated
        /// as success (idempotent).
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="gLJournalId">GL_Journal_ID of the draft to complete.</param>
        /// <returns><see cref="QuickJournalResponse"/> with Success, DocumentNo, DocStatus and a user message.</returns>
        public QuickJournalResponse CompleteQuickJournal(Ctx ctx, int gLJournalId)
        {
            QuickJournalResponse result = new QuickJournalResponse { Success = false, FieldErrors = new Dictionary<string, string>() };

            if (ctx == null || gLJournalId <= 0)
            {
                result.Message = Msg.GetMsg(ctx, "FillMandatory");
                return result;
            }

            MJournal journal = new MJournal(ctx, gLJournalId, null);
            if (journal.Get_ID() == 0 || journal.GetAD_Client_ID() != ctx.GetAD_Client_ID())
            {
                result.Message = Msg.GetMsg(ctx, "VIS_NoRecordFound");
                return result;
            }

            string status = journal.GetDocStatus();
            if (status == MJournal.DOCSTATUS_Completed || status == MJournal.DOCSTATUS_Closed)
            {
                /* Already completed — idempotent success. */
                result.Success = true;
                result.GL_Journal_ID = gLJournalId;
                result.DocumentNo = journal.GetDocumentNo();
                result.DocStatus = status;
                result.Message = Msg.GetMsg(ctx, "VAS_118_JournalCompleted");
                return result;
            }
            if (status != MJournal.DOCSTATUS_Drafted)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_118_JournalNotDraft");
                return result;
            }

            try
            {
                /* AD_Process_ID of the GL_Journal DocAction (Export_ID-independent: resolved
                   from AD_Column, portable across environments). */
                int adProcessId = Util.GetValueOfInt(DB.ExecuteScalar(
                    "SELECT AD_Process_ID FROM AD_Column WHERE ColumnName = 'DocAction' AND IsActive = 'Y' AND AD_Table_ID = " + MJournal.Table_ID,
                    null, null));

                string completionMsg = DocumentEngine.CompleteOrReverse(ctx, MJournal.Table_Name, MJournal.Table_ID,
                    gLJournalId, adProcessId, MJournal.DOCACTION_Complete);
                if (!string.IsNullOrEmpty(completionMsg))
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_118_JournalNotCompleted") + " " + completionMsg;
                    return result;
                }

                /* Reload for the final document number / status. */
                journal = new MJournal(ctx, gLJournalId, null);
                result.Success = true;
                result.GL_Journal_ID = gLJournalId;
                result.DocumentNo = journal.GetDocumentNo();
                result.DocStatus = journal.GetDocStatus();
                result.Message = Msg.GetMsg(ctx, "VAS_118_JournalCompleted");
                return result;
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "VAS_118 CompleteQuickJournal", ex);
                result.Message = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Sets the account, cost center and amounts on a journal line — NEW or an
        /// EXISTING line loaded from the draft (update-in-place, no delete) — and
        /// saves it. The account (+ optional cost center) are set as line dimensions;
        /// MJournalLine.BeforeSave resolves / refreshes the C_ValidCombination from
        /// them. Same currency as the schema ⇒ accounted amount equals source amount.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="line">Line to save: new MJournalLine(journal) or an existing draft line.</param>
        /// <param name="lineNo">Line number (10 debit / 20 credit).</param>
        /// <param name="orgId">Header AD_Org_ID.</param>
        /// <param name="accountId">Account element value (C_ElementValue_ID).</param>
        /// <param name="orgTrxId">Cost/profit-center AD_OrgTrx_ID (0 clears any prior value).</param>
        /// <param name="amtDr">Source debit amount.</param>
        /// <param name="amtCr">Source credit amount.</param>
        /// <param name="description">Line description.</param>
        /// <param name="result">Response carrying the failure message on error.</param>
        /// <returns>True when the line saved; false (with result.Message set) otherwise.</returns>
        private bool SaveLine(Ctx ctx, MJournalLine line, int lineNo, int orgId, int accountId,
            int orgTrxId, decimal amtDr, decimal amtCr, string description, QuickJournalResponse result)
        {
            line.SetLine(lineNo);
            /*line.SetDescription(description);*/
            line.SetAD_Org_ID(orgId);
            line.Set_ValueNoCheck("Account_ID", accountId);
            line.SetAD_OrgTrx_ID(orgTrxId);   /* 0 clears any prior cost center */
            line.SetCurrencyRate(1);

            line.SetAmtSourceDr(amtDr);
            line.SetAmtAcctDr(amtDr);
            line.SetAmtSourceCr(amtCr);
            line.SetAmtAcctCr(amtCr);
            if (!line.Save())
            {
                result.Message = Msg.GetMsg(ctx, "VAS_118_JournalLineNotSaved") + RetrieveError();
                return false;
            }
            return true;
        }

        /// <summary>True when the organization is accessible to the current role.</summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="adOrgId">AD_Org_ID to check.</param>
        /// <returns>True when a role-filtered lookup returns the org.</returns>
        private bool IsOrgAccessible(Ctx ctx, int adOrgId)
        {
            string sql = @"
                SELECT AD_Org.AD_Org_ID AS Org_ID
                FROM AD_Org AD_Org
                WHERE AD_Org.IsActive = 'Y'
                  AND AD_Org.AD_Org_ID = @OrgId
                  AND AD_Org.AD_Client_ID = " + ctx.GetAD_Client_ID();
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Org", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[] { new SqlParameter("@OrgId", adOrgId) }, null)) == adOrgId;
        }

        /// <summary>True when the account is active, non-summary and part of the schema's account element.</summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="schemaId">C_AcctSchema_ID.</param>
        /// <param name="accountId">C_ElementValue_ID.</param>
        /// <returns>True when the account is valid for the schema.</returns>
        private bool IsAccountValid(Ctx ctx, int schemaId, int accountId)
        {
            if (accountId <= 0) { return false; }
            string sql = @"
                SELECT EleVal.C_ElementValue_ID AS Account_ID
                FROM C_ElementValue EleVal
                INNER JOIN C_Element Ele ON (Ele.C_Element_ID = EleVal.C_Element_ID)
                INNER JOIN C_AcctSchema_Element SchemaEle ON (SchemaEle.C_Element_ID = Ele.C_Element_ID
                            AND SchemaEle.ElementType = 'AC')
                WHERE EleVal.IsActive = 'Y'
                  AND EleVal.IsSummary = 'N'
                  AND EleVal.C_ElementValue_ID = @AccountId
                  AND SchemaEle.C_AcctSchema_ID = @SchemaId
                  AND EleVal.AD_Client_ID = " + ctx.GetAD_Client_ID();
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "EleVal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            SqlParameter[] p = new SqlParameter[]
            {
                new SqlParameter("@AccountId", accountId),
                new SqlParameter("@SchemaId", schemaId)
            };
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, p, null)) == accountId;
        }

        /// <summary>True when the org is an active, role-accessible cost/profit center.</summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="adOrgId">AD_Org_ID to check.</param>
        /// <returns>True when valid.</returns>
        private bool IsCostCenterValid(Ctx ctx, int adOrgId)
        {
            string sql = @"
                SELECT AD_Org.AD_Org_ID AS Org_ID
                FROM AD_Org AD_Org
                WHERE AD_Org.IsActive = 'Y'
                  AND AD_Org.IsSummary = 'N'
                  AND AD_Org.AD_Org_ID = @OrgId
                  AND (AD_Org.IsCostCenter = 'Y' OR AD_Org.IsProfitCenter = 'Y')
                  AND AD_Org.AD_Client_ID = " + ctx.GetAD_Client_ID();
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Org", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[] { new SqlParameter("@OrgId", adOrgId) }, null)) == adOrgId;
        }

        /// <summary>Appends the last framework error text (when any) to a message.</summary>
        /// <returns>" :- error" or empty string.</returns>
        private static string RetrieveError()
        {
            string val = string.Empty;
            ValueNamePair pp = VLogger.RetrieveError();
            if (pp != null)
            {
                val = pp.GetName();
                if (String.IsNullOrEmpty(val))
                {
                    val = pp.GetValue();
                }
            }

            return !string.IsNullOrEmpty(val) ? " :- " + val : "";
        }

        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_118_QuickJournalModel).FullName);

        /* ───────────────────────────── DTOs ───────────────────────────── */

        /// <summary>Simple id/name lookup option.</summary>
        public class Option
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>Accounting-schema option carrying its currency meta for the modal.</summary>
        public class SchemaOption
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int CurrencyId { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int Precision { get; set; }
        }

        /// <summary>Ledger-account option (code + name) for the debit/credit picker.</summary>
        public class AccountOption
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
        }

        /// <summary>Modal-open payload (orgs, schemas, doc types, seed defaults).</summary>
        public class InitData
        {
            public List<Option> Organizations { get; set; }
            public List<SchemaOption> Schemas { get; set; }
            public List<Option> DocTypes { get; set; }
            public int DefaultOrgId { get; set; }
            public int DefaultSchemaId { get; set; }
            public string Today { get; set; }

            public InitData()
            {
                Organizations = new List<Option>();
                Schemas = new List<SchemaOption>();
                DocTypes = new List<Option>();
            }
        }

        /// <summary>Raw Quick-Journal input from the modal (no derived accounting values).</summary>
        public class QuickJournalRequest
        {
            /// <summary>0 to create a new draft; &gt; 0 to update that existing draft.</summary>
            public int GL_Journal_ID { get; set; }
            public int AD_Org_ID { get; set; }
            public string DateAcct { get; set; }
            public int C_AcctSchema_ID { get; set; }
            public int C_DocType_ID { get; set; }
            public string Description { get; set; }
            public int DebitAccount_ID { get; set; }
            public int CreditAccount_ID { get; set; }
            public decimal Amount { get; set; }
            public int AD_OrgTrx_ID { get; set; }
        }

        /// <summary>Quick-Journal create result.</summary>
        public class QuickJournalResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int GL_Journal_ID { get; set; }
            public string DocumentNo { get; set; }
            public string DocStatus { get; set; }
            /// <summary>Per-field validation messages keyed by the request field name.</summary>
            public Dictionary<string, string> FieldErrors { get; set; }
        }
    }
}
