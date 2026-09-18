/// <summary>
/// Module Name : VASLogic
/// Purpose     : Journal details right panel data (read side). Returns a
///               contextual, read-only summary of the selected GL_Journal record
///               for the VAS.VAS_291_GLJournalRightPanel tab panel:
///
///                 - the journal header (document, description, document /
///                   posting status, dates, totals, currency + conversion rate,
///                   accounting schema, period + fiscal year, batch, category),
///                 - the period-control state of the journal's period for the
///                   GL Journal document base type (open / closed / never opened),
///                 - the journal lines with the account and the accounting
///                   dimensions resolved through C_ValidCombination — the
///                   combination is the authority; the optional dimension
///                   columns on GL_JournalLine itself are never read,
///                 - the human workflow steps recorded against the record
///                   (AD_WF_Process -> AD_WF_Activity -> AD_WF_Node, user-choice /
///                   window / form nodes only), and
///                 - the moment the journal was posted (earliest Fact_Acct row).
///
///               The panel composes the Approval routing step flow on the client
///               from these facts; this model only reports them. Every section is
///               its own small query — one combined statement would multiply rows
///               across the lines and the workflow activities.
///
///               MRole access filtering is applied to the MAIN physical table of
///               each query — the alias the user actually fetches from — and never
///               to a lookup join. ORDER BY is appended AFTER AddAccessSQL, because
///               the rewriter appends its predicate to the end of the WHERE clause
///               it is given. The last JOIN of every statement keeps a plain ON
///               (no function call), which the access parser needs.
///
///               Portability: COALESCE / CASE / ANSI joins only — no NVL, DECODE,
///               ROWNUM, LIMIT or FETCH FIRST — so one statement serves both
///               Oracle and PostgreSQL. Free-text literals carry the
///               national-character prefix; stored code comparisons
///               (IsActive='Y', DocBaseType='GLJ') deliberately do not.
///
///               List-reference columns (DocStatus, Posted, GAAP, PeriodStatus,
///               WFState, PostingType) are resolved to their translated names
///               through AD_Ref_List / AD_Ref_List_Trl; the panel never renders
///               a raw code.
/// Chronological development:
///   VAI145   2026-09-18  Created.
///   VAI145   2026-09-18  Lines paged on the server (LINES_PAGE_SIZE = 50) through
///                        GetJournalLines; count + Dr / Cr totals from one aggregate.
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
    public class VAS_291_GLJournalRightPanelModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_291_GLJournalRightPanelModel).FullName);

        /// <summary>Document base type of a GL Journal on C_PeriodControl.</summary>
        private const string DOCBASETYPE_GLJOURNAL = "GLJ";

        /// <summary>Journal lines per page (server-side paged). The initial payload
        /// carries page 0; the panel asks for further pages through GetJournalLines.</summary>
        public const int LINES_PAGE_SIZE = 50;

        // ----------------------------------------------------------------- //
        //  Entry point                                                       //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Returns the full panel payload for the selected journal. The journal
        /// row itself is read under MRole, so an id the browser sent for a record
        /// the role cannot see comes back as an empty payload — the client-supplied
        /// id is never trusted on its own.
        /// </summary>
        /// <param name="ctx">User context (client / org / role / language).</param>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <returns>Populated <see cref="JournalPanelData"/>; an empty instance
        /// (GL_Journal_ID = 0) when the id is invalid or no accessible row exists.</returns>
        public JournalPanelData GetJournalOverview(Ctx ctx, int GL_Journal_ID)
        {
            JournalPanelData result = new JournalPanelData();
            result.Lines = new List<JournalLineRow>();
            result.WorkflowSteps = new List<WorkflowStepRow>();

            if (ctx == null || GL_Journal_ID <= 0)
            {
                return result;
            }

            if (!LoadJournal(ctx, GL_Journal_ID, result))
            {
                return result;   // not accessible to this role, or does not exist
            }

            LoadPeriodControl(ctx, result);

            /* Lines page on the server: the count and the Dr / Cr totals cover EVERY
               line and come from one aggregate, the rows are the first page only.
               Further pages arrive through GetJournalLines. */
            LoadLineTotals(ctx, GL_Journal_ID, result);
            result.LinesPageSize = LINES_PAGE_SIZE;
            result.Lines = LoadLines(ctx, GL_Journal_ID, 0, LINES_PAGE_SIZE);

            result.WorkflowSteps = LoadWorkflowSteps(ctx, GL_Journal_ID);

            if (result.Posted == "Y")
            {
                result.PostedOn = LoadPostedOn(ctx, GL_Journal_ID);
            }

            return result;
        }

        // ----------------------------------------------------------------- //
        //  1. Journal header                                                 //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reads the journal identity, status, dates, totals, currency, accounting
        /// schema, period, batch and category. This is the query that decides
        /// whether the caller may see the record at all, so MRole is applied here,
        /// on GL_Journal.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <param name="result">Payload filled in place.</param>
        /// <returns>True when an accessible row was found.</returns>
        private bool LoadJournal(Ctx ctx, int GL_Journal_ID, JournalPanelData result)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(@"SELECT jnl.GL_Journal_ID,
                                jnl.AD_Client_ID,
                                jnl.AD_Org_ID,
                                org.Name AS OrgName,
                                jnl.DocumentNo,
                                COALESCE(jnl.Description, N'') AS Description,
                                jnl.DocStatus,
                                jnl.DocAction,
                                jnl.Posted,
                                COALESCE(jnl.IsApproved, 'N') AS IsApproved,
                                jnl.Processed,
                                jnl.PostingType,
                                jnl.DateAcct,
                                jnl.DateDoc,
                                jnl.TotalDr,
                                jnl.TotalCr,
                                jnl.ControlAmt,
                                jnl.CurrencyRate,
                                jnl.C_Currency_ID,
                                cur.ISO_Code AS CurISO,
                                CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS CurSymbol,
                                cur.StdPrecision,
                                jnl.C_AcctSchema_ID,
                                sch.Name AS AcctSchemaName,
                                sch.GAAP,
                                acur.ISO_Code AS AcctCurISO,
                                jnl.C_Period_ID,
                                per.Name AS PeriodName,
                                per.StartDate AS PeriodStartDate,
                                per.EndDate AS PeriodEndDate,
                                yr.FiscalYear,
                                jnl.GL_JournalBatch_ID,
                                bat.DocumentNo AS BatchDocumentNo,
                                jnl.GL_Category_ID,
                                cat.Name AS CategoryName,
                                dt.Name AS DocTypeName,
                                jnl.Created,
                                cu.Name AS CreatedByName,
                                jnl.Updated,
                                uu.Name AS UpdatedByName
                         FROM GL_Journal jnl
                         INNER JOIN AD_Org org ON (org.AD_Org_ID=jnl.AD_Org_ID)
                         INNER JOIN C_Currency cur ON (cur.C_Currency_ID=jnl.C_Currency_ID)
                         INNER JOIN C_AcctSchema sch ON (sch.C_AcctSchema_ID=jnl.C_AcctSchema_ID)
                         INNER JOIN C_Currency acur ON (acur.C_Currency_ID=sch.C_Currency_ID)
                         INNER JOIN C_Period per ON (per.C_Period_ID=jnl.C_Period_ID)
                         INNER JOIN C_Year yr ON (yr.C_Year_ID=per.C_Year_ID)
                         INNER JOIN GL_Category cat ON (cat.GL_Category_ID=jnl.GL_Category_ID)
                         INNER JOIN C_DocType dt ON (dt.C_DocType_ID=jnl.C_DocType_ID)
                         INNER JOIN AD_User cu ON (cu.AD_User_ID=jnl.CreatedBy)
                         INNER JOIN AD_User uu ON (uu.AD_User_ID=jnl.UpdatedBy)
                         LEFT OUTER JOIN GL_JournalBatch bat ON (bat.GL_JournalBatch_ID=jnl.GL_JournalBatch_ID)
                         WHERE jnl.GL_Journal_ID=@GL_Journal_ID
                           AND jnl.IsActive='Y'
                           AND jnl.AD_Client_ID=@AD_Client_ID");

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "jnl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@GL_Journal_ID", GL_Journal_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_291 LoadJournal(" + GL_Journal_ID + "): " + ex.Message);
                return false;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return false;
            }

            DataRow r = ds.Tables[0].Rows[0];

            result.GL_Journal_ID = Util.GetValueOfInt(r["GL_Journal_ID"]);
            result.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            result.OrgName = Util.GetValueOfString(r["OrgName"]);
            result.DocumentNo = Util.GetValueOfString(r["DocumentNo"]);
            result.Description = Util.GetValueOfString(r["Description"]);

            result.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            result.DocAction = Util.GetValueOfString(r["DocAction"]);
            result.Posted = Util.GetValueOfString(r["Posted"]);
            result.IsApproved = Util.GetValueOfString(r["IsApproved"]) == "Y";
            result.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
            result.PostingType = Util.GetValueOfString(r["PostingType"]);
            /* List-reference columns are stored as short codes; the panel must never
               render the raw code, so the dictionary resolves the display name. */
            result.DocStatusName = GetListReferenceName(ctx, "GL_Journal", "DocStatus", result.DocStatus);
            result.PostedName = GetListReferenceName(ctx, "GL_Journal", "Posted", result.Posted);
            result.PostingTypeName = GetListReferenceName(ctx, "GL_Journal", "PostingType", result.PostingType);

            result.DateAcct = FormatDate(Util.GetValueOfDateTime(r["DateAcct"]));
            result.DateDoc = FormatDate(Util.GetValueOfDateTime(r["DateDoc"]));

            result.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
            result.TotalDr = Round(Util.GetValueOfDecimal(r["TotalDr"]), result.StdPrecision);
            result.TotalCr = Round(Util.GetValueOfDecimal(r["TotalCr"]), result.StdPrecision);
            result.ControlAmt = Round(Util.GetValueOfDecimal(r["ControlAmt"]), result.StdPrecision);
            result.CurrencyRate = Util.GetValueOfDecimal(r["CurrencyRate"]);

            result.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.CurISO = Util.GetValueOfString(r["CurISO"]);
            result.CurSymbol = Util.GetValueOfString(r["CurSymbol"]);

            result.C_AcctSchema_ID = Util.GetValueOfInt(r["C_AcctSchema_ID"]);
            result.AcctSchemaName = Util.GetValueOfString(r["AcctSchemaName"]);
            result.GAAP = Util.GetValueOfString(r["GAAP"]);
            result.GAAPName = GetListReferenceName(ctx, "C_AcctSchema", "GAAP", result.GAAP);
            result.AcctCurISO = Util.GetValueOfString(r["AcctCurISO"]);

            result.C_Period_ID = Util.GetValueOfInt(r["C_Period_ID"]);
            result.PeriodName = Util.GetValueOfString(r["PeriodName"]);
            result.PeriodStartDate = FormatDate(Util.GetValueOfDateTime(r["PeriodStartDate"]));
            result.PeriodEndDate = FormatDate(Util.GetValueOfDateTime(r["PeriodEndDate"]));
            result.FiscalYear = Util.GetValueOfString(r["FiscalYear"]);

            result.GL_JournalBatch_ID = Util.GetValueOfInt(r["GL_JournalBatch_ID"]);
            result.BatchDocumentNo = Util.GetValueOfString(r["BatchDocumentNo"]);
            result.GL_Category_ID = Util.GetValueOfInt(r["GL_Category_ID"]);
            result.CategoryName = Util.GetValueOfString(r["CategoryName"]);
            result.DocTypeName = Util.GetValueOfString(r["DocTypeName"]);

            result.Created = FormatStamp(Util.GetValueOfDateTime(r["Created"]));
            result.CreatedByName = Util.GetValueOfString(r["CreatedByName"]);
            result.Updated = FormatStamp(Util.GetValueOfDateTime(r["Updated"]));
            result.UpdatedByName = Util.GetValueOfString(r["UpdatedByName"]);

            return true;
        }

        // ----------------------------------------------------------------- //
        //  2. Period control                                                 //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reads the period-control status of the journal's period for the GL
        /// Journal document base type. A period with no control row for 'GLJ' is
        /// reported with an empty status, which the panel treats as "not closed".
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="result">Payload carrying C_Period_ID; filled in place.</param>
        private void LoadPeriodControl(Ctx ctx, JournalPanelData result)
        {
            if (result.C_Period_ID <= 0)
            {
                return;
            }

            string sql = @"SELECT pc.PeriodStatus
                             FROM C_PeriodControl pc
                            WHERE pc.C_Period_ID=@C_Period_ID
                              AND pc.DocBaseType=@DocBaseType
                              AND pc.IsActive='Y'";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "pc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                /* Two binds, each occurring once, in the order they appear. */
                string status = Util.GetValueOfString(DB.ExecuteScalar(accessSql, new SqlParameter[]
                {
                    new SqlParameter("@C_Period_ID", result.C_Period_ID),
                    new SqlParameter("@DocBaseType", DOCBASETYPE_GLJOURNAL)
                }, null));

                result.PeriodStatus = status;
                result.PeriodStatusName = GetListReferenceName(ctx, "C_PeriodControl", "PeriodStatus", status);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_291 LoadPeriodControl(" + result.C_Period_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  3. Journal lines                                                  //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// One page of the journal's lines, for the panel's pager. Only the rows are
        /// returned — the count and the Dr / Cr totals came with the initial payload
        /// and do not change between pages. The journal itself is re-read under
        /// MRole first, so a page can never be fetched for a record the role cannot
        /// see.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page; 0 or less means LINES_PAGE_SIZE.</param>
        /// <returns>Populated <see cref="JournalLinesPage"/>; empty Rows when the id
        /// is invalid, not accessible, or the page is past the end.</returns>
        public JournalLinesPage GetJournalLines(Ctx ctx, int GL_Journal_ID, int page, int pageSize)
        {
            JournalLinesPage result = new JournalLinesPage();
            result.Rows = new List<JournalLineRow>();
            result.Page = page < 0 ? 0 : page;
            result.PageSize = pageSize > 0 ? pageSize : LINES_PAGE_SIZE;

            if (ctx == null || GL_Journal_ID <= 0)
            {
                return result;
            }

            /* The access test is the journal read, exactly as on the initial load. */
            JournalPanelData probe = new JournalPanelData();
            if (!LoadJournal(ctx, GL_Journal_ID, probe))
            {
                return result;
            }

            result.GL_Journal_ID = GL_Journal_ID;
            result.Rows = LoadLines(ctx, GL_Journal_ID, result.Page, result.PageSize);
            return result;
        }

        /// <summary>
        /// Count and Dr / Cr totals over EVERY active line of the journal, in one
        /// aggregate. These feed the section summary and the grid's total row, which
        /// must describe the whole document and not just the page on screen.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <param name="result">Payload filled in place (LineCount, LinesTotalDr / Cr).</param>
        private void LoadLineTotals(Ctx ctx, int GL_Journal_ID, JournalPanelData result)
        {
            string sql = @"SELECT COUNT(jl.GL_JournalLine_ID) AS LineCount,
                                  COALESCE(SUM(jl.AmtSourceDr), 0) AS TotalDr,
                                  COALESCE(SUM(jl.AmtSourceCr), 0) AS TotalCr
                             FROM GL_JournalLine jl
                            WHERE jl.GL_Journal_ID=@GL_Journal_ID
                              AND jl.IsActive='Y'
                              AND jl.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "jl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@GL_Journal_ID", GL_Journal_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r = ds.Tables[0].Rows[0];
                    result.LineCount = Util.GetValueOfInt(r["LineCount"]);
                    result.LinesTotalDr = Util.GetValueOfDecimal(r["TotalDr"]);
                    result.LinesTotalCr = Util.GetValueOfDecimal(r["TotalCr"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_291 LoadLineTotals(" + GL_Journal_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads one page of the journal's active lines with the account and the
        /// accounting dimensions resolved through the line's C_ValidCombination.
        /// Amounts are SOURCE amounts (the journal's own currency) — the same figures
        /// the journal header totals — and the accounted amounts ride along for the
        /// tooltip. Dimension masters are joined with LEFT OUTER JOIN so a line
        /// missing any of them still comes back.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>Lines of that page in Line order; empty past the end.</returns>
        private List<JournalLineRow> LoadLines(Ctx ctx, int GL_Journal_ID, int page, int pageSize)
        {
            List<JournalLineRow> rows = new List<JournalLineRow>();

            string sql = @"SELECT jl.GL_JournalLine_ID,
                                  jl.Line,
                                  COALESCE(jl.Description, N'') AS Description,
                                  jl.AmtSourceDr,
                                  jl.AmtSourceCr,
                                  jl.AmtAcctDr,
                                  jl.AmtAcctCr,
                                  ev.Value AS AccountValue,
                                  ev.Name AS AccountName,
                                  sub.Value AS SubAcctValue,
                                  trxorg.Value AS OrgTrxValue,
                                  bp.Name AS BPName,
                                  prd.Value AS ProductValue,
                                  prj.Value AS ProjectValue,
                                  cmp.Value AS CampaignValue,
                                  act.Value AS ActivityValue,
                                  sr.Value AS SalesRegionValue,
                                  u1.Value AS User1Value,
                                  u2.Value AS User2Value
                             FROM GL_JournalLine jl
                             INNER JOIN C_ValidCombination vc ON (vc.C_ValidCombination_ID=jl.C_ValidCombination_ID)
                             INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=vc.Account_ID)
                             LEFT OUTER JOIN C_SubAcct sub ON (sub.C_SubAcct_ID=vc.C_SubAcct_ID)
                             LEFT OUTER JOIN AD_Org trxorg ON (trxorg.AD_Org_ID=vc.AD_OrgTrx_ID)
                             LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=vc.C_BPartner_ID)
                             LEFT OUTER JOIN M_Product prd ON (prd.M_Product_ID=vc.M_Product_ID)
                             LEFT OUTER JOIN C_Project prj ON (prj.C_Project_ID=vc.C_Project_ID)
                             LEFT OUTER JOIN C_Campaign cmp ON (cmp.C_Campaign_ID=vc.C_Campaign_ID)
                             LEFT OUTER JOIN C_Activity act ON (act.C_Activity_ID=vc.C_Activity_ID)
                             LEFT OUTER JOIN C_SalesRegion sr ON (sr.C_SalesRegion_ID=vc.C_SalesRegion_ID)
                             LEFT OUTER JOIN C_ElementValue u1 ON (u1.C_ElementValue_ID=vc.User1_ID)
                             LEFT OUTER JOIN C_ElementValue u2 ON (u2.C_ElementValue_ID=vc.User2_ID)
                            WHERE jl.GL_Journal_ID=@GL_Journal_ID
                              AND jl.IsActive='Y'
                              AND jl.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "jl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            /* ORDER BY and the paging suffix go AFTER the access filter, so the
               predicate lands in the WHERE clause and not behind a trailing clause. */
            accessSql += " ORDER BY jl.Line, jl.GL_JournalLine_ID";
            accessSql += PagingSuffix(page, pageSize);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@GL_Journal_ID", GL_Journal_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_291 LoadLines(" + GL_Journal_ID + "): " + ex.Message);
                return rows;
            }

            if (ds == null || ds.Tables.Count == 0)
            {
                return rows;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                JournalLineRow row = new JournalLineRow();
                row.GL_JournalLine_ID = Util.GetValueOfInt(r["GL_JournalLine_ID"]);
                row.Line = Util.GetValueOfInt(r["Line"]);
                row.Description = Util.GetValueOfString(r["Description"]);
                row.AmtSourceDr = Util.GetValueOfDecimal(r["AmtSourceDr"]);
                row.AmtSourceCr = Util.GetValueOfDecimal(r["AmtSourceCr"]);
                row.AmtAcctDr = Util.GetValueOfDecimal(r["AmtAcctDr"]);
                row.AmtAcctCr = Util.GetValueOfDecimal(r["AmtAcctCr"]);
                row.AccountValue = Util.GetValueOfString(r["AccountValue"]);
                row.AccountName = Util.GetValueOfString(r["AccountName"]);

                /* Dimension codes in a fixed, predictable order. The client joins
                   them with a middle dot after the account value. */
                row.Dimensions = new List<string>();
                AddDimension(row.Dimensions, r["SubAcctValue"]);
                AddDimension(row.Dimensions, r["OrgTrxValue"]);
                AddDimension(row.Dimensions, r["BPName"]);
                AddDimension(row.Dimensions, r["ProductValue"]);
                AddDimension(row.Dimensions, r["ProjectValue"]);
                AddDimension(row.Dimensions, r["CampaignValue"]);
                AddDimension(row.Dimensions, r["ActivityValue"]);
                AddDimension(row.Dimensions, r["SalesRegionValue"]);
                AddDimension(row.Dimensions, r["User1Value"]);
                AddDimension(row.Dimensions, r["User2Value"]);

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>Appends a dimension code when the cell carries one.</summary>
        /// <param name="list">Dimension list being built.</param>
        /// <param name="cell">Raw cell value.</param>
        private static void AddDimension(List<string> list, object cell)
        {
            string value = Util.GetValueOfString(cell);
            if (!string.IsNullOrEmpty(value))
            {
                list.Add(value.Trim());
            }
        }

        // ----------------------------------------------------------------- //
        //  4. Workflow steps                                                 //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Human workflow steps recorded against the journal, read through the
        /// platform's own workflow-to-record link (AD_WF_Process.AD_Table_ID +
        /// Record_ID). Only user-choice, user-window and user-form nodes are
        /// reported — those are the approval / review steps a person acted on;
        /// document-action and system nodes are the engine's own bookkeeping and
        /// would only add noise to a routing view.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <returns>Steps in the order they were raised; empty when no workflow ran.</returns>
        private List<WorkflowStepRow> LoadWorkflowSteps(Ctx ctx, int GL_Journal_ID)
        {
            List<WorkflowStepRow> steps = new List<WorkflowStepRow>();

            int tableId = MJournal.Table_ID;
            if (tableId <= 0)
            {
                return steps;
            }

            /* The table id is dictionary metadata this model resolved itself, so it
               is an integer literal here rather than a bind — that keeps the
               statement to a single bound value for positional binding. */
            string sql = @"SELECT wfa.AD_WF_Activity_ID,
                                  wfa.WFState,
                                  wfa.Created,
                                  wfa.Updated,
                                  wfn.Name AS NodeName,
                                  au.Name AS AssigneeName,
                                  uu.Name AS UpdatedByName
                             FROM AD_WF_Activity wfa
                             INNER JOIN AD_WF_Process wfp ON (wfp.AD_WF_Process_ID=wfa.AD_WF_Process_ID)
                             INNER JOIN AD_WF_Node wfn ON (wfn.AD_WF_Node_ID=wfa.AD_WF_Node_ID)
                             LEFT OUTER JOIN AD_User au ON (au.AD_User_ID=wfa.AD_User_ID)
                             LEFT OUTER JOIN AD_User uu ON (uu.AD_User_ID=wfa.UpdatedBy)
                            WHERE wfp.AD_Table_ID=" + tableId + @"
                              AND wfp.Record_ID=@GL_Journal_ID
                              AND wfa.IsActive='Y'
                              AND wfp.IsActive='Y'
                              AND wfn.Action IN ('C','W','X')";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "wfa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            accessSql += " ORDER BY wfa.Created, wfa.AD_WF_Activity_ID";

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, new SqlParameter[]
                {
                    new SqlParameter("@GL_Journal_ID", GL_Journal_ID)
                }, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_291 LoadWorkflowSteps(" + GL_Journal_ID + "): " + ex.Message);
                return steps;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return steps;
            }

            /* WFState is a LIST column; one dictionary read serves every row. */
            Dictionary<string, string> stateLabels = LoadRefListLabels(ctx, "AD_WF_Activity", "WFState");

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                WorkflowStepRow step = new WorkflowStepRow();
                step.AD_WF_Activity_ID = Util.GetValueOfInt(r["AD_WF_Activity_ID"]);
                step.NodeName = Util.GetValueOfString(r["NodeName"]);
                step.WFState = Util.GetValueOfString(r["WFState"]);
                step.WFStateName = (!string.IsNullOrEmpty(step.WFState) && stateLabels.ContainsKey(step.WFState))
                    ? stateLabels[step.WFState]
                    : step.WFState;
                /* The person who closed the step is the actor; while it is still
                   open the assignee is the only name there is. */
                string actor = Util.GetValueOfString(r["UpdatedByName"]);
                if (string.IsNullOrEmpty(actor) || step.WFState != "CC")
                {
                    actor = Util.GetValueOfString(r["AssigneeName"]);
                }
                step.ActorName = actor;
                step.Created = FormatStamp(Util.GetValueOfDateTime(r["Created"]));
                step.Updated = FormatStamp(Util.GetValueOfDateTime(r["Updated"]));
                steps.Add(step);
            }

            return steps;
        }

        // ----------------------------------------------------------------- //
        //  5. Posting moment                                                 //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The moment the journal was posted — the earliest Fact_Acct row written
        /// for the record. GL_Journal itself carries no posting timestamp.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <returns>Timestamp as yyyy-MM-dd HH:mm, or "" when no fact exists.</returns>
        private string LoadPostedOn(Ctx ctx, int GL_Journal_ID)
        {
            int tableId = MJournal.Table_ID;
            if (tableId <= 0)
            {
                return "";
            }

            /* Table id is a resolved dictionary literal (see LoadWorkflowSteps). */
            string sql = @"SELECT MIN(fa.Created) AS PostedOn
                             FROM Fact_Acct fa
                            WHERE fa.AD_Table_ID=" + tableId + @"
                              AND fa.Record_ID=@GL_Journal_ID
                              AND fa.IsActive='Y'
                              AND fa.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                /* Two binds, each occurring once, in the order they appear. */
                object value = DB.ExecuteScalar(accessSql, new SqlParameter[]
                {
                    new SqlParameter("@GL_Journal_ID", GL_Journal_ID),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                }, null);
                return FormatStamp(Util.GetValueOfDateTime(value));
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_291 LoadPostedOn(" + GL_Journal_ID + "): " + ex.Message);
                return "";
            }
        }

        // ----------------------------------------------------------------- //
        //  Shared helpers                                                    //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Resolves the translated display name of one List-reference value from
        /// AD_Ref_List / AD_Ref_List_Trl for the session language. The AD_Column is
        /// located by TableName + ColumnName — stable across environments — rather
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
                _log.Severe("VAS_291 GetListReferenceName(" + tableName + "." + columnName + "): " + ex.Message);
                return code;
            }
        }

        /// <summary>
        /// The display labels of a LIST column's reference values, keyed by their
        /// stored code, in the logged-in user's language. Used where one column is
        /// resolved for many rows, so the dictionary is read once instead of once
        /// per row.
        /// </summary>
        /// <param name="ctx">User context (supplies the UI language).</param>
        /// <param name="tableName">Physical table owning the column.</param>
        /// <param name="columnName">List-reference column.</param>
        /// <returns>Code → display name map; empty when the column has no list.</returns>
        private Dictionary<string, string> LoadRefListLabels(Ctx ctx, string tableName, string columnName)
        {
            Dictionary<string, string> labels = new Dictionary<string, string>();
            if (ctx == null)
            {
                return labels;
            }

            string sql = @"SELECT rl.Value,
                                  COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                             FROM AD_Column col
                             INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID=col.AD_Table_ID)
                             INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID=col.AD_Reference_Value_ID)
                             LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID
                                                                     AND rlt.AD_Language=@Language
                                                                     AND rlt.IsActive='Y')
                            WHERE tbl.TableName=@TableName
                              AND col.ColumnName=@ColumnName
                              AND col.IsActive='Y'
                              AND rl.IsActive='Y'";

            try
            {
                /* Three binds, each occurring once, in the order they appear. */
                DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
                {
                    new SqlParameter("@Language", ctx.GetAD_Language()),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null);

                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        string code = Util.GetValueOfString(r["Value"]);
                        if (!string.IsNullOrEmpty(code) && !labels.ContainsKey(code))
                        {
                            labels[code] = Util.GetValueOfString(r["DisplayName"]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_291 LoadRefListLabels(" + tableName + "." + columnName + "): " + ex.Message);
            }

            return labels;
        }

        /// <summary>
        /// DB-specific row-limit suffix for server-side paging: Oracle uses
        /// OFFSET / FETCH, PostgreSQL LIMIT / OFFSET. page and pageSize are integers
        /// the server owns (no injection risk), so they are inlined — some engines
        /// reject a bound LIMIT.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>SQL suffix.</returns>
        private static string PagingSuffix(int page, int pageSize)
        {
            if (page < 0)
            {
                page = 0;
            }
            if (pageSize <= 0)
            {
                pageSize = LINES_PAGE_SIZE;
            }
            int offset = page * pageSize;
            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }
            return " LIMIT " + pageSize + " OFFSET " + offset;
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

        /// <summary>Formats a nullable timestamp as yyyy-MM-dd HH:mm (empty when
        /// null). The client reads it as UTC and shows it in the browser's zone.</summary>
        /// <param name="value">Timestamp to format.</param>
        /// <returns>Timestamp string, or "".</returns>
        private static string FormatStamp(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "";
        }

        // ----------------------------------------------------------------- //
        //  Payload                                                           //
        // ----------------------------------------------------------------- //

        /// <summary>Everything the panel renders for one journal.</summary>
        public class JournalPanelData
        {
            public int GL_Journal_ID { get; set; }
            public int AD_Org_ID { get; set; }
            public string OrgName { get; set; }
            public string DocumentNo { get; set; }
            public string Description { get; set; }

            /// <summary>Stored DocStatus code and its translated name.</summary>
            public string DocStatus { get; set; }
            public string DocStatusName { get; set; }
            public string DocAction { get; set; }
            /// <summary>Stored Posted code (Y / N / error codes) and its translated name.</summary>
            public string Posted { get; set; }
            public string PostedName { get; set; }
            public bool IsApproved { get; set; }
            public bool Processed { get; set; }
            public string PostingType { get; set; }
            public string PostingTypeName { get; set; }

            /// <summary>ISO yyyy-MM-dd.</summary>
            public string DateAcct { get; set; }
            public string DateDoc { get; set; }

            public decimal TotalDr { get; set; }
            public decimal TotalCr { get; set; }
            public decimal ControlAmt { get; set; }
            public decimal CurrencyRate { get; set; }
            public int StdPrecision { get; set; }

            public int C_Currency_ID { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }

            public int C_AcctSchema_ID { get; set; }
            public string AcctSchemaName { get; set; }
            public string GAAP { get; set; }
            public string GAAPName { get; set; }
            public string AcctCurISO { get; set; }

            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }
            public string PeriodStartDate { get; set; }
            public string PeriodEndDate { get; set; }
            public string FiscalYear { get; set; }
            /// <summary>C_PeriodControl.PeriodStatus for DocBaseType 'GLJ' ("" when no control row).</summary>
            public string PeriodStatus { get; set; }
            public string PeriodStatusName { get; set; }

            public int GL_JournalBatch_ID { get; set; }
            public string BatchDocumentNo { get; set; }
            public int GL_Category_ID { get; set; }
            public string CategoryName { get; set; }
            public string DocTypeName { get; set; }

            /// <summary>yyyy-MM-dd HH:mm, read as UTC by the client.</summary>
            public string Created { get; set; }
            public string CreatedByName { get; set; }
            public string Updated { get; set; }
            public string UpdatedByName { get; set; }
            /// <summary>Earliest Fact_Acct.Created for the record; "" when not posted.</summary>
            public string PostedOn { get; set; }

            /// <summary>Count and Dr / Cr totals over EVERY line of the journal.</summary>
            public int LineCount { get; set; }
            public decimal LinesTotalDr { get; set; }
            public decimal LinesTotalCr { get; set; }
            /// <summary>Rows per page the server pages with; Lines is page 0.</summary>
            public int LinesPageSize { get; set; }
            public List<JournalLineRow> Lines { get; set; }
            /// <summary>AD_Table_ID of GL_JournalLine, so the panel can find the
            /// journal's line tab in its own window by table rather than by name.</summary>
            public int LineTable_ID { get { return MJournalLine.Table_ID; } }

            public List<WorkflowStepRow> WorkflowSteps { get; set; }
        }

        /// <summary>One page of journal lines, for the pager.</summary>
        public class JournalLinesPage
        {
            public int GL_Journal_ID { get; set; }
            /// <summary>Zero-based page index actually served.</summary>
            public int Page { get; set; }
            public int PageSize { get; set; }
            public List<JournalLineRow> Rows { get; set; }
        }

        /// <summary>One journal line, account and dimensions already resolved.</summary>
        public class JournalLineRow
        {
            public int GL_JournalLine_ID { get; set; }
            public int Line { get; set; }
            public string Description { get; set; }
            public decimal AmtSourceDr { get; set; }
            public decimal AmtSourceCr { get; set; }
            public decimal AmtAcctDr { get; set; }
            public decimal AmtAcctCr { get; set; }
            public string AccountValue { get; set; }
            public string AccountName { get; set; }
            /// <summary>Dimension codes (sub-account, trx org, partner, product,
            /// project, campaign, activity, sales region, user lists), blanks removed.</summary>
            public List<string> Dimensions { get; set; }
        }

        /// <summary>One human workflow step raised against the journal.</summary>
        public class WorkflowStepRow
        {
            public int AD_WF_Activity_ID { get; set; }
            public string NodeName { get; set; }
            /// <summary>Stored WFState code (CC / OR / OS / CA / CT / ON) and its translated name.</summary>
            public string WFState { get; set; }
            public string WFStateName { get; set; }
            public string ActorName { get; set; }
            public string Created { get; set; }
            public string Updated { get; set; }
        }
    }
}
