/********************************************************
 * Project Name   : ModelLibrary
 * Class Name     : MVASOpportunity
 * Purpose        : Business-logic layer over X_VAS_Opportunity (table VAS_Opportunity),
 *                  the opportunity counterpart of MProject / C_Project.
 * Class Used     : X_VAS_Opportunity
 * Chronological Development
 * VAI052           03-Jun-2026 - Promoted the empty stub to a public PO subclass and
 *                  ported the parent-side members required by VAS_GenerateQuotation
 *                  (GetLines() returning MVASOppLines[] and GetM_PriceList_ID() derived
 *                  from the price-list version) from MProject, adapted to the
 *                  VAS_Opportunity columns. Made the class public so the public
 *                  MVASOppLines API (parent constructor / GetOpportunity) compiles.
 * Claude            18-Sep-2026 - Moved from namespace ModelLibrary.Model to
 *                  VAdvantage.Model, matching every other hand-written model class in
 *                  this project (MAccount, MOrder, etc. - see CLAUDE.md's documented
 *                  "ModelLibrary -> VAdvantage.*" convention). MVASOppLines.cs references
 *                  MVASOpportunity with no "using ModelLibrary.Model;" and lives in
 *                  VAdvantage.Model itself, so the mismatch was a hard CS0246 compile
 *                  error (confirmed via a direct msbuild run) - MVASOppLines.UpdateHeader
 *                  never ran because the whole file, and this whole class, never
 *                  actually built.
 * Claude            18-Sep-2026 - Renamed MOpportunity -> MVASOpportunity (table
 *                  VAS_Opportunity). Every OTHER hand-written model for a custom
 *                  X_VAS_* table in this project keeps "VAS" in the class name
 *                  (X_VAS_ContractMaster -> MVASContractMaster, X_VAS_ContractLine ->
 *                  MVASContractLine, etc. - six for six checked). MOpportunity/MOppLines
 *                  were the only two that stripped it, which is why saving from the
 *                  classic Window never ran BeforeSave/AfterSave here: the framework's
 *                  reflection-based model lookup for VAS_Opportunity/VAS_OppLines
 *                  couldn't find them under the stripped names. Confirmed by contrast -
 *                  MLead (standard C_Lead prefix, core ADempiere convention) always
 *                  triggered AfterSave correctly; only this custom-prefixed pair didn't.
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAdvantage.Model
{
    public class MVASOpportunity : X_VAS_Opportunity
    {
        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="VAS_Opportunity_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MVASOpportunity(Ctx ctx, int VAS_Opportunity_ID, Trx trxName) : base(ctx, VAS_Opportunity_ID, trxName)
        {
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="rs">result set</param>
        /// <param name="trxName">transaction</param>
        public MVASOpportunity(Ctx ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName)
        {
        }
        protected override bool BeforeSave(bool newRecord)
        {
            if (GetAD_User_ID() == -1)  //	Summary Project in Dimensions
                SetAD_User_ID(0);

            //	Set Currency
            if (Is_ValueChanged("M_PriceList_Version_ID") && GetM_PriceList_Version_ID() != 0)
            {
                // A price list version change the user accepted through the tab's Commit Warning is allowed
                // while lines exist: the lines are re-priced after save (RepriceLinesForPriceListVersion)
                if (!newRecord && !IsCommitWarningConfirmed("M_PriceList_Version_ID"))
                {
                    int linesCount = Util.GetValueOfInt(DB.ExecuteScalar(
                        "SELECT COUNT(vo.VAS_OppLines_ID) FROM VAS_OppLines vo WHERE vo.VAS_Opportunity_ID = @param1",
                        new SqlParameter[] { new SqlParameter("@param1", GetVAS_Opportunity_ID()) }, Get_Trx()));
                    if (linesCount > 0)
                    {
                        log.SaveWarning("Warning", Msg.GetMsg(GetCtx(), "VAS_DeleteLinesFirst"));
                        return false;
                    }
                }
                MPriceList pl = MPriceList.Get(GetCtx(), GetM_PriceList_ID(), null);
                if (pl != null && pl.Get_ID() != 0)
                    SetC_Currency_ID(pl.GetC_Currency_ID());
            }
            return true;
        }
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
                return base.AfterSave(newRecord, success);

            // Price list version changed through the Commit Warning (see BeforeSave): the header is saved,
            // now re-price the lines (before the campaign total below, which sums PlannedAmt)
            if (!newRecord && Is_ValueChanged("M_PriceList_Version_ID") && IsCommitWarningConfirmed("M_PriceList_Version_ID")
                && !RepriceLinesForPriceListVersion())
            {
                return false;
            }

            if (GetC_Campaign_ID() != 0)
            {
                UpdateCampaignCosts();
            }

            // Re-priced lines whose product has no price on the selected price list version: price kept
            // (same currency) or set to 0 (other currency), with their line numbers. Reported last.
            ReportPriceNotFoundLines();
            return base.AfterSave(newRecord, success);
        }

        /// <summary>
        /// Campaign costs = sum of PlannedAmt of its active opportunities.
        /// </summary>
        private void UpdateCampaignCosts()
        {
            //Used transaction because total was not updating on header
            MCampaign cam = new MCampaign(GetCtx(), GetC_Campaign_ID(), Get_TrxName());
            decimal plnAmt = Util.GetValueOfDecimal(DB.ExecuteScalar("SELECT COALESCE(SUM(pl.PlannedAmt),0)  FROM VAS_Opportunity pl WHERE pl.IsActive = 'Y' AND pl.C_Campaign_ID = " + GetC_Campaign_ID(), null, Get_TrxName()));
            cam.SetCosts(plnAmt);
            cam.Save();
        }

        /// <summary>
        /// True while several lines of this opportunity are saved in one go (price list version re-pricing,
        /// the opportunity line panel): a line save then skips its header update (which re-saves the whole
        /// opportunity). The caller runs FinishBulkLineUpdate once after the lines are saved. Lines must have
        /// this object as parent (MVASOppLines.SetParent).
        /// </summary>
        public bool IsBulkLineUpdate { get; set; }

        /// <summary>
        /// After lines were saved with IsBulkLineUpdate on: header PlannedAmt = sum of the active lines,
        /// written once (SQL) instead of re-saving the opportunity per line, and the campaign costs.
        /// </summary>
        /// <param name="updateCampaign">update the campaign costs here (false when the caller does it)</param>
        /// <returns>false when the header could not be updated</returns>
        public bool FinishBulkLineUpdate(bool updateCampaign)
        {
            SqlParameter[] p = new SqlParameter[] { new SqlParameter("@param1", GetVAS_Opportunity_ID()) };
            decimal plnAmt = Util.GetValueOfDecimal(DB.ExecuteScalar(
                "SELECT COALESCE(SUM(pl.PlannedAmt),0) FROM VAS_OppLines pl WHERE pl.IsActive = 'Y' AND pl.VAS_Opportunity_ID = @param1",
                p, Get_Trx()));
            int no = DB.ExecuteQuery("UPDATE VAS_Opportunity SET PlannedAmt = @param1 WHERE VAS_Opportunity_ID = @param2",
                new SqlParameter[] { new SqlParameter("@param1", plnAmt), new SqlParameter("@param2", GetVAS_Opportunity_ID()) }, Get_Trx());
            if (no < 0)
            {
                return false;
            }
            SetPlannedAmt(plnAmt);
            if (updateCampaign && GetC_Campaign_ID() != 0)
            {
                UpdateCampaignCosts();
            }
            return true;
        }

        /// <summary>
        /// Lines ("Line (Product)") whose product has no price on the newly selected price list version in
        /// this save: kept their old price (same currency) / price set to 0 (other currency).
        /// </summary>
        private string _priceKeptLines = "", _priceZeroLines = "";

        /// <summary>
        /// Raise the "no price on the selected price list" message for the lines collected by
        /// RepriceLinesForPriceListVersion - as a warning (combined with one already pending) and as the
        /// save's Info (SetDocWFMsg), because records saved after AfterSave reset the logger.
        /// </summary>
        private void ReportPriceNotFoundLines()
        {
            if (string.IsNullOrEmpty(_priceKeptLines) && string.IsNullOrEmpty(_priceZeroLines))
            {
                return;
            }
            string text = (string.IsNullOrEmpty(_priceZeroLines) ? ""
                    : Msg.GetMsg(GetCtx(), "VAS_PriceNotFoundPriceZero") + " " + _priceZeroLines)
                + (!string.IsNullOrEmpty(_priceZeroLines) && !string.IsNullOrEmpty(_priceKeptLines) ? " | " : "")
                + (string.IsNullOrEmpty(_priceKeptLines) ? ""
                    : Msg.GetMsg(GetCtx(), "VAS_PriceNotFoundPriceKept") + " " + _priceKeptLines);
            _priceKeptLines = "";
            _priceZeroLines = "";
            ValueNamePair pending = VLogger.RetrieveWarning();
            if (pending != null)
            {
                log.SaveWarning(pending.GetValue(), (string.IsNullOrEmpty(pending.GetName()) ? "" : pending.GetName() + " - ") + text);
            }
            else
            {
                log.SaveWarning("", text);
            }
            if (string.IsNullOrEmpty(GetDocWFMsg()))
            {
                SetDocWFMsg(text);
            }
        }

        /// <summary>
        /// Price list of a price list version (0 when none).
        /// </summary>
        private int GetPriceListOfVersion(int M_PriceList_Version_ID)
        {
            if (M_PriceList_Version_ID <= 0)
            {
                return 0;
            }
            return Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT M_PriceList_ID FROM M_PriceList_Version WHERE M_PriceList_Version_ID = @param1",
                new SqlParameter[] { new SqlParameter("@param1", M_PriceList_Version_ID) }, Get_Trx()));
        }

        /// <summary>
        /// After the header price list version changed (Commit Warning confirmed): re-price every product
        /// line from the new version - the same source the opportunity line panel prices from (the
        /// version's M_ProductPrice row; the row for the line's attribute instance first, else the product's
        /// own row) - also a price the user typed. A product without a price on the new version keeps its old
        /// price when both price lists have the same currency, and gets price 0 when the currency differs.
        /// PlannedAmt / Discount / margin are recalculated by the line BeforeSave; the header PlannedAmt once
        /// (FinishBulkLineUpdate). Charge lines keep their price.
        /// </summary>
        /// <returns>false when a line could not be saved (the header save is then rolled back)</returns>
        private bool RepriceLinesForPriceListVersion()
        {
            MVASOppLines[] lines = GetLines();
            if (lines.Length == 0)
            {
                return true;
            }

            int oldPL = GetPriceListOfVersion(Util.GetValueOfInt(Get_ValueOld("M_PriceList_Version_ID")));
            int newPL = GetPriceListOfVersion(GetM_PriceList_Version_ID());
            bool sameCurrency = oldPL > 0 && newPL > 0
                && MPriceList.Get(GetCtx(), oldPL, Get_TrxName()).GetC_Currency_ID() == MPriceList.Get(GetCtx(), newPL, Get_TrxName()).GetC_Currency_ID();

            // All prices of the new version for the products on these lines, one query:
            // key "product|asi" -> {PriceList, PriceStd}
            Dictionary<string, decimal[]> prices = new Dictionary<string, decimal[]>();
            DataSet ds = DB.ExecuteDataset(@"SELECT pp.M_Product_ID, NVL(pp.M_AttributeSetInstance_ID, 0) AS M_AttributeSetInstance_ID,
                                    pp.PriceList, pp.PriceStd
                               FROM M_ProductPrice pp
                               WHERE pp.M_PriceList_Version_ID = @param1 AND pp.IsActive = 'Y'
                               AND pp.M_Product_ID IN (SELECT ol.M_Product_ID FROM VAS_OppLines ol WHERE ol.VAS_Opportunity_ID = @param2)",
                new SqlParameter[] { new SqlParameter("@param1", GetM_PriceList_Version_ID()), new SqlParameter("@param2", GetVAS_Opportunity_ID()) },
                Get_Trx());
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow dr in ds.Tables[0].Rows)
                {
                    string key = Util.GetValueOfInt(dr["M_Product_ID"]) + "|" + Util.GetValueOfInt(dr["M_AttributeSetInstance_ID"]);
                    if (!prices.ContainsKey(key))
                    {
                        prices[key] = new decimal[] { Util.GetValueOfDecimal(dr["PriceList"]), Util.GetValueOfDecimal(dr["PriceStd"]) };
                    }
                }
            }

            List<string> keptLines = new List<string>(), zeroLines = new List<string>();
            int saved = 0;
            IsBulkLineUpdate = true;
            try
            {
                foreach (MVASOppLines line in lines)
                {
                    int product = line.GetM_Product_ID();
                    if (!line.IsActive() || product <= 0)
                    {
                        continue;   // charge lines keep their price
                    }
                    line.SetParent(this);
                    int asi = Util.GetValueOfInt(line.Get_Value("M_AttributeSetInstance_ID"));
                    decimal[] price;
                    if ((asi > 0 && prices.TryGetValue(product + "|" + asi, out price)) || prices.TryGetValue(product + "|0", out price)
                        || (prices.Keys.FirstOrDefault(k => k.StartsWith(product + "|")) is string anyKey && prices.TryGetValue(anyKey, out price)))
                    {
                        line.Set_Value("PriceList", price[0]);
                        line.Set_Value("PlannedPrice", price[1]);
                    }
                    else
                    {
                        string label = line.GetLine() + " (" + MProduct.Get(GetCtx(), product).GetName() + ")";
                        if (sameCurrency)
                        {
                            keptLines.Add(label);
                            continue;
                        }
                        line.Set_Value("PriceList", Env.ZERO);
                        line.Set_Value("PlannedPrice", Env.ZERO);
                        zeroLines.Add(label);
                    }
                    if (!line.Is_Changed())
                    {
                        continue;
                    }
                    if (!line.Save(Get_Trx()))
                    {
                        ValueNamePair vp = VLogger.RetrieveError();
                        string error = vp != null ? (!string.IsNullOrEmpty(vp.GetName()) ? vp.GetName() : vp.GetValue()) : "";
                        log.SaveError("", Msg.GetMsg(GetCtx(), "VIS_LineNotSaved") + " " + line.GetLine() + (string.IsNullOrEmpty(error) ? "" : " - " + error));
                        return false;
                    }
                    saved++;
                }
            }
            finally
            {
                IsBulkLineUpdate = false;
            }

            // the campaign total is updated by AfterSave right after this
            if (saved > 0 && !FinishBulkLineUpdate(false))
            {
                log.SaveError("", Msg.GetMsg(GetCtx(), "VIS_LineNotSaved"));
                return false;
            }

            // reported at the end of AfterSave (a line save above resets the logger)
            _priceKeptLines = string.Join(", ", keptLines);
            _priceZeroLines = string.Join(", ", zeroLines);
            return true;
        }

        /// <summary>
        /// True when the framework's Commit Warning was confirmed by the user for this save and the warning
        /// covers the given column. The framework (ApplyCommitWarningContext) puts the matched rules in the
        /// context for the duration of the save: CommitWarning_Matched = "Y", CommitWarning_Columns = comma
        /// separated column names - as plain keys or as window keys ("&lt;WindowNo&gt;|CommitWarning_Columns");
        /// PO has no window number, so both are accepted.
        /// </summary>
        /// <param name="columnName">column changed on the header</param>
        /// <returns>true if the change was accepted through the commit warning</returns>
        private bool IsCommitWarningConfirmed(string columnName)
        {
            foreach (KeyValuePair<string, string> entry in GetCtx().GetMap())
            {
                if (entry.Key == null || !(entry.Key.Equals("CommitWarning_Columns") || entry.Key.EndsWith("|CommitWarning_Columns"))
                    || string.IsNullOrEmpty(entry.Value))
                {
                    continue;
                }
                string prefix = entry.Key.Substring(0, entry.Key.Length - "CommitWarning_Columns".Length);
                if (!"Y".Equals(GetCtx().GetContext(prefix + "CommitWarning_Matched")))
                {
                    continue;
                }
                if (entry.Value.Split(',').Any(c => c.Trim().Equals(columnName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get Opportunity Lines. Counterpart of MProject.GetLines(); reads the
        /// VAS_OppLines child table instead of C_ProjectLine and returns the
        /// hand-written MVASOppLines model for each row.
        /// </summary>
        /// <returns>array of opportunity lines (never null)</returns>
        public MVASOppLines[] GetLines()
        {
            List<MVASOppLines> list = new List<MVASOppLines>();
            String sql = "SELECT * FROM VAS_OppLines WHERE VAS_Opportunity_ID=" + GetVAS_Opportunity_ID() + " ORDER BY VAS_LineNo";
            IDataReader idr = null;
            DataTable dt = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    list.Add(new MVASOppLines(GetCtx(), dr, Get_TrxName()));
                }
            }
            catch (Exception ex)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                log.Log(Level.SEVERE, sql, ex);
            }
            finally
            {
                if (idr != null)
                {
                    idr.Close();
                }
                dt = null;
            }

            MVASOppLines[] retValue = new MVASOppLines[list.Count];
            retValue = list.ToArray();
            return retValue;
        }

        /// <summary>
        /// Get Price List. VAS_Opportunity stores only the Price List Version
        /// (M_PriceList_Version_ID), so the Price List id is derived from it. This
        /// stands in for MProject.GetM_PriceList_ID(), which reads a dedicated column.
        /// </summary>
        /// <returns>M_PriceList_ID (0 when no price-list version is set)</returns>
        public int GetM_PriceList_ID()
        {
            if (GetM_PriceList_Version_ID() == 0)
            {
                return 0;
            }
            return Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT M_PriceList_ID FROM M_PriceList_Version WHERE M_PriceList_Version_ID = " + GetM_PriceList_Version_ID(),
                null, Get_TrxName()));
        }
    }
}
