using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.DataContracts;

namespace VASLogic.Models
{
    public class VAS_RequestModel
    {
        string strQuery = "";

        //count of request
        public int getRequestCnt(Ctx ctx)
        {
            int ncnt = 0;
            try
            {
                //To Get Request count                
                strQuery = @" SELECT  count(R_Request.r_request_id) FROM R_Request
                        LEFT OUTER JOIN C_BPartner
                        ON (R_Request.C_BPartner_ID=C_BPartner.C_BPartner_ID)
                        LEFT OUTER JOIN r_requesttype rt
                        ON (R_Request.r_requesttype_id = rt.r_requesttype_ID)
                        LEFT OUTER JOIN R_Status rs
                        ON (rs.R_Status_ID=R_request.R_Status_ID)
                        LEFT OUTER JOIN ad_ref_list adl
                        ON (adl.Value=R_Request.Priority)
                        INNER JOIN AD_reference adr
                        ON (adr.AD_Reference_ID=adl.AD_Reference_ID) ";

                strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "R_Request", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                strQuery += "  AND adr.Name='_PriorityRule'  AND ( R_Request.SalesRep_ID =" + ctx.GetAD_User_ID() + " OR R_Request.AD_Role_ID =" + ctx.GetAD_Role_ID() + ")"
                 + " AND R_Request.Processed ='N' AND R_Request.VAS_IsRead = 'N'"
                + " AND (R_Request.R_Status_ID IS NULL OR R_Request.R_Status_ID IN (SELECT R_Status_ID FROM R_Status WHERE IsClosed='N'))";

                DataSet dsData = DB.ExecuteDataset(strQuery);
                ncnt = Util.GetValueOfInt(dsData.Tables[0].Rows[0][0].ToString());
            }
            catch (Exception)
            {

            }
            return ncnt;
        }
        //List of Request
        public List<HomeRequest> getHomeRequest(Ctx ctx, int PageSize, int page)
        {
            List<HomeRequest> lstAlerts = new List<HomeRequest>();

            strQuery = @" SELECT C_BPartner.Name ,
                          rt.Name AS CaseType,
                          R_Request.DocumentNo ,
                          R_Request.Summary ,
                          R_Request.StartDate ,
                          R_Request.DateNextAction,
                          R_Request.Created,
                          R_Request.R_Request_ID,
                          R_Request.Priority AS PriorityID,
                          adl.Name           AS Priority,
                          rs.name            AS Status,
                          R_Request.VAS_IsRead,
                          (SELECT AD_Table.TableName FROM AD_Table WHERE AD_Table.TableName='R_Request'
                          ) TableName,
                          (SELECT AD_Table.Ad_Window_ID
                          FROM AD_Table
                          WHERE AD_Table.TableName='R_Request'
                          ) AD_Window_ID
                        FROM R_Request
                        LEFT OUTER JOIN C_BPartner
                        ON (R_Request.C_BPartner_ID=C_BPartner.C_BPartner_ID)
                        LEFT OUTER JOIN r_requesttype rt
                        ON (R_Request.r_requesttype_id = rt.r_requesttype_ID)
                        LEFT OUTER JOIN R_Status rs
                        ON (rs.R_Status_ID=R_request.R_Status_ID)
                        LEFT OUTER JOIN ad_ref_list adl
                        ON (adl.Value=R_Request.Priority)
                        JOIN AD_reference adr
                        ON (adr.AD_Reference_ID=adl.AD_Reference_ID) ";

            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "R_Request", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            strQuery += "  AND adr.Name='_PriorityRule' AND ( R_Request.SalesRep_ID =" + ctx.GetAD_User_ID() + " OR R_Request.AD_Role_ID =" + ctx.GetAD_Role_ID() + ")"
            + " AND R_Request.Processed ='N' AND (R_Request.R_Status_ID IS NULL OR R_Request.R_Status_ID IN (SELECT R_Status_ID FROM R_Status WHERE IsClosed='N')) ORDER By R_Request.Updated, R_Request.Priority ";
            // change to sort Requests based on updated date and time

            SqlParamsIn objSP = new SqlParamsIn();
            objSP.page = page;
            objSP.pageSize = PageSize;
            objSP.sql = strQuery;
            DataSet dsData = VIS.DBase.DB.ExecuteDatasetPaging(objSP.sql, objSP.page, objSP.pageSize);
            if (dsData != null)
            {
                dsData = VAdvantage.DataBase.DB.SetUtcDateTime(dsData);
                for (int i = 0; i < dsData.Tables[0].Rows.Count; i++)
                {
                    var Alrt = new HomeRequest();
                    Alrt.R_Request_ID = Util.GetValueOfInt(dsData.Tables[0].Rows[i]["R_Request_ID"]);
                    Alrt.AD_Window_ID = Util.GetValueOfInt(dsData.Tables[0].Rows[i]["AD_Window_ID"]);
                    Alrt.TableName = Util.GetValueOfString(dsData.Tables[0].Rows[i]["TableName"]);
                    Alrt.Name = Util.GetValueOfString(dsData.Tables[0].Rows[i]["Name"]);
                    Alrt.CaseType = Util.GetValueOfString(dsData.Tables[0].Rows[i]["CaseType"]);
                    Alrt.DocumentNo = Util.GetValueOfString(dsData.Tables[0].Rows[i]["DocumentNo"]);
                    Alrt.Status = Util.GetValueOfString(dsData.Tables[0].Rows[i]["Status"]);
                    Alrt.Priority = Util.GetValueOfString(dsData.Tables[0].Rows[i]["Priority"]);
                    Alrt.Summary = Util.GetValueOfString(dsData.Tables[0].Rows[i]["Summary"]);
                    Alrt.IsRead = Util.GetValueOfString(dsData.Tables[0].Rows[i]["VAS_IsRead"]);

                    DateTime _DateNextAction = new DateTime();
                    if (dsData.Tables[0].Rows[i]["DateNextAction"].ToString() != null && dsData.Tables[0].Rows[i]["DateNextAction"].ToString() != "")
                    {
                        _DateNextAction = Convert.ToDateTime(dsData.Tables[0].Rows[i]["DateNextAction"].ToString());
                        DateTime _format = DateTime.SpecifyKind(new DateTime(_DateNextAction.Year, _DateNextAction.Month, _DateNextAction.Day, _DateNextAction.Hour, _DateNextAction.Minute, _DateNextAction.Second), DateTimeKind.Utc);
                        _DateNextAction = _format;
                        Alrt.NextActionDate = _format;
                    }

                    DateTime _createdDate = new DateTime();
                    if (dsData.Tables[0].Rows[i]["created"].ToString() != null && dsData.Tables[0].Rows[i]["created"].ToString() != "")
                    {
                        _createdDate = Convert.ToDateTime(dsData.Tables[0].Rows[i]["created"].ToString());
                        DateTime _format = DateTime.SpecifyKind(new DateTime(_createdDate.Year, _createdDate.Month, _createdDate.Day, _createdDate.Hour, _createdDate.Minute, _createdDate.Second), DateTimeKind.Utc);
                        _createdDate = _format;
                        Alrt.CreatedDate = _format;
                    }

                    DateTime _StartDate = new DateTime();
                    if (dsData.Tables[0].Rows[i]["StartDate"].ToString() != null && dsData.Tables[0].Rows[i]["StartDate"].ToString() != "")
                    {
                        _StartDate = Convert.ToDateTime(dsData.Tables[0].Rows[i]["StartDate"].ToString());
                        DateTime _format = DateTime.SpecifyKind(new DateTime(_StartDate.Year, _StartDate.Month, _StartDate.Day, _StartDate.Hour, _StartDate.Minute, _StartDate.Second), DateTimeKind.Utc);
                        _StartDate = _format;
                        Alrt.StartDate = _format;
                    }
                    lstAlerts.Add(Alrt);
                }
            }
            return lstAlerts;
        }
    }


    public class HomeRequest
    {
        public int R_Request_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public string DocumentNo { get; set; }
        public string TableName { get; set; }
        public string Name { get; set; }
        public string CaseType { get; set; }
        public string Summary { get; set; }
        public string Status { get; set; }
        public string Priority { get; set; }
        public string IsRead { get; set; }
        public DateTime? NextActionDate { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
