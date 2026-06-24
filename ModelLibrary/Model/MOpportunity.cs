/********************************************************
 * Project Name   : ModelLibrary
 * Class Name     : MOpportunity
 * Purpose        : Business-logic layer over X_VAS_Opportunity (table VAS_Opportunity),
 *                  the opportunity counterpart of MProject / C_Project.
 * Class Used     : X_VAS_Opportunity
 * Chronological Development
 * VAI052           03-Jun-2026 - Promoted the empty stub to a public PO subclass and
 *                  ported the parent-side members required by VAS_GenerateQuotation
 *                  (GetLines() returning MOppLines[] and GetM_PriceList_ID() derived
 *                  from the price-list version) from MProject, adapted to the
 *                  VAS_Opportunity columns. Made the class public so the public
 *                  MOppLines API (parent constructor / GetOpportunity) compiles.
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using ViennaAdvantage.Model;

namespace ModelLibrary.Model
{
    public class MOpportunity : X_VAS_Opportunity
    {
        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="VAS_Opportunity_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MOpportunity(Ctx ctx, int VAS_Opportunity_ID, Trx trxName) : base(ctx, VAS_Opportunity_ID, trxName)
        {
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="rs">result set</param>
        /// <param name="trxName">transaction</param>
        public MOpportunity(Ctx ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName)
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
        /// hand-written MOppLines model for each row.
        /// </summary>
        /// <returns>array of opportunity lines (never null)</returns>
        public MOppLines[] GetLines()
        {
            List<MOppLines> list = new List<MOppLines>();
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
                    list.Add(new MOppLines(GetCtx(), dr, Get_TrxName()));
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

            MOppLines[] retValue = new MOppLines[list.Count];
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
