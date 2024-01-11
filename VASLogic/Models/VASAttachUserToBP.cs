/*******************************************************
       * Module Name    : VAS_Standard
       * Purpose        : To get User and update user
       * Chronological Development
       * Employee code : VAI050
      * Created Date:  19-dec-2023
       * Updated Date:  

      ******************************************************/
using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VASAttachUserToBP
    {
        /// <summary>
        /// Get User list
        /// </summary>
        /// <param name="ctx">Contex</param>
        /// <returns>returns user list</returns>
        public List<Userdetail> GetUserList(Ctx ctx, string searchKey)
        {
            SqlParameter[] param = null;
            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT a.Name||' '||a.LastName as Name,a.Email,a.Mobile,a.Supervisor_ID,a.AD_User_ID," +
                "a.AD_Image_ID,a.Value,b.Name as SupervisorName,c.ImageExtension " +
                "FROM AD_User  a  LEFT JOIN AD_User b ON a.Supervisor_ID=b.AD_User_ID  " +
                " LEFT JOIN AD_Image c ON a.AD_Image_ID=c.AD_Image_ID WHERE a.C_BPartner_ID " +
                "IS NULL  AND a.IsActive = 'Y' ");
            if (searchKey != null && searchKey != string.Empty)
            {
                param = new SqlParameter[1];
                param[0] = new SqlParameter("@param1", "%" + searchKey + "%");
                sql.Append("  AND (UPPER(a.name) LIKE UPPER(@param1)  OR UPPER(a.Email)  LIKE UPPER(@param1) OR " +
                    "UPPER(a.Mobile) LIKE UPPER(@param1) OR UPPER(a.Value) LIKE UPPER(@param1))");

            }
            //sql.Append(" AND AD_Client_ID=" + ctx.GetAD_Client_ID());
          string  sql1 = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql1, param, null);
            List<Userdetail> user = new List<Userdetail>();
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Userdetail obj = new Userdetail();
                    obj.recid = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_User_ID"]);
                    obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.Mobile = Util.GetValueOfString(ds.Tables[0].Rows[i]["Mobile"]);
                    obj.Email = Util.GetValueOfString(ds.Tables[0].Rows[i]["Email"]);
                    obj.Supervisor_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["Supervisor_ID"]);
                    obj.Image_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Image_ID"]);
                    obj.UserID = Util.GetValueOfString(ds.Tables[0].Rows[i]["Value"]);
                    obj.AD_User_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_User_ID"]);
                    obj.SupervisorName = Util.GetValueOfString(ds.Tables[0].Rows[i]["SupervisorName"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Image_ID"]) > 0)
                    {
                        if (System.IO.File.Exists(VAdvantage.DataBase.GlobalVariable.ImagePath + "\\Thumb46x46\\" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Image_ID"]) + Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageExtension"])))
                        {
                            obj.ImageUrl = "Images/Thumb46x46/" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Image_ID"]) + Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageExtension"]);
                        }

                    }
                    user.Add(obj);
                }
            }

            return user;
        }

        /// <summary>
        /// Update user 
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="UserIds"></param>
        /// <param name="C_BPartnerID"></param>
        /// <returns>returns List of users which is not updated</returns>
        public List<dynamic> UpdateUser(Ctx ctx, List<String> userNames, List<int> userIds, int c_BPartnerID)
        {
            List<dynamic> obj = new List<dynamic>();
            if (userIds.Count > 0)
            {
                dynamic additionalItem = null;
                for (int i = 0; i < userIds.Count; i++)
                {
                    int count = 0;
                    string sql = "UPDATE AD_User SET C_BPartner_ID=" + c_BPartnerID + " WHERE AD_User_ID=" + userIds[i];
                    count = DB.ExecuteQuery(sql);
                    if (count < 0)
                    {
                        additionalItem = new ExpandoObject();
                        ValueNamePair vnp = VLogger.RetrieveError();
                        if (vnp != null && vnp.GetName() != null)
                        {
                            additionalItem.userID = userIds[i];
                            additionalItem.userName = userNames[i];
                            additionalItem.error = vnp.Name;
                        }
                        else
                        {
                            additionalItem.userID = userIds[i];
                            additionalItem.userName = userNames[i];
                            additionalItem.error = "couldnotupdated";
                        }
                        obj.Add(additionalItem);
                    }
                   // obj.Add(additionalItem);
                }
            }
            return obj;
        }

        public class Userdetail
        {
            public int recid { get; set; }
            public string Name { get; set; }
            public string Mobile { get; set; }
            public string Email { get; set; }
            public string SupervisorName { get; set; }
            public int Supervisor_ID { get; set; }
            public int Image_ID { get; set; }
            public string UserID { get; set; }
            public int AD_User_ID { get; set; }
            public string ImageUrl { get; set; }
        }

    }
}