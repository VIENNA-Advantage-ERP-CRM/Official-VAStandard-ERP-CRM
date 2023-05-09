using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VAdvantage.Process
{
    class CreateVRHeader : SvrProcess
    {
        protected override string DoIt()
        {
            string res = "Done";

            StringBuilder sb = new StringBuilder(@"SELECT rr.C_RFQ_ID,
                                                     rr.AD_Client_ID,
                                                     rr.AD_Org_ID,
                                                     rr.C_RFQ_ID,
                                                     r.Name ,
                                                     r.DocumentNo,
                                                     rr.CreatedBy,
                                                     rr.UpdatedBy
                                                FROM C_RfqResponse rr 
                                                 INNER JOIN C_RFQ r ON(r.C_RFQ_ID = rr.C_RFQ_ID)
                                                 WHERE rr.VAS_Response_ID IS NULL AND rr.IsActive='Y'");
            DataSet ds = DB.ExecuteDataset(sb.ToString());
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return "VIS_NoRecord";
            }
            List<int> processedRfqs = new List<int>();
            int rfqid = 0;
            int vasResonseID = 0;
            Trx trx = Trx.GetTrx("CreateVRHeader" + DateTime.Now.Ticks);
            for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
            {
                rfqid = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_RFQ_ID"]);
                if (processedRfqs.Contains(rfqid))
                { continue; }
                processedRfqs.Add(rfqid);
                vasResonseID = DB.GetNextID(GetCtx(), "VAS_Response", trx);
                sb.Clear();
                sb.Append(@"INSERT INTO VAS_Response (
                                AD_Client_ID    ,
                                AD_Org_ID       ,
                                C_RFQ_ID        ,
                                Created         ,
                                CreatedBy       ,
                                Description     ,
                                DocumentNo      ,
                                Help            ,
                                IsActive,
                                Name,
                                Updated,
                                UpdatedBy,
                                VAS_Response_ID )
                             VALUES(     ");

                sb.Append(ds.Tables[0].Rows[i]["AD_Client_ID"] + ",");
                sb.Append(ds.Tables[0].Rows[i]["AD_Org_ID"] + ",");
                sb.Append(rfqid + ",");
                sb.Append(GlobalVariable.TO_DATE(DateTime.Now, false) + ",");
                sb.Append(ds.Tables[0].Rows[i]["CreatedBy"] + ",");
                sb.Append("'" + ds.Tables[0].Rows[i]["Name"] + "',");
                sb.Append("'" + ds.Tables[0].Rows[i]["DocumentNo"] + "',");
                sb.Append("'" + ds.Tables[0].Rows[i]["Name"] + "',");
                sb.Append("'Y',");
                sb.Append("'" + ds.Tables[0].Rows[i]["Name"] + "',");
                sb.Append(GlobalVariable.TO_DATE(DateTime.Now, false) + ",");
                sb.Append(ds.Tables[0].Rows[i]["UpdatedBy"] + ",");
                sb.Append(vasResonseID);
                sb.Append(")");

                if (DB.ExecuteQuery(sb.ToString(), null, trx) < 1)
                {
                    trx.Rollback();
                    trx.Close();
                    ValueNamePair pp = VLogger.RetrieveError();
                    res = pp != null ? pp.GetValue() : "VAS_ResponseNotSaved";
                    return res;
                }

                sb.Clear();
                sb.Append("UPDATE C_RfqResponse SET VAS_Response_ID=" + vasResonseID + " WHERE  C_RFQ_ID="+rfqid);
                if (DB.ExecuteQuery(sb.ToString(), null, trx) < 1)
                {
                    trx.Rollback();
                    trx.Close();
                    ValueNamePair pp = VLogger.RetrieveError();
                    res = pp != null ? pp.GetValue() : "VAS_RFQResponseNotSaved";
                    return res;
                }
            }
            trx.Commit();
            trx.Close();
            return res;
        }

        protected override void Prepare()
        {
           // throw new NotImplementedException();
        }
    }
}
