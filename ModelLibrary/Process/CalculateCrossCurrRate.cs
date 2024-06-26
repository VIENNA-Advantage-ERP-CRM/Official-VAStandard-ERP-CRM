﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Model;
using VAdvantage.DataBase;
using System.Data;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VAdvantage.ProcessEngine;
namespace VAdvantage.Process
{
    public class CalculateCrossCurrRate : SvrProcess
    {
        StringBuilder retmsg = new StringBuilder();
        StringBuilder query = new StringBuilder();
        int FromCurr1 = 0;
        DateTime ValidFrom1 = new DateTime();
        DateTime ValidFrom2 = new DateTime();
        DateTime ValidTo1 = new DateTime();
        DateTime ValidTo2 = new DateTime();
        Decimal newMulRate = 0;
        Decimal newDivideRate = 0;
        Decimal MulRate1, MulRate2 = 0;
        DateTime newValidFrom = new DateTime();
        DateTime newValidTo = new DateTime();
        MConversionRate conobj;
        MCurrCrossRate Currobj;
        DataSet dsobj;

        /// <summary>
        /// Process to get Cross Currency Rate
        /// </summary>
        /// <returns>message</returns>
        protected override string DoIt()
        {

            return CreateCrossCurrency();
        }

        protected override void Prepare()
        {
        }

