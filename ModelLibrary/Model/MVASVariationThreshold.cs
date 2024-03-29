﻿/********************************************************
 * Module Name    : 
 * Purpose        : 
 * Class Used     : X_VAS_VariationThreshold
 * Chronological Development
 * Lakhwinder Singh     2-Jun-2023
 ******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Utility;

namespace VAdvantage.Model
{
    class MVASVariationThreshold : X_VAS_VariationThreshold
    {
        public MVASVariationThreshold(Ctx ctx, int VAS_ContractCategory_ID, Trx Trx_Name)
           : base(ctx, VAS_ContractCategory_ID, Trx_Name)
        {
        }
        public MVASVariationThreshold(Ctx ctx, DataRow dr, Trx trx)
            : base(ctx, dr, trx)
        {
        }

       
        /// <summary>
        /// Before Save
        /// </summary>
        /// <param name="newRecord"></param>
        /// <returns></returns>
        protected override bool BeforeSave(bool newRecord)
        {
            if (GetValidTo() < GetValidFrom())
            {
                log.SaveError("", Msg.GetMsg(GetCtx(),"ToDateMustGreater"));
                return false;
            }
            string sql = @"SELECT COUNT(VAS_VariationThreshold_ID)
                            FROM VAS_VariationThreshold
                            WHERE IsActive='Y' AND AD_Client_ID=" + GetAD_Client_ID() + @" AND AD_Org_ID=" + GetAD_Org_ID() + @"
                            AND( " + GlobalVariable.TO_DATE(GetValidFrom(), true) + @" <= ValidTo)
                            AND( " + GlobalVariable.TO_DATE(GetValidTo(), true) + @" >=ValidFrom)
                            AND VAS_VariationThreshold_ID<>" + GetVAS_VariationThreshold_ID();
            int count = Util.GetValueOfInt (DB.ExecuteScalar(sql,null,Get_Trx()));
            if (count == 0)
            { return true; }
            log.SaveError("", Msg.GetMsg(GetCtx(),"VAS_InvalidVariationDate"));

            return false;
        }
    }
}
