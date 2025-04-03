using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using VAdvantage.Utility;
using System.Text;
using System.Data;
using VAdvantage.ProcessEngine;
using VAdvantage.Logging;
using VAdvantage.Model;
using System.Dynamic;

namespace VASLogic.Models
{
    public class VAS_LeadModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_LeadModel).FullName);
        public string UserImage(string rec_ID)
        {
            string imgurl = Util.GetValueOfString(DB.ExecuteScalar(@"SELECT i.imageurl FROM C_Lead c 
            INNER JOIN AD_User u ON (c.SalesRep_ID = u.AD_User_ID) LEFT JOIN AD_Image i ON (u.AD_Image_ID = i.AD_Image_ID)
            WHERE c.C_Lead_ID=" + rec_ID));
            return imgurl;
        }

        public dynamic GetThreadID(string rec_ID)
        {
            dynamic retObj = new ExpandoObject();
            retObj.APiKey = Util.GetValueOfString(System.Web.Configuration.WebConfigurationManager.AppSettings["OpenAIAPIKey"]);
            retObj.ThreadID = Util.GetValueOfString(DB.ExecuteScalar("SELECT VA061_ThreadID FROM C_Lead WHERE C_Lead_ID=" + Util.GetValueOfInt(rec_ID)));
            return retObj;
        }

        public bool UpdateThreadID(string fields)
        {
            string[] val = fields.Split(',');
            if (Util.GetValueOfInt(DB.ExecuteQuery("UPDATE C_Lead SET VA061_ThreadID='" + val[1] + "' WHERE C_Lead_ID=" + Util.GetValueOfInt(val[0]))) > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public string GetPromptMsg(int tableID, int recordID)
        {
            StringBuilder sql = new StringBuilder();
            StringBuilder result = new StringBuilder();
            DataSet ds = null;
            if (tableID == 923)
            {
                sql.Append(@"SELECT c.AD_Org_ID, c.C_Lead_ID, c.DocumentNo, c.Name AS LeadName, c.BPName AS CompName,
                        u.Name AS UserName 
                        FROM C_Lead c LEFT JOIN AD_User u ON (c.AD_USER_ID=u.AD_User_ID)
                        WHERE C_Lead_ID=" + Util.GetValueOfInt(recordID));
                ds = DB.ExecuteDataset(sql.ToString());
                if (ds != null && ds.Tables[0].Rows.Count > 0)
                {
                    result.Append(" RecPromptMsg Lead ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Lead_ID"]) +
                        " Lead No. = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["DocumentNo"]) +
                        " Lead Name = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["LeadName"]) +
                        " Compant Name = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["CompName"]));
                }
            }
            else
            {
                int PriceList_ID = 0;
                sql.Append(@"SELECT c.C_Lead_ID, l.DocumentNo, c.C_Project_ID, c.Value, c.Name, CASE WHEN c.C_BPartner_ID > 0 
                        THEN cb.Name ELSE ps.Name END AS CompName, u.Name AS UserName, c.M_PriceList_ID 
                        FROM C_Project c LEFT JOIN C_Lead l ON (c.C_Lead_ID=l.C_Lead_ID)
                        LEFT JOIN C_BPartner cb ON (c.C_BPartner_ID=cb.C_BPartner_ID)
                        LEFT JOIN C_BPartner ps ON (c.C_BPartnerSR_ID=ps.C_BPartner_ID)
                        LEFT JOIN AD_User u ON (c.AD_User_ID=u.AD_User_ID)
                        WHERE c.C_Project_ID=" + Util.GetValueOfInt(recordID));
                ds = DB.ExecuteDataset(sql.ToString());
                if (ds != null && ds.Tables[0].Rows.Count > 0)
                {
                    result.Append(" RecPromptMsg Opportunity ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Project_ID"]) +
                        " Opportunity No. = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["Value"]) +
                        " Opportunity Name = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["Name"]) +
                        " Compant Name = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["CompName"]));

                    if (Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Lead_ID"]) > 0)
                    {
                        result.Append(" Lead ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Lead_ID"]) +
                        " Lead No. = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["DocumentNo"]));
                    }
                    PriceList_ID = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_PriceList_ID"]);
                }

                //sql.Clear();
                //sql.Append(@"SELECT p.M_Product_ID, p.Value, p.C_UOM_ID, p.Name AS ProductName, 
                //        u.Name AS UOM, NVL(pp.PriceStd,0) AS Price
                //        FROM M_Product p INNER JOIN C_UOM u ON (u.C_UOM_ID=p.C_UOM_ID) 
                //        LEFT JOIN M_ProductPrice pp ON (pp.M_Product_ID=p.M_Product_ID 
                //        AND NVL(pp.C_UOM_ID, 0)=p.C_UOM_ID AND NVL(pp.M_AttributeSetInstance_ID, 0)=0 
                //        AND pp.M_PriceList_Version_ID=(SELECT M_PriceList_Version_ID 
                //        FROM M_PriceList_Version WHERE M_PriceList_ID=" + PriceList_ID +
                //        @" AND VALIDFROM <= SYSDATE ORDER BY VALIDFROM DESC, M_Pricelist_Version_ID DESC FETCH FIRST ROW ONLY ))
                //        WHERE p.Value IN ('1000023', '1000024', '1000025', '1000026', '1000027', '1000028', '1000029', '1000030', 
                //        '1000031', '1000032', '1000033', '1000034', '1000035')");
                //ds = DB.ExecuteDataset(sql.ToString());
                //if (ds != null && ds.Tables[0].Rows.Count > 0)
                //{
                //    result.Append("Product Information: Product ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Product_ID"]) +
                //        " Serach Key = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["Value"]) +
                //        " Product Name = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["ProductName"]) +
                //        " UOM ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_UOM_ID"]) +
                //        " UOM = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["UOM"]) +
                //        " Price = " + Util.GetValueOfString(ds.Tables[0].Rows[0]["Price"]));
                //}
            }
            return result.ToString();
        }

        public string ConvertProspect(Ctx ctx, int Record_ID)
        {
            string processMsg;
            int AD_Process_ID = 357;
            MPInstance instance = new MPInstance(ctx, 357, 0);
            if (!instance.Save())
            {
                processMsg = Msg.GetMsg(ctx, "ProcessNoInstance"); ;
                log.SaveError("Error=" + Msg.GetMsg(ctx, "ProcessNoInstance"), "AD_Process_ID=" + AD_Process_ID);
                return processMsg;
            }

            VAdvantage.ProcessEngine.ProcessInfo pi = new VAdvantage.ProcessEngine.ProcessInfo("", AD_Process_ID);
            pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
            pi.SetAD_Client_ID(ctx.GetAD_Client_ID());
            pi.SetAD_User_ID(ctx.GetAD_User_ID());
            pi.SetRecord_ID(Record_ID);

            ProcessCtl worker = new ProcessCtl(ctx, null, pi, null);
            worker.Run();
            processMsg = pi.GetSummary();
            return processMsg;
        }

        public string GenerateOpprtunity(Ctx ctx, int Record_ID)
        {
            string processMsg;
            int AD_Process_ID = 357;
            MPInstance instance = new MPInstance(ctx, 1000143, 0);
            if (!instance.Save())
            {
                processMsg = Msg.GetMsg(ctx, "ProcessNoInstance"); ;
                log.SaveError("Error=" + Msg.GetMsg(ctx, "ProcessNoInstance"), "AD_Process_ID=" + AD_Process_ID);
                return processMsg;
            }

            VAdvantage.ProcessEngine.ProcessInfo pi = new VAdvantage.ProcessEngine.ProcessInfo("", AD_Process_ID);
            pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
            pi.SetAD_Client_ID(ctx.GetAD_Client_ID());
            pi.SetAD_User_ID(ctx.GetAD_User_ID());
            pi.SetRecord_ID(Record_ID);

            ProcessCtl worker = new ProcessCtl(ctx, null, pi, null);
            worker.Run();
            processMsg = pi.GetSummary();
            return processMsg;
        }

        public string GenerateLines(Ctx ctx, int Record_ID, List<ProductData> data)
        {
            string processMsg = "";
            if (data != null && data.Count > 0)
            {
                DataSet ds = DB.ExecuteDataset("SELECT AD_Org_ID FROM C_Project WHERE C_Project_ID = " + Record_ID);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int AD_Org_ID = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                    for (int i = 0; i < data.Count; i++)
                    {

                        MProjectLine pline = new MProjectLine(ctx, 0, null);
                        pline.SetClientOrg(ctx.GetAD_Client_ID(), AD_Org_ID);
                        pline.SetC_Project_ID(Record_ID);
                        pline.SetM_Product_ID(Util.GetValueOfInt(data[i].M_Product_ID));
                        pline.Set_Value("C_UOM_ID", Util.GetValueOfInt(data[i].C_UOM_ID));
                        pline.SetPlannedQty(Util.GetValueOfDecimal(data[i].Quantity));
                        pline.SetPlannedPrice(Util.GetValueOfDecimal(data[i].Price));

                        if (!pline.Save())
                        {
                            ValueNamePair vp = VLogger.RetrieveError();
                            if (vp != null)
                            {
                                string val = vp.GetName();
                                if (String.IsNullOrEmpty(val))
                                {
                                    val = vp.GetValue();
                                }
                                if (String.IsNullOrEmpty(val))
                                {
                                    val = Msg.GetMsg(ctx, "ErrorInSaving");
                                }
                                processMsg = val;
                            }
                        }
                    }
                }
            }
            else
            {
                processMsg = Msg.GetMsg(ctx, "NoDataFound");
            }
            return processMsg;
        }
    }
    public class ProductData
    {
        public string M_Product_ID { set; get; }
        public string C_UOM_ID { set; get; }
        public decimal Quantity { set; get; }
        public decimal Price { set; get; }
    }
}