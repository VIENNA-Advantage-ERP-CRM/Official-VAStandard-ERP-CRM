using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using VAdvantage.Utility;
using VAdvantage.DataBase;

namespace VAdvantage.Model
{
    public class MSLAGoal : X_PA_SLA_Goal
    {
        /** 100									*/
        private const Decimal HUNDRED = 100.0M;

        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="PA_SLA_Goal_ID">Criteria Goal ID</param>
        /// <param name="trxName">Transaction</param>
        public MSLAGoal(Ctx ctx, int PA_SLA_Goal_ID, Trx trxName)
            : base(ctx, PA_SLA_Goal_ID, trxName)
        {
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="dr">Data Row</param>
        /// <param name="trxName">Transaction</param>
        public MSLAGoal(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {
        }

        /// <summary>
        /// Before Save
        /// </summary>
        /// <param name="newRecord">New Record</param>
        /// <returns>true</returns>
        protected override bool BeforeSave(bool newRecord)
        {
            if (Env.IsModuleInstalled("VA068_") && (Is_ValueChanged("VA068_WeightagePertage") || Is_ValueChanged("IsActive")))
            {
                log.Fine("beforeSave");
                Set_Value("IsValid", false);
            }
            return true;
        }

        /// <summary>
        /// After Save
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <param name="success">success</param>
        /// <returns>success</returns>
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
            {
                return success;
            }

            if (Env.IsModuleInstalled("VA068_") && (newRecord || Is_ValueChanged("VA068_WeightagePertage") || Is_ValueChanged("IsActive")))
            {
                log.Fine("afterSave");
                Validate();

                //System should give the warning message on any change on Performance Criteria, if Performance Criteria is used in transaction.
                if (Is_Changed() && !newRecord)
                {
                    string sql = @" SELECT SUM(COUNT) FROM (
                              SELECT COUNT(VA068_Vendor_ID) AS COUNT FROM VA068_Vendor WHERE IsActive = 'Y' AND PA_SLA_Criteria_ID = " + GetPA_SLA_Criteria_ID() +
                                  @" UNION ALL 
                              SELECT COUNT(C_BPartner_ID) AS COUNT FROM C_BPartner WHERE IsActive = 'Y' AND PA_SLA_Criteria_ID = " + GetPA_SLA_Criteria_ID() +
                                  " ) t";
                    int no = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_Trx()));
                    if (no > 0)
                    {
                        log.SaveWarning("", Msg.GetMsg(GetCtx(), "VIS_ConflictChanges"));
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// After Delete
        /// </summary>
        /// <param name="success">success</param>
        /// <returns>true if deleted</returns>
        protected override bool AfterDelete(bool success)
        {
            // Check Record is Valid or Not
            if (Env.IsModuleInstalled("VA068_"))
            {
                Validate();
            }
            return true;
        }        

        /// <summary>
        /// Validate SLA Criteria & Goal
        /// </summary>
        /// <returns>Validation Message OK or error</returns>
        public string Validate()
        {
            string sql = "SELECT VA068_WeightagePertage FROM PA_SLA_Goal WHERE IsActive = 'Y' AND PA_SLA_Criteria_ID=" + GetPA_SLA_Criteria_ID();
            DataSet ds = DB.ExecuteDataset(sql, null, Get_Trx());
            Decimal total = Env.ZERO;

            //	Add up
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    total = decimal.Add(total, Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["VA068_WeightagePertage"]));
                }
            }

            bool valid = total.CompareTo(HUNDRED) == 0;

            int no = DB.ExecuteQuery("UPDATE PA_SLA_Criteria SET IsValid = " + (valid ? "'Y'" : "'N'") + " WHERE PA_SLA_Criteria_ID = "
                + GetPA_SLA_Criteria_ID(), null, Get_Trx());

            if (no > 0)
            {
                no = DB.ExecuteQuery("UPDATE PA_SLA_Goal SET IsValid = " + (valid ? "'Y'" : "'N'") + " WHERE PA_SLA_Criteria_ID = "
                                + GetPA_SLA_Criteria_ID(), null, Get_Trx());
            }

            String msg = "@OK@";
            if (!valid)
                msg = "@Total@ = " + total + " - @Difference@ = " + decimal.Subtract(HUNDRED, total);
            return Msg.ParseTranslation(GetCtx(), msg);
        }
    }
}
