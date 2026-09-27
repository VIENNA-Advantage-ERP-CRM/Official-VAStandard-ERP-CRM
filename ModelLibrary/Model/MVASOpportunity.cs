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
                MPriceList pl = MPriceList.Get(GetCtx(), GetM_PriceList_ID(), null);
                if (pl != null && pl.Get_ID() != 0)
                    SetC_Currency_ID(pl.GetC_Currency_ID());
            }
            return true;
        }
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (GetC_Campaign_ID() != 0 && success)
            {
                //Used transaction because total was not updating on header
                MCampaign cam = new MCampaign(GetCtx(), GetC_Campaign_ID(), Get_TrxName());
                decimal plnAmt = Util.GetValueOfDecimal(DB.ExecuteScalar("SELECT COALESCE(SUM(pl.PlannedAmt),0)  FROM VAS_Opportunity pl WHERE pl.IsActive = 'Y' AND pl.C_Campaign_ID = " + GetC_Campaign_ID(), null, Get_TrxName()));
                cam.SetCosts(plnAmt);
                cam.Save();
            }
            return base.AfterSave(newRecord, success);
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
