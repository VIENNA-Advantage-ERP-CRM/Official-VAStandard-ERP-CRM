﻿/********************************************************
 * Module Name    : 
 * Purpose        : Inventory Movement Confirmation Line
 * Class Used     : X_M_MovementLineConfirm
 * Chronological Development
 * Veena         27-Oct-2009
 ******************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.Common;
using VAdvantage.Process;
using VAdvantage.DataBase;
using VAdvantage.Utility;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.Logging;

namespace VAdvantage.Model
{
    /// <summary>
    /// Inventory Movement Confirmation Line
    /// </summary>
    public class MMovementLineConfirm : X_M_MovementLineConfirm
    {
        /**	Movement Line			*/
        private MMovementLine _line = null;

        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="M_MovementLineConfirm_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MMovementLineConfirm(Ctx ctx, int M_MovementLineConfirm_ID, Trx trxName)
            : base(ctx, M_MovementLineConfirm_ID, trxName)
        {
            if (M_MovementLineConfirm_ID == 0)
            {
                //	SetM_MovementConfirm_ID (0);	Parent
                //	SetM_MovementLine_ID (0);
                SetConfirmedQty(Env.ZERO);
                SetDifferenceQty(Env.ZERO);
                SetScrappedQty(Env.ZERO);
                SetTargetQty(Env.ZERO);
                SetProcessed(false);
            }
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="dr">data row</param>
        /// <param name="trxName">transation</param>
        public MMovementLineConfirm(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {
        }

        /// <summary>
        /// Parent constructor
        /// </summary>
        /// <param name="parent">parent</param>
        public MMovementLineConfirm(MMovementConfirm parent)
            : this(parent.GetCtx(), 0, parent.Get_TrxName())
        {
            SetClientOrg(parent);
            SetM_MovementConfirm_ID(parent.GetM_MovementConfirm_ID());
        }

        /// <summary>
        /// Set Movement Line
        /// </summary>
        /// <param name="line">line</param>
        public void SetMovementLine(MMovementLine line)
        {
            SetM_MovementLine_ID(line.GetM_MovementLine_ID());
            SetTargetQty(line.GetMovementQty());
            //Amit 25-nov-2014
            // SetConfirmedQty(GetTargetQty());	//	suggestion
            SetConfirmedQty(0);
            //amit
            _line = line;

            // VIS0060: Set Trx Org from Movement to Confirmation
            if (Util.GetValueOfInt(line.Get_Value("AD_OrgTrx_ID")) > 0)
            {
                Set_Value("AD_OrgTrx_ID", Util.GetValueOfInt(line.Get_Value("AD_OrgTrx_ID")));
            }
        }

        /// <summary>
        /// Get Movement Line
        /// </summary>
        /// <returns>line</returns>
        public MMovementLine GetLine()
        {
            if (_line == null)
                _line = new MMovementLine(GetCtx(), GetM_MovementLine_ID(), Get_TrxName());
            return _line;
        }

        /// <summary>
        /// Process Confirmation Line.
        ///	- Update Movement Line
        /// </summary>
        /// <returns>success</returns>
        public Boolean ProcessLine()
        {
            MMovementLine line = GetLine();

            //Lakhwinder
            //Apply UOM Conversion Logic
            MProduct _Pro = new MProduct(GetCtx(), line.GetM_Product_ID(), Get_TrxName());
            if (GetC_UOM_ID() != _Pro.GetC_UOM_ID())
            {

                line.SetTargetQty(Util.GetValueOfDecimal(MUOMConversion.ConvertProductFrom(GetCtx(), line.GetM_Product_ID(), GetC_UOM_ID(), GetTargetQty())));
                line.SetMovementQty(Util.GetValueOfDecimal(MUOMConversion.ConvertProductFrom(GetCtx(), line.GetM_Product_ID(), GetC_UOM_ID(), GetConfirmedQty())));
                line.SetConfirmedQty(Util.GetValueOfDecimal(MUOMConversion.ConvertProductFrom(GetCtx(), line.GetM_Product_ID(), GetC_UOM_ID(), GetConfirmedQty())));
                line.SetScrappedQty(Util.GetValueOfDecimal(MUOMConversion.ConvertProductFrom(GetCtx(), line.GetM_Product_ID(), GetC_UOM_ID(), GetScrappedQty())));
            }
            else
            {
                line.SetTargetQty(GetTargetQty());
                line.SetMovementQty(GetConfirmedQty());
                line.SetConfirmedQty(GetConfirmedQty());
                line.SetScrappedQty(GetScrappedQty());
            }

            return line.Save(Get_TrxName());
        }

        //Handle Reverse case
        public Boolean ProcessLineReverse()
        {
            MMovementLine line = GetLine();

            line.SetTargetQty(line.GetQtyEntered());
            line.SetMovementQty(line.GetQtyEntered());
            line.SetConfirmedQty(0);
            line.SetScrappedQty(0);

            return line.Save(Get_TrxName());
        }

        /// <summary>
        /// Is Fully Confirmed
        /// </summary>
        /// <returns>true if TarGet = Confirmed qty</returns>
        public Boolean IsFullyConfirmed()
        {
            return GetTargetQty().CompareTo(GetConfirmedQty()) == 0;
        }

        /// <summary>
        /// Before Delete - do not delete
        /// </summary>
        /// <returns>false</returns>
        protected override Boolean BeforeDelete()
        {
            return false;
        }

        /// <summary>
        /// Before Save
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <returns>true</returns>
        protected override Boolean BeforeSave(Boolean newRecord)
        {
            //	Calculate Difference = Target - Confirmed - Scrapped
            //Decimal difference = GetTargetQty();
            //difference = Decimal.Subtract(difference, GetConfirmedQty());
            //difference = Decimal.Subtract(difference, GetScrappedQty());
            //SetDifferenceQty(difference);
            //
            return true;
        }
        /// <summary>
        /// VAI050-This method used to apply logic after saving the record
        /// </summary>
        /// <param name="newRecord"></param>
        /// <param name="success"></param>
        /// <returns></returns>
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
                return success;
            if (!newRecord && Is_ValueChanged("VAS_ReceivingLocator_ID"))
            {
                MMovementLine mLine = new MMovementLine(GetCtx(), GetM_MovementLine_ID(), Get_Trx());
                mLine.SetM_LocatorTo_ID(Get_ValueAsInt("VAS_ReceivingLocator_ID"));
                if (!mLine.Save())
                {
                    return false;
                }
            }

            return true;
        }
    }
}