        /// <summary>
        /// Calculate Cross Currency Rate
        /// </summary>
        /// <param name="CommonCurrency_ID">Common Currency ID</param>
        /// <param name="date">Date (the day of which system look record for Cross rate calulation) </param>
        /// <writer>VIS_045</writer>
        /// <Date>01-May-2024</Date>
        /// <Task-ID>FEATURE 5703</Task-ID>
        /// <returns>Message</returns>
        public string CreateCrossCurrency(int CommonCurrency_ID = 0, DateTime? date = null)
        {
            // Getting records from cross rate setting
            query.Append("SELECT * FROM C_CurrCrossRate WHERE IsActive='Y'");
            if (CommonCurrency_ID != 0)
            {
                query.Append($" AND C_Currency_ID = {CommonCurrency_ID}");
            }
            dsobj = DB.ExecuteDataset(query.ToString());
            query.Clear();

            if (dsobj != null && dsobj.Tables.Count > 0 && dsobj.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow dr in dsobj.Tables[0].Rows)
                {
                    Currobj = new MCurrCrossRate(GetCtx(), dr, Get_Trx());
                    // Getting records from currency rate based on conditions
                    if (date == null)
                    {
                        date = DateTime.Now.Date;
                    }
                    query.Append("SELECT AD_Org_ID,C_Currency_ID,ValidFrom,ValidTo,MultiplyRate FROM C_Conversion_Rate WHERE " + GlobalVariable.TO_DATE(date, true) +
                        " BETWEEN ValidFrom AND ValidTo AND IsActive='Y' AND AD_Org_ID=" + Currobj.GetAD_Org_ID() + " AND C_ConversionType_ID=" + Currobj.GetC_ConversionType_ID() +
                        " AND C_Currency_To_ID=" + Currobj.GetC_Currency_ID() + " AND C_Currency_ID IN ('" + Currobj.GetC_Currency_From_ID() + "','" + Currobj.GetC_Currency_To_ID() + "')");
                    dsobj = DB.ExecuteDataset(query.ToString());
                    query.Clear();
                    if (dsobj != null && dsobj.Tables.Count > 0 && dsobj.Tables[0].Rows.Count == 2)
                    {
                        for (int j = 0; j < dsobj.Tables[0].Rows.Count; j++)
                        {
                            FromCurr1 = Util.GetValueOfInt(dsobj.Tables[0].Rows[j]["C_Currency_ID"]);
                            // Getting multiply rate from both records
                            if (Currobj.GetC_Currency_From_ID() == FromCurr1)
                            {
                                ValidFrom1 = Convert.ToDateTime(dsobj.Tables[0].Rows[j]["ValidFrom"]);
                                ValidTo1 = Convert.ToDateTime(dsobj.Tables[0].Rows[j]["ValidTo"]);
                                MulRate1 = Util.GetValueOfDecimal(dsobj.Tables[0].Rows[j]["MultiplyRate"]);
                            }
                            else if (Currobj.GetC_Currency_To_ID() == FromCurr1)
                            {
                                ValidFrom2 = Convert.ToDateTime(dsobj.Tables[0].Rows[j]["ValidFrom"]);
                                ValidTo2 = Convert.ToDateTime(dsobj.Tables[0].Rows[j]["ValidTo"]);
                                MulRate2 = Util.GetValueOfDecimal(dsobj.Tables[0].Rows[j]["MultiplyRate"]);
                            }
                            else
                            {
                                retmsg.Append("Error: " + Msg.GetMsg(GetCtx(), "VIS_CurrNotMatch"));
                            }
                        }
                        // Calculating new multiply rate & divide rate
                        if (MulRate2 > 0)
                        {
                            newMulRate = MulRate1 / MulRate2;
                            newDivideRate = 1 / newMulRate;
                        }
                        else
                        {
                            retmsg.Append("Error: " + Msg.GetMsg(GetCtx(), "ProductUOMConversionRateError"));
                        }

                        // Geting Valid from and valid to dates
                        if (ValidFrom1 <= ValidFrom2 && ValidTo1 <= ValidTo2)
                        {
                            newValidFrom = ValidFrom2;
                            newValidTo = ValidTo1;
                        }
                        else if (ValidFrom1 >= ValidFrom2 && ValidTo1 >= ValidTo2)
                        {
                            newValidFrom = ValidFrom1;
                            newValidTo = ValidTo2;
                        }
                        else if (ValidFrom1 <= ValidFrom2 && ValidTo1 >= ValidTo2)
                        {
                            newValidFrom = ValidFrom2;
                            newValidTo = ValidTo2;
                        }
                        else if (ValidFrom1 >= ValidFrom2 && ValidTo1 <= ValidTo2)
                        {
                            newValidFrom = ValidFrom1;
                            newValidTo = ValidTo1;
                        }
                        else
                        {
                            retmsg.Append(Msg.GetMsg(GetCtx(), "VIS_InvalidDate"));
                        }
                        query.Clear();
                        // Inserting new conversion in the currency rate
                        conobj = new MConversionRate(GetCtx(), 0, Get_Trx());
                        conobj.SetAD_Client_ID(Currobj.GetAD_Client_ID());
                        conobj.SetAD_Org_ID(Currobj.GetAD_Org_ID());
                        conobj.SetC_Currency_ID(Currobj.GetC_Currency_From_ID());
                        conobj.SetC_Currency_To_ID(Currobj.GetC_Currency_To_ID());
                        conobj.SetC_ConversionType_ID(Currobj.GetC_ConversionType_ID());
                        conobj.SetValidFrom(newValidFrom);
                        conobj.SetValidTo(newValidTo);
                        conobj.SetMultiplyRate(newMulRate);
                        conobj.SetDivideRate(newDivideRate);
                        if (!conobj.Save())
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            if (pp != null)
                            {
                                retmsg.Append(Msg.GetMsg(GetCtx(), "VIS_CrossRateNotCal") + ":- " + pp.GetName() + Currobj.GetName() + ", ");
                            }
                            else
                            {
                                retmsg.Append(Msg.GetMsg(GetCtx(), "VIS_CrossRateNotCal"));
                            }
                        }
                        else
                        {
                            retmsg.Append(Msg.GetMsg(GetCtx(), "VIS_CrossRateCal") + ":- " + Currobj.GetName() + ", ");
                        }
                    }
                    else
                    {
                        retmsg.Append(Msg.GetMsg(GetCtx(), "VIS_CrossRateNotCal") + ":- " + Currobj.GetName() + ", ");
                    }
                }
            }
            else
            {
                retmsg.Append(Msg.GetMsg(GetCtx(), "NoRecords"));
            }
            return retmsg.ToString();
        }
    }
}
