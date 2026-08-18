/******************************************************
 * Module Name    : VASLogic
 * Purpose        : AR Invoice detail tab panel - recurring invoice
 *                  schedule (banner state, existing MRecurring record,
 *                  generated-run history and the save/update action).
 * chronological  : Development
 *   VAI_145        Created  04 August 2026
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Recurring half of the AR invoice detail panel model. The dialog only
    /// configures the schedule - the existing Onfinity recurring process
    /// (MRecurring.ExecuteRun) is what creates the invoices, so nothing here
    /// duplicates invoice-generation logic.
    ///
    /// Frequency mapping. MRecurring exposes Daily / Weekly / Monthly /
    /// Quarterly only, so the UI's five buttons are expressed with those
    /// constants rather than guessed string values:
    ///   Weekly    -> FREQUENCYTYPE_Weekly,    every n weeks
    ///   Monthly   -> FREQUENCYTYPE_Monthly,   every n months
    ///   Quarterly -> FREQUENCYTYPE_Quarterly, every n quarters (3n months)
    ///   Annually  -> FREQUENCYTYPE_Monthly,   every 12n months
    ///   Custom    -> the caller's own unit + interval
    /// </summary>
    public partial class VAS_189_ARInvoiceDetailPanelModel
    {
        /// <summary>Preview rows are capped so a large RunsMax cannot flood the dialog.</summary>
        private const int RECURRING_PREVIEW_MAX = 20;

        #region Read

        /// <summary>
        /// Loads the recurring state for the banner and the dialog: the existing
        /// C_Recurring record (if any), its schedule fields, the generated-run
        /// history and the eligibility verdict used by the banner copy.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">source invoice</param>
        /// <returns>recurring info (never null)</returns>
        public RecurringInfo LoadRecurringInfo(Ctx ctx, int C_Invoice_ID)
        {
            RecurringInfo info = new RecurringInfo { Runs = new List<RecurringRunRow>() };
            if (C_Invoice_ID <= 0)
            {
                return info;
            }

            // One recurring record per source invoice (the standard
            // CreateRecurringFromInvoice process enforces the same rule).
            // Description is optional across environments, so it is selected on a
            // second attempt only: losing that one field must not cost the panel the
            // whole schedule (which would make an active recurring read as "eligible").
            string columns = @"rec.C_Recurring_ID,
                              rec.Name,
                              rec.RecurringType,
                              rec.FrequencyType,
                              rec.Frequency,
                              rec.RunsMax,
                              rec.RunsRemaining,
                              rec.DateNextRun,
                              rec.DateLastRun,
                              rec.IsActive";
            string where = @" FROM C_Recurring rec
                           WHERE rec.C_Invoice_ID=@C_Invoice_ID
                             AND rec.IsActive='Y'";

            MRole role = MRole.GetDefault(ctx);
            string sqlWithDesc = role.AddAccessSQL("SELECT " + columns + ", rec.Description" + where,
                "rec", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string sqlNoDesc = role.AddAccessSQL("SELECT " + columns + where,
                "rec", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            bool hasDescription = true;
            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(sqlWithDesc,
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            }
            catch (Exception ex)
            {
                log.Info("VAS_189 recurring Description column unavailable: " + ex.Message);
                hasDescription = false;
                try
                {
                    ds = DB.ExecuteDataset(sqlNoDesc,
                        new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
                }
                catch (Exception ex2)
                {
                    log.Info("VAS_189 recurring lookup skipped: " + ex2.Message);
                    return info;
                }
            }

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                info.C_Recurring_ID = Util.GetValueOfInt(r["C_Recurring_ID"]);
                info.Name = Util.GetValueOfString(r["Name"]);
                info.FrequencyType = Util.GetValueOfString(r["FrequencyType"]);
                info.Frequency = Util.GetValueOfInt(r["Frequency"]);
                info.RunsMax = Util.GetValueOfInt(r["RunsMax"]);
                info.RunsRemaining = Util.GetValueOfInt(r["RunsRemaining"]);
                info.DateNextRun = Util.GetValueOfDateTime(r["DateNextRun"]);
                info.DateLastRun = Util.GetValueOfDateTime(r["DateLastRun"]);
                info.Description = hasDescription ? Util.GetValueOfString(r["Description"]) : "";
                info.Exists = info.C_Recurring_ID > 0;
                // The dialog works in semantic tokens (WEEKLY / MONTHLY / ...) so it
                // never has to know the stored short codes.
                info.FrequencyToken = ToFrequencyToken(info.FrequencyType, info.Frequency);
                info.DisplayFrequency = ToDisplayFrequency(info.FrequencyType, info.Frequency);
            }

            if (info.Exists)
            {
                info.Runs = LoadRecurringRuns(ctx, info.C_Recurring_ID);
                // Generated runs are counted from the run log, never inferred from a
                // date matching a scheduled occurrence.
                info.GeneratedRuns = info.Runs.Count;
                info.RemainingRuns = Math.Max(0, info.RunsMax - info.GeneratedRuns);
            }
            return info;
        }

        /// <summary>
        /// Loads the invoices already generated by a recurring schedule, using
        /// the explicit C_Recurring_Run link rather than a date match.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Recurring_ID">recurring record</param>
        /// <returns>generated run rows, oldest first</returns>
        private List<RecurringRunRow> LoadRecurringRuns(Ctx ctx, int C_Recurring_ID)
        {
            List<RecurringRunRow> list = new List<RecurringRunRow>();
            string sql = @"SELECT
                              run.C_Recurring_Run_ID,
                              run.DateDoc,
                              run.C_Invoice_ID,
                              inv.DocumentNo,
                              dt.Name AS DocTypeName,
                              inv.GrandTotal,
                              cur.ISO_Code AS CurISO,
                              cur.CurSymbol AS CurSymbol,
                              cur.StdPrecision AS StdPrecision
                           FROM C_Recurring_Run run
                           LEFT OUTER JOIN C_Invoice inv ON (run.C_Invoice_ID=inv.C_Invoice_ID)
                           LEFT OUTER JOIN C_DocType dt ON (inv.C_DocTypeTarget_ID=dt.C_DocType_ID)
                           LEFT OUTER JOIN C_Currency cur ON (inv.C_Currency_ID=cur.C_Currency_ID)
                           WHERE run.C_Recurring_ID=@C_Recurring_ID
                             AND run.IsActive='Y'
                           ORDER BY run.DateDoc, run.C_Recurring_Run_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "run", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Recurring_ID", C_Recurring_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                RecurringRunRow run = new RecurringRunRow();
                run.DateDoc = Util.GetValueOfDateTime(r["DateDoc"]);
                run.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
                run.DocumentNo = Util.GetValueOfString(r["DocumentNo"]);
                run.DocTypeName = Util.GetValueOfString(r["DocTypeName"]);
                run.GrandTotal = Util.GetValueOfDecimal(r["GrandTotal"]);
                run.CurISO = Util.GetValueOfString(r["CurISO"]);
                run.CurSymbol = Util.GetValueOfString(r["CurSymbol"]);
                run.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
                // A run row without a readable invoice means the user has no access to
                // the generated document - the link is then not offered.
                run.IsCreated = run.C_Invoice_ID > 0 && !string.IsNullOrEmpty(run.DocumentNo);
                list.Add(run);
            }
            return list;
        }

        /// <summary>
        /// Returns everything the "Set up recurring" dialog needs: the source
        /// invoice summary (document type / number / amount / currency), the
        /// existing schedule when editing, and the generated-run history. Only
        /// the header summary is read - invoice lines are never reloaded for
        /// this dialog.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">source invoice</param>
        /// <param name="IsSOTrx">sales transaction flag supplied by the caller</param>
        /// <returns>dialog meta</returns>
        public RecurringMeta GetRecurringMeta(Ctx ctx, int C_Invoice_ID, bool IsSOTrx)
        {
            RecurringMeta meta = new RecurringMeta();
            if (C_Invoice_ID <= 0)
            {
                meta.Message = Msg.GetMsg(ctx, "VAS_189_InvalidRequest");
                return meta;
            }

            string sql = @"SELECT
                              i.C_Invoice_ID,
                              i.DocumentNo,
                              i.DocStatus,
                              i.DateInvoiced,
                              i.GrandTotal,
                              dt.Name AS DocTypeName,
                              cur.ISO_Code AS CurISO,
                              cur.CurSymbol AS CurSymbol,
                              cur.StdPrecision AS StdPrecision
                           FROM C_Invoice i
                           INNER JOIN C_DocType dt ON (i.C_DocTypeTarget_ID=dt.C_DocType_ID)
                           INNER JOIN C_Currency cur ON (i.C_Currency_ID=cur.C_Currency_ID)
                           WHERE i.C_Invoice_ID=@C_Invoice_ID
                             AND i.IsSOTrx=@IsSOTrx
                             AND i.IsActive='Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@C_Invoice_ID", C_Invoice_ID),
                new SqlParameter("@IsSOTrx", IsSOTrx ? "Y" : "N")
            }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                meta.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotFound");
                return meta;
            }

            DataRow r = ds.Tables[0].Rows[0];
            meta.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
            meta.DocumentNo = Util.GetValueOfString(r["DocumentNo"]);
            meta.DocTypeName = Util.GetValueOfString(r["DocTypeName"]);
            meta.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            meta.DateInvoiced = Util.GetValueOfDateTime(r["DateInvoiced"]);
            meta.GrandTotal = Util.GetValueOfDecimal(r["GrandTotal"]);
            meta.CurISO = Util.GetValueOfString(r["CurISO"]);
            meta.CurSymbol = Util.GetValueOfString(r["CurSymbol"]);
            meta.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
            meta.PreviewLimit = RECURRING_PREVIEW_MAX;
            meta.Recurring = LoadRecurringInfo(ctx, C_Invoice_ID);
            meta.Success = true;
            return meta;
        }

        #endregion

        #region Write

        /// <summary>
        /// Creates or updates the C_Recurring schedule for the source invoice.
        /// The schedule is validated and recalculated on the server - the browser
        /// preview is never authoritative - and no invoice is generated here.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">schedule fields from the dialog</param>
        /// <returns>saved schedule state</returns>
        public RecurringSaveResult SaveRecurring(Ctx ctx, RecurringSaveRequest req)
        {
            RecurringSaveResult result = new RecurringSaveResult();
            if (req == null || req.C_Invoice_ID <= 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_InvalidRequest");
                return result;
            }

            // Server-side validation of the schedule (mirrors the dialog rules). The
            // dialog sends a semantic token; the stored constant and the effective
            // interval are resolved here, so the browser never guesses a stored code.
            if (req.Frequency < 1)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_FrequencyGreaterZero");
                return result;
            }
            string frequencyType;
            int frequency;
            if (!ResolveFrequency(req.FrequencyType, req.Frequency, out frequencyType, out frequency))
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_SelectFrequencyType");
                return result;
            }
            if (req.RunsMax < 1)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_MaxRunsAtLeastOne");
                return result;
            }
            if (!req.DateNextRun.HasValue)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_EnterNextRunDate");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS189Recurring"));
            try
            {
                MInvoice inv = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (inv.GetC_Invoice_ID() <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotFound");
                    trx.Rollback();
                    return result;
                }
                if (inv.GetDocStatus() != "CO" && inv.GetDocStatus() != "CL")
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotCompleted");
                    trx.Rollback();
                    return result;
                }

                // Existing schedule for this invoice (one per source document).
                int recurringId = Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT rec.C_Recurring_ID FROM C_Recurring rec
                       WHERE rec.C_Invoice_ID=@C_Invoice_ID AND rec.IsActive='Y'",
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) }, trx));

                MRecurring recurring = new MRecurring(ctx, recurringId, trx);
                bool isNew = recurringId <= 0;

                if (isNew)
                {
                    recurring.SetAD_Client_ID(inv.GetAD_Client_ID());
                    recurring.SetAD_Org_ID(inv.GetAD_Org_ID());
                    // Name follows the standard recurring naming: DocType/DocumentNo.
                    recurring.SetName(BuildRecurringName(req.C_Invoice_ID, trx));
                    recurring.SetRecurringType(MRecurring.RECURRINGTYPE_Invoice);
                    recurring.SetC_Invoice_ID(req.C_Invoice_ID);
                }

                recurring.SetFrequencyType(frequencyType);
                recurring.SetFrequency(frequency);
                recurring.SetRunsMax(req.RunsMax);
                recurring.SetDateNextRun(req.DateNextRun.Value.Date);

                // RunsRemaining is derived from the run log, so an edited RunsMax cannot
                // resurrect runs that already happened.
                int generated = Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT COUNT(1) FROM C_Recurring_Run run WHERE run.C_Recurring_ID=@id",
                    new SqlParameter[] { new SqlParameter("@id", recurring.GetC_Recurring_ID()) }, trx));
                recurring.SetRunsRemaining(Math.Max(0, req.RunsMax - generated));

                // Description is optional and not present in every environment - only
                // set it when the column actually exists on the record.
                if (recurring.Get_ColumnIndex("Description") >= 0)
                {
                    recurring.Set_Value("Description", req.Description);
                }

                if (!recurring.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_189_RecurringNotSaved");
                    trx.Rollback();
                    return result;
                }

                trx.Commit();
                result.Success = true;
                result.C_Recurring_ID = recurring.GetC_Recurring_ID();
                result.Name = recurring.GetName();
                result.Message = Msg.GetMsg(ctx, "VAS_189_RecurringSaved");
            }
            catch (Exception ex)
            {
                try { if (trx != null) { trx.Rollback(); } } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                // trx was started -> it must be closed and nulled before returning
                // (runs on every exit path, including the early validation returns).
                if (trx != null)
                {
                    try { trx.Close(); } catch { /* ignore */ }
                    trx = null;
                }
            }

            if (result.Success)
            {
                // Hand the refreshed schedule back so the dialog and the banner can
                // re-render without a second round trip.
                result.Recurring = LoadRecurringInfo(ctx, req.C_Invoice_ID);
            }
            return result;
        }

        /// <summary>
        /// Maps the dialog's semantic frequency token onto the constants
        /// MRecurring actually exposes, together with the effective interval.
        /// "Annually" has no constant of its own, so it is stored as twelve
        /// months - which is exactly how MRecurring advances a monthly schedule.
        /// A raw stored constant is also accepted so an existing record round-trips.
        /// Unknown values are rejected rather than passed through as a guess.
        /// </summary>
        /// <param name="token">semantic token or stored constant</param>
        /// <param name="frequency">interval entered on the dialog</param>
        /// <param name="frequencyType">out: MRecurring frequency-type constant</param>
        /// <param name="effectiveFrequency">out: interval to store</param>
        /// <returns>true when the token resolved</returns>
        private bool ResolveFrequency(string token, int frequency, out string frequencyType, out int effectiveFrequency)
        {
            frequencyType = "";
            effectiveFrequency = frequency < 1 ? 1 : frequency;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            switch (token.Trim().ToUpperInvariant())
            {
                case "DAILY":
                    frequencyType = MRecurring.FREQUENCYTYPE_Daily;
                    return true;
                case "WEEKLY":
                    frequencyType = MRecurring.FREQUENCYTYPE_Weekly;
                    return true;
                case "MONTHLY":
                    frequencyType = MRecurring.FREQUENCYTYPE_Monthly;
                    return true;
                case "QUARTERLY":
                    frequencyType = MRecurring.FREQUENCYTYPE_Quarterly;
                    return true;
                case "ANNUALLY":
                    // One run every twelve months per interval.
                    frequencyType = MRecurring.FREQUENCYTYPE_Monthly;
                    effectiveFrequency = effectiveFrequency * 12;
                    return true;
            }

            // Already a stored constant (an existing record being re-saved).
            if (token == MRecurring.FREQUENCYTYPE_Daily
                || token == MRecurring.FREQUENCYTYPE_Weekly
                || token == MRecurring.FREQUENCYTYPE_Monthly
                || token == MRecurring.FREQUENCYTYPE_Quarterly)
            {
                frequencyType = token;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reverse of <see cref="ResolveFrequency"/>: names the stored schedule
        /// with the token the dialog's button group uses. A monthly schedule on a
        /// whole multiple of twelve reads back as Annually.
        /// </summary>
        /// <param name="frequencyType">stored constant</param>
        /// <param name="frequency">stored interval</param>
        /// <returns>semantic token</returns>
        private string ToFrequencyToken(string frequencyType, int frequency)
        {
            if (frequencyType == MRecurring.FREQUENCYTYPE_Daily)
            {
                return "DAILY";
            }
            if (frequencyType == MRecurring.FREQUENCYTYPE_Weekly)
            {
                return "WEEKLY";
            }
            if (frequencyType == MRecurring.FREQUENCYTYPE_Quarterly)
            {
                return "QUARTERLY";
            }
            if (frequencyType == MRecurring.FREQUENCYTYPE_Monthly)
            {
                return (frequency > 0 && frequency % 12 == 0) ? "ANNUALLY" : "MONTHLY";
            }
            return "MONTHLY";
        }

        /// <summary>
        /// Interval as the dialog shows it: an annual schedule stored as twelve
        /// months reads back as 1 year, not 12.
        /// </summary>
        /// <param name="frequencyType">stored constant</param>
        /// <param name="frequency">stored interval</param>
        /// <returns>interval in the token's own unit</returns>
        private int ToDisplayFrequency(string frequencyType, int frequency)
        {
            if (frequencyType == MRecurring.FREQUENCYTYPE_Monthly && frequency > 0 && frequency % 12 == 0)
            {
                return frequency / 12;
            }
            return frequency;
        }

        /// <summary>
        /// Builds the recurring record name as DocumentTypeName/DocumentNo, the
        /// same convention the standard CreateRecurringFromInvoice process uses.
        /// </summary>
        /// <param name="C_Invoice_ID">source invoice</param>
        /// <param name="trx">active transaction</param>
        /// <returns>recurring name</returns>
        private string BuildRecurringName(int C_Invoice_ID, Trx trx)
        {
            DataSet ds = DB.ExecuteDataset(
                @"SELECT dt.Name AS DocTypeName, i.DocumentNo
                    FROM C_Invoice i
                    INNER JOIN C_DocType dt ON (i.C_DocType_ID=dt.C_DocType_ID)
                   WHERE i.C_Invoice_ID=@C_Invoice_ID",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, trx);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return "" + C_Invoice_ID;
            }
            DataRow r = ds.Tables[0].Rows[0];
            return Util.GetValueOfString(r["DocTypeName"]) + "/" + Util.GetValueOfString(r["DocumentNo"]);
        }

        #endregion

        #region DTOs (recurring)

        public class RecurringInfo
        {
            public bool Exists { get; set; }
            public int C_Recurring_ID { get; set; }
            public string Name { get; set; }
            public string FrequencyType { get; set; }
            public int Frequency { get; set; }
            /// <summary>Semantic token the dialog's button group works in.</summary>
            public string FrequencyToken { get; set; }
            /// <summary>Interval in the token's own unit (12 months reads back as 1 year).</summary>
            public int DisplayFrequency { get; set; }
            public int RunsMax { get; set; }
            public int RunsRemaining { get; set; }
            public DateTime? DateNextRun { get; set; }
            public DateTime? DateLastRun { get; set; }
            public string Description { get; set; }
            /// <summary>Runs that actually produced an invoice (from C_Recurring_Run).</summary>
            public int GeneratedRuns { get; set; }
            /// <summary>RunsMax - GeneratedRuns, never negative.</summary>
            public int RemainingRuns { get; set; }
            public List<RecurringRunRow> Runs { get; set; }
        }

        public class RecurringRunRow
        {
            public DateTime? DateDoc { get; set; }
            public int C_Invoice_ID { get; set; }
            public string DocumentNo { get; set; }
            public string DocTypeName { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public bool IsCreated { get; set; }
        }

        public class RecurringMeta
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int C_Invoice_ID { get; set; }
            public string DocumentNo { get; set; }
            public string DocTypeName { get; set; }
            public string DocStatus { get; set; }
            public DateTime? DateInvoiced { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public int PreviewLimit { get; set; }
            public RecurringInfo Recurring { get; set; }
        }

        public class RecurringSaveRequest
        {
            public int C_Invoice_ID { get; set; }
            public string FrequencyType { get; set; }
            public int Frequency { get; set; }
            public int RunsMax { get; set; }
            public DateTime? DateNextRun { get; set; }
            public string Description { get; set; }
        }

        public class RecurringSaveResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int C_Recurring_ID { get; set; }
            public string Name { get; set; }
            public RecurringInfo Recurring { get; set; }
        }

        #endregion
    }
}
