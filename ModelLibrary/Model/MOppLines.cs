/********************************************************
 * Project Name   : ModelLibrary
 * Class Name     : MOppLines
 * Purpose        : Business-logic layer over X_VAS_OppLines (table VAS_OppLines),
 *                  the opportunity-line counterpart of MProjectLine / C_ProjectLine.
 * Class Used     : X_VAS_OppLines
 * Chronological Development
 * VAI052           03-Jun-2026 - Promoted the empty stub to a real PO subclass and
 *                  ported the line members required by VAS_GenerateQuotation
 *                  (constructors, line numbering, currency precision, PlannedAmt
 *                  calculation) from MProjectLine, adapted to VAS_Opportunity columns.
 ******************************************************/

using System;
using System.Text;
using System.Data;
using VAdvantage.Classes;
using VAdvantage.Utility;
using VAdvantage.DataBase;
using VAdvantage.Logging;

namespace ModelLibrary.Model
{
    public class MOppLines : X_VAS_OppLines
    {
        /** Parent				*/
        private MOpportunity _parent = null;
        private int currencyPrecision = 0;

        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="VAS_OppLines_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MOppLines(Ctx ctx, int VAS_OppLines_ID, Trx trxName)
            : base(ctx, VAS_OppLines_ID, trxName)
        {
            if (VAS_OppLines_ID == 0)
            {
                SetVAS_LineNo(0);
                SetPlannedAmt(Env.ZERO);
                SetPlannedMarginAmt(Env.ZERO);
                SetPlannedPrice(Env.ZERO);
                SetPlannedQty(Env.ONE);
            }
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="rs">result set</param>
        /// <param name="trxName">transaction</param>
        public MOppLines(Ctx ctx, DataRow rs, Trx trxName)
            : base(ctx, rs, trxName)
        {
        }

        /// <summary>
        /// Parent Constructor
        /// </summary>
        /// <param name="opportunity">parent</param>
        public MOppLines(MOpportunity opportunity)
            : this(opportunity.GetCtx(), 0, opportunity.Get_TrxName())
        {
            SetClientOrg(opportunity);
            SetVAS_Opportunity_ID(opportunity.GetVAS_Opportunity_ID());   // Parent
            _parent = opportunity;
            SetLine();
        }

        /// <summary>
        /// Before Save - mirror MProjectLine: keep line number and PlannedAmt in sync.
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <returns>true</returns>
        protected override bool BeforeSave(bool newRecord)
        {
            if (GetVAS_LineNo() == 0)
                SetLine();

            //	Planned Amount	- Currency Precision
            Decimal plannedAmt = Decimal.Multiply(GetPlannedQty(), GetPlannedPrice());
            if (Env.Scale(plannedAmt) > GetCurPrecision())
            {
                plannedAmt = Decimal.Round(plannedAmt, GetCurPrecision(), MidpointRounding.AwayFromZero);
            }
            SetPlannedAmt(plannedAmt);

            return true;
        }

        /// <summary>
        /// Get Line No. Alias kept so callers written against MProjectLine.GetLine()
        /// (e.g. VAS_GenerateQuotation) work unchanged against the VAS_LineNo column.
        /// </summary>
        /// <returns>line number</returns>
        public int GetLine()
        {
            return GetVAS_LineNo();
        }

        /// <summary>
        /// Set the next Line No (COALESCE(MAX(VAS_LineNo),0)+10).
        /// </summary>
        private void SetLine()
        {
            SetVAS_LineNo(DB.GetSQLValue(Get_TrxName(),
                "SELECT COALESCE(MAX(VAS_LineNo),0)+10 FROM VAS_OppLines WHERE VAS_Opportunity_ID=@param1", GetVAS_Opportunity_ID()));
        }

        /// <summary>
        /// Get parent Opportunity
        /// </summary>
        /// <returns>parent</returns>
        public MOpportunity GetOpportunity()
        {
            if (_parent == null && GetVAS_Opportunity_ID() != 0)
            {
                _parent = new MOpportunity(GetCtx(), GetVAS_Opportunity_ID(), Get_TrxName());
                if (Get_TrxName() != null)
                    _parent.Load(Get_TrxName());
            }
            return _parent;
        }

        /// <summary>
        /// Get Currency Precision (StdPrecision of the price-list currency).
        /// Adapted from MProjectLine.GetCurPrecision() to read the price-list version
        /// from VAS_Opportunity instead of C_Project.
        /// </summary>
        /// <returns>currency precision (defaults to 2)</returns>
        protected int GetCurPrecision()
        {
            if (currencyPrecision == 0)
            {
                currencyPrecision = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT StdPrecision FROM C_Currency WHERE C_Currency_ID =
                                ( SELECT C_Currency_ID FROM M_PriceList WHERE M_PriceList_ID =
                                    (SELECT M_PriceList_ID FROM M_PriceList_Version WHERE M_PriceList_Version_ID = "
                                         + (_parent != null ? _parent.GetM_PriceList_Version_ID().ToString() :
                                            " (SELECT M_PriceList_Version_ID FROM VAS_Opportunity WHERE VAS_Opportunity_ID = " + GetVAS_Opportunity_ID() + ")") + "))", null, Get_Trx()));
            }
            if (currencyPrecision == 0)
            {
                currencyPrecision = 2;
            }
            return currencyPrecision;
        }

        /// <summary>
        /// String Representation
        /// </summary>
        /// <returns>info</returns>
        public override String ToString()
        {
            StringBuilder sb = new StringBuilder("MOppLines[");
            sb.Append(Get_ID()).Append("-")
                .Append(GetVAS_LineNo())
                .Append(",VAS_Opportunity_ID=").Append(GetVAS_Opportunity_ID())
                .Append(", M_Product_ID=").Append(GetM_Product_ID())
                .Append(", PlannedQty=").Append(GetPlannedQty())
                .Append("]");
            return sb.ToString();
        }
    }
}
