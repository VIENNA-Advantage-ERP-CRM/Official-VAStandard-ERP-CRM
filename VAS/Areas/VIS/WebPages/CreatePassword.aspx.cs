using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using VAdvantage.Common;
using VAdvantage.Logging;
using VAdvantage.Utility;

namespace VIS.Areas.VIS.WebPages
{
    public partial class CreatePassword : System.Web.UI.Page
    {
        VLogger log = new VLogger("CreatePassword"); // Log initialization
        Ctx ctx = null;
        bool LinkExpire = false;
        string IsExpireLink = "";
        protected void Page_Load(object sender, EventArgs e)
        {
            passwordMsg.Visible = false;
            if (!IsPostBack)
            {
                HttpRequest q = Request;
                int AD_USER_ID = Convert.ToInt32(SecureEngine.Decrypt(q.QueryString["ID"]));
                if (AD_USER_ID > 0)
                {
                    String sql = "SELECT IsExpireLink FROM AD_User WHERE ISACTIVE ='Y' AND AD_User_ID=" + AD_USER_ID;
                    IsExpireLink = Util.GetValueOfString(DB.ExecuteScalar(sql));
                    if (IsExpireLink == "Y")
                    {
                        Response.Redirect("Expire.aspx");
                    }
                }
            }
        }
        protected void btnSave_Click(object sender, EventArgs e)
        {
            if (!IsValidate())
            {
                return;
            }
            HttpRequest q = Request;
            int AD_Client_ID = 0;
            int AD_Org_ID = 0;
            int AD_USER_ID = Convert.ToInt32(SecureEngine.Decrypt(q.QueryString["ID"]));
            String sql = "SELECT * FROM AD_User WHERE ISACTIVE ='Y' AND AD_User_ID=" + AD_USER_ID;
            DataSet dsIUser = DB.ExecuteDataset(sql);
            if (dsIUser != null && dsIUser.Tables[0].Rows.Count > 0)
            {
                AD_Org_ID = Convert.ToInt32(dsIUser.Tables[0].Rows[0]["AD_Org_ID"]);
                AD_Client_ID = Convert.ToInt32(dsIUser.Tables[0].Rows[0]["AD_Client_ID"]);
            }
            Ctx ctx = new Ctx();
            ctx.SetAD_Client_ID(AD_Client_ID);
            ctx.SetAD_Org_ID(AD_Org_ID);
            string newPwd = txtCreatePass.Value;
            LinkExpire = Common.UpdatePasswordAndValidity(newPwd, AD_USER_ID, AD_USER_ID, 3, ctx);
            if (LinkExpire == true)
            {
                String Sql = "UPDATE AD_User SET IsExpireLink='Y'  WHERE AD_User_ID=" + AD_USER_ID;
                int count = DB.ExecuteQuery(Sql);
            }
            divSetPassword.Visible = false;
            passwordMsg.Visible = true;
        }
        private bool IsValidate()
        {
            if (string.IsNullOrEmpty(txtCreatePass.Value) || string.IsNullOrEmpty(txtConfirmPass.Value))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}